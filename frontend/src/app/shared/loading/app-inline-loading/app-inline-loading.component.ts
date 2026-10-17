import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-inline-loading',
  standalone: true,
  template: `
    <span class="inline-loading" role="status" aria-live="polite">
      <span class="inline-loading__spinner" aria-hidden="true"></span>
      <span>{{ label || i18n.translate('shared.loading') }}</span>
    </span>
  `,
  styles: [
    `
      .inline-loading {
        display: inline-flex;
        gap: 0.5rem;
        align-items: center;
        color: var(--coglatas-color-text-secondary);
        font-size: 0.925rem;
      }

      .inline-loading__spinner {
        width: 1rem;
        height: 1rem;
        border: 2px solid var(--coglatas-color-border-default);
        border-top-color: var(--coglatas-color-action-primary);
        border-radius: var(--coglatas-radius-pill);
        animation: app-inline-loading-spin 0.8s linear infinite;
      }

      @keyframes app-inline-loading-spin {
        to {
          transform: rotate(360deg);
        }
      }
    `
  ],
})
export class AppInlineLoadingComponent {
  readonly i18n = inject(I18nService);
  @Input() label = '';
}
