import { expect, test, type Page, type Response, type TestInfo } from '@playwright/test';
import { installThreadApi, threadFixtureIds } from '../ui/fixtures/thread-api';
import { responseToFreshRequest } from '../functional/helpers/fresh-request-response';
import { installThreadDiagnosticGate } from './thread-gate';

const { rootMessageId, conversationId } = threadFixtureIds;
const body = 'Pinned parent body', reply = 'Existing thread reply', draft = 'Synthetic retained private draft';
const matches = (request: { method(): string; url(): string }) => request.method() === 'GET' &&
  new URL(request.url()).pathname === `/api/messages/${rootMessageId}/thread`;
const note = (id: string) => ({ annotation: [{ type: 'investigationCase', description: id }, { type: 'backend', description: 'mocked-transport' }] });
const gates = new WeakMap<Page, Awaited<ReturnType<typeof installThreadDiagnosticGate>>>();
test.afterEach(({ page }) => { gates.get(page)?.held.forEach((held) => held.release()); });

async function prepare(page: Page) {
  await installThreadApi(page);
  const gate = await installThreadDiagnosticGate(page);
  gates.set(page, gate);
  await page.goto(`/app/dm/${conversationId}`);
  const first = await responseToFreshRequest(page, matches, () => page.getByTestId(`open-message-thread-${rootMessageId}`).click());
  expect(first.status()).toBe(200); // Positive control: the same actor/UI can read the fixture.
  await expect(page.getByTestId('thread-root-message')).toContainText(body);
  await page.getByTestId('thread-reply-draft').fill(draft);
  await page.getByTestId('thread-workspace-toggle').click();
  await expect(page.getByTestId('thread-workspace-toggle')).toHaveAttribute('aria-expanded', 'true');
  return gate;
}

async function denied(response: Response) {
  expect(response.status()).toBe(400);
  const envelope = await response.json();
  expect(Object.keys(envelope)).toEqual(['error']);
  expect(envelope.error).toBe('Message thread not found.');
  const text = await response.text();
  for (const value of [rootMessageId, conversationId, body, reply, draft, 'rootMessage', 'replyCount', 'participantDisplayNames']) {
    expect(text).not.toContain(value);
  }
}

async function cleared(page: Page) {
  await expect(page.getByTestId('thread-load-failure')).toBeVisible();
  for (const id of ['thread-root-message', 'thread-replies', 'thread-reply-draft', 'message-draft']) {
    await expect(page.getByTestId(id)).toHaveCount(0);
  }
  await expect(page.getByRole('tab')).toHaveCount(0);
  await expect(page.getByRole('tabpanel')).not.toHaveAttribute('aria-labelledby', /.+/u);
  await expect(page.getByTestId('thread-workspace-toggle')).toHaveAttribute('aria-expanded', 'false');
  for (const value of [body, reply, draft]) { await expect(page.getByText(value, { exact: true })).toHaveCount(0); }
  const storage = await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)].join());
  expect(storage).not.toContain(draft);
}

async function background(page: Page, gate: Awaited<ReturnType<typeof installThreadDiagnosticGate>>) {
  gate.hold = true; gate.rejectNextReply = true;
  const response = page.waitForResponse((value) => matches(value.request()));
  // A real product UI validation failure starts refreshThreadProjection while
  // retaining the ready body and draft. No Angular service/global is injected.
  await page.getByTestId('send-thread-reply').click();
  await expect.poll(() => gate.rejectedReplies).toBe(1);
  await expect.poll(() => gate.held.length).toBe(1);
  await expect(page.getByTestId('thread-root-message')).toContainText(body);
  await expect(page.getByTestId('thread-reply-draft')).toHaveValue(draft);
  return { response };
}

async function attach(info: TestInfo, value: object) {
  await info.attach('thread-matrix-observation-v1', { body: Buffer.from(JSON.stringify(value)), contentType: 'application/json' });
}

test('TD-01 ordinary retained tab activation observes a new denied projection', note('TD-01'), async ({ page }, info) => {
  const phases: string[] = [];
  const gate = await prepare(page);
  gate.deny = true;
  const response = await responseToFreshRequest(page, matches, () => page.getByTestId(`thread-tab-${rootMessageId}`).click(), (phase) => phases.push(phase));
  await denied(response); await cleared(page);
  await attach(info, { caseId: 'TD-01', classification: 'ORDINARY_FRESH_DENIAL', phases, backgroundStatus: null, freshStatus: 400,
    earlyRedaction: false, clickPerformed: true, oldResponseExcluded: null, uiCleared: true });
});

test('TD-02 an earlier denied background response legitimately removes the next activation target', note('TD-02'), async ({ page }, info) => {
  const gate = await prepare(page);
  const pending = await background(page, gate);
  const beforeRelease = gate.gets;
  gate.deny = true; gate.held[0].release();
  await denied(await pending.response); await cleared(page);
  expect(gate.gets).toBe(beforeRelease);
  // The next click's precondition is absent. Do not force a missing tab or
  // represent zero new requests as proof of current-actor isolation.
  await expect(page.getByTestId(`thread-tab-${rootMessageId}`)).toHaveCount(0);
  await attach(info, { caseId: 'TD-02', classification: 'LEGITIMATE_EARLY_REDACTION', phases: [], backgroundStatus: 400, freshStatus: null,
    earlyRedaction: true, clickPerformed: false, oldResponseExcluded: null, uiCleared: true });
});

test('TD-03 an old denied background response cannot satisfy a new retained-tab request', note('TD-03'), async ({ page }, info) => {
  const phases: string[] = [];
  const gate = await prepare(page);
  const pending = await background(page, gate);
  gate.deny = true;
  const operation = responseToFreshRequest(page, matches, () => page.getByTestId(`thread-tab-${rootMessageId}`).click(), (phase) => {
    phases.push(phase);
    if (phase === 'REQUEST_OBSERVED') { gate.held[0].release(); }
  });
  await expect.poll(() => gate.held.length).toBe(2);
  await denied(await pending.response);
  // Flush rendering after the old response; the newer generation still owns
  // the visible loading tab until its own held response completes.
  await page.evaluate(() => new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
  await expect(page.getByTestId(`thread-tab-${rootMessageId}`)).toHaveCount(1);
  await expect(page.getByTestId('thread-load-failure')).toHaveCount(0);
  expect(phases).not.toContain('RESPONSE_OBSERVED');
  gate.held[1].release();
  await denied(await operation); await cleared(page);
  await attach(info, { caseId: 'TD-03', classification: 'OLD_RESPONSE_EXCLUDED', phases, backgroundStatus: 400, freshStatus: 400,
    earlyRedaction: false, clickPerformed: true, oldResponseExcluded: true, uiCleared: true });
});
