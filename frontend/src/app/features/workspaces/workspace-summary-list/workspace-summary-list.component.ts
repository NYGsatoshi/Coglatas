import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { WorkspaceCardComponent } from '../workspace-card/workspace-card.component';
import { WorkspaceCardViewModel } from '../workspaces.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-summary-list',
  standalone: true,
  imports: [WorkspaceCardComponent],
  templateUrl: './workspace-summary-list.component.html',
  styleUrl: './workspace-summary-list.component.scss',
})
export class WorkspaceSummaryListComponent {
  readonly i18n = inject(I18nService);
  @Input() workspaces: readonly WorkspaceCardViewModel[] = [];
}
