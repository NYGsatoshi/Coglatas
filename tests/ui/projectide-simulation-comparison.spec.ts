import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash = (text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update('coglatas.local-simulation-comparison/1' + '\0' + text).digest('hex') });
for (const locale of ['en', 'ja'] as const) for (const family of ['root', 'external', 'bounded']) {
  test('compares both immutable Core recordings with keyboard in ' + locale + ' ' + family, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-comparison-v1.json', 'utf8'));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open run comparison example' : '実行結果の比較例を開く', exact: true }).click();
    for (const revision of ['base', 'hypothesis']) {
      const value = bundle.cases.find((item: { key: string }) => item.key === 'tests-simulation-' + family + '-' + revision);
      const expected = JSON.parse(value.simulationComparison.text); await page.getByTestId('ide-context').selectOption(value.key);
      await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
      await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
      await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
      await expect(page.getByTestId('ide-scoped-region')).toHaveCount(family === 'root' ? 1 : 2);
      await page.getByRole('button', { name: locale === 'en' ? 'Load recorded simulation' : '記録されたシミュレーションを読み込む', exact: true }).click();
      const load = page.getByRole('button', { name: locale === 'en' ? 'Compare recorded runs' : '記録済みの実行結果を比較', exact: true });
      await load.focus(); await load.press('Enter'); const panel = page.getByTestId('ide-simulation-comparison'); await expect(panel).toBeFocused();
      await expect(page.getByTestId('ide-comparison-before')).toContainText(expected.before.runId);
      await expect(page.getByTestId('ide-comparison-before')).toContainText(expected.before.inputDigest.value);
      await expect(page.getByTestId('ide-comparison-after')).toContainText(expected.after.runId);
      await expect(page.getByTestId('ide-comparison-after')).toContainText(expected.after.inputDigest.value);
      await expect(page.getByTestId('ide-context')).toHaveValue(value.key);
      await expect(page.getByTestId('ide-comparison-delta')).toContainText(family === 'bounded' ? (locale === 'en' ? 'Unavailable for incomplete recordings' : '不完全な記録のため表示できません') : ': 0');
      await expect(page.getByTestId('ide-comparison-state')).toContainText(family === 'bounded' ? (locale === 'en' ? 'At least one recording is incomplete' : '不完全な実行記録を含む') : (locale === 'en' ? 'Both recordings are complete' : '両方の実行記録が完了'));
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
      await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
      await expect(panel).toHaveCount(0); await expect(page.getByTestId('ide-simulation')).toHaveCount(0);
    }
    await page.getByTestId('ide-context').selectOption('tests-simulation-unknown-base');
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
    await expect(page.getByRole('button', { name: locale === 'en' ? 'Compare recorded runs' : '記録済みの実行結果を比較', exact: true })).toHaveCount(0); expect(writes).toEqual([]);
  });
}
for (const locale of ['en', 'ja'] as const) for (const mode of ['bytes', 'after-input', 'false-delta', 'false-complete']) {
  test('refuses the altered two-recording comparison ' + mode + ' in ' + locale, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-comparison-v1.json', 'utf8'));
    const family = mode === 'false-complete' ? 'bounded' : 'root'; const value = bundle.cases.find((item: { key: string }) => item.key === 'tests-simulation-' + family + '-base');
    if (mode === 'bytes') value.simulationComparison.text += ' ';
    if (mode === 'after-input') { const after = bundle.cases.find((item: { key: string }) => item.key === value.simulationComparison.afterCaseKey); after.simulationRun = value.simulationRun; }
    if (mode === 'false-delta' || mode === 'false-complete') { const data = JSON.parse(value.simulationComparison.text); data.observedElapsedDelta = '1';
      if (mode === 'false-complete') { data.state = 'Complete'; data.reason = 'coglatas.comparison.complete-recordings'; }
      value.simulationComparison.text = JSON.stringify(data); value.simulationComparison.digest = hash(value.simulationComparison.text);
    }
    await page.route('**/assets/projectide/simulation-comparison-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bundle) }));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open run comparison example' : '実行結果の比較例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption(value.key);
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-scoped-region')).toHaveCount(family === 'root' ? 1 : 2);
    await page.getByRole('button', { name: locale === 'en' ? 'Load recorded simulation' : '記録されたシミュレーションを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Compare recorded runs' : '記録済みの実行結果を比較', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'Both recordings and their comparison could not be verified' : '両方の実行記録と比較結果を検証できませんでした');
    await expect(page.getByTestId('ide-simulation-comparison')).toHaveCount(0); await expect(page.getByTestId('ide-simulation')).toBeVisible();
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
