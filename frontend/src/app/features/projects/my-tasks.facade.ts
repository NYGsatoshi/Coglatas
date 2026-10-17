import { injectProjectsI18n } from '../../core/i18n/providers/projects-i18n';
import { projectFeedbackField, projectFeedbackText, projectFeedbackValue, projectFeedbackView, ProjectFeedbackString, normalizeProjectFeedbackError, projectPriorityFeedback } from './project-feedback';
import { HttpClient, HttpParams } from '@angular/common/http';
import { effect, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, forkJoin, of, Subscription } from 'rxjs';

import { FrontendApiError } from '../../core/api/api-error.model';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { NotificationOpenContextService } from '../../core/notifications/notification-open-context.service';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';
import { MyTasksCountsDto, MyTasksProjectionPageDto } from './projects.api';
import { mapMyTaskDtoToProjection, taskStageCategoryFromStatus } from './projects.mapper';
import {
  MyTasksCount,
  MyTasksFilterCondition,
  MyTasksFilters,
  MyTasksLiveTask,
  MyTasksSavedFilter,
  MyTasksSavedFilterSnapshot,
  MyTasksScope,
  MyTasksTab,
  MyTasksUrgencyGroup,
  MyTasksViewModel,
  PROJECTS_DEFAULT_PAGE_SIZE,
  PROJECTS_MAXIMUM_PAGE_SIZE,
  ProjectsPageStatus,
  ProjectsScenario,
  TaskGridRow,
  TaskRowAction
} from './projects.types';
import { SavedFiltersStatus, WorkViewPreferenceService } from './work-view-preference.service';

export const COGLATAS_MY_TASKS_MOCK = new InjectionToken<ProjectsScenario>('COGLATAS_MY_TASKS_MOCK');
export type MyTasksBuiltinFilter = 'running' | 'needsReview' | 'completed';

interface MyTasksState {
  readonly status: ProjectsPageStatus;
  readonly tasks: readonly MyTasksLiveTask[];
  readonly selectedTab: MyTasksTab;
  readonly scope: MyTasksScope;
  readonly workspaceId: string | null;
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly counts: readonly MyTasksCount[];
  readonly filters: MyTasksFilters;
  readonly projectFilterMasked: boolean;
  readonly savedFilters: readonly MyTasksSavedFilter[];
  readonly savedFiltersAvailable: boolean;
  readonly canPersistSavedFilters: boolean;
  readonly filterAnnouncement: string;
  readonly realtimeDegraded: boolean;
  readonly message?: string;
  readonly error?: FrontendApiError;
}

