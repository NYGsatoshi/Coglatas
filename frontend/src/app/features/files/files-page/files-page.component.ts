import { A11yModule } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Observable, Subscription } from 'rxjs';

import { I18nService } from '../../../core/i18n/i18n.service';
import { FrontendFeatureFlagsService } from '../../../core/feature-flags/frontend-feature-flags.service';
import { AuthSessionFacade } from '../../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { AppDataGridComponent } from '../../../shared/grid/app-data-grid/app-data-grid.component';
import { AppDataGridColumnDef } from '../../../shared/grid/app-data-grid/app-data-grid.types';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import { CoglatasFilterChipComponent } from '../../../shared/ui/coglatas-filter-chip/coglatas-filter-chip.component';
import { CoglatasFileUploaderComponent } from '../../../shared/ui/adapters/syncfusion/coglatas-file-uploader.component';
import { AttachmentPickerDialogComponent } from '../attachment-picker-dialog/attachment-picker-dialog.component';
import { FileActivityPanelComponent } from '../file-activity-panel/file-activity-panel.component';
import { FileFolderStore } from '../file-folders.service';
import { FileMoveDialogComponent } from '../file-move-dialog/file-move-dialog.component';
import { FilePreviewService } from '../file-preview.service';
import { FileBrowserShortcut, FileBrowserSidebarComponent } from '../file-browser-sidebar/file-browser-sidebar.component';
import { FileQuotaStateComponent } from '../file-quota-state/file-quota-state.component';
import { FilesFacade } from '../files.facade';
import { FileSharingDetailViewModel } from '../files.api';
import { RecentFilesListComponent } from '../recent-files-list/recent-files-list.component';
import {
  FileSearchFilters,
  FileSearchKindFilter,
  FileSearchModifiedFilter,
  FileSearchOwnerFilter,
  FileSelectionSnapshot,
  FileViewModel,
} from '../files.types';

type FileListOptionalColumn = 'type' | 'size' | 'scan';
type FileListDensity = 'comfortable' | 'compact';
type FilePreviewState = 'idle' | 'loading' | 'ready' | 'unsupported' | 'failed';
type FilePreviewRenderer = 'image' | 'pdf' | 'video' | 'text' | 'unsupported';
type FileInspectorTab = 'preview' | 'details' | 'activity';

const TEXT_PREVIEW_MAX_BYTES = 512 * 1024;
const PREVIEW_OVERLAY_MAX_WIDTH = 860;

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-files-page',
  standalone: true,
  imports: [
    A11yModule,
    CoglatasDialogComponent,
    CoglatasFilterChipComponent,
    CoglatasFileUploaderComponent,
    AppDataGridComponent,
    AttachmentPickerDialogComponent,
    FileActivityPanelComponent,
    FileMoveDialogComponent,
    FileQuotaStateComponent,
    FileBrowserSidebarComponent,
    RecentFilesListComponent,
  ],
  templateUrl: './files-page.component.html',
  styleUrl: './files-page.component.scss',
})
export class FilesPageComponent {
  @ViewChild(AppDataGridComponent) private dataGrid?: AppDataGridComponent<FileViewModel>;
  @ViewChild('previewPane') private previewPane?: ElementRef<HTMLElement>;

