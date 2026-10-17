import { I18nService } from '../../../core/i18n/i18n.service';
import { A11yModule } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, EventEmitter, HostListener, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-audit-reason-dialog',
  standalone: true,
  imports: [A11yModule, FormsModule],
  template: `
    @if (open) {
      <div class="dialog-backdrop" role="presentation">
        <form
          class="dialog"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="titleId"
          [cdkTrapFocus]="open"
          [cdkTrapFocusAutoCapture]="open"
          (ngSubmit)="submit()"
        >
          <h2 [id]="titleId">{{ title || i18n.translate('shared.reasonTitle') }}</h2>
          <label>
            <span>{{ i18n.translate('shared.reason') }}</span>
            <textarea required minlength="3" name="reason" [(ngModel)]="reason" [attr.aria-describedby]="hintId"></textarea>
          </label>
          <p [id]="hintId">{{ i18n.translate('shared.reasonHelp') }}</p>
          <div class="dialog__actions">
            <button type="button" class="dialog__secondary" (click)="cancel.emit()">{{ i18n.translate('common.cancel') }}</button>
            <button type="submit" class="dialog__primary" [disabled]="!canSubmit">{{ i18n.translate('shared.submit') }}</button>
          </div>
        </form>
      </div>
    }
  `,
  styles: [
    `
      .dialog-backdrop {
        position: fixed;
        inset: 0;
        display: grid;
        place-items: center;
        background: rgb(15 23 42 / 0.45);
        padding: 1rem;
        z-index: 50;
      }

      .dialog {
        display: grid;
        gap: 1rem;
        width: min(100%, 30rem);
        border-radius: 8px;
        background: white;
        padding: 1.25rem;
        color: #0f172a;
        box-shadow: 0 24px 60px rgb(15 23 42 / 0.2);
      }

      h2,
      p {
        margin: 0;
      }

      label {
        display: grid;
        gap: 0.35rem;
        font-weight: 700;
      }

      textarea {
        min-height: 7rem;
        resize: vertical;
        border: 1px solid #94a3b8;
        border-radius: 6px;
        padding: 0.625rem;
        font: inherit;
      }

      .dialog__actions {
        display: flex;
        flex-wrap: wrap;
        justify-content: flex-end;
        gap: 0.5rem;
      }

      button {
        border-radius: 6px;
        padding: 0.5rem 0.75rem;
        font-weight: 700;
      }

      .dialog__secondary {
        border: 1px solid #cbd5e1;
        background: white;
        color: #334155;
      }

      .dialog__primary {
        border: 1px solid #2563eb;
        background: #2563eb;
        color: white;
      }

      .dialog__primary:disabled {
        cursor: not-allowed;
        opacity: 0.55;
      }
    `
  ],
})
export class AppAuditReasonDialogComponent {
  readonly i18n = inject(I18nService);
  @Input() open = false;
  @Input() title = '';
  @Input() titleId = 'app-audit-reason-dialog-title';
  @Input() hintId = 'app-audit-reason-dialog-hint';
  @Output() reasonSubmit = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<void>();

  reason = '';

  get canSubmit(): boolean {
    return this.reason.trim().length >= 3;
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: KeyboardEvent): void {
    if (!this.open) {
      return;
    }

    event.preventDefault();
    this.cancel.emit();
  }

  submit(): void {
    if (!this.canSubmit) {
      return;
    }

    this.reasonSubmit.emit(this.reason.trim());
  }
}
