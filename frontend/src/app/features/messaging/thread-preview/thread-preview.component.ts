import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
} from '@angular/core';
import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { LucideArrowLeft, LucideX } from '@lucide/angular';

import {
  MessagingMentionCandidate,
  MessagingThreadViewModel
} from '../messaging.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-thread-preview',
  standalone: true,
  imports: [LucideArrowLeft, LucideX],
  template: `
    <aside
      #panel
      class="thread"
      tabindex="-1"
      aria-labelledby="message-thread-heading"
      data-testid="thread-preview"
      (keydown.escape)="closeFromKeyboard($event)"
    >
      <header class="thread__header">
        <button
          #backControl
          type="button"
          class="thread__back"
          data-testid="thread-back"
          [attr.aria-label]="i18n.translate('messaging.thread.backAria')"
          (click)="close.emit()"
        >
          <svg lucideArrowLeft aria-hidden="true"></svg>
          <span>{{ i18n.translate('messaging.back') }}</span>
        </button>
        <div>
          <p>{{ i18n.translate('messaging.thread.context') }}</p>
          <h2 id="message-thread-heading">{{ i18n.translate('messaging.thread.title') }}</h2>
        </div>
        <button
          type="button"
          class="thread__close"
          data-testid="thread-close"
          [attr.aria-label]="i18n.translate('messaging.thread.closeAria')"
          (click)="close.emit()"
        >
          <svg lucideX aria-hidden="true"></svg>
        </button>
      </header>

      @if (thread.status === 'loading') {
        <p class="thread__state" role="status">{{ i18n.translate('messaging.thread.loading') }}</p>
      } @else if (thread.status === 'permissionDenied' || thread.status === 'error') {
        <section class="thread__state" data-testid="thread-load-failure">
          <h3>{{ i18n.translate('messaging.thread.unavailable') }}</h3>
          <p>{{ thread.error ?? i18n.translate('messaging.thread.loadFailed') }}</p>
          @if (thread.status === 'error') {
            <button type="button" (click)="retry.emit()">{{ i18n.translate('messaging.refresh') }}</button>
          }
        </section>
      } @else if (thread.status === 'ready' && thread.rootMessage && thread.summary) {
        <section class="thread__root" data-testid="thread-root-message" [attr.aria-label]="i18n.translate('messaging.thread.parentAria')">
          <p class="thread__pinned">{{ i18n.translate('messaging.thread.pinned') }}</p>
          <div class="thread__message-meta">
            <strong>{{ thread.rootMessage.authorLabel }}</strong>
            <span>{{ thread.rootMessage.sentAtLabel }}</span>
          </div>
          @if (thread.rootMessage.isDeleted) {
            <p class="thread__tombstone">{{ i18n.translate('messaging.deleted') }}</p>
          } @else {
            <p class="thread__body">{{ thread.rootMessage.body }}</p>
          }
        </section>

        <section class="thread__summary" [attr.aria-label]="i18n.translate('messaging.thread.summaryAria')">
          <strong>{{ i18n.translate(thread.summary.replyCount === 1 ? 'messaging.thread.replyCountOne' : 'messaging.thread.replyCount', { count: i18n.formatNumber(thread.summary.replyCount) }) }}</strong>
          @if (thread.summary.participantDisplayNames.length > 0) {
            <span>{{ i18n.translate('messaging.thread.participants', { names: thread.summary.participantDisplayNames.join(i18n.locale() === 'ja' ? '、' : ', ') }) }}</span>
          }
          @if (thread.hasMore) {
            <p data-testid="thread-bounded-notice">
              @if (thread.anchorReplyMessageId) {
                {{ i18n.translate('messaging.thread.boundedAnchor', { shown: i18n.formatNumber(thread.replies.length), total: i18n.formatNumber(thread.summary.replyCount) }) }}
              } @else {
                {{ i18n.translate('messaging.thread.bounded', { shown: i18n.formatNumber(thread.maximumReplies), total: i18n.formatNumber(thread.summary.replyCount) }) }}
              }
            </p>
          }
        </section>

        <div class="thread__replies" data-testid="thread-replies">
          @if (thread.replies.length === 0) {
            <p class="thread__empty">{{ i18n.translate('messaging.thread.empty') }}</p>
          }
          @for (reply of thread.replies; track reply.id) {
            <article
              class="thread__reply"
              [class.thread__reply--focus-target]="focusMessageId === reply.id"
              [attr.id]="'thread-message-' + safeId(reply.id)"
              [attr.tabindex]="focusMessageId === reply.id ? -1 : null"
              [attr.data-message-id]="reply.id"
            >
              <div class="thread__message-meta">
                <strong>{{ reply.authorLabel }}</strong>
                <span>{{ reply.sentAtLabel }}</span>
              </div>
              @if (reply.isDeleted) {
                <p class="thread__tombstone">{{ i18n.translate('messaging.deleted') }}</p>
              } @else {
                <p class="thread__body">{{ reply.body }}</p>
              }
            </article>
          }
        </div>

        <form class="thread__composer" data-testid="thread-composer" (submit)="submit($event)">
          <label for="thread-reply-draft">
            {{ i18n.translate('messaging.thread.replyingTo', { name: thread.rootMessage.authorLabel }) }}
          </label>
          <textarea
            #replyDraft
            id="thread-reply-draft"
            rows="3"
            data-testid="thread-reply-draft"
            [value]="thread.draft"
            [disabled]="!canReply || thread.sending"
            [attr.aria-describedby]="thread.error ? 'thread-composer-hint thread-composer-error' : 'thread-composer-hint'"
            (input)="changeDraft($event)"
            (keydown)="onDraftKeydown($event)"
            (compositionstart)="composing = true"
            (compositionend)="composing = false"
          ></textarea>
          <p id="thread-composer-hint" class="thread__hint">{{ i18n.translate('messaging.thread.keyboard') }}</p>
          @if (!canReply) {
            <p class="thread__disabled" data-testid="thread-composer-disabled">{{ disabledReason }}</p>
          }
          @if (thread.error) {
            <p id="thread-composer-error" class="thread__error" role="alert">{{ thread.error }}</p>
          }
          <button
            type="submit"
            data-testid="send-thread-reply"
            [attr.aria-busy]="thread.sending"
            [disabled]="!canReply || thread.sending || thread.draft.trim().length === 0"
          >
            {{ i18n.translate(thread.sending ? 'messaging.thread.sending' : 'messaging.thread.reply') }}
          </button>
        </form>
      }
    </aside>
  `,
  styleUrl: './thread-preview.component.scss',
})
export class ThreadPreviewComponent implements AfterViewChecked, OnChanges {
  readonly i18n = injectMessagingI18n();
  @ViewChild('panel') private panel?: ElementRef<HTMLElement>;
  @ViewChild('backControl') private backControl?: ElementRef<HTMLButtonElement>;
  @ViewChild('replyDraft') private replyDraft?: ElementRef<HTMLTextAreaElement>;
  @Input({ required: true }) thread!: MessagingThreadViewModel;
  @Input() canPost = false;
  @Input() canCreateThread = false;
  @Input() mentionCandidates: readonly MessagingMentionCandidate[] = [];
  @Input() focusMessageId: string | null = null;
  @Output() readonly retry = new EventEmitter<void>();
  @Output() readonly close = new EventEmitter<void>();
  @Output() readonly draftChange = new EventEmitter<string>();
  @Output() readonly send = new EventEmitter<readonly string[]>();

