import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';

import { TaskStatus } from '../projects.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-status-badge',
  standalone: true,
  template: `<span class="task-status-badge" [attr.data-status]="status">{{ canonicalStatus || !label ? statusLabel() : label }}</span>`,
  styles: [
    `
      .task-status-badge {
        display: inline-flex;
        align-items: center;
        min-height: 1.5rem;
        padding: 0.125rem 0.5rem;
        border: 1px solid #a8b3cf;
        border-radius: 999px;
        background: #f7f9fc;
        color: #172033;
        font-size: 0.8125rem;
        font-weight: 600;
        white-space: nowrap;
      }

      .task-status-badge[data-status='blocked'] {
        border-color: #d77b7b;
        background: #fff4f4;
      }

      .task-status-badge[data-status='done'] {
        border-color: #6aa47a;
        background: #f0faf3;
      }

      .task-status-badge[data-status='cancelled'] {
        border-color: #a8b3cf;
        background: #f1f5f9;
      }
    `
  ],
})
export class TaskStatusBadgeComponent {
  readonly i18n = injectProjectsI18n();
  @Input({ required: true }) status: TaskStatus = 'notStarted';
  @Input({ required: true }) label = '';
  @Input() canonicalStatus = false;

  statusLabel(): string { return this.i18n.translate(`projectExecution.taskStatus.${this.status}`); }
}
