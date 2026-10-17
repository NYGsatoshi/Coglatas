import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';

import { NotificationItemComponent } from '../notification-item/notification-item.component';
import { RightPanelNotification } from '../right-panel.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-notifications-tab',
  standalone: true,
  imports: [NotificationItemComponent],
  template: `
    <section class="notifications-tab" [attr.aria-label]="i18n.translate('panel.notifications')">
      <p class="notifications-tab__note">{{ i18n.translate('panel.notificationScope') }}</p>
      @if (notifications.length > 0) {
        <div class="notifications-tab__list">
          @for (notification of notifications; track notification.id) {
            <app-notification-item
              [notification]="notification"
              (targetSelected)="targetSelected.emit($event)"
              (markReadRequested)="markReadRequested.emit($event)"
            />
          }
        </div>
      } @else {
        <p class="notifications-tab__empty">{{ i18n.translate('panel.empty') }}</p>
      }
    </section>
  `,
  styleUrl: './notifications-tab.component.scss',
})
export class NotificationsTabComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) notifications: readonly RightPanelNotification[] = [];
  @Output() readonly targetSelected = new EventEmitter<string>();
  @Output() readonly markReadRequested = new EventEmitter<string>();
}
