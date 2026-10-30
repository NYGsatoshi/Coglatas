import { HttpClient, HttpEventType, HttpResponse } from '@angular/common/http';
import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { catchError, concatMap, finalize, from, map, Observable, of, Subscription, toArray } from 'rxjs';

import { injectFilesI18n } from '../../core/i18n/providers/files-i18n';
import { normalizeApiError } from '../../core/api/api-error.adapter';
import { TranslationKey } from '../../core/i18n/i18n.service';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import {
  AttachmentUploadResponseDto,
  FileDownloadGrantDto,
  FileDisplayLocalizer,
  FileListItemDto,
  FileSharingDetailViewModel,
  mapFileSharingResponse,
  mapFileListItem,
  PagedResponseDto,
  safeFileNameFromHeader,
} from './files.api';
import { fileErrorMessage, fileMessage, FileMessagePresentation } from './files-presentation';
import {
  fileSearchFromDate,
  fileSearchParams,
  fileSearchSelectionSnapshotParams,
  mapFileSearchResponse,
} from './files-search.api';
import {
  FileDeleteViewModel,
  FileDownloadState,
  FilesPageViewModel,
  FileUploadQueueItem,
  FileUploadViewModel,
  FileViewModel,
  FileSearchFilters,
  FileSelectionSnapshot,
  FileSelectionSnapshotState,
  FileSearchViewModel,
  TaskFilePickerState,
} from './files.types';

export const COGLATAS_FILES_PAGE_MOCK = new InjectionToken<FilesPageViewModel>('COGLATAS_FILES_PAGE_MOCK');

const FILES_PAGE_SIZE = 50;
const EMPTY_FILE_SEARCH_FILTERS: FileSearchFilters = {
  query: '',
  kind: 'all',
  modified: 'any',
  owner: 'any',
};

export interface AttachmentDownloadContext {
  /** Prevent an obsolete Task route from receiving a completion callback. */
  readonly isCurrent?: () => boolean;
  /** Exact authorized Task aggregate scope captured before grant dispatch. */
  readonly workspaceId?: string;
  /** Exact FileObject projected by the authorized Task aggregate. */
  readonly fileObjectId?: string;
  readonly onState?: (state: FileDownloadState, message: string) => void;
  readonly onPermissionDenied?: () => void;
}

@Injectable({ providedIn: 'root' })
export class FilesFacade {
  private readonly http = inject(HttpClient);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly continueWorkingHistory = inject(ContinueWorkingHistoryService);
  private readonly i18n = injectFilesI18n();
  private readonly mockPage = inject(COGLATAS_FILES_PAGE_MOCK, { optional: true });
  private readonly pageState = signal<FilesPageViewModel>(this.mockPage ?? this.emptyPage(fileMessage(this.i18n, 'files.upload.loading')));
  private readonly deleteStateSignal = signal<FileDeleteViewModel>(this.emptyDeleteState());
  private readonly inventoryRevisionSignal = signal(0);
  private readonly searchState = signal<FileSearchViewModel>(this.emptySearchState());
  private readonly searchRevisionSignal = signal(0);
  private readonly selectionSnapshotState = signal<FileSelectionSnapshotState>(this.emptySelectionSnapshotState());
  /** Task detail owns this independent query; it must never alter Files-page workspace state. */
  private readonly pickerState = signal<TaskFilePickerState>(this.emptyPickerState());
  private pageWorkspaceId: string | null = null;
  private pageGeneration = 0;
  private pickerWorkspaceId: string | null = null;
  private pickerGeneration = 0;
  private pickerRequest: Subscription | null = null;
  private readonly attachmentDownloads = new Map<string, Subscription>();
  private readonly fileDownloads = new Map<string, Subscription>();
  private readonly pageRequests = new Set<Subscription>();
  private readonly loadingWorkspaceIds = new Set<string>();
  private readonly pendingUploads = new Map<string, { file: File; subscription: Subscription; workspaceId: string }>();
  private readonly failedUploads = new Map<string, { file: File; workspaceId: string; generation: number }>();
  private deleteRequest: Subscription | null = null;
  private searchRequest: Subscription | null = null;
  private selectionSnapshotRequest: Subscription | null = null;
  private selectionSnapshotDeleteRequest: Subscription | null = null;
  private searchGeneration = 0;
  private selectionSnapshotGeneration = 0;
  private searchCurrentUserId: string | null = null;
  private refreshAfterMutation = false;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;

  readonly page = this.pageState.asReadonly();
  readonly deleteState = this.deleteStateSignal.asReadonly();
  /** Changes only when the server inventory is replaced or protected state is cleared. */
  readonly inventoryRevision = this.inventoryRevisionSignal.asReadonly();
  readonly search = this.searchState.asReadonly();
  readonly searchRevision = this.searchRevisionSignal.asReadonly();
  readonly selectionSnapshot = this.selectionSnapshotState.asReadonly();
  readonly pickerStateForTask = this.pickerState.asReadonly();
  /** Compatibility projection for existing consumers; Task detail must consume pickerStateForTask. */
  readonly pickerFiles = () => this.pickerState().files;

  constructor() {
    this.realtime.durableEvents$.subscribe((event) => this.handleRealtimeEvent(event));
    if (!this.mockPage) {
      this.realtime.registerProtectedStateClearer?.('files', () => this.clearProtectedState());
    }
  }

  /** The Files page opts into its workspace inventory; other consumers must not prefetch it. */
  loadPageFilesForWorkspace(workspaceId: string | null | undefined, force = false): void {
    if (this.mockPage) {
      return;
    }
    if (!workspaceId) {
      this.cancelDeleteOperation();
      this.invalidatePageRequests();
      this.pageWorkspaceId = null;
      this.clearFileSearch();
      this.pageState.set(this.emptyPage(fileMessage(this.i18n, 'files.upload.unavailable'), false));
      this.inventoryRevisionSignal.update((revision) => revision + 1);
      return;
    }
    if (this.pageWorkspaceId === workspaceId && !force) {
      return;
    }
    this.cancelDeleteOperation();
    this.invalidatePageRequests();
    this.pageWorkspaceId = workspaceId;
    this.clearFileSearch();
    if (force) {
      // Reauthorize a previously hidden inventory without reviving cached labels.
      this.pageState.update((page) => ({ ...page, recentFiles: [], pickerFiles: [],
        page: 1, totalCount: 0, hasMore: false }));
      this.inventoryRevisionSignal.update((revision) => revision + 1);
    }
    this.loadFiles(workspaceId, 1, FILES_PAGE_SIZE);
  }

