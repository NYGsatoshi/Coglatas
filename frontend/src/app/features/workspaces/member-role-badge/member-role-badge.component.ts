import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { WorkspaceMemberGridRow, WorkspaceMemberRole } from '../members.types';

interface MemberRoleBadgeCellParams {
  readonly data?: WorkspaceMemberGridRow;
  readonly value?: unknown;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-member-role-badge',
  standalone: true,
  template: `<span [class]="'role-badge role-badge--' + role">{{ i18n.translate(role === 'owner' ? 'members.role.owner' : role === 'teacher' ? 'members.role.teacher' : role === 'viewer' ? 'members.role.viewer' : 'members.role.member') }}</span>`,
  styles: [
    `
      .role-badge {
        border: 1px solid #cbd5e1;
        border-radius: 999px;
        display: inline-flex;
        font-size: 0.8125rem;
        font-weight: 700;
        line-height: 1;
        padding: 0.35rem 0.55rem;
        white-space: nowrap;
      }

      .role-badge--owner {
        background: #ecfeff;
        border-color: #67e8f9;
        color: #155e75;
      }

      .role-badge--teacher {
        background: #f0fdf4;
        border-color: #86efac;
        color: #166534;
      }

      .role-badge--member {
        background: #eff6ff;
        border-color: #93c5fd;
        color: #1d4ed8;
      }

      .role-badge--viewer {
        background: #f8fafc;
        border-color: #cbd5e1;
        color: #334155;
      }
    `
  ],
})
export class MemberRoleBadgeComponent {
  readonly i18n = inject(I18nService);
  @Input() role: WorkspaceMemberRole = 'member';
  @Input() label = 'メンバー';

  agInit(params: MemberRoleBadgeCellParams): void {
    this.role = params.data?.role ?? 'member';
    this.label = params.data?.roleLabel ?? String(params.value ?? 'メンバー');
  }

  refresh(params: MemberRoleBadgeCellParams): boolean {
    this.agInit(params);
    return true;
  }
}
