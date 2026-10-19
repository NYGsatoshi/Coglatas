import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

const themeStorageKey = 'coglatas.ui.theme.v1';

test.describe('Files sharing mainline regression coverage', () => {
  test('keeps server-authorized File sharing state visible and reconciled at 320px in both themes', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    await page.addInitScript((storageKey) => globalThis.localStorage.setItem(storageKey, 'light'), themeStorageKey);

    const workspaceId = 'static-workspace-1';
    const fileObjectId = '36000000-0000-4000-8000-000000000002';
    const csrfToken = 'csrf-file-sharing-mainline';
    let csrfRequests = 0;
    let revokeRequests = 0;
    let revoked = false;
    const sharingSnapshot = () => ({
      fileObjectId,
      sharingPolicy: 'Private',
      accessState: revoked ? 'Private' : 'External',
      externalRecipientCount: revoked ? 0 : 1,
      canManageSharing: true,
      canInspectSharing: true,
      sharingVersion: revoked ? 2 : 1,
      recipients: revoked ? [] : [{
        grantId: '36000000-0000-4000-8000-000000000004',
        displayName: 'External Project Member',
        accessKind: 'ExternalProjectMember',
      }],
      availableRecipients: [],
    });
    await page.route('**/api/security/csrf-token', async (route) => {
      expect(route.request().method()).toBe('GET');
      csrfRequests += 1;
      await route.fulfill({ json: { token: csrfToken, headerName: 'X-CSRF-Token' } });
    });

    await page.route('**/api/files**', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (request.method() === 'GET' && url.pathname === '/api/files') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            items: [{
              id: '36000000-0000-4000-8000-000000000003',
              fileObjectId,
              workspaceId,
              originalFileName: 'sharing-evidence.zip',
              contentType: 'application/zip',
              sizeBytes: 4096,
              status: 'Active',
              scanStatus: 'Clean',
              uploadedByUserId: 'mock-user-a',
              uploadedByDisplayName: 'Mock User A',
              createdAt: '2026-08-30T02:00:00Z',
              updatedAt: '2026-08-30T03:30:00Z',
              canDelete: false,
              accessState: sharingSnapshot().accessState,
              externalRecipientCount: sharingSnapshot().externalRecipientCount,
              canManageSharing: true,
              sharingVersion: sharingSnapshot().sharingVersion,
            }],
            page: 1,
            pageSize: 20,
            totalCount: 1,
            hasMore: false,
          }),
        });
        return;
      }

      if (request.method() === 'GET' && url.pathname === `/api/files/${fileObjectId}/sharing`) {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify(sharingSnapshot()),
        });
        return;
      }

      if (
        request.method() === 'DELETE' &&
        url.pathname === `/api/files/${fileObjectId}/sharing/recipients/36000000-0000-4000-8000-000000000004`
      ) {
        expect(url.searchParams.get('expectedSharingVersion')).toBe('1');
        expect(request.headers()['x-csrf-token']).toBe(csrfToken);
        expect(revokeRequests).toBe(0);
        revokeRequests += 1;
        revoked = true;
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify(sharingSnapshot()),
        });
        return;
      }

      await route.fulfill({ status: 404 });
    });

    await page.goto('/app/files');
    const previewAction = page.getByRole('button', { name: 'Preview sharing-evidence.zip' });
    await expect(previewAction).toBeVisible();
    await expect(page.getByTestId('file-access-state')).toContainText('External');
    await expect(page.getByTestId('file-access-state')).toContainText('1 people');
    await previewAction.focus();
    await page.keyboard.press('Enter');

    const inspector = page.getByTestId('files-preview-pane');
    const previewAccess = inspector.getByTestId('files-preview-access-state');
    await expect(previewAccess).toContainText('External');
    await expect(previewAccess).toContainText('1 people');
    const manageSharing = inspector.getByTestId('files-preview-manage-sharing');
    await manageSharing.focus();
    await page.keyboard.press('Enter');

    const dialog = page.getByRole('dialog', { name: 'Manage file sharing' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByTestId('files-sharing-dialog-content')).toContainText('External');
    await expect(dialog).toContainText('External Project Member');
    await expectNoAccessibilityViolations(page, '[role="dialog"]');

    await dialog.getByTestId('files-sharing-revoke').click();
    await expect(previewAccess).toHaveText('Private');
    await expect(page.getByTestId('file-access-state')).toHaveText('Private');
    expect(csrfRequests).toBe(1);
    expect(revokeRequests).toBe(1);

    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(manageSharing).toBeFocused();

    await inspector.getByTestId('files-preview-close').click();
    await expect(previewAction).toBeFocused();
    await page.getByTestId('mobile-utilities-toggle').click();
    await page.getByTestId('theme-toggle').click();
    await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', 'dark');
    await previewAction.focus();
    await page.keyboard.press('Enter');
    await expect(previewAccess).toBeVisible();
    await expect(previewAccess).toHaveText('Private');
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
    await page.reload();
    await expect(page.getByTestId('file-access-state')).toHaveText('Private');
    await previewAction.click();
    await expect(previewAccess).toHaveText('Private');
    expect(revokeRequests).toBe(1);
  });
});

async function expectNoDocumentHorizontalOverflow(page: import('@playwright/test').Page): Promise<void> {
  const sizes = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(sizes.scrollWidth).toBeLessThanOrEqual(sizes.clientWidth);
}
