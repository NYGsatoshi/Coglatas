import assert from 'node:assert/strict';
import test from 'node:test';
import { EventEmitter } from 'node:events';
import { isAuthorizationChangeFrame, isWorkspaceRevocationFrame, observeAuthorizationChanges } from './real-backend-authorization-frame.mjs';

const userId = 'fixture-user';
const workspaceId = 'fixture-workspace';
const event = {
  eventType: 'Security.AuthorizationStateChanged.v1',
  payloadSchemaVersion: 1,
  payload: { affectedUserId: userId, scopeType: 'workspace', scopeId: workspaceId, change: 'revoked' },
};
const wire = (value = event) => `${JSON.stringify({ type: 1, target: 'DurableEvent', arguments: [value] })}\u001e`;

test('observes the matching real revocation in text and binary SignalR frames', () => {
  assert.equal(isWorkspaceRevocationFrame({ payload: wire() }, userId, workspaceId), true);
  assert.equal(isWorkspaceRevocationFrame({ payload: Buffer.from(wire()) }, userId, workspaceId), true);
});

test('ignores unrelated records in a batched SignalR frame', () => {
  const payload = `{"type":6}\u001e${wire()}{"type":7}\u001e`;
  assert.equal(isWorkspaceRevocationFrame({ payload }, userId, workspaceId), true);
});

for (const [field, value] of [
  ['affectedUserId', 'other-user'], ['scopeId', 'other-workspace'],
  ['scopeType', 'project'], ['change', 'granted'],
]) {
  test(`does not satisfy the fixture handoff with foreign ${field}`, () => {
    assert.equal(isWorkspaceRevocationFrame({ payload: wire({ ...event, payload: { ...event.payload, [field]: value } }) }, userId, workspaceId), false);
  });
}

test('requires the current authorization event schema and invocation target', () => {
  for (const payload of [
    wire({ ...event, eventType: 'Messaging.MessageCreated.v1' }),
    wire({ ...event, payloadSchemaVersion: 2 }),
    JSON.stringify({ type: 6, target: 'DurableEvent', arguments: [event] }),
    JSON.stringify({ type: 1, target: 'OtherEvent', arguments: [event] }),
    JSON.stringify({ type: 1, target: 'DurableEvent', arguments: [event, event] }),
  ]) {
    assert.equal(isWorkspaceRevocationFrame({ payload }, userId, workspaceId), false);
  }
});

test('malformed or incomplete traffic never claims revocation delivery', () => {
  for (const payload of ['', '{broken', 'null', '{}', undefined, wire({ ...event, payload: null })]) {
    assert.equal(isWorkspaceRevocationFrame({ payload }, userId, workspaceId), false);
  }
});

test('matches a Project archive without treating Workspace revocation as its delivery', () => {
  const expected = { affectedUserId: userId, scopeType: 'project', scopeId: 'fixture-project', change: 'archived' };
  assert.equal(isAuthorizationChangeFrame({ payload: wire({ ...event, payload: expected }) }, expected), true);
  assert.equal(isAuthorizationChangeFrame({ payload: wire() }, expected), false);
});

test('observes only the expected mutation across Hub reconnects and resets each handoff', () => {
  const page = new EventEmitter();
  const observer = observeAuthorizationChanges(page);
  const socket = new EventEmitter();
  socket.url = () => 'http://backend/hubs/app';
  page.emit('websocket', socket);
  socket.emit('framereceived', { payload: wire() });
  const expected = { ...event.payload };
  const first = observer.expectChange(expected);
  expected.change = 'granted';
  socket.emit('framereceived', { payload: wire({ ...event, payload: { ...event.payload, change: 'granted' } }) });
  assert.equal(first.received, false);
  socket.emit('framereceived', { payload: wire() });
  assert.equal(first.received, true);

  const archive = { ...event.payload, change: 'archived' };
  const second = observer.expectChange(archive);
  const unrelated = new EventEmitter();
  unrelated.url = () => 'http://backend/other-hub';
  page.emit('websocket', unrelated);
  unrelated.emit('framereceived', { payload: wire({ ...event, payload: archive }) });
  assert.equal(second.received, false);
  socket.emit('framereceived', { payload: wire() });
  assert.equal(second.received, false);

  const reconnected = new EventEmitter();
  reconnected.url = socket.url;
  page.emit('websocket', reconnected);
  reconnected.emit('framereceived', { payload: wire({ ...event, payload: archive }) });
  assert.equal(second.received, true);
});

test('retains bounded safe grant metadata when delivery precedes the create response', () => {
  const page = new EventEmitter();
  const observer = observeAuthorizationChanges(page);
  const socket = new EventEmitter();
  socket.url = () => 'http://backend/hubs/app';
  page.emit('websocket', socket);
  const grant = { ...event.payload, change: 'granted' };
  socket.emit('framereceived', { payload: wire({ ...event, payload: { ...grant, unrestrictedEvidence: 'discarded' } }) });
  assert.equal(observer.hasObservedChange(grant), true);
  const futureArchive = observer.expectChange({ ...grant, change: 'archived' });
  assert.equal(futureArchive.received, false);
  for (let index = 0; index < 16; index++) {
    socket.emit('framereceived', { payload: wire({ ...event, payload: { ...grant, scopeId: `other-${index}` } }) });
  }
  assert.equal(observer.hasObservedChange(grant), false);
});
