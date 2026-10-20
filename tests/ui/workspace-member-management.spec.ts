import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

const WORKSPACE = '00000000-0000-0000-0000-000000000101';
const MEMBER = '00000000-0000-0000-0000-000000000201';
const CANDIDATE = '00000000-0000-0000-0000-000000000202';
const OTHER_CANDIDATE = '00000000-0000-0000-0000-000000000203';
const BASE = `/api/workspaces/${WORKSPACE}/members`;

// Static frontend diagnostics only. Real HTTP/PostgreSQL correspondence has a
// separate owner and is not established by these response fixtures.
async function installManagementApi(page: Page) {
  const workspace = { id: WORKSPACE, name: 'Student Council', status: 0, currentUserRole: 0,
    accessSource: 'WorkspaceMembership', canOpenWorkspace: true, canOpenMembers: true, canOpenProjects: true };
  const user = { userId: '00000000-0000-0000-0000-000000000301', displayName: 'Synthetic manager',
    email: 'synthetic-manager@example.test', systemRole: 0, status: 0,
    capabilities: ['workspace:view', 'announcements:view', 'projects:view', 'files:view', 'account:view'],
    currentWorkspace: workspace, workspaces: [workspace] };
  const state = { canManage: true, writeError: 0, role: 3, status: 1, added: false,
    searches: [] as string[], writes: [] as { method: string; path: string; body: unknown }[] };
  await page.addInitScript(() => localStorage.setItem('coglatas.locale', 'en'));
  await page.route('**/api/**', (route) => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/me') return json(user);
    if (path === '/api/auth/status') return json({ isAuthenticated: true, user });
    if (path === '/api/workspaces') return json([workspace]);
    if (path === `/api/workspaces/${WORKSPACE}`) return json(workspace);
    if (path === '/api/security/csrf-token') return json({ token: 'synthetic-ui-csrf', headerName: 'X-CSRF-Token' });
    if (path === BASE + '/management/options') {
      if (!state.canManage) return json({ error: 'Unavailable', displayName: 'Denied response name' }, 403);
      return json({ workspaceId: WORKSPACE, canAddMember: true, target: url.searchParams.has('userId') ? {
        userId: MEMBER, currentRole: state.role, membershipStatus: state.status,
        canChangeRole: true, allowedRoles: [0, 1, 2, 3, 4], canRemove: true,
      } : null });
    }
    if (path === BASE + '/management/candidates') {
      state.searches.push(url.searchParams.get('q') ?? '');
      return json({ workspaceId: WORKSPACE, items: [
        { userId: CANDIDATE, displayName: 'Same name', allowedRoles: [3, 4] },
        { userId: OTHER_CANDIDATE, displayName: 'Same name', allowedRoles: [4] },
      ], hasMore: false, nextAfterUserId: null });
    }
    if (path === BASE && request.method() === 'GET') return json([
      { userId: MEMBER, displayName: 'Council member', role: state.role, status: state.status },
      ...(state.added ? [{ userId: CANDIDATE, displayName: 'Same name', role: 3, status: 1 }] : []),
    ]);
    if (path === BASE + '/' + MEMBER && request.method() === 'GET') return json({ userId: MEMBER, displayName: 'Council member', role: state.role, status: state.status });
    if ((path === BASE || path === BASE + '/' + MEMBER) && request.method() !== 'GET') {
      const body = request.method() === 'DELETE' ? null : request.postDataJSON();
      state.writes.push({ method: request.method(), path, body });
      if (state.writeError) return json({ error: 'Unconfirmed synthetic response' }, state.writeError);
      if (request.method() === 'POST') { state.added = true; return json({ userId: CANDIDATE, displayName: 'Same name', role: 3, status: 1 }); }
      if (request.method() === 'PATCH') { state.role = body.role; return json({ userId: MEMBER, displayName: 'Council member', role: state.role, status: state.status }); }
      state.status = 2; return json({ status: 'OK' });
    }
    return route.fallback();
  });
  return state;
}

