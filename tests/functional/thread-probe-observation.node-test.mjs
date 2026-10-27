import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import test from 'node:test';
import { actionFailureKind, observeThreadRequests, validateThreadObservation, withThreadProbeLifecycle } from './helpers/thread-probe-observation.mjs';
import { THREAD_PROBE_ATTACHMENT, THREAD_PROBE_SECONDARY_ANNOTATION, readThreadProbe, readThreadProbeSecondary, validateThreadProbe } from './helpers/thread-probe-diagnostics.mjs';
import { mobileOutcome } from './mobile/outcome.mjs';
import { mobileDeviceOptions, mobileProject } from './mobile/profile.mjs';

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

test('an old in-flight GET returning 400 after a new tab action triggers exactly one bounded hook', () => {
  const page = pageFixture(), observation = observeThreadRequests(page, matches);
  let captures = 0;
  observation.onPreActionDenied(() => { captures++; });
  observation.mark('AUTHORIZED_OBSERVER_REGISTERED');
  const allowed = requestFixture(page, 200); allowed.emit(); observation.selectAuthorized(allowed.request);
  page.emit('response', allowed.response); observation.mark('AUTHORIZED_READ');
  observation.mark('SESSION_REPLACEMENT_STARTED');
  const background = requestFixture(page, 400); background.emit();
  observation.mark('SESSION_REPLACEMENT_COMPLETED'); observation.mark('SESSION_VERIFIED');
  observation.mark('OBSERVER_REGISTERED');
  page.emit('response', background.response);
  assert.equal(captures, 0, '400 settled before the explicit action is not the interleaving');
  const raced = requestFixture(page, 400); raced.emit();
  // This request also started after registration: it may not be claimed as pre-action.
  observation.mark('ACTION_STARTED');
  page.emit('response', raced.response);
  assert.equal(captures, 0);
  const snapshot = observation.finish('FAILED');
  assert.equal(snapshot.classification, 'INCOMPLETE');
  assert.equal(page.listenerCount('response'), 0);
  assert.equal(JSON.stringify(snapshot).includes('private-canary'), false);
});

test('one pre-action GET 400 after ACTION_STARTED cannot be mistaken for a fresh request', () => {
  const page = pageFixture(), observation = observeThreadRequests(page, matches);
  let captures = 0;
  observation.onPreActionDenied(() => { captures++; });
  observation.mark('AUTHORIZED_OBSERVER_REGISTERED');
  const allowed = requestFixture(page, 200); allowed.emit(); observation.selectAuthorized(allowed.request);
  page.emit('response', allowed.response); observation.mark('AUTHORIZED_READ');
  observation.mark('SESSION_REPLACEMENT_STARTED');
  const prior = requestFixture(page, 400); prior.emit();
  observation.mark('SESSION_REPLACEMENT_COMPLETED'); observation.mark('SESSION_VERIFIED');
  observation.mark('OBSERVER_REGISTERED'); observation.mark('ACTION_STARTED');
  page.emit('response', prior.response);
  page.emit('response', prior.response);
  assert.equal(captures, 1, 'one old 400 yields one sample despite repeated notifications');
  const later = requestFixture(page, 400); later.emit(); page.emit('response', later.response);
  assert.equal(captures, 1, 'a new request never counts as pre-action');
  const snapshot = observation.finish('FAILED');
  assert.equal(snapshot.freshRequest, null);
  assert.equal(snapshot.classification, 'INCOMPLETE');
  assert.equal(JSON.stringify(snapshot).includes('private-canary'), false);
  assert.equal(page.listenerCount('response'), 0);
});

