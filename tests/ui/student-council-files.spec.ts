import { expect, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = 'd1000000-0000-4000-8000-000000000001';
const fileObjectId = 'd1000000-0000-4000-8000-000000000002';
const workspace = {
  id: workspaceId, name: 'Student Council', status: 'Active', currentUserRole: 'Member',
  accessSource: 'WorkspaceMembership', canOpenWorkspace: true, canOpenMembers: true,
  canOpenProjects: true, unreadAnnouncementCount: 0, unreadConversationCount: 0,
  inProgressProjectCount: 0, runningProjectCount: 0, needsReviewProjectCount: 0,
};
const file = {
  id: 'd1000000-0000-4000-8000-000000000003', fileObjectId, workspaceId,
  originalFileName: 'pilot-notes.txt', contentType: 'text/plain', sizeBytes: 31,
  status: 'Active', scanStatus: 'Allowed', uploadedByUserId: 'synthetic-member',
  uploadedByDisplayName: 'Council Member', createdAt: '2026-10-18T09:00:00Z',
  updatedAt: '2026-10-19T01:00:00Z', canDelete: false, accessState: 'Workspace',
  canManageSharing: false,
};

// Synthetic frontend evidence only. Tenant feature flags and actual upload
// denial require independent cookie/CSRF HTTP evidence against the pilot host.
test.describe('Student council Approved Dark Files', () => {
  for (const width of [1536, 1366, 768, 390, 320]) {
    test(`keeps scoped Files, preview and denied states usable at ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 1024 });
      await page.addInitScript(() => localStorage.setItem('coglatas.locale', 'en'));
      const errors: string[] = [];
      const uploads: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      page.on('request', (request) => {
        if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/files') {
          uploads.push(request.url());
        }
      });
      await page.route('**/api/auth/me', (route) => route.fulfill({ json: {
        userId: 'synthetic-member', displayName: 'Council Member', email: 'council@example.test',
        systemRole: 'TenantUser', status: 'Active', currentWorkspace: workspace, workspaces: [workspace],
      } }));
      await page.route('**/api/workspaces', (route) => route.fulfill({ json: [workspace] }));
      await page.route('**/api/security/csrf-token', (route) => route.fulfill({ json: {
        token: 'synthetic-files-csrf', headerName: 'X-CSRF-Token',
      } }));
      await page.route('**/api/file-folders**', (route) => route.fulfill({ json: {
        workspaceId, rootVersion: 1, folders: [],
      } }));
      let denied = false;
      await page.route('**/api/files**', async (route) => {
        const url = new URL(route.request().url());
        if (url.pathname === `/api/files/${fileObjectId}/download-grants`) {
          await route.fulfill({ json: { fileDownloadGrantId: 'synthetic-preview', fileObjectId, token: 'synthetic-token' } });
        } else if (url.pathname === '/api/files' && route.request().method() === 'GET') {
          await route.fulfill(denied
            ? { status: 403, json: { message: 'Private diagnostic must stay redacted' } }
            : { json: { items: [file], page: 1, pageSize: 50, totalCount: 1, hasMore: false } });
        } else { await route.fulfill({ status: 404 }); }
      });
      await page.route('**/api/file-download-grants/synthetic-preview/download', (route) =>
        route.fulfill({ contentType: 'text/plain', body: 'Student Council synthetic notes.' }));
      await page.route('**/api/search**', (route) => route.fulfill({ json: {
        items: [{ ...file, id: fileObjectId, title: file.originalFileName, type: 13 }],
        page: 1, pageSize: 50, totalCount: 1,
      } }));

      await page.goto('/app/files');
      await expect(page.getByTestId('files-upload-unavailable')).toBeDisabled();
      await expect(page.getByTestId('files-pilot-intake')).toContainText('Uploads are unavailable');
      await expect(page.locator('input[type="file"]')).toHaveCount(0);
      const open = page.getByRole('button', { name: width <= 860 ? 'Preview pilot-notes.txt' : 'pilot-notes.txt', exact: true });
      await expect(open).toBeVisible();
      await expectNoAccessibilityViolations(page, 'main');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      if (width === 1536) {
        const columns = await page.locator('.files-page__grid--browser').evaluate((grid) => {
          const widthOf = (selector: string) => grid.querySelector(selector)!.getBoundingClientRect().width;
          return {
            total: grid.getBoundingClientRect().width,
            sidebar: widthOf('app-file-browser-sidebar'),
            inventory: widthOf('.files-page__main'),
            inspector: widthOf('.files-page__side'),
            gap: Number.parseFloat(getComputedStyle(grid).columnGap),
          };
        });
        expect(columns.sidebar).toBeCloseTo(208, 1);
        expect(columns.inspector).toBeCloseTo(302, 1);
        expect(columns.inventory).toBeCloseTo(columns.total - 208 - 302 - 2 * columns.gap, 1);
      }
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-files-${width}.png`) });

      const grantResponse = page.waitForResponse((response) =>
        new URL(response.url()).pathname === `/api/files/${fileObjectId}/download-grants`);
      const downloadResponse = page.waitForResponse((response) =>
        new URL(response.url()).pathname === '/api/file-download-grants/synthetic-preview/download');
      await open.focus();
      await page.keyboard.press('Enter');
      const previewResponses = await Promise.all([grantResponse, downloadResponse]);
      for (const response of previewResponses) {
        expect(response.status()).toBe(200);
        expect(response.request().method()).toBe('POST');
        expect(response.request().headers()['x-csrf-token']).toBe('synthetic-files-csrf');
      }
      expect(previewResponses[0].request().postDataJSON()).toEqual({ purpose: 'files-page-preview' });
      expect(previewResponses[1].request().postDataJSON()).toEqual({ token: 'synthetic-token' });
      const inspector = page.getByTestId('files-preview-pane');
      await expect(inspector.getByTestId('files-preview-text')).toContainText('Student Council synthetic notes.');
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-file-content-${width}.png`) });
      await inspector.getByRole('tab', { name: 'Details', exact: true }).click();
      await expect(inspector.getByTestId('files-inspector-panel-details')).toContainText('Council Member');
      await expectNoAccessibilityViolations(page, '[data-testid="files-preview-pane"]');
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-preview-${width}.png`) });
      await inspector.getByTestId('files-preview-close').click();
      await expect(open).toBeFocused();

      await page.getByTestId('files-search-input').fill('pilot');
      await page.getByTestId('files-search-submit').click();
      await expect(page.getByTestId('files-search-status')).toContainText('1 currently authorized');
      await page.getByTestId('files-search-clear').click();
      denied = true;
      await page.reload();
      await expect(page.getByTestId('files-inventory-denied')).toBeVisible();
      await expect(page.getByTestId('files-inventory-empty')).toHaveCount(0);
      await expect(page.locator('main')).not.toContainText('pilot-notes.txt');
      await expect(page.locator('main')).not.toContainText('Private diagnostic');
      await expectNoAccessibilityViolations(page, 'main');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-files-denied-${width}.png`) });
      expect(uploads).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
});
