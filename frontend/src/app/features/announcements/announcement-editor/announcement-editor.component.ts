import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  computed,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { Subscription } from 'rxjs';

import { AnnouncementLocalPreviewComponent } from '../announcement-local-preview/announcement-local-preview.component';
import { AnnouncementPublicationStatusComponent } from '../announcement-publication-status/announcement-publication-status.component';
import {
  ANNOUNCEMENT_PRIORITY_LABELS,
  AnnouncementActionLink,
  AnnouncementAudienceOption,
  AnnouncementEditorDraft,
  AnnouncementEditorSubmission,
  AnnouncementDeliveryMode,
  AnnouncementLocalPreview,
  AnnouncementPriority,
  AnnouncementPublicationState,
} from '../announcements.types';
import { isSafeAnnouncementUrl } from '../announcements.api';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-announcement-editor',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AnnouncementPublicationStatusComponent,
    AnnouncementLocalPreviewComponent,
    CoglatasDialogComponent,
  ],
  templateUrl: './announcement-editor.component.html',
  styleUrl: './announcement-editor.component.scss',
})
export class AnnouncementEditorComponent implements OnChanges, OnInit, OnDestroy {
  @ViewChild('titleInput') private titleInput?: ElementRef<HTMLInputElement>;
  @ViewChild('bodyInput') private bodyInput?: ElementRef<HTMLTextAreaElement>;
  @ViewChild('priorityInput') private priorityInput?: ElementRef<HTMLSelectElement>;
  @ViewChild('audienceInput') private audienceInput?: ElementRef<HTMLSelectElement>;
  @ViewChild('scheduleInput') private scheduleInput?: ElementRef<HTMLInputElement>;
  @ViewChild('ctaLabelInput') private ctaLabelInput?: ElementRef<HTMLInputElement>;
  @ViewChild('ctaUrlInput') private ctaUrlInput?: ElementRef<HTMLInputElement>;
  @ViewChild('attachmentLabelInput') private attachmentLabelInput?: ElementRef<HTMLInputElement>;
  @ViewChild('attachmentUrlInput') private attachmentUrlInput?: ElementRef<HTMLInputElement>;
  @ViewChild(AnnouncementLocalPreviewComponent)
  private previewComponent?: AnnouncementLocalPreviewComponent;
  @ViewChild('reviewStepHeading') private reviewStepHeading?: ElementRef<HTMLElement>;

  @Input({ required: true }) draft!: AnnouncementEditorDraft;
  @Input() submissionError: string | undefined;
  @Input() publishing = false;
  @Output() readonly draftChanged = new EventEmitter<AnnouncementEditorDraft>();
  @Output() readonly saveDraftRequested = new EventEmitter<AnnouncementEditorSubmission>();
  @Output() readonly publishRequested = new EventEmitter<AnnouncementEditorSubmission>();

  readonly priorityOptions: readonly AnnouncementPriority[] = ['normal', 'important', 'critical'];
  readonly priorityLabels = ANNOUNCEMENT_PRIORITY_LABELS;
  readonly availableAudiences = signal<readonly AnnouncementAudienceOption[]>([]);
  readonly currentStep = signal<AnnouncementEditorStep>('content');
  readonly previewOpen = signal(false);
  readonly publicationReviewOpen = signal(false);
  readonly publicationConfirming = signal(false);
  readonly publicationReview = signal<AnnouncementEditorSubmission | null>(null);
  readonly ctaLabel = signal('');
  readonly ctaUrl = signal('');
  readonly attachmentLabel = signal('');
  readonly attachmentUrl = signal('');
  readonly editorSteps: readonly AnnouncementEditorStepDefinition[] = [
    { id: 'content', label: 'Content' },
    { id: 'audience', label: 'Audience' },
    { id: 'delivery', label: 'Delivery' },
    { id: 'review', label: 'Review' },
  ];
  private readonly previewRevision = signal(0);
  private readonly touchedLinkFields = new Set<AnnouncementEditorLinkField>();
  private submissionAttempted = false;
  private formInitialized = false;
  private contentLinksDirty = false;
  private focusRequestVersion = 0;
  private formChanges?: Subscription;
  private deliveryModeChanges?: Subscription;
  private audienceChanges?: Subscription;

