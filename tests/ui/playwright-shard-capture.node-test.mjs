import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import { UnverifiedShardCapture } from './playwright-shard-capture.mjs';

const metadata = {
  sourceSha: 'a'.repeat(40), runId: 12345, runAttempt: 1,
  frontendArtifactId: 98765, index: 1,
};
const full = [
  { id: 'private/project-desktop/pass', expectedStatus: 'passed' },
  { id: 'private/project-desktop/skip', expectedStatus: 'skipped' },
  { id: 'private/project-mobile/pass', expectedStatus: 'passed' },
  { id: 'private/project-mobile/skip', expectedStatus: 'skipped' },
];
const mock = (name, expectedStatus) => ({
  id: name, expectedStatus,
  outcome: () => expectedStatus === 'skipped' ? 'skipped' : 'expected',
});
const selected = () => full.slice(0, 2).map(t => mock(t.id, t.expectedStatus));

function createCapture(tests = selected()) {
  const adapter = new UnverifiedShardCapture();
  adapter.onBegin({}, { allTests: () => tests });
  return { adapter, tests };
}
function record(adapter, t, retry = 0) {
  adapter.onTestEnd(t, { status: t.expectedStatus, retry });
}
function assertRefused(adapter) {
  assert.deepEqual(adapter.onEnd({ status: 'passed' }), { status: 'failed' });
  assert.throws(() => adapter.snapshot({
    metadata, independentlyDiscoveredFullTests: full,
  }), /SHARD_EVIDENCE_REFUSED/);
}

test('source-bound reporter events produce only unverified hashed shard receipts', () => {
  const { adapter, tests } = createCapture();
  for (const t of tests) record(adapter, t);
  assert.equal(adapter.onEnd({ status: 'passed' }), undefined);
  const result = adapter.snapshot({
    metadata, independentlyDiscoveredFullTests: full,
  });
  assert.equal(result.requiredCheckQualified, false);
  assert.equal(result.artifactTrust, 'EXTERNAL_VERIFICATION_REQUIRED');
  assert.equal(result.roster.tests.length, 4);
  assert.equal(result.shard.tests.length, 2);
  assert.equal(result.shard.tests.filter(t => t.status === 'skipped').length, 1);
  const json = JSON.stringify(result);
  for (const t of full) assert.equal(json.includes(t.id), false);
  assert.ok(result.roster.tests.every(t => /^[0-9a-f]{64}$/.test(t.id)));
  assert.equal(adapter.printsToStdio(), false);
});

test('shard-only onBegin cannot masquerade as a complete full roster', () => {
  const { adapter, tests } = createCapture();
  for (const t of tests) record(adapter, t);
  assert.equal(adapter.onEnd({ status: 'passed' }), undefined);
  assert.throws(() => adapter.snapshot({
    metadata, independentlyDiscoveredFullTests: [],
  }), /SHARD_EVIDENCE_REFUSED/);
  assert.throws(() => adapter.snapshot({
    metadata, independentlyDiscoveredFullTests: full.slice(2),
  }), /SHARD_EVIDENCE_REFUSED/);
});

test('missing, repeated or foreign onTestEnd events never qualify', () => {
  for (const kind of ['missing', 'duplicate', 'foreign']) {
    const { adapter, tests } = createCapture();
    if (kind === 'missing') record(adapter, tests[0]);
    else {
      for (const t of tests) record(adapter, t);
      if (kind === 'duplicate') record(adapter, tests[0]);
      else record(adapter, mock('private/foreign', 'passed'));
    }
    assertRefused(adapter);
  }
});

test('any retry, flaky result, failure, unexpected skip or bad outcome blocks the gate', () => {
  for (const kind of ['retry', 'retry-boolean', 'failed', 'skipped', 'flaky']) {
    const { adapter, tests } = createCapture();
    const testCase = tests[0];
    if (kind === 'flaky') testCase.outcome = () => 'flaky';
    const result = { status: testCase.expectedStatus, retry: 0 };
    if (kind === 'retry') result.retry = 1;
    if (kind === 'retry-boolean') result.retry = true;
    if (kind === 'failed') result.status = 'failed';
    if (kind === 'skipped') result.status = 'skipped';
    adapter.onTestEnd(testCase, result);
    record(adapter, tests[1]);
    assertRefused(adapter);
  }
});

test('global reporter error and interrupted/failed full runs refuse output', () => {
  const { adapter, tests } = createCapture();
  for (const t of tests) record(adapter, t);
  adapter.onError(new Error('private server error with auth token'));
  assertRefused(adapter);
  for (const status of ['failed', 'timedout', 'interrupted']) {
    const fresh = createCapture();
    for (const t of fresh.tests) record(fresh.adapter, t);
    assert.deepEqual(fresh.adapter.onEnd({ status }), { status: 'failed' });
    assert.throws(() => fresh.adapter.snapshot({
      metadata, independentlyDiscoveredFullTests: full,
    }), /SHARD_EVIDENCE_REFUSED/);
  }
});

test('invalid onBegin discovery and duplicate lifecycle signals are not swallowed', () => {
  const bad = [
    [], null, {}, [mock('private/reused', 'passed'), mock('private/reused', 'passed')],
    [mock('private/expected-failure', 'failed')],
  ];
  for (const cases of bad) {
    const { adapter } = createCapture(cases);
    assertRefused(adapter);
  }
  const { adapter, tests } = createCapture();
  adapter.onBegin({}, { allTests: () => tests });
  assertRefused(adapter);
});

test('misordered lifecycle and later events cannot restore a successful receipt', () => {
  const a = new UnverifiedShardCapture();
  a.onTestEnd(mock('private/uninitialized', 'passed'), { status: 'passed', retry: 0 });
  assertRefused(a);
  const b = createCapture();
  for (const t of b.tests) record(b.adapter, t);
  assert.equal(b.adapter.onEnd({ status: 'passed' }), undefined);
  b.adapter.onTestEnd(b.tests[0], { status: 'passed', retry: 0 });
  assert.throws(() => b.adapter.snapshot({
    metadata, independentlyDiscoveredFullTests: full,
  }), /SHARD_EVIDENCE_REFUSED/);
});

test('adapter remains disconnected from native Playwright CLI and all uploader paths', () => {
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  const workflow = readFileSync('.github/workflows/ci.yml', 'utf8');
  const compose = readFileSync('infra/compose/dev/playwright.yml', 'utf8');
  const config = readFileSync('playwright.config.ts', 'utf8');
  assert.doesNotMatch(runner, /playwright-shard-capture\.mjs/);
  // A future multiline command, not just '--reporter=<module>', would be unsafe.
  assert.doesNotMatch(workflow, /playwright-shard-capture\.mjs/);
  assert.doesNotMatch(compose, /playwright-shard-capture\.mjs/);
  assert.doesNotMatch(config, /playwright-shard-capture/);
});
