import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { I18nService } from '../../../core/i18n/i18n.service';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import { FileFolderStore, FileFolderViewModel } from '../file-folders.service';

interface DestinationOption {
  readonly id: string;
  readonly label: string;
  readonly disabled: boolean;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-file-move-dialog',
  standalone: true,
  imports: [CoglatasDialogComponent],
  template: `
    <app-coglatas-dialog
      [open]="open"
      [title]="i18n.translate('files.actions.move')"
      [confirmLabel]="i18n.translate('files.actions.move')"
      [cancelLabel]="i18n.translate('common.cancel')"
      [busy]="busy()"
      [confirmDisabled]="busy() || !hasMoveTarget()"
      focusReturnFallbackId="files-contextual-toolbar"
      (confirm)="confirmMove()"
      (closed)="cancelMove()"
    >
      <div class="move-dialog" [attr.aria-busy]="busy()">
        <label>
          <span>{{ i18n.translate('files.browser.folders') }}</span>
          <select [value]="destinationFolderId() ?? ''" (change)="selectDestination($event)" data-testid="files-move-destination">
            <option value="">{{ i18n.translate('files.currentWorkspace') }}</option>
            @for (option of destinationOptions(); track option.id) {
              <option [value]="option.id" [disabled]="option.disabled">{{ option.label }}</option>
            }
          </select>
        </label>
        @if (!folderId && validFileObjectIds().length > 1) {
          <p role="status">{{ i18n.translate('files.actions.moveUnavailable') }}</p>
        }
        @if (folders.loading()) {
          <p role="status" aria-live="polite">{{ i18n.translate('files.upload.loading') }}</p>
        }
        @if (folders.failed()) {
          <p role="alert">{{ i18n.translate('files.search.unavailable') }}</p>
        }
        @if (errorMessage()) {
          <p role="alert" data-testid="files-move-error">{{ errorMessage() }}</p>
        }
      </div>
    </app-coglatas-dialog>
  `,
  styles: [`
    .move-dialog { display: grid; gap: 12px; min-width: min(420px, 75vw); }
    .move-dialog label { display: grid; gap: 6px; font-weight: 600; }
    .move-dialog select { width: 100%; min-height: 40px; padding: 7px 10px; }
    .move-dialog p { margin: 0; }
  `],
})
export class FileMoveDialogComponent implements OnChanges {
  @Input() open = false;
  @Input() fileObjectIds: readonly string[] = [];
  @Input() folderId: string | null = null;
  @Output() readonly moved = new EventEmitter<void>();
  @Output() readonly dismissed = new EventEmitter<void>();

  readonly i18n = inject(I18nService);
  readonly folders = inject(FileFolderStore);
  readonly destinationFolderId = signal<string | null>(null);
  readonly busy = signal(false);
  readonly errorMessage = signal('');

  private request: Subscription | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open'] && this.open) {
      this.request?.unsubscribe();
      this.request = null;
      this.destinationFolderId.set(null);
      this.errorMessage.set('');
      this.busy.set(false);
    }
  }

  validFileObjectIds(): readonly string[] {
    return [...new Set(this.fileObjectIds.filter((id) => id.length > 0))];
  }

  hasMoveTarget(): boolean {
    return this.folderId !== null || this.validFileObjectIds().length === 1;
  }

  destinationOptions(): readonly DestinationOption[] {
    const sourceFolderId = this.folderId;
    const folders = this.folders.folders();
    const excluded = sourceFolderId ? descendantIds(folders, sourceFolderId) : new Set<string>();
    if (sourceFolderId) {
      excluded.add(sourceFolderId);
    }
    return flattenFolders(folders).map(({ folder, depth }) => ({
      id: folder.id,
      label: `${'  '.repeat(Math.max(0, depth - 1))}${folder.name}`,
      disabled: excluded.has(folder.id),
    }));
  }

  selectDestination(event: Event): void {
    const value = event.target instanceof HTMLSelectElement ? event.target.value : '';
    this.destinationFolderId.set(value || null);
  }

  confirmMove(): void {
    if (this.busy() || !this.hasMoveTarget()) {
      return;
    }

    this.errorMessage.set('');
    this.busy.set(true);
    const destination = this.destinationFolderId();
    const operation = this.folderId
      ? this.folders.moveFolder(this.folderId, destination)
      : this.folders.moveFile(this.validFileObjectIds()[0]!, destination);

    this.request?.unsubscribe();
    this.request = operation.subscribe({
      next: () => {
        this.request = null;
        this.busy.set(false);
        this.moved.emit();
      },
      error: () => {
        this.request = null;
        this.busy.set(false);
        this.errorMessage.set(this.i18n.translate('files.search.unavailable'));
      },
    });
  }

  cancelMove(): void {
    if (this.busy()) {
      return;
    }
    this.request?.unsubscribe();
    this.request = null;
    this.errorMessage.set('');
    this.dismissed.emit();
  }
}

function flattenFolders(
  folders: readonly FileFolderViewModel[],
  parentFolderId: string | null = null,
  depth = 1,
): readonly { readonly folder: FileFolderViewModel; readonly depth: number }[] {
  const result: { folder: FileFolderViewModel; depth: number }[] = [];
  for (const folder of folders.filter((candidate) => candidate.parentFolderId === parentFolderId)) {
    result.push({ folder, depth });
    result.push(...flattenFolders(folders, folder.id, depth + 1));
  }
  return result;
}

function descendantIds(folders: readonly FileFolderViewModel[], sourceFolderId: string): Set<string> {
  const descendants = new Set<string>();
  const visit = (parentFolderId: string): void => {
    for (const folder of folders.filter((candidate) => candidate.parentFolderId === parentFolderId)) {
      if (descendants.has(folder.id)) {
        continue;
      }
      descendants.add(folder.id);
      visit(folder.id);
    }
  };
  visit(sourceFolderId);
  return descendants;
}
