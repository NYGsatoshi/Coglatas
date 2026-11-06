import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { MyTasksFacade } from './my-tasks.facade';
import { ProjectsFacade } from './projects.facade';

const detail = { checklist: [], comments: { items: [] }, files: { items: [] },
  labels: [], subtasks: { items: [] }, task: {
    id: 'task-1', projectId: 'project-1', workspaceId: 'workspace-1',
    title: 'Current Task', status: 1, priority: 2, progressPercent: 0,
    uiPermissions: { canEdit: true, allowedTransitions: [] },
  } };
const project = { id: 'project-1', workspaceId: 'workspace-1', title: 'Current Project',
  status: 1, uiPermissions: { canCreateTask: true } };

describe('ProjectsFacade uncached Task parent request ownership', () => {
  let facade: ProjectsFacade;
  let workspace: ActiveWorkspaceFacade;
  let http: HttpTestingController;
  const touchProject = vi.fn();

  beforeEach(() => {
    touchProject.mockReset();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: RealtimeFacade, useValue: {
        durableEvents$: EMPTY, registerSubscription: () => () => {},
        registerCatchUp: () => () => {}, registerProtectedStateClearer: () => () => {},
      } },
      { provide: MyTasksFacade, useValue: { refreshIfLoaded: vi.fn() } },
      { provide: ContinueWorkingHistoryService, useValue: { touchProject } },
    ] });
    workspace = TestBed.inject(ActiveWorkspaceFacade);
    facade = TestBed.inject(ProjectsFacade);
    http = TestBed.inject(HttpTestingController);
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
  });

  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  function changeWorkspace(change: 'cleared' | 'replaced') {
    if (change === 'cleared') workspace.clearWorkspace();
    else workspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace 2' });
  }

  for (const change of ['cleared', 'replaced'] as const) {
    it(`does not dispatch an uncached parent after Workspace is ${change} before effects run`, () => {
      facade.ensureTaskDetail('project-1', 'task-1');
      const task = http.expectOne('/api/tasks/task-1');
      changeWorkspace(change); task.flush(detail);
      const parents = http.match('/api/projects/project-1');
      for (const parent of parents) parent.flush(project);
      expect(parents).toHaveLength(0);
      expect(touchProject).not.toHaveBeenCalled();
      expect(facade.getTaskDetail('project-1', 'task-1').detail).toBeUndefined();
    });

    it.each([200, 401, 403, 503])(`discards the already dispatched parent %s when Workspace is ${change} before effects run`, (status) => {
      facade.ensureTaskDetail('project-1', 'task-1');
      http.expectOne('/api/tasks/task-1').flush(detail);
      const parent = http.expectOne('/api/projects/project-1');
      changeWorkspace(change);
      const before = facade.getTaskDetail('project-1', 'task-1');
      if (status === 200) parent.flush(project);
      else parent.flush({ message: 'Old parent response' }, { status, statusText: 'Unavailable' });
      expect(facade.getTaskDetail('project-1', 'task-1')).toEqual(before);
      expect(touchProject).not.toHaveBeenCalled();
    });
  }

  it('retains authorized parent loading and history after a same-Workspace label refresh', () => {
    facade.ensureTaskDetail('project-1', 'task-1');
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Updated label' });
    http.expectOne('/api/tasks/task-1').flush(detail);
    http.expectOne('/api/projects/project-1').flush(project);
    expect(facade.getTaskDetail('project-1', 'task-1')).toMatchObject({
      status: 'ready', project: { id: 'project-1', name: 'Current Project' },
      task: { id: 'task-1', title: 'Current Task' },
    });
    expect(touchProject).toHaveBeenCalledExactlyOnceWith('project-1', 'workspace-1');
  });
});
