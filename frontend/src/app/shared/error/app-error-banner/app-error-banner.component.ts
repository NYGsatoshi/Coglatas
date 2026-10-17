import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { FrontendApiError, FrontendApiErrorDetail } from '../../../core/api/api-error.model';
import { AppRequestIdComponent } from '../app-request-id/app-request-id.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-error-banner',
  standalone: true,
  imports: [AppRequestIdComponent],
  template: `
    @if (error) {
      <section class="error-banner" role="alert" [attr.aria-labelledby]="titleId">
        <div class="error-banner__body">
          <h2 [id]="titleId">{{ title || i18n.translate('shared.error') }}</h2>
          <p>{{ message }}</p>
          <app-request-id [requestId]="error.requestId" [localErrorId]="error.localErrorId" />
        </div>

        @if (safeDetails.length > 0) {
          <details class="error-banner__details">
            <summary>{{ i18n.translate('shared.details') }}</summary>
            <dl>
              <div>
                <dt>{{ i18n.translate('shared.code') }}</dt>
                <dd>{{ error.code }}</dd>
              </div>
              <div>
                <dt>{{ i18n.translate('shared.httpStatus') }}</dt>
                <dd>{{ error.httpStatus }}</dd>
              </div>
              @for (detail of safeDetails; track detail.message) {
                <div>
                  <dt>{{ detail.code || detail.target || i18n.translate('shared.details') }}</dt>
                  <dd>{{ detail.message }}</dd>
                </div>
              }
            </dl>
          </details>
        }
      </section>
    }
  `,
  styles: [
    `
      .error-banner {
        display: grid;
        gap: 0.75rem;
        border: 1px solid #fecaca;
        border-radius: 8px;
        background: #fff7ed;
        padding: 1rem;
        color: #7f1d1d;
      }

      .error-banner__body {
        display: grid;
        gap: 0.5rem;
      }

      h2,
      p {
        margin: 0;
      }

      h2 {
        font-size: 1rem;
      }

      .error-banner__details {
        border-top: 1px solid #fed7aa;
        padding-top: 0.75rem;
        color: #431407;
      }

      summary {
        cursor: pointer;
        font-weight: 700;
      }

      dl {
        display: grid;
        gap: 0.5rem;
        margin: 0.75rem 0 0;
      }

      div {
        display: grid;
        gap: 0.15rem;
      }

      dt {
        color: #64748b;
        font-size: 0.8125rem;
        font-weight: 700;
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }
    `
  ],
})
export class AppErrorBannerComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) error!: FrontendApiError;
  @Input() title = '';
  @Input() titleId = 'app-error-banner-title';

  get message(): string {
    const errorId = this.error?.requestId?.trim() || this.error?.localErrorId?.trim() || this.i18n.translate('shared.idUnavailable');
    return this.i18n.translate('shared.errorId', { id: errorId });
  }

  get safeDetails(): readonly FrontendApiErrorDetail[] {
    if (!this.error?.redactionApplied) {
      return [];
    }

    return this.error.details.filter(isSafeDetail);
  }
}

function isSafeDetail(detail: FrontendApiErrorDetail): boolean {
  return [detail.code, detail.message, detail.target].every((value) => !value || isSafeText(value));
}

function isSafeText(value: string): boolean {
  const unsafePatterns = [
    /\b(at\s+\S+\(|stack|exception|sql|select\s+|insert\s+|update\s+|delete\s+|from\s+)/i,
    /\b(password|secret|token|connection\s*string|storage\s*key|metadata)\b/i,
    /[A-Za-z]:\\|\/(?:home|var|etc|usr|opt)\//,
    /[{[]\s*["']?\w+["']?\s*:/
  ];

  return value.length <= 240 && !unsafePatterns.some((pattern) => pattern.test(value));
}
