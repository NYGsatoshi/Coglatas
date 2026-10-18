/**
 * Small, self-contained File-browser availability copy.
 *
 * This is a typed extension to the Files locale catalog, not a new global
 * locale source. Keep the two-language contract checked at compile time.
 */
export const FILE_BROWSER_AVAILABILITY_ENGLISH = {
  'files.browser.unavailableView': '{{view}} cannot be listed yet. This view has no authorized server query; zero results would be misleading. Your Workspace files remain available in Recent.',
  'files.browser.returnRecent': 'Return to Recent files',
} as const;

export const FILE_BROWSER_AVAILABILITY_JAPANESE: Record<
  keyof typeof FILE_BROWSER_AVAILABILITY_ENGLISH,
  string
> = {
  'files.browser.unavailableView': '{{view}}はまだ一覧表示に対応していません。サーバー側の権限付き検索がないため、0件とは表示しません。ワークスペースのファイルは「最近のファイル」から確認できます。',
  'files.browser.returnRecent': '最近のファイルに戻る',
};