  readonly form = new FormBuilder().nonNullable.group({
    title: [
      '',
      [
        Validators.required,
        nonWhitespaceValidator,
        Validators.maxLength(announcementTitleMaximumLength),
      ],
    ],
    body: [
      '',
      [
        Validators.required,
        nonWhitespaceValidator,
        Validators.maxLength(announcementBodyMaximumLength),
      ],
    ],
    priority: ['normal' as AnnouncementPriority, Validators.required],
    audienceKey: ['', Validators.required],
    requiresReadConfirmation: [false],
    deliveryMode: ['now' as AnnouncementDeliveryMode, Validators.required],
    scheduledLocalDateTime: [''],
    timeZoneId: ['UTC'],
  });

  /**
   * This is intentionally a local rendering model, not a DTO or an alternate
   * publication command. The audience is resolved from the current
   * server-authorized options on every change, so a revoked display name/count
   * cannot survive an audience refresh.
   */
  readonly preview = computed<AnnouncementLocalPreview | null>(() => {
    this.previewRevision();
    const audience = this.selectedAudience();
    if (audience === null) {
      return null;
    }

    const value = this.form.getRawValue();
    const cta = this.optionalLink('cta');
    const attachment = this.optionalLink('attachment');
    return {
      title: value.title.trim(),
      body: value.body.trim(),
      priority: value.priority,
      audience,
      requiresReadConfirmation: value.requiresReadConfirmation,
      ...(cta ? { cta } : {}),
      ...(attachment ? { attachment } : {}),
    };
  });

  get publicationState(): AnnouncementPublicationState {
    return this.draft.publicationState ?? 'draft';
  }

  get canPublish(): boolean {
    return this.publicationState === 'draft';
  }

  get canSaveDraft(): boolean {
    return this.publicationState === 'draft';
  }

  get summaryErrors(): readonly AnnouncementEditorFieldError[] {
    if (!this.submissionAttempted) {
      return [];
    }

    const formErrors: AnnouncementEditorFieldError[] = announcementEditorStepFields[this.currentStep()].flatMap(
      (field) => {
        const message = this.fieldError(field);
        return message ? [{ field, message }] : [];
      },
    );
    const linkErrors: AnnouncementEditorFieldError[] = (this.currentStep() === 'content'
      ? announcementEditorLinkFields
      : []).flatMap(
      (field) => {
        const message = this.linkFieldError(field);
        return message ? [{ field, message }] : [];
      },
    );
    return [...formErrors, ...linkErrors];
  }

  ngOnInit(): void {
    this.deliveryModeChanges = this.form.controls.deliveryMode.valueChanges.subscribe(() =>
      this.updateScheduleValidators(),
    );
    this.formChanges = this.form.valueChanges.subscribe(() => this.emitDraftChange());
    this.audienceChanges = this.form.controls.audienceKey.valueChanges.subscribe(() => this.applyAudienceTimeZone());
    this.updateScheduleValidators();
  }

