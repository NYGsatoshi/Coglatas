import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en', 'ja'] as const) {
  test('separates baseline, assumptions and fresh Scenario/Draft results in ' + locale, async ({ page }) => {
    await page.addInitScript(locale => {
      localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
    }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    const response = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/assets/projectide/scenario-v1.json'));
    await page.getByRole('button', { name: locale === 'en' ? 'Open Scenario example' : 'シナリオの例を開く', exact: true }).click();
    const fixture = await (await response).json(); const baseline = fixture.cases.find((item: { key: string }) => item.key === 'tests-scenario-base');
    const load = locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む'; const scenario = locale === 'en' ? 'Scenario' : 'シナリオ';
    await page.getByTestId('ide-context').selectOption('tests-scenario-base'); await page.getByRole('button', { name: load, exact: true }).click();
    await page.getByRole('button', { name: scenario, exact: true }).click();
    const read = page.getByRole('button', { name: locale === 'en' ? 'Load Scenario comparison' : 'シナリオの比較を読み込む', exact: true });
    await read.focus(); await read.press('Enter');
    await expect(page.getByTestId('ide-scenario-assumptions')).toContainText('true');
    await expect(page.getByTestId('ide-scenario-changes')).toContainText('/payload/ports');
    await expect(page.getByTestId('ide-scenario-result-0')).toContainText(locale === 'en' ? 'Failed' : '失敗');
    await expect(page.getByTestId('ide-scenario-result-0')).toContainText(locale === 'en' ? 'Passed' : '成功');
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
    await expect(page.getByTestId('ide-context')).toHaveValue('tests-scenario-hypothesis');
    await expect(page.getByRole('heading', { name: scenario, exact: true, level: 2 })).toBeFocused();
    await expect(page.getByTestId('ide-context-header')).toContainText('coglatas.scenario');
    await page.getByRole('button', { name: locale === 'en' ? 'Tests' : 'テスト', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Passed' : '成功');
    const assertion = page.getByTestId('ide-test-rows').getByRole('button'); await assertion.focus(); await assertion.press('Enter');
    await expect(page.getByTestId('ide-source-target')).toBeFocused();
    await expect(page.getByTestId('ide-source-location')).toHaveText('/payload/assertions/example.ready');
    await expect(page.getByTestId('ide-context-header')).toContainText('coglatas.scenario');
    await page.getByRole('button', { name: scenario, exact: true }).click();
    const loadDraft = page.getByRole('button', { name: locale === 'en' ? 'Load prepared Draft' : '準備済みDraftを読み込む', exact: true });
    await loadDraft.focus(); await loadDraft.press('Space');
    await expect(page.getByTestId('ide-context')).toHaveValue('tests-scenario-draft');
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
