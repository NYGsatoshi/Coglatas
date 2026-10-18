import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { CoglatasThemeService } from '../../../core/theme/coglatas-theme.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-coglatas-theme-toggle',
  standalone: true,
  template: `
    <button
      type="button"
      class="theme-toggle"
      data-testid="theme-toggle"
      [attr.aria-label]="theme.isDark() ? i18n.translate('theme.lightAction') : i18n.translate('theme.darkAction')"
      [attr.title]="theme.isDark() ? i18n.translate('theme.lightAction') : i18n.translate('theme.darkAction')"
      (click)="theme.toggleTheme()"
    >
      <svg class="theme-toggle__icon" aria-hidden="true" width="18" height="18"
        viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
        stroke-linecap="round" stroke-linejoin="round">
        @if (theme.isDark()) {
          <circle cx="12" cy="12" r="4"></circle>
          <path d="M12 2v2m0 16v2M4.93 4.93l1.42 1.42m11.3 11.3 1.42 1.42M2 12h2m16 0h2M4.93 19.07l1.42-1.42m11.3-11.3 1.42-1.42"></path>
        } @else {
          <path d="M20.985 12.486A9 9 0 0 1 11.514 3.015a9 9 0 1 0 9.471 9.471Z"></path>
        }
      </svg>
      <span>{{ theme.isDark() ? i18n.translate('theme.light') : i18n.translate('theme.dark') }}</span>
    </button>
  `,
  styleUrl: './coglatas-theme-toggle.component.scss',
})
export class CoglatasThemeToggleComponent {
  readonly i18n = inject(I18nService);
  readonly theme = inject(CoglatasThemeService);
}