test('an earlier denied GET interleaving with native tab action cannot qualify a new GET or conceal detach', async () => {
  const page = pageFixture(), observation = observeThreadRequests(page, matches);
  let preActionDenials = 0;
  observation.onPreActionDenied(() => { preActionDenials++; });
  observation.mark('AUTHORIZED_OBSERVER_REGISTERED');
  const authorized = requestFixture(page, 200);
  authorized.emit(); observation.selectAuthorized(authorized.request);
  page.emit('response', authorized.response);
  observation.mark('AUTHORIZED_READ');
  observation.mark('ROOT_ASSERTION_STARTED'); observation.mark('ROOT_ASSERTION_PASSED');

  // Reproduce the order of a bounded real-backend failure, not a simulated
  // authorization PASS: request #3 begins before the action observer exists.
  observation.mark('SESSION_REPLACEMENT_STARTED');
  const oldDuringLogin = requestFixture(page, 200);
  oldDuringLogin.emit();
  observation.mark('SESSION_REPLACEMENT_COMPLETED'); observation.mark('SESSION_VERIFIED');
  const deniedBeforeAction = requestFixture(page, 400);
  deniedBeforeAction.emit();
  page.emit('response', oldDuringLogin.response);

  const detached = new Error('element was detached from the DOM: private-canary');
  const operation = responseToFreshRequest(page, (request) => {
    if (!matches(request)) { return false; }
    observation.select(request);
    return true;
  }, async () => {
    // The original 400 arrives after the native click starts. It does not
    // manufacture the new Request that the strict owner expects.
    page.emit('response', deniedBeforeAction.response);
    throw detached;
  }, observation.mark);
  await assert.rejects(operation, (error) => error === detached);
  assert.equal(preActionDenials, 1, 'the old 400 is observed exactly once');
  const result = observation.finish('FAILED', null, actionFailureKind(detached));
  const requestIndex = result.events.findIndex((event) => event.kind === 'REQUEST' && event.request === 3);
  const observerIndex = result.events.findIndex((event) => event.kind === 'OBSERVER_REGISTERED');
  const actionIndex = result.events.findIndex((event) => event.kind === 'ACTION_STARTED');
  const deniedIndex = result.events.findIndex((event) =>
    event.kind === 'RESPONSE' && event.request === 3 && event.status === 400);
  assert.ok(requestIndex >= 0 && requestIndex < observerIndex && observerIndex < actionIndex &&
    actionIndex < deniedIndex, 'old 400 interleaving must retain its actual event ordering');
  assert.equal(result.freshRequest, null);
  assert.equal(result.actionFailure, 'DETACHED');
  assert.equal(result.classification, 'INCOMPLETE');
  assert.equal(result.outcome, 'FAILED');
  assert.equal(result.events.some((event) => event.kind === 'FRESH_SELECTED'), false);
  assert.equal(JSON.stringify(result).includes('private-canary'), false);
  assert.deepEqual(validateThreadObservation(result), result);
  assert.equal(page.listenerCount('response'), 0);
  // The synthetic unresolved waiter has no page-lifecycle owner in this unit
  // fixture; disposal is not an assertion that the actual UI ever completed.
  page.removeAllListeners('request');
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

test('actual fresh-helper request rejection survives observer capture and cleanup as the identical primary error', async () => {
  const page = pageFixture(), observation = observeThreadRequests(page, matches);
  const primary = new Error('private-canary');
  page.waitForRequest = () => Promise.reject(primary);
  observation.mark('SESSION_REPLACEMENT_STARTED'); observation.mark('SESSION_REPLACEMENT_COMPLETED');
  observation.mark('SESSION_VERIFIED');
  let captured, restored = false;
  const secondary = [];
  await assert.rejects(withThreadProbeLifecycle(
    () => responseToFreshRequest(page, matches, async () => {}, observation.mark),
    async () => { captured = observation.finish('FAILED', target()); },
    async () => { validateThreadProbe({ phases: ['REQUEST_FAILED'], target: target(), observation: captured }); },
    async () => { restored = true; }, (stage) => secondary.push(stage)), (error) => error === primary);
  assert.equal(captured.events.some((event) => event.kind === 'REQUEST_FAILED'), true);
  assert.equal(captured.classification, 'INCOMPLETE');
  assert.equal(restored, true); assert.deepEqual(secondary, []);
  assert.equal(JSON.stringify(captured).includes('private-canary'), false);
});

test('capture, attachment and restoration failures never mask a mandatory primary but still attempt every stage', async () => {
  for (const failingStages of [['CAPTURE'], ['PUBLISH'], ['RESTORE'], ['CAPTURE', 'PUBLISH', 'RESTORE']]) {
    const primary = new Error('private-primary'), attempted = [], secondary = [];
    const action = (stage) => async () => {
      attempted.push(stage); if (failingStages.includes(stage)) { throw new Error('private-secondary'); }
    };
    await assert.rejects(withThreadProbeLifecycle(async () => { throw primary; },
      action('CAPTURE'), action('PUBLISH'), action('RESTORE'), (stage) => secondary.push(stage)), (error) => error === primary);
    assert.deepEqual(attempted, ['CAPTURE', 'PUBLISH', 'RESTORE']);
    assert.deepEqual(secondary, failingStages);
    assert.equal(JSON.stringify(secondary).includes('private'), false);
  }
});

test('attachment or restoration failure on an otherwise successful owner remains failure without retries', async () => {
  for (const failingStage of ['PUBLISH', 'RESTORE']) {
    const secondaryError = new Error('private-secondary'), attempted = [], stages = [];
    const action = (stage) => async () => {
      attempted.push(stage); if (stage === failingStage) { throw secondaryError; }
    };
    await assert.rejects(withThreadProbeLifecycle(async () => { attempted.push('RUN'); },
      action('CAPTURE'), action('PUBLISH'), action('RESTORE'), (stage) => stages.push(stage)), (error) => error === secondaryError);
    assert.deepEqual(attempted, ['RUN', 'PUBLISH', 'RESTORE']); assert.deepEqual(stages, [failingStage]);
  }
});

test('secondary runtime annotation projection rejects raw values, duplicates and excess stages', () => {
  const annotation = (description) => ({ type: THREAD_PROBE_SECONDARY_ANNOTATION, description });
  assert.deepEqual(readThreadProbeSecondary([annotation('CAPTURE'), annotation('RESTORE')]),
    { state: 'AVAILABLE', stages: ['CAPTURE', 'RESTORE'] });
  for (const values of [['private-canary'], ['CAPTURE', 'CAPTURE'], ['CAPTURE', 'PUBLISH', 'RESTORE', 'CAPTURE']]) {
    assert.deepEqual(readThreadProbeSecondary(values.map(annotation)), { state: 'INVALID', stages: [] });
  }
  assert.deepEqual(readThreadProbeSecondary([]), { state: 'MISSING', stages: [] });
});

test('the outer owner evidence attachment cannot replace the inner mandatory failure', async () => {
  const primary = new Error('private-primary'), stages = [];
  const inner = () => withThreadProbeLifecycle(async () => { throw primary; }, async () => {}, async () => {},
    async () => { throw new Error('private-restoration'); }, (stage) => stages.push(stage));
  await assert.rejects(withThreadProbeLifecycle(inner, async () => {},
    async () => { throw new Error('private-outer-attachment'); }, async () => {}, (stage) => stages.push(stage)),
  (error) => error === primary);
  assert.deepEqual(stages, ['RESTORE', 'PUBLISH']);
});

test('secondary annotations suppress unsupported primary kind and source attribution without changing owner outcome', () => {
  const result = { status: 'failed', retry: 0, errors: [{ message: 'private-primary' }], steps: [{
    category: 'test.step', title: 'FUNC-MSG-001 / MSG-THREAD-DENIAL', error: {}, steps: [
      { category: 'expect', title: 'private-primary', error: { matcherResult: {} },
        location: { file: '/repo/tests/functional/messaging/collaboration-owner-journey.spec.ts', line: 415 } },
      { category: 'pw:api', title: 'private-cleanup', error: { message: 'TimeoutError: private-secondary' },
        location: { file: '/repo/tests/functional/messaging/collaboration-owner-journey.spec.ts', line: 457 } },
    ],
  }] };
  for (const description of ['RESTORE', 'private-canary']) {
    const outcome = mobileOutcome({ annotations: [{ type: 'journey', description: 'FUNC-MSG-001' },
      { type: THREAD_PROBE_SECONDARY_ANNOTATION, description }] }, result);
    assert.equal(outcome.status, 'failed'); assert.equal(outcome.retry, 0);
    assert.equal(outcome.failedStepId, 'MSG-THREAD-DENIAL');
    assert.equal(outcome.failureKind, 'UNCLASSIFIED_FAILURE');
    assert.equal(outcome.sourceModule, 'UNKNOWN'); assert.equal(outcome.sourceLine, null);
    assert.equal(outcome.additionalFailure, true); assert.equal(outcome.primaryAttributionSuppressed, true);
    assert.equal(JSON.stringify(outcome).includes('private'), false);
  }
});

test('actual Playwright reporter does not attribute a caught restoration timeout to the preserved primary assertion', () => {
  const directory = mkdtempSync(join(tmpdir(), 'thread-secondary-reporter-')), root = process.cwd();
  try {
    mkdirSync(join(directory, 'artifacts/mobile'), { recursive: true });
    mkdirSync(join(directory, 'messaging'));
    writeFileSync(join(directory, 'package.json'), JSON.stringify({ type: 'module' }));
    writeFileSync(join(directory, 'artifacts/mobile/source-sha'), 'a'.repeat(40) + '\n');
    const spec = `import {test,expect} from ${JSON.stringify(pathToFileURL(join(root, 'node_modules/@playwright/test/index.mjs')).href)};
      import {withThreadProbeLifecycle} from ${JSON.stringify(pathToFileURL(join(root, 'tests/functional/helpers/thread-probe-observation.mjs')).href)};
      test('FUNC-MSG-001 reporter control only', {annotation:[{type:'journey',description:'FUNC-MSG-001'},{type:'backend',description:'real'}]}, async ({},info)=>{
        await test.step('FUNC-MSG-001 / MSG-THREAD-DENIAL', async()=>{
          await withThreadProbeLifecycle(async()=>{await test.step('private-primary',()=>{expect(1,'private-primary').toBe(2);});},
            async()=>{},async()=>{},async()=>{await test.step('private-restoration',()=>{const error=new Error('TimeoutError: private-secondary');error.name='TimeoutError';throw error;});},
            stage=>info.annotations.push({type:${JSON.stringify(THREAD_PROBE_SECONDARY_ANNOTATION)},description:stage}));
        });
      });`;
    writeFileSync(join(directory, 'messaging/collaboration-owner-journey.spec.ts'), spec);
    writeFileSync(join(directory, 'config.mjs'), `import mobile from ${JSON.stringify(pathToFileURL(join(root, 'playwright.mobile-real.config.ts')).href)};
      export default {...mobile,...${JSON.stringify({ testDir: directory, testMatch: 'collaboration-owner-journey.spec.ts',
        outputDir: join(directory, 'runner-output'), workers: 1, retries: 0,
        reporter: [[join(root, 'tests/functional/mobile/reporter.mjs')]],
        projects: [{ name: mobileProject, use: { browserName: 'chromium', ...mobileDeviceOptions(mobileProject) }, retries: 0 }],
      })}};`);
    const run = spawnSync(process.execPath, [join(root, 'node_modules/@playwright/test/cli.js'), 'test', '--config', join(directory, 'config.mjs')], {
      cwd: directory, encoding: 'utf8', env: { ...process.env, TARGET_SHA: 'a'.repeat(40), COGLATAS_REAL_BACKEND_MOBILE: '1' },
    });
    assert.equal(run.status, 1); assert.equal((run.stdout + run.stderr).includes('private-'), false);
    assert.match(run.stdout + run.stderr, /Mobile owner outcome:/);
    assert.equal(existsSync(join(directory, 'artifacts/mobile/owners.json')), false);
    const text = readFileSync(join(directory, 'artifacts/mobile/diagnostics.json'), 'utf8');
    assert.equal(text.includes('private-'), false);
    const outcomes = JSON.parse(text).outcomes;
    assert.equal(outcomes.length, 1);
    const [outcome] = outcomes;
    assert.equal(outcome.status, 'failed'); assert.equal(outcome.retry, 0);
    assert.equal(outcome.failedStepId, 'MSG-THREAD-DENIAL');
    assert.deepEqual(outcome.threadProbeSecondary, { state: 'AVAILABLE', stages: ['RESTORE'] });
    assert.equal(outcome.failureKind, 'UNCLASSIFIED_FAILURE');
    assert.equal(outcome.sourceModule, 'UNKNOWN'); assert.equal(outcome.sourceLine, null);
    assert.equal(outcome.additionalFailure, true); assert.equal(outcome.primaryAttributionSuppressed, true);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});


test('relative timing distinguishes response before and after root rejection without attributing request initiator', () => {
  for (const late of [false, true]) {
    let time = 0;
    const page = pageFixture(), observation = observeThreadRequests(page, matches, () => time++);
    observation.mark('AUTHORIZED_OBSERVER_REGISTERED'); observation.mark('AUTHORIZED_ACTION_STARTED');
    const first = requestFixture(page, 200), second = requestFixture(page, 200);
    first.emit(); observation.selectAuthorized(first.request); page.emit('response', first.response);
    observation.mark('AUTHORIZED_ACTION_COMPLETED'); observation.mark('AUTHORIZED_READ');
    observation.mark('ROOT_ASSERTION_STARTED'); second.emit();
    if (!late) { page.emit('response', second.response); }
    observation.mark('ROOT_ASSERTION_FAILED');
    if (late) { page.emit('response', second.response); }
    const value = observation.finish('FAILED', target());
    const rejected = value.events.findIndex((event) => event.kind === 'ROOT_ASSERTION_FAILED');
    const response = value.events.findIndex((event) => event.kind === 'RESPONSE' && event.request === 2);
    assert.equal(value.elapsedMs[response] > value.elapsedMs[rejected], late);
    assert.equal(value.classification, 'INCOMPLETE', 'No restricted request was reached.');
    const historical = structuredClone(value); delete historical.elapsedMs;
    assert.equal(Object.hasOwn(validateThreadObservation(historical), 'elapsedMs'), false);
    assert.equal(JSON.stringify(value).includes('private-canary'), false);
  }
});

test('clock failure, regression and invalid samples cannot throw from markers or become complete evidence', () => {
  for (const bad of [() => { throw new Error('private-clock'); }, () => NaN, () => Infinity, () => -1, () => 3600001]) {
    let calls = 0;
    const page = pageFixture(), observation = observeThreadRequests(page, matches, () => calls++ === 0 ? 0 : bad());
    assert.doesNotThrow(() => observation.mark('AUTHORIZED_OBSERVER_REGISTERED'));
    const value = observation.finish('FAILED', target());
    assert.deepEqual(value.elapsedMs, [null]); assert.equal(value.classification, 'INCOMPLETE');
    assert.equal(JSON.stringify(value).includes('private-clock'), false);
  }
  let index = 0;
  const samples = [0, 5, 3], page = pageFixture();
  const regressed = observeThreadRequests(page, matches, () => samples[index++]);
  regressed.mark('AUTHORIZED_OBSERVER_REGISTERED'); regressed.mark('AUTHORIZED_ACTION_STARTED');
  assert.deepEqual(regressed.finish('FAILED', target()).elapsedMs, [5, null]);
  const { observation } = selectedObservation(), valid = observation.finish('FAILED', target());
  for (const elapsedMs of [[], Array(valid.events.length).fill('private-clock'), Array(valid.events.length).fill(3600001),
    valid.events.map((_, index) => index === 0 ? 2 : 1), Array(valid.events.length).fill(true)]) {
    assert.throws(() => validateThreadObservation({ ...valid, elapsedMs }));
  }
  const missing = { ...valid, elapsedMs: Array(valid.events.length).fill(null), classification: 'INCOMPLETE' };
  validateThreadObservation(missing);
  assert.throws(() => validateThreadObservation({ ...missing, classification: 'UNKNOWN' }));
});

test('root assertion markers reject invented order and success after a failed matcher', () => {
  const page = pageFixture(), observation = observeThreadRequests(page, matches, () => 0);
  observation.mark('AUTHORIZED_OBSERVER_REGISTERED'); observation.mark('AUTHORIZED_READ');
  observation.mark('ROOT_ASSERTION_STARTED'); observation.mark('ROOT_ASSERTION_FAILED');
  const valid = observation.finish('FAILED', target());
  for (const events of [valid.events.slice().reverse(), [...valid.events, valid.events.at(-1)],
    [...valid.events, { kind: 'ROOT_ASSERTION_PASSED', request: null, status: null }]]) {
    assert.throws(() => validateThreadObservation({ ...valid, events, elapsedMs: events.map(() => 0) }));
  }
  assert.throws(() => validateThreadObservation({ ...valid, outcome: 'COMPLETED', failureTarget: null }));
});


test('fractional raw clock regressions and overflow stay unknown without poisoning qualified recovery or replacing primary error', async () => {
  for (const [samples, expected] of [
    [[0, 10.9, 10.1, 10.5, 11], [10, null, null, 11]],
    [[0, 3600000.9, 3600000, 3600001], [null, 3600000, null]],
    [[0, -0.1, 0.9, 0.1, 1.1], [null, 0, null, 1]],
  ]) {
    let index = 0;
    const observation = observeThreadRequests(pageFixture(), matches, () => samples[index++]);
    observation.mark('AUTHORIZED_OBSERVER_REGISTERED');
    for (let count = 1; count < expected.length; count++) { observation.mark('AUTHORIZED_READ'); }
    const primary = new Error('private-primary-clock'), secondary = [];
    let snapshot;
    await assert.rejects(withThreadProbeLifecycle(async () => { throw primary; },
      async () => { snapshot = observation.finish('FAILED', target()); },
      async () => { throw new Error('private-attachment'); },
      async () => { throw new Error('private-restoration'); }, (stage) => secondary.push(stage)),
    (error) => error === primary);
    assert.deepEqual(snapshot.elapsedMs, expected);
    assert.equal(snapshot.classification, 'INCOMPLETE');
    assert.deepEqual(secondary, ['PUBLISH', 'RESTORE']);
    assert.equal(JSON.stringify(snapshot).includes('private'), false);
  }
});
