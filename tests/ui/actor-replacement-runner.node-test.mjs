import test from 'node:test';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import { criteria, validateAdmission, validateCompletion, validateJourney } from './run-real-backend-actor-replacement.mjs';

const source = 'a'.repeat(40), head = 'b'.repeat(40), tree = 'c'.repeat(40);
const hash = 'd'.repeat(64), imageId = `sha256:${hash}`;
const started = Date.parse('2026-10-30T12:00:00.000Z'), now = started + 20000;
function admission() {
  return { event: 'pull_request', ref: 'refs/pull/1/merge', source, head, checkout: source, sourceTree: tree,
    headTree: tree, artifactSource: source, artifactHead: head, dotnetSource: source, frontendSource: source,
    parents: ['e'.repeat(40), head], imageName: `coglatas-pr-functional:${source}`,
    producerImage: imageId, engineImage: imageId, runId: '12', attempt: '1' };
}
function journey() {
  return { schemaVersion: 1, scope: 'ISOLATED_REAL_BROWSER_SAME_CONTEXT_ACTOR_REPLACEMENT',
    source: { frontend: source, backend: source }, qualification: 'BOUNDED_PASS', release: 'RELEASE_NO_GO',
    startedAtUtc: new Date(started).toISOString(), completedAtUtc: new Date(now).toISOString(),
    failedCriteria: [], observations: { ...Object.fromEntries(criteria.map(key => [key, true])),
      passiveObservationMilliseconds: 15000, historyLengthBefore: 4, historyDeltaToLogin: -3 },
    runtime: { node: 'v24.21.0', playwright: '1.64.1', browser: '156.0.8078.4' },
    proofDigests: { aBody: hash, bBody: hash, tenant: hash, aUser: hash, bUser: hash } };
}
function completion() {
  const expectedHashes = { harnessSha256: hash, helperSha256: hash, verifierSha256: hash };
  return { exitCode: 0, signal: null, journey: journey(), admission: admission(), expectedHashes, started, now,
    image: { schemaVersion: 1, scope: 'within-this-functional-tool-invocation', sourceSha: source,
      toolSourceClaim: source, ...expectedHashes, expectedProducerImageId: imageId, engineImageId: imageId,
      bindingSha256: '', beforeProbeVerified: true, afterProbeVerified: true, suiteExitCode: 0 } };
}
await test('closed PR producer requires exact head, tree, both build stamps and same-run identity', () => {
  assert.doesNotThrow(() => validateAdmission(admission()));
  for (const key of ['checkout', 'artifactSource', 'dotnetSource', 'frontendSource', 'artifactHead', 'headTree']) {
    const input = admission(); input[key] = 'f'.repeat(40);
    assert.throws(() => validateAdmission(input), key);
  }
  for (const mutate of [v => { v.event = 'workflow_dispatch'; }, v => { v.parents = [head]; },
    v => { v.imageName = 'foreign:latest'; }, v => { v.runId = '12\n'; }, v => { v.engineImage = 'latest'; }]) {
    const input = admission(); mutate(input); assert.throws(() => validateAdmission(input));
  }
});
await test('Main producer is a distinct exact-source push selection', () => {
  const input = { ...admission(), event: 'push', ref: 'refs/heads/main', head: source,
    imageName: `coglatas-main-runtime:${source}` };
  assert.doesNotThrow(() => validateAdmission(input));
  input.ref = 'refs/heads/feature'; assert.throws(() => validateAdmission(input));
});
await test('each failed or absent criterion rejects an otherwise green receipt', () => {
  assert.equal(criteria.length, 17);
  for (const key of criteria) {
    const failed = journey(); failed.observations[key] = false;
    assert.throws(() => validateJourney(failed, source, started, now), key);
    const absent = journey(); delete absent.observations[key];
    assert.throws(() => validateJourney(absent, source, started, now), key);
  }
});
await test('freshness, watchdog, native history and continuous 15-second observation cannot be bypassed', () => {
  for (const mutate of [v => { v.source.backend = head; }, v => { v.qualification = 'INCOMPLETE'; },
    v => { v.failedCriteria = ['sameDocument']; }, v => { v.startedAtUtc = new Date(started - 60000).toISOString(); },
    v => { v.completedAtUtc = new Date(started + 240001).toISOString(); },
    v => { v.observations.passiveObservationMilliseconds = 14999; },
    v => { v.observations.historyDeltaToLogin = 0; }, v => { v.observations.historyLengthBefore = 1; }]) {
    const report = journey(); mutate(report); assert.throws(() => validateJourney(report, source, started, now));
  }
});
await test('raw diagnostics, URLs and arbitrary runtime fields never enter finite publication', () => {
  const report = journey();
  const finite = validateJourney(report, source, started, now);
  assert.deepEqual(Object.keys(finite).sort(), ['criteria', 'historyDeltaToLogin', 'passiveObservationMilliseconds', 'runtime']);
  assert.equal(JSON.stringify(finite).includes('proofDigests'), false);
  report.failure = { diagnostic: 'https://private.invalid' };
  assert.throws(() => validateJourney(report, source, started, now));
  delete report.failure; report.runtime.arbitrary = 'secret';
  assert.throws(() => validateJourney(report, source, started, now));
});
await test('failed child, signal and cleanup failure cannot be masked by a green browser report', () => {
  assert.doesNotThrow(() => validateCompletion(completion()));
  for (const exitCode of [1, 17, 130, 143, null]) {
    const input = completion(); input.exitCode = exitCode;
    assert.throws(() => validateCompletion(input));
  }
  const interrupted = completion(); interrupted.signal = 'SIGTERM';
  assert.throws(() => validateCompletion(interrupted));
});
await test('image continuity requires both checks, actual producer identity and exact verifier bytes', () => {
  for (const mutate of [v => { v.image = undefined; }, v => { v.image.beforeProbeVerified = false; },
    v => { v.image.afterProbeVerified = false; }, v => { v.image.suiteExitCode = 1; },
    v => { v.image.expectedProducerImageId = `sha256:${'e'.repeat(64)}`; },
    v => { v.image.harnessSha256 = 'f'.repeat(64); }, v => { v.image.sourceSha = head; }]) {
    const input = completion(); mutate(input); assert.throws(() => validateCompletion(input));
  }
  const rebound = completion(); rebound.admission.engineImage = `sha256:${'f'.repeat(64)}`;
  rebound.image.engineImageId = rebound.admission.engineImage;
  assert.throws(() => validateCompletion(rebound));
  rebound.image.bindingSha256 = hash;
  assert.doesNotThrow(() => validateCompletion(rebound));
});
await test('the actual canonical scenario retains the independently frozen byte identity', () => {
  const bytes = readFileSync(new URL('./actor-replacement-real.mjs', import.meta.url));
  assert.equal(createHash('sha256').update(bytes).digest('hex'),
    'b8eaf7577da1503db15ac85f57331090b40c981d932727f648dd2216907ba942');
});
