import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-account-rail',
  standalone: true,
  template: `
    <aside class="account-rail" [attr.aria-label]="i18n.translate('shell.accountSwitcher')">
      <div class="account-rail__brand" aria-hidden="true">C</div>
      <div class="account-rail__users" [attr.aria-label]="i18n.translate('shell.signedInUsers')">
        <span class="account-rail__avatar account-rail__avatar--active">{{ initials(displayName) }}</span>
        @for (user of supportingUsers; track user) {
          <span class="account-rail__avatar">{{ initials(user) }}</span>
        }
      </div>
    </aside>
  `,
  styleUrl: './account-rail.component.scss',
})
export class AccountRailComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) displayName = '';
  @Input() supportingUsers: readonly string[] = [];

  initials(name: string): string {
    return name.trim().slice(0, 1) || 'A';
  }
}