  searchFilesForWorkspace(
    workspaceId: string,
    filters: FileSearchFilters,
    currentUserId: string | null,
    page = 1,
  ): void {
    if (this.mockPage) {
      return;
    }

    const normalized: FileSearchFilters = { ...filters, query: filters.query.trim() };
    const hasFacet = normalized.kind !== 'all' || normalized.modified !== 'any' || normalized.owner !== 'any';
    if (!normalized.query && !hasFacet) {
      this.clearFileSearch();
      return;
    }
    if (
      !workspaceId ||
      workspaceId !== this.pageWorkspaceId ||
      workspaceId !== this.activeWorkspace.activeWorkspace()?.id ||
      (normalized.query.length > 0 && normalized.query.length < 2) ||
      (normalized.owner === 'me' && !currentUserId)
    ) {
      this.cancelSearchRequest();
      this.clearSearchSelectionSnapshot();
      this.searchState.set({
        ...this.emptySearchState(workspaceId || null),
        status: 'invalid',
        filters: normalized,
        ...(normalized.query.length === 1
          ? fileMessage(this.i18n, 'files.search.invalidShortQuery')
          : fileMessage(this.i18n, 'files.search.invalidContext')),
      });
      this.searchRevisionSignal.update((revision) => revision + 1);
      return;
    }

    const safePage = Math.max(1, Math.floor(page));
    const generation = ++this.searchGeneration;
    this.clearSearchSelectionSnapshot();
    this.searchRequest?.unsubscribe();
    this.searchRequest = null;
    this.searchCurrentUserId = currentUserId;
    const requestedAt = new Date();
    const fromDate = fileSearchFromDate(normalized.modified, requestedAt);
    this.searchState.set({
      ...this.emptySearchState(workspaceId),
      status: 'loading',
      filters: normalized,
      page: safePage,
      fromDate,
      ...fileMessage(this.i18n, 'files.search.loading'),
    });
    this.searchRevisionSignal.update((revision) => revision + 1);

    const request = this.http.get<unknown>('/api/search', {
      params: fileSearchParams(workspaceId, normalized, safePage, FILES_PAGE_SIZE, currentUserId, requestedAt),
      withCredentials: true,
    }).subscribe({
      next: (response) => {
        if (!this.isCurrentSearch(generation, workspaceId)) {
          return;
        }
        this.searchRequest = null;
        const pageResult = mapFileSearchResponse(response, workspaceId, this.fileDisplayLocalizer());
        if (!pageResult) {
          this.searchState.set({
            ...this.emptySearchState(workspaceId),
            status: 'error',
            filters: normalized,
            ...fileMessage(this.i18n, 'files.search.invalidResponse'),
          });
          this.searchRevisionSignal.update((revision) => revision + 1);
          return;
        }
        this.searchState.set({
          ...pageResult,
          status: pageResult.files.length > 0 ? 'ready' : 'empty',
          workspaceId,
          filters: normalized,
          fromDate,
          ...(pageResult.files.length > 0
            ? pageResult.totalCount === 1
              ? fileMessage(this.i18n, 'files.search.oneMatch')
              : fileMessage(this.i18n, 'files.search.matches', { count: pageResult.totalCount })
            : fileMessage(this.i18n, 'files.search.noMatches')),
        });
        this.searchRevisionSignal.update((revision) => revision + 1);
      },
        error: (error: unknown) => {
        if (!this.isCurrentSearch(generation, workspaceId)) {
          return;
        }
        this.searchRequest = null;
        this.searchState.set({
          ...this.emptySearchState(workspaceId),
          status: 'error',
          filters: normalized,
          ...this.localizedError(error, 'files.search.unavailable'),
        });
        this.searchRevisionSignal.update((revision) => revision + 1);
      },
    });
    this.searchRequest = request;
  }

  goToSearchPage(page: number): void {
    const state = this.searchState();
    if (!state.workspaceId || state.status === 'idle' || state.status === 'loading') {
      return;
    }
    const totalPages = Math.max(1, Math.ceil(state.totalCount / Math.max(1, state.pageSize)));
    const target = Math.max(1, Math.min(Math.floor(page), totalPages));
    if (target !== state.page) {
      this.searchFilesForWorkspace(state.workspaceId, state.filters, this.searchCurrentUserId, target);
    }
  }

  clearFileSearch(): void {
    this.cancelSearchRequest();
    this.clearSearchSelectionSnapshot();
    this.searchCurrentUserId = null;
    this.searchState.set(this.emptySearchState(this.pageWorkspaceId));
    this.searchRevisionSignal.update((revision) => revision + 1);
  }

  /** Captures the exact currently authorized search result set on the server. */
  captureSearchSelectionSnapshot(): void {
    if (this.mockPage) {
      return;
    }

    const search = this.searchState();
    const workspaceId = search.workspaceId;
    if (!workspaceId || search.status !== 'ready' || workspaceId !== this.pageWorkspaceId ||
      workspaceId !== this.activeWorkspace.activeWorkspace()?.id) {
      this.selectionSnapshotState.set({
        ...this.emptySelectionSnapshotState(),
        status: 'error',
        ...fileMessage(this.i18n, 'files.delete.refreshSearch'),
      });
      return;
    }

    const generation = ++this.selectionSnapshotGeneration;
    const searchGeneration = this.searchGeneration;
    this.selectionSnapshotRequest?.unsubscribe();
    this.selectionSnapshotRequest = null;
    this.selectionSnapshotState.set({
      ...this.emptySelectionSnapshotState(),
      status: 'capturing',
      ...fileMessage(this.i18n, 'files.delete.capturing'),
    });

    const request = this.http.post<unknown>('/api/files/selection-snapshots', null, {
      params: fileSearchSelectionSnapshotParams(workspaceId, search.filters, search.fromDate),
      withCredentials: true,
    }).subscribe({
      next: (response) => {
        if (!this.isCurrentSelectionSnapshotRequest(generation, searchGeneration, workspaceId)) {
          return;
        }
        this.selectionSnapshotRequest = null;
        const captured = mapSelectionSnapshotCapture(response, (key, parameters) => fileMessage(this.i18n, key, parameters));
        if (!captured) {
          this.selectionSnapshotState.set({
            ...this.emptySelectionSnapshotState(),
            status: 'error',
            ...fileMessage(this.i18n, 'files.delete.invalidSelection'),
          });
          return;
        }
        this.selectionSnapshotState.set(captured);
      },
      error: (error: unknown) => {
        if (!this.isCurrentSelectionSnapshotRequest(generation, searchGeneration, workspaceId)) {
          return;
        }
        this.selectionSnapshotRequest = null;
        const normalized = normalizeApiError(error);
        this.selectionSnapshotState.set({
          ...this.emptySelectionSnapshotState(),
          status: 'error',
          ...fileErrorMessage(this.i18n, normalized, 'files.delete.selectionUnavailable'),
        });
      },
    });
    this.selectionSnapshotRequest = request;
  }

