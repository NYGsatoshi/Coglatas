import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en', 'ja'] as const) {
  test('preserves a real dependency Compiler failure apart from passing Scenario Tests in ' + locale, async ({ page }) => {
    const body = await readFile('frontend/public/assets/projectide/dependency-analysis-v1.json', 'utf8');
    await page.route('**/assets/projectide/scenario-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body }));
    await page.addInitScript(locale => {
      localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
    }, locale);
    const writes: string[] = [];
    page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open Scenario example' : 'シナリオの例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption('tests-dependency-base');
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Scenario' : 'シナリオ', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load Scenario comparison' : 'シナリオの比較を読み込む', exact: true }).click();
    for (const stage of [0, 1, 2]) {
      const section = page.getByTestId('ide-scenario-result-' + stage);
      await expect(section.locator('dt').filter({ hasText: locale === 'en' ? 'Compiler verdict' : 'コンパイラーの判定' }).locator('+ dd')).toHaveText(locale === 'en' ? 'Failed' : '失敗');
    }
    await expect(page.getByTestId('ide-scenario-result-1')).toContainText(locale === 'en' ? 'Passed' : '成功');
    const load = page.getByRole('button', { name: locale === 'en' ? 'Load Scenario snapshot' : 'シナリオのスナップショットを読み込む', exact: true });
    await load.focus(); await load.press('Enter');
    await expect(page.getByTestId('ide-context')).toHaveValue('tests-dependency-hypothesis');
    await page.getByRole('button', { name: locale === 'en' ? 'Tests' : 'テスト', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Passed' : '成功');
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    expect(writes).toEqual([]);
  });
}
