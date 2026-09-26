import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  computed,
  signal,
} from '@angular/core';

import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import { AnnouncementEditorComponent } from '../announcement-editor/announcement-editor.component';
import {
  AnnouncementAudienceOption,
  AnnouncementEditorDraft,
  AnnouncementEditorSubmission,
} from '../announcements.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-announcement-multi-audience-editor',
  standalone: true,
  imports: [AnnouncementEditorComponent, CoglatasDialogComponent],
  templateUrl: './announcement-multi-audience-editor.component.html',
  styleUrl: './announcement-multi-audience-editor.component.scss',
})
export class AnnouncementMultiAudienceEditorComponent implements OnChanges {
  @Input({ required: true }) draft!: AnnouncementEditorDraft;
  @Input() submissionError: string | undefined;
  @Input() publishing = false;

  @Output() readonly draftChanged = new EventEmitter<AnnouncementEditorDraft>();
  @Output() readonly saveDraftRequested = new EventEmitter<AnnouncementEditorSubmission>();
  @Output() readonly publishRequested = new EventEmitter<AnnouncementEditorSubmission>();

  readonly innerDraft = signal<AnnouncementEditorDraft | null>(null);
  readonly selectedKeys = signal<readonly string[]>([]);
  readonly localError = signal<string | undefined>(undefined);
  readonly finalReviewOpen = signal(false);
  readonly pendingPublication = signal<AnnouncementEditorSubmission | null>(null);

  readonly selectedAudiences = computed<readonly AnnouncementAudienceOption[]>(() => {
    const draft = this.innerDraft();
    if (!draft) {return [];}
    return this.selectedKeys()
      .map((key) => draft.availableAudiences.find((audience) => audience.key === key))
      .filter((audience): audience is AnnouncementAudienceOption => audience !== undefined);
  });

  readonly selectableAudiences = computed<readonly AnnouncementAudienceOption[]>(() =>
    (this.innerDraft()?.availableAudiences ?? []).filter(
      (audience) => audience.scope === 'group' || audience.scope === 'channel',
    ),
  );

  readonly recipientUpperBound = computed<number | undefined>(() => {
    const audiences = this.selectedAudiences();
    if (audiences.some((audience) => audience.recipientCount === undefined)) {
      return undefined;
    }
    return audiences.reduce((total, audience) => total + (audience.recipientCount ?? 0), 0);
  });

