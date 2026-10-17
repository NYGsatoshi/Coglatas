import type { TranslationKey } from '../../core/i18n/i18n.service';

export type WorkspaceMembershipRole = 'Owner' | 'Admin' | 'Adviser' | 'Member' | 'ReadOnly';

export type WorkspaceDashboardAccessSource = 'WorkspaceMembership' | 'SystemAdmin';

export type WorkspaceRoleLabel =
  '管理者' | '先生' | 'メンバー' | '閲覧のみ' | 'システム管理者アクセス' | '役割情報なし';

export type WorkspaceActionCapability =
  | 'openWorkspace'
  | 'openMembers'
  | 'openProjects'
  | 'openProjectCreate'
  | 'createProject'
  | 'addFiles'
  | 'inspectSharing'
  | 'manageSharing';

export interface WorkspaceMemberPreview {
  readonly id: string;
  readonly displayName: string;
}

export type WorkspaceNeedsAttentionKind = 'ReviewRequired' | 'ResearchFailed';

export interface WorkspaceNeedsAttentionItemViewModel {
  readonly id: string;
  readonly kind: WorkspaceNeedsAttentionKind;
  readonly label: string;
  readonly targetRoute: string;
  readonly occurredAtLabel: string | null;
  readonly occurredAtUtc?: string | null;
}

export type WorkspacePageCapability = 'createWorkspace';

export type WorkspaceDashboardStatus =
  'ready' | 'loading' | 'error' | 'permissionDenied' | 'noWorkspaceAccess';

export interface WorkspaceSummaryAvailability {
  readonly unreadAnnouncements: boolean;
  readonly unreadConversations: boolean;
  readonly activeProjects: boolean;
  readonly runningProjects: boolean;
  readonly needsReviewProjects: boolean;
  readonly lastUpdated: boolean;
}

export interface WorkspaceCardViewModel {
  readonly id: string;
  readonly displayName: string;
  readonly currentUserRole: WorkspaceMembershipRole | null;
  readonly accessSource: WorkspaceDashboardAccessSource | null;
  readonly roleLabel: WorkspaceRoleLabel;
  readonly unreadAnnouncementCount: number | null;
  readonly unreadConversationCount: number | null;
  readonly activeProjectCount: number | null;
  readonly runningProjectCount: number | null;
  readonly needsReviewProjectCount: number | null;
  readonly needsAttentionCount?: number;
  readonly needsAttentionItems?: readonly WorkspaceNeedsAttentionItemViewModel[];
  readonly hasExternalShares?: boolean;
  readonly externalShareCount?: number | null;
  readonly memberPreview?: readonly WorkspaceMemberPreview[];
  readonly lastUpdatedLabel: string | null;
  readonly lastUpdatedUtc?: string | null;
  readonly availability: WorkspaceSummaryAvailability;
  readonly capabilities: readonly WorkspaceActionCapability[];
}

export interface WorkspaceDashboardViewModel {
  readonly status: WorkspaceDashboardStatus;
  readonly title: string;
  readonly subtitle: string;
  readonly workspaces: readonly WorkspaceCardViewModel[];
  readonly pageCapabilities: readonly WorkspacePageCapability[];
  readonly message?: string;
  readonly messageKey?: TranslationKey;
}

export interface WorkspaceCreateInput {
  readonly name: string;
  readonly description: string | null;
  readonly icon: string | null;
}

export type WorkspaceCreateField = 'name' | 'description' | 'icon' | 'form';

export interface WorkspaceCreateFieldError {
  readonly field: WorkspaceCreateField;
  readonly message: string;
  readonly messageKey?: TranslationKey;
}

export type WorkspaceCreateStatus =
  | 'idle'
  | 'submitting'
  | 'error'
  | 'committedPendingActivation'
  | 'succeeded';

export interface WorkspaceCreateViewModel {
  readonly status: WorkspaceCreateStatus;
  readonly fieldErrors: readonly WorkspaceCreateFieldError[];
  readonly message?: string;
  readonly messageKey?: TranslationKey;
  readonly requestId?: string;
  readonly createdWorkspaceId?: string;
}
