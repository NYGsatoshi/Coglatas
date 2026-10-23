import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import test from 'node:test';
import { actionFailureKind, observeThreadRequests, validateThreadObservation } from './helpers/thread-probe-observation.mjs';
import { THREAD_PROBE_ATTACHMENT, readThreadProbe, validateThreadProbe } from './helpers/thread-probe-diagnostics.mjs';
import { mobileOutcome } from './mobile/outcome.mjs';

const code = stripTypeScriptTypes(readFileSync(new URL('./helpers/fresh-request-response.ts', import.meta.url), 'utf8'), { mode: 'strip' });
const { responseToFreshRequest } = await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
const target = () => ({ singleTab: true, visible: true, enabled: true, selected: true,
  inViewport: true, centerReceivesPointer: true, workspaceExpanded: true });
const matches = (request) => request.target;
function pageFixture() {
  const page = new EventEmitter();
  page.waitForRequest = (predicate) => new Promise((resolve) => {
    const listener = (request) => { if (predicate(request)) { page.off('request', listener); resolve(request); } };
    page.on('request', listener);
  });
  return page;
}
function requestFixture(page, status = 400, selected = true) {
  const request = { target: selected, url: 'private-canary', headers: { cookie: 'private-canary' } };
  const response = { request: () => request, status: () => status, body: 'private-canary' };
  request.response = async () => response;
  return { request, response, emit: () => page.emit('request', request) };
}
function selectedObservation() {
  const page = pageFixture(), observation = observeThreadRequests(page, matches);
  observation.mark('SESSION_REPLACEMENT_STARTED'); observation.mark('SESSION_REPLACEMENT_COMPLETED');
  observation.mark('SESSION_VERIFIED'); observation.mark('OBSERVER_REGISTERED'); observation.mark('ACTION_STARTED');
  const fresh = requestFixture(page); fresh.emit(); observation.select(fresh.request);
  return { page, observation, fresh };
}

test('real correlation helper binds the new ordinal despite an old authorized response and freezes before cleanup', async () => {
  const page = pageFixture(), observation = observeThreadRequests(page, matches);
  const old = requestFixture(page, 200), fresh = requestFixture(page);
  old.emit(); observation.mark('AUTHORIZED_READ');
  observation.mark('SESSION_REPLACEMENT_STARTED'); observation.mark('SESSION_REPLACEMENT_COMPLETED'); observation.mark('SESSION_VERIFIED');
  const result = await responseToFreshRequest(page, (request) => {
    if (!matches(request)) { return false; } observation.select(request); return true;
  }, async () => {
    fresh.emit(); page.emit('response', old.response); page.emit('response', fresh.response);
  }, observation.mark);
  assert.equal(result, fresh.response);
  const snapshot = observation.finish('FAILED', target());
  assert.equal(snapshot.freshRequest, 2);
  assert.deepEqual(snapshot.events.filter((event) => event.kind === 'RESPONSE'), [
    { kind: 'RESPONSE', request: 1, status: 200 }, { kind: 'RESPONSE', request: 2, status: 400 },
  ]);
  assert.equal(snapshot.classification, 'UNKNOWN', 'observed ordering is not a historical causal finding');
  const beforeCleanup = JSON.stringify(snapshot);
  const restored = requestFixture(page, 500); restored.emit(); page.emit('response', restored.response);
  assert.equal(JSON.stringify(snapshot), beforeCleanup);
  assert.equal(JSON.stringify(observation.finish('COMPLETED')), beforeCleanup,
    'later cleanup cannot re-finalize a failed observation as a completed one');
  assert.equal(page.listenerCount('response'), 0);
  assert.equal(JSON.stringify(snapshot).includes('private-canary'), false);
});

test('a selected request during a pending native action differs from an action with no fresh request', async () => {
  for (const emitRequest of [false, true]) {
    const page = pageFixture(), observation = observeThreadRequests(page, matches);
    observation.mark('SESSION_REPLACEMENT_STARTED'); observation.mark('SESSION_REPLACEMENT_COMPLETED');
    observation.mark('SESSION_VERIFIED');
    let completeAction;
    const pendingAction = new Promise((resolve) => { completeAction = resolve; });
    const fresh = requestFixture(page);
    const operation = responseToFreshRequest(page, (request) => {
      observation.select(request); return true;
    }, () => pendingAction, observation.mark);
    if (emitRequest) { fresh.emit(); }
    await Promise.resolve();
    const snapshot = observation.finish('FAILED', { ...target(), singleTab: false });
    assert.equal(snapshot.events.some((event) => event.kind === 'ACTION_COMPLETED'), false);
    assert.equal(snapshot.freshRequest, emitRequest ? 1 : null);
    assert.equal(snapshot.classification, emitRequest ? 'UNKNOWN' : 'INCOMPLETE');
    completeAction();
    if (!emitRequest) { fresh.emit(); }
    assert.equal(await operation, fresh.response, 'observation cannot provide or replace the response');
  }
});

test('orphan terminal events and cap24 truncation cannot be presented as complete observation', () => {
  const { page, observation, fresh } = selectedObservation();
  const orphan = requestFixture(page, 200); page.emit('response', orphan.response);
  for (let index = 0; index < 30; index++) { page.emit('response', fresh.response); }
  const value = observation.finish('FAILED', target());
  assert.equal(value.events.length, 24); assert.equal(value.truncated, true); assert.equal(value.orphanResponse, true);
  assert.equal(value.classification, 'INCOMPLETE');
  assert.throws(() => validateThreadObservation({ ...value, classification: 'UNKNOWN' }));
  assert.ok(Buffer.byteLength(JSON.stringify(validateThreadProbe({ phases: [], target: target(), observation: value }))) <= 4096);
});

