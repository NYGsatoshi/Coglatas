import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { EMPTY } from 'rxjs';

import { ProtectedStateClearer, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { ProjectsFacade } from './projects.facade';
import { TaskDto } from './projects.api';

const projectDto = {
  id: 'project-1',
  title: 'Backend Project',
  status: 1,
  startDate: '2026-07-01',
  endDate: '2026-07-31',
  uiPermissions: { canCreateTask: true }
};

const editableTaskDto: TaskDto = {
  id: 'task-1',
  workspaceId: 'workspace-1',
  projectId: 'project-1',
  title: 'Backend Task',
  description: 'Persisted detail',
  status: 1,
  priority: 2,
  startDate: '2026-07-02',
  dueDate: '2026-07-20',
  progressPercent: 30,
  uiPermissions: {
    canEdit: true,
    canAssign: true,
    canChangeStatus: false,
    canDelete: true,
    allowedTransitions: []
  }
};

describe('ProjectsFacade live API mutations', () => {
  let facade: ProjectsFacade;
  let httpMock: HttpTestingController;
  let activeWorkspace: ActiveWorkspaceFacade;
  const continueWorkingHistory = { touchProject: vi.fn() };

  beforeEach(() => {
    continueWorkingHistory.touchProject.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ContinueWorkingHistoryService, useValue: continueWorkingHistory }
      ]
    });
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    facade = TestBed.inject(ProjectsFacade);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  function expectProjectList(workspaceId = 'workspace-1') {
    return httpMock.expectOne((request) =>
      request.method === 'GET' &&
      request.url === '/api/projects' &&
      request.params.get('workspaceId') === workspaceId
    );
  }

  function expectNoProjectList(): void {
    httpMock.expectNone((request) => request.url === '/api/projects');
  }

  it('loads project tasks without requiring My Tasks endpoint', () => {
    flushInitialLoad();

    const projectRows = facade.getProjectsOverview().rows;

    expect(projectRows.map((row) => row.id)).toEqual(['task-1']);
    httpMock.expectNone('/api/me/tasks');
  });

  it('scopes the Project inventory to the active Workspace and cancels the old scope before switching', () => {
    // Realtime's anonymous-session test boundary may clear the root
    // ActiveWorkspace while the facade graph is being constructed. Reassert
    // the authorized test scope so this regression observes the production
    // Workspace transition instead of relying on effect scheduling order.
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    const workspaceOne = expectProjectList('workspace-1');

    activeWorkspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace 2' });
    TestBed.flushEffects();

    expect(workspaceOne.cancelled).toBe(true);
    const workspaceTwo = expectProjectList('workspace-2');
    workspaceTwo.flush({ items: [] });
    expect(facade.getProjectsOverview().status).toBe('empty');
  });

  it('lists a canonical Draft without probing its not-yet-provisioned Task collection', () => {
    expectProjectList().flush({
      items: [{
        ...projectDto,
        status: 0,
        activationState: 1,
        versionNo: 1,
        uiPermissions: { canCreateTask: false, canActivate: true }
      }]
    });

    httpMock.expectNone('/api/projects/project-1/tasks');
    const page = facade.getProjectsOverview();
    expect(page.status).toBe('ready');
    expect(page.projects[0]).toMatchObject({ statusLabel: 'Draft', isOperational: false });
    expect(page.rows).toEqual([]);
  });

  it('fetches task detail by id when the task is not present in the project list page', () => {
    flushInitialLoad([]);

    expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('empty');
    facade.ensureTaskDetail('project-1', 'task-1');

    httpMock.expectOne('/api/tasks/task-1').flush(editableTaskDto);

    expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.title).toBe('Backend Task');
    expect(continueWorkingHistory.touchProject).toHaveBeenCalledWith('project-1', 'workspace-1');
    expect(continueWorkingHistory.touchProject).toHaveBeenCalledTimes(1);
  });

  it('keeps canonical Task Brief values authoritative across a compact ProjectChanged list refresh', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({
      task: {
        ...editableTaskDto,
        brief: {
          goal: { value: 'Reach review', source: 'taskSpecific' },
          deliverable: { value: 'Signed handoff', source: 'taskSpecific' },
          constraints: { value: null, source: 'notSet' }
        }
      },
      checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] }
    });

    vi.useFakeTimers();
    try {
      (facade as unknown as { handleRealtimeEvent(event: unknown): void }).handleRealtimeEvent({
        eventId: 'project-refresh', eventType: 'Projects.ProjectChanged.v1', aggregateId: 'project-1'
      });
      vi.advanceTimersByTime(100);

      expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.brief?.goal.value).toBe('Reach review');
      expectProjectList().flush({ items: [projectDto] });
      httpMock.expectOne('/api/projects/project-1/tasks').flush({ items: [editableTaskDto] });

      expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.brief).toEqual({
        goal: { value: 'Reach review', source: 'taskSpecific' },
        deliverable: { value: 'Signed handoff', source: 'taskSpecific' },
        constraints: { value: null, source: 'notSet' }
      });
    } finally {
      vi.useRealTimers();
    }
  });

  it('fails closed when the route Project context mismatches the loaded Task', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('wrong-project', 'task-1');

    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });

    const page = facade.getTaskDetail('wrong-project', 'task-1');
    expect(page.status).toBe('permissionDenied');
    expect(page.task).toBeUndefined();
    expect(page.editorTask).toBeUndefined();
    httpMock.expectNone('/api/projects/project-1');
  });

  it('cancels and discards an in-flight detail request after leaving the page', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    const request = httpMock.expectOne('/api/tasks/task-1');

    facade.releaseTaskDetail();

    expect(request.cancelled).toBe(true);
    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
  });

  it('cancels Task A before Task B becomes active and never retains A detail', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    const taskA = httpMock.expectOne('/api/tasks/task-1');
    facade.ensureTaskDetail('project-1', 'task-2');
    const taskB = httpMock.expectOne('/api/tasks/task-2');

    expect(taskA.cancelled).toBe(true);
    taskB.flush({ task: { ...editableTaskDto, id: 'task-2', title: 'Task B' }, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });

    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
    expect(facade.getTaskDetail('project-1', 'task-2').editorTask?.title).toBe('Task B');
  });

  it('loads project label definitions as soon as detail permissions allow labels', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, permissions: { canApplyLabels: true }, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });

    const labels = httpMock.expectOne('/api/projects/project-1/task-labels?includeArchived=true');
    labels.flush([{ id: 'label-1', name: 'Urgent', sortKey: 1024, isArchived: false, version: 1 }]);

    expect(facade.getTaskDetail('project-1', 'task-1').detail?.labelDefinitions.map((label) => label.name)).toEqual(['Urgent']);
  });

  it('retains canonical Task, relationship, permission, and pagination fields in the Task detail view model', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({
      task: {
        ...editableTaskDto, tenantId: 'tenant-1', workspaceId: 'workspace-1', kind: 0, parentTaskId: null,
        workflowStageId: 'stage-1', workflowStageName: 'In progress', stageCategory: 1, priority: 'Medium',
        reviewStatus: 0, version: 7, progressIsDerived: true,
        subresources: { checklistCompletedCount: 1, checklistTotalCount: 2, commentCount: 3, labelCount: 4, subtaskCount: 5 },
        primaryAssignee: { userId: 'user-1', displayName: 'Canonical assignee' }
      },
      relationships: { primaryAssignee: { userId: 'user-1', displayName: 'Canonical assignee' }, targetGroupId: 'group-1', collaborators: [{ userId: 'user-2', displayName: 'Collaborator' }], reviewer: { userId: 'user-3', displayName: 'Reviewer' }, version: 8 },
      permissions: { canCreateSubtask: true, canCreateChecklistItem: true, canUpdateChecklistItems: true, canDeleteChecklistItems: true, canReorderChecklist: true, canCreateComment: true, canMarkCommentImportant: true, canApplyLabels: true, canManageLabelDefinitions: true, canAssociateFiles: true, canRemoveFiles: true, canChangeWatch: true },
      checklist: [], labels: [], watchState: { isWatching: false, isExplicitOptOut: false, automaticSources: [], version: 1 },
      subtasks: { items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false }, comments: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }, files: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }
    });
    httpMock.expectOne('/api/projects/project-1/task-labels?includeArchived=true').flush([]);

    const detail = facade.getTaskDetail('project-1', 'task-1').detail!;
    expect(detail.canonicalTask).toMatchObject({ tenantId: 'tenant-1', workspaceId: 'workspace-1', projectId: 'project-1', workflowStageName: 'In progress', stageCategory: 1, priority: 'Medium', progressIsDerived: true, version: '7', subtaskCount: 5 });
    expect(detail.relationships).toMatchObject({ primaryAssignee: 'Canonical assignee', targetGroupId: 'group-1', reviewer: 'Reviewer', version: '8' });
    expect(detail.relationships.collaborators).toEqual([{ userId: 'user-2', displayName: 'Collaborator' }]);
    expect(detail.permissions.canChangeWatch).toBe(true);
    expect(detail.subtasks).toMatchObject({ page: 1, pageSize: 50, totalCount: 0, hasMore: false });
  });

  it('maps standalone Activity wire DTOs and keeps paging stable without fabricating Task state', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({
      task: { ...editableTaskDto, status: undefined, workflowStageName: 'In progress', stageCategory: 2 },
      checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] }
    });

    expect(facade.getDetailSectionState('activity').status).toBe('idle');
    expect(facade.getTaskDetail('project-1', 'task-1').task?.workflowStageName).toBe('In progress');
    facade.loadActivity('task-1');
    httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20').flush({
      items: [
        { id: 'activity-2', activityType: 'StatusUpdate', body: 'Ready for review', occurredAt: '2026-08-24T03:00:00Z', author: { userId: 'user-2', displayName: 'Status author' } },
        { id: 'activity-1', activityType: 3, body: 'Dependency needs attention', occurredAt: '2026-08-24T02:00:00Z', author: { userId: 'user-1', displayName: 'Issue author' } }
      ],
      page: 1, pageSize: 2, totalCount: 3, hasMore: true
    });

    let activity = facade.getTaskDetail('project-1', 'task-1').detail!.activity;
    expect(activity.items).toEqual([
      expect.objectContaining({ id: 'activity-2', activityType: 'statusUpdate', authorUserId: 'user-2', authorDisplayName: 'Status author' }),
      expect.objectContaining({ id: 'activity-1', activityType: 'issue', authorUserId: 'user-1', authorDisplayName: 'Issue author' })
    ]);

    facade.loadMoreActivity('task-1');
    httpMock.expectOne('/api/tasks/task-1/activity?page=2&pageSize=2').flush({
      items: [
        { id: 'activity-1', activityType: 'Issue', body: 'duplicate', author: { displayName: 'Duplicate' } },
        { id: 'activity-0', activityType: 2, body: 'Decision recorded', occurredAt: '2026-08-24T01:00:00Z', author: { userId: 'user-0', displayName: 'Decision author' } }
      ],
      page: 2, pageSize: 2, totalCount: 3, hasMore: false
    });

    activity = facade.getTaskDetail('project-1', 'task-1').detail!.activity;
    expect(activity.items.map(item => item.id)).toEqual(['activity-2', 'activity-1', 'activity-0']);
    expect(activity.items[2]).toMatchObject({ activityType: 'decision', authorDisplayName: 'Decision author' });
    expect(facade.getTaskDetail('project-1', 'task-1').task?.status).toBe('inProgress');
  });

  it('keeps the authoritative phase and loaded Activity when an independent realtime refresh fails', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({
      task: { ...editableTaskDto, status: undefined, workflowStageName: 'In progress', stageCategory: 2 },
      checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] }
    });
    facade.loadActivity('task-1');
    httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20').flush({
      items: [{ id: 'activity-old', activityType: 'Note', body: 'Existing history', author: { displayName: 'Author' } }],
      page: 1, pageSize: 20, totalCount: 1, hasMore: false
    });

    (facade as unknown as { handleRealtimeEvent(event: unknown): void }).handleRealtimeEvent({ eventId: 'task-refresh', eventType: 'Projects.TaskChanged.v1', aggregateId: 'task-1' });
    httpMock.expectOne('/api/tasks/task-1').flush({
      task: { ...editableTaskDto, status: undefined, workflowStageName: 'Review', stageCategory: 3 },
      checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] }
    });
    const activityRefresh = httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20');

    expect(facade.getTaskDetail('project-1', 'task-1').task?.workflowStageName).toBe('Review');
    activityRefresh.flush({ message: 'Activity temporarily unavailable', traceId: 'activity-refresh' }, { status: 500, statusText: 'Server Error' });
    expect(facade.getTaskDetail('project-1', 'task-1').task?.workflowStageName).toBe('Review');
    expect(facade.getTaskDetail('project-1', 'task-1').detail?.activity.items.map(item => item.id)).toEqual(['activity-old']);
    expect(facade.getDetailSectionState('activity')).toMatchObject({ status: 'error', failedPage: 1, retryKind: 'page' });

    facade.retrySection('task-1', 'activity');
    httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20').flush({
      items: [{ id: 'activity-new', activityType: 'StatusUpdate', body: 'Review started', author: { displayName: 'Reviewer' } }],
      page: 1, pageSize: 20, totalCount: 1, hasMore: false
    });
    expect(facade.getTaskDetail('project-1', 'task-1').detail?.activity.items.map(item => item.id)).toEqual(['activity-new']);
  });

  it('does not cancel an in-flight Activity request when a realtime aggregate refresh arrives', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    const aggregate = {
      task: { ...editableTaskDto, status: undefined, workflowStageName: 'In progress', stageCategory: 2 },
      checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] }
    };
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);

    facade.loadActivity('task-1');
    const inFlightActivity = httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20');
    expect(facade.getDetailSectionState('activity').status).toBe('loading');

    (facade as unknown as { handleRealtimeEvent(event: unknown): void }).handleRealtimeEvent({
      eventId: 'task-refresh-during-activity-load', eventType: 'Projects.TaskChanged.v1', aggregateId: 'task-1'
    });
    httpMock.expectOne('/api/tasks/task-1').flush({
      ...aggregate,
      task: { ...aggregate.task, workflowStageName: 'Review', stageCategory: 3 }
    });

    expect(inFlightActivity.cancelled).toBe(false);
    httpMock.expectNone('/api/tasks/task-1/activity?page=1&pageSize=20');
    inFlightActivity.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });

    expect(facade.getTaskDetail('project-1', 'task-1').task?.workflowStageName).toBe('Review');
    expect(facade.getDetailSectionState('activity').status).toBe('empty');
    expect(facade.getTaskDetail('project-1', 'task-1').detail?.activity).toMatchObject({ totalCount: 0, hasMore: false });
  });

  it('keeps an authorized phase visible when the initial Activity request has a transient error', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({
      task: { ...editableTaskDto, status: undefined, workflowStageName: 'In progress', stageCategory: 2 },
      checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] }
    });

    facade.loadActivity('task-1');
    httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20').flush({ message: 'temporary' }, { status: 500, statusText: 'Server Error' });

    const page = facade.getTaskDetail('project-1', 'task-1');
    expect(page.status).toBe('ready');
    expect(page.task?.workflowStageName).toBe('In progress');
    expect(page.detail?.activity.items).toEqual([]);
    expect(facade.getDetailSectionState('activity').status).toBe('error');
  });

  it('clears all protected Task data when the standalone Activity endpoint returns a safe 404', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });

    facade.loadActivity('task-1');
    httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20').flush({ error: { code: 'TASK_NOT_FOUND' } }, { status: 404, statusText: 'Not Found' });

    expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('permissionDenied');
    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
    expectNoProjectList();
  });

  it.each([401, 403])('reauthorizes and clears protected Task data when Activity returns %s', (status) => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });

    facade.loadActivity('task-1');
    httpMock.expectOne('/api/tasks/task-1/activity?page=1&pageSize=20').flush({}, { status, statusText: status === 401 ? 'Unauthorized' : 'Forbidden' });
    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
    expectProjectList().flush({ items: [] });
    expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('empty');
    expect(facade.getTaskDetail('project-1', 'task-1').task).toBeUndefined();
  });

  it('drops an obsolete project response after authorization changes and reloads the active task only after reauthorization', () => {
    const firstProjects = expectProjectList();
    (facade as unknown as { handleRealtimeEvent(event: unknown): void }).handleRealtimeEvent(realtimeEvent('Security.AuthorizationStateChanged.v1'));
    const currentProjects = expectProjectList();

    expect(firstProjects.cancelled).toBe(true);
    currentProjects.flush({ items: [projectDto] });
    httpMock.expectOne('/api/projects/project-1/tasks').flush({ items: [editableTaskDto] });

    expect(facade.getProjectsOverview().rows.map(row => row.id)).toEqual(['task-1']);
  });

  it('does not apply an obsolete save after Task A is replaced by Task B', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    facade.saveTask('task-1', 'project-1', { title: 'A edit', description: '', priority: 'medium', startDate: '', dueDate: '', progressPercent: 1, expectedVersion: '1' });
    const save = httpMock.expectOne('/api/tasks/task-1');

    facade.ensureTaskDetail('project-1', 'task-2');

    expect(save.cancelled).toBe(true);
    httpMock.expectOne('/api/tasks/task-2').flush({ task: { ...editableTaskDto, id: 'task-2', title: 'Task B' }, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    expect(facade.getTaskMutationState().status).toBe('idle');
    expect(facade.getTaskDetail('project-1', 'task-2').editorTask?.title).toBe('Task B');
  });

  it('exposes a retryable aggregate detail error instead of leaving the page in loading', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ message: 'temporary failure', traceId: 'detail-500' }, { status: 500, statusText: 'Server Error' });

    expect(facade.getTaskDetail('project-1', 'task-1').detailSectionState).toMatchObject({ status: 'error', requestId: 'detail-500' });
    facade.retryTaskDetail('task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    expect(facade.getTaskDetail('project-1', 'task-1').detailSectionState.status).toBe('ready');
  });

  it('clears protected Task detail without probing sibling resources when its safe read is not found', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });

    facade.retryTaskDetail('task-1');
    httpMock.expectOne('/api/tasks/task-1').flush(
      { error: { code: 'TASK_NOT_FOUND' }, requestId: 'safe-not-found' },
      { status: 404, statusText: 'Not Found' }
    );

    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
    expectNoProjectList();
    expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('permissionDenied');
  });

  it('creates a task through the backend and refreshes project and my-task rows after success', () => {
    flushInitialLoad();

    facade.createTask({
      projectId: 'project-1',
      title: 'Created Task',
      description: 'Created through UI',
      priority: 'high',
      startDate: '2026-07-05',
      dueDate: '2026-07-25'
    });

    const create = httpMock.expectOne('/api/projects/project-1/tasks');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({
      milestoneId: null,
      title: 'Created Task',
      description: 'Created through UI',
      priority: 2,
      startDate: '2026-07-05',
      dueDate: '2026-07-25'
    });
    create.flush({ ...editableTaskDto, id: 'task-created', title: 'Created Task' });

    httpMock.expectOne('/api/projects/project-1/tasks').flush({
      items: [{ ...editableTaskDto, id: 'task-created', title: 'Created Task' }]
    });

    expect(facade.getTaskCreateMutationState().status).toBe('success');
    expect(facade.getProjectsOverview().rows.map((row) => row.title)).toEqual(['Created Task']);
  });

  it('saves a task through PATCH and refreshes detail/list state after success', () => {
    flushInitialLoad();

    facade.saveTask('task-1', 'project-1', {
      title: 'Saved Task',
      description: 'Saved detail',
      priority: 'urgent',
      startDate: '2026-07-03',
      dueDate: '2026-07-22',
      progressPercent: 65,
      expectedVersion: '1'
    });

    const save = httpMock.expectOne('/api/tasks/task-1');
    expect(save.request.method).toBe('PATCH');
    expect(save.request.body).toEqual({
      title: 'Saved Task',
      description: 'Saved detail',
      priority: 3,
      plannedStartDate: '2026-07-03',
      plannedEndDate: '2026-07-22',
      progressPercent: 65,
      expectedVersion: 1
    });
    save.flush({ ...editableTaskDto, title: 'Saved Task', progressPercent: 65 });

    httpMock.expectOne('/api/tasks/task-1').flush({
      ...editableTaskDto,
      title: 'Saved Task',
      progressPercent: 65
    });
    httpMock.expectOne('/api/projects/project-1/tasks').flush({
      items: [{ ...editableTaskDto, title: 'Saved Task', progressPercent: 65 }]
    });

    expect(facade.getTaskMutationState().status).toBe('success');
    expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.title).toBe('Saved Task');
    expect(facade.getProjectsOverview().rows[0].progressPercent).toBe(65);
  });

  it('keeps task state unchanged and shows a safe backend error when save fails', () => {
    flushInitialLoad();

    facade.saveTask('task-1', 'project-1', {
      title: 'Rejected Task',
      description: 'Rejected detail',
      priority: 'medium',
      startDate: '2026-07-02',
      dueDate: '2026-07-20',
      progressPercent: 45,
      expectedVersion: '1'
    });

    httpMock.expectOne('/api/tasks/task-1').flush(
      { message: 'You are not allowed to update this task.', traceId: 'trace-123' },
      { status: 400, statusText: 'Bad Request' }
    );

    expect(facade.getTaskMutationState()).toEqual({
      status: 'validation',
      message: 'You are not allowed to update this task.',
      requestId: 'trace-123'
    });
    expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.title).toBe('Backend Task');
  });

  it('reports a successful save with a failed authoritative reload separately from a PATCH failure', () => {
    flushInitialLoad();
    facade.saveTask('task-1', 'project-1', { title: 'Saved', description: '', priority: 'medium', startDate: '', dueDate: '', progressPercent: 30, expectedVersion: '1' });
    httpMock.expectOne('/api/tasks/task-1').flush({ ...editableTaskDto, title: 'Saved' });
    const taskReload = httpMock.expectOne('/api/tasks/task-1');
    const listReload = httpMock.expectOne('/api/projects/project-1/tasks');
    taskReload.flush({ message: 'detail unavailable', traceId: 'after-save-500' }, { status: 500, statusText: 'Server Error' });
    expect(listReload.cancelled).toBe(true);
    expect(facade.getTaskMutationState()).toEqual({ status: 'savedButRefreshFailed', message: 'detail unavailable', requestId: 'after-save-500' });
  });

  it('maps both HTTP 409 and TASK_STALE_VERSION to a preserved task-save conflict', () => {
    flushInitialLoad();
    facade.saveTask('task-1', 'project-1', { title: 'Edited', description: '', priority: 'medium', startDate: '', dueDate: '', progressPercent: 1, expectedVersion: '1' });
    httpMock.expectOne('/api/tasks/task-1').flush({ code: 'TASK_STALE_VERSION', message: 'Task changed.', traceId: 'stale-1' }, { status: 409, statusText: 'Conflict' });

    expect(facade.getTaskMutationState()).toEqual({ status: 'conflict', message: 'Task changed.', requestId: 'stale-1' });
    expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.title).toBe('Backend Task');
  });

  it('reloads the authoritative task after an explicit save-conflict recovery and keeps conflict on reload failure', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    facade.saveTask('task-1', 'project-1', { title: 'Edited', description: '', priority: 'medium', startDate: '', dueDate: '', progressPercent: 1, expectedVersion: '1' });
    httpMock.expectOne('/api/tasks/task-1').flush({ error: { code: 'TASK_STALE_VERSION', message: 'Stale' } }, { status: 409, statusText: 'Conflict' });

    facade.reloadTaskAfterConflict('task-1');
    const reload = httpMock.expectOne('/api/tasks/task-1');
    facade.reloadTaskAfterConflict('task-1');
    httpMock.expectNone('/api/tasks/task-1');
    reload.flush({ message: 'temporary' }, { status: 500, statusText: 'Server Error' });
    expect(facade.getTaskMutationState().status).toBe('conflict');

    facade.reloadTaskAfterConflict('task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: { ...editableTaskDto, title: 'Authoritative' }, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    expect(facade.getTaskMutationState().status).toBe('idle');
    expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.title).toBe('Authoritative');
  });

  it('reauthorizes and removes protected detail when task save is forbidden', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    facade.saveTask('task-1', 'project-1', { title: 'Edited', description: '', priority: 'medium', startDate: '', dueDate: '', progressPercent: 1, expectedVersion: '1' });
    httpMock.expectOne('/api/tasks/task-1').flush({}, { status: 403, statusText: 'Forbidden' });

    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
    expectProjectList().flush({ items: [] });
    expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('empty');
  });

  it('reauthorizes when a successful detail mutation cannot reload protected detail', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    facade.setWatch('task-1', true, '1');
    httpMock.expectOne('/api/tasks/task-1/watch').flush({});
    httpMock.expectOne('/api/tasks/task-1').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
    expectProjectList().flush({ items: [] });
  });

  it('clears a comment conflict after aggregate reload, while a page retry stays a page request', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [{ id: 'comment-1', bodyPlainText: 'first' }] }, files: { items: [] } });
    facade.updateComment('task-1', 'comment-1', 'changed', false, '1');
    httpMock.expectOne('/api/task-comments/comment-1').flush({ code: 'TASK_STALE_VERSION' }, { status: 409, statusText: 'Conflict' });
    expect(facade.getDetailSectionState('comments').status).toBe('conflict');
    facade.retrySection('task-1', 'comments');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [{ id: 'comment-1', bodyPlainText: 'latest' }] }, files: { items: [] } });
    expect(facade.getDetailSectionState('comments').status).toBe('ready');
  });

  it('does not let a label-definition GET overwrite a submitting label mutation', () => {
    flushInitialLoad([]);
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, permissions: { canApplyLabels: true }, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    const definitions = httpMock.expectOne('/api/projects/project-1/task-labels?includeArchived=true');
    facade.applyLabel('task-1', 'label-1', '1');
    const mutation = httpMock.expectOne('/api/tasks/task-1/labels/label-1');
    expect(mutation.request.body).toEqual({ expectedVersion: 1 });
    definitions.flush([{ id: 'label-1', name: 'Label', sortKey: 1, isArchived: false, version: 1 }]);
    expect(facade.getDetailSectionState('labels').status).toBe('submitting');
    mutation.flush({});
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, permissions: { canApplyLabels: true }, checklist: [], labels: [{ id: 'label-1', name: 'Label' }], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } });
    expect(facade.getDetailSectionState('labels').status).toBe('ready');
  });

  it('keeps a comment conflict and the editor state when an unrelated watch mutation succeeds', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    const aggregate = { task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [{ id: 'comment-1', bodyPlainText: 'first' }] }, files: { items: [] } };
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);
    facade.updateComment('task-1', 'comment-1', 'changed', false, '1');
    httpMock.expectOne('/api/task-comments/comment-1').flush({ error: { code: 'TASK_STALE_VERSION' } }, { status: 409, statusText: 'Conflict' });

    facade.setWatch('task-1', true, '1');
    expect(facade.getTaskMutationState().status).toBe('idle');
    httpMock.expectOne('/api/tasks/task-1/watch').flush({});
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);

    expect(facade.getDetailSectionState('comments').status).toBe('conflict');
    expect(facade.getTaskMutationState().status).toBe('idle');
  });

  it('keeps previously loaded comment pages when a Watch mutation returns an aggregate first page', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [{ id: 'comment-1', bodyPlainText: 'one' }], page: 1, pageSize: 20, totalCount: 2, hasMore: true }, files: { items: [] } });
    facade.loadMoreComments('task-1');
    httpMock.expectOne('/api/tasks/task-1/comments?page=2&pageSize=20').flush({ items: [{ id: 'comment-2', bodyPlainText: 'two' }], page: 2, pageSize: 20, totalCount: 2, hasMore: false });
    facade.setWatch('task-1', true, '1');
    httpMock.expectOne('/api/tasks/task-1/watch').flush({});
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [{ id: 'comment-1', bodyPlainText: 'one updated' }], page: 1, pageSize: 20, totalCount: 2, hasMore: true }, files: { items: [] } });
    expect(facade.getTaskDetail('project-1', 'task-1').detail?.comments.items.map(item => item.id)).toEqual(['comment-1', 'comment-2']);
  });

  it('does not create a Task-body conflict while a subresource mutation is submitting', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    const aggregate = { task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [] }, files: { items: [] } };
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);
    facade.setWatch('task-1', true, '1');
    (facade as unknown as { handleRealtimeEvent(event: unknown): void }).handleRealtimeEvent({ eventId: 'self-task-change', eventType: 'Projects.TaskChanged.v1', aggregateId: 'task-1' });
    expect(facade.getTaskMutationState().status).toBe('idle');

    // The current realtime contract has no reliable self-event identity, so the
    // conservative realtime refresh and the mutation-authoritative refresh may
    // both be in flight. Account for both without treating either as a conflict.
    const realtimeRefresh = httpMock.expectOne('/api/tasks/task-1');
    httpMock.expectOne('/api/tasks/task-1/watch').flush({});
    const mutationRefresh = httpMock.expectOne('/api/tasks/task-1');
    realtimeRefresh.flush(aggregate);
    mutationRefresh.flush(aggregate);
    expect(facade.getTaskMutationState().status).toBe('idle');
  });

  it('rejects an invalid expected version without issuing a PATCH', () => {
    flushInitialLoad();
    facade.saveTask('task-1', 'project-1', { title: 'Edited', description: '', priority: 'medium', startDate: '', dueDate: '', progressPercent: 1, expectedVersion: 'invalid' });
    httpMock.expectNone('/api/tasks/task-1');
    expect(facade.getTaskMutationState().status).toBe('validation');
  });

  it('clears checklist, file, and label conflicts only after their authoritative aggregate reloads', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    const aggregate = { task: editableTaskDto, checklist: [{ id: 'check-1', text: 'one', isCompleted: false, version: 1 }], labels: [{ id: 'label-1', name: 'Label' }], subtasks: { items: [] }, comments: { items: [] }, files: { items: [{ id: 'file-1', fileName: 'one.txt' }] } };
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);

    facade.updateChecklist('task-1', 'check-1', 'next', false, '1');
    httpMock.expectOne('/api/tasks/task-1/checklist/check-1').flush({ code: 'TASK_STALE_VERSION' }, { status: 409, statusText: 'Conflict' });
    facade.retrySection('task-1', 'checklist');
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);
    expect(facade.getDetailSectionState('checklist').status).toBe('ready');

    facade.associateFile('task-1', 'file-2', '1');
    httpMock.expectOne('/api/tasks/task-1/files').flush({ code: 'TASK_STALE_VERSION' }, { status: 409, statusText: 'Conflict' });
    facade.retrySection('task-1', 'files');
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);
    expect(facade.getDetailSectionState('files').status).toBe('ready');

    facade.applyLabel('task-1', 'label-1', '1');
    httpMock.expectOne('/api/tasks/task-1/labels/label-1').flush({ code: 'TASK_STALE_VERSION' }, { status: 409, statusText: 'Conflict' });
    facade.retrySection('task-1', 'labels');
    httpMock.expectOne('/api/tasks/task-1').flush(aggregate);
    expect(facade.getDetailSectionState('labels').status).toBe('ready');
  });

  it('does not send label create or update requests beyond backend-trimmed limits', () => {
    flushInitialLoad();
    facade.createProjectLabel('task-1', 'project-1', 'n'.repeat(121));
    facade.updateProjectLabel('task-1', 'project-1', 'label-1', 'n'.repeat(121), '', '1', '1');
    facade.updateProjectLabel('task-1', 'project-1', 'label-1', 'valid', 'd'.repeat(1001), '1', '1');
    httpMock.expectNone(request => request.url.includes('/task-labels'));
  });

  it('retries the same failed comments page without duplicating existing comments', () => {
    flushInitialLoad();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({ task: editableTaskDto, checklist: [], labels: [], subtasks: { items: [] }, comments: { items: [{ id: 'comment-1', bodyPlainText: 'first' }], page: 1, pageSize: 20, totalCount: 2, hasMore: true }, files: { items: [] } });

    facade.loadMoreComments('task-1');
    httpMock.expectOne('/api/tasks/task-1/comments?page=2&pageSize=20').flush({ message: 'temporary', traceId: 'comments-2' }, { status: 500, statusText: 'Server Error' });
    expect(facade.getDetailSectionState('comments')).toMatchObject({ status: 'error', retryKind: 'page', failedPage: 2 });

    facade.retrySection('task-1', 'comments');
    httpMock.expectOne('/api/tasks/task-1/comments?page=2&pageSize=20').flush({ items: [{ id: 'comment-1', bodyPlainText: 'first' }, { id: 'comment-2', bodyPlainText: 'second' }], page: 2, pageSize: 20, totalCount: 2, hasMore: false });
    expect(facade.getTaskDetail('project-1', 'task-1').detail?.comments.items.map(item => item.id)).toEqual(['comment-1', 'comment-2']);
  });

  it('shows a Project error state and retries without rendering an empty success state', () => {
    httpMock
      .expectOne((request) => request.url === '/api/projects' && request.params.get('workspaceId') === 'workspace-1')
      .flush({ message: 'server failed', traceId: 'trace-projects' }, { status: 500, statusText: 'Server Error' });

    expect(facade.getProjectsOverview().status).toBe('error');
    expect(facade.getProjectsOverview().projects).toEqual([]);
    expect(facade.getProjectsOverview().error?.requestId).toBe('trace-projects');

    facade.retryProjects();
    flushInitialLoad();

    expect(facade.getProjectsOverview().status).toBe('ready');
    expect(facade.getProjectsOverview().projects.map((project) => project.id)).toEqual(['project-1']);
  });

  it('shows a Project error state when a project task list request fails', () => {
    expectProjectList().flush({ items: [projectDto] });
    httpMock
      .expectOne('/api/projects/project-1/tasks')
      .flush({ message: 'Task list failed', traceId: 'trace-tasks' }, { status: 500, statusText: 'Server Error' });

    const page = facade.getProjectsOverview();
    expect(page.status).toBe('error');
    expect(page.rows).toEqual([]);
    expect(page.error?.requestId).toBe('trace-tasks');
  });

  it('does not report task creation success after backend mutation failure', () => {
    flushInitialLoad();

    facade.createTask({
      projectId: 'project-1',
      title: 'Rejected Task',
      description: '',
      priority: 'medium',
      startDate: '',
      dueDate: ''
    });

    httpMock
      .expectOne('/api/projects/project-1/tasks')
      .flush({ message: 'Create rejected.', traceId: 'trace-create' }, { status: 500, statusText: 'Server Error' });

    expect(facade.getTaskCreateMutationState()).toEqual({
      status: 'failure',
      message: 'Create rejected.',
      requestId: 'trace-create'
    });
    expect(facade.getProjectsOverview().rows.map((row) => row.id)).toEqual(['task-1']);
  });

  function flushInitialLoad(projectTasks: readonly TaskDto[] = [editableTaskDto]): void {
    expectProjectList().flush({ items: [projectDto] });
    httpMock.expectOne('/api/projects/project-1/tasks').flush({ items: projectTasks });
  }

  function realtimeEvent(eventType: 'Security.AuthorizationStateChanged.v1') {
    return { eventId: 'event-1', eventType, payloadSchemaVersion: 1, occurredAt: '2026-07-24T00:00:00Z', tenantId: 'tenant-1', aggregateType: 'Security', aggregateId: 'security-1', aggregateVersion: null, actor: { actorType: 'System', actorId: null }, correlationId: null, causationId: null, payload: {} };
  }
});

