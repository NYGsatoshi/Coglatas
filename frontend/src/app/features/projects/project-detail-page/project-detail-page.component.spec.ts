import { By } from '@angular/platform-browser';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';

import { mapProjectKanbanSnapshot } from '../project-kanban.models';
import { snapshotDto } from '../project-kanban.test-data';
import { mapProjectGanttSnapshot } from '../project-gantt.models';
import { ganttSnapshotDto, viewerGanttSnapshotDto } from '../project-detail-gantt.test-data';
import {
  ProjectDetailFacade,
  ProjectDetailViewModel,
  ProjectKanbanViewModel,
  ProjectScheduleViewModel
} from '../project-detail.facade';
import { ProjectDetailPageComponent } from './project-detail-page.component';

describe('ProjectDetailPageComponent canonical Kanban states', () => {
  it('renders WIP, hierarchy, blocked, priority, recent-Done, and narrow-layout meaning as text', async () => {
    const { fixture } = await render(kanbanView('ready'));
    fixture.componentInstance.tab.set('tasks');
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Done shows 30 recent days');
    expect(text).toContain('Todo exceeds its warning limit.');
    expect(text).toContain('Warning: WIP limit 1 exceeded.');
    expect(text).toContain('Parent summary task');
    expect(text).toContain('Derived progress: 50%');
    expect(text).toContain('Derived dates: 2026-07-01 to 2026-07-31');
    expect(text).toContain('1 of 2 child tasks complete');
    expect(text).toContain('No parent task');
    expect(text).toContain('Priority: Critical');
    expect(text).toContain('Blocked');
    expect(text).toContain('grouped vertical list');
    expect((fixture.nativeElement as HTMLElement).querySelector('ejs-kanban')).toBeNull();
  });

  it('renders an authorized empty board separately from permission denial', async () => {
    const emptySnapshot = { ...mapProjectKanbanSnapshot(snapshotDto()), cards: [] };
    const { fixture: emptyFixture } = await render({ ...kanbanView('empty'), snapshot: emptySnapshot });
    emptyFixture.componentInstance.tab.set('tasks');
    emptyFixture.detectChanges();
    expect((emptyFixture.nativeElement as HTMLElement).textContent).toContain('No authorized Tasks match');
    emptyFixture.destroy();
    TestBed.resetTestingModule();

    const { fixture: deniedFixture } = await render({ ...kanbanView('permissionDenied'), snapshot: null });
    deniedFixture.componentInstance.tab.set('tasks');
    deniedFixture.detectChanges();
    expect((deniedFixture.nativeElement as HTMLElement).textContent).toContain('Project Kanban is not available');
  });

  it('falls back to the maintained Project Task List when the presentation flag is disabled', async () => {
    const { fixture, facade } = await render(
      { ...kanbanView('disabled'), snapshot: null, feedback: 'Project Kanban is disabled. The maintained Task List remains available.' },
      scheduleView(),
      { taskListFeedback: 'The Task list could not be synchronized.' }
    );
    fixture.componentInstance.tab.set('tasks');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('maintained Task List');
    expect(host.querySelector('coglatas-kanban')).toBeNull();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('could not be synchronized');
    host.querySelector<HTMLButtonElement>('[data-testid="task-list-retry"]')?.click();
    expect(facade.retryTaskList).toHaveBeenCalledOnce();
  });

  it('surfaces a transient authoritative Task-list refresh failure and offers a retry', async () => {
    const { fixture, facade } = await render(
      kanbanView('ready'),
      scheduleView(),
      { taskListFeedback: 'The Task list could not be synchronized. Temporarily unavailable.' }
    );
    fixture.componentInstance.tab.set('list');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('could not be synchronized');
    const retry = host.querySelector<HTMLButtonElement>('[data-testid="task-list-retry"]');
    expect(retry).not.toBeNull();
    retry?.click();
    expect(facade.retryTaskList).toHaveBeenCalledOnce();
  });

  it('renders the canonical narrow Schedule projection with calendar, WorkItem, dependency, warning, and form actions', async () => {
    const { fixture } = await render(kanbanView('ready'));
    fixture.componentInstance.tab.set('schedule');
    fixture.componentInstance.schedulePresentation.set('narrow');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const text = host.textContent ?? '';
    expect(text).toContain('Workspace timezone: Asia/Tokyo');
    expect(text).toContain('Scheduled work');
    expect(text).toContain('Canonical schedule task');
    expect(text).toContain('Milestones');
    expect(text).toContain('Launch');
    expect(text).toContain('Unscheduled work');
    expect(text).toContain('Unscheduled task');
    expect(text).toContain('UNSCHEDULED');
    expect(text).toContain('Dependencies');
    expect(text).toContain('Remove FS dependency');
    expect(text).toContain('Edit dates');
    expect(text).toContain('Edit progress');
    expect(text).toContain('Add FS predecessor');
    expect(host.querySelector('ejs-gantt')).toBeNull();
  });

  it('uses backend permissions for read-only controls and the feature flag only for the maintained projection', async () => {
    const { fixture: viewerFixture } = await render(
      kanbanView('ready'),
      scheduleView({ snapshot: mapProjectGanttSnapshot(viewerGanttSnapshotDto()) })
    );
    viewerFixture.componentInstance.tab.set('schedule');
    viewerFixture.componentInstance.schedulePresentation.set('narrow');
    viewerFixture.detectChanges();
    const viewerHost = viewerFixture.nativeElement as HTMLElement;
    expect(viewerHost.textContent).toContain('Schedule is read-only for the current actor.');
    expect(buttonLabels(viewerHost)).not.toContain('Edit dates');
    expect(buttonLabels(viewerHost)).not.toContain('Add FS predecessor');
    viewerFixture.destroy();
    TestBed.resetTestingModule();

    const { fixture: fallbackFixture } = await render(
      kanbanView('ready'),
      scheduleView({ canonicalEnabled: false })
    );
    fallbackFixture.componentInstance.tab.set('schedule');
    fallbackFixture.componentInstance.schedulePresentation.set('narrow');
    fallbackFixture.detectChanges();
    const fallbackText = (fallbackFixture.nativeElement as HTMLElement).textContent ?? '';
    expect(fallbackText).toContain('Canonical Gantt presentation is disabled');
    expect(fallbackText).toContain('Canonical schedule task');
    expect(fallbackText).toContain('read-only because the current API does not provide an authorized versioned schedule-write contract');
  });

  it('forwards canonical edit intents and opens the existing Task Detail route', async () => {
    const { fixture, facade } = await render(kanbanView('ready'));
    fixture.componentInstance.tab.set('schedule');
    fixture.componentInstance.schedulePresentation.set('narrow');
    fixture.detectChanges();
    const gantt = fixture.debugElement.query(By.css('coglatas-gantt')).componentInstance;
    const intent = {
      kind: 'progress' as const,
      taskId: 'task-1',
      progressPercent: 40,
      expectedVersion: 3,
      source: 'form' as const
    };

    gantt.editRequested.emit(intent);
    expect(facade.applyGanttEdit).toHaveBeenCalledWith(intent);

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    gantt.itemActivated.emit(scheduleView().snapshot!.scheduledItems[0]);
    expect(navigate).toHaveBeenCalledWith(['/projects', 'project-1', 'tasks', 'task-1']);

    gantt.itemActivated.emit(scheduleView().snapshot!.milestones[0]);
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('keeps preserved conflict intent actionable after authoritative refetch returns ready', async () => {
    const preservedIntent = {
      kind: 'progress' as const,
      taskId: 'task-1',
      progressPercent: 40,
      expectedVersion: 3,
      source: 'form' as const
    };
    const { fixture, facade } = await render(
      kanbanView('ready'),
      scheduleView({
        status: 'ready',
        feedback: 'Conflict reconciled from authoritative schedule data.',
        preservedIntent
      })
    );
    fixture.componentInstance.tab.set('schedule');
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const retry = [...host.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Retry preserved edit'));
    const discard = [...host.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Discard preserved edit'));

    expect(retry).toBeDefined();
    expect(discard).toBeDefined();
    retry!.click();
    discard!.click();
    expect(facade.retryPreservedScheduleIntent).toHaveBeenCalledOnce();
    expect(facade.clearPreservedScheduleIntent).toHaveBeenCalledOnce();
  });

  it('opens on Overview and renders only the canonical Draft explanation before activation', async () => {
    const { fixture, facade } = await render(
      { ...kanbanView('disabled'), snapshot: null },
      scheduleView({ status: 'empty', snapshot: null }),
      {
        project: draftProject(),
        activation: { status: 'idle', message: null }
      }
    );
    const host = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.tab()).toBe('overview');
    expect([...host.querySelectorAll('[role="tab"]')].map((tab) => tab.textContent?.trim())).toEqual(['Overview']);
    expect(host.textContent).toContain('saved as Draft');
    expect(host.textContent).toContain('sole Project Owner');
    expect(host.textContent).toContain('Project General and the Task workflow');
    expect(host.textContent).toContain('Members only');
    expect(host.textContent).not.toContain('project-1');

    host.querySelector<HTMLButtonElement>('[data-testid="activate-project"]')?.click();
    expect(facade.activate).toHaveBeenCalledOnce();
  });

  it('does not expose activation when the backend affordance is absent', async () => {
    const { fixture } = await render(
      { ...kanbanView('disabled'), snapshot: null },
      scheduleView({ status: 'empty', snapshot: null }),
      {
        project: { ...draftProject(), canActivate: false },
        activation: { status: 'idle', message: null }
      }
    );

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="activate-project"]')).toBeNull();
  });

  it('moves focus to the stable Overview announcement when activation removes the button', async () => {
    const rendered = await render(
      { ...kanbanView('disabled'), snapshot: null },
      scheduleView({ status: 'empty', snapshot: null }),
      {
        project: draftProject(),
        activation: { status: 'submitting', message: 'Activating Project…' }
      }
    );
    rendered.setView({
      project: {
        ...draftProject(),
        status: 'active',
        statusLabel: 'Running',
        activationState: 'activated',
        isOperational: true,
        canActivate: false
      },
      activation: { status: 'success', message: 'Project activated.' }
    });
    rendered.fixture.detectChanges();
    await rendered.fixture.whenStable();
    await Promise.resolve();

    const announcement = (rendered.fixture.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('.project-detail-page__activation-status');
    expect(announcement).toBe(document.activeElement);
    expect((rendered.fixture.nativeElement as HTMLElement).querySelector('[data-testid="activate-project"]')).toBeNull();
  });

  it('restores focus to activation completion after authorization temporarily removes the live region', async () => {
    const rendered = await render(
      { ...kanbanView('disabled'), snapshot: null },
      scheduleView({ status: 'empty', snapshot: null }),
      {
        project: draftProject(),
        activation: { status: 'submitting', message: 'Activating Project…' }
      }
    );

    rendered.setView({
      status: 'loading',
      project: undefined,
      activation: { status: 'idle', message: null }
    });
    rendered.fixture.detectChanges();
    expect((rendered.fixture.nativeElement as HTMLElement)
      .querySelector('.project-detail-page__activation-status')).toBeNull();

    rendered.setView({
      status: 'ready',
      project: {
        ...draftProject(),
        status: 'active',
        statusLabel: 'Running',
        activationState: 'activated',
        isOperational: true,
        canActivate: false
      },
      activation: {
        status: 'success',
        message: 'Project activated. Operational views were loaded from authoritative state.'
      }
    });
    rendered.fixture.detectChanges();
    await rendered.fixture.whenStable();
    await Promise.resolve();

    const announcement = (rendered.fixture.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('.project-detail-page__activation-status');
    expect(announcement).toBe(document.activeElement);
  });

  it('focuses the persistent page target when authorization clearing ends in denial', async () => {
    const rendered = await render(
      { ...kanbanView('disabled'), snapshot: null },
      scheduleView({ status: 'empty', snapshot: null }),
      {
        project: draftProject(),
        activation: { status: 'submitting', message: 'Activating Project…' }
      }
    );

    rendered.setView({
      status: 'permissionDenied',
      project: undefined,
      activation: { status: 'idle', message: null }
    });
    rendered.fixture.detectChanges();
    await rendered.fixture.whenStable();
    await Promise.resolve();

    const host = (rendered.fixture.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('[data-testid="project-detail-page"]');
    expect(host).toBe(document.activeElement);
    expect((rendered.fixture.nativeElement as HTMLElement)
      .querySelector('.project-detail-page__activation-status')).toBeNull();
  });

  it('moves focus to the persistent page target when activation authorization is denied', async () => {
    const rendered = await render(
      { ...kanbanView('disabled'), snapshot: null },
      scheduleView({ status: 'empty', snapshot: null }),
      {
        project: draftProject(),
        activation: { status: 'submitting', message: 'Activating Project…' }
      }
    );

    rendered.setView({
      status: 'permissionDenied',
      project: undefined,
      activation: {
        status: 'permissionDenied',
        message: 'Project activation is not available for the current session.'
      }
    });
    rendered.fixture.detectChanges();
    await rendered.fixture.whenStable();
    await Promise.resolve();

    const host = (rendered.fixture.nativeElement as HTMLElement)
      .querySelector<HTMLElement>('[data-testid="project-detail-page"]');
    expect(host).toBe(document.activeElement);
    expect((rendered.fixture.nativeElement as HTMLElement)
      .querySelector('.project-detail-page__activation-status')).toBeNull();
  });

  it('offers New Task only from an operational Project with server create authority', async () => {
    const rendered = await render(kanbanView('ready'));
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const host = rendered.fixture.nativeElement as HTMLElement;
    const create = host.querySelector<HTMLButtonElement>('[data-testid="project-create-task"]');

    expect(create).not.toBeNull();
    create?.click();
    expect(navigate).toHaveBeenCalledWith(['/projects', 'project-1', 'tasks', 'new']);

    rendered.setView({ project: { ...rendered.fixture.componentInstance.page().project!, canCreateTask: false } });
    rendered.fixture.detectChanges();
    expect(host.querySelector('[data-testid="project-create-task"]')).toBeNull();
  });

  it('reloads a reused detail page for each distinct route Project and resets local tabs', async () => {
    const rendered = await render(kanbanView('ready'));
    expect(rendered.facade.load).toHaveBeenCalledWith('project-1');

    rendered.fixture.componentInstance.tab.set('tasks');
    rendered.setProjectId('project-2');
    rendered.fixture.detectChanges();

    expect(rendered.facade.load).toHaveBeenNthCalledWith(2, 'project-2');
    expect(rendered.fixture.componentInstance.tab()).toBe('overview');

    rendered.setProjectId('project-2');
    expect(rendered.facade.load).toHaveBeenCalledTimes(2);
  });
});

async function render(
  kanban: ProjectKanbanViewModel,
  schedule: ProjectScheduleViewModel = scheduleView(),
  overrides: Partial<ProjectDetailViewModel> = {}
) {
  const view: ProjectDetailViewModel = {
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
      taskCounts: { total: 0, done: 0, blocked: 0 }
    },
    tasks: [],
    taskListFeedback: null,
    kanban,
    schedule,
    workload: [],
    members: [],
    activation: { status: 'idle', message: null },
    ...overrides
  };
  const viewState = signal(view);
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
    setIncludeOlderCompleted: vi.fn()
  };
  await TestBed.configureTestingModule({
    imports: [ProjectDetailPageComponent],
    providers: [
      provideRouter([]),
      { provide: ProjectDetailFacade, useValue: facade },
      {
        provide: ActivatedRoute,
        useValue: {
          paramMap: routeParams.asObservable(),
          snapshot: { paramMap: routeParams.value }
        }
      }
    ]
  }).compileComponents();
  const fixture = TestBed.createComponent(ProjectDetailPageComponent);
  fixture.detectChanges();
  return {
    fixture,
    facade,
    setView: (changes: Partial<ProjectDetailViewModel>) => viewState.set({ ...viewState(), ...changes }),
    setProjectId: (projectId: string) => routeParams.next(convertToParamMap({ projectId }))
  };
}

function draftProject(): NonNullable<ProjectDetailViewModel['project']> {
  return {
    id: 'project-1',
    workspaceId: 'workspace-1',
    groupId: null,
    ownerUserId: 'owner-1',
    name: 'Draft Project',
    description: 'A canonical Draft.',
    status: 'planning',
    statusLabel: 'Draft',
    visibility: 'membersOnly',
    visibilityLabel: 'Members only',
    activationState: 'neverActivated',
    versionNo: 3,
    isOperational: false,
    startDate: '',
    dueDate: '',
    group: 'No Group',
    canCreateTask: false,
    canActivate: true,
    taskCounts: { total: 0, done: 0, blocked: 0 }
  };
}

function kanbanView(status: ProjectKanbanViewModel['status']): ProjectKanbanViewModel {
  return {
    status,
    snapshot: mapProjectKanbanSnapshot(snapshotDto()),
    busyTaskId: null,
    focusTaskId: null,
    feedback: null,
    realtimeDegraded: false,
    reconciliationQueued: false
  };
}

function scheduleView(overrides: Partial<ProjectScheduleViewModel> = {}): ProjectScheduleViewModel {
  return {
    status: 'ready',
    snapshot: mapProjectGanttSnapshot(ganttSnapshotDto()),
    canonicalEnabled: true,
    busyItemId: null,
    focusItemId: null,
    feedback: null,
    preservedIntent: null,
    realtimeDegraded: false,
    reconciliationQueued: false,
    ...overrides
  };
}

function buttonLabels(host: HTMLElement): readonly string[] {
  return [...host.querySelectorAll('button')].map((button) => button.textContent?.trim() ?? '');
}
