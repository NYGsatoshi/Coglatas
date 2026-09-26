import { AnnouncementsPageViewModel, AnnouncementViewModel } from './announcements.types';

export const LONG_ANNOUNCEMENT_BODY =
  'これは長い本文の表示確認用です。架空の学習予定、持ち物、提出期限をまとめています。'.repeat(12);

const WORKSPACE_ID = '11111111-1111-1111-1111-111111111111';
const GROUP_ID = '22222222-2222-2222-2222-222222222222';
const CHANNEL_ID = '33333333-3333-3333-3333-333333333333';

export const DEFAULT_ANNOUNCEMENTS: readonly AnnouncementViewModel[] = [
  {
    id: 'mock-announcement-001',
    title: '来週の学習予定について',
    body: '来週は探究活動のまとめを行います。各自、配付済みの記録用紙を確認してください。',
    detailState: 'loaded',
    priority: 'important',
    audienceScope: 'workspace',
    publishedAtLabel: '2026年7月1日 09:00',
    expiresAt: '2026-07-15T09:00:00Z',
    expiresAtLabel: '2026年7月15日 09:00',
    publicationState: 'published',
    readState: {
      requiresReadConfirmation: true,
      isRead: false,
      isMarkingRead: false
    },
    capabilities: ['readAnnouncement', 'editAnnouncement'],
    notificationTarget: 'announcementDetail'
  },
  {
    id: 'mock-announcement-002',
    title: '保護者向け面談資料の確認',
    body: '面談前に共有資料の内容を確認してください。個人情報は含まない架空のサンプル資料です。',
    detailState: 'loaded',
    priority: 'normal',
    audienceScope: 'group',
    publishedAtLabel: '2026年6月30日 16:30',
    publicationState: 'updated',
    readState: {
      requiresReadConfirmation: false,
      isRead: true,
      isMarkingRead: false
    },
    capabilities: ['readAnnouncement'],
    notificationTarget: 'announcementDetail'
  },
  {
    id: 'mock-announcement-003',
    title: '下書き: 校内掲示の更新案',
    body: 'この下書きは編集状態の確認用です。公開前の安全な架空データのみを使用しています。',
    detailState: 'loaded',
    priority: 'critical',
    audienceScope: 'channel',
    publishedAtLabel: '下書き',
    publicationState: 'draft',
    readState: {
      requiresReadConfirmation: true,
      isRead: false,
      isMarkingRead: false
    },
    capabilities: ['readAnnouncement', 'editAnnouncement'],
    notificationTarget: 'announcementDetail'
  },
  {
    id: 'mock-announcement-004',
    title: '予約済み: 来月の行事予定',
    body: '予約公開状態の表示確認用です。',
    detailState: 'loaded',
    priority: 'normal',
    audienceScope: 'workspace',
    publishedAtLabel: '未公開',
    publicationState: 'scheduled',
    scheduledAtLabel: '2026年9月1日 08:00',
    timeZoneLabel: 'Asia/Tokyo',
    readState: {
      requiresReadConfirmation: false,
      isRead: false,
      isMarkingRead: false
    },
    capabilities: ['readAnnouncement', 'editAnnouncement'],
    notificationTarget: 'announcementDetail'
  },
  {
    id: 'mock-announcement-005',
    title: 'アーカイブ済み: 過去のお知らせ',
    body: 'アーカイブ状態の表示確認用です。',
    detailState: 'loaded',
    priority: 'normal',
    audienceScope: 'global',
    publishedAtLabel: '2026年5月1日 09:00',
    publicationState: 'archived',
    readState: {
      requiresReadConfirmation: false,
      isRead: true,
      isMarkingRead: false
    },
    capabilities: ['readAnnouncement', 'editAnnouncement'],
    notificationTarget: 'announcementDetail'
  }
];

export const HIDDEN_ANNOUNCEMENT_TITLE = '非表示のお知らせタイトル';
export const HIDDEN_ANNOUNCEMENT_BODY = '非表示のお知らせ本文';

export const DEFAULT_ANNOUNCEMENTS_PAGE: AnnouncementsPageViewModel = {
  status: 'ready',
  title: 'お知らせ',
  announcements: DEFAULT_ANNOUNCEMENTS,
  selectedAnnouncementId: DEFAULT_ANNOUNCEMENTS[0].id,
  pageCapabilities: ['readAnnouncement', 'createAnnouncement', 'editAnnouncement'],
  editorDraft: {
    title: '',
    body: '',
    priority: 'normal',
    audienceKey: `workspace:${WORKSPACE_ID}`,
    availableAudiences: [
      { key: 'global', scope: 'global', displayName: 'テナント全体', recipientCount: 1310 },
      {
        key: `workspace:${WORKSPACE_ID}`,
        scope: 'workspace',
        displayName: '西大和学園',
        recipientCount: 1248,
        workspaceId: WORKSPACE_ID
      },
      {
        key: `group:${GROUP_ID}`,
        scope: 'group',
        displayName: '西大和学園 / 教職員',
        recipientCount: 86,
        workspaceId: WORKSPACE_ID,
        groupId: GROUP_ID
      },
      {
        key: `channel:${CHANNEL_ID}`,
        scope: 'channel',
        displayName: '西大和学園 / Coglatas / #announcements',
        recipientCount: 32,
        workspaceId: WORKSPACE_ID,
        groupId: GROUP_ID,
        channelId: CHANNEL_ID
      }
    ],
    requiresReadConfirmation: false,
    publicationState: 'draft'
  }
};

export const ANNOUNCEMENT_PAGE_SCENARIOS = {
  default: DEFAULT_ANNOUNCEMENTS_PAGE,
  loading: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    status: 'loading',
    announcements: [],
    selectedAnnouncementId: null
  },
  empty: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    status: 'empty',
    announcements: [],
    selectedAnnouncementId: null
  },
  error: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    status: 'error',
    announcements: [],
    selectedAnnouncementId: null,
    message: 'お知らせを読み込めませんでした。'
  },
  permissionDenied: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    status: 'permissionDenied',
    announcements: [],
    selectedAnnouncementId: 'hidden-announcement',
    pageCapabilities: [],
    message: 'このお知らせを表示する権限がありません。'
  },
  noCreatePermission: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    pageCapabilities: ['readAnnouncement']
  },
  longBody: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    announcements: [
      {
        ...DEFAULT_ANNOUNCEMENTS[0],
        title: 'とても長いタイトルの表示確認用お知らせとても長いタイトルの表示確認用お知らせ',
        body: LONG_ANNOUNCEMENT_BODY
      }
    ],
    selectedAnnouncementId: DEFAULT_ANNOUNCEMENTS[0].id
  },
  audienceScopePreview: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    selectedAnnouncementId: DEFAULT_ANNOUNCEMENTS[1].id
  },
  attachmentDisabled: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    selectedAnnouncementId: DEFAULT_ANNOUNCEMENTS[1].id
  },
  recordAccessDenied: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    status: 'recordAccessDenied',
    announcements: [],
    selectedAnnouncementId: 'hidden-announcement',
    message: 'このお知らせを表示する権限がありません。'
  },
  unsafeBody: {
    ...DEFAULT_ANNOUNCEMENTS_PAGE,
    announcements: [
      {
        ...DEFAULT_ANNOUNCEMENTS[0],
        body: '<img src=x onerror=alert(1)>本文は文字として表示します。'
      }
    ],
    selectedAnnouncementId: DEFAULT_ANNOUNCEMENTS[0].id
  }
} satisfies Record<string, AnnouncementsPageViewModel>;
