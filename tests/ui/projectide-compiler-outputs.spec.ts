import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash = (text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update('coglatas.local-compiler-output-snapshot/1\0' + text).digest('hex') });
async function openExample(page: Page, locale: 'en' | 'ja') {
  await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
  await page.goto('/app/projectide/preview');
  await page.getByRole('button', { name: locale === 'en' ? 'Open Compiler output example' : 'Compiler出力の例を開く', exact: true }).click();
}
async function readSources(page: Page, locale: 'en' | 'ja', key: string, regions: number) {
  await page.getByTestId('ide-context').selectOption(key);
  await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
  await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
  await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click();
  await expect(page.getByTestId('ide-scoped-region')).toHaveCount(regions);
}
for (const locale of ['en', 'ja'] as const) for (const family of ['root', 'true', 'false', 'unknown']) {
  test('reads actual Compiler manifests and available bodies by keyboard in ' + locale + ' ' + family, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/compiler-outputs-v1.json', 'utf8'));
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await openExample(page, locale);
    for (const revision of ['base', 'hypothesis', 'draft']) {
      const value = bundle.cases.find((row: { key: string }) => row.key === 'tests-publication-' + family + '-' + revision);
      await readSources(page, locale, value.key, family === 'root' ? 1 : 2);
      const manifest = page.getByRole('button', { name: locale === 'en' ? 'Read output manifest' : '出力マニフェストを読む', exact: true });
      await manifest.focus(); await manifest.press('Enter'); const panel = page.getByTestId('ide-compiler-outputs'); await expect(panel).toBeFocused();
      await expect(panel).toContainText(locale === 'en' ? 'Manifest only' : 'マニフェストのみ'); await expect(panel.getByTestId('ide-compiler-output-body')).toHaveCount(0);
      await expect(panel).toContainText(value.resultDigest.value); await expect(panel).toContainText(value.sourceDigest.value); await expect(panel).toContainText(value.inputDigest.value);
      const available = page.getByRole('button', { name: locale === 'en' ? 'Read available output bodies' : '利用可能な出力本文を読む', exact: true });
      await available.focus(); await available.press('Space'); await expect(panel).toBeFocused(); const bodies = value.compilerOutputs.available.bodies;
      await expect(panel.getByTestId('ide-compiler-output-body')).toHaveCount(bodies.length);
      const gap = revision === 'draft' && family === 'root'; await expect(page.getByTestId('ide-compiler-outputs-state')).toContainText(gap ? (locale === 'en' ? 'exceed' : '超えています') : (locale === 'en' ? 'Available produced' : '利用可能な生成済み'));
      if (bodies.length) { const first = panel.getByTestId('ide-compiler-output-body').first(); await first.locator('summary').focus(); await first.locator('summary').press('Space'); await expect(first.locator('pre')).toBeVisible(); expect(await first.locator('pre').textContent()).toBe(bodies[0].text); await expect(first).toContainText(bodies[0].digest.value); }
      await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict); await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
      await page.getByRole('button', { name: locale === 'en' ? 'Load fixed sources' : '固定ソースを読み込む', exact: true }).click(); await expect(panel).toHaveCount(0);
    }
    expect(writes).toEqual([]);
  });
}
for (const locale of ['en', 'ja'] as const) for (const mode of ['bytes', 'result-id', 'body', 'false-captured-budget', 'trust', 'foreign-snapshot']) {
  test('refuses altered Compiler output snapshot ' + mode + ' in ' + locale, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/compiler-outputs-v1.json', 'utf8'));
    const value = bundle.cases.find((row: { key: string }) => row.key === (mode === 'false-captured-budget' ? 'tests-publication-root-draft' : 'tests-publication-true-base'));
    const frame = value.compilerOutputs.available;
    if (mode === 'bytes') frame.text += ' ';
    else if (mode === 'body') frame.bodies[0].text += ' ';
    else if (mode === 'foreign-snapshot') value.compilerOutputs.available = bundle.cases.find((row: { key: string }) => row.key === 'tests-publication-true-hypothesis').compilerOutputs.available;
    else { const data = JSON.parse(frame.text); if (mode === 'result-id') data.basis.resultDigest.value = 'b'.repeat(64); if (mode === 'false-captured-budget') data.state = 'CapturedAvailableOutputs'; if (mode === 'trust') data.producerTrust = 'Trusted'; frame.text = JSON.stringify(data); frame.digest = hash(frame.text); }
    await page.route('**/assets/projectide/compiler-outputs-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bundle) }));
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await openExample(page, locale); await readSources(page, locale, value.key, mode === 'false-captured-budget' ? 1 : 2);
    await page.getByRole('button', { name: locale === 'en' ? 'Read available output bodies' : '利用可能な出力本文を読む', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'The outputs could not be verified' : 'このCompiler結果の出力を検証できませんでした');
    await expect(page.getByTestId('ide-compiler-outputs')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-region')).toHaveCount(mode === 'false-captured-budget' ? 1 : 2);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
