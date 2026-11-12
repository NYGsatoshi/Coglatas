/**
 * Offline, nonpublishing preparation of privacy-safe Playwright test identities.
 * This is not a Reporter, a full-suite attestation or an active CI shard gate.
 */
import { createHash } from 'node:crypto';

const SHA = /^[0-9a-f]{40}$/;
const MAX_TESTS = 5000;
const deny = reason => { throw new Error('SHARD_EVIDENCE_REFUSED:' + reason); };
const integer = n => Number.isSafeInteger(n) && n > 0;
const lexical = (a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0;

function testHash(id) {
  if (typeof id !== 'string' || !id.length || id.length > 512 ||
      /[\u0000-\u001f\u007f]/.test(id)) deny('TEST_ID');
  return createHash('sha256')
    .update('coglatas.playwright-test-id.v1\0', 'utf8')
    .update(id, 'utf8').digest('hex');
}

function testsOf(tests, label) {
  if (!Array.isArray(tests) || tests.length < 1 ||
      tests.length > MAX_TESTS) deny(label + '_SIZE');
  const seen = new Set();
  return tests.map(test => {
    if (!test || typeof test !== 'object' || Array.isArray(test) ||
        !['passed', 'skipped'].includes(test.expectedStatus)) {
      deny(label + '_EXPECTED_STATUS');
    }
    const id = testHash(test.id);
    if (seen.has(id)) deny(label + '_DUPLICATE');
    seen.add(id);
    return { id, status: test.expectedStatus };
  }).sort(lexical);
}

function rosterDigest(sourceSha, tests) {
  // Canonical ASCII-only key order matches Python json.dumps(sort_keys=True).
  const json = JSON.stringify({
    sourceSha, suite: 'angular-static',
    tests: tests.map(t => ({ id: t.id, status: t.status })),
  });
  return createHash('sha256').update(json, 'utf8').digest('hex');
}

/**
 * Inputs must eventually come from independently observed Playwright test
 * discovery and results. Raw test identifiers never enter returned metadata.
 */
export function constructUnverifiedShardEvidence({
  metadata, fullTests, shardTests, results,
}) {
  if (!metadata || typeof metadata !== 'object' ||
      Array.isArray(metadata)) deny('METADATA');
  const { sourceSha, runId, runAttempt, frontendArtifactId, index } = metadata;
  if (typeof sourceSha !== 'string' || !SHA.test(sourceSha)) deny('SOURCE_SHA');
  if (![runId, runAttempt, frontendArtifactId].every(integer)) {
    deny('PROVENANCE_INTEGER');
  }
  if (index !== 1 && index !== 2) deny('SHARD_INDEX');

  const full = testsOf(fullTests, 'FULL');
  const selected = testsOf(shardTests, 'SHARD');
  const expected = new Map(full.map(t => [t.id, t.status]));
  const subset = new Map(selected.map(t => [t.id, t.status]));
  for (const test of selected) {
    if (expected.get(test.id) !== test.status) deny('ROSTER_DRIFT');
  }
  if (!Array.isArray(results) || results.length !== selected.length) {
    deny('INCOMPLETE_RESULTS');
  }
  const seen = new Set();
  const actual = results.map(result => {
    if (!result || typeof result !== 'object' || Array.isArray(result)) {
      deny('INVALID_RESULT');
    }
    const id = testHash(result.id);
    if (seen.has(id) || !subset.has(id)) deny('EXTRA_OR_DUPLICATE_RESULT');
    seen.add(id);
    if (result.retry !== 0 ||
        !['passed', 'skipped'].includes(result.status) ||
        result.status !== subset.get(id) ||
        (result.outcome !== undefined &&
         result.outcome !== (result.status === 'passed' ? 'expected' : 'skipped'))) {
      deny('FLAKY_RETRIED_FAILED_OR_MISMATCHED');
    }
    return { id, status: result.status, retries: 0 };
  }).sort(lexical);
  const roster = {
    schema: 'coglatas.playwright-roster.v1', suite: 'angular-static',
    sourceSha, frontendProducerSha: sourceSha,
    runId, runAttempt, frontendArtifactId, tests: full,
  };
  const shard = {
    schema: 'coglatas.playwright-shard.v1', suite: 'angular-static',
    sourceSha, frontendProducerSha: sourceSha,
    runId, runAttempt, frontendArtifactId,
    rosterSha256: rosterDigest(sourceSha, full), index, total: 2,
    status: 'passed', tests: actual,
  };
  return {
    roster, shard, artifactTrust: 'EXTERNAL_VERIFICATION_REQUIRED',
    requiredCheckQualified: false,
  };
}
