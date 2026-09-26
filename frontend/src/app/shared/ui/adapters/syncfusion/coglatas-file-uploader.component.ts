import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';

import { I18nService } from '../../../../core/i18n/i18n.service';

export interface CoglatasFileUploaderItem {
  readonly clientRequestId: string;
  readonly fileName: string;
  readonly state: 'pending' | 'uploading' | 'succeeded' | 'failed' | 'cancelled';
  readonly message?: string;
}

@Component({
  selector: 'app-coglatas-file-uploader',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      class="coglatas-uploader"
      [class.coglatas-uploader--disabled]="disabled"
      [attr.aria-label]="resolvedAriaLabel()"
      [attr.data-adapter]="syncfusionEnabled ? 'syncfusion' : 'native-fallback'"
      (dragover)="handleDragOver($event)"
      (drop)="handleDrop($event)"
    >
      <input
        #fileInput
        class="coglatas-uploader__input"
        type="file"
        [attr.aria-label]="resolvedAriaLabel()"
        [multiple]="multiple"
        [disabled]="disabled"
        (change)="handleInputChange($event)"
      />

      <div class="coglatas-uploader__prompt">
        <strong>{{ i18n.translate('files.upload.title') }}</strong>
        <span>{{ i18n.translate('files.upload.prompt') }}</span>
        <button type="button" [disabled]="disabled" (click)="fileInput.click()">{{ i18n.translate('files.upload.choose') }}</button>
      </div>

      @if (items.length > 0) {
        <ul class="coglatas-uploader__queue" [attr.aria-label]="i18n.translate('files.upload.queue')">
          @for (item of items; track item.clientRequestId) {
            <li>
              <div>
                <strong>{{ item.fileName }}</strong>
                <span>{{ stateLabel(item.state) }}</span>
                @if (item.message) {
                  <small>{{ item.message }}</small>
                }
              </div>

              @if (item.state === 'pending' || item.state === 'uploading') {
                <button type="button" [disabled]="disabled" (click)="cancel.emit(item.clientRequestId)">{{ i18n.translate('files.upload.cancel') }}</button>
              } @else if (item.state === 'failed' || item.state === 'cancelled') {
                <button type="button" [disabled]="disabled" (click)="retry.emit(item.clientRequestId)">{{ i18n.translate('common.retry') }}</button>
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: [`
    :host { display: block; min-width: 0; container-type: inline-size; }
    .coglatas-uploader { min-width: 0; border: 1px dashed var(--coglatas-color-border-strong); border-radius: 0.75rem; padding: 1rem; background: var(--coglatas-color-bg-elevated); color: var(--coglatas-color-text-primary); }
    .coglatas-uploader--disabled { opacity: 0.65; }
    .coglatas-uploader__input { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    .coglatas-uploader__prompt { display: grid; justify-items: start; gap: 0.5rem; }
    .coglatas-uploader__prompt span, .coglatas-uploader__queue span, .coglatas-uploader__queue small { color: var(--coglatas-color-text-secondary); }
    button { min-height: 2.5rem; border: 1px solid var(--coglatas-color-border-strong); border-radius: 0.5rem; padding: 0.5rem 0.9rem; background: var(--coglatas-color-bg-surface-subtle); color: var(--coglatas-color-text-primary); cursor: pointer; }
    button:hover:not(:disabled) { background: var(--coglatas-color-bg-hover); }
    button:focus-visible { outline: var(--coglatas-focus-outline); outline-offset: var(--coglatas-focus-offset); }
    button:disabled { cursor: not-allowed; opacity: 0.55; }
    .coglatas-uploader__queue { display: grid; gap: 0.5rem; margin: 1rem 0 0; padding: 0; list-style: none; }
    .coglatas-uploader__queue li { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 1rem; min-width: 0; border-top: 1px solid var(--coglatas-color-border-default); padding-top: 0.75rem; }
    .coglatas-uploader__queue li > div { display: grid; flex: 1 1 12rem; gap: 0.2rem; min-width: 0; }
    .coglatas-uploader__queue strong, .coglatas-uploader__queue span, .coglatas-uploader__queue small { overflow-wrap: anywhere; }
    @container (max-width: 26rem) { .coglatas-uploader__queue li > button { width: 100%; } }
  `]
})
export class CoglatasFileUploaderComponent {
  readonly i18n = inject(I18nService);

  @Input() ariaLabel?: string;
  @Input() items: readonly CoglatasFileUploaderItem[] = [];
  @Input() multiple = false;
  @Input() disabled = false;
  @Input() syncfusionEnabled = false;

  @Output() readonly filesSelected = new EventEmitter<readonly File[]>();
  @Output() readonly cancel = new EventEmitter<string>();
  @Output() readonly retry = new EventEmitter<string>();

  handleInputChange(event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
      return;
    }

    this.emitFiles(target.files);
    target.value = '';
  }

  handleDragOver(event: DragEvent): void {
    if (this.disabled) {
      return;
    }

    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  handleDrop(event: DragEvent): void {
    if (this.disabled) {
      return;
    }

    event.preventDefault();
    this.emitFiles(event.dataTransfer?.files ?? null);
  }

  stateLabel(state: CoglatasFileUploaderItem['state']): string {
    return this.i18n.fileUploadStateLabel(state);
  }

  resolvedAriaLabel(): string {
    return this.ariaLabel ?? this.i18n.translate('files.upload.ariaLabel');
  }

  private emitFiles(fileList: FileList | null): void {
    if (this.disabled || !fileList || fileList.length === 0) {
      return;
    }

    const selected = Array.from(fileList);
    this.filesSelected.emit(this.multiple ? selected : selected.slice(0, 1));
  }
}
