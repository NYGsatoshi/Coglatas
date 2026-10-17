import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-safe-not-found',
  standalone: true,
  template: `
    <section class="safe-state" role="status" [attr.aria-labelledby]="titleId">
      <h2 [id]="titleId">{{ title || i18n.translate('shared.notFound') }}</h2>
      <p>{{ i18n.translate('shared.notFoundMessage') }}</p>
    </section>
  `,
  styles: [
    `
      .safe-state {
        border: 1px solid #bfdbfe;
        border-radius: 8px;
        background: #eff6ff;
        padding: 1rem;
        color: #1e3a8a;
      }

      h2,
      p {
        margin: 0;
      }

      h2 {
        margin-bottom: 0.5rem;
        font-size: 1rem;
      }
    `
  ],
})
export class AppSafeNotFoundComponent {
  readonly i18n = inject(I18nService);
  @Input() title = '';
  @Input() titleId = 'app-safe-not-found-title';
}
