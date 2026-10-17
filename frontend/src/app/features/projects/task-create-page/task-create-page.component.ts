import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';
import { projectUiText, ProjectUiCopy } from '../projects.i18n';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { distinctUntilChanged, map } from 'rxjs';

import { AppRequestIdComponent } from '../../../shared/error/app-request-id/app-request-id.component';
import { AppFieldErrorComponent } from '../../../shared/form/app-field-error/app-field-error.component';
import { AppFormActionsComponent } from '../../../shared/form/app-form-actions/app-form-actions.component';
import { AppInlineLoadingComponent } from '../../../shared/loading/app-inline-loading/app-inline-loading.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { TASK_BRIEF_FIELD_MAX_LENGTH, TaskPriority } from '../projects.types';
import { TaskBriefFieldsComponent } from '../task-brief-fields/task-brief-fields.component';
import {
  TaskCreateField,
  TaskCreateFieldError,
  TaskCreateFacade,
  TaskCreateStatus,
} from '../task-create.facade';
import { TaskCreateInput, TaskCreateSourceScopeMode } from '../task-create.api';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-create-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AppFieldErrorComponent,
    AppFormActionsComponent,
    AppInlineLoadingComponent,
    AppPermissionDeniedComponent,
    AppRequestIdComponent,
    TaskBriefFieldsComponent,
  ],
  templateUrl: './task-create-page.component.html',
  styleUrl: './task-create-page.component.scss',
})
export class TaskCreatePageComponent {
  readonly i18n = injectProjectsI18n();
  readonly t = (text: ProjectUiCopy, parameters: Readonly<Record<string, string | number>> = {}): string => projectUiText(this.i18n, text, parameters);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly facade = inject(TaskCreateFacade);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly initializedProjectId = signal<string | null>(null);
  private readonly formRevision = signal(0);
  private readonly discardConfirmationVisible = signal(false);
  readonly optionsChangedNotice = signal<string | null>(null);
  private currentProjectId = '';
  private invalidSubmission = false;
  private previousCreateStatus: TaskCreateStatus = 'idle';
  private focusTitleAfterOptionsRecovery = false;

  readonly optionsState = this.facade.options;
  readonly createState = this.facade.createState;
  readonly errorSummary = viewChild<ElementRef<HTMLElement>>('errorSummary');
  readonly titleInput = viewChild<ElementRef<HTMLInputElement>>('titleInput');
  readonly optionsRetry = viewChild<ElementRef<HTMLButtonElement>>('optionsRetry');

  readonly priorities: readonly { value: TaskPriority; label: ProjectUiCopy }[] = [
    { value: 'low', label: 'Low' },
    { value: 'medium', label: 'Medium' },
    { value: 'high', label: 'High' },
    { value: 'urgent', label: 'Urgent' },
  ];

  readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, nonWhitespaceValidator, Validators.maxLength(240)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(8000)],
    }),
    priority: new FormControl<TaskPriority>('medium', { nonNullable: true }),
    milestoneId: new FormControl('', { nonNullable: true }),
    primaryAssigneeUserId: new FormControl('', { nonNullable: true }),
    startDate: new FormControl('', { nonNullable: true }),
    dueDate: new FormControl('', { nonNullable: true }),
    goal: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(TASK_BRIEF_FIELD_MAX_LENGTH)],
    }),
    deliverable: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(TASK_BRIEF_FIELD_MAX_LENGTH)],
    }),
    constraints: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(TASK_BRIEF_FIELD_MAX_LENGTH)],
    }),
    sourceScopeMode: new FormControl<TaskCreateSourceScopeMode>('Inherit', {
      nonNullable: true,
    }),
    webEnabled: new FormControl(false, { nonNullable: true }),
    projectFilesEnabled: new FormControl(false, { nonNullable: true }),
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.facade.release());

    this.route.paramMap
      .pipe(
        map((params) => params.get('projectId')?.trim() ?? ''),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((projectId) => {
        this.currentProjectId = projectId;
        this.resetForRoute();
        if (projectId) {
          void this.facade.load(projectId);
        } else {
          this.facade.release();
        }
      });

    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.formRevision.update((value) => value + 1);
      this.discardConfirmationVisible.set(false);
      if (this.facade.createState().status === 'error') {
        this.facade.resetCreatePresentation();
      }
    });

    effect(() => {
      const optionsState = this.optionsState();
      if (
        optionsState.status === 'idle' &&
        this.currentProjectId !== '' &&
        this.initializedProjectId() === this.currentProjectId
      ) {
        this.focusTitleAfterOptionsRecovery = true;
      }
      if (optionsState.status === 'ready' && optionsState.data) {
        const restoreTitleFocus =
          this.focusTitleAfterOptionsRecovery &&
          this.currentProjectId === optionsState.data.projectId &&
          this.initializedProjectId() === optionsState.data.projectId;
        this.synchronizeAuthorizedOptions(optionsState.data.projectId);
        if (restoreTitleFocus) {
          this.focusTitleAfterOptionsRecovery = false;
          this.focusAfterRender('title');
        }
      } else if (optionsState.status === 'denied' || optionsState.status === 'error') {
        this.focusTitleAfterOptionsRecovery = false;
      }

      const createState = this.createState();
      if (
        createState.status === 'error' &&
        this.previousCreateStatus !== 'error'
      ) {
        this.focusAfterRender(optionsState.status === 'error' ? 'optionsRetry' : 'summary');
      }
      this.previousCreateStatus = createState.status;
    });
  }

  get options() {
    return this.optionsState().status === 'ready' ? (this.optionsState().data ?? null) : null;
  }

  get busy(): boolean {
    return this.createState().status === 'submitting';
  }

  get navigationPending(): boolean {
    return this.createState().status === 'committedPendingNavigation';
  }

  get canManageTaskScope(): boolean {
    const options = this.options;
    return options?.canManageProject === true && options.projectScope.canSetTaskOverride === true;
  }

  get unsentDraft(): boolean {
    this.formRevision();
    return this.form.dirty && !this.navigationPending;
  }

  get showDiscardConfirmation(): boolean {
    return this.discardConfirmationVisible();
  }

  get summaryErrors(): readonly TaskCreateFieldError[] {
    const errors = this.invalidSubmission ? this.localErrors() : [];
    for (const error of this.createState().fieldErrors) {
      if (!errors.some((candidate) => candidate.field === error.field && candidate.message === error.message)) {
        errors.push(error);
      }
    }
    return errors;
  }

  get sourceScopeMode(): TaskCreateSourceScopeMode {
    return this.form.controls.sourceScopeMode.value;
  }

  /**
   * A local, advisory pre-create review. It deliberately mirrors only values
   * already present in the canonical create form: no new required fields,
   * inferred project Brief, or execution/runtime state is introduced here.
   */
  get qualityChecklist(): readonly TaskCreateQualityItem[] {
    this.formRevision();
    const controls = this.form.controls;
    const usesTaskOverride =
      controls.sourceScopeMode.value === 'TaskOverride' && this.canManageTaskScope;
    const sourcePolicy = usesTaskOverride
      ? {
          webEnabled: controls.webEnabled.value,
          projectFilesEnabled: controls.projectFilesEnabled.value,
        }
      : this.options?.projectScope.policy ?? {
          webEnabled: false,
          projectFilesEnabled: false,
        };
    const sourcePolicyLabel = this.t(usesTaskOverride ? 'Task-specific policy' : 'Project default policy');
    return [
      briefQualityItem('goal', this.t('Goal'), controls.goal.value, this.t),
      briefQualityItem('deliverable', this.t('Deliverable'), controls.deliverable.value, this.t),
      briefQualityItem('constraints', this.t('Constraints'), controls.constraints.value, this.t),
      {
        id: 'sourceScopeMode',
        label: this.t('Source scope'),
        status: 'complete',
        detail: this.t('{{policy}}: Web {{web}}; Project files {{files}}.', {policy: sourcePolicyLabel, web: this.t(sourcePolicy.webEnabled ? 'enabled' : 'disabled'), files: this.t(sourcePolicy.projectFilesEnabled ? 'enabled' : 'disabled')}),
        actionLabel: null,
      },
    ];
  }

  get qualityChecklistCompletedCount(): number {
    return this.qualityChecklist.filter((item) => item.status === 'complete').length;
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
    const input: TaskCreateInput = {
      title: value.title,
      description: value.description,
      priority: value.priority,
      milestoneId: value.milestoneId,
      startDate: value.startDate,
      dueDate: value.dueDate,
      goal: value.goal,
      deliverable: value.deliverable,
      constraints: value.constraints,
      primaryAssigneeUserId: value.primaryAssigneeUserId,
      sourceScopeMode: value.sourceScopeMode,
      taskOverridePolicy:
        value.sourceScopeMode === 'TaskOverride' && this.canManageTaskScope
          ? {
              webEnabled: value.webEnabled,
              projectFilesEnabled: value.projectFilesEnabled,
            }
          : null,
    };
    void this.facade.createTask(input);
  }

  retryOptions(): void {
    if (!this.busy) {
      void this.facade.retryOptions();
    }
  }

  retryCreatedTaskNavigation(): void {
    if (!this.busy) {
      void this.facade.retryCreatedTaskNavigation();
    }
  }

  setSourceScopeMode(mode: TaskCreateSourceScopeMode): void {
    if (mode === 'TaskOverride' && !this.canManageTaskScope) {
      return;
    }
    if (mode === 'TaskOverride' && this.form.controls.sourceScopeMode.value !== 'TaskOverride') {
      const inherited = this.options?.projectScope.policy;
      if (inherited) {
        this.form.controls.webEnabled.setValue(inherited.webEnabled);
        this.form.controls.projectFilesEnabled.setValue(inherited.projectFilesEnabled);
      }
    }
    this.form.controls.sourceScopeMode.setValue(mode);
  }

  requestCancel(): void {
    if (this.busy || this.navigationPending) {
      return;
    }
    if (this.unsentDraft && !this.showDiscardConfirmation) {
      this.discardConfirmationVisible.set(true);
      return;
    }
    this.discardDraftAndReturn();
  }

  keepDraft(): void {
    this.discardConfirmationVisible.set(false);
    this.focusAfterRender('title');
  }

  discardDraftAndReturn(): void {
    if (this.busy) {
      return;
    }
    this.discardConfirmationVisible.set(false);
    this.facade.resetCreatePresentation();
    this.resetForm(this.options?.projectScope.policy);
    if (this.currentProjectId) {
      void this.router.navigate(['/projects', this.currentProjectId]);
    }
  }

  returnToProject(): void {
    if (this.navigationPending && this.currentProjectId) {
      // This is an explicit abandonment of navigation recovery. Do not leave
      // a committed result resident in the root facade: reopening this route
      // must be able to create a different Task rather than retry the old one.
      this.facade.release();
      void this.router.navigate(['/projects', this.currentProjectId]);
      return;
    }
    this.requestCancel();
  }

  fieldErrors(field: Exclude<TaskCreateField, 'form'>): readonly string[] {
    const messages: string[] = [];
    if (this.invalidSubmission || this.formControl(field)?.touched) {
      for (const error of this.localErrors()) {
        if (error.field === field && !messages.includes(error.message)) {
          messages.push(error.message);
        }
      }
    }
    for (const error of this.createState().fieldErrors) {
      if (error.field === field && !messages.includes(error.message)) {
        messages.push(error.message);
      }
    }
    return messages;
  }

  fieldInvalid(field: Exclude<TaskCreateField, 'form'>): boolean {
    return this.fieldErrors(field).length > 0;
  }

  focusField(field: TaskCreateField, event: Event): void {
    event.preventDefault();
    this.focusTaskCreateField(field);
  }

  focusQualityItem(itemId: TaskCreateQualityItemId): void {
    this.focusTaskCreateField(itemId);
  }

  private focusTaskCreateField(field: TaskCreateField): void {
    if (field === 'form') {
      this.errorSummary()?.nativeElement.focus();
      return;
    }
    const anchor = document.getElementById(this.fieldAnchorId(field));
    if (field === 'sourceScopeMode') {
      const radio = anchor?.querySelector<HTMLInputElement>(
        '#task-create-sourceScopeMode-inherit, #task-create-sourceScopeMode-override',
      );
      (radio ?? anchor)?.focus();
      return;
    }
    anchor?.focus();
  }

  fieldAnchorId(field: Exclude<TaskCreateField, 'form'>): string {
    switch (field) {
      case 'milestoneId':
        return 'task-create-milestone';
      default:
        return fieldElementId(field);
    }
  }

  policyLabel(enabled: boolean): string {
    return this.t(enabled ? 'Enabled' : 'Disabled');
  }

  private synchronizeAuthorizedOptions(projectId: string): void {
    const options = this.options;
    if (!options) {
      return;
    }
    if (this.initializedProjectId() !== projectId) {
      this.initializedProjectId.set(projectId);
      this.optionsChangedNotice.set(null);
      this.resetForm(options.projectScope.policy);
      this.focusAfterRender('title');
      return;
    }

    if (!this.canManageTaskScope && this.form.controls.sourceScopeMode.value === 'TaskOverride') {
      this.form.controls.sourceScopeMode.setValue('Inherit', { emitEvent: false });
    }
    if (this.form.controls.sourceScopeMode.value === 'Inherit') {
      this.form.controls.webEnabled.setValue(options.projectScope.policy.webEnabled, { emitEvent: false });
      this.form.controls.projectFilesEnabled.setValue(
        options.projectScope.policy.projectFilesEnabled,
        { emitEvent: false },
      );
    }

    const removedSelections: string[] = [];
    if (
      this.form.controls.primaryAssigneeUserId.value &&
      !options.assignees.some(
        (assignee) => assignee.userId === this.form.controls.primaryAssigneeUserId.value,
      )
    ) {
      this.form.controls.primaryAssigneeUserId.setValue('', { emitEvent: false });
      this.form.controls.primaryAssigneeUserId.markAsDirty();
      removedSelections.push('Primary assignee');
    }
    if (
      this.form.controls.milestoneId.value &&
      !options.milestones.some(
        (milestone) => milestone.id === this.form.controls.milestoneId.value,
      )
    ) {
      this.form.controls.milestoneId.setValue('', { emitEvent: false });
      this.form.controls.milestoneId.markAsDirty();
      removedSelections.push('Milestone');
    }
    if (removedSelections.length > 0) {
      this.optionsChangedNotice.set(
        `${removedSelections.join(' and ')} ${removedSelections.length === 1 ? 'is' : 'are'} no longer available and ${removedSelections.length === 1 ? 'was' : 'were'} cleared.`,
      );
      this.formRevision.update((value) => value + 1);
    }
  }

  private resetForRoute(): void {
    this.initializedProjectId.set(null);
    this.invalidSubmission = false;
    this.focusTitleAfterOptionsRecovery = false;
    this.discardConfirmationVisible.set(false);
    this.optionsChangedNotice.set(null);
    this.previousCreateStatus = 'idle';
    this.resetForm(null);
  }

  private resetForm(policy: { readonly webEnabled: boolean; readonly projectFilesEnabled: boolean } | null | undefined): void {
    this.form.reset({
      title: '',
      description: '',
      priority: 'medium',
      milestoneId: '',
      primaryAssigneeUserId: '',
      startDate: '',
      dueDate: '',
      goal: '',
      deliverable: '',
      constraints: '',
      sourceScopeMode: 'Inherit',
      webEnabled: policy?.webEnabled ?? false,
      projectFilesEnabled: policy?.projectFilesEnabled ?? false,
    }, { emitEvent: false });
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.formRevision.update((value) => value + 1);
  }

  private localErrors(): TaskCreateFieldError[] {
    const controls = this.form.controls;
    const errors: TaskCreateFieldError[] = [];
    if (controls.title.hasError('required') || controls.title.hasError('whitespace')) {
      errors.push({ field: 'title', message: this.t('Enter a Task title.') });
    }
    if (controls.title.hasError('maxlength')) {
      errors.push({ field: 'title', message: this.t('Task title must be 240 characters or fewer.') });
    }
    if (controls.description.hasError('maxlength')) {
      errors.push({ field: 'description', message: this.t('Description must be 8,000 characters or fewer.') });
    }
    for (const [field, control, label] of [
      ['goal', controls.goal, 'Goal'],
      ['deliverable', controls.deliverable, 'Deliverable'],
      ['constraints', controls.constraints, 'Constraints'],
    ] as const) {
      if (control.hasError('maxlength')) {
        errors.push({
          field,
          message: this.t('{{field}} must be {{count}} characters or fewer.', {field: this.t(label as ProjectUiCopy), count: this.i18n.formatNumber(TASK_BRIEF_FIELD_MAX_LENGTH)}),
        });
      }
    }
    if (
      controls.milestoneId.value &&
      !this.options?.milestones.some((milestone) => milestone.id === controls.milestoneId.value)
    ) {
      errors.push({ field: 'milestoneId', message: this.t('Choose a Milestone available to this Task.') });
    }
    if (
      controls.primaryAssigneeUserId.value &&
      !this.options?.assignees.some((assignee) => assignee.userId === controls.primaryAssigneeUserId.value)
    ) {
      errors.push({
        field: 'primaryAssigneeUserId',
        message: this.t('Choose an assignee available to this Project.'),
      });
    }
    if (controls.startDate.value && !isIsoDate(controls.startDate.value)) {
      errors.push({ field: 'startDate', message: this.t('Enter a valid start date.') });
    }
    if (controls.dueDate.value && !isIsoDate(controls.dueDate.value)) {
      errors.push({ field: 'dueDate', message: this.t('Enter a valid due date.') });
    }
    if (
      controls.startDate.value &&
      controls.dueDate.value &&
      isIsoDate(controls.startDate.value) &&
      isIsoDate(controls.dueDate.value) &&
      controls.dueDate.value < controls.startDate.value
    ) {
      errors.push({ field: 'dueDate', message: this.t('Due date cannot be before the start date.') });
    }
    if (controls.sourceScopeMode.value === 'TaskOverride' && !this.canManageTaskScope) {
      errors.push({
        field: 'sourceScopeMode',
        message: this.t('A Task-specific source policy is not available for the current Project authority.'),
      });
    }
    return errors;
  }

  private formControl(field: Exclude<TaskCreateField, 'form'>): FormControl | null {
    switch (field) {
      case 'title':
      case 'description':
      case 'priority':
      case 'milestoneId':
      case 'startDate':
      case 'dueDate':
      case 'goal':
      case 'deliverable':
      case 'constraints':
      case 'primaryAssigneeUserId':
      case 'sourceScopeMode':
        return this.form.controls[field] as FormControl;
    }
  }

  private focusAfterRender(target: 'summary' | 'title' | 'optionsRetry'): void {
    afterNextRender(
      {
        write: () => {
          switch (target) {
            case 'summary':
              (this.errorSummary()?.nativeElement ?? this.optionsRetry()?.nativeElement)?.focus();
              return;
            case 'optionsRetry':
              (this.optionsRetry()?.nativeElement ?? this.errorSummary()?.nativeElement)?.focus();
              return;
            case 'title':
              this.titleInput()?.nativeElement.focus();
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

type TaskCreateQualityItemId = 'goal' | 'deliverable' | 'constraints' | 'sourceScopeMode';

interface TaskCreateQualityItem {
  readonly id: TaskCreateQualityItemId;
  readonly label: string;
  readonly status: 'complete' | 'missing';
  readonly detail: string;
  readonly actionLabel: string | null;
}

function briefQualityItem(
  id: Exclude<TaskCreateQualityItemId, 'sourceScopeMode'>,
  label: string,
  value: string,
  t: (text: ProjectUiCopy, parameters?: Readonly<Record<string, string | number>>) => string,
): TaskCreateQualityItem {
  const includedDetail = id === 'constraints' ? t('Constraints are included in this Task Brief.') : t('{{field}} is included in this Task Brief.', {field: label});
  return value.trim().length > 0
    ? {
        id,
        label,
        status: 'complete',
        detail: includedDetail,
        actionLabel: null,
      }
    : {
        id,
        label,
        status: 'missing',
        detail: t('Add {{field}} to make the Task easier to review before creation.', {field: label}),
        actionLabel: t('Add {{field}}', {field: label}),
      };
}

function fieldElementId(field: Exclude<TaskCreateField, 'form'>): string {
  return `task-create-${field}`;
}

function isIsoDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/u.test(value)) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}
