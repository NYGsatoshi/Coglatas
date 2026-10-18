/**
 * Small, self-contained File-browser availability copy.
 *
 * This is a typed extension to the Files locale catalog, not a new global
 * locale source. Keep the two-language contract checked at compile time.
 */
export const FILE_BROWSER_AVAILABILITY_ENGLISH = {
  'files.stars.title': 'Starred files · this browser',
  'files.stars.localOnly': 'Saved only in this browser for this account and Workspace. Not synced to the server or other devices; clearing browser data removes stars.',
  'files.stars.detailsOnly': 'Each file is checked again before its name is shown. Open Recent files for authorized content and file actions.',
  'files.stars.loading': 'Checking access to starred files…',
  'files.stars.empty': 'No currently accessible local stars. Open a file in Recent and choose Star in this browser.',
  'files.stars.error': 'Some files could not be checked. Unverified names are hidden. Retry to check access again.',
  'files.stars.storageUnavailable': 'Browser stars could not be saved or read. No synchronization or persistence is confirmed.',
  'files.stars.contextUnavailable': 'Select an authorized Workspace to use local stars.',
  'files.stars.ready': '{{count}} currently accessible local stars.',
  'files.stars.add': 'Star in this browser',
  'files.stars.remove': 'Remove local star',
  'files.stars.removeNamed': 'Remove local star: {{name}}',
  'files.stars.saved': 'Local star saved.',
  'files.stars.removed': 'Local star removed.',
  'files.stars.limit': 'This Workspace has reached the limit of 32 local stars. Remove a star before adding another.',
  'files.stars.discarded': 'Invalid browser star data was discarded.',
  'files.browser.unavailableView': '{{view}} cannot be listed yet. This view has no authorized server query; zero results would be misleading. Your Workspace files remain available in Recent.',
  'files.browser.returnRecent': 'Return to Recent files',
  'files.browser.loadingFolders': 'Loading folders…',
  'files.browser.foldersUnavailable': 'Folders could not be loaded or are not available to you.',
} as const;

export const FILE_BROWSER_AVAILABILITY_JAPANESE: Record<
  keyof typeof FILE_BROWSER_AVAILABILITY_ENGLISH,
  string
> = {
  'files.stars.title': 'スター付きファイル · このブラウザー',
  'files.stars.localOnly': 'このブラウザーのアカウント・ワークスペース別にのみ保存します。サーバーや他の端末には同期しません。ブラウザーデータを消去するとスターも削除されます。',
  'files.stars.detailsOnly': 'ファイル名を表示する前にアクセス権を再確認します。コンテンツや操作は「最近のファイル」から利用できます。',
  'files.stars.loading': 'スター付きファイルへのアクセス権を確認しています…',
  'files.stars.empty': '現在アクセスできるローカルのスターはありません。「最近のファイル」でファイルを開き、このブラウザーにスターを付けてください。',
  'files.stars.error': '一部のファイルを確認できませんでした。未確認の名前は表示しません。再試行してアクセス権を確認してください。',
  'files.stars.storageUnavailable': 'ブラウザーのスターを保存または読み込みできませんでした。同期や保存の完了は確認されていません。',
  'files.stars.contextUnavailable': 'ローカルのスターを使うには、アクセス可能なワークスペースを選択してください。',
  'files.stars.ready': '現在アクセスできるローカルのスター：{{count}}件',
  'files.stars.add': 'このブラウザーにスターを付ける',
  'files.stars.remove': 'ローカルのスターを外す',
  'files.stars.removeNamed': 'ローカルのスターを外す：{{name}}',
  'files.stars.saved': 'ローカルにスターを保存しました。',
  'files.stars.removed': 'ローカルのスターを外しました。',
  'files.stars.limit': 'このワークスペースのローカルのスターは上限の32件です。追加する前にスターを外してください。',
  'files.stars.discarded': '無効なブラウザーのスターデータを削除しました。',
  'files.browser.unavailableView': '{{view}}はまだ一覧表示に対応していません。サーバー側の権限付き検索がないため、0件とは表示しません。ワークスペースのファイルは「最近のファイル」から確認できます。',
  'files.browser.returnRecent': '最近のファイルに戻る',
  'files.browser.loadingFolders': 'フォルダーを読み込んでいます…',
  'files.browser.foldersUnavailable': 'フォルダーを読み込めないか、アクセスできません。',
};
