import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { TaskEditorComponent } from './task-editor.component';

describe('TaskEditorComponent conflict recovery outputs', () => {
  let fixture: ComponentFixture<TaskEditorComponent>;
  let component: TaskEditorComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [TaskEditorComponent] });
    fixture = TestBed.createComponent(TaskEditorComponent);
    component = fixture.componentInstance;
    component.task = { id: 'task-1', projectId: 'project-1', title: 'Current', description: '', status: 'notStarted', statusLabel: 'Not started', priority: 'medium', priorityLabel: 'Medium', assignee: '', startDate: '', dueDate: '', progressPercent: 0, milestone: '', allowedTransitions: [], authorized: true, capabilities: ['editTask'], dependencyIds: [], rowVersion: '1' };
    component.capabilities = ['editTask'];
    component.expectedVersion = '1';
  });

  it('emits reloadRequested, not cancel, from the 409 reload button', () => {
    const cancel = vi.fn();
    const reload = vi.fn();
    component.cancel.subscribe(cancel);
    component.reloadRequested.subscribe(reload);
    component.mutationState = { status: 'conflict', message: 'Task changed.' };
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="task-conflict-reload-button"]') as HTMLButtonElement).click();

    expect(reload).toHaveBeenCalledTimes(1);
    expect(cancel).not.toHaveBeenCalled();
  });

  it('emits cancel, not reloadRequested, from the ordinary cancel button', () => {
    const cancel = vi.fn();
    const reload = vi.fn();
    component.cancel.subscribe(cancel);
    component.reloadRequested.subscribe(reload);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="task-cancel-button"]') as HTMLButtonElement).click();

    expect(cancel).toHaveBeenCalledTimes(1);
    expect(reload).not.toHaveBeenCalled();
  });

  it('keeps the draft mounted while conflict reload is in progress or has failed', () => {
    component.mutationState = { status: 'conflict', message: 'Stale.' };
    component.conflictReloadState = 'loading';
    fixture.detectChanges();
    component.form.controls.title.setValue('Local draft');
    component.conflictReloadState = 'error';
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('[data-testid="task-title-input"]') as HTMLInputElement).value).toBe('Local draft');
    expect(fixture.nativeElement.querySelector('[data-testid="task-conflict-reload-error"]')).not.toBeNull();
  });

  it('does not emit a save when the canonical expected version is invalid', () => {
    const save = vi.fn();
    component.save.subscribe(save);
    component.expectedVersion = 'not-a-version';
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="task-save-button"]') as HTMLButtonElement).click();
    expect(save).not.toHaveBeenCalled();
  });

  it('preserves a dirty draft across a same-Task same-version projection refresh', () => {
    const initialTask = component.task!;
    fixture.componentRef.setInput('task', initialTask);
    fixture.detectChanges();

    component.form.controls.description.setValue('Unsaved local description');
    component.form.markAsDirty();
    const save = vi.fn();
    component.save.subscribe(save);

    fixture.componentRef.setInput('task', {
      ...initialTask,
      description: 'Same-version server projection'
    });
    fixture.detectChanges();

    expect(component.form.controls.description.value).toBe('Unsaved local description');
    component.submit();
    expect(save).toHaveBeenCalledWith(expect.objectContaining({
      description: 'Unsaved local description',
      expectedVersion: '1'
    }));
  });

  it('reloads a dirty draft when the same Task advances to a new canonical version', () => {
    const initialTask = component.task!;
    fixture.componentRef.setInput('task', initialTask);
    fixture.detectChanges();

    component.form.controls.description.setValue('Unsaved local description');
    component.form.markAsDirty();

    fixture.componentRef.setInput('task', {
      ...initialTask,
      description: 'Authoritative newer description',
      rowVersion: '2'
    });
    fixture.detectChanges();

    expect(component.form.controls.description.value).toBe('Authoritative newer description');
    expect(component.form.dirty).toBe(false);
  });

  it('keeps parent-derived progress and planned dates read-only while ordinary fields remain editable', () => {
    component.task = { ...component.task!, progressIsDerived: true };
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('[data-testid="task-progress-input"]') as HTMLInputElement).readOnly).toBe(true);
    expect((fixture.nativeElement.querySelector('[data-testid="task-start-date-input"]') as HTMLInputElement).readOnly).toBe(true);
    expect((fixture.nativeElement.querySelector('[data-testid="task-due-date-input"]') as HTMLInputElement).readOnly).toBe(true);
    expect((fixture.nativeElement.querySelector('[data-testid="task-title-input"]') as HTMLInputElement).readOnly).toBe(false);
    expect(fixture.nativeElement.querySelector('[data-testid="task-derived-fields-note"]')).not.toBeNull();
  });

  it('loads, reviews, clears, and saves the structured brief without changing free-form notes semantics', () => {
    component.task = {
      ...component.task!,
      description: 'Legacy free-form notes',
      brief: {
        goal: { value: 'Existing goal', source: 'taskSpecific' },
        deliverable: { value: 'Existing deliverable', source: 'taskSpecific' },
        constraints: { value: null, source: 'notSet' }
      }
    };
    const save = vi.fn();
    component.save.subscribe(save);
    component.resetForm();
    fixture.detectChanges();

    expect(component.form.controls.description.value).toBe('Legacy free-form notes');
    expect(component.form.controls.goal.value).toBe('Existing goal');
    component.form.controls.deliverable.setValue('');
    component.form.controls.constraints.setValue('New constraint');
    component.submit();

    expect(save).toHaveBeenCalledWith(expect.objectContaining({
      description: 'Legacy free-form notes',
      goal: 'Existing goal',
      deliverable: '',
      constraints: 'New constraint'
    }));
    const reviewText = fixture.nativeElement.querySelector('[data-testid="task-brief-review"]')?.textContent ?? '';
    expect(reviewText.indexOf('Goal')).toBeLessThan(reviewText.indexOf('Deliverable'));
    expect(reviewText.indexOf('Deliverable')).toBeLessThan(reviewText.indexOf('Constraints'));
  });

  it('makes all Task Brief fields read-only without edit authorization', () => {
    component.capabilities = [];
    fixture.detectChanges();

    const inputs = [...fixture.nativeElement.querySelectorAll('[data-testid^="task-brief-"][data-testid$="-input"]')] as HTMLTextAreaElement[];
    expect(inputs).toHaveLength(3);
    expect(inputs.every((input) => input.readOnly)).toBe(true);
  });
});
