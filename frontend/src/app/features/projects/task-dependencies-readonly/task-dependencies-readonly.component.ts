import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';

import { TaskDependencyViewModel } from '../projects.types';
import { TaskStatusBadgeComponent } from '../task-status-badge/task-status-badge.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-dependencies-readonly',
  standalone: true,
  imports: [TaskStatusBadgeComponent],
  template: `
    <section class="task-dependencies" data-testid="task-dependencies-readonly">
      <header>
        <h2>{{ i18n.translate('projectExecution.dependencies.title') }}</h2>
        <p data-testid="dependencies-display-only-note">{{ i18n.translate('projectExecution.dependencies.note') }}</p>
      </header>

      @if (dependencies.length > 0) {
        <ul>
          @for (dependency of dependencies; track dependency.id) {
            <li>
              <span>{{ dependency.title }}</span>
              <app-task-status-badge [status]="dependency.status" [label]="dependency.status" [canonicalStatus]="true" />
            </li>
          }
        </ul>
      } @else {
        <p class="task-dependencies__empty">{{ i18n.translate('projectExecution.dependencies.empty') }}</p>
      }
    </section>
  `,
  styles: [
    `
      .task-dependencies {
        display: grid;
        gap: 0.75rem;
      }

      .task-dependencies header {
        display: grid;
        gap: 0.25rem;
      }

      .task-dependencies h2 {
        margin: 0;
        font-size: 1rem;
      }

      .task-dependencies p {
        margin: 0;
        color: #536179;
      }

      .task-dependencies ul {
        display: grid;
        gap: 0.5rem;
        padding: 0;
        margin: 0;
        list-style: none;
      }

      .task-dependencies li {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.75rem;
        padding: 0.75rem;
        border: 1px solid #d7deea;
        border-radius: 0.5rem;
        background: #fff;
      }

      .task-dependencies__empty {
        padding: 0.75rem;
        border: 1px dashed #c7d0df;
        border-radius: 0.5rem;
      }
    `
  ],
})
export class TaskDependenciesReadonlyComponent {
  readonly i18n = injectProjectsI18n();
  @Input() dependencies: readonly TaskDependencyViewModel[] = [];
}
