import type { FileMessageSource } from './files-presentation';

export type FileKind = 'image' | 'pdf' | 'zip' | 'video' | 'svg' | 'document';

export type FileScanStatus = 'pending' | 'allowed' | 'blocked' | 'unavailable';

export type FileDownloadPolicy = 'available' | 'denied' | 'adminOverrideRequired';

/**
 * A conservative rendering of the server-owned File sharing projection. The
 * UI must never infer any of these values from ownership, paths, or cached
 * workspace members.
 */
export type FileAccessState = 'private' | 'workspace' | 'external' | 'unavailable';

export interface FileSharingViewModel {
  readonly accessState: FileAccessState;
  /** Only supplied by the server to a current sharing manager. */
  readonly externalRecipientCount?: number;
  readonly canManageSharing: boolean;
  /** Required by server-side optimistic concurrency for sharing mutations. */
  readonly sharingVersion?: number;
}

export type FileCapability = 'download' | 'requestQuotaException' | 'adminOverrideBlockedDownload';

export type FileUploadState =
  | 'idle'
  | 'pending'
  | 'succeeded'
  | 'progress'
  | 'failed'
  | 'cancelled'
  | 'tooLarge'
  | 'invalidType'
  | 'quotaExceeded'
  | 'quotaExceptionRequested'
  | 'quotaExceptionApproved'
  | 'quotaExceptionRejected';

export type FileDownloadState = 'idle' | 'pending' | 'succeeded' | 'failed';

export type FileDeleteState = 'idle' | 'pending' | 'succeeded' | 'partial' | 'failed';

export interface FileDeleteViewModel {
  readonly messageSource?: FileMessageSource;
  readonly state: FileDeleteState;
  readonly message?: string;
  readonly succeededCount: number;
  readonly failedCount: number;
}

export type FileQuotaState = 'available' | 'exceeded' | 'exceptionRequested' | 'exceptionApproved' | 'exceptionRejected';

export interface FileUploadViewModel {
  readonly messageSource?: FileMessageSource;
  readonly state: FileUploadState;
  readonly canUpload?: boolean;
  readonly selectedFileName?: string;
  readonly progressPercent?: number;
  readonly message?: string;
}

export interface FileUploadQueueItem {
  readonly messageSource?: FileMessageSource;
  readonly clientRequestId: string;
  readonly fileName: string;
  readonly state: 'pending' | 'uploading' | 'succeeded' | 'failed' | 'cancelled';
  readonly message?: string;
}

export interface FileQuotaViewModel {
  readonly messageSource?: FileMessageSource;
  readonly state: FileQuotaState;
  readonly usedBytes: number;
  readonly limitBytes: number;
  readonly message: string;
}

export interface FileViewModel {
  readonly id: string;
  readonly canonicalFileId?: string | null;
  readonly originalFileName: string;
  readonly originalFileNameIsFallback?: boolean;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly scanStatus: FileScanStatus;
  readonly uploadedByDisplay: string;
  readonly uploadedByDisplayIsFallback?: boolean;
  /** Raw server timestamps are retained so active-locale formatting happens at the UI boundary. */
  readonly createdAt?: string;
  readonly modifiedAt?: string;
  readonly createdAtLabel: string;
  readonly modifiedAtLabel: string;
  readonly kind: FileKind;
  readonly downloadPolicy: FileDownloadPolicy;
  readonly capabilities: readonly FileCapability[];
  /** Server-projected capability. Missing or malformed values must map to false. */
  readonly canDelete: boolean;
  readonly sharing: FileSharingViewModel;
  readonly downloadState?: FileDownloadState;
  readonly downloadMessage?: string;
  readonly downloadMessageSource?: FileMessageSource;
  readonly safeStatusLabel?: string;
  readonly internalStorageKey?: string;
  readonly internalPath?: string;
  readonly rawScanMetadata?: string;
}

export type FileSearchKindFilter = 'all' | 'document' | 'image' | 'pdf' | 'video' | 'archive';
export type FileSearchModifiedFilter = 'any' | 'last7Days' | 'last30Days' | 'last90Days';
export type FileSearchOwnerFilter = 'any' | 'me';

export interface FileSearchFilters {
  readonly query: string;
  readonly kind: FileSearchKindFilter;
  readonly modified: FileSearchModifiedFilter;
  readonly owner: FileSearchOwnerFilter;
}

export interface FileSearchPage {
  readonly files: readonly FileViewModel[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly hasMore: boolean;
}

export interface FileSearchViewModel extends FileSearchPage {
  readonly messageSource?: FileMessageSource;
  readonly status: 'idle' | 'loading' | 'ready' | 'empty' | 'invalid' | 'error';
  readonly workspaceId: string | null;
  readonly filters: FileSearchFilters;
  /** Exact date boundary sent with the current server-authorized search. */
  readonly fromDate?: string;
  readonly message: string;
}

export interface FileSelectionSnapshot {
  readonly id: string;
  readonly selectedCount: number;
  readonly expiresAt: string;
}

export interface FileSelectionSnapshotState {
  readonly messageSource?: FileMessageSource;
  readonly status: 'idle' | 'capturing' | 'ready' | 'overflow' | 'empty' | 'error';
  readonly selection: FileSelectionSnapshot | null;
  readonly message: string;
}

/** Query state owned by the Task-detail attachment picker, never by the Files page. */
export interface TaskFilePickerState {
  readonly messageSource?: FileMessageSource;
  readonly status: 'idle' | 'loading' | 'ready' | 'empty' | 'permissionDenied' | 'error';
  readonly workspaceId: string | null;
  readonly files: readonly FileViewModel[];
  readonly message?: string;
  readonly requestId?: string;
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly hasMore: boolean;
  readonly failedPage?: number;
}

export interface FilesPageViewModel {
  readonly title: string;
  readonly subtitle: string;
  readonly subtitleSource?: FileMessageSource;
  readonly upload: FileUploadViewModel;
  readonly uploadQueue: readonly FileUploadQueueItem[];
  readonly quota: FileQuotaViewModel;
  readonly recentFiles: readonly FileViewModel[];
  readonly pickerFiles: readonly FileViewModel[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly hasMore: boolean;
}

/** Legacy story fixture only; live upload validation is backend-authoritative. */
export const FILE_UPLOAD_MAX_BYTES = 50 * 1024 * 1024;
