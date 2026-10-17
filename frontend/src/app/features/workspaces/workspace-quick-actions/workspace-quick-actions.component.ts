import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { WorkspaceActionCapability } from '../workspaces.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-quick-actions',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './workspace-quick-actions.component.html',
  styleUrl: './workspace-quick-actions.component.scss',
})
export class WorkspaceQuickActionsComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) workspaceId = '';
  @Input() capabilities: readonly WorkspaceActionCapability[] = [];

  hasCapability(capability: WorkspaceActionCapability): boolean {
    return this.capabilities.includes(capability);
  }
}
