import { HttpClient } from '@angular/common/http';
import { DestroyRef, effect, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom, Subscription } from 'rxjs';

import { normalizeApiError } from '../../core/api/api-error.adapter';
import { FrontendApiError } from '../../core/api/api-error.model';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { createCryptographicUuid } from '../../core/security/client-id';
import {
  WorkspaceSelectionFacade,
  WorkspaceSelectionIdentity,
  workspaceIdFromRoute,
} from '../../core/workspace/workspace-selection.facade';
import {
  canonicalizeWorkspaceCreateInput,
  mapWorkspaceDashboardResponse,
  mapWorkspaceCreateSuccess,
  mapWorkspacePageCapabilities,
  WorkspaceCapabilitiesEnvelopeDto,
  WorkspaceCreateRequestDto,
  WorkspaceCreateSuccess,
} from './workspaces.api';
import {
  WorkspaceCardViewModel,
  WorkspaceCreateField,
  WorkspaceCreateFieldError,
  WorkspaceCreateInput,
  WorkspaceCreateViewModel,
  WorkspaceDashboardViewModel,
  WorkspacePageCapability,
} from './workspaces.types';

export const COGLATAS_WORKSPACES_DASHBOARD_MOCK = new InjectionToken<WorkspaceDashboardViewModel>(
  'COGLATAS_WORKSPACES_DASHBOARD_MOCK',
);

const INITIAL_CREATE_STATE: WorkspaceCreateViewModel = {
  status: 'idle',
  fieldErrors: [],
};

interface WorkspaceCreateAttempt {
  readonly identityKey: string;
  readonly payloadKey: string;
  readonly idempotencyKey: string;
}

interface CommittedWorkspaceCreate {
  readonly identityKey: string;
  readonly success: WorkspaceCreateSuccess;
}

