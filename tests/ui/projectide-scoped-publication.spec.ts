import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
for (const locale of ['en', 'ja'] as const) for (const truth of ['true', 'false', 'unknown', 'altered']) {
  test('verifies original scoped publication before local evidence ' + locale + ' ' + truth, async ({ page }) => {
    const raw = JSON.parse(await readFile('frontend/public/assets/projectide/scoped-publication-v2.json', 'utf8'));
    const key = 'tests-publication-' + (truth === 'altered' ? 'true' : truth) + '-base';
    if (truth === 'altered') raw.cases.find((c: { key: string }) => c.key === key).scopedSources.publication.semanticModel.text += ' ';
    await page.route('**/assets/projectide/scenario-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(raw) }));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open Scenario example' : 'シナリオの例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption(key);
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    const status = page.getByTestId('ide-evidence-status');
    await expect(status).toContainText(locale === 'en' ? 'Required evidence incomplete' : '必須証跡が不完全');
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    const load = page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true });
    await load.focus(); await load.press('Enter');
    if (truth === 'altered') {
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'Fixed sources could not be verified' : '固定ソースを検証できませんでした');
      await expect(status).toContainText(locale === 'en' ? 'Required evidence incomplete' : '必須証跡が不完全');
      await expect(page.getByTestId('ide-scoped-region')).toHaveCount(0);
    } else {
      await expect(page.getByTestId('ide-scoped-region')).toHaveCount(2);
      await expect(status).toContainText(truth === 'true' ? (locale === 'en' ? 'Complete current local PASS (synthetic only)' : '現在のローカルPASS（合成データのみ）') : truth === 'false' ? (locale === 'en' ? 'Complete evidence, but verdict is not PASS' : '証跡は完全ですが、判定はPASSではありません') : (locale === 'en' ? 'Partial or unknown coverage' : '部分的、または不明な対象範囲'));
      const foreign = page.getByTestId('ide-scoped-region').filter({ hasText: '000000ca-0000-4000-8000-000000000001' });
      await foreign.getByRole('button', { name: 'coglatas.project · 00000010-0000-4000-8000-000000000001', exact: true }).click();
      await expect(page.getByTestId('ide-scoped-source-target')).toBeFocused();
      await page.getByTestId('ide-context').selectOption('tests-publication-root-base');
      await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-region')).toHaveCount(0);
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
