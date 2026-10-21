import { expect, type Locator, type Page, test } from '@playwright/test';

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
      const quota = page.getByTestId('file-quota-state');
      await expect(quota).toHaveText('Storage usage unavailable.');
      await expect(quota.locator('progress')).toHaveCount(0);
      const quotaBounds = await quota.boundingBox();
      expect(quotaBounds).not.toBeNull();
      expect(quotaBounds!.height).toBeLessThanOrEqual(48);
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

      if (width === 1366) {
        const grid = page.locator('.app-data-grid').first();
        const nameHeader = grid.locator('[role="columnheader"][col-id="name"]');
        const statusHeader = grid.locator('[role="columnheader"][col-id="status"]');
        const scrollSurface = grid.locator('.ag-body-horizontal-scroll-viewport');
        const before = await scrollSurface.evaluate((element) => ({ width: element.clientWidth, scrollWidth: element.scrollWidth, scrollLeft: element.scrollLeft }));
        expect(before.scrollWidth).toBeGreaterThan(before.width);
        await tabTo(page, nameHeader);
        for (let column = 0; column < 4; column++) await page.keyboard.press('ArrowRight');
        await expect(statusHeader).toBeFocused();
        await expectFullyVisible(statusHeader);
        await page.screenshot({ path: testInfo.outputPath('approved-dark-files-1366-scrolled-end.png') });
        const after = await scrollSurface.evaluate((element) => ({ width: element.clientWidth, scrollWidth: element.scrollWidth, scrollLeft: element.scrollLeft }));
        expect(after.scrollLeft).toBeGreaterThan(before.scrollLeft);
        await testInfo.attach('files-grid-scroll-recovery', { body: JSON.stringify({ before, after }), contentType: 'application/json' });
        for (let column = 0; column < 4; column++) await page.keyboard.press('ArrowLeft');
        await expect(nameHeader).toBeFocused();
        await expectFullyVisible(nameHeader);
        await expect.poll(() => scrollSurface.evaluate((element) => element.scrollLeft)).toBeLessThan(after.scrollLeft);
      }

      const grantResponse = page.waitForResponse((response) =>
        new URL(response.url()).pathname === `/api/files/${fileObjectId}/download-grants`);
      const downloadResponse = page.waitForResponse((response) =>
        new URL(response.url()).pathname === '/api/file-download-grants/synthetic-preview/download');
      if (width <= 390) {
        await tabTo(page, open);
        await expectFullyVisible(open);
        await page.screenshot({ path: testInfo.outputPath(`approved-dark-files-keyboard-${width}.png`) });
      } else {
        await open.focus();
      }
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
      await expect(inspector.getByTestId('files-inspector-panel-details')).toContainText('File details are read-only here.');
      await expectNoAccessibilityViolations(page, '[data-testid="files-preview-pane"]');
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-preview-${width}.png`) });
      await inspector.getByTestId('files-preview-close').click();
      await expect(open).toBeFocused();

      if (width === 1536) {
        // Exercise a fresh server page, independently of AG Grid's local pager.
        const second = { ...file, id: 'd1000000-0000-4000-8000-000000000004',
          fileObjectId: 'd1000000-0000-4000-8000-000000000005', originalFileName: 'second-page.txt' };
        await page.route('**/api/files?**', async (route) => {
          const url = new URL(route.request().url());
          expect(url.searchParams.get('workspaceId')).toBe(workspaceId);
          const pageNumber = Number(url.searchParams.get('page'));
          expect([1, 2]).toContain(pageNumber);
          await route.fulfill({ json: { items: [pageNumber === 1 ? file : second],
            page: pageNumber, pageSize: 1, totalCount: 2, hasMore: pageNumber === 1 } });
        });
        await page.reload();
        await expect(page.getByTestId('files-page-pagination-status')).toContainText('Page 1 of 2');
        await expect(page.locator('.ag-paging-panel:visible')).toHaveCount(0);
        const pageTwoResponse = page.waitForResponse((response) => {
          const url = new URL(response.url());
          return url.pathname === '/api/files' && url.searchParams.get('page') === '2';
        });
        await page.getByTestId('files-page-next').click();
        expect((await pageTwoResponse).request().url()).toContain('pageSize=1');
        await expect(page.getByRole('button', { name: second.originalFileName, exact: true })).toBeVisible();
        await expect(page.getByRole('button', { name: file.originalFileName, exact: true })).toHaveCount(0);
        await expect(page.getByTestId('files-page-pagination-status')).toContainText('Page 2 of 2');
        await expect(page.getByTestId('files-page-next')).toBeDisabled();
        await expect(page.locator('.ag-paging-panel:visible')).toHaveCount(0);
        await page.getByTestId('files-page-previous').click();
        await expect(open).toBeVisible();
        await page.unroute('**/api/files?**');
      }

      await page.getByTestId('files-search-input').fill('pilot');
      await page.getByTestId('files-search-submit').click();
      await expect(page.getByTestId('files-search-status')).toContainText('1 currently authorized');
      await page.getByTestId('files-search-clear').click();
      denied = true;
      await page.reload();
      await expect(page.getByTestId('files-inventory-denied')).toBeVisible();
      await expectFullyVisible(page.getByTestId('files-inventory-denied'));
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

// Exercise the natural browser tab order; locator.focus() would hide an
// unreachable or offscreen action from this keyboard-only regression.
async function tabTo(page: Page, target: Locator): Promise<void> {
  for (let steps = 0; steps < 80; steps++) {
    if (await target.evaluate((element) => element === document.activeElement)) return;
    await page.keyboard.press('Tab');
  }
  await expect(target).toBeFocused();
}

async function expectFullyVisible(target: Locator): Promise<void> {
  await expect.poll(() => target.evaluate((element) => {
    const box = element.getBoundingClientRect();
    let left = 0, top = 0, right = innerWidth, bottom = innerHeight;
    for (let ancestor = element.parentElement; ancestor; ancestor = ancestor.parentElement) {
      const style = getComputedStyle(ancestor);
      const bounds = ancestor.getBoundingClientRect();
      if (/(auto|scroll|hidden|clip)/.test(style.overflowX)) {
        left = Math.max(left, bounds.left); right = Math.min(right, bounds.right);
      }
      if (/(auto|scroll|hidden|clip)/.test(style.overflowY)) {
        top = Math.max(top, bounds.top); bottom = Math.min(bottom, bounds.bottom);
      }
    }
    return box.left >= left - 1 && box.right <= right + 1 && box.top >= top - 1 && box.bottom <= bottom + 1;
  })).toBe(true);
}
