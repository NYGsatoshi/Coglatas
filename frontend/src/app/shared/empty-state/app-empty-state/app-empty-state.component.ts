import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-empty-state',
  standalone: true,
  template: `
    <section class="empty-state" [attr.aria-labelledby]="titleId">
      <h2 [id]="titleId">{{ title }}</h2>
      <p>{{ message }}</p>
      @if (actionLabel) {
        <button type="button" (click)="action.emit()">{{ actionLabel }}</button>
      }
    </section>
  `,
  styles: [
    `
      .empty-state {
        display: grid;
        justify-items: start;
        gap: var(--coglatas-component-gap);
        border: 1px dashed var(--coglatas-color-border-default);
        border-radius: var(--coglatas-radius-lg);
        background: var(--coglatas-color-bg-surface-subtle);
        padding: var(--coglatas-space-5);
        color: var(--coglatas-color-text-secondary);
      }

      h2,
      p {
        margin: 0;
      }

      h2 {
        font-size: 1rem;
      }

      button {
        min-height: var(--coglatas-touch-target);
        border: 1px solid var(--coglatas-color-action-primary);
        border-radius: var(--coglatas-radius-md);
        background: var(--coglatas-color-action-primary);
        padding: var(--coglatas-space-2) var(--coglatas-space-3);
        color: var(--coglatas-color-text-inverse);
        font-weight: 700;
      }
    `
  ],
})
export class AppEmptyStateComponent {
  @Input() title = '表示する項目がありません。';
  @Input() message = '条件を変更するか、後でもう一度確認してください。';
  @Input() titleId = 'app-empty-state-title';
  @Input() actionLabel = '';
  @Output() action = new EventEmitter<void>();
}
