import { expect, type Page, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

const WORKSPACE_ID = '35600000-0000-4000-8000-000000000001';
const FILE_OBJECT_ID = '35600000-0000-4000-8000-000000000002';
const FILE_ROW_ID = '35600000-0000-4000-8000-000000000003';

test.describe('Issue #363 File Activity and version history Angular smoke', () => {
  test('keeps the File inspector accessible while loading only authorized Activity', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });

    const activityRequests: string[] = [];
    const prohibitedRequests: string[] = [];
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (path === `/api/files/${FILE_OBJECT_ID}/activity`) {
        activityRequests.push(path);
      }
      if (/\/audit(?:\/|$)|\/versions\//i.test(path)) {
        prohibitedRequests.push(path);
      }
    });

    await installFileInspectorApi(page);
    await page.goto('/app/files');

    const previewAction = page.getByRole('button', { name: 'Preview inspector-evidence.zip' });
    await expect(previewAction).toBeVisible();
    await previewAction.focus();
    await page.keyboard.press('Enter');

    const inspector = page.getByTestId('files-preview-pane');
    await expect(inspector).toHaveAttribute('role', 'dialog');
    const previewTab = inspector.getByRole('tab', { name: 'Preview' });
    const detailsTab = inspector.getByRole('tab', { name: 'Details' });
    const activityTab = inspector.getByRole('tab', { name: 'Activity' });
    await expect(inspector.getByRole('tab')).toHaveCount(3);
    await expect(previewTab).toHaveAttribute('aria-selected', 'true');
    await expect(inspector.getByTestId('files-inspector-panel-preview')).toBeVisible();

    await detailsTab.focus();
    await page.keyboard.press('ArrowRight');
    await expect(activityTab).toBeFocused();
    await expect(activityTab).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('Home');
    await expect(previewTab).toBeFocused();
    await expect(previewTab).toHaveAttribute('aria-selected', 'true');

    await detailsTab.click();
    const details = inspector.getByTestId('files-inspector-panel-details');
    await expect(details).toContainText('Essential metadata');
    for (const label of ['Type', 'Size', 'Owner', 'Modified', 'Location', 'Access']) {
      await expect(details.getByText(label, { exact: true })).toBeVisible();
    }
    await expect(details.getByRole('textbox')).toHaveCount(0);
    const moreDetails = details.getByTestId('files-inspector-more-details');
    expect(await moreDetails.getAttribute('open')).toBeNull();
    await moreDetails.getByText('More metadata', { exact: true }).click();
    await expect(moreDetails).toHaveAttribute('open', '');
    await expect(moreDetails).toContainText(FILE_OBJECT_ID);

    await activityTab.click();
    const activityPanel = inspector.getByTestId('files-inspector-panel-activity');
    await expect(activityPanel).toContainText('No file activity or version history is available');
    await expect.poll(() => activityRequests.length).toBeGreaterThan(0);
    expect(activityRequests.every((path) => path === `/api/files/${FILE_OBJECT_ID}/activity`)).toBe(true);
    expect(prohibitedRequests).toEqual([]);

    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="files-preview-pane"]');
    await expectHealthyAngularPage(page);

    await inspector.getByTestId('files-preview-close').click();
    await expect(previewAction).toBeFocused();
  });
});

