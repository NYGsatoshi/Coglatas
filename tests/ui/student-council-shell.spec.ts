import { expect, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

// Diagnostic frontend evidence uses the existing synthetic static API. It is
// deliberately separate from PostgreSQL and public HTTPS pilot acceptance.
test.describe('Student council Approved Dark shell', () => {
  for (const shortcut of ['Control+k', 'Meta+k']) {
    test(`reveals and focuses existing Workspace search using ${shortcut}`, async ({ page }, testInfo) => {
      const mobile = testInfo.project.name === 'chromium-mobile';
      await page.setViewportSize({ width: mobile ? 320 : 1536, height: 800 });
      await page.goto('/app/workspaces');
      const utilities = page.getByTestId('mobile-utilities-toggle');
      const workspace = page.getByTestId('workspace-switcher');
      const search = page.getByTestId('workspace-search-input');
      await expect(page.getByTestId('top-bar')).toHaveClass(/top-bar--workspace-selected/);
      await expect(workspace).not.toHaveValue('');
      const selectedWorkspace = await workspace.inputValue();
      if (mobile) {
        await expect(utilities).toHaveAttribute('aria-expanded', 'false');
        await expect(search).toBeHidden();
        await utilities.focus();
      } else {
        await expect(utilities).toBeHidden();
        await workspace.focus();
      }
      await page.keyboard.press(shortcut);
      await expect(search).toBeFocused();
      if (mobile) await expect(utilities).toHaveAttribute('aria-expanded', 'true');
      await search.fill('Council documents');
      if (mobile) await utilities.click();
      await workspace.focus();
      await page.keyboard.press(shortcut);
      await expect(search).toBeFocused();
      await expect(search).toHaveValue('Council documents');
      await expect(workspace).toHaveValue(selectedWorkspace);
    });
  }

  for (const composition of [
    { name: 'composing Escape', isComposing: true, keyCode: 27 },
    { name: 'legacy composition Escape', isComposing: false, keyCode: 229 },
  ]) {
    test(`preserves mobile search focus and text during ${composition.name}`, async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 800 });
      await page.goto('/app/workspaces');
      const utilities = page.getByTestId('mobile-utilities-toggle');
      await utilities.click();
      const search = page.getByTestId('workspace-search-input');
      const originalSearch = await search.elementHandle();
      await search.fill('日本語の入力中');
      await search.dispatchEvent('compositionstart');
      await search.dispatchEvent('keydown', { key: 'Escape', ...composition });
      await expect(utilities).toHaveAttribute('aria-expanded', 'true');
      await expect(search).toBeFocused();
      await expect(search).toHaveValue('日本語の入力中');
      expect(await originalSearch!.evaluate((element) => element === document.querySelector('[data-testid="workspace-search-input"]'))).toBe(true);
      await search.dispatchEvent('compositionend');
      await search.press('Escape');
      await expect(utilities).toHaveAttribute('aria-expanded', 'false');
      await expect(utilities).toBeFocused();
      await utilities.press('Enter');
      await expect(search).toHaveValue('');
      expect(await originalSearch!.evaluate((element) => element === document.querySelector('[data-testid="workspace-search-input"]'))).toBe(true);
    });
  }

  test('keeps a compact mobile header with keyboard-accessible search and account actions', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 420 });
    await page.goto('/app/workspaces');
    const header = page.getByTestId('top-bar');
    const utilities = page.getByTestId('mobile-utilities-toggle');
    await expect(page.getByTestId('workspace-switcher')).toBeVisible();
    expect((await header.boundingBox())?.height).toBeLessThanOrEqual(72);
    await expect(utilities).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByTestId('workspace-search-input')).toBeHidden();
    await expect(page.getByTestId('account-action')).toBeHidden();
    await utilities.focus();
    await utilities.press('Enter');
    await expect(utilities).toHaveAttribute('aria-expanded', 'true');
    for (const id of ['workspace-search-input', 'workspace-members-action', 'right-panel-toggle', 'account-action', 'logout-action']) {
      const control = page.getByTestId(id);
      await expect(control).toBeVisible();
      expect((await control.boundingBox())?.height).toBeGreaterThanOrEqual(44);
      await control.focus();
      await expect(control).toBeFocused();
    }
    await page.keyboard.press('Escape');
    await expect(utilities).toHaveAttribute('aria-expanded', 'false');
    await expect(utilities).toBeFocused();
    expect((await header.boundingBox())?.height).toBeLessThanOrEqual(72);
    await page.route('**/api/auth/logout', (route) => route.fulfill({ status: 503, json: {} }));
    await utilities.press('Enter');
    await page.getByTestId('logout-action').click();
    await expect(page.getByTestId('logout-error')).toBeVisible();
    await utilities.focus();
    await utilities.press('Escape');
    await expect(utilities).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByTestId('logout-error')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });

  for (const width of [1536, 1366, 960, 768, 390, 320]) {
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

      if (width >= 1201) {
        const header = await page.getByTestId('top-bar').boundingBox();
        expect(header?.height).toBeLessThanOrEqual(56);
        await expect(page.locator('.feature-menu__brand img').first()).toBeVisible();
        const card = await page.getByTestId('workspace-card').first().boundingBox();
        expect(card?.height).toBeLessThan(120);
      }

      if (width <= 860) await page.getByTestId('mobile-utilities-toggle').click();
      await page.getByTestId('right-panel-toggle').click();
      await expect(page.getByTestId('right-panel')).toBeVisible();
      await expectNoAccessibilityViolations(page);
      await page.screenshot({ path: testInfo.outputPath(`approved-dark-notifications-${width}.png`) });
      if (width > 860) {
        expect(await page.getByTestId('workspace-card').first().evaluate((card) => card.scrollWidth <= card.clientWidth)).toBe(true);
        expect(await page.getByTestId('top-bar').evaluate((header) => header.scrollWidth <= header.clientWidth)).toBe(true);
      }
      await page.getByTestId('right-panel-close').click();
      await expect(page.getByTestId('right-panel-toggle')).toBeFocused();
      await expect(page.getByTestId('workspace-research-status')).toBeVisible();

      if (width <= 860) {
        await page.getByTestId('mobile-nav-toggle').click();
        await expectNoAccessibilityViolations(page);
        await page.screenshot({ path: testInfo.outputPath(`approved-dark-navigation-${width}.png`) });
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
