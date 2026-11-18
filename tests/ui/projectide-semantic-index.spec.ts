import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

for (const locale of ['en', 'ja'] as const) {
  for (const kind of ['base', 'hypothesis', 'draft'] as const) {
    test('navigates original full-index pages and typed regional candidates ' + locale + ' ' + kind, async ({ page }) => {
      await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
      const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
      await page.goto('/app/projectide/preview'); await page.getByTestId('ide-index-example').click();
      await page.getByTestId('ide-context').selectOption('tests-index-' + kind);
      await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
      await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click();
      const load = page.getByTestId('ide-load-index'); await load.focus(); await load.press('Enter');
      const panel = page.getByTestId('ide-semantic-index'); await expect(page.getByTestId('ide-index-symbol')).toHaveCount(32); await expect(panel).toBeFocused();
      const port = page.getByTestId('ide-index-symbol').filter({ hasText: '/payload/ports/00000058-0000-4000-8000-000000000001' });
      await port.focus(); await port.press('Enter'); await page.getByTestId('ide-index-find').click();
      await expect(page.getByTestId('ide-index-reference')).toHaveCount(32);
      const next = page.getByTestId('ide-index-references-next'); await next.focus(); await next.press('Enter');
      await expect(page.getByTestId('ide-index-reference')).toHaveCount(4); await expect(next).toBeDisabled(); await expect(panel).toBeFocused();
      await page.getByTestId('ide-index-reference').first().getByRole('button', { name: locale === 'en' ? 'Go to definition' : '定義へ移動', exact: true }).click();
      await expect(page.getByTestId('ide-scoped-source-target')).toContainText('/payload/ports/00000058-0000-4000-8000-000000000001');
      await page.getByTestId('ide-index-complete').click(); await expect(page.getByTestId('ide-index-candidate')).toHaveCount(5);
      const candidates = page.getByTestId('ide-index-candidate'); await candidates.filter({ hasText: '000000cd-0000-4000-8000-000000000001' }).first().focus(); await candidates.filter({ hasText: '000000cd-0000-4000-8000-000000000001' }).first().press('Enter');
      await expect(page.getByTestId('ide-scoped-source-target')).toContainText('000000cd-0000-4000-8000-000000000001');
      if (locale === 'en' && kind === 'base') await page.screenshot({ path: test.info().outputPath('semantic-index-source.png'), fullPage: true });
      await page.getByTestId('ide-index-symbols-next').click();
      const regional = page.getByTestId('ide-index-symbol').filter({ hasText: '00002904-0000-4000-8000-000000000001' }).filter({ hasText: '00000011-0000-4000-8000-000000000001' });
      await regional.focus(); await regional.press('Enter'); await page.getByTestId('ide-index-find').click();
      await expect(page.getByTestId('ide-index-reference')).toHaveCount(1);
      const ownedOrigin = page.getByTestId('ide-index-reference');
      await expect(ownedOrigin).toContainText('/source');
      await expect(ownedOrigin).toContainText('00000012-0000-4000-8000-000000000001');
      await ownedOrigin.getByTestId('ide-index-reference-open').click();
      await expect(page.getByTestId('ide-scoped-source-identity')).toContainText('00000012-0000-4000-8000-000000000001');
      await expect(page.getByTestId('ide-scoped-source-identity')).toContainText('00002904-0000-4000-8000-000000000001');
      await ownedOrigin.getByTestId('ide-index-reference-definition').click();
      await expect(page.getByTestId('ide-scoped-source-identity')).toContainText('00000011-0000-4000-8000-000000000001');
      await page.getByTestId('ide-context').selectOption('tests-index-partial-base');
      await expect(page.getByTestId('ide-index-symbol')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
    });
  }
  for (const kind of ['base', 'hypothesis', 'draft'] as const) {
    test('renders owned reference neighborhoods with keyboard Source navigation ' + locale + ' ' + kind, async ({ page }) => {
      await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
      const writes: string[] = []; page.on('request', request => { if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) writes.push(request.method()); });
      await page.goto('/app/projectide/preview'); await page.getByTestId('ide-index-example').click();
      await page.getByTestId('ide-context').selectOption('tests-index-' + kind);
      await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
      await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click(); await page.getByTestId('ide-load-index').click();
      const port = page.getByTestId('ide-index-symbol').filter({ hasText: '/payload/ports/00000058-0000-4000-8000-000000000001' });
      await port.focus(); await port.press('Enter'); await page.getByTestId('ide-index-find').click();
      const layout = page.getByTestId('ide-index-graph-toggle'); await layout.focus(); await layout.press('Space');
      await expect(layout).toHaveAttribute('aria-pressed', 'true'); await expect(page.getByTestId('ide-index-graph-origin')).toHaveCount(32);
      await expect(page.getByTestId('ide-index-reference')).toHaveCount(0); await expect(page.getByTestId('ide-index-graph-target')).toHaveCount(1);
      const edge = page.getByTestId('ide-index-reference-graph').locator('.reference-graph-edge').first();
      if ((page.viewportSize()?.width ?? 1280) <= 640) await expect(edge).toBeHidden(); else await expect(edge).toBeVisible();
      await page.getByTestId('ide-index-references-next').focus(); await page.getByTestId('ide-index-references-next').press('Enter');
      await expect(page.getByTestId('ide-index-graph-origin')).toHaveCount(4); await expect(page.getByTestId('ide-semantic-index')).toBeFocused();
      if (locale === 'en' && kind === 'base') await page.screenshot({ path: test.info().outputPath('reference-neighborhood.png'), fullPage: true });
      const target = page.getByTestId('ide-index-graph-target'); await target.focus(); await target.press('Enter');
      await expect(page.getByTestId('ide-scoped-source-target')).toContainText('/payload/ports/00000058-0000-4000-8000-000000000001');
      await page.getByTestId('ide-index-symbols-next').click();
      const regional = page.getByTestId('ide-index-symbol').filter({ hasText: '00002904-0000-4000-8000-000000000001' }).filter({ hasText: '00000011-0000-4000-8000-000000000001' });
      await regional.focus(); await regional.press('Enter'); await page.getByTestId('ide-index-find').click();
      await expect(page.getByTestId('ide-index-graph-origin')).toHaveCount(1);
      await page.getByTestId('ide-index-graph-origin').focus(); await page.getByTestId('ide-index-graph-origin').press('Enter');
      await expect(page.getByTestId('ide-scoped-source-identity')).toContainText('00000012-0000-4000-8000-000000000001');
      await expect(page.getByTestId('ide-scoped-source-path')).toContainText('/source');
      await page.getByTestId('ide-index-graph-target').focus(); await page.getByTestId('ide-index-graph-target').press('Enter');
      await expect(page.getByTestId('ide-scoped-source-identity')).toContainText('00000011-0000-4000-8000-000000000001');
      await layout.focus(); await layout.press('Space'); await expect(page.getByTestId('ide-index-reference-graph')).toHaveCount(0);
      await expect(page.getByTestId('ide-index-reference')).toHaveCount(1);
      await page.getByTestId('ide-context').selectOption('tests-index-partial-base');
      await expect(page.getByTestId('ide-index-reference-graph')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench'); expect(writes).toEqual([]);
    });
  }
  for (const altered of [false, true]) {
    test('refuses incomplete or altered index without borrowing targets ' + locale + ' altered=' + altered, async ({ page }) => {
      const raw = JSON.parse(await readFile('frontend/public/assets/projectide/semantic-index-v1.json', 'utf8'));
      if (altered) raw.cases[0].semanticIndex.referenceSearches[0].pages[0].scannedReferences++;
      await page.route('**/assets/projectide/semantic-index-v1.json', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(raw) }));
      await page.addInitScript(locale => { localStorage.setItem('coglatas.locale', locale); window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true }; }, locale);
      await page.goto('/app/projectide/preview'); await page.getByTestId('ide-index-example').click();
      await page.getByTestId('ide-context').selectOption(altered ? 'tests-index-base' : 'tests-index-partial-base');
      await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
      await page.getByRole('button', { name: locale === 'en' ? 'Source' : 'ソース', exact: true }).click(); await page.getByTestId('ide-load-index').click();
      await expect(page.getByTestId('ide-semantic-index').getByRole('alert')).toContainText(locale === 'en' ? 'Project symbols could not be verified for this input' : 'この入力のプロジェクトシンボルを検証できませんでした');
      await expect(page.getByTestId('ide-index-symbol')).toHaveCount(0); await expect(page.getByTestId('ide-scoped-source-target')).toHaveCount(0);
      await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    });
  }
}
