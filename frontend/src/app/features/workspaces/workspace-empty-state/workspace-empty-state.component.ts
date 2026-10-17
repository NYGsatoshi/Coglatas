import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-empty-state',
  standalone: true,
  templateUrl: './workspace-empty-state.component.html',
  styleUrl: './workspace-empty-state.component.scss',
})
export class WorkspaceEmptyStateComponent {
  readonly i18n = inject(I18nService);
  @Input() title = '';
  @Input() message = '';
  @Input() actionLabel: string | null = null;
  @Output() readonly action = new EventEmitter<void>();
}
