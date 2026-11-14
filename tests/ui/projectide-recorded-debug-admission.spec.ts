import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + '\0' + text).digest('hex') });
for (const locale of ['en', 'ja'] as const) for (const mode of ['bytes', 'version', 'mixed-run', 'false-terminal']) {
  test('refuses corrupted recorded debugger ' + mode + ' in ' + locale, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-debug-v1.json', 'utf8'));
    bundle.cases = bundle.cases.filter((item: { key: string }) => item.key.startsWith('tests-simulation-root-'));
    const value = bundle.cases.find((item: { key: string }) => item.key === 'tests-simulation-root-base');
    if (mode === 'bytes') value.simulationDebug.frames[0].snapshot.text += ' ';
    if (mode === 'version') value.simulationDebug.format = 'coglatas.local-simulation-debug-recording/2';
    if (mode === 'mixed-run') value.simulationDebug = bundle.cases[1].simulationDebug;
    if (mode === 'false-terminal') { const frame = value.simulationDebug.frames[3]; const snapshot = JSON.parse(frame.snapshot.text); snapshot.state = 'Terminated'; frame.snapshot.text = JSON.stringify(snapshot); frame.snapshot.digest = hash('coglatas.simulation-debug-snapshot/1', frame.snapshot.text); }
    await page.route('**/assets/projectide/simulation-debug-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bundle) }));
    await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await page.goto('/app/projectide/preview');
    await page.getByRole('button', { name: locale === 'en' ? 'Open recorded debugger example' : '記録済みデバッガーの例を開く', exact: true }).click();
    await page.getByTestId('ide-context').selectOption(value.key);
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-scoped-region')).toHaveCount(1);
    await page.getByRole('button', { name: locale === 'en' ? 'Load recorded simulation' : '記録されたシミュレーションを読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-simulation')).toBeVisible();
    await page.getByRole('button', { name: locale === 'en' ? 'Load recorded debugger frames' : '記録済みデバッグフレームを読み込む', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'Recorded debugger frames could not be verified' : '記録済みデバッグフレームを検証できませんでした');
    await expect(page.getByTestId('ide-recorded-debug')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
