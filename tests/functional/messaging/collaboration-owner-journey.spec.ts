/* eslint-disable max-lines, max-lines-per-function -- FCI-06 keeps one canonical multi-step collaboration owner and its bounded evidence together. */
import { randomUUID } from 'node:crypto';

import { expect, type APIRequestContext, type APIResponse, type Response as PlaywrightResponse, test } from '@playwright/test';

import { functionalFullExpansionEnabled } from '../fixtures/functional-gate-selection.mjs';
import { functionalMetadata } from '../fixtures/functional-metadata.mjs';
import { waitForAuthoritativeState } from '../helpers/authoritative-state';
import { loginViaApi, logoutViaApi } from '../helpers/auth';
import { csrfAwareRequest } from '../helpers/csrf';
import { assertSafeResponse, safeResponsePreview } from '../helpers/safe-response';

const smokeEmail = process.env.COGLATAS_BROWSER_SMOKE_EMAIL ?? '';
const smokePassword = process.env.COGLATAS_BROWSER_SMOKE_PASSWORD ?? '';
const recipientEmail = 'browser-smoke-recipient@example.test';
const recipientName = 'Browser Smoke Recipient';
const restrictedEmail = 'browser-smoke-pr05-manager@example.test';

interface JourneyEvidence {
  journeyId: string;
  conversationId?: string;
  messageId?: string;
  notificationId?: string;
  actorAUserId?: string;
  actorBUserId?: string;
  actorAStateBeforeRecipientRead?: ParticipantStateSummary;
  actorAStateAfterRecipientRead?: ParticipantStateSummary;
  actorBStateBeforeRead?: ParticipantStateSummary;
  actorBStateAfterRead?: ParticipantStateSummary;
  uiMessageCount?: number;
  persistedMessageCount?: number;
  realtimeConnected?: boolean;
  realtimeRecipientRenderedCount?: number;
  restrictedSendStatus?: number;
  notificationReadPersisted?: boolean;
  notificationOpenOutcome?: string;
  foreignNotificationOpenStatus?: number;
  removedTargetOpenStatus?: number;
  fullExpansion?: boolean;
}

interface ParticipantStateSummary {
  userId: string | null;
  lastReadMessageId: string | null;
  lastReadAt: string | null;
  unreadCount: number;
}

interface SyntheticCredentials {
  email: string;
  password: string;
}