  ngOnDestroy(): void {
    this.formChanges?.unsubscribe();
    this.deliveryModeChanges?.unsubscribe();
    this.audienceChanges?.unsubscribe();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const submissionErrorChange = changes['submissionError'];
    const publishingChange = changes['publishing'];
    if (
      submissionErrorChange?.currentValue ||
      (publishingChange?.previousValue === true && publishingChange.currentValue === false)
    ) {
      // A failed authoritative request leaves the draft editable. Return from the
      // busy confirmation state before the inline, preserved-draft error renders.
      this.publicationConfirming.set(false);
      this.publicationReviewOpen.set(false);
      this.publicationReview.set(null);
    }

    if (!changes['draft'] || !this.draft) {
      return;
    }

    // A durable schedule or publication is no longer an editable creation
    // flow. Start its retained read-only editor on the same review summary,
    // preserving the prior disabled-action behavior without exposing a
    // misleading editable Content step.
    if (this.publicationState !== 'draft') {
      this.currentStep.set('review');
    }

    this.availableAudiences.set(this.draft.availableAudiences);
    this.syncEditability();
    const currentAudienceKey = this.form.controls.audienceKey.value;
    const preferredAudienceKey =
      this.formInitialized && (this.form.dirty || this.contentLinksDirty)
        ? currentAudienceKey
        : this.draft.audienceKey;
    const authorizedAudienceKey = this.authorizedAudienceKey(preferredAudienceKey);

    if (this.formInitialized && (this.form.dirty || this.contentLinksDirty)) {
      if (currentAudienceKey !== authorizedAudienceKey) {
        this.form.controls.audienceKey.setValue(authorizedAudienceKey, { emitEvent: false });
        this.applyAudienceTimeZone();
        this.previewRevision.update((revision) => revision + 1);
        // The editor received a new authoritative audience projection. Do not
        // keep an already-open view that might have displayed the revoked
        // audience's name or recipient estimate.
        if (this.previewOpen()) {
          this.closePreview();
        }
      }
      return;
    }

    this.form.reset(
      {
        title: this.draft.title,
        body: this.draft.body,
        priority: this.draft.priority,
        audienceKey: authorizedAudienceKey,
        requiresReadConfirmation: this.draft.requiresReadConfirmation,
        deliveryMode: this.draft.deliveryMode ?? 'now',
        scheduledLocalDateTime: this.draft.scheduledLocalDateTime ?? '',
        timeZoneId:
          this.draft.timeZoneId ??
          this.draft.availableAudiences.find((audience) => audience.key === authorizedAudienceKey)
            ?.scheduleTimeZoneId ??
          'UTC',
      },
      { emitEvent: false },
    );
    this.ctaLabel.set(this.draft.cta?.label ?? '');
    this.ctaUrl.set(this.draft.cta?.url ?? '');
    this.attachmentLabel.set(this.draft.attachment?.label ?? '');
    this.attachmentUrl.set(this.draft.attachment?.url ?? '');
    this.submissionAttempted = false;
    this.formInitialized = true;
    this.contentLinksDirty = false;
    this.touchedLinkFields.clear();
    this.updateScheduleValidators();
    this.previewRevision.update((revision) => revision + 1);

    if (this.previewOpen() && currentAudienceKey !== authorizedAudienceKey) {
      this.closePreview();
    }
  }

  selectedAudience(): AnnouncementAudienceOption | null {
    const selectedKey = this.form.controls.audienceKey.value;
    return this.availableAudiences().find((audience) => audience.key === selectedKey) ?? null;
  }

  stepNumber(step: AnnouncementEditorStep): number {
    return this.editorSteps.findIndex((candidate) => candidate.id === step) + 1;
  }

  currentStepLabel(): string {
    return this.editorSteps.find((step) => step.id === this.currentStep())?.label ?? 'Content';
  }

  canNavigateToStep(step: AnnouncementEditorStep): boolean {
    return this.stepNumber(step) <= this.stepNumber(this.currentStep()) && !this.publishing;
  }

  goToStep(step: AnnouncementEditorStep): void {
    if (!this.canNavigateToStep(step)) {
      return;
    }

    this.previewOpen.set(false);
    this.currentStep.set(step);
    this.scheduleStepFocus(step);
  }

  previousStep(): void {
    const currentIndex = this.stepNumber(this.currentStep()) - 1;
    const previous = this.editorSteps[currentIndex - 1];
    if (previous) {
      this.goToStep(previous.id);
    }
  }

  nextStep(): void {
    const step = this.currentStep();
    this.submissionAttempted = true;
    this.markStepTouched(step);
    if (!this.stepIsValid(step)) {
      this.focusFirstInvalidStepControl(step);
      return;
    }

    const next = this.editorSteps[this.stepNumber(step)];
    if (next) {
      this.currentStep.set(next.id);
      this.scheduleStepFocus(next.id);
    }
  }

  submitCurrentStep(): void {
    if (this.currentStep() === 'review') {
      this.publish();
      return;
    }

    this.nextStep();
  }

