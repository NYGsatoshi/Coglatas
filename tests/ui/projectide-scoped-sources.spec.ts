import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
for (const locale of ['en', 'ja'] as const) for (const altered of [false, true]) {
  test('reads original fixed dependency versions with explicit admission ' + locale + ' altered=' + altered, async ({ page }) => {
    const raw = JSON.parse(await readFile('frontend/public/assets/projectide/scoped-sources-v1.json', 'utf8'));
    if (altered) raw.cases[0].scopedSources.sourceMap.text += ' ';
    await page.route('**/assets/projectide/scenario-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(raw) }));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open Scenario example' : 'シナリオの例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption('tests-scoped-base');
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    const load = page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true });
    await load.focus(); await load.press('Enter');
    if (altered) {
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'Fixed sources could not be verified' : '固定ソースを検証できませんでした');
      await expect(page.getByTestId('ide-scoped-region')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
    } else {
      await expect(page.getByTestId('ide-scoped-region')).toHaveCount(3);
      const versions = page.getByTestId('ide-scoped-region').filter({ hasText: '000000ca-0000-4000-8000-000000000001' });
      await expect(versions).toHaveCount(2);
      await versions.nth(0).getByRole('button', { name: 'coglatas.analysis.constraint-false', exact: true }).click();
      await expect(page.getByTestId('ide-scoped-source-target')).toContainText('00000011-0000-4000-8000-000000000001');
      await expect(page.getByTestId('ide-scoped-source-target')).toBeFocused();
      const rootIdentity = '00000006-0000-4000-8000-000000000001';
      await versions.nth(0).getByRole('button', { name: 'coglatas.project · ' + rootIdentity, exact: true }).click();
      await expect(page.getByTestId('ide-scoped-source-target')).toContainText('000000cc-0000-4000-8000-000000000001');
      await versions.nth(1).getByRole('button', { name: 'coglatas.project · ' + rootIdentity, exact: true }).click();
      await expect(page.getByTestId('ide-scoped-source-target')).toContainText('000000cd-0000-4000-8000-000000000001');
      await expect(page.getByTestId('ide-scoped-source-target')).toContainText('Second captured revision');
      await expect(page.getByTestId('ide-scoped-source-target')).toBeFocused();
      await page.getByTestId('ide-context').selectOption('tests-scoped-hypothesis');
      await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-region')).toHaveCount(0);
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
