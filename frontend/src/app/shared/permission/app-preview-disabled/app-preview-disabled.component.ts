import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-preview-disabled',
  standalone: true,
  template: `
    <section class="preview-disabled" role="status" [attr.aria-label]="label || i18n.translate('shared.previewDisabled')">
      <strong>{{ title || i18n.translate('shared.previewDisabled') }}</strong>
      <span>{{ message || i18n.translate('shared.previewMessage') }}</span>
    </section>
  `,
  styles: [
    `
      .preview-disabled {
        display: grid;
        gap: 0.35rem;
        border: 1px solid #cbd5e1;
        border-radius: 8px;
        background: #f8fafc;
        padding: 1rem;
        color: #475569;
      }

      strong {
        color: #334155;
      }
    `
  ],
})
export class AppPreviewDisabledComponent {
  readonly i18n = inject(I18nService);
  @Input() title = '';
  @Input() message = '';
  @Input() label = '';
}
