import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { MyTasksFacade } from './my-tasks.facade';
import { ProjectsFacade } from './projects.facade';

const project = {
  id: 'project-1',
  workspaceId: 'workspace-1',
  title: 'Authorized project',
  status: 1,
  uiPermissions: { canCreateTask: true },
};

describe('ProjectsFacade follow-up request scope', () => {
  let facade: ProjectsFacade;
  let workspace: ActiveWorkspaceFacade;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: RealtimeFacade, useValue: {
          durableEvents$: EMPTY,
          registerProtectedStateClearer: () => () => {},
        } },
        { provide: MyTasksFacade, useValue: { refreshIfLoaded: vi.fn() } },
        { provide: ContinueWorkingHistoryService, useValue: { touchProject: vi.fn() } },
      ],
    });
    workspace = TestBed.inject(ActiveWorkspaceFacade);
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Workspace 1' });
    facade = TestBed.inject(ProjectsFacade);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it.each(['cleared', 'replaced'] as const)(
    'does not dispatch old Project Task reads when the Workspace is %s before effects run',
    (change) => {
      const pendingProjects = http.expectOne((request) => request.url === '/api/projects');
      if (change === 'cleared') {
        workspace.clearWorkspace();
      } else {
        workspace.setActiveWorkspace({ id: 'workspace-2', label: 'Workspace 2' });
      }

      // Deliver the old response before deferred Workspace/Realtime teardown.
      pendingProjects.flush({ items: [project] });
      const staleTaskReads = http.match('/api/projects/project-1/tasks');
      for (const request of staleTaskReads) request.flush({ items: [] });

      expect(staleTaskReads).toHaveLength(0);
      expect(facade.getProjectsOverview().rows).toEqual([]);
    },
  );

  it('keeps the follow-up read when only the same Workspace label refreshes', () => {
    const pendingProjects = http.expectOne((request) => request.url === '/api/projects');
    workspace.setActiveWorkspace({ id: 'workspace-1', label: 'Refreshed workspace label' });
    pendingProjects.flush({ items: [project] });

    http.expectOne('/api/projects/project-1/tasks').flush({ items: [{
      id: 'task-1', projectId: 'project-1', workspaceId: 'workspace-1',
      title: 'Authorized task', status: 1, priority: 2,
      uiPermissions: { canEdit: true, allowedTransitions: [] },
    }] });
    expect(facade.getProjectsOverview().rows.map((row) => row.id)).toEqual(['task-1']);
  });
});
