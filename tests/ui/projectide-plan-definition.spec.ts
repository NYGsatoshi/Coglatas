import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
for (const locale of ['en', 'ja'] as const) for (const mode of ['root', 'external', 'unknown', 'altered', 'budget']) {
  test('reads a fixed compiled plan without starting a run ' + locale + ' ' + mode, async ({ page }) => {
    const raw = JSON.parse(await readFile('frontend/public/assets/projectide/execution-plan-v1.json', 'utf8'));
    const key = 'tests-plan-' + (mode === 'altered' ? 'root' : mode) + '-base';
    if (mode === 'altered') raw.cases.find((c: { key: string }) => c.key === key).scopedSources.publication.plan.text += ' ';
    if (mode === 'altered') await page.route('**/assets/projectide/execution-plan-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(raw) }));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview'); await page.getByRole('button', { name: locale === 'en' ? 'Open plan definition example' : '実行定義の例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption(key);
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    await expect(page.getByTestId('ide-plan-definition')).toHaveCount(0);
    const load = page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }); await load.focus(); await load.press('Enter');
    if (mode === 'altered') {
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'Fixed sources could not be verified' : '固定ソースを検証できませんでした');
      await expect(page.getByTestId('ide-plan-definition')).toHaveCount(0);
    } else if (mode === 'unknown') {
      await expect(page.getByTestId('ide-plan-not-produced')).toContainText('coglatas.plan.required-validation-incomplete');
      await expect(page.getByTestId('ide-plan-definition')).toHaveCount(0);
    } else {
      const plan = page.getByTestId('ide-plan-definition'); await expect(plan).toBeVisible();
      await expect(plan).toContainText(locale === 'en' ? 'No run has started' : '実行は開始しておらず');
      await expect(plan).toContainText((locale === 'en' ? 'Tasks: ' : 'タスク: ') + (mode === 'root' ? '3' : '4'));
      await plan.getByText(locale === 'en' ? 'Definition and digest' : '定義とダイジェスト', { exact: true }).click();
      await expect(plan.locator('pre')).toContainText('coglatas.not-instantiated');
      await expect(plan.locator('pre')).toContainText('prerequisite');
      await page.getByTestId('ide-context').selectOption(key.replace('-base', '-hypothesis'));
      await expect(page.getByTestId('ide-plan-definition')).toHaveCount(0);
      if (mode === 'budget') {
        await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
        await expect(page.getByTestId('ide-plan-definition')).toHaveCount(0);
        await expect(page.getByTestId('ide-evidence-status')).not.toContainText(locale === 'en' ? 'Complete current local PASS' : '現在のローカルPASS');
      }
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
