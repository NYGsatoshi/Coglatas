import { A11yModule } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  HostListener,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';

@Component({
  selector: 'app-coglatas-dialog',
  standalone: true,
  imports: [A11yModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open) {
      <div class="coglatas-dialog__backdrop" (mousedown)="handleBackdrop($event)">
        <section
          class="coglatas-dialog"
          [class.coglatas-dialog--wide]="size === 'wide'"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="titleId"
          [attr.aria-describedby]="description ? descriptionId : null"
          [attr.aria-busy]="busy"
          tabindex="-1"
          [cdkTrapFocus]="open"
          [cdkTrapFocusAutoCapture]="open"
          (mousedown)="$event.stopPropagation()"
        >
          <header class="coglatas-dialog__header">
            <div>
              <h2 [id]="titleId">{{ title }}</h2>
              @if (description) {
                <p [id]="descriptionId">{{ description }}</p>
              }
            </div>
            <button type="button" class="coglatas-dialog__close" [disabled]="busy" [attr.aria-label]="i18n.translate('common.close')" (click)="requestCancel()">×</button>
          </header>

          <div class="coglatas-dialog__content">
            <ng-content />
          </div>

          <footer class="coglatas-dialog__actions">
            <button type="button" [disabled]="busy" (click)="requestCancel()">{{ cancelLabel || i18n.translate('common.cancel') }}</button>
            <button
              [attr.type]="confirmForm ? 'submit' : 'button'"
              [attr.form]="confirmForm"
              class="coglatas-dialog__confirm"
              [class.coglatas-dialog__confirm--destructive]="destructive"
              [disabled]="busy || confirmDisabled"
              (click)="requestConfirm()"
            >
              {{ busy ? i18n.translate('common.working') : (confirmLabel || i18n.translate('common.confirm')) }}
            </button>
          </footer>
        </section>
      </div>
    }
  `,
  styles: [`
    :host { display: contents; }
    .coglatas-dialog__backdrop { position: fixed; inset: 0; z-index: 1000; display: grid; place-items: center; padding: 1rem; background: var(--coglatas-color-overlay, rgb(0 0 0 / 58%)); }
    .coglatas-dialog { width: min(42rem, 100%); max-height: min(48rem, calc(100vh - 2rem)); overflow: auto; border: 1px solid var(--coglatas-color-border-default, #48505e); border-radius: var(--coglatas-radius-lg, 0.75rem); background: var(--coglatas-color-bg-elevated, #171b22); color: var(--coglatas-color-text-primary, #f4f7fb); box-shadow: var(--coglatas-shadow-floating, 0 1.5rem 4rem rgb(0 0 0 / 45%)); }
    .coglatas-dialog--wide { width: min(72rem, 100%); }
    .coglatas-dialog__header, .coglatas-dialog__actions { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; padding: 1rem 1.25rem; }
    .coglatas-dialog__header { border-bottom: 1px solid var(--coglatas-color-border-default, #48505e); }
    .coglatas-dialog__header h2, .coglatas-dialog__header p { margin: 0; }
    .coglatas-dialog__header p { margin-top: 0.35rem; color: var(--coglatas-color-text-secondary, #b8c0cc); }
    .coglatas-dialog__content { padding: 1.25rem; }
    .coglatas-dialog__actions { justify-content: flex-end; border-top: 1px solid var(--coglatas-color-border-default, #48505e); }
    button { min-height: 2.75rem; border: 1px solid var(--coglatas-color-border-strong, #687282); border-radius: 0.5rem; padding: 0.5rem 0.9rem; background: var(--coglatas-color-bg-control, #242b35); color: var(--coglatas-color-text-primary, inherit); cursor: pointer; }
    button:focus-visible { outline: 3px solid var(--coglatas-color-focus, #79a9ff); outline-offset: 2px; }
    button:disabled { cursor: not-allowed; opacity: 0.55; }
    .coglatas-dialog__close { min-width: 2.75rem; padding: 0.25rem; font-size: 1.35rem; }
    .coglatas-dialog__confirm { border-color: var(--coglatas-color-action-primary, #5794ff); background: var(--coglatas-color-action-primary, #2764c5); color: var(--coglatas-color-text-inverse, #fff); }
    .coglatas-dialog__confirm--destructive { border-color: var(--coglatas-color-danger, #e26565); background: var(--coglatas-color-danger, #a93232); color: var(--coglatas-color-text-inverse, #fff); }
  `]
})
export class CoglatasDialogComponent implements OnChanges {
  private static nextInstanceId = 0;

  protected readonly i18n = inject(I18nService);
  private readonly instanceId = CoglatasDialogComponent.nextInstanceId++;
  private invocationFocus: HTMLElement | null = null;
  private focusTransition = 0;

  @Input() open = false;
  @Input() title = 'Dialog';
  @Input() description: string | null = null;
  @Input() titleId = `coglatas-dialog-title-${this.instanceId}`;
  @Input() descriptionId = `coglatas-dialog-description-${this.instanceId}`;
  @Input() confirmLabel = '';
  @Input() cancelLabel = '';
  @Input() confirmForm: string | null = null;
  @Input() focusReturnFallbackId: string | null = null;
  @Input() busy = false;
  @Input() confirmDisabled = false;
  @Input() destructive = false;
  @Input() size: 'default' | 'wide' = 'default';

  @Output() readonly confirm = new EventEmitter<void>();
  @Output() readonly cancel = new EventEmitter<void>();
  @Output() readonly closed = new EventEmitter<void>();

  ngOnChanges(changes: SimpleChanges): void {
    const openChange = changes['open'];
    if (!openChange || openChange.firstChange || openChange.previousValue === openChange.currentValue) {
      return;
    }

    if (openChange.currentValue) {
      this.focusTransition += 1;
      const activeElement = document.activeElement;
      this.invocationFocus = activeElement instanceof HTMLElement ? activeElement : null;
      return;
    }

    const transition = ++this.focusTransition;
    const returnFocusTo = this.invocationFocus;
    this.invocationFocus = null;

    queueMicrotask(() => {
      if (this.focusTransition !== transition || this.open) {
        return;
      }

      const focusTarget = returnFocusTo?.isConnected
        ? returnFocusTo
        : this.focusReturnFallbackId
          ? document.getElementById(this.focusReturnFallbackId)
          : null;
      focusTarget?.focus();
    });
  }

  @HostListener('document:keydown.escape', ['$event'])
  handleEscape(event: KeyboardEvent): void {
    if (!this.open) {
      return;
    }

    event.preventDefault();
    this.requestCancel();
  }

  handleBackdrop(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.requestCancel();
    }
  }

  requestCancel(): void {
    if (this.busy) {
      return;
    }

    this.cancel.emit();
    this.closed.emit();
  }

  requestConfirm(): void {
    if (this.busy || this.confirmDisabled) {
      return;
    }

    if (!this.confirmForm) {
      this.confirm.emit();
    }
  }
}
