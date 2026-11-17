import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
const bundle = JSON.parse(readFileSync('frontend/public/assets/projectide/regional-operator-recordings-v1.json', 'utf8'));
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + String.fromCharCode(0)).update(text).digest('hex') });
const roles = ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const;
async function button(page: Page, locale: 'en' | 'ja', en: string, ja: string) {
  const element = page.getByRole('button', { name: locale === 'en' ? en : ja, exact: true }); await element.focus(); await element.press('Enter');
}
async function open(page: Page, locale: 'en' | 'ja', key: string, scoped = true) {
  await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
  await page.goto('/app/projectide/preview');
  await page.getByTestId('ide-regional-operator-example').focus(); await page.getByTestId('ide-regional-operator-example').press('Enter');
  await expect(page.getByTestId('ide-context').locator('option[value="tests-regional-versioned-base"]')).toHaveCount(1);
  await expect(page.getByTestId('ide-context').locator('option')).toHaveCount(bundle.cases.length + 1);
  await page.getByTestId('ide-context').selectOption(key); await button(page, locale, 'Load result fixture', '結果Fixtureを読み込む');
  await button(page, locale, 'Source', 'ソース'); await expect(page.getByTestId('ide-regional-require-source')).toHaveCount(1);
  await button(page, locale, scoped ? 'Load fixed sources' : 'Read fixed root Source', scoped ? '固定ソースを読み込む' : '固定されたルートソースを読む');
  await expect(page.getByTestId(scoped ? 'ide-scoped-sources' : 'ide-root-source')).toHaveCount(1);
}
for (const locale of ['en', 'ja'] as const) for (const value of bundle.cases) {
  test('reads actual regional host pairs and original selected operators by keyboard: ' + value.key + ' ' + locale, async ({ page }) => {
    const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
    await open(page, locale, value.key);
    for (const role of roles) {
      const read = page.getByTestId('ide-regional-' + role); await read.focus(); await read.press('Enter');
      const panel = page.getByTestId('ide-regional-operator-recording'), run = value.regionalOperatorRecordings[role];
      await expect(panel).toBeFocused(); await expect(panel.getByTestId('ide-regional-run')).toHaveText(JSON.parse(run.manifest.text).runId);
      await expect(page.getByTestId('ide-regional-node')).toHaveCount(0);
      if (role === 'cancelled') { await expect(panel.getByTestId('ide-regional-no-result')).toContainText('Compiler'); await expect(panel.getByTestId('ide-regional-region')).toHaveCount(0); continue; }
      await expect(panel.getByTestId('ide-regional-region')).toHaveCount(run.regions.length);
      for (const [regionIndex, region] of run.regions.entries()) for (const [diagnosticIndex, entry] of region.diagnostics.entries()) {
        const select = panel.getByTestId('ide-regional-region').nth(regionIndex).getByTestId('ide-regional-select-diagnostic').nth(diagnosticIndex);
        await select.focus(); await select.press('Space'); await expect(select).toHaveAttribute('aria-pressed', 'true');
        const selected = page.getByTestId('ide-recorded-regional-operators'); await expect(selected.getByTestId('ide-regional-node')).toHaveCount(0);
        for (const choice of ['manifest', 'available', 'budget'] as const) {
          const read = selected.getByTestId('ide-regional-read-' + choice); await read.focus(); await read.press('Enter'); await expect(selected).toBeFocused();
          const supplied = entry[choice], snapshot = JSON.parse(supplied.frame.text);
          const retained = snapshot.evaluations.flatMap((e: { nodes: { bodyCaptured: boolean; digest: { value: string } }[] }) => e.nodes.filter(n => n.bodyCaptured));
          const nodes = selected.getByTestId('ide-regional-node'); await expect(nodes).toHaveCount(retained.length);
          for (let ordinal = 0; ordinal < retained.length; ordinal++) {
            const body = supplied.operatorBodies.find((b: { digest: { value: string } }) => b.digest.value === retained[ordinal].digest.value);
            const details = nodes.nth(ordinal).locator('details'); await details.locator('summary').focus(); await details.locator('summary').press('Space');
            const text = await details.locator('pre').textContent(); expect(text).toBe(body.text); expect(hash('coglatas.observed-predicate-node/1', text ?? '')).toEqual(body.digest);
          }
          const evaluations = selected.getByTestId('ide-regional-evaluation');
          for (const [index, frame] of supplied.evaluationFrames.entries()) {
            const details = evaluations.nth(index).locator(':scope > details'); await details.locator('summary').focus(); await details.locator('summary').press('Space');
            const text = await details.locator('pre').textContent(); expect(text).toBe(frame.text); expect(hash('coglatas.local-predicate-evaluation/1', text ?? '')).toEqual(frame.digest);
          }
          if (value.key === 'tests-regional-nested-duplicates-base' && locale === 'en' && role === 'diagnostic' && choice === 'available') {
            await test.info().attach('original-regional-operators', { body: await selected.screenshot(), contentType: 'image/png' });
          }
          const diagnostic = JSON.parse(entry.diagnostic.text);
          if (diagnostic.code === 'coglatas.analysis.requirement-evidence-unavailable') { expect(retained).toHaveLength(0); expect(supplied.evaluationFrames).toHaveLength(0); }
          await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
        }
        const navigate = selected.getByTestId('ide-regional-navigate'); await navigate.focus(); await navigate.press('Enter');
        await expect(page.getByTestId('ide-scoped-source-target')).toBeFocused();
        const context = JSON.parse(region.source.text).context;
        await expect(page.getByTestId('ide-scoped-source-target')).toContainText(context.kind === 'coglatas.committed' ? context.revisionId : context.kind === 'coglatas.candidate' ? context.candidateRevisionId : context.scenarioRevisionId);
      }
    }
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true); expect(writes).toEqual([]);
    await button(page, locale, 'Load fixed sources', '固定ソースを読み込む'); await expect(page.getByTestId('ide-regional-operator-recording')).toHaveCount(0);
    await expect(page.getByTestId('ide-regional-node')).toHaveCount(0);
  });
}
for (const locale of ['en', 'ja'] as const) {
  test('requires scoped Sources before exposing external regional diagnostics: ' + locale, async ({ page }) => {
    await open(page, locale, 'tests-regional-versioned-base', false);
    await page.getByTestId('ide-regional-diagnostic').focus(); await page.getByTestId('ide-regional-diagnostic').press('Enter');
    await expect(page.getByTestId('ide-regional-region')).toHaveCount(1); await expect(page.getByTestId('ide-regional-select-diagnostic')).toHaveCount(0);
    await button(page, locale, 'Load fixed sources', '固定ソースを読み込む'); await expect(page.getByTestId('ide-regional-operator-recording')).toHaveCount(0);
  });
  test('withdraws selected original frames after choosing another input: ' + locale, async ({ page }) => {
    await open(page, locale, 'tests-regional-false-base'); await page.getByTestId('ide-regional-diagnostic').press('Enter');
    const select = page.getByTestId('ide-regional-select-diagnostic'); await select.focus(); await select.press('Space');
    await page.getByTestId('ide-regional-read-available').press('Enter'); await expect(page.getByTestId('ide-regional-node')).toHaveCount(1);
    await page.getByTestId('ide-context').selectOption('tests-regional-unknown-base'); await expect(page.getByTestId('ide-regional-operator-recording')).toHaveCount(0);
    await expect(page.getByTestId('ide-regional-node')).toHaveCount(0);
  });
  for (const mode of ['body', 'unselected-body', 'region', 'foreign-member', 'origin', 'trust', 'snapshot', 'cancel-result']) {
    test('refuses altered original regional member ' + mode + ' ' + locale, async ({ page }) => {
      const altered = structuredClone(bundle), value = altered.cases.find((c: { key: string }) => c.key === 'tests-regional-versioned-base'), p = value.regionalOperatorRecordings;
      const observed = (run: typeof p.diagnostic) => run.regions.flatMap((r: { diagnostics: { available: { operatorBodies: { text: string }[] }; budget: { frame: { text: string } } }[] }) => r.diagnostics).find((d: { available: { operatorBodies: unknown[] } }) => d.available.operatorBodies.length)!;
      if (mode === 'body') observed(p.diagnostic).available.operatorBodies[0].text += ' ';
      else if (mode === 'unselected-body') observed(p.retry).available.operatorBodies[0].text += ' ';
      else if (mode === 'region') p.diagnostic.regions[2] = structuredClone(p.diagnostic.regions[1]);
      else if (mode === 'foreign-member') p.diagnostic = altered.cases[0].regionalOperatorRecordings.diagnostic;
      else if (mode === 'snapshot') observed(p.diagnostic).budget.frame.text += ' ';
      else if (mode === 'cancel-result') p.cancelled.result = p.original.result;
      else { const data = JSON.parse(p.diagnostic.manifest.text); if (mode === 'origin') data.origin.runId = JSON.parse(p.cancelled.manifest.text).runId; else data.authority = 'Approved';
        p.diagnostic.manifest.text = JSON.stringify(data); p.diagnostic.manifest.digest = hash(data.format, p.diagnostic.manifest.text); }
      await page.route('**/assets/projectide/regional-operator-recordings-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(altered) }));
      await open(page, locale, value.key); await page.getByTestId('ide-regional-diagnostic').press('Enter');
      await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'could not be verified' : '検証できませんでした');
      await expect(page.getByTestId('ide-regional-operator-recording')).toHaveCount(0); await expect(page.getByTestId('ide-regional-node')).toHaveCount(0);
      await expect(page.getByTestId('ide-scoped-sources')).toHaveCount(1); await expect(page.getByTestId('ide-analysis-status')).toContainText(value.result.verdict);
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    });
  }
}
