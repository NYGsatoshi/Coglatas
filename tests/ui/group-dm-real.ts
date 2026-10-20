import { createHash, randomUUID } from 'node:crypto';
import { expect, type Page, type Request, type Response, type WebSocket } from '@playwright/test';

type CommandResult = { status: number; text: string; csrfHeaderPresent: boolean };
type Fixture = {
  baseURL: string;
  authenticate(page: Page, account: 'recipient' | 'third'): Promise<string>;
  command(page: Page, method: 'POST' | 'PATCH' | 'DELETE', path: string, body?: unknown,
    headers?: Readonly<Record<string, string>>): Promise<CommandResult>;
  settleAuthorization(page: Page): Promise<void>;
  archiveWorkspace(page: Page, workspaceId: string): Promise<CommandResult>;
};
type Member = { userId: string; displayName: string; role: number; canRead: boolean;
  canPost: boolean; canCreateThread: boolean; leftAt: string | null; removedAt: string | null };
type OwnState = { participantId: string; userId: string; conversationId: string;
  unreadCount: number; lastReadMessageId: string | null };

/** Real Angular Group commands in an isolated Workspace; no intercepted responses. */
export async function exerciseGroupDm(manager: Page, fixture: Fixture) {
  const origin = new URL(fixture.baseURL).origin;
  expect(new URL(manager.url()).origin).toBe(origin);
  const originalUrl = manager.url();
  const originalWorkspaceId = await manager.getByTestId('workspace-switcher').inputValue();
  expect(originalWorkspaceId).toMatch(/^[0-9a-f-]{36}$/i);
  const browser = manager.context().browser();
  expect(browser).not.toBeNull();
  const recipientContext = await browser!.newContext({ baseURL: fixture.baseURL });
  const thirdContext = await browser!.newContext({ baseURL: fixture.baseURL });
  const recipient = await recipientContext.newPage();
  const third = await thirdContext.newPage();
  const authenticated: Page[] = [manager];
  const pageErrors: string[] = [], consoleErrors: string[] = [];
  const failedResponses: { path: string; method: string; status: number; expectedRemovalDenial: boolean }[] = [];
  let removed = false, workspaceId: string | undefined, groupId: string | undefined;
  const expectedBodies = new Set<string>();
  const managerWire = observeGroupWire(manager, origin, () => groupId, expectedBodies);
  const recipientWire = observeGroupWire(recipient, origin, () => groupId, expectedBodies);
  const commands: string[] = [];
  const thirdReadCommands: string[] = [];
  const thirdReadObserver = (request: Request) => {
    const url = new URL(request.url());
    if (groupId && url.origin === origin && request.method() === 'POST' && url.pathname === `${groupPath(groupId)}/read`) {
      thirdReadCommands.push(url.pathname);
    }
  };
  third.on('request', thirdReadObserver);
  const observeCommand = (actor: string) => (request: Request) => {
    const url = new URL(request.url()), method = request.method();
    if (url.origin === origin && ((method === 'POST' && ['/api/workspaces', '/api/conversations',
      ...(groupId ? [`${groupPath(groupId)}/messages`] : [])].includes(url.pathname)) ||
      (method === 'DELETE' && groupId && url.pathname.startsWith(`${groupPath(groupId)}/members/`)))) {
      commands.push(`${actor} ${method} ${url.pathname}`);
    }
  };
  const managerObserver = observeCommand('manager'), recipientObserver = observeCommand('recipient');
  manager.on('request', managerObserver); recipient.on('request', recipientObserver);
  for (const actor of [recipient, third]) {
    actor.on('pageerror', error => pageErrors.push(error.message));
    actor.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
    actor.on('response', response => {
      const url = new URL(response.url());
      if (url.origin !== origin || !url.pathname.startsWith('/api/') || response.status() < 400) return;
      const expectedRemovalDenial = actor === recipient && removed && response.status() === 400 &&
        response.request().method() === 'GET' && groupId !== undefined &&
        [groupPath(groupId), `${groupPath(groupId)}/messages`, `${groupPath(groupId)}/members`].includes(url.pathname);
      failedResponses.push({ path: url.pathname, method: response.request().method(), status: response.status(), expectedRemovalDenial });
    });
  }
  try {
    const actorId = await currentUserId(manager);
    const recipientId = await fixture.authenticate(recipient, 'recipient'); authenticated.push(recipient);
    const thirdId = await fixture.authenticate(third, 'third'); authenticated.push(third);
    expect(await currentUserId(recipient)).toBe(recipientId);
    expect(await currentUserId(third)).toBe(thirdId);
    expect(new Set([actorId, recipientId, thirdId]).size, 'three distinct existing synthetic accounts').toBe(3);
    const created = await fixture.command(manager, 'POST', '/api/workspaces', {
      name: `Synthetic Group Workspace ${randomUUID()}`, description: 'Isolated real Group browser fixture', icon: null,
    }, { 'Idempotency-Key': randomUUID() });
    expect(created.status, 'one delegated Workspace create command; no uncertain retry').toBe(201);
    expect(created.csrfHeaderPresent).toBe(true);
    workspaceId = JSON.parse(created.text).data.id as string;
    expect(workspaceId).toMatch(/^[0-9a-f-]{36}$/i);
    for (const userId of [recipientId, thirdId]) {
      const admission = await fixture.command(manager, 'POST', `/api/workspaces/${workspaceId}/members`, { userId, role: 3 });
      expect(admission.status, 'existing authorized membership command admits only the run-owned Workspace').toBe(200);
      expect(admission.csrfHeaderPresent).toBe(true);
    }
    const members = await manager.request.get(`/api/workspaces/${workspaceId}/members`);
    expect(members.status()).toBe(200);
    const workspaceMembers = await members.json() as { userId: string; status: number }[];
    expect(workspaceMembers.filter(member => member.status === 1).map(member => member.userId).sort((left, right) => left.localeCompare(right, 'en')))
      .toEqual([actorId, recipientId, thirdId].sort((left, right) => left.localeCompare(right, 'en')));
    // Settle fixture admission before fresh bootstrap; not an immediate-send race test.
    await settleAll(authenticated, fixture);
    await manager.goto('/app/workspaces');
    await expect(manager.getByTestId('workspace-dashboard')).toBeVisible();
    await manager.getByTestId('workspace-switcher').selectOption(workspaceId);
    await expect(manager.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await manager.goto('/app/dm');
    await expect(manager.getByTestId('messages-page')).toBeVisible();
    await manager.getByTestId('new-message-button').click();
    const dialog = manager.getByTestId('new-message-dialog');
    await expect(dialog).toBeVisible();
    const memberRead = responseFor(manager, 'GET', `/api/workspaces/${workspaceId}/members`);
    await manager.getByTestId('group-create-mode').check();
    expect((await memberRead).status()).toBe(200);
    await expect(dialog.getByTestId('group-recipient-option')).toHaveCount(2);
    await dialog.getByTestId('group-title').fill('Synthetic Council Group');
    await dialog.getByTestId('group-recipient-option').filter({ hasText: 'Browser Smoke Recipient' }).click();
    await expect(dialog.getByTestId('create-conversation-submit')).toBeDisabled();
    await dialog.getByTestId('group-recipient-option').filter({ hasText: 'PR05 Browser Manager' }).click();
    const createdGroup = responseFor(manager, 'POST', '/api/conversations');
    await dialog.getByTestId('create-conversation-submit').click();
    const create = await createdGroup;
    await assertAngularCommand(create, { type: 7, workspaceId, memberUserIds: [recipientId, thirdId], title: 'Synthetic Council Group' });
    const group = await create.json() as { id: string; type: string; workspaceId: string; members: Member[] };
    groupId = group.id;
    expect(groupId).toMatch(/^[0-9a-f-]{36}$/i);
    expect(group).toMatchObject({ type: 'Group', workspaceId, projectId: null, parentConversationId: null });
    expect(group.members.map(member => member.userId).sort((left, right) => left.localeCompare(right, 'en')))
      .toEqual([actorId, recipientId, thirdId].sort((left, right) => left.localeCompare(right, 'en')));
    expect(group.members.every(member => member.canRead && member.canPost && !member.canCreateThread)).toBe(true);
    await expect(manager).toHaveURL(new RegExp(`/app/dm/${groupId}$`));
    await expect(manager.getByTestId('group-member-row')).toHaveCount(3);
    await expect(manager.locator('[data-testid^="open-message-thread-"]')).toHaveCount(0);
    // Reading current own state does not open the third actor's Group timeline.
    const unreadBefore = await ownUnreadState(third, groupId, thirdId, 0);
    expect(unreadBefore.lastReadMessageId).toBeNull();
    const managerBody = `Synthetic Group manager message ${randomUUID()}`;
    const recipientBody = `Synthetic Group recipient message ${randomUUID()}`;
    expectedBodies.add(managerBody); expectedBodies.add(recipientBody);
    await recipient.goto('/app/workspaces');
    await expect(recipient.getByTestId('workspace-dashboard')).toBeVisible();
    await recipient.getByTestId('workspace-switcher').selectOption(workspaceId);
    await expect(recipient.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await recipient.goto(`/app/dm/${groupId}`);
    await expect(recipient.getByTestId('group-member-row')).toHaveCount(3);
    for (const [actor, wire] of [[manager, managerWire], [recipient, recipientWire]] as const) {
      await expect(actor.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
      // Observe the real public Hub RPC completion, not client internal state.
      await expect.poll(() => wire.isSubscribed(groupId!)).toBe(true);
    }
    const managerMessage = await send(manager, groupId, managerBody, actorId);
    await expect.poll(() => recipientWire.hasMessage(managerMessage.id, managerBody, actorId)).toBe(true);
    await expect(recipient.getByTestId('confirmed-message').filter({ hasText: managerBody })).toHaveCount(1);
    const recipientMessage = await send(recipient, groupId, recipientBody, recipientId);
    await expect.poll(() => managerWire.hasMessage(recipientMessage.id, recipientBody, recipientId)).toBe(true);
    await expect(manager.getByTestId('confirmed-message').filter({ hasText: recipientBody })).toHaveCount(1);
    const selectedWireEvidence = [
      { receiver: 'recipient', ...recipientWire.receipt(managerMessage.id) },
      { receiver: 'manager', ...managerWire.receipt(recipientMessage.id) },
    ];
    const unreadAfterMessages = await ownUnreadState(third, groupId, thirdId, 2);
    expect(unreadAfterMessages.lastReadMessageId).toBeNull();
    const otherStatesBeforeRead = [
      { actor: manager, state: await ownUnreadState(manager, groupId, actorId, 1) },
      { actor: recipient, state: await ownUnreadState(recipient, groupId, recipientId, 1) },
    ];
    expect(new Set([unreadAfterMessages.participantId, ...otherStatesBeforeRead.map(item => item.state.participantId)]).size).toBe(3);
    // Compare the server's full UTC precision, not millisecond ties or ID order.
    expect(messageUtcTicks(recipientMessage.createdAt)).toBeGreaterThan(messageUtcTicks(managerMessage.createdAt));
    await third.goto('/app/workspaces');
    await expect(third.getByTestId('workspace-dashboard')).toBeVisible();
    await third.getByTestId('workspace-switcher').selectOption(workspaceId);
    await expect(third.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await third.goto('/app/dm');
    await expect(third.getByTestId('messages-page')).toBeVisible();
    const thirdRow = third.locator(`[data-testid="conversation-list-item"][data-conversation-id="${groupId}"]`);
    await expect(thirdRow).toHaveCount(1);
    await expect(thirdRow.getByTestId('conversation-unread-badge')).toHaveText('Unread 2');
    await thirdRow.click();
    await expect(third).toHaveURL(new RegExp(`/app/dm/${groupId}$`));
    for (const body of [managerBody, recipientBody]) {
      await expect(third.getByTestId('confirmed-message').filter({ hasText: body })).toHaveCount(1);
    }
    const unreadOnOpen = await ownUnreadState(third, groupId, thirdId, 2);
    expect(unreadOnOpen.lastReadMessageId).toBeNull();
    expect(thirdReadCommands, 'opening a Group does not substitute for the explicit read command').toEqual([]);
    const markedRead = responseFor(third, 'POST', `${groupPath(groupId)}/read`);
    await third.getByTestId('conversation-mark-read').click();
    await assertAngularCommand(await markedRead, { lastReadMessageId: recipientMessage.id });
    await expect(third.getByTestId('conversation-mark-read-status')).toHaveAttribute('role', 'status');
    await expect(third.getByTestId('conversation-mark-read-status')).toHaveText(
      'Loaded messages marked as read. Refresh the list if its count is unavailable.');
    const unreadAfterRead = await ownUnreadState(third, groupId, thirdId, 0);
    expect(unreadAfterRead.lastReadMessageId).toBe(recipientMessage.id);
    for (const { actor, state: before } of otherStatesBeforeRead) {
      const after = await ownUnreadState(actor, groupId, before.userId, 1);
      expect(after).toMatchObject({ participantId: before.participantId, lastReadMessageId: before.lastReadMessageId });
    }
    await third.goto('/app/dm');
    await expect(third.getByTestId('messages-page')).toBeVisible();
    await expect(thirdRow).toHaveCount(1);
    await expect(thirdRow.getByTestId('conversation-unread-badge')).toHaveCount(0);
    await third.reload();
    await expect(third.getByTestId('messages-page')).toBeVisible();
    await expect(thirdRow).toHaveCount(1);
    await expect(thirdRow.getByTestId('conversation-unread-badge')).toHaveCount(0);
    expect((await ownUnreadState(third, groupId, thirdId, 0)).lastReadMessageId).toBe(recipientMessage.id);
    for (const actor of [manager, recipient]) {
      await actor.reload();
      await expect(actor.getByTestId('confirmed-message').filter({ hasText: managerBody })).toHaveCount(1);
      await expect(actor.getByTestId('confirmed-message').filter({ hasText: recipientBody })).toHaveCount(1);
      const history = await actor.request.get(`${groupPath(groupId)}/messages`);
      expect(history.status()).toBe(200);
      const page = await history.json() as { items: { id: string; body: string; authorUserId: string }[] };
      for (const message of [managerMessage, recipientMessage]) {
        expect(page.items.filter(item => item.id === message.id && item.body === message.body &&
          item.authorUserId === message.authorUserId)).toHaveLength(1);
      }
    }
    const participant = manager.getByTestId('group-member-row').filter({ hasText: 'Browser Smoke Recipient' });
    await participant.getByTestId('group-remove-member').click();
    const confirmation = manager.getByRole('dialog', { name: 'Remove member', exact: true });
    await expect(confirmation).toBeVisible();
    const deletion = responseFor(manager, 'DELETE', `${groupPath(groupId)}/members/${recipientId}`);
    removed = true;
    await confirmation.getByRole('button', { name: 'Remove member', exact: true }).click();
    await assertAngularCommand(await deletion, null);
    await expect(participant).toContainText('Removed');
    const current = await manager.request.get(`${groupPath(groupId)}/members`);
    expect(current.status()).toBe(200);
    const stored = (await current.json() as Member[]).filter(member => member.userId === recipientId);
    expect(stored).toHaveLength(1);
    expect(stored[0].removedAt).toBeTruthy();
    expect(stored[0].leftAt).toBeTruthy();
    for (const path of [groupPath(groupId), `${groupPath(groupId)}/messages`, `${groupPath(groupId)}/members`]) {
      const denial = await recipient.request.get(path);
      expect(denial.status(), 'Group retains its existing generic legacy denial contract').toBe(400);
      const body = await denial.json();
      expect(body).toEqual({ error: 'Conversation not found.' });
      const text = JSON.stringify(body);
      for (const protectedValue of [groupId, workspaceId, actorId, recipientId, thirdId, managerBody, recipientBody]) {
        expect(text).not.toContain(protectedValue);
      }
    }
    // Explicit authoritative refresh, not an unsolicited realtime-clearing oracle.
    await recipient.getByTestId('manual-refresh').click();
    await expect(recipient.getByTestId('message-body')).toHaveCount(0);
    await expect(recipient.getByTestId('group-members-panel')).toHaveCount(0);
    await expect(recipient.getByTestId('message-draft')).toHaveCount(0);
    expect(pageErrors).toEqual([]);
    expect(consoleErrors.filter(message => !/^Failed to load resource: the server responded with a status of 400/.test(message))).toEqual([]);
    expect(failedResponses.every(response => response.expectedRemovalDenial), 'only the precise current-removal GET denials are expected').toBe(true);
    expect(consoleErrors.length, '400 console noise is bounded by observed expected denial responses')
      .toBeLessThanOrEqual(failedResponses.filter(response => response.expectedRemovalDenial).length);
    expect(commands, 'single explicit create/send/remove commands, no automatic replay').toEqual([
      'manager POST /api/workspaces', 'manager POST /api/conversations',
      `manager POST ${groupPath(groupId)}/messages`, `recipient POST ${groupPath(groupId)}/messages`,
      `manager DELETE ${groupPath(groupId)}/members/${recipientId}`,
    ]);
    expect(thirdReadCommands, 'one explicit third-actor read command, no automatic replay').toEqual([`${groupPath(groupId)}/read`]);
    return { distinctUsers: 3, uiGroupCreates: 1, uiMessages: 2, cookieReaders: 3, historyCookieReaders: 2, reloads: 3,
      liveOppositeTimelineReceives: 2, selectedWireEvidence,
      thirdActorUnread: { beforeMessages: 0, afterTwoLiveMessages: 2, afterUiRead: 0, uiReadCommands: 1,
        uiReadResetVerified: true, directoryReloadVerified: true, otherActorCursorAndUnreadIsolationVerified: true },
      historyUniquenessVerified: true, uiRemovals: 1, currentGroupReadDenial: 400,
      explicitRecipientRefreshClearedBody: true, unsolicitedRealtimeClearingVerified: false };
  } finally {
    try {
      if (workspaceId) {
        await manager.goto('/app/workspaces');
        await expect(manager.getByTestId('workspace-dashboard')).toBeVisible();
        const archive = await fixture.archiveWorkspace(manager, workspaceId);
        expect(archive.status).toBe(200);
        expect(archive.csrfHeaderPresent).toBe(true);
        const active = await manager.request.get('/api/workspaces');
        expect(active.status()).toBe(200);
        const activeIds = (await active.json() as { id: string }[]).map(workspace => workspace.id);
        expect(activeIds).not.toContain(workspaceId);
        expect(activeIds).toContain(originalWorkspaceId);
      }
    } finally {
      try {
        // Archive can emit to any still-active fixture member after an earlier failure.
        // Settle each authenticated shared actor only after all cleanup mutations.
        await settleAll(authenticated, fixture);
      } finally {
        try {
          manager.off('request', managerObserver); recipient.off('request', recipientObserver);
          third.off('request', thirdReadObserver);
          managerWire.dispose(); recipientWire.dispose();
          await Promise.all([recipientContext.close(), thirdContext.close()]);
        } finally {
          await manager.goto('/app/workspaces');
          await expect(manager.getByTestId('workspace-dashboard')).toBeVisible();
          await manager.getByTestId('workspace-switcher').selectOption(originalWorkspaceId);
          await expect(manager.getByTestId('workspace-switcher')).toHaveValue(originalWorkspaceId);
          await manager.goto(originalUrl);
          await expect(manager.getByTestId('workspace-switcher')).toHaveValue(originalWorkspaceId);
        }
      }
    }
  }
}

function groupPath(id: string) { return `/api/conversations/${id}`; }

function messageUtcTicks(createdAt: string) {
  const utc = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(createdAt);
  expect(utc, 'actual message timestamp is UTC with at most seven fractional digits').not.toBeNull();
  const seconds = Date.parse(`${utc![1]}Z`) / 1000;
  expect(Number.isSafeInteger(seconds)).toBe(true);
  return BigInt(seconds) * 10000000n + BigInt((utc![2] ?? '').padEnd(7, '0'));
}

async function ownUnreadState(page: Page, conversationId: string, userId: string, unreadCount: number) {
  const response = await page.request.get(`${groupPath(conversationId)}/state`);
  expect(response.status()).toBe(200);
  const state = await response.json() as OwnState;
  expect(state).toMatchObject({ userId, conversationId, unreadCount });
  expect(state.participantId).toMatch(/^[0-9a-f-]{36}$/i);
  return state;
}

function observeGroupWire(page: Page, origin: string, groupId: () => string | undefined, expectedBodies: ReadonlySet<string>) {
  const sockets = new Map<WebSocket, { pending: Map<string, string>; subscribed: Set<string> }>();
  const messages = new Map<string, { eventId: string; messageId: string; senderUserId: string; body: string; bodySha256: string }>();
  let active = true;
  const records = (payload: string | Buffer): Record<string, any>[] => {
    const parsed: Record<string, any>[] = [];
    for (const record of payload.toString().split('\u001e').filter(Boolean)) {
      try {
        const value: unknown = JSON.parse(record);
        if (value && typeof value === 'object' && !Array.isArray(value)) parsed.push(value as Record<string, any>);
      } catch { /* Non-JSON/binary protocol frames are not selected evidence. */ }
    }
    return parsed;
  };
  const observe = (socket: WebSocket) => {
    const url = new URL(socket.url()), expected = new URL(origin);
    if (url.pathname !== '/hubs/app' || url.host !== expected.host ||
      url.protocol !== (expected.protocol === 'https:' ? 'wss:' : 'ws:')) return;
    const state = { pending: new Map<string, string>(), subscribed: new Set<string>() };
    sockets.set(socket, state);
    socket.on('close', () => sockets.delete(socket));
    socket.on('framesent', frame => {
      if (!active) return;
      for (const record of records(frame.payload)) {
        if (record.type === 1 && record.target === 'SubscribeConversation' && typeof record.invocationId === 'string' &&
          Array.isArray(record.arguments) && record.arguments.length === 1 && typeof record.arguments[0] === 'string') {
          state.pending.set(record.invocationId, record.arguments[0]);
        }
      }
    });
    socket.on('framereceived', frame => {
      if (!active) return;
      for (const record of records(frame.payload)) {
        if (record.type === 3 && typeof record.invocationId === 'string') {
          const resourceId = state.pending.get(record.invocationId);
          if (resourceId && !record.error && record.result?.allowed === true && record.result.code === 'Subscribed') {
            state.subscribed.add(resourceId);
          }
          state.pending.delete(record.invocationId);
        }
        if (record.type !== 1 || record.target !== 'DurableEvent' || !Array.isArray(record.arguments)) continue;
        const event = record.arguments[0], message = event?.payload?.message;
        if (event?.eventType !== 'Messaging.MessageCreated.v1' || event.payloadSchemaVersion !== 1 ||
          typeof event.eventId !== 'string' || !/^[0-9a-f-]{36}$/i.test(event.eventId) ||
          !groupId() || message?.conversationId !== groupId() ||
          typeof message.id !== 'string' || typeof message.sender?.userId !== 'string' ||
          typeof message.body !== 'string' || !expectedBodies.has(message.body)) continue;
        // Retain only the two known synthetic bodies in memory, never raw frames,
        // handshake/query tokens, headers, or unrelated event payloads.
        messages.set(message.id, { eventId: event.eventId, messageId: message.id,
          senderUserId: message.sender.userId, body: message.body,
          bodySha256: createHash('sha256').update(message.body).digest('hex') });
      }
    });
  };
  page.on('websocket', observe);
  return {
    isSubscribed: (id: string) => [...sockets].some(([socket, state]) => !socket.isClosed() && state.subscribed.has(id)),
    hasMessage: (id: string, body: string, authorUserId: string) =>
      messages.get(id)?.body === body && messages.get(id)?.senderUserId === authorUserId,
    receipt: (id: string) => {
      const message = messages.get(id); expect(message).toBeDefined();
      return { eventType: 'Messaging.MessageCreated.v1', eventId: message!.eventId,
        messageId: message!.messageId, senderUserId: message!.senderUserId, bodySha256: message!.bodySha256 };
    },
    dispose: () => { active = false; page.off('websocket', observe); sockets.clear(); messages.clear(); },
  };
}

async function settleAll(actors: readonly Page[], fixture: Fixture) {
  const results = await Promise.allSettled(actors.map(actor => fixture.settleAuthorization(actor)));
  const failures = results.filter((result): result is PromiseRejectedResult => result.status === 'rejected');
  if (failures.length) throw new AggregateError(failures.map(result => result.reason), 'Synthetic actor queue handoff failed');
}

async function currentUserId(page: Page) {
  const response = await page.request.get('/api/auth/me');
  expect(response.status()).toBe(200);
  const user = await response.json() as { userId: string; systemRole: number };
  expect(user.userId).toMatch(/^[0-9a-f-]{36}$/i);
  expect(user.systemRole, 'normal synthetic account, no system-administrator bypass').toBe(0);
  return user.userId;
}

function responseFor(page: Page, method: string, path: string) {
  const origin = new URL(page.url()).origin;
  return page.waitForResponse(response => response.request().method() === method &&
    new URL(response.url()).origin === origin && new URL(response.url()).pathname === path);
}

async function assertAngularCommand(response: Response, expectedBody: Record<string, unknown> | null) {
  expect(response.status()).toBe(200);
  expect(response.request().postDataJSON()).toEqual(expectedBody);
  expect(Boolean((await response.request().allHeaders())['x-csrf-token']), 'real Angular CSRF interceptor').toBe(true);
}

async function send(page: Page, conversationId: string, body: string, authorUserId: string) {
  await page.getByTestId('message-draft').fill(body);
  const pending = responseFor(page, 'POST', `${groupPath(conversationId)}/messages`);
  await page.getByTestId('send-message').click();
  const response = await pending;
  expect(response.status()).toBe(200);
  expect(Boolean((await response.request().allHeaders())['x-csrf-token'])).toBe(true);
  expect(response.request().postDataJSON()).toMatchObject({ body, mentionedUserIds: [] });
  expect(response.request().postDataJSON().clientRequestId).toMatch(/^[0-9a-f-]{36}$/i);
  const message = await response.json() as { id: string; body: string; conversationId: string; authorUserId: string; createdAt: string };
  expect(message).toMatchObject({ body, conversationId, authorUserId });
  expect(message.id).toMatch(/^[0-9a-f-]{36}$/i);
  await expect(page.getByTestId('confirmed-message').filter({ hasText: body })).toHaveCount(1);
  return message;
}
