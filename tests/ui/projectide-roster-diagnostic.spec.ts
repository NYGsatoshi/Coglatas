import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '@playwright/test';

// Diagnostic-only case for Issue #1959: list original source IDs without changing workflows.
test('enumerates all 26 original ProjectIDE Playwright spec test IDs', async ({}, testInfo) => {
  const root = process.cwd();
  const suiteDirectory = join(root, 'tests', 'ui');
  const specs = readdirSync(suiteDirectory)
    .filter((name) => /^projectide-.*\.spec\.ts$/.test(name) && name !== 'projectide-roster-diagnostic.spec.ts')
    .sort();
  expect(specs).toHaveLength(26);
  const cli = join(root, 'node_modules', '@playwright', 'test', 'cli.js');
  expect(existsSync(cli)).toBe(true);
  const command = [cli, 'test', '--config', 'playwright.config.ts', '--list', ...specs.map((name) => 'tests/ui/' + name)];
  const started = Date.now();
  const listing = execFileSync(process.execPath, command, {
    cwd: root,
    encoding: 'utf8',
    timeout: 120_000,
    maxBuffer: 16 * 1024 * 1024,
    env: { ...process.env, FORCE_COLOR: '0' }
  });
  const elapsedMs = Date.now() - started;
  const normalized = listing.replace(/\u001b\[[0-9;]*m/g, '');
  const rows = normalized.split(/\r?\n/).map((line) => line.trim())
    .filter((line) => /^\[[^\]]+\]\s*›\s*/.test(line));
  const uniqueIds = [...new Set(rows)];
  expect(rows.length).toBeGreaterThan(0);
  expect(uniqueIds).toHaveLength(rows.length);
  const listSha256 = createHash('sha256').update(normalized, 'utf8').digest('hex');
  const manifestSha256 = createHash('sha256').update(specs.join('\n') + '\n', 'utf8').digest('hex');
  const perProject = Object.fromEntries(
    [...new Set(uniqueIds.map((row) => row.slice(1, row.indexOf(']'))))].map((name) =>
      [name, uniqueIds.filter((row) => row.startsWith('[' + name + ']')).length])
  );
  console.log('BATCH6_BROWSER_ROSTER_SUMMARY ' + JSON.stringify({
    canonical: 'c693503bb54976742e313de24f905b95d828d19c',
    specCount: specs.length, selectedIds: uniqueIds.length, perProject,
    manifestSha256, listSha256, listOnlyElapsedMs: elapsedMs,
    note: 'All 26 canonical paths; no 24-path candidate selected or browser runtime inferred'
  }));
  for (const id of uniqueIds) console.log('BATCH6_BROWSER_ROSTER_ID ' + id);
  await testInfo.attach('batch6-original-26-projectide-manifest.txt', {
    body: specs.join('\n') + '\n', contentType: 'text/plain'
  });
  await testInfo.attach('batch6-original-26-projectide-playwright-list.txt', {
    body: normalized, contentType: 'text/plain'
  });
});
