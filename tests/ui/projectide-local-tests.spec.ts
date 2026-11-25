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

for (const locale of ['en', 'ja'] as const) {
  test('lists exact Test definitions independently of execution in ' + locale, async ({ page }) => {
    await page.addInitScript(locale => {
      localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
    }, locale);
    const writes: string[] = []; page.on('request', request => {
      if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method());
    });
    await page.goto('/app/projectide/preview');
    const load = locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む';
    const tests = locale === 'en' ? 'Tests' : 'テスト';
    const list = locale === 'en' ? 'List local Test definitions' : 'ローカルのテスト定義を一覧表示';
    const loadTests = locale === 'en' ? 'Load local Test results' : 'ローカルのテスト結果を読み込む';
    const choose = async (key: string) => {
      await page.getByTestId('ide-context').selectOption(key); await page.getByRole('button', { name: load, exact: true }).click();
      await page.getByRole('button', { name: tests, exact: true }).click();
    };
    const expectDefinitionSource = async (revisionId: string, testId = '0000000a-0000-4000-8000-000000000001') => {
      await expect(page.getByTestId('ide-source-location')).toHaveText('/');
      const record = JSON.parse((await page.getByTestId('ide-source-target').locator('pre').textContent())!);
      expect(record).toMatchObject({ entityId: testId, documentId: '00000004-0000-4000-8000-000000000001', kind: 'coglatas.project-test' });
      const buffer = page.locator('pre.source-buffer'); await expect(buffer).toHaveCount(1);
      const source = JSON.parse((await buffer.textContent())!);
      expect(source.context).toEqual({ branch: { tenantId: '00000001-0000-4000-8000-000000000001', projectId: '00000002-0000-4000-8000-000000000001', branchId: '00000003-0000-4000-8000-000000000001' }, kind: 'coglatas.committed', revisionId });
      expect(source.declarations.find((declaration: { entityId: string }) => declaration.entityId === record.entityId)).toEqual(record);
    };
    await choose('tests-fail');
    const action = page.getByRole('button', { name: list, exact: true }); await action.focus(); await action.press('Enter');
    await expect(page.getByTestId('ide-test-discovery-summary')).toContainText(locale === 'en' ? 'Definition inventory available' : '定義の一覧を取得しました');
    await expect(action).toBeFocused();
    await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
    await expect(page.getByTestId('ide-analysis-status')).toContainText('coglatas.pass');
    await page.getByRole('button', { name: loadTests, exact: true }).click();
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Failed' : '失敗');
    await expect(page.getByTestId('ide-test-discovery-summary')).toHaveText('');
    await expect(page.getByTestId('ide-test-discovery-rows')).toHaveCount(0);
    await action.focus(); await action.press('Space');
    await expect(page.getByTestId('ide-test-discovery-summary')).toContainText(locale === 'en' ? 'Assertions evaluated: 0' : '評価したアサーション: 0');
    await expect(page.getByTestId('ide-tests-summary')).toContainText(locale === 'en' ? 'Failed' : '失敗');
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    const open = page.getByTestId('ide-test-discovery-rows').getByRole('button'); await expect(open).toHaveCount(1);
    await open.focus(); await open.press('Enter'); await expect(page.getByTestId('ide-source-target')).toBeFocused();
    await expect(page.locator('pre.source-buffer')).toContainText('coglatas.project-test');
    await expectDefinitionSource('000000c8-0000-4000-8000-000000000001');
    await page.getByTestId('ide-context').selectOption('branch-pass'); await expect(page.getByTestId('ide-source-target')).toHaveCount(0);
    await choose('tests-unsupported'); await page.getByRole('button', { name: list, exact: true }).click();
    await expect(page.getByTestId('ide-test-discovery-rows')).toContainText(locale === 'en' ? 'Unknown' : '不明');
    await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
    await choose('tests-many'); await page.getByRole('button', { name: list, exact: true }).click();
    const rows = page.getByTestId('ide-test-discovery-rows').locator(':scope > li'); await expect(rows).toHaveCount(32);
    const nextDefinitions = page.getByRole('button', { name: locale === 'en' ? 'Next definitions' : '次の定義', exact: true });
    await nextDefinitions.focus(); await nextDefinitions.press('Enter');
    await expect(rows).toHaveCount(1); await expect(page.getByTestId('ide-test-discovery-summary')).toContainText('33');
    await expect(nextDefinitions).toBeDisabled(); const lastDefinition = rows.getByRole('button'); await expect(lastDefinition).toBeFocused();
    await expect(lastDefinition).toContainText('0000014c-0000-4000-8000-000000000001'); await lastDefinition.press('Enter');
    await expect(page.getByTestId('ide-source-target')).toBeFocused(); await expectDefinitionSource('000000d2-0000-4000-8000-000000000001', '0000014c-0000-4000-8000-000000000001');
    await page.getByRole('button', { name: tests, exact: true }).click();
    const previousDefinitions = page.getByRole('button', { name: locale === 'en' ? 'Previous definitions' : '前の定義', exact: true });
    await previousDefinitions.focus(); await previousDefinitions.press('Enter'); await expect(rows).toHaveCount(32);
    await expect(previousDefinitions).toBeDisabled(); await expect(rows.first().getByRole('button')).toBeFocused();
    await expect(rows.first()).toContainText('0000012c-0000-4000-8000-000000000001');
    for (const key of ['tests-stale', 'tests-exhausted']) {
      await choose(key); await page.getByRole('button', { name: list, exact: true }).click();
      await expect(page.getByTestId('ide-test-discovery-summary')).toContainText(key === 'tests-stale'
        ? locale === 'en' ? 'The requested input differs from this snapshot.' : '要求された入力がこのスナップショットと異なります。'
        : locale === 'en' ? 'The full inventory exceeds the local reading limit.' : '全件一覧がローカルの読み取り上限を超えています。');
      await expect(rows).toHaveCount(0); await expect(page.getByTestId('ide-test-discovery-summary')).toBeVisible();
      await expect(page.getByTestId('ide-test-discovery-summary').locator('dl')).toHaveCount(0);
      await expect(page.getByTestId('ide-test-discovery-summary')).not.toContainText(locale === 'en' ? 'Definitions listed: 0' : '表示した定義: 0');
    }
    await choose('tests-empty'); await page.getByRole('button', { name: list, exact: true }).click();
    await expect(page.getByTestId('ide-test-discovery-summary').locator('dl')).toHaveCount(1);
    await expect(page.getByTestId('ide-test-discovery-summary')).toContainText(locale === 'en' ? 'Definitions listed: 0' : '表示した定義: 0');
    await page.getByTestId('ide-simulation-example').click(); await choose('tests-simulation-root-base');
    await page.getByRole('button', { name: list, exact: true }).click();
    const scopedOpen = page.getByTestId('ide-test-discovery-rows').getByRole('button'); await expect(scopedOpen).toHaveCount(1);
    await scopedOpen.focus(); await scopedOpen.press('Enter'); await expect(page.getByTestId('ide-source-target')).toBeFocused();
    await expect(page.locator('pre.source-buffer')).toContainText('coglatas.project-test');
    await expectDefinitionSource('00000898-0000-4000-8000-000000000001');
    await page.getByTestId('ide-test-discovery-example').click();
    await page.getByTestId('ide-context').selectOption('tests-discovery-unavailable');
    await page.getByRole('button', { name: load, exact: true }).click(); await page.getByRole('button', { name: tests, exact: true }).click();
    await page.getByRole('button', { name: list, exact: true }).click();
    await expect(page.getByTestId('ide-test-discovery-summary')).toContainText(locale === 'en' ? 'full definition inventory is unavailable' : '定義の全件一覧を取得できません');
    await expect(rows).toHaveCount(0); await expect(page.getByTestId('ide-tests-summary')).toHaveCount(0);
    await expect(page.getByTestId('ide-test-discovery-summary').locator('dl')).toHaveCount(0);
    await expect(page.getByTestId('ide-test-discovery-summary')).toHaveAttribute('data-admission', 'admitted');
    await expect(page.getByTestId('ide-test-discovery-summary')).toHaveAttribute('data-discovery-state', 'Unavailable');
    // Negative transport control: missing metadata must not impersonate a Core-produced refusal.
    await page.route('**/assets/projectide/test-discovery-v1.json', async route => {
      const response = await route.fetch(); const bundle = await response.json();
      for (const item of bundle.cases) delete item.localProjectTestDiscovery;
      await route.fulfill({ response, json: bundle });
    });
    // The selected fixture is replaced asynchronously; wait for the intercepted response
    // and the newly rendered Ready workbench before choosing a context.
    const reloadedDiscovery = page.waitForResponse(response =>
      response.url().endsWith('/assets/projectide/test-discovery-v1.json') && response.ok());
    await page.getByTestId('ide-test-discovery-example').click();
    await reloadedDiscovery;
    await expect(page.getByTestId('ide-test-discovery-example')).toBeVisible();
    await choose('tests-discovery-unavailable');
    await page.getByRole('button', { name: list, exact: true }).click();
    await expect(page.getByTestId('ide-test-discovery-summary')).toHaveAttribute('data-admission', 'not-admitted');
    await expect(page.getByTestId('ide-test-discovery-summary')).toHaveAttribute('data-discovery-state', 'unavailable');
    await expect(rows).toHaveCount(0);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
