import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const key of ['tests-scenario', 'tests-reference', 'tests-nested-reference']) for (const locale of ['en', 'ja'] as const) {
  test('separates ' + key + ' assumptions and fresh Scenario/Draft results in ' + locale, async ({ page }) => {
    await page.addInitScript(locale => {
      localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
    }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    const response = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/assets/projectide/scenario-v1.json'));
    await page.getByRole('button', { name: locale === 'en' ? 'Open Scenario example' : 'シナリオの例を開く', exact: true }).click();
    const fixture = await (await response).json(); const baseline = fixture.cases.find((item: { key: string }) => item.key === key + '-base');
    const load = locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む'; const scenario = locale === 'en' ? 'Scenario' : 'シナリオ';
    await page.getByTestId('ide-context').selectOption(key + '-base'); await page.getByRole('button', { name: load, exact: true }).click();
    await page.getByRole('button', { name: scenario, exact: true }).click();
    const read = page.getByRole('button', { name: locale === 'en' ? 'Load Scenario comparison' : 'シナリオの比較を読み込む', exact: true });
    await read.focus(); await read.press('Enter');
    await expect(page.getByTestId('ide-scenario-assumptions')).toContainText(key === 'tests-scenario' ? 'true' : 'coglatas.entity-reference');
    await expect(page.getByTestId('ide-scenario-changes')).toContainText('/payload/ports');
    await expect(page.getByTestId('ide-scenario-result-0')).toContainText(locale === 'en' ? 'Failed' : '失敗');
    await expect(page.getByTestId('ide-scenario-result-0')).toContainText(key === 'tests-scenario' ? (locale === 'en' ? 'Passed' : '成功') : (locale === 'en' ? 'Inconclusive' : '判定不能'));
    if (key !== 'tests-scenario') for (const stage of [0, 1, 2]) {
      const section = page.getByTestId('ide-scenario-result-' + stage);
      await expect(section.locator('dt').filter({ hasText: locale === 'en' ? 'Compiler verdict' : 'コンパイラーの判定' }).locator('+ dd')).toHaveText(locale === 'en' ? 'Inconclusive' : '判定不能');
    }
    await expect(page.getByTestId('ide-scenario-result-1')).toContainText(locale === 'en' ? 'Passed' : '成功');
    expect(await page.getByTestId('ide-scenario-results').evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    const pending = page.waitForEvent('download');
    const exportButton = page.getByRole('button', { name: locale === 'en' ? 'Export assumptions' : '仮定を書き出す', exact: true });
    await exportButton.focus(); await exportButton.press('Space'); const download = await pending;
    expect(download.suggestedFilename()).toBe('project-scenario-assumptions.json');
    expect((await readFile((await download.path())!)).toString('utf8')).toBe(baseline.preparedScenario.overlay.text);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    const loadScenario = page.getByRole('button', { name: locale === 'en' ? 'Load Scenario snapshot' : 'シナリオのスナップショットを読み込む', exact: true });
    await loadScenario.focus(); await loadScenario.press('Enter');
    await expect(page.getByTestId('ide-context')).toHaveValue(key + '-hypothesis');
    await expect(page.getByRole('heading', { name: scenario, exact: true, level: 2 })).toBeFocused();
    await expect(page.getByTestId('ide-context-header')).toContainText('coglatas.scenario');
    await page.getByRole('button', { name: locale === 'en' ? 'Tests' : 'テスト', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Passed' : '成功');
    const assertion = page.getByTestId('ide-test-rows').getByRole('button').first(); await assertion.focus(); await assertion.press('Enter');
    await expect(page.getByTestId('ide-source-target')).toBeFocused();
    await expect(page.getByTestId('ide-source-location')).toHaveText('/payload/assertions/' + (key === 'tests-scenario' ? 'example.ready' : key === 'tests-reference' ? 'example.reference' : 'example.collection'));
    await expect(page.getByTestId('ide-context-header')).toContainText('coglatas.scenario');
    await page.getByRole('button', { name: scenario, exact: true }).click();
    const loadDraft = page.getByRole('button', { name: locale === 'en' ? 'Load prepared Draft' : '準備済みDraftを読み込む', exact: true });
    await loadDraft.focus(); await loadDraft.press('Space');
    await expect(page.getByTestId('ide-context')).toHaveValue(key + '-draft');
    await expect(page.getByRole('heading', { name: scenario, exact: true, level: 2 })).toBeFocused();
    await expect(page.getByTestId('ide-context-header')).toContainText('coglatas.candidate');
    await page.getByRole('button', { name: locale === 'en' ? 'Tests' : 'テスト', exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
    await page.getByRole('button', { name: locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Passed' : '成功');
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    await page.getByTestId('ide-context').selectOption('tests-scenario-base');
    await expect(page.getByTestId('ide-scenario-results')).toHaveCount(0); await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
    expect(writes).toEqual([]);
  });
}

for (const key of ['tests-missing-reference', 'tests-denied-reference']) for (const locale of ['en', 'ja'] as const) {
  test('keeps ' + key + ' incomplete without inherited PASS in ' + locale, async ({ page }) => {
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open Scenario example' : 'シナリオの例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption(key + '-hypothesis');
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Tests' : 'テスト', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Inconclusive' : '判定不能');
    await expect(page.getByTestId('ide-tests-summary')).not.toContainText(locale === 'en' ? 'Passed' : '成功');
    await page.getByRole('button', { name: locale === 'en' ? 'Scenario' : 'シナリオ', exact: true }).click();
    await expect(page.getByRole('button', { name: locale === 'en' ? 'Load Scenario comparison' : 'シナリオの比較を読み込む', exact: true })).toHaveCount(0);
    await expect(page.getByTestId('ide-scenario-results')).toHaveCount(0); expect(writes).toEqual([]);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
  });
}
