import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { effect, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { Router } from '@angular/router';
import { forkJoin, Observable, of, Subscription, throwError } from 'rxjs';
import { catchError, finalize, map, switchMap } from 'rxjs/operators';

import { normalizeApiError } from '../../core/api/api-error.adapter';
import { FrontendApiError } from '../../core/api/api-error.model';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { MyTasksFacade } from './my-tasks.facade';
import {
  ProtectedStateClearReason,
  RealtimeFacade,
} from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { CanonicalTaskDetailDto, PagedResponseDto, ProjectDto, TaskDto, TaskLabelDto, toCreateTaskRequestDto, toUpdateTaskRequestDto } from './projects.api';
import {
  mapProjectDtoToRecord,
  mapTaskDtoToRecord,
  taskStatusLabel
} from './projects.mapper';
import {
  CreateTaskFormRequest,
  PROJECTS_DEFAULT_PAGE_SIZE,
  PROJECTS_MAXIMUM_PAGE_SIZE,
  ProjectMockRecord,
  ProjectSummaryViewModel,
  ProjectsOverviewViewModel,
  ProjectsPageStatus,
  ProjectsScenario,
  MyTasksViewModel,
  TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE,
  TaskDetailAggregateViewModel,
  TaskDetailViewModel,
  TASK_LABEL_DESCRIPTION_MAX_LENGTH,
  TASK_LABEL_NAME_MAX_LENGTH,
  TaskEditorSaveRequest,
  TaskGridRow,
  TaskDetailSection,
  TaskDetailSectionState,
  TaskConflictReloadState,
  TaskMockRecord,
  TaskMutationState,
  TaskRowAction
} from './projects.types';

export const COGLATAS_PROJECTS_MOCK = new InjectionToken<ProjectsScenario>('COGLATAS_PROJECTS_MOCK');

interface ProjectsLoadResult {
  readonly projects: readonly ProjectMockRecord[];
  readonly tasks: readonly TaskMockRecord[];
}

interface TaskDetailLoadResult {
  readonly task: TaskMockRecord;
  readonly detail: CanonicalTaskDetailDto;
  readonly parentProject?: ProjectMockRecord;
  readonly scopeMismatch?: boolean;
  readonly workspacePending?: boolean;
}

interface ErrorBody {
  readonly message?: unknown;
  readonly error?: unknown;
  readonly traceId?: unknown;
  readonly requestId?: unknown;
}

@Injectable({
  providedIn: 'root'
})
export class ProjectsFacade {
  private readonly http = inject(HttpClient);
  private readonly myTasksFacade = inject(MyTasksFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly continueWorkingHistory = inject(ContinueWorkingHistoryService);
  private readonly router = inject(Router, { optional: true });
  private readonly scenario = inject(COGLATAS_PROJECTS_MOCK, { optional: true });
  private readonly liveState = signal<ProjectsScenario>(
    this.scenario ?? this.emptyScenario('loading')
  );
  private readonly taskMutationState = signal<TaskMutationState>({ status: 'idle' });
  private readonly taskConflictReloadState = signal<TaskConflictReloadState>('idle');
  private readonly taskCreateMutationState = signal<TaskMutationState>({ status: 'idle' });
  private readonly sectionStates = signal<Record<TaskDetailSection, TaskDetailSectionState>>(this.emptySectionStates());
  /** A single generation invalidates every protected response on navigation or authorization change. */
  /** Changes when a permission boundary changes. Never accept a prior authorization response. */
  private authorizationGeneration = 0;
  private detailGeneration = 0;
  private projectsRequest: Subscription | null = null;
  private detailRequest: Subscription | null = null;
  private readonly pageRequests = new Map<TaskDetailSection, Subscription>();
  private labelRequest: Subscription | null = null;
  private taskConflictReloadInProgress = false;
  private readonly detailMutations = new Set<Subscription>();
  private readonly taskDetails = signal<Record<string, CanonicalTaskDetailDto>>({});
  private readonly projectLabelDefinitions = signal<Record<string, readonly TaskLabelDto[]>>({});
  private readonly labelDefinitionStates = signal<Record<string, TaskDetailSectionState>>({});
  private activeTaskId: string | null = null;
  private activeProjectId: string | null = null;
  private activeProjectSubscription: (() => void) | null = null;
  private activeProjectCatchUpCleanup: (() => void) | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private observedWorkspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;

  constructor() {
    this.realtime.durableEvents$.subscribe((event) => this.handleRealtimeEvent(event));
    this.realtime.registerProtectedStateClearer?.(
      'projects-active-task',
      (reason) => this.clearProtectedState(reason),
    );
    // The Task route fetches one protected aggregate itself. Starting the broad
    // overview inventory during that route can race a post-revocation safe 404
    // and probe stale project/File resources.
    if (!this.scenario && this.activeWorkspace.activeWorkspace()?.id && !this.router?.url.includes('/tasks/')) {
      this.loadProjects();
    }
    effect(() => {
      const workspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
      const previousWorkspaceId = this.observedWorkspaceId;
      if (workspaceId === previousWorkspaceId) {
        return;
      }

      this.observedWorkspaceId = workspaceId;
      if (!this.scenario && workspaceId) {
        // Initial shell selection may race the constructor's already-active
        // request. Actual Workspace changes and authorization restoration have
        // no surviving request and must rehydrate the mounted Project route.
        if (previousWorkspaceId !== null || !this.projectsRequest) {
          if (this.activeTaskId && this.activeProjectId) {
            this.reauthorizeActiveTaskDetail();
          } else if (!this.router?.url.includes('/tasks/')) {
            this.loadProjects();
          }
        }
      }
    });
  }

  getProjectsOverview(): ProjectsOverviewViewModel {
    const scenario = this.liveState();
    if (scenario.status === 'permissionDenied') {
      return this.emptyOverview(scenario, 'permissionDenied', scenario.message);
    }

    const projects = this.authorizedProjects().map((project) => this.toProjectSummary(project));
    const rows = this.authorizedTasks().map((task) => this.toTaskRow(task));

    return {
      status: scenario.status,
      title: scenario.title,
      subtitle: scenario.subtitle,
      projects,
      rows,
      columns: [],
      pageSize: this.pageSize,
      message: scenario.message,
      error: scenario.error
    };
  }

  getMyTasks(): MyTasksViewModel {
    const scenario = this.liveState();
    if (scenario.status === 'permissionDenied') {
      return {
        status: 'permissionDenied',
        title: 'My tasks',
        subtitle: 'Tasks assigned to the signed-in user',
        rows: [],
        columns: [],
        pageSize: this.pageSize,
        message: scenario.message,
        tasks: [], selectedTab: 'assigned', scope: 'currentWorkspace', workspaceId: null, workspaceOptions: [], counts: [], totalCount: 0,
        page: 1, selectedPageSize: PROJECTS_DEFAULT_PAGE_SIZE, lastPage: 1,
        filters: { projectId: '', stageCategory: '', priority: '', blocked: '', search: '', timeGroup: null },
        projectFilterInputValue: '', savedFilters: [], savedFiltersAvailable: false, canPersistSavedFilters: false,
        filterConditions: [{ id: 'relationship', label: 'Relationship: Assigned to Me' }], filterAnnouncement: '',
        realtimeDegraded: false
      };
    }

    const myTasksStatus = scenario.myTasksStatus ?? scenario.status;
    return {
      status: myTasksStatus,
      title: 'My tasks',
      subtitle: 'Tasks assigned to the signed-in user',
      rows: myTasksStatus === 'ready' ? this.authorizedMyTasks().map((task) => this.toTaskRow(task)) : [],
        columns: [],
        pageSize: this.pageSize,
        message: scenario.myTasksMessage ?? scenario.message,
        tasks: [], selectedTab: 'assigned', scope: 'currentWorkspace', workspaceId: null, workspaceOptions: [], counts: [], totalCount: 0,
        page: 1, selectedPageSize: PROJECTS_DEFAULT_PAGE_SIZE, lastPage: 1,
        filters: { projectId: '', stageCategory: '', priority: '', blocked: '', search: '', timeGroup: null },
        projectFilterInputValue: '', savedFilters: [], savedFiltersAvailable: false, canPersistSavedFilters: false,
        filterConditions: [{ id: 'relationship', label: 'Relationship: Assigned to Me' }], filterAnnouncement: '',
        realtimeDegraded: false
    };
  }

  getTaskDetail(projectId?: string, taskId?: string): TaskDetailViewModel {
    const scenario = this.liveState();
    const aggregateState = this.getDetailSectionState('detail');
    if (scenario.status === 'permissionDenied') {
      return {
        status: 'permissionDenied',
        detailState: 'ready',
        detailSectionState: aggregateState,
        dependencies: [],
        capabilities: [],
        transitionNote: TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE,
        message: scenario.message
      };
    }

    const detail = taskId ? this.taskDetails()[taskId] : undefined;
    const aggregateTask = detail?.task
      ? mapTaskDtoToRecord(detail.task as TaskDto, this.authorizedProjects())
      : undefined;
    const listedTask = scenario.tasks.find(
      (candidate) => candidate.authorized && candidate.projectId === projectId && candidate.id === taskId
    );
    // Canonical detail is the editor authority. Project/list refreshes use a
    // deliberately compact Task projection and may omit detail-only fields
    // such as Brief; preferring that row would silently turn omission into a
    // clear on the next unrelated save.
    const task = aggregateTask?.projectId === projectId ? aggregateTask : listedTask;
    const project = task
      ? this.authorizedProjects().find((candidate) => candidate.id === task.projectId)
      : undefined;

    const loadedProjectId = typeof detail?.task?.projectId === 'string' ? detail.task.projectId : undefined;
    if (detail && loadedProjectId && loadedProjectId !== projectId) {
      return { status: 'empty', detailState: 'ready', detailSectionState: aggregateState, dependencies: [], capabilities: [], transitionNote: TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE, message: 'TASK_DETAIL_PROJECT_MISMATCH' };
    }
    return {
      status: task ? 'ready' : scenario.status === 'ready' ? 'empty' : scenario.status,
      detailState: scenario.detailState ?? 'ready',
      detailSectionState: aggregateState,
      project: project ? this.toProjectSummary(project) : undefined,
      task: task ? this.toTaskRow(task) : undefined,
      editorTask: task,
      dependencies: task ? this.toDependencies(task) : [],
      capabilities: task?.capabilities ?? [],
      transitionNote: TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE,
      detail: detail && task ? this.mapDetail(detail) : undefined,
      message: aggregateState.message ?? scenario.message
    };
  }

  ensureTaskDetail(projectId?: string, taskId?: string): void {
    if (!projectId || !taskId) {
      return;
    }

    if (this.activeTaskId !== taskId || this.activeProjectId !== projectId) {this.clearProtectedTaskState();}
    this.activeTaskId = taskId;
    this.activeProjectId = projectId;
    this.activeProjectSubscription?.();
    this.activeProjectCatchUpCleanup?.();
    this.activeProjectSubscription = this.realtime.registerSubscription('projects-active-task', { subscriptionType: 'project', resourceId: projectId });
    this.activeProjectCatchUpCleanup = this.realtime.registerCatchUp('projects-active-task', () => {
      if (this.activeTaskId && this.activeProjectId && !this.scenario) {
        this.reauthorizeActiveState();
      }
    });
    if (!this.taskDetails()[taskId]) {
      this.loadTaskDetail(taskId);
    }
  }

  releaseTaskDetail(): void {
    this.clearProtectedTaskState();
    this.activeTaskId = null;
    this.activeProjectId = null;
    this.activeProjectSubscription?.();
    this.activeProjectSubscription = null;
    this.activeProjectCatchUpCleanup?.();
    this.activeProjectCatchUpCleanup = null;
  }

  getTaskMutationState(): TaskMutationState {
    return this.scenario?.taskMutationState ?? this.taskMutationState();
  }

  getTaskConflictReloadState(): TaskConflictReloadState { return this.scenario?.taskConflictReloadState ?? this.taskConflictReloadState(); }

  getTaskCreateMutationState(): TaskMutationState {
    return this.taskCreateMutationState();
  }

  clearTaskMutationState(): void {
    // A stale-version conflict has only one recovery path: an authoritative reload.
    if (this.taskMutationState().status !== 'conflict') {this.taskMutationState.set({ status: 'idle' });}
  }

  clearTaskCreateMutationState(): void {
    this.taskCreateMutationState.set({ status: 'idle' });
  }

  getDetailSectionState(section: TaskDetailSection): TaskDetailSectionState {
    return this.sectionStates()[section];
  }

  loadProjectLabelDefinitions(projectId: string, force = false, onReady?: () => void): void {
    const taskId = this.activeTaskId;
    if (this.scenario || !projectId || !taskId || !this.isActive(taskId, this.detailGeneration)) {return;}
    if (!force && this.projectLabelDefinitions()[projectId] && this.labelDefinitionStates()[projectId]?.status === 'ready') { onReady?.(); return; }
    this.labelRequest?.unsubscribe();
    const generation = this.detailGeneration;
    this.setLabelDefinitionState(projectId, { status: 'loading' });
    this.labelRequest = this.http.get<readonly TaskLabelDto[]>(`/api/projects/${projectId}/task-labels?includeArchived=true`, { withCredentials: true })
      .pipe(finalize(() => { if (this.isActive(taskId, generation)) {this.labelRequest = null;} }))
      .subscribe({
      next: labels => {
          if (!this.isActive(taskId, generation)) {return;}
          this.projectLabelDefinitions.update(current => ({ ...current, [projectId]: labels }));
          this.setLabelDefinitionState(projectId, { status: labels.length ? 'ready' : 'empty' });
          onReady?.();
        },
      error: error => {
        if (!this.isActive(taskId, generation)) {return;}
        const normalized = normalizeApiError(error);
        if ((normalized.httpStatus === 401 || normalized.httpStatus === 403) && !isLabelDefinitionOnlyPermissionDenied(normalized.code)) {
          this.reauthorizeActiveState();
          return;
        }
        const failure = toSectionFailure(error, 'Label definitions could not be loaded.');
        this.setLabelDefinitionState(projectId, failure);
        }
      });
  }

  createProjectLabel(taskId: string, projectId: string, name: string, onSuccess?: () => void): void {
    const trimmed = name.trim();
    if (!trimmed || trimmed.length > TASK_LABEL_NAME_MAX_LENGTH) {return;}
    this.runDetailCommand(taskId, 'labels', this.http.post(`/api/projects/${projectId}/task-labels`, { name: trimmed, description: null }, { withCredentials: true }), () => this.loadProjectLabelDefinitions(projectId, true, onSuccess));
  }

  updateProjectLabel(taskId: string, projectId: string, labelId: string, name: string, description: string, sortKey: string, expectedVersion: string, onSuccess?: () => void): void {
    const trimmed = name.trim();
    const trimmedDescription = description.trim();
    if (!trimmed || trimmed.length > TASK_LABEL_NAME_MAX_LENGTH || trimmedDescription.length > TASK_LABEL_DESCRIPTION_MAX_LENGTH) {return;}
    this.runDetailCommand(taskId, 'labels', this.http.patch(`/api/projects/${projectId}/task-labels/${labelId}`, { name: trimmed, description: trimmedDescription || null, sortKey: Number(sortKey), expectedVersion: Number(expectedVersion) }, { withCredentials: true }), () => this.loadProjectLabelDefinitions(projectId, true, onSuccess));
  }

  setProjectLabelArchived(taskId: string, projectId: string, labelId: string, expectedVersion: string, archived: boolean): void {
    const action = archived ? 'archive' : 'restore';
    this.runDetailCommand(taskId, 'labels', this.http.post(`/api/projects/${projectId}/task-labels/${labelId}/${action}?expectedVersion=${encodeURIComponent(expectedVersion)}`, {}, { withCredentials: true }), () => this.loadProjectLabelDefinitions(projectId, true));
  }

  retryTaskDetail(taskId: string): void { this.loadTaskDetail(taskId); }

  /** Reload a stale editor only after an explicit user request; cancel never reaches this path. */
  reloadTaskAfterConflict(taskId: string): void {
    const status = this.taskMutationState().status;
    if (this.scenario || this.taskConflictReloadInProgress || (status !== 'conflict' && status !== 'savedButRefreshFailed') || !this.isActive(taskId, this.detailGeneration)) {return;}
    this.taskConflictReloadInProgress = true;
    this.taskConflictReloadState.set('loading');
    this.loadTaskDetail(taskId, { kind: 'taskBodyReload' });
  }

  loadMoreSubtasks(taskId: string): void { this.loadNextPage(taskId, 'subtasks'); }
  loadMoreComments(taskId: string): void { this.loadNextPage(taskId, 'comments'); }
  loadMoreFiles(taskId: string): void { this.loadNextPage(taskId, 'files'); }
  loadMoreActivity(taskId: string): void { this.loadNextPage(taskId, 'activity'); }
  loadActivity(taskId: string): void {
    if (this.getDetailSectionState('activity').status === 'idle') {this.loadActivityFirstPage(taskId);}
  }
  retrySection(taskId: string, section: TaskDetailSection): void {
    const failed = this.getDetailSectionState(section);
    if (section === 'detail' || failed.retryKind === 'aggregate') {this.loadTaskDetail(taskId, { kind: 'sectionRecovery', section });}
    else if (section === 'labels') {
      const projectId = this.taskDetails()[taskId]?.task?.projectId;
      if (typeof projectId === 'string') {this.loadProjectLabelDefinitions(projectId, true);}
    } else if (section === 'activity' || section === 'subtasks' || section === 'comments' || section === 'files') {this.loadNextPage(taskId, section, failed.failedPage);}
    else {this.loadTaskDetail(taskId);}
  }

  createSubtask(taskId: string, title: string, onSuccess?: () => void): void { const trimmed = title.trim(); if (trimmed && trimmed.length <= 300) {this.runDetailCommand(taskId, 'subtasks', this.http.post(`/api/tasks/${taskId}/subtasks`, { title: trimmed, description: null, priority: 1 }, { withCredentials: true }), onSuccess);} }
  createChecklist(taskId: string, text: string, onSuccess?: () => void): void { const trimmed = text.trim(); if (trimmed && trimmed.length <= 1000) {this.runDetailCommand(taskId, 'checklist', this.http.post(`/api/tasks/${taskId}/checklist`, { text: trimmed }, { withCredentials: true }), onSuccess);} }
  updateChecklist(taskId: string, itemId: string, text: string, isCompleted: boolean, expectedVersion: string, onSuccess?: () => void): void { const trimmed = text.trim(); if (trimmed && trimmed.length <= 1000) {this.runDetailCommand(taskId, 'checklist', this.http.patch(`/api/tasks/${taskId}/checklist/${itemId}`, { text: trimmed, isCompleted, expectedVersion: Number(expectedVersion) }, { withCredentials: true }), onSuccess);} }
  deleteChecklist(taskId: string, itemId: string, expectedVersion: string): void { this.runDetailCommand(taskId, 'checklist', this.http.delete(`/api/tasks/${taskId}/checklist/${itemId}?expectedVersion=${encodeURIComponent(expectedVersion)}`, { withCredentials: true })); }
  reorderChecklist(taskId: string, orderedItemIds: readonly string[], expectedTaskVersion: string): void { this.runDetailCommand(taskId, 'checklist', this.http.put(`/api/tasks/${taskId}/checklist/order`, { orderedItemIds, expectedTaskVersion: Number(expectedTaskVersion) }, { withCredentials: true })); }
  createComment(taskId: string, bodyPlainText: string, isImportant: boolean, onSuccess?: () => void): void { const body = bodyPlainText.trim(); if (body && body.length <= 12000) {this.runDetailCommand(taskId, 'comments', this.http.post(`/api/tasks/${taskId}/comments`, { bodyPlainText: body, isImportant }, { withCredentials: true }), onSuccess);} }
  updateComment(taskId: string, commentId: string, bodyPlainText: string, isImportant: boolean, expectedVersion: string, onSuccess?: () => void): void { const body = bodyPlainText.trim(); if (body && body.length <= 12000) {this.runDetailCommand(taskId, 'comments', this.http.patch(`/api/task-comments/${commentId}`, { bodyPlainText: body, isImportant, expectedVersion: Number(expectedVersion) }, { withCredentials: true }), onSuccess);} }
  deleteComment(taskId: string, commentId: string, expectedVersion: string): void { this.runDetailCommand(taskId, 'comments', this.http.delete(`/api/task-comments/${commentId}?expectedVersion=${encodeURIComponent(expectedVersion)}`, { withCredentials: true })); }
  applyLabel(taskId: string, labelId: string, expectedVersion: string, onSuccess?: () => void): void { this.runDetailCommand(taskId, 'labels', this.http.put(`/api/tasks/${taskId}/labels/${labelId}`, { expectedVersion: Number(expectedVersion) }, { withCredentials: true }), onSuccess); }
  removeLabel(taskId: string, labelId: string, expectedVersion: string): void { this.runDetailCommand(taskId, 'labels', this.http.delete(`/api/tasks/${taskId}/labels/${labelId}?expectedVersion=${encodeURIComponent(expectedVersion)}`, { withCredentials: true })); }
  setWatch(taskId: string, watching: boolean, expectedVersion: string): void { this.runDetailCommand(taskId, 'watch', watching ? this.http.put(`/api/tasks/${taskId}/watch`, { expectedVersion: Number(expectedVersion) }, { withCredentials: true }) : this.http.delete(`/api/tasks/${taskId}/watch?expectedVersion=${encodeURIComponent(expectedVersion)}`, { withCredentials: true })); }
  associateFile(taskId: string, attachmentId: string, expectedVersion: string, onSuccess?: () => void): void { this.runDetailCommand(taskId, 'files', this.http.post(`/api/tasks/${taskId}/files`, { attachmentId, expectedVersion: Number(expectedVersion) }, { withCredentials: true }), onSuccess); }
  removeFile(taskId: string, associationId: string, expectedVersion: string): void { this.runDetailCommand(taskId, 'files', this.http.delete(`/api/tasks/${taskId}/files/${associationId}?expectedVersion=${encodeURIComponent(expectedVersion)}`, { withCredentials: true })); }

  retryProjects(): void {
    if (this.scenario) {
      return;
    }

    this.loadProjects();
  }

  saveTask(taskId: string, projectId: string, request: TaskEditorSaveRequest): void {
    if (this.scenario || this.taskMutationState().status === 'submitting') {
      return;
    }
    const expectedVersion = Number(request.expectedVersion);
    if (!Number.isSafeInteger(expectedVersion) || expectedVersion <= 0) {
      this.taskMutationState.set({ status: 'validation', message: 'The latest task version is unavailable. Reload before saving.' });
      return;
    }

    const authorizationGeneration = this.authorizationGeneration;
    const detailGeneration = this.detailGeneration;
    const isCurrent = () => this.isAuthorizationCurrent(authorizationGeneration) &&
      (this.activeTaskId === null || this.isActive(taskId, detailGeneration));
    this.taskMutationState.set({ status: 'submitting' });
    const operation = this.http.patch<TaskDto>(`/api/tasks/${taskId}`, toUpdateTaskRequestDto(request), {
      withCredentials: true
    }).subscribe({
      next: () => {
        if (!isCurrent()) {return;}
        // PATCH has succeeded. A following GET error must not be reported as a save error.
        this.taskMutationState.set({ status: 'refreshingAfterSave' });
        const refresh = forkJoin({ task: this.fetchTask(taskId), projectTasks: this.fetchProjectTasks(projectId) }).subscribe({
          next: ({ task, projectTasks }) => {
            if (!isCurrent()) {return;}
            this.replaceProjectTasks(projectId, projectTasks);
            this.applyAggregate(taskId, task.detail, { kind: 'taskBodyReload' });
            this.replaceTask(task.task);
            this.myTasksFacade.refreshIfLoaded();
            this.taskMutationState.set({ status: 'success' });
          },
          error: (error: unknown) => {
            if (!isCurrent()) {return;}
            const normalized = normalizeApiError(error);
            if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {
              this.reauthorizeActiveState();
              return;
            }
            this.taskMutationState.set({ status: 'savedButRefreshFailed', message: normalized.message || 'Reload before editing again.', requestId: normalized.requestId });
          }
        });
        this.trackDetailMutation(refresh);
      },
      error: (error: unknown) => {
        if (!isCurrent()) {return;}
        const normalized = normalizeApiError(error);
        // Task reads deliberately use a safe 404 for revoked or cross-scope
        // access. An active detail receiving that response must discard every
        // protected projection before any dependent UI (including the File
        // picker) can issue another scoped request.
        if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {
          this.reauthorizeActiveState();
          return;
        }
        if (normalized.httpStatus === 404) {
          this.denyTaskDetail();
          return;
        }
        this.taskMutationState.set(toFailureState(error, 'Task save failed.'));
      }
    });
    this.trackDetailMutation(operation);
  }

  createTask(request: CreateTaskFormRequest): void {
    if (this.scenario || this.taskCreateMutationState().status === 'submitting') {
      return;
    }

    const authorizationGeneration = this.authorizationGeneration;
    this.taskCreateMutationState.set({ status: 'submitting' });
    const operation = this.http
      .post<TaskDto>(`/api/projects/${request.projectId}/tasks`, toCreateTaskRequestDto(request), {
        withCredentials: true
      })
      .pipe(
        switchMap(() =>
          forkJoin({
            projectTasks: this.fetchProjectTasks(request.projectId)
          })
        )
      )
      .subscribe({
        next: ({ projectTasks }) => {
          if (!this.isAuthorizationCurrent(authorizationGeneration)) {return;}
          this.replaceProjectTasks(request.projectId, projectTasks);
          this.myTasksFacade.refreshIfLoaded();
          this.taskCreateMutationState.set({ status: 'success' });
        },
        error: (error: unknown) => {
          if (!this.isAuthorizationCurrent(authorizationGeneration)) {return;}
          this.taskCreateMutationState.set(toFailureState(error, 'Task create failed.'));
        }
      });
    this.trackDetailMutation(operation);
  }

  getStatusLabel(status: TaskGridRow['status']): string {
    return taskStatusLabel(status);
  }

  private loadProjects(afterAuthorized?: () => void): void {
    const generation = this.authorizationGeneration;
    const workspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
    this.projectsRequest?.unsubscribe();
    this.projectsRequest = null;
    if (!workspaceId) {
      this.liveState.set(this.emptyScenario('loading', 'Choose an authorized Workspace to load its Projects.'));
      return;
    }
    this.liveState.set(this.emptyScenario('loading'));
    this.projectsRequest = this.fetchProjectList(workspaceId)
      .pipe(
        switchMap((projects) => {
          if (projects.length === 0) {
            return of({
              projects,
              tasks: []
            } satisfies ProjectsLoadResult);
          }

          const operationalProjects = projects.filter((project) => project.isOperational !== false);
          if (operationalProjects.length === 0) {
            return of({ projects, tasks: [] } satisfies ProjectsLoadResult);
          }

          return forkJoin({
            // A canonical NeverActivated Draft deliberately has no workflow or
            // Task mutation surface. Do not probe its operational collection.
            taskPages: forkJoin(operationalProjects.map((project) => this.fetchProjectTasks(project.id, projects)))
          }).pipe(
            map(({ taskPages }) => ({
              projects,
              tasks: taskPages.flat()
            }))
          );
        })
      )
      .subscribe({
        next: (result) => {
          if (!this.isAuthorizationCurrent(generation) ||
              this.activeWorkspace.activeWorkspace()?.id !== workspaceId) {return;}
          if (result.projects.length === 0) {
            this.liveState.set(
              this.emptyScenario('empty', 'No authorized projects were returned by the API.')
            );
            return;
          }

          this.liveState.set({
            status: 'ready',
            title: 'Projects',
            subtitle: 'Live API data',
            projects: result.projects,
            tasks: result.tasks,
            myTasks: [],
            currentUserAssignee: ''
          });
          afterAuthorized?.();
        },
        error: (error: unknown) => {
          if (!this.isAuthorizationCurrent(generation) ||
              this.activeWorkspace.activeWorkspace()?.id !== workspaceId) {return;}
          const normalized = normalizeApiError(error);
          this.liveState.set(
            this.emptyScenario(
              normalized.httpStatus === 401 || normalized.httpStatus === 403 ? 'permissionDenied' : 'error',
              normalized.httpStatus === 401 || normalized.httpStatus === 403
                ? 'Authentication or project permission is required.'
                : 'Projects could not be loaded. Try again.',
              normalized
            )
          );
        }
      });
  }

  private handleRealtimeEvent(event: DurableRealtimeEvent): void {
    if (this.scenario) {
      return;
    }

    if (event.eventType === 'Security.AuthorizationStateChanged.v1') {
      this.reauthorizeActiveState();
      return;
    }

    if (![
      'Projects.TaskChanged.v1',
      'Projects.TaskAssignmentChanged.v1',
      'Projects.TaskWorkflowChanged.v1',
      'Projects.TaskCommentChanged.v1',
      'Projects.ProjectChanged.v1',
      'Files.FileChanged.v1'
    ].includes(event.eventType)) {
      return;
    }

    if (event.eventType !== 'Projects.ProjectChanged.v1' && this.activeTaskId === event.aggregateId) {
      if (this.isTaskBodyEditing()) {
        this.taskMutationState.set({ status: 'conflict', message: 'This task changed elsewhere. Your editor was preserved; reload before saving again.', requestId: event.eventId });
      } else {
        this.loadTaskDetail(event.aggregateId, { kind: 'realtimeRefresh' });
      }
      return;
    }

    if (event.eventType === 'Files.FileChanged.v1' && this.activeTaskId) {
      this.loadTaskDetail(this.activeTaskId, { kind: 'realtimeRefresh' });
      return;
    }

    this.queueRealtimeRefresh();
  }

  private queueRealtimeRefresh(): void {
    if (this.refreshTimer !== null) {
      return;
    }

    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = null;
      this.loadProjects();
      this.myTasksFacade.refreshIfLoaded();
    }, 100);
  }

  private fetchProjectList(workspaceId: string): Observable<readonly ProjectMockRecord[]> {
    return this.http
      .get<PagedResponseDto<ProjectDto>>('/api/projects', {
        withCredentials: true,
        params: new HttpParams().set('workspaceId', workspaceId)
      })
      .pipe(map((response) => (response.items ?? []).map((project) => mapProjectDtoToRecord(project))));
  }

  private fetchProjectTasks(projectId: string, projects = this.authorizedProjects()): Observable<readonly TaskMockRecord[]> {
    return this.http
      .get<PagedResponseDto<TaskDto>>(`/api/projects/${projectId}/tasks`, {
        withCredentials: true
      })
      .pipe(
        map((response) =>
          (response.items ?? []).map((task) => mapTaskDtoToRecord(task, projects))
        )
      );
  }

  private fetchTask(taskId: string): Observable<{ readonly task: TaskMockRecord; readonly detail: CanonicalTaskDetailDto }> {
    return this.http
      .get<CanonicalTaskDetailDto>(`/api/tasks/${taskId}`, { withCredentials: true })
      .pipe(map((detail) => ({ task: mapTaskDtoToRecord((detail.task ?? detail) as TaskDto, this.authorizedProjects()), detail })));
  }

  private loadTaskDetail(taskId: string, scope: AggregateApplyScope = { kind: 'initialLoad' }): void {
    if (this.scenario) {
      return;
    }

    if (this.activeTaskId !== taskId) {return;}
    this.detailRequest?.unsubscribe();
    const authorizationGeneration = this.authorizationGeneration;
    const generation = this.detailGeneration;
    // Bind the asynchronous read to the Workspace authorization scope observed here.
    // Reading ActiveWorkspace later inside switchMap would create a TOCTOU race.
    // Cold direct-route hydration can otherwise change null -> Workspace mid-flight.
    // That race can duplicate the parent Project read across authorization generations.
    const expectedWorkspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
    if (scope.kind === 'initialLoad') {this.setSectionState('detail', { status: 'loading' });}
    if (scope.kind === 'sectionRecovery') {this.setSectionState(scope.section, { status: 'loading', retryKind: 'aggregate' });}
    this.detailRequest = this.fetchTaskWithParentProject(taskId, this.activeProjectId, expectedWorkspaceId).pipe(finalize(() => {
      if (this.isActive(taskId, generation)) {this.detailRequest = null;}
    })).subscribe({
      next: (response) => {
        if (!this.isAuthorizationCurrent(authorizationGeneration) || !this.isActive(taskId, generation)) {return;}
        if (response.scopeMismatch) {
          this.denyTaskDetail();
          return;
        }
        // A cold direct route can resolve its Task before shell Workspace hydration.
        // Keep the aggregate undisclosed until this route is reauthorized.
        if (response.workspacePending) {return;}
        if (response.parentProject) {this.replaceProject(response.parentProject);}
        // An aggregate refresh must not cancel an Activity request that the
        // user has already opened. Cancelling it starts a second request that
        // can remain loading after the first real response has completed.
        // Only refresh a settled Activity projection; errors retain their
        // explicit retry state and an in-flight request owns its response.
        const activityState = this.getDetailSectionState('activity').status;
        const refreshActivity = activityState === 'ready' || activityState === 'empty';
        this.applyAggregate(taskId, response.detail, scope);
        this.replaceTask(response.task);
        const authorizedTask = (response.detail.task ?? response.detail) as TaskDto;
        const taskProjectId = authorizedTask.projectId;
        const taskWorkspaceId = authorizedTask.workspaceId;
        if (typeof taskProjectId === 'string' && typeof taskWorkspaceId === 'string') {
          // A Task is not a Continue-working card. Its successfully applied,
          // authorized aggregate advances only its parent Research recency.
          this.continueWorkingHistory.touchProject(taskProjectId, taskWorkspaceId);
        }
        if (scope.kind === 'taskBodyReload') {
          this.taskMutationState.set({ status: this.taskMutationState().status === 'savedButRefreshFailed' ? 'success' : 'idle' });
          this.taskConflictReloadState.set('idle');
        }
        this.taskConflictReloadInProgress = false;
        if (refreshActivity) {this.loadActivityFirstPage(taskId);}
        const projectId = response.detail.task?.projectId;
        const permissions = response.detail.permissions;
        if (typeof projectId === 'string' && (permissions?.canApplyLabels === true || permissions?.canManageLabelDefinitions === true)) {this.loadProjectLabelDefinitions(projectId);}
      },
      error: (error: unknown) => {
        if (!this.isAuthorizationCurrent(authorizationGeneration) || !this.isActive(taskId, generation)) {return;}
        this.taskConflictReloadInProgress = false;
        const normalized = normalizeApiError(error);
        // A revoked membership is intentionally surfaced by the API as the same
        // safe 404 used for an unavailable Task. Treat it as an authorization
        // boundary: clear the cached aggregate before any dependent picker can
        // request workspace-scoped data with stale state.
        if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {
          this.reauthorizeActiveState();
          return;
        }
        if (normalized.httpStatus === 404) {
          this.denyTaskDetail();
          return;
        }
        if (scope.kind === 'taskBodyReload') {
          // The editor remains mounted, with its local FormGroup intact, until an authoritative reload succeeds.
          this.taskConflictReloadState.set('error');
          return;
        }
        const failure = toSectionFailure(error, 'Task detail could not be loaded.');
        this.setSectionState('detail', failure);
        if (scope.kind === 'sectionRecovery' && scope.section !== 'detail') {this.setSectionState(scope.section, failure);}
      }
    });
  }

  private fetchTaskWithParentProject(
    taskId: string,
    expectedProjectId: string | null,
    expectedWorkspaceId: string | null
  ): Observable<TaskDetailLoadResult> {
    return this.fetchTask(taskId).pipe(
      switchMap((response) => {
        const projectId = response.task.projectId;
        if (!expectedProjectId || projectId !== expectedProjectId) {
          return of({ ...response, scopeMismatch: true });
        }
        if (!expectedWorkspaceId) {return of({ ...response, workspacePending: true });}
        const taskWorkspaceId = (response.detail.task ?? (response.detail as TaskDto)).workspaceId;
        if (typeof taskWorkspaceId !== 'string' || taskWorkspaceId !== expectedWorkspaceId) {
          return of({ ...response, scopeMismatch: true });
        }
        // The broad inventory can contain Projects from multiple Workspaces,
        // so this shortcut is safe only after the canonical Task has matched
        // the Workspace snapshot captured for this authorization generation.
        if (this.authorizedProjects().some((project) => project.id === projectId)) {return of(response);}

        return this.http
          .get<ProjectDto>(`/api/projects/${projectId}`, { withCredentials: true })
          .pipe(
            map((project) => {
              if (project.id !== projectId || typeof project.title !== 'string' || project.title.trim().length === 0) {
                throw new Error('The parent Project response is invalid.');
              }

              return { ...response, parentProject: mapProjectDtoToRecord(project) };
            }),
            catchError((error: unknown) => {
              const status = normalizeApiError(error).httpStatus;
              return status === 401 || status === 403 || status === 404
                ? of({ ...response, scopeMismatch: true })
                : throwError(() => error);
            })
          );
      })
    );
  }

  /** Any section draft protects that section's data from an automatic aggregate overwrite. */
  private detailEditing = false;
  /** Only an unsaved Task-body draft may create a Task-body conflict banner. */
  private taskBodyEditing = false;
  setDetailEditing(editing: boolean): void { this.detailEditing = editing; }
  setTaskBodyEditing(editing: boolean): void { this.taskBodyEditing = editing; }
  private isTaskBodyEditing(): boolean {
    return this.taskBodyEditing || this.taskMutationState().status === 'submitting' || this.taskMutationState().status === 'refreshingAfterSave';
  }

  private reauthorizeActiveState(): void {
    const activeTaskId = this.activeTaskId;
    const activeProjectId = this.activeProjectId;
    this.authorizationGeneration++;
    // Drop protected state before issuing the first reauthorization request.
    this.clearProtectedTaskState();
    this.liveState.set(this.emptyScenario('loading'));
    this.loadProjects(() => {
      if (!activeTaskId || !activeProjectId || this.activeTaskId !== activeTaskId || this.activeProjectId !== activeProjectId) {return;}
      const visible = this.liveState().tasks.some(task => task.id === activeTaskId && task.projectId === activeProjectId && task.authorized);
      if (!visible) {
        this.setSectionState('detail', { status: 'permissionDenied', message: 'Task detail is no longer available with your current permission.' });
        return;
      }
      this.loadTaskDetail(activeTaskId);
    });
  }

  private clearForAuthorizationLoss(): void {
    if (this.scenario) {return;}
    this.authorizationGeneration++;
    this.projectsRequest?.unsubscribe();
    this.projectsRequest = null;
    this.clearProtectedTaskState();
    this.liveState.set(this.emptyScenario('loading'));
  }

  private clearProtectedState(reason: ProtectedStateClearReason): void {
    if (reason === 'authorization') {
      // Keep the mounted route's opaque IDs and declarative realtime intent.
      // A successful SignalR catch-up or degraded HTTP restoration will
      // reauthorize them before repopulating protected projections.
      this.clearForAuthorizationLoss();
      return;
    }

    this.authorizationGeneration++;
    this.projectsRequest?.unsubscribe();
    this.projectsRequest = null;
    this.releaseTaskDetail();
    this.liveState.set(this.emptyScenario('loading'));
  }

  private denyTaskDetail(): void {
    this.clearProtectedTaskState();
    this.liveState.set(this.emptyScenario('permissionDenied', 'Task detail is no longer available with your current permission.'));
  }

  private runDetailCommand(taskId: string, section: TaskDetailSection, request: Observable<unknown>, onSuccess?: () => void): void {
    if (this.scenario || !this.isActive(taskId, this.detailGeneration) || this.sectionStates()[section].status === 'submitting') {return;}
    const authorizationGeneration = this.authorizationGeneration;
    const generation = this.detailGeneration;
    this.setSectionState(section, { status: 'submitting' });
    const operation = request.pipe(
      switchMap(() => this.fetchTask(taskId).pipe(
        map(response => ({ response })),
        catchError(reloadError => of({ reloadError }))
      ))
    ).subscribe({
      next: result => {
        if (!this.isAuthorizationCurrent(authorizationGeneration) || !this.isActive(taskId, generation)) {return;}
        if ('reloadError' in result) {
          const normalized = normalizeApiError(result.reloadError);
          if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {
            this.reauthorizeActiveState();
            return;
          }
          const reloadFailure = toSectionFailure(result.reloadError, 'Saved successfully, but the latest task detail could not be loaded.');
          this.setSectionState(section, { ...reloadFailure, status: reloadFailure.status === 'permissionDenied' ? 'permissionDenied' : 'error', message: `Saved successfully, but the latest task detail could not be loaded. ${reloadFailure.message ?? ''}`.trim(), retryKind: 'aggregate' });
          return;
        }
        this.applyAggregate(taskId, result.response.detail, { kind: 'sectionMutation', section });
        this.replaceTask(result.response.task);
        onSuccess?.();
      },
      error: (error: unknown) => {
        if (!this.isAuthorizationCurrent(authorizationGeneration) || !this.isActive(taskId, generation)) {return;}
        const normalized = normalizeApiError(error);
        if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {
          this.reauthorizeActiveState();
          return;
        }
        const state = toFailureState(error, 'Task detail command failed.');
        this.setSectionState(section, toSectionFailure(error, state.status === 'failure' || state.status === 'conflict' ? state.message : 'Task detail command failed.'));
      }
    });
    this.trackDetailMutation(operation);
  }

  private loadNextPage(taskId: string, section: 'activity' | 'subtasks' | 'comments' | 'files', retryPage?: number): void {
    const detail = this.taskDetails()[taskId];
    if (!detail || !this.isActive(taskId, this.detailGeneration)) {return;}
    const current = detail[section];
    const currentPage = typeof current?.page === 'number' ? current.page : 1;
    const pageSize = typeof current?.pageSize === 'number' && current.pageSize > 0 ? current.pageSize : section === 'subtasks' ? 50 : 20;
    if (!retryPage && current?.hasMore !== true) {return;}
    if (this.pageRequests.has(section)) {return;}
    const generation = this.detailGeneration;
    const authorizationGeneration = this.authorizationGeneration;
    const page = retryPage ?? currentPage + 1;
    const replaceItems = section === 'activity' && page === 1;
    const endpoint = `/api/tasks/${taskId}/${section}?page=${page}&pageSize=${pageSize}`;
    this.setSectionState(section, { status: 'loading' });
    const request = this.http.get<PagedResponseDto<unknown>>(endpoint, { withCredentials: true }).pipe(finalize(() => {
      if (this.isActive(taskId, generation)) {this.pageRequests.delete(section);}
    })).subscribe({
      next: response => {
        if (!this.isAuthorizationCurrent(authorizationGeneration) || !this.isActive(taskId, generation)) {return;}
        const existing = replaceItems ? [] : (current?.items ?? []);
        const incoming = (response.items ?? []) as typeof existing;
        const seen = new Set(existing.map(item => typeof (item as { id?: unknown }).id === 'string' ? (item as { id: string }).id : ''));
        const items = [...existing, ...incoming.filter(item => {
          const id = typeof (item as { id?: unknown }).id === 'string' ? (item as { id: string }).id : '';
          return !id || !seen.has(id) && (seen.add(id), true);
        })];
        this.taskDetails.update(details => ({ ...details, [taskId]: { ...details[taskId], [section]: { items, page: response.page ?? page, pageSize: response.pageSize ?? pageSize, totalCount: response.totalCount ?? items.length, hasMore: response.hasMore === true } } }));
        this.setSectionState(section, { status: items.length ? 'ready' : 'empty' });
      },
      error: error => {
        if (!this.isAuthorizationCurrent(authorizationGeneration) || !this.isActive(taskId, generation)) {return;}
        const normalized = normalizeApiError(error);
        const failure = { ...toSectionFailure(error, `More ${section} could not be loaded.`), retryKind: 'page' as const, failedPage: page };
        if (failure.status === 'permissionDenied') { this.reauthorizeActiveState(); return; }
        if (section === 'activity' && normalized.httpStatus === 404) { this.denyTaskDetail(); return; }
        this.setSectionState(section, failure);
      }
    });
    this.pageRequests.set(section, request);
  }

  /** Refresh Task-linked Activity independently so it can never suppress the authoritative current phase. */
  private loadActivityFirstPage(taskId: string): void {
    const pending = this.pageRequests.get('activity');
    if (pending) {
      pending.unsubscribe();
      this.pageRequests.delete('activity');
    }
    this.loadNextPage(taskId, 'activity', 1);
  }

  private clearProtectedTaskState(): void {
    this.detailGeneration++;
    this.detailRequest?.unsubscribe();
    this.detailRequest = null;
    this.labelRequest?.unsubscribe();
    this.labelRequest = null;
    this.pageRequests.forEach(request => request.unsubscribe());
    this.pageRequests.clear();
    this.detailMutations.forEach(request => request.unsubscribe());
    this.detailMutations.clear();
    this.taskConflictReloadInProgress = false;
    this.taskConflictReloadState.set('idle');
    this.taskDetails.set({});
    this.projectLabelDefinitions.set({});
    this.labelDefinitionStates.set({});
    this.sectionStates.set(this.emptySectionStates());
    this.taskMutationState.set({ status: 'idle' });
    this.taskCreateMutationState.set({ status: 'idle' });
    this.detailEditing = false;
  }

  private isActive(taskId: string, generation: number): boolean {
    return this.activeTaskId === taskId && this.detailGeneration === generation;
  }

  private isAuthorizationCurrent(generation: number): boolean { return this.authorizationGeneration === generation; }

  private trackDetailMutation(request: Subscription): void {
    this.detailMutations.add(request);
    request.add(() => this.detailMutations.delete(request));
  }

  private setSectionState(section: TaskDetailSection, state: TaskDetailSectionState): void {
    this.sectionStates.update(current => ({ ...current, [section]: state }));
  }

  private setLabelDefinitionState(projectId: string, state: TaskDetailSectionState): void {
    this.labelDefinitionStates.update(current => ({ ...current, [projectId]: state }));
  }

  /**
   * Applies aggregate data, pagination and section state under one scope rule.
   * A response containing only page one must never erase an already loaded page two.
   */
  private applyAggregate(taskId: string, incoming: CanonicalTaskDetailDto, scope: AggregateApplyScope): void {
    const current = this.taskDetails()[taskId];
    const states = this.sectionStates();
    const preserve = (section: Exclude<TaskDetailSection, 'detail'>): boolean => {
      if (!current || scope.kind === 'initialLoad') {return false;}
      if (scope.kind === 'taskBodyReload') {return true;}
      if (scope.kind === 'sectionMutation' || scope.kind === 'sectionRecovery') {return section !== scope.section;}
      return this.detailEditing || isProtectedSectionState(states[section]);
    };

    const detail = !current || scope.kind === 'initialLoad'
      ? incoming
      : {
          ...current,
          ...incoming,
          task: incoming.task ?? current.task,
          relationships: incoming.relationships ?? current.relationships,
          permissions: incoming.permissions ?? current.permissions,
          checklist: preserve('checklist') ? current.checklist : (incoming.checklist ?? current.checklist),
          labels: preserve('labels') ? current.labels : (incoming.labels ?? current.labels),
          watchState: preserve('watch') ? current.watchState : (incoming.watchState ?? current.watchState),
          subtasks: preservePagedSection(current.subtasks, incoming.subtasks, preserve('subtasks')),
          comments: preservePagedSection(current.comments, incoming.comments, preserve('comments')),
          files: preservePagedSection(current.files, incoming.files, preserve('files')),
          activity: preservePagedSection(current.activity, incoming.activity, preserve('activity'))
        };

    this.taskDetails.update(details => ({ ...details, [taskId]: detail }));
    this.applyAggregateSectionStates(detail, scope);
  }

  /** An aggregate response never clears an unrelated section's error/conflict/retry evidence. */
  private applyAggregateSectionStates(detail: CanonicalTaskDetailDto, scope: AggregateApplyScope): void {
    const itemState = (items: readonly unknown[] | undefined): TaskDetailSectionState => ({ status: items?.length ? 'ready' : 'empty' });
    const next: Partial<Record<TaskDetailSection, TaskDetailSectionState>> = {
      detail: { status: 'ready' },
      subtasks: itemState(detail.subtasks?.items),
      checklist: itemState(detail.checklist),
      comments: itemState(detail.comments?.items),
      labels: itemState(detail.labels),
      watch: { status: 'ready' },
      files: itemState(detail.files?.items)
    };
    this.sectionStates.update(current => {
      const updated = { ...current };
      for (const [section, state] of Object.entries(next) as [TaskDetailSection, TaskDetailSectionState][]) {
        if (scope.kind === 'initialLoad') {
          updated[section] = state;
          continue;
        }
        if (section === 'detail') {
          updated[section] = state;
          continue;
        }
        if ((scope.kind === 'sectionMutation' || scope.kind === 'sectionRecovery') && section === scope.section) {
          updated[section] = state;
          continue;
        }
        if (scope.kind === 'realtimeRefresh' && !this.detailEditing && !isProtectedSectionState(current[section])) {
          updated[section] = state;
        }
      }
      return updated;
    });
  }

  private emptySectionStates(): Record<TaskDetailSection, TaskDetailSectionState> {
    return { detail: { status: 'idle' }, activity: { status: 'idle' }, subtasks: { status: 'idle' }, checklist: { status: 'idle' }, comments: { status: 'idle' }, labels: { status: 'idle' }, watch: { status: 'idle' }, files: { status: 'idle' } };
  }

  private mapDetail(detail: CanonicalTaskDetailDto): TaskDetailAggregateViewModel {
    const boolean = (value: unknown) => value === true;
    const text = (value: unknown) => typeof value === 'string' ? value : '';
    const nullableText = (value: unknown) => typeof value === 'string' && value.length > 0 ? value : null;
    const number = (value: unknown) => typeof value === 'number' && Number.isFinite(value) ? value : 0;
    const version = (value: unknown) => typeof value === 'string' || typeof value === 'number' ? String(value) : '0';
    const activityType = (value: unknown): 'note' | 'statusUpdate' | 'decision' | 'issue' | 'unknown' => {
      const normalized = typeof value === 'string' ? value.replace(/[\s_-]/g, '').toLowerCase() : value;
      if (normalized === 0 || normalized === 'note') {return 'note';}
      if (normalized === 1 || normalized === 'statusupdate') {return 'statusUpdate';}
      if (normalized === 2 || normalized === 'decision') {return 'decision';}
      if (normalized === 3 || normalized === 'issue') {return 'issue';}
      return 'unknown';
    };
    const page = <TSource, TView>(source: PagedResponseDto<TSource> | null | undefined, items: readonly TView[]) => ({ items, page: number(source?.page) || 1, pageSize: number(source?.pageSize) || items.length, totalCount: number(source?.totalCount), hasMore: boolean(source?.hasMore) });
    return {
      canonicalTask: {
        id: text(detail.task?.id), tenantId: nullableText(detail.task?.tenantId), workspaceId: nullableText(detail.task?.workspaceId), projectId: text(detail.task?.projectId),
        kind: typeof detail.task?.kind === 'string' || typeof detail.task?.kind === 'number' ? detail.task.kind : null,
        parentTaskId: nullableText(detail.task?.parentTaskId), title: text(detail.task?.title), description: nullableText(detail.task?.description),
        workflowStageId: nullableText(detail.task?.workflowStageId), workflowStageName: text(detail.task?.workflowStageName),
        stageCategory: typeof detail.task?.stageCategory === 'string' || typeof detail.task?.stageCategory === 'number' ? detail.task.stageCategory : null,
        priority: text(detail.task?.priority), plannedStartDate: nullableText(detail.task?.plannedStartDate), plannedEndDate: nullableText(detail.task?.plannedEndDate),
        deadlineAt: nullableText(detail.task?.deadlineAt), progressPercent: number(detail.task?.progressPercent), progressIsDerived: boolean(detail.task?.progressIsDerived),
        reviewStatus: typeof detail.task?.reviewStatus === 'string' || typeof detail.task?.reviewStatus === 'number' ? detail.task.reviewStatus : null,
        version: version(detail.task?.version), checklistCompletedCount: number(detail.task?.subresources?.checklistCompletedCount),
        checklistTotalCount: number(detail.task?.subresources?.checklistTotalCount), commentCount: number(detail.task?.subresources?.commentCount),
        labelCount: number(detail.task?.subresources?.labelCount), subtaskCount: number(detail.task?.subresources?.subtaskCount)
      },
      relationships: {
        primaryAssignee: nullableText(detail.relationships?.primaryAssignee?.displayName), targetGroupId: nullableText(detail.relationships?.targetGroupId),
        collaborators: (detail.relationships?.collaborators ?? []).map(person => ({ userId: text(person.userId), displayName: text(person.displayName) })).filter(person => person.userId && person.displayName),
        reviewer: nullableText(detail.relationships?.reviewer?.displayName), version: version(detail.relationships?.version)
      },
      workspaceId: nullableText(detail.task?.workspaceId),
      permissions: {
        canCreateSubtask: boolean(detail.permissions?.canCreateSubtask), canCreateChecklistItem: boolean(detail.permissions?.canCreateChecklistItem),
        canUpdateChecklistItems: boolean(detail.permissions?.canUpdateChecklistItems), canDeleteChecklistItems: boolean(detail.permissions?.canDeleteChecklistItems),
        canReorderChecklist: boolean(detail.permissions?.canReorderChecklist), canCreateComment: boolean(detail.permissions?.canCreateComment),
        canMarkCommentImportant: boolean(detail.permissions?.canMarkCommentImportant), canApplyLabels: boolean(detail.permissions?.canApplyLabels),
        canManageLabelDefinitions: boolean(detail.permissions?.canManageLabelDefinitions), canAssociateFiles: boolean(detail.permissions?.canAssociateFiles),
        canRemoveFiles: boolean(detail.permissions?.canRemoveFiles), canChangeWatch: boolean(detail.permissions?.canChangeWatch)
      },
      taskVersion: version(detail.task?.version),
      checklist: (detail.checklist ?? []).map((item) => ({ id: text(item.id), text: text(item.text), isCompleted: boolean(item.isCompleted), completedAt: nullableText(item.completedAt), completedByUserId: nullableText(item.completedByUserId), sortKey: version(item.sortKey), version: version(item.version) })),
      labels: (detail.labels ?? []).map((item) => ({ id: text(item.id), name: text(item.name), description: nullableText(item.description), sortKey: version(item.sortKey), isArchived: boolean(item.isArchived), version: version(item.version) })),
      labelDefinitions: (this.projectLabelDefinitions()[text(detail.task?.projectId)] ?? []).map((item) => ({ id: text(item.id), name: text(item.name), description: nullableText(item.description), sortKey: version(item.sortKey), isArchived: boolean(item.isArchived), version: version(item.version) })),
      labelDefinitionsState: this.labelDefinitionStates()[text(detail.task?.projectId)] ?? { status: 'idle' },
      subtasks: page(detail.subtasks, (detail.subtasks?.items ?? []).map((item) => ({ id: text(item.id), parentTaskId: text(item.parentTaskId), title: text(item.title), workflowStageId: nullableText(item.workflowStageId), stage: text(item.workflowStageName), stageCategory: text(item.stageCategory), priority: text(item.priority), progressPercent: number(item.progressPercent), primaryAssignee: nullableText(item.primaryAssignee?.displayName), plannedEndDate: nullableText(item.plannedEndDate), deadlineAt: nullableText(item.deadlineAt), isOverdue: boolean(item.isOverdue), version: version(item.version) }))),
      comments: page(detail.comments, (detail.comments?.items ?? []).map((item) => ({ id: text(item.id), taskId: text(item.taskId), author: nullableText(item.author?.displayName), body: nullableText(item.bodyPlainText), isImportant: boolean(item.isImportant), mentions: (item.mentions ?? []).map(mention => ({ userId: text(mention.userId), displayName: text(mention.displayName) })).filter(mention => mention.userId && mention.displayName), createdAt: nullableText(item.createdAt), updatedAt: nullableText(item.updatedAt), deletedAt: nullableText(item.deletedAt), version: version(item.version), canEdit: boolean(item.canEdit), canDelete: boolean(item.canDelete), canMarkImportant: boolean(item.canMarkImportant) }))),
      files: page(detail.files, (detail.files?.items ?? []).map((item) => ({ id: text(item.id), fileObjectId: text(item.fileObjectId), fileName: text(item.fileName), contentType: text(item.contentType), sizeBytes: number(item.sizeBytes), scanStatus: text(item.scanStatus), createdAt: nullableText(item.createdAt), accessState: text(item.accessState), canOpen: boolean(item.canOpen), canRequestDownloadGrant: boolean(item.canRequestDownloadGrant), downloadGrantRequired: boolean(item.downloadGrantRequired), restrictionCode: nullableText(item.restrictionCode) }))),
      activity: page(detail.activity, (detail.activity?.items ?? []).map((item) => ({ id: text(item.id), activityType: activityType(item.activityType), body: text(item.body), occurredAt: nullableText(item.occurredAt), authorUserId: nullableText(item.author?.userId), authorDisplayName: text(item.author?.displayName) || 'Unknown author' }))),
      watchState: { isWatching: boolean(detail.watchState?.isWatching), isExplicitOptOut: boolean(detail.watchState?.isExplicitOptOut), automaticSources: (detail.watchState?.automaticSources ?? []).filter((source): source is string => typeof source === 'string'), version: version(detail.watchState?.version) }
    };
  }

  private emptyScenario(status: ProjectsPageStatus, message?: string, error?: FrontendApiError): ProjectsScenario {
    return {
      status,
      title: 'Projects',
      subtitle: 'Live API data',
      projects: [],
      tasks: [],
      myTasks: [],
      currentUserAssignee: '',
      message,
      ...(error ? { error } : {})
    };
  }

  private emptyOverview(
    scenario: ProjectsScenario,
    status: ProjectsOverviewViewModel['status'],
    message?: string
  ): ProjectsOverviewViewModel {
    return {
      status,
      title: scenario.title,
      subtitle: scenario.subtitle,
      projects: [],
      rows: [],
      columns: [],
      pageSize: this.pageSize,
      message,
      error: scenario.error
    };
  }

  private get pageSize(): ProjectsOverviewViewModel['pageSize'] {
    return {
      defaultPageSize: PROJECTS_DEFAULT_PAGE_SIZE,
      maximumPageSize: PROJECTS_MAXIMUM_PAGE_SIZE
    };
  }

  private authorizedProjects(): readonly ProjectMockRecord[] {
    return this.liveState().projects.filter((project) => project.authorized);
  }

  private authorizedTasks(): readonly TaskMockRecord[] {
    const authorizedProjectIds = new Set(this.authorizedProjects().map((project) => project.id));
    return this.liveState().tasks.filter(
      (task) => task.authorized && authorizedProjectIds.has(task.projectId)
    );
  }

  private authorizedMyTasks(): readonly TaskMockRecord[] {
    const scenario = this.liveState();
    const myTasks =
      scenario.myTasks ??
      scenario.tasks.filter(
        (task) =>
          scenario.currentUserAssignee.length === 0 ||
          task.assignee === scenario.currentUserAssignee
      );
    const authorizedProjectIds = new Set(this.authorizedProjects().map((project) => project.id));
    return myTasks.filter((task) => task.authorized && authorizedProjectIds.has(task.projectId));
  }

  private toProjectSummary(project: ProjectMockRecord): ProjectSummaryViewModel {
    const tasks = this.authorizedTasks().filter((task) => task.projectId === project.id);

    return {
      id: project.id,
      workspaceId: project.workspaceId,
      groupId: project.groupId,
      ownerUserId: project.ownerUserId,
      name: project.name,
      description: project.description,
      status: project.status,
      statusLabel: project.statusLabel,
      visibility: project.visibility,
      visibilityLabel: project.visibilityLabel,
      activationState: project.activationState,
      versionNo: project.versionNo,
      isOperational: project.isOperational,
      startDate: project.startDate,
      dueDate: project.dueDate,
      updatedAt: project.updatedAt,
      group: project.group,
      taskCounts: {
        total: tasks.length,
        done: tasks.filter((task) => task.status === 'done').length,
        blocked: tasks.filter((task) => task.status === 'blocked').length
      },
      canCreateTask: project.canCreateTask,
      canActivate: project.canActivate
    };
  }

  private toTaskRow(task: TaskMockRecord): TaskGridRow {
    const projectName =
      this.authorizedProjects().find((project) => project.id === task.projectId)?.name ??
      'Project';

    return {
      id: task.id,
      projectId: task.projectId,
      title: task.title,
      project: projectName,
      status: task.status,
      statusLabel: task.statusLabel,
      workflowStageId: task.workflowStageId,
      workflowStageName: task.workflowStageName,
      stageCategory: task.stageCategory,
      isBlocked: task.isBlocked,
      createdAt: task.createdAt,
      updatedAt: task.updatedAt,
      hasArtifact: task.hasArtifact,
      priority: task.priority,
      priorityLabel: task.priorityLabel,
      assignee: task.assignee,
      startDate: task.startDate,
      dueDate: task.dueDate,
      progressPercent: task.progressPercent,
      milestone: task.milestone,
      allowedTransitions: task.allowedTransitions,
      rowActions: this.buildActions(task)
    };
  }

  private buildActions(task: TaskMockRecord): readonly TaskRowAction[] {
    const actions: TaskRowAction[] = [
      {
        id: 'openDetail',
        label: 'Open',
        disabled: false
      }
    ];

    if (task.capabilities.includes('editTask')) {
      actions.push({
        id: 'edit',
        label: 'Edit',
        disabled: false
      });
    }

    if (task.capabilities.includes('assignTask')) {
      actions.push({
        id: 'assign',
        label: 'Assign',
        disabled: true,
        disabledReason: 'Assignment controls are not implemented in the Angular MVP0 workflow.',
        mobileHidden: this.liveState().mobile
      });
    }

    if (task.capabilities.includes('changeTaskStatus')) {
      actions.push({
        id: 'changeStatus',
        label: 'Status',
        disabled: task.allowedTransitions.length === 0,
        disabledReason:
          task.allowedTransitions.length === 0
            ? 'No backend-provided transition is currently allowed.'
            : undefined,
        mobileHidden: this.liveState().mobile
      });
    }

    return actions.filter((action) => !action.mobileHidden);
  }

  private toDependencies(task: TaskMockRecord) {
    return task.dependencyIds
      .map((dependencyId) =>
        this.authorizedTasks().find((candidate) => candidate.id === dependencyId)
      )
      .filter((dependency): dependency is TaskMockRecord => dependency !== undefined)
      .map((dependency) => ({
        id: dependency.id,
        title: dependency.title,
        status: dependency.status
      }));
  }

  private replaceProjectTasks(projectId: string, projectTasks: readonly TaskMockRecord[]): void {
    this.liveState.update((state) => {
      const currentTasks = new Map(state.tasks.map((task) => [task.id, task]));
      const mergedTasks = projectTasks.map((task) => {
        const current = currentTasks.get(task.id);
        return task.brief === undefined && current?.brief !== undefined
          ? { ...task, brief: current.brief }
          : task;
      });
      return {
        ...state,
        tasks: [
          ...state.tasks.filter((task) => task.projectId !== projectId),
          ...mergedTasks
        ]
      };
    });
  }

  private reauthorizeActiveTaskDetail(): void {
    const activeTaskId = this.activeTaskId;
    const activeProjectId = this.activeProjectId;
    this.authorizationGeneration++;
    this.clearProtectedTaskState();
    this.liveState.set(this.emptyScenario('loading'));
    if (!activeTaskId || !activeProjectId || this.activeTaskId !== activeTaskId || this.activeProjectId !== activeProjectId) {return;}
    this.loadTaskDetail(activeTaskId);
  }

  private replaceProject(project: ProjectMockRecord): void {
    this.liveState.update((state) => {
      const exists = state.projects.some((candidate) => candidate.id === project.id);
      return {
        ...state,
        projects: exists
          ? state.projects.map((candidate) => (candidate.id === project.id ? project : candidate))
          : [...state.projects, project]
      };
    });
  }

  private replaceTask(task: TaskMockRecord): void {
    this.liveState.update((state) => {
      const exists = state.tasks.some((candidate) => candidate.id === task.id);
      return {
        ...state,
        tasks: exists
          ? state.tasks.map((candidate) => (candidate.id === task.id ? task : candidate))
          : [...state.tasks, task]
      };
    });
  }

}

