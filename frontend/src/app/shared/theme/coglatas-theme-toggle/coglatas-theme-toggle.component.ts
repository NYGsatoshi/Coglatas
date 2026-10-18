import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LucideMoon, LucideSun } from '@lucide/angular';

import { CoglatasThemeService } from '../../../core/theme/coglatas-theme.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-coglatas-theme-toggle',
  standalone: true,
  imports: [LucideMoon, LucideSun],
  template: `
    <button
      type="button"
      class="theme-toggle"
      data-testid="theme-toggle"
      [attr.aria-label]="theme.isDark() ? i18n.translate('theme.lightAction') : i18n.translate('theme.darkAction')"
      [attr.title]="theme.isDark() ? i18n.translate('theme.lightAction') : i18n.translate('theme.darkAction')"
      (click)="theme.toggleTheme()"
    >
      @if (theme.isDark()) {
        <svg lucideSun class="theme-toggle__icon" aria-hidden="true"></svg>
      } @else {
        <svg lucideMoon class="theme-toggle__icon" aria-hidden="true"></svg>
      }
      <span>{{ theme.isDark() ? i18n.translate('theme.light') : i18n.translate('theme.dark') }}</span>
    </button>
  `,
  styleUrl: './coglatas-theme-toggle.component.scss',
})
export class CoglatasThemeToggleComponent {
  readonly i18n = inject(I18nService);
  readonly theme = inject(CoglatasThemeService);
}
