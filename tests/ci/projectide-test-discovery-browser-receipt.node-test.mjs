import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rmdir, unlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import {
  EXPECTED_PROJECTS,
  EXPECTED_TITLES,
  REQUIRED_SPEC,
  validateBrowserReport,
  verifyServedBuild
} from './projectide-test-discovery-browser-receipt.mjs';

const reportFile = path.posix.basename(REQUIRED_SPEC);

function completeReport() {
  return {
    errors: [],
    stats: { expected: 8, unexpected: 0, skipped: 0, flaky: 0 },
    suites: [{
      file: reportFile,
      specs: EXPECTED_TITLES.flatMap((title, titleIndex) => EXPECTED_PROJECTS.map((projectName, projectIndex) => ({
        id: `case-${titleIndex}-${projectIndex}`,
        file: reportFile,
        title,
        tests: [{
          projectName,
          expectedStatus: 'passed',
          status: 'expected',
          results: [{ retry: 0, status: 'passed' }]
        }]
      })))
    }]
  };
}

test('requires eight original English/Japanese Test Discovery cases in both Chromium profiles', () => {
  const items = validateBrowserReport(completeReport());
  assert.equal(items.length, 8);
  assert.equal(new Set(items.map(x => x.project + ':' + x.title)).size, 8);
  assert.equal(new Set(items.map(x => x.id)).size, 8);
});

test('rejects no tests and no reporter errors inventory', () => {
  const empty = completeReport();
  empty.suites = [];
  assert.throws(() => validateBrowserReport(empty), /roster count/);
  const invalid = completeReport();
  delete invalid.errors;
  assert.throws(() => validateBrowserReport(invalid), /error inventory/);
});

test('rejects missing locale, project and duplicate tests', () => {
  const locale = completeReport();
  locale.suites[0].specs.pop();
  assert.throws(() => validateBrowserReport(locale), /roster count/);
  const project = completeReport();
  project.suites[0].specs[0].tests.pop();
  assert.throws(() => validateBrowserReport(project), /roster count/);
  const duplicate = completeReport();
  duplicate.suites[0].specs[0].tests[0].projectName = 'chromium-mobile';
  assert.throws(() => validateBrowserReport(duplicate), /duplicate or unexpected/);
});

test('rejects wrong or foreign browser spec, even with passing test names', () => {
  const invalid = completeReport();
  invalid.suites[0].specs[0].file = 'nested/' + reportFile;
  assert.throws(() => validateBrowserReport(invalid), /unpinned/);
});

test('rejects missing and duplicate Playwright IDs', () => {
  const missing = completeReport();
  delete missing.suites[0].specs[0].id;
  assert.throws(() => validateBrowserReport(missing), /Playwright test ID/);
  const duplicate = completeReport();
  duplicate.suites[0].specs[1].id = duplicate.suites[0].specs[0].id;
  assert.throws(() => validateBrowserReport(duplicate), /Playwright test ID/);
});

test('rejects skips, failures, flaky outcomes and retries', () => {
  for (const [status, outcome, attempts] of [
    ['skipped', 'skipped', []],
    ['failed', 'unexpected', [{ retry: 0, status: 'failed' }]],
    ['passed', 'flaky', [{ retry: 0, status: 'failed' }, { retry: 1, status: 'passed' }]]
  ]) {
    const report = completeReport();
    const row = report.suites[0].specs[0].tests[0];
    row.status = outcome;
    row.results = attempts;
    assert.throws(() => validateBrowserReport(report), /Skipped, failed, flaky/);
  }
  const retries = completeReport();
  retries.suites[0].specs[1].tests[0].results.push({ retry: 1, status: 'passed' });
  assert.throws(() => validateBrowserReport(retries), /retried/);
  const onlyRetry = completeReport();
  onlyRetry.suites[0].specs[1].tests[0].results[0].retry = 1;
  assert.throws(() => validateBrowserReport(onlyRetry), /retried/);
});

test('requires the served document and main script to match the fresh build', async () => {
  const root = await mkdtemp(path.join(tmpdir(), 'projectide-browser-build-'));
  const index = '<base href="/app/"><script src="main-X7.js"></script>';
  const script = 'const candidate = "this build";';
  const indexPath = path.join(root, 'index.html');
  const scriptPath = path.join(root, 'main-X7.js');
  await writeFile(indexPath, index);
  await writeFile(scriptPath, script);
  try {
    const expected = new Map([['/app/projectide/preview', index], ['/app/main-X7.js', script]]);
    assert.equal(await verifyServedBuild(root, url => new Response(expected.get(url.pathname))), 2);
    for (const pathname of expected.keys()) {
      const stale = url => new Response(url.pathname === pathname ? 'different build' : expected.get(url.pathname));
      await assert.rejects(verifyServedBuild(root, stale), /Served Angular output differs/);
    }
  } finally {
    await unlink(indexPath);
    await unlink(scriptPath);
    await rmdir(root);
  }
});

test('rejects global skipped/failed/incomplete counters', () => {
  for (const key of ['skipped', 'unexpected', 'flaky']) {
    const report = completeReport();
    report.stats[key] = 1;
    assert.throws(() => validateBrowserReport(report), /outcome counters/);
  }
  const partial = completeReport();
  partial.stats.expected = 7;
  assert.throws(() => validateBrowserReport(partial), /outcome counters/);
});

test('rejects out-of-scope browser cases and reporter suite errors', () => {
  const extra = completeReport();
  extra.suites[0].specs.push({
    title: 'unrelated case', tests: [{ projectName: 'chromium-desktop', expectedStatus: 'passed', status: 'expected', results: [{ status: 'passed' }] }]
  });
  assert.throws(() => validateBrowserReport(extra), /roster count/);
  const errors = completeReport();
  errors.errors.push({ message: 'runner failed' });
  assert.throws(() => validateBrowserReport(errors), /suite errors/);
});