function toFailureState(error: unknown, fallback: string): TaskMutationState {
  const normalized = normalizeApiError(error);
  const stale = normalized.code === 'TASK_STALE_VERSION' || normalized.details.some(detail => detail.code === 'TASK_STALE_VERSION');
  if (normalized.httpStatus === 409 || stale) {return { status: 'conflict', message: normalized.message || fallback, requestId: normalized.requestId };}
  if (normalized.httpStatus === 400 || normalized.httpStatus === 422) {return { status: 'validation', message: normalized.message || fallback, requestId: normalized.requestId };}
  if (normalized.httpStatus === 429) {return { status: 'rateLimited', message: normalized.message || fallback, requestId: normalized.requestId };}
  return { status: 'failure', message: normalized.message || fallback, requestId: normalized.requestId };
}

type AggregateApplyScope =
  | { readonly kind: 'initialLoad' }
  | { readonly kind: 'taskBodyReload' }
  | { readonly kind: 'sectionMutation'; readonly section: TaskDetailSection }
  | { readonly kind: 'sectionRecovery'; readonly section: TaskDetailSection }
  | { readonly kind: 'realtimeRefresh' };

function isProtectedSectionState(state: TaskDetailSectionState): boolean {
  return ['conflict', 'error', 'permissionDenied', 'submitting'].includes(state.status) || state.retryKind === 'page';
}

