import { deflateSync } from 'node:zlib';
import { expect, Locator, Page, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = 'd9000000-0000-4000-8000-000000000001';
const otherWorkspaceId = 'd9000000-0000-4000-8000-000000000002';
const workspace = {
  id: workspaceId, name: 'Student Council', status: 'Active', currentUserRole: 'Member',
  accessSource: 'WorkspaceMembership', canOpenWorkspace: true, canOpenMembers: true,
  canOpenProjects: true, unreadAnnouncementCount: 0, unreadConversationCount: 0,
  inProgressProjectCount: 0, runningProjectCount: 0, needsReviewProjectCount: 0,
};
const images = [
  { id: 'd9000000-0000-4000-8000-000000000003', name: 'synthetic-wide.png', width: 2400, height: 1200 },
  { id: 'd9000000-0000-4000-8000-000000000004', name: 'synthetic-portrait.png', width: 900, height: 1800 },
].map((image) => ({ ...image, bytes: syntheticPng(image.width, image.height) }));

// Decodable synthetic PNGs and HTTP mocks exercise frontend geometry and existing
// grant/CSRF dispatch only. They do not establish backend authorization or scanning.
test.describe('Authorized Files image viewport', () => {
  for (const width of [320, 390, 768, 1366, 1536]) {
    test(`fits, rotates and scrolls real wide and portrait PNGs at ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 1024 });
      const fixture = await installImages(page);
      await page.goto('/app/workspaces');
      await page.getByTestId('workspace-switcher').selectOption(workspaceId);
      await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
      await page.goto('/app/files');
      await expect(page.getByTestId('files-upload-unavailable')).toBeDisabled();
      await expect(page.locator('input[type="file"]')).toHaveCount(0);
      for (const image of images) {
        const open = previewButton(page, image.name, width);
        await open.focus();
        await page.keyboard.press('Enter');
        const preview = page.getByTestId('files-preview-image');
        await expect(preview).toBeVisible();
        await expect.poll(() => preview.evaluate((element: HTMLImageElement) => [element.naturalWidth, element.naturalHeight]))
          .toEqual([image.width, image.height]);
        await expect(page.getByTestId('files-image-fit')).toHaveAttribute('aria-pressed', 'true');
        await expect(page.getByTestId('files-image-status')).toContainText('0°');
        await expectFitted(page);
        expect(await preview.evaluate((element: HTMLImageElement) => Number.parseFloat(element.style.width) / element.naturalWidth)).toBeLessThan(0.25);
        await page.screenshot({ path: testInfo.outputPath(`${image.name}-${width}-fit.png`) });

        const fitWidth = await preview.evaluate((element: HTMLImageElement) => Number.parseFloat(element.style.width));
        await page.getByTestId('files-image-zoom-in').focus();
        await page.keyboard.press('Enter');
        await expect(page.getByTestId('files-image-zoom-in')).toBeFocused();
        await expect.poll(() => preview.evaluate((element: HTMLImageElement) => Number.parseFloat(element.style.width)))
          .toBeCloseTo(fitWidth * 1.25, 1);
        await page.getByTestId('files-image-actual').click();
        await expect(preview).toHaveCSS('width', `${image.width}px`);
        await expect(preview).toHaveCSS('height', `${image.height}px`);
        await expect(page.getByTestId('files-image-actual')).toHaveAttribute('aria-pressed', 'true');
        const viewport = page.getByTestId('files-image-viewport');
        await viewport.focus();
        await page.keyboard.press('ArrowRight');
        await page.keyboard.press('ArrowDown');
        await expect.poll(() => viewport.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);
        await expect.poll(() => viewport.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);

        for (const angle of [90, 180, 270]) {
          await page.getByTestId('files-image-rotate').click();
          await expect(page.getByTestId('files-image-status')).toContainText(`${angle}°`);
          await expectRotatedBounds(page, angle % 180 ? image.height : image.width, angle % 180 ? image.width : image.height);
        }
        await page.screenshot({ path: testInfo.outputPath(`${image.name}-${width}-rotated-actual.png`) });
        await page.getByTestId('files-image-fit').click();
        await expect(page.getByTestId('files-image-fit')).toHaveAttribute('aria-pressed', 'true');
        await expectFitted(page);
        await expectNoAccessibilityViolations(page, '[data-testid="files-preview-pane"]');
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await page.screenshot({ path: testInfo.outputPath(`${image.name}-${width}-rotated-fit.png`) });
        await page.getByTestId('files-preview-close').click();
        await expect(open).toBeFocused();
        await expect(preview).toHaveCount(0);
      }
      expect(fixture.grants).toHaveLength(2);
      expect(fixture.downloads).toHaveLength(2);
      expect(fixture.uploads).toEqual([]);
      expect(fixture.errors).toEqual([]);
      expect(await revokedCount(page)).toBe(2);
    });
  }

  test('discards dimensions, transforms and protected URLs on replacement, workspace, denial and session expiry', async ({ page }) => {
    await page.setViewportSize({ width: 1536, height: 1024 });
    const fixture = await installImages(page);
    await page.goto('/app/workspaces');
    await page.getByTestId('workspace-switcher').selectOption(workspaceId);
    await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await page.goto('/app/files');
    const wide = previewButton(page, images[0].name, 1536);
    const portrait = previewButton(page, images[1].name, 1536);
    await wide.click();
    await expect(page.getByTestId('files-preview-image')).toBeVisible();
    await page.getByTestId('files-image-actual').click();
    await page.getByTestId('files-image-rotate').click();
    await portrait.click();
    await expect(page.getByTestId('files-preview-image')).toHaveAttribute('alt', images[1].name);
    await expect(page.getByTestId('files-image-status')).toContainText('0°');
    await expect(page.getByTestId('files-image-fit')).toHaveAttribute('aria-pressed', 'true');
    await expectFitted(page);
    await expect.poll(() => revokedCount(page)).toBe(1);
    await page.getByTestId('workspace-switcher').selectOption(otherWorkspaceId);
    await expect(page.getByTestId('files-preview-image')).toHaveCount(0);
    await expect.poll(() => revokedCount(page)).toBe(2);
    await expect(page).toHaveURL(/\/app\/workspaces$/);
    await page.getByTestId('workspace-switcher').selectOption(workspaceId);
    await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await page.goto('/app/files');
    await expect(wide).toBeVisible();
    fixture.denied = true;
    await wide.click();
    await expect(page.getByTestId('files-preview-error')).toBeVisible();
    await expect(page.getByTestId('files-image-controls')).toHaveCount(0);
    await expect(page.getByTestId('files-preview-image')).toHaveCount(0);
    fixture.denied = false;
    await page.getByTestId('files-preview-close').click();
    await wide.click();
    await expect(page.getByTestId('files-preview-image')).toBeVisible();
    fixture.revoked = true;
    await page.getByTestId('files-search-input').fill('synthetic');
    const deniedSearch = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/search');
    await page.getByTestId('files-search-submit').click();
    expect((await deniedSearch).status()).toBe(403);
    await expect(page.getByTestId('files-preview-image')).toHaveCount(0);
    await expect(page.getByTestId('files-image-controls')).toHaveCount(0);
    await expect.poll(() => revokedCount(page)).toBe(1);
    fixture.revoked = false;
    await page.getByTestId('files-search-clear').click();
    await wide.click();
    await expect(page.getByTestId('files-image-fit')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByTestId('files-image-status')).toContainText('0°');
    fixture.expired = true;
    await page.getByTestId('files-preview-more').click();
    await page.getByTestId('files-preview-download').click();
    await expect(page.getByTestId('files-preview-image')).toHaveCount(0);
    await expect(page.getByTestId('files-image-controls')).toHaveCount(0);
    // Full navigation above creates a new document/revocation counter.
    await expect.poll(() => revokedCount(page)).toBe(2);
    expect(fixture.uploads).toEqual([]);
    expect(fixture.errors).toEqual([]);
  });
});

function previewButton(page: Page, name: string, width: number): Locator {
  return page.getByRole('button', { name: width <= 860 ? `Preview ${name}` : name, exact: true });
}

async function expectFitted(page: Page): Promise<void> {
  const bounds = await page.getByTestId('files-image-viewport').evaluate((viewport) => {
    const outer = viewport.getBoundingClientRect();
    const image = viewport.querySelector('img')!.getBoundingClientRect();
    return { left: image.left - outer.left, top: image.top - outer.top, right: outer.right - image.right, bottom: outer.bottom - image.bottom,
      scrollWidth: viewport.scrollWidth, width: viewport.clientWidth, scrollHeight: viewport.scrollHeight, height: viewport.clientHeight };
  });
  expect(Math.min(bounds.left, bounds.top, bounds.right, bounds.bottom)).toBeGreaterThanOrEqual(-1);
  expect(bounds.scrollWidth).toBeLessThanOrEqual(bounds.width + 1);
  expect(bounds.scrollHeight).toBeLessThanOrEqual(bounds.height + 1);
}

async function expectRotatedBounds(page: Page, width: number, height: number): Promise<void> {
  const stage = page.getByTestId('files-image-stage');
  await expect(stage).toHaveCSS('width', `${width}px`);
  await expect(stage).toHaveCSS('height', `${height}px`);
  const viewport = page.getByTestId('files-image-viewport');
  const origin = await viewport.evaluate((element) => {
    const stageBounds = element.querySelector('[data-testid="files-image-stage"]')!.getBoundingClientRect();
    const image = element.querySelector('img')!.getBoundingClientRect();
    return { x: image.left - stageBounds.left, y: image.top - stageBounds.top, width: image.width, height: image.height };
  });
  expect(origin.x).toBeCloseTo(0, 1);
  expect(origin.y).toBeCloseTo(0, 1);
  expect(origin.width).toBeCloseTo(width, 1);
  expect(origin.height).toBeCloseTo(height, 1);
  await viewport.evaluate((element) => { element.scrollLeft = element.scrollWidth; element.scrollTop = element.scrollHeight; });
  const edge = await viewport.evaluate((element) => {
    const outer = element.getBoundingClientRect();
    const image = element.querySelector('img')!.getBoundingClientRect();
    return { right: image.right - (outer.left + element.clientLeft + element.clientWidth), bottom: image.bottom - (outer.top + element.clientTop + element.clientHeight) };
  });
  expect(Math.abs(edge.right)).toBeLessThanOrEqual(1);
  expect(Math.abs(edge.bottom)).toBeLessThanOrEqual(1);
}

async function revokedCount(page: Page): Promise<number> {
  return page.evaluate(() => (window as Window & { revokedPreviews: number }).revokedPreviews);
}

async function installImages(page: Page) {
  const state = { denied: false, revoked: false, expired: false, grants: [] as string[], downloads: [] as string[], uploads: [] as string[], errors: [] as string[] };
  await page.addInitScript(() => {
    localStorage.setItem('coglatas.locale', 'en');
    const tracked = window as Window & { revokedPreviews: number };
    tracked.revokedPreviews = 0;
    const revoke = URL.revokeObjectURL.bind(URL);
    URL.revokeObjectURL = (url) => { tracked.revokedPreviews++; revoke(url); };
  });
  page.on('pageerror', (error) => state.errors.push(error.message));
  page.on('request', (request) => {
    if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/files') { state.uploads.push(request.url()); }
  });
  const workspaces = [workspace, { ...workspace, id: otherWorkspaceId, name: 'Other Council' }];
  await page.route('**/api/auth/me', (route) => route.fulfill(state.expired ? { status: 401, json: {} } : { json: {
    userId: 'synthetic-member', displayName: 'Council Member', email: 'council@example.test', systemRole: 'TenantUser',
    status: 'Active', currentWorkspace: workspace, workspaces,
  } }));
  await page.route('**/api/workspaces', (route) => route.fulfill({ json: workspaces }));
  await page.route('**/api/security/csrf-token', (route) => route.fulfill({ json: { token: 'synthetic-image-csrf', headerName: 'X-CSRF-Token' } }));
  await page.route('**/api/search**', (route) => route.fulfill(state.revoked
    ? { status: 403, json: {} }
    : { json: { items: [], page: 1, pageSize: 50, totalCount: 0 } }));
  await page.route('**/api/file-folders**', (route) => route.fulfill({ json: { workspaceId: new URL(route.request().url()).searchParams.get('workspaceId'), rootVersion: 1, folders: [] } }));
  await page.route('**/api/files**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const image = images.find((item) => url.pathname === `/api/files/${item.id}/download-grants`);
    if (image) {
      expect(request.method()).toBe('POST');
      expect(request.headers()['x-csrf-token']).toBe('synthetic-image-csrf');
      expect(['files-page-preview', 'files-page-download']).toContain(request.postDataJSON().purpose);
      state.grants.push(image.id);
      await route.fulfill(state.denied || state.expired ? { status: state.expired ? 401 : 403, json: {} } : {
        json: { fileDownloadGrantId: image.id, fileObjectId: image.id, token: 'synthetic-image-token' },
      });
    } else if (url.pathname === '/api/files' && request.method() === 'GET') {
      await route.fulfill({ json: { items: url.searchParams.get('workspaceId') === otherWorkspaceId ? [] : images.map((item) => ({
        id: item.id, fileObjectId: item.id, workspaceId, originalFileName: item.name, contentType: 'image/png', sizeBytes: item.bytes.length,
        status: 'Active', scanStatus: 'Allowed', uploadedByUserId: 'synthetic-member', uploadedByDisplayName: 'Council Member',
        createdAt: '2026-10-19T01:00:00Z', updatedAt: '2026-10-19T01:00:00Z', canDelete: false, accessState: 'Workspace', canManageSharing: false,
      })), page: 1, pageSize: 50, totalCount: url.searchParams.get('workspaceId') === otherWorkspaceId ? 0 : 2, hasMore: false } });
    } else { await route.fulfill({ status: 404 }); }
  });
  await page.route('**/api/file-download-grants/*/download', async (route) => {
    const image = images.find((item) => new URL(route.request().url()).pathname === `/api/file-download-grants/${item.id}/download`)!;
    expect(route.request().method()).toBe('POST');
    expect(route.request().headers()['x-csrf-token']).toBe('synthetic-image-csrf');
    expect(route.request().postDataJSON()).toEqual({ token: 'synthetic-image-token' });
    state.downloads.push(image.id);
    await route.fulfill({ contentType: 'image/png', body: image.bytes });
  });
  return state;
}

function syntheticPng(width: number, height: number): Buffer {
  const chunk = (name: string, data: Buffer): Buffer => {
    const type = Buffer.from(name);
    const length = Buffer.alloc(4); length.writeUInt32BE(data.length);
    let crc = 0xffffffff;
    for (const byte of Buffer.concat([type, data])) {
      crc ^= byte;
      for (let bit = 0; bit < 8; bit++) { crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
    }
    const checksum = Buffer.alloc(4); checksum.writeUInt32BE((crc ^ 0xffffffff) >>> 0);
    return Buffer.concat([length, type, data, checksum]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 2;
  const pixels = Buffer.alloc(height * (width * 3 + 1));
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const offset = y * (width * 3 + 1) + 1 + x * 3;
      pixels[offset] = x < width / 2 ? 60 : 220;
      pixels[offset + 1] = y < height / 2 ? 150 : 90;
      pixels[offset + 2] = 200;
    }
  }
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(pixels)), chunk('IEND', Buffer.alloc(0))]);
}
