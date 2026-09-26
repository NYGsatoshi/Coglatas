import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { RealtimeConnectionState } from './realtime.models';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-realtime-connection-indicator',
  standalone: true,
  template: `
    @if (enabled()) {
    <p class="realtime-indicator" data-testid="realtime-connection-state" aria-live="polite" aria-atomic="true"
      [class.realtime-indicator--quiet]="state() === 'Connected'"
      [class.realtime-indicator--attention]="state() === 'Reconnecting'">
      {{ label() }}
    </p>
    }
  `,
  styles: `
    .realtime-indicator { margin: 0; color: var(--coglatas-color-text-secondary); font-size: var(--coglatas-font-size-sm, .875rem); }
    .realtime-indicator--quiet { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
    .realtime-indicator--attention { color: var(--coglatas-color-warning, #b7791f); }
    @media (prefers-reduced-motion: reduce) { .realtime-indicator { transition: none; } }
  `,
})
export class RealtimeConnectionIndicatorComponent {
  readonly state = input.required<RealtimeConnectionState>();
  readonly enabled = input(true);

  label(): string {
    switch (this.state()) {
      case 'Connected':
        return 'Realtime updates connected.';
      case 'Reconnecting':
        return 'Reconnecting realtime updates.';
      case 'Degraded':
        return 'Realtime updates are delayed. HTTP and manual refresh remain available.';
      case 'Offline':
        return 'Offline. Core requests may be unavailable.';
    }
  }
}
