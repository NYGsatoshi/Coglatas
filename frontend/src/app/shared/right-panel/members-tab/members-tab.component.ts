import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { AppPermissionDeniedComponent } from '../../permission/app-permission-denied/app-permission-denied.component';
import { MemberListItemComponent } from '../member-list-item/member-list-item.component';
import { RightPanelMember, RightPanelPermission } from '../right-panel.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-members-tab',
  standalone: true,
  imports: [AppPermissionDeniedComponent, MemberListItemComponent],
  template: `
    <section class="members-tab" [attr.aria-label]="i18n.translate('panel.members')">
      @if (permission === 'denied') {
        <app-permission-denied [title]="i18n.translate('panel.membersDenied')" />
      } @else {
        <p class="members-tab__note">{{ i18n.translate('panel.memberScope') }}</p>
        @if (members.length > 0) {
          <ul class="members-tab__list">
            @for (member of members; track member.id) {
              <li app-member-list-item [member]="member"></li>
            }
          </ul>
        } @else {
          <p class="members-tab__empty">{{ i18n.translate('panel.noMembers') }}</p>
        }
      }
    </section>
  `,
  styleUrl: './members-tab.component.scss',
})
export class MembersTabComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) members: readonly RightPanelMember[] = [];
  @Input() permission: RightPanelPermission = 'granted';
}
