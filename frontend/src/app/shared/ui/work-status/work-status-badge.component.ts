import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import {
  LucideArchive,
  LucideCircle,
  LucideCircleCheck,
  LucideCircleX,
  LucideEye,
  LucidePause,
  LucidePlay,
  LucideTriangleAlert
} from '@lucide/angular';

import { WorkStatus } from './work-status';

@Component({
  selector: 'app-work-status-badge',
  standalone: true,
  imports: [
    LucideArchive,
    LucideCircle,
    LucideCircleCheck,
    LucideCircleX,
    LucideEye,
    LucidePause,
    LucidePlay,
    LucideTriangleAlert
  ],
  template: `
    <span
      class="work-status"
      [attr.data-work-status]="status"
      [attr.aria-label]="i18n.translate('workStatus.aria', { status: label })"
    >
      @switch (status) {
        @case ('draft') {
          <svg lucideCircle class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('running') {
          <svg lucidePlay class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('ready') {
          <svg lucideCircleCheck class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('needsReview') {
          <svg lucideEye class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('needsAttention') {
          <svg lucideTriangleAlert class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('completed') {
          <svg lucideCircleCheck class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('cancelled') {
          <svg lucideCircleX class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('paused') {
          <svg lucidePause class="work-status__icon" aria-hidden="true"></svg>
        }
        @case ('archived') {
          <svg lucideArchive class="work-status__icon" aria-hidden="true"></svg>
        }
      }
      <span>{{ label }}</span>
    </span>
  `,
  styles: [
    `
      :host {
        display: inline-flex;
      }

      .work-status {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        min-height: 26px;
        padding: 3px 8px;
        border: 1px solid var(--coglatas-color-border-default);
        border-radius: 5px;
        background: var(--coglatas-color-bg-elevated);
        color: var(--coglatas-color-text-secondary);
        font-size: 11px;
        font-weight: 600;
        line-height: 1.4;
        white-space: nowrap;
      }

      .work-status__icon {
        width: 0.9rem;
        height: 0.9rem;
        flex: 0 0 auto;
        stroke-width: 2.25;
      }

      .work-status[data-work-status='running'] {
        background: var(--coglatas-color-bg-selected);
        color: var(--coglatas-color-action-primary);
      }

      .work-status[data-work-status='ready'] { color: var(--coglatas-color-action-primary); }
      .work-status[data-work-status='needsReview'] { color: var(--coglatas-color-violet); }
      .work-status[data-work-status='needsAttention'] { color: var(--coglatas-color-warning); }
      .work-status[data-work-status='completed'] { color: var(--coglatas-color-success); }
      .work-status[data-work-status='cancelled'] { color: var(--coglatas-color-danger); }
    `
  ],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkStatusBadgeComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) status!: WorkStatus;

  get label(): string {
    return this.i18n.workStatusLabel(this.status);
  }
}
