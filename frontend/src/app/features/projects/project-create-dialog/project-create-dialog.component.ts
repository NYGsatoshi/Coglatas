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
  ProjectCreateGroupOption,
  ProjectCreateInput,
  ProjectCreateOptions,
  ProjectVisibility,
  PROJECT_VISIBILITY_MEMBERS_ONLY,
  projectVisibilityLabel,
} from '../project-create.api';
import {
  EMPTY_PROJECT_CREATE_OPTIONS,
  EMPTY_PROJECT_CREATE_STATE,
  ProjectCreateField,
  ProjectCreateFieldError,
  ProjectCreateOptionsViewModel,
  ProjectCreateViewModel,
} from '../project-create.facade';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-project-create-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, CoglatasDialogComponent, AppFieldErrorComponent, AppRequestIdComponent],
  templateUrl: './project-create-dialog.component.html',
  styleUrl: './project-create-dialog.component.scss',
})
export class ProjectCreateDialogComponent implements OnChanges {
  @Input() open = false;
  @Input() workspaceName = 'Workspace';
  @Input() optionsState: ProjectCreateOptionsViewModel = EMPTY_PROJECT_CREATE_OPTIONS;
  @Input() createState: ProjectCreateViewModel = EMPTY_PROJECT_CREATE_STATE;

  @Output() readonly submitted = new EventEmitter<ProjectCreateInput>();
  @Output() readonly optionsRetried = new EventEmitter<void>();
  @Output() readonly navigationRetried = new EventEmitter<void>();
  @Output() readonly cancelled = new EventEmitter<void>();

  @ViewChild('errorSummary') private errorSummary?: ElementRef<HTMLElement>;
  @ViewChild('titleInput') private titleInput?: ElementRef<HTMLInputElement>;
  @ViewChild('optionsRetry') private optionsRetry?: ElementRef<HTMLButtonElement>;

  private readonly injector = inject(Injector);
  private invalidSubmission = false;