  readonly isEditable = computed(() => (this.innerDraft()?.publicationState ?? 'draft') === 'draft');
  readonly canAddMore = computed(() => this.selectedKeys().length < maximumAudienceTargets);

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['draft'] || !this.draft) {
      return;
    }

    const authorizedKeys = new Set(this.draft.availableAudiences.map((audience) => audience.key));
    const current = this.selectedKeys();
    const explicitIncoming = this.draft.audienceKeys?.length ? this.draft.audienceKeys : undefined;
    const requested = explicitIncoming ??
      (current.length > 0 && current[0] === this.draft.audienceKey
        ? current
        : [this.draft.audienceKey]);
    const authorized = requested.filter((key) => authorizedKeys.has(key));
    const normalized = this.normalizeSelection(
      this.draft.audienceKey,
      authorized.length > 0 ? authorized : [this.draft.audienceKey],
      this.draft.availableAudiences,
    );
    const authorizationChangedSelection =
      explicitIncoming !== undefined && !this.sameSelection(explicitIncoming, normalized);
    const nextDraft = authorizationChangedSelection
      ? this.invalidateSelectionReplayIdentity(this.draft)
      : this.draft;

    this.selectedKeys.set(normalized);
    this.innerDraft.set({
      ...nextDraft,
      audienceKey: normalized[0] ?? '',
      audienceKeys: normalized,
    });

    if (authorizationChangedSelection) {
      this.localError.set(
        '一部の配信対象への権限が変更されたため、その対象を選択から外しました。公開前に対象を再確認してください。',
      );
      this.closeFinalReview();
    }
  }

  isSelected(audienceKey: string): boolean {
    return this.selectedKeys().includes(audienceKey);
  }

  isPrimary(audienceKey: string): boolean {
    return this.selectedKeys()[0] === audienceKey;
  }

  additionalSelectionDisabled(audience: AnnouncementAudienceOption): boolean {
    if (!this.isEditable() || this.isPrimary(audience.key)) {
      return true;
    }
    const primary = this.selectedAudiences()[0];
    if (!primary || (primary.scope !== 'group' && primary.scope !== 'channel')) {
      return true;
    }
    return !this.isSelected(audience.key) && !this.canAddMore();
  }

  toggleAudience(audienceKey: string, selected: boolean): void {
    if (!this.isEditable() || this.isPrimary(audienceKey)) {
      return;
    }

    const draft = this.innerDraft();
    if (!draft) {return;}
    const audience = draft.availableAudiences.find((item) => item.key === audienceKey);
    if (!audience || (audience.scope !== 'group' && audience.scope !== 'channel')) {
      return;
    }

    const primaryKey = this.selectedKeys()[0];
    const primary = draft.availableAudiences.find((item) => item.key === primaryKey);
    if (!primary || (primary.scope !== 'group' && primary.scope !== 'channel')) {
      this.localError.set('複数配信する場合は、下の Audience ステップで Group または Channel を主対象に選択してください。');
      return;
    }

    const next = selected
      ? [...this.selectedKeys(), audienceKey]
      : this.selectedKeys().filter((key) => key !== audienceKey);
    const normalized = this.normalizeSelection(primaryKey, next, draft.availableAudiences);
    this.applySelection(normalized);
  }

  removeAudience(audienceKey: string): void {
    if (this.isPrimary(audienceKey)) {return;}
    this.toggleAudience(audienceKey, false);
  }

  onInnerDraftChanged(nextDraft: AnnouncementEditorDraft): void {
    const previousSelection = this.selectedKeys();
    const previousPrimary = previousSelection[0];
    const candidateKeys = [
      nextDraft.audienceKey,
      ...previousSelection.filter((key) => key !== previousPrimary && key !== nextDraft.audienceKey),
    ];
    const normalized = this.normalizeSelection(
      nextDraft.audienceKey,
      candidateKeys,
      nextDraft.availableAudiences,
    );
    const selectionChanged = !this.sameSelection(previousSelection, normalized);
    const identitySafeDraft = selectionChanged
      ? this.invalidateSelectionReplayIdentity(nextDraft)
      : nextDraft;
    this.selectedKeys.set(normalized);
    const enriched: AnnouncementEditorDraft = {
      ...identitySafeDraft,
      audienceKey: normalized[0] ?? '',
      audienceKeys: normalized,
    };
    this.innerDraft.set(enriched);
    this.localError.set(undefined);
    this.closeFinalReview();
    this.draftChanged.emit(enriched);
  }

  onSaveDraftRequested(submission: AnnouncementEditorSubmission): void {
    const augmented = this.augmentSubmission(submission);
    const error = augmented ? this.validateSubmission(augmented) : '配信対象を1件以上選択してください。';
    if (!augmented || error) {
      this.localError.set(error ?? '配信対象を確認してください。');
      return;
    }
    this.localError.set(undefined);
    this.saveDraftRequested.emit(augmented);
  }

  onPublishRequested(submission: AnnouncementEditorSubmission): void {
    const augmented = this.augmentSubmission(submission);
    const error = augmented ? this.validateSubmission(augmented) : '配信対象を1件以上選択してください。';
    if (!augmented || error) {
      this.localError.set(error ?? '配信対象を確認してください。');
      return;
    }

    this.localError.set(undefined);
    if (this.selectedAudiences().length === 1) {
      this.publishRequested.emit(augmented);
      return;
    }

    this.pendingPublication.set(augmented);
    this.finalReviewOpen.set(true);
  }

  confirmFinalPublication(): void {
    const pending = this.pendingPublication();
    if (!pending || this.publishing) {return;}
    const refreshed = this.augmentSubmission(pending);
    const error = refreshed ? this.validateSubmission(refreshed) : '配信対象を1件以上選択してください。';
    if (!refreshed || error) {
      this.localError.set(error ?? '配信対象を確認してください。');
      this.closeFinalReview();
      return;
    }

    this.finalReviewOpen.set(false);
    this.pendingPublication.set(null);
    this.publishRequested.emit(refreshed);
  }

  closeFinalReview(): void {
    if (this.publishing) {return;}
    this.finalReviewOpen.set(false);
    this.pendingPublication.set(null);
  }

  displayError(): string | undefined {
    return this.localError() ?? this.submissionError;
  }

  finalConfirmLabel(): string {
    return this.pendingPublication()?.deliveryMode === 'scheduled'
      ? 'Schedule for selected targets'
      : 'Publish to selected targets';
  }

  private applySelection(keys: readonly string[]): void {
    const draft = this.innerDraft();
    if (!draft) {return;}
    this.selectedKeys.set(keys);
    this.localError.set(undefined);
    this.closeFinalReview();
    const enriched: AnnouncementEditorDraft = {
      ...this.invalidateSelectionReplayIdentity(draft),
      audienceKey: keys[0] ?? '',
      audienceKeys: keys,
    };
    this.innerDraft.set(enriched);
    this.draftChanged.emit(enriched);
  }

  private augmentSubmission(
    submission: AnnouncementEditorSubmission,
  ): AnnouncementEditorSubmission | null {
    const draft = this.innerDraft();
    if (!draft) {return null;}
    const audiences = this.selectedKeys()
      .map((key) => draft.availableAudiences.find((audience) => audience.key === key))
      .filter((audience): audience is AnnouncementAudienceOption => audience !== undefined);
    if (audiences.length !== this.selectedKeys().length || audiences.length === 0) {
      return null;
    }

    return {
      ...submission,
      audience: audiences[0],
      audiences,
    };
  }

  private validateSubmission(submission: AnnouncementEditorSubmission): string | undefined {
    const selected = submission.audiences?.length ? submission.audiences : [submission.audience];
    if (selected.length > maximumAudienceTargets) {
      return `配信対象は最大${maximumAudienceTargets}件まで選択できます。`;
    }
    if (
      selected.length > 1 &&
      selected.some((audience) => audience.scope !== 'group' && audience.scope !== 'channel')
    ) {
      return '複数配信では Group / Channel だけを選択できます。';
    }

    if (submission.deliveryMode === 'scheduled') {
      const zones = new Set(selected.map((audience) => audience.scheduleTimeZoneId ?? 'UTC'));
      if (zones.size > 1) {
        return '選択した対象の組織タイムゾーンが異なるため、同じ時刻で予約できません。即時公開するか、同じタイムゾーンの対象だけを選択してください。';
      }
    }
    return undefined;
  }

  private normalizeSelection(
    primaryKey: string,
    requestedKeys: readonly string[],
    audiences: readonly AnnouncementAudienceOption[],
  ): readonly string[] {
    const authorizedByKey = new Map(audiences.map((audience) => [audience.key, audience]));
    const primary = authorizedByKey.get(primaryKey);
    if (!primary) {return [];}

    if (primary.scope !== 'group' && primary.scope !== 'channel') {
      return [primary.key];
    }

    const result = [primary.key];
    for (const key of requestedKeys) {
      if (result.length >= maximumAudienceTargets) {break;}
      if (key === primary.key || result.includes(key)) {continue;}
      const candidate = authorizedByKey.get(key);
      if (candidate?.scope === 'group' || candidate?.scope === 'channel') {
        result.push(candidate.key);
      }
    }
    return result;
  }

  private sameSelection(left: readonly string[], right: readonly string[]): boolean {
    return left.length === right.length && left.every((key, index) => key === right[index]);
  }

  private invalidateSelectionReplayIdentity(draft: AnnouncementEditorDraft): AnnouncementEditorDraft {
    return {
      ...draft,
      createIdempotencyKey: draft.id ? draft.createIdempotencyKey : undefined,
      transitionIdempotencyKey: undefined,
    };
  }
}

const maximumAudienceTargets = 20;