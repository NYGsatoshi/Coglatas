import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { WorkspaceQuickActionsComponent } from '../workspace-quick-actions/workspace-quick-actions.component';
import { WorkspaceCardViewModel } from '../workspaces.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-card',
  standalone: true,
  imports: [WorkspaceQuickActionsComponent],
  templateUrl: './workspace-card.component.html',
  styleUrl: './workspace-card.component.scss',
})
export class WorkspaceCardComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) workspace!: WorkspaceCardViewModel;

  roleLabel(): string {
    if (this.workspace.accessSource === 'SystemAdmin') {return this.i18n.translate('workspace.systemAdminAccess');}
    const role = this.workspace.currentUserRole;
    return role ? this.i18n.translate(`workspace.role.${role}`) : this.i18n.translate('workspace.roleUnavailable');
  }

  unavailableLabel(available: boolean, value: number | string | null): string {
    return available && value !== null
      ? typeof value === 'number' ? this.i18n.formatNumber(value) : value
      : this.i18n.translate('workspace.unavailable');
  }
}
