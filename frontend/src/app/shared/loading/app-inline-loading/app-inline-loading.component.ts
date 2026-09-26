import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-inline-loading',
  standalone: true,
  template: `
    <span class="inline-loading" role="status" aria-live="polite">
      <span class="inline-loading__spinner" aria-hidden="true"></span>
      <span>{{ label }}</span>
    </span>
  `,
  styles: [
    `
      .inline-loading {
        display: inline-flex;
        gap: 0.5rem;
        align-items: center;
        color: var(--coglatas-color-text-secondary);
        font-size: 0.925rem;
      }

      .inline-loading__spinner {
        width: 1rem;
        height: 1rem;
        border: 2px solid var(--coglatas-color-border-default);
        border-top-color: var(--coglatas-color-action-primary);
        border-radius: var(--coglatas-radius-pill);
        animation: app-inline-loading-spin 0.8s linear infinite;
      }

      @keyframes app-inline-loading-spin {
        to {
          transform: rotate(360deg);
        }
      }
    `
  ],
})
export class AppInlineLoadingComponent {
  @Input() label = '読み込み中です。';
}
