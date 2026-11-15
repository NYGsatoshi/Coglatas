import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const bundle = JSON.parse(readFileSync('frontend/public/assets/projectide/operator-recordings-v1.json', 'utf8'));
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + String.fromCharCode(0) + text).digest('hex') });
const roles = [['original', 'Read original Manifest run', '元のManifest実行を読む'], ['diagnostic', 'Read captured diagnostic run', '記録済みの診断実行を読む'], ['budget', 'Read capture-limited run', '記録上限に達した実行を読む'], ['cancelled', 'Read cancelled attempt', 'キャンセルされた試行を読む'], ['retry', 'Read run after cancellation', 'キャンセル後の実行を読む']] as const;
async function button(page: Page, locale: 'en' | 'ja', en: string, ja: string) {
  const element = page.getByRole('button', { name: locale === 'en' ? en : ja, exact: true });
  await element.focus(); await element.press('Enter');
}
async function open(page: Page, locale: 'en' | 'ja', key: string) {
  await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
  await page.goto('/app/projectide/preview');
  await button(page, locale, 'Open recorded operator example', '記録済み演算子の例を開く');
  // A context shared with the old profile can be selected before the new bundle renders.
  await expect(page.getByTestId('ide-context').locator('option[value="operator-nested-duplicates"]')).toHaveCount(1);
  await expect(page.getByTestId('ide-context').locator('option')).toHaveCount(bundle.cases.length + 1);
  await page.getByTestId('ide-context').selectOption(key);
  await expect(page.getByTestId('ide-context')).toHaveValue(key);
  await button(page, locale, 'Load result fixture', '結果Fixtureを読み込む');
  await button(page, locale, 'Source', 'ソース');
  await expect(page.getByTestId('ide-operator-recording-actions')).toHaveCount(0);
  await button(page, locale, 'Read fixed root Source', '固定されたルートソースを読む');
  await expect(page.getByTestId('ide-root-source')).toBeFocused();
}
async function diagnostic(page: Page, locale: 'en' | 'ja', entry: { code: string; diagnosticId: string }) {
  await button(page, locale, 'Problems', '問題');
  const target = page.getByRole('button', { name: (locale === 'en' ? 'Inspect diagnostic' : '診断を調べる') + ': ' + entry.code, exact: true }).first();
  await target.focus(); await target.press('Space');
  await expect(page.getByTestId('ide-diagnostic-basis')).toContainText(entry.diagnosticId);
}
for (const locale of ['en', 'ja'] as const) for (const key of ['fail', 'pass', 'unknown', 'test-assertion-navigation', 'operator-and-short-circuit', 'operator-nested-duplicates', 'operator-or-unknown', 'operator-not-pass']) {
  test('reads original operator visit order, execution counts and selected frames by keyboard: ' + key + ' ' + locale, async ({ page }) => {
    const value = bundle.cases.find((row: { key: string }) => row.key === key);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await open(page, locale, key);
    for (const [role, en, ja] of roles) {
      await button(page, locale, 'Source', 'ソース'); await button(page, locale, en, ja);
      const panel = page.getByTestId('ide-operator-recording'); await expect(panel).toBeFocused();
      const frame = value.operatorRecordings[role]; const manifest = JSON.parse(frame.manifest.text); const response = JSON.parse(frame.response.text);
      await expect(panel.getByTestId('ide-operator-recording-run')).toHaveText(manifest.runId);
      await expect(panel).toContainText(response.attemptId);
      if (manifest.origin) await expect(panel).toContainText(manifest.origin.runId);
      await expect(panel.getByTestId('ide-operator-node')).toHaveCount(0);
      if (role === 'cancelled') await expect(panel.getByTestId('ide-operator-no-result')).toContainText('Compiler');
      else await expect(panel.getByTestId('ide-operator-visited')).toHaveText(String(JSON.parse(frame.observation.text).observedNodes));
      if (value.result.diagnostics.length) {
        await diagnostic(page, locale, value.result.diagnostics[0]);
        const selected = page.getByTestId('ide-recorded-operators');
        await expect(selected.getByTestId('ide-operator-node')).toHaveCount(0);
        if (role === 'cancelled') {
          await expect(selected).toContainText(locale === 'en' ? 'has no Compiler result or operator record' : 'Compilerの結果や演算子の記録がありません');
          await expect(selected.getByRole('button')).toHaveCount(0);
        } else {
          for (const [choice, readEn, readJa] of [['manifest', 'Read premise manifest', '根拠のマニフェストを読む'], ['available', 'Read selected original operators', '選択した元の演算子を読む'], ['budget', 'Read with one-byte viewing limit', '表示上限1バイトで読む']]) {
            const read = selected.getByRole('button', { name: locale === 'en' ? readEn : readJa, exact: true });
            await read.focus(); await read.press('Enter'); await expect(selected).toBeFocused();
            const supplied = frame.diagnostics[0][choice]; const snapshot = JSON.parse(supplied.frame.text);
            const captured = snapshot.evaluations.flatMap((e: { nodes: { bodyCaptured: boolean; digest: { value: string } }[] }) => e.nodes.filter(n => n.bodyCaptured));
            const nodes = selected.getByTestId('ide-operator-node'); await expect(nodes).toHaveCount(captured.length);
            for (let index = 0; index < captured.length; index++) {
              const original = supplied.operatorBodies.find((body: { digest: { value: string } }) => body.digest.value === captured[index].digest.value);
              const node = nodes.nth(index); const details = node.locator('details');
              await details.locator('summary').focus(); await details.locator('summary').press('Space');
              const text = await details.locator('pre').textContent(); expect(text).toBe(original.text);
              expect(hash('coglatas.observed-predicate-node/1', text ?? '')).toEqual(original.digest);
              const body = JSON.parse(original.text); await expect(node.locator('strong')).toContainText(String(body.ordinal));
              if (body.literal) await expect(node).toContainText(body.literal.digest.value);
            }
            if (captured.length) {
              const evaluation = selected.getByTestId('ide-operator-evaluation').locator(':scope > details');
              await evaluation.locator('summary').focus(); await evaluation.locator('summary').press('Space');
              const raw = await evaluation.locator('pre').textContent(); expect(raw).toBe(supplied.evaluationFrames[0].text);
              expect(hash('coglatas.local-predicate-evaluation/1', raw ?? '')).toEqual(supplied.evaluationFrames[0].digest);
            }
            if (role === 'budget' && key === 'fail') await expect(selected).toContainText(locale === 'en' ? 'Original capture byte limit exceeded' : '元の記録のバイト上限を超えました');
            if (role === 'diagnostic' && key === 'operator-nested-duplicates' && choice === 'available') {
              await expect(nodes).toHaveCount(6);
              const first = JSON.parse(supplied.operatorBodies.find((b: { digest: { value: string } }) => b.digest.value === captured[0].digest.value).text);
              expect(first.evaluatedChildren).toEqual([1, 4]); expect(first.skippedOperands).toBe(1);
              const premises = JSON.parse(supplied.evaluationFrames[0].text).premises;
              expect(premises).toHaveLength(3); expect(new Set(premises.map((d: { value: string }) => d.value)).size).toBe(2);
            }
            if (role === 'diagnostic' && key === 'fail' && choice === 'budget') {
              await expect(selected).toContainText(locale === 'en' ? 'Actual initial-root operator frames recorded' : '実際の初期ルート評価の演算子フレームを記録済み');
              await expect(selected.getByTestId('ide-operator-view-state')).toContainText(locale === 'en' ? 'No partial body set was retained' : '一部の本文だけを保持することはありません');
            }
          }
        }
      } else await expect(page.getByTestId('ide-operator-node')).toHaveCount(0);
      await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    await button(page, locale, 'Source', 'ソース'); await button(page, locale, 'Read fixed root Source', '固定されたルートソースを読む');
    await expect(page.getByTestId('ide-operator-recording')).toHaveCount(0); await expect(page.getByTestId('ide-operator-node')).toHaveCount(0);
    expect(writes).toEqual([]);
  });
}
for (const locale of ['en', 'ja'] as const) {
  test('clears visible original operators when selecting a new input: ' + locale, async ({ page }) => {
    const value = bundle.cases.find((row: { key: string }) => row.key === 'operator-nested-duplicates');
    await open(page, locale, value.key); await button(page, locale, roles[1][1], roles[1][2]);
    await diagnostic(page, locale, value.result.diagnostics[0]); await button(page, locale, 'Read selected original operators', '選択した元の演算子を読む');
    await expect(page.getByTestId('ide-operator-node')).toHaveCount(6);
    await page.getByTestId('ide-context').selectOption('unknown');
    await expect(page.getByTestId('ide-operator-recording')).toHaveCount(0); await expect(page.getByTestId('ide-operator-node')).toHaveCount(0);
  });
  for (const mode of ['body', 'unselected-body', 'origin', 'foreign-member', 'trust', 'snapshot', 'cancel-result', 'duplicate-run']) {
    test('refuses altered original operator member ' + mode + ' ' + locale, async ({ page }) => {
      const altered = structuredClone(bundle); const value = altered.cases.find((row: { key: string }) => row.key === 'fail'); const p = value.operatorRecordings;
      if (mode === 'body') p.diagnostic.diagnostics[0].available.operatorBodies[0].text += ' ';
      else if (mode === 'unselected-body') p.retry.diagnostics[0].available.operatorBodies[0].text += ' ';
      else if (mode === 'foreign-member') p.diagnostic = altered.cases.find((row: { key: string }) => row.key === 'unknown').operatorRecordings.diagnostic;
      else if (mode === 'cancel-result') p.cancelled.result = p.original.result;
      else if (mode === 'snapshot') p.diagnostic.diagnostics[0].budget.frame.text += ' ';
      else {
        const data = JSON.parse(p.diagnostic.manifest.text);
        if (mode === 'origin') data.origin.runId = '00000099-0000-4000-8000-000000000001';
        if (mode === 'trust') data.authority = 'Allowed';
        if (mode === 'duplicate-run') data.runId = JSON.parse(p.original.manifest.text).runId;
        p.diagnostic.manifest.text = JSON.stringify(data); p.diagnostic.manifest.digest = hash(data.format, p.diagnostic.manifest.text);
      }
      await page.route('**/assets/projectide/operator-recordings-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(altered) }));
      await open(page, locale, 'fail'); await button(page, locale, roles[1][1], roles[1][2]);
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'could not be verified' : '検証できませんでした');
      await expect(page.getByTestId('ide-operator-recording')).toHaveCount(0); await expect(page.getByTestId('ide-operator-node')).toHaveCount(0);
      await expect(page.getByTestId('ide-root-source')).toHaveCount(1); await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    });
  }
}

for (const locale of ['en', 'ja'] as const) {
  test('waits for the new operator profile before selecting a shared context: ' + locale, async ({ page }) => {
    let requested!: () => void; const request = new Promise<void>(resolve => requested = resolve);
    let release!: () => void; const response = new Promise<void>(resolve => release = resolve);
    await page.route('**/assets/projectide/operator-recordings-v1.json', async route => {
      requested(); await response; await route.continue();
    });
    const opening = open(page, locale, 'unknown');
    await request;
    try { await expect(page.getByTestId('ide-context')).toHaveCount(0); } finally { release(); }
    await opening; await expect(page.getByTestId('ide-context')).toHaveValue('unknown');
    await expect(page.getByTestId('ide-root-source')).toBeFocused();
    await expect(page.getByTestId('ide-analysis-status')).toContainText(bundle.cases.find((v: { key: string }) => v.key === 'unknown').result.verdict);
  });
}

for (const locale of ['en', 'ja'] as const) for (const key of ['operator-requirement-false-nested', 'operator-requirement-true-short-circuit', 'operator-requirement-unknown']) {
  test('keeps original Requirement condition graphs separate from missing evidence: ' + key + ' ' + locale, async ({ page }) => {
    const value = bundle.cases.find((row: { key: string }) => row.key === key);
    expect(value.result.evidenceComplete).toBe(false);
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await open(page, locale, key);
    for (const [role, en, ja] of roles) {
      await button(page, locale, 'Source', 'ソース'); await button(page, locale, en, ja);
      const panel = page.getByTestId('ide-operator-recording'); await expect(panel).toBeFocused();
      const frame = value.operatorRecordings[role];
      if (role === 'cancelled') await expect(panel.getByTestId('ide-operator-no-result')).toContainText('Compiler');
      else await expect(panel.getByTestId('ide-operator-visited')).toHaveText(String(JSON.parse(frame.observation.text).observedNodes));
      for (const [index, entry] of value.result.diagnostics.entries()) {
        await diagnostic(page, locale, entry);
        const selected = page.getByTestId('ide-recorded-operators'); await expect(selected.getByTestId('ide-operator-node')).toHaveCount(0);
        if (role === 'cancelled') { await expect(selected.getByRole('button')).toHaveCount(0); continue; }
        for (const [choice, readEn, readJa] of [['manifest', 'Read premise manifest', '根拠のマニフェストを読む'], ['available', 'Read selected original operators', '選択した元の演算子を読む'], ['budget', 'Read with one-byte viewing limit', '表示上限1バイトで読む']]) {
          await button(page, locale, readEn, readJa); await expect(selected).toBeFocused();
          const supplied = frame.diagnostics[index][choice]; const snapshot = JSON.parse(supplied.frame.text);
          const captured = snapshot.evaluations.flatMap((e: { nodes: { bodyCaptured: boolean; digest: { value: string } }[] }) => e.nodes.filter(n => n.bodyCaptured));
          const nodes = selected.getByTestId('ide-operator-node'); await expect(nodes).toHaveCount(captured.length);
          if (entry.code === 'coglatas.analysis.requirement-evidence-unavailable') {
            expect(captured).toHaveLength(0); expect(supplied.evaluationFrames).toHaveLength(0);
            if ((role === 'diagnostic' || role === 'retry') && choice === 'available') await expect(selected.getByTestId('ide-operator-view-state')).toContainText(locale === 'en' ? 'No observed initial-root evaluation for this published diagnostic' : 'この公開済み診断に対応する初期ルート評価の観測なし');
          } else if (captured.length) {
            expect(entry.code).toBe('coglatas.analysis.requirement-condition-false'); expect(captured).toHaveLength(6);
            for (let ordinal = 0; ordinal < captured.length; ordinal++) {
              const original = supplied.operatorBodies.find((body: { digest: { value: string } }) => body.digest.value === captured[ordinal].digest.value);
              const details = nodes.nth(ordinal).locator('details'); await details.locator('summary').focus(); await details.locator('summary').press('Space');
              const text = await details.locator('pre').textContent(); expect(text).toBe(original.text);
              expect(hash('coglatas.observed-predicate-node/1', text ?? '')).toEqual(original.digest);
            }
            const evaluation = selected.getByTestId('ide-operator-evaluation').locator(':scope > details');
            await evaluation.locator('summary').focus(); await evaluation.locator('summary').press('Space');
            const raw = await evaluation.locator('pre').textContent(); expect(raw).toBe(supplied.evaluationFrames[0].text);
            expect(hash('coglatas.local-predicate-evaluation/1', raw ?? '')).toEqual(supplied.evaluationFrames[0].digest);
          }
          await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
        }
      }
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
  });
}
