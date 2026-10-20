import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

const conversationId = '77777777-7777-4777-8777-777777777777';
const workspaceId = 'static-workspace-1';
const lastMessageId = '22222222-2222-4222-8222-222222222222';

test.describe('Explicit loaded-message read command', () => {
  for (const width of [1536, 1366, 768, 390, 320]) {
    test(`marks a Group read with keyboard and authoritative counts at ${width}px`, async ({ page }, testInfo) => {
      const control = await installReadApi(page, 'Group');
      await page.setViewportSize({ width, height: 1024 });
      await page.goto(`/app/dm/${conversationId}`);
      const button = page.getByTestId('conversation-mark-read');
      await expect(button).toBeEnabled();
      expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      await button.focus(); await page.keyboard.press('Enter');
      await expect(page.getByTestId('conversation-mark-read-status')).toContainText('Loaded messages marked as read');
      expect(control.reads).toEqual([{ lastReadMessageId: lastMessageId }]);
      await expect(page.getByTestId('confirmed-message')).toHaveCount(2);
      await expectNoAccessibilityViolations(page, '.dm-page__header');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`group-mark-read-${width}.png`) });
      await page.goto('/app/dm');
      const row = page.getByTestId('conversation-list-item').filter({ hasText: 'Unread example' });
      await expect(row).toBeVisible();
      await expect(row.getByTestId('conversation-unread-badge')).toHaveCount(0);
    });
  }

  for (const type of ['DirectMessage', 'WorkspaceGeneral']) {
    test(`offers the same read command to a readable ${type} participant at 320px`, async ({ page }) => {
      const control = await installReadApi(page, type);
      await page.setViewportSize({ width: 320, height: 800 });
      await page.goto(type === 'DirectMessage' ? `/app/dm/${conversationId}` : `/app/workspaces/${workspaceId}/channels/${conversationId}`);
      await page.getByTestId('conversation-mark-read').click();
      await expect(page.getByTestId('conversation-mark-read-status')).toHaveAttribute('role', 'status');
      expect(control.reads).toEqual([{ lastReadMessageId: lastMessageId }]);
      await expect(page.getByTestId('confirmed-message')).toHaveCount(2);
    });
  }

  test('keeps unknown failure truthful and clears protected content after a masked denial', async ({ page }) => {
    const control = await installReadApi(page, 'Group'); control.status = 503;
    await page.setViewportSize({ width: 320, height: 800 });
    await page.goto(`/app/dm/${conversationId}`);
    await page.getByTestId('conversation-mark-read').click();
    await expect(page.getByTestId('conversation-mark-read-status')).toHaveAttribute('role', 'alert');
    await expect(page.getByTestId('conversation-mark-read-status')).toContainText('could not be confirmed');
    await expect(page.locator('body')).not.toContainText('Private raw failure');
    expect(control.unread).toBe(2); expect(control.reads).toHaveLength(1);
    control.status = 400;
    await page.getByTestId('conversation-mark-read').click();
    await expect(page.getByTestId('conversation-mark-read')).toHaveCount(0);
    await expect(page.getByTestId('confirmed-message')).toHaveCount(0);
    await expect(page.getByTestId('group-members-panel')).toHaveCount(0);
    await expect(page.getByTestId('conversation-mark-read-status')).toHaveCount(0);
  });

  test('prevents duplicate keyboard commands and drops late feedback after route change', async ({ page }) => {
    const control = await installReadApi(page, 'Group'); control.hold = true;
    try {
      await page.goto(`/app/dm/${conversationId}`);
      const button = page.getByTestId('conversation-mark-read');
      await button.focus(); await page.keyboard.press('Enter');
      await expect(button).toBeDisabled();
      await page.keyboard.press('Enter');
      expect(control.reads).toHaveLength(1);
      await page.goto('/app/dm');
      control.release();
      await expect(page.getByTestId('conversation-mark-read-status')).toHaveCount(0);
      await expect(page.getByTestId('confirmed-message')).toHaveCount(0);
    } finally { control.release(); }
  });
});

async function installReadApi(page: Page, type: string) {
  let release = () => {};
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const control = { unread: 2, status: 200, hold: false, reads: [] as unknown[], release: () => release() };
  await page.addInitScript(() => { localStorage.setItem('coglatas.locale', 'en'); localStorage.setItem('coglatas.ui.theme.v1', 'dark'); });
  await page.route('**/api/**', async (route) => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (path === '/api/security/csrf-token') { await route.fulfill({ json: { token: 'csrf-read-ui', headerName: 'X-CSRF-Token' } }); return; }
    if (path === '/api/conversations') {
      await route.fulfill({ json: { items: [{ id: conversationId, workspaceId, type, title: 'Unread example', unreadCount: control.unread }],
        page: 1, pageSize: 20, totalCount: 1, view: 'All', counts: { all: 1, unread: control.unread > 0 ? 1 : 0, mentions: 0, later: 0 } } }); return;
    }
    if (path === `/api/conversations/${conversationId}`) {
      await route.fulfill({ json: { id: conversationId, workspaceId, type, title: 'Unread example', members: [
        { userId: 'mock-user-a', displayName: 'Current reader', role: 'ReadOnly', canRead: true, canPost: false },
        { userId: 'peer', displayName: 'Peer', role: 'Member', canRead: true, canPost: true }
      ] } }); return;
    }
    if (path === `/api/conversations/${conversationId}/messages`) {
      await route.fulfill({ json: { items: [
        { id: lastMessageId, body: 'Newer loaded body', authorUserId: 'peer', createdAt: '2026-10-20T00:00:00.0000009+00:00' },
        { id: '11111111-1111-4111-8111-111111111111', body: 'Older loaded body', authorUserId: 'peer', createdAt: '2026-10-20T00:00:00.0000001Z' }
      ] } }); return;
    }
    if (path === `/api/conversations/${conversationId}/read` && request.method() === 'POST') {
      expect(request.headers()['x-csrf-token']).toBe('csrf-read-ui');
      control.reads.push(request.postDataJSON());
      if (control.hold) { await gate; }
      if (control.status !== 200) { await route.fulfill({ status: control.status, json: { error: 'Private raw failure' } }); return; }
      control.unread = 0; await route.fulfill({ json: {} }); return;
    }
    await route.fallback();
  });
  return control;
}
