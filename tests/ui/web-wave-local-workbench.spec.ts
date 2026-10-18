import { expect, test, type Page, type TestInfo } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = 'd0190000-0000-4000-8000-000000000001';
const fileId = 'd0190000-0000-4000-8000-000000000002';
const fileName = 'Synthetic file 資料📄'.repeat(12) + '.zip';
const starsKey = `coglatas.local-file-stars.v1:${JSON.stringify(['mock-tenant', 'mock-user-a', workspaceId])}`;

// Static, synthetic browser checks. These do not establish ASP.NET Core or PostgreSQL compatibility.
test.describe('Web wave local Draft Links and Starred regression', () => {
  for (const width of [320, 1366]) {
    for (const theme of ['light', 'dark'] as const) {
      test(`keeps Draft and synthetic Links local, accessible and recoverable at ${width}px / ${theme}`, async ({ page }, testInfo) => {
        const api = await installApi(page, width, theme);
        await page.goto('/app/drafts');
        const workbench = page.getByTestId('draft-workbench');
        await expect(workbench).toBeVisible();
        await expect(workbench).toContainText('Refreshing or closing the tab loses them.');
        await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', theme);
        const body = page.getByLabel('Draft text', { exact: true });
        await page.getByLabel('Title', { exact: true }).fill('Synthetic Draft 下書き');
        await body.fill('Retained Unicode text 🧪\nUnsent local working content');
        await body.focus();
        // Synthetic composition events exercise handlers; no native OS IME claim is made.
        await body.dispatchEvent('compositionstart', { data: '変換中' });
        await body.dispatchEvent('keydown', { key: 'Enter', code: 'Enter', isComposing: true, keyCode: 229 });
        await expect(body).toHaveValue('Retained Unicode text 🧪\nUnsent local working content');
        await body.dispatchEvent('compositionend', { data: '変換中' });
        await page.getByRole('button', { name: 'Keep local checkpoint', exact: true }).click();
        await expect(page.getByTestId('draft-status')).toContainText('It is not saved to the server.');
        await page.getByRole('button', { name: 'Show server save status', exact: true }).click();
        await expect(page.getByTestId('draft-status')).toContainText('Server save and cross-device sync are unavailable.');
        await expect(body).toHaveValue('Retained Unicode text 🧪\nUnsent local working content');

        await expect(page.getByLabel('Search demo targets')).toHaveCount(0);
        await page.getByLabel('Enable synthetic Links preview').check();
        const query = page.getByLabel('Search demo targets');
        await query.fill('ｆｉｌｅ');
        const results = page.getByRole('list', { name: 'Demo target search results' });
        await expect(results.getByRole('button')).toHaveCount(1);
        await results.getByRole('button', { name: /Add preview link Demo File/ }).click();
        await expect(query).toBeFocused();
        const links = page.getByRole('list', { name: 'Local preview links' });
        await expect(links).toContainText('Demo File');
        await expect(results).toContainText('No matching unselected demo targets.');
        await links.getByRole('button', { name: /Remove preview link/ }).click();
        await expect(query).toBeFocused();
        await expect(links).toHaveCount(0);
        await expect(results.getByRole('button')).toHaveCount(1);
        await results.getByRole('button').click();
        await captureAndCheck(page, testInfo, `draft-${width}-${theme}`);
        await navigateWithinApp(page, width, 'nav-files');
        await expect(page.getByTestId('files-page')).toBeVisible();
        await navigateWithinApp(page, width, 'nav-drafts');
        await expect(body).toHaveValue('Retained Unicode text 🧪\nUnsent local working content');
        await expect(links).toContainText('Demo File');

        await page.getByRole('button', { name: 'Discard local draft', exact: true }).click();
        await page.getByRole('button', { name: 'Keep editing', exact: true }).click();
        await expect(page.getByRole('button', { name: 'Discard local draft', exact: true })).toBeFocused();
        await expect(body).toHaveValue('Retained Unicode text 🧪\nUnsent local working content');
        await page.getByRole('button', { name: 'Discard local draft', exact: true }).click();
        await page.getByRole('button', { name: 'Confirm discard', exact: true }).click();
        await expect(page.getByLabel('Title', { exact: true })).toBeFocused();
        await expect(body).toHaveValue('');
        await expect(links).toHaveCount(0);
        await expect(page.getByLabel('Enable synthetic Links preview')).not.toBeChecked();
        await body.fill('Refreshing must lose this memory-only draft');
        page.on('dialog', (dialog) => dialog.accept());
        await page.reload();
        await expect(body).toHaveValue('');
        await expect(page.getByLabel('Title', { exact: true })).toHaveValue('');
        expect(api.requests.filter((entry) => /\/api\/.*(?:draft|link|star)/iu.test(entry))).toEqual([]);
        expect(api.requests.filter((entry) => !entry.startsWith('GET '))).toEqual([]);
        const stored = await page.evaluate(() => Object.values(localStorage).join(' '));
        expect(stored).not.toContain('Unsent local working content');
        expect(stored).not.toContain('Demo File');
      });

      test(`reauthorizes local Starred IDs and recovers without stale metadata at ${width}px / ${theme}`, async ({ page }, testInfo) => {
        const api = await installApi(page, width, theme);
        await page.goto('/app/files');
        const preview = page.getByRole('button', { name: width <= 860 ? `Preview ${fileName}` : fileName, exact: true });
        await expect(preview).toBeVisible();
        await preview.focus();
        await preview.press('Enter');
        const inspector = page.getByTestId('files-preview-pane');
        await expect(inspector).toBeVisible();
        await page.getByTestId('files-preview-star').click();
        await expect(page.getByTestId('files-preview-star')).toHaveAttribute('aria-pressed', 'true');
        await expect(page.getByTestId('files-preview-star-status')).toContainText('Not synced to the server or other devices');
        expect(await page.evaluate((key) => JSON.parse(localStorage.getItem(key)!), starsKey)).toEqual({ version: 1, ids: [fileId] });
        await page.getByTestId('files-preview-close').click();
        await expect(preview).toBeFocused();

        const starred = page.getByRole('button', { name: 'Starred files', exact: true });
        await starred.click();
        const stars = page.getByTestId('files-stars-list');
        await expect(stars).toContainText(fileName);
        expect(api.detailReads).toBe(1);
        await expect(page.getByTestId('files-stars-local-notice')).toContainText('Not synced to the server or other devices');
        await stars.getByRole('button', { name: fileName, exact: true }).click();
        await expect(page.getByRole('tab', { name: 'Details', exact: true })).toHaveAttribute('aria-selected', 'true');
        await expect(page.getByTestId('files-preview-download')).toBeDisabled();
        await page.getByTestId('files-preview-close').click();
        await captureAndCheck(page, testInfo, `starred-${width}-${theme}`);

        api.detailMode = 'offline';
        await page.getByTestId('files-stars-return').click();
        await starred.click();
        await expect(page.getByTestId('files-stars-status')).toContainText('Unverified names are hidden.');
        await expect(stars).not.toContainText(fileName);
        expect(await page.evaluate((key) => JSON.parse(localStorage.getItem(key)!), starsKey)).toEqual({ version: 1, ids: [fileId] });
        api.detailMode = 'ready';
        await page.getByTestId('files-stars-retry').click();
        await expect(stars).toContainText(fileName);
        await stars.getByRole('button', { name: `Remove local star: ${fileName}`, exact: true }).click();
        await expect(page.getByTestId('files-stars-return')).toBeFocused();
        await expect(stars).not.toContainText(fileName);
        expect(await page.evaluate((key) => JSON.parse(localStorage.getItem(key)!), starsKey)).toEqual({ version: 1, ids: [] });
        await page.getByTestId('files-stars-return').click();
        await preview.click();
        await page.getByTestId('files-preview-star').click();
        await page.getByTestId('files-preview-close').click();

        api.detailMode = 'denied';
        await starred.click();
        await expect(stars).not.toContainText(fileName);
        await expect(page.getByTestId('files-stars-status')).toContainText('No currently accessible local stars.');
        expect(await page.evaluate((key) => JSON.parse(localStorage.getItem(key)!), starsKey)).toEqual({ version: 1, ids: [] });
        await expect(page.getByTestId('files-page')).not.toContainText('private diagnostic');
        expect(api.requests.filter((entry) => /\/api\/.*(?:draft|link|star|shared|artifact)/iu.test(entry))).toEqual([]);
        expect(api.requests.filter((entry) => !entry.startsWith('GET '))).toEqual([]);
        const stored = await page.evaluate((key) => localStorage.getItem(key), starsKey);
        expect(stored).not.toContain('Synthetic file');
        await noHorizontalOverflow(page);
      });
    }
  }
});

