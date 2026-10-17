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
        gap: 0.35rem;
        min-height: 1.75rem;
        padding: 0.2rem 0.55rem;
        border: 1px solid #cbd5e1;
        border-radius: 999px;
        background: #f8fafc;
        color: #334155;
        font-size: 0.78rem;
        font-weight: 700;
        line-height: 1.2;
        white-space: nowrap;
      }

      .work-status__icon {
        width: 0.9rem;
        height: 0.9rem;
        flex: 0 0 auto;
        stroke-width: 2.25;
      }

      .work-status[data-work-status='running'],
      .work-status[data-work-status='ready'] {
        border-color: #93c5fd;
        background: #eff6ff;
        color: #1e3a8a;
      }

      .work-status[data-work-status='needsReview'] {
        border-color: #c4b5fd;
        background: #f5f3ff;
        color: #5b21b6;
      }

      .work-status[data-work-status='needsAttention'] {
        border-color: #fbbf24;
        background: #fffbeb;
        color: #92400e;
      }

      .work-status[data-work-status='completed'] {
        border-color: #86efac;
        background: #f0fdf4;
        color: #166534;
      }

      .work-status[data-work-status='paused'],
      .work-status[data-work-status='cancelled'],
      .work-status[data-work-status='archived'] {
        border-color: #cbd5e1;
        background: #f1f5f9;
        color: #475569;
      }
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
