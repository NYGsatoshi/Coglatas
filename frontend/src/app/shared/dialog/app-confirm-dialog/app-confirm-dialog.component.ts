import { I18nService } from '../../../core/i18n/i18n.service';
import { A11yModule } from '@angular/cdk/a11y';
import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  OnChanges,
  Output,
  ViewChild, inject } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [A11yModule],
  template: `
    @if (open) {
      <div class="dialog-backdrop" role="presentation">
        <section
          #dialogPanel
          class="dialog"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="titleId"
          [attr.aria-describedby]="descriptionId"
          tabindex="-1"
          [cdkTrapFocus]="open"
          [cdkTrapFocusAutoCapture]="open"
        >
          <h2 [id]="titleId">{{ title || i18n.translate('shared.confirmTitle') }}</h2>
          <p [id]="descriptionId">{{ message || i18n.translate('shared.confirmMessage') }}</p>
          <div class="dialog__actions">
            <button #cancelButton type="button" class="dialog__secondary" (click)="close(false)">{{ cancelLabel || i18n.translate('common.cancel') }}</button>
            <button type="button" class="dialog__primary" (click)="close(true)">{{ confirmLabel || i18n.translate('shared.proceed') }}</button>
          </div>
        </section>
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
        width: min(100%, 28rem);
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

      h2 {
        font-size: 1.125rem;
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
        border: 1px solid #b91c1c;
        background: #b91c1c;
        color: white;
      }
    `
  ],
})
export class AppConfirmDialogComponent implements AfterViewChecked, OnChanges {
  readonly i18n = inject(I18nService);
  @Input() open = false;
  @Input() title = '';
  @Input() message = '';
  @Input() confirmLabel = '';
  @Input() cancelLabel = '';
  @Input() titleId = 'app-confirm-dialog-title';
  @Input() descriptionId = 'app-confirm-dialog-description';
  @Input() closeOnEscape = true;
  @Input() returnFocusTo: HTMLElement | null = null;
  @Output() confirm = new EventEmitter<void>();
  @Output() cancel = new EventEmitter<void>();
  @ViewChild('dialogPanel') dialogPanel?: ElementRef<HTMLElement>;
  @ViewChild('cancelButton') cancelButton?: ElementRef<HTMLButtonElement>;

  private fallbackReturnFocus: HTMLElement | null = null;
  private focusedCurrentOpen = false;

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: KeyboardEvent): void {
    if (!this.open || !this.closeOnEscape) {
      return;
    }

    event.preventDefault();
    this.close(false);
  }

  ngOnChanges(): void {
    if (this.open && !this.fallbackReturnFocus) {
      const activeElement = document.activeElement;
      this.fallbackReturnFocus = activeElement instanceof HTMLElement ? activeElement : null;
    }

    if (this.open) {
      this.focusedCurrentOpen = false;
    }
  }

  ngAfterViewChecked(): void {
    if (!this.open || this.focusedCurrentOpen) {
      return;
    }

    const focusTarget = this.cancelButton?.nativeElement ?? this.dialogPanel?.nativeElement;
    focusTarget?.focus();
    this.focusedCurrentOpen = true;
  }

  close(confirmed: boolean): void {
    if (confirmed) {
      this.confirm.emit();
    } else {
      this.cancel.emit();
    }

    queueMicrotask(() => {
      const target = this.returnFocusTo ?? this.fallbackReturnFocus;
      target?.focus();
      this.fallbackReturnFocus = null;
      this.focusedCurrentOpen = false;
    });
  }
}