  /** The server consumes the snapshot and reauthorizes every captured FileObject. */
  deleteSearchSelectionSnapshot(onComplete?: () => void): void {
    const selection = this.selectionSnapshotState().selection;
    if (this.mockPage || !selection || this.selectionSnapshotDeleteRequest || this.deleteRequest) {
      return;
    }

    const workspaceId = this.pageWorkspaceId;
    const generation = this.pageGeneration;
    const searchGeneration = this.searchGeneration;
    if (!workspaceId) {
      return;
    }

    this.deleteStateSignal.set({
      state: 'pending',
      ...fileMessage(this.i18n, 'files.delete.pendingCaptured', { count: selection.selectedCount }),
      succeededCount: 0,
      failedCount: 0,
    });
    const request = this.http.post<unknown>(
      `/api/files/selection-snapshots/${selection.id}/delete`,
      null,
      { withCredentials: true },
    ).subscribe({
      next: (response) => {
        if (generation !== this.pageGeneration || workspaceId !== this.pageWorkspaceId) {
          return;
        }
        const deleted = mapSelectionSnapshotDelete(response);
        if (deleted?.attemptedCount !== selection.selectedCount) {
          this.deleteStateSignal.set({
            state: 'failed',
            ...fileMessage(this.i18n, 'files.delete.invalidBatchResponse'),
            succeededCount: 0,
            failedCount: selection.selectedCount,
          });
        } else if (deleted.failedCount === 0) {
          this.deleteStateSignal.set({
            state: 'succeeded',
            ...(deleted.succeededCount === 1
              ? fileMessage(this.i18n, 'files.delete.oneSuccess')
              : fileMessage(this.i18n, 'files.delete.manySuccess', { count: deleted.succeededCount })),
            succeededCount: deleted.succeededCount,
            failedCount: 0,
          });
        } else if (deleted.succeededCount > 0) {
          this.deleteStateSignal.set({
            state: 'partial',
            ...fileMessage(this.i18n, 'files.delete.partial', {
              attempted: deleted.attemptedCount,
              succeeded: deleted.succeededCount,
              failed: deleted.failedCount,
            }),
            succeededCount: deleted.succeededCount,
            failedCount: deleted.failedCount,
          });
        } else {
          this.deleteStateSignal.set({
            state: 'failed',
            ...fileMessage(this.i18n, 'files.delete.noneCaptured'),
            succeededCount: 0,
            failedCount: deleted.failedCount,
          });
        }

        this.clearSearchSelectionSnapshot();
        onComplete?.();
        const search = this.searchState();
        if (this.searchGeneration === searchGeneration &&
          search.workspaceId === workspaceId &&
          search.status !== 'idle' && search.status !== 'invalid') {
          this.searchFilesForWorkspace(workspaceId, search.filters, this.searchCurrentUserId, search.page);
        }
        const page = this.pageState();
        this.loadFiles(workspaceId, page.page, page.pageSize);
      },
      error: (error: unknown) => {
        if (generation !== this.pageGeneration || workspaceId !== this.pageWorkspaceId) {
          return;
        }
        const normalized = normalizeApiError(error);
        this.deleteStateSignal.set({
          state: 'failed',
          ...fileErrorMessage(this.i18n, normalized, 'files.delete.capturedUnavailable'),
          succeededCount: 0,
          failedCount: selection.selectedCount,
        });
        this.clearSearchSelectionSnapshot();
        onComplete?.();
      },
    });
    this.selectionSnapshotDeleteRequest = request;
    request.add(() => {
      if (this.selectionSnapshotDeleteRequest === request) {
        this.selectionSnapshotDeleteRequest = null;
      }
    });
  }

  clearSearchSelectionSnapshot(cancelMutation = false): void {
    this.selectionSnapshotGeneration++;
    this.selectionSnapshotRequest?.unsubscribe();
    this.selectionSnapshotRequest = null;
    if (cancelMutation) {
      this.selectionSnapshotDeleteRequest?.unsubscribe();
      this.selectionSnapshotDeleteRequest = null;
    }
    this.selectionSnapshotState.set(this.emptySelectionSnapshotState());
  }

  goToPage(page: number): void {
    if (this.mockPage || !this.pageWorkspaceId) {
      return;
    }

    const state = this.pageState();
    const totalPages = Math.max(1, Math.ceil(state.totalCount / Math.max(1, state.pageSize)));
    const requestedPage = Number.isFinite(page) ? Math.floor(page) : state.page;
    const targetPage = Math.max(1, Math.min(requestedPage, totalPages));
    if (targetPage === state.page) {
      return;
    }

    this.loadFiles(this.pageWorkspaceId, targetPage, state.pageSize);
  }

  uploadFiles(files: readonly File[]): void {
    for (const file of files) { this.uploadFile(file); }
  }

  uploadFile(file: File): void {
    this.startUpload(file, crypto.randomUUID());
  }

  private startUpload(file: File, clientRequestId: string): void {
    const workspaceId = this.activeWorkspace.activeWorkspace()?.id;
    const generation = this.pageGeneration;
    const currentUpload = this.pageState().upload;
    if (!workspaceId || currentUpload.state === 'pending' || currentUpload.state === 'progress') {
      return;
    }

    if (!file.name || file.size <= 0) {
      this.setUpload({
        state: 'failed',
        canUpload: true,
        selectedFileName: file.name,
        ...fileMessage(this.i18n, 'files.upload.emptyFile'),
      });
      return;
    }
    this.failedUploads.delete(clientRequestId);
    this.updateQueue({ clientRequestId, fileName: file.name, state: 'pending' });

    this.setUpload({
      state: 'pending',
      canUpload: false,
      selectedFileName: file.name,
      ...fileMessage(this.i18n, 'files.upload.progress'),
    });

    const formData = new FormData();
    formData.append('OwnerType', 'Workspace');
    formData.append('OwnerId', workspaceId);
    formData.append('File', file);

    const subscription = this.http
      .post<AttachmentUploadResponseDto>('/api/files', formData, { withCredentials: true, observe: 'events', reportProgress: true })
      .subscribe({
        next: (event) => {
          if (generation !== this.pageGeneration) { return; }
          if (event.type === HttpEventType.Sent) { this.updateQueue({ clientRequestId, fileName: file.name, state: 'uploading' }); return; }
          if (event.type === HttpEventType.UploadProgress) {
            this.setUpload({
              state: 'progress',
              canUpload: false,
              selectedFileName: file.name,
              progressPercent: event.total ? Math.round(event.loaded / event.total * 100) : undefined,
              ...fileMessage(this.i18n, 'files.upload.progress'),
            });
            return;
          }
          if (event.type !== HttpEventType.Response) { return; }
          const response = event.body ?? {};
          this.pendingUploads.delete(clientRequestId);
          this.updateQueue({ clientRequestId, fileName: stringValue(response.originalFileName) ?? file.name, state: 'succeeded' });
          this.setUpload({
            state: 'succeeded',
            canUpload: true,
            selectedFileName: stringValue(response.originalFileName) ?? file.name,
            ...fileMessage(this.i18n, 'files.upload.accepted'),
          });
          this.loadingWorkspaceIds.delete(workspaceId);
          this.loadFiles(workspaceId, 1, this.pageState().pageSize);
          this.reconcileAfterMutation(workspaceId);
        },
        error: (error: unknown) => {
          if (generation !== this.pageGeneration) { return; }
          this.pendingUploads.delete(clientRequestId);
          const normalized = normalizeApiError(error);
          if (normalized.httpStatus !== 401 && normalized.httpStatus !== 403) {
            this.failedUploads.set(clientRequestId, { file, workspaceId, generation });
          }
          const message = fileErrorMessage(this.i18n, normalized, 'api.requestFailed');
          this.updateQueue({ clientRequestId, fileName: file.name, state: 'failed', ...message });
          this.setUpload({
            state: 'failed',
            canUpload: true,
            selectedFileName: file.name,
            ...message,
          });
          this.reconcileAfterMutation(workspaceId);
        },
      });
    if (!subscription.closed) { this.pendingUploads.set(clientRequestId, { file, subscription, workspaceId }); }
  }

