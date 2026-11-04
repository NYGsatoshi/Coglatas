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
  test('keeps visible filter choices aligned with retained Problems state after panel recreation', async ({ page }) => {
    await preview(page);
    await choose(page, 'fail');
    await page.getByRole('button', { name: 'Problems', exact: true }).click();
    const severity = page.getByLabel(/^Severity/u);
    const kind = page.getByLabel(/^Diagnostic kind/u);
    await severity.selectOption('coglatas.warning');
    const rule = await kind.locator('option').nth(1).getAttribute('value');
    expect(rule).toBeTruthy();
    await kind.selectOption(rule!);
    await page.getByRole('button', { name: 'Overview', exact: true }).click();
    await page.getByRole('button', { name: 'Problems', exact: true }).click();
    await expect(severity).toHaveValue('coglatas.warning');
    await expect(kind).toHaveValue(rule!);
    await expect(page.getByTestId('projectide-workbench')).toContainText('0 of 1 diagnostics');
    await severity.selectOption('');
    await kind.selectOption('');
    await page.getByRole('button', { name: 'Overview', exact: true }).click();
    await page.getByRole('button', { name: 'Problems', exact: true }).click();
    await expect(severity).toHaveValue('');
    await expect(kind).toHaveValue('');
    await expect(page.getByTestId('projectide-workbench')).toContainText('1 of 1 diagnostics');
  });
  for (const mutation of [
    "outside-document",
    "duplicate-artifact",
    "missing-proof",
  ]) {
    test(`rejects inconsistent synthetic ${mutation} before displaying Source`, async ({
      page,
    }) => {
      await page.route(
        "**/assets/projectide/preview-v1.json",
        async (route) => {
          const response = await route.fetch();
          const body = await response.json();
          const value = body.cases[0];
          if (mutation === "outside-document") {
            value.result.diagnostics[0].primaryLocation.documentId = "absent";
          } else if (mutation === "duplicate-artifact") {
            value.result.artifacts.push(value.result.artifacts[0]);
          } else {
            value.result.rules[0].evidence.premises = [];
          }
          await route.fulfill({ json: body });
        },
      );
      await preview(page);
      await expect(page.getByTestId("projectide-workbench")).toContainText(
        mutation === "outside-document"
          ? "Scope or revision mismatch"
          : "Unsupported",
      );
      await expect(page.getByTestId("ide-context")).toHaveCount(0);
      await expect(page.getByTestId("ide-source-target")).toHaveCount(0);
    });
  }
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

test('ProjectIDE synthetic semantic inspection shows Source-bound symbols and clears across contexts', async ({ page }) => {
  await preview(page);
  await choose(page, 'pass');
  await page.getByRole('button', { name: 'Source', exact: true }).click();
  await page.getByRole('button', { name: /coglatas.project ·/u }).click();
  const inspection = page.getByTestId('ide-semantic-inspection');
  await expect(inspection).toBeVisible();
  await expect(inspection).toContainText('coglatas.project');
  await expect(inspection).toContainText('Resolved references in loaded scope: 1');
  await expect(page.getByRole('button', { name: 'Compile unavailable', exact: true })).toBeDisabled();
  await choose(page, 'branch-pass');
  await page.getByRole('button', { name: 'Inspector', exact: true }).click();
  await expect(inspection).toHaveCount(0);
});

