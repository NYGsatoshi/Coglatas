import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import {
  CoglatasGanttContract,
  CoglatasGanttEditIntent,
  CoglatasGanttItem,
  CoglatasKanbanContract,
  CoglatasKanbanMoveRequest
} from '../../contracts/coglatas-complex-adapter.contracts';
import { CoglatasDataGridComponent, CoglatasGanttComponent, CoglatasKanbanComponent } from './coglatas-adapter-shells.components';

@Component({
  standalone: true,
  imports: [CoglatasDataGridComponent],
  template: '<coglatas-data-grid [contract]="contract" presentation="narrow" state="degraded" />'
})
class AdapterShellHostComponent {
  readonly contract = {
    ariaLabel: 'Members',
    columns: [],
    page: 1,
    pageSize: 25,
    presentation: 'desktop' as const,
    rowIdentity: (row: object) => JSON.stringify(row),
    rows: [],
    state: 'ready' as const
  };
}

describe('Coglatas complex adapter shells', () => {
  it('renders stable Coglatas selectors and consumes theme/density context without vendor DOM', async () => {
    document.documentElement.dataset['coglatasTheme'] = 'light';
    document.documentElement.dataset['coglatasDensity'] = 'comfortable';
    await TestBed.configureTestingModule({ imports: [AdapterShellHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(AdapterShellHostComponent);
    fixture.detectChanges();

    const shell = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-testid="coglatas-data-grid-adapter"]');
    expect(shell?.dataset['coglatasPresentation']).toBe('narrow');
    expect(shell?.dataset['coglatasState']).toBe('degraded');
    expect(shell?.getAttribute('aria-label')).toBe('Members');
    expect(shell?.querySelector('ejs-grid')).toBeNull();
  });

  it('uses the same vendor-neutral move intent for keyboard ordering and keeps detail activation separate', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    fixture.componentInstance.contract = kanbanContract();
    let move: CoglatasKanbanMoveRequest<object> | undefined;
    let opened: object | undefined;
    fixture.componentInstance.moveRequested.subscribe((value) => move = value);
    fixture.componentInstance.itemActivated.subscribe((value) => opened = value);
    fixture.detectChanges();

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'));
    buttons.find((button) => button.textContent?.includes('Open details'))!.click();
    buttons.find((button) => button.textContent?.trim() === 'Move')!.click();
    fixture.detectChanges();
    const selects = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLSelectElement>('.coglatas-kanban__move select');
    selects[0].value = 'stage-done';
    selects[0].dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLFormElement>('.coglatas-kanban__move')!
      .dispatchEvent(new Event('submit'));

    expect(opened).toMatchObject({ id: 'task-1' });
    expect(move).toMatchObject({
      targetStatus: 'stage-done',
      targetBeforeItemId: null,
      targetAfterItemId: null,
      source: 'keyboard'
    });
  });

  it('opens the reason-required move form for a Cancelled drop and emits the preserved drag intent only after a reason is entered', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    const moving = { id: 'task-1', stage: 'stage-todo', order: 1000, canMove: true };
    const cancelledNeighbor = { id: 'task-cancelled', stage: 'stage-cancelled', order: 2000, canMove: true };
    fixture.componentInstance.contract = { ...kanbanContract(), items: [moving, cancelledNeighbor] };
    const moves: CoglatasKanbanMoveRequest<object>[] = [];
    const interactionStates: boolean[] = [];
    fixture.componentInstance.moveRequested.subscribe((value) => moves.push(value));
    fixture.componentInstance.interactionActiveChange.subscribe((value) => interactionStates.push(value));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLElement>('[data-kanban-card-id="task-1"]')!
      .dispatchEvent(new Event('dragstart', { bubbles: true }));
    host.querySelector<HTMLElement>('[data-kanban-card-id="task-cancelled"]')!
      .dispatchEvent(dropEvent());
    fixture.detectChanges();

    const form = host.querySelector<HTMLFormElement>('.coglatas-kanban__move')!;
    const selects = form.querySelectorAll<HTMLSelectElement>('select');
    const reason = form.querySelector<HTMLTextAreaElement>('textarea')!;
    const apply = Array.from(form.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.trim() === 'Apply move')!;
    expect(moves).toEqual([]);
    expect(fixture.componentInstance.movingItemId).toBe('task-1');
    expect(selects[0].value).toBe('stage-cancelled');
    expect(selects[1].value).toBe('before:task-cancelled');
    expect(reason.required).toBe(true);
    expect(reason.value).toBe('');
    expect(apply.disabled).toBe(true);
    expect(interactionStates.at(-1)).toBe(true);

    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    expect(moves).toEqual([]);
    expect(fixture.componentInstance.movingItemId).toBe('task-1');
    expect(interactionStates.at(-1)).toBe(true);

    reason.value = '  Superseded by the approved approach.  ';
    reason.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(apply.disabled).toBe(false);
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    expect(moves).toHaveLength(1);
    expect(moves[0]).toMatchObject({
      item: moving,
      targetStatus: 'stage-cancelled',
      targetBeforeItemId: 'task-cancelled',
      targetAfterItemId: null,
      reason: 'Superseded by the approved approach.',
      source: 'drag'
    });
    expect(interactionStates.at(-1)).toBe(false);
  });

  it('keeps a reason-required drag active until Escape or Cancel and restores focus without emitting a command', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    fixture.componentInstance.contract = kanbanContract();
    const moves: CoglatasKanbanMoveRequest<object>[] = [];
    const interactionStates: boolean[] = [];
    fixture.componentInstance.moveRequested.subscribe((value) => moves.push(value));
    fixture.componentInstance.interactionActiveChange.subscribe((value) => interactionStates.push(value));
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const card = host.querySelector<HTMLElement>('[data-kanban-card-id="task-1"]')!;
    const cancelledColumn = Array.from(host.querySelectorAll<HTMLElement>('.coglatas-kanban__column'))
      .find((column) => column.querySelector('h3')?.textContent?.trim() === 'Cancelled')!;
    card.dispatchEvent(new Event('dragstart', { bubbles: true }));
    cancelledColumn.dispatchEvent(dropEvent());
    card.dispatchEvent(new Event('dragend', { bubbles: true }));
    fixture.detectChanges();
    expect(interactionStates.at(-1)).toBe(true);

    host.querySelector<HTMLFormElement>('.coglatas-kanban__move')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    await Promise.resolve();

    expect(moves).toEqual([]);
    expect(host.querySelector('.coglatas-kanban__move')).toBeNull();
    expect(document.activeElement).toBe(card);
    expect(interactionStates.at(-1)).toBe(false);

    card.dispatchEvent(new Event('dragstart', { bubbles: true }));
    cancelledColumn.dispatchEvent(dropEvent());
    fixture.detectChanges();
    Array.from(host.querySelectorAll<HTMLButtonElement>('.coglatas-kanban__move button'))
      .find((button) => button.textContent?.trim() === 'Cancel')!.click();
    fixture.detectChanges();
    await Promise.resolve();

    expect(moves).toEqual([]);
    expect(host.querySelector('.coglatas-kanban__move')).toBeNull();
    expect(document.activeElement).toBe(card);
    expect(interactionStates.at(-1)).toBe(false);
  });

  it('emits a reason-free pointer drop directly with the canonical end-of-Stage intent', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    fixture.componentInstance.contract = kanbanContract();
    let move: CoglatasKanbanMoveRequest<object> | undefined;
    fixture.componentInstance.moveRequested.subscribe((value) => move = value);
    fixture.detectChanges();

    const moving = fixture.componentInstance.contract.items[0];
    const host = fixture.nativeElement as HTMLElement;
    const card = host.querySelector<HTMLElement>('[data-kanban-card-id="task-1"]')!;
    const doneColumn = Array.from(host.querySelectorAll<HTMLElement>('.coglatas-kanban__column'))
      .find((column) => column.querySelector('h3')?.textContent?.trim() === 'Done')!;
    card.dispatchEvent(new Event('dragstart', { bubbles: true }));
    doneColumn.dispatchEvent(dropEvent());
    fixture.detectChanges();

    expect(move).toMatchObject({
      item: moving,
      targetStatus: 'stage-done',
      targetBeforeItemId: null,
      targetAfterItemId: null,
      reason: null,
      source: 'drag'
    });
    expect((fixture.nativeElement as HTMLElement).querySelector('.coglatas-kanban__move')).toBeNull();
  });

  it('restores logical card focus and renders narrow grouped columns without exposing a vendor element', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    fixture.componentInstance.presentation = 'narrow';
    fixture.componentInstance.contract = { ...kanbanContract(), focusItemId: 'task-1' };
    fixture.detectChanges();
    await Promise.resolve();

    const board = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-testid="coglatas-kanban-board"]');
    const card = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-kanban-card-id="task-1"]');
    expect(board?.classList.contains('coglatas-kanban--narrow')).toBe(true);
    expect(board?.querySelector('.coglatas-kanban__swimlane')?.textContent).toContain('Unassigned');
    expect(card?.tabIndex).toBe(0);
    expect(document.activeElement).toBe(card);
    expect(board?.querySelector('ejs-kanban')).toBeNull();
  });

  it('reapplies logical card focus when an authoritative snapshot replaces the focused items', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    const initial = kanbanContract();
    fixture.componentRef.setInput('contract', { ...initial, focusItemId: 'task-1' });
    fixture.detectChanges();
    await Promise.resolve();

    const host = fixture.nativeElement as HTMLElement;
    const initialCard = host.querySelector<HTMLElement>('[data-kanban-card-id="task-1"]')!;
    expect(document.activeElement).toBe(initialCard);
    const focus = vi.spyOn(initialCard, 'focus');

    fixture.componentRef.setInput('contract', { ...initial, feedback: 'Same snapshot.', focusItemId: 'task-1' });
    fixture.detectChanges();
    await Promise.resolve();
    expect(focus).not.toHaveBeenCalled();

    fixture.componentRef.setInput('contract', {
      ...initial,
      items: initial.items.map((item) => ({ ...item, order: 2000 })),
      focusItemId: 'task-1'
    });
    fixture.detectChanges();
    await Promise.resolve();
    expect(focus).toHaveBeenCalledTimes(1);
    expect(document.activeElement).toBe(initialCard);

    const unrelatedControl = document.createElement('button');
    document.body.append(unrelatedControl);
    unrelatedControl.focus();
    expect(document.activeElement).toBe(unrelatedControl);
    focus.mockClear();

    fixture.componentRef.setInput('contract', {
      ...initial,
      items: initial.items.map((item) => ({ ...item, order: 3000 })),
      focusItemId: 'task-1'
    });
    fixture.detectChanges();
    await Promise.resolve();

    expect(focus).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(unrelatedControl);
    unrelatedControl.remove();
  });

  it('cancels the keyboard move with Escape, restores focus, and hides denied move actions', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    fixture.componentInstance.contract = kanbanContract();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    Array.from(host.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.trim() === 'Move')!.click();
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-kanban__move')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await Promise.resolve();

    const card = host.querySelector<HTMLElement>('[data-kanban-card-id="task-1"]');
    expect(host.querySelector('.coglatas-kanban__move')).toBeNull();
    expect(document.activeElement).toBe(card);

    fixture.componentRef.setInput('contract', { ...kanbanContract(), canMoveItem: () => false });
    fixture.detectChanges();
    expect(Array.from(host.querySelectorAll('button')).some((button) => button.textContent?.trim() === 'Move')).toBe(false);
  });

  it('uses canonical Stage rank for start and before while End stays null/null for a truncated presentation', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasKanbanComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasKanbanComponent);
    const moving = { id: 'task-1', stage: 'stage-todo', order: 1000, lane: 'Moving', canMove: true };
    const rankFirst = { id: 'task-2', stage: 'stage-done', order: 1000, lane: 'Zulu', canMove: true };
    const rankSecond = { id: 'task-3', stage: 'stage-done', order: 2000, lane: 'Alpha', canMove: true };
    const base = kanbanContract();
    fixture.componentInstance.contract = {
      ...base,
      items: [moving, rankFirst, rankSecond],
      columns: base.columns.map((column) =>
        column.id === 'stage-done' ? { ...column, cardCount: 500 } : column),
      itemIdentity: (item) => String((item as typeof moving).id),
      itemStatus: (item) => String((item as typeof moving).stage),
      itemOrder: (item) => Number((item as typeof moving).order),
      itemSwimlane: (item) => {
        const lane = String((item as typeof moving).lane);
        return { key: lane, label: lane };
      }
    };
    const moves: CoglatasKanbanMoveRequest<object>[] = [];
    fixture.componentInstance.moveRequested.subscribe((value) => moves.push(value));
    fixture.detectChanges();

    fixture.componentInstance.openMove(moving);
    fixture.componentInstance.changeTarget('stage-done');
    fixture.componentInstance.movePosition = 'end';
    fixture.componentInstance.submitMove(moving, submitEvent());

    fixture.componentInstance.openMove(moving);
    fixture.componentInstance.changeTarget('stage-done');
    fixture.componentInstance.movePosition = 'start';
    fixture.componentInstance.submitMove(moving, submitEvent());

    fixture.componentInstance.openMove(moving);
    fixture.componentInstance.changeTarget('stage-done');
    fixture.componentInstance.movePosition = 'before:task-3';
    fixture.componentInstance.submitMove(moving, submitEvent());

    expect(moves).toHaveLength(3);
    expect(moves[0]).toMatchObject({
      targetBeforeItemId: null,
      targetAfterItemId: null,
      source: 'keyboard'
    });
    expect(moves[1]).toMatchObject({
      targetBeforeItemId: 'task-2',
      targetAfterItemId: null,
      source: 'keyboard'
    });
    expect(moves[2]).toMatchObject({
      targetBeforeItemId: 'task-3',
      targetAfterItemId: null,
      source: 'keyboard'
    });
  });

  it('renders the canonical narrow Schedule as semantic ordered sections with textual state and opens details', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    fixture.componentInstance.contract = ganttContract();
    let opened: CoglatasGanttItem | undefined;
    fixture.componentInstance.itemActivated.subscribe((item) => opened = item);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const leaf = host.querySelector<HTMLElement>('[data-gantt-item-id="task-leaf"]')!;
    const text = host.textContent ?? '';
    expect(text).toContain('Scheduled work');
    expect(text).toContain('Milestones');
    expect(text).toContain('Unscheduled work');
    expect(text).toContain('Dependencies');
    expect(text).toContain('Schedule warnings');
    expect(leaf.textContent).toContain('Priority');
    expect(leaf.textContent).toContain('High');
    expect(leaf.textContent).toContain('Blocked');
    expect(leaf.textContent).toContain('In progress');
    expect(leaf.textContent).toContain('DEPENDENCY_VIOLATION');
    expect(itemElement(host, 'milestone-1').textContent).not.toContain('Open details');
    expect(host.querySelector('coglatas-syncfusion-gantt')).toBeNull();
    findButton(leaf, 'Open details').click();
    expect(opened?.taskId).toBe('task-leaf');
  });

  it('routes schedule, clear, progress, Milestone, and FS dependency forms through canonical intents', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    fixture.componentInstance.contract = ganttContract();
    const edits: CoglatasGanttEditIntent[] = [];
    fixture.componentInstance.editRequested.subscribe((intent) => edits.push(intent));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    findButton(itemElement(host, 'task-leaf'), 'Edit dates').click();
    fixture.detectChanges();
    setValue(host.querySelector<HTMLInputElement>('input[name="plannedStartDate"]')!, '2026-08-03');
    setValue(host.querySelector<HTMLInputElement>('input[name="plannedEndDate"]')!, '2026-08-07');
    await fixture.whenStable();
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());
    fixture.detectChanges();

    findButton(itemElement(host, 'task-leaf'), 'Move to unscheduled').click();
    fixture.detectChanges();
    findButton(host.querySelector<HTMLElement>('.coglatas-gantt__dialog')!, 'Clear schedule').click();
    fixture.detectChanges();

    findButton(itemElement(host, 'task-leaf'), 'Edit progress').click();
    fixture.detectChanges();
    setValue(host.querySelector<HTMLInputElement>('input[name="progressPercent"]')!, '72');
    await fixture.whenStable();
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());
    fixture.detectChanges();

    findButton(itemElement(host, 'milestone-1'), 'Edit Milestone date').click();
    fixture.detectChanges();
    setValue(host.querySelector<HTMLInputElement>('input[name="milestoneDate"]')!, '2026-08-15');
    await fixture.whenStable();
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());
    fixture.detectChanges();

    findButton(itemElement(host, 'task-leaf'), 'Add FS predecessor').click();
    fixture.detectChanges();
    const predecessor = host.querySelector<HTMLSelectElement>('select[name="predecessorTaskId"]')!;
    expect(Array.from(predecessor.options).map((option) => option.value)).not.toContain('milestone-1');
    setValue(predecessor, 'task-unscheduled');
    await fixture.whenStable();
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());
    fixture.detectChanges();

    findButton(host.querySelector<HTMLElement>('[data-gantt-dependency-id="dependency-1"]')!, 'Remove FS dependency').click();
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());

    expect(edits).toEqual([
      {
        kind: 'schedule',
        taskId: 'task-leaf',
        plannedStartDate: '2026-08-03',
        plannedEndDate: '2026-08-07',
        milestoneDate: null,
        expectedVersion: 8,
        source: 'form'
      },
      {
        kind: 'schedule',
        taskId: 'task-leaf',
        plannedStartDate: null,
        plannedEndDate: null,
        milestoneDate: null,
        expectedVersion: 8,
        source: 'form'
      },
      {
        kind: 'progress',
        taskId: 'task-leaf',
        progressPercent: 72,
        expectedVersion: 8,
        source: 'form'
      },
      {
        kind: 'schedule',
        taskId: 'milestone-1',
        plannedStartDate: null,
        plannedEndDate: null,
        milestoneDate: '2026-08-15',
        expectedVersion: 3,
        source: 'form'
      },
      {
        kind: 'addDependency',
        predecessorTaskId: 'task-unscheduled',
        successorTaskId: 'task-leaf',
        type: 'finishToStart',
        expectedVersion: 8,
        source: 'form'
      },
      {
        kind: 'removeDependency',
        dependencyId: 'dependency-1',
        successorTaskId: 'task-leaf',
        expectedVersion: 4,
        source: 'form'
      }
    ]);
  });

  it('traps the editor workflow, sends no command on Escape or Cancel, and restores logical focus', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    fixture.componentInstance.contract = ganttContract();
    const edits: CoglatasGanttEditIntent[] = [];
    fixture.componentInstance.editRequested.subscribe((intent) => edits.push(intent));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const trigger = findButton(itemElement(host, 'task-leaf'), 'Edit dates');

    trigger.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.hasAttribute('aria-modal')).toBe(true);
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    await Promise.resolve();

    expect(edits).toEqual([]);
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);

    trigger.click();
    fixture.detectChanges();
    findButton(host.querySelector<HTMLElement>('[role="dialog"]')!, 'Cancel').click();
    fixture.detectChanges();
    await Promise.resolve();

    expect(edits).toEqual([]);
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('hides every mutating action for a read-only viewer while retaining semantic data and Open details', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    const contract = ganttContract();
    fixture.componentInstance.contract = { ...contract, readOnly: true };
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const labels = Array.from(host.querySelectorAll<HTMLButtonElement>('button'))
      .map((button) => button.textContent?.trim());
    expect(labels).toContain('Open details');
    expect(labels.some((label) => label?.startsWith('Edit'))).toBe(false);
    expect(labels).not.toContain('Move to unscheduled');
    expect(labels).not.toContain('Add FS predecessor');
    expect(labels).not.toContain('Remove FS dependency');
    expect(host.textContent).toContain('Schedule is read-only for the current actor.');
  });

  it('does not convert schedule edit permission into schedule-clear permission', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    const contract = ganttContract();
    const scheduledItems = contract.scheduledItems!.map((item) =>
      item.taskId === 'task-leaf'
        ? {
            ...item,
            scheduleEditPermissions: {
              ...item.scheduleEditPermissions,
              canClearSchedule: false
            }
          }
        : item);
    fixture.componentInstance.contract = { ...contract, scheduledItems };
    const edits: CoglatasGanttEditIntent[] = [];
    fixture.componentInstance.editRequested.subscribe((intent) => edits.push(intent));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    const leaf = itemElement(host, 'task-leaf');
    expect(Array.from(leaf.querySelectorAll('button')).some((button) =>
      button.textContent?.trim() === 'Move to unscheduled')).toBe(false);
    findButton(leaf, 'Edit dates').click();
    fixture.detectChanges();
    setValue(host.querySelector<HTMLInputElement>('input[name="plannedStartDate"]')!, '');
    setValue(host.querySelector<HTMLInputElement>('input[name="plannedEndDate"]')!, '');
    await fixture.whenStable();
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());
    fixture.detectChanges();

    expect(edits).toEqual([]);
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('permission to clear');
    expect(host.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('allows an authorized derived parent to author an FS dependency without exposing derived date or progress edits', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    const contract = ganttContract();
    const scheduledItems = contract.scheduledItems!.map((item) =>
      item.taskId === 'task-parent'
        ? {
            ...item,
            scheduleEditPermissions: {
              ...item.scheduleEditPermissions,
              canManageDependencies: true
            }
          }
        : item);
    fixture.componentInstance.contract = { ...contract, scheduledItems };
    const edits: CoglatasGanttEditIntent[] = [];
    fixture.componentInstance.editRequested.subscribe((intent) => edits.push(intent));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const parent = itemElement(host, 'task-parent');

    expect(Array.from(parent.querySelectorAll('button')).map((button) => button.textContent?.trim()))
      .not.toContain('Edit dates');
    expect(Array.from(parent.querySelectorAll('button')).map((button) => button.textContent?.trim()))
      .not.toContain('Edit progress');
    findButton(parent, 'Add FS predecessor').click();
    fixture.detectChanges();
    setValue(host.querySelector<HTMLSelectElement>('select[name="predecessorTaskId"]')!, 'task-unscheduled');
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());

    expect(edits).toEqual([{
      kind: 'addDependency',
      predecessorTaskId: 'task-unscheduled',
      successorTaskId: 'task-parent',
      type: 'finishToStart',
      expectedVersion: 5,
      source: 'form'
    }]);
  });

  it('restricts Milestone progress to the canonical 0 or 100 values', async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    fixture.componentInstance.contract = ganttContract();
    const edits: CoglatasGanttEditIntent[] = [];
    fixture.componentInstance.editRequested.subscribe((intent) => edits.push(intent));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    findButton(itemElement(host, 'milestone-1'), 'Edit progress').click();
    fixture.detectChanges();
    setValue(host.querySelector<HTMLInputElement>('input[name="progressPercent"]')!, '50');
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());
    fixture.detectChanges();

    expect(edits).toEqual([]);
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('0 or 100');

    setValue(host.querySelector<HTMLInputElement>('input[name="progressPercent"]')!, '100');
    fixture.detectChanges();
    host.querySelector<HTMLFormElement>('.coglatas-gantt__form')!.dispatchEvent(submitEvent());

    expect(edits).toEqual([{
      kind: 'progress',
      taskId: 'milestone-1',
      progressPercent: 100,
      expectedVersion: 3,
      source: 'form'
    }]);
  });
});

