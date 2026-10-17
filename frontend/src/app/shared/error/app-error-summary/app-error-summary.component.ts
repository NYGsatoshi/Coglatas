import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { FrontendApiError } from '../../../core/api/api-error.model';
import { AppErrorBannerComponent } from '../app-error-banner/app-error-banner.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-error-summary',
  standalone: true,
  imports: [AppErrorBannerComponent],
  template: `
    <div class="error-summary">
      @for (error of errors; track error.localErrorId) {
        <app-error-banner [error]="error" [title]="i18n.translate('shared.checkInput')" />
      }
    </div>
  `,
  styles: [
    `
      .error-summary {
        display: grid;
        gap: 0.75rem;
      }
    `
  ],
})
export class AppErrorSummaryComponent {
  readonly i18n = inject(I18nService);
  @Input() errors: readonly FrontendApiError[] = [];
}
