import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en', 'ja'] as const) for (const mode of ['root', 'external', 'unknown', 'bounded', 'altered']) {
  test('reads actual Core simulation with separate input and event origins ' + locale + ' ' + mode, async ({ page }) => {
    const key = 'tests-simulation-' + (mode === 'altered' ? 'root' : mode) + '-base';
    if (mode === 'altered') {
      const raw = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-v1.json', 'utf8'));
      raw.cases.find((value: { key: string }) => value.key === key).simulationRun.text += ' ';
      await page.route('**/assets/projectide/simulation-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(raw) }));
    }
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    const response = page.waitForResponse(response => response.url().endsWith('/assets/projectide/simulation-v1.json'));
    await page.getByRole('button', { name: locale === 'en' ? 'Open simulation result example' : 'シミュレーション結果の例を開く', exact: true }).click(); expect((await response).ok()).toBe(true);
    await page.getByTestId('ide-context').selectOption(key);
    const result = page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }); await result.click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    const run = page.getByTestId('ide-simulation'); await expect(run).toHaveCount(0);
    await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
    if (mode === 'unknown') {
      await expect(page.getByTestId('ide-plan-not-produced')).toContainText('coglatas.plan.required-validation-incomplete');
      await expect(page.getByRole('button', { name: locale === 'en' ? 'Load recorded simulation' : '記録されたシミュレーションを読み込む', exact: true })).toHaveCount(0);
    } else {
      const load = page.getByRole('button', { name: locale === 'en' ? 'Load recorded simulation' : '記録されたシミュレーションを読み込む', exact: true });
      await load.focus(); await load.press('Enter');
      if (mode === 'altered') {
        await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'recorded simulation could not be verified' : 'シミュレーション記録を検証できませんでした');
        await expect(run).toHaveCount(0);
      } else {
        await expect(run).toBeVisible(); await expect(run).toContainText(mode === 'bounded' ? 'ResourceExhausted' : 'Completed');
        await expect(run).toContainText(locale === 'en' ? 'Viewing this recording starts no run' : '記録の表示は新たな実行を開始せず');
        await expect(run.locator('li')).toHaveCount(mode === 'bounded' ? 2 : mode === 'root' ? 6 : 8);
        const first = run.locator('li button').first(); await first.focus(); await first.press('Enter');
        await expect(page.getByTestId('ide-scoped-source-target')).toBeFocused();
        await run.getByText(locale === 'en' ? 'Recorded result and digest' : '記録結果とダイジェスト', { exact: true }).click();
        await expect(run.locator('pre')).toContainText('coglatas.no-outcome-assertions'); await expect(run.locator('pre')).toContainText('coglatas.simulation-run-manifest/1');
        if (mode === 'bounded') await expect(run.locator('pre')).toContainText('coglatas.simulation.event-bound');
        await result.click(); await expect(run).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
      }
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
