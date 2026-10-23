import { randomUUID } from 'node:crypto';
import { expect, type Page } from '@playwright/test';
import { test } from '../fixtures/diagnostic-test';
import { functionalMetadata } from '../fixtures/functional-metadata.mjs';
import { loginViaApi } from '../helpers/auth';
import { csrfAwareRequest } from '../helpers/csrf';
import { assertMobileProfile } from './profile.mjs';
import { exerciseNamedWorkspaceChannel } from '../../ui/workspace-channel-directory-real';

// Derived from Main's existing Workspace create/channel/root/reply/reload owner.
// The held Pilot-01 cross-session private-channel lifecycle is not imported.
test('FUNC-MOBILE-001 persists a mobile Workspace channel Message and canonical Thread', functionalMetadata({
  journeyId: 'FUNC-MOBILE-001', gates: ['functional-fast'], domains: ['workspace', 'messaging'],
  priority: 'p0', backend: 'real', polarity: 'positive',
}), async ({ page, browserName }, testInfo) => {
  test.setTimeout(120_000);
  assertMobileProfile(browserName, testInfo.project);
  const api = page.context().request;
  let workspaceId = '', conversationId = '', rootId = '', replyId = '';
  let cleaned = false;
  const rootBody = `Synthetic mobile channel ${randomUUID()}`;
  const replyBody = `Synthetic mobile reply ${randomUUID()}`;
  const archive = async () => {
    if (!workspaceId || cleaned) { return; }
    await page.goto('/app/workspaces');
    const response = await csrfAwareRequest(api, 'POST', `/api/workspaces/${workspaceId}/archive`, { data: {} });
    expect(response.status()).toBe(200);
    await settled(page);
    const list = await api.get('/api/workspaces');
    expect(list.status()).toBe(200);
    expect((await list.json() as { id: string }[]).some((row) => row.id === workspaceId)).toBe(false);
    cleaned = true;
  };
  try {
    await test.step('FUNC-MOBILE-001 / MOB-01 authenticate and create an isolated Workspace through Angular', async () => {
      const actor = await loginViaApi(api, {
        email: process.env.COGLATAS_BROWSER_SMOKE_EMAIL ?? '', password: process.env.COGLATAS_BROWSER_SMOKE_PASSWORD ?? '',
      });
      await page.goto('/app/workspaces');
      await page.getByTestId('create-workspace-action').click();
      const dialog = page.getByRole('dialog', { name: 'Create Workspace', exact: true });
      await expect(dialog).toBeVisible();
      await page.getByTestId('workspace-create-name').fill(`Synthetic mobile ${randomUUID()}`);
      const [created] = await Promise.all([
        page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/workspaces'),
        dialog.getByRole('button', { name: 'Create Workspace', exact: true }).click(),
      ]);
      expect(created.status()).toBe(201);
      const data = (await created.json()).data;
      workspaceId = data.id;
      expect(workspaceId).toMatch(/^[0-9a-f-]{36}$/i);
      expect(data.createdByUserId === actor.userId).toBe(true);
      const headers = await created.request().allHeaders();
      expect(Boolean(headers['x-csrf-token'])).toBe(true);
      expect(headers['idempotency-key']).toMatch(/^[\x20-\x7e]{8,128}$/u);
      await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    });
    await test.step('FUNC-MOBILE-001 / MOB-02 create and reopen the real named private channel', async () => {
      await settled(page);
      const channel = await exerciseNamedWorkspaceChannel(page, workspaceId, () => settled(page));
      conversationId = channel.channelId;
      expect(channel.persisted).toBe(true);
    });
    await test.step('FUNC-MOBILE-001 / MOB-03 persist a Channel Message and canonical Thread reply through Angular', async () => {
      await page.getByTestId('message-draft').fill(rootBody);
      const [sent] = await Promise.all([
        page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === `/api/conversations/${conversationId}/messages`),
        page.getByTestId('send-message').click(),
      ]);
      expect(sent.status()).toBe(200);
      const root = await sent.json();
      rootId = root.id;
      expect(root.conversationId === conversationId && root.body === rootBody).toBe(true);
      expect(Boolean((await sent.request().allHeaders())['x-csrf-token'])).toBe(true);
      await expect(page.getByTestId('confirmed-message').filter({ hasText: rootBody })).toBeVisible();
      await page.getByTestId(`open-message-thread-${rootId}`).click();
      await expect(page.getByTestId('thread-root-message')).toContainText(rootBody);
      await page.getByTestId('thread-reply-draft').fill(replyBody);
      const [replied] = await Promise.all([
        page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === `/api/messages/${rootId}/thread/messages`),
        page.getByTestId('send-thread-reply').click(),
      ]);
      expect(replied.status()).toBe(200);
      const reply = (await replied.json()).message;
      replyId = reply.id;
      expect(replyId).toMatch(/^[0-9a-f-]{36}$/i);
      expect(reply.conversationId === conversationId && reply.threadRootMessageId === rootId && reply.body === replyBody).toBe(true);
      expect(Boolean((await replied.request().allHeaders())['x-csrf-token'])).toBe(true);
      await expect(page.getByTestId('thread-replies')).toContainText(replyBody);
    });
    await test.step('FUNC-MOBILE-001 / MOB-04 reload and reauthorize the same persisted root and reply', async () => {
      // Match the existing follow-up route contract: the reply focus and root
      // together reopen the Thread after a fresh Angular bootstrap.
      await page.goto(`/app/workspaces/${workspaceId}/channels/${conversationId}?focusMessageId=${replyId}&threadRootMessageId=${rootId}`);
      await page.reload();
      await expect(page.getByTestId('thread-root-message')).toContainText(rootBody);
      await expect(page.getByTestId('thread-replies')).toContainText(replyBody);
      const persisted = await api.get(`/api/messages/${rootId}/thread`);
      expect(persisted.status()).toBe(200);
      const projection = await persisted.json();
      expect(projection.rootMessage.id === rootId && projection.rootMessage.body === rootBody).toBe(true);
      expect(projection.replies.some((reply: { id: string; body: string; conversationId: string; threadRootMessageId: string }) =>
        reply.id === replyId && reply.body === replyBody && reply.conversationId === conversationId && reply.threadRootMessageId === rootId)).toBe(true);
    });
    await test.step('FUNC-MOBILE-001 / MOB-05 archive only the run-owned Workspace and verify directory removal', archive);
  } finally { await archive(); }
});

async function settled(page: Page): Promise<void> {
  await expect.poll(async () => {
    const response = await page.context().request.get('/internal/browser-smoke/authorization-outbox');
    return response.status() === 200 && (await response.json()).isSettled === true;
  }, { timeout: 30_000 }).toBe(true);
}
