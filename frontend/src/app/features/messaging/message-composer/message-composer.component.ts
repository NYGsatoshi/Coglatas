import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';

import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { MessagingMentionCandidate, MessageSendState } from '../messaging.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-message-composer',
  standalone: true,
  template: `
    <form class="composer" data-testid="message-composer" (submit)="submit($event)">
      <label for="message-draft">
        <span>{{ i18n.translate('messaging.composer.label') }}</span>
        <textarea
          id="message-draft"
          rows="1"
          data-testid="message-draft"
          [disabled]="disabled || isSending"
          [value]="draft"
          (input)="onDraftInput($event)"
          (keydown)="onDraftKeydown($event)"
          (compositionstart)="onCompositionStart()"
          (compositionend)="onCompositionEnd()"
          aria-describedby="composer-keyboard-hint"
          [placeholder]="i18n.translate('messaging.composer.placeholder')"
        ></textarea>
      </label>

      @if (isSending) {
        <p class="composer__status" data-testid="composer-send-status" role="status" aria-live="polite">
          {{ i18n.translate('messaging.composer.sending') }}
        </p>
      } @else if (sendState.status === 'failed') {
        <div class="composer__error" data-testid="composer-send-error" role="alert">
          <strong>{{ i18n.translate('messaging.composer.failed') }}</strong>
          <span>{{ sendState.message }}</span>
          <span>{{ i18n.translate('messaging.composer.retryHelp') }}</span>
        </div>
      }

      @if (disabledReason) {
        <p class="composer__disabled" data-testid="composer-disabled-reason">{{ disabledReason }}</p>
      }

      <div class="composer__footer">
        <details class="composer__secondary-tools" data-testid="composer-secondary-tools">
          <summary class="composer__options-toggle" data-testid="composer-options-toggle">{{ i18n.translate('messaging.composer.tools') }}</summary>
          <p id="composer-keyboard-hint" class="composer__hint">
            {{ i18n.translate('messaging.composer.keyboard') }}
          </p>

          @if (mentionCandidates.length > 0) {
            <div class="composer__mention-tools" role="group" [attr.aria-label]="i18n.translate('messaging.composer.addMention')" data-testid="mention-candidates">
              <span class="composer__mention-label">{{ i18n.translate('messaging.composer.mentions') }}</span>
              <div class="composer__mention-candidates">
                @for (candidate of mentionCandidates; track candidate.userId) {
                  <button
                    class="composer__mention-candidate"
                    type="button"
                    data-testid="mention-candidate"
                    [attr.aria-pressed]="isMentioned(candidate.userId)"
                    [disabled]="disabled || isSending || isMentioned(candidate.userId)"
                    (click)="addMention(candidate)"
                  >
                    {{ i18n.translate(isMentioned(candidate.userId) ? 'messaging.composer.mentionAdded' : 'messaging.composer.mentionAdd', { name: candidate.displayName }) }}
                  </button>
                }
              </div>
            </div>
          }

          <p class="composer__attachment" data-testid="attachment-disabled">{{ attachmentDisabledLabel || i18n.translate('messaging.attachmentsDisabled') }}</p>
        </details>

        <button
          class="composer__send"
          type="submit"
          data-testid="send-message"
          [attr.aria-busy]="isSending"
          [disabled]="disabled || isSending || draft.trim().length === 0"
        >
          {{ i18n.translate(isSending ? 'messaging.composer.sending' : 'messaging.composer.send') }}
        </button>
      </div>
    </form>
  `,
  styleUrl: './message-composer.component.scss',
})
export class MessageComposerComponent implements OnChanges {
  readonly i18n = injectMessagingI18n();
  @Input() draft = '';
  @Input() disabled = false;
  @Input() sending = false;
  @Input() sendState: MessageSendState = { status: 'idle' };
  @Input() disabledReason = '';
  @Input() attachmentDisabledLabel = '';
  @Input() mentionCandidates: readonly MessagingMentionCandidate[] = [];
  @Output() readonly draftChange = new EventEmitter<string>();
  @Output() readonly send = new EventEmitter<readonly string[]>();

  private composing = false;
  private selectedMentionUserIds: string[] = [];

  get isSending(): boolean {
    return this.sending || this.sendState.status === 'sending';
  }

  ngOnChanges(changes: SimpleChanges): void {
    // Candidate lists can change when membership/authorization changes without
    // a draft edit. Never submit mention identities from a previous eligible set.
    if (changes['draft'] || changes['mentionCandidates']) {
      this.reconcileSelectedMentions(this.draft);
    }
  }

  onDraftInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement | null)?.value ?? '';
    this.reconcileSelectedMentions(value);
    this.draftChange.emit(value);
  }

  onCompositionStart(): void {
    this.composing = true;
  }

  onCompositionEnd(): void {
    this.composing = false;
  }

  addMention(candidate: MessagingMentionCandidate): void {
    if (this.disabled || this.isSending || this.isMentioned(candidate.userId)) {
      return;
    }

    this.selectedMentionUserIds = [...this.selectedMentionUserIds, candidate.userId];
    const separator = this.draft.length > 0 && !/\s$/u.test(this.draft) ? ' ' : '';
    this.draftChange.emit(`${this.draft}${separator}@${candidate.displayName} `);
  }

  isMentioned(userId: string): boolean {
    return this.selectedMentionUserIds.includes(userId);
  }

  onDraftKeydown(event: KeyboardEvent): void {
    if (
      event.key !== 'Enter' ||
      event.shiftKey ||
      event.ctrlKey ||
      event.altKey ||
      event.metaKey ||
      event.isComposing ||
      event.keyCode === 229 ||
      this.composing
    ) {
      return;
    }

    const value = (event.target as HTMLTextAreaElement | null)?.value ?? this.draft;
    if (!this.canSend(value)) {
      return;
    }

    event.preventDefault();
    this.emitSend();
  }

  submit(event: Event): void {
    event.preventDefault();
    if (this.canSend(this.draft)) {
      this.emitSend();
    }
  }

  private reconcileSelectedMentions(value: string): void {
    if (this.selectedMentionUserIds.length === 0) {
      return;
    }

    const selected = new Set(this.selectedMentionUserIds);
    this.selectedMentionUserIds = this.mentionCandidates
      .filter((candidate) => selected.has(candidate.userId) && value.includes(`@${candidate.displayName}`))
      .map((candidate) => candidate.userId);
  }

  private emitSend(): void {
    this.send.emit([...this.selectedMentionUserIds]);
  }

  private canSend(value: string): boolean {
    return !this.disabled && !this.isSending && !this.composing && value.trim().length > 0;
  }
}
