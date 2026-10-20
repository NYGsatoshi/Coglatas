import { createHash, randomUUID } from 'node:crypto';
import { expect, type Page, type Request, type Response, type WebSocket } from '@playwright/test';

type CommandResult = { status: number; text: string; csrfHeaderPresent: boolean };
type Fixture = {
  baseURL: string;
  authenticate(page: Page, account: 'recipient' | 'third' | 'newcomer'): Promise<string>;
  command(page: Page, method: 'POST' | 'PATCH' | 'DELETE', path: string, body?: unknown,
    headers?: Readonly<Record<string, string>>): Promise<CommandResult>;
  settleAuthorization(page: Page): Promise<void>;
  archiveWorkspace(page: Page, workspaceId: string): Promise<CommandResult>;
};
type Member = { userId: string; displayName: string; role: number; canRead: boolean;
  canPost: boolean; canManageMembers: boolean; canCreateThread: boolean; joinedAt: string; leftAt: string | null; removedAt: string | null };
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
  const newcomerContext = await browser!.newContext({ baseURL: fixture.baseURL });
  const recipient = await recipientContext.newPage();
  const third = await thirdContext.newPage();
  const newcomer = await newcomerContext.newPage();
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
    // This settled live-delivery journey requires initial synchronization before creation.
    await expect(manager.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
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
    recipientWire.observeRemovalFor(recipientId);
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
    await expect(recipient.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
    await expect.poll(() => recipientWire.isSubscribed(groupId!)).toBe(true);
    for (const body of [managerBody, recipientBody]) {
      await expect(recipient.getByTestId('confirmed-message').filter({ hasText: body })).toHaveCount(1);
    }
    await expect(recipient.getByTestId('group-members-panel')).toBeVisible();
    await expect(recipient.getByTestId('message-draft')).toBeEnabled();
    expect(recipientWire.removalReceipt()).toBeUndefined();
    const participant = manager.getByTestId('group-member-row').filter({ hasText: 'Browser Smoke Recipient' });
    await participant.getByTestId('group-remove-member').click();
    const confirmation = manager.getByRole('dialog', { name: 'Remove member', exact: true });
    await expect(confirmation).toBeVisible();
    const deletion = responseFor(manager, 'DELETE', `${groupPath(groupId)}/members/${recipientId}`);
    removed = true;
    expect(recipientWire.removalReceipt()).toBeUndefined();
    await confirmation.getByRole('button', { name: 'Remove member', exact: true }).click();
    await assertAngularCommand(await deletion, null);
    await expect(participant).toContainText('Removed');
    // Require this removal's control and protected-state clear before fixture reads or refresh.
    await expect.poll(() => recipientWire.removalReceipt()).toBeDefined();
    const selectedRemovalControl = recipientWire.removalReceipt();
    expect(selectedRemovalControl).toEqual({
      eventType: 'Security.AuthorizationStateChanged.v1', payloadSchemaVersion: 1,
      eventId: expect.stringMatching(/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i),
      affectedUserId: recipientId, scopeType: 'conversation', scopeId: groupId, change: 'member_removed',
    });
    await expect(recipient.getByTestId('message-body')).toHaveCount(0);
    await expect(recipient.getByTestId('group-members-panel')).toHaveCount(0);
    await expect(recipient.getByTestId('message-draft')).toHaveCount(0);
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
    // Retain the explicit authoritative refresh after the unsolicited-clearing assertions.
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
    const lifecycle = await exerciseReentryAndLeave(manager, recipient, third, {
      groupId, workspaceId, actorId, recipientId, thirdId, messages: [managerMessage, recipientMessage],
    });
    const newcomerId = await fixture.authenticate(newcomer, 'newcomer'); authenticated.push(newcomer);
    expect(await currentUserId(newcomer)).toBe(newcomerId);
    expect(new Set([actorId, recipientId, thirdId, newcomerId]).size, 'four distinct ordinary synthetic accounts').toBe(4);
    const firstAddition = await exerciseFirstAddition(manager, newcomer, fixture, {
      groupId, workspaceId, newcomerId, originalParticipantIds: [actorId, recipientId, thirdId],
      messages: [managerMessage, recipientMessage],
    });
    lifecycle.neverParticipantFirstAddVerified = firstAddition.neverParticipantFirstAddVerified;
    const workspaceRevocation = await exerciseWorkspaceRevocation(manager, newcomer, fixture, {
      groupId, workspaceId, newcomerId, messages: [managerMessage, recipientMessage],
    });
    return { distinctUsers: 4, uiGroupCreates: 1, uiMessages: 2, cookieReaders: 4, historyCookieReaders: 3,
      reloads: 3 + lifecycle.additionalReloads + firstAddition.additionalReloads + workspaceRevocation.additionalReloads,
      initialJourneyReloads: 3,
      liveOppositeTimelineReceives: 2, selectedWireEvidence,
      thirdActorUnread: { beforeMessages: 0, afterTwoLiveMessages: 2, afterUiRead: 0, uiReadCommands: 1,
        uiReadResetVerified: true, directoryReloadVerified: true, otherActorCursorAndUnreadIsolationVerified: true },
      historyUniquenessVerified: true, uiRemovals: 1, currentGroupReadDenial: 400,
      explicitRecipientRefreshClearedBody: true, unsolicitedRealtimeClearingVerified: true,
      selectedRemovalControl, lifecycle, firstAddition, workspaceRevocation };
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
          await Promise.all([recipientContext.close(), thirdContext.close(), newcomerContext.close()]);
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

/** The fourth actor is absent from creation and every historical Group member row. */
async function exerciseFirstAddition(manager: Page, newcomer: Page, fixture: Fixture, scenario: {
  groupId: string; workspaceId: string; newcomerId: string; originalParticipantIds: readonly string[];
  messages: readonly { id: string; body: string; authorUserId: string; createdAt: string }[];
}) {
  const { groupId, workspaceId, newcomerId, originalParticipantIds, messages } = scenario;
  const path = groupPath(groupId), origin = new URL(fixture.baseURL).origin;
  const commands: string[] = [], pageErrors: string[] = [], consoleErrors: string[] = [];
  const failures: { method: string; path: string; status: number }[] = [];
  const request = (request: Request) => {
    const url = new URL(request.url());
    if (url.origin === origin && request.method() === 'POST' && url.pathname === path + '/members') {
      commands.push(url.pathname);
    }
  };
  const response = (response: Response) => {
    const url = new URL(response.url());
    if (url.origin === origin && url.pathname.startsWith('/api/') && response.status() >= 400) {
      failures.push({ method: response.request().method(), path: url.pathname, status: response.status() });
    }
  };
  const error = (error: Error) => pageErrors.push(error.message);
  const console = (message: import('@playwright/test').ConsoleMessage) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  };
  newcomer.on('response', response); newcomer.on('pageerror', error); newcomer.on('console', console);
  manager.on('request', request);
  try {
    const admission = await fixture.command(manager, 'POST', `/api/workspaces/${workspaceId}/members`, { userId: newcomerId, role: 3 });
    expect(admission.status).toBe(200);
    expect(admission.csrfHeaderPresent).toBe(true);
    await settleAll([manager, newcomer], fixture);
    const before = await manager.request.get(path + '/members');
    expect(before.status()).toBe(200);
    const historicalMembers = await before.json() as Member[];
    expect(historicalMembers.map(member => member.userId).sort((left, right) => left.localeCompare(right, 'en')))
      .toEqual([...originalParticipantIds].sort((left, right) => left.localeCompare(right, 'en')));
    expect(historicalMembers.some(member => member.userId === newcomerId), 'no current, removed or left row before first addition').toBe(false);
    await inSequence(['', '/messages', '/members'], async suffix => {
      const denied = await newcomer.request.get(path + suffix);
      expect(denied.status()).toBe(400);
      const deniedBody = await denied.json();
      expect(deniedBody).toEqual({ error: 'Conversation not found.' });
      for (const protectedValue of [groupId, workspaceId, ...originalParticipantIds, ...messages.map(message => message.body)]) {
        expect(JSON.stringify(deniedBody)).not.toContain(protectedValue);
      }
    });
    const deniedSelfAdmission = await fixture.command(newcomer, 'POST', path + '/members', { userId: newcomerId });
    expect(deniedSelfAdmission.status).toBe(400);
    expect(deniedSelfAdmission.csrfHeaderPresent).toBe(true);
    expect(JSON.parse(deniedSelfAdmission.text)).toEqual({ error: 'You are not allowed to manage this conversation.' });
    const unchanged = await manager.request.get(path + '/members');
    expect(unchanged.status()).toBe(200);
    expect(await unchanged.json()).toEqual(historicalMembers);

    const memberRead = responseFor(manager, 'GET', `/api/workspaces/${workspaceId}/members`);
    await manager.getByTestId('group-add-member-open').click();
    expect((await memberRead).status()).toBe(200);
    await manager.getByTestId('group-recipient-option').filter({ hasText: 'Browser Smoke Group Newcomer' }).click();
    const addition = responseFor(manager, 'POST', path + '/members');
    await manager.getByTestId('group-add-member-submit').click();
    const added = await addition;
    await assertAngularCommand(added, { userId: newcomerId });
    const expectedMember = { userId: newcomerId, role: 1, canRead: true, canPost: true,
      canManageMembers: false, canCreateThread: false, leftAt: null, removedAt: null };
    const addedMember = await added.json() as Member;
    expect(addedMember).toMatchObject(expectedMember);
    for (const message of messages) {
      expect(messageUtcTicks(addedMember.joinedAt), 'retained messages precede the first admission').toBeGreaterThan(messageUtcTicks(message.createdAt));
    }
    await expect(manager.getByTestId('group-member-row')).toHaveCount(4);
    await settleAll([manager, newcomer], fixture);
    await newcomer.goto('/app/workspaces');
    await expect(newcomer.getByTestId('workspace-dashboard')).toBeVisible();
    await newcomer.getByTestId('workspace-switcher').selectOption(workspaceId);
    await expect(newcomer.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await newcomer.goto(`/app/dm/${groupId}`);
    await inSequence([false, true], async reload => {
      if (reload) await newcomer.reload();
      await expect(newcomer.getByTestId('group-member-row')).toHaveCount(4);
      await expect(newcomer.getByTestId('group-add-member-open')).toHaveCount(0);
      await expect(newcomer.getByTestId('group-remove-member')).toHaveCount(0);
      await expect(newcomer.locator('[data-testid^="open-message-thread-"]')).toHaveCount(0);
      const membership = await newcomer.request.get(path + '/members');
      expect(membership.status()).toBe(200);
      const current = await membership.json() as Member[];
      expect(current).toHaveLength(4);
      expect(current.filter(member => member.userId === newcomerId)).toEqual([expect.objectContaining(expectedMember)]);
      for (const historical of historicalMembers) {
        expect(current.filter(member => member.userId === historical.userId)).toEqual([historical]);
      }
      const history = await newcomer.request.get(path + '/messages');
      expect(history.status()).toBe(200);
      const stored = await history.json() as { items: { id: string; body: string; authorUserId: string }[] };
      await inSequence(messages, async message => {
        expect(stored.items.filter(item => item.id === message.id && item.body === message.body &&
          item.authorUserId === message.authorUserId)).toHaveLength(1);
        await expect(newcomer.getByTestId('confirmed-message').filter({ hasText: message.body })).toHaveCount(1);
      });
    });
    const deniedManagement = await fixture.command(newcomer, 'POST', path + '/members', { userId: originalParticipantIds[2] });
    expect(deniedManagement.status).toBe(400);
    expect(deniedManagement.csrfHeaderPresent).toBe(true);
    expect(JSON.parse(deniedManagement.text)).toEqual({ error: 'You are not allowed to manage this conversation.' });
    await manager.reload();
    await expect(manager.getByTestId('group-member-row')).toHaveCount(4);
    const persisted = await manager.request.get(path + '/members');
    expect(persisted.status()).toBe(200);
    const rows = await persisted.json() as Member[];
    expect(rows).toHaveLength(4);
    expectPersistedFirstAdmission(rows.filter(member => member.userId === newcomerId), addedMember);
    for (const historical of historicalMembers) expect(rows.filter(member => member.userId === historical.userId)).toEqual([historical]);
    expect(commands, 'one explicit UI first addition without command replay').toEqual([path + '/members']);
    expect(pageErrors).toEqual([]);
    expect(failures.every(failure => failure.status === 400 && (
      (failure.method === 'GET' && [path, path + '/messages', path + '/members'].includes(failure.path)) ||
      (failure.method === 'POST' && failure.path === path + '/members')))).toBe(true);
    expect(consoleErrors.filter(message => !message.startsWith('Failed to load resource: the server responded with a status of 400'))).toEqual([]);
    expect(consoleErrors.length).toBeLessThanOrEqual(failures.length);
    return { neverParticipantFirstAddVerified: true, prejoinHistoryReadDenial: 400, selfAdmissionDenial: 400,
      uiFirstAdds: 1, fullPrejoinHistoryVerified: true, newMemberDefaultsVerified: true,
      uniqueMembershipAfterReloadVerified: true, historicalMembershipUnchanged: true,
      newMemberManagementDenial: 400, additionalReloads: 2 };
  } finally {
    manager.off('request', request);
    newcomer.off('response', response); newcomer.off('pageerror', error); newcomer.off('console', console);
  }
}

/** Workspace revocation must clear an already-loaded Group without fixture catch-up. */
async function exerciseWorkspaceRevocation(manager: Page, recipient: Page, fixture: Fixture, scenario: {
  groupId: string; workspaceId: string; newcomerId: string;
  messages: readonly { id: string; body: string }[];
}) {
  const { groupId, workspaceId, newcomerId, messages } = scenario;
  const origin = new URL(fixture.baseURL).origin;
  const path = groupPath(groupId), members = `/api/workspaces/${workspaceId}/members`;
  const wire = observeGroupWire(recipient, origin, () => groupId, new Set(messages.map(message => message.body)));
  wire.observeWorkspaceRevocationFor(newcomerId, workspaceId);
  const commands: string[] = [], pageErrors: string[] = [], consoleErrors: string[] = [];
  const draftSends: string[] = [];
  const failures: { method: string; path: string; status: number }[] = [];
  const request = (request: Request) => {
    const url = new URL(request.url());
    if (url.origin === origin && request.method() === 'DELETE' && url.pathname === `${members}/${newcomerId}`)
      commands.push(`${request.method()} ${url.pathname}`);
  };
  const observeDraftSend = (request: Request) => {
    const url = new URL(request.url());
    if (url.origin === origin && request.method() === 'POST' && url.pathname === path + '/messages')
      draftSends.push(url.pathname);
  };
  const response = (response: Response) => {
    const url = new URL(response.url());
    if (url.origin === origin && url.pathname.startsWith('/api/') && response.status() >= 400)
      failures.push({ method: response.request().method(), path: url.pathname, status: response.status() });
  };
  const error = (error: Error) => pageErrors.push(error.message);
  const console = (message: import('@playwright/test').ConsoleMessage) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  };
  manager.on('request', request); recipient.on('response', response);
  recipient.on('request', observeDraftSend);
  recipient.on('pageerror', error); recipient.on('console', console);
  try {
    const selfResponse = await recipient.request.get('/api/auth/me');
    expect(selfResponse.status()).toBe(200);
    expect(await selfResponse.json()).toMatchObject({ userId: newcomerId, systemRole: 0 });
    expect((await recipient.request.get(members)).status()).toBe(200);
    await recipient.reload();
    await expect(recipient.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await expect(recipient.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
    await expect.poll(() => wire.isSubscribed(groupId)).toBe(true);
    await inSequence(messages, async message => {
      await expect(recipient.getByTestId('confirmed-message').filter({ hasText: message.body })).toHaveCount(1);
    });
    await expect(recipient.getByTestId('group-members-panel')).toBeVisible();
    await recipient.getByTestId('message-draft').fill('Synthetic unsent Workspace revocation draft');
    expect(wire.removalReceipt()).toBeUndefined();

    await manager.goto(`/app/workspaces/${workspaceId}/members`);
    const row = manager.getByTestId('workspace-members-page').getByRole('row').filter({
      has: manager.getByText('Browser Smoke Group Newcomer', { exact: true }),
    });
    await expect(row).toHaveCount(1);
    const detail = responseFor(manager, 'GET', `${members}/${newcomerId}`);
    await row.locator('[data-grid-action="openMemberDetail"]').click();
    expect((await detail).status()).toBe(200);
    await expect(manager.getByTestId('workspace-member-detail-name')).toHaveText('Browser Smoke Group Newcomer');
    await manager.getByTestId('workspace-member-remove').click();
    const confirmation = manager.getByRole('dialog', { name: 'Remove access', exact: true });
    await expect(confirmation.getByRole('button', { name: 'Remove access', exact: true })).toBeDisabled();
    await manager.getByTestId('workspace-member-confirm-identity').check();
    const removal = responseFor(manager, 'DELETE', `${members}/${newcomerId}`);
    expect(new URL(recipient.url()).pathname).toBe(`/app/dm/${groupId}`);
    await expect(recipient.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
    await expect(recipient.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
    await expect.poll(() => wire.isSubscribed(groupId)).toBe(true);
    await inSequence(messages, async message => {
      await expect(recipient.getByTestId('confirmed-message').filter({ hasText: message.body })).toHaveCount(1);
    });
    await expect(recipient.getByTestId('group-members-panel')).toBeVisible();
    await expect(recipient.getByTestId('message-draft')).toHaveValue('Synthetic unsent Workspace revocation draft');
    expect(wire.removalReceipt()).toBeUndefined();
    await confirmation.getByRole('button', { name: 'Remove access', exact: true }).click();
    await assertAngularCommand(await removal, null);
    // No helper request, refresh, navigation or Outbox barrier before these assertions.
    await expect.poll(() => wire.removalReceipt()).toBeDefined();
    expect(wire.removalReceipt()).toEqual({
      eventType: 'Security.AuthorizationStateChanged.v1', payloadSchemaVersion: 1,
      eventId: expect.stringMatching(/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i),
      affectedUserId: newcomerId, scopeType: 'workspace', scopeId: workspaceId, change: 'revoked',
    });
    await expect(recipient.getByTestId('message-body')).toHaveCount(0);
    await expect(recipient.getByTestId('group-members-panel')).toHaveCount(0);
    await expect(recipient.getByTestId('message-draft')).toHaveCount(0);
    const stillAuthenticated = await recipient.request.get('/api/auth/me');
    expect(stillAuthenticated.status(), 'Workspace revocation does not log out the Tenant session').toBe(200);
    expect(await stillAuthenticated.json()).toMatchObject({ userId: newcomerId, systemRole: 0 });
    const remaining = await manager.request.get(members);
    expect(remaining.status()).toBe(200);
    expect((await remaining.json() as { userId: string }[]).some(member => member.userId === newcomerId)).toBe(false);
    expect((await recipient.request.get(members)).status()).toBe(404);
    await inSequence(['', '/messages', '/members'], async suffix => {
      const denial = await recipient.request.get(path + suffix);
      expect(denial.status()).toBe(400);
      const body = await denial.json();
      expect(body).toEqual({ error: 'Conversation not found.' });
      for (const protectedValue of [groupId, workspaceId, newcomerId, ...messages.map(message => message.body)])
        expect(JSON.stringify(body)).not.toContain(protectedValue);
    });
    const retained = await manager.request.get(path + '/messages');
    expect(retained.status(), 'remaining authorized participant retains the original history').toBe(200);
    const retainedPage = await retained.json() as { items: { id: string; body: string }[] };
    for (const message of messages)
      expect(retainedPage.items.filter(item => item.id === message.id && item.body === message.body)).toHaveLength(1);
    expect(draftSends, 'revoking the Workspace never submits the unsent draft').toEqual([]);
    expect(commands, 'one explicit Angular Workspace removal without replay').toEqual([`DELETE ${members}/${newcomerId}`]);
    expect(pageErrors).toEqual([]);
    expect(failures.every(failure => failure.method === 'GET' && [400, 403, 404].includes(failure.status) &&
      ([path, path + '/messages', path + '/members', '/api/conversations'].includes(failure.path) ||
        failure.path.startsWith(`/api/workspaces/${workspaceId}/`)))).toBe(true);
    expect(consoleErrors.filter(message => !/^Failed to load resource: the server responded with a status of (400|403|404)/.test(message))).toEqual([]);
    expect(consoleErrors.length).toBeLessThanOrEqual(failures.length);
    return { uiWorkspaceRemovals: 1, additionalReloads: 1, currentWorkspaceControlVerified: true,
      unsolicitedGroupClearingVerified: true, unsentDraftNotSent: true, sessionRemainsAuthenticated: true,
      retainedAuthorizedHistoryVerified: true, removedDirectoryStatus: 404, removedGroupReadStatus: 400 };
  } finally {
    wire.dispose(); manager.off('request', request); recipient.off('response', response);
    recipient.off('request', observeDraftSend);
    recipient.off('pageerror', error); recipient.off('console', console);
  }
}

/** Extends the original three-actor oracles; re-entry is not first-time admission. */
async function exerciseReentryAndLeave(manager: Page, recipient: Page, third: Page, fixture: {
  groupId: string; workspaceId: string; actorId: string; recipientId: string; thirdId: string;
  messages: readonly { id: string; body: string; authorUserId: string }[];
}) {
  const { groupId, workspaceId, actorId, recipientId, thirdId, messages } = fixture;
  const path = groupPath(groupId), origin = new URL(manager.url()).origin;
  const commands: string[] = [], pageErrors: string[] = [], consoleErrors: string[] = [];
  const failures: { actor: string; method: string; path: string; status: number }[] = [];
  const observers = [manager, recipient, third].map((page, index) => {
    const actor = ['manager', 'recipient', 'third'][index];
    const request = (request: Request) => {
      const url = new URL(request.url());
      if (url.origin === origin && request.method() === 'POST' &&
          [path + '/members', path + '/leave'].includes(url.pathname)) commands.push(`${actor} ${url.pathname}`);
    };
    const response = (response: Response) => {
      const url = new URL(response.url());
      if (url.origin === origin && url.pathname.startsWith('/api/') && response.status() >= 400) {
        failures.push({ actor, method: response.request().method(), path: url.pathname, status: response.status() });
      }
    };
    const error = (error: Error) => pageErrors.push(error.message);
    const console = (message: import('@playwright/test').ConsoleMessage) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    };
    page.on('request', request); page.on('response', response); page.on('pageerror', error); page.on('console', console);
    return () => {
      page.off('request', request); page.off('response', response); page.off('pageerror', error); page.off('console', console);
    };
  });
  try {
    const participant = manager.getByTestId('group-member-row').filter({ hasText: 'Browser Smoke Recipient' });
    await expect(participant).toContainText('Removed');
    const memberRead = responseFor(manager, 'GET', `/api/workspaces/${workspaceId}/members`);
    await manager.getByTestId('group-add-member-open').click();
    expect((await memberRead).status()).toBe(200);
    await manager.getByTestId('group-recipient-option').filter({ hasText: 'Browser Smoke Recipient' }).click();
    const addition = responseFor(manager, 'POST', path + '/members');
    await manager.getByTestId('group-add-member-submit').click();
    const added = await addition;
    await assertAngularCommand(added, { userId: recipientId });
    const expectedMember = { userId: recipientId, role: 1, canRead: true, canPost: true,
      canManageMembers: false, canCreateThread: false, leftAt: null, removedAt: null };
    expect(await added.json()).toMatchObject(expectedMember);
    await expect(participant).toContainText('Active');
    await expect(participant).toContainText('Member');
    await expect(manager.getByTestId('group-member-row')).toHaveCount(3);
    const membership = await manager.request.get(path + '/members');
    expect(membership.status()).toBe(200);
    const currentMembers = await membership.json() as Member[];
    expect(currentMembers.filter(member => member.userId === recipientId)).toEqual([expect.objectContaining(expectedMember)]);
    expect(currentMembers).toHaveLength(3);
    await inSequence([recipient, third], async page => {
      await page.goto(`/app/dm/${groupId}`);
      await expect(page.getByTestId('group-member-row')).toHaveCount(3);
      const history = await page.request.get(path + '/messages');
      expect(history.status()).toBe(200);
      const stored = await history.json() as { items: { id: string; body: string; authorUserId: string }[] };
      await inSequence(messages, async message => {
        expect(stored.items.filter(item => item.id === message.id && item.body === message.body &&
          item.authorUserId === message.authorUserId)).toHaveLength(1);
        await expect(page.getByTestId('confirmed-message').filter({ hasText: message.body })).toHaveCount(1);
      });
    });
    await recipient.reload();
    await inSequence(messages, async message => {
      await expect(recipient.getByTestId('confirmed-message').filter({ hasText: message.body })).toHaveCount(1);
    });
    await third.getByTestId('group-leave').click();
    const confirmation = third.getByRole('dialog', { name: 'Leave group', exact: true });
    await expect(confirmation).toBeVisible();
    const leaving = responseFor(third, 'POST', path + '/leave');
    await confirmation.getByRole('button', { name: 'Leave group', exact: true }).click();
    await assertAngularCommand(await leaving, {});
    await expect(third).toHaveURL(/\/app\/dm$/);
    await inSequence(['message-body', 'message-draft', 'group-members-panel'], async testId => {
      await expect(third.getByTestId(testId)).toHaveCount(0);
    });
    await expect(third.locator(`[data-testid="conversation-list-item"][data-conversation-id="${groupId}"]`)).toHaveCount(0);
    await inSequence(['', '/messages', '/members'], async suffix => {
      const denied = await third.request.get(path + suffix);
      expect(denied.status()).toBe(400);
      expect(await denied.json()).toEqual({ error: 'Conversation not found.' });
    });
    const retained = await manager.request.get(path + '/members');
    expect(retained.status()).toBe(200);
    const rows = await retained.json() as Member[];
    expect(rows.filter(member => member.userId === thirdId)).toEqual([expect.objectContaining({ removedAt: null, leftAt: expect.any(String) })]);
    expect(rows.filter(member => member.userId === actorId)).toEqual([expect.objectContaining({ role: 0, canRead: true, canManageMembers: true, leftAt: null, removedAt: null })]);
    expect(rows.filter(member => member.userId === recipientId)).toEqual([expect.objectContaining(expectedMember)]);
    expect(rows).toHaveLength(3);
    await manager.reload();
    await expect(manager.getByTestId('group-member-row').filter({ hasText: 'PR05 Browser Manager' })).toContainText('Left');
    await inSequence(messages, async message => {
      await expect(manager.getByTestId('confirmed-message').filter({ hasText: message.body })).toHaveCount(1);
    });
    expect(commands, 'one explicit re-entry and self-leave; no replay').toEqual([
      `manager ${path}/members`, `third ${path}/leave`,
    ]);
    expect(pageErrors).toEqual([]);
    expect(failures.every(failure => failure.actor === 'third' && failure.method === 'GET' &&
      [path, path + '/messages', path + '/members'].includes(failure.path) && failure.status === 400)).toBe(true);
    expect(consoleErrors.filter(message => !message.startsWith('Failed to load resource: the server responded with a status of 400'))).toEqual([]);
    expect(consoleErrors.length).toBeLessThanOrEqual(failures.length);
    return { uiAddEndpointReentries: 1, currentMemberRowUnique: true, reentryDefaultsVerified: true,
      fullHistoryAfterReentryVerified: true, reentryReloadVerified: true, additionalReloads: 2, uiSelfLeaves: 1,
      currentSelfLeaveReadDenial: 400, leaveClearedBodyAndDirectory: true, creatorStillAuthorized: true,
      neverParticipantFirstAddVerified: false, soleAdminLeaveVerified: false };
  } finally {
    for (const dispose of observers) dispose();
  }
}

function groupPath(id: string) { return `/api/conversations/${id}`; }

function inSequence<T>(values: readonly T[], check: (value: T) => Promise<void>): Promise<void> {
  return values.reduce((previous, value) => previous.then(() => check(value)), Promise.resolve());
}

function messageUtcTicks(createdAt: string) {
  const utc = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(createdAt);
  expect(utc, 'actual message timestamp is UTC with at most seven fractional digits').not.toBeNull();
  expect(utc![0], 'UTC timestamp has no trailing data').toBe(createdAt);
  const seconds = Date.parse(`${utc![1]}Z`) / 1000;
  expect(Number.isSafeInteger(seconds)).toBe(true);
  expect(Number(utc![1].slice(0, 4)), 'UTC year is in the .NET timestamp range').toBeGreaterThanOrEqual(1);
  expect(new Date(seconds * 1000).toISOString().slice(0, 19), 'UTC calendar date and time are valid').toBe(utc![1]);
  return BigInt(seconds) * 10000000n + BigInt((utc![2] ?? '').padEnd(7, '0'));
}

function expectPersistedFirstAdmission(rows: readonly Member[], addedMember: Member) {
  expect(rows, 'one persisted row for the first admission').toHaveLength(1);
  const persisted = rows[0];
  expect(persisted).toEqual({ ...addedMember, joinedAt: persisted.joinedAt });
  const persistedTicks = messageUtcTicks(persisted.joinedAt);
  expect(persistedTicks % 10n, 'persisted JoinedAt is microsecond-aligned').toBe(0n);
  const postgresEpochTicks = messageUtcTicks('2000-01-01T00:00:00Z');
  const addedTicks = messageUtcTicks(addedMember.joinedAt);
  // Npgsql binary timestamps drop sub-microsecond ticks relative to the PostgreSQL epoch.
  const encodedMicroseconds = (addedTicks - postgresEpochTicks) / 10n;
  expect(persistedTicks).toBe(encodedMicroseconds * 10n + postgresEpochTicks);
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
  let latestSocket: WebSocket | undefined;
  let removalUserId: string | undefined;
  let workspaceRevocationId: string | undefined;
  let removalControl: { eventType: string; payloadSchemaVersion: number; eventId: string;
    affectedUserId: string; scopeType: string; scopeId: string; change: string } | undefined;
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
    latestSocket = socket;
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
        if (!removalControl && record.arguments.length === 1 && socket === latestSocket &&
          !socket.isClosed() && sockets.has(socket) && removalUserId && groupId() &&
          event?.eventType === 'Security.AuthorizationStateChanged.v1' && event.payloadSchemaVersion === 1 &&
          typeof event.eventId === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(event.eventId) &&
          event.aggregateType === 'AuthorizationState' && event.aggregateId === removalUserId &&
          event.payload?.affectedUserId === removalUserId &&
          event.payload.scopeType === (workspaceRevocationId ? 'workspace' : 'conversation') &&
          event.payload.scopeId === (workspaceRevocationId ?? groupId()) &&
          event.payload.change === (workspaceRevocationId ? 'revoked' : 'member_removed')) {
          // Retain one selected synthetic control, never its raw frame or unrelated payload fields.
          removalControl = { eventType: event.eventType, payloadSchemaVersion: event.payloadSchemaVersion,
            eventId: event.eventId, affectedUserId: removalUserId,
            scopeType: workspaceRevocationId ? 'workspace' : 'conversation',
            scopeId: workspaceRevocationId ?? groupId()!, change: workspaceRevocationId ? 'revoked' : 'member_removed' };
        }
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
    observeRemovalFor: (userId: string) => { removalUserId = userId; },
    observeWorkspaceRevocationFor: (userId: string, workspaceId: string) => {
      removalUserId = userId; workspaceRevocationId = workspaceId;
    },
    removalReceipt: () => removalControl ? { ...removalControl } : undefined,
    isSubscribed: (id: string) => latestSocket !== undefined && !latestSocket.isClosed() &&
      sockets.get(latestSocket)?.subscribed.has(id) === true,
    hasMessage: (id: string, body: string, authorUserId: string) =>
      messages.get(id)?.body === body && messages.get(id)?.senderUserId === authorUserId,
    receipt: (id: string) => {
      const message = messages.get(id); expect(message).toBeDefined();
      return { eventType: 'Messaging.MessageCreated.v1', eventId: message!.eventId,
        messageId: message!.messageId, senderUserId: message!.senderUserId, bodySha256: message!.bodySha256 };
    },
    dispose: () => {
      active = false; page.off('websocket', observe); sockets.clear(); messages.clear();
      removalControl = undefined; removalUserId = undefined; workspaceRevocationId = undefined; latestSocket = undefined;
    },
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