async function installApi(page: Page, width: number, theme: 'light' | 'dark') {
  const control = { requests: [] as string[], detailReads: 0, detailMode: 'ready' as 'ready' | 'denied' | 'offline' };
  await page.setViewportSize({ width, height: 900 });
  await page.addInitScript((theme) => {
    localStorage.setItem('coglatas.ui.theme.v1', theme);
    localStorage.setItem('coglatas.locale', 'en');
  }, theme);
  const workspace = { id: workspaceId, name: 'Synthetic wave Workspace', status: 'Active',
    currentUserRole: 'Member', accessSource: 'WorkspaceMembership', canOpenWorkspace: true,
    canOpenProjects: true, canAddFiles: false, unreadAnnouncementCount: 0, unreadConversationCount: 0 };
  const file = { id: fileId, fileObjectId: fileId, workspaceId, originalFileName: fileName,
    contentType: 'application/zip', sizeBytes: 2048, status: 'Active', scanStatus: 'Clean',
    canDelete: false, createdAt: '2026-10-19T00:00:00Z', updatedAt: '2026-10-19T00:00:00Z' };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    control.requests.push(`${request.method()} ${path}`);
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/auth/me') { return json({ userId: 'mock-user-a', displayName: 'Synthetic user', systemRole: 'TenantUser',
      status: 'Active', capabilities: ['workspace:view', 'projects:view', 'files:view'], currentWorkspace: workspace, workspaces: [workspace] }); }
    if (path === '/api/tenants/current') { return json({ tenantId: 'mock-tenant', tenantSlug: 'mock', isAvailable: true,
      isPlatformScope: false, displayName: 'Synthetic Tenant', status: 'Active', currentUserRole: 'Member', appMode: 'OnPremSingleTenant' }); }
    if (path === '/api/workspaces') { return json([workspace]); }
    if (path === '/api/workspaces/capabilities') { return json({ requestId: 'synthetic-wave', data: { canCreate: false }, warnings: [] }); }
    if (path === '/api/file-folders') { return json({ workspaceId, rootVersion: 1, folders: [] }); }
    if (path === '/api/files') { return json({ items: [file], totalCount: 1, page: 1, pageSize: 20, hasMore: false }); }
    if (path === `/api/files/${fileId}`) {
      control.detailReads++;
      if (control.detailMode === 'offline') { return route.abort('internetdisconnected'); }
      if (control.detailMode === 'denied') { return route.fulfill({ status: 403, contentType: 'application/json', body: '{"message":"private diagnostic"}' }); }
      return json(file);
    }
    if (path === '/api/projects') { return json({ items: [], totalCount: 0, page: 1, pageSize: 20 }); }
    if (path === '/api/conversations') { return json({ items: [], counts: { all: 0, unread: 0, mentions: 0, later: 0 } }); }
    return route.fallback();
  });
  return control;
}

async function noHorizontalOverflow(page: Page) {
  const sizes = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
  expect(sizes.scroll).toBeLessThanOrEqual(sizes.client);
}

async function navigateWithinApp(page: Page, width: number, testId: string) {
  if (width <= 860) {
    await page.getByTestId('mobile-nav-toggle').click();
    await page.getByTestId('mobile-navigation').getByTestId(testId).click();
  } else {
    await page.locator('.app-shell__feature-menu').getByTestId(testId).click();
  }
}

async function captureAndCheck(page: Page, testInfo: TestInfo, name: string) {
  await noHorizontalOverflow(page);
  await expectNoAccessibilityViolations(page);
  const path = testInfo.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true });
  await testInfo.attach('Windows diagnostic screenshot', { path, contentType: 'image/png' });
}
