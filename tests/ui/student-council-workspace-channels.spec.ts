import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
import { serializeChannelDialogObservation } from './workspace-channel-directory-real';

const tenantId = '11000000-0000-4000-8000-000000000001';
const userId = '12000000-0000-4000-8000-000000000001';
const workspaceId = '13000000-0000-4000-8000-000000000001';
const generalId = '14000000-0000-4000-8000-000000000001';
const publicId = '15000000-0000-4000-8000-000000000001';
const privateId = '16000000-0000-4000-8000-000000000001';
const recipientId = '17000000-0000-4000-8000-000000000001';
const apiRoot = `/api/workspaces/${workspaceId}/chat-channels`;

type Channel = {
  id: string;
  workspaceId: string;
  name: string;
  visibility: 'PublicWithinScope' | 'Private';
  isMember: boolean;
  canJoin: boolean;
  canManage: boolean;
  isDefault: boolean;
};

async function installAuthorizedDirectory(page: Page) {
  const channels: Channel[] = [
    { id: generalId, workspaceId, name: 'general', visibility: 'PublicWithinScope',
      isMember: true, canJoin: false, canManage: false, isDefault: true },
    { id: publicId, workspaceId, name: 'Council notices', visibility: 'PublicWithinScope',
      isMember: false, canJoin: true, canManage: false, isDefault: false }
  ];
  const creates: Array<{ name: string; visibility: string; key: string }> = [];
  const invites: string[] = [];
  let denyList = false;

  await page.route('**/api/auth/me', (route) => route.fulfill({ json: {
    userId, displayName: 'Council operator', email: 'operator@example.test', systemRole: 'User',
    status: 'Active', capabilities: ['workspace:view', 'files:view', 'account:view'],
    currentWorkspace: { id: workspaceId, name: 'Student council' },
    workspaces: [{ id: workspaceId, name: 'Student council' }]
  } }));
  await page.route('**/api/tenants/current', (route) => route.fulfill({ json: {
    tenantId, tenantSlug: 'council-fixture', isAvailable: true, isPlatformScope: false,
    currentUserRole: 'Owner', displayName: 'Council fixture', status: 'Active',
    appMode: 'SaaS', allowTenantSwitching: false
  } }));
  await page.route('**/api/workspaces', (route) => route.fulfill({ json: [{
    id: workspaceId, name: 'Student council', currentUserRole: 'Owner', accessSource: 'WorkspaceMembership',
    canOpenWorkspace: true, canOpenMembers: true, canOpenProjects: true, canAddFiles: true
  }] }));
  await page.route('**/api/security/csrf-token', (route) => route.fulfill({
    json: { token: 'synthetic-channel-csrf', headerName: 'X-CSRF-Token' }
  }));
  await page.route('**/api/workspaces/**/chat-channels**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === apiRoot && request.method() === 'GET') {
      if (denyList) { await route.fulfill({ status: 403, json: { error: 'Denied' } }); return; }
      const q = (url.searchParams.get('search') ?? '').toLowerCase();
      const matches = channels.filter((item) => item.name.toLowerCase().includes(q));
      await route.fulfill({ json: {
        workspaceId, canCreate: true, items: matches, page: 1, pageSize: 50, totalCount: matches.length
      } });
      return;
    }
    if (path === apiRoot && request.method() === 'POST') {
      const data = request.postDataJSON() as { name: string; visibility: 'PublicWithinScope' | 'Private' };
      const key = request.headers()['idempotency-key'] ?? '';
      creates.push({ name: data.name, visibility: data.visibility, key });
      if (!/^[0-9a-f-]{36}$/i.test(key) || request.headers()['x-csrf-token'] !== 'synthetic-channel-csrf') {
        await route.fulfill({ status: 400, json: { error: 'Invalid request boundary' } });
        return;
      }
      const item: Channel = { id: privateId, workspaceId, name: data.name, visibility: data.visibility,
        isMember: true, canJoin: false, canManage: true, isDefault: false };
      channels.push(item);
      await route.fulfill({ json: item });
      return;
    }
    if (path === `${apiRoot}/${publicId}/join` && request.method() === 'POST') {
      const item = channels.find((candidate) => candidate.id === publicId)!;
      item.isMember = true;
      item.canJoin = false;
      await route.fulfill({ json: item });
      return;
    }
    if (path === `${apiRoot}/${privateId}/members` && request.method() === 'POST') {
      const data = request.postDataJSON() as { userId: string };
      if (request.headers()['x-csrf-token'] !== 'synthetic-channel-csrf' ||
          data.userId !== recipientId) {
        await route.fulfill({ status: 403, json: { error: 'Rejected membership boundary' } });
        return;
      }
      invites.push(data.userId);
      await route.fulfill({ json: { status: 'OK' } });
      return;
    }
    await route.fallback();
  });

  return { creates, invites, setDenied(value: boolean) { denyList = value; } };
}


