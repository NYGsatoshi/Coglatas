import { Component, EventEmitter, Input, Output, Provider, signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject, EMPTY, of } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { FrontendApiError } from '../../core/api/api-error.model';
import { NotificationOpenContextService } from '../../core/notifications/notification-open-context.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { AppDataGridActionEvent } from '../../shared/grid/app-data-grid/app-data-grid.types';
import { COGLATAS_MY_TASKS_MOCK, MyTasksFacade } from './my-tasks.facade';
import {
  EMPTY_PROJECT_CREATE_OPTIONS,
  EMPTY_PROJECT_CREATE_STATE,
  ProjectCreateFacade,
  ProjectCreateOptionsViewModel,
  ProjectCreateViewModel,
} from './project-create.facade';
import { COGLATAS_PROJECTS_MOCK, ProjectsFacade } from './projects.facade';
import {
  PROJECTS_PRIMARY_PROJECT_ID,
  PROJECTS_PRIMARY_TASK_ID,
  PROJECTS_SCENARIOS,
  PROJECTS_UNAUTHORIZED_PROJECT_NAME,
  PROJECTS_UNAUTHORIZED_TASK_TITLE,
} from './projects.mock';
import {
  PROJECTS_MAXIMUM_PAGE_SIZE,
  ProjectsScenario,
  TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE,
  TaskGridRow,
} from './projects.types';
import { MyTasksPageComponent } from './my-tasks-page/my-tasks-page.component';
import { COGLATAS_WORK_VIEW_PREFERENCE_STORAGE } from './work-view-preference.service';
import { ProjectsOverviewPageComponent } from './projects-overview-page/projects-overview-page.component';
import { WorkspacesFacade } from '../workspaces/workspaces.facade';
import { WorkspaceDashboardViewModel } from '../workspaces/workspaces.types';
import { TaskDependenciesReadonlyComponent } from './task-dependencies-readonly/task-dependencies-readonly.component';
import { TaskDetailPageComponent } from './task-detail-page/task-detail-page.component';
import { TaskEditorComponent } from './task-editor/task-editor.component';
import { TaskTableComponent } from './task-table/task-table.component';

@Component({
  selector: 'app-task-table',
  standalone: true,
  template: `
    <section data-testid="stub-task-table">
      <p data-testid="stub-page-size">{{ defaultPageSize }}/{{ maximumPageSize }}</p>
      @for (row of rows; track row.id) {
        <article data-testid="task-row">
          <span>{{ row.title }}</span>
          <span>{{ row.project }}</span>
          @for (action of row.rowActions; track action.id) {
            <button
              type="button"
              [attr.data-testid]="'task-action-' + action.id"
              [attr.aria-disabled]="action.disabled"
              (click)="actionInvoked.emit({ actionId: action.id, row })"
            >
              {{ action.label }}
            </button>
          }
        </article>
      }
    </section>
  `,
})
class StubTaskTableComponent {
  @Input() rows: readonly TaskGridRow[] = [];
  @Input() defaultPageSize = 0;
  @Input() maximumPageSize = 0;
  @Output() actionInvoked = new EventEmitter<AppDataGridActionEvent<TaskGridRow>>();
}

const angularCmpKey = '\u0275cmp';

const dependencyNames = (component: unknown): string[] => {
  const cmp = (component as Record<string, { dependencies?: unknown[] }>)[angularCmpKey];
  const dependencies = (cmp?.dependencies ?? []) as Array<{
    name?: string;
    type?: { name?: string };
  }>;
  return dependencies.map((dependency) => dependency.type?.name ?? dependency.name ?? '');
};

const routeStub = {
  snapshot: {
    paramMap: convertToParamMap({
      projectId: PROJECTS_PRIMARY_PROJECT_ID,
      taskId: PROJECTS_PRIMARY_TASK_ID,
    }),
  },
};

const textContent = <T>(fixture: ComponentFixture<T>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

const normalizedError = (localErrorId: string): FrontendApiError => ({
  code: 'Http500',
  message: 'Server failure',
  details: [],
  requestId: 'trace-visible',
  redactionApplied: true,
  httpStatus: 500,
  localErrorId,
});

const query = <T extends HTMLElement, C = unknown>(
  fixture: ComponentFixture<C>,
  selector: string,
): T | null => (fixture.nativeElement as HTMLElement).querySelector<T>(selector);

const queryAll = <T extends HTMLElement, C = unknown>(
  fixture: ComponentFixture<C>,
  selector: string,
): T[] => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<T>(selector));

// These static UI cases exercise the scenario adapters, not the root session
// graph. Keep the real scenario-aware facades while replacing the unrelated
// HTTP/realtime/session dependencies so each TestBed is self-contained.
const PROJECT_CREATE_WORKSPACE_ID = '11111111-1111-4111-8111-111111111111';
const PROJECT_CREATE_SECOND_WORKSPACE_ID = '99999999-9999-4999-8999-999999999999';
const PROJECT_CREATE_READY_OPTIONS: ProjectCreateOptionsViewModel = {
  status: 'ready',
  workspaceId: PROJECT_CREATE_WORKSPACE_ID,
  data: {
    requestId: 'project-create-options-request',
    workspaceId: PROJECT_CREATE_WORKSPACE_ID,
    canCreateUngrouped: true,
    allowedVisibilities: [1],
    groups: [],
  },
};

