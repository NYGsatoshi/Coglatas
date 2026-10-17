import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-request-id',
  standalone: true,
  template: `
    @if (displayId) {
      <p class="request-id">
        <span class="request-id__label">{{ requestId ? i18n.translate('shared.requestId') : i18n.translate('shared.localErrorId') }}</span>
        <code>{{ displayId }}</code>
        @if (!requestId) {
          <span class="request-id__note">{{ i18n.translate('shared.localErrorNote') }}</span>
        }
      </p>
    }
  `,
  styles: [
    `
      .request-id {
        display: flex;
        flex-wrap: wrap;
        gap: 0.35rem 0.5rem;
        align-items: center;
        margin: 0;
        color: #475569;
        font-size: 0.875rem;
      }

      .request-id__label {
        font-weight: 700;
      }

      code {
        overflow-wrap: anywhere;
        border-radius: 4px;
        background: #f1f5f9;
        padding: 0.125rem 0.375rem;
        color: #334155;
      }

      .request-id__note {
        flex-basis: 100%;
        color: #64748b;
      }
    `
  ],
})
export class AppRequestIdComponent {
  readonly i18n = inject(I18nService);
  @Input() requestId: string | null | undefined;
  @Input() localErrorId: string | null | undefined;

  get displayId(): string {
    return this.requestId?.trim() || this.localErrorId?.trim() || '';
  }
}
