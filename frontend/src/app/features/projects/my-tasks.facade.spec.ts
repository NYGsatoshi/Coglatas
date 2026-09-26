import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { COGLATAS_AUTH_SESSION_MOCK, AuthSessionFacade, AuthSessionSnapshot, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { NotificationOpenContextService } from '../../core/notifications/notification-open-context.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';
import { MyTasksFacade } from './my-tasks.facade';

const task = {
  taskId: 'task-2', tenantId: 'tenant-1', workspaceId: 'workspace-1', workspaceTitle: 'Backend workspace',
  projectId: 'project-1', projectTitle: 'Backend Project', title: 'Assigned Backend Task',
  workflowStageName: 'Product backlog', stageCategory: 'Backlog', priority: 'Medium', isBlocked: true,
  progressPercent: 0, timeGroup: 'Today', isOverdue: false, version: 1,
  checklistCompletedCount: 0, checklistTotalCount: 0, labels: [], quickEditPermissions: { canClaim: false, canChangeStage: true }, warnings: []
};

describe('MyTasksFacade', () => {
  let facade: MyTasksFacade;
  let httpMock: HttpTestingController;
  let activeWorkspace: ActiveWorkspaceFacade;
  let router: { url: string; navigateByUrl: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    localStorage.clear();
    router = {
      url: '/workspaces',
      navigateByUrl: vi.fn(async (url: string) => {
        router.url = url;
        return true;
      }),
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // The HTTP projection is valid only for an authenticated session. A
        // disabled realtime transport must not be modeled as a logout.
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: Router, useValue: router },
      ],
    });
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    TestBed.inject(WorkspaceSelectionFacade).reconcileAuthorizedWorkspaces(
      [
        { id: 'workspace-1', label: 'Workspace one' },
        { id: 'workspace-2', label: 'Workspace two' },
      ],
      { tenantId: 'tenant-1', userId: 'user-1' },
      'workspace-1',
    );
    facade = TestBed.inject(MyTasksFacade);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    httpMock.verify();
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('loads the canonical projection and counts using an explicit relationship view', () => {
    facade.load();
    flush({ items: [task], page: 1, pageSize: 50, totalCount: 1 }, { views: [{ view: 'Assigned', count: 1 }], timeGroups: [{ timeGroup: 'Today', count: 1 }] });

    const page = facade.getMyTasks();
    expect(page.status).toBe('ready');
    expect(page.tasks[0].workspaceTitle).toBe('Backend workspace');
    expect(page.tasks[0].stageCategory).toBe('backlog');
    expect(page.rows[0]).toEqual(expect.objectContaining({
      stageCategory: 'backlog',
      isBlocked: true,
      workflowStageName: 'Product backlog'
    }));
    expect(page.rows[0].hasArtifact).toBeUndefined();
    expect(page.counts.find((item) => item.key === 'assigned')?.count).toBe(1);
    expect(page.counts.find((item) => item.key === 'today')?.count).toBe(1);
  });

  it.each(['success', 'error'] as const)('preserves asynchronously hydrated saved filters after a delayed initial %s response', (outcome) => {
    localStorage.setItem('coglatas.work-view.saved-filters.v1:mock-tenant:mock-user-a:my-tasks', JSON.stringify({
      version: 1,
      filters: [{
        id: 'saved-12345678', name: 'Hydrated view',
        snapshot: { selectedTab: 'completed', projectId: '', stageCategory: 'done', priority: '', blocked: '', search: '', timeGroup: null }
      }]
    }));
    facade.load();
    const requests = pendingProjectionRequests();

    TestBed.flushEffects();
    expect(facade.getMyTasks().savedFilters.map((filter) => filter.name)).toEqual(['Hydrated view']);
    expect(facade.getMyTasks().savedFiltersAvailable).toBe(true);
    expect(facade.getMyTasks().canPersistSavedFilters).toBe(true);

    if (outcome === 'success') {
      completeProjectionRequests(requests);
    } else {
      requests.find((request) => request.request.url === '/api/me/tasks')!.flush(
        { error: { code: 'MY_TASKS_PROJECT_NOT_FOUND', message: 'Project is unavailable.' } },
        { status: 404, statusText: 'Not Found' }
      );
      expect(requests.find((request) => request.request.url === '/api/me/tasks/counts')?.cancelled).toBe(true);
    }

    expect(facade.getMyTasks().savedFilters.map((filter) => filter.name)).toEqual(['Hydrated view']);
    expect(facade.getMyTasks().savedFiltersAvailable).toBe(true);
    expect(facade.getMyTasks().canPersistSavedFilters).toBe(true);
  });

  it('cancels/replaces the active query when the relationship tab changes', () => {
    facade.load();
    flush({ items: [], page: 1, pageSize: 50, totalCount: 0 }, { views: [], timeGroups: [] });
    facade.setTab('watching');
    const requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests).toHaveLength(2);
    expect(requests[0].request.params.get('view')).toBe('watching');
    requests.find((request) => request.request.url === '/api/me/tasks')!.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    requests.find((request) => request.request.url === '/api/me/tasks/counts')!.flush({ views: [], timeGroups: [] });
  });

  it('rejects incomplete canonical rows rather than falling back to a legacy mock record', () => {
    facade.load();
    flush({ items: [{ taskId: 'task-1', title: 'Incomplete' }], page: 1, pageSize: 50, totalCount: 1 }, { views: [], timeGroups: [] });
    expect(facade.getMyTasks().status).toBe('error');
  });

  it('does not issue the current-workspace request until an explicit active workspace is available', () => {
    activeWorkspace.clearWorkspace();
    TestBed.flushEffects();
    facade.load();
    httpMock.expectNone('/api/me/tasks');
    httpMock.expectNone('/api/me/tasks/counts');

    activeWorkspace.setActiveWorkspace({ id: 'workspace-explicit', label: 'Explicit workspace' });
    TestBed.flushEffects();
    const requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests).toHaveLength(2);
    expect(requests.every((request) => request.request.params.get('workspaceId') === 'workspace-explicit')).toBe(true);
    requests[0].flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    requests[1].flush({ views: [], timeGroups: [] });
  });

  it('maps filters and server paging into canonical query parameters and resets the page', () => {
    facade.load();
    flush({ items: [], page: 1, pageSize: 50, totalCount: 60 }, { views: [], timeGroups: [] });

    facade.nextPage();
    let requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests[0].request.params.get('page')).toBe('2');
    requests[0].flush({ items: [], page: 2, pageSize: 50, totalCount: 60 });
    requests[1].flush({ views: [], timeGroups: [] });

    facade.setPriorityFilter('critical');
    requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests[0].request.params.get('page')).toBe('1');
    expect(requests[0].request.params.get('priority')).toBe('critical');
    requests[0].flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    requests[1].flush({ views: [], timeGroups: [] });
  });

  it('clears and cancels the prior workspace request before loading the newly selected workspace', () => {
    facade.load();
    const prior = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');

    facade.setWorkspace('workspace-2');
    TestBed.flushEffects();

    expect(prior.every((request) => request.cancelled)).toBe(true);
    expect(facade.getMyTasks().tasks).toEqual([]);
    expect(facade.getMyTasks().counts).toEqual([]);
    expect(facade.getMyTasks().page).toBe(1);
    const current = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(current).toHaveLength(2);
    expect(current.every((request) => request.request.params.get('workspaceId') === 'workspace-2')).toBe(true);
    current.find((request) => request.request.url === '/api/me/tasks')!
      .flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    current.find((request) => request.request.url === '/api/me/tasks/counts')!
      .flush({ views: [], timeGroups: [] });
  });

  it('returns the page-level Workspace control to My Tasks only after a safe neutral transition', async () => {
    router.url = '/tasks';

    facade.setWorkspace('workspace-2');

    await vi.waitFor(() => expect(router.navigateByUrl).toHaveBeenCalledTimes(2));
    expect(router.navigateByUrl).toHaveBeenNthCalledWith(1, '/workspaces');
    expect(router.navigateByUrl).toHaveBeenNthCalledWith(2, '/tasks');
    expect(activeWorkspace.activeWorkspace()?.id).toBe('workspace-2');
    expect(facade.getMyTasks().workspaceId).toBe('workspace-2');
  });

  it('leaves the neutral Workspace route in place when returning to My Tasks is canceled', async () => {
    router.url = '/tasks';
    router.navigateByUrl = vi.fn(async (url: string) => {
      if (url === '/workspaces') {
        router.url = url;
        return true;
      }
      return false;
    });

    facade.setWorkspace('workspace-2');

    await vi.waitFor(() => expect(router.navigateByUrl).toHaveBeenCalledTimes(2));
    expect(router.url).toBe('/workspaces');
    expect(activeWorkspace.activeWorkspace()?.id).toBe('workspace-2');
    expect(facade.getMyTasks().workspaceId).toBe('workspace-2');
  });

  it('repairs a stale My Tasks return when a newer Workspace wins during navigation', async () => {
    router.url = '/tasks';
    let resolveTaskNavigation!: (navigated: boolean) => void;
    const taskNavigation = new Promise<boolean>((resolve) => {
      resolveTaskNavigation = resolve;
    });
    router.navigateByUrl = vi.fn(async (url: string) => {
      if (url === '/workspaces') {
        router.url = url;
        return true;
      }

      const navigated = await taskNavigation;
      if (navigated) {
        router.url = url;
      }
      return navigated;
    });

    facade.setWorkspace('workspace-2');
    await vi.waitFor(() => expect(router.navigateByUrl).toHaveBeenCalledTimes(2));

    await TestBed.inject(WorkspaceSelectionFacade).selectWorkspace('workspace-1');
    resolveTaskNavigation(true);

    await vi.waitFor(() => expect(router.navigateByUrl).toHaveBeenCalledTimes(3));
    expect(router.navigateByUrl).toHaveBeenNthCalledWith(3, '/workspaces');
    expect(router.url).toBe('/workspaces');
    expect(activeWorkspace.activeWorkspace()?.id).toBe('workspace-1');
  });

  it('DigestOpenAppliesWorkspaceSpecificMyTasksContextAfterFacadeAlreadyExists', async () => {
    const context = TestBed.inject(NotificationOpenContextService);
    const selection = TestBed.inject(WorkspaceSelectionFacade);

    await selection.selectWorkspace('workspace-2');
    context.setDigestWorkspace('workspace-2');
    TestBed.flushEffects();

    expect(activeWorkspace.activeWorkspace()?.id).toBe('workspace-2');
    expect(facade.getMyTasks().workspaceId).toBe('workspace-2');
    expect(context.takeDigestWorkspace()).toBeNull();
  });

  it('clears protected rows, scope IDs, and active HTTP before a new Workspace is selected', () => {
    facade.load();
    const prior = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');

    TestBed.inject(RealtimeFacade).clearForWorkspaceBoundary();

    expect(prior.every((request) => request.cancelled)).toBe(true);
    expect(facade.getMyTasks().tasks).toEqual([]);
    expect(facade.getMyTasks().counts).toEqual([]);
    expect(facade.getMyTasks().totalCount).toBe(0);
    expect(facade.getMyTasks().workspaceId).toBeNull();
    expect(facade.getMyTasks().page).toBe(1);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    httpMock.expectNone('/api/me/tasks');
    httpMock.expectNone('/api/me/tasks/counts');

    activeWorkspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace two' });
    TestBed.flushEffects();
    const requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests).toHaveLength(2);
    expect(requests.every((request) => request.request.params.get('workspaceId') === 'workspace-2')).toBe(true);
    requests[0].flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    requests[1].flush({ views: [], timeGroups: [] });
  });

  it('applies the Running and Needs review presets as exact relationship/stage pairs', () => {
    facade.load();
    flush({ items: [], page: 1, pageSize: 50, totalCount: 0 }, { views: [], timeGroups: [] });

    facade.applyBuiltinFilter('running');
    let requests = pendingProjectionRequests();
    expect(requests.every((request) => request.request.params.get('view') === 'assigned')).toBe(true);
    expect(requests.every((request) => request.request.params.get('stageCategory') === 'inProgress')).toBe(true);
    completeProjectionRequests(requests);

    facade.applyBuiltinFilter('needsReview');
    requests = pendingProjectionRequests();
    expect(requests.every((request) => request.request.params.get('view') === 'reviews')).toBe(true);
    expect(requests.every((request) => request.request.params.get('stageCategory') === 'review')).toBe(true);
    completeProjectionRequests(requests);
  });

  it('atomically applies Completed, retains optional filters, and cancels a pending debounced search', () => {
    vi.useFakeTimers();
    const projectId = '11111111-1111-4111-8111-111111111111';
    facade.load();
    flush({ items: [], page: 1, pageSize: 50, totalCount: 0 }, { views: [], timeGroups: [] });

    facade.setProjectFilter(projectId);
    completeProjectionRequests(pendingProjectionRequests());
    facade.setPriorityFilter('critical');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setBlockedFilter('true');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setTimeGroupFilter('today');
    completeProjectionRequests(pendingProjectionRequests());

    facade.setSearchFilter('  retained search  ');
    httpMock.expectNone('/api/me/tasks');
    facade.applyBuiltinFilter('completed');

    const requests = pendingProjectionRequests();
    for (const request of requests) {
      const params = request.request.params;
      expect(params.get('view')).toBe('completed');
      expect(params.get('stageCategory')).toBe('done');
      expect(params.get('projectId')).toBe(projectId);
      expect(params.get('priority')).toBe('critical');
      expect(params.get('blocked')).toBe('true');
      expect(params.get('timeGroup')).toBe('today');
      expect(params.get('search')).toBe('retained search');
      expect(params.get('page')).toBe('1');
    }
    completeProjectionRequests(requests);
    vi.advanceTimersByTime(300);
    httpMock.expectNone('/api/me/tasks');
    httpMock.expectNone('/api/me/tasks/counts');
  });

  it('round-trips a combined custom filter through one request pair and masks its opaque Project ID', () => {
    const projectId = '22222222-2222-4222-8222-222222222222';
    facade.load();
    flush({ items: [], page: 1, pageSize: 50, totalCount: 0 }, { views: [], timeGroups: [] });
    facade.setTab('reviews');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setProjectFilter(projectId);
    completeProjectionRequests(pendingProjectionRequests());
    facade.setStageCategoryFilter('review');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setPriorityFilter('high');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setBlockedFilter('false');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setTimeGroupFilter('next7Days');
    completeProjectionRequests(pendingProjectionRequests());

    expect(facade.saveCurrentFilter('Review queue')).toBe(true);
    const filterId = facade.getMyTasks().savedFilters[0].id;
    facade.clearAllFilters();
    completeProjectionRequests(pendingProjectionRequests());

    facade.applySavedFilter(filterId);
    const requests = pendingProjectionRequests();
    for (const request of requests) {
      expect(request.request.params.get('view')).toBe('reviews');
      expect(request.request.params.get('projectId')).toBe(projectId);
      expect(request.request.params.get('stageCategory')).toBe('review');
      expect(request.request.params.get('priority')).toBe('high');
      expect(request.request.params.get('blocked')).toBe('false');
      expect(request.request.params.get('timeGroup')).toBe('next7Days');
      expect(request.request.params.get('page')).toBe('1');
    }
    expect(facade.getMyTasks().projectFilterInputValue).toBe('');
    expect(facade.getMyTasks().filterConditions).toContainEqual({ id: 'project', label: 'Project filter active' });
    expect(JSON.stringify(facade.getMyTasks().filterConditions)).not.toContain(projectId);
    completeProjectionRequests(requests);
  });

  it.each([403, 404] as const)('fails closed for a stale or cross-Tenant saved Project after HTTP %s without rendering its ID or cached task metadata', (httpStatus) => {
    const staleProjectId = '66666666-6666-4666-8666-666666666666';
    facade.load();
    flush({ items: [task], page: 1, pageSize: 50, totalCount: 1 }, { views: [], timeGroups: [] });
    facade.setProjectFilter(staleProjectId);
    completeProjectionRequests(pendingProjectionRequests());
    expect(facade.saveCurrentFilter('Stale scope')).toBe(true);
    const filterId = facade.getMyTasks().savedFilters[0].id;
    facade.clearAllFilters();
    completeProjectionRequests(pendingProjectionRequests());

    facade.applySavedFilter(filterId);
    const requests = pendingProjectionRequests();
    requests.find((request) => request.request.url === '/api/me/tasks')!.flush(
      { error: { code: httpStatus === 403 ? 'FORBIDDEN' : 'MY_TASKS_PROJECT_NOT_FOUND', message: 'Project is unavailable.' } },
      { status: httpStatus, statusText: httpStatus === 403 ? 'Forbidden' : 'Not Found' }
    );

    const vm = facade.getMyTasks();
    expect(vm.status).toBe(httpStatus === 403 ? 'permissionDenied' : 'error');
    expect(vm.tasks).toEqual([]);
    expect(vm.counts).toEqual([]);
    expect(vm.rows).toEqual([]);
    expect(vm.projectFilterInputValue).toBe('');
    expect(vm.filterConditions).toContainEqual({ id: 'project', label: 'Project filter active' });
    expect(vm.filterConditions.some((condition) => condition.label.includes(staleProjectId))).toBe(false);
    expect(vm.message).toBe(httpStatus === 403
      ? 'Authentication or workspace permission is required.'
      : 'The selected Project is not available.');
    expect(requests.find((request) => request.request.url === '/api/me/tasks/counts')?.cancelled).toBe(true);
  });

  it('clears relationship and optional filters without silently changing explicit All Workspaces scope', () => {
    facade.load();
    flush({ items: [], page: 1, pageSize: 50, totalCount: 0 }, { views: [], timeGroups: [] });
    facade.setScope('allWorkspaces');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setPriorityFilter('high');
    completeProjectionRequests(pendingProjectionRequests());

    facade.clearAllFilters();
    const requests = pendingProjectionRequests();
    for (const request of requests) {
      expect(request.request.params.get('scope')).toBe('allWorkspaces');
      expect(request.request.params.has('workspaceId')).toBe(false);
      expect(request.request.params.get('view')).toBe('assigned');
      expect(request.request.params.has('priority')).toBe(false);
      expect(request.request.params.get('page')).toBe('1');
    }
    expect(facade.getMyTasks().scope).toBe('allWorkspaces');
    completeProjectionRequests(requests);
  });

  it('clears active filter execution on authorization invalidation while retaining harmless saved descriptors', () => {
    vi.useFakeTimers();
    facade.load();
    flush({ items: [task], page: 1, pageSize: 50, totalCount: 1 }, { views: [], timeGroups: [] });
    facade.setProjectFilter('33333333-3333-4333-8333-333333333333');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setSearchFilter('protected query');
    expect(facade.saveCurrentFilter('Reusable condition')).toBe(true);

    const handleRealtimeEvent = (facade as unknown as { handleRealtimeEvent(event: unknown): void }).handleRealtimeEvent.bind(facade);
    handleRealtimeEvent({ eventType: 'Security.AuthorizationStateChanged.v1' });

    const vm = facade.getMyTasks();
    expect(vm.tasks).toEqual([]);
    expect(vm.counts).toEqual([]);
    expect(vm.filters).toEqual({ projectId: '', stageCategory: '', priority: '', blocked: '', search: '', timeGroup: null });
    expect(vm.selectedTab).toBe('assigned');
    expect(vm.savedFilters.map((filter) => filter.name)).toEqual(['Reusable condition']);
    vi.advanceTimersByTime(300);
    httpMock.expectNone('/api/me/tasks');
  });

  it('resets old filter state and loads only the new namespace after logout then another login', () => {
    vi.useFakeTimers();
    facade.load();
    flush({ items: [], page: 1, pageSize: 50, totalCount: 0 }, { views: [], timeGroups: [] });
    facade.setProjectFilter('44444444-4444-4444-8444-444444444444');
    completeProjectionRequests(pendingProjectionRequests());
    facade.setSearchFilter('old account query');
    expect(facade.saveCurrentFilter('Old account')).toBe(true);

    const auth = TestBed.inject(AuthSessionFacade);
    auth.logoutLocally();
    TestBed.flushEffects();
    localStorage.setItem('coglatas.work-view.saved-filters.v1:tenant-b:user-b:my-tasks', JSON.stringify({
      version: 1,
      filters: [{
        id: 'saved-87654321', name: 'New account',
        snapshot: { selectedTab: 'completed', projectId: '', stageCategory: 'done', priority: '', blocked: '', search: '', timeGroup: null }
      }]
    }));
    const newSession: AuthSessionSnapshot = {
      ...DEFAULT_AUTH_SESSION,
      currentUser: { ...DEFAULT_AUTH_SESSION.currentUser!, userId: 'user-b', email: 'user-b@example.test', displayName: 'User B' },
      currentTenant: { ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId: 'tenant-b' }
    };
    (auth as unknown as { sessionState: { set(value: AuthSessionSnapshot): void } }).sessionState.set(newSession);
    TestBed.flushEffects();

    const vm = facade.getMyTasks();
    expect(vm.filters).toEqual({ projectId: '', stageCategory: '', priority: '', blocked: '', search: '', timeGroup: null });
    expect(vm.selectedTab).toBe('assigned');
    expect(vm.savedFilters.map((filter) => filter.name)).toEqual(['New account']);
    expect(JSON.stringify(vm)).not.toContain('old account query');
    vi.advanceTimersByTime(300);
    httpMock.expectNone('/api/me/tasks');
  });

  it('coalesces TaskChanged realtime events into one My Tasks refetch', () => {
    vi.useFakeTimers();
    facade.load();
    flush({ items: [task], page: 1, pageSize: 50, totalCount: 1 }, { views: [{ view: 'Assigned', count: 1 }], timeGroups: [] });

    const handleRealtimeEvent = (facade as unknown as { handleRealtimeEvent(event: unknown): void }).handleRealtimeEvent.bind(facade);
    handleRealtimeEvent({ eventType: 'Projects.TaskChanged.v1' });
    handleRealtimeEvent({ eventType: 'Projects.TaskChanged.v1' });

    vi.advanceTimersByTime(149);
    httpMock.expectNone('/api/me/tasks');
    httpMock.expectNone('/api/me/tasks/counts');

    vi.advanceTimersByTime(1);
    const requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests).toHaveLength(2);
    requests.find((request) => request.request.url === '/api/me/tasks')!
      .flush({ items: [task], page: 1, pageSize: 50, totalCount: 1 });
    requests.find((request) => request.request.url === '/api/me/tasks/counts')!
      .flush({ views: [{ view: 'Assigned', count: 1 }], timeGroups: [] });
  });

  function flush(page: unknown, counts: unknown): void {
    const requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests).toHaveLength(2);
    const taskRequest = requests.find((request) => request.request.url === '/api/me/tasks')!;
    expect(taskRequest.request.params.get('view')).toBe('assigned');
    expect(taskRequest.request.params.get('scope')).toBe('currentWorkspace');
    expect(taskRequest.request.params.get('workspaceId')).toBe('workspace-1');
    taskRequest.flush(page);
    requests.find((request) => request.request.url === '/api/me/tasks/counts')!.flush(counts);
  }

  function pendingProjectionRequests() {
    const requests = httpMock.match((request) => request.url === '/api/me/tasks' || request.url === '/api/me/tasks/counts');
    expect(requests).toHaveLength(2);
    return requests;
  }

  function completeProjectionRequests(requests: ReturnType<typeof pendingProjectionRequests>): void {
    requests.find((request) => request.request.url === '/api/me/tasks')!
      .flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    requests.find((request) => request.request.url === '/api/me/tasks/counts')!
      .flush({ views: [], timeGroups: [] });
  }
});
