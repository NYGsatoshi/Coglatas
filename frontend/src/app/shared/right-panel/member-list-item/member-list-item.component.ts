import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { RightPanelMember } from '../right-panel.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'li[app-member-list-item]',
  standalone: true,
  template: `
    <div class="member__avatar" aria-hidden="true">{{ initials }}</div>
    <div class="member__body">
      <div class="member__line">
        <strong class="member__name">{{ member.displayName }}</strong>
        <span class="member__status">{{ member.accountStatus === '参加中' ? i18n.translate('members.status.active') : member.accountStatus === '利用停止' ? i18n.translate('members.status.disabled') : i18n.translate('members.status.removed') }}</span>
      </div>
      <p class="member__meta">{{ member.role }} / {{ member.groupLabel }}</p>
      @if (member.availability) {
        <p class="member__availability">{{ i18n.translate('panel.mockAvailability', { availability: availabilityLabel() }) }}</p>
      }
    </div>
  `,
  styleUrl: './member-list-item.component.scss',
})
export class MemberListItemComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) member!: RightPanelMember;

  availabilityLabel(): string {
    switch (this.member.availability) {
      case 'オンライン': return this.i18n.translate('panel.online');
      case 'オフライン': return this.i18n.translate('panel.offline');
      case '退席中': return this.i18n.translate('panel.away');
      case '応答不可': return this.i18n.translate('panel.busy');
      default: return '';
    }
  }

  get initials(): string {
    return this.member.displayName.slice(0, 2);
  }
}
