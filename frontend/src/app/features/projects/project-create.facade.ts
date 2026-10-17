import { injectProjectsI18n } from '../../core/i18n/providers/projects-i18n';
import { projectFeedbackField, projectFeedbackText, projectFeedbackValue, projectFeedbackView, ProjectFeedbackString, normalizeProjectFeedbackError } from './project-feedback';
import { DestroyRef, computed, effect, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';

import { FrontendApiError } from '../../core/api/api-error.model';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { createCryptographicUuid } from '../../core/security/client-id';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import {
  canonicalizeProjectCreateInput,
  ProjectCreateApi,
  ProjectCreateInput,
  ProjectCreateOptions,
  ProjectCreateRequestDto,
  ProjectCreateResponseError,
  ProjectCreateSuccess,
} from './project-create.api';
import { ProjectsFacade } from './projects.facade';

export type ProjectCreateField =
  'title' | 'description' | 'groupId' | 'visibility' | 'startDate' | 'endDate' | 'form';

export interface ProjectCreateFieldError {
  readonly field: ProjectCreateField;
  readonly message: string;
}

export type ProjectCreateOptionsStatus = 'idle' | 'loading' | 'ready' | 'denied' | 'error';

export interface ProjectCreateOptionsViewModel {
  readonly status: ProjectCreateOptionsStatus;
  readonly workspaceId?: string;
  readonly data?: ProjectCreateOptions;
  readonly message?: string;
  readonly requestId?: string;
}

export type ProjectCreateStatus =
  'idle' | 'submitting' | 'error' | 'committedPendingNavigation' | 'succeeded';

export interface ProjectCreateViewModel {
  readonly status: ProjectCreateStatus;
  readonly fieldErrors: readonly ProjectCreateFieldError[];
  readonly message?: string;
  readonly requestId?: string;
  readonly createdProjectId?: string;
}

export const EMPTY_PROJECT_CREATE_OPTIONS: ProjectCreateOptionsViewModel = { status: 'idle' };
export const EMPTY_PROJECT_CREATE_STATE: ProjectCreateViewModel = {
  status: 'idle',
  fieldErrors: [],
};

interface ProjectCreateAttempt {
  readonly identityKey: string;
  readonly workspaceId: string;
  readonly payloadKey: string;
  readonly idempotencyKey: string;
  hasDispatched: boolean;
}

interface CommittedProjectCreate {
  readonly identityKey: string;
  readonly workspaceId: string;
  readonly success: ProjectCreateSuccess;
}

@Injectable({ providedIn: 'root' })
export class ProjectCreateFacade {
  private readonly i18n = injectProjectsI18n();
  private readonly api = inject(ProjectCreateApi);
  private readonly router = inject(Router);
  private readonly projects = inject(ProjectsFacade);
  private readonly authSession = inject(AuthSessionFacade);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly destroyRef = inject(DestroyRef);

  private readonly optionsState = signal<ProjectCreateOptionsViewModel>(
    EMPTY_PROJECT_CREATE_OPTIONS,
  );
  private readonly mutationState = signal<ProjectCreateViewModel>(EMPTY_PROJECT_CREATE_STATE);
  private scopeIdentityKey: string | null = null;
  private scopeWorkspaceId: string | null = null;
  private observedIdentityKey = this.currentIdentityKey();
  private scopeGeneration = 0;
  private optionsCancellation = new Subject<void>();
  private mutationCancellation = new Subject<void>();
  private createAttempt: ProjectCreateAttempt | null = null;
  private activeCreateAttempt: ProjectCreateAttempt | null = null;
  private activeCreateHasDispatched = false;
  private committedCreate: CommittedProjectCreate | null = null;

  readonly options = computed(() => projectFeedbackView(this.i18n, this.optionsState()));
  readonly createState = computed(() => projectFeedbackView(this.i18n, this.mutationState()));

  constructor() {
    const unregister = this.realtime.registerProtectedStateClearer('project-create', (reason) =>
      this.clearProtectedState(reason),
    );
    this.destroyRef.onDestroy(unregister);

    effect(() => {
      const nextIdentityKey = this.currentIdentityKey();
      const nextWorkspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
      const identityChanged = nextIdentityKey !== this.observedIdentityKey;
      const activeWorkspaceLeftScope =
        this.scopeWorkspaceId !== null &&
        nextWorkspaceId !== null &&
        nextWorkspaceId !== this.scopeWorkspaceId;

      this.observedIdentityKey = nextIdentityKey;
      if (identityChanged) {
        this.clearProtectedState('session');
      } else if (activeWorkspaceLeftScope) {
        this.clearProtectedState('workspace');
      }
    });
  }

  async loadOptions(workspaceId: string): Promise<boolean> {
    const identityKey = this.currentIdentityKey();
    if (!identityKey) {
      this.optionsState.set({
        status: 'denied',
        workspaceId,
        ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.projectCreationIsUnavailableInTheCurrentSession")),
      });
      return false;
    }

    this.activateScope(identityKey, workspaceId);
    this.cancelOptionsRequest();
    const cancellation = this.optionsCancellation;
    const generation = this.scopeGeneration;
    this.optionsState.set({ status: 'loading', workspaceId });

    try {
      const options = await firstValueFrom(
        this.api.getOptions(workspaceId).pipe(takeUntil(cancellation)),
        { defaultValue: null },
      );
      if (!options || !this.isScopeCurrent(generation, identityKey, workspaceId)) {
        return false;
      }

      if (!options.canCreateUngrouped && options.groups.length === 0) {
        this.optionsState.set({
          status: 'denied',
          workspaceId,
          ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.youDoNotCurrentlyHavePermissionToCreateAProject")),
          requestId: options.requestId,
        });
        return false;
      }

      this.optionsState.set({
        status: 'ready',
        workspaceId,
        data: options,
        requestId: options.requestId,
      });
      return true;
    } catch (error: unknown) {
      if (!this.isScopeCurrent(generation, identityKey, workspaceId)) {
        return false;
      }
      const normalized = normalizeProjectCreateError(error);
      this.optionsState.set({
        status: normalized.httpStatus === 401 || normalized.httpStatus === 403 ? 'denied' : 'error',
        workspaceId,
        ...projectFeedbackField("message", normalized.httpStatus === 401 || normalized.httpStatus === 403 ? projectFeedbackText("projectFeedback.projectCreate.youDoNotCurrentlyHavePermissionToCreateAProject") : projectFeedbackText("projectFeedback.projectCreate.projectCreationOptionsCouldNotBeVerifiedTryAgain")),
        requestId: normalized.requestId,
      });
      return false;
    }
  }

  async createProject(workspaceId: string, input: ProjectCreateInput): Promise<boolean> {
    if (this.mutationState().status === 'submitting' || this.activeCreateAttempt !== null) {
      return false;
    }

    const identityKey = this.currentIdentityKey();
    const options = this.optionsState();
    if (
      identityKey &&
      this.committedCreate?.identityKey === identityKey &&
      this.committedCreate.workspaceId === workspaceId &&
      this.committedCreate.workspaceId === this.scopeWorkspaceId
    ) {
      this.markCommittedPendingNavigation(this.committedCreate.success);
      return false;
    }

    if (
      !identityKey ||
      this.scopeIdentityKey !== identityKey ||
      this.scopeWorkspaceId !== workspaceId ||
      options.status !== 'ready' ||
      options.workspaceId !== workspaceId ||
      !options.data
    ) {
      this.setCreateError(projectFeedbackText("projectFeedback.projectCreate.projectCreationOptionsAreUnavailableReloadThemAndTryAgain"));
      return false;
    }

    const request = canonicalizeProjectCreateInput(input);
    const fieldErrors = validateProjectCreateRequest(request, options.data);
    if (fieldErrors.length > 0) {
      this.mutationState.set({
        status: 'error',
        fieldErrors,
        ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.reviewTheHighlightedProjectDetailsAndTryAgain")),
      });
      return false;
    }

    const attempt = this.getOrCreateAttempt(identityKey, workspaceId, request);
    this.activeCreateAttempt = attempt;
    this.activeCreateHasDispatched = false;
    this.cancelMutationRequest();
    const cancellation = this.mutationCancellation;
    const generation = this.scopeGeneration;
    this.mutationState.set({ status: 'submitting', fieldErrors: [] });

    let success: ProjectCreateSuccess | null;
    try {
      success = await firstValueFrom(
        this.api
          .createProject(workspaceId, request, attempt.idempotencyKey, () => {
            if (this.activeCreateAttempt === attempt) {
              attempt.hasDispatched = true;
              this.activeCreateHasDispatched = true;
            }
          })
          .pipe(takeUntil(cancellation)),
        { defaultValue: null },
      );
    } catch (error: unknown) {
      if (this.isScopeCurrent(generation, identityKey, workspaceId)) {
        this.applyCreateError(error);
      }
      return false;
    } finally {
      if (this.activeCreateAttempt === attempt) {
        this.activeCreateAttempt = null;
        this.activeCreateHasDispatched = false;
      }
    }

    if (!success || !this.isScopeCurrent(generation, identityKey, workspaceId)) {
      return false;
    }

    // The strict canonical 201 consumes the key. Commit this client state
    // before any refresh or navigation that can trigger authorization clears.
    this.createAttempt = null;
    this.committedCreate = { identityKey, workspaceId, success };
    return this.continueCommittedNavigation(generation, identityKey, workspaceId, success);
  }

  async retryCreatedProjectNavigation(): Promise<boolean> {
    if (this.mutationState().status === 'submitting') {
      return false;
    }

    const identityKey = this.currentIdentityKey();
    const committed = this.committedCreate;
    if (
      !identityKey ||
      committed?.identityKey !== identityKey ||
      committed.workspaceId !== this.scopeWorkspaceId
    ) {
      return false;
    }

    return this.continueCommittedNavigation(
      this.scopeGeneration,
      identityKey,
      committed.workspaceId,
      committed.success,
    );
  }

  resetCreatePresentation(): void {
    const status = this.mutationState().status;
    if (status === 'submitting' || status === 'committedPendingNavigation') {
      return;
    }
    this.mutationState.set(EMPTY_PROJECT_CREATE_STATE);
  }

  clearWorkspaceScope(): void {
    this.clearProtectedState('workspace');
  }

  private async continueCommittedNavigation(
    generation: number,
    identityKey: string,
    workspaceId: string,
    success: ProjectCreateSuccess,
  ): Promise<boolean> {
    if (!this.isScopeCurrent(generation, identityKey, workspaceId)) {
      return false;
    }

    this.mutationState.set({
      status: 'submitting',
      fieldErrors: [],
      requestId: success.requestId,
      createdProjectId: success.data.id,
    });

    // This starts the authoritative list GET. Project Detail performs its own
    // authoritative GET after navigation; neither follow-up may repeat POST.
    try {
      this.projects.retryProjects();
    } catch {
      // The dedicated confirmation GET below still owns reconciliation.
    }

    if (!this.isScopeCurrent(generation, identityKey, workspaceId)) {
      return false;
    }

    try {
      const confirmation = await firstValueFrom(
        this.api
          .confirmCreatedProject(success.data.id, workspaceId)
          .pipe(takeUntil(this.mutationCancellation)),
        { defaultValue: null },
      );
      if (!confirmation || !this.isScopeCurrent(generation, identityKey, workspaceId)) {
        return false;
      }
    } catch {
      if (this.isScopeCurrent(generation, identityKey, workspaceId)) {
        this.markCommittedPendingNavigation(success);
      }
      return false;
    }

    try {
      const navigated = await this.router.navigate(['/projects', success.data.id]);
      if (!this.isScopeCurrent(generation, identityKey, workspaceId)) {
        return false;
      }
      if (!navigated) {
        this.markCommittedPendingNavigation(success);
        return false;
      }
    } catch {
      if (this.isScopeCurrent(generation, identityKey, workspaceId)) {
        this.markCommittedPendingNavigation(success);
      }
      return false;
    }

    this.committedCreate = null;
    this.mutationState.set({
      status: 'succeeded',
      fieldErrors: [],
      requestId: success.requestId,
      createdProjectId: success.data.id,
    });
    return true;
  }

  private activateScope(identityKey: string, workspaceId: string): void {
    if (this.scopeIdentityKey === identityKey && this.scopeWorkspaceId === workspaceId) {
      return;
    }
    this.clearProtectedState('workspace');
    this.scopeIdentityKey = identityKey;
    this.scopeWorkspaceId = workspaceId;
  }

  private clearProtectedState(reason: ProtectedStateClearReason): void {
    const committed = this.committedCreate;
    const attempt = this.createAttempt;
    const preserveCommitted =
      reason === 'authorization' &&
      committed !== null &&
      committed.identityKey === this.currentIdentityKey() &&
      committed.workspaceId === this.scopeWorkspaceId;
    const preserveUncertainAttempt =
      reason === 'authorization' &&
      committed === null &&
      attempt !== null &&
      attempt.identityKey === this.currentIdentityKey() &&
      attempt.workspaceId === this.scopeWorkspaceId;
    const attemptWasDispatched = preserveUncertainAttempt && attempt?.hasDispatched === true;
    const preserveDispatchedMutation =
      attemptWasDispatched &&
      attempt !== null &&
      this.activeCreateAttempt === attempt &&
      this.activeCreateHasDispatched;
    const preserveCommittedContinuation =
      preserveCommitted && this.mutationState().status === 'submitting';
    const preserveActiveMutation =
      preserveDispatchedMutation || preserveCommittedContinuation;

    // Once this exact invocation has dispatched its canonical idempotent POST,
    // an authorization invalidation must not abort the response. After the
    // strict 201, the confirmation GET/navigation may also stay alive because
    // those follow-up reads reauthorize on the server. Pre-dispatch requests
    // and all session/Tenant/Workspace boundary changes remain cancellable.
    if (!preserveActiveMutation) {
      this.scopeGeneration += 1;
    }
    this.cancelOptionsRequest();
    if (!preserveActiveMutation) {
      this.cancelMutationRequest();
    }

    if (preserveCommitted && committed) {
      this.scopeIdentityKey = committed.identityKey;
      this.scopeWorkspaceId = committed.workspaceId;
      this.createAttempt = null;
      this.optionsState.set(EMPTY_PROJECT_CREATE_OPTIONS);
      if (preserveCommittedContinuation) {
        // Keep the already-started authoritative GET/navigation chain alive.
        // A denied confirmation still falls back to pending recovery, while a
        // successful confirmation can finish navigation without a stale UI
        // transition racing the router.
        return;
      }
      this.mutationState.set({
        status: 'committedPendingNavigation',
        fieldErrors: [],
        ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.theProjectWasCreatedAsDraftRecheckAccessAndOpen")),
      });
      return;
    }

    if (preserveUncertainAttempt && attempt) {
      // An authorization invalidation always clears the protected projection.
      // If any request for this key had reached browser dispatch, the server
      // can have committed before its 201 reached this client. If none had
      // started, the cancellation prevented every POST, so an explicit retry
      // after a fresh options read is required without falsely claiming
      // uncertainty. In either case retain only the opaque canonical payload/key tuple. A
      // session, Tenant, or actual Workspace boundary below destroys it.
      this.scopeIdentityKey = attempt.identityKey;
      this.scopeWorkspaceId = attempt.workspaceId;
      this.createAttempt = attempt;
      this.optionsState.set({
        status: 'error',
        workspaceId: attempt.workspaceId,
        ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.projectCreationOptionsChangedAndMustBeCheckedAgain")),
      });
      if (preserveDispatchedMutation) {
        this.mutationState.set({ status: 'submitting', fieldErrors: [] });
        return;
      }
      this.mutationState.set({
        status: 'error',
        fieldErrors: [],
        ...projectFeedbackField("message", attemptWasDispatched ? projectFeedbackText("projectFeedback.projectCreate.theProjectMayHaveBeenCreatedRecheckAccessAndRetry") : projectFeedbackText("projectFeedback.projectCreate.projectCreationWasStoppedBeforeItWasSentRecheckAccess")),
      });
      return;
    }

    this.scopeIdentityKey = null;
    this.scopeWorkspaceId = null;
    this.createAttempt = null;
    this.committedCreate = null;
    this.optionsState.set(EMPTY_PROJECT_CREATE_OPTIONS);
    this.mutationState.set(EMPTY_PROJECT_CREATE_STATE);
  }

  private cancelOptionsRequest(): void {
    this.optionsCancellation.next();
    this.optionsCancellation.complete();
    this.optionsCancellation = new Subject<void>();
  }

  private cancelMutationRequest(): void {
    this.mutationCancellation.next();
    this.mutationCancellation.complete();
    this.mutationCancellation = new Subject<void>();
  }

  private isScopeCurrent(generation: number, identityKey: string, workspaceId: string): boolean {
    return (
      generation === this.scopeGeneration &&
      identityKey === this.currentIdentityKey() &&
      identityKey === this.scopeIdentityKey &&
      workspaceId === this.scopeWorkspaceId
    );
  }

  private getOrCreateAttempt(
    identityKey: string,
    workspaceId: string,
    request: ProjectCreateRequestDto,
  ): ProjectCreateAttempt {
    const payloadKey = JSON.stringify(request);
    if (
      this.createAttempt?.identityKey === identityKey &&
      this.createAttempt.workspaceId === workspaceId &&
      this.createAttempt.payloadKey === payloadKey
    ) {
      return this.createAttempt;
    }

    this.createAttempt = {
      identityKey,
      workspaceId,
      payloadKey,
      idempotencyKey: createProjectIdempotencyKey(),
      hasDispatched: false,
    };
    return this.createAttempt;
  }

  private applyCreateError(error: unknown): void {
    const normalized = normalizeProjectCreateError(error);
    if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {
      this.optionsState.set({
        status: 'denied',
        workspaceId: this.scopeWorkspaceId ?? undefined,
        ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.youDoNotCurrentlyHavePermissionToCreateAProject")),
        requestId: normalized.requestId,
      });
    }

    const uncertain =
      error instanceof ProjectCreateResponseError ||
      normalized.httpStatus === 0 ||
      (normalized.httpStatus >= 200 && normalized.httpStatus < 300) ||
      normalized.httpStatus >= 500;
    this.mutationState.set({
      status: 'error',
      fieldErrors: createFieldErrors(normalized),
      ...projectFeedbackField("message", uncertain ? projectFeedbackText("projectFeedback.projectCreate.theProjectMayHaveBeenCreatedRetryWithTheSame") : projectFeedbackValue(normalized, "message")),
      requestId: normalized.requestId,
    });
  }

  private setCreateError(message: ProjectFeedbackString): void {
    this.mutationState.set({
      status: 'error',
      fieldErrors: [{ field: 'form', ...projectFeedbackField("message", message) }],
      ...projectFeedbackField("message", message),
    });
  }

  private markCommittedPendingNavigation(success: ProjectCreateSuccess): void {
    this.mutationState.set({
      status: 'committedPendingNavigation',
      fieldErrors: [],
      ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.theProjectWasCreatedAsDraftButItCouldNot")),
      requestId: success.requestId,
      createdProjectId: success.data.id,
    });
  }

  private currentIdentityKey(): string | null {
    const session = this.authSession.session();
    const tenantId = session.currentTenant?.tenantId ?? session.currentTenant?.tenantSlug;
    const userId = session.currentUser?.userId;
    return session.isAuthenticated && tenantId && userId ? `${tenantId}:${userId}` : null;
  }
}

function validateProjectCreateRequest(
  request: ProjectCreateRequestDto,
  options: ProjectCreateOptions,
): readonly ProjectCreateFieldError[] {
  const errors: ProjectCreateFieldError[] = [];
  if (!request.title) {
    errors.push({ field: 'title', ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.enterAProjectName")) });
  } else if (request.title.length > 200) {
    errors.push({ field: 'title', ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.projectNameMustBe200CharactersOrFewer")) });
  }
  if (request.description !== null && request.description.length > 4000) {
    errors.push({
      field: 'description',
      ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.descriptionMustBe4000CharactersOrFewer")),
    });
  }

  if (request.groupId === null) {
    if (!options.canCreateUngrouped) {
      errors.push({ field: 'groupId', ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.chooseAGroupAvailableToYou")) });
    }
  } else if (!options.groups.some((group) => sameId(group.id, request.groupId!))) {
    errors.push({ field: 'groupId', ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.chooseAGroupAvailableToYou")) });
  }
  if (!options.allowedVisibilities.includes(request.visibility)) {
    errors.push({
      field: 'visibility',
      ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.chooseAVisibilityAvailableToYou")),
    });
  }

  if (request.startDate !== null && !isIsoDate(request.startDate)) {
    errors.push({ field: 'startDate', ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.enterAValidStartDate")) });
  }
  if (request.endDate !== null && !isIsoDate(request.endDate)) {
    errors.push({ field: 'endDate', ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.enterAValidTargetEndDate")) });
  }
  if (
    request.startDate !== null &&
    request.endDate !== null &&
    isIsoDate(request.startDate) &&
    isIsoDate(request.endDate) &&
    request.endDate < request.startDate
  ) {
    errors.push({
      field: 'endDate',
      ...projectFeedbackField("message", projectFeedbackText("projectFeedback.projectCreate.targetEndDateCannotBeBeforeTheStartDate")),
    });
  }
  return errors;
}

function createFieldErrors(error: FrontendApiError): readonly ProjectCreateFieldError[] {
  const targets = [
    ...(error.target ? [{ target: error.target, ...projectFeedbackField("message", projectFeedbackValue(error, "message")) }] : []),
    ...error.details,
  ];
  const mapped = targets.map((detail) => ({
    field: projectCreateField(detail.target),
    ...projectFeedbackField("message", projectFeedbackValue(detail, "message")),
  }));
  return mapped.filter(
    (candidate, index) =>
      mapped.findIndex(
        (item) => item.field === candidate.field && item.message === candidate.message,
      ) === index,
  );
}

function projectCreateField(target: string | undefined): ProjectCreateField {
  const normalized = target?.split('.').at(-1)?.toLowerCase();
  switch (normalized) {
    case 'title':
      return 'title';
    case 'description':
      return 'description';
    case 'groupid':
      return 'groupId';
    case 'visibility':
      return 'visibility';
    case 'startdate':
      return 'startDate';
    case 'enddate':
      return 'endDate';
    default:
      return 'form';
  }
}

function normalizeProjectCreateError(error: unknown): FrontendApiError {
  return normalizeProjectFeedbackError(
    error,
    error instanceof ProjectCreateResponseError ? error.httpStatus : undefined,
  );
}

function createProjectIdempotencyKey(): string {
  return `project-create-${createCryptographicUuid()}`;
}

function sameId(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase();
}

function isIsoDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/u.test(value)) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}