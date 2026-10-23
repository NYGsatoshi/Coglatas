import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { readFileSync } from 'node:fs';
import { stripTypeScriptTypes } from 'node:module';
import test from 'node:test';

const source = readFileSync(new URL('./helpers/fresh-request-response.ts', import.meta.url), 'utf8');
const code = stripTypeScriptTypes(source, { mode: 'strip' });
const { responseToFreshRequest } = await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);

function requestPage() {
  const events = new EventEmitter();
  return {
    events,
    waitForRequest(predicate) {
      return new Promise((resolve) => {
        function onRequest(request) {
          if (predicate(request)) {
            events.off('request', onRequest);
            resolve(request);
          }
        }
        events.on('request', onRequest);
      });
    }
  };
}

const matchesThread = (request) => request.method === 'GET' && request.operation === 'thread';

test('late authorized response cannot replace the newly emitted restricted thread response', async () => {
  const page = requestPage();
  const oldResponse = { status: 200 };
  const deniedResponse = { status: 400 };
  let oldResponseArrived = false;
  // This request started before the restricted login/request-observation boundary.
  page.events.emit('request', { method: 'GET', operation: 'thread', response: async () => oldResponse });
  let resolveDenied;
  const pendingDenied = new Promise((resolve) => { resolveDenied = resolve; });
  const result = responseToFreshRequest(page, matchesThread, async () => {
    assert.equal(page.events.listenerCount('request'), 1, 'observer is registered before the click');
    page.events.emit('response', oldResponse);
    oldResponseArrived = true;
    page.events.emit('request', { method: 'POST', operation: 'thread', response: async () => oldResponse });
    page.events.emit('request', { method: 'GET', operation: 'other', response: async () => oldResponse });
    page.events.emit('request', { method: 'GET', operation: 'thread', response: () => pendingDenied });
  });
  let settled = false;
  void result.then(() => { settled = true; });
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(oldResponseArrived, true);
  assert.equal(settled, false, 'an old 200 cannot settle the selected fresh request');
  resolveDenied(deniedResponse);
  assert.equal(await result, deniedResponse);
});

test('fresh response status remains visible without conversion or retry', async () => {
  const page = requestPage();
  const response = { status: 200 };
  let actions = 0;
  const result = await responseToFreshRequest(page, matchesThread, async () => {
    actions += 1;
    page.events.emit('request', { method: 'GET', operation: 'thread', response: async () => response });
  });
  assert.equal(result, response, 'a fresh unauthorized 200 must still fail the owner assertion');
  assert.equal(actions, 1);
});

test('missing HTTP response fails instead of supplying a denial', async () => {
  const page = requestPage();
  await assert.rejects(responseToFreshRequest(page, matchesThread, async () => {
    page.events.emit('request', { method: 'GET', operation: 'thread', response: async () => null });
  }), /without an HTTP response/);
});

test('request observation and action failures propagate without retry', async () => {
  const requestError = new Error('request observation failed');
  await assert.rejects(responseToFreshRequest({ waitForRequest: () => Promise.reject(requestError) }, matchesThread, async () => {}), requestError);
  const actionError = new Error('tab action failed');
  await assert.rejects(responseToFreshRequest({ waitForRequest: () => new Promise(() => {}) }, matchesThread, async () => { throw actionError; }), actionError);
});

test('phase diagnostics distinguish a pending click from a missing newly emitted request', async () => {
  const page = requestPage(), phases = [];
  let finishAction;
  const actionPending = new Promise((resolve) => { finishAction = resolve; });
  const operation = responseToFreshRequest(page, matchesThread, () => actionPending, (phase) => phases.push(phase));
  assert.deepEqual(phases, ['OBSERVER_REGISTERED', 'ACTION_STARTED']);
  page.events.emit('request', { method: 'GET', operation: 'thread', response: async () => ({ status: 400 }) });
  await Promise.resolve();
  assert.deepEqual(phases, ['OBSERVER_REGISTERED', 'ACTION_STARTED', 'REQUEST_OBSERVED']);
  finishAction();
  assert.equal((await operation).status, 400);
  assert.deepEqual(phases, ['OBSERVER_REGISTERED', 'ACTION_STARTED', 'REQUEST_OBSERVED', 'ACTION_COMPLETED', 'WAITING_RESPONSE', 'RESPONSE_OBSERVED']);

  const second = requestPage(), secondPhases = [];
  const missingRequest = responseToFreshRequest(second, matchesThread, async () => {}, (phase) => secondPhases.push(phase));
  await Promise.resolve();
  assert.deepEqual(secondPhases, ['OBSERVER_REGISTERED', 'ACTION_STARTED', 'ACTION_COMPLETED']);
  second.events.emit('request', { method: 'GET', operation: 'thread', response: async () => ({ status: 400 }) });
  assert.equal((await missingRequest).status, 400);
});

test('phase diagnostics distinguish a pending HTTP response without converting its status or retrying the action', async () => {
  const page = requestPage(), phases = [];
  let finishResponse;
  const pending = new Promise((resolve) => { finishResponse = resolve; });
  let actions = 0;
  const operation = responseToFreshRequest(page, matchesThread, async () => {
    actions += 1;
    page.events.emit('request', { method: 'GET', operation: 'thread', response: () => pending });
  }, (phase) => phases.push(phase));
  await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
  assert.equal(phases.at(-1), 'WAITING_RESPONSE');
  finishResponse({ status: 200 });
  assert.equal((await operation).status, 200);
  assert.equal(actions, 1);
  assert.equal(phases.at(-1), 'RESPONSE_OBSERVED');
});

test('phase diagnostics preserve action, request and response failures without exposing their error text', async () => {
  const cases = [
    { page: { waitForRequest: () => new Promise(() => {}) }, action: async () => { throw new Error('private-canary'); }, phase: 'ACTION_FAILED' },
    { page: { waitForRequest: () => Promise.reject(new Error('private-canary')) }, action: async () => {}, phase: 'REQUEST_FAILED' },
    { page: { waitForRequest: () => Promise.resolve({ response: async () => { throw new Error('private-canary'); } }) }, action: async () => {}, phase: 'RESPONSE_FAILED' },
    { page: { waitForRequest: () => Promise.resolve({ response: async () => null }) }, action: async () => {}, phase: 'RESPONSE_MISSING' },
  ];
  for (const item of cases) {
    const phases = [];
    await assert.rejects(responseToFreshRequest(item.page, matchesThread, item.action, (phase) => phases.push(phase)));
    assert.equal(phases.includes(item.phase), true);
    assert.equal(JSON.stringify(phases).includes('private-canary'), false);
  }
});
