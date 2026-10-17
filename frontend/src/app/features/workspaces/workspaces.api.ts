import {
  WorkspaceActionCapability,
  WorkspaceCardViewModel,
  WorkspaceDashboardAccessSource,
  WorkspaceMembershipRole,
  WorkspaceNeedsAttentionItemViewModel,
  WorkspaceNeedsAttentionKind,
  WorkspacePageCapability,
  WorkspaceRoleLabel,
  WorkspaceCreateInput,
} from './workspaces.types';

export interface WorkspaceDashboardListItemDto {
  readonly id?: unknown;
  readonly name?: unknown;
  readonly description?: unknown;
  readonly icon?: unknown;
  readonly status?: unknown;
  readonly createdAt?: unknown;
  readonly updatedAt?: unknown;
  readonly currentUserRole?: unknown;
  readonly accessSource?: unknown;
  readonly canOpenWorkspace?: unknown;
  readonly canOpenMembers?: unknown;
  readonly canOpenProjects?: unknown;
  readonly canOpenProjectCreate?: unknown;
  readonly canCreateProject?: unknown;
  readonly canAddFiles?: unknown;
  readonly unreadAnnouncementCount?: unknown;
  readonly unreadConversationCount?: unknown;
  readonly inProgressProjectCount?: unknown;
  readonly runningProjectCount?: unknown;
  readonly needsReviewProjectCount?: unknown;
  readonly needsAttentionCount?: unknown;
  readonly needsAttentionItems?: unknown;
  readonly hasExternalShares?: unknown;
  readonly externalShareCount?: unknown;
  readonly canInspectSharing?: unknown;
  readonly canManageSharing?: unknown;
  readonly memberPreview?: unknown;
}

export interface WorkspaceCapabilitiesDto {
  readonly canCreate?: unknown;
}

export interface WorkspaceCapabilitiesEnvelopeDto {
  readonly requestId?: unknown;
  readonly data?: WorkspaceCapabilitiesDto | null;
  readonly warnings?: readonly unknown[];
}

export interface WorkspaceCreateRequestDto {
  readonly name: string;
  readonly description: string | null;
  readonly icon: string | null;
}

