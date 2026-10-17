import { I18nService } from '../../../core/i18n/i18n.service';
import { TestBed } from '@angular/core/testing';

import { TaskGridRow } from '../projects.types';
import { TaskTableComponent } from './task-table.component';

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