  private readonly facade = inject(FilesFacade);
  private readonly fileFolders = inject(FileFolderStore);
  private readonly previewService = inject(FilePreviewService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly flags = inject(FrontendFeatureFlagsService);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly authSession = inject(AuthSessionFacade);
  private readonly realtime = inject(RealtimeFacade);
  readonly i18n = inject(I18nService);

  readonly page = this.facade.page;
  readonly search = this.facade.search;
  readonly syncfusionUploaderEnabled = this.flags.syncfusionUploaderEnabled;
  readonly searchQuery = signal('');
  readonly searchKind = signal<FileSearchKindFilter>('all');
  readonly searchModified = signal<FileSearchModifiedFilter>('any');
  readonly searchOwner = signal<FileSearchOwnerFilter>('any');
  readonly browserShortcut = signal<FileBrowserShortcut>('recent');
  readonly browserFolders = this.fileFolders.tree;
  readonly activeWorkspaceLabel = computed(() =>
    this.activeWorkspace.activeWorkspace()?.label ?? this.i18n.translate('files.currentWorkspace'));
  readonly browserShortcutLabel = computed(() => {
    switch (this.browserShortcut()) {
      case 'starred':
        return this.i18n.translate('files.browser.starred');
      case 'shared':
        return this.i18n.translate('files.browser.shared');
      default:
        return this.i18n.translate('files.browser.recent');
    }
  });
  readonly searchApplied = computed(() => this.search().status !== 'idle');
  readonly displayedList = computed(() => {
    const search = this.search();
    if (search.status !== 'idle') {
      return {
        files: search.files,
        page: search.page,
        pageSize: search.pageSize,
        totalCount: search.totalCount,
        hasMore: search.hasMore,
      };
    }
    const page = this.page();
    if (this.browserShortcut() !== 'recent') {
      return { files: [], page: 1, pageSize: page.pageSize, totalCount: 0, hasMore: false };
    }
    return {
      files: page.recentFiles,
      page: page.page,
      pageSize: page.pageSize,
      totalCount: page.totalCount,
      hasMore: page.hasMore,
    };
  });

  selectBrowserShortcut(shortcut: FileBrowserShortcut): void {
    this.browserShortcut.set(shortcut);
    this.closeMoveDialog();
    this.clearSelection();
    this.closePreview(false);
  }
  readonly density = signal<FileListDensity>('comfortable');
  readonly selectedFiles = signal<readonly FileViewModel[]>([]);
  readonly selectionSnapshot = this.facade.selectionSnapshot;
  readonly selectedSearchResults = computed(() => this.selectionSnapshot().selection);
  readonly selectedCount = computed(() =>
    this.selectedSearchResults()?.selectedCount ?? this.selectedFiles().length);
  readonly hasSelection = computed(() => this.selectedCount() > 0);
  readonly hasSearchResultsSelection = computed(() => this.selectedSearchResults() !== null);
  readonly selectionCaptureBusy = computed(() => this.selectionSnapshot().status === 'capturing');
  readonly selectedFileIds = computed<ReadonlySet<string>>(() =>
    new Set(this.selectedFiles().map((file) => file.id)));
  readonly moveDialogOpen = signal(false);
  readonly moveFolderId = signal<string | null>(null);
  readonly moveFileObjectIds = computed<readonly string[]>(() =>
    this.selectedFiles()
      .map((file) => file.canonicalFileId)
      .filter((id): id is string => typeof id === 'string' && id.length > 0));
  readonly canMoveSelection = computed(() => {
    if (this.hasSearchResultsSelection()) {
      return false;
    }
    const selected = this.selectedFiles();
    return selected.length > 0 && this.moveFileObjectIds().length === selected.length;
  });
  readonly canDeleteSelection = computed(() => {
    if (this.hasSearchResultsSelection()) {
      // The selection snapshot grants no delete authority. The server
      // reauthorizes each item at batch execution.
      return true;
    }
    const selected = this.selectedFiles();
    return selected.length > 0 && selected.every((file) => file.canDelete === true && !!file.canonicalFileId);
  });
  readonly downloadableSelection = computed(() => {
    if (this.hasSearchResultsSelection()) {
      return null;
    }
    const selected = this.selectedFiles();
    const file = selected.length === 1 ? selected[0] : undefined;
    return file?.canonicalFileId && file.downloadPolicy === 'available' &&
      file.scanStatus === 'allowed' && file.downloadState !== 'pending'
      ? file
      : null;
  });

  readonly previewFile = signal<FileViewModel | null>(null);
  readonly previewState = signal<FilePreviewState>('idle');
  readonly previewRenderer = signal<FilePreviewRenderer>('unsupported');
  readonly previewUrl = signal<string | null>(null);
  readonly previewText = signal('');
  readonly previewMessage = signal('');
  readonly previewActionStatus = signal('');
  readonly previewOverlay = signal(this.isCompactViewport());
  readonly previewOpen = computed(() => this.previewFile() !== null);
  readonly inspectorTab = signal<FileInspectorTab>('preview');
  readonly inspectorTabs: readonly FileInspectorTab[] = ['preview', 'details', 'activity'];
  readonly fileLocationLabel = computed(() =>
    this.activeWorkspace.activeWorkspace()?.label ?? this.i18n.translate('files.currentWorkspace'));
  readonly previewCanDownload = computed(() => {
    const file = this.previewFile();
    return !!file?.canonicalFileId && file.downloadPolicy === 'available' &&
      file.scanStatus === 'allowed' && file.capabilities.includes('download') &&
      file.downloadState !== 'pending';
  });
  readonly previewCanOpen = computed(() => this.previewState() === 'ready' && !!this.previewUrl());
  readonly researchHandoffHref = computed(() => {
    const file = this.previewFile();
    const workspaceId = this.activeWorkspace.activeWorkspace()?.id;
    if (!workspaceId || !file?.canonicalFileId) {
      return null;
    }
    const params = new URLSearchParams({
      sourceFileObjectId: file.canonicalFileId,
      sourceFileName: file.originalFileName,
    });
    return `/workspaces/${encodeURIComponent(workspaceId)}/research/new?${params.toString()}`;
  });
  readonly previewCanManageSharing = computed(() => {
    const file = this.previewFile();
    return !!file?.canonicalFileId && file.sharing.canManageSharing === true && !!file.sharing.sharingVersion;
  });

  readonly sharingDialogOpen = signal(false);
  readonly sharingDetail = signal<FileSharingDetailViewModel | null>(null);
  readonly sharingBusy = signal(false);
  readonly sharingError = signal('');
  readonly sharingSelectedRecipientId = signal('');
  readonly sharingDialogFile = computed(() => this.previewFile());
  readonly sharingDialogCanEdit = computed(() =>
    this.sharingDetail()?.sharing.canManageSharing === true && !this.sharingBusy());

  readonly deleteDialogOpen = signal(false);
  readonly deleteTargets = signal<readonly FileViewModel[]>([]);
  readonly deleteSnapshot = signal<FileSelectionSnapshot | null>(null);
  readonly deleteState = this.facade.deleteState;
  readonly deleteBusy = computed(() => this.deleteState().state === 'pending');
  readonly deleteDialogTitle = computed(() =>
    this.deleteSelectionCount() === 1
      ? this.i18n.translate('files.delete.titleOne')
      : this.i18n.translate('files.delete.titleMany', { count: this.deleteSelectionCount() }));
  readonly deleteDialogDescription = computed(() => {
    const snapshot = this.deleteSnapshot();
    if (snapshot) {
      return this.i18n.translate('files.delete.descriptionSearch', { count: snapshot.selectedCount });
    }
    const targets = this.deleteTargets();
    if (targets.length === 1) {
      return this.i18n.translate('files.delete.descriptionOne', {
        name: targets[0]?.originalFileName ?? this.i18n.translate('files.untitledFile'),
      });
    }
    return this.i18n.translate('files.delete.descriptionMany', { count: targets.length });
  });
  readonly totalPages = computed(() => {
    const page = this.displayedList();
    return Math.max(1, Math.ceil(page.totalCount / Math.max(1, page.pageSize)));
  });
  readonly optionalColumns = computed(() => [
    { id: 'type' as const, label: this.i18n.translate('files.table.type') },
    { id: 'size' as const, label: this.i18n.translate('files.table.size') },
    { id: 'scan' as const, label: this.i18n.translate('files.table.scanDetails') },
  ]);
  private readonly visibleOptionalColumns = signal<ReadonlySet<FileListOptionalColumn>>(new Set());
  private previewRequest: Subscription | null = null;
  private previewGeneration = 0;
  private previewObjectUrl: string | null = null;
  private previewReturnFocus: HTMLElement | null = null;
  private mobileSelectionAnchorId: string | null = null;
  private sharingRequest: Subscription | null = null;
  private sharingGeneration = 0;

  readonly columns = computed<readonly AppDataGridColumnDef<FileViewModel>[]>(() => {
    const visible = this.visibleOptionalColumns();
    const columns: AppDataGridColumnDef<FileViewModel>[] = [
      {
        colId: 'name',
        headerName: this.i18n.translate('files.table.name'),
        flex: 2,
        minWidth: 220,
        actions: (row) => [{
          id: 'open',
          label: row.originalFileName,
          row,
        }],
      },
      {
        colId: 'modified',
        headerName: this.i18n.translate('files.table.modified'),
        flex: 1,
        minWidth: 150,
        valueGetter: ({ data }) => data ? this.fileModifiedLabel(data) : '',
      },
      { field: 'uploadedByDisplay', headerName: this.i18n.translate('files.table.owner'), flex: 1, minWidth: 140 },
      {
        colId: 'access',
        headerName: this.i18n.translate('files.table.access'),
        minWidth: 130,
        valueGetter: ({ data }) => data ? this.fileSharingLabel(data) : '',
      },
      {
        colId: 'status',
        headerName: this.i18n.translate('files.table.status'),
        minWidth: 130,
        valueGetter: ({ data }) => data ? this.i18n.fileScanStatusLabel(data.scanStatus) : '',
      },
    ];

    if (visible.has('type')) {
      columns.push({
        field: 'contentType',
        headerName: this.i18n.translate('files.table.type'),
        flex: 1,
        minWidth: 160,
        valueFormatter: ({ value }) => this.i18n.fileContentTypeLabel(String(value ?? '')),
      });
    }
    if (visible.has('size')) {
      columns.push({
        field: 'sizeBytes',
        headerName: this.i18n.translate('files.table.size'),
        minWidth: 100,
        valueFormatter: ({ value }) => this.i18n.formatFileSize(Number(value ?? 0)),
      });
    }
    if (visible.has('scan')) {
      columns.push({
        field: 'scanStatus',
        headerName: this.i18n.translate('files.table.scanDetails'),
        minWidth: 130,
        valueFormatter: ({ value }) => this.i18n.fileScanStatusLabel(value as FileViewModel['scanStatus']),
      });
    }

    return columns;
  });

  constructor() {
    const unregisterSearchDraftClearer = this.realtime.registerProtectedStateClearer?.(
      'files-page-search-draft',
      () => this.resetSearchControls(),
    );
    effect(() => {
      const pageFacade = this.facade as FilesFacade & {
        loadPageFilesForWorkspace?: (workspaceId: string | undefined) => void;
      };
      const workspaceId = this.activeWorkspace.activeWorkspace()?.id;
      this.resetSearchControls();
      this.closeMoveDialog();
      this.closePreview(false);
      this.clearSelection();
      this.closeDeleteDialog();
      this.fileFolders.load(workspaceId);
      pageFacade.loadPageFilesForWorkspace?.call(pageFacade, workspaceId);
    });
    effect(() => {
      // A server inventory replacement can revoke a capability or remove a row.
      // Selection and preview never survive that authoritative reload.
      this.facade.inventoryRevision();
      this.closeMoveDialog();
      this.closePreview(false);
      this.clearSelection();
      this.closeDeleteDialog();
    });
    effect(() => {
      // Search responses are server-authorized snapshots. Never keep a
      // selection or preview from the snapshot they replace.
      this.facade.searchRevision();
      if (this.search().status === 'idle') {
        this.resetSearchControls();
      }
      this.closeMoveDialog();
      this.closePreview(false);
      this.clearSelection();
      this.closeDeleteDialog();
    });
    this.destroyRef.onDestroy(() => {
      unregisterSearchDraftClearer?.();
      this.closeMoveDialog();
      this.closePreview(false);
    });
  }

  @HostListener('window:resize')
  handleWindowResize(): void {
    this.previewOverlay.set(this.isCompactViewport());
  }

  @HostListener('document:keydown.escape', ['$event'])
  handleEscape(event: KeyboardEvent): void {
    if (this.moveDialogOpen() || !this.previewOpen() || this.deleteDialogOpen() || this.sharingDialogOpen()) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.closePreview();
  }

  acceptUpload(files: readonly File[]): void {
    this.facade.uploadFiles(files);
  }

  updateSearchQuery(event: Event): void {
    this.searchQuery.set(event.target instanceof HTMLInputElement ? event.target.value : '');
  }

  updateSearchKind(event: Event): void {
    this.searchKind.set((event.target as HTMLSelectElement).value as FileSearchKindFilter);
  }

  updateSearchModified(event: Event): void {
    this.searchModified.set((event.target as HTMLSelectElement).value as FileSearchModifiedFilter);
  }

  updateSearchOwner(event: Event): void {
    this.searchOwner.set((event.target as HTMLSelectElement).value as FileSearchOwnerFilter);
  }

  submitFileSearch(event: Event): void {
    event.preventDefault();
    this.applyFileSearch();
  }

  clearFileSearch(): void {
    this.searchQuery.set('');
    this.searchKind.set('all');
    this.searchModified.set('any');
    this.searchOwner.set('any');
    this.facade.clearFileSearch();
  }

  removeSearchFilter(filter: 'kind' | 'modified' | 'owner'): void {
    const applied = this.search().filters;
    // Chips describe the last server request, so removal starts from that
    // request rather than combining it with unrelated, unsubmitted drafts.
    this.searchQuery.set(applied.query);
    this.searchKind.set(filter === 'kind' ? 'all' : applied.kind);
    this.searchModified.set(filter === 'modified' ? 'any' : applied.modified);
    this.searchOwner.set(filter === 'owner' ? 'any' : applied.owner);
    this.applyFileSearch();
  }

  searchKindLabel(kind: FileSearchKindFilter): string {
    const key = ({
      all: 'files.search.anyType',
      document: 'files.search.documents',
      image: 'files.search.images',
      pdf: 'files.search.pdf',
      video: 'files.search.video',
      archive: 'files.search.archives',
    } as const)[kind];
    return this.i18n.translate(key);
  }

  searchModifiedLabel(modified: FileSearchModifiedFilter): string {
    const key = ({
      any: 'files.search.anyTime',
      last7Days: 'files.search.last7Days',
      last30Days: 'files.search.last30Days',
      last90Days: 'files.search.last90Days',
    } as const)[modified];
    return this.i18n.translate(key);
  }

  cancelUpload(clientRequestId: string): void {
    this.facade.cancelUpload(clientRequestId);
  }

  retryUpload(clientRequestId: string): void {
    this.facade.retryUpload(clientRequestId);
  }

  downloadFile(fileObjectId: string): void {
    this.facade.downloadFile(fileObjectId);
  }

  isColumnVisible(column: FileListOptionalColumn): boolean {
    return this.visibleOptionalColumns().has(column);
  }

  toggleColumn(column: FileListOptionalColumn, visible: boolean): void {
    this.visibleOptionalColumns.update((current) => {
      const next = new Set(current);
      if (visible) {
        next.add(column);
      } else {
        next.delete(column);
      }
      return next;
    });
  }

  setDensity(density: FileListDensity): void {
    this.density.set(density);
  }

  handleSelectionChanged(event: { rows: readonly FileViewModel[] }): void {
    this.facade.clearSearchSelectionSnapshot();
    this.selectedFiles.set([...event.rows]);
  }

  handleMobileSelection(event: { file: FileViewModel; selected: boolean; range?: boolean }): void {
    this.facade.clearSearchSelectionSnapshot();
    const selectedIds = new Set(this.selectedFiles().map((file) => file.id));
    const files = this.displayedList().files;
    const anchorIndex = this.mobileSelectionAnchorId
      ? files.findIndex((file) => file.id === this.mobileSelectionAnchorId)
      : -1;
    const targetIndex = files.findIndex((file) => file.id === event.file.id);
    const affected = event.range && anchorIndex >= 0 && targetIndex >= 0
      ? files.slice(Math.min(anchorIndex, targetIndex), Math.max(anchorIndex, targetIndex) + 1)
      : [event.file];
    for (const file of affected) {
      if (event.selected) {
        selectedIds.add(file.id);
      } else {
        selectedIds.delete(file.id);
      }
    }
    this.selectedFiles.set(files.filter((file) => selectedIds.has(file.id)));
    this.mobileSelectionAnchorId = event.file.id;
  }

  clearSelection(): void {
    this.facade.clearSearchSelectionSnapshot();
    this.selectedFiles.set([]);
    this.mobileSelectionAnchorId = null;
    this.dataGrid?.clearSelection();
  }

  selectThisPage(): void {
    this.facade.clearSearchSelectionSnapshot();
    this.selectedFiles.set([...this.displayedList().files]);
    this.mobileSelectionAnchorId = this.displayedList().files[0]?.id ?? null;
    this.dataGrid?.selectAllRows();
  }

  selectAllSearchResults(): void {
    if (!this.searchApplied() || this.selectionCaptureBusy()) {
      return;
    }
    this.selectedFiles.set([]);
    this.mobileSelectionAnchorId = null;
    this.dataGrid?.clearSelection();
    this.facade.captureSearchSelectionSnapshot();
  }

  openFileMoveDialog(): void {
    if (!this.canMoveSelection()) {
      return;
    }
    this.moveFolderId.set(null);
    this.moveDialogOpen.set(true);
  }

  openFolderMoveDialog(folderId: string): void {
    if (!folderId || !this.fileFolders.folders().some((folder) => folder.id === folderId)) {
      return;
    }
    this.moveFolderId.set(folderId);
    this.moveDialogOpen.set(true);
  }

  closeMoveDialog(): void {
    this.moveDialogOpen.set(false);
    this.moveFolderId.set(null);
  }

  handleMoveCompleted(): void {
    this.closeMoveDialog();
    this.clearSelection();
  }

  openPreview(file: FileViewModel): void {
    this.capturePreviewReturnFocus();
    this.cancelPreviewRequest();
    this.revokePreviewObjectUrl();
    this.previewFile.set(file);
    this.inspectorTab.set('preview');
    this.previewRenderer.set(this.previewRendererFor(file));
    this.previewText.set('');
    this.previewMessage.set('');
    this.previewActionStatus.set('');

    const accessMessage = this.previewAccessMessage(file);
    if (accessMessage) {
      this.previewState.set('failed');
      this.previewMessage.set(accessMessage);
      return;
    }

    if (this.previewRenderer() === 'unsupported') {
      this.previewState.set('unsupported');
      this.previewMessage.set(this.i18n.translate('files.preview.unsupportedType'));
      return;
    }

    const fileObjectId = file.canonicalFileId;
    if (!fileObjectId) {
      this.previewState.set('failed');
      this.previewMessage.set(this.i18n.translate('files.preview.canonicalPending'));
      return;
    }

    this.previewState.set('loading');
    const generation = this.previewGeneration;
    const request = this.previewService.load(fileObjectId).subscribe((result) => {
      if (generation !== this.previewGeneration || this.previewFile()?.id !== file.id) {
        return;
      }
      this.previewRequest = null;
      if (!result.ok || !result.blob) {
        this.previewState.set('failed');
        this.previewMessage.set(result.message || this.i18n.translate('files.preview.contentUnavailable'));
        return;
      }
      this.applyPreviewBlob(file, result.blob, generation);
    });
    this.previewRequest = request;
  }

  closePreview(returnFocus = true): void {
    const target = returnFocus ? this.previewReturnFocus : null;
    this.previewReturnFocus = null;
    this.cancelPreviewRequest();
    this.revokePreviewObjectUrl();
    this.previewFile.set(null);
    this.inspectorTab.set('preview');
    this.previewState.set('idle');
    this.previewRenderer.set('unsupported');
    this.previewText.set('');
    this.previewMessage.set('');
    this.previewActionStatus.set('');
    this.closeSharingDialog();

    if (target) {
      queueMicrotask(() => {
        if (!target.isConnected) {
          return;
        }
        try {
          target.focus({ preventScroll: true });
        } catch {
          target.focus();
        }
      });
    }
  }

  downloadPreviewFile(): void {
    const file = this.previewFile();
    if (this.previewCanDownload() && file?.canonicalFileId) {
      this.downloadFile(file.canonicalFileId);
    }
  }

  selectInspectorTab(tab: FileInspectorTab): void {
    if (!this.previewFile()) {
      return;
    }
    this.inspectorTab.set(tab);
  }

  handleInspectorTabKeydown(event: KeyboardEvent, currentTab: FileInspectorTab): void {
    const currentIndex = this.inspectorTabs.indexOf(currentTab);
    let targetIndex = currentIndex;

    switch (event.key) {
      case 'ArrowRight':
        targetIndex = (currentIndex + 1) % this.inspectorTabs.length;
        break;
      case 'ArrowLeft':
        targetIndex = (currentIndex - 1 + this.inspectorTabs.length) % this.inspectorTabs.length;
        break;
      case 'Home':
        targetIndex = 0;
        break;
      case 'End':
        targetIndex = this.inspectorTabs.length - 1;
        break;
      default:
        return;
    }

    event.preventDefault();
    const targetTab = this.inspectorTabs[targetIndex] ?? currentTab;
    this.inspectorTab.set(targetTab);
    const tabList = event.currentTarget instanceof HTMLElement
      ? event.currentTarget.parentElement
      : null;
    queueMicrotask(() => {
      tabList
        ?.querySelector<HTMLButtonElement>(`[data-inspector-tab="${targetTab}"]`)
        ?.focus();
    });
  }

  fileAccessLabel(file: FileViewModel): string {
    return file.downloadPolicy === 'available' &&
      file.scanStatus === 'allowed' &&
      file.capabilities.includes('download')
      ? this.i18n.translate('files.preview.authorizedDownload')
      : this.i18n.translate('files.preview.restricted');
  }

  fileSharingLabel(file: FileViewModel): string {
    const accessState = file.sharing.accessState;
    if (accessState === 'external' && typeof file.sharing.externalRecipientCount === 'number') {
      return this.i18n.translate('files.sharing.externalCount', { count: file.sharing.externalRecipientCount });
    }
    return this.fileSharingStateLabel(accessState);
  }

  fileSharingStateLabel(accessState: FileViewModel['sharing']['accessState']): string {
    switch (accessState) {
      case 'private':
        return this.i18n.translate('files.sharing.private');
      case 'workspace':
        return this.i18n.translate('files.sharing.workspace');
      case 'external':
        return this.i18n.translate('files.sharing.external');
      default:
        return this.i18n.translate('files.sharing.unavailable');
    }
  }

  copyPreviewCitation(): void {
    const file = this.previewFile();
    if (!file?.canonicalFileId) {
      this.previewActionStatus.set(this.i18n.translate('files.preview.citationUnavailable'));
      return;
    }
    const citation = this.previewCitationText(file);
    const clipboard = typeof navigator !== 'undefined' ? navigator.clipboard : undefined;
    if (clipboard && typeof clipboard.writeText === 'function') {
      void clipboard.writeText(citation).then(
        () => this.previewActionStatus.set(this.i18n.translate('files.preview.citationCopied')),
        () => this.previewActionStatus.set(this.fallbackCopyText(citation)
          ? this.i18n.translate('files.preview.citationCopied')
          : this.i18n.translate('files.preview.copyUnavailable')),
      );
      return;
    }
    this.previewActionStatus.set(this.fallbackCopyText(citation)
      ? this.i18n.translate('files.preview.citationCopied')
      : this.i18n.translate('files.preview.copyUnavailable'));
  }

  openSharingDialog(): void {
    const file = this.previewFile();
    if (!file?.canonicalFileId || !this.previewCanManageSharing()) {
      return;
    }

    this.cancelSharingRequest();
    const generation = ++this.sharingGeneration;
    this.sharingDialogOpen.set(true);
    this.sharingDetail.set(null);
    this.sharingError.set('');
    this.sharingSelectedRecipientId.set('');
    this.sharingBusy.set(true);
    const request = this.facade.getFileSharing(file.canonicalFileId).subscribe({
      next: (detail) => {
        if (!this.isCurrentSharingRequest(generation, file.canonicalFileId)) {
          return;
        }
        this.sharingRequest = null;
        this.sharingBusy.set(false);
        if (!detail.sharing.canManageSharing || !detail.canInspectSharing) {
          this.applySharingDetail(detail);
          this.sharingError.set(this.i18n.translate('files.sharing.permissionChanged'));
          return;
        }
        this.applySharingDetail(detail);
      },
      error: () => {
        if (!this.isCurrentSharingRequest(generation, file.canonicalFileId)) {
          return;
        }
        this.sharingRequest = null;
        this.sharingBusy.set(false);
        this.sharingError.set(this.i18n.translate('files.sharing.loadFailed'));
      },
    });
    this.sharingRequest = request;
  }

  closeSharingDialog(): void {
    this.sharingGeneration += 1;
    this.cancelSharingRequest();
    this.sharingDialogOpen.set(false);
    this.sharingBusy.set(false);
    this.sharingError.set('');
    this.sharingSelectedRecipientId.set('');
    this.sharingDetail.set(null);
  }

  updateSharingWorkspace(enabled: boolean): void {
    const detail = this.sharingDetail();
    if (!detail || !this.sharingDialogCanEdit()) {
      return;
    }
    this.runSharingMutation(this.facade.updateFileSharingPolicy(
      detail.fileObjectId,
      enabled,
      detail.sharing.sharingVersion!,
    ));
  }

  updateSharingRecipient(event: Event): void {
    this.sharingSelectedRecipientId.set(
      event.target instanceof HTMLSelectElement ? event.target.value : '');
  }

  grantSharingRecipient(): void {
    const detail = this.sharingDetail();
    const recipientUserId = this.sharingSelectedRecipientId();
    if (!detail || !recipientUserId || !this.sharingDialogCanEdit()) {
      return;
    }
    this.runSharingMutation(this.facade.grantFileSharingRecipient(
      detail.fileObjectId,
      recipientUserId,
      detail.sharing.sharingVersion!,
    ));
  }

  revokeSharingRecipient(grantId: string): void {
    const detail = this.sharingDetail();
    if (!detail || !grantId || !this.sharingDialogCanEdit()) {
      return;
    }
    this.runSharingMutation(this.facade.revokeFileSharingRecipient(
      detail.fileObjectId,
      grantId,
      detail.sharing.sharingVersion!,
    ));
  }

  private runSharingMutation(request: Observable<FileSharingDetailViewModel>): void {
    const fileObjectId = this.sharingDetail()?.fileObjectId;
    if (!fileObjectId) {
      return;
    }
    this.cancelSharingRequest();
    const generation = ++this.sharingGeneration;
    this.sharingBusy.set(true);
    this.sharingError.set('');
    const subscription = request.subscribe({
      next: (detail) => {
        if (!this.isCurrentSharingRequest(generation, fileObjectId)) {
          return;
        }
        this.sharingRequest = null;
        this.sharingBusy.set(false);
        this.sharingSelectedRecipientId.set('');
        this.applySharingDetail(detail);
        this.previewActionStatus.set(this.i18n.translate('files.sharing.updated'));
      },
      error: () => {
        if (!this.isCurrentSharingRequest(generation, fileObjectId)) {
          return;
        }
        this.sharingRequest = null;
        this.sharingBusy.set(false);
        this.sharingError.set(this.i18n.translate('files.sharing.saveFailed'));
      },
    });
    this.sharingRequest = subscription;
  }

  private applySharingDetail(detail: FileSharingDetailViewModel): void {
    this.sharingDetail.set(detail);
    this.facade.reconcileFileSharing(detail);
    this.previewFile.update((file) =>
      file?.canonicalFileId === detail.fileObjectId
        ? { ...file, sharing: detail.sharing }
        : file);
  }

  private isCurrentSharingRequest(generation: number, fileObjectId: string): boolean {
    return generation === this.sharingGeneration &&
      this.sharingDialogOpen() &&
      this.previewFile()?.canonicalFileId === fileObjectId;
  }

  private cancelSharingRequest(): void {
    this.sharingRequest?.unsubscribe();
    this.sharingRequest = null;
  }

  downloadSelectedFile(): void {
    const file = this.downloadableSelection();
    if (file?.canonicalFileId) {
      this.downloadFile(file.canonicalFileId);
    }
  }

  openDeleteConfirmation(): void {
    if (!this.canDeleteSelection() || this.deleteBusy()) {
      return;
    }
    const snapshot = this.selectedSearchResults();
    if (snapshot) {
      this.deleteTargets.set([]);
      this.deleteSnapshot.set(snapshot);
      this.deleteDialogOpen.set(true);
      return;
    }
    this.deleteTargets.set([...this.selectedFiles()]);
    this.deleteSnapshot.set(null);
    this.deleteDialogOpen.set(true);
  }

  closeDeleteDialog(): void {
    if (this.deleteBusy()) {
      return;
    }
    this.deleteDialogOpen.set(false);
    this.deleteTargets.set([]);
    this.deleteSnapshot.set(null);
  }

  confirmDelete(): void {
    const snapshot = this.deleteSnapshot();
    if (snapshot && !this.deleteBusy()) {
      this.facade.deleteSearchSelectionSnapshot(() => {
        this.deleteDialogOpen.set(false);
        this.deleteSnapshot.set(null);
        this.clearSelection();
      });
      return;
    }
    const targets = this.deleteTargets();
    if (targets.length === 0 || this.deleteBusy()) {
      return;
    }
    this.facade.deleteFiles(targets, () => {
      this.deleteDialogOpen.set(false);
      this.deleteTargets.set([]);
      this.deleteSnapshot.set(null);
      this.clearSelection();
    });
  }

  deleteSelectionCount(): number {
    return this.deleteSnapshot()?.selectedCount ?? this.deleteTargets().length;
  }

  goToPreviousPage(): void {
    const current = this.displayedList();
    if (current.page <= 1) {
      return;
    }
    this.closePreview(false);
    this.clearSelection();
    if (this.searchApplied()) {
      this.facade.goToSearchPage(current.page - 1);
    } else {
      this.facade.goToPage(current.page - 1);
    }
  }

  goToNextPage(): void {
    const current = this.displayedList();
    if (!current.hasMore) {
      return;
    }
    this.closePreview(false);
    this.clearSelection();
    if (this.searchApplied()) {
      this.facade.goToSearchPage(current.page + 1);
    } else {
      this.facade.goToPage(current.page + 1);
    }
  }

  handleGridAction(event: { actionId: string; row: FileViewModel }): void {
    if (event.actionId === 'open') {
      this.openPreview(event.row);
      return;
    }
    if (event.actionId === 'download' && event.row.canonicalFileId) {
      this.downloadFile(event.row.canonicalFileId);
    }
  }

  formatBytes(bytes: number): string {
    return this.i18n.formatFileSize(bytes);
  }

  fileCreatedLabel(file: FileViewModel): string {
    return this.formatFileDate(file.createdAt, file.createdAtLabel);
  }

  fileModifiedLabel(file: FileViewModel): string {
    return this.formatFileDate(file.modifiedAt, file.modifiedAtLabel);
  }

  private applyFileSearch(): void {
    const workspaceId = this.activeWorkspace.activeWorkspace()?.id;
    if (!workspaceId) {
      this.facade.clearFileSearch();
      return;
    }
    const filters: FileSearchFilters = {
      query: this.searchQuery(),
      kind: this.searchKind(),
      modified: this.searchModified(),
      owner: this.searchOwner(),
    };
    this.facade.searchFilesForWorkspace(
      workspaceId,
      filters,
      this.authSession.currentUser()?.userId ?? null,
    );
  }

  private resetSearchControls(): void {
    this.searchQuery.set('');
    this.searchKind.set('all');
    this.searchModified.set('any');
    this.searchOwner.set('any');
  }

  private capturePreviewReturnFocus(): void {
    if (typeof document === 'undefined') {
      return;
    }
    const active = document.activeElement;
    if (active instanceof HTMLElement && active !== document.body && !this.previewPane?.nativeElement.contains(active)) {
      this.previewReturnFocus = active;
    }
  }

  private cancelPreviewRequest(): void {
    this.previewGeneration++;
    this.previewRequest?.unsubscribe();
    this.previewRequest = null;
  }

  private revokePreviewObjectUrl(): void {
    if (this.previewObjectUrl && typeof URL !== 'undefined' && typeof URL.revokeObjectURL === 'function') {
      URL.revokeObjectURL(this.previewObjectUrl);
    }
    this.previewObjectUrl = null;
    this.previewUrl.set(null);
  }

  private previewAccessMessage(file: FileViewModel): string | null {
    if (!file.canonicalFileId) {
      return this.i18n.translate('files.preview.canonicalPending');
    }
    if (file.scanStatus === 'pending' || file.scanStatus === 'unavailable') {
      return this.i18n.translate('files.preview.scanPending');
    }
    if (file.scanStatus === 'blocked') {
      return this.i18n.translate('files.preview.blocked');
    }
    if (file.downloadPolicy !== 'available' || !file.capabilities.includes('download')) {
      return this.i18n.translate('files.preview.permissionDenied');
    }
    return null;
  }

  private previewRendererFor(file: FileViewModel): FilePreviewRenderer {
    if (file.kind === 'image' || file.kind === 'svg') {
      return 'image';
    }
    if (file.kind === 'pdf') {
      return 'pdf';
    }
    if (file.kind === 'video') {
      return 'video';
    }
    if (this.isTextLike(file)) {
      return 'text';
    }
    return 'unsupported';
  }

  private isTextLike(file: FileViewModel): boolean {
    const contentType = file.contentType.toLowerCase().split(';', 1)[0]?.trim() ?? '';
    if (contentType.startsWith('text/')) {
      return true;
    }
    if (['application/json', 'application/xml', 'application/yaml', 'application/x-yaml', 'application/x-ndjson'].includes(contentType)) {
      return true;
    }
    const fileName = file.originalFileName.toLowerCase();
    return ['.txt', '.md', '.csv', '.log', '.json', '.xml', '.yaml', '.yml'].some((extension) => fileName.endsWith(extension));
  }

  private applyPreviewBlob(file: FileViewModel, blob: Blob, generation: number): void {
    const renderer = this.previewRenderer();
    const actualContentType = blob.type.toLowerCase();

    if (renderer === 'image' && !actualContentType.startsWith('image/')) {
      this.failPreviewForContentType();
      return;
    }
    if (renderer === 'pdf' && actualContentType !== 'application/pdf') {
      this.failPreviewForContentType();
      return;
    }
    if (renderer === 'video' && !actualContentType.startsWith('video/')) {
      this.failPreviewForContentType();
      return;
    }

    const objectUrlAvailable = typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function';
    if (objectUrlAvailable) {
      const objectUrl = URL.createObjectURL(blob);
      this.previewObjectUrl = objectUrl;
      this.previewUrl.set(objectUrl);
    } else if (renderer !== 'text') {
      this.previewState.set('failed');
      this.previewMessage.set(this.i18n.translate('files.preview.localUrlUnavailable'));
      return;
    }

    if (renderer === 'text') {
      const truncated = blob.size > TEXT_PREVIEW_MAX_BYTES;
      void blob.slice(0, TEXT_PREVIEW_MAX_BYTES).text().then((text) => {
        if (generation !== this.previewGeneration || this.previewFile()?.id !== file.id) {
          return;
        }
        this.previewText.set(text);
        this.previewMessage.set(truncated ? this.i18n.translate('files.preview.textLimited') : '');
        this.previewState.set('ready');
      }).catch(() => {
        if (generation === this.previewGeneration && this.previewFile()?.id === file.id) {
          this.previewState.set('failed');
          this.previewMessage.set(this.i18n.translate('files.preview.textDecodeFailed'));
        }
      });
      return;
    }

    this.previewState.set('ready');
  }

  private previewCitationText(file: FileViewModel): string {
    const modifiedAt = this.fileModifiedLabel(file);
    const modified = modifiedAt
      ? this.i18n.translate('files.preview.citationModified', { value: modifiedAt })
      : '';
    return this.i18n.translate('files.preview.citation', {
      name: file.originalFileName,
      owner: file.uploadedByDisplay,
      modified,
      id: file.canonicalFileId ?? file.id,
    });
  }

  private fallbackCopyText(text: string): boolean {
    if (typeof document === 'undefined' || typeof document.execCommand !== 'function') {
      return false;
    }
    const active = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.append(textarea);
    textarea.select();
    let copied = false;
    try {
      copied = document.execCommand('copy');
    } finally {
      textarea.remove();
      active?.focus({ preventScroll: true });
    }
    return copied;
  }

  private failPreviewForContentType(): void {
    this.previewState.set('failed');
    this.previewMessage.set(this.i18n.translate('files.preview.contentTypeMismatch'));
  }

  private formatFileDate(value: string | undefined, fallback: string): string {
    return value
      ? this.i18n.formatDateTime(value, { dateStyle: 'medium', timeStyle: 'short' })
      : fallback;
  }

  private isCompactViewport(): boolean {
    return typeof window !== 'undefined' && window.innerWidth <= PREVIEW_OVERLAY_MAX_WIDTH;
  }
}
