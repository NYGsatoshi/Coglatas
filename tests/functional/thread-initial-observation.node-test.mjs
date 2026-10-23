import assert from 'node:assert/strict';
import test from 'node:test';
import { EventEmitter } from 'node:events';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { stripTypeScriptTypes } from 'node:module';
import { observeInitialThreadShape, readInitialThreadDom, threadResponseShape, validateInitialThreadObservation } from './helpers/thread-initial-observation.mjs';
import { observeThreadRequests, validateThreadObservation, withThreadProbeLifecycle } from './helpers/thread-probe-observation.mjs';
import { readThreadProbe, THREAD_PROBE_ATTACHMENT, validateThreadProbe } from './helpers/thread-probe-diagnostics.mjs';
import { attemptDiagnostic } from './fixtures/functional-diagnostics.mjs';
import FunctionalEvidenceReporter from './fixtures/functional-evidence-reporter.mjs';

const privateValue = 'PRIVATE_BODY_COOKIE_URL_RESOURCE';
const dom = () => ({ workspacePresent: true, workspaceVisible: true, loadingPresent: true, loadingVisible: true,
  errorPresent: false, errorVisible: false, rootPresent: false, rootVisible: false });
const dto = () => ({ rootMessage: { id: privateValue, conversationId: privateValue, body: privateValue },
  summary: { threadRootMessageId: privateValue, replyCount: 0 }, replies: [], hasMore: false, maximumReplies: 100 });
const fixture = () => {
  const page = new EventEmitter(), observation = observeThreadRequests(page, () => true);
  observation.mark('AUTHORIZED_OBSERVER_REGISTERED');
  const request = {}, response = { request: () => request, status: () => 200 };
  page.emit('request', request); observation.selectAuthorized(request); page.emit('response', response);
  observation.mark('AUTHORIZED_READ');
  return { page, observation, request };
};
const probe = () => {
  const { observation } = fixture();
  return { phases: [], target: null, observation: observation.finish('FAILED'),
    initial: { responseShape: threadResponseShape(dto(), privateValue, privateValue), failureDom: dom() } };
};

test('actual response comparisons export booleans and distinguish invalid identity/count shapes without content', () => {
  assert.ok(Object.values(threadResponseShape(dto(), privateValue, privateValue)).every(Boolean));
  for (const value of [null, {}, { ...dto(), replies: null }, { ...dto(), rootMessage: { id: 'other' } },
    { ...dto(), summary: { ...dto().summary, replyCount: 1 } }, { ...dto(), replies: [{ id: privateValue, conversationId: 'other' }] }]) {
    const shape = threadResponseShape(value, privateValue, privateValue);
    assert.ok(Object.values(shape).every((flag) => typeof flag === 'boolean'));
    assert.ok(Object.values(shape).some((flag) => !flag));
    assert.equal(JSON.stringify(shape).includes(privateValue), false);
  }
});

test('body observation never joins progress and a sealed lease rejects late completion or rejection', async () => {
  for (const outcome of ['resolve-before-seal', 'resolve-after-seal', 'reject-after-seal', 'never-resolves']) {
    let resolve, reject;
    const body = new Promise((yes, no) => { resolve = yes; reject = no; });
    const initial = { responseShape: null, failureDom: null };
    const seal = observeInitialThreadShape(initial, () => body, privateValue, privateValue);
    assert.equal(typeof seal, 'function', 'Starting diagnostics returns immediately, not a body promise.');
    await Promise.resolve();
    if (outcome === 'resolve-before-seal') { resolve(dto()); }
    else { seal(); if (outcome === 'resolve-after-seal') { resolve(dto()); } else if (outcome === 'reject-after-seal') { reject(new Error(privateValue)); } }
    await new Promise((done) => setImmediate(done));
    assert.equal(initial.responseShape === null, outcome !== 'resolve-before-seal');
    seal();
    const sealed = JSON.stringify(initial);
    await new Promise((done) => setImmediate(done));
    assert.equal(JSON.stringify(initial), sealed);
    assert.equal(sealed.includes(privateValue), false);
  }
});

