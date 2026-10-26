import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { chromium, devices, expect } from '@playwright/test';

// Fixed local controls: hold Angular's scheduled render, then compare the
// historical hidden-opener protocol with semantic readiness after release.
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:4489';
const target = new URL(baseURL);
assert.equal(target.hostname, '127.0.0.1');
assert.equal(target.protocol, 'http:');
const output = process.env.SEARCH_OPENER_CONTROL_OUTPUT;
assert.ok(output, 'A unique evidence output path is required.');
const repositoryRoot = fileURLToPath(new URL('../../', import.meta.url));
const git = (...args) => execFileSync('git', args, { cwd: repositoryRoot, encoding: 'utf8' }).trim();
const expectedSubjectSourceSha = 'ea7be66d3322bf2fc1ad608ad24e22bfba3efc90';
const expectedSubjectFrontendTree = 'c016252362f6e2ea1d6e3eb29c52909dbc8d1cca';
const producerSourceSha = git('rev-parse', 'HEAD');
assert.match(producerSourceSha, /^[0-9a-f]{40}$/);
assert.equal(git('status', '--porcelain', '--untracked-files=no'), '', 'Tracked producer files must be clean.');
assert.equal(git('rev-parse', 'HEAD:frontend'), expectedSubjectFrontendTree);
const scriptDigest = async () => createHash('sha256').update(await readFile(new URL(import.meta.url))).digest('hex');
const producerScriptSha256 = await scriptDigest();
const browser = await chromium.launch({ headless: true });
const results = [];
try {
  for (const control of ['HELD_RENDER_NEGATIVE', 'RELEASED_RENDER_POSITIVE']) {
    const context = await browser.newContext({
      ...devices['Pixel 5'], viewport: { width: 320, height: 800 },
      locale: 'en-US', timezoneId: 'UTC', reducedMotion: 'reduce',
      storageState: { cookies: [], origins: [{ origin: target.origin,
        localStorage: [{ name: 'coglatas.locale', value: 'en' }] }] }
    });
    const page = await context.newPage();
    let clockInstalled = false;
    try {
      await page.route('**/api/conversations**', async route => {
        if (new URL(route.request().url()).pathname !== '/api/conversations') {
          await route.fallback(); return;
        }
        await route.fulfill({ json: { items: [], page: 1, pageSize: 20,
          totalCount: 0, view: 'All', counts: { all: 0, unread: 0, mentions: 0, later: 0 } } });
      });
      await page.goto(`${target.origin}/app/messages?q=synthetic-readiness-canary`);
      await expect.poll(() => new URL(page.url()).searchParams.has('q')).toBe(false);
      const toggle = page.getByTestId('message-filter-drawer-toggle');
      const opener = page.getByTestId('message-advanced-filters-open');
      await expect(toggle).toBeVisible();
      await expect(toggle).toHaveAttribute('aria-expanded', 'false');
      await expect(opener).toBeHidden();
      await page.evaluate(() => {
        const host = document.querySelector('[data-testid="message-search-filters"]');
        const role = node => node?.closest?.('[data-testid="message-filter-drawer-toggle"]') ? 'TOGGLE'
          : node?.closest?.('[data-testid="message-advanced-filters-open"]') ? 'OPENER' : 'OTHER';
        const events = [];
        let truncated = false;
        const record = event => {
          if (event.type !== 'click' && event.key !== 'Enter') return;
          if (events.length >= 32) { truncated = true; return; }
          events.push({ kind: event.type.toUpperCase(), target: role(event.target),
            active: role(document.activeElement), trusted: event.isTrusted,
            sameHost: host === document.querySelector('[data-testid="message-search-filters"]'),
            connected: host?.isConnected === true });
        };
        for (const kind of ['keydown', 'keyup', 'click']) document.addEventListener(kind, record, true);
        window.__searchOpenerControl = { events, isTruncated: () => truncated, snapshot: () => ({
          expanded: document.querySelector('[data-testid="message-filter-drawer-toggle"]')?.getAttribute('aria-expanded') === 'true',
          openerVisible: !!document.querySelector('[data-testid="message-advanced-filters-open"]')?.getClientRects().length,
          active: role(document.activeElement),
          drawerVisible: !!document.querySelector('[data-testid="message-advanced-filters-drawer"]')?.getClientRects().length,
          sameHost: host === document.querySelector('[data-testid="message-search-filters"]'), connected: host?.isConnected === true
        }) };
      });
      const fixedTime = new Date('2026-10-26T00:00:00Z');
      await page.clock.install({ time: fixedTime });
      clockInstalled = true;
      await page.clock.pauseAt(new Date(fixedTime.getTime() + 1000));
      await toggle.press('Enter');
      await expect(page.getByTestId('message-search-input')).toHaveValue('');
      const held = await page.evaluate(() => window.__searchOpenerControl.snapshot());
      assert.equal(held.expanded, false, 'The scheduled render must actually remain held.');
      assert.equal(held.openerVisible, false);
      assert.equal(held.active, 'TOGGLE');
      if (control === 'HELD_RENDER_NEGATIVE') {
        await opener.focus();
        await expect(toggle).toBeFocused();
        await opener.press('Enter');
        await page.clock.resume();
        await expect(toggle).toHaveAttribute('aria-expanded', 'false');
        await expect(page.getByTestId('message-advanced-filters-drawer')).toBeHidden();
      } else {
        await page.clock.resume();
        await expect(toggle).toHaveAttribute('aria-expanded', 'true');
        await expect(opener).toBeVisible();
        await opener.focus();
        await expect(opener).toBeFocused();
        await opener.press('Enter');
        await expect(page.getByTestId('message-advanced-filters-drawer')).toBeVisible();
        await expect(page.getByTestId('message-advanced-author')).toBeFocused();
      }
      const observation = await page.evaluate(() => ({
        events: window.__searchOpenerControl.events,
        truncated: window.__searchOpenerControl.isTruncated(),
        final: window.__searchOpenerControl.snapshot()
      }));
      assert.equal(observation.truncated, false);
      const keys = observation.events.filter(event => event.kind === 'KEYDOWN').map(event => event.target);
      assert.deepEqual(keys, control === 'HELD_RENDER_NEGATIVE' ? ['TOGGLE', 'TOGGLE'] : ['TOGGLE', 'OPENER']);
      assert.ok(observation.events.every(event => event.sameHost && event.connected && event.trusted));
      results.push({ control, outcome: 'PASS', held, ...observation });
    } catch (error) {
      if (clockInstalled) await page.clock.resume().catch(() => {});
      results.push({ control, outcome: 'FAIL', errorKind: error.name === 'AssertionError' ? 'ASSERTION' : 'CONTROL_SETUP_OR_BROWSER' });
    } finally { await context.close(); }
  }
} finally { await browser.close(); }
assert.equal(git('rev-parse', 'HEAD'), producerSourceSha, 'Producer HEAD changed during execution.');
assert.equal(git('status', '--porcelain', '--untracked-files=no'), '', 'Tracked producer files changed during execution.');
assert.equal(await scriptDigest(), producerScriptSha256, 'Producer script changed during execution.');
const receipt = { schemaVersion: 1, producerSourceSha, producerScriptSha256,
  expectedSubjectSourceSha, expectedSubjectFrontendTree, results,
  limits: ['Synthetic static HTTP; no backend qualification.',
    'Expected subject constants do not measure the served build; binding requires external build/asset evidence.',
    'Controlled render gating does not establish the hosted initiating cause.'] };
assert.ok(Buffer.byteLength(JSON.stringify(receipt)) <= 16 * 1024);
await writeFile(output, JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
for (const result of results) process.stdout.write(`${result.control}: ${result.outcome}\n`);
assert.ok(results.every(result => result.outcome === 'PASS'), 'Fixed control failure retained in receipt.');
