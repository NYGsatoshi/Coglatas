import { A11yModule } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Injector,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  afterNextRender,
  inject,
} from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';

import { AppRequestIdComponent } from '../../../shared/error/app-request-id/app-request-id.component';
import { AppFieldErrorComponent } from '../../../shared/form/app-field-error/app-field-error.component';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import {
  WorkspaceCreateFieldError,
  WorkspaceCreateInput,
  WorkspaceCreateViewModel,
} from '../workspaces.types';

const EMPTY_CREATE_STATE: WorkspaceCreateViewModel = {
  status: 'idle',
  fieldErrors: [],
};

const ICON_OPTIONS = [
  { value: '', label: 'None' },
  { value: '🧪', label: 'Experiment' },
  { value: '🔬', label: 'Research' },
  { value: '📚', label: 'Library' },
  { value: '🚀', label: 'Launch' },
] as const;

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-create-dialog',
  standalone: true,
  imports: [
    A11yModule,
    ReactiveFormsModule,
    CoglatasDialogComponent,
    AppFieldErrorComponent,
    AppRequestIdComponent,
  ],
  templateUrl: './workspace-create-dialog.component.html',
  styleUrl: './workspace-create-dialog.component.scss',
})
export class WorkspaceCreateDialogComponent implements OnChanges {
  @Input() open = false;
  @Input() canCreate = false;
  @Input() createState: WorkspaceCreateViewModel = EMPTY_CREATE_STATE;

  @Output() readonly submitted = new EventEmitter<WorkspaceCreateInput>();
  @Output() readonly retryActivation = new EventEmitter<void>();
  @Output() readonly cancelled = new EventEmitter<void>();

  @ViewChild('errorSummary') private errorSummary?: ElementRef<HTMLElement>;

  private readonly injector = inject(Injector);

  readonly formId = 'workspace-create-form';
  readonly iconOptions = ICON_OPTIONS;
  readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, nonWhitespaceValidator, Validators.maxLength(160)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(2000)],
    }),
    icon: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(120)],
    }),
  });

  private invalidSubmission = false;

  get busy(): boolean {
    return this.createState.status === 'submitting';
  }

  get activationPending(): boolean {
    return (
      this.createState.status === 'committedPendingActivation' ||
      (this.createState.status === 'submitting' && !!this.createState.createdWorkspaceId)
    );
  }

  get confirmDisabled(): boolean {
    return !this.activationPending && !this.canCreate;
  }

  get confirmLabel(): string {
    return this.activationPending ? 'Retry activation' : 'Create Workspace';
  }

  get summaryErrors(): readonly WorkspaceCreateFieldError[] {
    const errors: WorkspaceCreateFieldError[] = [];
    if (this.invalidSubmission) {
      if (this.form.controls.name.hasError('required') || this.form.controls.name.hasError('whitespace')) {
        errors.push({ field: 'name', message: 'Enter a Workspace name.' });
      }
      if (this.form.controls.name.hasError('maxlength')) {
        errors.push({ field: 'name', message: 'Workspace name must be 160 characters or fewer.' });
      }
      if (this.form.controls.description.hasError('maxlength')) {
        errors.push({ field: 'description', message: 'Description must be 2,000 characters or fewer.' });
      }
      if (this.form.controls.icon.hasError('maxlength')) {
        errors.push({ field: 'icon', message: 'Icon must be 120 characters or fewer.' });
      }
    }

    for (const error of this.createState.fieldErrors) {
      if (!errors.some((candidate) => candidate.field === error.field && candidate.message === error.message)) {
        errors.push(error);
      }
    }
    return errors;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open']?.currentValue === true && changes['open'].previousValue !== true) {
      this.form.reset({ name: '', description: '', icon: '' });
      this.invalidSubmission = false;
    }
  }

  submit(): void {
    if (this.busy || this.activationPending) {
      return;
    }

    if (this.form.invalid) {
      this.invalidSubmission = true;
      this.form.markAllAsTouched();
      // The summary is created by the template's @if after this submit event.
      afterNextRender(
        {
          write: () => {
            const summary = this.errorSummary?.nativeElement;
            if (this.open && summary?.isConnected) {
              summary.focus();
            }
          },
        },
        { injector: this.injector },
      );
      return;
    }

    this.invalidSubmission = false;
    this.submitted.emit(this.form.getRawValue());
  }

  handleDialogConfirm(): void {
    if (this.activationPending && !this.busy) {
      this.retryActivation.emit();
    }
  }

  requestCancel(): void {
    if (!this.busy) {
      this.cancelled.emit();
    }
  }

  fieldErrors(field: 'name' | 'description' | 'icon'): readonly string[] {
    const messages: string[] = [];
    const control = this.form.controls[field];
    if ((control.touched || this.invalidSubmission) && (control.hasError('required') || control.hasError('whitespace'))) {
      messages.push('Enter a Workspace name.');
    }
    if ((control.touched || this.invalidSubmission) && control.hasError('maxlength')) {
      messages.push(
        field === 'name'
          ? 'Workspace name must be 160 characters or fewer.'
          : field === 'description'
            ? 'Description must be 2,000 characters or fewer.'
            : 'Icon must be 120 characters or fewer.',
      );
    }
    for (const error of this.createState.fieldErrors) {
      if (error.field === field && !messages.includes(error.message)) {
        messages.push(error.message);
      }
    }
    return messages;
  }

  fieldInvalid(field: 'name' | 'description' | 'icon'): boolean {
    return this.fieldErrors(field).length > 0;
  }

  focusField(field: WorkspaceCreateFieldError['field'], event: Event): void {
    event.preventDefault();
    if (field === 'form') {
      this.errorSummary?.nativeElement.focus();
      return;
    }
    document.getElementById(`workspace-create-${field}`)?.focus();
  }
}

function nonWhitespaceValidator(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > 0 ? null : { whitespace: true };
}
