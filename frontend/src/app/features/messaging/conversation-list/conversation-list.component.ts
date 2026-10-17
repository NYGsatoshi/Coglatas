import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { MessageNavigationStateService } from '../message-navigation-state.service';
import { MessagingConversationListItem } from '../messaging.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-conversation-list',
  standalone: true,
  imports: [RouterLink],
  template: `
    <nav class="conversation-list" [class.conversation-list--compact]="compact" [attr.aria-label]="i18n.translate('messaging.conversations')" data-testid="conversation-list">
      @for (conversation of conversations; track conversation.id) {
        <div class="conversation-list__row">
          <a
            class="conversation-list__item"
            [class.conversation-list__item--selected]="isSelected(conversation)"
            [class.conversation-list__item--unread]="isUnread(conversation)"
            [class.conversation-list__item--mention]="conversation.hasMention === true"
            [attr.aria-current]="isSelected(conversation) ? 'page' : null"
            [attr.data-conversation-id]="conversation.id"
            [routerLink]="conversation.route"
            data-testid="conversation-list-item"
            (click)="rememberListScrollBeforeNavigate(conversation.id)"
          >
            <span class="conversation-list__title">{{ conversation.titleKey ? i18n.translate(conversation.titleKey) : conversation.title }}</span>
            <span class="conversation-list__meta">{{ conversation.lastActivityAt ? i18n.formatDateTime(conversation.lastActivityAt, { dateStyle: 'medium', timeStyle: 'short' }) : conversation.lastActivityLabel }}</span>
            @if (conversation.kind === 'dm') {
              <span class="conversation-list__preview" data-testid="dm-preview-hidden">{{ i18n.translate('messaging.list.dmHidden') }}</span>
            } @else {
              <span class="conversation-list__preview" data-testid="channel-preview">{{ conversation.safePreviewLabel }}</span>
            }

            @if (isSelected(conversation) || (showUnreadBadges && isUnread(conversation)) || conversation.hasMention === true || conversation.isLater === true) {
              <span class="conversation-list__states" data-testid="conversation-state-summary">
                @if (isSelected(conversation)) {
                  <span class="conversation-list__state conversation-list__state--selected" data-testid="conversation-selected">
                    {{ i18n.translate('messaging.list.selected') }}
                  </span>
                }
                @if (showUnreadBadges && isUnread(conversation)) {
                  <span class="conversation-list__state conversation-list__state--unread" data-testid="conversation-unread">
                    <span class="conversation-list__unread-dot" aria-hidden="true"></span>
                    <span data-testid="conversation-unread-badge">{{ i18n.translate('messaging.list.unread', { count: i18n.formatNumber(conversation.unreadCount ?? 0) }) }}</span>
                  </span>
                }
                @if (conversation.hasMention === true) {
                  <span
                    class="conversation-list__state conversation-list__state--mention"
                    data-testid="conversation-mention"
                    [attr.aria-label]="i18n.translate('messaging.list.mentionAria')"
                  >
                    {{ i18n.translate('messaging.list.you') }}
                  </span>
                }
                @if (conversation.isLater === true) {
                  <span class="conversation-list__state conversation-list__state--later" data-testid="conversation-later-state">
                    {{ i18n.translate('messaging.inbox.Later') }}
                  </span>
                }
              </span>
            }
          </a>
          @if (showLaterActions) {
            <button
              type="button"
              class="conversation-list__later"
              data-testid="conversation-later-toggle"
              [attr.aria-label]="laterActionLabel(conversation)"
              [attr.aria-pressed]="conversation.isLater === true"
              [disabled]="laterPendingConversationId !== null"
              (click)="laterChanged.emit({ conversationId: conversation.id, isLater: conversation.isLater !== true })"
            >
              {{ i18n.translate(laterPendingConversationId === conversation.id ? 'messaging.saving' : (conversation.isLater === true ? 'messaging.list.removeLater' : 'messaging.inbox.Later')) }}
            </button>
          }
        </div>
      }
    </nav>
  `,
  styleUrl: './conversation-list.component.scss',
})
export class ConversationListComponent {
  readonly i18n = injectMessagingI18n();
  private readonly navigationState = inject(MessageNavigationStateService);
  @Input({ required: true }) conversations: readonly MessagingConversationListItem[] = [];
  @Input() compact = false;
  @Input() selectedConversationId: string | null = null;
  @Input() preserveListScroll = false;
  @Input() showUnreadBadges = true;
  @Input() showLaterActions = false;
  @Input() laterPendingConversationId: string | null = null;
  @Output() readonly laterChanged = new EventEmitter<{ conversationId: string; isLater: boolean }>();

  isSelected(conversation: MessagingConversationListItem): boolean {
    return this.selectedConversationId === conversation.id;
  }

  isUnread(conversation: MessagingConversationListItem): boolean {
    return (conversation.unreadCount ?? 0) > 0;
  }

  rememberListScrollBeforeNavigate(conversationId: string): void {
    if (this.preserveListScroll) {
      this.navigationState.rememberListScroll(conversationId);
    }
  }

  laterActionLabel(conversation: MessagingConversationListItem): string {
    return this.i18n.translate(conversation.isLater === true ? 'messaging.list.removeLaterAria' : 'messaging.list.addLaterAria', { title: conversation.titleKey ? this.i18n.translate(conversation.titleKey) : conversation.title });
  }
}
