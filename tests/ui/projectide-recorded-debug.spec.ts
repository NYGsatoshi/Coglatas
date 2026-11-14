import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
for (const locale of ['en', 'ja'] as const) for (const family of ['root', 'external', 'bounded']) {
  test('browses original recorded debugger frames with keyboard in ' + locale + ' ' + family, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-debug-v1.json', 'utf8'));
    const value = bundle.cases.find((item: { key: string }) => item.key === 'tests-simulation-' + family + '-base');
    const frames = value.simulationDebug.frames.map((frame: { snapshot: { text: string } }) => JSON.parse(frame.snapshot.text));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open recorded debugger example' : '記録済みデバッガーの例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption(value.key);
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-scoped-region')).toHaveCount(family === 'root' ? 1 : 2);
    await page.getByRole('button', { name: locale === 'en' ? 'Load recorded simulation' : '記録されたシミュレーションを読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-simulation')).toBeVisible();
    const load = page.getByRole('button', { name: locale === 'en' ? 'Load recorded debugger frames' : '記録済みデバッグフレームを読み込む', exact: true });
    await load.focus(); await load.press('Enter');
    const current = page.getByTestId('ide-recorded-debug-frame');
    const next = page.getByRole('button', { name: locale === 'en' ? 'Next recorded frame' : '次の記録フレーム', exact: true });
    const previous = page.getByRole('button', { name: locale === 'en' ? 'Previous recorded frame' : '前の記録フレーム', exact: true });
    for (const [index, frame] of frames.entries()) {
      await expect(current).toBeFocused(); await expect(current.getByRole('heading', { level: 4 })).toContainText((index + 1) + ' / ' + frames.length);
      await expect(current).toContainText(frame.reason);
      await expect(current).toContainText((locale === 'en' ? 'Last committed virtual tick' : '最後に確定した仮想tick') + ': ' + frame.virtualTicks);
      if (frame.pending) await expect(current).toContainText((locale === 'en' ? 'Future pending transition' : '未来の保留遷移') + ': ' + frame.pending.kind + ' · ' + frame.pending.ticks);
      if (frame.inspectionDigest) await expect(page.getByTestId('ide-recorded-debug-inspection')).toBeVisible();
      if (index === 0 || frame.state === 'Paused' && index === 3 || index === frames.length - 1) await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
      if (index === 0) await expect(previous).toBeDisabled();
      if (index < frames.length - 1) { await next.focus(); await next.press(index % 2 ? 'Enter' : 'Space'); }
    }
    await expect(next).toBeDisabled(); await previous.focus(); await previous.press('Enter'); await expect(current).toBeFocused();
    await page.getByTestId('ide-context').selectOption('tests-simulation-' + family + '-draft');
    await expect(page.getByTestId('ide-recorded-debug')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
    expect(writes).toEqual([]);
  });
}
