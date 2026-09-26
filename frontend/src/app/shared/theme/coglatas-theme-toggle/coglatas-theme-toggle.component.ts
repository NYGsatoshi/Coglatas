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
      [attr.aria-label]="theme.isDark() ? 'Switch to light mode' : 'Switch to dark mode'"
      [attr.title]="theme.isDark() ? 'Switch to light mode' : 'Switch to dark mode'"
      (click)="theme.toggleTheme()"
    >
      <span class="theme-toggle__icon" aria-hidden="true">{{ theme.isDark() ? '☀' : '☾' }}</span>
      <span>{{ theme.isDark() ? 'Light' : 'Dark' }}</span>
    </button>
  `,
  styleUrl: './coglatas-theme-toggle.component.scss',
})
export class CoglatasThemeToggleComponent {
  readonly theme = inject(CoglatasThemeService);
}
