import assert from 'node:assert/strict';
import test from 'node:test';

import {
  classifyUnexpectedApiFailures,
  classifyUnexpectedConsoleErrors,
  isExpectedFailure,
  selectPr03cExpectedRevocationRefreshFailures,
} from './real-backend-failure-correlation.mjs';

const FIRST_INDEX = 0;
const NO_FAILURES = 0;
const ONE_FAILURE = 1;
const TWO_FAILURES = 2;
const THREE_FAILURES = 3;
const HTTP_BAD_REQUEST = 400;
const HTTP_UNAUTHORIZED = 401;
const HTTP_FORBIDDEN = 403;
const HTTP_NOT_FOUND = 404;
const HTTP_CONFLICT = 409;
const HTTP_TOO_MANY_REQUESTS = 429;
const HTTP_INTERNAL_SERVER_ERROR = 500;
const HTTP_SERVICE_UNAVAILABLE = 503;

const taskId = '11111111-1111-4111-8111-111111111111';
const otherTaskId = '22222222-2222-4222-8222-222222222222';
const projectId = '33333333-3333-4333-8333-333333333333';
const otherProjectId = '44444444-4444-4444-8444-444444444444';
const executionScopePath = `/api/tasks/${taskId}/execution-scope`;
const projectExecutionScopePath = `/api/projects/${projectId}/execution-scope`;
const failure = (overrides = {}) => ({ method: 'GET', path: executionScopePath, status: HTTP_NOT_FOUND, ...overrides });
const networkError = (status) => `Failed to load resource: the server responded with a status of ${status} (test)`;
const evidenceFor = (failedApiResponses) => ({
  failedApiResponses,
  consoleErrors: failedApiResponses.map(({ status }) => networkError(status))
});

function selectExpected(evidence, postRevocationFailureStart, selectedTaskId, selectedProjectId = projectId) {
  return selectPr03cExpectedRevocationRefreshFailures(
    evidence,
    postRevocationFailureStart,
    selectedTaskId,
    selectedProjectId,
  );
}

function expectUnexpectedApiFailures(evidence, expected) {
  const { unexpected, remainingScenarioExpected } = classifyUnexpectedApiFailures(evidence, expected);
  assert.deepStrictEqual(unexpected, [], 'unexpected failed API responses');
  assert.deepStrictEqual(
    remainingScenarioExpected,
    [],
    'scenario-expected failed API responses were not observed',
  );
}

function expectUnexpectedConsoleErrors(evidence, expected) {
  assert.deepStrictEqual(
    classifyUnexpectedConsoleErrors(evidence, expected),
    [],
    'unexpected browser console errors',
  );
}

function expectBothAccept(evidence, expected) {
  assert.doesNotThrow(() => expectUnexpectedApiFailures(evidence, expected));
  assert.doesNotThrow(() => expectUnexpectedConsoleErrors(evidence, expected));
}

function expectBothReject(evidence, expected) {
  assert.throws(() => expectUnexpectedApiFailures(evidence, expected), /unexpected failed API responses/);
  assert.throws(() => expectUnexpectedConsoleErrors(evidence, expected), /unexpected browser console errors/);
}

