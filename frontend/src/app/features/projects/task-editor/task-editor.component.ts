import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges } from '@angular/core';
import { Subscription } from 'rxjs';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators
} from '@angular/forms';

import {
  BackendAuthoritativeTransitionNote,
  ProjectCapability,
  TASK_BRIEF_FIELD_MAX_LENGTH,
  TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE,
  TaskConflictReloadState,
  TaskDetailState,
  TaskEditorSaveRequest,
  TaskMockRecord,
  TaskMutationState,
  TaskPriority,
  TaskStatus
} from '../projects.types';
import { TaskBriefFieldsComponent } from '../task-brief-fields/task-brief-fields.component';

export const integerRangeValidator = (min: number, max: number): ValidatorFn => {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value;
    if (value === null || value === '') {
      return null;
    }

    const numericValue = Number(value);
    if (!Number.isInteger(numericValue)) {
      return { integer: true };
    }

    if (numericValue < min || numericValue > max) {
      return { range: { min, max } };
    }

    return null;
  };
};

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-editor',
  standalone: true,
  imports: [ReactiveFormsModule, TaskBriefFieldsComponent],
  template: `
    @if (task) {
      <form class="task-editor" [formGroup]="form" (ngSubmit)="submit()" data-testid="task-editor">
        @if (state === 'rowVersionConflict') {
          <section class="task-editor__recoverable" data-testid="row-version-conflict">
            <h2>Row version conflict</h2>
            <p>The task changed on the server. Reload before saving again.</p>
            <button type="button" (click)="reloadRequested.emit()">Reload form</button>
          </section>
        }

        @if (state === 'invalidStateTransition') {
          <section class="task-editor__recoverable" data-testid="invalid-state-transition">
            <h2>Invalid status transition</h2>
            <p>The selected transition is no longer allowed by the backend.</p>
          </section>
        }

        @if (mutationState.status === 'failure') {
          <section class="task-editor__error" role="alert" data-testid="task-save-error">
            <strong>Save failed</strong>
            <span>{{ mutationState.message }}</span>
            @if (mutationState.requestId) {
              <small>Request ID: {{ mutationState.requestId }}</small>
            }
          </section>
        }

        @if (mutationState.status === 'savedButRefreshFailed') {
          <section class="task-editor__recoverable" role="alert" data-testid="task-saved-refresh-failed">
            <strong>Task was saved, but the latest data could not be loaded.</strong>
            <span>Reload before editing again. {{ mutationState.message }}</span>
            @if (mutationState.requestId) { <small>Request ID: {{ mutationState.requestId }}</small> }
            <button type="button" data-testid="task-saved-refresh-reload-button" (click)="reloadRequested.emit()">Reload latest task</button>
          </section>
        }

        @if (mutationState.status === 'conflict') {
          <section class="task-editor__recoverable" role="alert" data-testid="task-save-conflict">
            <strong>Task changed elsewhere</strong>
            <span>{{ mutationState.message }}</span>
            @if (mutationState.requestId) { <small>Request ID: {{ mutationState.requestId }}</small> }
            @if (conflictReloadState === 'error') { <span data-testid="task-conflict-reload-error">Reload failed. Your changes are still here; try again.</span> }
            <button type="button" data-testid="task-conflict-reload-button" [disabled]="conflictReloadState === 'loading'" (click)="reloadRequested.emit()">{{ conflictReloadState === 'loading' ? 'Reloading...' : 'Reload before saving' }}</button>
          </section>
        }

        @if (mutationState.status === 'validation' || mutationState.status === 'rateLimited') {
          <section class="task-editor__error" role="alert"><strong>{{ mutationState.status === 'validation' ? 'Check the task fields' : 'Please wait before retrying' }}</strong><span>{{ mutationState.message }}</span></section>
        }

        @if (mutationState.status === 'success') {
          <p class="task-editor__success" data-testid="task-save-success">Task saved.</p>
        }

        @if (!canEdit) {
          <p class="task-editor__note" data-testid="task-editor-readonly-note">
            Task editing is disabled because the backend did not grant edit permission for this task.
          </p>
        }

        <p class="task-editor__note" data-testid="status-transition-authoritative-note">
          {{ transitionNote.message }}
        </p>

        @if (!canChangeStatus) {
          <p class="task-editor__note" data-testid="task-status-disabled-note">
            Status changes are disabled until the backend publishes allowed transitions for this task.
          </p>
        }

        @if (task.progressIsDerived) {
          <p class="task-editor__note" data-testid="task-derived-fields-note">Progress and planned dates are calculated from direct subtasks.</p>
        }

        <label>
          <span>Title</span>
          <input
            type="text"
            formControlName="title"
            data-testid="task-title-input"
            [readonly]="!canEdit || editingLocked"
          />
          @if (form.controls.title.invalid && form.controls.title.touched) {
            <small data-testid="task-title-error">Task title is required.</small>
          }
        </label>

        <app-task-brief-fields
          [goalControl]="form.controls.goal"
          [deliverableControl]="form.controls.deliverable"
          [constraintsControl]="form.controls.constraints"
          [readonly]="!canEdit || editingLocked"
        />

        <label>
          <span>Free-form task notes <small>(optional)</small></span>
          <textarea
            formControlName="description"
            rows="4"
            data-testid="task-description-input"
            [readonly]="!canEdit || editingLocked"
          ></textarea>
        </label>

        <label><span>Status</span><input formControlName="status" data-testid="task-status-readonly" readonly aria-readonly="true" /></label>

        <label>
          <span>Priority</span>
          <select
            formControlName="priority"
            data-testid="task-priority-select"
            [attr.aria-disabled]="!canEdit || editingLocked"
            [attr.tabindex]="canEdit && !editingLocked ? null : -1"
            (mousedown)="preventWhenDisabled($event, canEdit)"
            (keydown)="preventWhenDisabled($event, canEdit)"
          >
            @for (priority of priorities; track priority.value) {
              <option [value]="priority.value">{{ priority.label }}</option>
            }
          </select>
        </label>

        <label>
          <span>Due date</span>
          <input
            type="date"
            formControlName="dueDate"
            data-testid="task-due-date-input"
            [readonly]="!canEdit || editingLocked || task.progressIsDerived"
            [attr.aria-readonly]="task.progressIsDerived ? 'true' : null"
          />
          @if (hasUnsupportedDueDateClear()) {
            <small data-testid="task-due-date-clear-error">Existing due dates can be replaced but not cleared in MVP0.</small>
          }
        </label>

        <label>
          <span>Assignee</span>
          <input
            type="text"
            formControlName="assignee"
            data-testid="task-assignee-input"
            readonly
            aria-readonly="true"
          />
        </label>

        <label>
          <span>Start date</span>
          <input
            type="date"
            formControlName="startDate"
            data-testid="task-start-date-input"
            [readonly]="!canEdit || editingLocked || task.progressIsDerived"
            [attr.aria-readonly]="task.progressIsDerived ? 'true' : null"
          />
          @if (hasUnsupportedStartDateClear()) {
            <small data-testid="task-start-date-clear-error">Existing start dates can be replaced but not cleared in MVP0.</small>
          }
        </label>

        <label>
          <span>Progress percent</span>
          <input
            type="number"
            formControlName="progressPercent"
            data-testid="task-progress-input"
            [readonly]="!canEdit || editingLocked || task.progressIsDerived"
            [attr.aria-readonly]="task.progressIsDerived ? 'true' : null"
          />
          @if (form.controls.progressPercent.invalid && form.controls.progressPercent.touched) {
            <small data-testid="task-progress-error">Progress must be an integer from 0 to 100.</small>
          }
        </label>

        <label>
          <span>Milestone</span>
          <input
            type="text"
            formControlName="milestone"
            readonly
            aria-readonly="true"
            data-testid="task-milestone-input"
          />
        </label>

        <div class="task-editor__actions">
          <button
            type="submit"
            class="task-editor__save"
            data-testid="task-save-button"
            [disabled]="!canSubmit"
          >
            {{ isSubmitting ? 'Saving...' : 'Save task' }}
          </button>
          <button
            type="button"
            class="task-editor__cancel"
            data-testid="task-cancel-button"
            [disabled]="editingLocked"
            (click)="resetForm(); cancel.emit()"
          >
            Cancel
          </button>
        </div>
      </form>
    }
  `,
  styles: [
    `
      .task-editor {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 1rem;
      }

      .task-editor label,
      .task-editor__recoverable,
      .task-editor__note,
      .task-editor__error,
      .task-editor__success {
        display: grid;
        gap: 0.375rem;
      }

      .task-editor label:nth-of-type(2),
      .task-editor app-task-brief-fields,
      .task-editor__recoverable,
      .task-editor__note,
      .task-editor__error,
      .task-editor__success,
      .task-editor__actions {
        grid-column: 1 / -1;
        min-width: 0;
      }

      .task-editor input,
      .task-editor select,
      .task-editor textarea {
        width: 100%;
        box-sizing: border-box;
        padding: 0.625rem 0.75rem;
        border: 1px solid #b7c2d6;
        border-radius: 0.5rem;
        font: inherit;
      }

      .task-editor input[readonly],
      .task-editor textarea[readonly],
      .task-editor select[aria-disabled='true'] {
        background: #eef2f7;
        color: #475569;
      }

      .task-editor small {
        color: #a33131;
      }

      .task-editor__note,
      .task-editor__recoverable,
      .task-editor__error,
      .task-editor__success {
        padding: 0.75rem;
        border: 1px solid #c7d0df;
        border-radius: 0.5rem;
        background: #f7f9fc;
      }

      .task-editor__error {
        border-color: #efb4b4;
        background: #fff5f5;
      }

      .task-editor__success {
        border-color: #86bf91;
        background: #f1fbf3;
      }

      .task-editor__recoverable h2,
      .task-editor__recoverable p,
      .task-editor__note,
      .task-editor__success {
        margin: 0;
      }

      .task-editor__actions {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }

      .task-editor__save,
      .task-editor__cancel {
        min-height: 40px;
        border: 1px solid #94a3b8;
        border-radius: 6px;
        font-weight: 700;
        padding: 0 14px;
      }

      .task-editor__save {
        background: #184b8f;
        color: #fff;
      }

      .task-editor__cancel {
        background: #fff;
        color: #172033;
      }

      .task-editor__save:disabled,
      .task-editor__cancel:disabled {
        cursor: not-allowed;
        opacity: 0.6;
      }

      @media (max-width: 720px) {
        .task-editor {
          grid-template-columns: 1fr;
        }

        .task-editor label:nth-of-type(2),
        .task-editor app-task-brief-fields,
        .task-editor__recoverable,
        .task-editor__note,
        .task-editor__error,
        .task-editor__success,
        .task-editor__actions {
          grid-column: auto;
        }
      }
    `
  ],
})
export class TaskEditorComponent implements OnChanges, OnInit, OnDestroy {
  @Input() task: TaskMockRecord | undefined;
  @Input() capabilities: readonly ProjectCapability[] = [];
  @Input() state: TaskDetailState = 'ready';
  @Input() transitionNote: BackendAuthoritativeTransitionNote = TASK_STATUS_BACKEND_AUTHORITATIVE_NOTE;
  @Input() mutationState: TaskMutationState = { status: 'idle' };
  /** Canonical aggregate version supplied by Task Detail, not the project-list row. */
  @Input() expectedVersion = '';
  @Input() conflictReloadState: TaskConflictReloadState = 'idle';
  @Output() save = new EventEmitter<TaskEditorSaveRequest>();
  @Output() cancel = new EventEmitter<void>();
  /** Reloading after a stale version must fetch the server-authoritative task. */
  @Output() reloadRequested = new EventEmitter<void>();
  /** Exposes unsaved form state without leaking the editor implementation to its parent. */
  @Output() dirtyChange = new EventEmitter<boolean>();
  private formChanges: Subscription | null = null;
  private formSourceKey = '';