test('precleanup DOM capture inspects fixed structural selectors only, never text, attributes or product globals', async () => {
  const previousDocument = globalThis.document, previousStyle = globalThis.getComputedStyle;
  const seen = [];
  const element = { getBoundingClientRect: () => ({ width: 1, height: 1 }),
    get textContent() { throw new Error('No private DOM text'); }, getAttribute() { throw new Error('No attributes'); } };
  try {
    globalThis.document = { querySelectorAll(selector) { seen.push(selector); return selector.includes('root-message') ? [] : [element]; } };
    globalThis.getComputedStyle = () => ({ visibility: 'visible' });
    const captured = await readInitialThreadDom({ evaluate: (callback) => callback() });
    assert.equal(captured.rootPresent, false); assert.equal(captured.loadingVisible, true);
    assert.equal(seen.length, 4);
    validateInitialThreadObservation({ responseShape: null, failureDom: captured });
  } finally { globalThis.document = previousDocument; globalThis.getComputedStyle = previousStyle; }
});

test('new authorized selection excludes an old response and refuses pre-observer ordinal binding', () => {
  const page = new EventEmitter(), observation = observeThreadRequests(page, () => true);
  const old = {}, fresh = {};
  page.emit('request', old); observation.mark('AUTHORIZED_OBSERVER_REGISTERED');
  page.emit('request', fresh); page.emit('response', { request: () => old, status: () => 200 });
  observation.selectAuthorized(fresh); page.emit('response', { request: () => fresh, status: () => 200 });
  const data = observation.finish('FAILED');
  assert.equal(data.events.find((event) => event.kind === 'AUTHORIZED_SELECTED').request, 2);
  const altered = structuredClone(data);
  altered.events.find((event) => event.kind === 'AUTHORIZED_SELECTED').request = 1;
  assert.throws(() => validateThreadObservation(altered));
});

test('optional initial receipt strictly rejects unknown fields, nonbooleans, invented visibility and unbound shape', () => {
  const valid = probe();
  const mutations = [
    (value) => { value.initial.private = privateValue; },
    (value) => { value.initial.responseShape.rootMatches = privateValue; },
    (value) => { value.initial.failureDom.rootVisible = true; },
    (value) => { value.initial.failureDom.cookie = privateValue; },
    (value) => { value.observation.events = value.observation.events.filter((event) => event.kind !== 'AUTHORIZED_SELECTED'); },
    (value) => { value.observation.events.find((event) => event.kind === 'RESPONSE').status = 500; },
    (value) => { value.observation.events = value.observation.events.filter((event) => event.kind !== 'AUTHORIZED_READ'); },
  ];
  for (const mutate of mutations) {
    const altered = structuredClone(valid); mutate(altered);
    assert.throws(() => validateThreadProbe(altered));
    assert.deepEqual(readThreadProbe([{ name: THREAD_PROBE_ATTACHMENT, body: Buffer.from(JSON.stringify(altered)) }]), { state: 'INVALID', data: null });
  }
  assert.deepEqual(readThreadProbe([{ name: THREAD_PROBE_ATTACHMENT, body: Buffer.alloc(4097) }]), { state: 'INVALID', data: null });
  assert.deepEqual(readThreadProbe([{}, ...Array(2).fill({ name: THREAD_PROBE_ATTACHMENT, body: Buffer.from(JSON.stringify(valid)) })]), { state: 'INVALID', data: null });
});

test('24-event receipt with all new boolean fields remains below4096 bytes and truncation stays INCOMPLETE', () => {
  const value = probe();
  while (value.observation.events.length < 24) {
    value.observation.events.push({ kind: 'REQUEST_FINISHED', request: 1, status: null });
  }
  value.observation.truncated = true;
  const accepted = validateThreadProbe(value);
  assert.ok(Buffer.byteLength(JSON.stringify(accepted)) < 4096);
  assert.equal(accepted.observation.classification, 'INCOMPLETE');
  assert.throws(() => validateThreadProbe({ ...value, observation: { ...value.observation, classification: 'UNKNOWN' } }));
});