  audienceOptionLabel(audience: AnnouncementAudienceOption): string {
    return audience.recipientCount === undefined
      ? audience.displayName
      : `${audience.displayName} — ${audience.recipientCount.toLocaleString('ja-JP')}名`;
  }

  fieldError(field: AnnouncementEditorFormField): string | null {
    const control = this.form.controls[field];
    if (!control.invalid || !control.touched) {
      return null;
    }

    if (field === 'scheduledLocalDateTime') {
      return 'Choose a local date and time before scheduling.';
    }

    if (field === 'title') {
      if (control.hasError('required') || control.hasError('whitespace')) {
        return 'タイトルを入力してください。空白だけでは公開できません。';
      }
      if (control.hasError('maxlength')) {
        return 'タイトルは200文字以内で入力してください。';
      }
    }

    if (field === 'body') {
      if (control.hasError('required') || control.hasError('whitespace')) {
        return '本文を入力してください。空白だけでは公開できません。';
      }
      if (control.hasError('maxlength')) {
        return '本文は20,000文字以内で入力してください。';
      }
    }

    if (field === 'priority') {
      return '優先度を選択してください。';
    }

    return '配信対象を選択してください。権限のある対象のみ公開できます。';
  }

  linkFieldError(field: AnnouncementEditorLinkField): string | null {
    if (!this.submissionAttempted && !this.touchedLinkFields.has(field)) {
      return null;
    }

    const isCta = field.startsWith('cta');
    const isLabel = field.endsWith('Label');
    const label = (isCta ? this.ctaLabel() : this.attachmentLabel()).trim();
    const url = (isCta ? this.ctaUrl() : this.attachmentUrl()).trim();
    const subject = isCta ? 'CTA' : 'リンク添付';

    if (isLabel) {
      if (url && !label) {
        return `${subject}の表示名を入力してください。`;
      }
      if (label.length > announcementLinkLabelMaximumLength) {
        return `${subject}の表示名は${announcementLinkLabelMaximumLength}文字以内で入力してください。`;
      }
      return null;
    }

    if (label && !url) {
      return `${subject}のURLを入力してください。`;
    }
    if (url.length > announcementLinkUrlMaximumLength) {
      return `${subject}のURLは${announcementLinkUrlMaximumLength.toLocaleString('ja-JP')}文字以内で入力してください。`;
    }
    if (url && !isSafeAnnouncementUrl(url)) {
      return `${subject}のURLには / から始まるアプリ内パス、または安全なHTTPS URLを指定してください。`;
    }
    return null;
  }

  fieldDescribedBy(field: AnnouncementEditorFormField, helpId: string): string {
    const errorId = this.fieldError(field)
      ? `announcement-${announcementEditorFieldDomId(field)}-error`
      : null;
    return [helpId, errorId].filter((id): id is string => id !== null).join(' ');
  }

  linkFieldDescribedBy(field: AnnouncementEditorLinkField, helpId: string): string {
    const errorId = this.linkFieldError(field)
      ? `announcement-${announcementLinkFieldDomId(field)}-error`
      : null;
    return [helpId, errorId].filter((id): id is string => id !== null).join(' ');
  }

  focusField(field: AnnouncementEditorField, event: Event): void {
    event.preventDefault();
    this.focusControl(field);
  }

  touchContentLink(field: AnnouncementEditorLinkField): void {
    this.touchedLinkFields.add(field);
  }

  updateContentLink(field: AnnouncementEditorLinkField, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    switch (field) {
      case 'ctaLabel':
        this.ctaLabel.set(value);
        break;
      case 'ctaUrl':
        this.ctaUrl.set(value);
        break;
      case 'attachmentLabel':
        this.attachmentLabel.set(value);
        break;
      case 'attachmentUrl':
        this.attachmentUrl.set(value);
        break;
    }
    this.contentLinksDirty = true;
    this.emitDraftChange();
  }

