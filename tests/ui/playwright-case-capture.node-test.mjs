import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import test from 'node:test';
import { NativeCaseCapture } from './playwright-case-capture.mjs';
import { hashPlaywrightTestId } from './playwright-shard-evidence.mjs';

const metadata = { sourceSha: 'a'.repeat(40), executionKind: 'LOCAL', runId: 'local-' + 'c'.repeat(32), runAttempt: 1 };
const caseOf = (id = 'native-case', expectedStatus = 'passed', outcome = 'expected') =>
  ({ id, expectedStatus, outcome: () => outcome });
const capture = (cases = [caseOf()]) => {
  const observer = new NativeCaseCapture();
  observer.onBegin({}, { allTests: () => cases });
  return observer;
};

test('capture reuses the integrated hash and serializes only allowlisted native fields', () => {
  const native = { ...caseOf('PRIVATE_NATIVE_CANARY'), title: 'PRIVATE_BODY_CANARY', location: { file: 'PRIVATE_PATH' } };
  const observer = capture([native]);
  observer.onTestEnd(native, { retry: 0, status: 'passed', error: { message: 'PRIVATE_ERROR' }, stdout: ['PRIVATE_STDOUT'] });
  assert.equal(observer.onEnd({ status: 'passed' }), undefined);
  const record = observer.snapshot(metadata);
  assert.equal(record.captureComplete, true);
  assert.equal(record.tests[0].id, createHash('sha256').update('coglatas.playwright-test-id.v1\0').update(native.id).digest('hex'));
  assert.equal(record.tests[0].id, hashPlaywrightTestId(native.id));
  assert.doesNotMatch(JSON.stringify(record), /PRIVATE_/);
  assert.equal(observer.printsToStdio(), false);
});

test('native failed attempt and later success remain separate, without causal flake attribution', () => {
  const native = caseOf('retry', 'passed', 'flaky');
  const observer = capture([native]);
  observer.onTestEnd(native, { retry: 0, status: 'failed' });
  observer.onTestEnd(native, { retry: 1, status: 'passed' });
  observer.onEnd({ status: 'passed' });
  const record = observer.snapshot(metadata);
  assert.deepEqual(record.tests[0].attempts, [{ retry: 0, status: 'failed' }, { retry: 1, status: 'passed' }]);
  assert.equal(record.tests[0].outcome, 'flaky'); // Original SDK enum only, not a causal diagnosis.
});

test('list-only, interrupted, duplicate, global-error and unknown events cannot appear complete', () => {
  const native = caseOf();
  const list = capture([native]); list.onEnd({ status: 'passed' });
  assert.equal(list.snapshot(metadata).captureComplete, false);
  const duplicate = capture([native]);
  duplicate.onTestEnd(native, { retry: 0, status: 'passed' });
  duplicate.onTestEnd(native, { retry: 0, status: 'passed' });
  duplicate.onEnd({ status: 'passed' });
  assert.ok(duplicate.snapshot(metadata).captureErrors.includes('DUPLICATE_RESULT'));
  const interrupted = capture([native]);
  interrupted.onTestEnd(native, { retry: 0, status: 'interrupted' });
  interrupted.onEnd({ status: 'interrupted' });
  assert.equal(interrupted.snapshot(metadata).status, 'interrupted');
  const error = capture([native]); error.onError({ message: 'PRIVATE_ERROR' }); error.onEnd({ status: 'failed' });
  assert.equal(error.snapshot(metadata).globalErrors, 1);
  assert.equal(error.snapshot(metadata).captureComplete, false);
  const unknown = capture([native]); unknown.onTestEnd(caseOf('other'), { retry: 0, status: 'passed' });
  unknown.onEnd({ status: 'passed' });
  assert.ok(unknown.snapshot(metadata).tests.some(row => row.listed === false));
});

test('duplicate begin, invalid IDs/statuses and excessive case/attempt bounds fail closed', () => {
  for (const cases of [[], [caseOf('')], [caseOf('duplicate'), caseOf('duplicate')],
                       [caseOf('unsupported', 'failed')], Array.from({ length: 5001 }, (_, i) => caseOf(String(i)))]) {
    const observer = capture(cases); observer.onEnd({ status: 'passed' });
    assert.equal(observer.snapshot(metadata).captureComplete, false);
  }
  const observer = capture(); observer.onBegin({}, { allTests: () => [caseOf()] });
  observer.onTestEnd(caseOf(), { retry: 10, status: 'passed' }); observer.onEnd({ status: 'passed' });
  assert.equal(observer.snapshot(metadata).captureComplete, false);
  assert.throws(() => observer.snapshot({ ...metadata, runId: 'PRIVATE_TOKEN' }), /CASE_CAPTURE_REFUSED/);
  assert.throws(() => observer.snapshot({ ...metadata, sourceSha: 'wrong' }), /CASE_CAPTURE_REFUSED/);
});

function flatten(suites) {
  return suites.flatMap(suite => [...(suite.specs ?? []), ...flatten(suite.suites ?? [])]);
}