  cancelUpload(clientRequestId: string): void {
    const pending = this.pendingUploads.get(clientRequestId);
    if (!pending) { return; }
    pending.subscription.unsubscribe();
    this.pendingUploads.delete(clientRequestId);
    this.updateQueue({ clientRequestId, fileName: pending.file.name, state: 'cancelled' });
    this.setUpload({
      state: 'cancelled',
      canUpload: this.canUploadNow(),
      selectedFileName: pending.file.name,
      ...fileMessage(this.i18n, 'files.upload.cancelledMessage'),
    });
    if (pending.workspaceId === this.pageWorkspaceId && pending.workspaceId === this.activeWorkspace.activeWorkspace()?.id) {
      this.reconcileAfterMutation(pending.workspaceId);
    }
  }

  retryUpload(clientRequestId: string): void {
    const item = this.failedUploads.get(clientRequestId);
    if (!item) { return; }
    if (item.workspaceId !== this.activeWorkspace.activeWorkspace()?.id || item.generation !== this.pageGeneration) {
      this.failedUploads.delete(clientRequestId);
      return;
    }
    this.startUpload(item.file, clientRequestId);
  }

  /** Reads the manager-only sharing inspection projection for one File. */
  getFileSharing(fileObjectId: string): Observable<FileSharingDetailViewModel> {
    return this.http.get<unknown>(`/api/files/${encodeURIComponent(fileObjectId)}/sharing`, {
      withCredentials: true,
    }).pipe(map((response) => this.requireSharingDetail(response)));
  }

  updateFileSharingPolicy(
    fileObjectId: string,
    shareWithWorkspace: boolean,
    expectedSharingVersion: number,
  ): Observable<FileSharingDetailViewModel> {
    return this.http.put<unknown>(`/api/files/${encodeURIComponent(fileObjectId)}/sharing`, {
      shareWithWorkspace,
      expectedSharingVersion,
    }, { withCredentials: true }).pipe(map((response) => this.requireSharingDetail(response)));
  }

  grantFileSharingRecipient(
    fileObjectId: string,
    recipientUserId: string,
    expectedSharingVersion: number,
  ): Observable<FileSharingDetailViewModel> {
    return this.http.post<unknown>(`/api/files/${encodeURIComponent(fileObjectId)}/sharing/recipients`, {
      recipientUserId,
      expectedSharingVersion,
    }, { withCredentials: true }).pipe(map((response) => this.requireSharingDetail(response)));
  }

  revokeFileSharingRecipient(
    fileObjectId: string,
    grantId: string,
    expectedSharingVersion: number,
  ): Observable<FileSharingDetailViewModel> {
    return this.http.delete<unknown>(
      `/api/files/${encodeURIComponent(fileObjectId)}/sharing/recipients/${encodeURIComponent(grantId)}`,
      {
        params: { expectedSharingVersion },
        withCredentials: true,
      },
    ).pipe(map((response) => this.requireSharingDetail(response)));
  }

  /**
   * Applies a successful server mutation to every visible Files projection
   * without manufacturing a state client-side. The page still receives the
   * regular realtime refresh for all other viewers.
   */
  reconcileFileSharing(detail: FileSharingDetailViewModel): void {
    const replace = (files: readonly FileViewModel[]): readonly FileViewModel[] => {
      let changed = false;
      const next = files.map((file) => {
        if (file.canonicalFileId !== detail.fileObjectId) {
          return file;
        }
        changed = true;
        return { ...file, sharing: detail.sharing };
      });
      return changed ? next : files;
    };

    this.pageState.update((page) => {
      const recentFiles = replace(page.recentFiles);
      const pickerFiles = replace(page.pickerFiles);
      return recentFiles === page.recentFiles && pickerFiles === page.pickerFiles
        ? page
        : { ...page, recentFiles, pickerFiles };
    });
    this.searchState.update((search) => {
      // Do not manufacture a new idle search snapshot. FilesPageComponent
      // treats a replaced search snapshot as a server-authoritative reset,
      // so an unnecessary write here would close the active Preview/dialog.
      if (search.status === 'idle') {
        return search;
      }
      const files = replace(search.files);
      return files === search.files ? search : { ...search, files };
    });
  }

  downloadFile(fileObjectId: string): void {
    if (!fileObjectId || this.mockPage) {
      return;
    }

    const file = this.findFile(fileObjectId);
    // A fresh inventory/search row does not own the lifetime of an active download.
    if (!file || file.downloadState === 'pending' || this.fileDownloads.has(fileObjectId)) {
      return;
    }

    this.updateFileDownload(fileObjectId, {
      downloadState: 'pending',
      ...downloadMessage(fileMessage(this.i18n, 'files.download.authorizing')),
    });

    const generation = this.pageGeneration;
    const operation = new Subscription();
    this.fileDownloads.set(fileObjectId, operation);
    const grantRequest = this.http.post<FileDownloadGrantDto>(
        `/api/files/${fileObjectId}/download-grants`,
        { purpose: 'files-page-download' },
        { withCredentials: true },
      )
      .subscribe({
        next: (grant) => {
          if (!this.isCurrentPageOperation(generation, fileObjectId)) { return; }
          this.downloadWithGrant(fileObjectId, grant, generation, operation);
        },
        error: (error: unknown) => {
          if (!this.isCurrentPageOperation(generation, fileObjectId)) { return; }
          const normalized = normalizeApiError(error);
          this.updateFileDownload(fileObjectId, {
            downloadState: 'failed',
            ...downloadMessage(normalized.httpStatus === 403
              ? fileMessage(this.i18n, 'files.download.denied')
              : fileErrorMessage(this.i18n, normalized, 'api.requestFailed')),
          });
          operation.unsubscribe();
        },
      });
    operation.add(grantRequest);
    operation.add(() => this.fileDownloads.delete(fileObjectId));
    this.trackPageRequest(operation);
  }

