import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, EventEmitter, Output , inject} from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-new-message-banner',
  standalone: true,
  template: `
    <button type="button" class="new-message-banner" data-testid="new-message-banner" (click)="acknowledge.emit()">
      {{ i18n.translate('messaging.newMessages') }}
    </button>
  `,
  styles: [
    `
      :host {
        display: block;
      }

      .new-message-banner {
        width: 100%;
        min-height: 38px;
        border: 1px solid #2f6f6d;
        border-radius: 6px;
        background: #edfafa;
        color: #164e4b;
        font: inherit;
        font-weight: 900;
        cursor: pointer;
      }
    `
  ],
})
export class NewMessageBannerComponent {
  readonly i18n = inject(I18nService);
  @Output() readonly acknowledge = new EventEmitter<void>();
}
