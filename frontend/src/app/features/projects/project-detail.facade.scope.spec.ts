import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { ProjectDetailFacade } from './project-detail.facade';
import { ganttSnapshotDto } from './project-detail-gantt.test-data';
import { snapshotDto } from './project-kanban.test-data';
import { ProjectDto } from './projects.api';

const draft: ProjectDto = {
  id: 'project-1', workspaceId: 'workspace-1', groupId: null, ownerUserId: 'owner-1',
  title: 'Authorized draft', description: null, status: 0, visibility: 1,
  activationState: 1, activatedAtUtc: null, activationVersion: null, versionNo: 7,
  startDate: null, endDate: null, uiPermissions: { canCreateTask: false, canActivate: true },
};
const active: ProjectDto = {
  ...draft, status: 1, activationState: 2, activatedAtUtc: '2026-08-24T00:00:00Z',
  activationVersion: 1, uiPermissions: { canCreateTask: true, canActivate: false },
};

describe('ProjectDetailFacade initial response scope', () => {
  let facade: ProjectDetailFacade;
  let workspace: ActiveWorkspaceFacade;
  let http: HttpTestingController;
  let events: Subject<DurableRealtimeEvent>;
  const history = { touchProject: vi.fn() };

  beforeEach(() => {
    history.touchProject.mockReset();
    events = new Subject<DurableRealtimeEvent>();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: RealtimeFacade, useValue: {
        durableEvents$: events.asObservable(), connectionState: signal('Connected'),
        registerSubscription: () => () => {}, registerCatchUp: () => () => {},
        registerProtectedStateClearer: () => () => {},
      } },
      { provide: FrontendFeatureFlagsService, useValue: {
        kanbanV1Enabled: () => false, ganttV1Enabled: () => false,
      } },
      { provide: ContinueWorkingHistoryService, useValue: history },
    ] });
    workspace = TestBed.inject(ActiveWorkspaceFacade);
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    facade = TestBed.inject(ProjectDetailFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    facade.release(); http.verify(); TestBed.resetTestingModule();
  });

  function enableViews() {
    const flags = TestBed.inject(FrontendFeatureFlagsService);
    vi.spyOn(flags, 'kanbanV1Enabled').mockReturnValue(true);
    vi.spyOn(flags, 'ganttV1Enabled').mockReturnValue(true);
  }

  function emitCurrent(type: DurableRealtimeEvent['eventType']) {
    events.next({ eventId: `current-${type}`, eventType: type, payloadSchemaVersion: 1,
      occurredAt: '2026-11-06T00:00:00Z', tenantId: 'tenant-1', aggregateType: 'Project',
      aggregateId: 'project-1', aggregateVersion: 1, actor: { actorType: 'System', actorId: null },
      correlationId: null, causationId: null, payload: { projectId: 'project-1', taskId: 'task-1', taskVersion: 100 } });
  }

  function finishNavigation() {
    http.expectOne('/api/projects/project-1').flush(active);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
  }

  function finishViews() {
    http.expectOne(request => request.url === '/api/projects/project-1/kanban').flush(snapshotDto());
    http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
  }

  for (const inFlight of [false, true]) {
    it.each([401, 403])(`releases Task refresh ownership after schedule command %s with inFlight=${inFlight}`, async (status) => {
      enableViews(); facade.load('project-1'); finishNavigation(); finishViews();
      facade.applyGanttEdit({ kind: 'progress', taskId: 'task-1', progressPercent: 40, expectedVersion: 3, source: 'form' });
      const command = http.expectOne('/api/tasks/task-1/progress');
      facade.retryTaskList();
      if (inFlight) await Promise.resolve();
      const oldRequests = http.match('/api/projects/project-1/tasks');
      expect(oldRequests).toHaveLength(inFlight ? 1 : 0);
      command.flush({ message: 'Schedule authority changed' }, { status, statusText: 'Denied' });
      await Promise.resolve();
      http.expectNone('/api/projects/project-1/tasks');
      expect(facade.view().schedule.status).toBe('permissionDenied');
      expect(facade.view().schedule.snapshot).toBeNull();

      facade.retryTaskList(); await Promise.resolve();
      const current = http.expectOne('/api/projects/project-1/tasks');
      for (const old of oldRequests) old.flush({ items: [] });
      facade.retryTaskList(); await Promise.resolve();
      http.expectNone('/api/projects/project-1/tasks');
      current.flush({ items: [] }); await Promise.resolve();
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      expect(facade.view().status).toBe('ready');
      expect(facade.view().schedule.status).toBe('permissionDenied');
      expect(facade.view().schedule.snapshot).toBeNull();
      http.expectNone('/api/projects/project-1/gantt');
    });
  }

  for (const type of ['Projects.TaskChanged.v1', 'Projects.ProjectChanged.v1'] as const) {
  it.each([true, false])(`keeps ${type} behind pending authorization collections with childFirst=%s`, async (childFirst) => {
    enableViews(); facade.load('project-1'); finishNavigation(); finishViews();
    emitCurrent('Security.AuthorizationStateChanged.v1'); await Promise.resolve();
    http.expectOne('/api/projects/project-1').flush(active);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    const workload = http.expectOne('/api/projects/project-1/workload');
    const gatedBoard = http.expectOne(request => request.url === '/api/projects/project-1/kanban');
    const gatedSchedule = http.expectOne('/api/projects/project-1/gantt');
    emitCurrent(type); await Promise.resolve();
    const extraParents = http.match('/api/projects/project-1');
    for (const parent of extraParents) {
      parent.flush(active);
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
      http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    }
    const bypass = http.match(request => request.url === '/api/projects/project-1/kanban' || request.url === '/api/projects/project-1/gantt');
    for (const task of http.match('/api/projects/project-1/tasks')) task.flush({ items: [] });
    const completeBypass = () => {
      for (const request of bypass) request.flush(request.request.url.endsWith('/kanban') ? snapshotDto() : ganttSnapshotDto());
    };
    if (childFirst) {completeBypass();}
    const earlyBoard = facade.view().kanban.snapshot, earlySchedule = facade.view().schedule.snapshot;
    workload.flush({ message: 'Current collections unavailable' }, { status: 503, statusText: 'Unavailable' });
    if (!childFirst) {completeBypass();}
    gatedBoard.flush(snapshotDto()); gatedSchedule.flush(ganttSnapshotDto());
    const failedBoard = facade.view().kanban.snapshot, failedSchedule = facade.view().schedule.snapshot;
    emitCurrent('Security.AuthorizationStateChanged.v1'); await Promise.resolve(); finishNavigation();
    const laterBoard = facade.view().kanban.snapshot, laterSchedule = facade.view().schedule.snapshot;
    finishViews();
    expect(bypass).toHaveLength(0);
    expect(extraParents).toHaveLength(0);
    expect(earlyBoard).toBeNull(); expect(earlySchedule).toBeNull();
    expect(failedBoard).toBeNull(); expect(failedSchedule).toBeNull();
    expect(laterBoard).toBeNull(); expect(laterSchedule).toBeNull();
    expect(facade.view().status).toBe('ready');
  });
  }

  for (const type of ['Projects.TaskChanged.v1', 'Projects.ProjectChanged.v1'] as const) {
  it.each([
    { lateSuccess: false, boundary: 'none' },
    { lateSuccess: true, boundary: 'none' },
    { lateSuccess: true, boundary: 'cleared' },
    { lateSuccess: true, boundary: 'replaced' },
  ] as const)(`keeps ${type} behind the initial join with lateSuccess=$lateSuccess, boundary=$boundary`, async ({ lateSuccess, boundary }) => {
      enableViews(); facade.load('project-1'); http.expectOne('/api/projects/project-1').flush(active);
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      http.expectOne('/api/projects/project-1/members').flush({ members: [] });
      const workload = http.expectOne('/api/projects/project-1/workload');
      const board = http.expectOne(request => request.url === '/api/projects/project-1/kanban');
      const schedule = http.expectOne('/api/projects/project-1/gantt');
      emitCurrent(type); await Promise.resolve();
      const extras = [];
      for (let round = 0; round < 3; round++) {
        const requests = http.match(request => request.url.startsWith('/api/projects/project-1'));
        extras.push(...requests);
        for (const request of requests) {
          const url = request.request.url;
          if (!lateSuccess) request.flush(url.endsWith('/kanban') ? snapshotDto() : url.endsWith('/gantt') ? ganttSnapshotDto()
            : url.endsWith('/tasks') ? taskRows : url.endsWith('/workload') || url.endsWith('/members') ? { members: [] } : active);
        }
      }
      const earlyBoard = facade.view().kanban.snapshot, earlySchedule = facade.view().schedule.snapshot;
      board.flush(snapshotDto()); schedule.flush(ganttSnapshotDto());
      workload.flush({ message: 'Initial collections unavailable' }, { status: 503, statusText: 'Unavailable' });
      if (boundary === 'cleared') workspace.clearWorkspace();
      else if (boundary === 'replaced') workspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace 2' });
      if (lateSuccess) for (const request of extras) {
        const url = request.request.url;
        request.flush(url.endsWith('/kanban') ? snapshotDto() : url.endsWith('/gantt') ? ganttSnapshotDto()
          : url.endsWith('/tasks') ? taskRows : url.endsWith('/workload') || url.endsWith('/members') ? { members: [] } : active);
      }
      const failedBoard = facade.view().kanban.snapshot, failedSchedule = facade.view().schedule.snapshot;
      const failedTasks = facade.view().tasks;
      workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
      facade.load('project-1'); finishNavigation(); finishViews();
      // Preserve the original concurrent refresh contract; successful responses
      // cannot become visible until a complete current join authorizes them.
      expect(extras.length).toBeGreaterThan(0);
      expect(earlyBoard).toBeNull(); expect(earlySchedule).toBeNull();
      expect(failedBoard).toBeNull(); expect(failedSchedule).toBeNull(); expect(failedTasks).toEqual([]);
      expect(facade.view().status).toBe('ready');
      expect(facade.view().kanban.snapshot).not.toBeNull();
    });
  }

  const taskRows = { items: [{ id: 'task-1', projectId: 'project-1', title: 'Scoped Task',
    workflowStageId: 'stage-progress', workflowStageName: 'In progress', stageCategory: 'InProgress',
    status: 1, isBlocked: false, priority: 'Medium', progressPercent: 25,
    createdAt: '2026-08-20T09:00:00Z', updatedAt: '2026-08-21T09:00:00Z', hasArtifact: false, version: 3 }] };

  for (const queued of [true, false]) {
    it.each(['cleared', 'replaced'] as const)(`discards an ordinary Task-list read when Workspace is %s with queued=${queued}`, async (boundary) => {
      enableViews(); facade.load('project-1'); finishNavigation(); finishViews();
      facade.retryTaskList();
      if (!queued) {await Promise.resolve();}
      const held = queued ? [] : [http.expectOne('/api/projects/project-1/tasks')];
      if (boundary === 'cleared') workspace.clearWorkspace();
      else workspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace 2' });
      await Promise.resolve();
      const unexpected = http.match('/api/projects/project-1/tasks');
      for (const request of [...held, ...unexpected]) request.flush(taskRows);
      expect(unexpected).toHaveLength(0);
      expect(facade.view().tasks).toEqual([]);
      http.expectNone(() => true);
    });
    it(`retains a same-Workspace Task-list label refresh with queued=${queued}`, async () => {
      enableViews(); facade.load('project-1'); finishNavigation(); finishViews();
      facade.retryTaskList();
      if (!queued) {await Promise.resolve();}
      workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Refreshed label' });
      await Promise.resolve();
      http.expectOne('/api/projects/project-1/tasks').flush(taskRows);
      expect(facade.view().tasks[0].title).toBe('Scoped Task');
      http.expectNone(() => true);
    });
  }

  it('releases the old Schedule owner after activation reconciliation advances its response generation', async () => {
    enableViews(); facade.load('project-1'); http.expectOne('/api/projects/project-1').flush(draft);
    facade.activate(); const activation = http.expectOne('/api/projects/project-1/activate');
    emitCurrent('Projects.ProjectChanged.v1'); await Promise.resolve(); finishNavigation();
    http.expectOne(request => request.url === '/api/projects/project-1/kanban').flush(snapshotDto());
    const oldSchedule = http.expectOne('/api/projects/project-1/gantt');
    activation.flush({ requestId: 'synthetic-activation', data: { projectId: 'project-1' }, warnings: [] });
    finishNavigation(); finishViews();
    oldSchedule.flush({ ...ganttSnapshotDto(), projectVersion: 99 });
    facade.retrySchedule();
    http.expectOne('/api/projects/project-1/gantt').flush({ ...ganttSnapshotDto(), projectVersion: 37 });
    expect(facade.view().schedule.snapshot?.projectVersion).toBe(37);
    expect(facade.view().activation.status).toBe('success');
  });

  for (const kind of ['active', 'draft'] as const) {
    it.each(['cleared', 'replaced'] as const)(
      `discards the held ${kind} response when Workspace is %s before teardown`,
      (change) => {
        facade.load('project-1');
        const pending = http.expectOne('/api/projects/project-1');
        if (change === 'cleared') workspace.clearWorkspace();
        else workspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace 2' });

        pending.flush(kind === 'active' ? active : draft);
        // Drain only to preserve the original failure rather than an afterEach leak.
        const followups = http.match(() => true);
        for (const request of followups) request.flush({ items: [], members: [] });

        expect(followups).toHaveLength(0);
        expect(facade.view().project).toBeUndefined();
        expect(history.touchProject).not.toHaveBeenCalled();
      },
    );
  }

  it('retains a held Draft response during a label refresh of the same Workspace', () => {
    facade.load('project-1');
    const pending = http.expectOne('/api/projects/project-1');
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Refreshed label' });
    pending.flush(draft);
    expect(facade.view().project?.id).toBe('project-1');
    expect(facade.view().status).toBe('ready');
    expect(history.touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
    http.expectNone(() => true);
  });

  it.each(['cleared', 'replaced'] as const)(
    'discards completed operational collections when Workspace is %s during the reads',
    (change) => {
      facade.load('project-1');
      http.expectOne('/api/projects/project-1').flush(active);
      const tasks = http.expectOne('/api/projects/project-1/tasks');
      const gantt = http.expectOne('/api/projects/project-1/gantt');
      const workload = http.expectOne('/api/projects/project-1/workload');
      const members = http.expectOne('/api/projects/project-1/members');
      if (change === 'cleared') workspace.clearWorkspace();
      else workspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace 2' });
      tasks.flush({ items: [] }); gantt.flush(ganttSnapshotDto());
      workload.flush({ members: [] }); members.flush({ members: [] });
      expect(facade.view().project).toBeUndefined();
      expect(history.touchProject).not.toHaveBeenCalled();
      http.expectNone(() => true);
    },
  );

  it('keeps operational reads and application for a label refresh of the same Workspace', () => {
    facade.load('project-1');
    const project = http.expectOne('/api/projects/project-1');
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Refreshed label' });
    project.flush(active);
    http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
    http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
    http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    expect(facade.view().project?.id).toBe('project-1');
    expect(facade.view().status).toBe('ready');
    expect(history.touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
    http.expectNone(() => true);
  });

  it.each(['active', 'draft'] as const)(
    'reauthorizes a cold %s route after initial Workspace hydration',
    (kind) => {
      workspace.clearWorkspace();
      facade.load('project-1');
      const old = http.expectOne('/api/projects/project-1');
      workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Hydrated workspace' });
      old.flush(kind === 'active' ? active : draft);
      expect(facade.view().project).toBeUndefined();
      expect(history.touchProject).not.toHaveBeenCalled();
      http.expectNone(() => true);

      TestBed.tick();
      const fresh = http.expectOne('/api/projects/project-1');
      fresh.flush(kind === 'active' ? active : draft);
      if (kind === 'active') {
        http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
        http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
        http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
        http.expectOne('/api/projects/project-1/members').flush({ members: [] });
      }
      expect(facade.view().project?.id).toBe('project-1');
      expect(facade.view().status).toBe('ready');
      expect(history.touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
      http.expectNone(() => true);
    },
  );

  for (const deniedProjection of ['kanban', 'gantt'] as const) {
    for (const eventType of ['Projects.TaskChanged.v1', 'Projects.ProjectChanged.v1'] as const) {
      it.each([401, 403])(`keeps ${deniedProjection} denial through ${eventType} during navigation recovery after %s`, async (status) => {
        const flags = TestBed.inject(FrontendFeatureFlagsService);
        vi.spyOn(flags, 'kanbanV1Enabled').mockReturnValue(true);
        vi.spyOn(flags, 'ganttV1Enabled').mockReturnValue(true);
        facade.load('project-1');
        http.expectOne('/api/projects/project-1').flush(active);
        const oldTasks = http.expectOne('/api/projects/project-1/tasks');
        const oldWorkload = http.expectOne('/api/projects/project-1/workload');
        const oldMembers = http.expectOne('/api/projects/project-1/members');
        const oldBoard = http.expectOne('/api/projects/project-1/kanban');
        const oldGantt = http.expectOne('/api/projects/project-1/gantt');
        (deniedProjection === 'kanban' ? oldBoard : oldGantt).flush(
          { message: 'Initial projection denied' }, { status, statusText: 'Denied' },
        );
        http.expectOne('/api/projects/project-1').flush(active);
        const tasks = http.expectOne('/api/projects/project-1/tasks');
        const workload = http.expectOne('/api/projects/project-1/workload');
        const members = http.expectOne('/api/projects/project-1/members');
        const emit = (type: DurableRealtimeEvent['eventType']) => events.next({
          eventId: 'recovery-event', eventType: type, payloadSchemaVersion: 1,
          occurredAt: '2026-11-06T00:00:00Z', tenantId: 'tenant-1', aggregateType: 'Project',
          aggregateId: 'project-1', aggregateVersion: 1, actor: { actorType: 'System', actorId: null },
          correlationId: null, causationId: null, payload: { projectId: 'project-1', taskId: 'task-1', taskVersion: 100 },
        });
        emit(eventType); await Promise.resolve();
        const premature = http.match(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
        for (const request of premature) request.flush(request.request.url.endsWith('/kanban') ? snapshotDto() : ganttSnapshotDto());
        for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
        expect(premature).toHaveLength(0);
        expect(facade.view().status).toBe('permissionDenied');
        tasks.flush({ items: [] }); workload.flush({ members: [] }); members.flush({ members: [] });
        oldTasks.flush({ items: [] }); oldWorkload.flush({ members: [] }); oldMembers.flush({ members: [] });
        if (deniedProjection === 'kanban') oldGantt.flush(ganttSnapshotDto());
        else oldBoard.flush(snapshotDto());
        emit(eventType); await Promise.resolve();
        http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));
        for (const request of http.match('/api/projects/project-1/tasks')) request.flush({ items: [] });
        expect(facade.view().status).toBe('ready');
        expect(facade.view().kanban.snapshot).toBeNull(); expect(facade.view().schedule.snapshot).toBeNull();
        expect(facade.view().kanban.status).toBe('permissionDenied'); expect(facade.view().schedule.status).toBe('permissionDenied');

        // Explicit authority invalidation still starts fresh operational reads.
        emit('Security.AuthorizationStateChanged.v1'); await Promise.resolve();
        http.expectOne('/api/projects/project-1').flush(active);
        http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
        http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
        http.expectOne('/api/projects/project-1/members').flush({ members: [] });
        http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')).flush(snapshotDto());
        http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
        expect(facade.view().status).toBe('ready');
        expect(facade.view().kanban.snapshot).not.toBeNull(); expect(facade.view().schedule.snapshot).not.toBeNull();
      });
    }
  }

  for (const interaction of [false, true]) {
  it.each([401, 403])(`discards an already queued ordinary Gantt refresh after current %s with interaction=${interaction} and permits explicit authorization recovery`, async (status) => {
    const flags = TestBed.inject(FrontendFeatureFlagsService);
    vi.spyOn(flags, 'kanbanV1Enabled').mockReturnValue(true);
    vi.spyOn(flags, 'ganttV1Enabled').mockReturnValue(true);
    facade.load('project-1');
    http.expectOne('/api/projects/project-1').flush(active);
    const finishCollections = () => {
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
      http.expectOne('/api/projects/project-1/workload').flush({ members: [] });
      http.expectOne('/api/projects/project-1/members').flush({ members: [] });
    };
    finishCollections();
    http.expectOne('/api/projects/project-1/kanban').flush(snapshotDto());
    http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
    const taskEvent: DurableRealtimeEvent = {
      eventId: 'queued-schedule-event', eventType: 'Projects.TaskChanged.v1', payloadSchemaVersion: 1,
      occurredAt: '2026-11-06T00:00:00Z', tenantId: 'tenant-1', aggregateType: 'Project',
      aggregateId: 'project-1', aggregateVersion: 1, actor: { actorType: 'System', actorId: null },
      correlationId: null, causationId: null, payload: { projectId: 'project-1', taskId: 'task-1', taskVersion: 100 },
    };
    const finishOrdinarySiblings = () => {
      http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')).flush(snapshotDto());
      http.expectOne('/api/projects/project-1/tasks').flush({ items: [] });
    };
    events.next(taskEvent); await Promise.resolve();
    const pending = http.expectOne('/api/projects/project-1/gantt'); finishOrdinarySiblings();
    events.next({ ...taskEvent, eventId: 'queued-schedule-followup' }); await Promise.resolve();
    finishOrdinarySiblings();
    if (interaction) facade.setScheduleInteractionActive(true);
    pending.flush({ message: 'Current Gantt denied' }, { status, statusText: 'Denied' });
    expect(facade.view().schedule.status).toBe('permissionDenied');
    expect(facade.view().schedule.snapshot).toBeNull();
    if (interaction) facade.setScheduleInteractionActive(false);
    await Promise.resolve();
    const forbiddenRetry = http.match('/api/projects/project-1/gantt');
    for (const request of forbiddenRetry) request.flush(ganttSnapshotDto());
    expect(forbiddenRetry).toHaveLength(0);
    expect(facade.view().schedule.snapshot).toBeNull();

    // Current denial restores navigation only through fresh parent authority.
    // Closing the editor and queued data events must not retry the Gantt read.
    http.expectOne('/api/projects/project-1').flush(active); finishCollections();
    expect(facade.view().status).toBe('ready');
    expect(facade.view().kanban.status).toBe('permissionDenied');
    expect(facade.view().schedule.status).toBe('permissionDenied');
    http.expectNone(request => request.url.endsWith('/kanban') || request.url.endsWith('/gantt'));

    events.next({ ...taskEvent, eventId: 'fresh-authority-event', eventType: 'Security.AuthorizationStateChanged.v1' });
    await Promise.resolve();
    http.expectOne('/api/projects/project-1').flush(active); finishCollections();
    http.expectOne(request => request.url.startsWith('/api/projects/project-1/kanban')).flush(snapshotDto());
    http.expectOne('/api/projects/project-1/gantt').flush(ganttSnapshotDto());
    expect(facade.view().status).toBe('ready'); expect(facade.view().schedule.snapshot).not.toBeNull();
  });
  }

  it('uses the fresh authorization denial after initial Workspace hydration', () => {
    workspace.clearWorkspace();
    facade.load('project-1');
    const old = http.expectOne('/api/projects/project-1');
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Hydrated workspace' });
    old.flush({ message: 'Previous request denied' }, { status: 401, statusText: 'Unauthorized' });
    expect(facade.view().status).toBe('loading');

    TestBed.tick();
    http.expectOne('/api/projects/project-1').flush(
      { message: 'Current request denied' }, { status: 403, statusText: 'Forbidden' },
    );
    expect(facade.view().status).toBe('permissionDenied');
    expect(facade.view().project).toBeUndefined();
    expect(history.touchProject).not.toHaveBeenCalled();
    http.expectNone(() => true);
  });
});