  /**
   * Uses the existing single-file mutation contract. Requests are intentionally
   * serial and independent; callers must never present this as an atomic batch.
   */
  deleteFiles(files: readonly FileViewModel[], onComplete?: () => void): void {
    if (this.mockPage || this.deleteRequest || !this.pageWorkspaceId || files.length === 0) {
      return;
    }

    const uniqueFiles = [...new Map(files.map((file) => [file.canonicalFileId, file])).values()]
      .filter((file): file is FileViewModel & { canonicalFileId: string } =>
        typeof file.canonicalFileId === 'string' && file.canonicalFileId.length > 0);
    if (uniqueFiles.length !== files.length || uniqueFiles.some((file) => file.canDelete !== true)) {
      this.deleteStateSignal.set({
        state: 'failed',
        ...fileMessage(this.i18n, 'files.delete.notAuthorized'),
        succeededCount: 0,
        failedCount: files.length,
      });
      return;
    }

    const workspaceId = this.pageWorkspaceId;
    const generation = this.pageGeneration;
    this.deleteStateSignal.set({
      state: 'pending',
      ...(uniqueFiles.length === 1
        ? fileMessage(this.i18n, 'files.delete.pendingOne')
        : fileMessage(this.i18n, 'files.delete.pendingMany', { count: uniqueFiles.length })),
      succeededCount: 0,
      failedCount: 0,
    });

    const request = from(uniqueFiles).pipe(
      concatMap((file) => this.http
        .delete<void>(`/api/files/${file.canonicalFileId}`, { withCredentials: true })
        .pipe(
          map(() => ({ file, succeeded: true as const })),
          catchError(() => of({ file, succeeded: false as const })),
        )),
      toArray(),
    ).subscribe((results) => {
      if (generation !== this.pageGeneration || workspaceId !== this.pageWorkspaceId) {
        return;
      }

      const succeeded = results.filter((result) => result.succeeded);
      const failedCount = results.length - succeeded.length;
      const succeededIds = new Set(succeeded.map((result) => result.file.canonicalFileId));
      if (succeededIds.size > 0) {
        this.pageState.update((page) => ({
          ...page,
          recentFiles: page.recentFiles.filter((file) => !file.canonicalFileId || !succeededIds.has(file.canonicalFileId)),
          pickerFiles: page.pickerFiles.filter((file) => !file.canonicalFileId || !succeededIds.has(file.canonicalFileId)),
          totalCount: Math.max(0, page.totalCount - succeededIds.size),
        }));
      }

      if (failedCount === 0) {
        this.deleteStateSignal.set({
          state: 'succeeded',
          ...(results.length === 1
            ? fileMessage(this.i18n, 'files.delete.oneSuccess')
            : fileMessage(this.i18n, 'files.delete.manySuccess', { count: results.length })),
          succeededCount: results.length,
          failedCount: 0,
        });
      } else if (succeeded.length > 0) {
        this.deleteStateSignal.set({
          state: 'partial',
          ...fileMessage(this.i18n, 'files.delete.partial', {
            attempted: results.length,
            succeeded: succeeded.length,
            failed: failedCount,
          }),
          succeededCount: succeeded.length,
          failedCount,
        });
      } else {
        this.deleteStateSignal.set({
          state: 'failed',
          ...fileMessage(this.i18n, 'files.delete.noneSelected'),
          succeededCount: 0,
          failedCount,
        });
      }

      onComplete?.();
      const current = this.pageState();
      this.loadFiles(workspaceId, current.page, current.pageSize);
    });
    this.deleteRequest = request;
    request.add(() => {
      if (this.deleteRequest === request) {
        this.deleteRequest = null;
      }
    });
  }

  loadPickerFilesForWorkspace(workspaceId: string): void {
    if (!workspaceId || this.mockPage) { this.clearPickerFiles(); return; }
    if (this.pickerWorkspaceId === workspaceId && (this.pickerRequest || this.pickerState().status !== 'idle')) {return;}
    this.pickerGeneration++;
    const generation = this.pickerGeneration;
    this.pickerWorkspaceId = workspaceId;
    this.pickerRequest?.unsubscribe();
    this.pickerState.set({ ...this.emptyPickerState(workspaceId), status: 'loading' });
    this.loadPickerPage(workspaceId, 1, generation, true);
  }

  loadMorePickerFiles(): void {
    const state = this.pickerState();
    if (!state.workspaceId || !state.hasMore || this.pickerRequest) {return;}
    this.loadPickerPage(state.workspaceId, state.page + 1, this.pickerGeneration, false);
  }

  retryPickerFiles(): void {
    const state = this.pickerState();
    if (!state.workspaceId || this.pickerRequest) {return;}
    this.loadPickerPage(state.workspaceId, state.failedPage ?? 1, this.pickerGeneration, (state.failedPage ?? 1) === 1);
  }

  clearPickerFiles(): void {
    this.pickerGeneration++;
    this.pickerRequest?.unsubscribe();
    this.pickerRequest = null;
    this.pickerWorkspaceId = null;
    this.pickerState.set(this.emptyPickerState());
  }

  private loadPickerPage(workspaceId: string, page: number, generation: number, replace: boolean): void {
    const before = this.pickerState();
    this.pickerRequest = this.http.get<PagedResponseDto<FileListItemDto>>('/api/files', {
      params: { workspaceId, page, pageSize: before.pageSize || 20 }, withCredentials: true
    }).pipe(finalize(() => {
      if (generation === this.pickerGeneration) {this.pickerRequest = null;}
    })).subscribe({
      next: response => {
        if (generation !== this.pickerGeneration || this.pickerWorkspaceId !== workspaceId) {return;}
        const incoming = (response.items ?? [])
          .map((item) => mapFileListItem(item, this.fileDisplayLocalizer()))
          .filter(file => file.id.length > 0);
        const existing = replace ? [] : this.pickerState().files;
        const ids = new Set(existing.map(file => file.id));
        const files = [...existing, ...incoming.filter(file => !ids.has(file.id) && (ids.add(file.id), true))];
        this.pickerState.set({ status: files.length ? 'ready' : 'empty', workspaceId, files, page: numberValue(response.page) || page, pageSize: numberValue(response.pageSize) || before.pageSize || 20, totalCount: numberValue(response.totalCount) || files.length, hasMore: response.hasMore === true });
      },
      error: error => {
        if (generation !== this.pickerGeneration || this.pickerWorkspaceId !== workspaceId) {return;}
        const normalized = normalizeApiError(error);
        const current = this.pickerState();
        this.pickerState.set({
          ...current,
          workspaceId,
          status: normalized.httpStatus === 401 || normalized.httpStatus === 403 ? 'permissionDenied' : 'error',
          ...fileErrorMessage(this.i18n, normalized, 'api.requestFailed'),
          requestId: normalized.requestId,
          failedPage: page,
        });
      }
    });
  }