test.describe('FCI-06 collaboration owner journeys', () => {
  test.setTimeout(120_000);

  test.beforeAll(() => {
    if (process.env.COGLATAS_REAL_BACKEND_SMOKE !== '1') {
      throw new Error(
        'FCI-06 collaboration owners require COGLATAS_REAL_BACKEND_SMOKE=1 and the deterministic full-stack harness.'
      );
    }

    const baseURL = process.env.PLAYWRIGHT_BASE_URL;
    if (!baseURL || /^(?:http:\/\/)?(?:127\.0\.0\.1|localhost):4173(?:\/|$)/iu.test(baseURL)) {
      throw new Error('FCI-06 collaboration owners require the real ASP.NET Core backend, not the static Angular server.');
    }

    if (!smokeEmail.toLowerCase().endsWith('@example.test') || !smokePassword) {
      throw new Error('FCI-06 collaboration owners require deterministic synthetic @example.test credentials.');
    }
  });

  test.beforeEach(({ browserName }, testInfo) => {
    test.skip(
      browserName !== 'chromium' || testInfo.project.name !== 'functional-chromium',
      'FCI-06 mutates one isolated deterministic backend and runs only in the canonical Functional Chromium project.'
    );
  });

  test(
    'FUNC-MSG-001 sends and reads one direct message with durable per-user read state',
    functionalMetadata({
      journeyId: 'FUNC-MSG-001',
      gates: ['functional-fast', 'functional-full', 'functional-extended'],
      domains: ['auth', 'messaging', 'notification'],
      priority: 'p0',
      backend: 'real',
      polarity: 'positive'
    }),
    async ({ page, browser }, testInfo) => {
      const api = page.context().request;
      const runToken = randomUUID().slice(0, 12);
      const messageBody = `FCI-06 direct collaboration ${runToken}`;
      const editedBody = `FCI-06 edited collaboration ${runToken}`;
      const realtimeBody = `FCI-06 realtime reconciliation ${runToken}`;
      const evidence: JourneyEvidence = { journeyId: 'FUNC-MSG-001' };
      let conversationId = '';
      let messageId = '';
      let realtimeMessageId = '';

      try {
        await test.step('FUNC-MSG-001 / MSG-01 actor A creates a direct conversation and sends', async () => {
          const actorA = await loginViaApi(api, actorACredentials());
          evidence.actorAUserId = readOptionalString(actorA, 'id', 'userId');

          await page.goto('/app/messages');
          await expect(page.getByTestId('messages-page')).toBeVisible();
          await page.getByTestId('new-message-button').click();
          await page.getByTestId('recipient-search').fill(recipientName);
          const recipient = page.getByTestId('recipient-option').filter({ hasText: recipientName }).first();
          await expect(recipient).toBeVisible();
          await recipient.click();

          const createResponsePromise = page.waitForResponse((response) =>
            response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/conversations/direct'
          );
          await page.getByTestId('create-conversation-submit').click();
          const createResponse = await createResponsePromise;
          expect(createResponse.status(), await boundedResponsePreview(createResponse)).toBe(200);
          const conversation = asRecord(await createResponse.json(), 'direct conversation response');
          conversationId = requireStringField(conversation, 'id');
          evidence.conversationId = conversationId;

          await expect(page).toHaveURL(new RegExp(`/app/dm/${escapeRegExp(conversationId)}$`, 'u'));
          await expect(page.getByTestId('dm-page')).toBeVisible();
          await page.getByTestId('message-draft').fill(messageBody);

          const sendResponsePromise = page.waitForResponse((response) =>
            response.request().method() === 'POST' &&
            new URL(response.url()).pathname === `/api/conversations/${conversationId}/messages`
          );
          await page.getByTestId('send-message').click();
          const sendResponse = await sendResponsePromise;
          expect(sendResponse.status(), await boundedResponsePreview(sendResponse)).toBe(200);
          const sentMessage = asRecord(await sendResponse.json(), 'send message response');
          messageId = requireStringField(sentMessage, 'id');
          evidence.messageId = messageId;

          const rendered = page.getByTestId('message-timeline').getByText(messageBody, { exact: true });
          await expect(rendered).toHaveCount(1);
          evidence.uiMessageCount = await rendered.count();
        });

        await test.step('FUNC-MSG-001 / MSG-02 authoritative persistence survives a fresh read and reload', async () => {
          const persisted = await readMessages(api, conversationId);
          const matching = persisted.filter((message) =>
            readOptionalString(message, 'id') === messageId && readOptionalString(message, 'body') === messageBody
          );
          expect(matching, 'fresh authoritative message read').toHaveLength(1);
          evidence.persistedMessageCount = matching.length;

          await page.reload();
          await expect(page.getByTestId('message-timeline').getByText(messageBody, { exact: true })).toHaveCount(1);
          evidence.actorAStateBeforeRecipientRead = summarizeParticipantState(
            await readParticipantState(api, conversationId)
          );
        });

        await test.step('FUNC-MSG-001 / MSG-03 actor B reads the same persisted message and advances only its read state', async () => {
          await logoutViaApi(api);
          const actorB = await loginViaApi(api, actorBCredentials());
          evidence.actorBUserId = readOptionalString(actorB, 'id', 'userId');

          const recipientMessages = await readMessages(api, conversationId);
          expect(
            recipientMessages.filter((message) =>
              readOptionalString(message, 'id') === messageId && readOptionalString(message, 'body') === messageBody
            ),
            'recipient fresh message read'
          ).toHaveLength(1);

          const beforeRead = await readParticipantState(api, conversationId);
          evidence.actorBStateBeforeRead = summarizeParticipantState(beforeRead);
          expect(readNumber(beforeRead, 'unreadCount')).toBeGreaterThan(0);

          const markRead = await csrfAwareRequest(api, 'POST', `/api/conversations/${conversationId}/read`, {
            data: { lastReadMessageId: messageId }
          });
          await assertSafeResponse(markRead, {
            label: 'recipient conversation mark-read',
            expectedStatus: 200
          });

          const afterRead = await waitForAuthoritativeState(
            () => readParticipantState(api, conversationId),
            {
              label: 'recipient direct-message read state',
              isReady: (state) =>
                readOptionalString(state, 'lastReadMessageId') === messageId && readNumber(state, 'unreadCount') === 0
            }
          );
          evidence.actorBStateAfterRead = summarizeParticipantState(afterRead);

          await page.goto(`/app/dm/${conversationId}`);
          await expect(page.getByTestId('dm-page')).toBeVisible();
          await expect(page.getByTestId('message-timeline').getByText(messageBody, { exact: true })).toHaveCount(1);
          await page.reload();
          await expect(page.getByTestId('message-timeline').getByText(messageBody, { exact: true })).toHaveCount(1);
        });

        await test.step('FUNC-MSG-001 / MSG-04 actor B read does not mutate actor A state', async () => {
          await logoutViaApi(api);
          await loginViaApi(api, actorACredentials());
          const actorAAfter = summarizeParticipantState(await readParticipantState(api, conversationId));
          evidence.actorAStateAfterRecipientRead = actorAAfter;
          expect(actorAAfter).toEqual(evidence.actorAStateBeforeRecipientRead);
        });

        if (functionalFullExpansionEnabled()) {
          evidence.fullExpansion = true;

          await test.step('FUNC-MSG-001 / MSG-FULL realtime reconciliation keeps one recipient UI entity', async () => {
            const baseURL = process.env.PLAYWRIGHT_BASE_URL;
            if (!baseURL) {
              throw new Error('PLAYWRIGHT_BASE_URL is required for the recipient realtime context.');
            }

            const recipientContext = await browser.newContext({
              baseURL,
              storageState: {
                cookies: [],
                origins: [
                  {
                    origin: new URL(baseURL).origin,
                    localStorage: [{ name: 'coglatas.locale', value: 'en' }]
                  }
                ]
              }
            });
            try {
              await loginViaApi(recipientContext.request, actorBCredentials());
              const recipientPage = await recipientContext.newPage();
              await recipientPage.goto(`/app/dm/${conversationId}`);
              await expect(recipientPage.getByTestId('dm-page')).toBeVisible();
              await expect(recipientPage.getByTestId('realtime-connection-state')).toContainText(
                'Realtime updates connected.',
                { timeout: 30_000 }
              );
              evidence.realtimeConnected = true;

              const realtimeSend = await csrfAwareRequest(api, 'POST', `/api/conversations/${conversationId}/messages`, {
                data: { body: realtimeBody, clientRequestId: randomUUID() }
              });
              await assertSafeResponse(realtimeSend, { label: 'realtime reconciliation message send', expectedStatus: 200 });
              const realtimeMessage = asRecord(await realtimeSend.json(), 'realtime reconciliation message response');
              realtimeMessageId = requireStringField(realtimeMessage, 'id');

              const rendered = recipientPage.getByTestId('message-timeline').getByText(realtimeBody, { exact: true });
              await expect(rendered).toHaveCount(1, { timeout: 15_000 });
              const persisted = await waitForAuthoritativeState(
                () => readMessages(api, conversationId),
                {
                  label: 'realtime message authoritative persistence',
                  isReady: (messages) => messages.some((message) => readOptionalString(message, 'id') === realtimeMessageId)
                }
              );
              expect(
                persisted.filter((message) => readOptionalString(message, 'id') === realtimeMessageId)
              ).toHaveLength(1);
              await expect(rendered).toHaveCount(1);
              evidence.realtimeRecipientRenderedCount = await rendered.count();
            } finally {
              await recipientContext.close();
            }
          });

          await test.step('FUNC-MSG-001 / MSG-NEG non-member sender is denied without protected metadata', async () => {
            await logoutViaApi(api);
            await loginViaApi(api, restrictedCredentials());
            const denied = await csrfAwareRequest(api, 'POST', `/api/conversations/${conversationId}/messages`, {
              data: { body: `FCI-06 denied sender probe ${runToken}`, clientRequestId: randomUUID() }
            });
            evidence.restrictedSendStatus = denied.status();
            expect(denied.status(), await safeResponsePreview(denied)).toBe(400);
            const denialText = await denied.text();
            expect(denialText).not.toContain(messageBody);
            expect(denialText).not.toContain(realtimeBody);
            expect(denialText).not.toContain(recipientEmail);

            await logoutViaApi(api);
            await loginViaApi(api, actorACredentials());
          });

          await test.step('FUNC-MSG-001 / MSG-FULL current edit and delete semantics remain durable', async () => {
            const update = await csrfAwareRequest(api, 'PATCH', `/api/messages/${messageId}`, {
              data: { body: editedBody }
            });
            await assertSafeResponse(update, { label: 'message edit', expectedStatus: 200 });

            const afterEdit = await readMessages(api, conversationId);
            const edited = afterEdit.find((message) => readOptionalString(message, 'id') === messageId);
            if (!edited) {
              throw new Error('Edited message disappeared from the authoritative timeline.');
            }
            expect(readOptionalString(edited, 'body')).toBe(editedBody);
            expect(readOptionalString(edited, 'editedAt')).not.toBeNull();

            await logoutViaApi(api);
            await loginViaApi(api, actorBCredentials());
            const recipientAfterEdit = await readMessages(api, conversationId);
            expect(
              recipientAfterEdit.find((message) => readOptionalString(message, 'id') === messageId)?.body
            ).toBe(editedBody);

            await logoutViaApi(api);
            await loginViaApi(api, actorACredentials());
            const remove = await csrfAwareRequest(api, 'DELETE', `/api/messages/${messageId}`);
            await assertSafeResponse(remove, { label: 'message delete', expectedStatus: 200 });

            const afterDelete = await readMessages(api, conversationId);
            const deletedProjection = afterDelete.find((message) => readOptionalString(message, 'id') === messageId);
            expect(
              afterDelete.some((message) => readOptionalString(message, 'body') === editedBody),
              'deleted content must not remain readable through the authoritative timeline'
            ).toBe(false);
            if (deletedProjection) {
              expect(deletedProjection.isDeleted).toBe(true);
              expect(readOptionalString(deletedProjection, 'body')).toBeNull();
            }

            if (realtimeMessageId) {
              const removeRealtime = await csrfAwareRequest(api, 'DELETE', `/api/messages/${realtimeMessageId}`);
              await assertSafeResponse(removeRealtime, { label: 'realtime message cleanup', expectedStatus: 200 });
            }
          });
        }
      } finally {
        await testInfo.attach('func-msg-001-evidence.json', {
          body: Buffer.from(JSON.stringify(evidence, null, 2)),
          contentType: 'application/json'
        });
      }
    }
  );

  test(
    'FUNC-NOTIF-001 delivers a message notification only to the recipient and persists read state',
    functionalMetadata({
      journeyId: 'FUNC-NOTIF-001',
      gates: ['functional-fast', 'functional-full', 'functional-extended'],
      domains: ['auth', 'messaging', 'notification'],
      priority: 'p0',
      backend: 'real',
      polarity: 'positive'
    }),
    async ({ page }, testInfo) => {
      const api = page.context().request;
      const runToken = randomUUID().slice(0, 12);
      const privateMessageBody = `FCI-06 notification source ${runToken}`;
      const evidence: JourneyEvidence = { journeyId: 'FUNC-NOTIF-001' };
      let conversationId = '';
      let messageId = '';
      let notificationId = '';

      try {
        await test.step('FUNC-NOTIF-001 / NOTIF-01 source event creates no actor-self notification', async () => {
          await loginViaApi(api, actorACredentials());
          const recipientUserId = await resolveRecipientUserId(api);
          const conversation = await createDirectConversation(api, recipientUserId);
          conversationId = requireStringField(conversation, 'id');
          evidence.conversationId = conversationId;

          const send = await csrfAwareRequest(api, 'POST', `/api/conversations/${conversationId}/messages`, {
            data: { body: privateMessageBody, clientRequestId: randomUUID() }
          });
          await assertSafeResponse(send, { label: 'notification source message send', expectedStatus: 200 });
          const sent = asRecord(await send.json(), 'notification source message response');
          messageId = requireStringField(sent, 'id');
          evidence.messageId = messageId;

          const actorANotifications = await readNotifications(api);
          expect(
            actorANotifications.some((notification) => readOptionalString(notification, 'relatedEntityId') === messageId),
            'sender must not receive a duplicate direct-message notification'
          ).toBe(false);
        });

        await test.step('FUNC-NOTIF-001 / NOTIF-02 actor B receives a generic notification without message body leakage', async () => {
          await logoutViaApi(api);
          await loginViaApi(api, actorBCredentials());

          const notification = await waitForAuthoritativeState(
            async () => {
              const notifications = await readNotifications(api);
              return notifications.find((candidate) =>
                readOptionalString(candidate, 'relatedEntityType') === 'Message' &&
                readOptionalString(candidate, 'relatedEntityId') === messageId
              ) ?? null;
            },
            {
              label: 'recipient direct-message notification',
              isReady: (candidate) => candidate !== null
            }
          );
          if (!notification) {
            throw new Error('Recipient notification was not available after authoritative polling.');
          }
          notificationId = requireStringField(notification, 'id');
          evidence.notificationId = notificationId;
          expect(readOptionalString(notification, 'title')).toBe('New direct message');
          expect(readOptionalString(notification, 'body')).toBe('You have a new message.');
          expect(JSON.stringify(notification)).not.toContain(privateMessageBody);
        });

        await test.step('FUNC-NOTIF-001 / NOTIF-03 read and open state survive a fresh recipient session', async () => {
          const markRead = await csrfAwareRequest(api, 'PATCH', `/api/notifications/${notificationId}/read`);
          await assertSafeResponse(markRead, { label: 'notification mark-read', expectedStatus: 200 });

          const marked = await waitForAuthoritativeState(
            async () => (await readNotifications(api)).find(
              (candidate) => readOptionalString(candidate, 'id') === notificationId
            ) ?? null,
            {
              label: 'notification durable read state',
              isReady: (candidate) => candidate?.isRead === true
            }
          );
          expect(marked?.isRead).toBe(true);

          const open = await csrfAwareRequest(api, 'POST', `/api/notifications/${notificationId}/open`);
          await assertSafeResponse(open, { label: 'recipient notification open', expectedStatus: 200 });
          const opened = asRecord(await open.json(), 'notification open response');
          expect(readOptionalString(opened, 'outcome')).toBe('Opened');
          evidence.notificationOpenOutcome = readOptionalString(opened, 'outcome') ?? undefined;

          await logoutViaApi(api);
          await loginViaApi(api, actorBCredentials());
          const afterReentry = (await readNotifications(api)).find(
            (candidate) => readOptionalString(candidate, 'id') === notificationId
          );
          expect(afterReentry?.isRead).toBe(true);
          evidence.notificationReadPersisted = true;
        });

        await test.step('FUNC-NOTIF-001 / NOTIF-04 another actor cannot open the recipient-owned notification', async () => {
          await logoutViaApi(api);
          await loginViaApi(api, actorACredentials());
          const foreignOpen = await csrfAwareRequest(api, 'POST', `/api/notifications/${notificationId}/open`);
          evidence.foreignNotificationOpenStatus = foreignOpen.status();
          expect(foreignOpen.status(), await safeResponsePreview(foreignOpen)).toBe(404);
          const denialText = await foreignOpen.text();
          expect(denialText).not.toContain(privateMessageBody);
          expect(denialText).not.toContain(recipientEmail);
        });

        if (functionalFullExpansionEnabled()) {
          evidence.fullExpansion = true;
          await test.step('FUNC-NOTIF-001 / NOTIF-FULL removed target fails navigation safely', async () => {
            const remove = await csrfAwareRequest(api, 'DELETE', `/api/messages/${messageId}`);
            await assertSafeResponse(remove, { label: 'notification target delete', expectedStatus: 200 });

            await logoutViaApi(api);
            await loginViaApi(api, actorBCredentials());
            const removedTargetOpen = await csrfAwareRequest(api, 'POST', `/api/notifications/${notificationId}/open`);
            evidence.removedTargetOpenStatus = removedTargetOpen.status();
            expect(removedTargetOpen.status(), await safeResponsePreview(removedTargetOpen)).toBe(404);
            const body = await removedTargetOpen.text();
            expect(body).not.toContain(privateMessageBody);
            expect(body).not.toContain(recipientEmail);
          });
        }
      } finally {
        await testInfo.attach('func-notif-001-evidence.json', {
          body: Buffer.from(JSON.stringify(evidence, null, 2)),
          contentType: 'application/json'
        });
      }
    }
  );
});

