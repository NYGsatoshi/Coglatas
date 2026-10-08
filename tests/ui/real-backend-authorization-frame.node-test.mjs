import assert from 'node:assert/strict';
import test from 'node:test';
import { isWorkspaceRevocationFrame } from './real-backend-authorization-frame.mjs';

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