  openPreview(): void {
    if (this.preview() === null) {
      this.focusControl('audienceKey');
      return;
    }

    this.previewOpen.set(true);
    // The preview child is created by this state change. Wait for Angular to
    // attach that view before asking its heading to receive keyboard focus.
    setTimeout(() => {
      if (this.previewOpen()) {
        this.previewComponent?.focusHeading();
      }
    });
  }

  closePreview(restoreFocus = true): void {
    this.previewOpen.set(false);
    if (restoreFocus) {
      setTimeout(() => {
        if (!this.previewOpen()) {
          this.focusStep(this.currentStep());
        }
      });
    }
  }

  publish(): void {
    this.submissionAttempted = true;
    this.form.markAllAsTouched();
    this.markContentLinksTouched();
    const audience = this.selectedAudience();
    if (!this.canPublish || this.form.invalid || !this.contentLinksAreValid() || audience === null) {
      this.focusFirstInvalidControl(audience === null ? 'audienceKey' : undefined);
      return;
    }

    const submission = this.createSubmission(true);
    if (submission === null) {
      return;
    }

    this.publicationReview.set(submission);
    this.publicationReviewOpen.set(true);
  }

  saveDraft(): void {
    this.submissionAttempted = true;
    this.form.markAllAsTouched();
    this.markContentLinksTouched();
    const submission = this.createSubmission(false);
    if (submission === null || !this.canSaveDraft || this.publishing) {
      return;
    }

    this.saveDraftRequested.emit(submission);
  }

  cancelPublicationReview(): void {
    if (this.publicationConfirming() || this.publishing) {
      return;
    }

    this.publicationReviewOpen.set(false);
    this.publicationReview.set(null);
  }

  confirmPublication(): void {
    const submission = this.publicationReview();
    if (!submission || this.publicationConfirming() || this.publishing) {
      return;
    }

    this.publicationConfirming.set(true);
    this.publishRequested.emit(submission);
  }

  publicationTimingLabel(): string {
    const review = this.publicationReview();
    const mode = review?.deliveryMode ?? this.form.controls.deliveryMode.value;
    if (mode !== 'scheduled') {
      return 'Publish immediately';
    }

    const localDateTime = review?.scheduledLocalDateTime ?? this.form.controls.scheduledLocalDateTime.value;
    const timeZoneId = review?.timeZoneId ?? this.form.controls.timeZoneId.value;
    return localDateTime && timeZoneId
      ? `Schedule for ${localDateTime} ${timeZoneId}`
      : 'Scheduled publication requires a local date, time, and IANA time zone';
  }

  confirmationLabel(): string {
    const recipientCount = this.publicationReview()?.audience.recipientCount;
    const isScheduled = this.publicationReview()?.deliveryMode === 'scheduled';
    if (recipientCount === undefined) {
      return isScheduled ? 'Schedule publication' : 'Publish now';
    }

    return isScheduled
      ? `Schedule for ${recipientCount.toLocaleString('ja-JP')} recipients`
      : `Publish to ${recipientCount.toLocaleString('ja-JP')} recipients now`;
  }

  private authorizedAudienceKey(preferredAudienceKey: string): string {
    return (
      this.draft.availableAudiences.find((audience) => audience.key === preferredAudienceKey)
        ?.key ??
      this.draft.availableAudiences[0]?.key ??
      ''
    );
  }

  private markStepTouched(step: AnnouncementEditorStep): void {
    announcementEditorStepFields[step].forEach((field) => this.form.controls[field].markAsTouched());
    if (step === 'content') {
      this.markContentLinksTouched();
    }
    if (step === 'delivery' && this.form.controls.deliveryMode.value === 'scheduled') {
      this.form.controls.timeZoneId.markAsTouched();
    }
  }

  private stepIsValid(step: AnnouncementEditorStep): boolean {
    switch (step) {
      case 'content':
        return !this.form.controls.title.invalid &&
          !this.form.controls.body.invalid &&
          this.contentLinksAreValid();
      case 'audience':
        return !this.form.controls.audienceKey.invalid && this.selectedAudience() !== null;
      case 'delivery':
        return !this.form.controls.priority.invalid &&
          (this.form.controls.deliveryMode.value !== 'scheduled' ||
            (!this.form.controls.scheduledLocalDateTime.invalid && !this.form.controls.timeZoneId.invalid));
      case 'review':
        return true;
    }
  }

