export type WorkspaceMemberRoleValue = 0 | 1 | 2 | 3 | 4;
export type MemberManagementStatus = 'idle' | 'loading' | 'ready' | 'denied' | 'error';

export interface WorkspaceMemberMutationOptions {
  readonly workspaceId: string;
  readonly canAddMember: boolean;
  readonly target: {
    readonly userId: string;
    readonly currentRole: WorkspaceMemberRoleValue;
    readonly membershipStatus: 0 | 1 | 2;
    readonly canChangeRole: boolean;
    readonly allowedRoles: readonly WorkspaceMemberRoleValue[];
    readonly canRemove: boolean;
  } | null;
}

export interface WorkspaceMemberCandidate {
  readonly userId: string;
  readonly displayName: string;
  readonly allowedRoles: readonly WorkspaceMemberRoleValue[];
}

export interface WorkspaceMemberEditor {
  readonly workspaceId: string;
  readonly mode: 'add' | 'change' | 'remove';
  readonly status: MemberManagementStatus | 'submitting' | 'uncertain';
  readonly identity: { readonly userId: string; readonly displayName: string } | null;
  readonly allowedRoles: readonly WorkspaceMemberRoleValue[];
  readonly role: WorkspaceMemberRoleValue | null;
  readonly identityConfirmed: boolean;
  readonly query: string;
  readonly searchStatus: MemberManagementStatus;
  readonly candidates: readonly WorkspaceMemberCandidate[];
  readonly hasMore: boolean;
  readonly nextAfterUserId: string | null;
}

export const WORKSPACE_MEMBER_ROLE_NAMES = ['Owner', 'Admin', 'Adviser', 'Member', 'ReadOnly'] as const;

export function isWorkspaceMemberRole(value: unknown): value is WorkspaceMemberRoleValue {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 4;
}

export function isMemberSearchQuery(value: string): boolean {
  const characters = Array.from(value.trim());
  return characters.length >= 2 && characters.length <= 100 && characters.every((character) => {
    const scalar = character.codePointAt(0) ?? 0;
    return scalar !== 0 && (scalar < 0xd800 || scalar > 0xdfff);
  });
}