test('channel-dialog diagnostic attachment accepts only fixed bounded structural data', () => {
  const event = { atMs: 1, kind: 'click', directoryPresent: true,
    createPresent: true, createEnabled: true, dialogPresent: false };
  const valid = { schemaVersion: 1, events: [event], eventsTruncated: 0 };
  expect(serializeChannelDialogObservation(valid)).toBe(JSON.stringify(valid));

  const secret = 'private-channel-name-or-session-value';
  const invalid = [
    { ...valid, channelName: secret },
    { ...valid, events: [{ ...event, channelName: secret }] },
    { ...valid, events: [{ ...event, kind: secret }] },
    { ...valid, events: [{ ...event, kind: { privateText: secret } }] },
    { ...valid, events: [{ ...event, atMs: Number.NaN }] },
    { ...valid, events: [{ ...event, atMs: true }] },
    { ...valid, events: [{ ...event, createEnabled: secret }] },
    { ...valid, events: new Array(49).fill(event) },
    { ...valid, eventsTruncated: 1_000_001 },
    { ...valid, events: [] },
    { ...valid, unexpectedPayload: { body: secret } }
  ];
  for (const payload of invalid) {
    expect(serializeChannelDialogObservation(payload)).toBeNull();
  }
});

test.describe('Student council Workspace channel directory with real Angular rendering', () => {
  for (const width of [1536, 1366, 960, 768, 390, 320]) {
    test(`authorizes, joins and creates channels with keyboard and responsive layout at ${width}px`,
      async ({ page }, testInfo) => {
        await page.addInitScript(() => localStorage.setItem('coglatas.locale', 'en'));
        await page.setViewportSize({ width, height: 920 });
        const backend = await installAuthorizedDirectory(page);
        await page.goto('/app/teams');
        const directory = page.getByTestId('workspace-channel-directory');
        await expect(directory).toBeVisible();
        await expect(page.getByRole('main')).toHaveCount(1);
        await expect(directory.getByRole('heading', { name: 'Workspace channels' })).toBeVisible();
        await expect(directory).toContainText('Student council');
        await expect(directory.locator('[data-channel-id]')).toHaveCount(2);
        await expect(directory).not.toContainText('Council private');
        const join = directory.getByTestId('channel-directory-join');
        await join.focus();
        await expect(join).toBeFocused();
        await join.press('Enter');
        await expect(directory.getByTestId('channel-confirmed-open')).toHaveAttribute('href',
          `/app/workspaces/${workspaceId}/channels/${publicId}`);
        await expect(directory.getByTestId('channel-directory-open')).toHaveCount(2);

        const create = directory.getByTestId('channel-create-open');
        await create.press('Enter');
        const dialog = page.getByTestId('channel-create-dialog');
        await expect(dialog).toBeVisible();
        await expect(dialog).toHaveAttribute('role', 'dialog');
        await expect(dialog.locator('form')).toHaveCount(1);
        await expect(dialog.getByTestId('channel-create-name')).toBeFocused();
        const channelName = dialog.getByTestId('channel-create-name');
        await channelName.fill('Council private');
        await channelName.dispatchEvent('compositionstart', { data: '生徒会' });
        await channelName.press('Enter');
        await expect(dialog).toBeVisible();
        expect(backend.creates).toHaveLength(0);
        await channelName.dispatchEvent('compositionend', { data: '生徒会' });
        await dialog.getByTestId('channel-create-visibility').selectOption('Private');
        await expectNoAccessibilityViolations(page, '[data-testid="channel-create-dialog"]');
        await dialog.getByTestId('channel-create-submit').click();
        await expect(dialog).toHaveCount(0);
        await expect(directory).toContainText('Council private');
        await expect(directory.getByTestId('channel-confirmed-open')).toHaveAttribute('href',
          `/app/workspaces/${workspaceId}/channels/${privateId}`);
        expect(backend.creates).toHaveLength(1);
        expect(backend.creates[0]).toMatchObject({ name: 'Council private', visibility: 'Private' });

        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
        await expectNoAccessibilityViolations(page, '[data-testid="workspace-channel-directory"]');
        await page.screenshot({ path: testInfo.outputPath(`workspace-channels-${width}.png`), fullPage: true });

        backend.setDenied(true);
        await directory.getByRole('button', { name: 'Refresh', exact: true }).click();
        await expect(directory.getByTestId('channel-directory-denied')).toBeVisible();
        await expect(directory).not.toContainText('Council private');
        await expect(directory.getByTestId('channel-confirmed-open')).toHaveCount(0);
      });

  }

  test('manager admits only a current Workspace recipient with CSRF and accessible dialog', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('coglatas.locale', 'en'));
    const backend = await installAuthorizedDirectory(page);
    await page.route(`**/api/workspaces/${workspaceId}/members`, (route) =>
      route.fulfill({ json: [
        { userId: recipientId, displayName: 'Council participant', status: 'Active' },
        { userId: '18000000-0000-4000-8000-000000000001',
          displayName: 'Suspended participant', status: 'Suspended' }
      ] }));
    await page.goto('/app/teams');
    const directory = page.getByTestId('workspace-channel-directory');
    await expect(directory).toBeVisible();
    await directory.getByTestId('channel-create-open').click();
    const createDialog = page.getByTestId('channel-create-dialog');
    await createDialog.getByTestId('channel-create-name').fill('Council private');
    await createDialog.getByTestId('channel-create-visibility').selectOption('Private');
    await createDialog.getByTestId('channel-create-submit').click();
    await expect(createDialog).toHaveCount(0);

    const channel = directory.locator(`[data-channel-id="${privateId}"]`);
    await expect(channel).toContainText('Council private');
    await channel.getByTestId('channel-invite-open').click();
    const invite = page.getByTestId('channel-invite-dialog');
    await expect(invite).toBeVisible();
    await expect(invite).toHaveAttribute('role', 'dialog');
    await expect(invite.getByTestId('group-recipient-option')).toHaveCount(1);
    await expect(invite).not.toContainText('Suspended participant');
    await expect(invite.getByTestId('channel-invite-submit')).toBeDisabled();
    await invite.getByTestId('group-recipient-option').click();
    await expect(invite.getByTestId('channel-invite-submit')).toBeEnabled();
    await expectNoAccessibilityViolations(page, '[data-testid="channel-invite-dialog"]');
    await invite.getByTestId('channel-invite-submit').click();
    await expect(invite).toContainText('The server confirmed membership.');
    expect(backend.invites).toEqual([recipientId]);
    await expect(invite.getByTestId('channel-invite-submit')).toBeDisabled();
    await invite.getByRole('button', { name: 'Close' }).click();
    await expect(invite).toHaveCount(0);

    backend.setDenied(true);
    await directory.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(directory.getByTestId('channel-directory-denied')).toBeVisible();
    await expect(directory.getByTestId('channel-invite-open')).toHaveCount(0);
    expect(backend.invites).toEqual([recipientId]);
  });
});
