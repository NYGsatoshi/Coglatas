import assert from 'node:assert/strict';
import test from 'node:test';
import { attemptDiagnostic, createBrowserObservation, endpointOperation, OBSERVATION_ATTACHMENT, stableStepId, structuralSnapshotHtml, validateBrowserObservation } from './fixtures/functional-diagnostics.mjs';

const baseURL = 'http://127.0.0.1:5080';
const protectedValue = 'PRIVATE_PROTECTED_BODY_COOKIE_TOKEN_PASSWORD';
const request = () => ({ url: () => `${baseURL}/api/files/private-resource?token=${protectedValue}`, method: () => 'GET',
  headers: () => { throw new Error('Headers must never be read'); }, postData: () => { throw new Error('Bodies must never be read'); } });

test('browser diagnostics discard URLs, resource identities, console text and every response body', () => {
  const observation = createBrowserObservation(baseURL), apiRequest = request();
  observation.onRequest(apiRequest);
  observation.onResponse({ request: () => apiRequest, status: () => 200, body: () => { throw new Error('Response body must never be read'); } });
  observation.onConsole({ type: () => 'error', text: () => { throw new Error('Console text must never be read'); } });
  observation.onPageError(new Error(protectedValue));
  assert.equal(validateBrowserObservation(observation.data), observation.data);
  assert.deepEqual(observation.data.network, [
    { event: 'REQUEST', requestSequence: 1, method: 'GET', operation: 'files.detail', status: null },
    { event: 'RESPONSE', requestSequence: 1, method: 'GET', operation: 'files.detail', status: 200 },
  ]);
  assert.equal(observation.data.consoleCounts.error, 1);
  assert.equal(observation.data.consoleCounts.pageError, 1);
  assert.ok(!JSON.stringify(observation.data).includes(protectedValue));
  assert.ok(!structuralSnapshotHtml(observation.data).includes(protectedValue));
  assert.match(structuralSnapshotHtml(observation.data), /not a screenshot of the authenticated product/);
});

test('chronological network metadata is bounded and exposes truncation without retaining source strings', () => {
  const observation = createBrowserObservation(baseURL);
  for (let index = 0; index < 85; index += 1) observation.onRequest(request());
  assert.equal(observation.data.network.length, 80);
  assert.equal(observation.data.networkTruncated, 5);
  assert.equal(observation.data.network[0].requestSequence, 6);
  assert.equal(observation.data.network.at(-1).requestSequence, 85);
  assert.equal(endpointOperation(`https://external.example/api/files?cookie=${protectedValue}`, baseURL), null);
  assert.equal(endpointOperation(`${baseURL}/api/unknown/${protectedValue}`, baseURL), 'api.other');
});

test('only stable step tokens and fixed failure kinds survive arbitrary assertion text and titles', () => {
  const failure = attemptDiagnostic('FUNC-FILE-002', { status: 'failed', retry: 0,
    steps: [{ category: 'test.step', title: `FUNC-FILE-002 / F05-FAST-04 ${protectedValue}`, error: { message: `expect(locator) ${protectedValue}` } },
      { category: 'test.step', title: `FUNC-FILE-002 / F05-FAST-03 ${protectedValue}` }],
    errors: [{ message: protectedValue }], attachments: [] });
  assert.equal(failure.failedStepId, 'F05-FAST-04');
  assert.equal(failure.failureKind, 'ASSERTION');
  assert.deepEqual(failure.completedStepIds, ['F05-FAST-03']);
  assert.equal(failure.browserEvidenceState, 'MISSING');
  assert.ok(!JSON.stringify(failure).includes(protectedValue));
  assert.equal(stableStepId('FUNC-FILE-002', `FUNC-FILE-002 / PRIVATE-${protectedValue}`), 'OWNER');
  assert.equal(stableStepId('FUNC-FILE-002', 'FUNC-TASK-001 / STEP-01'), 'OWNER');
  assert.equal(attemptDiagnostic('FUNC-TASK-001', { status: 'timedOut', retry: 0 }).failureKind, 'TIMEOUT');
});

test('unexpected protected fields, unbounded data and malformed attachments are refused', () => {
  const data = createBrowserObservation(baseURL).data;
  for (const altered of [{ ...data, protectedBody: protectedValue }, { ...data, networkTruncated: Infinity },
    { ...data, projection: { ...data.projection, detailHeading: protectedValue } },
    { ...data, consoleCounts: { ...data.consoleCounts, error: true } },
    { ...data, network: [{ event: 'RESPONSE', method: 'GET', operation: protectedValue, status: 200, requestSequence: 1 }] }]) {
    assert.throws(() => validateBrowserObservation(altered));
    assert.throws(() => structuralSnapshotHtml(altered));
  }
  const result = attemptDiagnostic('FUNC-TASK-001', { status: 'failed', retry: 0,
    attachments: [{ name: OBSERVATION_ATTACHMENT, body: Buffer.from(JSON.stringify({ ...data, body: protectedValue })) }] });
  assert.equal(result.browserEvidenceState, 'INCOMPLETE');
  assert.equal(result.browser, null);
  assert.ok(!JSON.stringify(result).includes(protectedValue));
});

test('nested full expansion identifies the failed stable child rather than its owner wrapper', () => {
  const result = attemptDiagnostic('FUNC-FILE-002', { status: 'failed', retry: 0, steps: [
    { category: 'test.step', title: 'FUNC-FILE-002 / F05-FAST-09 full expansion', error: { message: protectedValue }, steps: [
      { category: 'test.step', title: 'FUNC-FILE-002 / F05-FULL-01 search', duration: 1 },
      { category: 'test.step', title: 'FUNC-FILE-002 / F05-FULL-02 move', error: { name: 'TimeoutError', message: protectedValue } },
    ] },
  ] });
  assert.equal(result.failedStepId, 'F05-FULL-02');
  assert.equal(result.failureKind, 'TIMEOUT');
  assert.deepEqual(result.completedStepIds, ['F05-FULL-01']);
  assert.ok(!JSON.stringify(result).includes(protectedValue));
});

test('safe structural panel visibly binds its execution identity and rejects arbitrary identity content', () => {
  const data = createBrowserObservation(baseURL).data;
  const identity = { commitSha: 'a'.repeat(40), runId: '100', runAttempt: '2', domain: 'files', journeyId: 'FUNC-FILE-002', gate: 'functional-full', retry: 0 };
  const html = structuralSnapshotHtml(data, identity);
  for (const value of [identity.commitSha, 'Run: 100', 'attempt: 2', 'test retry: 0', 'domain: files', 'FUNC-FILE-002', 'functional-full']) assert.ok(html.includes(value));
  for (const field of ['commitSha', 'runId', 'runAttempt', 'domain', 'journeyId', 'gate', 'retry']) {
    assert.throws(() => structuralSnapshotHtml(data, { ...identity, [field]: protectedValue }));
  }
});
