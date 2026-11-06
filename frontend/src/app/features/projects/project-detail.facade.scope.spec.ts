import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { ProjectDetailFacade } from './project-detail.facade';
import { ganttSnapshotDto } from './project-detail-gantt.test-data';
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
  const history = { touchProject: vi.fn() };

  beforeEach(() => {
    history.touchProject.mockReset();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: RealtimeFacade, useValue: {
        durableEvents$: EMPTY, connectionState: signal('Connected'),
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
});
