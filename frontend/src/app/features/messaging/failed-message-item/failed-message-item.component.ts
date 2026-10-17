import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output , inject} from '@angular/core';

import { MessagingMessageViewModel } from '../messaging.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-failed-message-item',
  standalone: true,
  template: `
    <article class="failed-message" data-testid="failed-message">
      <header class="failed-message__meta">
        <span>{{ message.authorLabel }}</span>
        <span>{{ message.sentAtLabel }}</span>
        @if (message.clientRequestId) {
          <span data-testid="client-request-id">{{ message.clientRequestId }}</span>
        }
      </header>
      <p class="failed-message__body" data-testid="failed-message-body">{{ message.body }}</p>
      <p class="failed-message__reason" data-testid="safe-failure-reason">
        {{ message.safeFailureReason ?? i18n.translate('messaging.notSent') }}
      </p>
      @if (message.retryAllowed) {
        <button type="button" data-testid="retry-failed-message" (click)="retry.emit(message.id)">
          {{ i18n.translate('common.retry') }}
        </button>
      }
    </article>
  `,
  styleUrl: './failed-message-item.component.scss',
})
export class FailedMessageItemComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) message!: MessagingMessageViewModel;
  @Output() readonly retry = new EventEmitter<string>();
}
