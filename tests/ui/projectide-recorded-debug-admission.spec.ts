import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + '\0' + text).digest('hex') });
for (const locale of ['en', 'ja'] as const) for (const mode of ['bytes', 'version', 'mixed-run', 'false-terminal', 'retained-inspection', 'dropped-inspection']) {
  test((mode === 'retained-inspection' ? 'retains inspected state across breakpoint updates' : 'refuses corrupted recorded debugger ' + mode) + ' in ' + locale, async ({ page }) => {
    const bundle = JSON.parse(await readFile('frontend/public/assets/projectide/simulation-debug-v1.json', 'utf8'));
    bundle.cases = bundle.cases.filter((item: { key: string }) => item.key.startsWith('tests-simulation-root-'));
    const value = bundle.cases.find((item: { key: string }) => item.key === 'tests-simulation-root-base');
    if (mode === 'bytes') value.simulationDebug.frames[0].snapshot.text += ' ';
    if (mode === 'version') value.simulationDebug.format = 'coglatas.local-simulation-debug-recording/2';
    if (mode === 'mixed-run') value.simulationDebug = bundle.cases[1].simulationDebug;
    if (mode === 'false-terminal') { const frame = value.simulationDebug.frames[3]; const snapshot = JSON.parse(frame.snapshot.text); snapshot.state = 'Terminated'; frame.snapshot.text = JSON.stringify(snapshot); frame.snapshot.digest = hash('coglatas.simulation-debug-snapshot/1', frame.snapshot.text); }
    if (mode === 'retained-inspection' || mode === 'dropped-inspection') {
      const original = value.simulationDebug.frames; const retained = structuredClone(original[4]);
      retained.action = 'coglatas.recorded-set-breakpoints'; retained.target = null;
      const snapshot = JSON.parse(retained.snapshot.text); snapshot.reason = 'coglatas.debug.breakpoints-set';
      if (mode === 'dropped-inspection') { retained.inspection = null; snapshot.inspectionDigest = null; }
      retained.snapshot.text = JSON.stringify(snapshot);
      value.simulationDebug.frames = [...original.slice(0, 5), retained, ...original.slice(7)];
      value.simulationDebug.frames.forEach((frame: { snapshot: { text: string; digest: unknown } }, index: number) => {
        const snapshot = JSON.parse(frame.snapshot.text); snapshot.revision = index; frame.snapshot.text = JSON.stringify(snapshot);
        frame.snapshot.digest = hash('coglatas.simulation-debug-snapshot/1', frame.snapshot.text);
      });
    }
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
    if (mode === 'retained-inspection') {
      const current = page.getByTestId('ide-recorded-debug-frame');
      const next = page.getByRole('button', { name: locale === 'en' ? 'Next recorded frame' : '次の記録フレーム', exact: true });
      await expect(current).toBeVisible();
      for (let index = 0; index < 5; index++) { await next.focus(); await next.press('Enter'); await expect(current.getByRole('heading', { level: 4 })).toContainText((index + 2) + ' / 11'); }
      await expect(current).toContainText('coglatas.debug.breakpoints-set');
      const inspection = page.getByTestId('ide-recorded-debug-inspection'); await expect(inspection).toContainText('Ready');
      await inspection.getByRole('button').click(); await expect(page.getByTestId('ide-scoped-source-target')).toBeVisible();
      await next.focus(); await next.press('Enter'); await expect(current).toBeFocused();
      await expect(current.getByRole('heading', { level: 4 })).toContainText('7 / 11');
      await expect(current).toContainText('coglatas.debug.breakpoint'); await expect(inspection).toHaveCount(0);
      await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
      await page.getByTestId('ide-context').selectOption('tests-simulation-root-draft'); await expect(page.getByTestId('ide-recorded-debug')).toHaveCount(0);
    } else {
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'Recorded debugger frames could not be verified' : '記録済みデバッグフレームを検証できませんでした');
      await expect(page.getByTestId('ide-recorded-debug')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