@Injectable({ providedIn: 'root' })
export class MyTasksFacade {
  private readonly i18n = injectProjectsI18n();
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeFacade);
  private readonly authSession = inject(AuthSessionFacade);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly workspaceSelection = inject(WorkspaceSelectionFacade);
  private readonly notificationOpenContext = inject(NotificationOpenContextService);
  private readonly workViewPreferences = inject(WorkViewPreferenceService);
  private readonly router = inject(Router, { optional: true });
  private readonly scenario = inject(COGLATAS_MY_TASKS_MOCK, { optional: true });
  private readonly state = signal<MyTasksState>(this.initialState());
  private hasRequested = false;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private requestSubscription: Subscription | null = null;
  private requestGeneration = 0;
  private preferenceIdentity: string | null = null;

  constructor() {
    this.realtime.durableEvents$.subscribe((event) => this.handleRealtimeEvent(event));
    this.realtime.registerProtectedStateClearer?.('my-tasks', () => this.clearProtectedState());
    this.realtime.registerCatchUp('my-tasks', () => {
      if (this.hasRequested && !this.scenario) {
        this.requestMyTasks();
      }
    });
    effect(() => {
      const session = this.authSession.session();
      const identity = session.status === 'active' && session.isAuthenticated && session.currentTenant?.isAvailable && !session.currentTenant.isPlatformScope
        ? `${session.currentTenant.tenantId}:${session.currentUser?.userId ?? ''}`
        : null;
      if (!identity || identity.endsWith(':')) {
        this.invalidateActiveRequest();
        this.cancelPendingSearch();
        this.preferenceIdentity = null;
        this.state.update((current) => ({
          ...current,
          ...this.resetFilterState(current),
          savedFilters: [],
          savedFiltersAvailable: false,
          canPersistSavedFilters: false,
          ...projectFeedbackField("filterAnnouncement", '')
        }));
        return;
      }
      if (identity === this.preferenceIdentity) {return;}
      const identityChanged = this.preferenceIdentity !== null;
      if (identityChanged) {
        this.invalidateActiveRequest();
        this.cancelPendingSearch();
      }
      this.preferenceIdentity = identity;
      const loaded = this.workViewPreferences.loadMyTasksSavedFilters();
      this.state.update((current) => ({
        ...current,
        ...(identityChanged ? this.resetFilterState(current) : {}),
        savedFilters: loaded.filters,
        savedFiltersAvailable: loaded.status === 'ready' || loaded.status === 'discarded',
        canPersistSavedFilters: loaded.status === 'ready' || loaded.status === 'discarded',
        ...projectFeedbackField("filterAnnouncement", savedFilterStatusAnnouncement(loaded.status))
      }));
    });
    effect(() => {
      const digestWorkspaceId = this.notificationOpenContext.digestWorkspaceId();
      if (!digestWorkspaceId) {return;}

      // The backend open response is the only source that can select this
      // Workspace. Consume it even when this root facade was already created
      // by another Project surface before the notification was opened.
      const selected = this.applyAuthorizedDigestWorkspace(digestWorkspaceId);
      this.notificationOpenContext.clear();
      if (selected && this.hasRequested && !this.scenario) {this.requestMyTasks();}
    });
    effect(() => {
      const workspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
      const current = this.state();
      if (this.scenario || current.scope !== 'currentWorkspace' || current.workspaceId === workspaceId) {return;}

      this.invalidateActiveRequest();
      this.state.set({
        ...current,
        workspaceId,
        page: 1,
        tasks: [],
        counts: [],
        totalCount: 0,
        status: 'loading',
        ...projectFeedbackField("message", workspaceId ? undefined : projectFeedbackText("projectFeedback.myTasks.waitingForAnActiveWorkspaceSelection")),
        error: undefined
      });
      if (this.hasRequested && workspaceId) {this.requestMyTasks();}
    });
  }

  load(): void {
    if (this.scenario || this.hasRequested) {return;}
    this.hasRequested = true;
    this.requestMyTasks();
  }

  retry(): void { if (!this.scenario) {this.requestMyTasks();} }
  refresh(): void { if (!this.scenario) {this.requestMyTasks();} }
  refreshIfLoaded(): void { if (!this.scenario && this.hasRequested) {this.requestMyTasks();} }

  setTab(tab: MyTasksTab): void {
    this.cancelPendingSearch();
    this.state.update((current) => ({ ...current, selectedTab: tab, page: 1, ...projectFeedbackField("filterAnnouncement", projectFeedbackText("projectFeedback.myTasks.filterApplied", { value0: relationshipLabel(tab) })) }));
    this.requestMyTasks();
  }

  setScope(scope: MyTasksScope): void {
    this.cancelPendingSearch();
    const workspaceId = scope === 'currentWorkspace' ? this.activeWorkspace.activeWorkspace()?.id ?? null : null;
    this.state.update((current) => ({
      ...current,
      scope,
      workspaceId,
      page: 1,
      tasks: [],
      counts: [],
      totalCount: 0,
      ...projectFeedbackField("filterAnnouncement", scope === 'allWorkspaces' ? projectFeedbackText("projectFeedback.myTasks.allWorkspacesScopeApplied") : projectFeedbackText("projectFeedback.myTasks.currentWorkspaceScopeApplied"))
    }));
    this.requestMyTasks();
  }

  setWorkspace(workspaceId: string): void {
    void this.selectWorkspaceAndReturnToTasks(workspaceId);
  }

  private async selectWorkspaceAndReturnToTasks(workspaceId: string): Promise<void> {
    const selected = await this.workspaceSelection.selectWorkspace(workspaceId);
    if (
      !selected ||
      this.workspaceSelection.selection().workspaceId !== workspaceId ||
      !this.router
    ) {
      return;
    }

    const transitionRevision = this.workspaceSelection.transitionRevision();
    // Selection first neutralizes the old Workspace-scoped route so its
    // component cannot survive under the new authorization context. This
    // page-level scope control can then safely remount My Tasks for the newly
    // selected Workspace. A canceled target navigation leaves the neutral
    // Workspace dashboard in place rather than restoring stale route state.
    try {
      const navigated = await this.router.navigateByUrl('/tasks');
      if (!navigated) {
        return;
      }

      if (
        this.workspaceSelection.selection().workspaceId !== workspaceId ||
        this.workspaceSelection.transitionRevision() !== transitionRevision
      ) {
        // A newer authorization or Workspace transition superseded this
        // page-owned continuation while navigation was in flight. Repair only
        // the route this operation just landed; a newer, different route owns
        // its own navigation outcome.
        if (this.router.url === '/tasks') {
          await this.router.navigateByUrl('/workspaces');
        }
      }
    } catch {
      // The neutral route is the fail-closed fallback.
    }
  }

  private applyAuthorizedDigestWorkspace(workspaceId: string): boolean {
    // RightPanel completes the neutralize -> activate transaction before
    // publishing this one-shot digest context. My Tasks may consume that
    // context, but must never perform a second synchronous scope switch.
    if (this.workspaceSelection.selection().workspaceId !== workspaceId) {
      this.clearProtectedState();
      return false;
    }
    this.invalidateActiveRequest();
    this.state.update((current) => ({
      ...current,
      scope: 'currentWorkspace',
      workspaceId,
      page: 1,
      tasks: [],
      counts: [],
      totalCount: 0
    }));
    return true;
  }

  setProjectFilter(projectId: string): void { this.updateFilter({ projectId: projectId.trim() }, false); }
  setStageCategoryFilter(stageCategory: MyTasksFilters['stageCategory']): void { this.updateFilter({ stageCategory }); }
  setPriorityFilter(priority: MyTasksFilters['priority']): void { this.updateFilter({ priority }); }
  setBlockedFilter(blocked: MyTasksFilters['blocked']): void { this.updateFilter({ blocked }); }
  setTimeGroupFilter(timeGroup: MyTasksUrgencyGroup | null): void { this.updateFilter({ timeGroup }); }

  setSearchFilter(search: string): void {
    this.state.update((current) => ({ ...current, filters: { ...current.filters, search }, page: 1 }));
    this.cancelPendingSearch();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.requestMyTasks();
    }, 300);
  }

  applyBuiltinFilter(filter: MyTasksBuiltinFilter): void {
    const current = this.state();
    const mapping: Record<MyTasksBuiltinFilter, { readonly selectedTab: MyTasksTab; readonly stageCategory: MyTasksFilters['stageCategory']; readonly label: string }> = {
      running: { selectedTab: 'assigned', stageCategory: 'inProgress', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.running")) },
      needsReview: { selectedTab: 'reviews', stageCategory: 'review', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.needsReview")) },
      completed: { selectedTab: 'completed', stageCategory: 'done', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.completed")) }
    };
    const preset = mapping[filter];
    this.applyFilterSnapshot(
      { ...current.filters, selectedTab: preset.selectedTab, stageCategory: preset.stageCategory },
      projectFeedbackText("projectFeedback.myTasks.presetApplied", { value0: projectFeedbackValue(preset, "label") }),
      current.projectFilterMasked
    );
  }

  saveCurrentFilter(name: string): boolean {
    const current = this.state();
    const result = this.workViewPreferences.saveMyTasksFilter(name, {
      ...current.filters,
      selectedTab: current.selectedTab
    });
    if (result.status !== 'ready') {
      this.state.update((state) => ({
        ...state,
        canPersistSavedFilters: result.status === 'storageUnavailable' || result.status === 'identityUnavailable'
          ? false
          : state.canPersistSavedFilters,
        ...projectFeedbackField("filterAnnouncement", savedFilterMutationAnnouncement(result.status, 'save'))
      }));
      return false;
    }
    const normalizedName = name.trim();
    this.state.update((state) => ({
      ...state,
      savedFilters: result.filters,
      savedFiltersAvailable: true,
      canPersistSavedFilters: true,
      ...projectFeedbackField("filterAnnouncement", projectFeedbackText("projectFeedback.myTasks.savedFilter", { value0: normalizedName }))
    }));
    return true;
  }

  applySavedFilter(filterId: string): void {
    const filter = this.state().savedFilters.find((candidate) => candidate.id === filterId);
    if (!filter) {
      this.state.update((current) => ({ ...current, ...projectFeedbackField("filterAnnouncement", projectFeedbackText("projectFeedback.myTasks.thatSavedFilterIsNoLongerAvailable")) }));
      return;
    }
    this.applyFilterSnapshot(filter.snapshot, projectFeedbackText("projectFeedback.myTasks.savedFilterApplied", { value0: filter.name }), filter.snapshot.projectId.length > 0);
  }

  deleteSavedFilter(filterId: string): boolean {
    const current = this.state();
    const filter = current.savedFilters.find((candidate) => candidate.id === filterId);
    if (!filter) {return false;}
    const result = this.workViewPreferences.deleteMyTasksFilter(filterId);
    if (result.status !== 'ready') {
      this.state.update((state) => ({
        ...state,
        canPersistSavedFilters: result.status === 'storageUnavailable' || result.status === 'identityUnavailable'
          ? false
          : state.canPersistSavedFilters,
        ...projectFeedbackField("filterAnnouncement", savedFilterMutationAnnouncement(result.status, 'delete'))
      }));
      return false;
    }
    this.state.update((state) => ({
      ...state,
      savedFilters: result.filters,
      canPersistSavedFilters: true,
      ...projectFeedbackField("filterAnnouncement", projectFeedbackText("projectFeedback.myTasks.deletedSavedFilter", { value0: filter.name }))
    }));
    return true;
  }

  clearAllFilters(): void {
    this.cancelPendingSearch();
    this.state.update((current) => ({
      ...current,
      selectedTab: 'assigned',
      filters: emptyFilters(),
      projectFilterMasked: false,
      page: 1,
      ...projectFeedbackField("filterAnnouncement", projectFeedbackText("projectFeedback.myTasks.allOptionalFiltersClearedAssignedToMeIsActive"))
    }));
    this.requestMyTasks();
  }

  previousPage(): void {
    const current = this.state();
    if (current.page <= 1) {return;}
    this.state.set({ ...current, page: current.page - 1 });
    this.requestMyTasks();
  }

  nextPage(): void {
    const current = this.state();
    const lastPage = Math.max(1, Math.ceil(current.totalCount / current.pageSize));
    if (current.page >= lastPage) {return;}
    this.state.set({ ...current, page: current.page + 1 });
    this.requestMyTasks();
  }

  setPageSize(pageSize: number): void {
    const bounded = Math.min(PROJECTS_MAXIMUM_PAGE_SIZE, Math.max(1, Math.trunc(pageSize)));
    this.state.update((current) => ({ ...current, page: 1, pageSize: bounded }));
    this.requestMyTasks();
  }

  getMyTasks(): MyTasksViewModel { return projectFeedbackView(this.i18n, this.rawMyTasks()); }

  private rawMyTasks(): MyTasksViewModel {
    const current = this.state();
    return {
      status: current.status,
      ...projectFeedbackField("title", projectFeedbackText("projectFeedback.projects.myTasks")),
      ...projectFeedbackField("subtitle", current.scope === 'allWorkspaces' ? projectFeedbackText("projectFeedback.myTasks.authorizedTasksAcrossAllWorkspaces") : projectFeedbackText("projectFeedback.myTasks.tasksInTheSelectedWorkspace")),
      rows: current.status === 'ready' ? current.tasks.map((task) => this.toTaskRow(task)) : [],
      columns: [],
      pageSize: { defaultPageSize: PROJECTS_DEFAULT_PAGE_SIZE, maximumPageSize: PROJECTS_MAXIMUM_PAGE_SIZE },
      ...projectFeedbackField("message", projectFeedbackValue(current, "message")),
      error: current.error,
      tasks: current.tasks,
      selectedTab: current.selectedTab,
      scope: current.scope,
      workspaceId: current.workspaceId,
      workspaceOptions: this.authSession.session().currentUser?.workspaces ?? [],
      counts: current.counts,
      totalCount: current.totalCount,
      page: current.page,
      selectedPageSize: current.pageSize,
      lastPage: Math.max(1, Math.ceil(current.totalCount / current.pageSize)),
      filters: current.filters,
      projectFilterInputValue: current.projectFilterMasked ? '' : current.filters.projectId,
      savedFilters: current.savedFilters,
      savedFiltersAvailable: current.savedFiltersAvailable,
      canPersistSavedFilters: current.canPersistSavedFilters,
      filterConditions: filterConditions(current.selectedTab, current.filters),
      ...projectFeedbackField("filterAnnouncement", projectFeedbackValue(current, "filterAnnouncement")),
      realtimeDegraded: this.realtime.connectionState() !== 'Connected'
    };
  }

  private requestMyTasks(): void {
    if (this.scenario) {return;}
    const current = this.state();
    if (current.scope === 'currentWorkspace' && !current.workspaceId) {
      this.invalidateActiveRequest();
      this.state.set({
        ...current,
        status: 'loading',
        tasks: [],
        counts: [],
        totalCount: 0,
        ...projectFeedbackField("message", projectFeedbackText("projectFeedback.myTasks.waitingForAnActiveWorkspaceSelection")),
        error: undefined
      });
      return;
    }

    this.requestSubscription?.unsubscribe();
    const generation = ++this.requestGeneration;
    this.state.set({ ...current, status: 'loading', tasks: [], counts: [], ...projectFeedbackField("message", undefined), error: undefined });
    const params = this.queryParams(current);
    this.requestSubscription = forkJoin({
      page: this.http.get<MyTasksProjectionPageDto>('/api/me/tasks', { params, withCredentials: true }),
      counts: this.http.get<MyTasksCountsDto>('/api/me/tasks/counts', { params, withCredentials: true })
    }).pipe(catchError((error: unknown) => of({ error }))).subscribe((response) => {
      if (generation !== this.requestGeneration) {return;}
      if ('error' in response) {
        this.state.update((latest) => this.toErrorState(response.error, latest));
        return;
      }
      try {
        const tasks = (response.page.items ?? []).map((task) => mapMyTaskDtoToProjection(task));
        this.state.update((latest) => ({
          ...latest,
          status: tasks.length > 0 ? 'ready' : 'empty',
          tasks,
          page: numeric(response.page.page, current.page),
          pageSize: numeric(response.page.pageSize, current.pageSize),
          totalCount: numeric(response.page.totalCount, 0),
          counts: toCounts(response.counts),
          realtimeDegraded: false,
          ...projectFeedbackField("message", tasks.length > 0 ? undefined : projectFeedbackText("projectFeedback.myTasks.noTasksMatchThisRelationshipViewAndScope")),
          error: undefined
        }));
      } catch (error: unknown) {
        this.state.update((latest) => this.toErrorState(error, latest));
      }
    });
  }

  private queryParams(state: MyTasksState): HttpParams {
    let params = new HttpParams()
      .set('view', state.selectedTab)
      .set('scope', state.scope)
      .set('page', String(state.page))
      .set('pageSize', String(state.pageSize));
    if (state.scope === 'currentWorkspace' && state.workspaceId) {params = params.set('workspaceId', state.workspaceId);}
    if (state.filters.projectId) {params = params.set('projectId', state.filters.projectId);}
    if (state.filters.stageCategory) {params = params.set('stageCategory', state.filters.stageCategory);}
    if (state.filters.priority) {params = params.set('priority', state.filters.priority);}
    if (state.filters.blocked) {params = params.set('blocked', state.filters.blocked);}
    if (state.filters.timeGroup) {params = params.set('timeGroup', state.filters.timeGroup);}
    if (state.filters.search.trim()) {params = params.set('search', state.filters.search.trim());}
    return params;
  }

  private handleRealtimeEvent(event: DurableRealtimeEvent): void {
    if (this.scenario || !this.hasRequested) {return;}
    if (event.eventType === 'Security.AuthorizationStateChanged.v1') {
      this.clearProtectedState();
      return;
    }
    if (![
      'Projects.TaskChanged.v1',
      'Projects.TaskAssignmentChanged.v1',
      'Projects.TaskWorkflowChanged.v1',
      'Projects.TaskCommentChanged.v1',
      'Projects.ProjectChanged.v1',
      'Security.AuthorizationStateChanged.v1'
    ].includes(event.eventType)) {return;}
    if (this.refreshTimer !== null) {return;}
    this.refreshTimer = setTimeout(() => { this.refreshTimer = null; this.requestMyTasks(); }, 150);
  }

  private toErrorState(error: unknown, current: MyTasksState): MyTasksState {
    const normalized = normalizeProjectFeedbackError(error);
    if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {
      return { ...current, status: 'permissionDenied', tasks: [], counts: [], totalCount: 0, ...projectFeedbackField("message", projectFeedbackText("projectFeedback.myTasks.authenticationOrWorkspacePermissionIsRequired")), error: normalized };
    }
    if (normalized.code === 'MY_TASKS_INVALID_WORKSPACE_SCOPE') {
      return { ...current, status: 'error', tasks: [], counts: [], totalCount: 0, ...projectFeedbackField("message", projectFeedbackText("projectFeedback.myTasks.selectAnActiveWorkspaceAndTryAgain")), error: normalized };
    }
    if (normalized.httpStatus === 404) {
      return { ...current, status: 'error', tasks: [], counts: [], totalCount: 0, ...projectFeedbackField("message", projectFeedbackText("projectFeedback.myTasks.theSelectedProjectIsNotAvailable")), error: normalized };
    }
    if (normalized.httpStatus === 0) {
      return { ...current, status: 'error', tasks: [], counts: [], totalCount: 0, ...projectFeedbackField("message", projectFeedbackText("projectFeedback.myTasks.theNetworkIsUnavailableTryTheManualRefreshWhenConnectivity")), error: normalized };
    }
    return { ...current, status: 'error', tasks: [], counts: [], totalCount: 0, ...projectFeedbackField("message", projectFeedbackText("projectFeedback.myTasks.myTasksCouldNotBeLoadedTryAgain")), error: normalized };
  }

  private toTaskRow(task: MyTasksLiveTask): TaskGridRow {
    return {
      id: task.taskId, projectId: task.projectId, title: task.title,
      project: task.projectTitle, status: task.status, statusLabel: task.workflowStageName,
      workflowStageId: task.workflowStageId, workflowStageName: task.workflowStageName,
      stageCategory: task.stageCategory, isBlocked: task.isBlocked,
      priority: task.priority, ...projectFeedbackField("priorityLabel", projectPriorityFeedback(task.priority)),
      assignee: task.primaryAssignee, startDate: '', dueDate: task.deadlineAt || task.plannedEndDate,
      progressPercent: task.progressPercent, milestone: '', allowedTransitions: [], rowActions: this.buildActions()
    };
  }

  private buildActions(): readonly TaskRowAction[] { return [{ id: 'openDetail', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.projectDetail.open")), disabled: false }]; }

  private initialState(): MyTasksState {
    return {
      status: this.scenario ? (this.scenario.myTasksStatus ?? this.scenario.status) : 'loading',
      tasks: this.scenario ? scenarioTasks(this.scenario) : [], selectedTab: 'assigned', scope: 'currentWorkspace',
      workspaceId: this.activeWorkspace.activeWorkspace()?.id ?? null,
      page: 1, pageSize: PROJECTS_DEFAULT_PAGE_SIZE, totalCount: 0, counts: [], realtimeDegraded: false,
      filters: emptyFilters(), projectFilterMasked: false, savedFilters: [], savedFiltersAvailable: false, canPersistSavedFilters: false, ...projectFeedbackField("filterAnnouncement", ''),
      ...projectFeedbackField("message", this.scenario?.myTasksMessage ?? projectFeedbackValue(this.scenario, "message")), error: this.scenario?.myTasksError
    };
  }

  private updateFilter(patch: Partial<MyTasksFilters>, preserveProjectMask = true): void {
    this.cancelPendingSearch();
    this.state.update((current) => ({
      ...current,
      filters: { ...current.filters, ...patch },
      projectFilterMasked: preserveProjectMask ? current.projectFilterMasked : false,
      page: 1
    }));
    this.requestMyTasks();
  }

  private applyFilterSnapshot(snapshot: MyTasksSavedFilterSnapshot, announcement: ProjectFeedbackString, projectFilterMasked: boolean): void {
    this.cancelPendingSearch();
    this.state.update((current) => ({
      ...current,
      selectedTab: snapshot.selectedTab,
      filters: {
        projectId: snapshot.projectId,
        stageCategory: snapshot.stageCategory,
        priority: snapshot.priority,
        blocked: snapshot.blocked,
        search: snapshot.search,
        timeGroup: snapshot.timeGroup
      },
      projectFilterMasked,
      page: 1,
      ...projectFeedbackField("filterAnnouncement", announcement)
    }));
    this.requestMyTasks();
  }

  private cancelPendingSearch(): void {
    if (this.searchTimer === null) {return;}
    clearTimeout(this.searchTimer);
    this.searchTimer = null;
  }

  private resetFilterState(current: MyTasksState): Partial<MyTasksState> {
    return {
      selectedTab: 'assigned',
      filters: emptyFilters(),
      projectFilterMasked: false,
      page: 1,
      tasks: [],
      counts: [],
      totalCount: 0,
      status: current.workspaceId ? 'loading' : current.status
    };
  }

  private invalidateActiveRequest(): void {
    this.requestGeneration++;
    this.requestSubscription?.unsubscribe();
    this.requestSubscription = null;
  }

  private clearProtectedState(): void {
    const current = this.state();
    this.invalidateActiveRequest();
    if (this.refreshTimer !== null) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
    this.cancelPendingSearch();
    this.state.set({
      ...current,
      selectedTab: 'assigned',
      filters: emptyFilters(),
      projectFilterMasked: false,
      scope: 'currentWorkspace',
      workspaceId: null,
      page: 1,
      tasks: [],
      counts: [],
      totalCount: 0,
      status: 'loading',
      ...projectFeedbackField("message", projectFeedbackText("projectFeedback.myTasks.waitingForAnActiveWorkspaceSelection")),
      error: undefined,
      ...projectFeedbackField("filterAnnouncement", ''),
      savedFilters: current.savedFilters,
      savedFiltersAvailable: current.savedFiltersAvailable,
      canPersistSavedFilters: current.canPersistSavedFilters
    });
  }
}

function numeric(value: unknown, fallback: number): number { return typeof value === 'number' && Number.isFinite(value) ? value : fallback; }

function emptyFilters(): MyTasksFilters {
  return { projectId: '', stageCategory: '', priority: '', blocked: '', search: '', timeGroup: null };
}

function filterConditions(selectedTab: MyTasksTab, filters: MyTasksFilters): readonly MyTasksFilterCondition[] {
  const conditions: MyTasksFilterCondition[] = [{ id: 'relationship', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.relationship", { value0: relationshipLabel(selectedTab) })) }];
  if (filters.projectId) {conditions.push({ id: 'project', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.projectFilterActive")) });}
  if (filters.stageCategory) {conditions.push({ id: 'stage', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.stage", { value0: stageCategoryLabel(filters.stageCategory) })) });}
  if (filters.priority) {conditions.push({ id: 'priority', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.priority", { value0: projectPriorityFeedback(filters.priority) })) });}
  if (filters.blocked) {conditions.push({ id: 'blocked', ...projectFeedbackField("label", filters.blocked === 'true' ? projectFeedbackText("projectFeedback.myTasks.blocked") : projectFeedbackText("projectFeedback.myTasks.notBlocked")) });}
  if (filters.timeGroup) {conditions.push({ id: 'urgency', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.urgency", { value0: urgencyLabel(filters.timeGroup) })) });}
  if (filters.search.trim()) {conditions.push({ id: 'search', ...projectFeedbackField("label", projectFeedbackText("projectFeedback.myTasks.search", { value0: filters.search.trim() })) });}
  return conditions;
}

function relationshipLabel(tab: MyTasksTab): ProjectFeedbackString {
  const labels: Record<MyTasksTab, ProjectFeedbackString> = {
    assigned: projectFeedbackText("projectFeedback.myTasks.assignedToMe"),
    participating: projectFeedbackText("projectFeedback.myTasks.participating"),
    reviews: projectFeedbackText("projectFeedback.myTasks.reviews"),
    created: projectFeedbackText("projectFeedback.myTasks.createdByMe"),
    watching: projectFeedbackText("projectFeedback.myTasks.watching"),
    teamQueue: projectFeedbackText("projectFeedback.myTasks.teamQueue"),
    completed: projectFeedbackText("projectFeedback.myTasks.completed")
  };
  return labels[tab];
}

function stageCategoryLabel(stage: Exclude<MyTasksFilters['stageCategory'], ''>): ProjectFeedbackString {
  const labels: Record<Exclude<MyTasksFilters['stageCategory'], ''>, ProjectFeedbackString> = {
    backlog: projectFeedbackText("projectFeedback.myTasks.backlog"), todo: projectFeedbackText("projectFeedback.myTasks.todo"), inProgress: projectFeedbackText("projectFeedback.myTasks.inProgress"), review: projectFeedbackText("projectFeedback.myTasks.review"), done: projectFeedbackText("projectFeedback.myTasks.done"), cancelled: projectFeedbackText("projectFeedback.myTasks.cancelled")
  };
  return labels[stage];
}

function urgencyLabel(group: MyTasksUrgencyGroup): ProjectFeedbackString {
  const labels: Record<MyTasksUrgencyGroup, ProjectFeedbackString> = {
    overdue: projectFeedbackText("projectFeedback.myTasks.overdue"), today: projectFeedbackText("projectFeedback.myTasks.today"), next7Days: projectFeedbackText("projectFeedback.myTasks.next7Days"), later: projectFeedbackText("projectFeedback.myTasks.later"), noDeadline: projectFeedbackText("projectFeedback.myTasks.noDeadline")
  };
  return labels[group];
}

function capitalize(value: string): string { return value[0].toUpperCase() + value.slice(1); }

function savedFilterStatusAnnouncement(status: SavedFiltersStatus): ProjectFeedbackString {
  if (status === 'storageUnavailable') {return projectFeedbackText("projectFeedback.myTasks.savedFiltersAreUnavailableInThisBrowserCurrentFiltersStill");}
  if (status === 'discarded') {return projectFeedbackText("projectFeedback.myTasks.anInvalidSavedFilterRecordWasDiscarded");}
  return '';
}

function savedFilterMutationAnnouncement(status: SavedFiltersStatus, operation: 'save' | 'delete'): ProjectFeedbackString {
  if (status === 'storageUnavailable') {return projectFeedbackText("projectFeedback.myTasks.couldNotTheSavedFilterBecauseBrowserStorageIsUnavailable", { value0: projectFeedbackText(operation === 'save' ? 'projectFeedback.operation.save' : 'projectFeedback.operation.delete') });}
  if (status === 'identityUnavailable') {return projectFeedbackText("projectFeedback.myTasks.couldNotTheSavedFilterUntilTheAuthenticatedTenantAnd", { value0: projectFeedbackText(operation === 'save' ? 'projectFeedback.operation.save' : 'projectFeedback.operation.delete') });}
  if (status === 'invalidInput') {return operation === 'save' ? projectFeedbackText("projectFeedback.myTasks.enterAUniqueSavedFilterNameOf80CharactersOr") : projectFeedbackText("projectFeedback.myTasks.thatSavedFilterIsNoLongerAvailable");}
  if (status === 'discarded') {return projectFeedbackText("projectFeedback.myTasks.anInvalidSavedFilterRecordWasDiscarded");}
  return '';
}

function toCounts(dto: MyTasksCountsDto): readonly MyTasksCount[] {
  const views = Array.isArray(dto.views) ? dto.views.map((item) => ({ key: String(item.view).replace(/^./, (value) => value.toLowerCase()) as MyTasksTab, count: numeric(item.count, 0) })) : [];
  const groups = Array.isArray(dto.timeGroups) ? dto.timeGroups.map((item) => ({ key: String(item.timeGroup).replace(/^./, (value) => value.toLowerCase()) as MyTasksUrgencyGroup, count: numeric(item.count, 0) })) : [];
  return [...views, ...groups];
}

// Stories and static UI tests may still inject their pre-PR04 scenario object. This
// adapter is deliberately confined to the optional test token; production HTTP state
// is parsed only through mapMyTaskDtoToProjection.
function scenarioTasks(scenario: ProjectsScenario): readonly MyTasksLiveTask[] {
  const tasks = scenario.myTasks ?? scenario.tasks;
  return tasks.map((task) => ({
    taskId: task.id, tenantId: 'scenario-tenant', workspaceId: 'scenario-workspace', workspaceTitle: 'Scenario workspace',
    projectId: task.projectId, projectTitle: task.milestone || 'Project', title: task.title, workflowStageId: null,
    workflowStageName: task.statusLabel, stageCategory: taskStageCategoryFromStatus(task.status),
    status: task.status, priority: task.priority, isBlocked: task.status === 'blocked',
    plannedEndDate: task.dueDate, deadlineAt: '', progressPercent: task.progressPercent ?? 0, timeGroup: 'noDeadline',
    isOverdue: false, version: task.rowVersion || 'scenario', primaryAssignee: task.assignee, targetGroup: '', reviewer: '', labels: [],
    checklistCompletedCount: 0, checklistTotalCount: 0, canClaim: false, canChangeStage: false, warnings: []
  }));
}
