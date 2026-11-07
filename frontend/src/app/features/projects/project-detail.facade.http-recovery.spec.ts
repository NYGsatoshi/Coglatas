import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { EMPTY, Subject } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { COGLATAS_REALTIME_TRANSPORT, RealtimeTransportStatus } from '../../core/realtime/realtime-transport';
import { RealtimeSubscriptionRequest } from '../../core/realtime/realtime.models';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';
import { COGLATAS_WORKSPACE_PREFERENCE_STORAGE } from '../../core/workspace/workspace-preference.service';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { WorkspacesFacade } from '../workspaces/workspaces.facade';
import { ganttSnapshotDto } from './project-detail-gantt.test-data';
import { ProjectDetailFacade } from './project-detail.facade';
import { snapshotDto } from './project-kanban.test-data';
import { ProjectDto } from './projects.api';
import { ProjectDetailPageComponent } from './project-detail-page/project-detail-page.component';

const draft: ProjectDto = {
  id: 'project-1', workspaceId: 'workspace-1', groupId: null, ownerUserId: 'owner-1',
  title: 'Authorized draft', description: null, status: 0, visibility: 1,
  activationState: 1, activatedAtUtc: null, activationVersion: null, versionNo: 7,
  startDate: null, endDate: null, uiPermissions: { canCreateTask: false, canActivate: true },
};
const active: ProjectDto = { ...draft, status: 1, activationState: 2,
  activatedAtUtc: '2026-08-24T00:00:00Z', activationVersion: 1,
  uiPermissions: { canCreateTask: true, canActivate: false } };
const workspace = { id: 'workspace-1', name: 'Authorized Workspace', description: null,
  icon: null, status: 0, createdAt: '2026-08-22T00:00:00Z', updatedAt: null,
  currentUserRole: 'Owner', accessSource: 'WorkspaceMembership', canOpenWorkspace: true,
  canOpenMembers: true, canOpenProjects: true, unreadAnnouncementCount: 0,
  unreadConversationCount: 0, inProgressProjectCount: 0, runningProjectCount: 0,
  needsReviewProjectCount: 0 };

