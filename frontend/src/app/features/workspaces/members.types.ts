import { AppDataGridColumnDef } from '../../shared/grid/app-data-grid/app-data-grid.types';

export const WORKSPACE_MEMBERS_DEFAULT_PAGE_SIZE = 50;
export const WORKSPACE_MEMBERS_MAXIMUM_PAGE_SIZE = 100;

export type WorkspaceMemberRole = 'owner' | 'teacher' | 'member' | 'viewer';
export type WorkspaceMemberAccountStatus = 'active' | 'disabled' | 'removed';
export type WorkspaceMemberAccountStatusLabel = '参加中' | '利用停止' | '削除済み';
export type WorkspaceMemberPageStatus = 'ready' | 'loading' | 'empty' | 'error' | 'permissionDenied';
export type WorkspaceMemberCapability = 'openMemberDetail' | 'changeRole' | 'disableMember';
export type WorkspaceMemberActionId = 'openMemberDetail' | 'changeRole' | 'disableMember';

export interface WorkspaceMembersQueryOwnership {
  readonly loadedRowsOwner: 'backendAuthorization';
  readonly clientSearchOwner: 'alreadyLoadedAuthorizedRowsOnly';
  readonly futureSortOwner: 'backendWhenLive';
  readonly futureFilterOwner: 'backendWhenLive';
}

export interface WorkspaceMemberMockRecord {
  readonly id: string;
  readonly workspaceId: string;
  readonly displayName: string;
  readonly role: WorkspaceMemberRole;
  readonly roleLabel: string;
  readonly groupProjectLabel: string;
  readonly accountStatus: WorkspaceMemberAccountStatus;
  readonly accountStatusLabel: WorkspaceMemberAccountStatusLabel;
  readonly joinedAtLabel: string;
  readonly joinedAtUtc?: string | null;
  readonly capabilities: readonly WorkspaceMemberCapability[];
  readonly mockDetailSupported: boolean;
}

export interface WorkspaceMemberRowAction {
  readonly id: WorkspaceMemberActionId;
  readonly label: string;
  readonly destructive: boolean;
  readonly disabled: boolean;
  readonly disabledReason?: string;
}

export interface WorkspaceMemberGridRow {
  readonly id: string;
  readonly workspaceId: string;
  readonly displayName: string;
  readonly role: WorkspaceMemberRole;
  readonly roleLabel: string;
  readonly groupProjectLabel: string;
  readonly accountStatus: WorkspaceMemberAccountStatus;
  readonly accountStatusLabel: WorkspaceMemberAccountStatusLabel;
  readonly joinedAtLabel: string;
  readonly joinedAtUtc?: string | null;
  readonly rowActions: readonly WorkspaceMemberRowAction[];
}

export interface WorkspaceMembersPageSizePolicy {
  readonly defaultPageSize: typeof WORKSPACE_MEMBERS_DEFAULT_PAGE_SIZE;
  readonly maximumPageSize: typeof WORKSPACE_MEMBERS_MAXIMUM_PAGE_SIZE;
}

export interface WorkspaceMembersViewModel {
  readonly status: WorkspaceMemberPageStatus;
  readonly workspaceId: string;
  readonly title: string;
  readonly subtitle: string;
  readonly rows: readonly WorkspaceMemberGridRow[];
  readonly columns: readonly AppDataGridColumnDef<WorkspaceMemberGridRow>[];
  readonly pageSize: WorkspaceMembersPageSizePolicy;
  readonly queryOwnership: WorkspaceMembersQueryOwnership;
  readonly message?: string;
}

export interface WorkspaceMembersScenario {
  readonly status: WorkspaceMemberPageStatus;
  readonly title: string;
  readonly subtitle: string;
  readonly members: readonly WorkspaceMemberMockRecord[];
  readonly message?: string;
}
