import { I18nService } from '../../../core/i18n/i18n.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { signal } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';

import { TaskCreateOptions } from '../task-create.api';
import {
  EMPTY_TASK_CREATE_STATE,
  TaskCreateFacade,
  TaskCreateOptionsViewModel,
  TaskCreateViewModel,
} from '../task-create.facade';
import { TaskCreatePageComponent } from './task-create-page.component';

const projectId = '11111111-1111-4111-8111-111111111111';
const workspaceId = '22222222-2222-4222-8222-222222222222';
const milestoneId = '33333333-3333-4333-8333-333333333333';
const assigneeId = '44444444-4444-4444-8444-444444444444';

const options: TaskCreateOptions = {
  requestId: 'task-create-options-200',
  projectId,
  workspaceId,
  projectTitle: 'Evidence Project',
  canCreateTask: true,
  canManageProject: true,
  milestones: [{ id: milestoneId, title: 'Evidence milestone' }],
  assignees: [{ userId: assigneeId, displayName: 'Project member' }],
  projectScope: {
    policy: { webEnabled: false, projectFilesEnabled: true },
    version: 1,
    canSetTaskOverride: true,
  },
};

class TaskCreateFacadeStub {
  readonly options = signal<TaskCreateOptionsViewModel>({
    status: 'ready',
    projectId,
    data: options,
    requestId: options.requestId,
  });
  readonly createState = signal<TaskCreateViewModel>(EMPTY_TASK_CREATE_STATE);
  readonly load = vi.fn().mockResolvedValue(true);
  readonly release = vi.fn();
  readonly resetCreatePresentation = vi.fn();
  readonly createTask = vi.fn().mockResolvedValue(false);
  readonly retryOptions = vi.fn().mockResolvedValue(true);
  readonly retryCreatedTaskNavigation = vi.fn().mockResolvedValue(true);
}

beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));