  /** Uses the canonical attachment grant boundary; grant tokens never enter signal state. */
  downloadAttachment(attachmentId: string, fallbackFileName: string, context: AttachmentDownloadContext = {}): Subscription | null {
    if (!attachmentId || this.mockPage || this.attachmentDownloads.has(attachmentId)) {return null;}
    const operationWorkspaceId = context.workspaceId;
    const expectedFileObjectId = fileObjectIdentity(context.fileObjectId);
    if (operationWorkspaceId && this.activeWorkspace.activeWorkspace()?.id !== operationWorkspaceId) {return null;}
    if (!expectedFileObjectId) {
      if (context.isCurrent?.() !== false) {
          context.onState?.('failed', this.i18n.translate('files.download.missingIdentity'));
      }
      return null;
    }
    const operation = new Subscription();
    this.attachmentDownloads.set(attachmentId, operation);
    const operationIsCurrent = () => context.isCurrent?.() !== false &&
      (!operationWorkspaceId || this.activeWorkspace.activeWorkspace()?.id === operationWorkspaceId);
    const report = (state: FileDownloadState, message: string) => {
      if (operationIsCurrent()) {context.onState?.(state, message);}
    };
    const denied = () => { if (operationIsCurrent()) {context.onPermissionDenied?.();} };
    report('pending', this.i18n.translate('files.download.authorizing'));
    const grantRequest = this.http.post<FileDownloadGrantDto>(`/api/attachments/${attachmentId}/download-grants`, { purpose: 'task-detail-download' }, { withCredentials: true }).subscribe({
      next: grant => {
        if (!operationIsCurrent()) { operation.unsubscribe(); return; }
        const grantId = stringValue(grant.fileDownloadGrantId);
        const fileObjectId = fileObjectIdentity(grant.fileObjectId);
        const token = stringValue(grant.token);
        if (!grantId || !fileObjectId || !token || fileObjectId !== expectedFileObjectId) {
          report('failed', this.i18n.translate('files.download.invalidGrant'));
          operation.unsubscribe();
          return;
        }
        const downloadRequest = this.http.post(`/api/attachment-download-grants/${grantId}/download`, { token }, { observe: 'response', responseType: 'blob', withCredentials: true }).subscribe({
          next: response => {
            if (!operationIsCurrent()) { operation.unsubscribe(); return; }
            const downloaded = this.saveBlob(response, safeFileNameFromHeader(response.headers.get('content-disposition'), fallbackFileName));
            if (downloaded && operationWorkspaceId) {
              this.continueWorkingHistory.touchFile(expectedFileObjectId, operationWorkspaceId);
            }
            report(downloaded ? 'succeeded' : 'failed', this.i18n.translate(downloaded ? 'files.download.started' : 'api.requestFailed'));
            operation.unsubscribe();
          },
          error: error => {
            if (!operationIsCurrent()) { operation.unsubscribe(); return; }
            const normalized = normalizeApiError(error);
            if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {denied();}
            report('failed', normalized.httpStatus === 403
              ? this.i18n.translate('files.download.denied')
              : this.i18n.apiErrorMessage(normalized, 'api.requestFailed'));
            operation.unsubscribe();
          }
        });
        operation.add(downloadRequest);
      },
      error: error => {
        if (!operationIsCurrent()) { operation.unsubscribe(); return; }
        const normalized = normalizeApiError(error);
        if (normalized.httpStatus === 401 || normalized.httpStatus === 403) {denied();}
        report('failed', normalized.httpStatus === 403
          ? this.i18n.translate('files.download.denied')
          : this.i18n.apiErrorMessage(normalized, 'api.requestFailed'));
        operation.unsubscribe();
      }
    });
    operation.add(grantRequest);
    operation.add(() => this.attachmentDownloads.delete(attachmentId));
    return operation;
  }

  cancelAttachmentDownloads(): void {
    this.attachmentDownloads.forEach(request => request.unsubscribe());
    this.attachmentDownloads.clear();
  }

  private loadFiles(workspaceId: string, page: number, pageSize: number): void {
    if (this.loadingWorkspaceIds.has(workspaceId)) {
      return;
    }

    const safePage = Math.max(1, Math.floor(page));
    const safePageSize = Math.max(1, Math.min(Math.floor(pageSize || FILES_PAGE_SIZE), 100));
    const generation = this.pageGeneration;
    this.loadingWorkspaceIds.add(workspaceId);
    this.pageState.update((state) => ({ ...state, inventoryState: 'loading' }));
    const request = this.http
      .get<PagedResponseDto<FileListItemDto>>('/api/files', {
        params: { workspaceId, page: safePage, pageSize: safePageSize },
        withCredentials: true,
      })
      .subscribe({
        next: (response) => {
          this.loadingWorkspaceIds.delete(workspaceId);
          if (generation !== this.pageGeneration || this.pageWorkspaceId !== workspaceId) {
            return;
          }

          const files = (response.items ?? [])
            .map((item) => mapFileListItem(item, this.fileDisplayLocalizer()))
            .filter((file) => file.id.length > 0);
          const responsePage = numberValue(response.page) ?? safePage;
          const responsePageSize = numberValue(response.pageSize) ?? safePageSize;
          const totalCount = numberValue(response.totalCount) ?? files.length;
          const totalPages = Math.max(1, Math.ceil(totalCount / Math.max(1, responsePageSize)));

          if (files.length === 0 && responsePage > totalPages) {
            this.loadFiles(workspaceId, totalPages, responsePageSize);
            return;
          }

          this.pageState.set({
            ...this.emptyPage(files.length === 0
              ? fileMessage(this.i18n, 'files.empty')
              : fileMessage(this.i18n, 'files.loaded')),
            upload: this.pageUploadForWorkspace(workspaceId),
            uploadQueue: this.pageState().uploadQueue,
            recentFiles: files,
            inventoryState: 'ready',
            pickerFiles: files,
            page: responsePage,
            pageSize: responsePageSize,
            totalCount,
            hasMore: response.hasMore === true || responsePage * responsePageSize < totalCount,
          });
          this.inventoryRevisionSignal.update((revision) => revision + 1);
        },
        error: (error: unknown) => {
          this.loadingWorkspaceIds.delete(workspaceId);
          if (generation !== this.pageGeneration || this.pageWorkspaceId !== workspaceId) {
            return;
          }
          const normalized = normalizeApiError(error);
          this.pageState.set({
            ...this.emptyPage(fileErrorMessage(this.i18n, normalized, 'api.requestFailed')),
            upload: this.pageUploadForWorkspace(workspaceId),
            uploadQueue: this.pageState().uploadQueue,
            inventoryState: [401, 403, 404].includes(normalized.httpStatus) ? 'denied' : 'failed',
            page: safePage,
            pageSize: safePageSize,
          });
          this.inventoryRevisionSignal.update((revision) => revision + 1);
        },
      });
    this.trackPageRequest(request);
  }

