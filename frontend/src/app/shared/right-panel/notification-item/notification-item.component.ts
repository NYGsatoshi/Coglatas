import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { RightPanelNotification } from '../right-panel.types';
import { isSupportedNotificationTarget, requiresAuthorizedServerOpen } from '../right-panel.facade';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-notification-item',
  standalone: true,
  imports: [],
  template: `
    <article class="notification" [class.notification--read]="notification.read">
      <div class="notification__header">
        <span class="notification__state">{{ notification.read ? i18n.translate('panel.read') : i18n.translate('panel.unread') }}</span>
        <span class="notification__target">{{ targetLabel() }}</span>
      </div>
      <h3 class="notification__title">{{ notification.title }}</h3>
      <p class="notification__body">{{ notification.body }}</p>

      <div class="notification__actions">
        @if (canOpenTarget()) {
          <button
            type="button"
            class="notification__link"
            data-testid="notification-target-link"
            (click)="targetSelected.emit(notification.id)"
            [attr.aria-label]="i18n.translate('panel.openTargetFor', { title: notification.title })"
          >
            {{ i18n.translate('panel.openTarget') }}
          </button>
        } @else {
          <span class="notification__unsupported" data-testid="notification-target-unavailable">
            {{ i18n.translate('panel.unsupported') }}
          </span>
        }

        @if (!notification.read) {
          <button
            type="button"
            class="notification__mark"
            data-testid="notification-mark-read-action"
            (click)="markReadRequested.emit(notification.id)"
          >
            {{ i18n.translate('panel.markRead') }}
          </button>
        }
      </div>
    </article>
  `,
  styleUrl: './notification-item.component.scss',
})
export class NotificationItemComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) notification!: RightPanelNotification;
  @Output() readonly targetSelected = new EventEmitter<string>();
  @Output() readonly markReadRequested = new EventEmitter<string>();

  targetLabel(): string {
    return this.i18n.translate(`panel.target.${this.notification.target.type}`);
  }

  canOpenTarget(): boolean {
    return !!this.notification.id && (
      requiresAuthorizedServerOpen(this.notification.target.type) ||
      (isSupportedNotificationTarget(this.notification.target.type) && !!this.notification.target.route)
    );
  }
}
