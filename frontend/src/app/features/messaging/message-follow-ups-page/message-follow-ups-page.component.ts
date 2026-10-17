import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideBookmarkCheck, LucideCheck, LucideChevronLeft } from '@lucide/angular';

import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { MessageFollowUpFacade } from '../message-follow-up.facade';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-message-follow-ups-page',
  standalone: true,
  imports: [ LucideBookmarkCheck, LucideCheck, LucideChevronLeft, RouterLink],
  template: `
    <section class="saved" aria-labelledby="saved-messages-heading" data-testid="saved-messages-page">
      <a class="saved__back" routerLink="/messages" data-testid="saved-messages-back">
        <svg lucideChevronLeft aria-hidden="true"></svg>
        {{ i18n.translate('messaging.conversations') }}
      </a>
      <header class="saved__header">
        <div>
          <p class="saved__eyebrow">{{ i18n.translate('messaging.saved.eyebrow') }}</p>
          <h1 id="saved-messages-heading">{{ i18n.translate('messaging.saved.title') }}</h1>
          <p>{{ i18n.translate('messaging.saved.scope') }}</p>
        </div>
        <svg class="saved__heading-icon" lucideBookmarkCheck aria-hidden="true"></svg>
      </header>

      <p class="saved__notice">
        {{ i18n.translate('messaging.saved.reminders') }}
      </p>

      @if (facade.view().status === 'loading') {
        <p role="status" data-testid="saved-messages-loading">{{ i18n.translate('messaging.saved.loading') }}</p>
      } @else if (facade.view().status === 'error') {
        <section class="saved__state" data-testid="saved-messages-error">
          <h2>{{ i18n.translate('messaging.saved.unavailable') }}</h2>
          <p>{{ facade.view().error }}</p>
          <button type="button" (click)="facade.load()">{{ i18n.translate('messaging.tryAgain') }}</button>
        </section>
      } @else if (facade.view().status === 'empty') {
        <section class="saved__state" data-testid="saved-messages-empty">
          <h2>{{ i18n.translate('messaging.saved.empty') }}</h2>
          <p>{{ i18n.translate('messaging.saved.emptyHelp') }}</p>
        </section>
      } @else {
        <ul class="saved__list" [attr.aria-label]="i18n.translate('messaging.saved.title')">
          @for (item of facade.view().items; track item.messageId) {
            <li class="saved__item" data-testid="saved-message-item">
              <div class="saved__meta">
                <strong>{{ item.conversationTitle }}</strong>
                <span>{{ item.authorDisplayName }} · {{ i18n.formatDateTime(item.messageCreatedAt, { dateStyle: 'medium', timeStyle: 'medium' }) }}</span>
              </div>
              <p>{{ item.body }}</p>
              <div class="saved__actions">
                <a
                  [routerLink]="item.route"
                  [queryParams]="{ focusMessageId: item.messageId, threadRootMessageId: item.threadRootMessageId }"
                  [attr.aria-label]="i18n.translate('messaging.saved.openAria', { title: item.conversationTitle })"
                  data-testid="open-saved-message"
                >{{ i18n.translate('messaging.saved.open') }}</a>
                <button
                  type="button"
                  [disabled]="!!facade.view().pendingMessageId"
                  [attr.aria-label]="i18n.translate('messaging.saved.completeAria', { name: item.authorDisplayName })"
                  data-testid="complete-saved-message"
                  (click)="facade.remove(item.messageId)"
                >
                  <svg lucideCheck aria-hidden="true"></svg>
                  {{ i18n.translate(facade.view().pendingMessageId === item.messageId ? 'messaging.saved.completing' : 'messaging.saved.complete') }}
                </button>
              </div>
            </li>
          }
        </ul>
        <nav class="saved__paging" [attr.aria-label]="i18n.translate('messaging.saved.pagesAria')">
          <button type="button" [disabled]="facade.view().page <= 1" (click)="facade.load(facade.view().page - 1)">{{ i18n.translate('messaging.saved.previous') }}</button>
          <span>{{ i18n.translate('messaging.saved.page', { page: i18n.formatNumber(facade.view().page) }) }}</span>
          <button type="button" [disabled]="!facade.hasNextPage()" (click)="facade.load(facade.view().page + 1)">{{ i18n.translate('messaging.saved.next') }}</button>
        </nav>
      }
    </section>
  `,
  styleUrl: './message-follow-ups-page.component.scss',
})
export class MessageFollowUpsPageComponent {
  readonly i18n = injectMessagingI18n();
  readonly facade = inject(MessageFollowUpFacade);

  constructor() {
    this.facade.load();
  }
}
