import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, Output, QueryList, ViewChildren, computed, inject, signal } from '@angular/core';
import { LucideChevronLeft, LucideChevronRight, LucideClock3, LucideFolder, LucideFolderOpen, LucideShare2, LucideStar } from '@lucide/angular';

import { I18nService } from '../../../core/i18n/i18n.service';

export type FileBrowserShortcut = 'recent' | 'starred' | 'shared';

export interface FileBrowserFolderNode {
  readonly id: string;
  readonly name: string;
  readonly children: readonly FileBrowserFolderNode[];
}

interface VisibleFolderNode extends FileBrowserFolderNode {
  readonly level: number;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-file-browser-sidebar',
  standalone: true,
  imports: [LucideChevronLeft, LucideChevronRight, LucideClock3, LucideFolder, LucideFolderOpen, LucideShare2, LucideStar],
  templateUrl: './file-browser-sidebar.component.html',
  styleUrl: './file-browser-sidebar.component.scss',
})
export class FileBrowserSidebarComponent {
  @Input() selectedFolderId: string | null = null;
  @Input() activeShortcut: FileBrowserShortcut | null = 'recent';
  @Output() readonly folderSelected = new EventEmitter<string>();
  @Output() readonly folderMoveRequested = new EventEmitter<string>();
  @Output() readonly shortcutSelected = new EventEmitter<FileBrowserShortcut>();

  @ViewChildren('treeitem', { read: ElementRef }) private treeItems?: QueryList<ElementRef<HTMLElement>>;

  readonly i18n = inject(I18nService);
  readonly collapsed = signal(false);
  readonly width = signal(264);
  readonly expandedIds = signal<ReadonlySet<string>>(new Set());
  readonly focusedId = signal<string | null>(null);
  readonly visibleFolders = computed<readonly VisibleFolderNode[]>(() => {
    const visible: VisibleFolderNode[] = [];
    const expanded = this.expandedIds();
    const visit = (nodes: readonly FileBrowserFolderNode[], level: number): void => {
      for (const node of nodes) {
        visible.push({ ...node, level });
        if (level < 2 || expanded.has(node.id)) {
          visit(node.children, level + 1);
        }
      }
    };
    visit(this.folderState(), 1);
    return visible;
  });
  private readonly folderState = signal<readonly FileBrowserFolderNode[]>([]);

  get folders(): readonly FileBrowserFolderNode[] {
    return this.folderState();
  }
  @Input() set folders(value: readonly FileBrowserFolderNode[]) {
    this.folderState.set(value);
  }

  toggleCollapsed(): void {
    this.collapsed.update((value) => !value);
  }

  chooseShortcut(shortcut: FileBrowserShortcut): void {
    this.shortcutSelected.emit(shortcut);
  }

  chooseFolder(node: FileBrowserFolderNode): void {
    this.focusedId.set(node.id);
    this.folderSelected.emit(node.id);
  }

  requestFolderMove(node: FileBrowserFolderNode): void {
    this.focusedId.set(node.id);
    this.folderMoveRequested.emit(node.id);
  }

  toggle(node: FileBrowserFolderNode): void {
    if (node.children.length === 0) {return;}
    this.expandedIds.update((current) => {
      const next = new Set(current);
      next.has(node.id) ? next.delete(node.id) : next.add(node.id);
      return next;
    });
  }

  onTreeKeydown(event: KeyboardEvent, node: VisibleFolderNode): void {
    const visible = this.visibleFolders();
    const index = visible.findIndex((candidate) => candidate.id === node.id);
    let targetIndex: number | null = null;
    if (event.key === 'ArrowDown') {targetIndex = Math.min(visible.length - 1, index + 1);}
    if (event.key === 'ArrowUp') {targetIndex = Math.max(0, index - 1);}
    if (event.key === 'Home') {targetIndex = 0;}
    if (event.key === 'End') {targetIndex = visible.length - 1;}
    if (event.key === 'ArrowRight') {
      if (node.children.length > 0 && !this.expandedIds().has(node.id)) {this.toggle(node);}
      else if (node.children.length > 0) {targetIndex = index + 1;}
    }
    if (event.key === 'ArrowLeft') {
      if (this.expandedIds().has(node.id)) {this.toggle(node);}
      else {
        for (let candidate = index - 1; candidate >= 0; candidate -= 1) {
          if (visible[candidate].level < node.level) { targetIndex = candidate; break; }
        }
      }
    }
    if (event.key === 'Enter' || event.key === ' ') {this.chooseFolder(node);}
    if (targetIndex !== null || ['ArrowLeft', 'ArrowRight', 'Enter', ' '].includes(event.key)) {
      event.preventDefault();
      if (targetIndex !== null) {this.focusAt(targetIndex);}
    }
  }

  resize(event: Event): void {
    this.width.set(Number((event.target as HTMLInputElement).value));
  }

  private focusAt(index: number): void {
    const node = this.visibleFolders()[index];
    if (!node) {return;}
    this.focusedId.set(node.id);
    queueMicrotask(() => this.treeItems?.get(index)?.nativeElement.focus());
  }
}