  private pageUploadForWorkspace(workspaceId: string): FileUploadViewModel {
    const canUpload = this.activeWorkspace.activeWorkspace()?.id === workspaceId,
      { upload } = this.pageState();
    if (upload.state !== 'idle') {return upload;}
    let message = fileMessage(this.i18n, 'files.upload.unavailable');
    if (canUpload) {message = fileMessage(this.i18n, 'files.upload.select');}
    return { ...upload, canUpload, ...message };
  }

  private downloadWithGrant(fileObjectId: string, grant: FileDownloadGrantDto, generation: number, operation: Subscription): void {
    const expectedFileObjectId = fileObjectIdentity(fileObjectId);
    const grantId = stringValue(grant.fileDownloadGrantId);
    const grantedFileObjectId = fileObjectIdentity(grant.fileObjectId);
    const token = stringValue(grant.token);
    if (!expectedFileObjectId || !grantId || !grantedFileObjectId || grantedFileObjectId !== expectedFileObjectId || !token) {
      this.updateFileDownload(fileObjectId, {
        downloadState: 'failed',
        ...downloadMessage(fileMessage(this.i18n, 'files.download.invalidGrant')),
      });
      operation.unsubscribe();
      return;
    }

    const request = this.http
      .post(`/api/file-download-grants/${grantId}/download`, { token }, {
        observe: 'response',
        responseType: 'blob',
        withCredentials: true,
      })
      .subscribe({
        next: (response) => {
          if (!this.isCurrentPageOperation(generation, fileObjectId)) { return; }
          const fileName = safeFileNameFromHeader(
            response.headers.get('content-disposition'),
            this.findFile(fileObjectId)?.originalFileName ?? this.i18n.translate('files.untitledFile'),
          );
          const downloaded = this.saveBlob(response, fileName);
          if (downloaded) {
            this.continueWorkingHistory.touchFile(expectedFileObjectId, this.pageWorkspaceId);
          }
          this.updateFileDownload(fileObjectId, {
            downloadState: downloaded ? 'succeeded' : 'failed',
            ...downloadMessage(fileMessage(this.i18n, downloaded ? 'files.download.started' : 'api.requestFailed')),
          });
          operation.unsubscribe();
        },
        error: (error: unknown) => {
          if (!this.isCurrentPageOperation(generation, fileObjectId)) { return; }
          const normalized = normalizeApiError(error);
          this.updateFileDownload(fileObjectId, {
            downloadState: 'failed',
            ...downloadMessage(normalized.httpStatus === 403
              ? fileMessage(this.i18n, 'files.download.denied')
              : fileErrorMessage(this.i18n, normalized, 'api.requestFailed')),
          });
          operation.unsubscribe();
        },
      });
    operation.add(request);
  }

  private saveBlob(response: HttpResponse<Blob>, fileName: string): boolean {
    const blob = response.body;
    if (!blob || typeof URL === 'undefined' || typeof URL.createObjectURL !== 'function') {
      return false;
    }

    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(objectUrl);
    return true;
  }

  private setUpload(upload: FileUploadViewModel): void {
    this.pageState.update((page) => ({ ...page, upload }));
  }

  private updateFileDownload(fileObjectId: string, patch: Pick<FileViewModel, 'downloadState' | 'downloadMessage' | 'downloadMessageSource'>): void {
    this.pageState.update((page) => {
      const files = page.recentFiles.map((file) =>
        file.canonicalFileId === fileObjectId ? { ...file, ...patch } : file,
      );
      return {
        ...page,
        recentFiles: files,
        pickerFiles: page.pickerFiles.map((file) =>
          file.canonicalFileId === fileObjectId ? { ...file, ...patch } : file,
        ),
      };
    });
    this.searchState.update((search) => ({
      ...search,
      files: search.files.map((file) =>
        file.canonicalFileId === fileObjectId ? { ...file, ...patch } : file),
    }));
  }

  private requireSharingDetail(response: unknown): FileSharingDetailViewModel {
    const detail = mapFileSharingResponse(response);
    if (!detail) {
      throw new Error('Invalid File sharing response.');
    }
    return detail;
  }

  private handleRealtimeEvent(event: DurableRealtimeEvent): void {
    if (this.mockPage || event.eventType !== 'Files.FileChanged.v1') {
      return;
    }
    const workspaceId = this.activeWorkspace.activeWorkspace()?.id;
    if (!workspaceId) {
      return;
    }
    if (this.pendingUploads.size > 0) {
      this.refreshAfterMutation = true;
      return;
    }
    this.queueRefresh(workspaceId);
  }

  private reconcileAfterMutation(workspaceId: string): void {
    if (this.pendingUploads.size === 0 && this.refreshAfterMutation) {
      this.refreshAfterMutation = false;
      this.queueRefresh(workspaceId);
    }
  }

