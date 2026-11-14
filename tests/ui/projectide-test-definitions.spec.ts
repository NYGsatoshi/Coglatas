import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en', 'ja'] as const) {
  test('exports canonical Test Source and loads its Core-prepared local edit in ' + locale, async ({ page }) => {
    await page.addInitScript(locale => {
      localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
    }, locale);
    const writes: string[] = []; page.on('request', request => {
      if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method());
    });
    await page.goto('/app/projectide/preview');
    const response = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/assets/projectide/test-edit-v1.json'));
    await page.getByRole('button', { name: locale === 'en' ? 'Open Test editing example' : 'テスト編集の例を開く', exact: true }).click();
    const fixture = await (await response).json(); const before = fixture.cases.find((item: { key: string }) => item.key === 'tests-edit-before');
    const load = locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む'; const tests = locale === 'en' ? 'Tests' : 'テスト';
    const loadTests = locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む';
    await page.getByTestId('ide-context').selectOption('tests-edit-before'); await page.getByRole('button', { name: load, exact: true }).click();
    await page.getByRole('button', { name: tests, exact: true }).click(); await page.getByRole('button', { name: loadTests, exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Failed' : '失敗');
    await expect(page.getByTestId('ide-test-definition')).toHaveText(before.preparedTestEdit.definitionText);
    const pending = page.waitForEvent('download');
    const exported = page.getByRole('button', { name: locale === 'en' ? 'Export Test definition' : 'テスト定義を書き出す', exact: true });
    await exported.focus(); await exported.press('Enter'); const download = await pending;
    expect(download.suggestedFilename()).toBe('project-test-' + before.preparedTestEdit.plan.target.entityId + '.json');
    expect((await readFile((await download.path())!)).toString('utf8')).toBe(before.preparedTestEdit.definitionText);
    const imported = page.getByTestId('ide-test-edit-preview').locator('details');
    await imported.locator('summary').focus(); await imported.locator('summary').press('Enter'); await expect(imported.locator('pre')).toContainText('"value":false');
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    const apply = page.getByRole('button', { name: locale === 'en' ? 'Load prepared Test edit' : '準備済みのテスト編集を読み込む', exact: true });
    await apply.focus(); await apply.press('Space');
    await expect(page.getByTestId('ide-context')).toHaveValue('tests-edit-after');
    await expect(page.getByRole('heading', { name: locale === 'en' ? 'Source' : 'ソース', exact: true })).toBeFocused();
    await expect(page.locator('pre.source-buffer')).toContainText('coglatas.candidate');
    await page.getByRole('button', { name: tests, exact: true }).click(); await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
    await page.getByRole('button', { name: loadTests, exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Passed' : '成功');
    await expect(page.getByTestId('ide-test-edit-preview')).toHaveCount(0); await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    expect(writes).toEqual([]);
  });
}
