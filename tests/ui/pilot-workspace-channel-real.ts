import { randomUUID } from 'node:crypto';
import { expect, type APIResponse, type Page } from '@playwright/test';
import type { WorkspaceMemberJourneyHooks } from './workspace-member-management-real';

export type PilotPrivateHistory = {
  readonly channelId: string;
  readonly rootId: string;
  readonly body: string;
  readonly replyBody: string;
};

type Stage = 'before-admission' | 'after-admission' | 'readonly' | 'restored' | 'removed';

/** Requires an exact HTTP status for a live, independently authenticated API request. */
async function requireStatus(response: APIResponse, status: number, action: string): Promise<void> {
  expect(response.status(), action).toBe(status);
}

/** Rejects server errors and checks that an unauthorized response leaks no seeded data. */
async function assertMasked(response: APIResponse, label: string, secrets: readonly string[]): Promise<void> {
  // Refusal must be an authorization/validation outcome, never a server crash,
  // redirect or merely a response that happened not to be HTTP 200.
  expect([400, 401, 403, 404], label + ' requires a bounded access rejection')
    .toContain(response.status());
  const payload = await response.text();
  for (const secret of secrets) {
    expect(payload, label + ' must not disclose protected synthetic content').not.toContain(secret);
  }
}

/** Asserts whether the private channel is discoverable in an authorized Workspace view. */
async function readPrivateDirectory(page: Page, endpoint: string, channelId: string, expected: boolean): Promise<void> {
  const response = await page.request.get(endpoint);
  await requireStatus(response, 200, 'scoped channel discovery after Workspace admission');
  const payload = await response.json() as { readonly items: readonly { readonly id: string }[] };
  expect(Array.isArray(payload.items)).toBe(true);
  expect(payload.items.some(row => row.id === channelId), 'private channel visibility follows membership').toBe(expected);
}

/** Sends a CSRF-authenticated Message command using the recipient session's cookies. */
async function messageCommand(page: Page, path: string, body: string): Promise<APIResponse> {
  // BrowserContext.request shares the independently authenticated browser's
  // cookie jar; unlike page.route, this uses the live application and database.
  const csrfResponse = await page.request.get('/api/security/csrf-token');
  await requireStatus(csrfResponse, 200, 'read real CSRF token for an authorized synthetic command');
  const csrf = await csrfResponse.json() as { readonly headerName: string; readonly token: string };
  expect(csrf.headerName).toBe('X-CSRF-Token');
  expect(csrf.token).toBeTruthy();
  return page.request.post(path, {
    data: { body, clientRequestId: randomUUID() },
    headers: { [csrf.headerName]: csrf.token }
  });
}

/** Creates and confirms a private root Message and Thread reply via the real Angular UI. */
export async function preparePilotPrivateChannelHistory(manager: Page, channelId: string): Promise<PilotPrivateHistory> {
  const messages = '/api/conversations/' + channelId + '/messages';
  const body = 'Synthetic council private history ' + randomUUID();
  await expect(manager.getByTestId('channel-messaging-page')).toBeVisible();
  await manager.getByTestId('message-draft').fill(body);
  const [sent] = await Promise.all([
    manager.waitForResponse(response => new URL(response.url()).pathname === messages &&
      response.request().method() === 'POST'),
    manager.getByTestId('send-message').click()
  ]);
  expect(sent.status(), 'private channel root commit').toBe(200);
  expect((await sent.request().allHeaders())['x-csrf-token']).toBeTruthy();
  const root = await sent.json() as { readonly id: string; readonly body: string };
  expect(root.id).toMatch(/^[0-9a-f-]{36}$/iu);
  expect(root.body).toBe(body);
  await expect(manager.getByTestId('confirmed-message').filter({ hasText: body })).toBeVisible();

  await manager.getByTestId('open-message-thread-' + root.id).click();
  const replyBody = 'Synthetic council private reply ' + randomUUID();
  await expect(manager.getByTestId('thread-root-message')).toContainText(body);
  await manager.getByTestId('thread-reply-draft').fill(replyBody);
  const [reply] = await Promise.all([
    manager.waitForResponse(response => new URL(response.url()).pathname ===
      '/api/messages/' + root.id + '/thread/messages' && response.request().method() === 'POST'),
    manager.getByTestId('send-thread-reply').click()
  ]);
  expect(reply.status(), 'private channel thread reply commit').toBe(200);
  expect((await reply.request().allHeaders())['x-csrf-token']).toBeTruthy();
  await expect(manager.getByTestId('thread-replies')).toContainText(replyBody);
  return { channelId, rootId: root.id, body, replyBody };
}