/** Keep a previously loaded aggregate page intact when this scope does not own it. */
function preservePagedSection<T>(
  current: PagedResponseDto<T> | null | undefined,
  incoming: PagedResponseDto<T> | null | undefined,
  preserve: boolean
): PagedResponseDto<T> | null | undefined {
  if (preserve && current) {return current;}
  if (!incoming) {return current;}
  if (!current) {return incoming;}
  const items = incoming.items ?? current.items ?? [];
  const uniqueItems = items.filter((item, index, all) => {
    const id = typeof (item as { id?: unknown }).id === 'string' ? (item as { id: string }).id : null;
    return !id || all.findIndex(candidate => (candidate as { id?: unknown }).id === id) === index;
  });
  return { ...incoming, items: uniqueItems, page: incoming.page ?? current.page, pageSize: incoming.pageSize ?? current.pageSize, totalCount: incoming.totalCount ?? uniqueItems.length, hasMore: incoming.hasMore ?? current.hasMore };
}

function toSectionFailure(error: unknown, fallback: string): TaskDetailSectionState {
  const normalized = normalizeApiError(error);
  if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {return { status: 'permissionDenied', message: 'Permission was denied. Protected task data was removed.', retryable: true, retryKind: 'authorization', requestId: normalized.requestId };}
  if (normalized.httpStatus === 409 || normalized.code === 'TASK_STALE_VERSION') {return { status: 'conflict', message: normalized.message || fallback, retryable: true, retryKind: 'aggregate', requestId: normalized.requestId };}
  return { status: 'error', message: normalized.message || fallback, retryable: true, requestId: normalized.requestId };
}

/** The label list endpoint can deny definition management without revoking task visibility. */
function isLabelDefinitionOnlyPermissionDenied(code: string): boolean {
  return code === 'TASK_LABEL_FORBIDDEN' || code === 'TASK_LABEL_DEFINITION_FORBIDDEN';
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}