test('actual Playwright JSON-list spec.id equals actual reporter TestCase.id for fixture runs', () => {
  const root = fileURLToPath(new URL('../../', import.meta.url));
  const cli = path.join(root, 'node_modules/@playwright/test/cli.js');
  const captureUrl = pathToFileURL(path.join(root, 'tests/ui/playwright-case-capture.mjs')).href;
  const testEntry = path.join(root, 'node_modules/@playwright/test/index.mjs');
  for (const mode of ['pass', 'retry', 'fail', 'runtime-skip']) {
    const directory = mkdtempSync(path.join(tmpdir(), 'coglatas-native-case-control-'));
    try {
      const resultFile = path.join(directory, 'capture.json');
      writeFileSync(path.join(directory, 'capture.mjs'), `
        import { NativeCaseCapture } from ${JSON.stringify(captureUrl)};
        import { writeFileSync } from 'node:fs';
        export default class extends NativeCaseCapture {
          onEnd(result) { super.onEnd(result); writeFileSync(${JSON.stringify(resultFile)},
            JSON.stringify(this.snapshot(${JSON.stringify(metadata)})), {flag:'wx',mode:0o600}); }
        }`);
      const body = {
        pass: `test('PRIVATE_CASE_ONE', async () => {}); test.skip('PRIVATE_CASE_SKIP', async () => {});`,
        retry: `test('PRIVATE_CASE_RETRY', async ({}, info) => { if(info.retry===0) throw new Error('PRIVATE_FAILURE'); });`,
        fail: `test('PRIVATE_CASE_FAILURE', async () => { throw new Error('PRIVATE_FAILURE'); });`,
        'runtime-skip': `test('PRIVATE_RUNTIME_SKIP', async () => { test.skip(true, 'PRIVATE_REASON'); });`,
      }[mode];
      writeFileSync(path.join(directory, 'fixture.spec.mjs'), `import { test } from ${JSON.stringify(testEntry)};\n${body}`);
      writeFileSync(path.join(directory, 'playwright.config.mjs'),
        `export default {testDir:'.',testMatch:'fixture.spec.mjs',workers:1,retries:1,reporter:[['./capture.mjs']]};`);
      const common = [cli, 'test', '--config', path.join(directory, 'playwright.config.mjs')];
      const listed = spawnSync(process.execPath, [...common, '--list', '--reporter=json'], {
        cwd: directory, encoding: 'utf8', timeout: 30000,
      });
      assert.equal(listed.status, 0, 'Native list control failed; private child output withheld.');
      const specs = flatten(JSON.parse(listed.stdout).suites);
      const expected = specs.map(spec => ({ id: hashPlaywrightTestId(spec.id), status: spec.tests[0].expectedStatus }))
        .sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
      const executed = spawnSync(process.execPath, common, { cwd: directory, encoding: 'utf8', timeout: 30000 });
      assert.equal(executed.status, mode === 'fail' ? 1 : 0, 'Native fixture exit differs; private child output withheld.');
      const actual = JSON.parse(readFileSync(resultFile, 'utf8'));
      assert.deepEqual(actual.tests.map(row => row.id), expected.map(row => row.id));
      assert.equal(actual.captureComplete, true);
      assert.doesNotMatch(JSON.stringify(actual), /PRIVATE_/);
      if (mode === 'retry') assert.deepEqual(actual.tests[0].attempts,
        [{ retry: 0, status: 'failed' }, { retry: 1, status: 'passed' }]);
      if (mode === 'fail') assert.equal(actual.status, 'failed');
      if (mode === 'runtime-skip') {
        assert.equal(expected[0].status, 'passed');
        assert.equal(actual.tests[0].declaredExpectedStatus, 'passed');
        assert.equal(actual.tests[0].expectedStatus, 'skipped');
      }
      const source = {
        schema: 'coglatas.playwright-source-discovery.v1', suite: 'angular-static', sourceSha: metadata.sourceSha,
        tests: expected, rosterSha256: createHash('sha256').update(JSON.stringify({
          sourceSha: metadata.sourceSha, suite: 'angular-static', tests: expected,
        })).digest('hex'), artifactTrust: 'EXTERNAL_VERIFICATION_REQUIRED', requiredCheckQualified: false,
        qualification: 'SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF',
      };
      const reconciled = spawnSync(process.env.COGLATAS_CASE_PYTHON ?? (process.platform === 'win32' ? 'python' : 'python3'),
        ['-B', path.join(root, 'scripts/ci/playwright_case_reconcile.py')], {
          input: JSON.stringify({ source, execution: actual, expected: {
            source_sha: metadata.sourceSha, execution_kind: metadata.executionKind,
            run_id: metadata.runId, attempt: metadata.runAttempt,
          } }), encoding: 'utf8', timeout: 10000,
        });
      assert.equal(reconciled.status, mode === 'pass' ? 0 : 1, 'Cross-language native fixture reconciliation failed.');
      const comparison = JSON.parse(reconciled.stdout);
      assert.equal(comparison.decision, mode === 'pass' ? 'CONTENT_MATCH' : 'REJECTED');
      assert.equal(comparison.counts.sourceCases, expected.length);
      assert.equal(comparison.counts.resultCases, expected.length);
      assert.equal(comparison.states.ARTIFACT_PROVENANCE_VERIFIED, false);
      if (mode === 'retry') {
        assert.equal(comparison.counts.failed, 1); assert.equal(comparison.counts.retried, 1);
      }
      if (mode === 'runtime-skip') assert.equal(comparison.counts.unauthorizedSkips, 1);
    } finally {
      // The path is the directory returned by mkdtemp, never an evidence-supplied path.
      rmSync(directory, { recursive: true, force: true });
    }
  }
});