  private applyAudienceTimeZone(): void {
    const zone = this.selectedAudience()?.scheduleTimeZoneId ?? 'UTC';
    this.form.controls.timeZoneId.setValue(zone, { emitEvent: false });
    this.emitDraftChange();
  }

  private emitDraftChange(): void {
    this.previewRevision.update((revision) => revision + 1);
    const value = this.form.getRawValue();
    const cta = this.optionalLink('cta');
    const attachment = this.optionalLink('attachment');
    this.draftChanged.emit({
      id: this.draft.id,
      version: this.draft.version,
      createIdempotencyKey: this.draft.createIdempotencyKey,
      transitionIdempotencyKey: this.draft.transitionIdempotencyKey,
      title: value.title,
      body: value.body,
      priority: value.priority,
      audienceKey: value.audienceKey,
      availableAudiences: this.availableAudiences(),
      requiresReadConfirmation: value.requiresReadConfirmation,
      ...(cta ? { cta } : {}),
      ...(attachment ? { attachment } : {}),
      deliveryMode: value.deliveryMode,
      scheduledLocalDateTime: value.scheduledLocalDateTime,
      timeZoneId: value.timeZoneId,
      publicationState: this.draft.publicationState,
      scheduledAtLabel: this.draft.scheduledAtLabel,
      timeZoneLabel: this.draft.timeZoneLabel,
    });
  }

  private focusFirstInvalidControl(fallback?: AnnouncementEditorField): void {
    const invalidFormField = announcementEditorFormFields.find(
      (field) => this.form.controls[field].invalid,
    );
    const invalidLinkField = announcementEditorLinkFields.find(
      (field) => this.linkFieldError(field) !== null,
    );
    const invalidField = invalidFormField ?? invalidLinkField ?? fallback;
    if (!invalidField) {
      return;
    }

    setTimeout(() => this.focusControl(invalidField));
  }

  private focusFirstInvalidStepControl(step: AnnouncementEditorStep): void {
    const invalidFormField = announcementEditorStepFields[step].find(
      (field) => this.form.controls[field].invalid,
    );
    const invalidLinkField = step === 'content'
      ? announcementEditorLinkFields.find((field) => this.linkFieldError(field) !== null)
      : undefined;
    const invalidField = invalidFormField ?? invalidLinkField ??
      (step === 'audience' ? 'audienceKey' : step === 'delivery' ? 'scheduledLocalDateTime' : 'title');
    setTimeout(() => this.focusControl(invalidField));
  }

  private focusControl(field: AnnouncementEditorField): void {
    const targetStep = announcementEditorFieldSteps[field];
    if (!this.previewOpen() && this.currentStep() !== targetStep) {
      this.currentStep.set(targetStep);
      this.scheduleFieldFocus(field);
      return;
    }

    switch (field) {
      case 'title':
        this.titleInput?.nativeElement.focus();
        break;
      case 'body':
        this.bodyInput?.nativeElement.focus();
        break;
      case 'priority':
        this.priorityInput?.nativeElement.focus();
        break;
      case 'audienceKey':
        this.audienceInput?.nativeElement.focus();
        break;
      case 'scheduledLocalDateTime':
        this.scheduleInput?.nativeElement.focus();
        break;
      case 'ctaLabel':
        this.ctaLabelInput?.nativeElement.focus();
        break;
      case 'ctaUrl':
        this.ctaUrlInput?.nativeElement.focus();
        break;
      case 'attachmentLabel':
        this.attachmentLabelInput?.nativeElement.focus();
        break;
      case 'attachmentUrl':
        this.attachmentUrlInput?.nativeElement.focus();
        break;
    }
  }

  private focusStep(step: AnnouncementEditorStep): void {
    switch (step) {
      case 'content':
        this.focusControl('title');
        break;
      case 'audience':
        this.focusControl('audienceKey');
        break;
      case 'delivery':
        this.focusControl('priority');
        break;
      case 'review':
        this.reviewStepHeading?.nativeElement.focus();
        break;
    }
  }