describe('Project detail through real HTTP-only Workspace recovery', () => {
  let detail: ProjectDetailFacade;
  let workspaces: WorkspacesFacade;
  let realtime: RealtimeFacade;
  let http: HttpTestingController;
  let invalidations: Subject<void>;
  let transportStatuses: Subject<RealtimeTransportStatus>;
  let subscriptions: RealtimeSubscriptionRequest[];
  let denyProjectSubscription: boolean;
  const history = { touchProject: vi.fn() };
  const start = vi.fn(async () => {});

  beforeEach(() => {
    history.touchProject.mockReset(); start.mockClear(); invalidations = new Subject<void>();
    transportStatuses = new Subject<RealtimeTransportStatus>(); subscriptions = [];
    denyProjectSubscription = false;
    TestBed.configureTestingModule({ imports: [ProjectDetailPageComponent], providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useFactory: (router: Router) => ({
        paramMap: new Subject(), snapshot: router.routerState.root.snapshot,
      }), deps: [Router] },
      provideHttpClient(), provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: COGLATAS_WORKSPACE_PREFERENCE_STORAGE, useValue: {
        getItem: () => null, setItem: () => {}, removeItem: () => {},
      } },
      { provide: COGLATAS_REALTIME_TRANSPORT, useValue: {
        durableEvents$: EMPTY, authorizationInvalidations$: invalidations, statuses$: transportStatuses,
        start, stop: async () => {},
        subscribe: async (request: RealtimeSubscriptionRequest) => {
          subscriptions.push(request);
          return request.subscriptionType === 'project' && denyProjectSubscription
            ? { allowed: false, code: 'Forbidden' } : { allowed: true, code: 'Subscribed' };
        },
        unsubscribe: async () => ({ allowed: true, code: 'Unsubscribed' }),
      } },
      { provide: ContinueWorkingHistoryService, useValue: history },
    ] });
    TestBed.inject(FrontendFeatureFlagsService).setForTesting({
      'realtime.signalR': false, 'tasks.kanbanV1': false, 'tasks.ganttV1': false,
    });
    http = TestBed.inject(HttpTestingController);
    workspaces = TestBed.inject(WorkspacesFacade);
    realtime = TestBed.inject(RealtimeFacade);
    detail = TestBed.inject(ProjectDetailFacade);
  });

  afterEach(() => {
    try { detail.release(); http.verify(); }
    finally { TestBed.resetTestingModule(); }
  });

  function selectFromHttp() {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspace]);
    expect(TestBed.inject(ActiveWorkspaceFacade).activeWorkspace()?.id).toBe('workspace-1');
  }

  function flushActiveCollections() {
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
  }

  async function settle() {
    for (let count = 0; count < 5; count++) { TestBed.tick(); await Promise.resolve(); }
  }

  function enableBoard() {
    TestBed.inject(FrontendFeatureFlagsService).setForTesting({
      'realtime.signalR': false, 'tasks.kanbanV1': true, 'tasks.ganttV1': true,
    });
  }

  function flushBoard() {
    http.expectOne('/api/projects/project-1').flush(active);
    flushActiveCollections();
    http.expectOne((request) => request.url === '/api/projects/project-1/kanban').flush(snapshotDto());
  }

  async function mountBoard() {
    enableBoard(); detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle(); flushBoard();
  }

  for (const failureFirst of [false, true]) {
    it.each([false, true])(`recovers a queued Schedule when overlapping catch-up fails with failureFirst=${failureFirst}, childFirst=%s`, async (childFirst) => {
      await mountBoard();
      const first = workspaces.loadWorkspaces(true);
      selectFromHttp(); await first; await settle();
      const olderParent = http.expectOne('/api/projects/project-1');
      const olderBoard = http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban'));
      const olderSchedule = http.expectOne('/api/projects/project-1/gantt');
      const second = workspaces.loadWorkspaces(true);
      selectFromHttp(); await second; await settle();
      const currentParent = http.expectOne('/api/projects/project-1');
      http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')).flush(snapshotDto());
      const failOlderParent = () => olderParent.flush(
        { message: 'Older catch-up unavailable' }, { status: 503, statusText: 'Unavailable' },
      );
      if (childFirst) olderSchedule.flush(ganttSnapshotDto());
      if (failureFirst) failOlderParent();
      currentParent.flush(active);
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
      http.expectOne('/api/projects/project-1/members').flush({ members: [] });
      if (!failureFirst) failOlderParent();
      olderBoard.flush(snapshotDto());
      if (!childFirst) olderSchedule.flush(ganttSnapshotDto());
      await settle();
      const followUps = http.match('/api/projects/project-1/gantt');
      for (const read of followUps) read.flush({ ...ganttSnapshotDto(), projectVersion: 37 });
      expect(followUps).toHaveLength(1);
      expect(detail.view().status).toBe('ready');
      expect(detail.view().schedule.snapshot?.projectVersion).toBe(37);
      http.expectNone(request => request.url.startsWith('/api/projects/project-1'));
    });
  }

  it.each([401, 403])('honors current %s from an older catch-up after a newer catch-up succeeds', async (status) => {
    await mountBoard();
    const first = workspaces.loadWorkspaces(true);
    selectFromHttp(); await first; await settle();
    const olderParent = http.expectOne('/api/projects/project-1');
    const olderBoard = http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban'));
    const olderSchedule = http.expectOne('/api/projects/project-1/gantt');
    const second = workspaces.loadWorkspaces(true);
    selectFromHttp(); await second; await settle();
    http.expectOne('/api/projects/project-1').flush(active);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')).flush(snapshotDto());
    expect(detail.view().status).toBe('ready');
    olderParent.flush({ message: 'Current authority denied' }, { status, statusText: 'Denied' });
    olderBoard.flush(snapshotDto()); olderSchedule.flush(ganttSnapshotDto()); await settle();
    expect(detail.view().status).toBe('permissionDenied');
    expect(detail.view().kanban.snapshot).toBeNull();
    expect(detail.view().schedule.snapshot).toBeNull();
    http.expectNone(request => request.url.startsWith('/api/projects/project-1'));
  });

  it.each(['draft', 'active'] as const)('retains newer Active recovery when an older successful %s catch-up finishes last', async (kind) => {
    await mountBoard();
    const first = workspaces.loadWorkspaces(true);
    selectFromHttp(); await first; await settle();
    const olderParent = http.expectOne('/api/projects/project-1');
    const olderBoard = http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban'));
    const olderSchedule = http.expectOne('/api/projects/project-1/gantt');
    if (kind === 'active') olderParent.flush({ ...active, title: 'Older Active view' });
    const olderCollections = kind === 'active' ? [
      http.expectOne('/api/projects/project-1/tasks'),
      http.expectOne('/api/projects/project-1/workload'),
      http.expectOne('/api/projects/project-1/members'),
    ] : [];
    const second = workspaces.loadWorkspaces(true);
    selectFromHttp(); await second; await settle();
    http.expectOne('/api/projects/project-1').flush({ ...active, title: 'Current Active view' });
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')).flush(snapshotDto());
    if (kind === 'draft') olderParent.flush({ ...draft, title: 'Older Draft view' });
    for (const collection of olderCollections) collection.flush(
      collection.request.url.endsWith('/tasks') ? { items: [] } : { members: [] },
    );
    olderBoard.flush(snapshotDto()); olderSchedule.flush(ganttSnapshotDto()); await settle();
    for (const read of http.match('/api/projects/project-1/gantt')) read.flush({ ...ganttSnapshotDto(), projectVersion: 37 });
    expect(detail.view().project?.name).toBe('Current Active view');
    expect(detail.view().status).toBe('ready');
    expect(detail.view().kanban.snapshot).not.toBeNull();
    expect(detail.view().schedule.snapshot?.projectVersion).toBe(37);
    http.expectNone(request => request.url.startsWith('/api/projects/project-1'));
  });

  it.each(['draft', 'active'] as const)('retains newer Active recovery when an older initial %s load finishes last', async (kind) => {
    enableBoard(); detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    const initial = http.expectOne('/api/projects/project-1');
    if (kind === 'active') initial.flush({ ...active, title: 'Older initial Active' });
    const oldCollections = kind === 'active' ? {
      tasks: http.expectOne('/api/projects/project-1/tasks'),
      board: http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')),
      schedule: http.expectOne('/api/projects/project-1/gantt'),
      workload: http.expectOne('/api/projects/project-1/workload'),
      members: http.expectOne('/api/projects/project-1/members'),
    } : null;
    const recovery = workspaces.loadWorkspaces(true);
    selectFromHttp(); await recovery; await settle();
    http.expectOne('/api/projects/project-1').flush({ ...active, title: 'Current recovered Active' });
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/workload').flush({
      members: [{ userId: 'current-owner', displayName: 'Current Owner', assignedTaskCount: 2 }],
    });
    http.expectOne('/api/projects/project-1/members').flush([
      { userId: 'current-owner', displayName: 'Current Owner', role: 'Owner' },
    ]);
    http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')).flush(snapshotDto());
    http.expectOne('/api/projects/project-1/gantt').flush({ ...ganttSnapshotDto(), projectVersion: 37 });
    if (kind === 'draft') initial.flush({ ...draft, title: 'Older initial Draft' });
    if (oldCollections) {
      oldCollections.tasks.flush({ items: [] }); oldCollections.board.flush(snapshotDto());
      oldCollections.schedule.flush(ganttSnapshotDto()); oldCollections.workload.flush({ members: [] });
      oldCollections.members.flush([]);
    }
    expect(detail.view().project?.name).toBe('Current recovered Active');
    expect(detail.view().project?.isOperational).toBe(true);
    expect(detail.view().workload[0]?.assignedTaskCount).toBe(2);
    expect(detail.view().members[0]?.displayName).toBe('Current Owner');
    expect(detail.view().kanban.snapshot).not.toBeNull();
    expect(detail.view().schedule.snapshot?.projectVersion).toBe(37);
    http.expectNone(request => request.url.startsWith('/api/projects/project-1'));
  });

  async function loadBoardWithHeldInitialCollections() {
    enableBoard(); detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    http.expectOne('/api/projects/project-1').flush(active);
    const held = {
      tasks: http.expectOne('/api/projects/project-1/tasks'),
      board: http.expectOne('/api/projects/project-1/kanban'),
      schedule: http.expectOne('/api/projects/project-1/gantt'),
      workload: http.expectOne('/api/projects/project-1/workload'),
      members: http.expectOne('/api/projects/project-1/members'),
    };
    const recovery = workspaces.loadWorkspaces(true);
    selectFromHttp(); await recovery; await settle(); flushBoard();
    expect(detail.view().status).toBe('ready');
    return held;
  }

  it('does not dispatch a cold Active projection before the first Workspace selection and reads a denied Board only once', async () => {
    enableBoard(); detail.load('project-1');
    http.expectOne('/api/projects/project-1').flush(active);
    const premature = http.match(request => request.url.startsWith('/api/projects/project-1/'));
    for (const request of premature) {
      request.flush(request.request.url.endsWith('/kanban') ? snapshotDto()
        : request.request.url.endsWith('/gantt') ? ganttSnapshotDto()
        : request.request.url.endsWith('/tasks') ? { items: [] } : { members: [] });
    }
    selectFromHttp(); await settle();
    http.expectOne('/api/projects/project-1').flush(active);
    flushActiveCollections();
    http.expectOne('/api/projects/project-1/kanban').flush(
      { message: 'Current Board denied' }, { status: 403, statusText: 'Denied' },
    );
    http.expectOne('/api/projects/project-1').flush(active);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    expect(premature).toHaveLength(0);
    expect(detail.view().status).toBe('ready');
    expect(detail.view().kanban.status).toBe('permissionDenied');
    expect(detail.view().kanban.snapshot).toBeNull();
    http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
  });

  it.each(['draft', 'active'] as const)('keeps HTTP catch-up %s reads behind explicit selection with multiple Workspaces', async (kind) => {
    enableBoard(); detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    cold.flush(draft);
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspace, { ...workspace, id: 'workspace-2', name: 'Other Workspace' }]);
    await settle();
    const beforeSelection = http.match('/api/projects/project-1');
    for (const parent of beforeSelection) parent.flush(kind === 'draft' ? draft : active);
    const children = http.match(request => request.url.startsWith('/api/projects/project-1/'));
    for (const child of children) child.flush(child.request.url.endsWith('/tasks') ? { items: [] }
      : child.request.url.endsWith('/kanban') ? snapshotDto() : child.request.url.endsWith('/gantt') ? ganttSnapshotDto() : { members: [] });
    expect(beforeSelection).toHaveLength(0);
    expect(children).toHaveLength(0);
    expect(detail.view().project).toBeUndefined();
    expect(history.touchProject).not.toHaveBeenCalled();
    expect(TestBed.inject(WorkspaceSelectionFacade).selection().status).toBe('selectionRequired');
    expect(detail.view().status).toBe('error');
    await TestBed.inject(WorkspaceSelectionFacade).selectWorkspace('workspace-1');
    await settle(); flushBoard();
    expect(detail.view().status).toBe('ready');
    expect(detail.view().project?.id).toBe('project-1');
  });

  it.each(['empty', 'unavailable'] as const)('ends the cold Project spinner when Workspace discovery is %s and recovers after selection', async (kind) => {
    detail.load('project-1');
    http.expectOne('/api/projects/project-1').flush({ message: 'Unscoped response' }, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    const listing = http.expectOne('/api/workspaces');
    if (kind === 'empty') listing.flush([]);
    else listing.flush({ message: 'Discovery unavailable' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    expect(detail.view().status).toBe('error');
    expect(detail.view().message).toBe(TestBed.inject(I18nService).translate('topBar.workspaceUnavailable'));
    expect(detail.view().project).toBeUndefined();
    expect(history.touchProject).not.toHaveBeenCalled();
    const retry = workspaces.loadWorkspaces(true); selectFromHttp(); await retry; await settle();
    // The first scoped load and HTTP catch-up may both revalidate this scope.
    // Do not assume a single parent read or suppress a fresh denial.
    const parents = http.match('/api/projects/project-1');
    expect(parents.length).toBeGreaterThan(0);
    for (const parent of parents) parent.flush(draft);
    expect(detail.view().status).toBe('ready');
    expect(history.touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
  });

  it.each([401, 403])('restores navigation after current Gantt %s without automatically retrying either projection', async (status) => {
    await mountBoard(); detail.retrySchedule();
    http.expectOne('/api/projects/project-1/gantt').flush({ message: 'Current schedule denied' }, { status, statusText: 'Denied' });
    expect(detail.view().schedule.snapshot).toBeNull();
    expect(detail.view().status).toBe('permissionDenied');
    const parents = http.match('/api/projects/project-1');
    expect(parents).toHaveLength(1);
    parents[0].flush(active);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    expect(detail.view().status).toBe('ready');
    expect(detail.view().kanban.status).toBe('permissionDenied');
    expect(detail.view().kanban.snapshot).toBeNull();
    expect(detail.view().schedule.status).toBe('permissionDenied');
    expect(detail.view().schedule.snapshot).toBeNull();
    http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
    detail.retrySchedule();
    http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
    expect(detail.view().schedule.status).toBe('degraded');
    expect(detail.view().schedule.snapshot).not.toBeNull();
  });

  it('does not let completed old Schedule cleanup release a newer refresh', async () => {
    await mountBoard();
    detail.retrySchedule(); const oldRead = http.expectOne('/api/projects/project-1/gantt');
    detail.load('project-1'); flushBoard();
    detail.retrySchedule(); const currentRead = http.expectOne('/api/projects/project-1/gantt');
    oldRead.flush({ ...ganttSnapshotDto(), projectVersion: 99 });
    detail.retrySchedule();
    http.expectNone('/api/projects/project-1/gantt');
    expect(detail.view().schedule.reconciliationQueued).toBe(true);
    currentRead.flush({ ...ganttSnapshotDto(), projectVersion: 37 });
    await settle();
    http.expectOne('/api/projects/project-1/gantt').flush({ ...ganttSnapshotDto(), projectVersion: 38 });
    expect(detail.view().schedule.snapshot?.projectVersion).toBe(38);
    http.expectNone('/api/projects/project-1/gantt');
  });

  for (const denial of [401, 403]) {
    it.each([403, 503])(`keeps current Gantt ${denial} redacted when fresh parent authority returns %s`, async (status) => {
      await mountBoard(); detail.retrySchedule();
      http.expectOne('/api/projects/project-1/gantt').flush({ message: 'Schedule denied' }, { status: denial, statusText: 'Denied' });
      const parents = http.match('/api/projects/project-1');
      expect(parents).toHaveLength(1);
      parents[0].flush({ message: 'Parent unavailable' }, { status, statusText: 'Unavailable' });
      detail.retrySchedule();
      http.expectNone(request => request.url.startsWith('/api/projects/project-1/'));
      expect(detail.view().status).toBe('permissionDenied');
      expect(detail.view().project).toBeUndefined();
      expect(detail.view().kanban.snapshot).toBeNull();
      expect(detail.view().schedule.snapshot).toBeNull();
    });
  }

  it.each([401, 403])('restores navigation after current Board %s while requiring an explicit Board retry', async (status) => {
    await mountBoard(); detail.retryKanban();
    http.expectOne(request => request.url === '/api/projects/project-1/kanban').flush(
      { message: 'Current Board denied' }, { status, statusText: 'Denied' });
    expect(detail.view().status).toBe('permissionDenied');
    expect(detail.view().kanban.snapshot).toBeNull();
    const parents = http.match('/api/projects/project-1');
    expect(parents).toHaveLength(1);
    parents[0].flush(active);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    expect(detail.view().status).toBe('ready');
    expect(detail.view().kanban.status).toBe('permissionDenied');
    expect(detail.view().kanban.snapshot).toBeNull();
    expect(detail.view().schedule.status).toBe('permissionDenied');
    expect(detail.view().schedule.snapshot).toBeNull();
    http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
    detail.retryKanban();
    http.expectOne(request => request.url === '/api/projects/project-1/kanban').flush(snapshotDto());
    expect(detail.view().kanban.status).toBe('ready');
    expect(detail.view().kanban.snapshot).not.toBeNull();
  });

  it.each([401, 403])('offers a page-level retry after Board %s and sends no operational request before the click', async (status) => {
    enableBoard(); selectFromHttp(); await settle();
    const route = TestBed.inject(ActivatedRoute);
    const fixture = TestBed.createComponent(ProjectDetailPageComponent);
    try {
      (route.paramMap as Subject<ReturnType<typeof convertToParamMap>>).next(convertToParamMap({ projectId: 'project-1' }));
      fixture.detectChanges();
      http.expectOne('/api/projects/project-1').flush(active); flushActiveCollections();
      http.expectOne(request => request.url === '/api/projects/project-1/kanban').flush(
        { message: 'Current Board denied' }, { status, statusText: 'Denied' });
      http.expectOne('/api/projects/project-1').flush(active);
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
      http.expectOne('/api/projects/project-1/members').flush({ members: [] });
      fixture.componentInstance.tab.set('tasks'); fixture.detectChanges();
      expect(detail.view().status).toBe('ready');
      expect(detail.view().kanban.status).toBe('permissionDenied');
      http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
      const retry = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="project-board-denied-retry"]');
      expect(retry).not.toBeNull(); expect(retry?.disabled).toBe(false);
      retry!.click();
      const fresh = http.expectOne(request => request.url === '/api/projects/project-1/kanban');
      expect(fresh.request.method).toBe('GET'); expect(fresh.request.withCredentials).toBe(true);
      fresh.flush(snapshotDto());
      expect(detail.view().kanban.status).toBe('ready');
      expect(detail.view().kanban.snapshot).not.toBeNull();
    } finally { fixture.destroy(); }
  });

  it.each([401, 403])('reauthorizes subscriptions and fresh HTTP projections on reconnect after current Board %s navigation recovery', async (status) => {
    const projectId = '76d614a0-4339-4d48-8d22-47dbdf36cc31';
    const selectedWorkspace = { ...workspace, id: '644f02a2-3f97-4e36-91bb-7272cd768c25' };
    const parent = { ...active, id: projectId, workspaceId: selectedWorkspace.id };
    const path = `/api/projects/${projectId}`;
    const connected = () => vi.waitFor(() => {
      TestBed.tick(); expect(realtime.connectionState()).toBe('Connected');
    }, { timeout: 5000, interval: 10 });
    const flushWorkspace = () => {
      http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
      http.expectOne('/api/workspaces').flush([selectedWorkspace]);
    };
    const navigation = () => {
      http.expectOne(path).flush(parent);
      http.expectOne(`${path}/tasks`).flush({ items: [] });
      http.expectOne(`${path}/workload`).flush({ members: [] });
      http.expectOne(`${path}/members`).flush({ members: [] });
    };
    const operational = () => {
      const board = snapshotDto();
      http.expectOne(request => request.url === `${path}/kanban`).flush({ ...board, board: { ...board.board!, projectId } });
      http.expectOne(`${path}/gantt`).flush({ ...ganttSnapshotDto(), projectId });
    };
    enableBoard(); flushWorkspace(); await settle();
    detail.load(projectId); navigation(); operational();
    TestBed.inject(FrontendFeatureFlagsService).setForTesting({ 'realtime.signalR': true });
    await connected(); navigation(); operational(); await settle();
    expect(realtime.connectionState()).toBe('Connected');
    detail.retryKanban();
    http.expectOne(request => request.url === `${path}/kanban`).flush(
      { message: 'Current Board denied' }, { status, statusText: 'Denied' });
    navigation();
    expect(detail.view().status).toBe('ready');
    expect(detail.view().kanban.status).toBe('permissionDenied');
    expect(detail.view().schedule.status).toBe('permissionDenied');
    http.expectNone(request => request.url.startsWith(path));
    subscriptions.length = 0;
    transportStatuses.next('reconnecting'); transportStatuses.next('reconnected'); await connected();
    expect(subscriptions).toContainEqual({ subscriptionType: 'project', resourceId: projectId });
    // A same-scope reconnect reauthorizes the Project subscription, then reads
    // current projections. It need not reload an unchanged Workspace directory.
    expect(detail.view().kanban.snapshot).toBeNull(); expect(detail.view().schedule.snapshot).toBeNull();
    navigation(); operational(); await settle();
    expect(realtime.connectionState()).toBe('Connected');
    expect(detail.view().kanban.snapshot).not.toBeNull(); expect(detail.view().schedule.snapshot).not.toBeNull();
  });

  for (const firstProjection of ['board', 'schedule'] as const) {
    for (const subscriptionDenied of [false, true]) {
      it.each([200, 403, 503].flatMap(parentStatus => [true, false].map(childFirst => ({ parentStatus, childFirst }))))(
        `holds ${firstProjection} until reconnect parent $parentStatus settles with subscriptionDenied=${subscriptionDenied}, childFirst=$childFirst`, async ({ parentStatus, childFirst }) => {
        const projectId = 'a1e33828-b8d3-4b70-a802-dde89e185915';
        const parent = { ...active, id: projectId };
        const path = `/api/projects/${projectId}`;
        const board = snapshotDto({ board: { ...snapshotDto().board!, projectId } });
        const schedule = { ...ganttSnapshotDto(), projectId };
        const connected = () => vi.waitFor(() => {
          TestBed.tick(); expect(realtime.connectionState()).toBe('Connected');
        }, { timeout: 5000, interval: 10 });
        const navigation = () => {
          http.expectOne(path).flush(parent);
          http.expectOne(`${path}/tasks`).flush({ items: [] });
          http.expectOne(`${path}/workload`).flush({ members: [] });
          http.expectOne(`${path}/members`).flush({ members: [] });
        };
        enableBoard(); selectFromHttp(); await settle(); detail.load(projectId); navigation();
        http.expectOne(request => request.url === `${path}/kanban`).flush(board);
        http.expectOne(`${path}/gantt`).flush(schedule);
        TestBed.inject(FrontendFeatureFlagsService).setForTesting({ 'realtime.signalR': true });
        await connected(); navigation();
        http.expectOne(request => request.url === `${path}/kanban`).flush(board);
        http.expectOne(`${path}/gantt`).flush(schedule);
        detail.retryKanban();
        http.expectOne(request => request.url === `${path}/kanban`).flush(
          { message: 'Current Board denied' }, { status: 403, statusText: 'Denied' });
        navigation();
        expect(detail.view().kanban.status).toBe('permissionDenied');
        expect(detail.view().schedule.status).toBe('permissionDenied');
        denyProjectSubscription = subscriptionDenied;
        transportStatuses.next('reconnecting'); transportStatuses.next('reconnected'); await connected();
        const early = http.match(request => request.url === `${path}/kanban` || request.url === `${path}/gantt`);
        const ordered = early.sort((left, right) => Number(right.request.url.endsWith(firstProjection === 'board' ? '/kanban' : '/gantt'))
          - Number(left.request.url.endsWith(firstProjection === 'board' ? '/kanban' : '/gantt')));
        const flushEarly = () => {
          for (const read of ordered) read.flush(read.request.url.endsWith('/kanban') ? board : schedule);
        };
        if (childFirst) {flushEarly();}
        const earlyBoard = detail.view().kanban.snapshot, earlySchedule = detail.view().schedule.snapshot;
        if (parentStatus === 200) {
          http.expectOne(path).flush(parent);
          http.expectOne(`${path}/tasks`).flush({ items: [] });
          http.expectOne(`${path}/workload`).flush({ members: [] });
          expect(detail.view().kanban.snapshot).toBeNull(); expect(detail.view().schedule.snapshot).toBeNull();
          http.expectOne(`${path}/members`).flush({ members: [] });
          for (const read of http.match(request => request.url === `${path}/kanban` || request.url === `${path}/gantt`))
            read.flush(read.request.url.endsWith('/kanban') ? board : schedule);
        } else {
          http.expectOne(path).flush({ message: 'Fresh parent not authorized' }, { status: parentStatus, statusText: 'Unavailable' });
        }
        if (!childFirst) {flushEarly();}
        await settle();
        // Denied subscription revalidation preserves the established parallel
        // HTTP contract, but successful child responses await the fresh parent.
        expect(early).toHaveLength(subscriptionDenied ? 2 : 0);
        expect(earlyBoard).toBeNull(); expect(earlySchedule).toBeNull();
        if (parentStatus === 200) {
          expect(detail.view().kanban.snapshot).not.toBeNull(); expect(detail.view().schedule.snapshot).not.toBeNull();
        } else {
          expect(detail.view().kanban.snapshot).toBeNull(); expect(detail.view().schedule.snapshot).toBeNull();
        }
        http.expectNone(request => request.url.startsWith(path));
        if (parentStatus === 503) {
          denyProjectSubscription = false;
          transportStatuses.next('reconnecting'); transportStatuses.next('reconnected'); await connected();
          navigation();
          http.expectOne(request => request.url === `${path}/kanban`).flush(board);
          http.expectOne(`${path}/gantt`).flush(schedule);
          expect(detail.view().schedule.snapshot).not.toBeNull();
          detail.retrySchedule();
          http.expectOne(`${path}/gantt`).flush({ ...schedule, projectVersion: 37 });
          expect(detail.view().schedule.snapshot?.projectVersion).toBe(37);
          http.expectNone(request => request.url.startsWith(path));
        }
      });
    }
  }

  for (const denial of [401, 403]) {
    it.each([403, 503])(`keeps current Board ${denial} redacted when fresh parent returns %s`, async (status) => {
      await mountBoard(); detail.retryKanban();
      http.expectOne(request => request.url === '/api/projects/project-1/kanban').flush(
        { message: 'Board denied' }, { status: denial, statusText: 'Denied' });
      const parents = http.match('/api/projects/project-1');
      expect(parents).toHaveLength(1);
      parents[0].flush({ message: 'Parent unavailable' }, { status, statusText: 'Unavailable' });
      detail.retryKanban();
      http.expectNone(request => request.url.startsWith('/api/projects/project-1/'));
      expect(detail.view().status).toBe('permissionDenied');
      expect(detail.view().project).toBeUndefined();
      expect(detail.view().kanban.snapshot).toBeNull();
      expect(detail.view().schedule.snapshot).toBeNull();
    });
  }

  for (const moveFirst of [true, false]) {
    it.each([401, 403])(`rejects an in-flight move after Board refresh %s ${moveFirst ? 'before' : 'after'} navigation recovery`, async (status) => {
      await mountBoard(); detail.retryKanban();
      const board = http.expectOne(request => request.url === '/api/projects/project-1/kanban');
      moveFirstCard(); const move = http.expectOne('/api/tasks/task-1/kanban-move');
      board.flush({ message: 'Current Board denied' }, { status, statusText: 'Denied' });
      const parents = http.match('/api/projects/project-1');
      expect(parents).toHaveLength(1);
      const completeMove = () => move.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 99 } }), warnings: [] });
      if (moveFirst) completeMove();
      expect(detail.view().status).toBe('permissionDenied');
      expect(detail.view().kanban.snapshot).toBeNull();
      parents[0].flush(active);
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
      http.expectOne('/api/projects/project-1/members').flush({ members: [] });
      if (!moveFirst) completeMove();
      await settle();
      expect(detail.view().status).toBe('ready');
      expect(detail.view().kanban.status).toBe('permissionDenied');
      expect(detail.view().kanban.snapshot).toBeNull();
      expect(detail.view().schedule.snapshot).toBeNull();
      http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
    });
  }

  it.each(['draft', 'active'] as const)('discards a held HTTP catch-up %s after an explicit Workspace replacement', async (kind) => {
    await mountBoard();
    const refresh = workspaces.loadWorkspaces(true);
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspace, { ...workspace, id: 'workspace-2', name: 'Other Workspace' }]);
    await refresh; await settle();
    const parent = http.expectOne('/api/projects/project-1');
    const oldBoard = http.expectOne(request => request.url === '/api/projects/project-1/kanban');
    const oldSchedule = http.expectOne('/api/projects/project-1/gantt');
    expect(await TestBed.inject(WorkspaceSelectionFacade).selectWorkspace('workspace-2')).toBe(true);
    parent.flush(kind === 'draft' ? draft : active);
    oldBoard.flush(snapshotDto()); oldSchedule.flush(ganttSnapshotDto());
    await settle();
    expect(TestBed.inject(ActiveWorkspaceFacade).activeWorkspace()?.id).toBe('workspace-2');
    expect(detail.view().project).toBeUndefined();
    expect(detail.view().kanban.snapshot).toBeNull();
    expect(detail.view().schedule.snapshot).toBeNull();
    expect(history.touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
    http.expectNone(request => request.url.startsWith('/api/projects/project-1'));
  });

  for (const projection of ['board', 'schedule'] as const) {
    it.each([401, 403])(`requires a fresh authorized parent and collections after initial ${projection} %s without retrying the projection`, async (status) => {
      const held = await holdInitialOperationalRead();
      held[projection].flush({ message: 'Initial authority denied' }, { status, statusText: 'Denied' });
      expect(detail.view().status).toBe('permissionDenied');
      expect(detail.view().project).toBeUndefined();
      expect(detail.view().kanban.snapshot).toBeNull();
      expect(detail.view().schedule.snapshot).toBeNull();

      const freshParent = http.expectOne('/api/projects/project-1');
      freshParent.flush({ ...active, title: 'Freshly authorized Project' });
      expect(detail.view().status).toBe('permissionDenied');
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
      http.expectOne('/api/projects/project-1/members').flush({ members: [] });
      expect(detail.view().status).toBe('ready');
      expect(detail.view().project?.name).toBe('Freshly authorized Project');
      expect(detail.view().kanban.status).toBe('permissionDenied');
      expect(detail.view().schedule.status).toBe('permissionDenied');

      finishOldInitialRead(held, projection);
      expect(detail.view().project?.name).toBe('Freshly authorized Project');
      expect(detail.view().kanban.snapshot).toBeNull();
      expect(detail.view().schedule.snapshot).toBeNull();
      http.expectNone((request) => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
    });
  }

  it.each(['clear', 'replace', 'release', 'project'] as const)(
    'discards the new parent response before dispatch when the mounted scope changes by %s', async (boundary) => {
      const held = await holdInitialOperationalRead();
      held.board.flush({ message: 'Denied' }, { status: 403, statusText: 'Denied' });
      const freshParent = http.expectOne('/api/projects/project-1');
      const selection = TestBed.inject(ActiveWorkspaceFacade);
      if (boundary === 'clear') selection.clearWorkspace();
      else if (boundary === 'replace') selection.setActiveWorkspace({ id: 'workspace-2', label: 'Other Workspace' });
      else if (boundary === 'release') detail.release();
      else {
        detail.load('project-2');
        http.expectOne('/api/projects/project-2').flush({ ...draft, id: 'project-2', title: 'Current Project' });
      }
      freshParent.flush({ ...active, title: 'Stale authorized Project' });
      finishOldInitialRead(held, 'board');
      expect(detail.view().project?.id).toBe(boundary === 'project' ? 'project-2' : undefined);
      http.expectNone((request) => request.url.startsWith('/api/projects/project-1/'));
    },
  );

  it.each([403, 503])('keeps initial denial after a fresh parent %s without retrying it', async (status) => {
    const held = await holdInitialOperationalRead();
    held.board.flush({ message: 'Denied' }, { status: 403, statusText: 'Denied' });
    http.expectOne('/api/projects/project-1').flush({ message: 'Parent unavailable' }, { status, statusText: 'Unavailable' });
    finishOldInitialRead(held, 'board');
    expect(detail.view().status).toBe('permissionDenied');
    expect(detail.view().project).toBeUndefined();
    expect(detail.view().kanban.snapshot).toBeNull();
    expect(detail.view().schedule.snapshot).toBeNull();
    http.expectNone(() => true);
  });

  async function holdInitialOperationalRead() {
    enableBoard(); detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    http.expectOne('/api/projects/project-1').flush(active);
    return {
      tasks: http.expectOne('/api/projects/project-1/tasks'),
      board: http.expectOne('/api/projects/project-1/kanban'),
      schedule: http.expectOne('/api/projects/project-1/gantt'),
      workload: http.expectOne('/api/projects/project-1/workload'),
      members: http.expectOne('/api/projects/project-1/members'),
    };
  }

  function finishOldInitialRead(
    held: Awaited<ReturnType<typeof holdInitialOperationalRead>>, denied: 'board' | 'schedule',
  ) {
    if (denied === 'board') held.schedule.flush(ganttSnapshotDto());
    else held.board.flush(snapshotDto());
    held.tasks.flush({ items: [] }); held.workload.flush({ members: [] }); held.members.flush({ members: [] });
  }

  for (const projection of ['board', 'schedule'] as const) {
    it.each([401, 403])(`honors initial ${projection} %s before a joined load or newer move can restore it`, async (status) => {
      const held = await loadBoardWithHeldInitialCollections(); moveFirstCard();
      const move = http.expectOne('/api/tasks/task-1/kanban-move');
      held[projection].flush({ message: 'Initial authority denied' }, { status, statusText: 'Denied' });
      const immediateStatus = detail.view().status;
      if (projection === 'board') held.schedule.flush(ganttSnapshotDto());
      else held.board.flush(snapshotDto());
      held.tasks.flush({ items: [] }); held.workload.flush({ members: [] }); held.members.flush({ members: [] });
      move.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 99 } }), warnings: [] });
      await settle();
      for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
      for (const request of http.match((request) => request.url === '/api/projects/project-1/kanban')) request.flush(snapshotDto());
      expect(immediateStatus).toBe('permissionDenied');
      expect(detail.view().status).toBe('permissionDenied');
      http.expectOne('/api/projects/project-1').flush(
        { message: 'Current parent denied' }, { status, statusText: 'Denied' },
      );
      expect(detail.view().status).toBe('permissionDenied');
      expect(detail.view().kanban.snapshot).toBeNull();
      expect(detail.view().schedule.snapshot).toBeNull();
    });
  }

  it('keeps a new filter when an initial Board result loses ownership to a fallback and move', async () => {
    const held = await loadBoardWithHeldInitialCollections(); moveFirstCard();
    const move = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.setIncludeOlderCompleted(true);
    held.board.flush(snapshotDto()); held.schedule.flush(ganttSnapshotDto());
    held.tasks.flush({ items: [] }); held.workload.flush({ members: [] }); held.members.flush({ members: [] });
    move.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 8 } }), warnings: [] });
    await settle();
    const reads = http.match((request) => request.url === '/api/projects/project-1/kanban');
    const filters = reads.map((request) => request.request.params.get('includeOlderCompleted'));
    for (const request of reads) request.flush(snapshotDto({ board: { ...snapshotDto().board!, includesOlderCompleted: true } }));
    for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
    expect(filters).toEqual(['true']);
    expect(detail.view().kanban.snapshot?.includesOlderCompleted).toBe(true);
  });

  function moveFirstCard() {
    detail.moveTask({ item: detail.view().kanban.snapshot!.cards[0],
      targetStatus: 'stage-done', targetBeforeItemId: null, targetAfterItemId: null,
      reason: null, source: 'keyboard' });
  }

  it('keeps a fallback move locked when an old successful move arrives during the initial join', async () => {
    await mountBoard(); moveFirstCard();
    const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.release(); detail.load('project-1');
    http.expectOne('/api/projects/project-1').flush(active);
    const tasks = http.expectOne('/api/projects/project-1/tasks');
    const board = http.expectOne('/api/projects/project-1/kanban');
    const schedule = http.expectOne('/api/projects/project-1/gantt');
    const workload = http.expectOne('/api/projects/project-1/workload');
    const members = http.expectOne('/api/projects/project-1/members');
    const recovery = workspaces.loadWorkspaces(true);
    selectFromHttp(); await recovery; await settle(); flushBoard();
    moveFirstCard(); const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
    oldMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 99 } }), warnings: [] });
    await settle();
    http.expectNone('/api/projects/project-1');
    http.expectNone((request) => request.url === '/api/projects/project-1/kanban');
    expect(detail.view().kanban.busyTaskId).toBe('task-1');
    expect(detail.view().kanban.reconciliationQueued).toBe(true);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    tasks.flush({ items: [] }); board.flush(snapshotDto()); schedule.flush(ganttSnapshotDto());
    workload.flush({ members: [] }); members.flush({ members: [] });
    moveFirstCard(); http.expectNone('/api/tasks/task-1/kanban-move');
    currentMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 8 } }), warnings: [] });
    await settle();
    expect(detail.view().kanban.snapshot?.boardVersion).toBe(8);
    http.expectOne((request) => request.url === '/api/projects/project-1/kanban').flush(
      snapshotDto({ board: { ...snapshotDto().board!, version: 9 } }),
    );
    for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
    expect(detail.view().kanban.snapshot?.boardVersion).toBe(9);
  });

  it.each([401, 403])('releases only the invalidated move when an overlapping schedule command returns %s', async (status) => {
    await mountBoard();
    moveFirstCard(); const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.applyGanttEdit({ kind: 'schedule', taskId: 'task-1',
      plannedStartDate: '2026-07-02', plannedEndDate: '2026-07-11', milestoneDate: null,
      expectedVersion: 3, source: 'form' });
    http.expectOne('/api/tasks/task-1/schedule').flush(
      { message: 'Schedule permission changed' }, { status, statusText: 'Denied' },
    );
    http.expectOne((request) => request.url === '/api/projects/project-1/kanban').flush(snapshotDto());
    oldMove.flush({ message: 'Old denied move' }, { status: 403, statusText: 'Forbidden' });
    expect(detail.view().kanban.busyTaskId).toBeNull();
    // A fresh explicit reload remains authorized by its current server responses.
    detail.load('project-1'); flushBoard(); moveFirstCard();
    http.expectOne('/api/tasks/task-1/kanban-move').flush(
      { message: 'Current failure' }, { status: 503, statusText: 'Unavailable' },
    );
    expect(detail.view().kanban.status).toBe('rollback');
  });

  for (const failedProjection of ['tasks', 'workload'] as const) {
    it.each([200, 503])(`retains fallback ownership after initial ${failedProjection} fails and the current move returns %s`, async (moveStatus) => {
      await mountBoard(); moveFirstCard();
      const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
      detail.release(); detail.load('project-1');
      http.expectOne('/api/projects/project-1').flush(active);
      const held = {
        tasks: http.expectOne('/api/projects/project-1/tasks'),
        board: http.expectOne('/api/projects/project-1/kanban'),
        schedule: http.expectOne('/api/projects/project-1/gantt'),
        workload: http.expectOne('/api/projects/project-1/workload'),
        members: http.expectOne('/api/projects/project-1/members'),
      };
      const recovery = workspaces.loadWorkspaces(true);
      selectFromHttp(); await recovery; await settle(); flushBoard();
      moveFirstCard(); const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
      oldMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 99 } }), warnings: [] });
      await settle();
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      held[failedProjection].flush({ message: 'Older initial projection unavailable' }, { status: 503, statusText: 'Unavailable' });
      const statusBeforeCompletion = detail.view().status;
      const busyBeforeCompletion = detail.view().kanban.busyTaskId;
      const queuedBeforeCompletion = detail.view().kanban.reconciliationQueued;
      if (moveStatus === 200) currentMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 8 } }), warnings: [] });
      else currentMove.flush({ message: 'Current failure' }, { status: 503, statusText: 'Unavailable' });
      await settle();
      const boards = http.match((request) => request.url === '/api/projects/project-1/kanban');
      for (const request of boards) request.flush(snapshotDto({ board: { ...snapshotDto().board!, version: 9 } }));
      for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
      expect(statusBeforeCompletion).toBe('ready');
      expect(busyBeforeCompletion).toBe('task-1');
      expect(queuedBeforeCompletion).toBe(true);
      expect(boards).toHaveLength(1);
      expect(detail.view().status).toBe('ready');
      expect(detail.view().kanban.snapshot?.boardVersion).toBe(9);
    });
  }

  it('retains the first-load error when no later authorized view has recovered', async () => {
    enableBoard(); detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    http.expectOne('/api/projects/project-1').flush(active);
    const tasks = http.expectOne('/api/projects/project-1/tasks');
    http.expectOne('/api/projects/project-1/kanban');
    http.expectOne('/api/projects/project-1/gantt');
    http.expectOne('/api/projects/project-1/workload');
    http.expectOne('/api/projects/project-1/members');
    tasks.flush({ message: 'Initial Task load unavailable' }, { status: 503, statusText: 'Unavailable' });
    expect(detail.view().status).toBe('error');
    expect(detail.view().project).toBeUndefined();
    expect(detail.view().kanban.snapshot).toBeNull();
  });

  it('reconciles a late successful move after a same-Project remount using fresh reads instead of the old response', async () => {
    await mountBoard(); moveFirstCard();
    const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.release(); detail.load('project-1'); flushBoard();
    oldMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 99 } }),
      focusTaskId: 'task-1', warnings: [] });
    await settle();
    expect(detail.view().kanban.snapshot?.boardVersion).toBe(7);
    const boards = http.match((request) => request.url === '/api/projects/project-1/kanban');
    const tasks = http.match('/api/projects/project-1/tasks');
    for (const request of boards) request.flush(snapshotDto({ board: { ...snapshotDto().board!, version: 8 } }));
    for (const request of tasks) request.flush({ items: [] });
    expect(boards).toHaveLength(1);
    expect(tasks).toHaveLength(1);
    expect(detail.view().kanban.snapshot?.boardVersion).toBe(8);
  });

  it('restarts an overlapping initial reload after an old move succeeds without applying either stale response', async () => {
    await mountBoard(); moveFirstCard();
    const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.load('project-1'); const staleRead = http.expectOne('/api/projects/project-1');
    oldMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 99 } }), warnings: [] });
    staleRead.flush(active);
    http.expectNone('/api/projects/project-1/tasks');
    flushBoard();
    expect(detail.view().kanban.snapshot?.boardVersion).toBe(7);
  });

  it('queues a late old move success behind a new command and drains the refresh after its failure', async () => {
    await mountBoard(); moveFirstCard();
    const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.load('project-1'); flushBoard(); moveFirstCard();
    const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
    oldMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 99 } }), warnings: [] });
    await settle();
    expect(detail.view().kanban.reconciliationQueued).toBe(true);
    http.expectNone((request) => request.url === '/api/projects/project-1/kanban');
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    moveFirstCard(); http.expectNone('/api/tasks/task-1/kanban-move');
    currentMove.flush({ message: 'Current failure' }, { status: 503, statusText: 'Unavailable' });
    http.expectOne((request) => request.url === '/api/projects/project-1/kanban').flush(snapshotDto());
    expect(detail.view().kanban.reconciliationQueued).toBe(false);
    expect(detail.view().kanban.snapshot?.boardVersion).toBe(7);
  });

  for (const refreshFirst of [true, false]) {
    it.each([200, 401, 403])(`keeps current move authority when an older recovery returns %s ${refreshFirst ? 'before' : 'after'} command completion`, async (status) => {
      await mountBoard(); moveFirstCard();
      const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
      detail.applyGanttEdit({ kind: 'schedule', taskId: 'task-1',
        plannedStartDate: '2026-07-02', plannedEndDate: '2026-07-11', milestoneDate: null,
        expectedVersion: 3, source: 'form' });
      http.expectOne('/api/tasks/task-1/schedule').flush(
        { message: 'Schedule denied' }, { status: 403, statusText: 'Forbidden' },
      );
      const recovery = http.expectOne((request) => request.url === '/api/projects/project-1/kanban');
      oldMove.flush({ message: 'Old move denied' }, { status: 403, statusText: 'Forbidden' });
      moveFirstCard(); const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
      const completeRecovery = () => {
        if (status === 200) recovery.flush(snapshotDto({ board: { ...snapshotDto().board!, version: 6 } }));
        else recovery.flush({ message: 'Current board authority denied' }, { status, statusText: 'Denied' });
      };
      if (refreshFirst) completeRecovery();
      if (refreshFirst && status === 200) {
        expect(detail.view().kanban.snapshot?.boardVersion).toBe(7);
        expect(detail.view().kanban.busyTaskId).toBe('task-1');
      }
      currentMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 8 } }), warnings: [] });
      if (!refreshFirst) completeRecovery();
      await settle();
      const resultingBoard = detail.view().kanban.snapshot?.boardVersion;
      const resultingStatus = detail.view().kanban.status;
      const followups = http.match((request) => request.url === '/api/projects/project-1/kanban');
      for (const request of followups) request.flush(snapshotDto({ board: { ...snapshotDto().board!, version: 9 } }));
      for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
      if (status === 200) {
        expect(resultingBoard).toBe(8);
        expect(followups).toHaveLength(1);
        expect(detail.view().kanban.snapshot?.boardVersion).toBe(9);
      } else {
        expect(resultingBoard).toBeUndefined();
        expect(resultingStatus).toBe('permissionDenied');
        expect(followups).toHaveLength(refreshFirst ? 0 : 1);
        expect(detail.view().kanban.snapshot).toBeNull();
        expect(detail.view().kanban.status).toBe('permissionDenied');
      }
      if (status !== 200) {
        http.expectOne('/api/projects/project-1').flush(
          { message: 'Fresh parent authority also denied' }, { status: 403, statusText: 'Denied' });
        expect(detail.view().kanban.snapshot).toBeNull();
        expect(detail.view().status).toBe('permissionDenied');
        http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
      }
    });
  }

  it.each([200, 503])('queues explicit Board refresh during a move until its %s completion', async (status) => {
    await mountBoard(); moveFirstCard();
    const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.retryKanban();
    const premature = http.match((request) => request.url === '/api/projects/project-1/kanban');
    for (const request of premature) request.flush(snapshotDto({ board: { ...snapshotDto().board!, version: 6 } }));
    const busyBeforeCompletion = detail.view().kanban.busyTaskId;
    if (status === 200) currentMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 8 } }), warnings: [] });
    else currentMove.flush({ message: 'Current failure' }, { status, statusText: 'Unavailable' });
    await settle();
    const currentReads = http.match((request) => request.url === '/api/projects/project-1/kanban');
    for (const request of currentReads) request.flush(snapshotDto({ board: { ...snapshotDto().board!, version: 9 } }));
    for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
    expect(premature).toHaveLength(0);
    expect(busyBeforeCompletion).toBe('task-1');
    expect(currentReads).toHaveLength(1);
    expect(detail.view().kanban.snapshot?.boardVersion).toBe(9);
  });

  it('retains a presentation filter selected during a move for its deferred authoritative read', async () => {
    await mountBoard(); moveFirstCard();
    const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.setIncludeOlderCompleted(true);
    const premature = http.match((request) => request.url === '/api/projects/project-1/kanban');
    for (const request of premature) request.flush(snapshotDto({ board: { ...snapshotDto().board!, includesOlderCompleted: true } }));
    currentMove.flush({ snapshot: snapshotDto({ board: { ...snapshotDto().board!, version: 8 } }), warnings: [] });
    await settle();
    const currentReads = http.match((request) => request.url === '/api/projects/project-1/kanban');
    const filterValues = currentReads.map((request) => request.request.params.get('includeOlderCompleted'));
    for (const request of currentReads) request.flush(snapshotDto({ board: { ...snapshotDto().board!, includesOlderCompleted: true, version: 9 } }));
    for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
    expect(premature).toHaveLength(0);
    expect(filterValues).toEqual(['true']);
    expect(detail.view().kanban.snapshot?.includesOlderCompleted).toBe(true);
  });

  it.each([200, 404, 401, 403])('drains a queued refresh after a moved Task disappears and handles Board %s independently', async (status) => {
    await mountBoard(); moveFirstCard();
    const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.retryKanban();
    http.expectNone((request) => request.url === '/api/projects/project-1/kanban');
    currentMove.flush({ message: 'Task no longer available' }, { status: 404, statusText: 'Not Found' });
    const reads = http.match((request) => request.url === '/api/projects/project-1/kanban');
    for (const request of reads) {
      if (status === 200) request.flush(snapshotDto({ board: { ...snapshotDto().board!, version: 9 } }));
      else request.flush({ message: 'Board unavailable' }, { status, statusText: 'Unavailable' });
    }
    expect(reads).toHaveLength(1);
    if (status === 200) expect(detail.view().kanban.snapshot?.boardVersion).toBe(9);
    else {
      expect(detail.view().kanban.snapshot).toBeNull();
      expect(detail.view().kanban.status).toBe(status === 404 ? 'notFound' : 'permissionDenied');
    }
    if (status === 401 || status === 403) {
      http.expectOne('/api/projects/project-1').flush(
        { message: 'Fresh parent unavailable' }, { status: 403, statusText: 'Denied' });
      expect(detail.view().kanban.snapshot).toBeNull();
      http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
    }
  });

  it.each(['draft', 'active'] as const)('loads a fresh %s after the actual initial Workspace HTTP selection', async (kind) => {
    detail.load('project-1');
    const old = http.expectOne('/api/projects/project-1');
    selectFromHttp(); old.flush(draft);
    expect(detail.view().project).toBeUndefined();
    await settle();
    http.expectOne('/api/projects/project-1').flush(kind === 'draft' ? draft : active);
    if (kind === 'active') flushActiveCollections();
    expect(detail.view().status).toBe('ready');
    expect(history.touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
    expect(start).not.toHaveBeenCalled();
    expect(realtime.connectionState()).toBe('Degraded');
  });

  it('retains fresh HTTP fallback denial over an older initial success in the same scope', async () => {
    detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    const initial = http.expectOne('/api/projects/project-1');
    const recovered = workspaces.loadWorkspaces(true);
    selectFromHttp(); await recovered; await settle();
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Fresh access denied' }, { status: 403, statusText: 'Forbidden' },
    );
    expect(detail.view().status).toBe('permissionDenied');
    initial.flush(draft);
    expect(detail.view().status).toBe('permissionDenied');
    expect(detail.view().project).toBeUndefined();
    expect(history.touchProject).not.toHaveBeenCalled();
  });

  it('does not revive a route released before the hydration effect', async () => {
    detail.load('project-1'); const old = http.expectOne('/api/projects/project-1');
    selectFromHttp(); detail.release(); old.flush(draft); await settle();
    http.expectNone('/api/projects/project-1');
    expect(detail.view().project).toBeUndefined();
    expect(history.touchProject).not.toHaveBeenCalled();
  });

  it('retains an authorized initial result after a transient fallback error', async () => {
    detail.load('project-1'); const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    const initial = http.expectOne('/api/projects/project-1');
    const recovered = workspaces.loadWorkspaces(true);
    selectFromHttp(); await recovered; await settle();
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Temporary service failure' }, { status: 503, statusText: 'Unavailable' },
    );
    expect(detail.view().status).toBe('error');
    initial.flush(draft);
    expect(detail.view().status).toBe('ready');
    expect(history.touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
  });

  it.each([401, 403])('retains initial %s denial over an older catch-up and permits a new authorized recovery', async (status) => {
    detail.load('project-1'); const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    const initial = http.expectOne('/api/projects/project-1');
    const recovered = workspaces.loadWorkspaces(true);
    selectFromHttp(); await recovered; await settle();
    const catchUp = http.expectOne('/api/projects/project-1');
    initial.flush({ message: 'Initial access denied' }, { status, statusText: 'Denied' });
    expect(detail.view().status).toBe('permissionDenied');
    catchUp.flush(draft);
    expect(detail.view().status).toBe('permissionDenied');
    expect(detail.view().project).toBeUndefined();
    expect(history.touchProject).not.toHaveBeenCalled();

    const authorized = workspaces.loadWorkspaces(true);
    selectFromHttp(); await authorized; await settle();
    http.expectOne('/api/projects/project-1').flush(draft);
    expect(detail.view().status).toBe('ready');
    expect(detail.view().project?.id).toBe('project-1');
  });

  it('permits an authorized catch-up after an initial transient failure', async () => {
    detail.load('project-1'); const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    const initial = http.expectOne('/api/projects/project-1');
    const recovered = workspaces.loadWorkspaces(true);
    selectFromHttp(); await recovered; await settle();
    const catchUp = http.expectOne('/api/projects/project-1');
    initial.flush({ message: 'Temporary failure' }, { status: 503, statusText: 'Unavailable' });
    expect(detail.view().status).toBe('error');
    catchUp.flush(draft);
    expect(detail.view().status).toBe('ready');
    expect(detail.view().project?.id).toBe('project-1');
  });

  it.each([401, 403])('invalidates overlapping recoveries on a repeated %s denial and permits a later fresh read', async (status) => {
    detail.load('project-1'); const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Initial denial' }, { status: 403, statusText: 'Forbidden' },
    );
    const first = workspaces.loadWorkspaces(true);
    selectFromHttp(); await first; await settle();
    const older = http.expectOne('/api/projects/project-1');
    const second = workspaces.loadWorkspaces(true);
    selectFromHttp(); await second; await settle();
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Repeated denial' }, { status, statusText: 'Denied' },
    );
    older.flush(draft);
    expect(detail.view().status).toBe('permissionDenied');
    expect(detail.view().project).toBeUndefined();
    const current = workspaces.loadWorkspaces(true);
    selectFromHttp(); await current; await settle();
    http.expectOne('/api/projects/project-1').flush(draft);
    expect(detail.view().status).toBe('ready');
  });

  it.each([400, 404, 503])('preserves existing denial when a fallback returns %s', async (status) => {
    detail.load('project-1'); const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Authoritative denial' }, { status: 403, statusText: 'Forbidden' },
    );
    const message = detail.view().message;
    const fallback = workspaces.loadWorkspaces(true);
    selectFromHttp(); await fallback; await settle();
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Later failure' }, { status, statusText: 'Unavailable' },
    );
    expect(detail.view().status).toBe('permissionDenied');
    expect(detail.view().message).toBe(message);
    expect(detail.view().project).toBeUndefined();
  });

  it.each([403, 503])('keeps a new Kanban move owned when an old move returns %s after denial and recovery', async (status) => {
    TestBed.inject(FrontendFeatureFlagsService).setForTesting({
      'realtime.signalR': false, 'tasks.kanbanV1': true, 'tasks.ganttV1': false,
    });
    const base = snapshotDto();
    const board = snapshotDto({
      board: { ...base.board!, totalAuthorizedCardCount: 2 },
      cards: [base.cards![0], { ...base.cards![0], taskId: 'task-2', summary: 'Second card' }],
    });
    const flushBoard = () => {
      http.expectOne('/api/projects/project-1').flush(active);
      flushActiveCollections();
      http.expectOne('/api/projects/project-1/kanban').flush(board);
    };
    const move = (index: number) => detail.moveTask({
      item: detail.view().kanban.snapshot!.cards.find((card) => card.taskId === `task-${index + 1}`)!,
      targetStatus: 'stage-done',
      targetBeforeItemId: null, targetAfterItemId: null, reason: null, source: 'keyboard',
    });
    detail.load('project-1'); const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle(); flushBoard();
    move(0); const oldMove = http.expectOne('/api/tasks/task-1/kanban-move');
    detail.load('project-1');
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Current denial' }, { status: 403, statusText: 'Forbidden' },
    );
    detail.load('project-1'); flushBoard();
    move(0); const currentMove = http.expectOne('/api/tasks/task-1/kanban-move');
    oldMove.flush({ message: 'Old result' }, { status, statusText: 'Unavailable' });
    const earlyRefreshes = http.match((request) => request.url === '/api/projects/project-1/kanban');
    move(1);
    const unexpectedMoves = http.match('/api/tasks/task-2/kanban-move');
    for (const request of earlyRefreshes) request.flush(board);
    currentMove.flush({ message: 'Temporary move failure' }, { status: 503, statusText: 'Unavailable' });
    const ownedRefreshes = http.match((request) => request.url === '/api/projects/project-1/kanban');
    for (const request of ownedRefreshes) request.flush(board);
    detail.release();
    for (const request of unexpectedMoves) request.flush(
      { message: 'Discarded extra request' }, { status: 503, statusText: 'Unavailable' },
    );
    expect(earlyRefreshes).toHaveLength(0);
    expect(unexpectedMoves).toHaveLength(0);
    expect(ownedRefreshes).toHaveLength(0);
  });

  it('keeps the explicit same-Project reload instead of duplicating it during hydration', async () => {
    detail.load('project-1'); const old = http.expectOne('/api/projects/project-1');
    selectFromHttp(); detail.load('project-1');
    const current = http.expectOne('/api/projects/project-1');
    await settle(); http.expectNone('/api/projects/project-1');
    old.flush(draft); expect(history.touchProject).not.toHaveBeenCalled();
    current.flush(draft);
    expect(detail.view().project?.id).toBe('project-1');
    expect(history.touchProject).toHaveBeenCalledOnce();
  });

  it('rejects an invalidated initial response and recovers through current HTTP authorization', async () => {
    detail.load('project-1'); const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle();
    const initial = http.expectOne('/api/projects/project-1');
    invalidations.next();
    expect(detail.view().status).toBe('permissionDenied');
    initial.flush(draft);
    expect(detail.view().project).toBeUndefined();
    expect(history.touchProject).not.toHaveBeenCalled();
    await settle();
    http.expectOne('/api/auth/status').flush({
      isAuthenticated: true, user: DEFAULT_AUTH_SESSION.currentUser,
    });
    await settle(); selectFromHttp(); await settle();
    http.expectOne('/api/projects/project-1').flush(draft);
    expect(detail.view().status).toBe('ready');
    expect(detail.view().project?.id).toBe('project-1');
    expect(start).not.toHaveBeenCalled();
  });
});
