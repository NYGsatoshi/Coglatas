import { expect, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = 'static-workspace-1';
const memberId = 'synthetic-member-detail-a';
const membersPath = `/api/workspaces/${workspaceId}/members`;

test.describe('Ordinary Workspace member details', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('coglatas.locale', 'en'));
    await page.route(`**${membersPath}`, (route) => route.fulfill({
      status: 200, contentType: 'application/json',
      body: JSON.stringify([{ userId: memberId, displayName: 'List member', role: 'Owner', status: 1 }]),
    }));
  });

  for (const width of [1536, 1366, 768, 390, 320]) {
    test(`reads fresh minimal details with keyboard and bounded layout at ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 1024 });
      const reads: string[] = [];
      await page.route(`**${membersPath}/*`, (route) => {
        reads.push(new URL(route.request().url()).pathname);
        return route.fulfill({
          status: 200, contentType: 'application/json',
          body: JSON.stringify({ userId: memberId, displayName: 'Fresh ordinary member', role: 'ReadOnly', status: 1 }),
        });
      });
      await page.goto(`/app/workspaces/${workspaceId}/members`);
      const trigger = page.getByRole('button', { name: 'Member details', exact: true });
      await expect(trigger).toHaveCount(1);
      await trigger.focus();
      await page.keyboard.press('Enter');
      const panel = page.getByTestId('workspace-member-detail');
      await expect(panel).toBeFocused();
      await expect(panel.getByTestId('workspace-member-detail-name')).toHaveText('Fresh ordinary member');
      await expect(panel.getByTestId('workspace-member-detail-role')).toHaveText('Read only');
      await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
      expect(reads).toEqual([`${membersPath}/${memberId}`]);
      await expect(panel).not.toContainText(/email|capability|joined|manage members/i);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await expectNoAccessibilityViolations(page, 'main');
      await page.screenshot({ path: testInfo.outputPath(`ordinary-member-details-${width}.png`) });
      await page.keyboard.press('Tab');
      await expect(page.getByTestId('workspace-member-detail-close')).toBeFocused();
      await page.keyboard.press('Escape');
      await expect(panel).toHaveCount(0);
      expect(await page.getByTestId('workspace-members-page').evaluate((element) => element.contains(document.activeElement))).toBe(true);
    });
  }

  test('clears old detail before failure, retries only a GET, and shows no denied body', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 420 });
    let mode: 'ready' | 'error' | 'denied' = 'ready';
    const requests: string[] = [];
    await page.route(`**${membersPath}/${memberId}`, (route) => {
      requests.push(route.request().method());
      return route.fulfill({ status: mode === 'ready' ? 200 : mode === 'error' ? 500 : 404,
        contentType: 'application/json', body: JSON.stringify({ userId: memberId, displayName: mode === 'ready' ? 'Current authorized detail' : 'Denied response name', role: 'Owner' }),
      });
    });
    await page.goto(`/app/workspaces/${workspaceId}/members`);
    const trigger = page.getByRole('button', { name: 'Member details', exact: true });
    await trigger.click();
    await expect(page.getByTestId('workspace-member-detail-name')).toHaveText('Current authorized detail');
    await page.getByTestId('workspace-member-detail-close').click();
    mode = 'error';
    await trigger.click();
    await expect(page.getByTestId('workspace-member-detail-name')).toHaveCount(0);
    const retry = page.getByTestId('workspace-member-detail-retry');
    await expect(retry).toBeVisible();
    mode = 'ready';
    await retry.click();
    await expect(page.getByTestId('workspace-member-detail-name')).toHaveText('Current authorized detail');
    await page.getByTestId('workspace-member-detail-close').click();
    mode = 'denied';
    await trigger.click();
    await expect(page.getByTestId('workspace-member-detail')).toContainText('Member details are unavailable or access has changed.');
    await expect(page.getByTestId('workspace-member-detail-name')).toHaveCount(0);
    await expect(page.getByTestId('workspace-member-detail')).not.toContainText('Denied response name');
    expect(requests).toEqual(['GET', 'GET', 'GET', 'GET']);
    await page.getByTestId('workspace-member-detail-close').click();
    await expect(page.getByTestId('workspace-member-detail')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
});
