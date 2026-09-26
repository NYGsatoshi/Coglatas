import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, EMPTY, finalize, forkJoin, map, of, switchMap } from 'rxjs';

import { normalizeApiError } from '../../core/api/api-error.adapter';
import { FrontendApiError } from '../../core/api/api-error.model';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import {
  ProtectedStateClearReason,
  RealtimeFacade,
} from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import {
  CoglatasGanttDependency,
  CoglatasGanttEditIntent,
  CoglatasGanttItem,
  CoglatasGanttWarning,
  CoglatasKanbanMoveRequest
} from '../../shared/ui/contracts/coglatas-complex-adapter.contracts';
import {
  ProjectGanttCommandResult,
  ProjectGanttSnapshot,
  mapProjectGanttCommandResponse,
  mapProjectGanttSnapshot
} from './project-gantt.models';
import {
  ProjectKanbanCard,
  ProjectKanbanColumn,
  ProjectKanbanSnapshot,
  ProjectKanbanSwimlane,
  mapProjectKanbanCommand,
  mapProjectKanbanSnapshot,
  swimlaneApiValue
} from './project-kanban.models';
import {
  MoveTaskOnKanbanRequestDto,
  AddTaskDependencyRequestDto,
  PagedResponseDto,
  ProjectActivationResponseError,
  ProjectDto,
  ProjectGanttCommandResponseDto,
  ProjectGanttSnapshotDto,
  ProjectKanbanCommandResponseDto,
  ProjectKanbanSnapshotDto,
  RemoveTaskDependencyResponseDto,
  TaskDto,
  TaskDependencyCommandResponseDto,
  UpdateTaskProgressRequestDto,
  UpdateTaskScheduleRequestDto,
  UpdateProjectKanbanConfigRequestDto,
  mapProjectActivationSuccess
} from './projects.api';
import { mapProjectDtoToRecord, mapTaskDtoToRecord } from './projects.mapper';
import { ProjectSummaryViewModel, ProjectsPageStatus, TaskGridRow, TaskMockRecord } from './projects.types';

export type ProjectDetailTab = 'overview' | 'tasks' | 'list' | 'schedule' | 'workload' | 'members';
export type ProjectKanbanStatus = 'disabled' | 'loading' | 'ready' | 'empty' | 'permissionDenied' | 'notFound' | 'error' | 'conflict' | 'rollback';
export type ProjectScheduleStatus = 'loading' | 'ready' | 'empty' | 'permissionDenied' | 'error' | 'conflict' | 'rollback' | 'degraded';

export interface ProjectScheduleViewModel {
  readonly status: ProjectScheduleStatus;
  readonly snapshot: ProjectGanttSnapshot | null;
  readonly canonicalEnabled: boolean;
  readonly busyItemId: string | null;
  readonly focusItemId: string | null;
  readonly feedback: string | null;
  readonly preservedIntent: CoglatasGanttEditIntent | null;
  readonly realtimeDegraded: boolean;
  readonly reconciliationQueued: boolean;
  readonly error?: FrontendApiError;
}

export interface ProjectWorkloadViewModel { readonly userId: string; readonly displayName: string; readonly projectRole: string; readonly assignedTaskCount: number; readonly overdueTaskCount: number; readonly estimatedHours: number; readonly actualHours: number; }
export interface ProjectMemberViewModel { readonly userId: string; readonly displayName: string; readonly role: string; }
export interface ProjectKanbanViewModel {
  readonly status: ProjectKanbanStatus;
  readonly snapshot: ProjectKanbanSnapshot | null;
  readonly busyTaskId: string | null;
  readonly focusTaskId: string | null;
  readonly feedback: string | null;
  readonly error?: FrontendApiError;
  readonly realtimeDegraded: boolean;
  readonly reconciliationQueued: boolean;
}
export interface ProjectDetailViewModel {
  readonly status: ProjectsPageStatus;
  readonly project?: ProjectSummaryViewModel;
  readonly tasks: readonly TaskGridRow[];
  readonly taskListFeedback: string | null;
  readonly kanban: ProjectKanbanViewModel;
  readonly schedule: ProjectScheduleViewModel;
  readonly workload: readonly ProjectWorkloadViewModel[];
  readonly members: readonly ProjectMemberViewModel[];
  readonly activation: ProjectActivationViewModel;
  readonly message?: string;
}

export type ProjectActivationStatus =
  | 'idle'
  | 'submitting'
  | 'reconciling'
  | 'success'
  | 'failure'
  | 'conflict'
  | 'uncertain'
  | 'permissionDenied';

export interface ProjectActivationViewModel {
  readonly status: ProjectActivationStatus;
  readonly message: string | null;
  readonly requestId?: string;
}

interface ProjectActivationNotice {
  readonly projectId: string;
  readonly generation: number;
  readonly outcome: 'pending' | 'accepted' | 'failure' | 'conflict';
  readonly requestId?: string;
}

type KanbanLoadOutcome =
  | { readonly kind: 'disabled' }
  | { readonly kind: 'success'; readonly dto: ProjectKanbanSnapshotDto }
  | { readonly kind: 'error'; readonly error: unknown };

type ScheduleLoadOutcome =
  | { readonly kind: 'success'; readonly dto: ProjectGanttSnapshotDto }
  | { readonly kind: 'error'; readonly error: unknown };

const PROJECT_REALTIME_OWNER = 'project-detail';

type CommandOutcome<T> =
  | { readonly kind: 'success'; readonly value: T }
  | { readonly kind: 'error'; readonly error: unknown };

