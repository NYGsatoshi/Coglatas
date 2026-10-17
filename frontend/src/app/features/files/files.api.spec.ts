import {
  FileDisplayLocalizer,
  mapFileListItem,
  mapFileSharingPresentation,
  mapFileSharingResponse,
  safeFileNameFromHeader,
} from './files.api';

const DISPLAY_LOCALIZER: FileDisplayLocalizer = {
  untitledFile: '無題のファイル',
  unknownUser: '不明なユーザー',
  formatDate: (value) => value ? `表示日時: ${value}` : '',
};

describe('files api mapper', () => {
  it('distinguishes absent display fallbacks from identical authored names', () => {
    const missing = mapFileListItem({}, DISPLAY_LOCALIZER);
    expect(missing.originalFileNameIsFallback).toBe(true);
    expect(missing.uploadedByDisplayIsFallback).toBe(true);
    const authored = mapFileListItem({
      originalFileName: DISPLAY_LOCALIZER.untitledFile,
      uploadedByDisplayName: DISPLAY_LOCALIZER.unknownUser,
    }, DISPLAY_LOCALIZER);
    expect(authored.originalFileNameIsFallback).toBe(false);
    expect(authored.uploadedByDisplayIsFallback).toBe(false);
    expect(authored.originalFileName).toBe(DISPLAY_LOCALIZER.untitledFile);
    expect(authored.uploadedByDisplay).toBe(DISPLAY_LOCALIZER.unknownUser);
  });

  it('maps backend file list items to safe view models', () => {
    const vm = mapFileListItem({
      id: 'attachment-1',
      fileObjectId: 'file-object-1',
      workspaceId: 'workspace-1',
      originalFileName: 'report.pdf',
      contentType: 'application/pdf',
      sizeBytes: 2048,
      status: 'Active',
      scanStatus: 'Skipped',
      uploadedByUserId: 'user-1',
      uploadedByDisplayName: 'Fixture User',
      createdAt: '2026-07-08T00:00:00Z',
      updatedAt: '2026-07-09T12:30:00Z',
      canDelete: true,
    }, DISPLAY_LOCALIZER);

    expect(vm.id).toBe('attachment-1');
    expect(vm.canonicalFileId).toBe('file-object-1');
    expect(vm.scanStatus).toBe('allowed');
    expect(vm.kind).toBe('pdf');
    expect(vm.downloadPolicy).toBe('available');
    expect(vm.capabilities).toEqual(['download']);
    expect(vm.canDelete).toBe(true);
    expect(vm.uploadedByDisplay).toBe('Fixture User');
    expect(vm.modifiedAtLabel).toBe('表示日時: 2026-07-09T12:30:00Z');
    expect(vm.createdAtLabel).toBe('表示日時: 2026-07-08T00:00:00Z');
  });

  it('maps a missing or malformed delete capability fail-closed', () => {
    const missing = mapFileListItem({
      id: 'attachment-missing',
      fileObjectId: 'file-missing',
      originalFileName: 'missing.txt',
      status: 'Active',
      scanStatus: 'Clean',
    }, DISPLAY_LOCALIZER);
    const malformed = mapFileListItem({
      id: 'attachment-malformed',
      fileObjectId: 'file-malformed',
      originalFileName: 'malformed.txt',
      status: 'Active',
      scanStatus: 'Clean',
      canDelete: 'true',
    }, DISPLAY_LOCALIZER);

    expect(missing.canDelete).toBe(false);
    expect(malformed.canDelete).toBe(false);
  });

  it('falls back to createdAt when the backend has no updated timestamp', () => {
    const vm = mapFileListItem({
      id: 'attachment-legacy',
      originalFileName: 'legacy.txt',
      contentType: 'text/plain',
      status: 'Active',
      scanStatus: 'Skipped',
      createdAt: '2026-07-08T00:00:00Z',
    }, DISPLAY_LOCALIZER);

    expect(vm.modifiedAtLabel).toBe(vm.createdAtLabel);
  });

  it('does not allow download for quarantined or deleted files', () => {
    const quarantined = mapFileListItem({
      id: 'attachment-2',
      fileObjectId: 'file-object-2',
      originalFileName: 'archive.zip',
      contentType: 'application/zip',
      sizeBytes: 4096,
      status: 'Quarantined',
      scanStatus: 'Infected',
    }, DISPLAY_LOCALIZER);
    const deleted = mapFileListItem({
      id: 'attachment-3',
      fileObjectId: 'file-object-3',
      originalFileName: 'old.txt',
      contentType: 'text/plain',
      sizeBytes: 128,
      status: 'Deleted',
      scanStatus: 'Skipped',
      deletedAt: '2026-07-08T00:00:00Z',
    }, DISPLAY_LOCALIZER);

    expect(quarantined.scanStatus).toBe('blocked');
    expect(quarantined.capabilities).toEqual([]);
    expect(deleted.downloadPolicy).toBe('denied');
    expect(deleted.capabilities).toEqual([]);
  });

  it('does not duplicate backend upload type or size policy in the client mapper', () => {
    expect('isAllowedUploadFile' in { mapFileListItem }).toBe(false);
  });

  it('extracts backend download filenames from content disposition headers', () => {
    expect(safeFileNameFromHeader('attachment; filename="report.pdf"', 'fallback.txt')).toBe('report.pdf');
    expect(safeFileNameFromHeader("attachment; filename*=UTF-8''report%20copy.pdf", 'fallback.txt')).toBe(
      'report copy.pdf',
    );
    expect(safeFileNameFromHeader(null, 'fallback.txt')).toBe('fallback.txt');
  });

  it('renders only explicit server sharing states and redacts external counts without inspection authority', () => {
    expect(mapFileSharingPresentation({ accessState: 'Private', sharingVersion: 2, canManageSharing: true }))
      .toEqual({ accessState: 'private', canManageSharing: true, sharingVersion: 2, externalRecipientCount: undefined });
    expect(mapFileSharingPresentation({ accessState: 'Workspace', sharingVersion: 3, canManageSharing: true }))
      .toEqual({ accessState: 'workspace', canManageSharing: true, sharingVersion: 3, externalRecipientCount: undefined });
    expect(mapFileSharingPresentation({
      accessState: 'External', externalRecipientCount: 2, canManageSharing: true, sharingVersion: 4,
    })).toEqual({ accessState: 'external', externalRecipientCount: 2, canManageSharing: true, sharingVersion: 4 });
    expect(mapFileSharingPresentation({
      accessState: 'External', externalRecipientCount: 2, canManageSharing: false, sharingVersion: 4,
    })).toEqual({ accessState: 'external', externalRecipientCount: undefined, canManageSharing: false, sharingVersion: 4 });
    expect(mapFileSharingPresentation({ accessState: 'External', externalRecipientCount: '2', canManageSharing: true }))
      .toEqual({ accessState: 'external', externalRecipientCount: undefined, canManageSharing: false, sharingVersion: undefined });
  });

  it('does not expose recipient data when the server withholds sharing inspection authority', () => {
    const detail = mapFileSharingResponse({
      fileObjectId: 'file-object-1',
      sharingPolicy: 'Private',
      accessState: 'External',
      sharingVersion: 4,
      canManageSharing: false,
      canInspectSharing: false,
      externalRecipientCount: 9,
      recipients: [{ grantId: 'grant-1', displayName: 'Protected person', accessKind: 'ExternalProjectMember' }],
      availableRecipients: [{ userId: 'person-1', displayName: 'Protected person', accessKind: 'ExternalProjectMember' }],
    });

    expect(detail).toEqual({
      fileObjectId: 'file-object-1',
      sharing: { accessState: 'external', canManageSharing: false, sharingVersion: 4, externalRecipientCount: undefined },
      shareWithWorkspace: false,
      canInspectSharing: false,
      recipients: [],
      availableRecipients: [],
    });
  });
});