const scenarioProviders = (
  scenario: ProjectsScenario,
  canOpenProjectCreate = false,
  committedCreate = false,
) => {
  const activeWorkspace = signal(
    canOpenProjectCreate ? { id: PROJECT_CREATE_WORKSPACE_ID, label: 'Evidence Workspace' } : null,
  );
  const options = signal(EMPTY_PROJECT_CREATE_OPTIONS);
  const createState = signal(
    committedCreate
      ? {
          status: 'committedPendingNavigation' as const,
          fieldErrors: [],
          message: 'The Project was created as Draft.',
        }
      : EMPTY_PROJECT_CREATE_STATE,
  );
  const projectCreate = {
    options,
    createState,
    loadOptions: vi.fn().mockResolvedValue(true),
    createProject: vi.fn().mockResolvedValue(false),
    retryCreatedProjectNavigation: vi.fn().mockResolvedValue(false),
    resetCreatePresentation: vi.fn(),
    clearWorkspaceScope: vi.fn(),
  };
  const workspaceRows = [PROJECT_CREATE_WORKSPACE_ID, PROJECT_CREATE_SECOND_WORKSPACE_ID].map(
    (id, index) => ({
      id,
      displayName: index === 0 ? 'Evidence Workspace' : 'Second Workspace',
      capabilities: canOpenProjectCreate ? ['openProjectCreate'] : [],
    }),
  );

  return [
    { provide: HttpClient, useValue: { get: () => of(null) } },
    { provide: COGLATAS_PROJECTS_MOCK, useValue: scenario },
    { provide: COGLATAS_MY_TASKS_MOCK, useValue: scenario },
    {
      provide: RealtimeFacade,
      useValue: {
        durableEvents$: EMPTY,
        connectionState: () => 'Degraded',
        registerProtectedStateClearer: () => () => undefined,
        registerSubscription: () => () => undefined,
        registerCatchUp: () => () => undefined,
      },
    },
    {
      provide: AuthSessionFacade,
      useValue: {
        session: () => ({
          status: 'active', isAuthenticated: true,
          currentUser: { userId: 'scenario-user', workspaces: [] },
          currentTenant: { tenantId: 'scenario-tenant', isAvailable: true, isPlatformScope: false }
        })
      },
    },
    {
      provide: ActiveWorkspaceFacade,
      useValue: { activeWorkspace, setActiveWorkspace: activeWorkspace.set.bind(activeWorkspace) },
    },
    {
      provide: WorkspacesFacade,
      useValue: {
        dashboard: signal({
          status: 'ready',
          title: 'Workspaces',
          subtitle: '',
          workspaces: workspaceRows,
          pageCapabilities: [],
        }),
      },
    },
    { provide: ProjectCreateFacade, useValue: projectCreate },
    {
      provide: NotificationOpenContextService,
      useValue: { digestWorkspaceId: () => null, clear: () => undefined },
    },
  ];
};

const renderProjectsOverview = async (
  scenario: ProjectsScenario = PROJECTS_SCENARIOS.default,
  canOpenProjectCreate = false,
  committedCreate = false,
  routeOverride?: {
    readonly paramMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
    readonly queryParamMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  },
) => {
  await TestBed.configureTestingModule({
    imports: [ProjectsOverviewPageComponent],
    providers: [
      provideRouter([]),
      ...scenarioProviders(scenario, canOpenProjectCreate, committedCreate),
      ...(routeOverride
        ? [
            {
              provide: ActivatedRoute,
              useValue: {
                snapshot: {
                  paramMap: routeOverride.paramMap.value,
                  queryParamMap: routeOverride.queryParamMap.value,
                },
                paramMap: routeOverride.paramMap.asObservable(),
                queryParamMap: routeOverride.queryParamMap.asObservable(),
              },
            },
          ]
        : []),
    ],
  })
    .overrideComponent(ProjectsOverviewPageComponent, {
      remove: { imports: [TaskTableComponent] },
      add: { imports: [StubTaskTableComponent] },
    })
    .compileComponents();

  if (routeOverride) {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  }
  const fixture = TestBed.createComponent(ProjectsOverviewPageComponent);
  fixture.detectChanges();
  return fixture;
};

const renderMyTasks = async (scenario: ProjectsScenario = PROJECTS_SCENARIOS.default, extraProviders: readonly Provider[] = []) => {
  await TestBed.configureTestingModule({
    imports: [MyTasksPageComponent],
    providers: [provideRouter([]), ...scenarioProviders(scenario), ...extraProviders],
  })
    .overrideComponent(MyTasksPageComponent, {
      remove: { imports: [TaskTableComponent] },
      add: { imports: [StubTaskTableComponent] },
    })
    .compileComponents();

  const fixture = TestBed.createComponent(MyTasksPageComponent);
  fixture.detectChanges();
  return fixture;
};

