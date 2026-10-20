import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = 'static-workspace-1';
const groupId = '77777777-7777-4777-8777-777777777777';
const actor = 'mock-user-a';

test.describe('Student Council Group DM mock frontend contract', () => {
  for (const width of [1536, 1366, 768, 390, 320]) {
    const locale = width === 390 ? 'ja' : 'en';
    test(`creates and sends with workspace recipients, keyboard and IME at ${width}px / ${locale}`, async ({ page }, testInfo) => {
      const control = await installGroupApi(page);
      control.existingGroup = false;
      await page.setViewportSize({ width, height: 1024 });
      await page.addInitScript((locale) => { localStorage.setItem('coglatas.ui.theme.v1', 'dark'); localStorage.setItem('coglatas.locale', locale); }, locale);
      await page.goto('/app/dm');
      await page.getByTestId('new-message-button').click();
      const dialog = page.getByTestId('new-message-dialog');
      await page.getByTestId('group-create-mode').check();
      await expect(page.getByTestId('group-recipient-option')).toHaveCount(3);
      await expect(dialog).not.toContainText('Suspended candidate');
      const title = page.getByTestId('group-title');
      await title.fill('Council preparation');
      const sameTitle = await title.elementHandle();
      await title.dispatchEvent('compositionstart', { data: '生徒会' });
      await title.dispatchEvent('keydown', { key: 'Escape', code: 'Escape', isComposing: true, keyCode: 229 });
      await expect(dialog).toBeVisible();
      expect(await sameTitle!.evaluate((element) => element.isConnected)).toBe(true);
      await title.dispatchEvent('compositionend', { data: '生徒会' });
      await page.getByTestId('group-recipient-option').filter({ hasText: 'Council peer' }).click();
      await expect(page.getByTestId('create-conversation-submit')).toBeDisabled();
      await page.getByTestId('group-recipient-option').filter({ hasText: 'New participant' }).click();
      const submit = page.getByTestId('create-conversation-submit');
      await expect(submit).toBeEnabled();
      const bounds = await submit.boundingBox();
      expect(bounds!.height).toBeGreaterThanOrEqual(44);
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(1024);
      await expectNoOverflow(page);
      await expectNoAccessibilityViolations(page, '[data-testid="new-message-dialog"]');
      await page.screenshot({ path: testInfo.outputPath(`group-create-${width}-dark.png`) });
      await submit.click();
      await expect(page).toHaveURL(new RegExp(`/app/dm/${groupId}$`));
      expect(control.creates).toEqual([{ type: 7, workspaceId, memberUserIds: ['peer', 'new-peer'], title: 'Council preparation' }]);
      expect(control.sharedRecipientReads).toBe(0);
      await expect(page.getByTestId('dm-page')).toContainText('Council preparation');
      await expect(page.getByTestId('group-members-panel')).toContainText('New participant');
      await expect(page.getByTestId('group-member-row')).toHaveCount(3);
      await expect(page.locator('[data-testid^="open-message-thread-"]')).toHaveCount(0);
      const draft = page.getByTestId('message-draft');
      await draft.fill('Synthetic Group IME draft');
      await draft.dispatchEvent('compositionstart');
      await draft.press('Enter');
      expect(control.sends).toHaveLength(0);
      await draft.dispatchEvent('compositionend');
      await draft.fill('Synthetic Group confirmed message');
      await page.getByTestId('send-message').click();
      await expect(draft).toHaveValue('');
      await expect(page.getByTestId('confirmed-message').filter({ hasText: 'Synthetic Group confirmed message' })).toHaveCount(1);
      expect(control.sends).toHaveLength(1);
      expect(control.sends[0]).toMatchObject({ body: 'Synthetic Group confirmed message', mentionedUserIds: [] });
      expect(control.sends[0].clientRequestId).toMatch(/^[0-9a-f-]{36}$/i);
      await page.reload();
      await expect(page.getByTestId('confirmed-message').filter({ hasText: 'Synthetic Group confirmed message' })).toHaveCount(1);
      expect(control.threadRequests).toEqual([]);
      await expectNoOverflow(page);
      await page.screenshot({ path: testInfo.outputPath(`group-conversation-${width}-dark.png`), fullPage: true });
      await testInfo.attach('group-css-viewport.json', { body: JSON.stringify(await page.evaluate(() => ({ width: innerWidth, height: innerHeight, devicePixelRatio, locale: document.documentElement.lang }))), contentType: 'application/json' });
    });
  }

  test('re-adds a former manager as Member, removes an Admin without hierarchy, and allows sole-manager leave', async ({ page }) => {
    await page.setViewportSize({ width: 1000, height: 800 });
    const control = await installGroupApi(page);
    await page.goto(`/app/dm/${groupId}`);
    const panel = page.getByTestId('group-members-panel');
    await expect(panel).toBeVisible();
    const detailsBounds = await page.locator('.dm-page__group-details').boundingBox();
    const conversationBounds = await page.getByTestId('dm-conversation-surface').boundingBox();
    expect(detailsBounds!.width).toBe(300);
    expect(detailsBounds!.x).toBeGreaterThan(conversationBounds!.x + conversationBounds!.width);
    await expect(page.getByTestId('dm-conversation-navigator')).toBeHidden();
    await page.setViewportSize({ width: 320, height: 800 });
    await expect(panel.getByTestId('group-member-row').filter({ hasText: 'Former manager' })).toContainText('Left');
    await page.getByTestId('group-add-member-open').click();
    await page.getByTestId('group-recipient-option').filter({ hasText: 'Former manager' }).click();
    await page.getByTestId('group-add-member-submit').click();
    await expect(panel.getByTestId('group-member-row').filter({ hasText: 'Former manager' })).toContainText('Member');
    await expect(panel.locator('summary')).toBeFocused();
    expect(control.adds).toEqual(['former']);
    const peer = panel.getByTestId('group-member-row').filter({ hasText: 'Council peer' });
    await expect(peer).toContainText('Admin');
    await peer.getByTestId('group-remove-member').click();
    const confirmation = page.getByRole('dialog', { name: 'Remove member', exact: true });
    await expect(confirmation).toContainText('last manager may leave or be removed');
    await confirmation.getByRole('button', { name: 'Remove member', exact: true }).click();
    await expect(panel.getByTestId('group-member-row').filter({ hasText: 'Council peer' })).toContainText('Removed');
    expect(control.removes).toEqual(['peer']);
    await page.getByTestId('group-leave').click();
    await page.getByRole('dialog', { name: 'Leave group', exact: true }).getByRole('button', { name: 'Leave group', exact: true }).click();
    await expect(page).toHaveURL(/\/app\/dm$/);
    await expect(page.getByTestId('group-members-panel')).toHaveCount(0);
    expect(control.leaves).toBe(1);
    expect(control.threadRequests).toEqual([]);
  });

  test('retains a safe create error and fresh workspace choices after canonical rejection', async ({ page }) => {
    const control = await installGroupApi(page); control.rejectCreate = true;
    await page.goto('/app/dm'); await page.getByTestId('new-message-button').click();
    await page.getByRole('radio', { name: 'Group DM', exact: true }).check();
    await page.getByTestId('group-recipient-option').filter({ hasText: 'Council peer' }).click();
    await page.getByTestId('group-recipient-option').filter({ hasText: 'New participant' }).click();
    await page.getByTestId('create-conversation-submit').click();
    await expect(page.getByTestId('create-conversation-error')).toBeVisible();
    await expect(page.getByTestId('new-message-dialog')).not.toContainText('Protected raw rejection');
    await expect(page.getByTestId('create-conversation-submit')).toBeDisabled();
    await expect(page.getByTestId('group-recipient-option')).toHaveCount(3);
    expect(control.creates).toHaveLength(1); expect(control.sharedRecipientReads).toBe(0);
  });

  test('keeps ordinary ReadOnly management hidden and clears participants and bodies after revocation', async ({ page }) => {
    const control = await installGroupApi(page); control.manager = false; control.readOnly = true;
    await page.goto(`/app/dm/${groupId}`);
    await expect(page.getByTestId('message-draft')).toBeDisabled();
    await expect(page.getByTestId('group-add-member-open')).toHaveCount(0);
    await expect(page.getByTestId('group-remove-member')).toHaveCount(0);
    await expect(page.getByTestId('group-leave')).toBeEnabled();
    await expect(page.getByTestId('dm-page')).toContainText('Protected Group body');
    control.denied = true;
    await page.getByTestId('manual-refresh').click();
    await expect(page.getByTestId('group-members-panel')).toHaveCount(0);
    await expect(page.getByTestId('message-body')).toHaveCount(0);
    await expect(page.getByTestId('dm-page')).not.toContainText('Former manager');
    expect(control.sends).toHaveLength(0); expect(control.adds).toEqual([]); expect(control.threadRequests).toEqual([]);
  });
});