function actorACredentials(): SyntheticCredentials {
  return { email: smokeEmail, password: smokePassword };
}

function actorBCredentials(): SyntheticCredentials {
  return { email: recipientEmail, password: `${smokePassword}:recipient` };
}

function restrictedCredentials(): SyntheticCredentials {
  return { email: restrictedEmail, password: smokePassword };
}

async function resolveRecipientUserId(api: APIRequestContext): Promise<string> {
  const response = await api.get(`/api/conversations/recipients?query=${encodeURIComponent(recipientName)}`);
  await assertSafeResponse(response, { label: 'direct-message recipient lookup', expectedStatus: 200 });
  const body = await response.json();
  if (!Array.isArray(body)) {
    throw new Error('Direct-message recipient lookup did not return an array.');
  }
  const recipient = body
    .map((value) => asRecord(value, 'recipient option'))
    .find((value) => readOptionalString(value, 'displayName') === recipientName);
  if (!recipient) {
    throw new Error(`Synthetic collaboration recipient ${recipientName} is unavailable.`);
  }
  return requireStringField(recipient, 'userId');
}

async function createDirectConversation(api: APIRequestContext, recipientUserId: string): Promise<Record<string, unknown>> {
  const response = await csrfAwareRequest(api, 'POST', '/api/conversations/direct', {
    data: { recipientUserId }
  });
  await assertSafeResponse(response, { label: 'direct conversation create/open', expectedStatus: 200 });
  return asRecord(await response.json(), 'direct conversation create/open response');
}

