import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en', 'ja'] as const) for (const mode of ['assertion-prefix', 'export-whitespace']) {
  test('refuses rehashed inconsistent Test admission ' + locale + ' ' + mode, async ({ page }) => {
    const filename = mode === 'assertion-prefix' ? 'preview-v1.json' : 'test-edit-v1.json';
    const fixture = JSON.parse(await readFile('frontend/public/assets/projectide/' + filename, 'utf8'));
    const key = mode === 'assertion-prefix' ? 'tests-pass' : 'tests-edit-before';
    const value = fixture.cases.find((item: { key: string }) => item.key === key);
    const hash = (domain: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + '\0' + text).digest('hex') });
    if (mode === 'assertion-prefix') {
      const run = JSON.parse(value.localProjectTests.result.text); run.tests[0].assertions[0].location.propertyPath = 'x' + run.tests[0].assertions[0].location.propertyPath;
      const text = JSON.stringify(run); value.localProjectTests.result = { text, digest: hash('coglatas.project-test-run/1', text) };
    } else {
      value.preparedTestEdit.definitionText += ' ';
      value.preparedTestEdit.plan.beforeDeclarationDigest = hash('coglatas.project-test-declaration/1', value.preparedTestEdit.definitionText);
    }
    await page.route('**/assets/projectide/' + filename, route => route.fulfill({ json: fixture }));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; const downloads: string[] = [];
    page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    page.on('download', item => downloads.push(item.suggestedFilename()));
    await page.goto('/app/projectide/preview');
    if (mode === 'export-whitespace') await page.getByRole('button', { name: locale === 'en' ? 'Open Test editing example' : 'テスト編集の例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption(key);
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Tests' : 'テスト', exact: true }).click();
    const load = page.getByRole('button', { name: locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む', exact: true });
    await load.focus(); await load.press('Enter');
    if (mode === 'assertion-prefix') {
      await expect(page.getByText(locale === 'en' ? 'No local Test results are available for this snapshot.' : 'このスナップショットのローカルテスト結果はありません。', { exact: true })).toBeVisible();
      await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
    } else {
      await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Failed' : '失敗');
      const exported = page.getByRole('button', { name: locale === 'en' ? 'Export Test definition' : 'テスト定義を書き出す', exact: true });
      await exported.focus(); await exported.press('Enter');
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'The Test edit no longer matches the current input.' : 'テスト編集が現在の入力と一致しなくなりました。');
      const apply = page.getByRole('button', { name: locale === 'en' ? 'Load prepared Test edit' : '準備済みのテスト編集を読み込む', exact: true });
      await apply.focus(); await apply.press('Space');
      await expect(page.getByTestId('ide-context')).toHaveValue(key);
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    expect(downloads).toEqual([]); expect(writes).toEqual([]);
  });
}