  private scheduleStepFocus(step: AnnouncementEditorStep): void {
    const requestVersion = ++this.focusRequestVersion;
    setTimeout(() => {
      if (
        requestVersion === this.focusRequestVersion &&
        !this.previewOpen() &&
        this.currentStep() === step
      ) {
        this.focusStep(step);
      }
    });
  }

  private scheduleFieldFocus(field: AnnouncementEditorField): void {
    const requestVersion = ++this.focusRequestVersion;
    const targetStep = announcementEditorFieldSteps[field];
    setTimeout(() => {
      if (
        requestVersion === this.focusRequestVersion &&
        !this.previewOpen() &&
        this.currentStep() === targetStep
      ) {
        this.focusControl(field);
      }
    });
  }

  private createSubmission(requireSchedule: boolean): AnnouncementEditorSubmission | null {
    const audience = this.selectedAudience();
    const value = this.form.getRawValue();
    const title = value.title.trim();
    const body = value.body.trim();
    if (!title || !body || audience === null || !this.contentLinksAreValid()) {
      this.focusFirstInvalidControl(!title ? 'title' : !body ? 'body' : audience === null ? 'audienceKey' : undefined);
      return null;
    }

    const deliveryMode = value.deliveryMode;
    const scheduledLocalDateTime = value.scheduledLocalDateTime.trim();
    const timeZoneId = value.timeZoneId.trim();
    if (requireSchedule && deliveryMode === 'scheduled' && (!scheduledLocalDateTime || !timeZoneId)) {
      this.form.controls.scheduledLocalDateTime.markAsTouched();
      this.form.controls.timeZoneId.markAsTouched();
      this.focusFirstInvalidControl('scheduledLocalDateTime');
      return null;
    }

    const cta = this.optionalLink('cta');
    const attachment = this.optionalLink('attachment');
    return {
      ...(this.draft.id ? { draftId: this.draft.id } : {}),
      ...(this.draft.version !== undefined ? { draftVersion: this.draft.version } : {}),
      ...(this.draft.createIdempotencyKey
        ? { createIdempotencyKey: this.draft.createIdempotencyKey }
        : {}),
      ...(this.draft.transitionIdempotencyKey
        ? { transitionIdempotencyKey: this.draft.transitionIdempotencyKey }
        : {}),
      title,
      body,
      priority: value.priority,
      audience,
      requiresReadConfirmation: value.requiresReadConfirmation,
      ...(cta ? { cta } : {}),
      ...(attachment ? { attachment } : {}),
      deliveryMode,
      ...(deliveryMode === 'scheduled'
        ? { scheduledLocalDateTime, timeZoneId }
        : {}),
    };
  }

  private optionalLink(kind: 'cta' | 'attachment'): AnnouncementActionLink | null {
    const label = (kind === 'cta' ? this.ctaLabel() : this.attachmentLabel()).trim();
    const url = (kind === 'cta' ? this.ctaUrl() : this.attachmentUrl()).trim();
    if (!label && !url) {
      return null;
    }
    if (
      !label ||
      label.length > announcementLinkLabelMaximumLength ||
      !url ||
      url.length > announcementLinkUrlMaximumLength ||
      !isSafeAnnouncementUrl(url)
    ) {
      return null;
    }
    return { label, url };
  }

  private contentLinksAreValid(): boolean {
    return this.linkPairIsValid(this.ctaLabel(), this.ctaUrl()) &&
      this.linkPairIsValid(this.attachmentLabel(), this.attachmentUrl());
  }

  private linkPairIsValid(rawLabel: string, rawUrl: string): boolean {
    const label = rawLabel.trim();
    const url = rawUrl.trim();
    if (!label && !url) {
      return true;
    }
    return Boolean(
      label &&
      label.length <= announcementLinkLabelMaximumLength &&
      url &&
      url.length <= announcementLinkUrlMaximumLength &&
      isSafeAnnouncementUrl(url),
    );
  }