function kanbanContract(): CoglatasKanbanContract<object> {
  const task = { id: 'task-1', stage: 'stage-todo', order: 1000, canMove: true };
  return {
    ariaLabel: 'Project board',
    presentation: 'desktop',
    state: 'ready',
    items: [task],
    itemIdentity: (item) => String((item as typeof task).id),
    itemTitle: () => 'Task one',
    itemStatus: (item) => String((item as typeof task).stage),
    itemOrder: (item) => Number((item as typeof task).order),
    itemDescription: () => 'Priority: High',
    itemMetadata: () => ['Not blocked'],
    itemKindLabel: () => 'Actionable leaf task',
    itemSwimlane: () => ({ key: 'unassigned', label: 'Unassigned' }),
    canOpenItem: () => true,
    canMoveItem: (item) => Boolean((item as typeof task).canMove),
    canRequestTransition: () => true,
    columns: [
      { id: 'stage-todo', label: 'Todo', category: 'todo', cardCount: 1, wipWarningLimit: null, hasWipWarning: false },
      { id: 'stage-done', label: 'Done', category: 'done', cardCount: 0, wipWarningLimit: null, hasWipWarning: false },
      { id: 'stage-cancelled', label: 'Cancelled', category: 'cancelled', cardCount: 0, wipWarningLimit: null, hasWipWarning: false, requiresReason: true }
    ]
  };
}

