import { randomUUID } from 'node:crypto';
import { expect, type Page, type Response } from '@playwright/test';
import { isUuid } from './public-https-golden-path-helpers.mjs';
import { isGroupContractDenial, pilotMessageUtcTicks, validateGroupCheckpoint, type PilotConfiguration } from './student-council-pilot-helpers.mjs';

type Result = { status: number; text: string };
type Access = {
  configuration: PilotConfiguration;
  api(page: Page, path: string, method?: string, body?: unknown): Promise<Result>;
  navigate(page: Page, path: string): Promise<unknown>;
  send(page: Page, id: string, kind: 'dm', body: string): Promise<Record<string, any>>;
};
type Member = { userId: string; displayName: string; status: number };

// Reuses only existing public routes and Angular controls. It does not provision
// accounts or Workspaces, wait for Outbox, intercept responses, or grant access.
export async function selectPilotWorkspace(page: Page, access: Access) {
  await access.navigate(page, '/app/workspaces');
  await expect(page.getByTestId('workspace-dashboard')).toBeVisible();
  await page.getByTestId('workspace-switcher').selectOption(access.configuration.fixture.allowed.workspaceId);
  await expect(page.getByTestId('workspace-switcher')).toHaveValue(access.configuration.fixture.allowed.workspaceId);
}

export async function assertGroupFixtures(manager: Page, receiver: Page, observer: Page, access: Access) {
  const { fixture } = access.configuration;
  const expected = [fixture.memberUserId, fixture.operatorUserId, fixture.revocation.userId];
  // Discovery is not admission authority. The subsequent server create/add
  // commands must independently accept these exact persisted memberships.
  for (const actor of [manager, receiver, observer]) {
    const result = await access.api(actor, `/api/workspaces/${fixture.allowed.workspaceId}/members`);
    assert(result.status === 200, 'ordinary Workspace member discovery');
    const members = parse(result) as Member[];
    assert(Array.isArray(members), 'ordinary Workspace member contract');
    for (const userId of expected) {
      const found = members.filter(member => member.userId === userId && member.status === 1);
      assert(found.length === 1 && typeof found[0].displayName === 'string' && found[0].displayName.trim().length > 0,
        'three existing active Workspace membership controls');
      assert(members.filter(member => member.displayName === found[0].displayName).length === 1,
        'unambiguous existing recipient label');
    }
  }
  const same = fixture.groups!.deniedSameTenant;
  const detail = await access.api(receiver, `/api/conversations/${same.conversationId}`);
  assert(detail.status === 200 && parse(detail).type === 'Group' &&
    parse(detail).workspaceId === fixture.deniedSameTenant.workspaceId, 'known same-Tenant denied Group scope');
  const known = await access.api(receiver, `/api/conversations/${same.conversationId}/messages?anchorMessageId=${same.messageId}`);
  assert(known.status === 200 && items(parse(known)).filter(row => row.id === same.messageId && row.body === same.body &&
    row.conversationId === same.conversationId && row.threadRootMessageId === null && row.isDeleted === false).length === 1,
    'authorized existing same-Tenant Group body control');
  for (const scope of Object.values(fixture.groups!)) await deniedGroup(manager, scope, access);
}