  private markContentLinksTouched(): void {
    announcementEditorLinkFields.forEach((field) => this.touchedLinkFields.add(field));
  }

  private updateScheduleValidators(): void {
    const scheduled = this.form.controls.deliveryMode.value === 'scheduled';
    this.form.controls.scheduledLocalDateTime.setValidators(scheduled ? [Validators.required] : []);
    this.form.controls.timeZoneId.setValidators(scheduled ? [Validators.required, ianaTimeZoneValidator] : []);
    this.form.controls.scheduledLocalDateTime.updateValueAndValidity({ emitEvent: false });
    this.form.controls.timeZoneId.updateValueAndValidity({ emitEvent: false });
  }

  /**
   * Once the server has accepted a delivery request, the retained draft is a
   * durable schedule rather than editable browser state. It stays visible so
   * the author can see its accepted timing, but no local edit can race the
   * worker's immutable Scheduled -> Published transition.
   */
  private syncEditability(): void {
    if (this.draft.publicationState === 'scheduled' || this.draft.publicationState === 'published') {
      this.form.disable({ emitEvent: false });
      return;
    }

    this.form.enable({ emitEvent: false });
  }
}

type AnnouncementEditorFormField = 'title' | 'body' | 'priority' | 'audienceKey' | 'scheduledLocalDateTime';
type AnnouncementEditorLinkField = 'ctaLabel' | 'ctaUrl' | 'attachmentLabel' | 'attachmentUrl';
type AnnouncementEditorField = AnnouncementEditorFormField | AnnouncementEditorLinkField;
type AnnouncementEditorStep = 'content' | 'audience' | 'delivery' | 'review';

interface AnnouncementEditorStepDefinition {
  readonly id: AnnouncementEditorStep;
  readonly label: string;
}

interface AnnouncementEditorFieldError {
  readonly field: AnnouncementEditorField;
  readonly message: string;
}

const announcementEditorFormFields: readonly AnnouncementEditorFormField[] = [
  'title',
  'body',
  'audienceKey',
  'priority',
  'scheduledLocalDateTime',
];
const announcementEditorStepFields: Readonly<Record<AnnouncementEditorStep, readonly AnnouncementEditorFormField[]>> = {
  content: ['title', 'body'],
  audience: ['audienceKey'],
  delivery: ['priority', 'scheduledLocalDateTime'],
  review: [],
};
const announcementEditorLinkFields: readonly AnnouncementEditorLinkField[] = [
  'ctaLabel',
  'ctaUrl',
  'attachmentLabel',
  'attachmentUrl',
];
const announcementEditorFieldSteps: Readonly<Record<AnnouncementEditorField, AnnouncementEditorStep>> = {
  title: 'content',
  body: 'content',
  ctaLabel: 'content',
  ctaUrl: 'content',
  attachmentLabel: 'content',
  attachmentUrl: 'content',
  audienceKey: 'audience',
  priority: 'delivery',
  scheduledLocalDateTime: 'delivery',
};

const announcementTitleMaximumLength = 200;
const announcementBodyMaximumLength = 20_000;
const announcementLinkLabelMaximumLength = 120;
const announcementLinkUrlMaximumLength = 2_048;

const announcementEditorFieldDomId = (field: AnnouncementEditorFormField): string =>
  field === 'audienceKey' ? 'audience' : field === 'scheduledLocalDateTime' ? 'schedule-local-time' : field;

const announcementLinkFieldDomId = (field: AnnouncementEditorLinkField): string =>
  field === 'ctaLabel'
    ? 'cta-label'
    : field === 'ctaUrl'
      ? 'cta-url'
      : field === 'attachmentLabel'
        ? 'attachment-label'
        : 'attachment-url';

const nonWhitespaceValidator: ValidatorFn = (
  control: AbstractControl<string>,
): ValidationErrors | null => (control.value.trim().length > 0 ? null : { whitespace: true });

const ianaTimeZoneValidator: ValidatorFn = (
  control: AbstractControl<string>,
): ValidationErrors | null => {
  const value = control.value.trim();
  return value === 'UTC' || value.includes('/') ? null : { ianaTimeZone: true };
};