test('strict observation validation rejects raw data, invented cause, reordered binding and unknown target values', () => {
  const { observation } = selectedObservation();
  const value = observation.finish('FAILED', target());
  const mutations = [
    { ...value, private: 'private-canary' }, { ...value, classification: 'CAUSE_PROVEN' },
    { ...value, freshRequest: 2 }, { ...value, actionFailure: 'private-canary' },
    { ...value, failureTarget: { ...target(), private: 'private-canary' } },
    { ...value, failureTarget: { ...target(), visible: 'private-canary' } },
    { ...value, events: value.events.map((event) => ({ ...event, url: 'private-canary' })) },
    { ...value, events: [...value.events].reverse() },
    { ...value, events: value.events.map((event) => event.kind === 'REQUEST' ? { ...event, status: 400 } : event) },
    { ...value, events: Array(25).fill(value.events[0]) },
  ];
  for (const mutation of mutations) {
    assert.throws(() => validateThreadObservation(mutation));
    const projected = readThreadProbe([{ name: THREAD_PROBE_ATTACHMENT,
      body: Buffer.from(JSON.stringify({ phases: [], target: target(), observation: mutation })) }]);
    assert.deepEqual(projected, { state: 'INVALID', data: null });
    assert.equal(JSON.stringify(projected).includes('private-canary'), false);
  }
  const oldBound = structuredClone(value);
  const requestIndex = oldBound.events.findIndex((event) => event.kind === 'REQUEST');
  [oldBound.events[0], oldBound.events[requestIndex]] = [oldBound.events[requestIndex], oldBound.events[0]];
  assert.throws(() => validateThreadObservation(oldBound), 'a request predating observer/session is not a fresh selection');
});

test('login interval markers distinguish emissions before, during and after replacement without claiming cookie-set time', () => {
  const page = pageFixture(), observation = observeThreadRequests(page, matches);
  const before = requestFixture(page), during = requestFixture(page), after = requestFixture(page);
  before.emit(); observation.mark('SESSION_REPLACEMENT_STARTED'); during.emit();
  observation.mark('SESSION_REPLACEMENT_COMPLETED'); observation.mark('SESSION_VERIFIED'); after.emit();
  const value = observation.finish('FAILED', target());
  assert.deepEqual(value.events.map((event) => [event.kind, event.request]), [
    ['REQUEST', 1], ['SESSION_REPLACEMENT_STARTED', null], ['REQUEST', 2],
    ['SESSION_REPLACEMENT_COMPLETED', null], ['SESSION_VERIFIED', null], ['REQUEST', 3],
  ]);
  assert.equal(value.classification, 'INCOMPLETE');
  const reordered = structuredClone(value);
  [reordered.events[1], reordered.events[3]] = [reordered.events[3], reordered.events[1]];
  assert.throws(() => validateThreadObservation(reordered));
});

test('missing failure snapshot remains incomplete and native action text is reduced to fixed tokens', () => {
  const { observation } = selectedObservation();
  assert.equal(observation.finish('FAILED').classification, 'INCOMPLETE');
  for (const [message, expected] of [
    ['private-canary intercepts pointer events', 'POINTER_INTERCEPTION'],
    ['private-canary element was detached', 'DETACHED'], ['private-canary element is not visible', 'NOT_VISIBLE'],
    ['private-canary waiting for locator', 'WAITING_LOCATOR'], ['private-canary', 'UNKNOWN'],
  ]) { assert.equal(actionFailureKind(new Error(message)), expected); }
});

test('successful-only observations retain UNKNOWN and cannot label a historical cause', () => {
  const { observation } = selectedObservation();
  const value = observation.finish('COMPLETED');
  assert.equal(value.classification, 'UNKNOWN');
  assert.equal(value.failureTarget, null);
  assert.throws(() => validateThreadObservation({ ...value, classification: 'CAUSE_PROVEN' }));
  assert.throws(() => validateThreadObservation({ ...value, failureTarget: target() }));
});

test('mobile outcome retains the finite failed observation while rejecting an injected protected field', () => {
  const { observation } = selectedObservation();
  const value = { phases: ['OBSERVER_REGISTERED', 'ACTION_STARTED'], target: target(),
    observation: observation.finish('FAILED', target()) };
  const project = (probe) => mobileOutcome({ annotations: [{ type: 'journey', description: 'FUNC-MSG-001' }] }, {
    status: 'timedOut', retry: 0, errors: [{ message: 'private-canary' }], steps: [],
    attachments: [{ name: THREAD_PROBE_ATTACHMENT, body: Buffer.from(JSON.stringify(probe)) }],
  });
  const outcome = project(value);
  assert.equal(outcome.status, 'timedOut');
  assert.equal(outcome.threadProbe.state, 'AVAILABLE');
  assert.deepEqual(outcome.threadProbe.data, value);
  assert.deepEqual(project({ ...value, private: 'private-canary' }).threadProbe, { state: 'INVALID', data: null });
  assert.equal(JSON.stringify(outcome).includes('private-canary'), false);
});
