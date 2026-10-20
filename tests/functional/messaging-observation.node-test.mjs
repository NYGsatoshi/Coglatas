import assert from 'node:assert/strict';
import test from 'node:test';
import { createMessagingControlBuffer, installMessagingObservation, messagingControlKinds, validateMessagingObservation } from './fixtures/messaging-observation.mjs';
import { createBrowserObservation, validateBrowserObservation } from './fixtures/functional-diagnostics.mjs';

const secret = 'PRIVATE_BODY_TOKEN_URL_COOKIE_PASSWORD';
const control = (target, argument) => JSON.stringify({ type: 1, target, arguments: argument === undefined ? [] : [argument] }) + '\u001e';
const sample = () => ({ documentStartedAtMs: 1000, storageReadable: true, storageWritable: true,
  events: [{ atMs: 1001, kind: 'input', composerPresent: true, draftEnabled: true, sendEnabled: true, draftFocused: true, connection: 'Connected' }],
  eventsTruncated: 0, webSocketObserved: true, controls: [{ atMs: 1002, openedAtMs: 1000, kind: 'AuthorizationInvalidated' }], controlsTruncated: 0 });

test('control diagnostics retain only known invocation discriminants and never their payloads', () => {
  assert.deepEqual(messagingControlKinds(control('AuthorizationInvalidated', secret)), ['AuthorizationInvalidated']);
  assert.deepEqual(messagingControlKinds(control('DurableEvent', { eventType: 'Security.AuthorizationStateChanged.v1', body: secret, token: secret })), ['AuthorizationStateChanged']);
  for (const value of [Buffer.from(control('AuthorizationInvalidated')), secret, 'x'.repeat(65537), control(secret),
    control('DurableEvent', { eventType: 'Messaging.MessageCreated.v1', body: secret }), JSON.stringify({ type: 6, target: 'AuthorizationInvalidated' })]) {
    assert.deepEqual(messagingControlKinds(value), []);
  }
  const buffer = createMessagingControlBuffer();
  for (let index = 0; index < 50; index += 1) { buffer.append(control('AuthorizationInvalidated', secret), 1000 + index); }
  const record = buffer.snapshot();
  assert.equal(record.controls.length, 48);
  assert.equal(record.controlsTruncated, 2);
  assert.equal(record.controls[0].atMs, 1002);
  assert.ok(!JSON.stringify(record).includes(secret));
  buffer.reset();
  assert.deepEqual(buffer.snapshot(), { webSocketObserved: false, controls: [], controlsTruncated: 0 });
  buffer.append(control('AuthorizationInvalidated'), 2005, 1000);
  buffer.append(control('AuthorizationInvalidated'), 2006, 2001);
  assert.deepEqual(buffer.snapshot(2000).controls, [{ atMs: 2006, openedAtMs: 2001, kind: 'AuthorizationInvalidated' }],
    'late frames from sockets opened before this document must not be attributed to it');
});

test('Messaging structural validation rejects arbitrary fields, text, identities, malformed times and unbounded rows', () => {
  const data = sample();
  assert.equal(validateMessagingObservation(data), data);
  assert.equal(validateMessagingObservation(null), null);
  for (const altered of [{ ...data, body: secret }, { ...data, storageReadable: secret },
    { ...data, documentStartedAtMs: Infinity }, { ...data, events: [...data.events, { ...data.events[0], value: secret }] },
    { ...data, events: [{ ...data.events[0], connection: secret }] }, { ...data, controls: [{ ...data.controls[0], kind: secret }] },
    { ...data, controls: [{ ...data.controls[0], atMs: true }] }, { ...data, events: new Array(49).fill(data.events[0]) }]) {
    assert.throws(() => validateMessagingObservation(altered));
  }
  const browser = createBrowserObservation('http://127.0.0.1:5080').data;
  browser.messaging = data;
  assert.equal(validateBrowserObservation(browser), browser);
  const legacy = { ...browser, schemaVersion: 1 };
  delete legacy.messaging;
  assert.equal(validateBrowserObservation(legacy), legacy, 'historical v1 is independently exact-key validated');
  assert.throws(() => validateBrowserObservation({ ...legacy, messaging: data }));
  assert.throws(() => validateBrowserObservation({ ...browser, messaging: { ...data, body: secret } }));
});

