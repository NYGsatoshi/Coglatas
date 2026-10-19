import { WorkspaceMemberMockRecord, WorkspaceMembersScenario } from './members.types';

export const WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID = 'workspace-alpha';
export const WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID = 'workspace-hidden-other';

const activeCapabilities = ['openMemberDetail', 'changeRole', 'disableMember'] as const;

export const DEFAULT_WORKSPACE_MEMBER_RECORDS: readonly WorkspaceMemberMockRecord[] = [
  {
    id: 'member-sample-001',
    workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
    displayName: 'サンプル参加者 01',
    role: 'Owner',
    roleLabel: '管理者',
    groupProjectLabel: '教材準備 / A班',
    accountStatus: 'active',
    accountStatusLabel: '参加中',
    joinedAtLabel: '2026-04-10',
    capabilities: activeCapabilities,
    mockDetailSupported: true
  },
  {
    id: 'member-sample-002',
    workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
    displayName: 'サンプル参加者 02',
    role: 'Adviser',
    roleLabel: '先生',
    groupProjectLabel: '探究活動 / B班',
    accountStatus: 'active',
    accountStatusLabel: '参加中',
    joinedAtLabel: '2026-04-12',
    capabilities: ['openMemberDetail', 'changeRole'],
    mockDetailSupported: true
  },
  {
    id: 'member-sample-003',
    workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
    displayName: 'サンプル参加者 03',
    role: 'Member',
    roleLabel: 'メンバー',
    groupProjectLabel: '発表準備 / C班',
    accountStatus: 'disabled',
    accountStatusLabel: '利用停止',
    joinedAtLabel: '2026-04-18',
    capabilities: ['openMemberDetail', 'disableMember'],
    mockDetailSupported: true
  },
  {
    id: 'member-sample-004',
    workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
    displayName: 'サンプル参加者 04',
    role: 'ReadOnly',
    roleLabel: '閲覧のみ',
    groupProjectLabel: '共有資料 / 確認',
    accountStatus: 'removed',
    accountStatusLabel: '削除済み',
    joinedAtLabel: '2026-03-25',
    capabilities: ['openMemberDetail'],
    mockDetailSupported: false
  },
  {
    id: 'member-hidden-other-001',
    workspaceId: WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID,
    displayName: '別ワークスペース参加者',
    role: 'Member',
    roleLabel: 'メンバー',
    groupProjectLabel: '非表示領域',
    accountStatus: 'active',
    accountStatusLabel: '参加中',
    joinedAtLabel: '2026-04-01',
    capabilities: activeCapabilities,
    mockDetailSupported: true
  }
];

export const LONG_NAME_WORKSPACE_MEMBER: WorkspaceMemberMockRecord = {
  id: 'member-sample-long-name',
  workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
  displayName:
    'とても長い表示名のサンプル参加者-校内確認用-年度横断プロジェクト-共有ワークスペース-権限表示確認',
  role: 'Member',
  roleLabel: 'メンバー',
  groupProjectLabel: '年度横断の長いグループ名 / 発表準備と資料確認の長いプロジェクト名',
  accountStatus: 'active',
  accountStatusLabel: '参加中',
  joinedAtLabel: '2026-05-02',
  capabilities: activeCapabilities,
  mockDetailSupported: true
};

export const MANY_WORKSPACE_MEMBERS: readonly WorkspaceMemberMockRecord[] = Array.from({ length: 128 }, (_, index) => {
  const memberNumber = String(index + 1).padStart(3, '0');
  return {
    id: `member-many-${memberNumber}`,
    workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
    displayName: `サンプル参加者 ${memberNumber}`,
    role: index % 5 === 0 ? 'Adviser' : index % 3 === 0 ? 'ReadOnly' : 'Member',
    roleLabel: index % 5 === 0 ? '先生' : index % 3 === 0 ? '閲覧のみ' : 'メンバー',
    groupProjectLabel: index % 2 === 0 ? '教材準備 / A班' : '探究活動 / B班',
    accountStatus: index % 17 === 0 ? 'disabled' : 'active',
    accountStatusLabel: index % 17 === 0 ? '利用停止' : '参加中',
    joinedAtLabel: `2026-05-${String((index % 28) + 1).padStart(2, '0')}`,
    capabilities: index % 4 === 0 ? ['openMemberDetail'] : activeCapabilities,
    mockDetailSupported: true
  };
});

export const WORKSPACE_MEMBERS_SCENARIOS = {
  default: {
    status: 'ready',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: DEFAULT_WORKSPACE_MEMBER_RECORDS
  },
  loading: {
    status: 'loading',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: []
  },
  empty: {
    status: 'empty',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: []
  },
  error: {
    status: 'error',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: [],
    message: 'メンバー一覧を読み込めませんでした。'
  },
  permissionDenied: {
    status: 'permissionDenied',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: [],
    message: 'このワークスペースのメンバーを表示する権限がありません。'
  },
  manyRowsBoundedPage: {
    status: 'ready',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: MANY_WORKSPACE_MEMBERS
  },
  longNames: {
    status: 'ready',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: [LONG_NAME_WORKSPACE_MEMBER, ...DEFAULT_WORKSPACE_MEMBER_RECORDS]
  },
  removedMember: {
    status: 'ready',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: [DEFAULT_WORKSPACE_MEMBER_RECORDS[3]]
  },
  noRoleChangeCapability: {
    status: 'ready',
    title: 'メンバー',
    subtitle: 'ワークスペース参加者',
    members: DEFAULT_WORKSPACE_MEMBER_RECORDS.map((member) => ({
      ...member,
      capabilities: member.capabilities.filter((capability) => capability !== 'changeRole')
    }))
  }
} satisfies Record<string, WorkspaceMembersScenario>;
