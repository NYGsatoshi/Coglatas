import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { FileRowComponent } from '../file-row/file-row.component';
import { FileViewModel } from '../files.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-recent-files-list',
  standalone: true,
  imports: [FileRowComponent],
  templateUrl: './recent-files-list.component.html',
  styleUrl: './recent-files-list.component.scss',
})
export class RecentFilesListComponent {
  readonly i18n = injectFilesI18n();

  @Input({ required: true }) files: readonly FileViewModel[] = [];
  @Input() selectedFileIds: ReadonlySet<string> = new Set();
  @Output() readonly previewRequested = new EventEmitter<FileViewModel>();
  @Output() readonly downloadRequested = new EventEmitter<string>();
  @Output() readonly selectionChanged = new EventEmitter<{ file: FileViewModel; selected: boolean }>();
}
