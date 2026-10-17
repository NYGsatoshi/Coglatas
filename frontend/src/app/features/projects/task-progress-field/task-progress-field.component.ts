import { Component, Input } from '@angular/core';

import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';

@Component({
  selector: 'app-task-progress-field',
  standalone: true,
  template: `
    <div class="task-progress-field" data-testid="task-progress-field">
      <span>{{ i18n.formatNumber(value / 100, { style: 'percent', maximumFractionDigits: 2 }) }}</span>
      <meter [attr.aria-label]="i18n.translate('projectExecution.progressAria')" min="0" max="100" [value]="value">{{ i18n.formatNumber(value / 100, { style: 'percent', maximumFractionDigits: 2 }) }}</meter>
    </div>
  `,
  styles: [
    `
      .task-progress-field {
        display: grid;
        gap: 0.25rem;
        min-width: 6rem;
      }

      .task-progress-field span {
        font-weight: 700;
      }

      .task-progress-field meter {
        width: 100%;
      }
    `
  ]
})
export class TaskProgressFieldComponent {
  readonly i18n = injectProjectsI18n();
  @Input({ required: true }) value = 0;
}