test.describe('Files folder reliability mock browser regression', () => {
  for (const width of [960, 1366]) {
    test(`distinguishes failure, retries the current Workspace and keeps folder keys independent at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFileInspectorApi(page);
      const parentId = '35600000-0000-4000-8000-000000000004';
      const childId = '35600000-0000-4000-8000-000000000005';
      const folderQueries: string[] = [];
      const mutations: string[] = [];
      let releaseFirst: () => void = () => undefined;
      await page.route('**/api/file-folders**', async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        if (request.method() !== 'GET' || url.pathname !== '/api/file-folders') {
          mutations.push(`${request.method()} ${url.pathname}`);
          await route.fulfill({ status: 405 });
          return;
        }
        folderQueries.push(url.searchParams.get('workspaceId') ?? '');
        if (folderQueries.length === 1) {
          await new Promise<void>((resolve) => { releaseFirst = resolve; });
          await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ error: 'QA internal failure must stay hidden' }) });
          return;
        }
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
          workspaceId: WORKSPACE_ID, rootVersion: 5, folders: [
            { id: parentId, workspaceId: WORKSPACE_ID, parentFolderId: null, name: 'QA parent folder', sortOrder: 0, version: 2 },
            { id: childId, workspaceId: WORKSPACE_ID, parentFolderId: parentId, name: 'QA child folder', sortOrder: 0, version: 3 }
          ]
        }) });
      });
      await page.goto('/app/files');
      const sidebar = page.locator('app-file-browser-sidebar');
      await expect(sidebar.getByRole('status')).toContainText('Loading folders');
      await expect(sidebar.getByText('No folders', { exact: false })).toHaveCount(0);
      releaseFirst();
      await expect(sidebar.getByRole('alert')).toContainText('Folders could not be loaded');
      await expect(page.getByText('QA internal failure must stay hidden')).toHaveCount(0);
      await sidebar.getByRole('button', { name: 'Retry', exact: true }).click();
      const parent = sidebar.locator(`[role="treeitem"][data-folder-id="${parentId}"]`);
      const child = sidebar.locator(`[role="treeitem"][data-folder-id="${childId}"]`);
      await expect(parent).toHaveAttribute('aria-expanded', 'true');
      await expect(child).toBeVisible();
      await parent.focus();
      await parent.press('ArrowLeft');
      await expect(parent).toHaveAttribute('aria-expanded', 'false');
      await expect(child).toHaveCount(0);
      await parent.press('ArrowRight');
      await expect(parent).toHaveAttribute('aria-expanded', 'true');
      await parent.press('ArrowDown');
      await expect(child).toBeFocused();
      await child.getByRole('button', { name: 'Move: QA child folder', exact: true }).focus();
      await page.keyboard.press('Enter');
      const moveDialog = page.getByRole('dialog', { name: 'Move', exact: true });
      await expect(moveDialog).toBeVisible();
      await expect(moveDialog.getByTestId('files-move-destination')).toBeVisible();
      await moveDialog.getByRole('button', { name: 'Cancel', exact: true }).click();
      expect(folderQueries).toEqual([WORKSPACE_ID, WORKSPACE_ID]);
      expect(mutations).toEqual([]);
      await expectNoDocumentHorizontalOverflow(page);
      await expectNoAccessibilityViolations(page, 'app-file-browser-sidebar');
    });
  }
});

async function installFileInspectorApi(page: Page): Promise<void> {
  const workspace = {
    id: WORKSPACE_ID,
    name: 'Inspector Workspace',
    currentUserRole: 'Member',
    canAddFiles: true,
    runningProjectCount: 0,
    needsReviewProjectCount: 0,
  };

  await page.route('**/api/auth/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({
        userId: 'mock-user-a',
        displayName: 'Mock User A',
        email: 'mock-user-a@example.invalid',
        systemRole: 'TenantUser',
        status: 'Active',
        capabilities: ['workspace:view', 'projects:view', 'files:view', 'account:view'],
        currentWorkspace: workspace,
        workspaces: [workspace],
      }),
    });
  });

  await page.route('**/api/workspaces/capabilities', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({
        requestId: 'issue-363-workspace-capabilities',
        data: { canCreate: false },
        warnings: [],
      }),
    });
  });

  await page.route('**/api/workspaces', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fulfill({ status: 405 });
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify([{
        ...workspace,
        description: 'Issue #363 Playwright fixture',
        icon: null,
        status: 'Active',
        createdAt: '2026-07-06T00:00:00Z',
        updatedAt: '2026-07-06T00:00:00Z',
        accessSource: 'WorkspaceMembership',
        canOpenWorkspace: true,
        canOpenMembers: true,
        canOpenProjects: true,
        canOpenProjectCreate: false,
        canCreateProject: false,
        unreadAnnouncementCount: 0,
        unreadConversationCount: 0,
        inProgressProjectCount: 0,
      }]),
    });
  });

  await page.route('**/api/security/csrf-token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({ token: 'csrf-issue-363', headerName: 'X-CSRF-Token' }),
    });
  });

  await page.route('**/api/files**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    if (request.method() === 'GET' && url.pathname === `/api/files/${FILE_OBJECT_ID}/activity`) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ fileObjectId: FILE_OBJECT_ID, items: [] }),
      });
      return;
    }

    if (request.method() !== 'GET' || url.pathname !== '/api/files') {
      await route.fulfill({ status: 404 });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({
        items: [{
          id: FILE_ROW_ID,
          fileObjectId: FILE_OBJECT_ID,
          workspaceId: WORKSPACE_ID,
          originalFileName: 'inspector-evidence.zip',
          contentType: 'application/zip',
          sizeBytes: 4096,
          status: 'Active',
          scanStatus: 'Clean',
          uploadedByUserId: 'mock-user-a',
          uploadedByDisplayName: 'Mock User A',
          createdAt: '2026-08-28T02:00:00Z',
          updatedAt: '2026-08-29T03:30:00Z',
          canDelete: false,
        }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        hasMore: false,
      }),
    });
  });
}

async function expectHealthyAngularPage(page: Page): Promise<void> {
  const body = page.locator('body');
  await expect(body).not.toContainText('Cannot GET /');
  await expect(body).not.toContainText('Application error');
  await expect(body).not.toContainText(/NG0\d+/);
  await expect(body).not.toContainText('TypeError');
  await expect(page.locator('app-root')).toBeAttached();
}

async function expectNoDocumentHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => {
    const documentElement = document.documentElement;
    const body = document.body;
    return {
      bodyScrollWidth: body.scrollWidth,
      documentScrollWidth: documentElement.scrollWidth,
      viewportWidth: documentElement.clientWidth,
    };
  });

  expect(overflow.documentScrollWidth).toBeLessThanOrEqual(overflow.viewportWidth);
  expect(overflow.bodyScrollWidth).toBeLessThanOrEqual(overflow.viewportWidth);
}