describe('ProjectsFacade direct Task route parent context', () => {
  let facade: ProjectsFacade;
  let httpMock: HttpTestingController;
  let activeWorkspace: ActiveWorkspaceFacade;
  let clearTaskState: ProtectedStateClearer;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: { url: '/projects/project-1/tasks/task-1' } },
        {
          provide: RealtimeFacade,
          useValue: {
            durableEvents$: EMPTY,
            registerProtectedStateClearer: (_owner: string, clear: ProtectedStateClearer) => {
              clearTaskState = clear;
              return () => undefined;
            },
            registerSubscription: () => () => undefined,
            registerCatchUp: () => () => undefined
          }
        }
      ]
    });
    facade = TestBed.inject(ProjectsFacade);
    httpMock = TestBed.inject(HttpTestingController);
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  function expectProjectList(workspaceId = 'workspace-1') {
    return httpMock.expectOne((request) =>
      request.method === 'GET' &&
      request.url === '/api/projects' &&
      request.params.get('workspaceId') === workspaceId
    );
  }

  function expectNoProjectList(): void {
    httpMock.expectNone((request) => request.url === '/api/projects');
  }

  it('loads the authorized parent Project without issuing the broad Project list request', () => {
    expectNoProjectList();
    facade.ensureTaskDetail('project-1', 'task-1');
    const preSelectionTask = httpMock.expectOne('/api/tasks/task-1');

    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    TestBed.flushEffects();

    expect(preSelectionTask.cancelled).toBe(true);
    expectNoProjectList();

    httpMock.expectOne('/api/tasks/task-1').flush(taskDetail({ ...editableTaskDto, workspaceId: 'workspace-1' }));
    expect(facade.getTaskDetail('project-1', 'task-1').detailSectionState.status).toBe('loading');

    const parent = httpMock.expectOne('/api/projects/project-1');
    expect(parent.request.method).toBe('GET');
    parent.flush(projectDto);

    const page = facade.getTaskDetail('project-1', 'task-1');
    expect(page.project?.name).toBe('Backend Project');
    expect(page.task?.title).toBe('Backend Task');
    expectNoProjectList();
  });

  it('keeps a fast cold-route Task undisclosed until Workspace hydration reauthorizes it', () => {
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush(taskDetail({ ...editableTaskDto, workspaceId: 'workspace-1' }));

    expect(facade.getTaskDetail('project-1', 'task-1').task).toBeUndefined();
    expect(facade.getTaskDetail('project-1', 'task-1').detailSectionState.status).toBe('loading');
    httpMock.expectNone('/api/projects/project-1');
    expectNoProjectList();

    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    TestBed.flushEffects();

    httpMock.expectOne('/api/tasks/task-1').flush(taskDetail({ ...editableTaskDto, workspaceId: 'workspace-1' }));
    httpMock.expectOne('/api/projects/project-1').flush(projectDto);
    expect(facade.getTaskDetail('project-1', 'task-1').project?.name).toBe('Backend Project');
    expectNoProjectList();
  });

  it('does not load parent context for a Task outside the active Workspace', () => {
    facade.ensureTaskDetail('project-1', 'task-1');
    const preSelectionTask = httpMock.expectOne('/api/tasks/task-1');
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    TestBed.flushEffects();
    expect(preSelectionTask.cancelled).toBe(true);

    httpMock.expectOne('/api/tasks/task-1').flush(taskDetail({ ...editableTaskDto, workspaceId: 'workspace-2' }));

    const page = facade.getTaskDetail('project-1', 'task-1');
    expect(page.status).toBe('permissionDenied');
    expect(page.task).toBeUndefined();
    httpMock.expectNone('/api/projects/project-1');
    expectNoProjectList();
  });

  it('does not let a cached Project bypass the active Workspace boundary', () => {
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    facade.retryProjects();
    expectProjectList().flush({ items: [projectDto] });
    httpMock.expectOne('/api/projects/project-1/tasks').flush({ items: [] });

    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({
      ...taskDetail({ ...editableTaskDto, workspaceId: 'workspace-2' }),
      permissions: { canApplyLabels: true, canManageLabelDefinitions: true }
    });

    expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('permissionDenied');
    httpMock.expectNone('/api/projects/project-1');
    httpMock.expectNone('/api/projects/project-1/task-labels?includeArchived=true');
    expectNoProjectList();
  });

  it('cancels a stale parent Project read when the Task route changes', () => {
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush(taskDetail({ ...editableTaskDto, workspaceId: 'workspace-1' }));
    const staleParent = httpMock.expectOne('/api/projects/project-1');

    facade.ensureTaskDetail('project-2', 'task-2');
    expect(staleParent.cancelled).toBe(true);

    httpMock.expectOne('/api/tasks/task-2').flush(taskDetail({
      ...editableTaskDto,
      id: 'task-2',
      projectId: 'project-2',
      workspaceId: 'workspace-1',
      title: 'Task B'
    }));
    httpMock.expectOne('/api/projects/project-2').flush({
      ...projectDto,
      id: 'project-2',
      title: 'Project B'
    });

    const page = facade.getTaskDetail('project-2', 'task-2');
    expect(page.project?.name).toBe('Project B');
    expect(page.task?.title).toBe('Task B');
    expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
  });

  it('fails closed when the parent Project is no longer readable', () => {
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush(taskDetail({ ...editableTaskDto, workspaceId: 'workspace-1' }));
    httpMock.expectOne('/api/projects/project-1').flush(
      { error: { code: 'PROJECT_NOT_FOUND' }, requestId: 'masked-parent' },
      { status: 404, statusText: 'Not Found' }
    );

    const page = facade.getTaskDetail('project-1', 'task-1');
    expect(page.status).toBe('permissionDenied');
    expect(page.task).toBeUndefined();
    expect(page.project).toBeUndefined();
    expectNoProjectList();
  });

  it('discards a Task whose canonical Project does not match the route before dependent reads', () => {
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush({
      ...taskDetail({ ...editableTaskDto, workspaceId: 'workspace-1', projectId: 'project-2' }),
      permissions: { canApplyLabels: true, canManageLabelDefinitions: true }
    });

    const page = facade.getTaskDetail('project-1', 'task-1');
    expect(page.status).toBe('permissionDenied');
    expect(page.task).toBeUndefined();
    httpMock.expectNone('/api/projects/project-2');
    httpMock.expectNone('/api/projects/project-2/task-labels?includeArchived=true');
    expectNoProjectList();
  });

  it('fails closed without a broad Project request when the parent read is forbidden', () => {
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.flushEffects();
    facade.ensureTaskDetail('project-1', 'task-1');
    httpMock.expectOne('/api/tasks/task-1').flush(taskDetail({ ...editableTaskDto, workspaceId: 'workspace-1' }));
    httpMock.expectOne('/api/projects/project-1').flush(
      { error: { code: 'FORBIDDEN' }, requestId: 'masked-parent' },
      { status: 403, statusText: 'Forbidden' }
    );

    expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('permissionDenied');
    expectNoProjectList();
  });
  describe('unavailable Task authorization boundaries', () => {
    beforeEach(() => {
      activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
      TestBed.tick();
      facade.ensureTaskDetail('project-1', 'task-1');
      httpMock.expectOne('/api/tasks/task-1').flush({}, { status: 404, statusText: 'Not Found' });
      clearTaskState('authorization');
    });

    it('keeps a safe Task denial visible across a later realtime authorization invalidation', () => {
      const page = facade.getTaskDetail('project-1', 'task-1');
      expect(page.status).toBe('permissionDenied');
      expect(page.task).toBeUndefined();
      expect(page.detail).toBeUndefined();
      expectNoProjectList();
      httpMock.expectNone('/api/tasks/task-1');
    });

    it('keeps a safe Task denial visible when Workspace invalidation follows the denied read', () => {
      clearTaskState('workspace');
      expectClearedTask('permissionDenied');
    });

    it('keeps the safe denial after repeated Workspace invalidations release the mounted Task intent', () => {
      clearTaskState('workspace');
      expectClearedTask('permissionDenied');
      clearTaskState('workspace');
      expectClearedTask('permissionDenied');
      facade.ensureTaskDetail('project-1', 'task-1');
      expect(facade.getTaskDetail('project-1', 'task-1').task).toBeUndefined();
      httpMock.expectOne('/api/tasks/task-1').flush(taskDetail(editableTaskDto));
      httpMock.expectOne('/api/projects/project-1').flush(projectDto);
      expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('ready');
      expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.title).toBe('Backend Task');
    });

    it.each(['session', 'tenant'] as const)('clears a denied route at a %s boundary without reloading it', (reason) => {
      clearTaskState(reason);
      expectClearedTask('loading');
    });

    it('restores a denied Task only after a fresh authorized aggregate completes', () => {
      activeWorkspace.clearWorkspace();
      TestBed.tick();
      activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
      TestBed.tick();

      expect(facade.getTaskDetail('project-1', 'task-1').task).toBeUndefined();
      httpMock.expectOne('/api/tasks/task-1').flush(taskDetail(editableTaskDto));
      httpMock.expectOne('/api/projects/project-1').flush(projectDto);

      expect(facade.getTaskDetail('project-1', 'task-1').status).toBe('ready');
      expect(facade.getTaskDetail('project-1', 'task-1').editorTask?.title).toBe('Backend Task');
    });
  });

  it('cancels an in-flight mounted Task and shows denial at a Workspace boundary', () => {
    activeWorkspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    TestBed.tick();
    facade.ensureTaskDetail('project-1', 'task-1');
    const pendingTask = httpMock.expectOne('/api/tasks/task-1');

    clearTaskState('workspace');

    expect(pendingTask.cancelled).toBe(true);
    expectClearedTask('permissionDenied');
  });

  const expectClearedTask = (status: 'loading' | 'permissionDenied'): void => {
    const page = facade.getTaskDetail('project-1', 'task-1');
    expect(page.status).toBe(status);
    expect(page.task).toBeUndefined();
    expect(page.detail).toBeUndefined();
    expectNoProjectList();
    httpMock.expectNone('/api/tasks/task-1');
  };
});

function taskDetail(task: TaskDto) {
  return {
    task,
    relationships: {},
    permissions: {},
    checklist: [],
    labels: [],
    watchState: { isWatching: false, isExplicitOptOut: false, automaticSources: [], version: 1 },
    subtasks: { items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false },
    comments: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false },
    files: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }
  };
}