async function openAdd(page: Page) {
  await page.goto(`/app/workspaces/${WORKSPACE}/members`);
  await page.getByTestId('workspace-member-add').click();
  const dialog = page.getByRole('dialog', { name: 'Add member', exact: true });
  await expect(dialog).toBeVisible();
  return dialog;
}

test.describe('Workspace member management presentation', () => {
  for (const width of [1536, 1366, 768, 390, 320]) {
    test(`keeps server-owned member controls and identity confirmation usable at ${width}px`, async ({ page }, testInfo) => {
      const state = await installManagementApi(page);
      await page.setViewportSize({ width, height: 1024 });
      await page.goto(`/app/workspaces/${WORKSPACE}/members`);
      await expect(page.getByTestId('workspace-member-add')).toBeVisible();
      await page.screenshot({ path: testInfo.outputPath(`member-management-directory-${width}.png`) });
      await page.getByTestId('workspace-member-add').click();
      const dialog = page.getByRole('dialog', { name: 'Add member', exact: true });
      const query = page.getByTestId('workspace-member-candidate-query');
      await query.fill('Sa');
      await query.press('Enter');
      await expect(page.getByTestId('workspace-member-candidate')).toHaveCount(2);
      const selected = page.getByTestId('workspace-member-candidate').filter({ hasText: CANDIDATE });
      await selected.click();
      await expect(dialog.getByTestId('workspace-member-selected-identity')).toContainText(CANDIDATE);
      const role = page.getByTestId('workspace-member-selected-role');
      await expect(role.locator('option')).toHaveText(['Choose a role', 'Member', 'Read only']);
      await expect(dialog.getByRole('button', { name: 'Add member', exact: true })).toBeDisabled();
      await role.selectOption({ label: 'Member' });
      await page.getByTestId('workspace-member-confirm-identity').check();
      await expect(dialog.getByRole('button', { name: 'Add member', exact: true })).toBeEnabled();
      const bounds = await dialog.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width + 1);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await expectNoAccessibilityViolations(page, '[role="dialog"]');
      const action = await dialog.getByRole('button', { name: 'Add member', exact: true }).boundingBox();
      expect(action!.y).toBeGreaterThanOrEqual(bounds!.y);
      expect(action!.y + action!.height).toBeLessThanOrEqual(bounds!.y + bounds!.height);
      await page.screenshot({ path: testInfo.outputPath(`member-management-add-${width}.png`) });
      await query.focus();
      for (let index = 0; index < 8; index++) {
        await page.keyboard.press('Tab');
        expect(await dialog.evaluate((element) => element.contains(document.activeElement))).toBe(true);
      }
      await page.keyboard.press('Escape');
      await expect(dialog).toHaveCount(0);
      await expect(page.getByTestId('workspace-member-add')).toBeFocused();
      expect(state.writes).toEqual([]);
    });
  }

  test('requires explicit identity and role before adding once and refreshes current rows', async ({ page }) => {
    const state = await installManagementApi(page);
    const dialog = await openAdd(page);
    const query = page.getByTestId('workspace-member-candidate-query');
    await query.fill('Sa');
    await query.dispatchEvent('keydown', { key: 'Enter', isComposing: true });
    await query.dispatchEvent('keydown', { key: 'Enter', keyCode: 229 });
    expect(state.searches).toEqual([]);
    await query.press('Enter');
    await page.getByTestId('workspace-member-candidate').filter({ hasText: CANDIDATE }).click();
    await page.getByTestId('workspace-member-selected-role').selectOption({ label: 'Member' });
    await page.getByTestId('workspace-member-confirm-identity').check();
    await dialog.getByRole('button', { name: 'Add member', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByTestId('workspace-members-page')).toContainText('Same name');
    expect(state.writes).toEqual([{ method: 'POST', path: BASE, body: { userId: CANDIDATE, role: 3 } }]);
  });

  test('uses the existing role-only patch and confirmed removal without inventing an Owner guard', async ({ page }) => {
    const state = await installManagementApi(page);
    await page.goto(`/app/workspaces/${WORKSPACE}/members`);
    await page.getByRole('button', { name: 'Member details', exact: true }).click();
    await page.getByTestId('workspace-member-change-role').click();
    const change = page.getByRole('dialog', { name: 'Change role', exact: true });
    await page.getByTestId('workspace-member-selected-role').selectOption({ label: 'Owner' });
    await page.getByTestId('workspace-member-confirm-identity').check();
    await change.getByRole('button', { name: 'Change role', exact: true }).click();
    await expect(change).toHaveCount(0);
    await page.getByRole('button', { name: 'Member details', exact: true }).click();
    await expect(page.getByTestId('workspace-member-detail-role')).toHaveText('Owner');
    await page.getByTestId('workspace-member-remove').click();
    const remove = page.getByRole('dialog', { name: 'Remove access', exact: true });
    await page.getByTestId('workspace-member-confirm-identity').check();
    await remove.getByRole('button', { name: 'Remove access', exact: true }).click();
    await expect(remove).toHaveCount(0);
    expect(state.writes).toEqual([
      { method: 'PATCH', path: BASE + '/' + MEMBER, body: { role: 0 } },
      { method: 'DELETE', path: BASE + '/' + MEMBER, body: null },
    ]);
  });

  test('keeps an uncertain write private and recovers with reads without repeating it at 320px', async ({ page }, testInfo) => {
    const state = await installManagementApi(page); state.writeError = 503;
    await page.setViewportSize({ width: 320, height: 420 });
    const dialog = await openAdd(page);
    await page.getByTestId('workspace-member-candidate-query').fill('Sa');
    await page.getByTestId('workspace-member-candidate-search').click();
    await page.getByTestId('workspace-member-candidate').filter({ hasText: CANDIDATE }).click();
    await page.getByTestId('workspace-member-selected-role').selectOption({ label: 'Member' });
    await page.getByTestId('workspace-member-confirm-identity').check();
    await dialog.getByRole('button', { name: 'Add member', exact: true }).click();
    await expect(page.getByTestId('workspace-member-management-error')).toContainText('The result is not confirmed');
    await expect(dialog).not.toContainText('Same name');
    await expect(dialog.getByRole('button', { name: 'Add member', exact: true })).toBeDisabled();
    await page.screenshot({ path: testInfo.outputPath('member-management-uncertain-320x420.png') });
    await dialog.getByRole('button', { name: 'Refresh current access', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    expect(state.writes).toHaveLength(1);
  });

  test('keeps confirmation visible while long member content scrolls on a short mobile viewport', async ({ page }, testInfo) => {
    const state = await installManagementApi(page);
    await page.setViewportSize({ width: 320, height: 420 });
    const dialog = await openAdd(page);
    await page.getByTestId('workspace-member-candidate-query').fill('Sa');
    await page.getByTestId('workspace-member-candidate-search').click();
    await page.getByTestId('workspace-member-candidate').filter({ hasText: CANDIDATE }).click();
    await page.getByTestId('workspace-member-selected-role').selectOption({ label: 'Member' });
    await page.getByTestId('workspace-member-confirm-identity').check();
    const action = dialog.getByRole('button', { name: 'Add member', exact: true });
    await expect(action).toBeEnabled();
    const bounds = await action.boundingBox();
    expect(bounds!.y).toBeGreaterThanOrEqual(0);
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(420);
    expect(await action.evaluate((element) => {
      const rect = element.getBoundingClientRect();
      return element.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2));
    })).toBe(true);
    await expectNoAccessibilityViolations(page, '[role="dialog"]');
    await page.screenshot({ path: testInfo.outputPath('member-management-selected-320x420.png') });
    await action.click();
    await expect(dialog).toHaveCount(0);
    expect(state.writes).toHaveLength(1);
  });

  test('does not present management authority or denied response identities to an ordinary reader', async ({ page }) => {
    const state = await installManagementApi(page); state.canManage = false;
    await page.goto(`/app/workspaces/${WORKSPACE}/members`);
    await expect(page.getByRole('button', { name: 'Member details', exact: true })).toBeVisible();
    await expect(page.getByTestId('workspace-member-add')).toHaveCount(0);
    await expect(page.getByTestId('workspace-members-page')).not.toContainText('Denied response name');
    expect(state.writes).toEqual([]);
  });
});