test('ProjectIDE synthetic Diagnostic reload clears the previous Inspector and Source selection', async ({ page }) => {
  await preview(page);
  await choose(page, 'fail');
  await page.getByRole('button', { name: 'Problems', exact: true }).click();
  await page.getByRole('button', { name: /^Inspect diagnostic:/u }).click();
  await page.getByRole('button', { name: 'Go to source location', exact: true }).click();
  await expect(page.getByTestId('ide-source-target')).toBeFocused();
  const reload = page.getByRole('button', { name: 'Load result fixture', exact: true });
  await reload.focus();
  await page.keyboard.press('Enter');
  await expect(reload).toBeFocused();
  await expect(page.getByTestId('ide-source-target')).toHaveCount(0);
  await page.getByRole('button', { name: 'Inspector', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Go to source location', exact: true })).toHaveCount(0);
  await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
});

test('ProjectIDE synthetic Diagnostic Inspector refuses an invalid property location', async ({ page }) => {
  await page.route('**/assets/projectide/preview-v1.json', async (route) => {
    const response = await route.fetch();
    const body = await response.json();
    body.cases[0].result.diagnostics[0].primaryLocation.propertyPath = '/payload/missing';
    await route.fulfill({ json: body });
  });
  await preview(page);
  await choose(page, 'fail');
  await page.getByRole('button', { name: 'Problems', exact: true }).click();
  await page.getByRole('button', { name: /^Inspect diagnostic:/u }).click();
  await expect(page.getByRole('heading', { name: 'Inspector', exact: true })).toBeFocused();
  await page.getByRole('button', { name: 'Go to source location', exact: true }).click();
  await expect(page.getByTestId('ide-source-target')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Declared properties', exact: true })).toHaveCount(0);
});

test('ProjectIDE synthetic Diagnostic navigates to an exact Source-backed symbol', async ({ page }) => {
  await preview(page);
  await choose(page, 'fail');
  await page.getByRole('button', { name: 'Problems', exact: true }).click();
  await page.getByRole('button', { name: /^Inspect diagnostic:/u }).click();
  await page.getByRole('button', { name: 'Go to related symbol source', exact: true }).click();
  await expect(page.getByTestId('ide-source-target')).toBeFocused();
  await expect(page.getByTestId('ide-source-target')).toContainText('00000007-0000-4000-8000-000000000001');
  await choose(page, 'unsupported');
  await page.getByRole('button', { name: 'Problems', exact: true }).click();
  await page.getByRole('button', { name: 'Inspect diagnostic: coglatas.binding.unsupported-declaration', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Go to related symbol source', exact: true })).toHaveCount(0);
  await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
});

test('ProjectIDE synthetic local reference origin and definition have separate keyboard targets', async ({ page }) => {
  await preview(page);
  await choose(page, 'unknown');
  await page.getByRole('button', { name: 'Source', exact: true }).click();
  await page.getByRole('button', { name: /00000014-0000-4000-8000-000000000001/u }).click();
  await expect(page.getByRole('heading', { name: 'Inspector', exact: true })).toBeFocused();
  const reference = page.getByRole('button', { name: /^Reference origin.*\/target$/u });
  for (let i = 0; i < 12 && !(await reference.evaluate((el) => el === document.activeElement)); i++) {
    await page.keyboard.press('Tab');
  }
  await expect(reference).toBeFocused();
  await page.keyboard.press('Enter');
  const origin = page.getByRole('button', { name: 'Go to reference origin', exact: true });
  for (let i = 0; i < 5 && !(await origin.evaluate((el) => el === document.activeElement)); i++) {
    await page.keyboard.press('Tab');
  }
  await expect(origin).toBeFocused();
  await page.keyboard.press('Enter');
  const target = page.getByTestId('ide-source-target');
  await expect(target).toBeFocused();
  await expect(page.getByTestId('ide-source-location')).toContainText('/target');
  await page.getByRole('button', { name: 'Inspector', exact: true }).click();
  await page.getByRole('button', { name: 'Go to definition', exact: true }).click();
  await expect(target).toBeFocused();
  await expect(target).toContainText('00000006-0000-4000-8000-000000000001');
  await expect(page.getByTestId('ide-source-location')).toHaveText('/');
  await choose(page, 'stale-pass');
  await page.getByRole('button', { name: 'Source', exact: true }).click();
  await expect(target).toHaveCount(0);
  await page.getByRole('button', { name: /00000014-0000-4000-8000-000000000001/u }).click();
  await expect(page.getByRole('button', { name: 'Go to definition', exact: true })).toHaveCount(0);
  await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
});

test('ProjectIDE synthetic local reference definition is available in Japanese', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('coglatas.locale', 'ja');
    window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
  });
  await page.goto('/app/projectide/preview');
  await page.getByTestId('ide-context').selectOption('pass');
  await page.getByRole('button', { name: '結果Fixtureを読み込む', exact: true }).click();
  await page.getByRole('button', { name: 'ソース', exact: true }).click();
  await page.getByRole('button', { name: /00000014-0000-4000-8000-000000000001/u }).click();
  await page.getByRole('button', { name: /^参照元.*\/source$/u }).click();
  await page.getByRole('button', { name: '定義へ移動', exact: true }).click();
  await expect(page.getByTestId('ide-source-target')).toContainText('00000007-0000-4000-8000-000000000001');
});

test('ProjectIDE synthetic Find References navigates from an exact symbol to its origin', async ({ page }) => {
  await preview(page);
  await choose(page, 'unknown');
  await page.getByRole('button', { name: 'Source', exact: true }).click();
  await page.getByRole('button', { name: /coglatas.project ·/u }).click();
  await page.getByRole('button', { name: /^Find references · coglatas.project ·/u }).click();
  const search = page.getByTestId('ide-reference-search');
  await expect(search).toContainText('Matching navigable local references: 1');
  await expect(search).toContainText('Coverage is incomplete.');
  await page.getByRole('button', { name: /^Reference origin.*\/target$/u }).click();
  await page.getByRole('button', { name: 'Go to reference origin', exact: true }).click();
  await expect(page.getByTestId('ide-source-target')).toBeFocused();
  await expect(page.getByTestId('ide-source-location')).toContainText('/target');
  await choose(page, 'branch-pass');
  await page.getByRole('button', { name: 'Inspector', exact: true }).click();
  await expect(search).toHaveCount(0);
});

test('ProjectIDE synthetic Find References interpolates the count in Japanese', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('coglatas.locale', 'ja');
    window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
  });
  await page.goto('/app/projectide/preview');
  await page.getByTestId('ide-context').selectOption('unknown');
  await page.getByRole('button', { name: '結果Fixtureを読み込む', exact: true }).click();
  await page.getByRole('button', { name: 'ソース', exact: true }).click();
  await page.getByRole('button', { name: /coglatas.project ·/u }).click();
  await page.getByRole('button', { name: /^参照を検索 · coglatas.project ·/u }).click();
  const search = page.getByTestId('ide-reference-search');
  await expect(search).toContainText('移動可能なローカル参照: 1 件');
  await expect(search).toContainText('参照の取得範囲は不完全です。');
});
