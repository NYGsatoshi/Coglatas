import { TestBed } from '@angular/core/testing';
import { FormControl, Validators } from '@angular/forms';

import { TASK_BRIEF_FIELD_MAX_LENGTH } from '../projects.types';
import { TaskBriefFieldsComponent } from './task-brief-fields.component';

beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));

describe('TaskBriefFieldsComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders optional fields and review content in Goal, Deliverable, Constraints order', async () => {
    await TestBed.configureTestingModule({ imports: [TaskBriefFieldsComponent] }).compileComponents();
    const fixture = TestBed.createComponent(TaskBriefFieldsComponent);
    const goal = new FormControl('Make review possible', { nonNullable: true });
    const deliverable = new FormControl('', { nonNullable: true });
    const constraints = new FormControl('Keep the URL stable', { nonNullable: true });
    fixture.componentRef.setInput('goalControl', goal);
    fixture.componentRef.setInput('deliverableControl', deliverable);
    fixture.componentRef.setInput('constraintsControl', constraints);
    fixture.detectChanges();

    const inputs = [...fixture.nativeElement.querySelectorAll('textarea')] as HTMLTextAreaElement[];
    const review = [...fixture.nativeElement.querySelectorAll('[data-testid="task-brief-review"] dt')]
      .map((element) => element.textContent?.trim());

    expect(inputs.map((input) => input.dataset['testid'])).toEqual([
      'task-brief-goal-input',
      'task-brief-deliverable-input',
      'task-brief-constraints-input'
    ]);
    expect(inputs.every((input) => input.maxLength === TASK_BRIEF_FIELD_MAX_LENGTH)).toBe(true);
    expect(review).toEqual(['Goal', 'Deliverable', 'Constraints']);
    expect(fixture.nativeElement.querySelector('[data-testid="task-brief-goal-source"]').textContent).toContain('Task-specific');
    expect(fixture.nativeElement.querySelector('[data-testid="task-brief-deliverable-source"]').textContent).toContain('Not set');
    expect(fixture.nativeElement.querySelector('[data-testid="task-brief-review-deliverable"]').textContent).toContain('Not set');
  });

  it('keeps every field read-only when authorization does not grant editing', async () => {
    await TestBed.configureTestingModule({ imports: [TaskBriefFieldsComponent] }).compileComponents();
    const fixture = TestBed.createComponent(TaskBriefFieldsComponent);
    fixture.componentRef.setInput('goalControl', new FormControl('', { nonNullable: true }));
    fixture.componentRef.setInput('deliverableControl', new FormControl('', { nonNullable: true }));
    fixture.componentRef.setInput('constraintsControl', new FormControl('', { nonNullable: true }));
    fixture.componentRef.setInput('readonly', true);
    fixture.detectChanges();

    const inputs = [...fixture.nativeElement.querySelectorAll('textarea')] as HTMLTextAreaElement[];
    expect(inputs.every((input) => input.readOnly)).toBe(true);
    expect(inputs.every((input) => input.getAttribute('aria-describedby')?.includes('task-brief-help'))).toBe(true);
  });

  it('exposes the mirrored 4000-character validation state', async () => {
    await TestBed.configureTestingModule({ imports: [TaskBriefFieldsComponent] }).compileComponents();
    const goal = new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(TASK_BRIEF_FIELD_MAX_LENGTH)]
    });
    const fixture = TestBed.createComponent(TaskBriefFieldsComponent);
    fixture.componentRef.setInput('goalControl', goal);
    fixture.componentRef.setInput('deliverableControl', new FormControl('', { nonNullable: true }));
    fixture.componentRef.setInput('constraintsControl', new FormControl('', { nonNullable: true }));
    goal.setValue('x'.repeat(TASK_BRIEF_FIELD_MAX_LENGTH + 1));
    goal.markAsTouched();
    fixture.detectChanges();

    expect(goal.invalid).toBe(true);
    const input = fixture.nativeElement.querySelector('[data-testid="task-brief-goal-input"]') as HTMLTextAreaElement;
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toContain('task-brief-goal-error');
    expect(alert.id).toBe('task-brief-goal-error');
    expect(alert.textContent).toContain('4,000 characters or fewer');
  });
});
