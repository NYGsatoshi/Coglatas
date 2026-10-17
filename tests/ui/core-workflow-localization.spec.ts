import { expect, type Page, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = 'static-workspace-1';
const projectId = '72100000-0000-4000-8000-000000000001';
const taskId = '72100000-0000-4000-8000-000000000002';
const conversationId = '72100000-0000-4000-8000-000000000003';
const fileId = '72100000-0000-4000-8000-000000000004';
const projectTitle = 'User-authored project 計画';
const taskTitle = 'User-authored task 調査';
const conversationTitle = 'User-authored DM 相談';
const fileName = 'User-authored file 資料.zip';
const draft = 'Unsent draft 下書き α';

test('changes language through preferences across core workflows without translating supplied content or sending a draft', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: testInfo.project.name === 'chromium-mobile' ? 320 : 1366, height: 844 });
  const errors: string[] = [];
  const mutations: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname;
    if (path.startsWith('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && !path.endsWith('/read')) {
      mutations.push(`${request.method()} ${path}`);
    }
  });
  await installLocalizedWorkflowApi(page);
  await page.goto('/app/account');

  for (const locale of ['ja', 'en'] as const) {
    await navigate(page, 'account', '/app/account');
    await page.getByTestId('display-language').selectOption(locale);
    await expect(page.locator('html')).toHaveAttribute('lang', locale);
    await expect(page.getByTestId('language-preferences')).toContainText(locale === 'ja' ? '表示言語' : 'Display language');
    expect(await page.evaluate(() => localStorage.getItem('coglatas.locale'))).toBe(locale);

    await navigate(page, 'projects', '/app/projects');
    await expect(page.getByRole('heading', { name: locale === 'ja' ? 'プロジェクト' : 'Projects', exact: true })).toBeVisible();
    await expect(page.getByText(projectTitle, { exact: true }).first()).toBeVisible();
    await expectContained(page);

    await navigate(page, 'my-tasks', '/app/tasks');
    await expect(page.getByRole('heading', { name: locale === 'ja' ? 'マイタスク' : 'My tasks', exact: true })).toBeVisible();
    await expect(page.getByTestId('my-tasks-search')).toHaveAccessibleName(locale === 'ja' ? '検索' : 'Search');
    await expect(page.getByText(taskTitle, { exact: true }).first()).toBeVisible();
    await expect(page.getByTestId('my-tasks-results')).toContainText('Configured stage ユーザー');
    await expectContained(page);

    await navigate(page, 'messages', '/app/messages');
    const conversation = page.getByTestId('conversation-list-item').filter({ hasText: conversationTitle });
    await conversation.click();
    await expect(page).toHaveURL(new RegExp(`/app/dm/${conversationId}$`));
    await expect(page.getByTestId('send-message')).toHaveAccessibleName(locale === 'ja' ? '送信' : 'Send');
    await expect(page.getByText('Supplied message 本文', { exact: true })).toBeVisible();
    const composer = page.getByTestId('message-draft');
    if (locale === 'ja') {
      await composer.fill(draft);
    } else {
      await expect(composer).toHaveValue(draft);
    }
    await expectContained(page);

    await navigate(page, 'files', '/app/files');
    await expect(page.getByRole('heading', { name: locale === 'ja' ? 'ファイル' : 'Files', exact: true })).toBeVisible();
    await expect(page.getByText(fileName, { exact: true }).filter({ visible: true }).first()).toBeVisible();
    await expectContained(page);
    await expectNoAccessibilityViolations(page, 'main');
  }

  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  expect(await page.evaluate(() => localStorage.getItem('coglatas.locale'))).toBe('en');
  expect(mutations).toEqual([]);
  expect(errors).toEqual([]);
});

async function navigate(page: Page, id: string, path: string): Promise<void> {
  const toggle = page.getByTestId('mobile-nav-toggle');
  if ((page.viewportSize()?.width ?? 1366) < 768) {
    await expect(toggle).toBeVisible();
    const navigation = page.getByTestId('mobile-navigation');
    if (await navigation.getAttribute('aria-hidden') === 'true') {
      await toggle.click();
    }
    await navigation.getByTestId(`nav-${id}`).click();
  } else {
    await page.getByTestId(`nav-${id}`).filter({ visible: true }).first().click();
  }
  await expect(page).toHaveURL(new RegExp(`${path}$`));
}