  readonly formId = 'project-create-form';
  readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, nonWhitespaceValidator, Validators.maxLength(200)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(4000)],
    }),
    groupSearch: new FormControl('', { nonNullable: true }),
    groupId: new FormControl('', { nonNullable: true }),
    visibility: new FormControl<ProjectVisibility>(PROJECT_VISIBILITY_MEMBERS_ONLY, {
      nonNullable: true,
    }),
    startDate: new FormControl('', { nonNullable: true }),
    endDate: new FormControl('', { nonNullable: true }),
  });

  get options(): ProjectCreateOptions | null {
    return this.optionsState.status === 'ready' ? (this.optionsState.data ?? null) : null;
  }

  get busy(): boolean {
    return this.createState.status === 'submitting';
  }

  get navigationPending(): boolean {
    return this.createState.status === 'committedPendingNavigation';
  }

  get confirmDisabled(): boolean {
    return !this.navigationPending && this.options === null;
  }

  get confirmLabel(): string {
    return this.navigationPending ? 'Open Project' : 'Create Project';
  }

  get focusReturnTargetId(): string {
    return this.navigationPending ? 'projects-resume-created-project' : 'projects-create-project';
  }

  get groupRequired(): boolean {
    return this.options?.canCreateUngrouped === false;
  }

  get visibilityOptions(): readonly { value: ProjectVisibility; label: string }[] {
    return (this.options?.allowedVisibilities ?? []).map((value) => ({
      value,
      label: projectVisibilityLabel(value),
    }));
  }

  get filteredGroups(): readonly ProjectCreateGroupOption[] {
    const groups = this.options?.groups ?? [];
    const query = this.form.controls.groupSearch.value.trim().toLocaleLowerCase();
    if (!query) {
      return groups;
    }

    const selectedId = this.form.controls.groupId.value;
    return groups.filter(
      (group) => group.name.toLocaleLowerCase().includes(query) || group.id === selectedId,
    );
  }

  get summaryErrors(): readonly ProjectCreateFieldError[] {
    const errors = this.invalidSubmission ? this.localErrors() : [];
    for (const error of this.createState.fieldErrors) {
      if (
        !errors.some(
          (candidate) => candidate.field === error.field && candidate.message === error.message,
        )
      ) {
        errors.push(error);
      }
    }
    return errors;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open']?.currentValue === true && changes['open'].previousValue !== true) {
      this.form.reset({
        title: '',
        description: '',
        groupSearch: '',
        groupId: '',
        visibility: PROJECT_VISIBILITY_MEMBERS_ONLY,
        startDate: '',
        endDate: '',
      });
      this.invalidSubmission = false;
    }

    const optionsChange = changes['optionsState'];
    if (
      this.open &&
      optionsChange?.currentValue?.status === 'error' &&
      optionsChange.previousValue?.status !== 'error'
    ) {
      this.focusAfterRender('optionsRetry');
    }
    if (
      this.open &&
      optionsChange?.currentValue?.status === 'ready' &&
      optionsChange.previousValue?.status !== 'ready'
    ) {
      const available = this.optionsState.data?.allowedVisibilities ?? [];
      const currentVisibility = this.form.controls.visibility.value;
      if (!available.includes(currentVisibility)) {
        this.form.controls.visibility.setValue(
          available.includes(PROJECT_VISIBILITY_MEMBERS_ONLY)
            ? PROJECT_VISIBILITY_MEMBERS_ONLY
            : (available[0] ?? PROJECT_VISIBILITY_MEMBERS_ONLY),
        );
      }
      this.focusAfterRender('title');
    }

    const createChange = changes['createState'];
    if (
      this.open &&
      createChange?.currentValue?.status === 'error' &&
      createChange.previousValue?.status !== 'error'
    ) {
      this.focusAfterRender(this.optionsState.status === 'error' ? 'optionsRetry' : 'summary');
    }
  }

  submit(): void {
    if (this.busy || this.navigationPending || !this.options) {
      return;
    }

    this.invalidSubmission = true;
    this.form.markAllAsTouched();
    if (this.form.invalid || this.localErrors().length > 0) {
      this.focusAfterRender('summary');
      return;
    }

    this.invalidSubmission = false;
    const value = this.form.getRawValue();
    this.submitted.emit({
      title: value.title,
      description: value.description,
      groupId: value.groupId,
      visibility: value.visibility,
      startDate: value.startDate,
      endDate: value.endDate,
    });
  }

  handleDialogConfirm(): void {
    if (this.navigationPending && !this.busy) {
      this.navigationRetried.emit();
    }
  }

  requestCancel(): void {
    if (!this.busy) {
      this.cancelled.emit();
    }
  }

  retryOptions(): void {
    if (!this.busy) {
      this.optionsRetried.emit();
    }
  }

  fieldErrors(field: Exclude<ProjectCreateField, 'form'>): readonly string[] {
    const messages: string[] = [];
    if (this.invalidSubmission || this.formControl(field)?.touched) {
      for (const error of this.localErrors()) {
        if (error.field === field && !messages.includes(error.message)) {
          messages.push(error.message);
        }
      }
    }
    for (const error of this.createState.fieldErrors) {
      if (error.field === field && !messages.includes(error.message)) {
        messages.push(error.message);
      }
    }
    return messages;
  }

  fieldInvalid(field: Exclude<ProjectCreateField, 'form'>): boolean {
    return this.fieldErrors(field).length > 0;
  }

  focusField(field: ProjectCreateField, event: Event): void {
    event.preventDefault();
    if (field === 'form') {
      this.errorSummary?.nativeElement.focus();
      return;
    }
    document.getElementById(fieldElementId(field))?.focus();
  }

  private localErrors(): ProjectCreateFieldError[] {
    const errors: ProjectCreateFieldError[] = [];
    const controls = this.form.controls;
    if (controls.title.hasError('required') || controls.title.hasError('whitespace')) {
      errors.push({ field: 'title', message: 'Enter a Project name.' });
    }
    if (controls.title.hasError('maxlength')) {
      errors.push({
        field: 'title',
        message: 'Project name must be 200 characters or fewer.',
      });
    }
    if (controls.description.hasError('maxlength')) {
      errors.push({
        field: 'description',
        message: 'Description must be 4,000 characters or fewer.',
      });
    }
    if (this.groupRequired && !controls.groupId.value) {
      errors.push({ field: 'groupId', message: 'Choose a Group available to you.' });
    }
    if (
      controls.groupId.value &&
      !this.options?.groups.some((group) => group.id === controls.groupId.value)
    ) {
      errors.push({ field: 'groupId', message: 'Choose a Group available to you.' });
    }
    if (!this.options?.allowedVisibilities.includes(controls.visibility.value)) {
      errors.push({ field: 'visibility', message: 'Choose a visibility available to you.' });
    }
    if (
      controls.startDate.value &&
      controls.endDate.value &&
      controls.endDate.value < controls.startDate.value
    ) {
      errors.push({
        field: 'endDate',
        message: 'Target end date cannot be before the start date.',
      });
    }
    return errors;
  }

  private formControl(field: Exclude<ProjectCreateField, 'form'>): FormControl | null {
    switch (field) {
      case 'title':
      case 'description':
      case 'groupId':
      case 'visibility':
      case 'startDate':
      case 'endDate':
        return this.form.controls[field] as FormControl;
    }
  }

  private focusAfterRender(target: 'summary' | 'title' | 'optionsRetry'): void {
    afterNextRender(
      {
        write: () => {
          if (!this.open) {
            return;
          }
          switch (target) {
            case 'summary':
              (this.errorSummary?.nativeElement ?? this.optionsRetry?.nativeElement)?.focus();
              return;
            case 'optionsRetry':
              (this.optionsRetry?.nativeElement ?? this.errorSummary?.nativeElement)?.focus();
              return;
            case 'title':
              this.titleInput?.nativeElement.focus();
          }
        },
      },
      { injector: this.injector },
    );
  }
}

function nonWhitespaceValidator(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > 0 ? null : { whitespace: true };
}

function fieldElementId(field: Exclude<ProjectCreateField, 'form'>): string {
  switch (field) {
    case 'groupId':
      return 'project-create-group';
    default:
      return `project-create-${field}`;
  }
}
