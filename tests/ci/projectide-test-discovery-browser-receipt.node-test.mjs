import test from 'node:test';
import assert from 'node:assert/strict';
import {
  EXPECTED_PROJECTS,
  EXPECTED_TITLES,
  REQUIRED_SPEC,
  validateBrowserReport
} from './projectide-test-discovery-browser-receipt.mjs';

function completeReport() {
  return {
    errors: [],
    stats: { expected: 8, unexpected: 0, skipped: 0, flaky: 0 },
    suites: [{
      file: REQUIRED_SPEC,
      specs: EXPECTED_TITLES.map(title => ({
        title,
        tests: EXPECTED_PROJECTS.map(projectName => ({
          projectName,
          expectedStatus: 'passed',
          status: 'expected',
          results: [{ status: 'passed' }]
        }))
      }))
    }]
  };
}

test('requires eight original English/Japanese Test Discovery cases in both Chromium profiles', () => {
  const items = validateBrowserReport(completeReport());
  assert.equal(items.length, 8);
  assert.equal(new Set(items.map(x => x.project + ':' + x.title)).size, 8);
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
  invalid.suites[0].file = 'tests/ui/unrelated.spec.ts';
  assert.throws(() => validateBrowserReport(invalid), /Unpinned|unpinned/);
});

test('rejects skips, failures, flaky outcomes and retries', () => {
  for (const [status, outcome, attempts] of [
    ['skipped', 'skipped', []],
    ['failed', 'unexpected', [{ status: 'failed' }]],
    ['passed', 'flaky', [{ status: 'failed' }, { status: 'passed' }]]
  ]) {
    const report = completeReport();
    const row = report.suites[0].specs[0].tests[0];
    row.status = outcome;
    row.results = attempts;
    assert.throws(() => validateBrowserReport(report), /Skipped, failed, flaky/);
  }
  const retries = completeReport();
  retries.suites[0].specs[1].tests[1].results.push({ status: 'passed' });
  assert.throws(() => validateBrowserReport(retries), /retried/);
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