describe('TaskCreatePageComponent', () => {
  let fixture: ComponentFixture<TaskCreatePageComponent>;
  let component: TaskCreatePageComponent;
  let facade: TaskCreateFacadeStub;
  let router: { navigate: ReturnType<typeof vi.fn> };
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(async () => {
    facade = new TaskCreateFacadeStub();
    router = { navigate: vi.fn().mockResolvedValue(true) };
    params = new BehaviorSubject(convertToParamMap({ projectId }));
    await TestBed.configureTestingModule({
      imports: [TaskCreatePageComponent],
      providers: [
        { provide: TaskCreateFacade, useValue: facade },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TaskCreatePageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  async function settleView(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function remountLoadingRoute(): Promise<void> {
    fixture.destroy();
    facade.options.set({ status: 'loading', projectId });
    fixture = TestBed.createComponent(TaskCreatePageComponent);
    component = fixture.componentInstance;
    await settleView();
    expect(component.titleInput()).toBeUndefined();
  }

  function readyOptions(): TaskCreateOptionsViewModel {
    return { status: 'ready', projectId, data: options, requestId: options.requestId };
  }

  it('switches Japanese to English without replacing a draft, selected authority, or issuing another load', async () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLocale('ja');
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const title = root.querySelector<HTMLInputElement>('#task-create-title')!;
    title.value = '未送信の task draft';
    title.dispatchEvent(new Event('input', { bubbles: true }));
    component.form.controls.goal.setValue('Keep this user-authored goal unchanged.');
    component.form.controls.priority.setValue('high');
    component.form.controls.milestoneId.setValue(milestoneId);
    component.form.controls.primaryAssigneeUserId.setValue(assigneeId);
    component.form.markAsDirty();
    fixture.detectChanges();
    expect(root.querySelector('h1')?.textContent).toContain('タスクを作成');
    expect(root.textContent).toContain('Evidence Project');
    expect(root.textContent).toContain('Evidence milestone');
    const values = component.form.getRawValue();
    const reads = facade.load.mock.calls.length;

    i18n.setLocale('en');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('h1')?.textContent).toContain('Create Task');
    expect(root.querySelector('#task-create-title')).toBe(title);
    expect(component.form.getRawValue()).toEqual(values);
    expect(component.form.dirty).toBe(true);
    expect(facade.load).toHaveBeenCalledTimes(reads);
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it.each(['assignee', 'both'] as const)('retranslates a removed %s choice notice without clearing another draft or reloading options', async (removed) => {
    component.i18n.setLocale('ja');
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const title = root.querySelector<HTMLInputElement>('#task-create-title')!;
    title.value = '未送信の task title';
    title.dispatchEvent(new Event('input', { bubbles: true }));
    component.form.controls.primaryAssigneeUserId.setValue(assigneeId);
    component.form.controls.milestoneId.setValue(milestoneId);
    facade.options.set({ status: 'ready', projectId, data: {
      ...options, assignees: [], milestones: removed === 'both' ? [] : options.milestones,
    } });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const notice = root.querySelector('[data-testid="task-create-options-changed"]')!;
    const values = component.form.getRawValue();
    const reads = facade.load.mock.calls.length;
    expect(notice.textContent).toContain(removed === 'both' ? '主担当者とマイルストーンは利用できなくなったためクリアされました。' : '主担当者は利用できなくなったためクリアされました。');
    expect(values.primaryAssigneeUserId).toBe('');
    expect(values.milestoneId).toBe(removed === 'both' ? '' : milestoneId);
    expect(root.querySelector('[data-testid="task-create-unsent-draft"]')?.textContent).toContain('このフォームはこのブラウザータブにのみ保存されています。');

    component.i18n.setLocale('en');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="task-create-options-changed"]')).toBe(notice);
    expect(notice.textContent).toContain(removed === 'both' ? 'Primary assignee and Milestone are no longer available and were cleared.' : 'Primary assignee is no longer available and was cleared.');
    expect(notice.textContent).toContain('Review the remaining named choices before creating the Task.');
    expect(root.querySelector('#task-create-title')).toBe(title);
    expect(component.form.getRawValue()).toEqual(values);
    expect(facade.load).toHaveBeenCalledTimes(reads);
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it('renders only server-authorized named choices and a Create-only source-scope form', () => {
    const root = fixture.nativeElement as HTMLElement;
    const text = root.textContent ?? '';

    expect(facade.load).toHaveBeenCalledWith(projectId);
    expect(text).toContain('Evidence Project');
    expect(text).toContain('Evidence milestone');
    expect(text).toContain('Project member');
    expect(text).toContain('does not start a runtime or retrieve sources');
    expect(root.textContent).not.toContain(projectId);
    expect(root.textContent).not.toContain(workspaceId);
    expect(root.querySelector('[name="projectId"], [name="workspaceId"], [name="webUrl"], [name="provider"]')).toBeNull();
    expect([...root.querySelectorAll('button')].map((button) => button.textContent?.trim())).not.toContain('Start');
    expect(root.querySelector<HTMLInputElement>('#task-create-sourceScopeMode-inherit')).not.toBeNull();
    expect(root.querySelector<HTMLInputElement>('#task-create-sourceScopeMode-override')).not.toBeNull();
    expect(root.querySelectorAll('#task-create-sourceScopeMode-inherit')).toHaveLength(1);
    expect(root.querySelectorAll('#task-create-sourceScopeMode-override')).toHaveLength(1);
  });

  it('keeps override controls server-manager-gated and makes an empty assignee authority explicit', async () => {
    facade.options.set({
      status: 'ready',
      projectId,
      data: {
        ...options,
        canManageProject: false,
        assignees: [],
        projectScope: { ...options.projectScope, canSetTaskOverride: false },
      },
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-create-source-override"]')).toBeNull();
    expect(root.querySelector('[data-testid="task-create-scope-controls"]')).toBeNull();
    expect(root.querySelector('[data-testid="task-create-primary-assignee"]')).toBeNull();
    expect(root.textContent).toContain('created unassigned');
    expect(root.textContent).toContain('Only a Project manager can choose a Task-specific policy.');
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain(
      'Project default policy: Web disabled; Project files enabled.',
    );
  });

  it('shows missing Brief items, retains a fail-closed inherited source policy, and moves focus to editable fields', async () => {
    facade.options.set({
      status: 'ready',
      projectId,
      data: {
        ...options,
        projectScope: {
          ...options.projectScope,
          policy: { webEnabled: false, projectFilesEnabled: false },
        },
      },
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const checklist = root.querySelector<HTMLElement>('[data-testid="task-create-quality-checklist"]');
    expect(checklist?.textContent).toContain('Advisory only: 1 of 4 items are covered.');
    expect(root.querySelectorAll('li[data-testid^="task-create-quality-"]')).toHaveLength(4);
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain(
      'Project default policy: Web disabled; Project files disabled.',
    );
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain('Covered');

    [...root.querySelectorAll<HTMLButtonElement>('[data-testid="task-create-quality-goal"] button')][0]?.click();
    expect(document.activeElement).toBe(root.querySelector('#task-create-goal'));
    [...root.querySelectorAll<HTMLButtonElement>('[data-testid="task-create-quality-deliverable"] button')][0]?.click();
    expect(document.activeElement).toBe(root.querySelector('#task-create-deliverable'));
    [...root.querySelectorAll<HTMLButtonElement>('[data-testid="task-create-quality-constraints"] button')][0]?.click();
    expect(document.activeElement).toBe(root.querySelector('#task-create-constraints'));
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"] button')).toBeNull();
  });

  it('reflects the effective inherited or Task-specific source policy in the advisory review', () => {
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain(
      'Project default policy: Web disabled; Project files enabled.',
    );
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain('Covered');

    component.setSourceScopeMode('TaskOverride');
    component.form.controls.webEnabled.setValue(false);
    component.form.controls.projectFilesEnabled.setValue(false);
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain(
      'Task-specific policy: Web disabled; Project files disabled.',
    );
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain('Covered');

    component.form.controls.webEnabled.setValue(true);
    component.form.controls.goal.setValue('Clarify the outcome');
    component.form.controls.deliverable.setValue('A reviewable result');
    component.form.controls.constraints.setValue('Stay within the approved policy');
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="task-create-quality-sourceScopeMode"]')?.textContent).toContain(
      'Task-specific policy: Web enabled; Project files disabled.',
    );
    expect(root.querySelector('[data-testid="task-create-quality-goal"]')?.textContent).toContain(
      'Goal is included in this Task Brief.',
    );
    expect(root.querySelector('[data-testid="task-create-quality-deliverable"]')?.textContent).toContain(
      'Deliverable is included in this Task Brief.',
    );
    expect(root.querySelector('[data-testid="task-create-quality-constraints"]')?.textContent).toContain(
      'Constraints are included in this Task Brief.',
    );
    expect(root.querySelector('[data-testid="task-create-quality-checklist"]')?.textContent).toContain(
      'Advisory only: 4 of 4 items are covered.',
    );
  });

  it('keeps the quality checklist advisory when optional information is missing', () => {
    component.form.controls.title.setValue('Task with an incomplete review');

    component.submit();

    expect(facade.createTask).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Task with an incomplete review',
        goal: '',
        deliverable: '',
        constraints: '',
        sourceScopeMode: 'Inherit',
      }),
    );
  });

  it('focuses linked Milestone, reusable Brief, and source-scope errors on real controls', async () => {
    facade.createState.set({
      status: 'error',
      fieldErrors: [
        { field: 'milestoneId', message: 'Choose an available Milestone.' },
        { field: 'goal', message: 'Goal is too long.' },
        { field: 'sourceScopeMode', message: 'Task override is not allowed.' },
      ],
      message: 'Check the Task details.',
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const summary = root.querySelector<HTMLElement>('[data-testid="task-create-error-summary"]');
    expect(summary).not.toBeNull();
    expect(summary?.querySelector<HTMLAnchorElement>('a[href="#task-create-milestone"]')).not.toBeNull();
    expect(summary?.querySelector<HTMLAnchorElement>('a[href="#task-create-goal"]')).not.toBeNull();
    expect(summary?.querySelector<HTMLAnchorElement>('a[href="#task-create-sourceScopeMode"]')).not.toBeNull();

    summary?.querySelector<HTMLAnchorElement>('a[href="#task-create-milestone"]')?.click();
    expect(document.activeElement).toBe(root.querySelector('#task-create-milestone'));
    summary?.querySelector<HTMLAnchorElement>('a[href="#task-create-goal"]')?.click();
    expect(document.activeElement).toBe(root.querySelector('#task-create-goal'));
    summary?.querySelector<HTMLAnchorElement>('a[href="#task-create-sourceScopeMode"]')?.click();
    expect(document.activeElement).toBe(root.querySelector('#task-create-sourceScopeMode-inherit'));
  });

  it('keeps a tab-local unsent draft until explicit discard and never submits on Cancel', () => {
    component.form.controls.title.setValue('Local evidence task');
    // Programmatic FormControl writes are not user edits, so model the dirty
    // state that an actual keyboard entry produces before checking the draft UI.
    component.form.controls.title.markAsDirty();
    component.form.markAsDirty();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-create-unsent-draft"]')?.textContent).toContain(
      'This form exists only in this browser tab.',
    );
    [...root.querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === 'Cancel')
      ?.click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="task-create-discard-confirmation"]')).not.toBeNull();
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it('clears a no-longer-authorized hidden assignee choice after an options refresh', async () => {
    component.form.controls.primaryAssigneeUserId.setValue(assigneeId);
    facade.options.set({
      status: 'ready',
      projectId,
      data: { ...options, assignees: [] },
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.form.controls.primaryAssigneeUserId.value).toBe('');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="task-create-options-changed"]')?.textContent,
    ).toContain('Primary assignee');
  });

  it('preserves the local draft and restores title focus after same-route authorization recovery', async () => {
    component.form.controls.title.setValue('Local Task recovery evidence');
    component.form.controls.goal.setValue('Keep a browser-local draft only.');
    fixture.detectChanges();

    facade.options.set({ status: 'idle' });
    fixture.detectChanges();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="task-create-form"]')).toBeNull();

    facade.options.set({
      status: 'ready',
      projectId,
      data: {
        ...options,
        canManageProject: false,
        assignees: [],
      },
      requestId: 'task-create-options-reauthorized',
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    const title = root.querySelector<HTMLInputElement>('#task-create-title');
    expect(component.form.controls.title.value).toBe('Local Task recovery evidence');
    expect(component.form.controls.goal.value).toBe('Keep a browser-local draft only.');
    expect(root.querySelector('[data-testid="task-create-primary-assignee"]')).toBeNull();
    expect(document.activeElement).toBe(title);
  });

  it('delivers initial title focus after a ready render is interrupted by loading', async () => {
    await remountLoadingRoute();
    facade.options.set(readyOptions());
    expect(component.titleInput()).toBeUndefined();
    fixture.detectChanges();
    const firstTitle = component.titleInput()?.nativeElement;
    const firstReadyHadFocus = firstTitle !== undefined && document.activeElement === firstTitle;
    facade.options.set({ status: 'loading', projectId });
    await settleView();
    expect(component.titleInput()).toBeUndefined();
    facade.options.set(readyOptions());
    await settleView();
    const recoveredTitle = component.titleInput()?.nativeElement;
    expect(recoveredTitle).toBeDefined();
    expect(document.activeElement === recoveredTitle,
      `title focus after interrupted initial render; first ready had focus=${firstReadyHadFocus}`).toBe(true);
  });

  it('restores title focus after an ordinary options refresh removes the focused title', async () => {
    const originalTitle = component.titleInput()!.nativeElement;
    expect(document.activeElement === originalTitle).toBe(true);
    component.form.controls.title.setValue('Unsent current-tab Task');
    facade.options.set({ status: 'loading', projectId });
    await settleView();
    expect(originalTitle.isConnected).toBe(false);
    expect(component.titleInput()).toBeUndefined();
    facade.options.set(readyOptions());
    await settleView();
    const recoveredTitle = component.titleInput()!.nativeElement;
    expect(recoveredTitle).not.toBe(originalTitle);
    expect(recoveredTitle.value).toBe('Unsent current-tab Task');
    expect(document.activeElement === recoveredTitle).toBe(true);
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it('does not steal user-selected navigation focus while initial options are pending', async () => {
    await remountLoadingRoute();
    const navigation = document.createElement('button');
    document.body.append(navigation);
    try {
      navigation.focus();
      expect(document.activeElement === navigation).toBe(true);
      facade.options.set(readyOptions());
      await settleView();
      expect(component.titleInput()).toBeDefined();
      expect(document.activeElement === navigation).toBe(true);
    } finally {
      navigation.remove();
    }
  });

  it('keeps description focus and draft during a ready-to-ready options update', async () => {
    const description = (fixture.nativeElement as HTMLElement).querySelector<HTMLTextAreaElement>('#task-create-description')!;
    component.form.controls.description.setValue('Unsent description');
    description.focus();
    facade.options.set({ ...readyOptions(), data: { ...options, assignees: [] } });
    await settleView();
    expect(document.activeElement === description).toBe(true);
    expect(description.value).toBe('Unsent description');
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it.each(['denied', 'error'] as const)('does not focus an obsolete title when initial loading ends in %s', async (status) => {
    await remountLoadingRoute();
    facade.options.set(readyOptions());
    fixture.detectChanges();
    const obsoleteTitle = component.titleInput()?.nativeElement;
    facade.options.set({ status, projectId });
    await settleView();
    expect(component.titleInput()).toBeUndefined();
    expect(obsoleteTitle?.isConnected ?? false).toBe(false);
    expect(document.activeElement === obsoleteTitle).toBe(false);
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it('does not restore the old title when a pending route is replaced by another Project', async () => {
    const obsoleteTitle = component.titleInput()!.nativeElement;
    const replacementProjectId = '77777777-7777-4777-8777-777777777777';
    facade.options.set({ status: 'loading', projectId: replacementProjectId });
    params.next(convertToParamMap({ projectId: replacementProjectId }));
    await settleView();
    facade.options.set({ status: 'denied', projectId: replacementProjectId });
    await settleView();
    expect(obsoleteTitle.isConnected).toBe(false);
    expect(component.titleInput()).toBeUndefined();
    expect(document.activeElement === obsoleteTitle).toBe(false);
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it('does not move navigation focus after the pending component is destroyed', async () => {
    await remountLoadingRoute();
    facade.options.set(readyOptions());
    fixture.detectChanges();
    fixture.destroy();
    const navigation = document.createElement('button');
    document.body.append(navigation);
    try {
      navigation.focus();
      await fixture.whenStable();
      expect(document.activeElement === navigation).toBe(true);
      expect(facade.createTask).not.toHaveBeenCalled();
    } finally {
      navigation.remove();
    }
  });

  it('does not duplicate a submit while the authoritative create state is pending', () => {
    component.form.controls.title.setValue('Evidence review');
    facade.createState.set({ status: 'submitting', fieldErrors: [] });
    fixture.detectChanges();

    component.submit();
    component.submit();
    expect(facade.createTask).not.toHaveBeenCalled();
  });

  it('abandons committed navigation recovery before returning to the Project', () => {
    facade.createState.set({
      status: 'committedPendingNavigation',
      fieldErrors: [],
      requestId: 'task-create-201',
      createdTaskId: '55555555-5555-4555-8555-555555555555',
    });
    fixture.detectChanges();

    component.returnToProject();

    expect(facade.release).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/projects', projectId]);
  });

  it('hides the editable form when protected options are cleared or denied', async () => {
    facade.options.set({
      status: 'denied',
      projectId,
      message: 'Task creation is not available for this Project.',
    });
    fixture.detectChanges();
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-create-form"]')).toBeNull();
    expect(root.textContent).toContain('Task creation is not available for this Project.');
  });
});