  readonly statuses: readonly { value: TaskStatus; label: string }[] = [
    { value: 'notStarted', label: 'Not started' },
    { value: 'inProgress', label: 'In progress' },
    { value: 'blocked', label: 'Blocked' },
    { value: 'review', label: 'Review' },
    { value: 'done', label: 'Done' },
    { value: 'cancelled', label: 'Cancelled' }
  ];
  readonly priorities: readonly { value: TaskPriority; label: string }[] = [
    { value: 'low', label: 'Low' },
    { value: 'medium', label: 'Medium' },
    { value: 'high', label: 'High' },
    { value: 'urgent', label: 'Urgent' }
  ];

  readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true }),
    goal: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(TASK_BRIEF_FIELD_MAX_LENGTH)] }),
    deliverable: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(TASK_BRIEF_FIELD_MAX_LENGTH)] }),
    constraints: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(TASK_BRIEF_FIELD_MAX_LENGTH)] }),
    status: new FormControl<TaskStatus>('notStarted', { nonNullable: true }),
    priority: new FormControl<TaskPriority>('medium', { nonNullable: true }),
    dueDate: new FormControl('', { nonNullable: true }),
    assignee: new FormControl('', { nonNullable: true }),
    startDate: new FormControl('', { nonNullable: true }),
    progressPercent: new FormControl(0, {
      nonNullable: true,
      validators: [integerRangeValidator(0, 100)]
    }),
    milestone: new FormControl('', { nonNullable: true })
  });

  get canEdit(): boolean {
    return this.capabilities.includes('editTask');
  }

  get canChangeStatus(): boolean {
    return this.capabilities.includes('changeTaskStatus') && (this.task?.allowedTransitions.length ?? 0) > 0;
  }

  get isSubmitting(): boolean {
    return this.mutationState.status === 'submitting' || this.mutationState.status === 'refreshingAfterSave';
  }

  get editingLocked(): boolean { return this.isSubmitting || this.mutationState.status === 'savedButRefreshFailed'; }

  get canSubmit(): boolean {
    return (
      this.canEdit &&
      !this.isSubmitting &&
      this.mutationState.status !== 'savedButRefreshFailed' &&
      this.form.valid &&
      !this.hasUnsupportedStartDateClear() &&
      !this.hasUnsupportedDueDateClear() &&
      this.mutationState.status !== 'conflict' &&
      this.conflictReloadState !== 'loading' &&
      this.hasValidExpectedVersion()
    );
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['task'] && (!this.form.dirty || this.formSourceKey !== this.currentTaskSourceKey())) {
      this.resetForm();
    }
  }

  ngOnInit(): void {
    this.formChanges = this.form.valueChanges.subscribe(() => this.emitDirty());
  }

  ngOnDestroy(): void { this.formChanges?.unsubscribe(); this.dirtyChange.emit(false); }

  submit(): void {
    this.form.markAllAsTouched();
    if (!this.canSubmit) {
      return;
    }

    const value = this.form.getRawValue();
    this.save.emit({
      title: value.title,
      description: value.description,
      goal: value.goal,
      deliverable: value.deliverable,
      constraints: value.constraints,
      priority: value.priority,
      startDate: value.startDate,
      dueDate: value.dueDate,
      progressPercent: value.progressPercent,
      expectedVersion: this.expectedVersion
    });
  }

  resetForm(): void {
    if (!this.task) {
      return;
    }

    this.form.setValue({
      title: this.task.title,
      description: this.task.description,
      goal: this.task.brief?.goal.value ?? '',
      deliverable: this.task.brief?.deliverable.value ?? '',
      constraints: this.task.brief?.constraints.value ?? '',
      status: this.task.status,
      priority: this.task.priority,
      dueDate: this.task.dueDate,
      assignee: this.task.assignee,
      startDate: this.task.startDate,
      progressPercent: this.task.progressPercent ?? 0,
      milestone: this.task.milestone
    });
    this.formSourceKey = this.currentTaskSourceKey();
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.emitDirty();
  }

  preventWhenDisabled(event: Event, enabled: boolean): void {
    if (!enabled || this.editingLocked) {
      event.preventDefault();
    }
  }

  hasUnsupportedStartDateClear(): boolean {
    return this.hasUnsupportedDateClear(this.task?.startDate, this.form.controls.startDate.value);
  }

  hasUnsupportedDueDateClear(): boolean {
    return this.hasUnsupportedDateClear(this.task?.dueDate, this.form.controls.dueDate.value);
  }

  isUnsupportedTransition(status: TaskStatus): boolean {
    if (!this.task || status === this.task.status) {
      return false;
    }

    return !this.canChangeStatus || !this.task.allowedTransitions.includes(status);
  }

  private currentTaskSourceKey(): string {
    return this.task ? `${this.task.id}:${this.task.rowVersion}` : '';
  }

  private hasUnsupportedDateClear(original: string | undefined, next: string): boolean {
    return (original?.length ?? 0) > 0 && next.trim().length === 0;
  }

  private hasValidExpectedVersion(): boolean {
    const version = Number(this.expectedVersion);
    return Number.isSafeInteger(version) && version > 0;
  }

  private emitDirty(): void { this.dirtyChange.emit(this.form.dirty && !this.isSubmitting); }
}
