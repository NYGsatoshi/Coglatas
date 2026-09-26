import { FILE_UPLOAD_MAX_BYTES, FilesPageViewModel, FileViewModel } from './files.types';

const MOCK_FILE_SHARING = {
  accessState: 'unavailable',
  canManageSharing: false,
} as const;

export const DEFAULT_FILES: readonly FileViewModel[] = [
  {
    id: 'attachment-001',
    canonicalFileId: 'file-object-001',
    originalFileName: 'sanitized-project-note.pdf',
    contentType: 'application/pdf',
    sizeBytes: 2_440_192,
    scanStatus: 'allowed',
    uploadedByDisplay: 'Fixture User',
    createdAtLabel: '2026-07-01 09:30',
    modifiedAtLabel: '2026-07-01 09:30',
    kind: 'pdf',
    downloadPolicy: 'available',
    capabilities: ['download'],
    canDelete: true,
    sharing: MOCK_FILE_SHARING,
    internalStorageKey: 'tenant-a/private/raw/sanitized-project-note.pdf',
    internalPath: '/var/lib/coglatas/private/raw/sanitized-project-note.pdf',
    rawScanMetadata: 'engine=mock;signature=private-debug-value',
  },
  {
    id: 'attachment-002',
    canonicalFileId: 'file-object-002',
    originalFileName: 'classroom-photo.png',
    contentType: 'image/png',
    sizeBytes: 814_320,
    scanStatus: 'pending',
    uploadedByDisplay: 'Fixture User',
    createdAtLabel: '2026-07-01 10:12',
    modifiedAtLabel: '2026-07-01 10:12',
    kind: 'image',
    downloadPolicy: 'available',
    capabilities: ['download'],
    canDelete: true,
    sharing: MOCK_FILE_SHARING,
  },
  {
    id: 'attachment-003',
    canonicalFileId: 'file-object-003',
    originalFileName: 'blocked-archive.zip',
    contentType: 'application/zip',
    sizeBytes: 18_982_144,
    scanStatus: 'blocked',
    uploadedByDisplay: 'Fixture User',
    createdAtLabel: '2026-07-01 10:40',
    modifiedAtLabel: '2026-07-01 10:40',
    kind: 'zip',
    downloadPolicy: 'adminOverrideRequired',
    capabilities: ['adminOverrideBlockedDownload'],
    canDelete: false,
    sharing: MOCK_FILE_SHARING,
    safeStatusLabel: 'Admin override requires a backend path.',
  },
  {
    id: 'attachment-004',
    canonicalFileId: null,
    originalFileName: 'pending-canonical-id.svg',
    contentType: 'image/svg+xml',
    sizeBytes: 44_220,
    scanStatus: 'allowed',
    uploadedByDisplay: 'Fixture User',
    createdAtLabel: '2026-07-01 11:05',
    modifiedAtLabel: '2026-07-01 11:05',
    kind: 'svg',
    downloadPolicy: 'denied',
    capabilities: [],
    canDelete: false,
    sharing: MOCK_FILE_SHARING,
    safeStatusLabel: 'Canonical file ID is required.',
  },
  {
    id: 'attachment-005',
    canonicalFileId: 'file-object-005',
    originalFileName: 'lesson-recording.mp4',
    contentType: 'video/mp4',
    sizeBytes: 76_984_320,
    scanStatus: 'allowed',
    uploadedByDisplay: 'Fixture User',
    createdAtLabel: '2026-07-01 11:28',
    modifiedAtLabel: '2026-07-01 11:28',
    kind: 'video',
    downloadPolicy: 'denied',
    capabilities: [],
    canDelete: false,
    sharing: MOCK_FILE_SHARING,
    safeStatusLabel: 'You do not have permission to download this file.',
  },
];

const basePage = (overrides: Partial<FilesPageViewModel> = {}): FilesPageViewModel => ({
  title: 'Files',
  subtitle: 'Mock file states for stories and unit tests.',
  upload: {
    state: 'idle',
    canUpload: true,
    message: 'Select a file to upload to the backend.',
  },
  uploadQueue: [],
  quota: {
    state: 'available',
    usedBytes: 46 * 1024 * 1024,
    limitBytes: FILE_UPLOAD_MAX_BYTES,
    message: 'Quota summary is not available in MVP0.',
  },
  recentFiles: DEFAULT_FILES,
  pickerFiles: DEFAULT_FILES,
  page: 1,
  pageSize: 50,
  totalCount: DEFAULT_FILES.length,
  hasMore: false,
  ...overrides,
});

