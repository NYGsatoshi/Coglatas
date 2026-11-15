import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const bundle = JSON.parse(readFileSync('frontend/public/assets/projectide/predicate-recordings-v1.json', 'utf8'));
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + '\0' + text).digest('hex') });
const roles = [['original', 'Read original Manifest run', '元のManifest実行を読む'], ['diagnostic', 'Read captured diagnostic run', '記録済みの診断実行を読む'], ['budget', 'Read capture-limited run', '記録上限に達した実行を読む'], ['cancelled', 'Read cancelled attempt', 'キャンセルされた試行を読む'], ['retry', 'Read run after cancellation', 'キャンセル後の実行を読む']] as const;
async function button(page: Page, locale: 'en' | 'ja', en: string, ja: string) {
  const element = page.getByRole('button', { name: locale === 'en' ? en : ja, exact: true });
  await element.focus(); await element.press('Enter');
}
async function open(page: Page, locale: 'en' | 'ja', key: string) {
  await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
  await page.goto('/app/projectide/preview');
  await button(page, locale, 'Open recorded predicate example', '記録済みの述語の例を開く');
  await page.getByTestId('ide-context').selectOption(key);
  await button(page, locale, 'Load result fixture', '結果Fixtureを読み込む');
  await button(page, locale, 'Source', 'ソース');
  await expect(page.getByTestId('ide-predicate-recording-actions')).toHaveCount(0);
  await button(page, locale, 'Read fixed root Source', '固定されたルートソースを読む');
  await expect(page.getByTestId('ide-root-source')).toBeFocused();
}
async function diagnostic(page: Page, locale: 'en' | 'ja', value: { result: { diagnostics: { code: string; diagnosticId: string }[] } }, index: number) {
  await button(page, locale, 'Problems', '問題');
  const entry = value.result.diagnostics[index];
  const occurrence = value.result.diagnostics.slice(0, index).filter(d => d.code === entry.code).length;
  const target = page.getByRole('button', { name: (locale === 'en' ? 'Inspect diagnostic' : '診断を調べる') + ': ' + entry.code, exact: true }).nth(occurrence);
  await target.focus(); await target.press('Space');
  await expect(page.getByTestId('ide-diagnostic-basis')).toContainText(entry.diagnosticId);
}
for (const locale of ['en', 'ja'] as const) for (const key of ['fail', 'unknown', 'test-assertion-navigation']) {
  test('reads actual run identity and selected original predicate arrays by keyboard: ' + key + ' ' + locale, async ({ page }) => {
    const value = bundle.cases.find((row: { key: string }) => row.key === key);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await open(page, locale, key);
    for (const [role, en, ja] of roles) {
      await button(page, locale, 'Source', 'ソース'); await button(page, locale, en, ja);
      const panel = page.getByTestId('ide-predicate-recording'); await expect(panel).toBeFocused();
      const frame = value.predicateRecordings[role]; const manifest = JSON.parse(frame.manifest.text); const response = JSON.parse(frame.response.text);
      await expect(panel.getByTestId('ide-predicate-recording-run')).toHaveText(manifest.runId);
      await expect(panel).toContainText(response.attemptId); await expect(panel).toContainText(manifest.sessionId);
      if (manifest.origin) await expect(panel).toContainText(manifest.origin.runId);
      await expect(panel.getByTestId('ide-recorded-predicate-body')).toHaveCount(0);
      await diagnostic(page, locale, value, 0);
      const selected = page.getByTestId('ide-recorded-predicate');
      if (role === 'cancelled') {
        await expect(selected).toContainText(locale === 'en' ? 'retains no Compiler result' : 'Compilerの結果も述語本文も保持されていません');
        await expect(selected.getByRole('button')).toHaveCount(0);
      } else {
        for (const [choice, readEn, readJa] of [['manifest', 'Read premise manifest', '根拠のマニフェストを読む'], ['available', 'Read selected original arrays', '選択した元の配列を読む'], ['budget', 'Read with one-byte viewing limit', '表示上限1バイトで読む']]) {
          const read = selected.getByRole('button', { name: locale === 'en' ? readEn : readJa, exact: true }); await read.focus(); await read.press('Enter'); await expect(selected).toBeFocused();
          const snapshot = JSON.parse(frame.diagnostics[0][choice].text); const captured = snapshot.references.filter((reference: { bodyCaptured: boolean }) => reference.bodyCaptured);
          const bodies = selected.getByTestId('ide-recorded-predicate-body'); await expect(bodies).toHaveCount(captured.length);
          for (let index = 0; index < captured.length; index++) {
            const original = frame.bodies.find((body: { digest: { value: string } }) => body.digest.value === captured[index].digest.value);
            const details = bodies.nth(index); await details.locator('summary').focus(); await details.locator('summary').press('Space');
            const text = await details.locator('pre').textContent(); expect(text).toBe(original.text);
            expect(hash('coglatas.predicate-premises/1', text ?? '')).toEqual(original.digest);
          }
          if (role === 'budget' && key === 'fail') await expect(selected).toContainText(locale === 'en' ? 'Original capture byte limit exceeded' : '元の記録のバイト上限を超えました');
          if (role === 'diagnostic' && key === 'fail' && choice === 'budget') {
            await expect(selected).toContainText(locale === 'en' ? 'Original root arrays recorded' : '元のルート配列が記録されています');
            await expect(selected.getByTestId('ide-predicate-view-state')).toContainText(locale === 'en' ? 'No partial body set was retained' : '一部の本文だけを保持することはありません');
          }
        }
      }
      await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    await button(page, locale, 'Source', 'ソース'); await button(page, locale, 'Read fixed root Source', '固定されたルートソースを読む');
    await expect(page.getByTestId('ide-predicate-recording')).toHaveCount(0); await expect(page.getByTestId('ide-recorded-predicate-body')).toHaveCount(0);
    expect(writes).toEqual([]);
  });
}
for (const locale of ['en', 'ja'] as const) {
  test('keeps successful captured arrays unavailable without a diagnostic: ' + locale, async ({ page }) => {
    await open(page, locale, 'pass'); await button(page, locale, roles[1][1], roles[1][2]);
    await expect(page.getByTestId('ide-predicate-capture-state')).toContainText(locale === 'en' ? 'Original root arrays recorded' : '元のルート配列が記録されています');
    await expect(page.getByTestId('ide-recorded-predicate-body')).toHaveCount(0); await expect(page.getByTestId('ide-analysis-status')).toContainText(bundle.cases.find((row: { key: string }) => row.key === 'pass').result.verdict);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
  });
  test('clears visible original arrays when selecting a new input: ' + locale, async ({ page }) => {
    const value = bundle.cases[0]; await open(page, locale, 'fail'); await button(page, locale, roles[1][1], roles[1][2]);
    await diagnostic(page, locale, value, 0); await button(page, locale, 'Read selected original arrays', '選択した元の配列を読む');
    await expect(page.getByTestId('ide-recorded-predicate-body')).toHaveCount(1);
    await page.getByTestId('ide-context').selectOption('unknown');
    await expect(page.getByTestId('ide-predicate-recording')).toHaveCount(0); await expect(page.getByTestId('ide-recorded-predicate-body')).toHaveCount(0);
  });
  for (const mode of ['body', 'unselected-body', 'origin', 'foreign-member', 'trust', 'snapshot', 'cancel-result', 'duplicate-run']) {
    test('refuses altered recorded predicate member ' + mode + ' ' + locale, async ({ page }) => {
      const altered = structuredClone(bundle); const value = altered.cases.find((row: { key: string }) => row.key === 'fail'); const projection = value.predicateRecordings;
      if (mode === 'body') projection.diagnostic.bodies[0].text += ' ';
      else if (mode === 'unselected-body') projection.retry.bodies[0].text += ' ';
      else if (mode === 'foreign-member') projection.diagnostic = altered.cases.find((row: { key: string }) => row.key === 'unknown').predicateRecordings.diagnostic;
      else if (mode === 'cancel-result') projection.cancelled.result = projection.original.result;
      else if (mode === 'snapshot') projection.diagnostic.diagnostics[0].available.text += ' ';
      else {
        const frame = projection.diagnostic; const data = JSON.parse(frame.manifest.text);
        if (mode === 'origin') data.origin.runId = '00000099-0000-4000-8000-000000000001';
        if (mode === 'trust') data.authority = 'Allowed';
        if (mode === 'duplicate-run') data.runId = JSON.parse(projection.original.manifest.text).runId;
        frame.manifest.text = JSON.stringify(data); frame.manifest.digest = hash(data.format, frame.manifest.text);
        const receipt = JSON.parse(frame.receipt.text); receipt.manifestDigest = frame.manifest.digest;
        frame.receipt.text = JSON.stringify(receipt); frame.receipt.digest = hash(receipt.format, frame.receipt.text);
      }
      await page.route('**/assets/projectide/predicate-recordings-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(altered) }));
      await open(page, locale, 'fail'); await button(page, locale, roles[1][1], roles[1][2]);
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'could not be verified' : '検証できませんでした');
      await expect(page.getByTestId('ide-predicate-recording')).toHaveCount(0); await expect(page.getByTestId('ide-recorded-predicate-body')).toHaveCount(0);
      await expect(page.getByTestId('ide-root-source')).toHaveCount(1); await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    });
  }
}