export interface WorkspaceCreatedDto {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly icon: string | null;
  readonly status: 0;
  readonly createdByUserId: string;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

export interface WorkspaceCreateSuccess {
  readonly requestId: string;
  readonly data: WorkspaceCreatedDto;
  readonly warnings: readonly unknown[];
}

const CANONICAL_TASK_ROUTE =
  /^\/projects\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\/tasks\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;

export function canonicalizeWorkspaceCreateInput(
  input: WorkspaceCreateInput,
): WorkspaceCreateRequestDto {
  return {
    name: input.name.trim(),
    description: optionalString(input.description),
    icon: optionalString(input.icon),
  };
}

/**
 * Maps only the canonical HTTP 201 Workspace-create body. A successful HTTP
 * status with an incomplete or incompatible body is uncertain delivery, not a
 * resource the client may fabricate or activate.
 */
export function mapWorkspaceCreateSuccess(value: unknown): WorkspaceCreateSuccess {
  const envelope = recordValue(value, 'Workspace create response');
  const requestId = requiredString(envelope['requestId'], 'requestId');
  if (!Array.isArray(envelope['warnings'])) {
    throw new Error('Workspace create response warnings must be an array.');
  }

  const data = recordValue(envelope['data'], 'Workspace create response data');
  const id = requiredUuid(data['id'], 'data.id');
  const name = requiredString(data['name'], 'data.name');
  const description = nullableString(data['description'], 'data.description');
  const icon = nullableString(data['icon'], 'data.icon');
  if (data['status'] !== 0) {
    throw new Error('Workspace create response data.status must be Active.');
  }
  const createdByUserId = requiredUuid(data['createdByUserId'], 'data.createdByUserId');
  const createdAt = requiredTimestamp(data['createdAt'], 'data.createdAt');
  const updatedAt = nullableTimestamp(data['updatedAt'], 'data.updatedAt');

  return {
    requestId,
    data: {
      id,
      name,
      description,
      icon,
      status: 0,
      createdByUserId,
      createdAt,
      updatedAt,
    },
    warnings: envelope['warnings'],
  };
}

export function mapWorkspaceDashboardResponse(value: unknown): readonly WorkspaceCardViewModel[] {
  if (!Array.isArray(value)) {
    throw new Error('Workspace dashboard response must be an array.');
  }

  return value.map((item, index) => {
    if (!item || typeof item !== 'object' || Array.isArray(item)) {
      throw new Error(`Workspace dashboard item ${index} is invalid.`);
    }

    return mapWorkspaceDashboardItem(item as WorkspaceDashboardListItemDto, index);
  });
}

export function mapWorkspacePageCapabilities(
  response: unknown,
): readonly WorkspacePageCapability[] {
  if (!response || typeof response !== 'object' || Array.isArray(response)) {
    return [];
  }

  const envelope = response as WorkspaceCapabilitiesEnvelopeDto;
  if (
    typeof envelope.requestId !== 'string' ||
    envelope.requestId.trim().length === 0 ||
    !envelope.data ||
    typeof envelope.data !== 'object' ||
    Array.isArray(envelope.data) ||
    typeof envelope.data.canCreate !== 'boolean' ||
    !Array.isArray(envelope.warnings)
  ) {
    return [];
  }

  return envelope.data?.canCreate === true ? ['createWorkspace'] : [];
}

export function mapWorkspaceDashboardItem(
  workspace: WorkspaceDashboardListItemDto,
  index = 0,
): WorkspaceCardViewModel {
  const id = stringValue(workspace.id);
  if (!id) {
    throw new Error(`Workspace dashboard item ${index} is missing an id.`);
  }

  const currentUserRole = membershipRole(workspace.currentUserRole);
  const accessSource = dashboardAccessSource(workspace.accessSource);
  const unreadAnnouncementCount = nonNegativeInteger(workspace.unreadAnnouncementCount);
  const unreadConversationCount = nonNegativeInteger(workspace.unreadConversationCount);
  const runningProjectCount = nonNegativeInteger(workspace.runningProjectCount);
  const needsReviewProjectCount = nonNegativeInteger(workspace.needsReviewProjectCount);
  const mappedNeedsAttentionItems = needsAttentionItems(workspace.needsAttentionItems);
  const needsAttentionCount =
    nonNegativeInteger(workspace.needsAttentionCount) ?? mappedNeedsAttentionItems.length;
  const canInspectSharing = workspace.canInspectSharing === true;
  const activeProjectCount =
    nonNegativeInteger(workspace.inProgressProjectCount) ??
    (runningProjectCount !== null && needsReviewProjectCount !== null
      ? runningProjectCount + needsReviewProjectCount
      : null);
  const lastUpdatedLabel = dateLabel(workspace.updatedAt) ?? dateLabel(workspace.createdAt);

  return {
    id,
    displayName: stringValue(workspace.name) ?? 'Workspace',
    currentUserRole,
    accessSource,
    roleLabel: roleLabel(currentUserRole, accessSource),
    unreadAnnouncementCount,
    unreadConversationCount,
    activeProjectCount,
    runningProjectCount,
    needsReviewProjectCount,
    needsAttentionCount,
    needsAttentionItems: mappedNeedsAttentionItems,
    hasExternalShares: workspace.hasExternalShares === true,
    externalShareCount: canInspectSharing
      ? nonNegativeInteger(workspace.externalShareCount)
      : null,
    memberPreview: memberPreview(workspace.memberPreview),
    lastUpdatedLabel,
    lastUpdatedUtc: timestampValue(workspace.updatedAt) ?? timestampValue(workspace.createdAt),
    availability: {
      unreadAnnouncements: unreadAnnouncementCount !== null,
      unreadConversations: unreadConversationCount !== null,
      activeProjects: activeProjectCount !== null,
      runningProjects: runningProjectCount !== null,
      needsReviewProjects: needsReviewProjectCount !== null,
      lastUpdated: lastUpdatedLabel !== null,
    },
    capabilities: actionCapabilities(workspace),
  };
}

function actionCapabilities(
  workspace: WorkspaceDashboardListItemDto,
): readonly WorkspaceActionCapability[] {
  const capabilities: WorkspaceActionCapability[] = [];
  if (workspace.canOpenWorkspace === true) {
    capabilities.push('openWorkspace');
  }
  if (workspace.canOpenMembers === true) {
    capabilities.push('openMembers');
  }
  if (workspace.canOpenProjects === true) {
    capabilities.push('openProjects');
  }
  if (workspace.canOpenProjectCreate === true) {
    capabilities.push('openProjectCreate');
  }
  if (workspace.canCreateProject === true) {
    capabilities.push('createProject');
  }
  if (workspace.canAddFiles === true) {
    capabilities.push('addFiles');
  }
  if (workspace.canInspectSharing === true) {
    capabilities.push('inspectSharing');
  }
  if (workspace.canManageSharing === true) {
    capabilities.push('manageSharing');
  }
  return capabilities;
}

function memberPreview(value: unknown): readonly { readonly id: string; readonly displayName: string }[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.slice(0, 3).flatMap((item) => {
    if (!item || typeof item !== 'object' || Array.isArray(item)) {
      return [];
    }
    const record = item as Record<string, unknown>;
    const id = stringValue(record['userId']);
    const displayName = stringValue(record['displayName']);
    return id && displayName ? [{ id, displayName }] : [];
  });
}

function needsAttentionItems(value: unknown): readonly WorkspaceNeedsAttentionItemViewModel[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.flatMap((item) => {
    if (!item || typeof item !== 'object' || Array.isArray(item)) {
      return [];
    }

    const record = item as Record<string, unknown>;
    const id = stringValue(record['id']);
    const kind = needsAttentionKind(record['kind']);
    const targetRoute = stringValue(record['targetRoute']);
    if (!id || !kind || !targetRoute || !CANONICAL_TASK_ROUTE.test(targetRoute)) {
      return [];
    }

    return [{
      id,
      kind,
      label: needsAttentionLabel(kind),
      targetRoute,
      occurredAtLabel: dateTimeLabel(record['occurredAt']),
      occurredAtUtc: timestampValue(record['occurredAt']),
    }];
  });
}

function needsAttentionKind(value: unknown): WorkspaceNeedsAttentionKind | null {
  return value === 'ReviewRequired' || value === 'ResearchFailed' ? value : null;
}

function needsAttentionLabel(kind: WorkspaceNeedsAttentionKind): string {
  switch (kind) {
    case 'ReviewRequired':
      return '確認が必要なTaskがあります';
    case 'ResearchFailed':
      return 'Researchの実行に失敗しました';
  }
}

function membershipRole(value: unknown): WorkspaceMembershipRole | null {
  return value === 'Owner' ||
    value === 'Admin' ||
    value === 'Adviser' ||
    value === 'Member' ||
    value === 'ReadOnly'
    ? value
    : null;
}

function dashboardAccessSource(value: unknown): WorkspaceDashboardAccessSource | null {
  return value === 'WorkspaceMembership' || value === 'SystemAdmin' ? value : null;
}

function roleLabel(
  role: WorkspaceMembershipRole | null,
  accessSource: WorkspaceDashboardAccessSource | null,
): WorkspaceRoleLabel {
  if (accessSource === 'SystemAdmin' && role === null) {
    return 'システム管理者アクセス';
  }

  switch (role) {
    case 'Owner':
    case 'Admin':
      return '管理者';
    case 'Adviser':
      return '先生';
    case 'Member':
      return 'メンバー';
    case 'ReadOnly':
      return '閲覧のみ';
    default:
      return '役割情報なし';
  }
}

function nonNegativeInteger(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : null;
}

function dateLabel(value: unknown): string | null {
  const raw = stringValue(value);
  if (!raw) {
    return null;
  }

  const date = new Date(raw);
  return Number.isNaN(date.getTime()) ? null : date.toLocaleDateString('ja-JP');
}

function dateTimeLabel(value: unknown): string | null {
  const raw = stringValue(value);
  if (!raw) {
    return null;
  }

  const date = new Date(raw);
  return Number.isNaN(date.getTime())
    ? null
    : date.toLocaleString('ja-JP', { dateStyle: 'short', timeStyle: 'short' });
}

function stringValue(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function optionalString(value: string | null): string | null {
  const normalized = value?.trim() ?? '';
  return normalized.length > 0 ? normalized : null;
}

function recordValue(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} must be an object.`);
  }

  return value as Record<string, unknown>;
}

function requiredString(value: unknown, path: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`Workspace create response ${path} must be a non-empty string.`);
  }

  return value;
}

function nullableString(value: unknown, path: string): string | null {
  if (value === null) {
    return null;
  }
  if (typeof value === 'string') {
    return value;
  }

  throw new Error(`Workspace create response ${path} must be a string or null.`);
}

function requiredUuid(value: unknown, path: string): string {
  const uuid = requiredString(value, path);
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(uuid) ||
    uuid === '00000000-0000-0000-0000-000000000000'
  ) {
    throw new Error(`Workspace create response ${path} must be a UUID.`);
  }

  return uuid;
}

function requiredTimestamp(value: unknown, path: string): string {
  const timestamp = requiredString(value, path);
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(timestamp) || Number.isNaN(Date.parse(timestamp))) {
    throw new Error(`Workspace create response ${path} must be an ISO 8601 timestamp.`);
  }

  return timestamp;
}

function nullableTimestamp(value: unknown, path: string): string | null {
  return value === null ? null : requiredTimestamp(value, path);
}


function timestampValue(value: unknown): string | null {
  return typeof value === 'string' && Number.isFinite(Date.parse(value)) ? value : null;
}
