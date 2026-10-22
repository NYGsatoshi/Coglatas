// Keep each localized entry adjacent so display contracts cannot drift by language.
// The two public dictionaries retain the same consumer-facing API.
const copy = {
  title: { en: 'Teams', ja: 'チーム' },
  channels: { en: 'Workspace channels', ja: 'ワークスペースのチャンネル' },
  workspace: { en: 'Current Workspace', ja: '現在のワークスペース' },
  description: { en: 'Choose a named channel for a topic. Direct and group messages stay in DM.', ja: '話題ごとのチャンネルを選択します。個人・グループの会話はDMに分かれています。' },
  scope: { en: 'This directory belongs to the selected Workspace. Public channel names are visible to its members; message history requires participation.', ja: '選択中のワークスペース内の一覧です。公開チャンネル名はメンバーに表示されますが、メッセージ履歴の閲覧には参加が必要です。' },
  activity: { en: 'Joined channel activity and Project channels', ja: '参加済みの会話・プロジェクトのチャンネル' },
  dm: { en: 'Open DM', ja: 'DMを開く' },
  search: { en: 'Find a channel', ja: 'チャンネルを検索' },
  searchAction: { en: 'Search', ja: '検索' },
  refresh: { en: 'Refresh', ja: '更新' },
  loading: { en: 'Loading current channels…', ja: '現在のチャンネルを読み込んでいます…' },
  noWorkspace: { en: 'Select an active Workspace using the header to view its channels.', ja: 'ヘッダーから有効なワークスペースを選択してください。' },
  unavailable: { en: 'The channel directory is unavailable for the current account or Workspace.', ja: '現在のアカウントまたはワークスペースではチャンネル一覧を利用できません。' },
  failed: { en: 'Channels could not be loaded. No previous channel data is displayed.', ja: 'チャンネルを取得できませんでした。以前のチャンネル情報は表示していません。' },
  empty: { en: 'No channels match this page and search.', ja: 'このページと検索条件に一致するチャンネルはありません。' },
  create: { en: 'Create channel', ja: 'チャンネルを作成' },
  name: { en: 'Channel name', ja: 'チャンネル名' },
  nameHelp: { en: 'Use 1–200 characters. The default general channel is managed by the Workspace.', ja: '1〜200文字で入力してください。既定のgeneralはワークスペースで管理されます。' },
  visibility: { en: 'Who can find this channel?', ja: 'このチャンネルを見つけられる人' },
  public: { en: 'Public within this Workspace', ja: 'このワークスペース内に公開' },
  private: { en: 'Private — participants only', ja: '非公開 — 参加者のみ' },
  participantOnly: { en: 'Existing participants only', ja: '既存の参加者のみ' },
  privateHelp: { en: 'Private channels are not listed for nonparticipants. Creating a private channel does not grant other users access.', ja: '非公開チャンネルは未参加の人の一覧に表示されません。作成だけでは他の人にアクセス権は付与されません。' },
  publicHelp: { en: 'Workspace members can find and join this channel. Removed participants must be admitted again by a manager.', ja: 'ワークスペースのメンバーが検索して参加できます。除外された人の再参加には管理者による追加が必要です。' },
  cancel: { en: 'Cancel', ja: 'キャンセル' },
  close: { en: 'Close', ja: '閉じる' },
  creating: { en: 'Creating…', ja: '作成中…' },
  pending: { en: 'Waiting for the server…', ja: 'サーバーの応答を待っています…' },
  join: { en: 'Join channel', ja: 'チャンネルに参加' },
  open: { en: 'Open channel', ja: 'チャンネルを開く' },
  joined: { en: 'Joined', ja: '参加済み' },
  default: { en: 'Workspace default', ja: 'ワークスペースの既定' },
  noJoin: { en: 'Ask a channel manager for access.', ja: 'アクセスについてはチャンネル管理者に確認してください。' },
  createDenied: { en: 'Channel creation is not available with your current permissions.', ja: '現在の権限ではチャンネルを作成できません。' },
  uncertain: { en: 'The outcome could not be confirmed. Refresh the directory before retrying. An unchanged create retry keeps its request identity.', ja: '処理結果を確認できませんでした。再試行する前に一覧を更新してください。同じ内容で作成を再試行する場合は同じリクエスト識別子を使用します。' },
  denied: { en: 'The operation is unavailable. Current channel access has been cleared; refresh to recheck.', ja: 'この操作は利用できません。現在のチャンネル情報を消去しました。更新してアクセス権を再確認してください。' },
  committed: { en: 'The channel was confirmed by the server. Open it below; creating it again is unnecessary.', ja: 'サーバーでチャンネルを確認しました。下のリンクから開けます。再作成する必要はありません。' },
  invalid: { en: 'Check the channel name and visibility.', ja: 'チャンネル名と公開範囲を確認してください。' },
  previous: { en: 'Previous page', ja: '前のページ' },
  next: { en: 'Next page', ja: '次のページ' },
  page: { en: 'Page', ja: 'ページ' },
  of: { en: 'of', ja: '/' },
  total: { en: 'Channels', ja: 'チャンネル数' },
  createScope: { en: 'Create in this Workspace', ja: '作成先のワークスペース' },
  readonlyNotice: { en: 'Opening a channel rechecks permission before loading messages. This directory never previews message bodies.', ja: 'チャンネルを開く際に権限を再確認してからメッセージを読み込みます。この一覧では本文を表示しません。' },
} as const;

export type WorkspaceChannelsMessages = { readonly [Key in keyof typeof copy]: string };

function catalog(locale: 'en' | 'ja'): WorkspaceChannelsMessages {
  return Object.fromEntries(
    Object.entries(copy).map(([key, value]) => [key, value[locale]])
  ) as WorkspaceChannelsMessages;
}

export const WORKSPACE_CHANNELS_EN = catalog('en');
export const WORKSPACE_CHANNELS_JA = catalog('ja');