async function expectNoOverflow(page: Page): Promise<void> {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
}

async function installGroupApi(page: Page) {
  const control = { creates: [] as Record<string, unknown>[], sends: [] as Record<string, unknown>[], adds: [] as string[], removes: [] as string[],
    leaves: 0, denied: false, existingGroup: true, rejectCreate: false, manager: true, readOnly: false, sharedRecipientReads: 0, threadRequests: [] as string[] };
  const workspaceMembers = [
    { userId: actor, displayName: 'Current participant', status: 1 },
    { userId: 'peer', displayName: 'Council peer', status: 1 },
    { userId: 'new-peer', displayName: 'New participant', status: 'Active' },
    { userId: 'former', displayName: 'Former manager', status: 1 },
    { userId: 'suspended', displayName: 'Suspended candidate', status: 2 }
  ];
  let title = 'Council Group';
  let members = [
    { userId: actor, displayName: 'Current participant', role: 'Admin', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null as string | null, removedAt: null as string | null },
    { userId: 'peer', displayName: 'Council peer', role: 'Admin', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null as string | null, removedAt: null as string | null },
    { userId: 'former', displayName: 'Former manager', role: 'Admin', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: '2026-10-19T00:00:00Z' as string | null, removedAt: null as string | null }
  ];
  let messages = [{ id: 'group-root', workspaceId, conversationId: groupId, authorUserId: 'peer', authorDisplayName: 'Council peer', body: 'Protected Group body',
    createdAt: '2026-10-19T01:00:00Z', clientRequestId: 'original-group-key', isDeleted: false, version: 1 }];
  const detail = () => ({ id: groupId, workspaceId, type: 'Group', title, projectId: null, parentConversationId: null, rootConversationId: null,
    isLocked: false, isArchived: false, members: members.map((member) => member.userId === actor
      ? { ...member, role: control.readOnly ? 'ReadOnly' : control.manager ? 'Admin' : 'Member', canManageMembers: control.manager } : member) });
  await page.route('**/api/security/csrf-token', (route) => route.fulfill({ json: { token: 'csrf-group-ui', headerName: 'X-CSRF-Token' } }));
  await page.route('**/api/**', async (route) => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (path === `/api/workspaces/${workspaceId}/members` && method === 'GET') { await route.fulfill({ json: workspaceMembers }); return; }
    if (path === '/api/conversations/recipients') { control.sharedRecipientReads++; await route.fulfill({ json: [] }); return; }
    if (/^\/api\/messages\/.*\/thread/.test(path)) { control.threadRequests.push(`${method} ${path}`); await route.fulfill({ status: 400, json: { error: 'Held' } }); return; }
    if (path === '/api/conversations' && method === 'GET') {
      await route.fulfill({ json: { items: control.denied || !control.existingGroup ? [] : [{ id: groupId, workspaceId, type: 'Group', title, unreadCount: 1 }], page: 1, pageSize: 20, totalCount: control.denied || !control.existingGroup ? 0 : 1 } }); return;
    }
    if (path === '/api/conversations' && method === 'POST') {
      const payload = request.postDataJSON(); control.creates.push(payload);
      expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
      if (control.rejectCreate) { await route.fulfill({ status: 400, json: { error: 'Protected raw rejection' } }); return; }
      control.existingGroup = true; title = payload.title || 'Group DM'; messages = [];
      members = [
        { userId: actor, displayName: 'Current participant', role: 'Admin', canRead: true, canPost: true, canManageMembers: true, canCreateThread: false, leftAt: null, removedAt: null },
        ...payload.memberUserIds.map((userId: string) => ({ userId, displayName: workspaceMembers.find((member) => member.userId === userId)!.displayName,
          role: 'Member', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null, removedAt: null }))
      ];
      await route.fulfill({ json: detail() }); return;
    }
    if (path === `/api/conversations/${groupId}` && method === 'GET') {
      await route.fulfill(control.denied ? { status: 400, json: { error: 'Conversation not found.' } } : { json: detail() }); return;
    }
    if (path === `/api/conversations/${groupId}/messages`) {
      if (method === 'POST') {
        const payload = request.postDataJSON(); control.sends.push(payload); expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
        const message = { id: 'created-group-message', workspaceId, conversationId: groupId, createdAt: '2026-10-20T00:00:00Z', isDeleted: false, version: 1,
          authorUserId: actor, authorDisplayName: 'Current participant', body: payload.body, clientRequestId: payload.clientRequestId };
        messages = [...messages, message]; await route.fulfill({ json: message });
      } else { await route.fulfill({ json: { items: messages } }); }
      return;
    }
    if (path === `/api/conversations/${groupId}/members` && method === 'POST') {
      const userId = request.postDataJSON().userId; control.adds.push(userId); expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
      const person = workspaceMembers.find((member) => member.userId === userId && (member.status === 1 || member.status === 'Active'));
      if (!person || members.some((member) => member.userId === userId && !member.leftAt && !member.removedAt)) {
        await route.fulfill({ status: 400, json: { error: 'Membership change unavailable.' } }); return;
      }
      if (!members.some((member) => member.userId === userId)) {
        members.push({ userId, displayName: person.displayName, role: 'Member', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null, removedAt: null });
      }
      members = members.map((member) => member.userId === userId ? { ...member, role: 'Member', canManageMembers: false, leftAt: null, removedAt: null } : member);
      await route.fulfill({ json: members.find((member) => member.userId === userId) }); return;
    }
    if (path.startsWith(`/api/conversations/${groupId}/members/`) && method === 'DELETE') {
      const userId = path.split('/').at(-1)!; control.removes.push(userId); expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
      members = members.map((member) => member.userId === userId ? { ...member, removedAt: '2026-10-20T00:00:00Z' } : member);
      await route.fulfill({ json: {} }); return;
    }
    if (path === `/api/conversations/${groupId}/leave` && method === 'POST') {
      control.leaves++; expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui'); control.denied = true; await route.fulfill({ json: {} }); return;
    }
    await route.fallback();
  });
  return control;
}
