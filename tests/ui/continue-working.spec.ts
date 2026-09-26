import { expect, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = '33000000-0000-4000-8000-000000000001';
const projectId = '33000000-0000-4000-8000-000000000002';
const fileId = '33000000-0000-4000-8000-000000000003';
const grantId = '33000000-0000-4000-8000-000000000004';
const historyKey = `coglatas.continue-working.v1:mock-tenant:mock-user-a:${workspaceId}`;

test.describe('Continue working', () => {
  test('reauthorizes opaque Research and File history and remains accessible at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    const apiRequests: string[] = [];
    const downloadToken = 'playwright-download-token-must-not-persist';
    const workspace = {
      id: workspaceId,
      name: 'Continue Working Workspace',
      description: 'Authorized Playwright workspace',
      icon: null,
      status: 'Active',
      createdAt: '2026-08-20T00:00:00Z',
      updatedAt: '2026-08-27T00:00:00Z',
      currentUserRole: 'Member',
      accessSource: 'WorkspaceMembership',
      canOpenWorkspace: true,
      canOpenMembers: false,
      canOpenProjects: true,
      canOpenProjectCreate: true,
      canCreateProject: true,
      canAddFiles: false,
      unreadAnnouncementCount: 0,
      unreadConversationCount: 0,
      inProgressProjectCount: 1,
      runningProjectCount: 1,
      needsReviewProjectCount: 0,
    };

    await page.addInitScript(({ key, projectResourceId, fileResourceId }) => {
      if (globalThis.sessionStorage.getItem('continue-working-disable-seed') === '1') {
        return;
      }
      globalThis.localStorage.setItem(key, JSON.stringify({
        version: 1,
        items: [
          { kind: 'file', resourceId: fileResourceId, lastOpenedUtc: '2026-08-28T01:00:00.000Z' },
          { kind: 'project', resourceId: projectResourceId, lastOpenedUtc: '2026-08-28T00:00:00.000Z' },
        ],
      }));
    }, { key: historyKey, projectResourceId: projectId, fileResourceId: fileId });

    await page.route('**/api/**', async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      apiRequests.push(`${request.method()} ${path}`);

      if (path === '/api/auth/me') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            userId: 'mock-user-a',
            displayName: 'Mock User A',
            email: 'mock-user-a@example.invalid',
            systemRole: 'TenantUser',
            status: 'Active',
            capabilities: ['workspace:view', 'projects:view', 'files:view'],
            currentWorkspace: workspace,
            workspaces: [workspace],
          }),
        });
        return;
      }
      if (path === '/api/tenants/current') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            tenantId: 'mock-tenant',
            tenantSlug: 'mock',
            isAvailable: true,
            isPlatformScope: false,
            displayName: 'Mock Tenant',
            status: 'Active',
            currentUserRole: 'Admin',
            appMode: 'OnPremSingleTenant',
            allowTenantSwitching: false,
          }),
        });
        return;
      }
      if (path === '/api/workspaces') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify([workspace]),
        });
        return;
      }
      if (path === '/api/workspaces/capabilities') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ requestId: 'continue-working-capabilities', data: { canCreate: false }, warnings: [] }),
        });
        return;
      }
      if (path === '/api/security/csrf-token') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ token: 'continue-working-csrf', headerName: 'X-CSRF-Token' }),
        });
        return;
      }
      if (path === '/api/projects' && request.method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false }),
        });
        return;
      }
      if (path === `/api/projects/${projectId}` && request.method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            id: projectId,
            workspaceId,
            title: 'Server-authorized Research',
            status: 'Active',
            createdAt: '2026-08-20T00:00:00Z',
            updatedAt: '2026-08-27T12:00:00Z',
          }),
        });
        return;
      }
      if (path === `/api/files/${fileId}` && request.method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            id: fileId,
            workspaceId,
            originalFileName: '[redacted:file]',
            status: 'Active',
            createdAt: '2026-08-21T00:00:00Z',
            updatedAt: '2026-08-27T11:00:00Z',
            deletedAt: null,
          }),
        });
        return;
      }
      if (path === `/api/files/${fileId}/download-grants` && request.method() === 'POST') {
        expect(request.postDataJSON()).toEqual({ purpose: 'continue-working-download' });
        expect(request.headers()['x-csrf-token']).toBe('continue-working-csrf');
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            fileDownloadGrantId: grantId,
            fileObjectId: fileId,
            expiresAt: '2026-08-28T12:00:00Z',
            token: downloadToken,
          }),
        });
        return;
      }
      if (path === `/api/file-download-grants/${grantId}/download` && request.method() === 'POST') {
        expect(request.postDataJSON()).toEqual({ token: downloadToken });
        expect(request.headers()['x-csrf-token']).toBe('continue-working-csrf');
        await route.fulfill({
          status: 200,
          contentType: 'application/octet-stream',
          headers: { 'Content-Disposition': 'attachment; filename="evidence.txt"' },
          body: 'download evidence',
        });
        return;
      }

      await route.fallback();
    });

    await page.goto(`/app/workspaces/${workspaceId}/projects`);
    const panel = page.getByTestId('continue-working');
    await expect(panel).toBeVisible();
    await expect(page.getByTestId('continue-working-item')).toHaveCount(2);
    await expect(panel).toContainText('Server-authorized Research');
    await expect(panel).toContainText('Research');
    await expect(panel).toContainText('Running');
    await expect(panel).toContainText('File');
    await expect(panel).toContainText('Ready');
    await expect(page.getByTestId('continue-working-project-link')).toHaveAttribute('href', `/app/projects/${projectId}`);
    await expect(page.locator(`[data-testid="continue-working-item"][data-kind="file"] a`)).toHaveCount(0);
    expect(apiRequests).toContain(`GET /api/projects/${projectId}`);
    expect(apiRequests).toContain(`GET /api/files/${fileId}`);
    expect(apiRequests.some((request) => request.includes('/api/tasks'))).toBe(false);

    const panelBox = await panel.boundingBox();
    expect(panelBox).not.toBeNull();
    expect(panelBox!.x).toBeGreaterThanOrEqual(0);
    expect(panelBox!.x + panelBox!.width).toBeLessThanOrEqual(320);
    expect(await panel.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
    await expectNoAccessibilityViolations(page, '[data-testid="continue-working"]');

    const download = page.waitForEvent('download');
    await page.getByTestId('continue-working-download').click();
    expect((await download).suggestedFilename()).toBe('evidence.txt');
    await expect(panel).toContainText('Download started.');

    const stored = await page.evaluate((key) => globalThis.localStorage.getItem(key), historyKey);
    expect(stored).not.toBeNull();
    expect(stored).not.toContain('Server-authorized Research');
    expect(stored).not.toContain(downloadToken);
    expect(JSON.parse(stored!)).toEqual({
      version: 1,
      items: expect.arrayContaining([
        expect.objectContaining({ kind: 'project', resourceId: projectId }),
        expect.objectContaining({ kind: 'file', resourceId: fileId }),
      ]),
    });
    for (const item of JSON.parse(stored!).items as Record<string, unknown>[]) {
      expect(Object.keys(item).sort()).toEqual(['kind', 'lastOpenedUtc', 'resourceId']);
    }

    await page.evaluate((key) => {
      globalThis.sessionStorage.setItem('continue-working-disable-seed', '1');
      globalThis.localStorage.removeItem(key);
    }, historyKey);
    await page.reload();
    await expect(page.getByTestId('continue-working-empty')).toBeVisible();
    await expect(page.getByRole('link', { name: 'New Research' })).toHaveAttribute(
      'href',
      `/app/workspaces/${workspaceId}/research/new`,
    );
    await expect(page.getByRole('link', { name: 'Browse Files' })).toHaveAttribute(
      'href',
      `/app/workspaces/${workspaceId}/files`,
    );
    await expectNoAccessibilityViolations(page, '[data-testid="continue-working"]');
  });
});
