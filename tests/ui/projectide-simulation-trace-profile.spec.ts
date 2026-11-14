import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash = (text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update('coglatas.local-simulation-trace-profile/1' + '\0' + text).digest('hex') });
async function openExample(page: Page, locale: 'en' | 'ja') {
  await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
  await page.goto('/app/projectide/preview');
  await page.getByRole('button', { name: locale === 'en' ? 'Open model trace summary example' : 'モデル記録の集計例を開く', exact: true }).click();
}
async function readRun(page: Page, locale: 'en' | 'ja', key: string, regions: number) {
  await page.getByTestId('ide-context').selectOption(key);
  await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
  await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
  await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
  await expect(page.getByTestId('ide-scoped-region')).toHaveCount(regions);
  await page.getByRole('button', { name: locale === 'en' ? 'Load recorded simulation' : '記録されたシミュレーションを読み込む', exact: true }).click();
}
for (const locale of ['en', 'ja'] as const) for (const family of ['root', 'external', 'bounded']) {
  test('reads actual model trace metrics and definitions by keyboard in ' + locale + ' ' + family, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-profile-v1.json', 'utf8'));
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await openExample(page, locale);
    for (const revision of ['base', 'hypothesis', 'draft']) {
      const value = bundle.cases.find((item: { key: string }) => item.key === 'tests-simulation-' + family + '-' + revision);
      const expected = JSON.parse(value.simulationTraceProfile.text);
      await readRun(page, locale, value.key, family === 'root' ? 1 : 2);
      const load = page.getByRole('button', { name: locale === 'en' ? 'Read model trace summary' : 'モデル記録の集計を読む', exact: true });
      await load.focus(); await load.press('Enter'); const panel = page.getByTestId('ide-trace-profile'); await expect(panel).toBeFocused();
      for (const [key, count] of Object.entries(expected.metrics)) await expect(page.getByTestId('ide-profile-' + key).locator('dd')).toContainText(String(count));
      await expect(panel).toContainText(expected.basis.runId); await expect(panel).toContainText(expected.basis.runDigest.value);
      await expect(panel).toContainText(expected.period.start + ' → ' + expected.period.through);
      await expect(panel).toContainText(locale === 'en' ? 'Real operational observations are unavailable here' : '実運用の観測結果はありません');
      await expect(page.getByTestId('ide-profile-state')).toContainText(family === 'bounded' ? (locale === 'en' ? 'Recorded prefix only' : '記録済み区間のみ') : (locale === 'en' ? 'Complete model recording' : '完了したモデル記録'));
      await expect(page.getByTestId('ide-profile-recordedBusyTicks')).toContainText(locale === 'en' ? 'Overlapping Tasks count once' : '複数タスクが重なる区間も一度だけ数えます');
      await expect(page.getByTestId('ide-profile-recordedTaskTicks')).toContainText(locale === 'en' ? 'Each overlapping Task counts separately' : '重なるタスクを別々に数え');
      await expect(page.getByTestId('ide-context')).toHaveValue(value.key); await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
      await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
      await expect(panel).toHaveCount(0); await expect(page.getByTestId('ide-simulation')).toHaveCount(0);
    }
    await page.getByTestId('ide-context').selectOption('tests-simulation-unknown-base');
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
    await expect(page.getByRole('button', { name: locale === 'en' ? 'Read model trace summary' : 'モデル記録の集計を読む', exact: true })).toHaveCount(0); expect(writes).toEqual([]);
  });
}
for (const locale of ['en', 'ja'] as const) for (const mode of ['bytes', 'metric', 'false-complete', 'observed']) {
  test('refuses altered model trace profile ' + mode + ' in ' + locale, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-profile-v1.json', 'utf8'));
    const family = mode === 'false-complete' ? 'bounded' : 'external';
    const value = bundle.cases.find((item: { key: string }) => item.key === 'tests-simulation-' + family + '-base');
    if (mode === 'bytes') value.simulationTraceProfile.text += ' ';
    else {
      const data = JSON.parse(value.simulationTraceProfile.text);
      if (mode === 'metric') data.metrics.recordedTaskTicks = data.metrics.recordedBusyTicks;
      if (mode === 'false-complete') { data.state = 'Complete'; data.reason = 'coglatas.profile.complete-recording'; }
      if (mode === 'observed') data.inputKind = 'ObservedOperational';
      value.simulationTraceProfile.text = JSON.stringify(data); value.simulationTraceProfile.digest = hash(value.simulationTraceProfile.text);
    }
    await page.route('**/assets/projectide/simulation-profile-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bundle) }));
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await openExample(page, locale); await readRun(page, locale, value.key, 2);
    await page.getByRole('button', { name: locale === 'en' ? 'Read model trace summary' : 'モデル記録の集計を読む', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'The model trace summary could not be verified' : 'この記録のモデル集計を検証できませんでした');
    await expect(page.getByTestId('ide-trace-profile')).toHaveCount(0); await expect(page.getByTestId('ide-simulation')).toBeVisible();
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