@Injectable({ providedIn: 'root' })
export class ProjectDetailFacade {
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeFacade);
  private readonly flags = inject(FrontendFeatureFlagsService);
  private readonly continueWorkingHistory = inject(ContinueWorkingHistoryService);
  private readonly state = signal<ProjectDetailViewModel>(this.loading());
  private projectId: string | null = null;
  private projectOperational = false;
  private realtimeCleanups: (() => void)[] = [];
  private refreshPending = false;
  private draftProjectRefreshInFlight = false;
  private interactionActive = false;
  private moveInFlight = false;
  private scheduleInteractionActive = false;
  private scheduleCommandInFlight = false;
  private scheduleCommandGeneration = 0;
  private loadGeneration = 0;
  private kanbanRequestGeneration = 0;
  private scheduleRequestGeneration = 0;
  private authorizationGeneration = 0;
  private activationGeneration = 0;
  private activationInFlight = false;
  private activationNotice: ProjectActivationNotice | null = null;
  private scheduleRefreshPending = false;
  private scheduleRefreshInFlight = false;
  private scheduleRefreshAfterFlight = false;
  private scheduleRefreshAfterFlightFeedback: string | undefined;
  private taskListRequestGeneration = 0;
  private taskListAppliedGeneration = 0;
  private taskListRefreshPending = false;
  private taskListRefreshInFlight = false;
  private taskListRefreshAfterFlight = false;
  private taskListRefreshFlightGeneration = 0;
  private taskListRefreshFailureGeneration = 0;
  private taskListRefreshFeedback: string | null = null;
  private pendingDependencySequence = 0;
  private selectedSwimlane: ProjectKanbanSwimlane | null = null;
  private includeOlderCompleted = false;

  constructor() {
    this.realtime.durableEvents$.subscribe((event) => this.handleRealtimeEvent(event));
    this.realtime.registerProtectedStateClearer?.(
      PROJECT_REALTIME_OWNER,
      (reason) => this.clearProtectedState(reason),
    );
  }

  view(): ProjectDetailViewModel {
    const current = this.state();
    return {
      ...current,
      taskListFeedback: this.taskListRefreshFeedback,
      kanban: {
        ...current.kanban,
        realtimeDegraded: this.realtime.connectionState() !== 'Connected'
      },
      schedule: {
        ...current.schedule,
        status: this.schedulePresentationStatus(current.schedule),
        realtimeDegraded: this.realtime.connectionState() !== 'Connected'
      }
    };
  }

  load(projectId: string): void {
    if (this.projectId !== projectId)
      {this.activationNotice = null;}
    const loadGeneration = ++this.loadGeneration;
    this.activationGeneration++;
    this.activationInFlight = false;
    this.scheduleCommandGeneration++;
    this.scheduleCommandInFlight = false;
    const authorizationGeneration = this.authorizationGeneration;
    const initialKanbanRequestGeneration = ++this.kanbanRequestGeneration;
    const initialScheduleRequestGeneration = ++this.scheduleRequestGeneration;
    const initialTaskListRequestGeneration = ++this.taskListRequestGeneration;
    this.projectId = projectId;
    this.projectOperational = false;
    this.draftProjectRefreshInFlight = false;
    this.moveInFlight = false;
    this.interactionActive = false;
    this.scheduleInteractionActive = false;
    this.scheduleRefreshInFlight = false;
    this.scheduleRefreshAfterFlight = false;
    this.scheduleRefreshAfterFlightFeedback = undefined;
    this.resetTaskListRefreshState();
    this.selectedSwimlane = null;
    this.includeOlderCompleted = false;
    this.releaseRealtime();
    this.registerRealtime(projectId);
    this.state.set(this.loading());
    this.http.get<ProjectDto>(`/api/projects/${projectId}`, { withCredentials: true }).pipe(
      switchMap((project) => {
        if (!this.projectRequestIsCurrent(projectId, loadGeneration, authorizationGeneration))
          {return EMPTY;}
        const record = mapProjectDtoToRecord(project);
        this.projectOperational = record.isOperational === true;
        if (!record.isOperational)
          {return of(this.draftReady(project));}
        return forkJoin({
          tasks: this.http.get<PagedResponseDto<TaskDto>>(`/api/projects/${projectId}/tasks`, { withCredentials: true }),
          kanban: this.initialKanbanRequest(projectId),
          gantt: this.initialScheduleRequest(projectId),
          workload: this.http.get<unknown>(`/api/projects/${projectId}/workload`, { withCredentials: true }),
          members: this.http.get<unknown>(`/api/projects/${projectId}/members`, { withCredentials: true })
        }).pipe(map((response) => this.ready(
          project,
          response.tasks.items ?? [],
          response.kanban,
          response.gantt,
          response.workload,
          response.members
        )));
      }),
      catchError((error: unknown) => of(this.failure(error)))
    ).subscribe((view) => {
      if (this.projectId === projectId &&
          this.loadGeneration === loadGeneration &&
          this.authorizationGeneration === authorizationGeneration) {
        const latest = this.state();
        if (view.status !== 'ready') {
          this.state.set(view);
          return;
        }

        // A later request that failed must not make a valid initial response
        // disappear. Only a later successfully applied Task snapshot wins.
        const applyInitialTasks = initialTaskListRequestGeneration >= this.taskListAppliedGeneration;
        if (applyInitialTasks)
          {this.taskListAppliedGeneration = initialTaskListRequestGeneration;}
        this.clearTaskListRefreshFeedback(initialTaskListRequestGeneration);
        this.state.set({
          ...view,
          kanban: this.kanbanRequestGeneration === initialKanbanRequestGeneration
            ? view.kanban
            : latest.kanban,
          schedule: this.scheduleRequestGeneration === initialScheduleRequestGeneration
            ? view.schedule
            : latest.schedule,
          tasks: applyInitialTasks ? view.tasks : latest.tasks,
          project: applyInitialTasks
            ? view.project
            : this.withTaskCounts(view.project, latest.tasks),
          activation: this.activationForAuthorizedProjection(
            projectId,
            view.project?.isOperational === true,
            view.activation
          )
        });
        const appliedProject = this.state().project;
        if (appliedProject) {
          this.continueWorkingHistory.touchProject(appliedProject.id, appliedProject.workspaceId);
        }
      }
    });
  }

  retryKanban(): void { this.refreshKanban(true); }
  retryTaskList(): void { this.queueTaskListRefresh(); }
  retrySchedule(): void { this.refreshSchedule(true, 'Schedule refreshed from authoritative HTTP state.'); }

  activate(): void {
    const current = this.state();
    const project = current.project;
    if (
      this.projectId !== project?.id ||
      project.canActivate !== true ||
      !Number.isSafeInteger(project.versionNo) ||
      (project.versionNo ?? 0) <= 0 ||
      this.activationInFlight ||
      !['idle', 'failure', 'conflict'].includes(current.activation.status)
    ) {return;}

    const projectId = project.id;
    const expectedVersion = project.versionNo!;
    const generation = ++this.activationGeneration;
    const loadGeneration = this.loadGeneration;
    const authorizationGeneration = this.authorizationGeneration;
    this.activationInFlight = true;
    this.activationNotice = { projectId, generation, outcome: 'pending' };
    this.state.set({
      ...current,
      activation: {
        status: 'submitting',
        message: 'Activating Project…'
      }
    });

    this.http.post<unknown>(
      `/api/projects/${projectId}/activate`,
      { expectedVersion },
      { observe: 'response', withCredentials: true }
    ).pipe(
      map((response) => ({
        kind: 'success' as const,
        response: mapProjectActivationSuccess(response.body, projectId, response.status)
      })),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((outcome) => {
      const noticeMatchesAttempt =
        this.activationNotice?.projectId === projectId &&
        this.activationNotice.generation === generation;
      let activationDenied = false;
      let deniedRequestId: string | undefined;

      if (outcome.kind === 'success' && noticeMatchesAttempt) {
        this.activationNotice = {
          projectId,
          generation,
          outcome: 'accepted',
          requestId: outcome.response.requestId
        };
      } else if (outcome.kind === 'error' && noticeMatchesAttempt) {
        const normalized = normalizeApiError(outcome.error);
        const responseError = outcome.error instanceof ProjectActivationResponseError
          ? outcome.error
          : null;
        const requestId = responseError?.requestId ?? normalized.requestId;
        if (
          normalized.httpStatus === 401 ||
          normalized.httpStatus === 403 ||
          normalized.httpStatus === 404
        ) {
          activationDenied = true;
          deniedRequestId = requestId;
          this.activationNotice = null;
        } else {
          this.activationNotice = {
            projectId,
            generation,
            outcome: normalized.httpStatus === 409 ? 'conflict' : 'failure',
            requestId
          };
        }
      }

      if (!this.activationAttemptIsCurrent(
        projectId,
        generation,
        loadGeneration,
        authorizationGeneration
      )) {
        if (
          noticeMatchesAttempt &&
          activationDenied &&
          this.projectId === projectId
        ) {
          this.activationInFlight = false;
          this.setActivationPermissionDenied(deniedRequestId);
          return;
        }
        if (
          noticeMatchesAttempt &&
          this.activationNotice?.projectId === projectId &&
          this.activationNotice.generation === generation &&
          this.projectId === projectId &&
          this.state().status === 'ready'
        ) {
          const current = this.state();
          if (current.project?.id === projectId && current.project.isOperational) {
            this.state.set({
              ...current,
              activation: this.activationForAuthorizedProjection(
                projectId,
                true,
                current.activation
              )
            });
          } else {
            this.refreshProjectProjections(
              projectId,
              this.authorizationGeneration,
              true
            );
          }
        }
        return;
      }

      if (outcome.kind === 'error') {
        const normalized = normalizeApiError(outcome.error);
        const responseError = outcome.error instanceof ProjectActivationResponseError
          ? outcome.error
          : null;
        const requestId = responseError?.requestId ?? normalized.requestId;
        if (
          normalized.httpStatus === 401 ||
          normalized.httpStatus === 403 ||
          normalized.httpStatus === 404
        ) {
          this.activationInFlight = false;
          this.activationNotice = null;
          this.setActivationPermissionDenied(requestId);
          return;
        }
        this.reconcileActivation(
          projectId,
          generation,
          loadGeneration,
          authorizationGeneration,
          false,
          normalized.httpStatus === 409 ? 'conflict' : 'failure',
          requestId
        );
        return;
      }

      // The command is committed before any navigation or projection refresh.
      // A later refresh failure must never cause a second activation POST.
      this.state.set({
        ...this.state(),
        activation: {
          status: 'success',
          message: 'Activation was accepted. Confirming authoritative Project state…',
          requestId: outcome.response.requestId
        }
      });
      this.reconcileActivation(
        projectId,
        generation,
        loadGeneration,
        authorizationGeneration,
        true,
        'failure',
        outcome.response.requestId
      );
    });
  }

  setScheduleInteractionActive(active: boolean): void {
    this.scheduleInteractionActive = active;
    if (!active && !this.scheduleCommandInFlight && this.state().schedule.reconciliationQueued)
      {this.refreshSchedule(true, 'Queued schedule changes synchronized from authoritative HTTP state.');}
  }

  applyGanttEdit(intent: CoglatasGanttEditIntent): void {
    const schedule = this.state().schedule;
    if (!schedule.canonicalEnabled || !schedule.snapshot || this.scheduleCommandInFlight)
      {return;}

    if (intent.kind === 'schedule') {
      this.updateSchedule(intent);
      return;
    }
    if (intent.kind === 'progress') {
      this.updateProgress(intent);
      return;
    }
    if (intent.kind === 'addDependency') {
      this.addDependency(intent);
      return;
    }
    this.removeDependency(intent);
  }

  reportGanttAdapterFailure(): void {
    const current = this.state();
    if (!current.schedule.snapshot) {return;}
    this.state.set({
      ...current,
      schedule: {
        ...current.schedule,
        status: 'degraded',
        feedback: 'The visual timeline is unavailable. The accessible schedule forms and HTTP refresh remain available.'
      }
    });
  }

  clearPreservedScheduleIntent(): void {
    const current = this.state();
    if (!current.schedule.preservedIntent) {return;}
    this.state.set({
      ...current,
      schedule: { ...current.schedule, preservedIntent: null }
    });
  }

  retryPreservedScheduleIntent(): void {
    const current = this.state();
    const snapshot = current.schedule.snapshot;
    const preserved = current.schedule.preservedIntent;
    if (!snapshot || !preserved || this.scheduleCommandInFlight)
      {return;}
    const taskId = preserved.kind === 'addDependency' || preserved.kind === 'removeDependency'
      ? preserved.successorTaskId
      : preserved.taskId;
    const item = scheduleItem(snapshot, taskId);
    if (!item)
      {return;}
    this.applyGanttEdit({ ...preserved, expectedVersion: item.version });
  }

  setKanbanInteractionActive(active: boolean): void {
    this.interactionActive = active;
    if (!active && !this.moveInFlight && this.state().kanban.reconciliationQueued)
      {this.refreshKanban(true);}
  }

  private updateSchedule(intent: Extract<CoglatasGanttEditIntent, { readonly kind: 'schedule' }>): void {
    const current = this.state();
    const snapshot = current.schedule.snapshot;
    const item = snapshot ? scheduleItem(snapshot, intent.taskId) : undefined;
    if (!snapshot || !item || !canEditSchedule(snapshot, item, intent) || item.version !== intent.expectedVersion)
      {return;}

    const rollbackSnapshot = snapshot;
    const optimisticSnapshot = updateScheduleItem(snapshot, item, {
      ...item,
      plannedStartDate: intent.plannedStartDate,
      plannedEndDate: intent.plannedEndDate,
      milestoneDate: intent.milestoneDate
    });
    const commandGeneration = this.beginScheduleCommand(
      current,
      optimisticSnapshot,
      intent.taskId,
      `Saving ${item.title} schedule...`
    );

    const request: UpdateTaskScheduleRequestDto = {
      plannedStartDate: intent.plannedStartDate,
      plannedEndDate: intent.plannedEndDate,
      milestoneDate: intent.milestoneDate,
      expectedVersion: intent.expectedVersion
    };
    const projectId = this.projectId;
    const authorizationGeneration = this.authorizationGeneration;
    this.http.patch<ProjectGanttCommandResponseDto>(
      `/api/tasks/${item.taskId}/schedule`,
      request,
      { withCredentials: true }
    ).pipe(
      map((dto) => ({ kind: 'success' as const, value: mapProjectGanttCommandResponse(dto) })),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((result) =>
      this.completeTaskScheduleCommand(
        result,
        intent,
        rollbackSnapshot,
        projectId,
        authorizationGeneration,
        commandGeneration,
        'Schedule saved.'
      ));
  }

  private updateProgress(intent: Extract<CoglatasGanttEditIntent, { readonly kind: 'progress' }>): void {
    const current = this.state();
    const snapshot = current.schedule.snapshot;
    const item = snapshot ? scheduleItem(snapshot, intent.taskId) : undefined;
    if (!snapshot || !item || !canEditProgress(snapshot, item, intent.progressPercent) || item.version !== intent.expectedVersion)
      {return;}

    const rollbackSnapshot = snapshot;
    const optimisticSnapshot = updateScheduleItem(snapshot, item, {
      ...item,
      progressPercent: intent.progressPercent
    });
    const commandGeneration = this.beginScheduleCommand(
      current,
      optimisticSnapshot,
      intent.taskId,
      `Saving ${item.title} progress...`
    );

    const request: UpdateTaskProgressRequestDto = {
      progressPercent: intent.progressPercent,
      expectedVersion: intent.expectedVersion
    };
    const projectId = this.projectId;
    const authorizationGeneration = this.authorizationGeneration;
    this.http.patch<ProjectGanttCommandResponseDto>(
      `/api/tasks/${item.taskId}/progress`,
      request,
      { withCredentials: true }
    ).pipe(
      map((dto) => ({ kind: 'success' as const, value: mapProjectGanttCommandResponse(dto) })),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((result) =>
      this.completeTaskScheduleCommand(
        result,
        intent,
        rollbackSnapshot,
        projectId,
        authorizationGeneration,
        commandGeneration,
        'Progress saved.'
      ));
  }

  private addDependency(intent: Extract<CoglatasGanttEditIntent, { readonly kind: 'addDependency' }>): void {
    const current = this.state();
    const snapshot = current.schedule.snapshot;
    const successor = snapshot ? scheduleItem(snapshot, intent.successorTaskId) : undefined;
    const predecessor = snapshot ? scheduleItem(snapshot, intent.predecessorTaskId) : undefined;
    if (!snapshot || !successor || !predecessor ||
        !canManageDependencies(snapshot, successor) ||
        successor.kind !== 'task' || predecessor.kind !== 'task' ||
        successor.taskId === predecessor.taskId ||
        successor.version !== intent.expectedVersion ||
        snapshot.dependencies.some((dependency) =>
          dependency.predecessorTaskId === predecessor.taskId &&
          dependency.successorTaskId === successor.taskId))
      {return;}

    const rollbackSnapshot = snapshot;
    const pendingDependency: CoglatasGanttDependency = {
      dependencyId: `local-pending:${++this.pendingDependencySequence}`,
      predecessorTaskId: predecessor.taskId,
      successorTaskId: successor.taskId,
      type: 'finishToStart',
      editable: false,
      version: successor.version,
      warnings: []
    };
    const optimisticSnapshot: ProjectGanttSnapshot = {
      ...snapshot,
      dependencies: [...snapshot.dependencies, pendingDependency]
    };
    const commandGeneration = this.beginScheduleCommand(
      current,
      optimisticSnapshot,
      successor.taskId,
      `Adding dependency for ${successor.title}...`
    );
    const request: AddTaskDependencyRequestDto = {
      predecessorTaskId: predecessor.taskId,
      dependencyType: 'FinishToStart',
      expectedVersion: intent.expectedVersion
    };
    const projectId = this.projectId;
    const authorizationGeneration = this.authorizationGeneration;
    this.http.post<TaskDependencyCommandResponseDto>(
      `/api/tasks/${successor.taskId}/dependencies`,
      request,
      { withCredentials: true }
    ).pipe(
      map((value) => ({ kind: 'success' as const, value })),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((result) => {
      if (result.kind === 'success' &&
          (result.value.successorTaskId !== successor.taskId ||
           result.value.predecessorTaskId !== predecessor.taskId ||
           result.value.dependencyType !== 'FinishToStart' ||
           result.value.version <= intent.expectedVersion)) {
        this.completeScheduleFailure(
          new Error('The dependency response did not match the requested canonical Task pair.'),
          intent,
          rollbackSnapshot,
          projectId,
          authorizationGeneration,
          commandGeneration
        );
        return;
      }
      this.completeDependencyCommand(
        result,
        intent,
        rollbackSnapshot,
        projectId,
        authorizationGeneration,
        commandGeneration,
        'Dependency added.'
      );
    });
  }

  private removeDependency(intent: Extract<CoglatasGanttEditIntent, { readonly kind: 'removeDependency' }>): void {
    const current = this.state();
    const snapshot = current.schedule.snapshot;
    const dependency = snapshot?.dependencies.find((item) => item.dependencyId === intent.dependencyId);
    const successor = snapshot ? scheduleItem(snapshot, intent.successorTaskId) : undefined;
    if (!snapshot || !dependency || !successor || !dependency.editable ||
        dependency.successorTaskId !== successor.taskId ||
        !canManageDependencies(snapshot, successor) ||
        successor.version !== intent.expectedVersion)
      {return;}

    const rollbackSnapshot = snapshot;
    const optimisticSnapshot = {
      ...snapshot,
      dependencies: snapshot.dependencies.filter((item) => item.dependencyId !== dependency.dependencyId)
    };
    const commandGeneration = this.beginScheduleCommand(
      current,
      optimisticSnapshot,
      successor.taskId,
      `Removing dependency for ${successor.title}...`
    );
    const projectId = this.projectId;
    const authorizationGeneration = this.authorizationGeneration;
    const params = new HttpParams().set('expectedVersion', String(intent.expectedVersion));
    this.http.delete<RemoveTaskDependencyResponseDto>(
      `/api/tasks/${successor.taskId}/dependencies/${dependency.dependencyId}`,
      { params, withCredentials: true }
    ).pipe(
      map((value) => ({ kind: 'success' as const, value })),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((result) =>
      this.completeDependencyCommand(
        result,
        intent,
        rollbackSnapshot,
        projectId,
        authorizationGeneration,
        commandGeneration,
        'Dependency removed.'
      ));
  }

  private beginScheduleCommand(
    current: ProjectDetailViewModel,
    optimisticSnapshot: ProjectGanttSnapshot,
    busyItemId: string,
    feedback: string
  ): number {
    this.scheduleCommandInFlight = true;
    const commandGeneration = ++this.scheduleCommandGeneration;
    this.state.set({
      ...current,
      schedule: {
        ...current.schedule,
        status: scheduleStatus(optimisticSnapshot),
        snapshot: optimisticSnapshot,
        busyItemId,
        focusItemId: null,
        feedback,
        preservedIntent: null,
        error: undefined
      }
    });
    return commandGeneration;
  }

  private completeTaskScheduleCommand(
    result: CommandOutcome<ProjectGanttCommandResult>,
    intent: Extract<CoglatasGanttEditIntent, { readonly kind: 'schedule' | 'progress' }>,
    rollbackSnapshot: ProjectGanttSnapshot,
    projectId: string | null,
    authorizationGeneration: number,
    commandGeneration: number,
    successFeedback: string
  ): void {
    if (this.projectId !== projectId ||
        this.authorizationGeneration !== authorizationGeneration ||
        this.scheduleCommandGeneration !== commandGeneration)
      {return;}
    if (result.kind === 'error') {
      this.completeScheduleFailure(
        result.error,
        intent,
        rollbackSnapshot,
        projectId,
        authorizationGeneration,
        commandGeneration
      );
      return;
    }

    this.scheduleCommandInFlight = false;
    const latest = this.state();
    const snapshot = latest.schedule.snapshot;
    const item = snapshot ? scheduleItem(snapshot, intent.taskId) : undefined;
    const validNoOpProgress =
      item !== undefined &&
      intent.kind === 'progress' &&
      result.value.version === intent.expectedVersion &&
      result.value.progressPercent === intent.progressPercent &&
      result.value.plannedStartDate === item.plannedStartDate &&
      result.value.plannedEndDate === item.plannedEndDate &&
      result.value.milestoneDate === item.milestoneDate;
    if (!snapshot ||
        result.value.taskId !== item?.taskId ||
        result.value.kind !== item.kind ||
        (result.value.version <= intent.expectedVersion && !validNoOpProgress)) {
      this.completeScheduleFailure(
        new Error('The Task command response did not match the requested canonical WorkItem.'),
        intent,
        rollbackSnapshot,
        projectId,
        authorizationGeneration,
        commandGeneration
      );
      return;
    }

    const authoritativeItem: CoglatasGanttItem = {
      ...item,
      plannedStartDate: result.value.plannedStartDate,
      plannedEndDate: result.value.plannedEndDate,
      milestoneDate: result.value.milestoneDate,
      progressPercent: result.value.progressPercent,
      version: result.value.version,
      warnings: item.warnings
    };
    const authoritativeSnapshot = reconcileTaskCommandWarnings(
      updateScheduleItem(snapshot, item, authoritativeItem),
      item.taskId,
      result.value.warnings
    );
    const warningFeedback = result.value.warnings[0]?.message;
    const feedback = warningFeedback ? `${successFeedback} ${warningFeedback}` : successFeedback;
    this.state.set({
      ...latest,
      schedule: {
        ...latest.schedule,
        status: scheduleStatus(authoritativeSnapshot),
        snapshot: authoritativeSnapshot,
        busyItemId: null,
        focusItemId: item.taskId,
        feedback,
        preservedIntent: null,
        reconciliationQueued: false,
        error: undefined
      }
    });
    this.queueTaskListRefresh();
    this.refreshSchedule(true, feedback);
  }

  private completeDependencyCommand<T>(
    result: CommandOutcome<T>,
    intent: Extract<CoglatasGanttEditIntent, { readonly kind: 'addDependency' | 'removeDependency' }>,
    rollbackSnapshot: ProjectGanttSnapshot,
    projectId: string | null,
    authorizationGeneration: number,
    commandGeneration: number,
    successFeedback: string
  ): void {
    if (this.projectId !== projectId ||
        this.authorizationGeneration !== authorizationGeneration ||
        this.scheduleCommandGeneration !== commandGeneration)
      {return;}
    if (result.kind === 'error') {
      this.completeScheduleFailure(
        result.error,
        intent,
        rollbackSnapshot,
        projectId,
        authorizationGeneration,
        commandGeneration
      );
      return;
    }
    this.scheduleCommandInFlight = false;
    const latest = this.state();
    this.state.set({
      ...latest,
      schedule: {
        ...latest.schedule,
        busyItemId: null,
        focusItemId: intent.successorTaskId,
        feedback: successFeedback,
        preservedIntent: null,
        reconciliationQueued: false,
        error: undefined
      }
    });
    this.refreshSchedule(true, successFeedback);
  }

  private completeScheduleFailure(
    value: unknown,
    intent: CoglatasGanttEditIntent,
    rollbackSnapshot: ProjectGanttSnapshot,
    projectId: string | null,
    authorizationGeneration: number,
    commandGeneration: number
  ): void {
    if (this.projectId !== projectId ||
        this.authorizationGeneration !== authorizationGeneration ||
        this.scheduleCommandGeneration !== commandGeneration)
      {return;}
    this.scheduleCommandInFlight = false;
    const error = normalizeApiError(value);
    const latest = this.state();
    const focusItemId = intent.kind === 'addDependency' || intent.kind === 'removeDependency'
      ? intent.successorTaskId
      : intent.taskId;

    if (error.httpStatus === 401 || error.httpStatus === 403) {
      this.authorizationGeneration++;
      this.scheduleRequestGeneration++;
      this.scheduleRefreshInFlight = false;
      this.scheduleRefreshAfterFlight = false;
      this.scheduleRefreshAfterFlightFeedback = undefined;
      this.state.set({
        ...latest,
        schedule: {
          ...this.loadingSchedule(),
          status: 'permissionDenied',
          canonicalEnabled: latest.schedule.canonicalEnabled,
          feedback: 'Schedule permission changed. Protected schedule data was cleared.',
          error
        }
      });
      return;
    }
    if (error.httpStatus === 404) {
      this.scheduleRequestGeneration++;
      this.scheduleRefreshInFlight = false;
      this.scheduleRefreshAfterFlight = false;
      this.scheduleRefreshAfterFlightFeedback = undefined;
      this.state.set({
        ...latest,
        schedule: {
          ...this.loadingSchedule(),
          status: 'error',
          canonicalEnabled: latest.schedule.canonicalEnabled,
          feedback: error.message,
          error
        }
      });
      return;
    }

    const conflict = error.httpStatus === 409;
    this.state.set({
      ...latest,
      schedule: {
        ...latest.schedule,
        status: conflict ? 'conflict' : 'rollback',
        snapshot: rollbackSnapshot,
        busyItemId: null,
        focusItemId,
        feedback: conflict
          ? 'The WorkItem changed elsewhere. Your safe edit intent was preserved while authoritative schedule data is refetched.'
          : `The schedule change failed and was rolled back. ${error.message}`,
        preservedIntent: preserveGanttIntent(intent),
        reconciliationQueued: false,
        error
      }
    });
    if (conflict || latest.schedule.reconciliationQueued) {
      this.refreshSchedule(
        true,
        conflict
          ? 'Conflict reconciled from authoritative schedule data; the edit intent remains available.'
          : 'The failed edit was rolled back and queued schedule changes are being reconciled.'
      );
    }
  }

  moveTask(intent: CoglatasKanbanMoveRequest<ProjectKanbanCard>): void {
    const current = this.state();
    const snapshot = current.kanban.snapshot;
    const authoritativeCard = snapshot?.cards.find((card) => card.taskId === intent.item.taskId);
    if (!snapshot || !authoritativeCard || !authoritativeCard.canMove || this.moveInFlight)
      {return;}

    this.moveInFlight = true;
    const projectId = this.projectId;
    const authorizationGeneration = this.authorizationGeneration;
    const presentationSwimlane = this.selectedSwimlane ?? snapshot.selectedSwimlane;
    const presentationIncludesOlderCompleted = this.includeOlderCompleted;
    const rollbackSnapshot = snapshot;
    const optimistic = this.optimisticMove(snapshot, authoritativeCard, intent.targetStatus, intent.targetBeforeItemId, intent.targetAfterItemId);
    this.state.set({
      ...current,
      kanban: {
        ...current.kanban,
        status: optimistic.cards.length ? 'ready' : 'empty',
        snapshot: optimistic,
        busyTaskId: authoritativeCard.taskId,
        focusTaskId: null,
        feedback: `Moving ${authoritativeCard.summary}...`,
        error: undefined
      }
    });

    const request: MoveTaskOnKanbanRequestDto = {
      targetWorkflowStageId: intent.targetStatus,
      targetBeforeTaskId: intent.targetBeforeItemId,
      targetAfterTaskId: intent.targetAfterItemId,
      expectedTaskVersion: authoritativeCard.version,
      expectedBoardVersion: snapshot.boardVersion,
      reason: intent.reason
    };
    this.http.post<ProjectKanbanCommandResponseDto>(`/api/tasks/${authoritativeCard.taskId}/kanban-move`, request, { withCredentials: true })
      .pipe(
        map((dto) => ({ kind: 'success' as const, value: mapProjectKanbanCommand(dto) })),
        catchError((error: unknown) => of({ kind: 'error' as const, error }))
      )
      .subscribe((result) => {
        this.moveInFlight = false;
        const latest = this.state();
        if (this.projectId !== projectId || this.authorizationGeneration !== authorizationGeneration) {
          if (latest.kanban.reconciliationQueued) {this.refreshKanban(true);}
          return;
        }
        if (result.kind === 'success') {
          const warning = result.value.warnings.find((item) => item.code === 'KANBAN_WIP_LIMIT_EXCEEDED');
          const feedback = warning ? `Move saved. ${warning.message}` : 'Move saved.';
          const presentationRefetchRequired =
            result.value.snapshot.selectedSwimlane !== presentationSwimlane ||
            result.value.snapshot.includesOlderCompleted !== presentationIncludesOlderCompleted;
          this.selectedSwimlane = presentationRefetchRequired
            ? presentationSwimlane
            : result.value.snapshot.selectedSwimlane;
          this.includeOlderCompleted = presentationRefetchRequired
            ? presentationIncludesOlderCompleted
            : result.value.snapshot.includesOlderCompleted;
          this.state.set({
            ...latest,
            kanban: {
              status: result.value.snapshot.cards.length ? 'ready' : 'empty',
              snapshot: result.value.snapshot,
              busyTaskId: null,
              focusTaskId: result.value.focusTaskId ?? authoritativeCard.taskId,
              feedback,
              realtimeDegraded: latest.kanban.realtimeDegraded,
              reconciliationQueued: false
            }
          });
          this.queueTaskListRefresh();
          if (latest.kanban.reconciliationQueued || presentationRefetchRequired)
            {this.refreshKanban(true, feedback);}
          return;
        }

        const error = normalizeApiError(result.error);
        if (error.httpStatus === 404) {
          this.state.set({
            ...latest,
            kanban: {
              ...latest.kanban,
              status: 'notFound',
              snapshot: null,
              busyTaskId: null,
              focusTaskId: null,
              feedback: 'The Task or Project is no longer available.',
              error,
              reconciliationQueued: false
            }
          });
          return;
        }

        const conflict = error.httpStatus === 409;
        this.state.set({
          ...latest,
          kanban: {
            ...latest.kanban,
            status: conflict ? 'conflict' : 'rollback',
            snapshot: rollbackSnapshot,
            busyTaskId: null,
            focusTaskId: authoritativeCard.taskId,
            feedback: conflict ? 'The board changed elsewhere. The move was rolled back and an authoritative refresh is required.' : `Move denied and rolled back. ${error.message}`,
            error,
            reconciliationQueued: false
          }
        });
        if (conflict) {this.refreshKanban(true, 'Conflict resolved from the authoritative Project board.');}
      });
  }

  updateKanbanConfig(defaultSwimlane: ProjectKanbanSwimlane, columns: readonly ProjectKanbanColumn[]): void {
    const current = this.state();
    const snapshot = current.kanban.snapshot;
    if (!snapshot?.canConfigure || current.kanban.busyTaskId) {return;}
    const projectId = this.projectId;
    const authorizationGeneration = this.authorizationGeneration;
    const request: UpdateProjectKanbanConfigRequestDto = {
      expectedBoardVersion: snapshot.boardVersion,
      defaultSwimlane: swimlaneApiValue(defaultSwimlane),
      columns: columns.map((column, index) => ({
        workflowStageId: column.workflowStageId,
        displayOrder: index,
        wipWarningLimit: column.wipWarningLimit
      }))
    };
    this.state.set({ ...current, kanban: { ...current.kanban, feedback: 'Saving board configuration...', error: undefined } });
    this.http.put<ProjectKanbanCommandResponseDto>(`/api/projects/${snapshot.projectId}/kanban/config`, request, { withCredentials: true })
      .pipe(
        map((dto) => ({ kind: 'success' as const, value: mapProjectKanbanCommand(dto) })),
        catchError((error: unknown) => of({ kind: 'error' as const, error }))
      )
      .subscribe((result) => {
        const latest = this.state();
        if (this.projectId !== projectId || this.authorizationGeneration !== authorizationGeneration)
          {return;}
        if (result.kind === 'success') {
          this.selectedSwimlane = result.value.snapshot.selectedSwimlane;
          this.state.set({
            ...latest,
            kanban: {
              ...latest.kanban,
              status: result.value.snapshot.cards.length ? 'ready' : 'empty',
              snapshot: result.value.snapshot,
              focusTaskId: null,
              feedback: 'Board configuration saved.',
              error: undefined
            }
          });
          return;
        }
        const error = normalizeApiError(result.error);
        this.state.set({
          ...latest,
          kanban: {
            ...latest.kanban,
            status: error.httpStatus === 409 ? 'conflict' : 'rollback',
            feedback: `Board configuration was not saved. ${error.message}`,
            error
          }
        });
        if (error.httpStatus === 409) {this.refreshKanban(true, 'Configuration conflict resolved from the authoritative Project board.');}
      });
  }

  setKanbanSwimlane(swimlane: ProjectKanbanSwimlane): void {
    this.selectedSwimlane = swimlane;
    this.refreshKanban(true);
  }

  setIncludeOlderCompleted(include: boolean): void {
    this.includeOlderCompleted = include;
    this.refreshKanban(true);
  }

  release(): void {
    this.releaseRealtime();
    this.projectId = null;
    this.projectOperational = false;
    this.draftProjectRefreshInFlight = false;
    this.loadGeneration++;
    this.activationGeneration++;
    this.activationInFlight = false;
    this.activationNotice = null;
    this.kanbanRequestGeneration++;
    this.scheduleRequestGeneration++;
    this.taskListRequestGeneration++;
    this.scheduleCommandGeneration++;
    this.scheduleCommandInFlight = false;
    this.scheduleInteractionActive = false;
    this.scheduleRefreshInFlight = false;
    this.scheduleRefreshAfterFlight = false;
    this.scheduleRefreshAfterFlightFeedback = undefined;
    this.resetTaskListRefreshState();
  }

  private reconcileActivation(
    projectId: string,
    generation: number,
    loadGeneration: number,
    authorizationGeneration: number,
    committed: boolean,
    failureStatus: 'failure' | 'conflict',
    requestId?: string
  ): void {
    if (!this.activationAttemptIsCurrent(
      projectId,
      generation,
      loadGeneration,
      authorizationGeneration
    )) {return;}

    this.state.set({
      ...this.state(),
      activation: {
        status: 'reconciling',
        message: committed
          ? 'Activation was accepted. Confirming authoritative Project state…'
          : 'Confirming authoritative Project state before another activation attempt…',
        requestId
      }
    });

    const taskRequestGeneration = ++this.taskListRequestGeneration;
    const kanbanRequestGeneration = ++this.kanbanRequestGeneration;
    const scheduleRequestGeneration = ++this.scheduleRequestGeneration;
    this.http.get<ProjectDto>(`/api/projects/${projectId}`, { withCredentials: true }).pipe(
      switchMap((project) => {
        if (!this.activationAttemptIsCurrent(
          projectId,
          generation,
          loadGeneration,
          authorizationGeneration
        )) {return EMPTY;}
        const record = mapProjectDtoToRecord(project);
        if (!record.isOperational)
          {return of({ kind: 'draft' as const, project });}

        return forkJoin({
          tasks: this.http.get<PagedResponseDto<TaskDto>>(
            `/api/projects/${projectId}/tasks`,
            { withCredentials: true }
          ),
          kanban: this.initialKanbanRequest(projectId),
          gantt: this.initialScheduleRequest(projectId),
          workload: this.http.get<unknown>(
            `/api/projects/${projectId}/workload`,
            { withCredentials: true }
          ),
          members: this.http.get<unknown>(
            `/api/projects/${projectId}/members`,
            { withCredentials: true }
          )
        }).pipe(
          map((response) => ({
            kind: 'active' as const,
            view: this.ready(
              project,
              response.tasks.items ?? [],
              response.kanban,
              response.gantt,
              response.workload,
              response.members
            )
          })),
          catchError((error: unknown) => of({
            kind: 'activeProjectionError' as const,
            project,
            error
          }))
        );
      }),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((result) => {
      if (!this.activationAttemptIsCurrent(
        projectId,
        generation,
        loadGeneration,
        authorizationGeneration
      )) {return;}
      this.activationInFlight = false;

      if (result.kind === 'active') {
        this.projectOperational = true;
        this.taskListAppliedGeneration = taskRequestGeneration;
        const activation = this.activationForAuthorizedProjection(
          projectId,
          true,
          {
            status: 'success',
            message: 'Project activated. Operational views were loaded from authoritative state.',
            requestId
          }
        );
        this.state.set({
          ...result.view,
          kanban: this.kanbanRequestGeneration === kanbanRequestGeneration
            ? result.view.kanban
            : this.state().kanban,
          schedule: this.scheduleRequestGeneration === scheduleRequestGeneration
            ? result.view.schedule
            : this.state().schedule,
          activation
        });
        return;
      }

      if (result.kind === 'draft') {
        this.projectOperational = false;
        if (!committed)
          {this.activationNotice = null;}
        const activation = this.activationForAuthorizedProjection(
          projectId,
          false,
          committed
            ? {
                status: 'success',
                message: 'Activation was accepted, but the authoritative Project is still Draft. Reload before taking further action.',
                requestId
              }
            : {
                status: failureStatus,
                message: failureStatus === 'conflict'
                  ? 'The Project changed before activation. The latest Draft was reloaded; review it before retrying.'
                  : 'Activation was not confirmed. The latest Draft was reloaded and can be retried.',
                requestId
              }
        );
        this.state.set({
          ...this.draftReady(result.project),
          activation
        });
        return;
      }

      if (result.kind === 'activeProjectionError') {
        this.projectOperational = true;
        const normalized = normalizeApiError(result.error);
        if (
          normalized.httpStatus === 401 ||
          normalized.httpStatus === 403 ||
          normalized.httpStatus === 404
        ) {
          this.setActivationPermissionDenied(normalized.requestId ?? requestId);
          return;
        }
        this.activationForAuthorizedProjection(projectId, true, this.state().activation);
        this.state.set({
          ...this.projectOnlyReady(result.project),
          activation: {
            status: 'success',
            message: 'Project is active. Some operational views could not be loaded; refresh them before editing.',
            requestId
          }
        });
        return;
      }

      const normalized = normalizeApiError(result.error);
      if (
        normalized.httpStatus === 401 ||
        normalized.httpStatus === 403 ||
        normalized.httpStatus === 404
      ) {
        this.setActivationPermissionDenied(normalized.requestId ?? requestId);
        return;
      }
      const latest = this.state();
      this.state.set({
        ...latest,
        activation: {
          status: 'uncertain',
          message: 'Activation outcome could not be confirmed. Retry is disabled until the Project is reloaded.',
          requestId: normalized.requestId ?? requestId
        }
      });
    });
  }

  private activationAttemptIsCurrent(
    projectId: string,
    generation: number,
    loadGeneration: number,
    authorizationGeneration: number
  ): boolean {
    return this.projectId === projectId &&
      this.activationGeneration === generation &&
      this.loadGeneration === loadGeneration &&
      this.authorizationGeneration === authorizationGeneration;
  }

  private projectRequestIsCurrent(
    projectId: string,
    loadGeneration: number,
    authorizationGeneration: number
  ): boolean {
    return this.projectId === projectId &&
      this.loadGeneration === loadGeneration &&
      this.authorizationGeneration === authorizationGeneration;
  }

  private setActivationPermissionDenied(requestId?: string): void {
    this.projectOperational = false;
    this.draftProjectRefreshInFlight = false;
    this.state.set({
      ...this.loading(),
      status: 'permissionDenied',
      message: 'Project activation is not available for the current session.',
      activation: {
        status: 'permissionDenied',
        message: 'Project activation is not available for the current session.',
        requestId
      }
    });
  }

  private registerRealtime(projectId: string): void {
    this.realtimeCleanups = [
      this.realtime.registerSubscription(PROJECT_REALTIME_OWNER, { subscriptionType: 'project', resourceId: projectId }),
      this.realtime.registerCatchUp(PROJECT_REALTIME_OWNER, (context) => {
        const wasOperational = this.projectOperational;
        const denied = context.deniedOwners.has(PROJECT_REALTIME_OWNER);
        if (denied)
          {this.clearProtectedProjectionsForDeniedSubscription();}
        this.refreshProjectProjections(projectId, this.authorizationGeneration, !wasOperational);
        if (wasOperational) {
          this.refreshKanban(
            false,
            denied
              ? 'Project access was denied during reconnect. Protected board data was cleared before authoritative HTTP revalidation.'
              : 'Project board synchronized from authoritative HTTP state.',
            true
          );
          this.refreshSchedule(
            false,
            denied
              ? 'Project access was denied during reconnect. Protected schedule data was cleared before authoritative HTTP revalidation.'
              : 'Schedule synchronized from authoritative HTTP state.',
            true
          );
        }
      })
    ];
  }

  private clearProtectedProjectionsForDeniedSubscription(): void {
    this.authorizationGeneration++;
    this.loadGeneration++;
    this.activationGeneration++;
    this.activationInFlight = false;
    this.projectOperational = false;
    this.draftProjectRefreshInFlight = false;
    this.kanbanRequestGeneration++;
    this.scheduleRequestGeneration++;
    this.taskListRequestGeneration++;
    this.scheduleCommandGeneration++;
    this.moveInFlight = false;
    this.interactionActive = false;
    this.scheduleCommandInFlight = false;
    this.scheduleInteractionActive = false;
    this.scheduleRefreshInFlight = false;
    this.scheduleRefreshAfterFlight = false;
    this.scheduleRefreshAfterFlightFeedback = undefined;
    this.resetTaskListRefreshState();
    this.state.set({
      ...this.loading(),
      status: 'permissionDenied',
      message: 'Project access was denied during reconnect. Protected Project data was cleared.',
      kanban: {
        ...this.loadingKanban(),
        feedback: 'Project access was denied during reconnect. Protected board data was cleared.'
      },
      schedule: {
        ...this.loadingSchedule(),
        feedback: 'Project access was denied during reconnect. Protected schedule data was cleared.'
      }
    });
  }

  private clearProtectedState(reason: ProtectedStateClearReason): void {
    this.clearProtectedProjectionsForDeniedSubscription();
    if (reason !== 'authorization') {
      // A genuine Workspace/session/Tenant boundary invalidates the mounted
      // route intent itself. Authorization rechecks preserve it so either the
      // SignalR catch-up or the degraded HTTP fallback can revalidate it.
      this.release();
    }
  }

  private releaseRealtime(): void {
    for (const cleanup of this.realtimeCleanups.splice(0))
      {cleanup();}
  }

  private initialKanbanRequest(projectId: string) {
    if (!this.flags.kanbanV1Enabled()) {return of<KanbanLoadOutcome>({ kind: 'disabled' });}
    return this.http.get<ProjectKanbanSnapshotDto>(`/api/projects/${projectId}/kanban`, { withCredentials: true }).pipe(
      map((dto): KanbanLoadOutcome => ({ kind: 'success', dto })),
      catchError((error: unknown) => of<KanbanLoadOutcome>({ kind: 'error', error }))
    );
  }

  private initialScheduleRequest(projectId: string) {
    return this.http.get<ProjectGanttSnapshotDto>(
      `/api/projects/${projectId}/gantt`,
      { withCredentials: true }
    ).pipe(
      map((dto): ScheduleLoadOutcome => ({ kind: 'success', dto })),
      catchError((error: unknown) => of<ScheduleLoadOutcome>({ kind: 'error', error }))
    );
  }

  private refreshSchedule(force = false, feedback?: string, allowDuringRevalidation = false): void {
    const projectId = this.projectId;
    if (!projectId || (!this.projectOperational && !allowDuringRevalidation)) {return;}
    if (this.scheduleInteractionActive || this.scheduleCommandInFlight) {
      const current = this.state();
      this.state.set({
        ...current,
        schedule: {
          ...current.schedule,
          reconciliationQueued: true,
          feedback: force
            ? 'Manual refresh is queued until the active schedule edit finishes.'
            : 'A live schedule update is queued until the active edit finishes.'
        }
      });
      return;
    }
    if (this.scheduleRefreshInFlight) {
      this.scheduleRefreshAfterFlight = true;
      this.scheduleRefreshAfterFlightFeedback =
        feedback ?? this.scheduleRefreshAfterFlightFeedback;
      const current = this.state();
      this.state.set({
        ...current,
        schedule: {
          ...current.schedule,
          reconciliationQueued: true,
          feedback: feedback ??
            'An authoritative schedule refresh is already in progress; one follow-up refresh is queued.'
        }
      });
      return;
    }

    const requestGeneration = ++this.scheduleRequestGeneration;
    const authorizationGeneration = this.authorizationGeneration;
    this.scheduleRefreshInFlight = true;
    const current = this.state();
    this.state.set({
      ...current,
      schedule: {
        ...current.schedule,
        status: current.schedule.snapshot ? current.schedule.status : 'loading',
        feedback: feedback ?? current.schedule.feedback,
        reconciliationQueued: false
      }
    });
    this.http.get<ProjectGanttSnapshotDto>(
      `/api/projects/${projectId}/gantt`,
      { withCredentials: true }
    ).pipe(
      map((dto) => ({ kind: 'success' as const, value: mapProjectGanttSnapshot(dto) })),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((result) => {
      if (requestGeneration === this.scheduleRequestGeneration)
        {this.scheduleRefreshInFlight = false;}
      if (this.projectId !== projectId ||
          requestGeneration !== this.scheduleRequestGeneration ||
          authorizationGeneration !== this.authorizationGeneration) {
        this.scheduleFollowUpRefresh(projectId);
        return;
      }

      const latest = this.state();
      if (this.scheduleInteractionActive || this.scheduleCommandInFlight) {
        this.state.set({
          ...latest,
          schedule: {
            ...latest.schedule,
            reconciliationQueued: true,
            feedback: 'Authoritative schedule data arrived during an active edit and will be reconciled after it finishes.'
          }
        });
        return;
      }
      if (result.kind === 'success') {
        this.state.set({
          ...latest,
          schedule: {
            status: scheduleStatus(result.value),
            snapshot: result.value,
            canonicalEnabled: this.flags.ganttV1Enabled(),
            busyItemId: null,
            focusItemId: latest.schedule.focusItemId,
            feedback: feedback ?? latest.schedule.feedback,
            preservedIntent: latest.schedule.preservedIntent,
            realtimeDegraded: this.realtime.connectionState() !== 'Connected',
            reconciliationQueued: false
          }
        });
        this.scheduleFollowUpRefresh(projectId);
        return;
      }
      this.state.set({
        ...latest,
        schedule: this.scheduleFailure(result.error, latest.schedule.snapshot, latest.schedule)
      });
      this.scheduleFollowUpRefresh(projectId);
    });
  }

  private scheduleFollowUpRefresh(projectId: string): void {
    if (!this.scheduleRefreshAfterFlight ||
        this.scheduleRefreshInFlight ||
        this.projectId !== projectId)
      {return;}
    const feedback = this.scheduleRefreshAfterFlightFeedback;
    this.scheduleRefreshAfterFlight = false;
    this.scheduleRefreshAfterFlightFeedback = undefined;
    queueMicrotask(() => this.refreshSchedule(true, feedback));
  }

  private queueTaskListRefresh(): void {
    if (this.taskListRefreshPending || !this.projectId || !this.projectOperational)
      {return;}
    this.taskListRefreshPending = true;
    queueMicrotask(() => {
      this.taskListRefreshPending = false;
      this.refreshTaskList();
    });
  }

  private refreshTaskList(): void {
    const projectId = this.projectId;
    if (!projectId || !this.projectOperational)
      {return;}
    if (this.taskListRefreshInFlight) {
      this.taskListRefreshAfterFlight = true;
      return;
    }

    const requestGeneration = ++this.taskListRequestGeneration;
    const flightGeneration = ++this.taskListRefreshFlightGeneration;
    const loadGeneration = this.loadGeneration;
    const authorizationGeneration = this.authorizationGeneration;
    this.taskListRefreshInFlight = true;
    this.http.get<PagedResponseDto<TaskDto>>(
      `/api/projects/${projectId}/tasks`,
      { withCredentials: true }
    ).pipe(
      map((response) => ({
        kind: 'success' as const,
        rows: (response.items ?? []).map((task) => this.toRow(mapTaskDtoToRecord(task, [])))
      })),
      catchError((error: unknown) => of({ kind: 'error' as const, error }))
    ).subscribe((result) => {
      if (flightGeneration === this.taskListRefreshFlightGeneration)
        {this.taskListRefreshInFlight = false;}
      if (this.projectId !== projectId ||
          loadGeneration !== this.loadGeneration ||
          authorizationGeneration !== this.authorizationGeneration) {
        this.taskListFollowUpRefresh(projectId);
        return;
      }

      if (result.kind === 'success') {
        const current = this.state();
        const applyRows = requestGeneration >= this.taskListAppliedGeneration;
        if (applyRows)
          {this.taskListAppliedGeneration = requestGeneration;}
        const feedbackCleared = this.clearTaskListRefreshFeedback(requestGeneration);
        if (applyRows || feedbackCleared) {
          this.state.set({
            ...current,
            tasks: applyRows ? result.rows : current.tasks,
            project: applyRows
              ? this.withTaskCounts(current.project, result.rows)
              : current.project
          });
        }
        this.taskListFollowUpRefresh(projectId);
        return;
      }

      // A response superseded by another Task-list request cannot establish
      // an authorization decision or replace a newer successful snapshot.
      if (requestGeneration !== this.taskListRequestGeneration ||
          requestGeneration < this.taskListAppliedGeneration) {
        this.taskListFollowUpRefresh(projectId);
        return;
      }

      const error = normalizeApiError(result.error);
      if (error.httpStatus === 401 || error.httpStatus === 403 || error.httpStatus === 404) {
        this.clearProtectedProjectionsForDeniedSubscription();
        return;
      }

      this.taskListRefreshFailureGeneration = requestGeneration;
      this.taskListRefreshFeedback =
        `The Task list could not be synchronized. ${error.message}`;
      this.state.set({ ...this.state() });
      this.taskListFollowUpRefresh(projectId);
    });
  }

  private taskListFollowUpRefresh(projectId: string): void {
    if (!this.taskListRefreshAfterFlight ||
        this.taskListRefreshInFlight ||
        this.projectId !== projectId)
      {return;}
    this.taskListRefreshAfterFlight = false;
    this.queueTaskListRefresh();
  }

  private clearTaskListRefreshFeedback(successGeneration: number): boolean {
    if (!this.taskListRefreshFeedback ||
        successGeneration < this.taskListRefreshFailureGeneration)
      {return false;}
    this.taskListRefreshFailureGeneration = 0;
    this.taskListRefreshFeedback = null;
    return true;
  }

  private resetTaskListRefreshState(): void {
    this.taskListRefreshPending = false;
    this.taskListRefreshInFlight = false;
    this.taskListRefreshAfterFlight = false;
    this.taskListRefreshFlightGeneration++;
    this.taskListAppliedGeneration = 0;
    this.taskListRefreshFailureGeneration = 0;
    this.taskListRefreshFeedback = null;
  }

  private refreshKanban(force = false, feedback?: string, allowDuringRevalidation = false): void {
    const projectId = this.projectId;
    if (!projectId || (!this.projectOperational && !allowDuringRevalidation)) {return;}
    if (!this.flags.kanbanV1Enabled()) {
      const current = this.state();
      this.state.set({ ...current, kanban: this.disabledKanban() });
      return;
    }
    if (!force && (this.interactionActive || this.moveInFlight)) {
      const current = this.state();
      this.state.set({
        ...current,
        kanban: {
          ...current.kanban,
          reconciliationQueued: true,
          feedback: 'A live update is waiting until the current board operation finishes.'
        }
      });
      return;
    }

    const generation = ++this.kanbanRequestGeneration;
    const authorizationGeneration = this.authorizationGeneration;
    const current = this.state();
    this.state.set({
      ...current,
      kanban: {
        ...current.kanban,
        status: current.kanban.snapshot ? current.kanban.status : 'loading',
        feedback: feedback ?? current.kanban.feedback,
        reconciliationQueued: false
      }
    });
    let params = new HttpParams().set('includeOlderCompleted', String(this.includeOlderCompleted));
    if (this.selectedSwimlane) {params = params.set('swimlane', String(swimlaneApiValue(this.selectedSwimlane)));}
    this.http.get<ProjectKanbanSnapshotDto>(`/api/projects/${projectId}/kanban`, { params, withCredentials: true })
      .pipe(
        map((dto) => ({ kind: 'success' as const, value: mapProjectKanbanSnapshot(dto) })),
        catchError((error: unknown) => of({ kind: 'error' as const, error }))
      )
      .subscribe((result) => {
        if (generation !== this.kanbanRequestGeneration ||
            authorizationGeneration !== this.authorizationGeneration)
          {return;}
        const latest = this.state();
        if (result.kind === 'success') {
          this.selectedSwimlane = result.value.selectedSwimlane;
          this.includeOlderCompleted = result.value.includesOlderCompleted;
          this.state.set({
            ...latest,
            kanban: {
              status: result.value.cards.length ? 'ready' : 'empty',
              snapshot: result.value,
              busyTaskId: null,
              focusTaskId: latest.kanban.focusTaskId,
              feedback: feedback ?? latest.kanban.feedback,
              realtimeDegraded: latest.kanban.realtimeDegraded,
              reconciliationQueued: false
            }
          });
        } else {
          this.state.set({ ...latest, kanban: this.kanbanFailure(result.error, latest.kanban.snapshot) });
        }
      });
  }

  private handleRealtimeEvent(event: DurableRealtimeEvent): void {
    if (!this.projectId || ![
      'Projects.ProjectChanged.v1',
      'Projects.TaskChanged.v1',
      'Projects.TaskAssignmentChanged.v1',
      'Projects.TaskWorkflowChanged.v1',
      'Projects.TaskCommentChanged.v1',
      'Security.AuthorizationStateChanged.v1'
    ].includes(event.eventType))
      {return;}
    if (event.eventType !== 'Security.AuthorizationStateChanged.v1' && text(event.payload['projectId']) !== this.projectId)
      {return;}

    if (event.eventType === 'Security.AuthorizationStateChanged.v1') {
      const wasOperational = this.projectOperational;
      this.authorizationGeneration++;
      this.loadGeneration++;
      this.activationGeneration++;
      this.activationInFlight = false;
      this.projectOperational = false;
      this.draftProjectRefreshInFlight = false;
      this.kanbanRequestGeneration++;
      this.scheduleRequestGeneration++;
      this.taskListRequestGeneration++;
      this.scheduleCommandGeneration++;
      this.scheduleCommandInFlight = false;
      this.scheduleInteractionActive = false;
      this.scheduleRefreshInFlight = false;
      this.scheduleRefreshAfterFlight = false;
      this.scheduleRefreshAfterFlightFeedback = undefined;
      this.resetTaskListRefreshState();
      const restartInitialLoad = this.state().status === 'loading';
      const authorizationGeneration = this.authorizationGeneration;
      this.state.set({
        ...this.loading(),
        message: 'Authorization changed. Protected Project data was cleared before revalidation.',
        kanban: {
          ...this.loadingKanban(),
          feedback: 'Authorization changed. Protected board data was cleared before revalidation.'
        },
        schedule: {
          ...this.loadingSchedule(),
          feedback: 'Authorization changed. Protected schedule data was cleared before revalidation.'
        }
      });
      const projectId = this.projectId;
      queueMicrotask(() => {
        if (!projectId ||
            this.projectId !== projectId ||
            this.authorizationGeneration !== authorizationGeneration)
          {return;}
        if (restartInitialLoad) {
          this.load(projectId);
          return;
        }
        this.releaseRealtime();
        this.registerRealtime(projectId);
        this.refreshProjectProjections(projectId, authorizationGeneration, !wasOperational);
        if (wasOperational) {
          this.refreshKanban(true, 'Authorization revalidated from authoritative HTTP state.', true);
          this.refreshSchedule(true, 'Authorization revalidated from authoritative HTTP state.', true);
        }
      });
      return;
    }

    if (!this.projectOperational) {
      if (event.eventType === 'Projects.ProjectChanged.v1')
        {this.refreshDraftProjectAfterChange();}
      return;
    }

    let refreshTaskList = event.eventType === 'Projects.ProjectChanged.v1' ||
      event.eventType === 'Projects.TaskChanged.v1' ||
      event.eventType === 'Projects.TaskAssignmentChanged.v1' ||
      event.eventType === 'Projects.TaskWorkflowChanged.v1';
    let refreshKanban = true;
    let refreshSchedule = true;
    if (event.eventType !== 'Projects.ProjectChanged.v1') {
      const taskId = text(event.payload['taskId']);
      const eventVersion = number(event.payload['taskVersion']);
      const taskRow = this.state().tasks.find((item) => item.id === taskId);
      const taskRowVersion = Number(taskRow?.rowVersion ?? Number.NaN);
      if (taskRow && eventVersion > 0 && Number.isFinite(taskRowVersion) && eventVersion <= taskRowVersion)
        {refreshTaskList = false;}
      const card = this.state().kanban.snapshot?.cards.find((item) => item.taskId === taskId);
      if (card && eventVersion > 0 && eventVersion <= card.version)
        {refreshKanban = false;}
      const item = this.state().schedule.snapshot
        ? scheduleItem(this.state().schedule.snapshot!, taskId)
        : undefined;
      if (item && eventVersion > 0 && eventVersion <= item.version)
        {refreshSchedule = false;}
    } else if (event.eventType === 'Projects.ProjectChanged.v1') {
      const snapshot = this.state().schedule.snapshot;
      const projectVersion = number(event.payload['projectVersion']);
      const workflowVersion = number(event.payload['workflowVersion']);
      if (snapshot &&
          (projectVersion > 0 || workflowVersion > 0) &&
          (projectVersion <= 0 || projectVersion <= snapshot.projectVersion) &&
          (workflowVersion <= 0 || workflowVersion <= snapshot.workflowVersion))
        {refreshSchedule = false;}
    }

    if (refreshKanban && (this.interactionActive || this.moveInFlight))
      {this.refreshKanban(false);}
    else if (refreshKanban && !this.refreshPending) {
      this.refreshPending = true;
      queueMicrotask(() => {
        this.refreshPending = false;
        this.refreshKanban(true);
      });
    }

    if (refreshSchedule && (this.scheduleInteractionActive || this.scheduleCommandInFlight))
      {this.refreshSchedule(false);}
    else if (refreshSchedule && !this.scheduleRefreshPending) {
      this.scheduleRefreshPending = true;
      queueMicrotask(() => {
        this.scheduleRefreshPending = false;
        this.refreshSchedule(true);
      });
    }

    if (refreshTaskList)
      {this.queueTaskListRefresh();}
  }

  private refreshProjectProjections(
    projectId: string,
    authorizationGeneration: number,
    refreshOperationalViews = true,
    onSettled?: () => void
  ): void {
    const loadGeneration = this.loadGeneration;
    const taskListRequestGeneration = ++this.taskListRequestGeneration;
    this.http.get<ProjectDto>(`/api/projects/${projectId}`, { withCredentials: true }).pipe(
      switchMap((project) => {
        if (!this.projectRequestIsCurrent(projectId, loadGeneration, authorizationGeneration))
          {return EMPTY;}
        const record = mapProjectDtoToRecord(project);
        this.projectOperational = record.isOperational === true;
        if (!record.isOperational)
          {return of({ kind: 'draft' as const, project });}
        return forkJoin({
          tasks: this.http.get<PagedResponseDto<TaskDto>>(
            `/api/projects/${projectId}/tasks`,
            { withCredentials: true }
          ),
          workload: this.http.get<unknown>(
            `/api/projects/${projectId}/workload`,
            { withCredentials: true }
          ),
          members: this.http.get<unknown>(
            `/api/projects/${projectId}/members`,
            { withCredentials: true }
          )
        }).pipe(map((response) => ({
          kind: 'success' as const,
          value: this.projectProjections(
            project,
            response.tasks.items ?? [],
            response.workload,
            response.members
          )
        })));
      }),
      catchError((error: unknown) => of({ kind: 'error' as const, error })),
      finalize(() => onSettled?.())
    ).subscribe((result) => {
      if (
        this.projectId !== projectId ||
        this.loadGeneration !== loadGeneration ||
        this.authorizationGeneration !== authorizationGeneration
      ) {return;}

      if (result.kind === 'error') {
        // A denied realtime reauthorization has already cleared every
        // protected projection and established the safe presentation state.
        // The current Project endpoint deliberately returns a uniform safe
        // denial that can be a 400/404 rather than a credential 401/403; do
        // not let that response replace the security state with a generic
        // loading error after the clear.
        if (this.state().status === 'permissionDenied')
          {return;}
        this.state.set(this.failure(result.error));
        return;
      }
      if (result.kind === 'draft') {
        this.projectOperational = false;
        const previousActivation = this.state().activation;
        const fallbackActivation: ProjectActivationViewModel =
          previousActivation.status === 'permissionDenied'
            ? { status: 'idle', message: null }
            : previousActivation;
        this.state.set({
          ...this.draftReady(result.project),
          activation: this.activationForAuthorizedProjection(
            projectId,
            false,
            fallbackActivation
          )
        });
        return;
      }
      const current = this.state();
      this.projectOperational = true;
      const applyTaskList = taskListRequestGeneration >= this.taskListAppliedGeneration;
      if (applyTaskList)
        {this.taskListAppliedGeneration = taskListRequestGeneration;}
      this.clearTaskListRefreshFeedback(taskListRequestGeneration);
      this.state.set({
        ...current,
        ...result.value,
        tasks: applyTaskList ? result.value.tasks : current.tasks,
        project: applyTaskList
          ? result.value.project
          : this.withTaskCounts(result.value.project, current.tasks),
        activation: this.activationForAuthorizedProjection(
          projectId,
          true,
          current.activation
        ),
        status: 'ready',
        message: undefined
      });
      this.taskListFollowUpRefresh(projectId);
      if (refreshOperationalViews) {
        this.refreshKanban(false, 'Project board synchronized from authoritative HTTP state.');
        this.refreshSchedule(false, 'Schedule synchronized from authoritative HTTP state.');
      }
    });
  }

  private refreshDraftProjectAfterChange(): void {
    const projectId = this.projectId;
    if (!projectId || this.draftProjectRefreshInFlight)
      {return;}

    const loadGeneration = this.loadGeneration;
    const authorizationGeneration = this.authorizationGeneration;
    this.draftProjectRefreshInFlight = true;
    this.refreshProjectProjections(projectId, authorizationGeneration, true, () => {
      if (this.projectRequestIsCurrent(projectId, loadGeneration, authorizationGeneration))
        {this.draftProjectRefreshInFlight = false;}
    });
  }

  private activationForAuthorizedProjection(
    projectId: string,
    operational: boolean,
    fallback: ProjectActivationViewModel
  ): ProjectActivationViewModel {
    const notice = this.activationNotice;
    if (notice?.projectId !== projectId)
      {return fallback;}

    if (operational) {
      const requestId = notice.requestId ?? fallback.requestId;
      this.activationNotice = {
        projectId,
        generation: notice.generation,
        outcome: 'accepted',
        requestId
      };
      return {
        status: 'success',
        message: 'Project activated. Operational views were loaded from authoritative state.',
        requestId
      };
    }

    if (notice.outcome === 'accepted') {
      return {
        status: 'success',
        message: 'Activation was accepted, but the authoritative Project is still Draft. Reload before taking further action.',
        requestId: notice.requestId
      };
    }

    if (notice.outcome === 'pending') {
      return {
        status: 'reconciling',
        message: 'Activation is still being confirmed after authorization changed. Another activation attempt is disabled.'
      };
    }

    const result: ProjectActivationViewModel = {
      status: notice.outcome,
      message: notice.outcome === 'conflict'
        ? 'The Project changed before activation. The latest Draft was reloaded; review it before retrying.'
        : 'Activation was not confirmed. The latest Draft was reloaded and can be retried.',
      requestId: notice.requestId ?? fallback.requestId
    };
    this.activationNotice = null;
    return result;
  }

  private ready(projectDto: ProjectDto, taskDtos: readonly TaskDto[], kanban: KanbanLoadOutcome, gantt: ScheduleLoadOutcome, workload: unknown, members: unknown): ProjectDetailViewModel {
    return {
      status: 'ready',
      ...this.projectProjections(projectDto, taskDtos, workload, members),
      taskListFeedback: null,
      kanban: this.mapInitialKanban(kanban),
      schedule: this.mapInitialSchedule(gantt),
      activation: { status: 'idle', message: null }
    };
  }

  private projectProjections(
    projectDto: ProjectDto,
    taskDtos: readonly TaskDto[],
    workload: unknown,
    members: unknown
  ): Pick<ProjectDetailViewModel, 'project' | 'tasks' | 'workload' | 'members'> {
    const record = mapProjectDtoToRecord(projectDto);
    const taskRecords = taskDtos.map((task) => mapTaskDtoToRecord(task, [record]));
    const project = this.projectSummary(record, taskRecords);
    const rows = taskRecords.map((task) => this.toRow(task));
    return {
      project,
      tasks: rows,
      workload: this.workload(workload),
      members: this.members(members)
    };
  }

  private projectSummary(
    record: ReturnType<typeof mapProjectDtoToRecord>,
    taskRecords: readonly TaskMockRecord[]
  ): ProjectSummaryViewModel {
    return {
      id: record.id,
      workspaceId: record.workspaceId ?? null,
      groupId: record.groupId ?? null,
      ownerUserId: record.ownerUserId ?? null,
      name: record.name,
      description: record.description ?? '',
      status: record.status,
      statusLabel: record.statusLabel,
      visibility: record.visibility ?? 'unknown',
      visibilityLabel: record.visibilityLabel ?? 'Visibility unavailable',
      activationState: record.activationState ?? 'legacyUnknown',
      versionNo: record.versionNo ?? 0,
      isOperational: record.isOperational === true,
      startDate: record.startDate,
      dueDate: record.dueDate,
      group: record.group,
      canCreateTask: record.canCreateTask,
      canActivate: record.canActivate === true,
      taskCounts: {
        total: taskRecords.length,
        done: taskRecords.filter((task) => task.stageCategory === 'done' || task.status === 'done').length,
        blocked: taskRecords.filter((task) => task.isBlocked === true || task.status === 'blocked').length
      }
    };
  }

  private draftReady(projectDto: ProjectDto): ProjectDetailViewModel {
    const project = this.projectSummary(mapProjectDtoToRecord(projectDto), []);
    return {
      status: 'ready',
      project,
      tasks: [],
      taskListFeedback: null,
      kanban: {
        ...this.disabledKanban(),
        feedback: 'Operational views remain unavailable while this Project is Draft.'
      },
      schedule: {
        ...this.loadingSchedule(),
        status: 'empty',
        feedback: 'Operational views remain unavailable while this Project is Draft.'
      },
      workload: [],
      members: [],
      activation: { status: 'idle', message: null }
    };
  }

  private projectOnlyReady(projectDto: ProjectDto): ProjectDetailViewModel {
    const project = this.projectSummary(mapProjectDtoToRecord(projectDto), []);
    const error = new Error('Operational Project views could not be loaded.');
    return {
      status: 'ready',
      project,
      tasks: [],
      taskListFeedback: 'The Task list could not be synchronized. Refresh before editing.',
      kanban: this.kanbanFailure(error, null),
      schedule: this.scheduleFailure(error, null),
      workload: [],
      members: [],
      activation: { status: 'idle', message: null }
    };
  }

  private mapInitialKanban(outcome: KanbanLoadOutcome): ProjectKanbanViewModel {
    if (outcome.kind === 'disabled') {return this.disabledKanban();}
    if (outcome.kind === 'error') {return this.kanbanFailure(outcome.error, null);}
    try {
      const snapshot = mapProjectKanbanSnapshot(outcome.dto);
      this.selectedSwimlane = snapshot.selectedSwimlane;
      this.includeOlderCompleted = snapshot.includesOlderCompleted;
      return {
        status: snapshot.cards.length ? 'ready' : 'empty',
        snapshot,
        busyTaskId: null,
        focusTaskId: null,
        feedback: null,
        realtimeDegraded: this.realtime.connectionState() !== 'Connected',
        reconciliationQueued: false
      };
    } catch (error: unknown) {
      return this.kanbanFailure(error, null);
    }
  }

  private kanbanFailure(error: unknown, snapshot: ProjectKanbanSnapshot | null): ProjectKanbanViewModel {
    const normalized = normalizeApiError(error);
    const status: ProjectKanbanStatus =
      normalized.httpStatus === 401 || normalized.httpStatus === 403 ? 'permissionDenied' :
      normalized.httpStatus === 404 ? 'notFound' :
      normalized.httpStatus === 409 ? 'conflict' : 'error';
    return {
      status,
      snapshot: status === 'permissionDenied' || status === 'notFound' ? null : snapshot,
      busyTaskId: null,
      focusTaskId: null,
      feedback: normalized.message,
      error: normalized,
      realtimeDegraded: this.realtime.connectionState() !== 'Connected',
      reconciliationQueued: false
    };
  }

  private mapInitialSchedule(outcome: ScheduleLoadOutcome): ProjectScheduleViewModel {
    if (outcome.kind === 'error')
      {return this.scheduleFailure(outcome.error, null);}
    try {
      const snapshot = mapProjectGanttSnapshot(outcome.dto);
      return {
        status: scheduleStatus(snapshot),
        snapshot,
        canonicalEnabled: this.flags.ganttV1Enabled(),
        busyItemId: null,
        focusItemId: null,
        feedback: this.flags.ganttV1Enabled()
          ? null
          : 'Canonical Gantt presentation is disabled. The maintained read-only schedule list uses the same HTTP snapshot.',
        preservedIntent: null,
        realtimeDegraded: this.realtime.connectionState() !== 'Connected',
        reconciliationQueued: false
      };
    } catch (error: unknown) {
      return this.scheduleFailure(error, null);
    }
  }

  private scheduleFailure(
    value: unknown,
    snapshot: ProjectGanttSnapshot | null,
    previous?: ProjectScheduleViewModel
  ): ProjectScheduleViewModel {
    const error = normalizeApiError(value);
    const permissionDenied = error.httpStatus === 401 || error.httpStatus === 403;
    const safeSnapshot = permissionDenied || error.httpStatus === 404 ? null : snapshot;
    return {
      status: permissionDenied ? 'permissionDenied' : 'error',
      snapshot: safeSnapshot,
      canonicalEnabled: this.flags.ganttV1Enabled(),
      busyItemId: null,
      focusItemId: previous?.focusItemId ?? null,
      feedback: error.message,
      preservedIntent: permissionDenied ? null : previous?.preservedIntent ?? null,
      realtimeDegraded: this.realtime.connectionState() !== 'Connected',
      reconciliationQueued: false,
      error
    };
  }

  private schedulePresentationStatus(schedule: ProjectScheduleViewModel): ProjectScheduleStatus {
    if (
      this.realtime.connectionState() !== 'Connected' &&
      schedule.snapshot &&
      (schedule.status === 'ready' || schedule.status === 'empty' || schedule.status === 'degraded')
    ) {return 'degraded';}
    if (schedule.status === 'degraded' && this.realtime.connectionState() === 'Connected')
      {return schedule.snapshot ? scheduleStatus(schedule.snapshot) : 'error';}
    return schedule.status;
  }

  private optimisticMove(snapshot: ProjectKanbanSnapshot, card: ProjectKanbanCard, targetStageId: string, beforeId: string | null, afterId: string | null): ProjectKanbanSnapshot {
    const without = snapshot.cards.filter((item) => item.taskId !== card.taskId);
    const target = without.filter((item) => item.workflowStageId === targetStageId)
      .sort((left, right) => left.boardOrder - right.boardOrder || left.taskId.localeCompare(right.taskId));
    let index = target.length;
    if (beforeId) {
      const found = target.findIndex((item) => item.taskId === beforeId);
      if (found >= 0) {index = found;}
    } else if (afterId) {
      const found = target.findIndex((item) => item.taskId === afterId);
      if (found >= 0) {index = found + 1;}
    }
    target.splice(index, 0, { ...card, workflowStageId: targetStageId });
    const reordered = new Map(target.map((item, order) => [item.taskId, { ...item, boardOrder: (order + 1) * 1000 }]));
    return { ...snapshot, cards: without.map((item) => reordered.get(item.taskId) ?? item).concat(reordered.has(card.taskId) ? [reordered.get(card.taskId)!] : []) };
  }

  private withTaskCounts(
    project: ProjectSummaryViewModel | undefined,
    tasks: readonly TaskGridRow[]
  ): ProjectSummaryViewModel | undefined {
    if (!project)
      {return undefined;}
    return {
      ...project,
      taskCounts: {
        total: tasks.length,
        done: tasks.filter((task) => task.stageCategory === 'done' || task.status === 'done').length,
        blocked: tasks.filter((task) => task.isBlocked === true || task.status === 'blocked').length
      }
    };
  }

  private toRow(task: TaskMockRecord): TaskGridRow {
    return {
      id: task.id,
      projectId: task.projectId,
      title: task.title,
      project: task.milestone || 'Project',
      status: task.status,
      statusLabel: task.statusLabel,
      workflowStageId: task.workflowStageId,
      workflowStageName: task.workflowStageName,
      stageCategory: task.stageCategory,
      isBlocked: task.isBlocked,
      createdAt: task.createdAt,
      updatedAt: task.updatedAt,
      hasArtifact: task.hasArtifact,
      rowVersion: task.rowVersion,
      priority: task.priority,
      priorityLabel: task.priorityLabel,
      assignee: task.assignee,
      startDate: task.startDate,
      dueDate: task.dueDate,
      progressPercent: task.progressPercent,
      milestone: task.milestone,
      allowedTransitions: task.allowedTransitions,
      rowActions: [{ id: 'openDetail', label: 'Open', disabled: false }]
    };
  }
  private workload(value: unknown): readonly ProjectWorkloadViewModel[] { return array(object(value)['members']).map((item) => { const row = object(item); return { userId: text(row['userId']), displayName: text(row['displayName'], 'Member'), projectRole: text(row['projectRole'], 'Member'), assignedTaskCount: number(row['assignedTaskCount']), overdueTaskCount: number(row['overdueTaskCount']), estimatedHours: number(row['estimatedHours']), actualHours: number(row['actualHours']) }; }); }
  private members(value: unknown): readonly ProjectMemberViewModel[] { return array(value).map((item) => { const row = object(item); return { userId: text(row['userId']), displayName: text(row['displayName'], 'Member'), role: text(row['role'], 'Member') }; }); }
  private loading(): ProjectDetailViewModel { return { status: 'loading', tasks: [], taskListFeedback: null, kanban: this.loadingKanban(), schedule: this.loadingSchedule(), workload: [], members: [], activation: { status: 'idle', message: null } }; }
  private loadingKanban(): ProjectKanbanViewModel { return { status: 'loading', snapshot: null, busyTaskId: null, focusTaskId: null, feedback: null, realtimeDegraded: this.realtime.connectionState() !== 'Connected', reconciliationQueued: false }; }
  private loadingSchedule(): ProjectScheduleViewModel { return { status: 'loading', snapshot: null, canonicalEnabled: this.flags.ganttV1Enabled(), busyItemId: null, focusItemId: null, feedback: null, preservedIntent: null, realtimeDegraded: this.realtime.connectionState() !== 'Connected', reconciliationQueued: false }; }
  private disabledKanban(): ProjectKanbanViewModel { return { ...this.loadingKanban(), status: 'disabled', feedback: 'Project Kanban is disabled. The maintained Task List remains available.' }; }
  private failure(error: unknown): ProjectDetailViewModel { const normalized = normalizeApiError(error); return { ...this.loading(), status: normalized.httpStatus === 401 || normalized.httpStatus === 403 ? 'permissionDenied' : 'error', message: normalized.message }; }
}

function object(value: unknown): Record<string, unknown> { return value !== null && typeof value === 'object' ? value as Record<string, unknown> : {}; }
function array(value: unknown): readonly unknown[] { return Array.isArray(value) ? value : []; }
function text(value: unknown, fallback = ''): string { return typeof value === 'string' && value.length > 0 ? value : fallback; }
function number(value: unknown): number { return typeof value === 'number' && Number.isFinite(value) ? value : 0; }

function scheduleStatus(snapshot: ProjectGanttSnapshot): 'ready' | 'empty' {
  return snapshot.scheduledItems.length + snapshot.unscheduledItems.length + snapshot.milestones.length > 0
    ? 'ready'
    : 'empty';
}

function scheduleItems(snapshot: ProjectGanttSnapshot): readonly CoglatasGanttItem[] {
  return [...snapshot.scheduledItems, ...snapshot.unscheduledItems, ...snapshot.milestones];
}

function scheduleItem(snapshot: ProjectGanttSnapshot, taskId: string): CoglatasGanttItem | undefined {
  return scheduleItems(snapshot).find((item) => item.taskId === taskId);
}

function canEditSchedule(
  snapshot: ProjectGanttSnapshot,
  item: CoglatasGanttItem,
  intent: Extract<CoglatasGanttEditIntent, { readonly kind: 'schedule' }>
): boolean {
  if (
    item.progressIsDerived ||
    !snapshot.permissions.canEditSchedule ||
    !item.scheduleEditPermissions.canEditSchedule
  ) {return false;}

  if (
    (intent.plannedStartDate !== null && !validDateOnly(intent.plannedStartDate)) ||
    (intent.plannedEndDate !== null && !validDateOnly(intent.plannedEndDate)) ||
    (intent.milestoneDate !== null && !validDateOnly(intent.milestoneDate)) ||
    (intent.plannedStartDate !== null &&
      intent.plannedEndDate !== null &&
      intent.plannedEndDate < intent.plannedStartDate)
  ) {return false;}

  if (item.kind === 'milestone')
    {return intent.plannedStartDate === null &&
      intent.plannedEndDate === null &&
      intent.milestoneDate !== null;}
  if (intent.milestoneDate !== null)
    {return false;}

  const clearing = intent.plannedStartDate === null && intent.plannedEndDate === null;
  return !clearing ||
    (snapshot.permissions.canClearSchedule && item.scheduleEditPermissions.canClearSchedule);
}

function canEditProgress(snapshot: ProjectGanttSnapshot, item: CoglatasGanttItem, progress: number): boolean {
  if (
    item.progressIsDerived ||
    !snapshot.permissions.canEditProgress ||
    !item.scheduleEditPermissions.canEditProgress ||
    !Number.isInteger(progress) ||
    progress < 0 ||
    progress > 100
  ) {return false;}
  if (item.kind === 'milestone' && progress !== 0 && progress !== 100)
    {return false;}
  return item.stageCategory !== 'done' || progress === 100;
}

function canManageDependencies(snapshot: ProjectGanttSnapshot, item: CoglatasGanttItem): boolean {
  return snapshot.permissions.canManageDependencies &&
    item.scheduleEditPermissions.canManageDependencies;
}

function updateScheduleItem(
  snapshot: ProjectGanttSnapshot,
  previous: CoglatasGanttItem,
  updated: CoglatasGanttItem
): ProjectGanttSnapshot {
  const normalized = withUnscheduledWarning(updated);
  const remove = (items: readonly CoglatasGanttItem[]) =>
    items.filter((item) => item.taskId !== previous.taskId);
  if (normalized.kind === 'milestone') {
    return {
      ...snapshot,
      milestones: replaceItem(snapshot.milestones, previous.taskId, normalized)
    };
  }

  const isUnscheduled = normalized.plannedStartDate === null && normalized.plannedEndDate === null;
  return {
    ...snapshot,
    scheduledItems: isUnscheduled
      ? remove(snapshot.scheduledItems)
      : replaceItem(snapshot.scheduledItems, previous.taskId, normalized),
    unscheduledItems: isUnscheduled
      ? replaceItem(snapshot.unscheduledItems, previous.taskId, normalized)
      : remove(snapshot.unscheduledItems)
  };
}

function reconcileTaskCommandWarnings(
  snapshot: ProjectGanttSnapshot,
  changedItemId: string,
  authoritativeWarnings: readonly CoglatasGanttWarning[]
): ProjectGanttSnapshot {
  const connectedDependencyIds = new Set(
    snapshot.dependencies
      .filter((dependency) =>
        dependency.predecessorTaskId === changedItemId ||
        dependency.successorTaskId === changedItemId)
      .map((dependency) => dependency.dependencyId)
  );
  const warningsByDependencyId = new Map<string, CoglatasGanttWarning[]>();
  for (const warning of authoritativeWarnings) {
    if (warning.targetType.toLowerCase() !== 'dependency' ||
        warning.targetId === null ||
        !connectedDependencyIds.has(warning.targetId))
      {continue;}
    const warnings = warningsByDependencyId.get(warning.targetId) ?? [];
    warnings.push(warning);
    warningsByDependencyId.set(warning.targetId, warnings);
  }

  const dependencies = snapshot.dependencies.map((dependency) =>
    connectedDependencyIds.has(dependency.dependencyId)
      ? {
          ...dependency,
          warnings: warningsByDependencyId.get(dependency.dependencyId) ?? []
        }
      : dependency
  );
  const affectedSuccessorIds = new Set(
    dependencies
      .filter((dependency) => connectedDependencyIds.has(dependency.dependencyId))
      .map((dependency) => dependency.successorTaskId)
  );
  const directItemWarnings = authoritativeWarnings.filter((warning) =>
    warning.targetType.toLowerCase() !== 'dependency' &&
    warning.targetId === changedItemId
  );

  const reconcileItem = (item: CoglatasGanttItem): CoglatasGanttItem => {
    if (item.taskId === changedItemId) {
      const warnings = [...directItemWarnings];
      if (item.kind === 'task' && hasDependencyViolation(dependencies, item.taskId)) {
        warnings.push(dependencyViolationItemWarning(item.taskId));
      }
      return withUnscheduledWarning({ ...item, warnings: uniqueGanttWarnings(warnings) });
    }
    if (!affectedSuccessorIds.has(item.taskId))
      {return item;}
    const warnings = item.warnings.filter((warning) => warning.code !== 'DEPENDENCY_VIOLATION');
    if (hasDependencyViolation(dependencies, item.taskId))
      {warnings.push(dependencyViolationItemWarning(item.taskId));}
    return { ...item, warnings: uniqueGanttWarnings(warnings) };
  };

  const scheduledItems = snapshot.scheduledItems.map(reconcileItem);
  const unscheduledItems = snapshot.unscheduledItems.map(reconcileItem);
  const milestones = snapshot.milestones.map(reconcileItem);
  return {
    ...snapshot,
    scheduledItems,
    unscheduledItems,
    milestones,
    dependencies,
    warnings: uniqueGanttWarnings([
      ...scheduledItems.flatMap((item) => item.warnings),
      ...unscheduledItems.flatMap((item) => item.warnings),
      ...milestones.flatMap((item) => item.warnings),
      ...dependencies.flatMap((dependency) => dependency.warnings)
    ])
  };
}

function hasDependencyViolation(
  dependencies: readonly CoglatasGanttDependency[],
  successorTaskId: string
): boolean {
  return dependencies.some((dependency) =>
    dependency.successorTaskId === successorTaskId &&
    dependency.warnings.some((warning) => warning.code === 'DEPENDENCY_VIOLATION')
  );
}

function dependencyViolationItemWarning(taskId: string): CoglatasGanttWarning {
  return {
    code: 'DEPENDENCY_VIOLATION',
    message: 'A predecessor is planned to finish after this Task starts. No dates were changed automatically.',
    severity: 'warning',
    targetType: 'Task',
    targetId: taskId,
    field: 'plannedStartDate',
    blocking: false
  };
}

function uniqueGanttWarnings(warnings: readonly CoglatasGanttWarning[]): readonly CoglatasGanttWarning[] {
  const unique = new Map<string, CoglatasGanttWarning>();
  for (const warning of warnings) {
    unique.set(
      `${warning.code}:${warning.targetType}:${warning.targetId ?? ''}:${warning.field ?? ''}`,
      warning
    );
  }
  return [...unique.values()];
}

function replaceItem(
  items: readonly CoglatasGanttItem[],
  taskId: string,
  updated: CoglatasGanttItem
): readonly CoglatasGanttItem[] {
  const index = items.findIndex((item) => item.taskId === taskId);
  if (index < 0)
    {return [...items, updated];}
  return items.map((item) => item.taskId === taskId ? updated : item);
}

function withUnscheduledWarning(item: CoglatasGanttItem): CoglatasGanttItem {
  if (item.kind !== 'task')
    {return item;}
  const unscheduled = item.plannedStartDate === null && item.plannedEndDate === null;
  const warnings = item.warnings.filter((warning) => warning.code !== 'UNSCHEDULED');
  return {
    ...item,
    warnings: unscheduled
      ? [...warnings, {
          code: 'UNSCHEDULED',
          message: 'This Task has no planned dates and is listed as unscheduled.',
          severity: 'info',
          targetType: 'Task',
          targetId: item.taskId,
          field: 'plannedStartDate',
          blocking: false
        }]
      : warnings
  };
}

function preserveGanttIntent(intent: CoglatasGanttEditIntent): CoglatasGanttEditIntent {
  if (intent.kind === 'schedule') {
    return {
      kind: 'schedule',
      taskId: intent.taskId,
      plannedStartDate: intent.plannedStartDate,
      plannedEndDate: intent.plannedEndDate,
      milestoneDate: intent.milestoneDate,
      expectedVersion: intent.expectedVersion,
      source: intent.source
    };
  }
  if (intent.kind === 'progress') {
    return {
      kind: 'progress',
      taskId: intent.taskId,
      progressPercent: intent.progressPercent,
      expectedVersion: intent.expectedVersion,
      source: intent.source
    };
  }
  if (intent.kind === 'addDependency') {
    return {
      kind: 'addDependency',
      predecessorTaskId: intent.predecessorTaskId,
      successorTaskId: intent.successorTaskId,
      type: 'finishToStart',
      expectedVersion: intent.expectedVersion,
      source: intent.source
    };
  }
  return {
    kind: 'removeDependency',
    dependencyId: intent.dependencyId,
    successorTaskId: intent.successorTaskId,
    expectedVersion: intent.expectedVersion,
    source: intent.source
  };
}

function validDateOnly(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(value);
  if (!match) {return false;}
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1) {return false;}
  const monthLengths = [31, leapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day <= monthLengths[month - 1];
}

function leapYear(year: number): boolean {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
}
