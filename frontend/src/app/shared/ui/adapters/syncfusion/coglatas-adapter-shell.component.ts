import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { CoglatasAdapterPresentation, CoglatasAdapterState, CoglatasComplexAdapterName } from '../../contracts/coglatas-complex-adapter.contracts';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'coglatas-adapter-shell',
  standalone: true,
  template: `
    <section
      class="coglatas-adapter-shell"
      [attr.aria-label]="ariaLabel"
      [attr.data-coglatas-adapter]="adapter"
      [attr.data-coglatas-presentation]="presentation"
      [attr.data-coglatas-state]="state"
      [attr.data-testid]="'coglatas-' + adapter + '-adapter'">
      <span class="coglatas-adapter-shell__label">{{ label }}</span>
      <span class="coglatas-adapter-shell__state" aria-live="polite">{{ stateLabel }}</span>
      <ng-content />
    </section>
  `,
  styleUrl: './coglatas-adapter-shell.component.scss',
})
export class CoglatasAdapterShellComponent {
  @Input({ required: true }) adapter!: CoglatasComplexAdapterName;
  @Input({ required: true }) ariaLabel!: string;
  @Input() label = 'Coglatas adapter fallback';
  @Input() presentation: CoglatasAdapterPresentation = 'desktop';
  @Input() state: CoglatasAdapterState = 'ready';

  get stateLabel(): string {
    return this.state.replace('-', ' ');
  }
}