  composing = false;
  private focusedKey: string | null = null;
  private focusBackAfterRootDeletion = false;
  private focusedMessageId: string | null = null;

  get canReply(): boolean {
    return this.thread.status === 'ready' &&
      this.thread.rootMessage?.isDeleted !== true &&
      this.canPost &&
      ((this.thread.summary?.replyCount ?? 0) > 0 || this.canCreateThread);
  }

  get disabledReason(): string {
    if (this.thread.rootMessage?.isDeleted) {
      return this.i18n.translate('messaging.thread.deletedDisabled');
    }
    if (!this.canPost) {
      return this.i18n.translate('messaging.thread.postDisabled');
    }
    return this.i18n.translate('messaging.thread.createDisabled');
  }

  changeDraft(event: Event): void {
    this.draftChange.emit((event.target as HTMLTextAreaElement | null)?.value ?? '');
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
      this.composing ||
      !this.canReply ||
      this.thread.sending ||
      !((event.target as HTMLTextAreaElement | null)?.value ?? this.thread.draft).trim()
    ) {
      return;
    }
    event.preventDefault();
    this.send.emit([]);
  }

  submit(event: Event): void {
    event.preventDefault();
    if (this.canReply && !this.composing && !this.thread.sending && this.thread.draft.trim()) {
      this.send.emit([]);
    }
  }

  closeFromKeyboard(event: Event): void {
    const keyboard = event as KeyboardEvent;
    if (keyboard.isComposing || keyboard.keyCode === 229 || this.composing) {
      return;
    }
    event.preventDefault();
    this.close.emit();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const threadChange = changes['thread'];
    if (!threadChange || threadChange.firstChange) {
      return;
    }
    const previous = threadChange.previousValue as MessagingThreadViewModel;
    const current = threadChange.currentValue as MessagingThreadViewModel;
    if (
      previous.rootMessageId === current.rootMessageId &&
      previous.rootMessage?.isDeleted !== true &&
      current.rootMessage?.isDeleted === true &&
      this.replyDraft?.nativeElement === document.activeElement
    ) {
      // Disabling a focused textarea moves browser focus to BODY. Capture the
      // deletion transition before Angular applies `disabled`, then keep focus
      // on a stable control inside the still-open thread panel.
      this.focusBackAfterRootDeletion = true;
    }
  }

  ngAfterViewChecked(): void {
    if (this.focusBackAfterRootDeletion) {
      this.focusBackAfterRootDeletion = false;
      queueMicrotask(() => this.backControl?.nativeElement.focus());
    }
    const key = this.thread.status === 'closed'
      ? null
      : this.thread.rootMessageId ?? 'unknown';
    const shouldFocusPanel = !!key && key !== this.focusedKey;
    if (!key) {
      this.focusedKey = null;
    } else if (shouldFocusPanel) {
      this.focusedKey = key;
    }
    const shouldFocusMessage = !!this.focusMessageId &&
      this.focusMessageId !== this.focusedMessageId &&
      this.thread.status === 'ready';
    if (shouldFocusMessage) {
      this.focusedMessageId = this.focusMessageId;
      queueMicrotask(() => document.getElementById(`thread-message-${this.safeId(this.focusMessageId!)}`)?.focus());
    } else if (shouldFocusPanel) {
      queueMicrotask(() => {
        // Roving tab navigation owns focus while switching the thread panel.
        if (!document.activeElement?.closest('[role="tablist"]')) {
          this.panel?.nativeElement.focus();
        }
      });
    }
    if (!this.focusMessageId) {
      this.focusedMessageId = null;
    }
  }

  safeId(value: string): string {
    return value.replace(/[^a-zA-Z0-9_-]/g, '-');
  }
}
