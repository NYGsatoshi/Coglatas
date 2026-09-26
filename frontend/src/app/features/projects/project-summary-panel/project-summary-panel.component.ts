import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { WorkStatusBadgeComponent } from '../../../shared/ui/work-status/work-status-badge.component';
import { projectWorkStatus } from '../projects.mapper';
import { ProjectSummaryViewModel } from '../projects.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-project-summary-panel',
  standalone: true,
  imports: [RouterLink, WorkStatusBadgeComponent],
  template: `
    <section class="project-summary-panel" data-testid="project-summary-panel" data-presentation="card">
      @for (project of projects; track project.id) {
        <article class="project-summary-panel__item" data-testid="project-summary-card">
          <div class="project-summary-panel__primary">
            <div class="project-summary-panel__identity">
              <a [routerLink]="['/projects', project.id]" [attr.aria-label]="'Open ' + project.name">
                <h2>{{ project.name }}</h2>
              </a>
              <p class="project-summary-panel__updated" data-testid="project-updated-at">
                <span>Updated</span>
                @if (project.updatedAt) {
                  <time [attr.datetime]="project.updatedAt">{{ formatUpdatedAt(project.updatedAt) }}</time>
                } @else {
                  <span>Not available</span>
                }
              </p>
            </div>
            <app-work-status-badge
              [status]="workStatus(project.status)"
              [attr.data-testid]="'project-status-' + project.id"
            />
          </div>

          <details class="project-summary-panel__secondary">
            <summary>Project details</summary>
            <dl>
              <div>
                <dt>Start</dt>
                <dd>{{ project.startDate || 'Not set' }}</dd>
              </div>
              <div>
                <dt>Due</dt>
                <dd>{{ project.dueDate || 'Not set' }}</dd>
              </div>
              <div>
                <dt>Tasks</dt>
                <dd>
                  {{ project.taskCounts.done }}/{{ project.taskCounts.total }} done
                  @if (project.taskCounts.blocked > 0) {
                    <span> · {{ project.taskCounts.blocked }} blocked</span>
                  }
                </dd>
              </div>
            </dl>
          </details>
        </article>
      }
    </section>
  `,
  styles: [
    `
      .project-summary-panel {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
        gap: 0.75rem;
      }

      .project-summary-panel__item {
        display: grid;
        gap: 0.75rem;
        min-width: 0;
        padding: var(--coglatas-space-4);
        border: 1px solid var(--coglatas-color-border-default);
        border-radius: var(--coglatas-radius-lg);
        background: var(--coglatas-color-bg-surface);
      }

      .project-summary-panel__primary {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        gap: 0.75rem;
      }

      .project-summary-panel__identity {
        display: grid;
        gap: 0.35rem;
        min-width: 0;
      }

      .project-summary-panel h2,
      .project-summary-panel dl,
      .project-summary-panel p {
        margin: 0;
      }

      .project-summary-panel h2 {
        font-size: 1.05rem;
        overflow-wrap: anywhere;
      }

      .project-summary-panel a {
        min-width: 0;
        color: inherit;
        text-decoration: none;
      }

      .project-summary-panel a:hover,
      .project-summary-panel a:focus-visible {
        text-decoration: underline;
      }

      .project-summary-panel__updated {
        display: flex;
        flex-wrap: wrap;
        gap: 0.3rem;
        color: var(--coglatas-color-text-secondary);
        font-size: 0.8rem;
      }

      .project-summary-panel__updated time,
      .project-summary-panel__updated > span:last-child {
        color: var(--coglatas-color-text-primary);
        font-weight: 650;
      }

      .project-summary-panel__secondary {
        border-top: 1px solid var(--coglatas-color-border-default);
        padding-top: 0.625rem;
        color: var(--coglatas-color-text-secondary);
        font-size: 0.875rem;
      }

      .project-summary-panel__secondary summary {
        width: fit-content;
        cursor: pointer;
        font-weight: 600;
      }

      .project-summary-panel__secondary dl {
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 0.5rem;
        margin-top: 0.625rem;
      }

      .project-summary-panel__secondary dt {
        font-size: 0.75rem;
      }

      .project-summary-panel__secondary dd {
        margin: 0.125rem 0 0;
        color: var(--coglatas-color-text-primary);
        font-weight: 650;
      }

      @media (max-width: 36rem) {
        .project-summary-panel__primary {
          align-items: flex-start;
          flex-direction: column;
        }

        .project-summary-panel__secondary dl {
          grid-template-columns: 1fr;
        }
      }
    `
  ],
})
export class ProjectSummaryPanelComponent {
  @Input() projects: readonly ProjectSummaryViewModel[] = [];

  readonly workStatus = projectWorkStatus;

  formatUpdatedAt(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return 'Not available';
    }

    return new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short'
    }).format(date);
  }
}
