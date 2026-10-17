import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';

import { ProjectDetailFacade, ProjectDetailViewModel } from '../project-detail.facade';
import { ProjectDetailPageComponent } from './project-detail-page.component';

beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));

describe('ProjectDetailPageComponent Task create navigation recovery', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('retries the same Task create navigation after a transient authorization clear reauthorizes the Project', async () => {
    const rendered = await render();
    const router = TestBed.inject(Router);
    const firstNavigation = deferred<boolean>();
    const navigate = vi
      .spyOn(router, 'navigate')
      .mockImplementationOnce(() => firstNavigation.promise)
      .mockResolvedValueOnce(true);
    const authorizedProject = rendered.current().project!;
    const create = (rendered.fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="project-create-task"]');

    expect(create).not.toBeNull();
    create?.click();
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenNthCalledWith(1, ['/projects', 'project-1', 'tasks', 'new']);

    rendered.setView({ status: 'loading', project: undefined });
    rendered.fixture.detectChanges();
    await Promise.resolve();

    rendered.setView({ status: 'ready', project: authorizedProject });
    rendered.fixture.detectChanges();
    await Promise.resolve();

    // Reauthorization alone must not start a duplicate navigation while the
    // original Router operation is still unresolved.
    expect(navigate).toHaveBeenCalledTimes(1);

    firstNavigation.resolve(false);
    await firstNavigation.promise;
    await flushEffects(rendered.fixture);

    expect(navigate).toHaveBeenCalledTimes(2);
    expect(navigate).toHaveBeenNthCalledWith(2, ['/projects', 'project-1', 'tasks', 'new']);
  });

  it('discards the retry intent when the reauthorized Project no longer grants Task create authority', async () => {
    const rendered = await render();
    const router = TestBed.inject(Router);
    const firstNavigation = deferred<boolean>();
    const navigate = vi
      .spyOn(router, 'navigate')
      .mockImplementationOnce(() => firstNavigation.promise)
      .mockResolvedValueOnce(true);
    const authorizedProject = rendered.current().project!;

    rendered.fixture.componentInstance.openCreateTask();
    expect(navigate).toHaveBeenCalledTimes(1);

    rendered.setView({ status: 'loading', project: undefined });
    rendered.fixture.detectChanges();
    await Promise.resolve();

    rendered.setView({
      status: 'ready',
      project: { ...authorizedProject, canCreateTask: false },
    });
    rendered.fixture.detectChanges();
    await Promise.resolve();

    firstNavigation.resolve(false);
    await firstNavigation.promise;
    await flushEffects(rendered.fixture);
    expect(navigate).toHaveBeenCalledTimes(1);

    // A later permission change must not resurrect the stale click. The user
    // has to explicitly choose New Task again after an authoritative denial.
    rendered.setView({ status: 'ready', project: authorizedProject });
    rendered.fixture.detectChanges();
    await flushEffects(rendered.fixture);

    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('discards the pending retry when the mounted route moves to another Project', async () => {
    const rendered = await render();
    const router = TestBed.inject(Router);
    const firstNavigation = deferred<boolean>();
    const navigate = vi
      .spyOn(router, 'navigate')
      .mockImplementationOnce(() => firstNavigation.promise)
      .mockResolvedValueOnce(true);

    rendered.fixture.componentInstance.openCreateTask();
    expect(navigate).toHaveBeenCalledTimes(1);

    rendered.setView({ status: 'loading', project: undefined });
    rendered.fixture.detectChanges();
    await Promise.resolve();

    rendered.setProjectId('project-2');
    rendered.fixture.detectChanges();
    await Promise.resolve();

    firstNavigation.resolve(false);
    await firstNavigation.promise;
    await flushEffects(rendered.fixture);

    expect(rendered.facade.load).toHaveBeenCalledWith('project-2');
    expect(navigate).toHaveBeenCalledTimes(1);
  });
});

async function render() {
  const viewState = signal<ProjectDetailViewModel>(view());
  const routeParams = new BehaviorSubject(convertToParamMap({ projectId: 'project-1' }));
  const facade = {
    view: () => viewState(),
    load: vi.fn(),
    release: vi.fn(),
    retryKanban: vi.fn(),
    retryTaskList: vi.fn(),
    retrySchedule: vi.fn(),
    activate: vi.fn(),
    retryPreservedScheduleIntent: vi.fn(),
    moveTask: vi.fn(),
    applyGanttEdit: vi.fn(),
    reportGanttAdapterFailure: vi.fn(),
    clearPreservedScheduleIntent: vi.fn(),
    updateKanbanConfig: vi.fn(),
    setKanbanInteractionActive: vi.fn(),
    setScheduleInteractionActive: vi.fn(),
    setKanbanSwimlane: vi.fn(),
    setIncludeOlderCompleted: vi.fn(),
  };

  await TestBed.configureTestingModule({
    imports: [ProjectDetailPageComponent],
    providers: [
      provideRouter([]),
      { provide: ProjectDetailFacade, useValue: facade },
      {
        provide: ActivatedRoute,
        useFactory: (router: Router) => ({
          paramMap: routeParams.asObservable(),
          // RouterLink builds a real UrlTree from an ActivatedRouteSnapshot.
          // Preserve the test-only reactive Project parameter stream while
          // using Router's proper snapshot for the absolute breadcrumb link.
          snapshot: router.routerState.root.snapshot,
        }),
        deps: [Router]
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(ProjectDetailPageComponent);
  fixture.detectChanges();

  return {
    fixture,
    facade,
    current: () => viewState(),
    setView: (changes: Partial<ProjectDetailViewModel>) =>
      viewState.set({ ...viewState(), ...changes }),
    setProjectId: (projectId: string) => routeParams.next(convertToParamMap({ projectId })),
  };
}

function view(): ProjectDetailViewModel {
  return {
    status: 'ready',
    project: {
      id: 'project-1',
      workspaceId: 'workspace-1',
      groupId: null,
      ownerUserId: 'owner-1',
      name: 'Project',
      description: '',
      status: 'active',
      statusLabel: 'Active',
      visibility: 'membersOnly',
      visibilityLabel: 'Members only',
      activationState: 'activated',
      versionNo: 3,
      isOperational: true,
      startDate: '',
      dueDate: '',
      group: 'Group',
      canCreateTask: true,
      canActivate: false,
      taskCounts: { total: 0, done: 0, blocked: 0 },
    },
    tasks: [],
    taskListFeedback: null,
    kanban: {
      status: 'disabled',
      snapshot: null,
      busyTaskId: null,
      focusTaskId: null,
      feedback: null,
      realtimeDegraded: false,
      reconciliationQueued: false,
    },
    schedule: {
      status: 'empty',
      snapshot: null,
      canonicalEnabled: false,
      busyItemId: null,
      focusItemId: null,
      feedback: null,
      preservedIntent: null,
      realtimeDegraded: false,
      reconciliationQueued: false,
    },
    workload: [],
    members: [],
    activation: { status: 'idle', message: null },
  };
}

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolver) => {
    resolve = resolver;
  });
  return { promise, resolve };
}

async function flushEffects(fixture: { detectChanges(): void }): Promise<void> {
  await Promise.resolve();
  fixture.detectChanges();
  await Promise.resolve();
  fixture.detectChanges();
  await Promise.resolve();
}