function documentFixture(mode = 'available') {
  const handlers = new Map(), entries = new Map();
  let mutation, now = 1000;
  const draft = { disabled: false, matches: (selector) => selector === '[data-testid="message-draft"]' };
  for (const key of ['value', 'textContent', 'innerText']) { Object.defineProperty(draft, key, { get: () => { throw new Error('Private values must never be read'); } }); }
  const send = { disabled: false };
  const indicator = { classList: { contains: (name) => name === 'realtime-indicator--quiet' } };
  const document = { activeElement: draft, querySelector: (selector) => {
    if (selector.includes('message-draft')) { return draft; }
    if (selector.includes('send-message')) { return send; }
    return indicator;
  },
    addEventListener: (type, handler) => handlers.set(type, handler) };
  const storage = { get length() { if (mode === 'length-denied') { throw new Error(secret); } return entries.size; },
    setItem: (key, value) => { if (mode === 'read-only') { throw new Error(secret); } entries.set(key, value); },
    removeItem: (key) => entries.delete(key) };
  const window = { get sessionStorage() { if (mode === 'unavailable') { throw new Error(secret); } return storage; } };
  const context = { window, document, crypto: { randomUUID: () => 'synthetic-probe-only' }, Date: { now: () => now++ },
    MutationObserver: class {
      constructor(callback) { mutation = callback; }
      observe(target) { assert.equal(target, document); }
    } };
  installMessagingObservation('testObservation', context);
  return { state: window.testObservation, draft, send, document, entries,
    mutate: () => mutation(), event: (type) => handlers.get(type)({ target: draft, get data() { throw new Error(secret); } }) };
}

test('browser installer observes structure, input and focus while never reading draft values or event data', () => {
  const fixture = documentFixture();
  assert.equal(fixture.entries.size, 0, 'the unique diagnostic probe must be removed');
  fixture.event('input');
  fixture.draft.disabled = true;
  fixture.mutate();
  fixture.document.activeElement = null;
  fixture.event('focusout');
  const record = { documentStartedAtMs: fixture.state.documentStartedAtMs,
    storageReadable: fixture.state.storageReadable, storageWritable: fixture.state.storageWritable,
    events: fixture.state.events, eventsTruncated: fixture.state.eventsTruncated, webSocketObserved: false, controls: [], controlsTruncated: 0 };
  assert.equal(validateMessagingObservation(record), record);
  assert.deepEqual(Array.from(record.events, (event) => event.kind), ['initial', 'input', 'structure', 'blur']);
  assert.equal(record.events[2].draftEnabled, false);
  assert.equal(record.events[3].draftFocused, false);
  assert.ok(!JSON.stringify(record).includes(secret));
  for (let index = 0; index < 50; index += 1) { fixture.event('input'); }
  assert.equal(fixture.state.events.length, 48);
  assert.equal(fixture.state.eventsTruncated, 6);
  fixture.state.frozen = true;
  fixture.event('input');
  assert.equal(fixture.state.eventsTruncated, 6);
});

test('storage diagnostics distinguish denied access from denied writes without reading persisted values', () => {
  for (const [mode, readable, writable] of [['available', true, true], ['unavailable', false, false], ['length-denied', false, true], ['read-only', true, false]]) {
    const fixture = documentFixture(mode);
    assert.equal(fixture.state.storageReadable, readable);
    assert.equal(fixture.state.storageWritable, writable);
    assert.equal(fixture.entries.size, 0);
    assert.ok(!JSON.stringify(fixture.state).includes(secret));
  }
});

test('maximum diagnostic buffers fit the unchanged attachment budget without relaxing failure acceptance', () => {
  const observation = createBrowserObservation('http://127.0.0.1:5080');
  for (let index = 0; index < 80; index += 1) {
    observation.onRequest({url:()=> 'http://127.0.0.1:5080/api/messages/private/thread/messages', method:()=> 'POST'});
  }
  const data = sample();
  data.documentStartedAtMs = 8640000000000000;
  data.events = Array.from({length:48},()=>({...data.events[0],atMs:8640000000000000,connection:'Reconnecting',kind:'structure'}));
  data.controls = Array.from({length:48},()=>({...data.controls[0],atMs:8640000000000000,openedAtMs:8640000000000000,kind:'AuthorizationStateChanged'}));
  data.eventsTruncated = data.controlsTruncated = 1000000;
  observation.data.messaging = data;
  validateBrowserObservation(observation.data);
  assert.ok(Buffer.byteLength(JSON.stringify(observation.data)) < 32768);
});