@Injectable({
  providedIn: 'root',
})
export class WorkspacesFacade {
  private readonly http = inject(HttpClient, { optional: true });
  private readonly router = inject(Router, { optional: true });
  private readonly destroyRef = inject(DestroyRef);
  private readonly selection = inject(WorkspaceSelectionFacade);
  private readonly authSession = inject(AuthSessionFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly mockDashboard = inject(COGLATAS_WORKSPACES_DASHBOARD_MOCK, { optional: true });
  private readonly dashboardState = signal<WorkspaceDashboardViewModel>(
    this.mockDashboard ?? this.emptyDashboard('loading'),
  );
  private pageCapabilities: readonly WorkspacePageCapability[] = [];
  private loadGeneration = 0;
  private capabilityLoadGeneration = 0;
  private observedIdentityKey: string | null;
  private observedAuthorizationRevision: number;
  private reloadStartedAuthorizationRevision: number;
  private authorizationRecheckPending = false;
  private httpFallbackRecoveryGeneration: number | null = null;
  private workspaceListRequest: Subscription | null = null;
  private workspaceListCompletion: (() => void) | null = null;
  private workspaceListInFlight: Promise<void> | null = null;
  private readonly workspaceCreateState = signal<WorkspaceCreateViewModel>(INITIAL_CREATE_STATE);
  private createAttempt: WorkspaceCreateAttempt | null = null;
  private committedCreate: CommittedWorkspaceCreate | null = null;
  private createOperationGeneration = 0;

  readonly dashboard = this.dashboardState.asReadonly();
  readonly workspaceCreate = this.workspaceCreateState.asReadonly();

  constructor() {
    const initialIdentity = this.currentIdentity();
    this.observedIdentityKey = identityKey(initialIdentity);
    this.observedAuthorizationRevision = this.realtime.authorizationRevision();
    this.reloadStartedAuthorizationRevision = this.observedAuthorizationRevision;

    if (this.mockDashboard) {
      this.reconcileSelection(this.mockDashboard.workspaces, initialIdentity);
      return;
    }

    this.initializeForIdentity(initialIdentity);

    const unregisterCatchUp = this.realtime.registerCatchUp(
      'workspaces-dashboard',
      () => this.reloadAfterAuthorizationCatchUp(),
    );
    this.destroyRef.onDestroy(unregisterCatchUp);

    effect(() => {
      const identity = this.currentIdentity();
      const nextIdentityKey = identityKey(identity);
      if (nextIdentityKey === this.observedIdentityKey) {
        return;
      }

      this.observedIdentityKey = nextIdentityKey;
      this.resetCreateScope();
      // A tenant or session identity change has already cleared protected
      // projections. Let the newly authenticated Workspace HTTP response be
      // the authority that permits feature catch-ups, including in HTTP-only
      // mode where SignalR never reconnects.
      this.initializeForIdentity(identity, true);
    });

    effect(() => {
      const authorizationRevision = this.realtime.authorizationRevision();
      const state = this.realtime.connectionState();
      if (authorizationRevision !== this.observedAuthorizationRevision) {
        this.observedAuthorizationRevision = authorizationRevision;
        if (authorizationRevision !== this.reloadStartedAuthorizationRevision) {
          this.authorizationRecheckPending = true;
          this.invalidateForAuthorizationRecheck();
        }
      }

      if (state === 'Degraded' && this.authorizationRecheckPending) {
        // SignalR is an enhancement. If reauthorization transport cannot
        // recover, the current cookie-authenticated HTTP list still owns the
        // authorization decision and must restore (or deny) the shell scope.
        void this.loadWorkspaces(true);
      }
    });

    this.router?.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((event) => this.reconcileCurrentRoute(event.urlAfterRedirects));
  }

  loadWorkspaces(recoverProtectedState = false): Promise<void> {
    this.cancelWorkspaceListRequest();
    const authorizationRevision = this.realtime.authorizationRevision();
    this.observedAuthorizationRevision = authorizationRevision;
    this.reloadStartedAuthorizationRevision = authorizationRevision;
    this.authorizationRecheckPending = false;

    const identity = this.currentIdentity();
    if (!identity || !this.http) {
      this.loadGeneration += 1;
      this.httpFallbackRecoveryGeneration = null;
      this.pageCapabilities = [];
      this.selection.beginLoading(null);
      this.dashboardState.set(this.emptyDashboard('error'));
      return Promise.resolve();
    }

    const generation = ++this.loadGeneration;
    this.httpFallbackRecoveryGeneration = recoverProtectedState ? generation : null;
    this.pageCapabilities = [];
    this.selection.beginLoading(identity);
    this.dashboardState.set(this.emptyDashboard('loading'));
    this.loadCapabilities(generation);

    const completionPromise = new Promise<void>((resolve) => {
      let settled = false;
      const settle = (): void => {
        if (!settled) {
          settled = true;
          resolve();
        }
      };
      this.workspaceListCompletion = settle;
      const request = this.http?.get<unknown>('/api/workspaces', { withCredentials: true }).subscribe({
        next: (workspaces) => this.applyWorkspaceResponse(workspaces, identity, generation),
        error: (error: unknown) => {
          this.applyWorkspaceError(error, generation);
          settle();
        },
        complete: settle,
      });
      this.workspaceListRequest = request ?? null;
      request?.add(() => {
        if (this.workspaceListRequest === request) {
          this.workspaceListRequest = null;
        }
        if (this.workspaceListCompletion === settle) {
          this.workspaceListCompletion = null;
        }
        settle();
      });
    });
    this.workspaceListInFlight = completionPromise;
    void completionPromise.finally(() => {
      if (this.workspaceListInFlight === completionPromise) {
        this.workspaceListInFlight = null;
      }
    });
    return completionPromise;
  }

  async createWorkspace(input: WorkspaceCreateInput): Promise<boolean> {
    if (this.workspaceCreateState().status === 'submitting') {
      return false;
    }

    const identity = this.currentIdentity();
    const currentIdentityKey = identityKey(identity);
    if (!identity || !currentIdentityKey || !this.http) {
      this.setCreateError('Workspace creation is unavailable in the current session.');
      return false;
    }

    if (this.committedCreate?.identityKey === currentIdentityKey) {
      this.markCommittedPendingActivation(this.committedCreate.success);
      return false;
    }

    if (!this.pageCapabilities.includes('createWorkspace')) {
      this.setCreateError('You do not currently have permission to create a Workspace.');
      return false;
    }

    const payload = canonicalizeWorkspaceCreateInput(input);
    const validationErrors = validateWorkspaceCreatePayload(payload);
    if (validationErrors.length > 0) {
      this.workspaceCreateState.set({
        status: 'error',
        fieldErrors: validationErrors,
        message: 'Review the highlighted fields and try again.',
      });
      return false;
    }

    const attempt = this.getOrCreateAttempt(currentIdentityKey, payload);
    const operationGeneration = ++this.createOperationGeneration;
    this.workspaceCreateState.set({ status: 'submitting', fieldErrors: [] });

    let response;
    try {
      response = await firstValueFrom(
        this.http.post<unknown>('/api/workspaces', payload, {
          headers: { 'Idempotency-Key': attempt.idempotencyKey },
          observe: 'response',
          withCredentials: true,
        }),
      );
    } catch (error: unknown) {
      if (this.isCreateOperationCurrent(operationGeneration, currentIdentityKey)) {
        const normalized = normalizeApiError(error);
        if (normalized.httpStatus >= 200 && normalized.httpStatus < 300) {
          // HttpClient reports an invalid JSON body on an otherwise successful
          // response through its error channel. The server may already have
          // committed the command, so preserve the key and present uncertainty.
          this.applyMalformedCreateSuccess();
        } else {
          this.applyCreateError(normalized);
        }
      }
      return false;
    }

    if (!this.isCreateOperationCurrent(operationGeneration, currentIdentityKey)) {
      return false;
    }

    if (response.status !== 201) {
      this.applyMalformedCreateSuccess();
      return false;
    }

    let success: WorkspaceCreateSuccess;
    try {
      success = mapWorkspaceCreateSuccess(response.body);
    } catch {
      this.applyMalformedCreateSuccess();
      return false;
    }

    // A canonical 201 proves that this key has been consumed. Activation may
    // still fail, but it must recover through GET/selection and never by
    // posting the create command again.
    this.createAttempt = null;
    this.committedCreate = { identityKey: currentIdentityKey, success };
    this.markActivationSubmitting(success);
    return this.activateCommittedWorkspace(operationGeneration, currentIdentityKey);
  }

  async retryWorkspaceActivation(): Promise<boolean> {
    if (this.workspaceCreateState().status === 'submitting') {
      return false;
    }

    const identity = this.currentIdentity();
    const currentIdentityKey = identityKey(identity);
    const committed = this.committedCreate;
    if (!identity || !currentIdentityKey || committed?.identityKey !== currentIdentityKey) {
      return false;
    }

    const operationGeneration = ++this.createOperationGeneration;
    this.markActivationSubmitting(committed.success);
    return this.activateCommittedWorkspace(operationGeneration, currentIdentityKey);
  }

  resetWorkspaceCreatePresentation(): void {
    const status = this.workspaceCreateState().status;
    if (status === 'submitting' || status === 'committedPendingActivation') {
      return;
    }

    this.workspaceCreateState.set(INITIAL_CREATE_STATE);
  }

  private initializeForIdentity(
    identity: WorkspaceSelectionIdentity | null,
    recoverProtectedState = false,
  ): void {
    if (!identity) {
      this.loadGeneration += 1;
      this.cancelWorkspaceListRequest();
      this.pageCapabilities = [];
      this.selection.beginLoading(null);
      this.dashboardState.set(this.emptyDashboard('loading'));
      return;
    }

    void this.loadWorkspaces(recoverProtectedState);
  }

  private async activateCommittedWorkspace(
    operationGeneration: number,
    expectedIdentityKey: string,
  ): Promise<boolean> {
    const committed = this.committedCreate;
    if (
      committed?.identityKey !== expectedIdentityKey ||
      !this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey)
    ) {
      return false;
    }

    const createdWorkspaceId = committed.success.data.id;
    const isListed = await this.reloadCreatedWorkspace(
      createdWorkspaceId,
      operationGeneration,
      expectedIdentityKey,
    );
    if (!isListed || !this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey)) {
      if (this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey)) {
        this.markCommittedPendingActivation(committed.success);
      }
      return false;
    }

    const expectedAuthorizationRevision = this.realtime.authorizationRevision();
    const expectedTransitionRevision = this.selection.transitionRevision();
    const selectionGuard = (): boolean =>
      this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey) &&
      this.realtime.authorizationRevision() === expectedAuthorizationRevision &&
      this.selection.transitionRevision() === expectedTransitionRevision &&
      this.dashboardState().workspaces.some((workspace) => workspace.id === createdWorkspaceId);

    const selected = await this.selection.selectWorkspace(createdWorkspaceId, selectionGuard);
    const selectedWorkspaceIsCurrent = (): boolean =>
      this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey) &&
      this.realtime.authorizationRevision() === expectedAuthorizationRevision &&
      this.selection.selection().workspaceId === createdWorkspaceId &&
      this.dashboardState().workspaces.some((workspace) => workspace.id === createdWorkspaceId);
    if (!selected || !selectedWorkspaceIsCurrent()) {
      if (this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey)) {
        this.markCommittedPendingActivation(committed.success);
      }
      return false;
    }

    const activatedTransitionRevision = this.selection.transitionRevision();
    if (this.router && routePath(this.router.url) !== '/workspaces') {
      try {
        const navigated = await this.router.navigateByUrl('/workspaces');
        if (
          !navigated ||
          !selectedWorkspaceIsCurrent() ||
          this.selection.transitionRevision() !== activatedTransitionRevision
        ) {
          this.markCommittedPendingActivation(committed.success);
          return false;
        }
      } catch {
        if (this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey)) {
          this.markCommittedPendingActivation(committed.success);
        }
        return false;
      }
    }

    this.committedCreate = null;
    this.workspaceCreateState.set({
      status: 'succeeded',
      fieldErrors: [],
      requestId: committed.success.requestId,
      createdWorkspaceId,
    });
    return true;
  }

  private async reloadCreatedWorkspace(
    createdWorkspaceId: string,
    operationGeneration: number,
    expectedIdentityKey: string,
  ): Promise<boolean> {
    let pending = this.loadWorkspaces();

    // A WorkspaceCreated authorization invalidation can cancel the list that
    // this flow just started. Follow the replacement request, or initiate the
    // authoritative replacement ourselves while the catch-up is pending.
    for (let pass = 0; pass < 4; pass += 1) {
      await pending;
      await Promise.resolve();
      if (!this.isCreateOperationCurrent(operationGeneration, expectedIdentityKey)) {
        return false;
      }

      const replacement = this.workspaceListInFlight;
      if (replacement && replacement !== pending) {
        pending = replacement;
        continue;
      }

      const dashboard = this.dashboardState();
      if (dashboard.status === 'loading' && this.authorizationRecheckPending) {
        pending = this.loadWorkspaces();
        continue;
      }

      return (
        dashboard.status === 'ready' &&
        dashboard.workspaces.some((workspace) => workspace.id === createdWorkspaceId)
      );
    }

    return false;
  }

  private getOrCreateAttempt(
    currentIdentityKey: string,
    payload: WorkspaceCreateRequestDto,
  ): WorkspaceCreateAttempt {
    const payloadKey = JSON.stringify(payload);
    if (
      this.createAttempt?.identityKey === currentIdentityKey &&
      this.createAttempt.payloadKey === payloadKey
    ) {
      return this.createAttempt;
    }

    this.createAttempt = {
      identityKey: currentIdentityKey,
      payloadKey,
      idempotencyKey: createIdempotencyKey(),
    };
    return this.createAttempt;
  }

  private applyCreateError(error: FrontendApiError): void {
    if (error.httpStatus === 401 || error.httpStatus === 403) {
      this.pageCapabilities = [];
      this.applyPageCapabilities();
      this.loadCapabilities(this.loadGeneration);
    }

    const fieldErrors = createFieldErrors(error);
    this.workspaceCreateState.set({
      status: 'error',
      fieldErrors,
      message: createErrorMessage(error),
      requestId: error.requestId,
    });
  }

  private applyMalformedCreateSuccess(): void {
    this.workspaceCreateState.set({
      status: 'error',
      fieldErrors: [{ field: 'form', message: 'The server response could not be verified.' }],
      message:
        'The Workspace may have been created. Retry with the same details so the server can safely reconcile the request.',
    });
  }

  private setCreateError(message: string): void {
    this.workspaceCreateState.set({
      status: 'error',
      fieldErrors: [{ field: 'form', message }],
      message,
    });
  }

  private markCommittedPendingActivation(success: WorkspaceCreateSuccess): void {
    this.workspaceCreateState.set({
      status: 'committedPendingActivation',
      fieldErrors: [],
      message:
        'The Workspace was created, but it could not yet be activated. Retry activation without submitting the form again.',
      requestId: success.requestId,
      createdWorkspaceId: success.data.id,
    });
  }

  private markActivationSubmitting(success: WorkspaceCreateSuccess): void {
    this.workspaceCreateState.set({
      status: 'submitting',
      fieldErrors: [],
      requestId: success.requestId,
      createdWorkspaceId: success.data.id,
    });
  }

  private isCreateOperationCurrent(
    operationGeneration: number,
    expectedIdentityKey: string,
  ): boolean {
    return (
      operationGeneration === this.createOperationGeneration &&
      identityKey(this.currentIdentity()) === expectedIdentityKey
    );
  }

  private resetCreateScope(): void {
    this.createOperationGeneration += 1;
    this.createAttempt = null;
    this.committedCreate = null;
    this.workspaceCreateState.set(INITIAL_CREATE_STATE);
  }

  private invalidateForAuthorizationRecheck(): void {
    this.loadGeneration += 1;
    this.cancelWorkspaceListRequest();
    this.pageCapabilities = [];
    this.selection.markAuthorizationPending();
    this.dashboardState.set(this.emptyDashboard('loading'));
  }

  private cancelWorkspaceListRequest(): void {
    const request = this.workspaceListRequest;
    const completion = this.workspaceListCompletion;
    this.workspaceListRequest = null;
    this.workspaceListCompletion = null;
    this.workspaceListInFlight = null;
    request?.unsubscribe();
    completion?.();
  }

  private reloadAfterAuthorizationCatchUp(): Promise<void> | void {
    const authorizationRevision = this.realtime.authorizationRevision();
    if (authorizationRevision === this.reloadStartedAuthorizationRevision) {
      return this.workspaceListInFlight ?? undefined;
    }

    // A fast reconnect can reach catch-up before Angular has scheduled the
    // authorization-revision effect. Invalidate synchronously here as well so
    // no stale authorized card is visible while its HTTP replacement loads.
    if (authorizationRevision !== this.observedAuthorizationRevision) {
      this.observedAuthorizationRevision = authorizationRevision;
      this.authorizationRecheckPending = true;
      this.invalidateForAuthorizationRecheck();
    }

    return this.loadWorkspaces();
  }

  private loadCapabilities(generation: number): void {
    const capabilityGeneration = ++this.capabilityLoadGeneration;
    this.http
      ?.get<WorkspaceCapabilitiesEnvelopeDto>('/api/workspaces/capabilities', {
        withCredentials: true,
      })
      .subscribe({
        next: (response) => {
          if (
            !this.isCurrentGeneration(generation) ||
            capabilityGeneration !== this.capabilityLoadGeneration
          ) {
            return;
          }

          this.pageCapabilities = mapWorkspacePageCapabilities(response);
          this.applyPageCapabilities();
        },
        error: () => {
          if (
            !this.isCurrentGeneration(generation) ||
            capabilityGeneration !== this.capabilityLoadGeneration
          ) {
            return;
          }

          this.pageCapabilities = [];
          this.applyPageCapabilities();
        },
      });
  }

  private applyWorkspaceResponse(
    response: unknown,
    identity: WorkspaceSelectionIdentity,
    generation: number,
  ): void {
    if (!this.isCurrentGeneration(generation) || !sameIdentity(identity, this.currentIdentity())) {
      return;
    }

    let cards: readonly WorkspaceCardViewModel[];
    try {
      cards = mapWorkspaceDashboardResponse(response);
    } catch {
      this.selection.markTransientFailure();
      this.dashboardState.set({
        ...this.emptyDashboard('error'),
        message: 'Workspace APIの応答形式が正しくありません。',
      });
      if (this.httpFallbackRecoveryGeneration === generation) {
        this.httpFallbackRecoveryGeneration = null;
      }
      return;
    }

    this.reconcileSelection(cards, identity);

    if (cards.length === 0) {
      this.dashboardState.set({
        ...this.emptyDashboard('noWorkspaceAccess'),
        pageCapabilities: this.pageCapabilities,
        message: this.emptyWorkspaceMessage(),
      });
      this.completeHttpFallbackRecovery(generation);
      return;
    }

    this.dashboardState.set({
      ...this.emptyDashboard('ready'),
      workspaces: cards,
      pageCapabilities: this.pageCapabilities,
    });
    this.completeHttpFallbackRecovery(generation);
  }

  private applyWorkspaceError(error: unknown, generation: number): void {
    if (!this.isCurrentGeneration(generation)) {
      return;
    }

    const status = normalizeApiError(error).httpStatus;
    const permissionDenied = status === 401 || status === 403;
    if (permissionDenied) {
      this.selection.markUnavailable(true);
    } else {
      this.selection.markTransientFailure();
    }
    if (this.httpFallbackRecoveryGeneration === generation) {
      this.httpFallbackRecoveryGeneration = null;
    }

    this.dashboardState.set({
      ...this.emptyDashboard(permissionDenied ? 'permissionDenied' : 'error'),
      message: workspaceErrorMessage(status),
    });
  }

  private applyPageCapabilities(): void {
    const status = this.dashboardState().status;
    if (status !== 'ready' && status !== 'noWorkspaceAccess') {
      return;
    }

    this.dashboardState.update((dashboard) => ({
      ...dashboard,
      pageCapabilities: this.pageCapabilities,
      message:
        dashboard.status === 'noWorkspaceAccess'
          ? this.emptyWorkspaceMessage()
          : dashboard.message,
    }));
  }

  private completeHttpFallbackRecovery(generation: number): void {
    if (this.httpFallbackRecoveryGeneration !== generation) {
      return;
    }

    this.httpFallbackRecoveryGeneration = null;
    void this.realtime.runAuthoritativeHttpCatchUps();
  }

  private reconcileCurrentRoute(url = this.router?.url ?? ''): void {
    const identity = this.currentIdentity();
    const dashboard = this.dashboardState();
    if (!identity || dashboard.status !== 'ready') {
      return;
    }

    this.reconcileSelection(dashboard.workspaces, identity, url);
  }

  private reconcileSelection(
    cards: readonly WorkspaceCardViewModel[],
    identity: WorkspaceSelectionIdentity | null,
    routeUrl = this.router?.url ?? '',
  ): void {
    if (!identity) {
      this.selection.markUnavailable();
      return;
    }

    const routeWorkspaceId = workspaceIdFromRoute(routeUrl);
    this.selection.reconcileAuthorizedWorkspaces(
      cards.map((card) => ({ id: card.id, label: card.displayName })),
      identity,
      routeWorkspaceId,
    );
    if (routeWorkspaceId && !cards.some((card) => card.id === routeWorkspaceId)) {
      this.navigateRevokedRouteToNeutral(routeWorkspaceId);
    }
  }

  private navigateRevokedRouteToNeutral(revokedWorkspaceId: string): void {
    if (!this.router || workspaceIdFromRoute(this.router.url) !== revokedWorkspaceId) {
      return;
    }

    // The old scoped projection was cleared before this navigation. A guard
    // cancellation therefore remains fail closed instead of mounting the
    // revoked route under an unrelated sole/preferred Workspace.
    void this.router.navigateByUrl('/workspaces').catch(() => undefined);
  }

  private currentIdentity(): WorkspaceSelectionIdentity | null {
    const session = this.authSession.session();
    if (!session.isAuthenticated || !session.currentTenant || !session.currentUser) {
      return null;
    }

    return {
      tenantId: session.currentTenant.tenantId,
      userId: session.currentUser.userId,
    };
  }

  private isCurrentGeneration(generation: number): boolean {
    return generation === this.loadGeneration;
  }

  private emptyWorkspaceMessage(): string {
    if (this.pageCapabilities.includes('createWorkspace')) {
      return '最初のWorkspaceを作成して、リサーチを始められます。';
    }

    const capabilities = this.authSession.session().capabilities;
    if (capabilities.includes('admin:access')) {
      return '作成権限がありません。権限のあるTenant管理者に依頼してください。';
    }

    return 'Workspaceに所属していません。管理者に招待を依頼してください。';
  }

  private emptyDashboard(
    status: WorkspaceDashboardViewModel['status'],
  ): WorkspaceDashboardViewModel {
    return {
      status,
      title: 'Workspaces',
      subtitle: '参加中のWorkspace',
      workspaces: [],
      pageCapabilities: [],
    };
  }
}

