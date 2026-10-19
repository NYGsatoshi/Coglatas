import { AfterViewChecked, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';

import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { FailedMessageItemComponent } from '../failed-message-item/failed-message-item.component';
import { MessageItemComponent } from '../message-item/message-item.component';
import {
  MessagingMessageActionState,
  MessagingMessageViewModel,
  MessagingPageStatus,
  MessagingRouteKind
} from '../messaging.types';
import { NewMessageBannerComponent } from '../new-message-banner/new-message-banner.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-message-timeline',
  standalone: true,
  imports: [FailedMessageItemComponent, MessageItemComponent, NewMessageBannerComponent],
  template: `
    <section #timeline class="timeline" id="message-timeline" [class.timeline--dm]="routeKind === 'dm'" tabindex="-1" data-testid="message-timeline">
      @if (inlineError) {
        <p class="timeline__error" data-testid="manual-refresh-error">{{ inlineError }}</p>
      }

      @if (messageAction.feedback) {
        <p class="timeline__action-feedback" role="status" aria-live="polite" data-testid="message-action-status">
          {{ messageAction.feedback.message }}
        </p>
      }

      @if (hasNewMessagesWhileReading) {
        <app-new-message-banner (acknowledge)="acknowledgeNewMessages.emit()" />
      }

      @if (status === 'loading') {
        <section class="timeline__empty" data-testid="messages-loading-state" role="status" aria-live="polite">
          <p>{{ i18n.translate('messaging.timeline.loading') }}</p>
        </section>
      } @else if (status === 'sessionExpired') {
        <section class="timeline__empty" data-testid="messages-session-expired" role="status">
          <h2>{{ i18n.translate('messaging.timeline.sessionTitle') }}</h2>
          <p>{{ i18n.translate('messaging.timeline.sessionHelp') }}</p>
        </section>
      } @else if (!canReadBody || status === 'permissionDenied' || status === 'removedParticipant') {
        <section class="timeline__empty" data-testid="body-hidden-state">
          <h2>{{ i18n.translate('messaging.timeline.hiddenTitle') }}</h2>
          <p>{{ i18n.translate('messaging.timeline.hiddenHelp') }}</p>
        </section>
      } @else if (messages.length === 0 || status === 'empty') {
        <section class="timeline__empty" data-testid="no-messages-state">
          <h2>{{ i18n.translate('messaging.timeline.emptyTitle') }}</h2>
          <p>{{ i18n.translate('messaging.timeline.emptyHelp') }}</p>
        </section>
      } @else {
        <div class="timeline__messages">
          @for (message of messages; track message.id) {
            @if (message.deliveryState === 'failed') {
              <app-failed-message-item [message]="message" (retry)="retry.emit($event)" />
            } @else {
              <app-message-item
                [message]="message"
                [routeKind]="routeKind"
                [messageAction]="messageAction"
                [canEditOwnMessages]="canEditOwnMessages"
                [canViewOthersPreciseReadTimestamps]="canViewOthersPreciseReadTimestamps"
                [canOpenThreads]="canOpenThreads"
                [focusTarget]="focusMessageId === message.id"
                (startEdit)="startEdit.emit($event)"
                (editDraftChange)="editDraftChange.emit($event)"
                (saveEditRequested)="saveEditRequested.emit($event)"
                (cancelAction)="cancelAction.emit()"
                (requestDelete)="requestDelete.emit($event)"
                (confirmDelete)="confirmDelete.emit($event)"
                (requestReport)="requestReport.emit($event)"
                (saveForLaterRequested)="saveForLaterRequested.emit($event)"
                (confirmReport)="confirmReport.emit($event)"
                (openThread)="openThread.emit($event)"
              />
            }
          }
        </div>
      }
    </section>
  `,
  styleUrl: './message-timeline.component.scss',
})
export class MessageTimelineComponent implements AfterViewChecked {
  readonly i18n = injectMessagingI18n();
  @ViewChild('timeline') private timeline?: ElementRef<HTMLElement>;
  @Input() routeKind: MessagingRouteKind = 'channel';
  @Input() messages: readonly MessagingMessageViewModel[] = [];
  @Input() status: MessagingPageStatus = 'ready';
  @Input() canReadBody = true;
  @Input() canEditOwnMessages = false;
  @Input() canViewOthersPreciseReadTimestamps = false;
  @Input() canOpenThreads = false;
  @Input() hasNewMessagesWhileReading = false;
  @Input() inlineError = '';
  @Input() focusMessageId: string | null = null;
  @Input() messageAction: MessagingMessageActionState = {
    messageId: null,
    mode: 'idle',
    draft: '',
    pending: null
  };
  @Output() readonly retry = new EventEmitter<string>();
  @Output() readonly loadOlder = new EventEmitter<void>();
  @Output() readonly loadNewer = new EventEmitter<void>();
  @Output() readonly acknowledgeNewMessages = new EventEmitter<void>();
  @Output() readonly startEdit = new EventEmitter<string>();
  @Output() readonly editDraftChange = new EventEmitter<{ readonly messageId: string; readonly draft: string }>();
  @Output() readonly saveEditRequested = new EventEmitter<string>();
  @Output() readonly cancelAction = new EventEmitter<void>();
  @Output() readonly requestDelete = new EventEmitter<string>();
  @Output() readonly confirmDelete = new EventEmitter<string>();
  @Output() readonly requestReport = new EventEmitter<string>();
  @Output() readonly saveForLaterRequested = new EventEmitter<string>();
  @Output() readonly confirmReport = new EventEmitter<{ readonly messageId: string; readonly reasonCode: string }>();
  @Output() readonly openThread = new EventEmitter<{
    readonly messageId: string;
    readonly triggerElementId: string;
  }>();

  private focusedFeedbackId: number | null = null;

  ngAfterViewChecked(): void {
    const feedback = this.messageAction.feedback;
    if (!feedback?.focusTimeline || feedback.id === this.focusedFeedbackId) {
      return;
    }
    this.focusedFeedbackId = feedback.id;
    queueMicrotask(() => this.timeline?.nativeElement.focus());
  }
}