export const FILES_PAGE_SCENARIOS = {
  default: basePage(),
  uploadPending: basePage({
    upload: {
      state: 'pending',
      canUpload: false,
      selectedFileName: 'new-attachment.pdf',
      message: 'Uploading file to backend.',
    },
  }),
  uploadProgress: basePage({
    upload: {
      state: 'progress',
      canUpload: false,
      selectedFileName: 'new-attachment.pdf',
      progressPercent: 40,
      message: 'Uploading file to backend.',
    },
  }),
  uploadFailed: basePage({
    upload: {
      state: 'failed',
      canUpload: true,
      selectedFileName: 'new-attachment.pdf',
      message: 'Upload failed after backend rejection.',
    },
  }),
  fileTooLarge: basePage({
    upload: {
      state: 'tooLarge',
      canUpload: true,
      selectedFileName: 'oversized-video.mp4',
      message: 'Files larger than 50 MB are rejected before upload.',
    },
  }),
  scanPending: basePage({
    recentFiles: [DEFAULT_FILES[1]],
    totalCount: 1,
  }),
  scanBlocked: basePage({
    recentFiles: [DEFAULT_FILES[2]],
    totalCount: 1,
  }),
  scanAllowed: basePage({
    recentFiles: [DEFAULT_FILES[0]],
    totalCount: 1,
  }),
  downloadDenied: basePage({
    recentFiles: [DEFAULT_FILES[4]],
    totalCount: 1,
  }),
  noCanonicalFileIdYet: basePage({
    pickerFiles: [DEFAULT_FILES[3], DEFAULT_FILES[0]],
    recentFiles: [DEFAULT_FILES[3]],
    totalCount: 1,
  }),
  quotaExceeded: basePage({
    upload: {
      state: 'quotaExceeded',
      message: 'Quota exceeded.',
    },
    quota: {
      state: 'exceeded',
      usedBytes: 54 * 1024 * 1024,
      limitBytes: FILE_UPLOAD_MAX_BYTES,
      message: 'Quota exceeded.',
    },
  }),
  quotaExceptionRequested: basePage({
    upload: {
      state: 'quotaExceptionRequested',
      message: 'Quota exception was requested.',
    },
    quota: {
      state: 'exceptionRequested',
      usedBytes: 54 * 1024 * 1024,
      limitBytes: FILE_UPLOAD_MAX_BYTES,
      message: 'Quota exception was requested.',
    },
  }),
  quotaExceptionApproved: basePage({
    upload: {
      state: 'quotaExceptionApproved',
      message: 'Quota exception was approved.',
    },
    quota: {
      state: 'exceptionApproved',
      usedBytes: 54 * 1024 * 1024,
      limitBytes: FILE_UPLOAD_MAX_BYTES,
      message: 'Quota exception was approved.',
    },
  }),
  quotaExceptionRejected: basePage({
    upload: {
      state: 'quotaExceptionRejected',
      message: 'Quota exception was rejected.',
    },
    quota: {
      state: 'exceptionRejected',
      usedBytes: 54 * 1024 * 1024,
      limitBytes: FILE_UPLOAD_MAX_BYTES,
      message: 'Quota exception was rejected.',
    },
  }),
  adminOverrideRequired: basePage({
    recentFiles: [DEFAULT_FILES[2]],
    totalCount: 1,
  }),
  previewDisabled: basePage({
    recentFiles: [DEFAULT_FILES[0], DEFAULT_FILES[1], DEFAULT_FILES[2], DEFAULT_FILES[3], DEFAULT_FILES[4]],
  }),
  mobile: basePage({
    recentFiles: [DEFAULT_FILES[0], DEFAULT_FILES[1], DEFAULT_FILES[4]],
    totalCount: 3,
  }),
} satisfies Record<string, FilesPageViewModel>;
