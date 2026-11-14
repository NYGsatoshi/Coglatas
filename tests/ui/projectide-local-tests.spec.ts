import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en', 'ja'] as const) {
  test('shows local Test outcomes separately from Compiler results in ' + locale, async ({ page }) => {
    await page.addInitScript(locale => {
      localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
    }, locale);
    const writes: string[] = []; page.on('request', request => {
      if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method());
    });
    await page.goto('/app/projectide/preview');
    const load = locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む';
    const tests = locale === 'en' ? 'Tests' : 'テスト'; const loadTests = locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む';
    const cases = [
      ['tests-pass', 'Passed', '成功'], ['tests-fail', 'Failed', '失敗'], ['tests-unknown', 'Inconclusive', '判定不能'],
      ['tests-empty', 'Not run', '未実行'], ['tests-unsupported', 'Not run', '未実行'], ['tests-cancelled', 'Cancelled', '取消し'],
      ['tests-stale', 'Stale', '古い入力'], ['tests-evaluator-mismatch', 'Not run', '未実行'],
      ['tests-error', 'Error', 'エラー'], ['tests-exhausted', 'Resource limit reached', '実行上限に到達'],
    ];
    for (const [key, english, japanese] of cases) {
      await page.getByTestId('ide-context').selectOption(key); await page.getByRole('button', { name: load, exact: true }).click();
      await page.getByRole('button', { name: tests, exact: true }).click();
      await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
      const action = page.getByRole('button', { name: loadTests, exact: true }); await action.focus(); await action.press('Enter');
      await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? english : japanese);
      if (key === 'tests-fail') {
        await expect(page.getByTestId('ide-analysis-status')).toContainText('coglatas.pass');
        const details = page.getByTestId('ide-tests-summary').locator('details');
        await details.locator('summary').focus(); await details.locator('summary').press('Enter');
        await expect(details.locator('code')).toHaveCount(2);
        await expect(details.locator('code').first()).toBeVisible();
        expect(await page.getByTestId('ide-tests-summary').evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
      }
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    }
    for (const [index, key] of ['tests-fail', 'tests-unicode'].entries()) {
      await page.getByTestId('ide-context').selectOption(key); await page.getByRole('button', { name: load, exact: true }).click();
      await page.getByRole('button', { name: tests, exact: true }).click(); await page.getByRole('button', { name: loadTests, exact: true }).click();
      const open = page.getByTestId('ide-test-rows').getByRole('button'); await expect(open).toHaveCount(1);
      await open.focus(); await open.press(index ? 'Space' : 'Enter');
      await expect(page.getByTestId('ide-source-target')).toBeFocused();
      // Unicode is intentional assertion-key fixture data, including pointer escape characters.
      await expect(page.getByTestId('ide-source-location')).toHaveText('/payload/assertions/' + (index ? 'example.テスト~1α~0β' : 'example.ready'));
      await page.getByTestId('ide-context').selectOption('branch-pass'); await expect(page.getByTestId('ide-source-target')).toHaveCount(0);
    }
    await page.getByTestId('ide-context').selectOption('tests-many'); await page.getByRole('button', { name: load, exact: true }).click();
    await page.getByRole('button', { name: tests, exact: true }).click(); await page.getByRole('button', { name: loadTests, exact: true }).click();
    const rows = page.getByTestId('ide-test-rows').locator(':scope > li'); await expect(rows).toHaveCount(32);
    const next = page.getByRole('button', { name: locale === 'en' ? 'Next Tests' : '次のテスト', exact: true }); await next.focus(); await next.press('Enter');
    await expect(rows).toHaveCount(1); await expect(page.getByTestId('ide-tests-summary')).toContainText('33');
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