async function readMessages(api: APIRequestContext, conversationId: string): Promise<Record<string, unknown>[]> {
  const response = await api.get(`/api/conversations/${conversationId}/messages?limit=100`);
  await assertSafeResponse(response, { label: 'conversation message list', expectedStatus: 200 });
  return readPagedItems(await response.json(), 'conversation message list');
}

async function readParticipantState(api: APIRequestContext, conversationId: string): Promise<Record<string, unknown>> {
  const response = await api.get(`/api/conversations/${conversationId}/state`);
  await assertSafeResponse(response, { label: 'conversation participant state', expectedStatus: 200 });
  return asRecord(await response.json(), 'conversation participant state');
}

async function readNotifications(api: APIRequestContext): Promise<Record<string, unknown>[]> {
  const response = await api.get('/api/notifications?page=1&pageSize=100');
  await assertSafeResponse(response, { label: 'notification list', expectedStatus: 200 });
  return readPagedItems(await response.json(), 'notification list');
}

function summarizeParticipantState(state: Record<string, unknown>): ParticipantStateSummary {
  return {
    userId: readOptionalString(state, 'userId'),
    lastReadMessageId: readOptionalString(state, 'lastReadMessageId'),
    lastReadAt: readOptionalString(state, 'lastReadAt'),
    unreadCount: readNumber(state, 'unreadCount')
  };
}

function readPagedItems(value: unknown, label: string): Record<string, unknown>[] {
  const response = asRecord(value, label);
  const items = response.items ?? response.Items;
  if (!Array.isArray(items)) {
    throw new Error(`${label} did not contain an items array.`);
  }
  return items.map((item) => asRecord(item, `${label} item`));
}

function asRecord(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} was not a JSON object.`);
  }
  return value as Record<string, unknown>;
}

function requireStringField(record: Record<string, unknown>, ...keys: string[]): string {
  const value = readOptionalString(record, ...keys);
  if (!value) {
    throw new Error(`Required string field is missing: ${keys.join(' / ')}`);
  }
  return value;
}

function readOptionalString(record: Record<string, unknown>, ...keys: string[]): string | null {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }
  return null;
}

function readNumber(record: Record<string, unknown>, key: string): number {
  const value = record[key];
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new Error(`Required numeric field is missing: ${key}`);
  }
  return value;
}

async function boundedResponsePreview(response: APIResponse | PlaywrightResponse): Promise<string> {
  try {
    const text = await response.text();
    return text.length <= 1024 ? text : `${text.slice(0, 1024)}…[TRUNCATED]`;
  } catch {
    return '[response body unavailable]';
  }
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
}
