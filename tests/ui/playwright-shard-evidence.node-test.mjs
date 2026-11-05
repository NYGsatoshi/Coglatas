import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { constructUnverifiedShardEvidence } from './playwright-shard-evidence.mjs';

const sha = 'a'.repeat(40);
const metadata = Object.freeze({
  sourceSha: sha, runId: 12345, runAttempt: 1,
  frontendArtifactId: 98765, index: 1,
});
const all = Object.freeze([
  { id: 'private/desktop/case-a', expectedStatus: 'passed' },
  { id: 'private/desktop/case-b', expectedStatus: 'skipped' },
  { id: 'private/mobile/case-a', expectedStatus: 'passed' },
  { id: 'private/mobile/case-b', expectedStatus: 'passed' },
]);
const events = tests => tests.map(t => ({
  id: t.id, status: t.expectedStatus, retry: 0,
  outcome: t.expectedStatus === 'passed' ? 'expected' : 'skipped',
}));
const input = () => ({
  metadata: { ...metadata }, fullTests: structuredClone(all),
  shardTests: structuredClone(all.slice(0, 2)),
  results: events(all.slice(0, 2)),
});
const make = opts => constructUnverifiedShardEvidence(opts);
const reject = opts => assert.throws(() => make(opts), /SHARD_EVIDENCE_REFUSED/);

test('full roster and two disjoint shard receipts share a canonical source-bound digest', () => {
  const first = make(input());
  const second = make({
    ...input(), metadata: { ...metadata, index: 2 },
    shardTests: structuredClone(all.slice(2)),
    results: events(all.slice(2)),
  });
  assert.equal(first.roster.tests.length, 4);
  assert.equal(first.shard.tests.length, 2);
  assert.equal(second.shard.tests.length, 2);
  assert.equal(first.shard.rosterSha256, second.shard.rosterSha256);
  assert.deepEqual(first.roster, second.roster);
  const fullIds = new Set(first.roster.tests.map(t => t.id));
  const union = [...first.shard.tests, ...second.shard.tests].map(t => t.id);
  assert.equal(new Set(union).size, fullIds.size);
  assert.ok(union.every(id => fullIds.has(id)));
  assert.equal(first.artifactTrust, 'EXTERNAL_VERIFICATION_REQUIRED');
  assert.equal(first.requiredCheckQualified, false);
  assert.equal(first.shard.status, 'passed');
  assert.equal(second.shard.status, 'passed');
  assert.equal(first.roster.tests.filter(t => t.status === 'skipped').length, 1);
  const serialized = JSON.stringify(first) + JSON.stringify(second);
  for (const source of all) assert.ok(!serialized.includes(source.id));
});

test('privacy-safe ids use domain-separated sha256 and canonical hexadecimal sorting', () => {
  const receipt = make(input());
  const expected = createHash('sha256').update('coglatas.playwright-test-id.v1\0')
    .update(all[0].id).digest('hex');
  assert.ok(receipt.roster.tests.some(x => x.id === expected));
  assert.ok(receipt.roster.tests.every(x => /^[0-9a-f]{64}$/.test(x.id)));
  assert.deepEqual(receipt.roster.tests.map(t => t.id),
    receipt.roster.tests.map(t => t.id).sort());
  const canonical = JSON.stringify({
    sourceSha: sha, suite: 'angular-static', tests: receipt.roster.tests,
  });
  assert.equal(receipt.shard.rosterSha256,
    createHash('sha256').update(canonical).digest('hex'));
});

test('different input ordering cannot alter canonical roster identity', () => {
  const swapped = input();
  swapped.fullTests.reverse();
  swapped.shardTests.reverse();
  swapped.results.reverse();
  assert.deepEqual(make(input()), make(swapped));
});

test('foreign or duplicated test ids fail before producing evidence', () => {
  const list = [
    p => { p.fullTests[1].id = p.fullTests[0].id; },
    p => { p.shardTests[1].id = p.shardTests[0].id; },
    p => { p.shardTests[0].id = 'foreign/case'; },
    p => { p.results[1].id = p.results[0].id; },
    p => { p.results[0].id = 'outside/results'; },
    p => { p.results.pop(); },
    p => { p.results.push({ ...p.results[0] }); },
  ];
  for (const mutate of list) {
    const opts = input();
    mutate(opts);
    reject(opts);
  }
});

test('unexpected skip, changed expectation or success after retry never qualifies', () => {
  for (const mutate of [
    p => { p.shardTests[0].expectedStatus = 'skipped'; },
    p => { p.results[0].status = 'skipped'; },
    p => { p.results[0].status = 'failed'; },
    p => { p.results[0].status = 'timedOut'; },
    p => { p.results[0].status = 'interrupted'; },
    p => { p.results[0].outcome = 'flaky'; },
    p => { p.results[0].retry = 1; },
    p => { p.results[0].retry = true; },
    p => { p.results[0].retry = undefined; },
    p => { p.results[1].status = 'passed'; },
    p => { p.fullTests[0].expectedStatus = 'failed'; },
  ]) {
    const opts = input();
    mutate(opts);
    reject(opts);
  }
});

test('source, run, producer and shard identities are exact typed inputs', () => {
  for (const [key, value] of [
    ['sourceSha', 'A'.repeat(40)], ['sourceSha', 'a'.repeat(39)],
    ['runId', true], ['runId', 1.5], ['runId', 0],
    ['runAttempt', true], ['runAttempt', '1'], ['runAttempt', 0],
    ['frontendArtifactId', null], ['frontendArtifactId', 1.25],
    ['index', 0], ['index', 3], ['index', true], ['index', '1'],
  ]) {
    const opts = input();
    opts.metadata[key] = value;
    reject(opts);
  }
});

test('bounded or malformed Playwright identifiers are rejected without logging', () => {
  for (const id of ['', '\n', '\0', 'a'.repeat(513), 42, null]) {
    const opts = input();
    opts.fullTests[0].id = id;
    reject(opts);
  }
});

test('empty or out-of-limit full and shard discovery fail closed', () => {
  for (const key of ['fullTests', 'shardTests']) {
    for (const value of [[], null, {}, Array(5001).fill(all[0])]) {
      const opts = input();
      opts[key] = value;
      reject(opts);
    }
  }
});

test('this is offline preparation: no protected runner, uploader or Playwright config changed', () => {
  const workflow = readFileSync('.github/workflows/ci.yml', 'utf8');
  const launch = readFileSync('scripts/ci/run-frontend-parallel.sh', 'utf8');
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  assert.match(launch, /Protected frontend-test must never execute a partial Playwright shard/);
  assert.match(workflow, /    name: frontend-test/);
  assert.doesNotMatch(workflow, /SHARD_EVIDENCE_REFUSED|playwright-shard-evidence\.mjs/);
  assert.doesNotMatch(runner, /playwright-shard-evidence\.mjs/);
});
