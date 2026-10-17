import {
  FileAccessState,
  FileKind,
  FileScanStatus,
  FileSharingViewModel,
  FileViewModel,
} from './files.types';

export interface PagedResponseDto<T> {
  readonly items?: readonly T[];
  readonly page?: unknown;
  readonly pageSize?: unknown;
  readonly totalCount?: unknown;
  readonly hasMore?: unknown;
}

export interface FileListItemDto {
  readonly id?: unknown;
  readonly fileObjectId?: unknown;
  readonly workspaceId?: unknown;
  readonly originalFileName?: unknown;
  readonly contentType?: unknown;
  readonly sizeBytes?: unknown;
  readonly status?: unknown;
  readonly scanStatus?: unknown;
  readonly uploadedByUserId?: unknown;
  readonly uploadedByDisplayName?: unknown;
  readonly createdAt?: unknown;
  readonly updatedAt?: unknown;
  readonly deletedAt?: unknown;
  readonly canDelete?: unknown;
  readonly accessState?: unknown;
  readonly externalRecipientCount?: unknown;
  readonly canManageSharing?: unknown;
  readonly sharingVersion?: unknown;
}

export interface FileSharingResponseDto extends FileListItemDto {
  readonly fileObjectId?: unknown;
  readonly sharingPolicy?: unknown;
  readonly canInspectSharing?: unknown;
  readonly recipients?: unknown;
  readonly availableRecipients?: unknown;
}

export interface FileShareRecipientViewModel {
  readonly grantId: string;
  readonly displayName: string;
  readonly accessKind: 'workspaceMember' | 'externalProjectMember';
}

export interface FileShareRecipientCandidateViewModel {
  readonly userId: string;
  readonly displayName: string;
  readonly accessKind: 'workspaceMember' | 'externalProjectMember';
}

export interface FileSharingDetailViewModel {
  readonly fileObjectId: string;
  readonly sharing: FileSharingViewModel;
  readonly shareWithWorkspace: boolean;
  readonly canInspectSharing: boolean;
  readonly recipients: readonly FileShareRecipientViewModel[];
  readonly availableRecipients: readonly FileShareRecipientCandidateViewModel[];
}

export interface AttachmentUploadResponseDto {
  readonly id?: unknown;
  readonly fileObjectId?: unknown;
  readonly originalFileName?: unknown;
}

export interface FileDownloadGrantDto {
  readonly fileDownloadGrantId?: unknown;
  readonly fileObjectId?: unknown;
  readonly expiresAt?: unknown;
  readonly token?: unknown;
}

export interface FileDisplayLocalizer {
  readonly untitledFile: string;
  readonly unknownUser: string;
  formatDate(value: string | undefined): string;
}

/**
 * API values stay language-neutral. UI callers can provide the app's active
 * locale formatter for values that are only display fallbacks.
 */
export function mapFileListItem(
  dto: FileListItemDto,
  displayLocalizer: FileDisplayLocalizer,
): FileViewModel {
  const canonicalFileId = stringValue(dto.fileObjectId);
  const originalFileName = stringValue(dto.originalFileName) ?? displayLocalizer.untitledFile;
  const contentType = stringValue(dto.contentType) ?? 'application/octet-stream';
  const scanStatus = toScanStatus(dto.scanStatus, dto.status);
  const active = isActiveStatus(dto.status) && !stringValue(dto.deletedAt);
  const canDownload = active && scanStatus === 'allowed' && !!canonicalFileId;
  const createdAt = stringValue(dto.createdAt);
  const modifiedAt = stringValue(dto.updatedAt) ?? createdAt;

  return {
    id: stringValue(dto.id) ?? canonicalFileId ?? '',
    canonicalFileId,
    originalFileName,
    originalFileNameIsFallback: !stringValue(dto.originalFileName),
    contentType,
    sizeBytes: numberValue(dto.sizeBytes),
    scanStatus,
    uploadedByDisplay: stringValue(dto.uploadedByDisplayName) ?? displayLocalizer.unknownUser,
    uploadedByDisplayIsFallback: !stringValue(dto.uploadedByDisplayName),
    createdAt,
    modifiedAt,
    createdAtLabel: displayLocalizer.formatDate(createdAt),
    modifiedAtLabel: displayLocalizer.formatDate(modifiedAt),
    kind: fileKind(originalFileName, contentType),
    downloadPolicy: canDownload ? 'available' : 'denied',
    capabilities: canDownload ? ['download'] : [],
    canDelete: active && !!canonicalFileId && dto.canDelete === true,
    sharing: mapFileSharingPresentation(dto),
    downloadState: 'idle',
  };
}

/**
 * Maps only an explicit API projection. Invalid or absent values deliberately
 * render as unavailable rather than a guessed Private/Workspace state.
 */
export function mapFileSharingPresentation(dto: Pick<
  FileListItemDto,
  'accessState' | 'externalRecipientCount' | 'canManageSharing' | 'sharingVersion'
>): FileSharingViewModel {
  const accessState = accessStateValue(dto.accessState);
  const sharingVersion = positiveInteger(dto.sharingVersion);
  const canManageSharing = accessState !== 'unavailable' &&
    sharingVersion !== undefined &&
    dto.canManageSharing === true;
  const externalRecipientCount = canManageSharing && accessState === 'external'
    ? nonNegativeInteger(dto.externalRecipientCount)
    : undefined;

  return {
    accessState,
    externalRecipientCount,
    canManageSharing,
    sharingVersion,
  };
}