async function expectContained(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => ({
    document: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    main: (() => {
      const main = document.getElementById('app-shell-main-content');
      return main ? main.scrollWidth - main.clientWidth : 0;
    })()
  }));
  expect(overflow.document).toBeLessThanOrEqual(1);
  expect(overflow.main).toBeLessThanOrEqual(1);
}

async function installLocalizedWorkflowApi(page: Page): Promise<void> {
  const conversation = {
    id: conversationId, workspaceId, type: 'DirectMessage', title: conversationTitle,
    unreadCount: 0, hasMention: false, isMuted: false, isArchived: false, isLocked: false,
    createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z'
  };
  const project = {
    id: projectId, workspaceId, title: projectTitle, status: 'Active', visibility: 'WorkspaceVisible',
    activationState: 'Activated', createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z'
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/security/csrf-token') { return json({ token: 'localization-csrf', headerName: 'X-CSRF-Token' }); }
    if (path === '/api/projects') { return json({ items: [project], totalCount: 1, page: 1, pageSize: 50 }); }
    if (path === `/api/projects/${projectId}`) { return json(project); }
    if (path === `/api/projects/${projectId}/tasks`) { return json({ items: [], totalCount: 0, page: 1, pageSize: 50 }); }
    if (path === '/api/me/tasks/counts') {
      return json({ views: [{ view: 'Assigned', count: 1 }], timeGroups: [{ timeGroup: 'Today', count: 1 }] });
    }
    if (path === '/api/me/tasks') {
      return json({
        items: [{ taskId, tenantId: 'mock-tenant', workspaceId, workspaceTitle: 'Static Workspace', projectId,
          projectTitle, title: taskTitle, workflowStageName: 'Configured stage ユーザー', stageCategory: 'Todo',
          priority: 'High', isBlocked: false, progressPercent: 0, timeGroup: 'Today', isOverdue: false,
          version: 1, labels: [], checklistCompletedCount: 0, checklistTotalCount: 0,
          quickEditPermissions: { canClaim: false, canChangeStage: false }, warnings: [] }],
        totalCount: 1, page: 1, pageSize: 50
      });
    }
    if (path === '/api/conversations') { return json({ items: [conversation], counts: { all: 1, unread: 0, mentions: 0, later: 0 } }); }
    if (path === `/api/conversations/${conversationId}`) {
      return json({ ...conversation, members: [
        { userId: 'mock-user-a', displayName: 'Mock User A', canRead: true, canPost: true },
        { userId: 'mock-user-b', displayName: 'User-authored name 利用者', canRead: true, canPost: true }
      ] });
    }
    if (path === `/api/conversations/${conversationId}/state`) {
      return json({ conversationId, userId: 'mock-user-a', isMuted: false, isLater: false, unreadCount: 0 });
    }
    if (path === `/api/conversations/${conversationId}/messages` && request.method() === 'GET') {
      return json({ items: [{ id: 'localization-message', workspaceId, conversationId, authorUserId: 'mock-user-b',
        authorDisplayName: 'User-authored name 利用者', body: 'Supplied message 本文',
        createdAt: '2026-10-16T10:00:00Z', version: 1 }] });
    }
    if (path === `/api/conversations/${conversationId}/read` && request.method() === 'POST') { return json({}); }
    if (path === '/api/file-folders') { return json({ workspaceId, rootVersion: 1, folders: [] }); }
    if (path === '/api/files') {
      return json({ items: [{ id: fileId, fileObjectId: fileId, workspaceId, originalFileName: fileName,
        contentType: 'application/zip', sizeBytes: 2048, status: 'Active', scanStatus: 'Clean',
        uploadedByUserId: 'mock-user-b', uploadedByDisplayName: 'User-authored name 利用者', canDelete: false, accessState: 'Workspace',
        createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z' }],
        totalCount: 1, page: 1, pageSize: 50, hasMore: false });
    }
    return route.fallback();
  });
}