  private queueRefresh(workspaceId: string): void {
    if (this.refreshTimer !== null) {
      return;
    }
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = null;
      const search = this.searchState();
      if (search.workspaceId === workspaceId && search.status !== 'idle' && search.status !== 'invalid') {
        this.searchFilesForWorkspace(workspaceId, search.filters, this.searchCurrentUserId, search.page);
      }
      const state = this.pageState();
      this.loadFiles(workspaceId, state.page, state.pageSize);
    }, 100);
  }

  private updateQueue(item: FileUploadQueueItem): void {
    this.pageState.update((page) => ({ ...page, uploadQueue: [...page.uploadQueue.filter((queued) => queued.clientRequestId !== item.clientRequestId), item] }));
  }

  private findFile(fileObjectId: string): FileViewModel | undefined {
    return this.searchState().files.find((file) => file.canonicalFileId === fileObjectId) ??
      this.pageState().recentFiles.find((file) => file.canonicalFileId === fileObjectId);
  }

  private emptyPage(subtitle: Required<FileMessagePresentation>, canUpload = true): FilesPageViewModel {
    return {
      title: this.i18n.translate('files.title'),
      inventoryState: canUpload ? 'ready' : 'unavailable',
      subtitle: subtitle.message,
      subtitleSource: subtitle.messageSource,
      upload: {
        state: 'idle',
        canUpload,
        ...(canUpload
          ? fileMessage(this.i18n, 'files.upload.select')
          : fileMessage(this.i18n, 'files.upload.unavailable')),
      },
      uploadQueue: [],
      quota: {
        state: 'available',
        usedBytes: 0,
        limitBytes: 0,
        ...fileMessage(this.i18n, 'files.quota.unavailable'),
      },
      recentFiles: [],
      pickerFiles: [],
      page: 1,
      pageSize: FILES_PAGE_SIZE,
      totalCount: 0,
      hasMore: false,
    };
  }

  private canUploadNow(): boolean {
    return this.pendingUploads.size === 0 && this.pageWorkspaceId !== null &&
      this.activeWorkspace.activeWorkspace()?.id === this.pageWorkspaceId;
  }

  private emptyPickerState(workspaceId: string | null = null): TaskFilePickerState {
    return { status: 'idle', workspaceId, files: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false };
  }

  private emptySearchState(workspaceId: string | null = null): FileSearchViewModel {
    return {
      status: 'idle',
      workspaceId,
      filters: EMPTY_FILE_SEARCH_FILTERS,
      files: [],
      page: 1,
      pageSize: FILES_PAGE_SIZE,
      totalCount: 0,
      hasMore: false,
      message: '',
    };
  }

  private emptySelectionSnapshotState(): FileSelectionSnapshotState {
    return { status: 'idle', selection: null, message: '' };
  }

  private emptyDeleteState(): FileDeleteViewModel {
    return { state: 'idle', succeededCount: 0, failedCount: 0 };
  }

  private fileDisplayLocalizer(): FileDisplayLocalizer {
    return {
      untitledFile: this.i18n.translate('files.untitledFile'),
      unknownUser: this.i18n.translate('files.unknownUser'),
      formatDate: (value) => value
        ? this.i18n.formatDateTime(value, { dateStyle: 'medium', timeStyle: 'short' })
        : '',
    };
  }

  private localizedError(error: unknown, fallback: TranslationKey): Required<FileMessagePresentation> {
    return fileErrorMessage(this.i18n, error, fallback);
  }

  private cancelDeleteOperation(): void {
    this.deleteRequest?.unsubscribe();
    this.deleteRequest = null;
    this.deleteStateSignal.set(this.emptyDeleteState());
  }

  private trackPageRequest(request: Subscription): void {
    this.pageRequests.add(request);
    request.add(() => this.pageRequests.delete(request));
  }

  private invalidatePageRequests(): void {
    this.pageGeneration++;
    this.failedUploads.clear();
    for (const pending of [...this.pendingUploads.values()]) {pending.subscription.unsubscribe();}
    this.pendingUploads.clear();
    this.pageState.update((page) => ({
      ...page,
      upload: { state: 'idle', canUpload: false, ...fileMessage(this.i18n, 'files.upload.unavailable') },
      uploadQueue: [],
    }));
    if (this.refreshTimer !== null) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
    this.refreshAfterMutation = false;
    for (const request of [...this.pageRequests]) {request.unsubscribe();}
    this.pageRequests.clear();
    this.loadingWorkspaceIds.clear();
  }

  private cancelSearchRequest(): void {
    this.searchGeneration++;
    this.searchRequest?.unsubscribe();
    this.searchRequest = null;
  }

  private isCurrentSearch(generation: number, workspaceId: string): boolean {
    return generation === this.searchGeneration &&
      this.pageWorkspaceId === workspaceId &&
      this.activeWorkspace.activeWorkspace()?.id === workspaceId;
  }

  private isCurrentSelectionSnapshotRequest(
    generation: number,
    searchGeneration: number,
    workspaceId: string,
  ): boolean {
    return generation === this.selectionSnapshotGeneration &&
      searchGeneration === this.searchGeneration &&
      this.isCurrentSearch(searchGeneration, workspaceId);
  }

  private isCurrentPageOperation(generation: number, fileObjectId: string): boolean {
    return generation === this.pageGeneration &&
      this.pageWorkspaceId !== null &&
      this.fileDownloads.has(fileObjectId);
  }

  private clearProtectedState(): void {
    this.cancelDeleteOperation();
    this.clearSearchSelectionSnapshot(true);
    this.invalidatePageRequests();
    this.pageWorkspaceId = null;
    this.clearFileSearch();
    this.clearPickerFiles();
    for (const operation of [...this.fileDownloads.values()]) {operation.unsubscribe();}
    this.fileDownloads.clear();
    this.cancelAttachmentDownloads();
    this.pageState.set(this.emptyPage(fileMessage(this.i18n, 'files.upload.unavailable'), false));
    this.inventoryRevisionSignal.update((revision) => revision + 1);
  }

}

function numberValue(value: unknown): number | undefined { return typeof value === 'number' && Number.isFinite(value) ? value : undefined; }

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function fileObjectIdentity(value: unknown): string | undefined {
  if (typeof value !== 'string') {return undefined;}
  const normalized = value.trim().toLowerCase();
  return fileObjectIdPattern.test(normalized) ? normalized : undefined;
}

const fileObjectIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u;

function mapSelectionSnapshotCapture(
  value: unknown,
  translate: (key: TranslationKey, parameters?: Readonly<Record<string, string | number>>) => Required<FileMessagePresentation>,
): FileSelectionSnapshotState | null {
  if (!isRecord(value) || typeof value['outcome'] !== 'string') {
    return null;
  }

  if (value['outcome'] === 'Overflow' && positiveInteger(value['maximumSelectionCount'])) {
    const maximumSelectionCount = positiveInteger(value['maximumSelectionCount']);
    return {
      status: 'overflow',
      selection: null,
      ...translate('files.selection.overflow', { count: maximumSelectionCount }),
    };
  }
  if (value['outcome'] === 'Empty') {
    return {
      status: 'empty',
      selection: null,
      ...translate('files.selection.empty'),
    };
  }
  if (value['outcome'] !== 'Captured') {
    return null;
  }

  const id = fileObjectIdentity(value['selectionSnapshotId']);
  const selectedCount = positiveInteger(value['selectedCount']);
  const expiresAt = isoTimestamp(value['expiresAt']);
  if (!id || !selectedCount || !expiresAt) {
    return null;
  }
  return {
    status: 'ready',
    selection: { id, selectedCount, expiresAt },
    ...translate('files.selection.capturedCount', { count: selectedCount }),
  };
}

function mapSelectionSnapshotDelete(value: unknown): {
  attemptedCount: number;
  succeededCount: number;
  failedCount: number;
} | null {
  if (!isRecord(value)) {
    return null;
  }
  const attemptedCount = nonNegativeInteger(value['attemptedCount']);
  const succeededCount = nonNegativeInteger(value['succeededCount']);
  const failedCount = nonNegativeInteger(value['failedCount']);
  if (attemptedCount === null || succeededCount === null || failedCount === null ||
    attemptedCount !== succeededCount + failedCount) {
    return null;
  }
  return { attemptedCount, succeededCount, failedCount };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function positiveInteger(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : null;
}

function nonNegativeInteger(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : null;
}

function isoTimestamp(value: unknown): string | undefined {
  return typeof value === 'string' && Number.isFinite(Date.parse(value)) ? value : undefined;
}

function downloadMessage(value: Required<FileMessagePresentation>): Pick<FileViewModel, 'downloadMessage' | 'downloadMessageSource'> {
  return { downloadMessage: value.message, downloadMessageSource: value.messageSource };
}