function identityKey(identity: WorkspaceSelectionIdentity | null): string | null {
  return identity ? `${identity.tenantId}\u0000${identity.userId}` : null;
}

function sameIdentity(
  left: WorkspaceSelectionIdentity | null,
  right: WorkspaceSelectionIdentity | null,
): boolean {
  return left?.tenantId === right?.tenantId && left?.userId === right?.userId;
}

function workspaceErrorMessage(status: number | undefined): string {
  if (status === 0) {
    return 'ネットワークエラーまたはAPIに接続できません。';
  }

  if (status === 401) {
    return '未ログインまたは認証cookieが無効です。再ログインしてください。';
  }

  if (status === 403) {
    return 'Workspaceを表示する権限がありません。';
  }

  if (status === 404) {
    return 'Workspace APIが見つかりません。バックエンドのルートを確認してください。';
  }

  if (status !== undefined && status >= 500) {
    return 'Workspace APIでサーバーエラーが発生しました。';
  }

  return 'Workspaceを取得できません。';
}

function validateWorkspaceCreatePayload(
  payload: WorkspaceCreateRequestDto,
): readonly WorkspaceCreateFieldError[] {
  const errors: WorkspaceCreateFieldError[] = [];
  if (payload.name.length === 0) {
    errors.push({ field: 'name', message: 'Enter a Workspace name.' });
  } else if (payload.name.length > 160) {
    errors.push({ field: 'name', message: 'Workspace name must be 160 characters or fewer.' });
  }

  if (payload.description !== null && payload.description.length > 2000) {
    errors.push({ field: 'description', message: 'Description must be 2,000 characters or fewer.' });
  }

  if (payload.icon !== null && payload.icon.length > 120) {
    errors.push({ field: 'icon', message: 'Icon must be 120 characters or fewer.' });
  }

  return errors;
}