export async function exercisePilotGroup(manager: Page, receiver: Page, observer: Page, runId: string, access: Access) {
  const { fixture, baseURL } = access.configuration;
  const ids = [fixture.memberUserId, fixture.operatorUserId, fixture.revocation.userId];
  const members = parse(await access.api(manager, `/api/workspaces/${fixture.allowed.workspaceId}/members`)) as Member[];
  const names = ids.map(id => members.find(member => member.userId === id)!.displayName);
  await selectPilotWorkspace(manager, access);
  await access.navigate(manager, '/app/dm');
  await manager.getByTestId('new-message-button').click();
  const dialog = manager.getByTestId('new-message-dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByTestId('group-create-mode').check();
  const title = `Synthetic pilot ${runId} Group`;
  await dialog.getByTestId('group-title').fill(title);
  for (const name of names.slice(1)) await dialog.getByTestId('group-recipient-option').filter({ hasText: exactText(name) }).click();
  const pending = response(manager, baseURL, 'POST', '/api/conversations');
  await dialog.getByTestId('create-conversation-submit').click();
  const created = await pending;
  await command(created, { type: 7, workspaceId: fixture.allowed.workspaceId, memberUserIds: ids.slice(1), title });
  const group = await created.json();
  assert(isUuid(group.id) && group.type === 'Group' && group.workspaceId === fixture.allowed.workspaceId &&
    group.projectId === null && group.parentConversationId === null, 'canonical Workspace Group create');
  const id = group.id as string;
  let observerReadCommands = 0;
  observer.on('request', request => {
    const url = new URL(request.url());
    if (url.origin === baseURL && url.pathname === `/api/conversations/${id}/read` && request.method() === 'POST') observerReadCommands++;
  });
  assert(Array.isArray(group.members) && group.members.length === 3 && ids.every(userId =>
    group.members.some((member: any) => member.userId === userId && member.canRead === true && member.canCreateThread === false)),
    'server-admitted initial Group members');
  await expect(manager).toHaveURL(new RegExp(`/app/dm/${id}$`));
  await expect(manager.getByTestId('group-member-row')).toHaveCount(3);
  await expect(manager.locator('[data-testid^="open-message-thread-"]')).toHaveCount(0);
  const before = await ownState(observer, id, ids[2], access);
  assert(before.unreadCount === 0 && before.lastReadMessageId === null, 'fresh third-actor state before timeline open');
  await selectPilotWorkspace(receiver, access);
  await access.navigate(receiver, `/app/dm/${id}`);
  await expect(receiver.getByTestId('group-member-row')).toHaveCount(3);
  const first = await access.send(manager, id, 'dm', `Synthetic pilot ${runId} Group first`);
  assert(first.authorUserId === ids[0], 'Group first author');
  await expect(receiver.getByTestId('confirmed-message').filter({ hasText: first.body })).toHaveCount(1);
  const second = await access.send(receiver, id, 'dm', `Synthetic pilot ${runId} Group second`);
  assert(second.authorUserId === ids[1], 'Group second author');
  await expect(manager.getByTestId('confirmed-message').filter({ hasText: second.body })).toHaveCount(1);
  assert(pilotMessageUtcTicks(second.createdAt) > pilotMessageUtcTicks(first.createdAt), 'server full-precision newest loaded cursor');
  const unread = await ownState(observer, id, ids[2], access);
  assert(unread.unreadCount === 2 && unread.lastReadMessageId === null, 'third-actor unread accumulation');
  const otherBefore = await Promise.all([ownState(manager, id, ids[0], access), ownState(receiver, id, ids[1], access)]);
  assert(new Set([unread.participantId, ...otherBefore.map(state => state.participantId)]).size === 3,
    'three distinct actor-owned participant states');
  await selectPilotWorkspace(observer, access);
  await access.navigate(observer, '/app/dm');
  const row = observer.locator(`[data-testid="conversation-list-item"][data-conversation-id="${id}"]`);
  await expect(row).toHaveCount(1);
  await expect(row.getByTestId('conversation-unread-badge')).toHaveText('Unread 2');
  await row.click();
  for (const message of [first, second]) await expect(observer.getByTestId('confirmed-message').filter({ hasText: message.body })).toHaveCount(1);
  const onOpen = await ownState(observer, id, ids[2], access);
  assert(onOpen.unreadCount === 2 && onOpen.lastReadMessageId === null, 'opening does not substitute for explicit read');
  expect(observerReadCommands, 'no read command before explicit user action').toBe(0);
  const read = response(observer, baseURL, 'POST', `/api/conversations/${id}/read`);
  await observer.getByTestId('conversation-mark-read').click();
  await command(await read, { lastReadMessageId: second.id });
  await expect(observer.getByTestId('conversation-mark-read-status')).toHaveText(
    'Loaded messages marked as read. Refresh the list if its count is unavailable.');
  const after = await ownState(observer, id, ids[2], access);
  assert(after.unreadCount === 0 && after.lastReadMessageId === second.id && after.participantId === unread.participantId,
    'authoritative current-actor read cursor');
  expect(observerReadCommands, 'one explicit read command without automatic replay').toBe(1);
  for (const [index, actor] of [manager, receiver].entries()) {
    const current = await ownState(actor, id, ids[index], access), prior = otherBefore[index];
    assert(current.unreadCount === prior.unreadCount && current.lastReadMessageId === prior.lastReadMessageId &&
      current.participantId === prior.participantId, 'other actor unread and cursor isolation');
  }
  await access.navigate(observer, '/app/dm');
  await expect(row).toHaveCount(1);
  await expect(row.getByTestId('conversation-unread-badge')).toHaveCount(0);
  await observer.reload();
  await expect(row).toHaveCount(1);
  await expect(row.getByTestId('conversation-unread-badge')).toHaveCount(0);
  await access.navigate(observer, `/app/dm/${id}`);
  await expect(observer.getByTestId('message-draft')).toBeVisible();
  const observerRow = manager.getByTestId('group-member-row').filter({ has: manager.getByText(names[2], { exact: true }) });
  await observerRow.getByTestId('group-remove-member').click();
  const removal = response(manager, baseURL, 'DELETE', `/api/conversations/${id}/members/${ids[2]}`);
  await manager.getByRole('dialog', { name: 'Remove member', exact: true }).getByRole('button', { name: 'Remove member', exact: true }).click();
  await command(await removal, null);
  await expect(observerRow).toContainText('Removed');
  await deniedGroup(observer, { conversationId: id, messageId: first.id, body: first.body }, access);
  await observer.getByTestId('manual-refresh').click();
  await expect(observer.getByTestId('message-body')).toHaveCount(0);
  await expect(observer.getByTestId('group-members-panel')).toHaveCount(0);
  await expect(observer.getByTestId('message-draft')).toHaveCount(0);
  await manager.getByTestId('group-add-member-open').click();
  await manager.getByTestId('group-recipient-option').filter({ hasText: exactText(names[2]) }).click();
  const addition = response(manager, baseURL, 'POST', `/api/conversations/${id}/members`);
  await manager.getByTestId('group-add-member-submit').click();
  await command(await addition, { userId: ids[2] });
  const readded = parse(await access.api(manager, `/api/conversations/${id}/members`));
  const member = readded.find((entry: any) => entry.userId === ids[2]);
  assert(member && member.role === 1 && member.leftAt === null && member.removedAt === null &&
    member.canRead === true && member.canCreateThread === false, 'authoritative readd resets existing membership');
  await access.navigate(observer, `/app/dm/${id}`);
  await expect(observer.getByTestId('confirmed-message').filter({ hasText: first.body })).toHaveCount(1);
  await observer.getByTestId('group-leave').click();
  const leave = response(observer, baseURL, 'POST', `/api/conversations/${id}/leave`);
  await observer.getByRole('dialog', { name: 'Leave group', exact: true }).getByRole('button', { name: 'Leave group', exact: true }).click();
  await command(await leave, {});
  await deniedGroup(observer, { conversationId: id, messageId: first.id, body: first.body }, access);
  const retained = parse(await access.api(manager, `/api/conversations/${id}/members`));
  assert(retained.some((entry: any) => entry.userId === ids[0] && entry.role === 0 && entry.canRead === true &&
    entry.leftAt === null && entry.removedAt === null) && retained.some((entry: any) => entry.userId === ids[2] && entry.leftAt !== null),
    'creator remains authorized after third actor self-leaves');
  const held = await access.api(manager, `/api/messages/${first.id}/thread/messages`, 'POST', {
    body: `Synthetic pilot ${runId} held Group thread`, clientRequestId: randomUUID()
  });
  assert(isGroupContractDenial(held, [id, first.id, first.body, second.body], 'thread'), 'held Group thread permission denial');
  return validateGroupCheckpoint({ conversationId: id, firstMessageId: first.id, secondMessageId: second.id,
    unreadUiVerified: true, removedReaddedLeftVerified: true, creatorRetained: true });
}

export async function verifyPilotGroup(manager: Page, checkpoint: Record<string, any>, access: Access) {
  const group = validateGroupCheckpoint(checkpoint.group);
  const detail = await access.api(manager, `/api/conversations/${group.conversationId}`);
  assert(detail.status === 200 && parse(detail).type === 'Group' &&
    parse(detail).workspaceId === access.configuration.fixture.allowed.workspaceId, 'retained authorized Group detail');
  const members = parse(detail).members;
  assert(Array.isArray(members) && members.some((member: any) => member.userId === access.configuration.fixture.memberUserId &&
    member.role === 0 && member.canRead === true && member.leftAt === null && member.removedAt === null) &&
    members.some((member: any) => member.userId === access.configuration.fixture.revocation.userId && member.leftAt !== null),
    'retained creator and historical self-leave');
  for (const [key, suffix, author] of [['firstMessageId', 'first', access.configuration.fixture.memberUserId],
    ['secondMessageId', 'second', access.configuration.fixture.operatorUserId]] as const) {
    const result = await access.api(manager, `/api/conversations/${group.conversationId}/messages?anchorMessageId=${group[key]}`);
    assert(result.status === 200 && items(parse(result)).filter(row => row.id === group[key] &&
      row.body === `Synthetic pilot ${checkpoint.runId} Group ${suffix}` && row.authorUserId === author &&
      row.conversationId === group.conversationId && row.threadRootMessageId === null).length === 1, 'durable unique Group body');
  }
  await access.navigate(manager, `/app/dm/${group.conversationId}`);
  await manager.reload();
  for (const suffix of ['first', 'second']) await expect(manager.getByTestId('confirmed-message').filter({
    hasText: `Synthetic pilot ${checkpoint.runId} Group ${suffix}` })).toHaveCount(1);
}

async function deniedGroup(page: Page, group: { conversationId: string; messageId: string; body: string }, access: Access) {
  const path = `/api/conversations/${group.conversationId}`;
  for (const suffix of ['', '/members', '/state', '/messages', `/messages?anchorMessageId=${group.messageId}`]) {
    assert(isGroupContractDenial(await access.api(page, path + suffix), [group.conversationId, group.messageId, group.body], 'read'), 'known protected Group read denial');
  }
  assert(isGroupContractDenial(await access.api(page, `${path}/messages`, 'POST', {
    body: 'Synthetic denied Group send control', clientRequestId: randomUUID()
  }), [group.conversationId, group.messageId, group.body], 'send'), 'known protected Group send denial');
}
async function ownState(page: Page, id: string, userId: string, access: Access) {
  const result = await access.api(page, `/api/conversations/${id}/state`), value = parse(result);
  assert(result.status === 200 && value.userId === userId && value.conversationId === id && isUuid(value.participantId) &&
    Number.isSafeInteger(value.unreadCount) && value.unreadCount >= 0, 'current actor-only participant state');
  return value;
}
function response(page: Page, origin: string, method: string, path: string) {
  return page.waitForResponse(result => new URL(result.url()).origin === origin &&
    new URL(result.url()).pathname === path && result.request().method() === method);
}
async function command(result: Response, expected: Record<string, unknown> | null) {
  assert(result.status() === 200, 'confirmed Angular Group command');
  const actual = result.request().postDataJSON();
  if (expected === null) expect(actual).toBeNull();
  else {
    if (expected.memberUserIds) {
      expect(actual.memberUserIds.slice().sort()).toEqual((expected.memberUserIds as string[]).slice().sort());
      expect({ ...actual, memberUserIds: undefined }).toEqual({ ...expected, memberUserIds: undefined });
    } else expect(actual).toEqual(expected);
  }
  assert(Boolean((await result.request().allHeaders())['x-csrf-token']), 'actual Angular CSRF header presence');
}
function items(value: any): Record<string, any>[] { return Array.isArray(value?.items) ? value.items : []; }
function exactText(value: string) { return new RegExp(`^${value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&')}$`, 'u'); }
function parse(result: Result): any { try { return JSON.parse(result.text); } catch { throw new Error('Pilot Group response was not the required JSON contract.'); } }
function assert(value: unknown, label: string): asserts value { if (!value) throw new Error(`Pilot check failed: ${label}.`); }