test('canonical failure attribution remains conservative when secondary diagnostics or restoration fail', () => {
  for (const stage of ['CAPTURE', 'RESTORE', privateValue]) {
    const result = attemptDiagnostic('FUNC-MSG-001', { status: 'failed', retry: 0, errors: [{ message: 'TimeoutError: secondary' }],
      steps: [{ category: 'test.step', title: 'FUNC-MSG-001 / MSG-THREAD-DENIAL', error: { matcherResult: {} } }] },
    [{ type: 'restricted-thread-secondary-v1', description: stage }]);
    assert.equal(result.status, 'failed'); assert.equal(result.failureKind, 'UNCLASSIFIED_FAILURE');
    assert.equal(result.failedStepId, 'MSG-THREAD-DENIAL');
    assert.equal(JSON.stringify(result).includes(privateValue), false);
  }
});

test('canonical attempt retains validated Thread evidence but never promotes missing, invalid or truncated diagnostics to a result', () => {
  const valid = probe();
  for (const attachments of [[], [{ name: THREAD_PROBE_ATTACHMENT, body: Buffer.from(privateValue) }],
    [{ name: THREAD_PROBE_ATTACHMENT, body: Buffer.from(JSON.stringify(valid)) }]]) {
    const output = attemptDiagnostic('FUNC-MSG-001', { status: 'failed', retry: 0, attachments,
      steps: [{ category: 'test.step', title: 'FUNC-MSG-001 / MSG-THREAD-DENIAL', error: { matcherResult: {} } }] });
    assert.equal(output.status, 'failed'); assert.equal(output.failedStepId, 'MSG-THREAD-DENIAL');
    assert.equal(output.threadProbe.state, attachments.length ? attachments[0].body.toString() === privateValue ? 'INVALID' : 'AVAILABLE' : 'MISSING');
    assert.equal(JSON.stringify(output).includes(privateValue), false);
    if (output.threadProbe.data) { assert.equal(output.threadProbe.data.observation.classification, 'INCOMPLETE'); }
  }
  const altered = probe(); altered.observation.truncated = true;
  assert.equal(validateThreadProbe(altered).observation.classification, 'INCOMPLETE');
});