function ganttContract(): CoglatasGanttContract<object> {
  const parent = ganttItem({
    taskId: 'task-parent',
    title: 'Parent delivery',
    progressIsDerived: true,
    plannedStartDate: '2026-08-01',
    plannedEndDate: '2026-08-10',
    version: 5,
    editable: false
  });
  const leaf = ganttItem({
    taskId: 'task-leaf',
    title: 'Blocked implementation',
    parentTaskId: parent.taskId,
    plannedStartDate: '2026-08-02',
    plannedEndDate: '2026-08-05',
    version: 8,
    isBlocked: true,
    warnings: [{
      code: 'DEPENDENCY_VIOLATION',
      message: 'Starts before its predecessor finishes.',
      severity: 'warning',
      targetType: 'task',
      targetId: 'task-leaf',
      field: 'plannedStartDate',
      blocking: false
    }]
  });
  const milestone = ganttItem({
    taskId: 'milestone-1',
    title: 'Release checkpoint',
    kind: 'milestone',
    milestoneDate: '2026-08-12',
    version: 3
  });
  const unscheduled = ganttItem({
    taskId: 'task-unscheduled',
    title: 'Unscheduled follow-up',
    version: 2,
    warnings: [{
      code: 'UNSCHEDULED',
      message: 'Task has no planned dates.',
      severity: 'info',
      targetType: 'task',
      targetId: 'task-unscheduled',
      field: 'plannedStartDate',
      blocking: false
    }]
  });
  const permissions = ganttPermissions(true);
  return {
    ariaLabel: 'Canonical Project schedule',
    presentation: 'narrow',
    state: 'ready',
    tasks: [],
    taskIdentity: () => '',
    taskLabel: () => '',
    milestones: [],
    timezone: 'Asia/Tokyo',
    readOnly: false,
    calendar: {
      timeZone: 'Asia/Tokyo',
      workingDays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
      holidaysAvailable: false,
      limitations: ['Holiday service is unavailable.']
    },
    scheduledItems: [parent, leaf],
    unscheduledItems: [unscheduled],
    canonicalMilestones: [milestone],
    dependencies: [{
      dependencyId: 'dependency-1',
      predecessorTaskId: parent.taskId,
      successorTaskId: leaf.taskId,
      type: 'finishToStart',
      editable: true,
      version: 4,
      warnings: []
    }],
    warnings: [{
      code: 'MISSING_ACTIVE_PLANNED_END',
      message: 'An active Task has no planned end.',
      severity: 'warning',
      targetType: 'project',
      targetId: null,
      field: 'plannedEndDate',
      blocking: false
    }],
    permissions,
    busyItemId: null,
    focusItemId: null,
    feedback: 'Schedule loaded.'
  };
}