const renderTaskDetail = async (
  params: BehaviorSubject<ReturnType<typeof convertToParamMap>>,
  scenario: ProjectsScenario = PROJECTS_SCENARIOS.default,
) => {
  await TestBed.configureTestingModule({
    imports: [TaskDetailPageComponent],
    providers: [
      provideRouter([]),
      ...scenarioProviders(scenario),
      { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(TaskDetailPageComponent);
  fixture.detectChanges();
  return fixture;
};

describe('Projects and tasks mock UI', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => { localStorage.clear(); TestBed.resetTestingModule(); });

  it('route pages do not directly use AgGridAngular', () => {
    const routePageDependencies = [
      ...dependencyNames(ProjectsOverviewPageComponent),
      ...dependencyNames(MyTasksPageComponent),
      ...dependencyNames(TaskDetailPageComponent),
    ];

    expect(routePageDependencies).not.toContain('AgGridAngular');
  });

  it('keeps ag-grid-enterprise absent from feature component dependencies', () => {
    const enterprisePackageName = 'ag-grid' + '-enterprise';
    const featureDependencies = [
      ...dependencyNames(ProjectsOverviewPageComponent),
      ...dependencyNames(MyTasksPageComponent),
      ...dependencyNames(TaskDetailPageComponent),
      ...dependencyNames(TaskTableComponent),
    ].join(' ');

    expect(featureDependencies).not.toContain(enterprisePackageName);
    expect(
      dependencyNames(TaskTableComponent).some((name) => name.includes('AppDataGridComponent')),
    ).toBe(true);
  });

  it('keeps project discovery separate from task creation and detail', async () => {
    const fixture = await renderProjectsOverview();

    expect(query(fixture, '[data-testid="project-summary-card"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="task-create-form"]')).toBeNull();
    expect(query(fixture, '[data-testid="stub-task-table"]')).toBeNull();
  });

  it('shows the full Project create opener only for the backend presentation capability', async () => {
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.default, true);
    const create = query<HTMLButtonElement>(fixture, '[data-testid="projects-create-project"]');
    expect(create).not.toBeNull();

    create?.click();
    fixture.detectChanges();
    const projectCreate = TestBed.inject(ProjectCreateFacade) as unknown as {
      loadOptions: ReturnType<typeof vi.fn>;
    };
    expect(projectCreate.loadOptions).toHaveBeenCalledWith(PROJECT_CREATE_WORKSPACE_ID);
    expect(query(fixture, '[role="dialog"]')).not.toBeNull();
  });

  it('keeps the Project create opener absent without the backend presentation capability', async () => {
    const fixture = await renderProjectsOverview();
    expect(query(fixture, '[data-testid="projects-create-project"]')).toBeNull();
  });

  it('preserves a filled create flow across a transient Workspace recheck and closes on a real switch', async () => {
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.default, true);
    const projectCreate = TestBed.inject(ProjectCreateFacade) as unknown as {
      options: { set(value: ProjectCreateOptionsViewModel): void };
      loadOptions: ReturnType<typeof vi.fn>;
      clearWorkspaceScope: ReturnType<typeof vi.fn>;
    };
    const activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    const workspaces = TestBed.inject(WorkspacesFacade) as unknown as {
      dashboard: WritableSignal<WorkspaceDashboardViewModel>;
    };
    const readyDashboard = workspaces.dashboard();
    projectCreate.options.set(PROJECT_CREATE_READY_OPTIONS);
    fixture.detectChanges();
    query<HTMLButtonElement>(fixture, '[data-testid="projects-create-project"]')?.click();
    fixture.detectChanges();

    const title = query<HTMLInputElement>(fixture, '[data-testid="project-create-title"]');
    expect(title).not.toBeNull();
    title!.value = 'Draft retained during reauthorization';
    title!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(projectCreate.loadOptions).toHaveBeenCalledTimes(1);

    workspaces.dashboard.set({ ...readyDashboard, status: 'loading', workspaces: [] });
    projectCreate.options.set(EMPTY_PROJECT_CREATE_OPTIONS);
    activeWorkspace.setActiveWorkspace(null);
    TestBed.flushEffects();
    fixture.detectChanges();

    expect(fixture.componentInstance.createDialogOpen()).toBe(true);
    expect(projectCreate.clearWorkspaceScope).not.toHaveBeenCalled();
    expect(
      query<HTMLFormElement>(fixture, '[data-testid="project-create-form"]')?.hidden,
    ).toBe(true);

    activeWorkspace.setActiveWorkspace({
      id: PROJECT_CREATE_WORKSPACE_ID,
      label: 'Evidence Workspace',
    });
    TestBed.flushEffects();
    workspaces.dashboard.set(readyDashboard);
    fixture.detectChanges();
    expect(projectCreate.loadOptions).toHaveBeenCalledTimes(2);
    expect(projectCreate.loadOptions).toHaveBeenLastCalledWith(PROJECT_CREATE_WORKSPACE_ID);

    projectCreate.options.set(PROJECT_CREATE_READY_OPTIONS);
    fixture.detectChanges();
    expect(query<HTMLInputElement>(fixture, '[data-testid="project-create-title"]')?.value).toBe(
      'Draft retained during reauthorization',
    );

    activeWorkspace.setActiveWorkspace({
      id: PROJECT_CREATE_SECOND_WORKSPACE_ID,
      label: 'Second Workspace',
    });
    TestBed.flushEffects();
    fixture.detectChanges();
    expect(fixture.componentInstance.createDialogOpen()).toBe(false);
    expect(projectCreate.clearWorkspaceScope).not.toHaveBeenCalled();
  });

  it('reauthorizes a filled create flow whose Workspace is fixed by the route', async () => {
    const paramMap = new BehaviorSubject(
      convertToParamMap({ workspaceId: PROJECT_CREATE_WORKSPACE_ID }),
    );
    const queryParamMap = new BehaviorSubject(convertToParamMap({}));
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.default, true, false, {
      paramMap,
      queryParamMap,
    });
    const projectCreate = TestBed.inject(ProjectCreateFacade) as unknown as {
      options: WritableSignal<ProjectCreateOptionsViewModel>;
      loadOptions: ReturnType<typeof vi.fn>;
    };
    const activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    const workspaces = TestBed.inject(WorkspacesFacade) as unknown as {
      dashboard: WritableSignal<WorkspaceDashboardViewModel>;
    };
    const readyDashboard = workspaces.dashboard();

    projectCreate.options.set(PROJECT_CREATE_READY_OPTIONS);
    fixture.detectChanges();
    query<HTMLButtonElement>(fixture, '[data-testid="projects-create-project"]')?.click();
    fixture.detectChanges();
    const title = query<HTMLInputElement>(fixture, '[data-testid="project-create-title"]');
    title!.value = 'Route-owned draft retained';
    title!.dispatchEvent(new Event('input'));

    workspaces.dashboard.set({ ...readyDashboard, status: 'loading', workspaces: [] });
    projectCreate.options.set(EMPTY_PROJECT_CREATE_OPTIONS);
    activeWorkspace.setActiveWorkspace(null);
    TestBed.flushEffects();
    fixture.detectChanges();
    expect(fixture.componentInstance.createDialogOpen()).toBe(true);

    workspaces.dashboard.set(readyDashboard);
    activeWorkspace.setActiveWorkspace({
      id: PROJECT_CREATE_WORKSPACE_ID,
      label: 'Evidence Workspace',
    });
    TestBed.flushEffects();
    fixture.detectChanges();
    expect(projectCreate.loadOptions).toHaveBeenCalledTimes(2);

    projectCreate.options.set(PROJECT_CREATE_READY_OPTIONS);
    fixture.detectChanges();
    expect(query<HTMLInputElement>(fixture, '[data-testid="project-create-title"]')?.value).toBe(
      'Route-owned draft retained',
    );
  });

  it('closes an uncommitted create flow when the same Workspace loses its create affordance', async () => {
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.default, true);
    const projectCreate = TestBed.inject(ProjectCreateFacade) as unknown as {
      clearWorkspaceScope: ReturnType<typeof vi.fn>;
    };
    const workspaces = TestBed.inject(WorkspacesFacade) as unknown as {
      dashboard: WritableSignal<WorkspaceDashboardViewModel>;
    };
    const readyDashboard = workspaces.dashboard();

    query<HTMLButtonElement>(fixture, '[data-testid="projects-create-project"]')?.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.createDialogOpen()).toBe(true);

    workspaces.dashboard.set({
      ...readyDashboard,
      workspaces: readyDashboard.workspaces.map((workspace) =>
        workspace.id === PROJECT_CREATE_WORKSPACE_ID
          ? { ...workspace, capabilities: [] }
          : workspace,
      ),
    });
    TestBed.flushEffects();
    fixture.detectChanges();

    expect(fixture.componentInstance.createDialogOpen()).toBe(false);
    expect(projectCreate.clearWorkspaceScope).not.toHaveBeenCalled();
  });

  it('clears an open create flow when a Workspace recheck ends with no access', async () => {
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.default, true);
    const projectCreate = TestBed.inject(ProjectCreateFacade) as unknown as {
      options: { set(value: ProjectCreateOptionsViewModel): void };
      clearWorkspaceScope: ReturnType<typeof vi.fn>;
    };
    const activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    const workspaces = TestBed.inject(WorkspacesFacade) as unknown as {
      dashboard: WritableSignal<WorkspaceDashboardViewModel>;
    };
    const readyDashboard = workspaces.dashboard();

    query<HTMLButtonElement>(fixture, '[data-testid="projects-create-project"]')?.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.createDialogOpen()).toBe(true);

    workspaces.dashboard.set({ ...readyDashboard, status: 'loading', workspaces: [] });
    projectCreate.options.set(EMPTY_PROJECT_CREATE_OPTIONS);
    activeWorkspace.setActiveWorkspace(null);
    TestBed.flushEffects();
    fixture.detectChanges();
    expect(fixture.componentInstance.createDialogOpen()).toBe(true);
    expect(projectCreate.clearWorkspaceScope).not.toHaveBeenCalled();

    workspaces.dashboard.set({
      ...readyDashboard,
      status: 'noWorkspaceAccess',
      workspaces: [],
    });
    TestBed.flushEffects();
    fixture.detectChanges();
    expect(fixture.componentInstance.createDialogOpen()).toBe(false);
    expect(projectCreate.clearWorkspaceScope).not.toHaveBeenCalled();
  });

  it('consumes the Workspace quick-action query once and closes before opening another Workspace', async () => {
    const paramMap = new BehaviorSubject(
      convertToParamMap({ workspaceId: PROJECT_CREATE_WORKSPACE_ID }),
    );
    const queryParamMap = new BehaviorSubject(convertToParamMap({ create: '1' }));
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.default, true, false, {
      paramMap,
      queryParamMap,
    });
    TestBed.flushEffects();
    fixture.detectChanges();

    const projectCreate = TestBed.inject(ProjectCreateFacade) as unknown as {
      loadOptions: ReturnType<typeof vi.fn>;
      clearWorkspaceScope: ReturnType<typeof vi.fn>;
    };
    const router = TestBed.inject(Router);
    expect(projectCreate.loadOptions).toHaveBeenCalledTimes(1);
    expect(projectCreate.loadOptions).toHaveBeenLastCalledWith(PROJECT_CREATE_WORKSPACE_ID);
    expect(router.navigate).toHaveBeenCalledWith([], {
      relativeTo: TestBed.inject(ActivatedRoute),
      queryParams: { create: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });

    queryParamMap.next(convertToParamMap({}));
    fixture.detectChanges();
    TestBed.flushEffects();
    paramMap.next(convertToParamMap({ workspaceId: PROJECT_CREATE_SECOND_WORKSPACE_ID }));
    fixture.detectChanges();
    TestBed.flushEffects();
    expect(projectCreate.clearWorkspaceScope).not.toHaveBeenCalled();
    expect(fixture.componentInstance.createDialogOpen()).toBe(false);

    queryParamMap.next(convertToParamMap({ create: '1' }));
    fixture.detectChanges();
    TestBed.flushEffects();
    expect(projectCreate.loadOptions).toHaveBeenCalledTimes(2);
    expect(projectCreate.loadOptions).toHaveBeenLastCalledWith(PROJECT_CREATE_SECOND_WORKSPACE_ID);
  });

  it('offers capability-independent opaque resume after a committed create', async () => {
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.default, false, true);
    const projectCreate = TestBed.inject(ProjectCreateFacade) as unknown as {
      createState: WritableSignal<ProjectCreateViewModel>;
      retryCreatedProjectNavigation: ReturnType<typeof vi.fn>;
      clearWorkspaceScope: ReturnType<typeof vi.fn>;
    };
    let resolveRetry!: (result: boolean) => void;
    projectCreate.retryCreatedProjectNavigation.mockImplementation(() => {
      projectCreate.createState.set({
        status: 'submitting',
        fieldErrors: [],
        createdProjectId: 'opaque-created-project',
      });
      return new Promise<boolean>((resolve) => {
        resolveRetry = resolve;
      });
    });
    const resume = query<HTMLButtonElement>(
      fixture,
      '[data-testid="projects-resume-created-project"]',
    );
    expect(query(fixture, '[data-testid="projects-create-project"]')).toBeNull();
    expect(resume?.textContent).not.toContain(PROJECT_CREATE_WORKSPACE_ID);

    resume?.click();
    fixture.detectChanges();
    expect(query(fixture, '[data-testid="project-create-pending"]')).not.toBeNull();
    query<HTMLButtonElement>(fixture, '.coglatas-dialog__confirm')?.click();
    TestBed.flushEffects();
    fixture.detectChanges();
    expect(projectCreate.retryCreatedProjectNavigation).toHaveBeenCalledOnce();
    expect(fixture.componentInstance.createDialogOpen()).toBe(true);
    expect(projectCreate.clearWorkspaceScope).not.toHaveBeenCalled();

    projectCreate.createState.set({
      status: 'committedPendingNavigation',
      fieldErrors: [],
      message: 'The Project was created as Draft.',
    });
    resolveRetry(false);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(query(fixture, '[data-testid="project-create-pending"]')).not.toBeNull();
  });

  it('renders the authoritative Project and Task hierarchy and updates it after a Task route switch', async () => {
    const params = new BehaviorSubject(
      convertToParamMap({
        projectId: PROJECTS_PRIMARY_PROJECT_ID,
        taskId: PROJECTS_PRIMARY_TASK_ID,
      }),
    );
    const fixture = await renderTaskDetail(params);

    const hierarchy = query<HTMLElement>(fixture, '[data-testid="project-task-hierarchy"]');
    const parentProject = query<HTMLAnchorElement>(fixture, '[data-testid="parent-project-link"]');
    expect(hierarchy?.textContent).toContain('Sample Project Alpha');
    expect(query<HTMLElement>(fixture, '[aria-current="page"]')?.textContent?.trim()).toBe(
      'Prepare sample kickoff checklist',
    );
    expect(parentProject?.getAttribute('href')).toBe(`/projects/${PROJECTS_PRIMARY_PROJECT_ID}`);
    expect(queryAll<HTMLElement>(fixture, 'h1')).toHaveLength(1);

    params.next(
      convertToParamMap({
        projectId: PROJECTS_PRIMARY_PROJECT_ID,
        taskId: 'task-sample-002',
      }),
    );
    fixture.detectChanges();

    expect(query<HTMLElement>(fixture, '[aria-current="page"]')?.textContent?.trim()).toBe(
      'Collect sample project notes',
    );
    expect(query<HTMLElement>(fixture, 'h1')?.textContent?.trim()).toBe(
      'Collect sample project notes',
    );
    expect(
      query<HTMLAnchorElement>(fixture, '[data-testid="parent-project-link"]')?.textContent?.trim(),
    ).toBe('Sample Project Alpha');
  });

  it('renders Project load failures as retryable errors instead of empty states', async () => {
    const fixture = await renderProjectsOverview({
      ...PROJECTS_SCENARIOS.default,
      status: 'error',
      projects: [],
      tasks: [],
      message: 'Projects could not be loaded. Try again.',
      error: normalizedError('local-projects-error'),
    } satisfies ProjectsScenario);

    expect(query(fixture, '[data-testid="projects-load-error"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="projects-retry"]')).not.toBeNull();
    expect(textContent(fixture)).not.toContain('No projects');
  });

  it('renders My Tasks failures as retryable errors instead of empty states', async () => {
    const fixture = await renderMyTasks({
      ...PROJECTS_SCENARIOS.default,
      myTasksStatus: 'error',
      myTasks: [],
      myTasksMessage: 'My Tasks could not be loaded. Try again.',
      myTasksError: normalizedError('local-my-tasks-error'),
    } satisfies ProjectsScenario);

    expect(query(fixture, '[data-testid="my-tasks-load-error"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="my-tasks-retry"]')).not.toBeNull();
    expect(textContent(fixture)).not.toContain('No tasks');
  });

  it('does not render unauthorized project or task names in denied state', async () => {
    const fixture = await renderProjectsOverview(PROJECTS_SCENARIOS.permissionDenied);
    const text = textContent(fixture);

    expect(text).not.toContain(PROJECTS_UNAUTHORIZED_PROJECT_NAME);
    expect(text).not.toContain(PROJECTS_UNAUTHORIZED_TASK_TITLE);
  });

  it('limits maximum page size to 100', () => {
    TestBed.configureTestingModule({
      providers: scenarioProviders(PROJECTS_SCENARIOS.manyRowsBoundedPage),
    });
    const facade = TestBed.inject(ProjectsFacade);

    expect(facade.getMyTasks().pageSize.maximumPageSize).toBe(PROJECTS_MAXIMUM_PAGE_SIZE);
    expect(PROJECTS_MAXIMUM_PAGE_SIZE).toBe(100);
  });

  it('uses a semantic task list as the narrow/touch primary workflow', async () => {
    const fixture = await renderMyTasks(PROJECTS_SCENARIOS.mobile);

    expect(query(fixture, '[data-testid="my-tasks-semantic-list"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="my-tasks-kanban"]')).toBeNull();
  });

  it('keeps My Tasks List-only after canonical Project Kanban is delivered', async () => {
    const fixture = await renderMyTasks();
    expect(query(fixture, '[data-testid="my-tasks-kanban"]')).toBeNull();
    expect(textContent(fixture)).toContain(
      'List is the canonical cross-Project My Tasks projection',
    );
    expect(textContent(fixture)).toContain('Project Kanban is available from Project Detail');
  });

  it('offers one-action presets and announces the exact applied relationship and Stage', async () => {
    const fixture = await renderMyTasks();
    TestBed.flushEffects();
    fixture.detectChanges();

    query<HTMLButtonElement>(fixture, '[data-testid="my-tasks-preset-completed"]')?.click();
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Relationship: Completed');
    expect(textContent(fixture)).toContain('Stage: Done');
    expect(query(fixture, '[data-testid="my-tasks-filter-announcement"]')?.textContent).toContain('Completed preset applied');
  });

  it('saves, applies, and deletes a combined filter through keyboard-native controls and safe chips', async () => {
    const fixture = await renderMyTasks();
    TestBed.flushEffects();
    fixture.detectChanges();
    const projectId = '55555555-5555-4555-8555-555555555555';

    const project = query<HTMLInputElement>(fixture, '[data-testid="my-tasks-project-filter"]')!;
    project.value = projectId;
    project.dispatchEvent(new Event('change'));
    const stage = query<HTMLSelectElement>(fixture, '[data-testid="my-tasks-stage-filter"]')!;
    stage.value = 'review';
    stage.dispatchEvent(new Event('change'));
    const priority = query<HTMLSelectElement>(fixture, '[data-testid="my-tasks-priority-filter"]')!;
    priority.value = 'high';
    priority.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const name = query<HTMLInputElement>(fixture, '[data-testid="my-tasks-saved-filter-name"]')!;
    name.value = 'Review queue';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    name.focus();
    name.closest('form')?.requestSubmit();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Review queue');
    expect(query(fixture, '[data-testid="my-tasks-filter-announcement"]')?.textContent).toContain('Saved filter Review queue');
    expect(document.activeElement).toBe(name);
    const stored = localStorage.getItem('coglatas.work-view.saved-filters.v1:scenario-tenant:scenario-user:my-tasks') ?? '';
    expect(stored).toContain(projectId);
    expect(stored).not.toMatch(/Sample Project|Prepare sample|rows|counts|permissions/iu);

    query<HTMLButtonElement>(fixture, '[data-testid="my-tasks-clear-filters"]')?.click();
    fixture.detectChanges();
    queryAll<HTMLButtonElement>(fixture, 'button').find((button) => button.getAttribute('aria-label') === 'Apply saved filter Review queue')?.click();
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Project filter active');
    expect(textContent(fixture)).toContain('Stage: Review');
    expect(textContent(fixture)).toContain('Priority: High');
    expect(query<HTMLInputElement>(fixture, '[data-testid="my-tasks-project-filter"]')?.value).toBe('');
    expect(query<HTMLInputElement>(fixture, '[data-testid="my-tasks-project-filter"]')?.placeholder).toBe('Saved Project condition is active');
    expect(textContent(fixture)).not.toContain(projectId);

    queryAll<HTMLButtonElement>(fixture, 'button').find((button) => button.getAttribute('aria-label') === 'Delete saved filter Review queue')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(textContent(fixture)).toContain('No custom filters saved');
    expect(document.activeElement).toBe(name);
  });

  it('keeps saved-filter recovery controls mounted through a stale Project error', async () => {
    const projectId = '77777777-7777-4777-8777-777777777777';
    localStorage.setItem('coglatas.work-view.saved-filters.v1:scenario-tenant:scenario-user:my-tasks', JSON.stringify({
      version: 1,
      filters: [{
        id: 'saved-77777777', name: 'Stale Project view',
        snapshot: { selectedTab: 'completed', projectId, stageCategory: 'done', priority: '', blocked: '', search: '', timeGroup: null }
      }]
    }));
    const fixture = await renderMyTasks({
      ...PROJECTS_SCENARIOS.default,
      myTasksStatus: 'error',
      myTasks: [],
      myTasksMessage: 'The selected Project is not available.',
      myTasksError: { ...normalizedError('stale-project'), httpStatus: 404 }
    } satisfies ProjectsScenario);
    TestBed.flushEffects();
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="my-tasks-load-error"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="my-tasks-saved-filters"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="my-tasks-filter-summary"]')).not.toBeNull();
    const apply = queryAll<HTMLButtonElement>(fixture, 'button')
      .find((button) => button.getAttribute('aria-label') === 'Apply saved filter Stale Project view')!;
    apply.focus();
    apply.click();
    fixture.detectChanges();
    expect(document.activeElement).toBe(apply);
    expect(textContent(fixture)).toContain('Project filter active');
    expect(textContent(fixture)).not.toContain(projectId);

    const clear = query<HTMLButtonElement>(fixture, '[data-testid="my-tasks-clear-filters"]')!;
    clear.focus();
    clear.click();
    fixture.detectChanges();
    expect(document.activeElement).toBe(clear);
    expect(TestBed.inject(MyTasksFacade).getMyTasks().selectedTab).toBe('assigned');
    expect(TestBed.inject(MyTasksFacade).getMyTasks().filters.projectId).toBe('');

    queryAll<HTMLButtonElement>(fixture, 'button')
      .find((button) => button.getAttribute('aria-label') === 'Delete saved filter Stale Project view')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(textContent(fixture)).toContain('No custom filters saved');
  });

  it('truthfully disables custom persistence when storage is unavailable while keeping presets usable', async () => {
    const fixture = await renderMyTasks(PROJECTS_SCENARIOS.default, [
      { provide: COGLATAS_WORK_VIEW_PREFERENCE_STORAGE, useValue: null }
    ]);
    TestBed.flushEffects();
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="my-tasks-saved-filters-unavailable"]')?.textContent).toContain('Current filters and frequent presets still work');
    expect(query(fixture, '[data-testid="my-tasks-saved-filter-name"]')).toBeNull();
    expect(query(fixture, '[data-testid="my-tasks-save-filter"]')).toBeNull();
    expect(query(fixture, '[data-testid="my-tasks-preset-running"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="my-tasks-filter-announcement"]')?.textContent).toContain('Saved filters are unavailable');
  });
});

