import { expect, type Page, type Request, type Response } from '@playwright/test';

type Member = { userId: string; displayName: string; role: number; status: number };

/** Existing synthetic seed and real Angular commands only; no intercepted responses. */
export async function exerciseWorkspaceMemberManagement(
  manager: Page,
  recipient: Page,
  workspaceId: string,
  conversationId: string,
  expectedMessage: string,
  recipientName: string,
) {
  const api = `/api/workspaces/${workspaceId}/members`;
  const route = `/app/workspaces/${workspaceId}/members`;
  const options = api + '/management/options';
  const messages = `/api/conversations/${conversationId}/messages`;
  const commands: string[] = [];
  const observe = (request: Request) => {
    const path = new URL(request.url()).pathname;
    if (['POST', 'PATCH', 'DELETE'].includes(request.method()) && (path === api || path.startsWith(api + '/')))
      commands.push(`${request.method()} ${path}`);
  };
  manager.on('request', observe);
  try {
    const selfResponse = await recipient.request.get('/api/auth/me');
    expect(selfResponse.status()).toBe(200);
    const self = await selfResponse.json() as { userId: string; systemRole: number };
    expect(self.userId).toMatch(/^[0-9a-f-]{36}$/iu);
    expect(self.systemRole, 'the recipient has no platform-management bypass').toBe(0);
    expect((await recipient.request.get(options)).status(), 'recipient is initially outside the new Workspace').toBe(404);

    await manager.getByTestId('workspace-member-add').click();
    const add = manager.getByRole('dialog', { name: 'Add member', exact: true });
    await expect(add).toBeVisible();
    await manager.getByTestId('workspace-member-candidate-query').fill(recipientName);
    const candidateResponse = responseFor(manager, 'GET', api + '/management/candidates');
    await manager.getByTestId('workspace-member-candidate-search').click();
    const candidateResult = await candidateResponse;
    expect(candidateResult.status()).toBe(200);
    const query = new URL(candidateResult.request().url()).searchParams;
    expect(query.get('q')).toBe(recipientName);
    expect(query.get('take')).toBe('20');
    expect(candidateResult.headers()['cache-control']).toContain('private');
    expect(candidateResult.headers()['cache-control']).toContain('no-store');
    const candidates = await candidateResult.json() as { items: { userId: string; displayName: string; allowedRoles: number[] }[] };
    const matches = candidates.items.filter((item) => item.userId === self.userId);
    expect(matches, 'the bounded manager read finds the independently authenticated synthetic recipient').toHaveLength(1);
    expect(Object.keys(matches[0]).sort((left, right) => left.localeCompare(right, 'en'))).toEqual(['allowedRoles', 'displayName', 'userId']);
    expect(matches[0].displayName).toBe(recipientName);
    expect(matches[0].allowedRoles).toContain(1);
    await manager.getByTestId('workspace-member-candidate').filter({ hasText: self.userId }).click();
    await expect(add.getByRole('button', { name: 'Add member', exact: true })).toBeDisabled();
    await manager.getByTestId('workspace-member-selected-role').selectOption({ label: 'Admin' });
    await manager.getByTestId('workspace-member-confirm-identity').check();
    const addedResponse = responseFor(manager, 'POST', api);
    await add.getByRole('button', { name: 'Add member', exact: true }).click();
    await assertCommand(await addedResponse, { userId: self.userId, role: 1 });
    await expect(add).toHaveCount(0);
    await assertPersistedMember(manager, api, self.userId, 1);
    await manager.reload();
    await expect(manager.getByTestId('workspace-members-page')).toBeVisible();
    await expect(manager.getByTestId('workspace-member-add')).toBeVisible();
    const admission = await recipient.request.get(options);
    expect(admission.status(), 'fresh recipient management read after admission').toBe(200);
    expect(await admission.json()).toMatchObject({ workspaceId, canAddMember: true, target: null });
    const initialMessages = await recipient.request.get(messages);
    expect(initialMessages.status(), 'new member can read the canonical General body').toBe(200);
    expect(await initialMessages.text()).toContain(expectedMessage);

    await changeRole(manager, api, self.userId, recipientName, 'Read only', 4);
    await manager.reload();
    await assertPersistedMember(manager, api, self.userId, 4);
    expect((await recipient.request.get(options)).status(), 'ReadOnly loses management authority').toBe(403);
    expect((await recipient.request.get(api)).status(), 'ReadOnly retains ordinary directory access').toBe(200);
    await changeRole(manager, api, self.userId, recipientName, 'Admin', 1);
    await assertPersistedMember(manager, api, self.userId, 1);
    expect((await recipient.request.get(options)).status(), 'restored Admin is a positive control before removal').toBe(200);

    await openDetail(manager, api, self.userId, recipientName);
    await manager.getByTestId('workspace-member-remove').click();
    const remove = manager.getByRole('dialog', { name: 'Remove access', exact: true });
    await expect(remove.getByRole('button', { name: 'Remove access', exact: true })).toBeDisabled();
    await manager.getByTestId('workspace-member-confirm-identity').check();
    const removedResponse = responseFor(manager, 'DELETE', `${api}/${self.userId}`);
    await remove.getByRole('button', { name: 'Remove access', exact: true }).click();
    await assertCommand(await removedResponse, null);
    await expect(remove).toHaveCount(0);
    await manager.goto(route);
    await manager.reload();
    const remainingResponse = await manager.request.get(api);
    expect(remainingResponse.status()).toBe(200);
    const remaining = await remainingResponse.json() as Member[];
    expect(remaining.some((item) => item.userId === self.userId)).toBe(false);
    const removedOptions = await manager.request.get(`${options}?userId=${self.userId}`);
    expect(removedOptions.status()).toBe(200);
    expect(await removedOptions.json()).toMatchObject({ target: { userId: self.userId, currentRole: 1, membershipStatus: 2 } });
    expect((await recipient.request.get(options)).status(), 'removed Admin cannot reuse an earlier preview').toBe(404);
    const deniedMessages = await recipient.request.get(messages);
    expect(deniedMessages.status(), 'removed Workspace member cannot read General through stale participation').toBe(400);
    expect(await deniedMessages.json()).toEqual({ error: 'Conversation not found.' });
    expect(await deniedMessages.text()).not.toContain(expectedMessage);
    for (const id of [workspaceId, conversationId, self.userId]) {
      expect(await deniedMessages.text()).not.toContain(id);
    }
    await expect(manager.getByTestId('workspace-member-editor')).toHaveCount(0);
    const afterRemovalCandidates = await manager.request.get(api + '/management/candidates', { params: { q: recipientName } });
    expect(afterRemovalCandidates.status()).toBe(200);
    const afterRemovalPage = await afterRemovalCandidates.json() as { items: { userId: string }[] };
    expect(afterRemovalPage.items.some((item) => item.userId === self.userId), 'suspended relationships cannot be added again through candidate search').toBe(false);
    expect(commands, 'exactly four explicit Angular writes; no automatic command replay').toEqual([
      `POST ${api}`, `PATCH ${api}/${self.userId}`, `PATCH ${api}/${self.userId}`, `DELETE ${api}/${self.userId}`,
    ]);
    return { added: 1, roleChanges: 2, removed: 1, reloadChecked: true,
      readOnlyManagementStatus: 403, removedManagementStatus: 404, removedGeneralStatus: 400 };
  } finally {
    manager.off('request', observe);
  }
}

