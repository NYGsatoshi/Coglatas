import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

async function preview(page: Page, enabled = true) {
  await page.addInitScript((enabled) => {
    localStorage.setItem('coglatas.locale', 'en');
    window.__COGLATAS_FEATURE_FLAGS__ = {
      'projectide.syntheticPreview': enabled,
    };
  }, enabled);
  await page.goto('/app/projectide/preview');
}
async function choose(page: Page, key: string) {
  await page.getByTestId('ide-context').selectOption(key);
  await page
    .getByRole('button', { name: 'Load result fixture', exact: true })
    .click();
}

test.describe('ProjectIDE synthetic read-only workbench', () => {
  test('completes context, Source, Problems, Inspector and source navigation in a real browser', async ({
    page,
  }, info) => {
    const requests: string[] = [];
    page.on('request', (r) => {
      if (new URL(r.url()).pathname.startsWith('/api/'))
        requests.push(`${r.method()} ${new URL(r.url()).pathname}`);
    });
    await preview(page);
    await page.getByTestId('ide-context').selectOption('fail');
    const workbench = page.getByTestId('projectide-workbench');
    await expect(workbench).toContainText('Synthetic fixture preview');
    await expect(page.getByTestId('ide-analysis-status')).toContainText(
      'Compiler execution: NotRun',
    );
    await page.getByRole('button', { name: 'Source', exact: true }).click();
    await expect(page.locator('.source-buffer')).toContainText('Observe 🌌');
    await page
      .getByRole('button', { name: 'Load result fixture', exact: true })
      .click();
    await page.getByRole('button', { name: 'Problems', exact: true }).click();
    await expect(workbench).toContainText('1 of 1 diagnostics');
    await page
      .getByRole('button', {
        name: 'Inspect diagnostic: coglatas.analysis.constraint-false',
        exact: true,
      })
      .click();
    await expect(
      page.getByRole('heading', { name: 'Inspector', exact: true }),
    ).toBeFocused();
    await expect(workbench).toContainText('coglatas.constraint');
    await expect(workbench).toContainText('Launch readiness');
    await page.screenshot({
      path: info.outputPath('inspector.png'),
      fullPage: true,
    });
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    await page
      .getByRole('button', { name: 'Go to source location', exact: true })
      .click();
    await expect(page.getByTestId('ide-source-target')).toContainText(
      '00000007-0000-4000-8000-000000000001',
    );
    await expect(page.getByTestId('ide-source-target')).toBeFocused();
    await page.screenshot({
      path: info.outputPath('source-navigation.png'),
      fullPage: true,
    });
    await info.attach('source-navigation', {
      path: info.outputPath('source-navigation.png'),
      contentType: 'image/png',
    });
    expect(requests.filter((r) => !r.startsWith('GET '))).toEqual([]);
    expect(
      requests.some((r) => /compiler|projectide|\/api\/projects\//u.test(r)),
    ).toBe(false);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
  });
  test('drops prior revision results, selection and source targets', async ({
    page,
  }) => {
    await preview(page);
    await choose(page, 'fail');
    await page.getByRole('button', { name: 'Problems', exact: true }).click();
    await page.getByRole('button', { name: /^Inspect diagnostic:/u }).click();
    await page.getByTestId('ide-context').selectOption('stale-pass');
    await expect(page.getByTestId('ide-analysis-status')).toContainText(
      'Compiler execution: NotRun',
    );
    await page.getByRole('button', { name: 'Inspector', exact: true }).click();
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'Select a diagnostic',
    );
    await expect(
      page.getByRole('button', { name: 'Go to source location', exact: true }),
    ).toHaveCount(0);
  });
  test('keeps stale PASS visible and blocks source navigation', async ({
    page,
  }) => {
    await preview(page);
    await choose(page, 'stale-pass');
    await expect(page.getByTestId('ide-analysis-status')).toContainText(
      'Stale result',
    );
    await expect(page.getByTestId('ide-analysis-status')).not.toContainText(
      'Complete current PASS',
    );
    await page.getByRole('button', { name: 'Source', exact: true }).click();
    await page
      .getByRole('button', {
        name: 'coglatas.project · 00000006-0000-4000-8000-000000000001',
        exact: true,
      })
      .click();
    await page
      .getByRole('button', { name: 'Go to source location', exact: true })
      .click();
    await expect(
      page.locator('app-projectide-workbench [role="alert"]'),
    ).toContainText('Stale result');
    await expect(page.getByTestId('ide-source-target')).toHaveCount(0);
  });
  test('distinguishes unknown, unsupported and zero unexecuted findings', async ({
    page,
  }) => {
    await preview(page);
    for (const key of [
      'unknown',
      'unsupported',
      'not-run',
      'cancelled',
      'timeout',
    ]) {
      await choose(page, key);
      await page.getByRole('button', { name: 'Problems', exact: true }).click();
      await expect(page.getByTestId('ide-analysis-status')).toContainText(
        'Analysis is incomplete',
      );
      await expect(page.getByTestId('ide-analysis-status')).not.toContainText(
        'Complete current PASS',
      );
    }
  });
  test('keeps preview disabled and avoids fetching artifacts without explicit opt-in', async ({
    page,
  }) => {
    let reads = 0;
    page.on('request', (r) => {
      if (r.url().endsWith('/projectide/preview-v1.json')) reads++;
    });
    await preview(page, false);
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'preview is disabled',
    );
    expect(reads).toBe(0);
  });
  test('rejects an unauthorized context read without exposing cached content', async ({
    page,
  }) => {
    await page.route('**/assets/projectide/preview-v1.json', (r) =>
      r.fulfill({ status: 403, body: 'private details' }),
    );
    await preview(page);
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'denied',
    );
    await expect(page.getByTestId('projectide-workbench')).not.toContainText(
      'private details',
    );
    await expect(page.getByTestId('ide-context')).toHaveCount(0);
  });
  test('rejects wrong-scope artifact diagnostics before rendering context', async ({
    page,
  }) => {
    await page.route('**/assets/projectide/preview-v1.json', async (route) => {
      const response = await route.fetch();
      const body = await response.json();
      body.cases[0].result.diagnostics[0].primaryLocation.context.branch.tenantId =
        'wrong-tenant';
      await route.fulfill({ json: body });
    });
    await preview(page);
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'Scope or revision mismatch',
    );
    await expect(page.getByTestId('ide-context')).toHaveCount(0);
  });
  test('recovers from an unavailable read without reusing an earlier result', async ({
    page,
  }) => {
    let fail = true;
    await page.route('**/assets/projectide/preview-v1.json', (route) =>
      fail ? route.fulfill({ status: 503, body: 'private' }) : route.continue(),
    );
    await preview(page);
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'Preview could not be loaded',
    );
    fail = false;
    await page
      .getByRole('button', { name: 'Reload preview', exact: true })
      .click();
    await choose(page, 'pass');
    await expect(page.getByTestId('ide-analysis-status')).toContainText(
      'Complete current PASS fixture',
    );
  });
  test('rejects malformed nested artifact data before navigation can render it', async ({
    page,
  }) => {
    await page.route('**/assets/projectide/preview-v1.json', async (route) => {
      const response = await route.fetch();
      const body = await response.json();
      delete body.cases[0].sourceMap.nodes;
      await route.fulfill({ json: body });
    });
    await preview(page);
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'Unsupported preview contract version or value',
    );
    await expect(page.getByTestId('ide-context')).toHaveCount(0);
  });
  test('cancels loading and discards a delayed artifact', async ({ page }) => {
    let release!: () => void;
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route('**/assets/projectide/preview-v1.json', async (route) => {
      await held;
      await route.continue().catch(() => undefined);
    });
    await preview(page);
    await page
      .getByRole('button', { name: 'Cancel loading', exact: true })
      .click();
    release();
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'Preview loading cancelled',
    );
    await expect(page.getByTestId('ide-context')).toHaveCount(0);
  });
  test('does not admit an unauthenticated route', async ({ page }) => {
    await page.route('**/api/auth/me', (r) =>
      r.fulfill({ status: 401, body: '{}' }),
    );
    await page.route('**/api/auth/status', (r) =>
      r.fulfill({
        status: 200,
        contentType: 'application/json',
        body: '{"isAuthenticated":false}',
      }),
    );
    await preview(page);
    await expect(page).toHaveURL(/\/app\/login/u);
    await expect(page.getByTestId('projectide-workbench')).toHaveCount(0);
  });
  test('supports keyboard-only context, Problems, Inspector and navigation', async ({
    page,
  }) => {
    await preview(page);
    const context = page.getByTestId('ide-context');
    await context.focus();
    await page.keyboard.press('ArrowDown');
    await expect(context).toHaveValue('fail');
    const load = page.getByRole('button', {
      name: 'Load result fixture',
      exact: true,
    });
    await page.keyboard.press('Tab');
    await expect(load).toBeFocused();
    await page.keyboard.press('Enter');
    for (let i = 0; i < 3; i++) await page.keyboard.press('Tab');
    await expect(
      page.getByRole('button', { name: 'Problems', exact: true }),
    ).toBeFocused();
    await page.keyboard.press('Enter');
    const inspect = page.getByRole('button', { name: /^Inspect diagnostic:/u });
    for (
      let i = 0;
      i < 6 && !(await inspect.evaluate((el) => el === document.activeElement));
      i++
    )
      await page.keyboard.press('Tab');
    await expect(inspect).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(
      page.getByRole('heading', { name: 'Inspector', exact: true }),
    ).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(
      page.getByRole('button', { name: 'Close Inspector', exact: true }),
    ).toBeFocused();
    const go = page.getByRole('button', {
      name: 'Go to source location',
      exact: true,
    });
    for (
      let i = 0;
      i < 8 && !(await go.evaluate((el) => el === document.activeElement));
      i++
    ) {
      await page.keyboard.press('Tab');
    }
    await expect(go).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ide-source-target')).toBeVisible();
  });
  test('renders Japanese copy while retaining semantic diagnostic codes', async ({
    page,
  }) => {
    await page.addInitScript(() => {
      localStorage.setItem('coglatas.locale', 'ja');
      window.__COGLATAS_FEATURE_FLAGS__ = {
        'projectide.syntheticPreview': true,
      };
    });
    await page.goto('/app/projectide/preview');
    await page.getByTestId('ide-context').selectOption('fail');
    await page
      .getByRole('button', { name: '結果Fixtureを読み込む', exact: true })
      .click();
    await page.getByRole('button', { name: '問題', exact: true }).click();
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      '合成Fixtureプレビュー',
    );
    await expect(page.getByTestId('projectide-workbench')).toContainText(
      'coglatas.analysis.constraint-false',
    );
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
  });
});