test('actual canonical reporter publishes the bounded probe with failed owner and refuses raw attachment projection', () => {
  const previousDirectory = process.cwd(), directory = mkdtempSync(join(tmpdir(), 'canonical-thread-retention-'));
  const envKeys = ['COGLATAS_FUNCTIONAL_DOMAIN', 'COGLATAS_FUNCTIONAL_SELECTED_GATES', 'TARGET_SHA', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT'];
  const previousEnv = Object.fromEntries(envKeys.map((key) => [key, process.env[key]]));
  try {
    process.chdir(directory);
    mkdirSync('tests/functional', { recursive: true }); mkdirSync('artifacts/functional', { recursive: true });
    writeFileSync('tests/functional/quarantine.json', '{"entries":[]}');
    writeFileSync('artifacts/functional/setup-collaboration.json', '{"setupSeconds":1}');
    Object.assign(process.env, { COGLATAS_FUNCTIONAL_DOMAIN: 'collaboration', COGLATAS_FUNCTIONAL_SELECTED_GATES: 'functional-fast',
      TARGET_SHA: 'a'.repeat(40), GITHUB_RUN_ID: '100', GITHUB_RUN_ATTEMPT: '1' });
    const validBody = Buffer.from(JSON.stringify(probe()));
    for (const body of [validBody, Buffer.from(privateValue), Buffer.alloc(4097),
      Buffer.from(JSON.stringify({ ...probe(), cookie: privateValue }))]) {
      const tests = ['FUNC-MSG-001', 'FUNC-NOTIF-001'].map((journey) => ({ expectedStatus: 'passed',
        annotations: [{ type: 'journey', description: journey }, { type: 'backend', description: 'real' }],
        results: [{ retry: 0, status: journey === 'FUNC-MSG-001' ? 'failed' : 'passed', duration: 1,
          attachments: journey === 'FUNC-MSG-001' ? [{ name: THREAD_PROBE_ATTACHMENT, body }] : [],
          steps: [{ category: 'test.step', title: `${journey} / ${journey === 'FUNC-MSG-001' ? 'MSG-THREAD-DENIAL' : 'NOTIF-01'}`,
            ...(journey === 'FUNC-MSG-001' ? { error: { matcherResult: {}, message: privateValue } } : {}) }] }] }));
      const reporter = new FunctionalEvidenceReporter(); reporter.onBegin({}, { allTests: () => tests });
      assert.deepEqual(reporter.onEnd({ status: 'failed' }), { status: 'failed' });
      const diagnostics = readFileSync('artifacts/functional/diagnostics-collaboration.json', 'utf8');
      assert.equal(diagnostics.includes(privateValue), false);
      const outcome = JSON.parse(diagnostics).journeys[0];
      assert.equal(outcome.status, 'FAIL'); assert.equal(outcome.attempts[0].status, 'failed');
      assert.equal(outcome.attempts[0].threadProbe.state, body === validBody ? 'AVAILABLE' : 'INVALID');
      assert.equal(outcome.attempts[0].failedStepId, 'MSG-THREAD-DENIAL');
    }
  } finally {
    process.chdir(previousDirectory);
    for (const key of envKeys) { if (previousEnv[key] === undefined) { delete process.env[key]; } else { process.env[key] = previousEnv[key]; } }
    rmSync(directory, { recursive: true, force: true });
  }
});

// Execute the exact current owner callback, not a rewritten approximation. All transport/DOM here is synthetic.
function ownerCallback() {
  const source = readFileSync(new URL('./messaging/collaboration-owner-journey.spec.ts', import.meta.url), 'utf8');
  const marker = "await diagnosticStep('FUNC-MSG-001 / MSG-THREAD-DENIAL";
  const start = source.indexOf('const deniedDraft', source.indexOf(marker));
  const end = source.indexOf('\n        });', start);
  assert.ok(start > 0 && end > start);
  const stripped = stripTypeScriptTypes(`async function replay() {\n${source.slice(start, end)}\n}`, { mode: 'strip' });
  return stripped.slice(stripped.indexOf('{') + 1, stripped.lastIndexOf('}'));
}

for (const condition of ['missing-root', 'shape-unavailable', 'dom-unavailable', 'restore-failure', 'failure-capture-error', 'request-failure', 'body-never-resolves', 'root-pass-body-never-resolves']) {
  test(`exact owner callback retains first root failure before cleanup under ${condition}; mocked transport only`, { timeout: 2000 }, async () => {
    const primary = new Error('private-primary'), attachments = [], secondary = [], ordering = [];
    let cleanup = false, requestListener, bodyReadCount = 0, bodyStartedAfterMatcher = false;
    const page = new EventEmitter();
    const old = { method: () => 'GET', url: () => `http://127.0.0.1/api/messages/${privateValue}/thread` };
    const request = { ...old };
    const response = { request: () => request, status: () => 200, json: async () => {
      bodyReadCount++; bodyStartedAfterMatcher = ordering.includes('mandatory-root-assertion');
      if (condition.endsWith('body-never-resolves') || condition === 'body-never-resolves') { return new Promise(() => {}); }
      if (condition === 'shape-unavailable') { throw new Error(privateValue); } return dto();
    } };
    request.response = async () => response;
    page.url = () => 'private-retained-url';
    page.waitForRequest = (predicate, options) => {
      assert.equal(options.timeout, 20000);
      if (condition === 'request-failure') { return Promise.reject(primary); }
      return new Promise((resolve) => { requestListener = (value) => { if (predicate(value)) { page.off('request', requestListener); resolve(value); } }; page.on('request', requestListener); });
    };
    page.waitForResponse = (predicate, options) => {
      assert.equal(options.timeout, 20000);
      return new Promise((resolve) => {
        const listener = (value) => { if (predicate(value)) { page.off('response', listener); resolve(value); } };
        page.on('response', listener);
      });
    };
    page.getByTestId = (id) => ({
      click: async () => {
        assert.equal(id, `open-message-thread-${privateValue}`);
        ordering.push('native-action');
        page.emit('response', { request: () => old, status: () => 200 });
        page.emit('request', request); page.emit('response', response);
      },
      evaluateAll: async () => id === 'thread-workspace-toggle' ? false :
        { singleTab: true, visible: true, enabled: true, selected: true, inViewport: true, centerReceivesPointer: true },
      fill: async () => { ordering.push('next-functional-action'); throw primary; },
    });
    page.evaluate = async () => {
      assert.equal(cleanup, false); ordering.push('precleanup-dom');
      if (condition === 'dom-unavailable') { throw new Error(privateValue); } return dom();
    };
    const bindings = { page, runToken: privateValue, messageId: privateValue, conversationId: privateValue, messageBody: privateValue,
      api: {}, actorACredentials: () => ({}), restrictedCredentials: () => { throw new Error('Restricted login must not be reached'); },
      observeThreadRequests, withThreadProbeLifecycle, validateThreadProbe, THREAD_PROBE_ATTACHMENT,
      observeInitialThreadShape, readInitialThreadDom,
      boundedResponsePreview: async (value) => { assert.equal(value, response, 'Only the newly selected Request response may reach the status assertion.'); return 'bounded'; },
      expect: (value) => ({ toBe: (expected) => assert.equal(value, expected), toContainText: async (expected) => {
        assert.equal(expected, privateValue); ordering.push('mandatory-root-assertion');
        if (condition === 'root-pass-body-never-resolves') { return; } throw primary;
      } }),
      captureFunctionalFailure: async () => {
        assert.equal(cleanup, false); ordering.push('failure-capture');
        if (condition === 'failure-capture-error') { throw new Error(privateValue); }
      },
      testInfo: { attach: async (name, attachment) => {
        assert.equal(cleanup, false); ordering.push('attachment'); attachments.push({ name, ...attachment });
      } },
      loginViaApi: async () => { cleanup = true; ordering.push('restore'); if (condition === 'restore-failure') { throw new Error(privateValue); } },
      reportThreadSecondary: (stage) => secondary.push(stage),
    };
    const run = new (Object.getPrototypeOf(async function () {}).constructor)(...Object.keys(bindings), ownerCallback());
    await assert.rejects(run(...Object.values(bindings)), (error) => error === primary);
    assert.deepEqual(ordering, ['native-action', ...(condition === 'request-failure' ? [] : ['mandatory-root-assertion']),
      ...(condition === 'root-pass-body-never-resolves' ? ['next-functional-action'] : []), 'precleanup-dom', 'failure-capture', 'attachment', 'restore']);
    const output = readThreadProbe(attachments);
    assert.equal(output.state, 'AVAILABLE'); assert.equal(output.data.observation.outcome, 'FAILED');
    assert.equal(output.data.observation.classification, 'INCOMPLETE');
    assert.equal(output.data.observation.events.some((event) => event.kind === 'SESSION_REPLACEMENT_STARTED'), false);
    assert.equal(output.data.initial.failureDom === null, condition === 'dom-unavailable');
    // The immediately rejected root can seal before a ready body observer runs; null is explicitly unknown.
    if (['shape-unavailable', 'request-failure', 'body-never-resolves', 'root-pass-body-never-resolves'].includes(condition)) {
      assert.equal(output.data.initial.responseShape, null);
    }
    assert.equal(JSON.stringify(output).includes(privateValue), false);
    assert.deepEqual(secondary, condition === 'restore-failure' ? ['RESTORE'] : condition === 'failure-capture-error' ? ['CAPTURE'] : []);
    if (condition !== 'request-failure') {
      assert.equal(bodyReadCount, 1); assert.equal(bodyStartedAfterMatcher, true);
    }
  });
}