test('exact post-revocation GET execution-scope 404 is shared by both classifiers', () => {
  const evidence = evidenceFor([failure()]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  assert.deepStrictEqual(expected, evidence.failedApiResponses);
  assert.equal(expected[FIRST_INDEX], evidence.failedApiResponses[FIRST_INDEX]);
  Object.freeze(expected);
  expectBothAccept(evidence, expected);
  assert.equal(expected.length, ONE_FAILURE, 'Neither classifier mutates the shared scenario list');
});

test('exact post-revocation GET Project execution-scope 404 is accepted by both classifiers', () => {
  const evidence = evidenceFor([failure({ path: projectExecutionScopePath })]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  assert.deepStrictEqual(expected, evidence.failedApiResponses);
  assert.equal(isExpectedFailure(evidence.failedApiResponses[FIRST_INDEX]), false);
  expectBothAccept(evidence, expected);
});

test('pre-revocation Project execution-scope 404 is never tolerated', () => {
  const evidence = evidenceFor([failure({ path: projectExecutionScopePath })]);
  const expected = selectExpected(evidence, ONE_FAILURE, taskId);
  assert.deepStrictEqual(expected, []);
  expectBothReject(evidence, expected);
});

test('post-revocation 404 for another Project execution scope remains unexpected', () => {
  const evidence = evidenceFor([failure({ path: `/api/projects/${otherProjectId}/execution-scope` })]);
  expectBothReject(evidence, selectExpected(evidence, NO_FAILURES, taskId));
});

test('post-revocation Project execution-scope wrong method or status remains unexpected', () => {
  for (const variant of [
    { method: 'POST', path: projectExecutionScopePath },
    { path: projectExecutionScopePath, status: HTTP_FORBIDDEN },
    { path: `${projectExecutionScopePath}/history` },
  ]) {
    const evidence = evidenceFor([failure(variant)]);
    expectBothReject(evidence, selectExpected(evidence, NO_FAILURES, taskId));
  }
});

test('four 404s from the actual revoked Project/Task/File journey are all accounted for', () => {
  const attachmentId = '55555555-5555-4555-8555-555555555555';
  const evidence = evidenceFor([
    failure({ path: projectExecutionScopePath }),
    failure({ path: `/api/tasks/${taskId}` }),
    failure({ path: `/api/tasks/${taskId}` }),
    failure({ method: 'POST', path: `/api/attachments/${attachmentId}/download-grants` })
  ]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  assert.equal(expected.length, ONE_FAILURE, 'only the project-scoped revocation race needs scenario correlation');
  expectBothAccept(evidence, expected);
  const unexpectedEvidence = evidenceFor([
    ...evidence.failedApiResponses,
    failure({ path: `/api/projects/${otherProjectId}/execution-scope` })
  ]);
  expectBothReject(unexpectedEvidence, selectExpected(unexpectedEvidence, NO_FAILURES, taskId));
});

test('execution-scope 404 is never added to the global expected-failure classifier', () => {
  assert.equal(isExpectedFailure(failure()), false);
  expectBothReject(evidenceFor([failure()]), []);
});

test('a pre-revocation execution-scope 404 remains unexpected', () => {
  const evidence = evidenceFor([failure()]);
  const expected = selectExpected(evidence, ONE_FAILURE, taskId);
  assert.equal(expected.length, NO_FAILURES);
  expectBothReject(evidence, expected);
});

test('an identical post-revocation tuple cannot hide an earlier unexpected 404', () => {
  const evidence = evidenceFor([failure(), failure()]);
  const expected = selectExpected(evidence, ONE_FAILURE, taskId);
  assert.equal(expected.length, ONE_FAILURE);
  expectBothReject(evidence, expected);
});

for (const [name, path] of [
  ['another Task', `/api/tasks/${otherTaskId}/execution-scope`],
  ['another endpoint', `/api/tasks/${taskId}/activity`],
  ['a suffix', `${executionScopePath}/history`],
  ['a trailing slash', `${executionScopePath}/`],
  ['a similar endpoint', `${executionScopePath}-unexpected`]
]) {
  test(`post-revocation 404 for ${name} remains unexpected`, () => {
    const evidence = evidenceFor([failure({ path })]);
    const expected = selectExpected(evidence, NO_FAILURES, taskId);
    assert.equal(expected.length, NO_FAILURES);
    expectBothReject(evidence, expected);
  });
}

for (const method of ['POST', 'PATCH', 'DELETE', 'PUT', 'HEAD', 'OPTIONS']) {
  test(`post-revocation ${method} execution-scope 404 remains unexpected`, () => {
    const evidence = evidenceFor([failure({ method })]);
    const expected = selectExpected(evidence, NO_FAILURES, taskId);
    assert.equal(expected.length, NO_FAILURES);
    expectBothReject(evidence, expected);
  });
}

for (const status of [
  HTTP_BAD_REQUEST, HTTP_UNAUTHORIZED, HTTP_FORBIDDEN, HTTP_CONFLICT,
  HTTP_TOO_MANY_REQUESTS, HTTP_INTERNAL_SERVER_ERROR, HTTP_SERVICE_UNAVAILABLE
]) {
  test(`post-revocation GET execution-scope ${status} remains unexpected`, () => {
    const evidence = evidenceFor([failure({ status })]);
    const expected = selectExpected(evidence, NO_FAILURES, taskId);
    assert.equal(expected.length, NO_FAILURES);
    expectBothReject(evidence, expected);
  });
}

test('pathname normalization accepts a query without accepting a different path', () => {
  const evidence = evidenceFor([failure({ path: `${executionScopePath}?refresh=1` })]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  assert.equal(expected.length, ONE_FAILURE);
  expectBothAccept(evidence, expected);
});

test('multiple observed post-revocation refreshes retain one-to-one failure budgets', () => {
  const evidence = evidenceFor([failure(), failure(), failure()]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  assert.equal(expected.length, THREE_FAILURES);
  expectBothAccept(evidence, expected);
  evidence.consoleErrors.push(networkError(HTTP_NOT_FOUND));
  assert.throws(() => expectUnexpectedConsoleErrors(evidence, expected), /unexpected browser console errors/);
});

test('non-network console errors are not masked by an expected refresh', () => {
  const evidence = evidenceFor([failure()]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  evidence.consoleErrors.push('Unexpected application error');
  assert.throws(() => expectUnexpectedConsoleErrors(evidence, expected), /unexpected browser console errors/);
});

test('an expected 404 cannot consume a console failure with another status', () => {
  const evidence = evidenceFor([failure()]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  evidence.consoleErrors = [networkError(HTTP_INTERNAL_SERVER_ERROR)];
  assert.throws(() => expectUnexpectedConsoleErrors(evidence, expected), /unexpected browser console errors/);
});

test('an unobserved scenario failure cannot be fabricated to pass the API assertion', () => {
  assert.throws(
    () => expectUnexpectedApiFailures(evidenceFor([]), [failure()]),
    /scenario-expected failed API responses were not observed/
  );
});

test('absence of the optional racing refresh is valid', () => {
  const evidence = evidenceFor([]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  assert.equal(expected.length, NO_FAILURES);
  expectBothAccept(evidence, expected);
});

test('existing post-revocation project task-list 400 correlation is preserved', () => {
  const evidence = evidenceFor([
    failure({ path: `/api/projects/${otherTaskId}/tasks`, status: HTTP_BAD_REQUEST }),
    failure()
  ]);
  const expected = selectExpected(evidence, NO_FAILURES, taskId);
  assert.equal(expected.length, TWO_FAILURES);
  expectBothAccept(evidence, expected);
});