/** Builds five authorization checkpoints spanning one Workspace's membership lifecycle. */
export function createPilotWorkspaceChannelJourney(
  workspaceId: string, privateHistory: PilotPrivateHistory, recipientName: string
): { readonly hooks: WorkspaceMemberJourneyHooks; readonly evidence: () => Record<string, number | boolean> } {
  const stages: Stage[] = [];
  const channelRoot = '/api/workspaces/' + workspaceId + '/chat-channels';
  const history = '/api/conversations/' + privateHistory.channelId + '/messages';
  const thread = '/api/messages/' + privateHistory.rootId + '/thread';
  const files = '/api/files?workspaceId=' + encodeURIComponent(workspaceId) + '&page=1&pageSize=20';
  const protectedContents = [
    privateHistory.body, privateHistory.replyBody, privateHistory.channelId,
    privateHistory.rootId, workspaceId
  ];

  const hooks: WorkspaceMemberJourneyHooks = {
    beforeAdmission: async (_manager, recipient) => {
      await assertMasked(await recipient.request.get(channelRoot), 'nonmember directory denied', protectedContents);
      await assertMasked(await recipient.request.get(history), 'nonmember private history denied', protectedContents);
      await assertMasked(await recipient.request.get(thread), 'nonmember private thread denied', protectedContents);
      await assertMasked(await recipient.request.get(files), 'nonmember scoped Files denied', protectedContents);
      stages.push('before-admission');
    },
    afterAdmission: async (manager, recipient, userId) => {
      // Workspace access is not automatically a grant to private conversation history.
      await readPrivateDirectory(recipient, channelRoot, privateHistory.channelId, false);
      await assertMasked(await recipient.request.get(history), 'uninvited private history denied', protectedContents);
      await assertMasked(await recipient.request.get(thread), 'uninvited private thread denied', protectedContents);
      await requireStatus(await recipient.request.get(files), 200, 'new member can read Workspace Files metadata');

      await manager.goto('/app/teams');
      const directory = manager.getByTestId('workspace-channel-directory');
      await expect(directory).toBeVisible();
      await expect(manager.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
      const row = directory.locator('[data-channel-id="' + privateHistory.channelId + '"]');
      await expect(row).toBeVisible();
      await row.getByTestId('channel-invite-open').click();
      const dialog = manager.getByTestId('channel-invite-dialog');
      await expect(dialog).toBeVisible();
      await expect(dialog).toHaveAttribute('role', 'dialog');
      const candidate = dialog.getByTestId('group-recipient-option').filter({ hasText: recipientName });
      await expect(candidate).toBeVisible();
      await candidate.click();
      const submit = dialog.getByTestId('channel-invite-submit');
      await expect(submit).toBeEnabled();
      const endpoint = channelRoot + '/' + privateHistory.channelId + '/members';
      const [response] = await Promise.all([
        manager.waitForResponse(r => new URL(r.url()).pathname === endpoint && r.request().method() === 'POST'),
        submit.click()
      ]);
      expect(response.status(), 'scoped UI invite completed').toBe(200);
      expect(response.request().postDataJSON()).toEqual({ userId });
      expect((await response.request().allHeaders())['x-csrf-token']).toBeTruthy();
      await expect(dialog).toContainText('The server confirmed membership.');
      await dialog.getByRole('button', { name: 'Close' }).click();

      await readPrivateDirectory(recipient, channelRoot, privateHistory.channelId, true);
      const visible = await recipient.request.get(history);
      await requireStatus(visible, 200, 'invited member reads durable private message');
      expect(await visible.text()).toContain(privateHistory.body);
      const reply = await recipient.request.get(thread);
      await requireStatus(reply, 200, 'invited member reads canonical thread');
      expect(await reply.text()).toContain(privateHistory.replyBody);
      await manager.goto('/app/workspaces/' + workspaceId + '/members');
      await expect(manager.getByTestId('workspace-members-page')).toBeVisible();
      stages.push('after-admission');
    },
    afterReadOnly: async (_manager, recipient) => {
      const visible = await recipient.request.get(history);
      await requireStatus(visible, 200, 'ReadOnly member retains private history read');
      expect(await visible.text()).toContain(privateHistory.body);
      await requireStatus(await recipient.request.get(files), 200, 'ReadOnly member retains Files list read');
      const denied = await messageCommand(recipient, history, 'Synthetic ReadOnly post denied ' + randomUUID());
      await assertMasked(denied, 'current ReadOnly membership rejects post despite retained participant flags', protectedContents);
      stages.push('readonly');
    },
    afterRestored: async (_manager, recipient) => {
      const resumed = await messageCommand(recipient, history, 'Synthetic restored member post ' + randomUUID());
      await requireStatus(resumed, 200, 'Workspace role restoration permits current authorized write');
      const result = await resumed.json() as { readonly id: string };
      expect(result.id).toMatch(/^[0-9a-f-]{36}$/iu);
      stages.push('restored');
    },
    afterRemoval: async (_manager, recipient) => {
      await assertMasked(await recipient.request.get(channelRoot), 'removed member cannot discover channel metadata', protectedContents);
      await assertMasked(await recipient.request.get(history), 'removed member cannot read private history', protectedContents);
      await assertMasked(await recipient.request.get(thread), 'removed member cannot read previous thread', protectedContents);
      await assertMasked(await recipient.request.get(files), 'removed member cannot browse the old Workspace Files', protectedContents);
      const removedPost = await messageCommand(recipient, history, 'Synthetic removed-member post denied ' + randomUUID());
      await assertMasked(removedPost, 'removed member cannot post after Workspace removal', protectedContents);
      stages.push('removed');
    }
  };

  return {
    hooks,
    evidence: () => {
      expect(stages, 'all five cross-session authorization phases executed in order')
        .toEqual(['before-admission', 'after-admission', 'readonly', 'restored', 'removed']);
      // No message contents, session credentials, CSRF tokens, IDs, signed URLs
      // or recipient identity enter the persisted CI evidence object.
      return {
        independentSessions: 2,
        phases: stages.length,
        privateHistoryCreated: true,
        privateThreadCreated: true,
        positiveAdmissionAndReload: true,
        uninvitedPrivateThreadDenied: true,
        readonlyWriteDenied: true,
        restoredWritePermitted: true,
        removedMemberWriteDenied: true,
        scopedFilesDeniedAfterRemoval: true
      };
    }
  };
}
