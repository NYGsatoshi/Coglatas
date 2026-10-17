import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { displayFileName } from '../files-presentation';
import { FileScanStatusBadgeComponent } from '../file-scan-status-badge/file-scan-status-badge.component';
import { FileViewModel } from '../files.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-attachment-picker-dialog',
  standalone: true,
  imports: [FileScanStatusBadgeComponent],
  templateUrl: './attachment-picker-dialog.component.html',
  styleUrl: './attachment-picker-dialog.component.scss',
})
export class AttachmentPickerDialogComponent {
  readonly i18n = injectFilesI18n();

  @Input({ required: true }) files: readonly FileViewModel[] = [];
  @Input() selectedAttachmentId: string | null = null;
  @Input() disabled = true;
  @Input() disabledMessage?: string;

  @Output() selectionChange = new EventEmitter<string | null>();

  fileName(file: FileViewModel): string { return displayFileName(this.i18n, file); }

  toggleFile(file: FileViewModel, checked: boolean): void {
    if (this.disabled || !file.id || file.scanStatus !== 'allowed') {
      return;
    }

    this.selectionChange.emit(checked ? file.id : null);
  }
}