export function mapFileSharingResponse(dto: unknown): FileSharingDetailViewModel | null {
  if (!isObject(dto)) {
    return null;
  }

  const source = dto as FileSharingResponseDto;
  const fileObjectId = stringValue(source.fileObjectId);
  const sharing = mapFileSharingPresentation(source);
  const sharingPolicy = accessStateValue(source.sharingPolicy);
  if (!fileObjectId || sharing.accessState === 'unavailable' || !sharing.sharingVersion ||
    (sharingPolicy !== 'private' && sharingPolicy !== 'workspace')) {
    return null;
  }

  const canInspectSharing = sharing.canManageSharing && source.canInspectSharing === true;
  if (!canInspectSharing) {
    return {
      fileObjectId,
      sharing,
      shareWithWorkspace: sharingPolicy === 'workspace',
      canInspectSharing: false,
      recipients: [],
      availableRecipients: [],
    };
  }

  const recipients = mapRecipients(source.recipients);
  const availableRecipients = mapRecipientCandidates(source.availableRecipients);
  if (!recipients || !availableRecipients) {
    return null;
  }

  return {
    fileObjectId,
    sharing,
    shareWithWorkspace: sharingPolicy === 'workspace',
    canInspectSharing: true,
    recipients,
    availableRecipients,
  };
}

export function safeFileNameFromHeader(headerValue: string | null, fallback: string): string {
  if (!headerValue) {
    return fallback;
  }

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(headerValue);
  if (utf8Match?.[1]) {
    return decodeURIComponent(utf8Match[1].replaceAll('"', '').trim());
  }

  const filenameMatch = /filename="?([^";]+)"?/i.exec(headerValue);
  return filenameMatch?.[1]?.trim() || fallback;
}

function toScanStatus(scanStatus: unknown, status: unknown): FileScanStatus {
  const normalizedScan = String(scanStatus ?? '').toLowerCase();
  if (normalizedScan === 'pending') {
    return 'pending';
  }

  if (['infected', 'failed', 'blocked', 'quarantined'].includes(normalizedScan)) {
    return 'blocked';
  }

  if (['clean', 'skipped', 'allowed'].includes(normalizedScan)) {
    return 'allowed';
  }

  const normalizedStatus = String(status ?? '').toLowerCase();
  if (normalizedStatus === 'quarantined') {
    return 'blocked';
  }

  return normalizedScan ? 'unavailable' : 'allowed';
}

function isActiveStatus(status: unknown): boolean {
  const normalized = String(status ?? 'active').toLowerCase();
  return normalized === 'active';
}

function fileKind(fileName: string, contentType: string): FileKind {
  const extension = extensionOf(fileName);
  if (extension === '.svg' || contentType === 'image/svg+xml') {
    return 'svg';
  }

  if (contentType.startsWith('image/')) {
    return 'image';
  }

  if (contentType === 'application/pdf') {
    return 'pdf';
  }

  if (extension === '.zip' || contentType === 'application/zip') {
    return 'zip';
  }

  if (contentType.startsWith('video/')) {
    return 'video';
  }

  return 'document';
}

function extensionOf(fileName: string): string {
  const dotIndex = fileName.lastIndexOf('.');
  return dotIndex >= 0 ? fileName.slice(dotIndex).toLowerCase() : '';
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function numberValue(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function accessStateValue(value: unknown): FileAccessState {
  switch (typeof value === 'string' ? value.toLowerCase() : '') {
    case 'private':
      return 'private';
    case 'workspace':
      return 'workspace';
    case 'external':
      return 'external';
    default:
      return 'unavailable';
  }
}

function positiveInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : undefined;
}

function nonNegativeInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : undefined;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function mapRecipients(value: unknown): readonly FileShareRecipientViewModel[] | null {
  if (!Array.isArray(value)) {
    return null;
  }

  const recipients: FileShareRecipientViewModel[] = [];
  for (const candidate of value) {
    if (!isObject(candidate)) {
      return null;
    }
    const grantId = stringValue(candidate['grantId']);
    const displayName = stringValue(candidate['displayName']);
    const accessKind = recipientKindValue(candidate['accessKind']);
    if (!grantId || !displayName || !accessKind) {
      return null;
    }
    recipients.push({ grantId, displayName, accessKind });
  }
  return recipients;
}

function mapRecipientCandidates(value: unknown): readonly FileShareRecipientCandidateViewModel[] | null {
  if (!Array.isArray(value)) {
    return null;
  }

  const candidates: FileShareRecipientCandidateViewModel[] = [];
  for (const candidate of value) {
    if (!isObject(candidate)) {
      return null;
    }
    const userId = stringValue(candidate['userId']);
    const displayName = stringValue(candidate['displayName']);
    const accessKind = recipientKindValue(candidate['accessKind']);
    if (!userId || !displayName || !accessKind) {
      return null;
    }
    candidates.push({ userId, displayName, accessKind });
  }
  return candidates;
}

function recipientKindValue(value: unknown): FileShareRecipientViewModel['accessKind'] | undefined {
  switch (typeof value === 'string' ? value.toLowerCase() : '') {
    case 'workspacemember':
      return 'workspaceMember';
    case 'externalprojectmember':
      return 'externalProjectMember';
    default:
      return undefined;
  }
}