function createFieldErrors(error: FrontendApiError): readonly WorkspaceCreateFieldError[] {
  const fields = new Set<WorkspaceCreateField>();
  const addTarget = (target: string | undefined): void => {
    const field = createFieldFromTarget(target);
    if (field) {
      fields.add(field);
    }
  };
  addTarget(error.target);
  error.details.forEach((detail) => addTarget(detail.target));

  if (fields.size === 0 && error.code === 'ValidationFailed') {
    fields.add('form');
  }

  return [...fields].map((field) => ({
    field,
    message: fieldValidationMessage(field),
  }));
}

function createFieldFromTarget(target: string | undefined): WorkspaceCreateField | null {
  const normalized = target?.trim().toLowerCase();
  if (!normalized) {
    return null;
  }

  if (normalized === 'body.name' || normalized === 'name' || normalized === '$.name') {
    return 'name';
  }
  if (
    normalized === 'body.description' ||
    normalized === 'description' ||
    normalized === '$.description'
  ) {
    return 'description';
  }
  if (normalized === 'body.icon' || normalized === 'icon' || normalized === '$.icon') {
    return 'icon';
  }

  return null;
}

function fieldValidationMessage(field: WorkspaceCreateField): string {
  switch (field) {
    case 'name':
      return 'Review the Workspace name.';
    case 'description':
      return 'Review the Workspace description.';
    case 'icon':
      return 'Review the Workspace icon.';
    default:
      return 'Review the form values.';
  }
}

function createErrorMessage(error: FrontendApiError): string {
  if (error.httpStatus === 0) {
    return 'The server could not be reached. Retry with the same details.';
  }
  if (error.httpStatus === 401) {
    return 'Your session is no longer available. Sign in again before creating a Workspace.';
  }
  if (error.httpStatus === 403 || error.code === 'CapabilityDenied') {
    return 'You do not currently have permission to create a Workspace.';
  }
  if (error.code === 'IdempotencyConflict' || error.httpStatus === 409) {
    return 'This create request conflicts with server state. Review the details before retrying.';
  }
  if (error.httpStatus >= 500) {
    return 'The Workspace may have been created. Retry with the same details so the server can safely reconcile the request.';
  }
  if (error.code === 'ValidationFailed' || error.httpStatus === 400) {
    return 'Review the highlighted fields and try again.';
  }

  return 'The Workspace could not be created.';
}

function createIdempotencyKey(): string {
  return createCryptographicUuid();
}

function routePath(url: string): string {
  return url.split(/[?#]/u, 1)[0];
}