import { I18nService } from '../../../core/i18n/i18n.service';
import { TestBed } from '@angular/core/testing';

import { TaskGridRow } from '../projects.types';
import { TaskStageCategoryCellComponent, TaskTableComponent } from './task-table.component';
import { taskStageWorkStatus } from '../projects.mapper';

beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));

describe('TaskTableComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [TaskTableComponent] });
  });

  it('renders stage, category, independent Blocked state, update time, and artifact state in semantic cards', () => {
    const fixture = TestBed.createComponent(TaskTableComponent);
    fixture.componentRef.setInput('rows', [row({
      workflowStageName: 'Editorial review',
      stageCategory: 'review',
      status: 'review',
      statusLabel: 'Review',
      isBlocked: true,
      createdAt: '2026-08-20T09:00:00Z',
      updatedAt: '2026-08-24T10:30:00Z',
      hasArtifact: true
    })]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-state-card-task-1-mobile"]')?.getAttribute('data-task-id')).toBe('task-1');
    expect(root.querySelector('[data-testid="task-stage-name-task-1-mobile"]')?.textContent).toContain('Editorial review');
    expect(root.querySelector('[data-testid="task-category-task-1-mobile"]')?.textContent).toContain('Needs review');
    expect(root.querySelector('[data-testid="task-blocked-task-1-mobile"]')?.textContent).toContain('Blocked');
    expect(root.querySelector('[data-testid="task-updated-task-1-mobile"] time')?.getAttribute('datetime')).toBe('2026-08-24T10:30:00Z');
    expect(root.querySelector('[data-testid="task-artifact-task-1-mobile"]')?.textContent).toContain('Artifact available');
  });

  it('localizes UI and instant dates without translating a configured Stage or changing source timestamps', () => {
    const fixture = TestBed.createComponent(TaskTableComponent);
    const i18n = TestBed.inject(I18nService);
    fixture.componentRef.setInput('rows', [row({ workflowStageName: 'Review', updatedAt: '2026-08-24T10:30:00Z' })]);
    i18n.setLocale('ja');
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-stage-name-task-1-mobile"]')?.textContent).toContain('Review');
    expect(root.textContent).toContain('最終更新');
    const time = root.querySelector<HTMLTimeElement>('[data-testid="task-updated-task-1-mobile"] time')!;
    const japanese = time.textContent;

    i18n.setLocale('en');
    fixture.detectChanges();

    expect(root.textContent).toContain('Last update');
    expect(root.querySelector('[data-testid="task-stage-name-task-1-mobile"]')?.textContent).toContain('Review');
    expect(time.dateTime).toBe('2026-08-24T10:30:00Z');
    expect(time.textContent).not.toBe(japanese);
  });

  it('falls back to CreatedAt and states negative Blocked and artifact values with text', () => {
    const fixture = TestBed.createComponent(TaskTableComponent);
    fixture.componentRef.setInput('rows', [row({
      createdAt: '2026-08-20T09:00:00Z',
      updatedAt: '',
      isBlocked: false,
      hasArtifact: false
    })]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-updated-task-1-mobile"] time')?.getAttribute('datetime')).toBe('2026-08-20T09:00:00Z');
    expect(root.querySelector('[data-testid="task-blocked-task-1-mobile"]')?.textContent).toContain('Not blocked');
    expect(root.querySelector('[data-testid="task-artifact-task-1-mobile"]')?.textContent).toContain('No artifact');
  });

  it('does not claim artifact absence when the source contract has no artifact projection', () => {
    const fixture = TestBed.createComponent(TaskTableComponent);
    fixture.componentRef.setInput('rows', [row({ hasArtifact: undefined })]);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="task-artifact-task-1-mobile"]')?.textContent)
      .toContain('Artifact state unavailable');
  });

  it('keeps every demo-critical state and the Open action ahead of secondary desktop columns', () => {
    const fixture = TestBed.createComponent(TaskTableComponent);

    expect(fixture.componentInstance.columns.slice(0, 7).map((column) => column.colId ?? column.field))
      .toEqual([
        'title',
        'workflowStageName',
        'stageCategory',
        'isBlocked',
        'updatedAt',
        'hasArtifact',
        'rowActions'
      ]);
  });

  it('keeps unsafe desktop titles as plain text and distinguishes missing data from denied actions', () => {
    const instance = TestBed.createComponent(TaskTableComponent).componentInstance;
    const render = (columnId: string, data?: TaskGridRow): HTMLElement | string => {
      const column = instance.columns.find(candidate => (candidate.colId ?? candidate.field) === columnId);
      expect(column?.cellRenderer).toEqual(expect.any(Function));
      return (column!.cellRenderer as (params: { data?: TaskGridRow }) => HTMLElement | string)({ data });
    };
    const textOnlyTitle = '<img src=x onerror=alert(1)>';
    const deniedRow = row({
      title: textOnlyTitle,
      status: 'blocked',
      stageCategory: undefined,
      workflowStageName: '',
      statusLabel: 'Fallback stage',
      isBlocked: undefined,
      updatedAt: '',
      createdAt: '',
      hasArtifact: undefined,
      rowActions: [
        { id: 'openDetail', label: 'Open', disabled: false },
        { id: 'edit', label: 'Edit task', disabled: true, disabledReason: 'Permission denied' }
      ]
    });

    const title = render('title', deniedRow) as HTMLElement;
    expect(title.textContent).toBe(textOnlyTitle);
    expect(title.querySelector('img')).toBeNull();
    expect(title.dataset['taskId']).toBe('task-1');
    expect(title.dataset['testid']).toBe('task-state-row-task-1-desktop');
    expect((render('workflowStageName', deniedRow) as HTMLElement).textContent).toBe('Fallback stage');
    const blocked = render('isBlocked', deniedRow) as HTMLElement;
    expect(blocked.textContent).toBe('Blocked');
    expect(blocked.dataset['blocked']).toBe('true');
    expect((render('updatedAt', deniedRow) as HTMLElement).textContent).toBe('Not available');

    const artifact = render('hasArtifact', deniedRow) as HTMLElement;
    expect(artifact.textContent).toBe('Artifact state unavailable');
    expect(artifact.dataset['hasArtifact']).toBe('unknown');
    const actions = render('rowActions', deniedRow) as HTMLElement;
    const edit = actions.querySelector<HTMLButtonElement>('[data-grid-action="edit"]');
    expect(edit?.disabled).toBe(true);
    expect(edit?.getAttribute('aria-disabled')).toBe('true');
    expect(edit?.title).toBe('Permission denied');
    expect(actions.querySelector<HTMLButtonElement>('[data-grid-action="openDetail"]')?.disabled).toBe(false);

    for (const id of ['title', 'workflowStageName', 'isBlocked', 'updatedAt', 'hasArtifact', 'rowActions']) {
      expect(render(id)).toBe('', 'No desktop cell may invent a row without authority: ' + id);
    }
  });

  it('renders source-derived desktop timestamps, flags and missing optional state exactly', () => {
    const instance = TestBed.createComponent(TaskTableComponent).componentInstance;
    const render = (colId: string, data: TaskGridRow): HTMLElement => {
      const col = instance.columns.find(candidate => (candidate.colId ?? candidate.field) === colId);
      return (col!.cellRenderer as (params: { data?: TaskGridRow }) => HTMLElement)({ data });
    };
    const complete = row({
      workflowStageName: 'Editorial review',
      status: 'review',
      stageCategory: 'review',
      isBlocked: false,
      hasArtifact: true,
      updatedAt: '2026-08-24T10:30:00Z'
    });
    expect(render('workflowStageName', complete).textContent).toBe('Editorial review');
    const blocked = render('isBlocked', complete);
    expect(blocked.textContent).toBe('Not blocked');
    expect(blocked.dataset['blocked']).toBe('false');
    const updated = render('updatedAt', complete);
    expect(updated.querySelector('time')?.getAttribute('datetime')).toBe('2026-08-24T10:30:00Z');
    expect(updated.querySelector('time')?.textContent).not.toBe('');
    expect(render('hasArtifact', complete).dataset['hasArtifact']).toBe('true');
    expect(render('hasArtifact', complete).textContent).toBe('Artifact available');
    expect(render('hasArtifact', row({ hasArtifact: false })).textContent).toBe('No artifact');
    expect(render('hasArtifact', row({ hasArtifact: false })).dataset['hasArtifact']).toBe('false');

    const invalidTime = render('updatedAt', row({ updatedAt: 'not-a-timestamp', createdAt: '' }));
    expect(invalidTime.querySelector('time')?.textContent).toBe('not-a-timestamp');
    expect(instance.formatTimestamp('not-a-timestamp')).toBe('not-a-timestamp');
  });

  it('exposes neutral desktop value formatters for absent or malformed row values', () => {
    const instance = TestBed.createComponent(TaskTableComponent).componentInstance;
    const get = (name: string) => {
      const col = instance.columns.find(candidate => (candidate.colId ?? candidate.field) === name);
      expect(col).toBeDefined();
      return col!;
    };
    expect(get('workflowStageName').valueGetter?.({})).toBe('');
    expect(get('workflowStageName').valueGetter?.({ data: row({ workflowStageName: '', statusLabel: 'Fallback' }) }))
      .toBe('Fallback');
    expect(get('stageCategory').valueGetter?.({})).toBe('');
    expect(get('isBlocked').valueGetter?.({})).toBe(false);
    expect(get('hasArtifact').valueGetter?.({})).toBeNull();
    expect(get('updatedAt').valueGetter?.({})).toBe('');
    expect(get('priorityLabel').valueGetter?.({})).toBe('');
    expect(get('priorityLabel').valueGetter?.({ data: row({ priority: 'high' }) })).toBe(instance.priorityLabel('high'));
    expect(get('startDate').valueFormatter?.({ value: 'invalid-date' })).toBe('invalid-date');
    expect(get('dueDate').valueFormatter?.({ value: null })).toBe('');
    expect(get('progressPercent').valueFormatter?.({ value: null })).toBe('Not shown');
    expect(get('progressPercent').valueFormatter?.({ value: undefined })).toBe('Not shown');
    expect(get('progressPercent').valueFormatter?.({ value: 0 })).toBe('0%');
  });

  it('refreshes the desktop category cell without confusing workflow state with blocked status', () => {
    const cell = new TaskStageCategoryCellComponent();
    expect(cell.workStatus).toBe(taskStageWorkStatus('todo'));
    cell.agInit({ data: row({ status: 'review', stageCategory: 'review' }) });
    expect(cell.workStatus).toBe(taskStageWorkStatus('review'));
    expect(cell.refresh({ data: row({ status: 'done', stageCategory: undefined }) })).toBe(true);
    expect(cell.workStatus).toBe(taskStageWorkStatus('done'));
    expect(cell.refresh({ data: row({ status: 'blocked', stageCategory: undefined }) })).toBe(true);
    expect(cell.workStatus).toBe(taskStageWorkStatus('todo'));
    expect(cell.refresh({})).toBe(true);
    expect(cell.row).toBeNull();
    expect(cell.workStatus).toBe(taskStageWorkStatus('todo'));
  });

  it('never emits disabled mobile card actions, including a synthetic direct invocation', () => {
    const fixture = TestBed.createComponent(TaskTableComponent);
    const disabled = { id: 'edit' as const, label: 'Edit', disabled: true, disabledReason: 'Permission denied' };
    const task = row({ rowActions: [disabled] });
    fixture.componentRef.setInput('rows', [task]);
    const emitted = vi.fn();
    fixture.componentInstance.actionInvoked.subscribe(emitted);
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="task-edit-task-1-mobile"]');
    expect(button?.disabled).toBe(true);
    expect(button?.title).toBe('Permission denied');
    button?.click();
    fixture.componentInstance.invokeCardAction(task, disabled, new Event('click'));
    expect(emitted).not.toHaveBeenCalled();
    fixture.componentInstance.invokeCardAction(task, { id: 'openDetail', label: 'Open', disabled: false }, new Event('click'));
    expect(emitted).toHaveBeenCalledWith(expect.objectContaining({ actionId: 'openDetail', row: task }));
  });

  it('emits the same row action contract from the responsive card', () => {
    const fixture = TestBed.createComponent(TaskTableComponent);
    fixture.componentRef.setInput('rows', [row()]);
    const emitted = vi.fn();
    fixture.componentInstance.actionInvoked.subscribe(emitted);
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="task-openDetail-task-1-mobile"]'
    );
    button?.click();

    expect(emitted).toHaveBeenCalledWith(expect.objectContaining({
      actionId: 'openDetail',
      row: expect.objectContaining({ id: 'task-1' }),
      trigger: button
    }));
  });
});

function row(overrides: Partial<TaskGridRow> = {}): TaskGridRow {
  return {
    id: 'task-1',
    projectId: 'project-1',
    title: 'Task one',
    project: 'Project',
    status: 'inProgress',
    statusLabel: 'In progress',
    workflowStageId: 'stage-progress',
    workflowStageName: 'Doing',
    stageCategory: 'inProgress',
    isBlocked: false,
    createdAt: '2026-08-20T09:00:00Z',
    updatedAt: '2026-08-21T09:00:00Z',
    hasArtifact: false,
    rowVersion: '3',
    priority: 'medium',
    priorityLabel: 'Medium',
    assignee: 'Member',
    startDate: '2026-08-20',
    dueDate: '2026-08-31',
    progressPercent: 50,
    milestone: '',
    allowedTransitions: [],
    rowActions: [{ id: 'openDetail', label: 'Open', disabled: false }],
    ...overrides
  };
}