function ganttItem(overrides: {
  taskId: string;
  title: string;
  kind?: 'task' | 'milestone';
  parentTaskId?: string | null;
  progressIsDerived?: boolean;
  plannedStartDate?: string | null;
  plannedEndDate?: string | null;
  milestoneDate?: string | null;
  version: number;
  editable?: boolean;
  isBlocked?: boolean;
  warnings?: CoglatasGanttItem['warnings'];
}): CoglatasGanttItem {
  return {
    taskId: overrides.taskId,
    kind: overrides.kind ?? 'task',
    parentTaskId: overrides.parentTaskId ?? null,
    milestoneId: null,
    title: overrides.title,
    plannedStartDate: overrides.plannedStartDate ?? null,
    plannedEndDate: overrides.plannedEndDate ?? null,
    milestoneDate: overrides.milestoneDate ?? null,
    progressPercent: 40,
    progressIsDerived: overrides.progressIsDerived ?? false,
    workflowStageId: 'stage-in-progress',
    workflowStageName: 'In progress',
    stageCategory: 'inProgress',
    priority: 'high',
    isBlocked: overrides.isBlocked ?? false,
    primaryAssignee: { userId: 'user-1', displayName: 'Taylor' },
    version: overrides.version,
    scheduleEditPermissions: ganttPermissions(overrides.editable ?? true),
    warnings: overrides.warnings ?? []
  };
}

function ganttPermissions(editable: boolean): {
  canEditSchedule: boolean;
  canEditProgress: boolean;
  canManageDependencies: boolean;
  canClearSchedule: boolean;
  canOpen: true;
} {
  return {
    canEditSchedule: editable,
    canEditProgress: editable,
    canManageDependencies: editable,
    canClearSchedule: editable,
    canOpen: true
  };
}

function itemElement(host: HTMLElement, taskId: string): HTMLElement {
  return host.querySelector<HTMLElement>(`[data-gantt-item-id="${taskId}"]`)!;
}

function findButton(host: HTMLElement, label: string): HTMLButtonElement {
  return Array.from(host.querySelectorAll<HTMLButtonElement>('button'))
    .find((button) => button.textContent?.trim() === label)!;
}

function setValue(control: HTMLInputElement | HTMLSelectElement, value: string): void {
  control.value = value;
  control.dispatchEvent(new Event('input', { bubbles: true }));
  control.dispatchEvent(new Event('change', { bubbles: true }));
}

function dropEvent(): DragEvent {
  return new Event('drop', { bubbles: true, cancelable: true }) as DragEvent;
}

function submitEvent(): Event {
  return new Event('submit', { bubbles: true, cancelable: true });
}
