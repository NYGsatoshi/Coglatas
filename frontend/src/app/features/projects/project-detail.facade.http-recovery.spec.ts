import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { COGLATAS_REALTIME_TRANSPORT } from '../../core/realtime/realtime-transport';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { COGLATAS_WORKSPACE_PREFERENCE_STORAGE } from '../../core/workspace/workspace-preference.service';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { WorkspacesFacade } from '../workspaces/workspaces.facade';
import { ganttSnapshotDto } from './project-detail-gantt.test-data';
import { ProjectDetailFacade } from './project-detail.facade';
import { snapshotDto } from './project-kanban.test-data';
import { ProjectDto } from './projects.api';

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
  const history = { touchProject: vi.fn() };
  const start = vi.fn(async () => {});

  beforeEach(() => {
    history.touchProject.mockReset(); start.mockClear(); invalidations = new Subject<void>();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: COGLATAS_WORKSPACE_PREFERENCE_STORAGE, useValue: {
        getItem: () => null, setItem: () => {}, removeItem: () => {},
      } },
      { provide: COGLATAS_REALTIME_TRANSPORT, useValue: {
        durableEvents$: EMPTY, authorizationInvalidations$: invalidations, statuses$: EMPTY,
        start, stop: async () => {},
        subscribe: async () => ({ allowed: true, code: 'Subscribed' }),
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
    http.expectOne('/api/projects/project-1/kanban').flush(snapshotDto());
  }

  async function mountBoard() {
    enableBoard(); detail.load('project-1');
    const cold = http.expectOne('/api/projects/project-1');
    selectFromHttp(); cold.flush(draft); await settle(); flushBoard();
  }

  function moveFirstCard() {
    detail.moveTask({ item: detail.view().kanban.snapshot!.cards[0],
      targetStatus: 'stage-done', targetBeforeItemId: null, targetAfterItemId: null,
      reason: null, source: 'keyboard' });
  }

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
    });
  }

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
