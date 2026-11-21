import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const bundle = JSON.parse(readFileSync('frontend/public/assets/projectide/pass-recordings-v1.json', 'utf8'));
const hash = (domain: string, text: string) => ({algorithm: 'sha-256', value: createHash('sha256').update(domain + String.fromCharCode(0) + text).digest('hex')});
const rewriteFrame = (frame: {text: string; digest: {algorithm: string; value: string}}, data: Record<string, unknown>) => {
  frame.text = JSON.stringify(data); frame.digest = hash(String(data['format']), frame.text);
};
const roles = [['original', 'Read original Manifest run', '元のManifest実行を読む'], ['diagnostic', 'Read captured diagnostic run', '記録済みの診断実行を読む'],
  ['budget', 'Read capture-limited run', '記録上限に達した実行を読む'], ['eventGap', 'Read event-limited run', 'イベント上限ありの実行を読む'],
  ['cancelled', 'Read cancelled attempt', 'キャンセルされた試行を読む'], ['retry', 'Read run after cancellation', 'キャンセル後の実行を読む']] as const;
async function button(page: Page, locale: 'en' | 'ja', en: string, ja: string) {
  const element = page.getByRole('button', {name: locale === 'en' ? en : ja, exact: true}); await element.focus(); await element.press('Enter');
}
async function open(page: Page, locale: 'en' | 'ja', key: string) {
  await page.addInitScript(locale => {localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = {'projectide.syntheticPreview': true};}, locale);
  await page.goto('/app/projectide/preview'); await button(page, locale, 'Open recorded pass example', 'パス記録の例を開く');
  await expect(page.getByTestId('ide-context').locator('option[value="operator-nested-duplicates"]')).toHaveCount(1);
  await expect(page.getByTestId('ide-context').locator('option')).toHaveCount(bundle.cases.length + 1);
  await page.getByTestId('ide-context').selectOption(key); await expect(page.getByTestId('ide-context')).toHaveValue(key);
  await button(page, locale, 'Load result fixture', '結果Fixtureを読み込む'); await button(page, locale, 'Source', 'ソース');
  await expect(page.getByTestId('ide-pass-recording-actions')).toHaveCount(0);
  await button(page, locale, 'Read fixed root Source', '固定されたルートソースを読む'); await expect(page.getByTestId('ide-root-source')).toBeFocused();
}
for (const locale of ['en', 'ja'] as const) for (const entry of bundle.cases) {
  test('reads original whole-pass boundaries and exact JSON by keyboard: ' + entry.key + ' ' + locale, async ({page}) => {
    const writes: string[] = []; page.on('request', request => {if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method());});
    await open(page, locale, entry.key);
    for (const [role, en, ja] of roles) {
      await button(page, locale, en, ja); const panel = page.getByTestId('ide-pass-recording'); await expect(panel).toBeFocused();
      const frame = entry.passRecordings[role]; const manifest = JSON.parse(frame.manifest.text); const response = JSON.parse(frame.response.text);
      await expect(panel.getByTestId('ide-pass-recording-run')).toHaveText(manifest.runId); await expect(panel).toContainText(response.attemptId);
      if (manifest.origin) await expect(panel).toContainText(manifest.origin.runId);
      await expect(page.getByTestId('ide-pass-body')).toHaveCount(0);
      if (role === 'cancelled') {
        await expect(panel.getByTestId('ide-pass-no-result')).toContainText('Compiler');
        await expect(panel.getByRole('button')).toHaveCount(0); await expect(page.getByTestId('ide-recorded-passes')).toHaveCount(0);
        expect(frame.result).toBeNull(); expect(frame.observation).toBeNull();
      } else {
        const record = JSON.parse(frame.observation.text); await expect(panel.getByTestId('ide-pass-visited')).toHaveText(String(record.visitedEvents));
        const steps = panel.getByTestId('ide-pass-events').locator('li'); await expect(steps).toHaveCount(record.retainedEvents);
        for (let i = 0; i < record.events.length; i++) await expect(steps.nth(i).locator('p')).toHaveText(record.events[i].gap);
        for (const [choice, readEn, readJa] of [['manifest', 'Read pass manifest', 'パスのマニフェストを読む'], ['available', 'Read original pass JSON', '元のパスJSONを読む'], ['budget', 'Read with one-byte viewing limit', '表示上限1バイトで読む']] as const) {
          await button(page, locale, readEn, readJa); const selected = page.getByTestId('ide-recorded-passes'); await expect(selected).toBeFocused();
          const supplied = frame.snapshots[choice]; const snapshot = JSON.parse(supplied.frame.text); const bodies = selected.getByTestId('ide-pass-body');
          await expect(bodies).toHaveCount(supplied.bodies.length);
          for (let i = 0; i < supplied.bodies.length; i++) {
            const details = bodies.nth(i); await details.locator('summary').focus(); await details.locator('summary').press('Space');
            const text = await details.locator('pre').textContent(); expect(text).toBe(supplied.bodies[i].text);
            expect(hash(supplied.bodies[i].domain, text ?? '')).toEqual(supplied.bodies[i].digest);
          }
          expect(snapshot.originalRecord.metadata.state).toBe(record.state);
          if (role === 'budget' || role === 'eventGap') {expect(snapshot.retainedBodyBytes).toBe(0); expect(supplied.bodies).toHaveLength(0);}
          if ((role === 'diagnostic' || role === 'retry') && choice === 'budget') {expect(snapshot.state).toBe('BodyBudgetExceeded'); expect(record.state).toBe('AvailableOriginalJson');}
          if (entry.key === 'test-assertion-navigation') expect(supplied.bodies.some((b: {domain: string}) => b.domain === 'coglatas.project-ir/1')).toBe(false);
          await expect(page.getByTestId('ide-analysis-status')).toContainText(entry.result.verdict);
        }
      }
    }
    if (entry.key.startsWith('operator-requirement-')) expect(entry.result.evidenceComplete).toBe(false);
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true); expect(writes).toEqual([]);
    await button(page, locale, 'Read fixed root Source', '固定されたルートソースを読む');
    await expect(page.getByTestId('ide-pass-recording')).toHaveCount(0); await expect(page.getByTestId('ide-pass-body')).toHaveCount(0);
  });
}
for (const locale of ['en', 'ja'] as const) {
  for (const mode of ['body', 'origin', 'foreign-member', 'cancel-result', 'snapshot', 'duplicate-run', 'known-input-bytes', 'known-source-bytes']) {
    test('refuses altered original pass member ' + mode + ' ' + locale, async ({page}) => {
      const altered = structuredClone(bundle); const entry = altered.cases[0]; const p = entry.passRecordings;
      if (mode === 'known-input-bytes' || mode === 'known-source-bytes') {
        const run = p.budget; const record = JSON.parse(run.observation.text);
        for (const event of record.events) for (const ref of event.originalBodies)
          if (mode === 'known-source-bytes' ? ['source', 'decoded-source'].includes(ref.role) : ref.role === 'input') ref.bytes++;
        rewriteFrame(run.observation, record);
        for (const view of Object.values(run.snapshots) as {frame: {text: string; digest: {algorithm: string; value: string}}}[]) {
          const snapshot = JSON.parse(view.frame.text); snapshot.originalRecord = {digest: run.observation.digest, metadata: record}; rewriteFrame(view.frame, snapshot);
        }
        const response = JSON.parse(run.response.text); response.observationDigest = run.observation.digest; rewriteFrame(run.response, response);
        const receipt = JSON.parse(run.receipt.text); receipt.observationRecordDigest = run.observation.digest; receipt.hostResponseDigest = run.response.digest; rewriteFrame(run.receipt, receipt);
        for (const [child, parent] of [['diagnostic', 'original'], ['budget', 'diagnostic'], ['eventGap', 'budget'], ['retry', 'cancelled']]) {
          const manifest = JSON.parse(p[child].manifest.text); manifest.origin = {runId: JSON.parse(p[parent].manifest.text).runId,
            manifestDigest: p[parent].manifest.digest, receiptDigest: p[parent].receipt.digest, attemptId: JSON.parse(p[parent].response.text).attemptId};
          rewriteFrame(p[child].manifest, manifest); const reply = JSON.parse(p[child].response.text); reply.manifestDigest = p[child].manifest.digest; rewriteFrame(p[child].response, reply);
          const linked = JSON.parse(p[child].receipt.text); linked.manifestDigest = p[child].manifest.digest; linked.hostResponseDigest = p[child].response.digest; rewriteFrame(p[child].receipt, linked);
        }
      } else if (mode === 'body') p.retry.snapshots.available.bodies[0].text += ' ';
      else if (mode === 'foreign-member') p.diagnostic = altered.cases[1].passRecordings.diagnostic;
      else if (mode === 'cancel-result') p.cancelled.result = p.original.result;
      else if (mode === 'snapshot') p.diagnostic.snapshots.budget.frame.text += ' ';
      else {const data = JSON.parse(p.diagnostic.manifest.text); if (mode === 'origin') data.origin.runId = '00000099-0000-4000-8000-000000000001';
        if (mode === 'duplicate-run') data.runId = JSON.parse(p.original.manifest.text).runId;
        p.diagnostic.manifest.text = JSON.stringify(data); p.diagnostic.manifest.digest = hash(data.format, p.diagnostic.manifest.text);}
      await page.route('**/assets/projectide/pass-recordings-v1.json', route => route.fulfill({status: 200, contentType: 'application/json', body: JSON.stringify(altered)}));
      await open(page, locale, entry.key); await button(page, locale, roles[1][1], roles[1][2]);
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'could not be verified' : '検証できませんでした');
      await expect(page.getByTestId('ide-pass-recording')).toHaveCount(0); await expect(page.getByTestId('ide-pass-body')).toHaveCount(0);
      await expect(page.getByTestId('ide-root-source')).toHaveCount(1); await expect(page.getByTestId('ide-analysis-status')).toContainText(entry.result.verdict);
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    });
  }
  test('withdraws original pass bodies when selecting a new input: ' + locale, async ({page}) => {
    await open(page, locale, 'fail'); await button(page, locale, roles[1][1], roles[1][2]); await button(page, locale, 'Read original pass JSON', '元のパスJSONを読む');
    await expect(page.getByTestId('ide-pass-body')).not.toHaveCount(0); await page.getByTestId('ide-context').selectOption('unknown');
    await expect(page.getByTestId('ide-pass-recording')).toHaveCount(0); await expect(page.getByTestId('ide-pass-body')).toHaveCount(0);
  });
  test('waits for the new pass profile before selecting a shared context: ' + locale, async ({page}) => {
    let requested!: () => void; const request = new Promise<void>(resolve => requested = resolve);
    let release!: () => void; const response = new Promise<void>(resolve => release = resolve);
    await page.route('**/assets/projectide/pass-recordings-v1.json', async route => {requested(); await response; await route.continue();});
    const opening = open(page, locale, 'unknown'); await request;
    try {await expect(page.getByTestId('ide-context')).toHaveCount(0);} finally {release();}
    await opening; await expect(page.getByTestId('ide-context')).toHaveValue('unknown'); await expect(page.getByTestId('ide-root-source')).toBeFocused();
  });
}