describe('TaskEditorComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  const renderEditor = async (scenario: ProjectsScenario = PROJECTS_SCENARIOS.default) => {
    await TestBed.configureTestingModule({
      imports: [TaskEditorComponent],
      providers: scenarioProviders(scenario),
    }).compileComponents();

    const facade = TestBed.inject(ProjectsFacade);
    const detail = facade.getTaskDetail(PROJECTS_PRIMARY_PROJECT_ID, PROJECTS_PRIMARY_TASK_ID);
    const fixture = TestBed.createComponent(TaskEditorComponent);
    fixture.componentRef.setInput('task', detail.editorTask);
    fixture.componentRef.setInput('capabilities', detail.capabilities);
    fixture.componentRef.setInput('state', detail.detailState);
    fixture.componentRef.setInput('transitionNote', detail.transitionNote);
    fixture.componentRef.setInput('mutationState', { status: 'idle' });
    fixture.componentRef.setInput('expectedVersion', '1');
    fixture.detectChanges();
    return fixture;
  };

  it('progressPercent rejects below 0, above 100, and non-integer values', async () => {
    const fixture = await renderEditor();
    const control = fixture.componentInstance.form.controls.progressPercent;

    control.setValue(-1);
    expect(control.valid).toBe(false);

    control.setValue(101);
    expect(control.valid).toBe(false);

    control.setValue(10.5);
    expect(control.valid).toBe(false);

    control.setValue(85);
    expect(control.valid).toBe(true);
  });

  it('makes milestone read-only without capability', async () => {
    const fixture = await renderEditor(PROJECTS_SCENARIOS.milestoneReadOnly);

    expect(query<HTMLInputElement>(fixture, '[data-testid="task-milestone-input"]')?.readOnly).toBe(
      true,
    );
  });

  it('renders existing task detail in editable backend fields', async () => {
    const fixture = await renderEditor();

    expect(query(fixture, '[data-testid="task-editor-readonly-note"]')).toBeNull();
    expect(query<HTMLInputElement>(fixture, '[data-testid="task-title-input"]')?.value).toContain(
      'Prepare sample kickoff',
    );
    expect(query<HTMLInputElement>(fixture, '[data-testid="task-title-input"]')?.readOnly).toBe(
      false,
    );
    expect(
      query<HTMLTextAreaElement>(fixture, '[data-testid="task-description-input"]')?.readOnly,
    ).toBe(false);
    expect(query<HTMLButtonElement>(fixture, '[data-testid="task-save-button"]')?.disabled).toBe(
      false,
    );
  });

  it('preserves user input when a save failure is shown', async () => {
    const fixture = await renderEditor();
    const title = query<HTMLInputElement>(fixture, '[data-testid="task-title-input"]');
    title!.value = 'Changed but not persisted';
    title!.dispatchEvent(new Event('input'));
    fixture.componentRef.setInput('mutationState', {
      status: 'failure',
      message: 'Backend rejected save.',
    });
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="task-save-error"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="task-save-success"]')).toBeNull();
    expect(query<HTMLInputElement>(fixture, '[data-testid="task-title-input"]')?.value).toBe(
      'Changed but not persisted',
    );
  });

  it('keeps status read-only so ordinary saves cannot invoke a workflow transition', async () => {
    const fixture = await renderEditor();
    expect(query<HTMLInputElement>(fixture, '[data-testid="task-status-readonly"]')?.readOnly).toBe(
      true,
    );
  });

  it('keeps status transitions backend-authoritative by design', async () => {
    const fixture = await renderEditor();

    expect(TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE.owner).toBe(
      'backendAuthoritativeDuringApiWiring',
    );
    expect(textContent(fixture)).toContain('live API remains authoritative');
  });

  it('keeps status read-only when backend permissions do not allow transitions', async () => {
    const fixture = await renderEditor(PROJECTS_SCENARIOS.milestoneReadOnly);
    const task = {
      ...PROJECTS_SCENARIOS.default.tasks[0],
      allowedTransitions: [],
      capabilities: PROJECTS_SCENARIOS.default.tasks[0].capabilities.filter(
        (capability) => capability !== 'changeTaskStatus',
      ),
    };

    fixture.componentRef.setInput('task', task);
    fixture.componentRef.setInput('capabilities', task.capabilities);
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="task-status-disabled-note"]')).not.toBeNull();
    expect(query<HTMLInputElement>(fixture, '[data-testid="task-status-readonly"]')?.readOnly).toBe(
      true,
    );
  });

  it('shows recoverable invalid transition and row version conflict states', async () => {
    const invalidTransition = await renderEditor(PROJECTS_SCENARIOS.invalidStateTransition);
    expect(query(invalidTransition, '[data-testid="invalid-state-transition"]')).not.toBeNull();

    TestBed.resetTestingModule();

    const rowConflict = await renderEditor(PROJECTS_SCENARIOS.rowVersionConflict);
    expect(query(rowConflict, '[data-testid="row-version-conflict"]')).not.toBeNull();
  });
});

describe('TaskDependenciesReadonlyComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders dependencies as display-only content', async () => {
    await TestBed.configureTestingModule({
      imports: [TaskDependenciesReadonlyComponent],
      providers: scenarioProviders(PROJECTS_SCENARIOS.dependenciesDisplayOnly),
    }).compileComponents();

    const facade = TestBed.inject(ProjectsFacade);
    const fixture = TestBed.createComponent(TaskDependenciesReadonlyComponent);
    fixture.componentRef.setInput(
      'dependencies',
      facade.getTaskDetail(PROJECTS_PRIMARY_PROJECT_ID, PROJECTS_PRIMARY_TASK_ID).dependencies,
    );
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="dependencies-display-only-note"]')).not.toBeNull();
    expect(query(fixture, '[draggable="true"]')).toBeNull();
    expect(query(fixture, 'input')).toBeNull();
  });
});
