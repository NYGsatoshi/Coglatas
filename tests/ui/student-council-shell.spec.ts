import { expect, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

// Diagnostic frontend evidence uses the existing synthetic static API. It is
// deliberately separate from PostgreSQL and public HTTPS pilot acceptance.
test.describe('Student council Approved Dark shell', () => {
  for (const width of [1536, 1366, 768, 390, 320]) {
    test(`keeps Workspace selection and members usable at ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 1024 });
      await page.addInitScript(() => {
        localStorage.setItem('coglatas.locale', 'en');
        localStorage.removeItem('coglatas.ui.theme.v1');
      });
      await page.emulateMedia({ colorScheme: 'light' });
      await page.route('**/api/workspaces/static-workspace-1/members', (route) => route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { userId: 'synthetic-member-a', displayName: 'Pilot Member', role: 'Member', joinedAt: '2026-10-19T00:00:00Z' },
          { userId: 'synthetic-member-b', displayName: 'Pilot Owner', role: 'Owner', joinedAt: '2026-10-19T00:00:00Z' },
        ]),
      }));
      await page.goto('/app/workspaces');
      await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', 'dark');
      await expect(page.getByTestId('workspace-dashboard')).toBeVisible();
      const select = page.getByTestId('select-workspace-action').first();
      await expect(select).toBeEnabled();
      await select.focus();
      await page.keyboard.press('Enter');
      await expect(select).toHaveAttribute('aria-pressed', 'true');

      if (width <= 860) {
        await page.getByTestId('mobile-nav-toggle').click();
      }
      const navigation = page.locator(width <= 860 ? '#mobile-navigation' : '.app-shell__feature-menu');
      for (const id of ['workspaces', 'teams', 'dm', 'files']) {
        await expect(navigation.getByTestId(`nav-${id}`)).toBeVisible();
      }
      if (width <= 860) {
        await navigation.getByTestId('nav-workspaces').click();
        await expect(page.getByTestId('mobile-nav-toggle')).toHaveAttribute('aria-expanded', 'false');
      }
      await expectNoAccessibilityViolations(page);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-workspaces-${width}.png`) });

      await page.goto('/app/workspaces/static-workspace-1/members');
      await expect(page.getByTestId('workspace-members-page')).toBeVisible();
      const search = page.getByTestId('workspace-members-local-search');
      await expect(search).toBeVisible();
      await search.fill('no matching member');
      await expect(page.getByTestId('workspace-member-mobile-card')).toHaveCount(0);
      await search.fill('');
      await expectNoAccessibilityViolations(page, 'main');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-members-${width}.png`) });
    });
  }
});