async function openDetail(page: Page, api: string, userId: string, displayName: string) {
  const row = page.getByTestId('workspace-members-page').getByRole('row').filter({
    has: page.getByText(displayName, { exact: true }),
  });
  await expect(row).toHaveCount(1);
  const detailResponse = responseFor(page, 'GET', `${api}/${userId}`);
  await row.locator('[data-grid-action="openMemberDetail"]').click();
  const response = await detailResponse;
  expect(response.status()).toBe(200);
  expect(await response.json()).toMatchObject({ userId, displayName });
  await expect(page.getByTestId('workspace-member-detail-name')).toHaveText(displayName);
}

async function changeRole(page: Page, api: string, userId: string, displayName: string, label: string, role: number) {
  await openDetail(page, api, userId, displayName);
  await page.getByTestId('workspace-member-change-role').click();
  const dialog = page.getByRole('dialog', { name: 'Change role', exact: true });
  await expect(dialog.getByRole('button', { name: 'Change role', exact: true })).toBeDisabled();
  await page.getByTestId('workspace-member-selected-role').selectOption({ label });
  await page.getByTestId('workspace-member-confirm-identity').check();
  const response = responseFor(page, 'PATCH', `${api}/${userId}`);
  await dialog.getByRole('button', { name: 'Change role', exact: true }).click();
  await assertCommand(await response, { role });
  await expect(dialog).toHaveCount(0);
}

async function assertPersistedMember(page: Page, api: string, userId: string, role: number) {
  const response = await page.request.get(api);
  expect(response.status()).toBe(200);
  const members = await response.json() as Member[];
  const matches = members.filter((item) => item.userId === userId);
  expect(matches).toHaveLength(1);
  expect(matches[0]).toMatchObject({ userId, role, status: 1 });
}

function responseFor(page: Page, method: string, path: string) {
  const origin = new URL(page.url()).origin;
  return page.waitForResponse((response) => response.request().method() === method &&
    new URL(response.url()).origin === origin && new URL(response.url()).pathname === path);
}

async function assertCommand(response: Response, body: Record<string, unknown> | null) {
  expect(response.status()).toBe(200);
  expect(response.request().postDataJSON()).toEqual(body);
  const headers = await response.request().allHeaders();
  expect(Boolean(headers['x-csrf-token']), 'the real Angular CSRF interceptor supplied a token').toBe(true);
}
