import { Component, EventEmitter, Input, Output } from '@angular/core';

import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { FileMessagePresentation, renderFileMessage } from '../files-presentation';
import { FileUploadViewModel } from '../files.types';

@Component({
  selector: 'app-upload-drop-zone',
  standalone: true,
  templateUrl: './upload-drop-zone.component.html',
  styleUrl: './upload-drop-zone.component.scss'
})
export class UploadDropZoneComponent {
  readonly i18n = injectFilesI18n();

  @Input({ required: true }) upload!: FileUploadViewModel;
  @Input({ required: true }) maxUploadBytes = 0;
  @Output() readonly uploadAccepted = new EventEmitter<File>();
  @Output() readonly uploadRejected = new EventEmitter<string>();

  get canUpload(): boolean {
    return this.upload.canUpload === true;
  }

  onFileInputChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.handleFiles(input.files);
    input.value = '';
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.handleFiles(event.dataTransfer?.files ?? null);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  handleFiles(files: FileList | readonly File[] | null): void {
    const file = files?.[0];
    if (!file) {
      return;
    }

    if (this.upload.state === 'pending' || this.upload.state === 'progress') {
      return;
    }

    if (file.size > this.maxUploadBytes) {
      this.uploadRejected.emit(file.name);
      return;
    }

    if (!this.canUpload) {
      return;
    }

    this.uploadAccepted.emit(file);
  }

  message(value: FileMessagePresentation): string { return renderFileMessage(this.i18n, value); }

  formatBytes(bytes: number): string {
    return this.i18n.formatFileSize(bytes);
  }
}
