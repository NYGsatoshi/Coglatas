import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-field-error',
  standalone: true,
  template: `
    @if (messages.length > 0) {
      <div class="field-error" [attr.id]="id" role="alert">
        <ul class="field-error__list">
          @for (message of messages; track message) {
            <li>{{ message }}</li>
          }
        </ul>
      </div>
    }
  `,
  styles: [
    `
      .field-error {
        color: var(--coglatas-color-danger, #b91c1c);
        font-size: 0.875rem;
      }

      .field-error__list {
        display: grid;
        gap: 0.25rem;
        margin: 0.35rem 0 0;
        padding: 0;
        list-style: none;
      }
    `
  ],
})
export class AppFieldErrorComponent {
  @Input() id: string | null = null;
  @Input() messages: readonly string[] = [];
}
