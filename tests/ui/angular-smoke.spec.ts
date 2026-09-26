import { expect, type Locator, type Page, type Request, type TestInfo, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

const coreResponsiveRoutes = [
  '/app/workspaces',
  '/app/workspaces/static-workspace-1/members',
  '/app/announcements',
  '/app/workspaces/static-workspace-1/channels/static-conversation-main',
  '/app/dm/static-dm-1',
  '/app/files',
  '/app/projects',
  '/app/tasks',
  '/app/admin/audit',
  '/app/admin/export-diagnostics',
  '/app/account',
  '/app/register/invite'
];

const themeStorageKey = 'coglatas.ui.theme.v1';

const workspacePreferenceKey = (tenantId: string, userId: string) =>
  `coglatas.workspace.last-used:${encodeURIComponent(tenantId)}:${encodeURIComponent(userId)}`;

const approvedThemeMigrationDiffRatio = {
  desktop: 0.055,
  mobile: 0.002
} as const;

test.describe('MVP-A P0 Angular frontend smoke', () => {
  test('serves the built Angular shell', async ({ page }) => {
    await page.goto('/');

    await expect(page.locator('app-root')).toBeVisible();
    await expect(page.getByTestId('app-shell')).toBeVisible();
    await expect(page.getByTestId('shell-body')).toBeVisible();
    await expect(page.getByTestId('top-bar-region')).toBeVisible();
    await expect(page.locator('router-outlet').first()).toBeAttached();
    await expect(page.locator('app-shell router-outlet')).toBeAttached();

    await expectHealthyAngularPage(page);
    await expectNoAccessibilityViolations(page);
  });

  test('renders the Angular login/session placeholder route', async ({ page }) => {
    await page.goto('/app/login');

    await expect(page.locator('app-root')).toBeVisible();
    await expect(page.getByTestId('page-placeholder')).toBeVisible();
    await expect(page.getByTestId('page-placeholder')).toHaveAttribute('data-tone', 'public');
    await expect(page.locator('app-shell')).toHaveCount(0);
    await expectHealthyAngularPage(page);
  });

  test('renders the workspace route in the Angular shell', async ({ page }) => {
    await page.goto('/app/workspaces');

    await waitForWorkspaceShellReady(page);
    await expect(page.locator('a[href="/app/workspaces"]').first()).toBeAttached();
    await expect(page.getByTestId('workspace-switcher')).toHaveValue('static-workspace-1');
    await expect(page.getByTestId('workspace-research-status')).toContainText('2 Running');
    await expect(page.getByTestId('workspace-research-status')).toContainText('1 Needs review');
    await expect(page.getByRole('navigation', { name: 'Workspace actions' })).toContainText('Members');
    await expect(page.getByRole('navigation', { name: 'Global actions' })).toContainText('Notifications');
    await expectHealthyAngularPage(page);
  });

  test('saves and reapplies My Tasks filters accessibly without exposing an opaque Project ID at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const projectId = '34600000-0000-4000-8000-000000000001';
    let denySavedProject = false;
    let responseGate: Promise<void> | null = null;
    let releaseResponseGate: (() => void) | null = null;
    let gatedRequestCount = 0;
    await page.route('**/api/me/tasks**', async (route) => {
      const url = new URL(route.request().url());
      const activeGate = responseGate;
      if (activeGate) {
        gatedRequestCount += 1;
        await activeGate;
      }
      if (url.pathname === '/api/me/tasks/counts') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            views: [{ view: 'Assigned', count: 1 }, { view: 'Completed', count: 1 }],
            timeGroups: [{ timeGroup: 'Today', count: 1 }]
          })
        });
        return;
      }
      if (denySavedProject && url.searchParams.get('projectId') === projectId) {
        await route.fulfill({
          status: 404,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ error: { code: 'MY_TASKS_PROJECT_NOT_FOUND', message: 'Project is unavailable.' } })
        });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          items: [{
            taskId: '34600000-0000-4000-8000-000000000002',
            tenantId: 'mock-tenant',
            workspaceId: 'static-workspace-1',
            workspaceTitle: 'Static Workspace',
            projectId,
            projectTitle: 'Authorized Project',
            title: 'Saved filter evidence task',
            workflowStageName: 'Done',
            stageCategory: 'Done',
            priority: 'High',
            isBlocked: false,
            progressPercent: 100,
            timeGroup: 'Today',
            isOverdue: false,
            version: 1,
            checklistCompletedCount: 0,
            checklistTotalCount: 0,
            labels: [],
            quickEditPermissions: { canClaim: false, canChangeStage: false },
            warnings: []
          }],
          page: 1,
          pageSize: 50,
          totalCount: 1
        })
      });
    });

    await page.goto('/app/tasks');
    await expect(page.getByTestId('my-tasks-page')).toBeVisible();
    await expect(page.getByTestId('my-tasks-saved-filters')).toBeVisible();

    const completedRequest = page.waitForRequest((request) => {
      const url = new URL(request.url());
      return url.pathname === '/api/me/tasks' && url.searchParams.get('view') === 'completed';
    });
    const completed = page.getByTestId('my-tasks-preset-completed');
    await completed.focus();
    await page.keyboard.press('Enter');
    let requestUrl = new URL((await completedRequest).url());
    expect(requestUrl.searchParams.get('stageCategory')).toBe('done');
    await expect(page.getByTestId('my-tasks-filter-summary')).toContainText('Relationship: Completed');
    await expect(page.getByTestId('my-tasks-filter-summary')).toContainText('Stage: Done');
    await expect(page.getByTestId('my-tasks-results')).not.toHaveAttribute('aria-busy', 'true');
    await expect(completed).toBeFocused();

    const projectRequest = page.waitForRequest((request) => new URL(request.url()).pathname === '/api/me/tasks' && new URL(request.url()).searchParams.get('projectId') === projectId);
    await page.getByTestId('my-tasks-project-filter').fill(projectId);
    await page.getByTestId('my-tasks-project-filter').press('Tab');
    await projectRequest;
    const priorityRequest = page.waitForRequest((request) => new URL(request.url()).pathname === '/api/me/tasks' && new URL(request.url()).searchParams.get('priority') === 'high');
    await page.getByTestId('my-tasks-priority-filter').selectOption('high');
    await priorityRequest;

    const name = page.getByTestId('my-tasks-saved-filter-name');
    await name.fill('Completed evidence');
    await name.press('Enter');
    await expect(page.getByRole('button', { name: 'Apply saved filter Completed evidence' })).toBeVisible();
    await expect(page.getByTestId('my-tasks-filter-announcement')).toContainText('Saved filter Completed evidence');
    const stored = await page.evaluate(() => globalThis.localStorage.getItem('coglatas.work-view.saved-filters.v1:mock-tenant:mock-user-a:my-tasks'));
    expect(stored).toContain(projectId);
    expect(stored).not.toMatch(/Authorized Project|Saved filter evidence task|rows|counts|permissions/iu);

    responseGate = new Promise<void>((resolve) => { releaseResponseGate = resolve; });
    gatedRequestCount = 0;
    await page.reload({ waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('button', { name: 'Apply saved filter Completed evidence' })).toBeVisible();
    await expect(page.getByTestId('my-tasks-results')).toHaveAttribute('aria-busy', 'true');
    await expect.poll(() => gatedRequestCount).toBe(2);
    releaseResponseGate?.();
    responseGate = null;
    await expect(page.getByTestId('my-tasks-results')).not.toHaveAttribute('aria-busy', 'true');
    await expect(page.getByRole('button', { name: 'Apply saved filter Completed evidence' })).toBeVisible();

    const clearRequest = page.waitForRequest((request) => {
      const url = new URL(request.url());
      return url.pathname === '/api/me/tasks' && url.searchParams.get('view') === 'assigned' && !url.searchParams.has('projectId');
    });
    const clear = page.getByTestId('my-tasks-clear-filters');
    await clear.focus();
    await page.keyboard.press('Enter');
    requestUrl = new URL((await clearRequest).url());
    expect(requestUrl.searchParams.get('scope')).toBe('currentWorkspace');
    expect(requestUrl.searchParams.get('workspaceId')).toBe('static-workspace-1');
    await expect(page.getByTestId('my-tasks-results')).not.toHaveAttribute('aria-busy', 'true');
    await expect(clear).toBeFocused();

    denySavedProject = true;
    const applyRequest = page.waitForRequest((request) => {
      const url = new URL(request.url());
      return url.pathname === '/api/me/tasks' && url.searchParams.get('projectId') === projectId && url.searchParams.get('view') === 'completed';
    });
    const deniedResponse = page.waitForResponse((response) => {
      const url = new URL(response.url());
      return url.pathname === '/api/me/tasks' && url.searchParams.get('projectId') === projectId && response.status() === 404;
    });
    const apply = page.getByRole('button', { name: 'Apply saved filter Completed evidence' });
    await apply.focus();
    await page.keyboard.press('Enter');
    requestUrl = new URL((await applyRequest).url());
    expect(requestUrl.searchParams.get('stageCategory')).toBe('done');
    expect(requestUrl.searchParams.get('priority')).toBe('high');
    await deniedResponse;
    await expect(page.getByTestId('my-tasks-load-error')).toBeVisible();
    await expect(page.getByTestId('my-tasks-saved-filters')).toBeVisible();
    await expect(page.getByTestId('my-tasks-filter-summary')).toBeVisible();
    await expect(apply).toBeFocused();
    await expect(page.getByTestId('my-tasks-project-filter')).toHaveValue('');
    await expect(page.getByTestId('my-tasks-project-filter')).toHaveAttribute('placeholder', 'Saved Project condition is active');
    await expect(page.getByTestId('my-tasks-filter-summary')).toContainText('Project filter active');
    await expect(page.locator('body')).not.toContainText(projectId);

    const deleteSaved = page.getByRole('button', { name: 'Delete saved filter Completed evidence' });
    await deleteSaved.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Apply saved filter Completed evidence' })).toHaveCount(0);
    await expect(page.getByTestId('my-tasks-saved-filter-name')).toBeFocused();

    denySavedProject = false;
    const recoveryRequest = page.waitForRequest((request) => {
      const url = new URL(request.url());
      return url.pathname === '/api/me/tasks' && url.searchParams.get('view') === 'assigned' && !url.searchParams.has('projectId');
    });
    await clear.focus();
    await page.keyboard.press('Enter');
    await recoveryRequest;
    await expect(page.getByTestId('my-tasks-load-error')).toHaveCount(0);
    await expect(page.getByTestId('my-tasks-results')).not.toHaveAttribute('aria-busy', 'true');
    await expect(clear).toBeFocused();

    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
    await expectHealthyAngularPage(page);
  });

  test('keeps the redacted audit drawer deep-linkable, focus-safe, and accessible at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const rows = auditGridFixtures(8);
    await installAuditGridApi(page, rows, {
      canViewSensitiveMetadata: true,
      sensitiveMetadata: {
        [rows[0].id]: {
          outcome: 'Allowed',
          change: { category: '<img src=x onerror=alert(1)>' },
        },
      },
      redactionApplied: true,
    });
    let sensitiveMetadataRequests = 0;
    page.on('request', (request) => {
      if (new URL(request.url()).pathname.endsWith('/sensitive-metadata')) {
        sensitiveMetadataRequests += 1;
      }
    });

    const firstAuditId = auditGridFixtureId(0);
    await page.goto(`/app/admin/audit?event=${firstAuditId}`);
    const drawer = page.getByTestId('audit-detail-drawer');
    await expect(drawer).toBeVisible();
    await expect(drawer).toContainText('Audit row 001 was opened with safe fields.');
    await expect(page.getByTestId('audit-sensitive-metadata-toggle')).toBeVisible();
    expect(sensitiveMetadataRequests).toBe(0);
    await page.getByTestId('audit-detail-close').click();
    await expect(page).toHaveURL(/\/app\/admin\/audit$/);
    await expect(page.getByTestId('audit-log-title')).toBeFocused();

    await page.goto('/app/admin/audit');
    const mobileList = page.getByTestId('audit-log-mobile-list');
    const opener = page.getByTestId('open-audit-mobile-detail').first();
    await expect(mobileList).toBeVisible();
    await expect(page.getByTestId('audit-log-status')).toContainText('Showing 8 audit entries.');
    await expect(mobileList).toContainText('Warning');
    await expect(mobileList).toContainText('Critical');
    await expect(mobileList).toContainText('Failed');
    await opener.focus();
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(new RegExp(`/app/admin/audit\\?event=${firstAuditId}$`));
    await expect(drawer).toBeVisible();
    await expect(page.getByTestId('audit-detail-close')).toBeFocused();
    await expect(drawer).toContainText('Audit row 001 was opened with safe fields.');
    await expect(drawer).not.toContainText('restricted body must stay hidden');
    await expect(drawer).not.toContainText('tenant/private/key');
    const disclosure = page.getByTestId('audit-sensitive-metadata-toggle');
    await disclosure.focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => sensitiveMetadataRequests).toBe(1);
    await expect(page.getByTestId('audit-sensitive-metadata-json')).toContainText('Allowed');
    await expect(page.getByTestId('audit-sensitive-metadata-json')).toContainText('<img src=x onerror=alert(1)>');
    await expect(drawer.locator('img')).toHaveCount(0);
    await expect(page.getByTestId('audit-sensitive-metadata-redacted')).toContainText('Prohibited fields were removed by the server.');
    await expect(disclosure).toBeFocused();
    await page.keyboard.press('Space');
    await expect(page.getByTestId('audit-sensitive-metadata-json')).toHaveCount(0);
    await expect(disclosure).toHaveText('Show sensitive metadata');
    await expect(disclosure).toBeFocused();
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await page.goBack();
    await expect(drawer).toHaveCount(0);
    await expect(opener).toBeFocused();

    await page.goForward();
    await expect(drawer).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page).toHaveURL(/\/app\/admin\/audit$/);
    await expect(opener).toBeFocused();
  });

  test('keeps Audit filters, chips, URL state, and saved views keyboard-accessible at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 900 });
    await installAuditGridApi(page, auditGridFixtures(8), { applyListFilters: true });
    await page.goto('/app/admin/audit');

    const search = page.getByTestId('audit-filter-search');
    const severity = page.getByTestId('audit-filter-severity');
    const apply = page.getByTestId('audit-apply-filters');
    await search.fill('Audit row 002');
    await severity.selectOption('warning');
    await apply.focus();
    await page.keyboard.press('Enter');

    await expect.poll(() => {
      const url = new URL(page.url());
      return `${url.searchParams.get('q')}|${url.searchParams.get('severity')}`;
    }).toBe('Audit row 002|warning');
    await expect(page.getByTestId('audit-result-count')).toContainText('1 authorized result');
    await expect(page.getByTestId('audit-log-mobile-list')).toContainText('Audit row 002 was opened with safe fields.');
    await expect(page.getByLabel('Remove filter Search: Audit row 002')).toBeVisible();
    await expect(page.getByLabel('Remove filter Severity: Warning')).toBeVisible();

    const name = page.getByTestId('audit-saved-view-name');
    const save = page.getByTestId('audit-save-view');
    await name.fill('Warning row evidence');
    await save.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('audit-saved-view-select')).toContainText('Warning row evidence');

    const removeSearch = page.getByLabel('Remove filter Search: Audit row 002');
    await removeSearch.focus();
    await page.keyboard.press('Enter');
    await expect(removeSearch).toHaveCount(0);
    await expect.poll(() => new URL(page.url()).searchParams.has('q')).toBe(false);
    await expect(page.getByTestId('audit-result-count')).toContainText('3 authorized results');

    await page.getByTestId('audit-clear-all').focus();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/app\/admin\/audit$/);
    await expect(page.getByTestId('audit-result-count')).toContainText('8 authorized results');

    await page.getByTestId('audit-saved-view-select').selectOption({ label: 'Warning row evidence' });
    const applySaved = page.getByTestId('audit-apply-saved-view');
    await applySaved.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('audit-result-count')).toContainText('1 authorized result');
    await expect.poll(() => new URL(page.url()).searchParams.get('q')).toBe('Audit row 002');

    await page.reload();
    await expect(page.getByTestId('audit-result-count')).toContainText('1 authorized result');
    await expect(page.getByTestId('audit-saved-view-select')).toContainText('Warning row evidence');
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
    await expectHealthyAngularPage(page);
  });

  test('keeps the audit header fixed and restores bounded AG scroll context after drawer close', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-desktop', 'The desktop grid is replaced by the mobile audit list.');
    await installAuditGridApi(page, auditGridFixtures(128));
    await page.goto('/app/admin/audit');

    const grid = page.locator('ag-grid-angular.app-data-grid__grid--sticky-header');
    const header = grid.locator('.ag-header');
    // AG Grid v36 owns the normal-layout scroll on ag-grid-viewport; keep the
    // older class alternatives for the retained adapter's compatible builds.
    const bodyViewport = grid.locator('.ag-grid-viewport, .ag-center-cols-viewport, .ag-body-viewport').first();
    await expect(grid).toBeVisible();
    await expect(header).toBeVisible();
    await expect(bodyViewport).toBeVisible();
    await expect(grid.locator('.ag-header-cell-text')).toHaveText([
      'Created',
      'Action',
      'Actor',
      'Target',
      'Severity',
      'Result',
      'Summary',
    ]);
    for (const name of ['Created', 'Action', 'Actor', 'Target', 'Severity', 'Result', 'Summary']) {
      await expect(grid.getByRole('columnheader', { name })).toBeVisible();
    }
    const before = await header.boundingBox();
    await bodyViewport.evaluate((element) => { element.scrollTop = 500; });

    await expect.poll(async () => (await header.boundingBox())?.y).toBeCloseTo(before?.y ?? 0, 1);

    const openerIndex = await bodyViewport.evaluate((viewport) => {
      const viewportBounds = viewport.getBoundingClientRect();
      return Array.from(viewport.querySelectorAll<HTMLButtonElement>('button[data-grid-action="openAuditDetail"]'))
        .findIndex((button) => {
          const bounds = button.getBoundingClientRect();
          return bounds.top >= viewportBounds.top && bounds.bottom <= viewportBounds.bottom;
        });
    });
    expect(openerIndex).toBeGreaterThanOrEqual(0);
    // Do not let Playwright scroll an offscreen first row back to the top:
    // activate a row already visible in the bounded viewport we are testing.
    const opener = bodyViewport.locator('button[data-grid-action="openAuditDetail"]').nth(openerIndex);
    await expect(opener).toBeVisible();
    const openerBounds = await opener.boundingBox();
    const headerBounds = await header.boundingBox();
    expect(openerBounds?.width ?? 0).toBeGreaterThanOrEqual(24);
    expect(openerBounds?.height ?? 0).toBeGreaterThanOrEqual(24);
    expect(openerBounds?.top ?? 0).toBeGreaterThanOrEqual(headerBounds?.bottom ?? 0);
    const originalScrollTop = await bodyViewport.evaluate((element) => element.scrollTop);
    await opener.click();
    await expect(page.getByTestId('audit-detail-drawer')).toBeVisible();
    await expect(page.getByTestId('audit-detail-close')).toBeFocused();

    // Simulate a user moving the bounded grid while its non-modal inspector
    // is open. Closing must return to the original virtualized row context.
    await bodyViewport.evaluate((element) => { element.scrollTop = 0; });
    await page.getByTestId('audit-detail-close').click();

    await expect.poll(async () => bodyViewport.evaluate((element) => element.scrollTop)).toBeCloseTo(originalScrollTop, 1);
    await expect(opener).toBeFocused();

    await page.getByTestId('audit-density-dense').click();
    const denseOpener = bodyViewport.locator('button[data-grid-action="openAuditDetail"]').nth(openerIndex);
    await expect(denseOpener).toBeVisible();
    const denseOpenerBounds = await denseOpener.boundingBox();
    expect(denseOpenerBounds?.width ?? 0).toBeGreaterThanOrEqual(24);
    expect(denseOpenerBounds?.height ?? 0).toBeGreaterThanOrEqual(24);
  });

  test('keeps the Audit initial-load skeleton and transient Retry path structural, safe, and keyboard-stable', async ({ page }) => {
    const rows = auditGridFixtures(1);
    let listRequests = 0;
    let releaseInitialFailure: (() => void) | undefined;
    let releaseRetrySuccess: (() => void) | undefined;
    const initialFailure = new Promise<void>((resolve) => { releaseInitialFailure = resolve; });
    const retrySuccess = new Promise<void>((resolve) => { releaseRetrySuccess = resolve; });

    await page.route('**/api/admin/audit-grid**', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (request.method() !== 'GET' || url.pathname !== '/api/admin/audit-grid') {
        await route.fulfill({ status: 405 });
        return;
      }

      listRequests += 1;
      if (listRequests === 1) {
        await initialFailure;
        await route.fulfill({
          status: 503,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ error: 'sensitive upstream error body must stay hidden' }),
        });
        return;
      }

      await retrySuccess;
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ items: rows, page: 1, pageSize: 100, totalCount: rows.length }),
      });
    });

    await page.goto('/app/admin/audit');
    const content = page.getByTestId('audit-log-content');
    const status = page.getByTestId('audit-log-status');
    const skeleton = page.getByTestId('audit-log-skeleton');
    await expect(skeleton).toBeVisible();
    await expect(content).toHaveAttribute('aria-busy', 'true');
    await expect(status).toHaveText('Loading audit log.');
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    releaseInitialFailure?.();
    const retry = page.getByTestId('audit-log-retry');
    await expect(retry).toBeVisible();
    await expect(retry).toHaveText('Retry');
    await expect(page.getByText('sensitive upstream error body must stay hidden')).toHaveCount(0);
    await retry.focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => listRequests).toBe(2);
    await expect(retry).toBeFocused();
    await expect(retry).toHaveAttribute('aria-disabled', 'true');
    await expect(retry).toHaveText('Retrying...');
    await expect(status).toHaveText('Retrying audit log.');
    await expect(skeleton).toBeVisible();
    await page.keyboard.press('Enter');
    await expect.poll(() => listRequests).toBe(2);
    await expectNoDocumentHorizontalOverflow(page);

    releaseRetrySuccess?.();
    await expect(page.getByTestId('audit-log-mobile-list')).toContainText('Audit row 001 was opened with safe fields.');
    await expect(status).toContainText('Showing 1 audit entry.');
    await expect(retry).toHaveCount(0);
    await expect(content).not.toHaveAttribute('aria-busy', 'true');
    await expect(page.getByTestId('audit-log-title')).toBeFocused();
  });

  test('keeps the audit detail action keyboard-accessible through the flagged Syncfusion adapter', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-desktop', 'The mobile Audit route uses the audited card list.');
    await page.addInitScript(() => {
      window.__AIP_FEATURE_FLAGS__ = { 'frontend.syncfusionGrid': true };
    });
    const rows = auditGridFixtures(8);
    await installAuditGridApi(page, rows);
    let detailRequests = 0;
    page.on('request', (request) => {
      const url = new URL(request.url());
      if (request.method() === 'GET' && url.pathname === `/api/admin/audit-grid/${rows[0].id}`) {
        detailRequests += 1;
      }
    });
    await page.goto('/app/admin/audit');

    const grid = page.locator('[data-grid-implementation="syncfusion"]');
    const content = grid.locator('.e-gridcontent .e-content');
    const horizontalViewport = grid.locator('xpath=ancestor::section[contains(@class, "app-data-grid")]');
    await expect(grid).toBeVisible();
    for (const name of ['Created', 'Action', 'Actor', 'Target', 'Severity', 'Result', 'Summary']) {
      await expect(grid.getByRole('columnheader', { name })).toBeVisible();
    }
    await horizontalViewport.evaluate((element) => { element.scrollLeft = element.scrollWidth; });
    await expect.poll(() => horizontalViewport.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);

    const action = content.locator(`button[data-grid-action="openAuditDetail"][data-grid-row-id="${rows[0].id}"]`);
    await expect(action).toBeVisible();
    await action.focus();
    await expect(action).toBeFocused();
    const actionBounds = await action.boundingBox();
    expect(actionBounds?.width ?? 0).toBeGreaterThanOrEqual(24);
    expect(actionBounds?.height ?? 0).toBeGreaterThanOrEqual(24);

    await page.keyboard.press('Enter');
    await expect(page.getByTestId('audit-detail-drawer')).toBeVisible();
    expect(detailRequests).toBe(1);
    await expect(page.getByTestId('audit-detail-close')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('audit-detail-drawer')).toHaveCount(0);
    await expect(action).toBeFocused();

    await page.keyboard.press('Space');
    await expect(page.getByTestId('audit-detail-drawer')).toBeVisible();
    expect(detailRequests).toBe(2);
    await page.keyboard.press('Escape');
    await expect(action).toBeFocused();
  });

  test('requires an explicit Workspace choice when multiple authorized Workspaces have no preference', async ({ page }) => {
    const workspaces = workspaceContextFixtures();
    await installWorkspaceContextApi(page, workspaces, null);

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);

    const switcher = page.getByTestId('workspace-switcher');
    await expect(switcher).toHaveValue('');
    await expect(page.getByTestId('workspace-selection-status')).toContainText('Choose a Workspace');
    await expect(page.getByTestId('workspace-members-action')).toHaveCount(0);
    await expect
      .poll(() => page.evaluate((key) => globalThis.localStorage.getItem(key), workspacePreferenceKey('mock-tenant', 'mock-user-a')))
      .toBeNull();
  });

  test('switches Workspace with the keyboard and restores the scoped preference', async ({ page }) => {
    const workspaces = workspaceContextFixtures();
    await installWorkspaceContextApi(page, workspaces, null);

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);

    const switcher = page.getByTestId('workspace-switcher');
    await switcher.focus();
    await page.keyboard.press('End');
    await page.keyboard.press('Enter');

    await expect(switcher).toHaveValue('workspace-beta');
    await expect(page.getByTestId('workspace-research-status')).toContainText('0 Running');
    await expect(page.getByTestId('workspace-research-status')).toContainText('0 Needs review');
    const preferenceKey = workspacePreferenceKey('mock-tenant', 'mock-user-a');
    await expect
      .poll(() => page.evaluate((key) => globalThis.localStorage.getItem(key), preferenceKey))
      .toBe('workspace-beta');

    await page.reload();
    await waitForWorkspaceShellReady(page);
    await expect(page.getByTestId('workspace-switcher')).toHaveValue('workspace-beta');
  });

  test('gives a valid route Workspace precedence over the stored preference', async ({ page }) => {
    const workspaces = workspaceContextFixtures();
    const preferenceKey = workspacePreferenceKey('mock-tenant', 'mock-user-a');
    await page.addInitScript(({ key }) => globalThis.localStorage.setItem(key, 'workspace-beta'), {
      key: preferenceKey
    });
    await installWorkspaceContextApi(page, workspaces, null);

    await page.goto('/app/workspaces/workspace-alpha/members');
    await expect(page.getByTestId('app-shell')).toBeVisible();
    await expect(page.getByTestId('workspace-switcher')).toHaveValue('workspace-alpha');
    await expect
      .poll(() => page.evaluate((key) => globalThis.localStorage.getItem(key), preferenceKey))
      .toBe('workspace-alpha');
  });

  test('discards a stale Workspace preference without selecting the first authorized row', async ({ page }) => {
    const preferenceKey = workspacePreferenceKey('mock-tenant', 'mock-user-a');
    await page.addInitScript(({ key }) => globalThis.localStorage.setItem(key, 'revoked-workspace'), {
      key: preferenceKey
    });
    await installWorkspaceContextApi(page, workspaceContextFixtures(), null);

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);

    await expect(page.getByTestId('workspace-switcher')).toHaveValue('');
    await expect
      .poll(() => page.evaluate((key) => globalThis.localStorage.getItem(key), preferenceKey))
      .toBeNull();
  });

  test('distinguishes an authorized sole Workspace with unavailable Research counts from zero', async ({ page }) => {
    const workspace = { id: 'workspace-unavailable', name: 'Workspace Unavailable' };
    await installWorkspaceContextApi(page, [workspace], null);

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);

    await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspace.id);
    await expect(page.getByTestId('workspace-research-status')).toHaveText(/Status unavailable/);
  });

  test('keeps one keyboard-accessible File inspector with staged metadata at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const workspace: WorkspaceContextFixture = {
      id: '35600000-0000-4000-8000-000000000001',
      name: 'Inspector Workspace',
      canAddFiles: true,
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    };
    const fileObjectId = '35600000-0000-4000-8000-000000000002';
    const sensitiveRequests: string[] = [];
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (/audit|activity|version/i.test(path)) {sensitiveRequests.push(path);}
    });

    await installWorkspaceContextApi(page, [workspace], workspace);
    await page.route('**/api/files**', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (request.method() !== 'GET' || url.pathname !== '/api/files') {
        await route.fulfill({ status: 404 });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          items: [{
            id: '35600000-0000-4000-8000-000000000003',
            fileObjectId,
            workspaceId: workspace.id,
            originalFileName: 'inspector-evidence.zip',
            contentType: 'application/zip',
            sizeBytes: 4096,
            status: 'Active',
            scanStatus: 'Clean',
            uploadedByUserId: 'mock-user-a',
            uploadedByDisplayName: 'Mock User A',
            createdAt: '2026-08-28T02:00:00Z',
            updatedAt: '2026-08-29T03:30:00Z',
            canDelete: false
          }],
          page: 1,
          pageSize: 20,
          totalCount: 1,
          hasMore: false
        })
      });
    });

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
    await expect(moreDetails).toContainText(fileObjectId);

    await activityTab.click();
    await expect(inspector.getByTestId('files-inspector-panel-activity')).toContainText(
      'No file activity or version history is available from the current authorized Files API.'
    );
    expect(sensitiveRequests).toEqual([]);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="files-preview-pane"]');

    await inspector.getByTestId('files-preview-close').click();
    await expect(previewAction).toBeFocused();
  });

  test('Japanese Files labels cover search, selection, and destructive confirmation at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    await page.addInitScript(() => globalThis.localStorage.setItem('coglatas.locale', 'ja'));
    const workspace: WorkspaceContextFixture = {
      id: '35700000-0000-4000-8000-000000000001',
      name: '日本語ワークスペース',
      canAddFiles: true,
      runningProjectCount: 0,
      needsReviewProjectCount: 0,
    };
    const fileObjectId = '35700000-0000-4000-8000-000000000002';

    await installWorkspaceContextApi(page, [workspace], workspace);
    await page.route('**/api/files**', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (request.method() !== 'GET' || url.pathname !== '/api/files') {
        await route.fulfill({ status: 404 });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          items: [{
            id: '35700000-0000-4000-8000-000000000003',
            fileObjectId,
            workspaceId: workspace.id,
            originalFileName: '日本語テスト.pdf',
            contentType: 'application/pdf',
            sizeBytes: 2048,
            status: 'Active',
            scanStatus: 'Clean',
            uploadedByUserId: 'mock-user-a',
            uploadedByDisplayName: 'Mock User A',
            createdAt: '2026-08-28T02:00:00Z',
            updatedAt: '2026-08-29T03:30:00Z',
            canDelete: true,
          }],
          page: 1,
          pageSize: 20,
          totalCount: 1,
          hasMore: false,
        }),
      });
    });

    await page.goto('/app/files');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ja');
    await expect(page.getByText('ファイルを検索・絞り込み', { exact: true })).toBeVisible();
    await expect(page.getByTestId('files-search-input')).toHaveAttribute('placeholder', 'ファイル名を検索');
    await expect(page.getByText('ファイルをアップロード', { exact: true }).first()).toBeVisible();
    await expect(page.getByTestId('file-content-type')).toHaveText('PDF');

    await page.getByRole('checkbox', { name: '日本語テスト.pdfを選択' }).check();
    await page.getByTestId('files-selected-delete').click();
    await expect(page.getByRole('dialog')).toContainText('日本語テスト.pdfを削除しますか？');
    await expect(page.getByRole('dialog').getByRole('button', { name: 'キャンセル' })).toBeVisible();
    await expect(page.getByRole('dialog').getByRole('button', { name: 'ファイルを削除' })).toBeVisible();
    await expectNoDocumentHorizontalOverflow(page);
  });

  test('uses File container breakpoints inside the desktop shell without page overflow', async ({ page }) => {
    const cases = [
      { width: 1440, columns: 3, usesMobileList: false },
      { width: 1024, columns: 1, usesMobileList: true },
      { width: 768, columns: 1, usesMobileList: false },
    ];

    for (const scenario of cases) {
      await page.setViewportSize({ width: scenario.width, height: 800 });
      await page.goto('/app/files');
      await expect(page.getByTestId('files-page')).toBeVisible();

      const layout = await page.locator('.files-page__grid').evaluate((element) => ({
        columns: getComputedStyle(element).gridTemplateColumns.split(' ').filter(Boolean).length,
        desktopGridDisplay: getComputedStyle(element.querySelector('.files-page__desktop-grid')!).display,
        mobileListDisplay: getComputedStyle(element.querySelector('.files-page__mobile-list')!).display,
        children: Array.from(element.children).map((child) => {
          const rect = child.getBoundingClientRect();
          return { gridArea: getComputedStyle(child).gridArea, x: rect.x, y: rect.y };
        }),
      }));

      expect(layout.columns).toBe(scenario.columns);
      expect(layout.desktopGridDisplay === 'none').toBe(scenario.usesMobileList);
      expect(layout.mobileListDisplay !== 'none').toBe(scenario.usesMobileList);

      if (scenario.columns === 3) {
        const [browser, main, inspector] = layout.children;
        expect(browser.gridArea).toBe('browser');
        expect(main.gridArea).toBe('main');
        expect(inspector.gridArea).toBe('inspector');
        expect(browser.x).toBeLessThan(main.x);
        expect(main.x).toBeLessThan(inspector.x);
        expect(browser.y).toBeCloseTo(main.y, 0);
        expect(main.y).toBeCloseTo(inspector.y, 0);
      }
      await expectNoDocumentHorizontalOverflow(page);
    }
  });

  test('keeps the canonical Research Quick Create flow accessible and duplicate-safe at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const workspace: WorkspaceContextFixture = {
      id: '11111111-1111-4111-8111-111111111111',
      name: 'Quick Create Workspace',
      currentUserRole: 'Owner',
      canCreateProject: true,
      canAddFiles: true,
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    };
    const projectId = '22222222-2222-4222-8222-222222222222';
    const createRequests: WorkspaceProjectCreateMockRequest[] = [];
    let releaseCreate!: () => void;
    const createGate = new Promise<void>((resolve) => {
      releaseCreate = resolve;
    });

    await installWorkspaceContextApi(page, [workspace], workspace);
    await page.route(`**/api/workspaces/${workspace.id}/projects`, async (route) => {
      const request = route.request();
      createRequests.push({
        body: request.postDataJSON() as Record<string, unknown>,
        idempotencyKey: request.headers()['idempotency-key'] ?? '',
        csrfToken: request.headers()['x-csrf-token'] ?? ''
      });
      await createGate;
      await route.fulfill({
        status: 201,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          requestId: 'project-create-201',
          data: {
            id: projectId,
            workspaceId: workspace.id,
            groupId: null,
            ownerUserId: '33333333-3333-4333-8333-333333333333',
            title: 'U-22 Quick Research',
            description: null,
            status: 0,
            visibility: 1,
            activationState: 1,
            startDate: null,
            endDate: null,
            versionNo: 1,
            createdAt: '2026-08-24T05:00:00Z'
          },
          warnings: []
        })
      });
    });

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page, { mobile: true });

    const createGroup = page.getByRole('group', { name: '作成' });
    const primary = page.getByTestId('start-research-action');
    const addFiles = page.getByTestId('add-files-action');
    await expect(createGroup).toBeVisible();
    await expect(primary).toHaveCount(1);
    await expect(addFiles).toHaveAttribute(
      'href',
      `/app/workspaces/${workspace.id}/files#upload`
    );
    await expect.poll(() => primary.evaluate((element) => element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(44);
    await expect.poll(() => addFiles.evaluate((element) => element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(44);

    await pressTabUntilFocused(page, primary, 20);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(`/app/workspaces/${workspace.id}/research/new`);
    await expect(page.getByText('リサーチはWorkspace内のProjectとして作成されます')).toBeVisible();
    await expect(page.getByText(/下書き/)).toBeVisible();
    await expect(page.getByText(/Planning/)).toHaveCount(0);
    await expect(
      page.locator('[name="description"], [name="groupId"], [name="startDate"], [name="endDate"]')
    ).toHaveCount(0);

    const title = page.getByTestId('quick-create-research-title');
    const submit = page.getByTestId('quick-create-submit');
    await submit.click();
    await expect(title).toBeFocused();
    await expect(title).toHaveAttribute('aria-invalid', 'true');
    await expect(title).toHaveAttribute('aria-describedby', 'research-title-error');
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await title.fill('  U-22 Quick Research  ');
    const response = page.waitForResponse((candidate) =>
      candidate.request().method() === 'POST' &&
      new URL(candidate.url()).pathname === `/api/workspaces/${workspace.id}/projects`
    );
    await submit.click();
    await expect.poll(() => createRequests.length).toBe(1);
    await page.keyboard.press('Enter');
    await expect.poll(() => createRequests.length).toBe(1);
    releaseCreate();
    expect((await response).status()).toBe(201);

    await expect(page).toHaveURL(`/app/projects/${projectId}`);
    expect(createRequests).toHaveLength(1);
    expect(createRequests[0]?.body).toEqual({ title: 'U-22 Quick Research' });
    expect(createRequests[0]?.idempotencyKey).toMatch(/^workspace-research-/);
    expect(createRequests[0]?.csrfToken).toBe('csrf-workspace-create');
  });

  test('keeps scoped Files search and removable filter chips keyboard-accessible at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const workspace: WorkspaceContextFixture = {
      id: '34100000-0000-4000-8000-000000000001',
      name: 'Files Search Workspace',
      runningProjectCount: 0,
      needsReviewProjectCount: 0,
    };
    const fileId = '34100000-0000-4000-8000-000000000002';
    const searchRequests: URL[] = [];
    await installWorkspaceContextApi(page, [workspace], workspace);
    await page.route('**/api/files?**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false }),
      });
    });
    await page.route('**/api/search?**', async (route) => {
      const url = new URL(route.request().url());
      searchRequests.push(url);
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          page: 1,
          pageSize: 50,
          totalCount: 1,
          items: [{
            type: 13,
            id: fileId,
            title: 'authorized-report.pdf',
            workspaceId: workspace.id,
            createdAt: '2026-08-20T00:00:00Z',
            updatedAt: '2026-08-28T00:00:00Z',
            authorDisplayName: 'Mock User A',
            contentType: 'application/pdf',
            sizeBytes: 2048,
            status: 'Active',
            scanStatus: 'Allowed',
            snippet: 'restricted body snippet must stay hidden',
            storageKey: 'tenant/private/authorized-report.pdf',
          }],
        }),
      });
    });

    await page.goto(`/app/workspaces/${workspace.id}/files`);
    const surface = page.getByTestId('files-search-surface');
    await expect(surface).toBeVisible();
    await expect(surface).toContainText('Scope: Files Search Workspace');
    await page.getByTestId('files-search-input').fill('report');
    await page.getByTestId('files-filter-type').selectOption('pdf');
    await page.getByTestId('files-filter-modified').selectOption('last30Days');
    await page.getByTestId('files-filter-owner').selectOption('me');

    const submit = page.getByTestId('files-search-submit');
    await submit.focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => searchRequests.length).toBe(1);
    expect(searchRequests[0]?.searchParams.get('type')).toBe('File');
    expect(searchRequests[0]?.searchParams.get('workspaceId')).toBe(workspace.id);
    expect(searchRequests[0]?.searchParams.get('q')).toBe('report');
    expect(searchRequests[0]?.searchParams.get('fileKind')).toBe('Pdf');
    expect(searchRequests[0]?.searchParams.get('fromDate')).toBeTruthy();
    expect(searchRequests[0]?.searchParams.get('authorUserId')).toBe('mock-user-a');

    await expect(page.getByTestId('files-filter-chips')).toContainText('Type: PDF');
    await expect(page.getByTestId('files-filter-chips')).toContainText('Modified: Last 30 days');
    await expect(page.getByTestId('files-filter-chips')).toContainText('Owner: Uploaded by me');
    await expect(page.getByTestId('files-search-status')).toContainText('1 currently authorized file matches.');
    await expect(page.getByTestId('preview-action')).toHaveText('authorized-report.pdf');
    await expect(page.getByText('restricted body snippet must stay hidden')).toHaveCount(0);
    await expect(page.getByText('tenant/private/authorized-report.pdf')).toHaveCount(0);

    const removeType = page.getByRole('button', { name: 'Remove filter Type: PDF' });
    await removeType.focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => searchRequests.length).toBe(2);
    expect(searchRequests[1]?.searchParams.has('fileKind')).toBe(false);
    await expect(removeType).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Remove filter Modified: Last 30 days' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Remove filter Owner: Uploaded by me' })).toBeVisible();

    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
    await expectHealthyAngularPage(page);
  });

  test('keeps Announcement publish validation and preserved failures accessible at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const workspace: WorkspaceContextFixture = {
      id: '38000000-0000-4000-8000-000000000001',
      name: 'Announcement evidence workspace',
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    };
    await installWorkspaceContextApi(page, [workspace], workspace);
    const api = await installAnnouncementEditorApi(page);
    await page.addInitScript((storageKey) => {
      globalThis.localStorage.setItem(storageKey, 'light');
    }, themeStorageKey);

    await page.goto('/app/announcements');
    const create = page.getByTestId('create-announcement-action');
    await expect(create).toBeVisible();
    await create.focus();
    await page.keyboard.press('Enter');

    const title = page.getByTestId('announcement-editor-title');
    const body = page.getByTestId('announcement-editor-body');
    const nextStep = page.getByTestId('announcement-next-step');
    const publish = page.getByTestId('announcement-publish-action');
    await expect(title).toBeVisible();
    await nextStep.focus();
    await page.keyboard.press('Enter');

    const validationSummary = page.getByTestId('announcement-editor-error-summary');
    await expect(validationSummary).toBeVisible();
    await expect(title).toBeFocused();
    await expect(title).toHaveAttribute('aria-invalid', 'true');
    await expect(title).toHaveAttribute('aria-describedby', /announcement-title-error/);
    await expect(body).toHaveAttribute('aria-invalid', 'true');
    await expect(body).toHaveAttribute('aria-describedby', /announcement-body-error/);
    await validationSummary.getByRole('link', { name: /本文を入力してください/ }).focus();
    await page.keyboard.press('Enter');
    await expect(body).toBeFocused();
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await title.fill('Accessible announcement');
    await body.fill('The draft must remain available after an API failure.');

    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('announcement-editor-current-step')).toContainText('Step 2 of 4: Audience');
    await expect(page.getByTestId('announcement-editor-content-step')).toBeHidden();
    await expect(page.getByTestId('announcement-editor-audience')).toBeVisible();
    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('announcement-editor-current-step')).toContainText('Step 3 of 4: Delivery');
    await expect(page.getByTestId('announcement-editor-audience')).toBeHidden();
    await expect(page.getByTestId('announcement-editor-priority')).toBeVisible();

    const previewAction = page.getByTestId('announcement-preview-action');
    const editAction = page.getByTestId('announcement-edit-action');
    const previewSideEffects: string[] = [];
    const collectPreviewSideEffects = (request: Request) => {
      const pathname = new URL(request.url()).pathname;
      if (
        request.method() === 'POST' &&
        (pathname === '/api/announcements' ||
        pathname.startsWith('/api/announcement-drafts') ||
        pathname.endsWith('/read'))
      ) {
        previewSideEffects.push(`${request.method()} ${pathname}`);
      }
    };
    page.on('request', collectPreviewSideEffects);

    await page.getByTestId('announcement-editor-priority').selectOption('critical');
    await page.getByTestId('announcement-editor-read-confirmation').check();
    await previewAction.focus();
    await page.keyboard.press('Enter');

    const localPreview = page.getByTestId('announcement-local-preview');
    await expect(localPreview).toBeVisible();
    await expect(page.getByTestId('announcement-preview-heading')).toBeFocused();
    await expect(localPreview).toContainText('Accessible announcement');
    await expect(localPreview).toContainText('The draft must remain available after an API failure.');
    await expect(localPreview).toContainText('CRITICAL');
    await expect(localPreview).toContainText('Announcement evidence workspace');
    await expect(localPreview).toContainText('24 recipients');
    await expect(page.getByTestId('announcement-preview-cta-inert')).toBeVisible();
    await expect(page.getByTestId('announcement-mark-read-action')).toHaveCount(0);
    expect(api.publishRequests).toHaveLength(0);
    expect(previewSideEffects).toEqual([]);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await editAction.focus();
    await page.keyboard.press('Enter');
    await expect(localPreview).toHaveCount(0);
    await expect(page.getByTestId('announcement-editor-priority')).toBeFocused();
    await expect(title).toHaveValue('Accessible announcement');
    await expect(body).toHaveValue('The draft must remain available after an API failure.');
    await expect(page.getByTestId('announcement-editor-priority')).toHaveValue('critical');
    expect(previewSideEffects).toEqual([]);
    page.off('request', collectPreviewSideEffects);

    await page.getByTestId('announcement-editor-priority').selectOption('normal');
    await page.getByTestId('announcement-editor-read-confirmation').uncheck();

    await nextStep.focus();
    await page.keyboard.press('Enter');
    const review = page.getByTestId('announcement-review-summary');
    await expect(review).toBeVisible();
    await expect(page.getByTestId('announcement-editor-priority')).toBeHidden();
    await expect(review).toContainText('Accessible announcement');
    await expect(review).toContainText('Announcement evidence workspace');
    await expect(review).toContainText('24名');

    const reviewPreview = page.getByTestId('announcement-review-preview');
    await reviewPreview.focus();
    await page.keyboard.press('Enter');
    await expect(localPreview).toBeVisible();
    await expect(page.getByTestId('announcement-preview-heading')).toBeFocused();
    await editAction.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Review before publication' })).toBeFocused();

    const editContent = page.getByTestId('announcement-review-edit-content');
    await editContent.focus();
    await page.keyboard.press('Enter');
    await expect(title).toBeFocused();
    await expect(title).toHaveValue('Accessible announcement');
    await expect(body).toHaveValue('The draft must remain available after an API failure.');
    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('announcement-editor-audience')).toBeFocused();
    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('announcement-editor-priority')).toBeFocused();
    await expect(page.getByTestId('announcement-editor-priority')).toHaveValue('normal');
    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Review before publication' })).toBeFocused();
    await expect(review).toBeVisible();

    await publish.focus();
    await page.keyboard.press('Enter');
    const confirmation = page.getByTestId('announcement-publication-confirmation');
    const dialog = page.getByRole('dialog', { name: 'Confirm publication' });
    await expect(confirmation).toBeVisible();
    await expect(dialog).toHaveAttribute('aria-modal', 'true');
    await expect(confirmation).toContainText('Announcement evidence workspace');
    await expect(confirmation).toContainText('24 recipients');
    await expect(confirmation).toContainText('NORMAL');
    await expect(confirmation).toContainText('Publish immediately');
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(publish).toBeFocused();
    await expect(title).toHaveValue('Accessible announcement');
    await expect(body).toHaveValue('The draft must remain available after an API failure.');

    await page.keyboard.press('Enter');
    await expect(dialog).toBeVisible();

    const failedResponse = page.waitForResponse((response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname.endsWith('/publish')
    );
    await dialog.getByRole('button', { name: /Publish to 24 recipients now/ }).click();
    expect((await failedResponse).status()).toBe(503);
    const submissionError = page.getByTestId('announcement-editor-submission-error');
    await expect(submissionError).toBeVisible();
    await expect(submissionError).toHaveAttribute('role', 'alert');
    await expect(submissionError).toContainText('could not be published right now');
    await expect(submissionError).not.toContainText('internal upstream detail');
    await expect(title).toHaveValue('Accessible announcement');
    await expect(body).toHaveValue('The draft must remain available after an API failure.');

    await publish.focus();
    await page.keyboard.press('Enter');
    await expect(dialog).toBeVisible();
    const succeededResponse = page.waitForResponse((response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname.endsWith('/publish')
    );
    await dialog.getByRole('button', { name: /Publish to 24 recipients now/ }).click();
    expect((await succeededResponse).status()).toBe(200);
    await expect(page.getByText(/Publication queued/)).toBeVisible();
    expect(api.publishRequests).toHaveLength(2);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await page.getByTestId('theme-toggle').focus();
    await page.keyboard.press('Enter');
    await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', 'dark');
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
  });

  test('keeps recipient announcement detail navigable, readable, and confirmable at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const workspace: WorkspaceContextFixture = {
      id: '38500000-0000-4000-8000-000000000001',
      name: 'Announcement mobile detail workspace',
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    };
    await installWorkspaceContextApi(page, [workspace], workspace);
    const api = await installAnnouncementMobileDetailApi(page, workspace.id);

    await page.goto('/app/announcements');
    const listRow = page.getByTestId('announcement-list-item').filter({ hasText: 'Mobile recipient detail' });
    await expect(listRow).toBeVisible();
    await page.evaluate(() => { document.scrollingElement!.scrollTop = 160; });
    await expect.poll(() => page.evaluate(() => document.scrollingElement!.scrollTop)).toBe(160);
    await listRow.scrollIntoViewIfNeeded();
    const originScrollTop = await page.evaluate(() => document.scrollingElement!.scrollTop);
    expect(originScrollTop).toBeGreaterThan(0);
    await listRow.click();

    await expect(page).toHaveURL(new RegExp(`/app/announcements/${api.id}$`));
    await expect(page.getByTestId('announcement-detail-title')).toBeFocused();
    await expect.poll(() => page.evaluate(() => document.scrollingElement!.scrollTop)).toBe(0);

    const priority = page.getByTestId('announcement-priority-label');
    const title = page.getByTestId('announcement-detail-title');
    const published = page.getByTestId('announcement-published-at');
    const expiry = page.getByTestId('announcement-expires-at');
    const audience = page.getByTestId('announcement-audience-label');
    const positions = await Promise.all(
      [priority, title, published, expiry, audience].map((locator) =>
        locator.evaluate((element) => element.getBoundingClientRect().top),
      ),
    );
    expect(positions[0]!).toBeLessThan(positions[1]!);
    expect(positions[1]!).toBeLessThan(positions[2]!);
    expect(positions[2]!).toBeLessThan(positions[3]!);
    expect(positions[3]!).toBeLessThan(positions[4]!);
    await expect(expiry).toBeVisible();
    await expect(page.getByTestId('announcement-body-text')).toContainText('long recipient-facing body');
    const action = page.getByTestId('announcement-mark-read-action');
    await expect(action).toBeVisible();
    await expect.poll(() => action.evaluate((element) => element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(44);
    await page.evaluate(() => { document.scrollingElement!.scrollTop = 480; });
    await expect.poll(() => action.evaluate((element) => {
      const bounds = element.getBoundingClientRect();
      return bounds.top >= 0 && bounds.bottom <= window.innerHeight;
    })).toBe(true);
    await expect(page.locator('app-message-composer')).toHaveCount(0);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    const readResponse = page.waitForResponse((response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === `/api/announcements/${api.id}/read`,
    );
    await action.click();
    expect((await readResponse).status()).toBe(200);
    expect(api.readRequests).toEqual([{ body: {}, csrfToken: 'csrf-announcement-read' }]);
    await expect(action).toHaveCount(0);
    await expect(page.getByTestId('announcement-read-status')).toBeFocused();

    await page.evaluate(() => { document.scrollingElement!.scrollTop = 0; });
    await expect.poll(() => page.evaluate(() => document.scrollingElement!.scrollTop)).toBe(0);
    await page.getByTestId('announcement-mobile-back').click();
    await expect(page).toHaveURL(/\/app\/announcements$/);
    await expect.poll(() => page.evaluate(() => document.scrollingElement!.scrollTop)).toBe(originScrollTop);
    await expect(listRow).toBeFocused();
    await page.goBack();
    await expect(page).toHaveURL(/\/app\/announcements$/);
    await page.goForward();
    await expect(page).toHaveURL(/\/app\/announcements$/);
  });

  test('keeps live Announcement edits when a reauthorization refresh is delayed', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const workspace: WorkspaceContextFixture = {
      id: '38000000-0000-4000-8000-000000000001',
      name: 'Announcement evidence workspace',
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    };
    await installWorkspaceContextApi(page, [workspace], workspace);
    const api = await installAnnouncementEditorApi(page, {
      firstPublishFailure: 'audienceAuthorization',
      holdAudienceRefresh: true
    });

    await page.goto('/app/announcements');
    await page.getByTestId('create-announcement-action').click();

    const title = page.getByTestId('announcement-editor-title');
    const body = page.getByTestId('announcement-editor-body');
    const nextStep = page.getByTestId('announcement-next-step');
    const publish = page.getByTestId('announcement-publish-action');
    await title.fill('Submitted title');
    await body.fill('Submitted body');

    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('announcement-editor-audience')).toBeFocused();
    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('announcement-editor-priority')).toBeFocused();
    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Review before publication' })).toBeFocused();
    await expect(publish).toBeVisible();

    await publish.focus();
    await page.keyboard.press('Enter');
    const confirmationDialog = page.getByRole('dialog', { name: 'Confirm publication' });
    await expect(confirmationDialog).toBeVisible();
    const failedResponse = page.waitForResponse((response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname.endsWith('/publish')
    );
    await confirmationDialog.getByRole('button', { name: /Publish to 24 recipients now/ }).click();
    expect((await failedResponse).status()).toBe(403);
    await api.audienceRefreshRequested;

    const submissionError = page.getByTestId('announcement-editor-submission-error');
    await expect(submissionError).toContainText('selected audience is no longer authorized');
    await expect(submissionError).not.toContainText('Announcement audience is not authorized.');

    await page.getByTestId('announcement-review-edit-content').focus();
    await page.keyboard.press('Enter');
    await title.fill('Live title edited after publish failed');
    await body.fill('Live body edited after publish failed');
    api.releaseAudienceRefresh();

    await nextStep.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('announcement-audience-unavailable')).toBeVisible();
    await expect(title).toHaveValue('Live title edited after publish failed');
    await expect(body).toHaveValue('Live body edited after publish failed');
    await expect(page.getByTestId('announcement-save-draft-action')).toBeDisabled();
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
  });

  test('creates a canonical Draft Project once and activates it only through the explicit command', async ({ page }, testInfo) => {
    const mobile = testInfo.project.name === 'chromium-mobile';
    await page.setViewportSize(mobile
      ? { width: 320, height: 900 }
      : { width: 1280, height: 900 });

    const workspace: WorkspaceContextFixture = {
      id: '40900000-0000-4000-8000-000000000001',
      name: 'U-22 Project Workspace',
      currentUserRole: 'Owner',
      canOpenProjectCreate: true,
      canCreateProject: false,
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    };
    const projectId = '40900000-0000-4000-8000-000000000002';
    const groupId = '40900000-0000-4000-8000-000000000003';
    await installWorkspaceContextApi(page, [workspace], workspace);
    const api = await installCanonicalProjectCreateActivationApi(page, {
      workspaceId: workspace.id,
      projectId,
      groupId
    });

    await page.goto(`/app/workspaces/${workspace.id}/projects?create=1`);
    const dialog = page.getByRole('dialog', { name: 'Create Project' });
    await expect(dialog).toBeVisible();
    await expect(page).toHaveURL(`/app/workspaces/${workspace.id}/projects`);
    await expect(page.getByTestId('project-create-title')).toBeFocused();
    await expect(page.getByTestId('project-create-group')).toContainText('Evidence Review Group');
    await expect(dialog).not.toContainText(groupId);
    await expect(dialog.locator('input[name="groupId"], input[name="workspaceId"], input[name="ownerUserId"]')).toHaveCount(0);

    const title = page.getByTestId('project-create-title');
    const group = page.getByTestId('project-create-group');
    const startDate = page.getByTestId('project-create-start-date');
    const endDate = page.getByTestId('project-create-end-date');
    for (let index = 0; index < 14; index += 1) {
      await page.keyboard.press('Tab');
      await expect.poll(() => dialog.evaluate((element) => element.contains(document.activeElement))).toBe(true);
    }
    await title.focus();
    await title.fill('  U-22 Canonical Project  ');
    await page.getByTestId('project-create-description').fill('Canonical create and activation browser evidence.');
    await page.getByTestId('project-create-group-search').fill('Evidence Review');
    await group.selectOption(groupId);
    await startDate.fill('2026-09-10');
    await endDate.fill('2026-09-09');

    const submit = dialog.locator('.coglatas-dialog__confirm');
    await expect(submit).toHaveText('Create Project');
    await submit.click();
    const errorSummary = page.getByTestId('project-create-error-summary');
    await expect(errorSummary).toBeFocused();
    await expect(errorSummary).toContainText('Target end date cannot be before the start date.');
    expect(api.createRequests).toHaveLength(0);
    await errorSummary.getByRole('link', { name: 'Target end date cannot be before the start date.' }).click();
    await expect(endDate).toBeFocused();
    await endDate.fill('2026-09-20');

    await submit.click();
    await expect
      .poll(
        async () => {
          if (api.createRequests.length === 1) {
            return 'posted';
          }

          const recoveryText = await page
            .getByTestId('project-create-create-status')
            .evaluateAll((nodes) => nodes.map((node) => node.textContent ?? '').join(' '));
          return recoveryText.includes('stopped before it was sent') ? 'stopped' : 'pending';
        },
        { timeout: 10_000 }
      )
      .not.toBe('pending');

    const stoppedBeforeDispatch = api.createRequests.length === 0;
    if (stoppedBeforeDispatch) {
      // The registered authorization clearer can win before browser dispatch.
      // The original form stays local, but the next POST is only user-led
      // after the authoritative options endpoint is rechecked.
      expect(api.createRequests).toHaveLength(0);
      await expect(page.getByTestId('project-create-create-status')).toContainText('stopped before it was sent');
      await page.getByTestId('project-create-options-retry').click();
      await expect(title).toBeVisible();
      expect(api.createRequests, 'reauthorizing options never auto-repeats the create POST').toHaveLength(0);

      const recoveredAttempt = page.waitForResponse((response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname === `/api/workspaces/${workspace.id}/projects`
      );
      api.allowFirstCreateSuccess();
      await submit.click();
      expect((await recoveredAttempt).status()).toBe(201);
    } else {
      // The fixture deterministically holds the first actual POST so this
      // remains the posted/in-flight duplicate-command regression.
      await expect.poll(() => api.createRequests.length).toBe(1);
      const firstAttempt = page.waitForResponse((response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname === `/api/workspaces/${workspace.id}/projects`
      );
      await expect(dialog).toHaveAttribute('aria-busy', 'true');
      await expect(submit).toBeDisabled();
      await expect(submit).toContainText('Working');
      await title.press('Enter');
      await page.waitForTimeout(100);
      expect(api.createRequests, 'Enter cannot duplicate an in-flight Project command').toHaveLength(1);
      api.releaseFirstCreate();
      expect((await firstAttempt).status()).toBe(503);
      await expect(errorSummary).toBeFocused();
      await expect(errorSummary).toContainText('may have been created');

      const secondAttempt = page.waitForResponse((response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname === `/api/workspaces/${workspace.id}/projects`
      );
      await submit.click();
      expect((await secondAttempt).status()).toBe(201);
    }

    const expectedCreateRequestCount = stoppedBeforeDispatch ? 1 : 2;

    await expect(page).toHaveURL(`/app/projects/${projectId}`);
    await expect(page.getByTestId('project-draft-overview')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'U-22 Canonical Project' })).toBeVisible();
    await expect(page.getByRole('tab')).toHaveCount(1);
    await expect(page.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
    await expect.poll(api.projectGetCount).toBeGreaterThanOrEqual(2);
    await expect.poll(() => api.projectListRequests.filter((request) =>
      request.workspaceId === workspace.id && request.includesCreatedProject).length)
      .toBeGreaterThanOrEqual(1);

    const expectedCreateBody = {
      title: 'U-22 Canonical Project',
      description: 'Canonical create and activation browser evidence.',
      groupId,
      visibility: 1,
      startDate: '2026-09-10',
      endDate: '2026-09-20'
    };
    expect(api.createRequests).toHaveLength(expectedCreateRequestCount);
    for (const request of api.createRequests) {
      expect(request.body).toEqual(expectedCreateBody);
    }
    expect(api.createRequests[0]?.idempotencyKey).toMatch(/^project-create-/);
    if (!stoppedBeforeDispatch) {
      expect(api.createRequests[1]?.rawBody).toBe(api.createRequests[0]?.rawBody);
      expect(api.createRequests[1]?.idempotencyKey).toBe(api.createRequests[0]?.idempotencyKey);
    }
    expect(api.createRequests.every((request) => request.csrfToken === 'csrf-workspace-create')).toBe(true);
    expect(api.operationalGetPaths).toEqual([]);

    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await page.goto(`/app/workspaces/${workspace.id}/projects`);
    const createdCard = page.getByTestId('project-summary-card')
      .filter({ hasText: 'U-22 Canonical Project' });
    await expect(createdCard).toBeVisible();
    await expect(createdCard).toContainText('Draft');
    expect(api.createRequests, 'returning to Projects never repeats the create POST').toHaveLength(expectedCreateRequestCount);
    await createdCard.getByRole('link', { name: 'Open U-22 Canonical Project' }).click();
    await expect(page).toHaveURL(`/app/projects/${projectId}`);
    await expect(page.getByTestId('project-draft-overview')).toBeVisible();
    expect(api.createRequests, 'reopening the created Project never repeats the create POST').toHaveLength(expectedCreateRequestCount);
    expect(api.operationalGetPaths).toEqual([]);

    const activate = page.getByTestId('activate-project');
    await activate.focus();
    await expect(activate).toBeFocused();
    const activationResponse = page.waitForResponse((response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === `/api/projects/${projectId}/activate`
    );
    await page.keyboard.press('Enter');
    expect((await activationResponse).status()).toBe(200);

    const activationStatus = page.locator('.project-detail-page__activation-status');
    await expect(activationStatus).toContainText('Project activated. Operational views were loaded from authoritative state.');
    await expect(activationStatus).toBeFocused();
    await expect(page.getByTestId('project-draft-overview')).toHaveCount(0);
    await expect(page.getByRole('tab', { name: 'Tasks' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Schedule' })).toBeVisible();
    await expect.poll(() => new Set(api.operationalGetPaths).size).toBe(5);
    expect(api.activationRequests).toEqual([{ expectedVersion: 1 }]);
    expect(api.activationCsrfTokens).toEqual(['csrf-workspace-create']);

    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
    await expectHealthyAngularPage(page);
  });

  test('keeps Project New Task keyboard-accessible and horizontal-overflow-free at 320px without a Start action', async ({ page }) => {
    const workspace: WorkspaceContextFixture = {
      id: '41000000-0000-4000-8000-000000000001',
      name: 'Task creation evidence workspace',
      currentUserRole: 'Owner',
      canCreateProject: true,
      runningProjectCount: 1,
      needsReviewProjectCount: 0,
    };
    const projectId = '41000000-0000-4000-8000-000000000002';
    const taskId = '41000000-0000-4000-8000-000000000003';
    await page.setViewportSize({ width: 320, height: 900 });
    await installWorkspaceContextApi(page, [workspace], workspace);
    const api = await installTaskCreateStaticApi(page, { workspaceId: workspace.id, projectId, taskId });

    await page.goto(`/app/projects/${projectId}`);
    const create = page.getByTestId('project-create-task');
    await expect(create).toBeVisible();
    await pressTabUntilFocused(page, create, 24);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(`/app/projects/${projectId}/tasks/new`);

    const title = page.getByTestId('task-create-title');
    const goal = page.getByTestId('task-brief-goal-input');
    const deliverable = page.getByTestId('task-brief-deliverable-input');
    const constraints = page.getByTestId('task-brief-constraints-input');
    const milestone = page.getByTestId('task-create-milestone');
    const assignee = page.getByTestId('task-create-primary-assignee');
    const submit = page.getByTestId('task-create-submit');
    const qualityChecklist = page.getByTestId('task-create-quality-checklist');
    await expect(title).toBeFocused();
    await expect(page.getByTestId('task-create-page')).toContainText('does not start a runtime or retrieve sources');
    await expect(page.getByRole('button', { name: 'Start' })).toHaveCount(0);
    await expect(page.locator('[name="webUrl"], [name="provider"], [name="projectId"], [name="workspaceId"]')).toHaveCount(0);
    await expect(qualityChecklist).toContainText('Advisory only: 1 of 4 items are covered.');
    await expect(qualityChecklist).toContainText('Project default policy: Web disabled; Project files enabled.');
    const addGoal = page.getByTestId('task-create-quality-goal').getByRole('button', { name: 'Add Goal' });
    await pressTabUntilFocused(page, addGoal, 24);
    await page.keyboard.press('Enter');
    await expect(goal).toBeFocused();
    await title.fill('  Accessible evidence Task  ');
    await goal.fill('Review the immutable source-scope choice.');
    await deliverable.fill('A reviewable task record.');
    await constraints.fill('Use only the authorized Project policy.');
    await milestone.selectOption(api.milestoneId);
    await assignee.selectOption(api.assigneeId);
    await expect(qualityChecklist).toContainText('Advisory only: 4 of 4 items are covered.');
    await expect(qualityChecklist.getByRole('button')).toHaveCount(0);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    const created = page.waitForResponse((response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === `/api/projects/${projectId}/tasks/create`,
    );
    await submit.click();
    expect((await created).status()).toBe(201);
    await expect(page).toHaveURL(`/app/projects/${projectId}/tasks/${taskId}`);
    expect(api.createRequests).toHaveLength(1);
    expect(api.createRequests[0]?.body).toEqual({
      title: 'Accessible evidence Task',
      priority: 1,
      milestoneId: api.milestoneId,
      primaryAssigneeUserId: api.assigneeId,
      goal: 'Review the immutable source-scope choice.',
      deliverable: 'A reviewable task record.',
      constraints: 'Use only the authorized Project policy.',
      sourceScopeMode: 'Inherit',
    });
    expect(api.createRequests[0]?.csrfToken).toBe('csrf-workspace-create');
    expect(api.createRequests[0]?.idempotencyKey).toMatch(/^task-create-/u);
    expect(api.createRequests[0]?.body).not.toHaveProperty('projectId');
    expect(api.createRequests[0]?.body).not.toHaveProperty('workspaceId');
    expect(api.createRequests[0]?.body).not.toHaveProperty('webUrl');
    expect(api.createRequests[0]?.body).not.toHaveProperty('provider');
    expect(api.createRequests[0]?.body).not.toHaveProperty('taskOverridePolicy');
  });

  test('fails closed when the backend does not grant Workspace creation', async ({ page }) => {
    const api = await installWorkspaceContextApi(
      page,
      workspaceContextFixtures(),
      null,
      { canCreate: false }
    );

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);

    await expect(page.getByTestId('create-workspace-action')).toHaveCount(0);
    await expect(page.getByTestId('workspace-empty-create-action')).toHaveCount(0);
    expect(api.createRequests).toEqual([]);
  });

  test('opens Workspace creation from the authorized empty state', async ({ page }) => {
    const api = await installWorkspaceContextApi(page, [], null, { canCreate: true });

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);

    const emptyStateAction = page.getByTestId('workspace-empty-create-action');
    await expect(page.getByTestId('workspace-empty-state')).toBeVisible();
    await expect(emptyStateAction).toBeVisible();
    await emptyStateAction.click();
    await expect(page.getByRole('dialog', { name: 'Create Workspace' })).toBeVisible();
    await expect(page.getByTestId('workspace-create-name')).toBeFocused();
    expect(api.createRequests).toEqual([]);

    await page.getByRole('button', { name: 'Cancel' }).click();
    await expect(emptyStateAction).toBeFocused();
    expect(api.createRequests).toEqual([]);
  });

  test('keeps Workspace creation cancellable, keyboard-contained, and reachable at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const api = await installWorkspaceContextApi(
      page,
      workspaceContextFixtures(),
      null,
      { canCreate: true }
    );

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page, { mobile: true });

    const opener = page.getByTestId('create-workspace-action');
    await opener.click();

    const dialog = page.getByRole('dialog', { name: 'Create Workspace' });
    const name = page.getByTestId('workspace-create-name');
    await expect(dialog).toBeVisible();
    await expect(name).toBeFocused();
    await expect(page.locator('[name="workspaceId"], [name="id"], [name="slug"]')).toHaveCount(0);

    await page.getByRole('button', { name: 'Create Workspace' }).click();
    const errorSummary = page.getByTestId('workspace-create-error-summary');
    await expect(errorSummary).toBeFocused();
    await expect(errorSummary).toContainText('Enter a Workspace name');
    await expect(name).toHaveAttribute('aria-invalid', 'true');
    expect(api.createRequests).toEqual([]);

    await errorSummary.getByRole('link', { name: 'Enter a Workspace name.' }).click();
    await expect(name).toBeFocused();
    for (let index = 0; index < 12; index += 1) {
      await page.keyboard.press('Tab');
      await expect
        .poll(() => dialog.evaluate((element) => element.contains(document.activeElement)))
        .toBe(true);
    }
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);

    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
    expect(api.createRequests).toEqual([]);

    await opener.click();
    await expect(name).toBeFocused();
    await page.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
    expect(api.createRequests).toEqual([]);
  });

  test('submits Workspace creation once in flight and safely reuses the request identity after a 503', async ({ page }) => {
    const existingWorkspace = workspaceContextFixtures()[0];
    const createdWorkspace: WorkspaceContextFixture = {
      id: 'c68465db-8058-4a2f-ae3e-df457fe69d52',
      name: 'Evidence Workspace',
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    };
    let releaseFirstPost!: () => void;
    const firstPostGate = new Promise<void>((resolve) => {
      releaseFirstPost = resolve;
    });
    const api = await installWorkspaceContextApi(
      page,
      [existingWorkspace],
      existingWorkspace,
      {
        canCreate: true,
        onCreate: async (_request, attempt) => {
          if (attempt === 1) {
            await firstPostGate;
            return {
              status: 503,
              body: {
                requestId: 'workspace-create-503',
                error: {
                  code: 'DependencyUnavailable',
                  message: 'Workspace creation is temporarily unavailable.',
                  target: 'workspace',
                  details: [],
                  redactionApplied: false
                },
                traceId: 'workspace-create-503',
                status: 503
              }
            };
          }

          return {
            status: 201,
            workspace: createdWorkspace,
            body: {
              requestId: 'workspace-create-201',
              data: {
                id: createdWorkspace.id,
                name: createdWorkspace.name,
                description: 'Evidence for the U-22 demo',
                icon: null,
                status: 0,
                createdByUserId: 'a7ad8352-2012-4328-8318-7d9c466af46d',
                createdAt: '2026-08-24T05:00:00Z',
                updatedAt: null
              },
              warnings: []
            }
          };
        }
      }
    );

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);
    await page.getByTestId('create-workspace-action').click();
    await page.getByTestId('workspace-create-name').fill(`  ${createdWorkspace.name}  `);
    await page.getByTestId('workspace-create-description').fill('Evidence for the U-22 demo');

    const firstResponse = waitForWorkspaceCreateResponse(page);
    await page.getByRole('button', { name: 'Create Workspace' }).click();
    await expect.poll(() => api.createRequests.length).toBe(1);
    await expect(page.locator('.coglatas-dialog__confirm')).toBeDisabled();

    // Native submit plus the facade's synchronous busy guard must suppress a
    // second request even if Enter is pressed while the first response waits.
    await page.getByTestId('workspace-create-name').press('Enter');
    await expect.poll(() => api.createRequests.length).toBe(1);

    releaseFirstPost();
    expect((await firstResponse).status()).toBe(503);
    await expect(page.getByTestId('workspace-create-error-summary')).toContainText(
      'The Workspace may have been created. Retry with the same details'
    );

    const retryResponse = waitForWorkspaceCreateResponse(page);
    await page.getByRole('button', { name: 'Create Workspace' }).click();
    expect((await retryResponse).status()).toBe(201);

    await expect(page).toHaveURL(/\/app\/workspaces$/);
    await expect(page.getByTestId('workspace-card').filter({ hasText: createdWorkspace.name })).toBeVisible();
    await expect(page.getByTestId('workspace-switcher')).toHaveValue(createdWorkspace.id);
    await expect(page.getByTestId('workspace-created-announcement')).toContainText(
      `${createdWorkspace.name} Workspace`
    );
    await expect(page.getByTestId('workspace-dashboard')).toBeFocused();

    expect(api.createRequests).toHaveLength(2);
    const [firstRequest, retryRequest] = api.createRequests;
    expect(firstRequest.body).toEqual({
      name: createdWorkspace.name,
      description: 'Evidence for the U-22 demo',
      icon: null
    });
    expect(retryRequest.body).toEqual(firstRequest.body);
    expect(retryRequest.rawBody).toBe(firstRequest.rawBody);
    expect(retryRequest.idempotencyKey).toBe(firstRequest.idempotencyKey);
    expect(firstRequest.idempotencyKey).toMatch(/^[\x20-\x7e]{8,128}$/u);
    expect(api.createRequests.every((request) => request.csrfToken === 'csrf-workspace-create')).toBe(true);
    expect(api.workspaceListRequests).toBeGreaterThanOrEqual(2);
    await expectHealthyAngularPage(page);
  });

  test('switches and persists the selected light or dark theme', async ({ page }) => {
    await page.addInitScript((storageKey) => {
      if (!globalThis.localStorage.getItem(storageKey)) {
        globalThis.localStorage.setItem(storageKey, 'light');
      }
    }, themeStorageKey);

    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page);

    const root = page.locator('html');
    const toggle = page.getByTestId('theme-toggle');
    await expect(root).toHaveAttribute('data-coglatas-theme', 'light');
    await expect(toggle).toHaveAccessibleName('Switch to dark mode');

    await toggle.click();
    await expect(root).toHaveAttribute('data-coglatas-theme', 'dark');
    await expect(toggle).toHaveAccessibleName('Switch to light mode');
    await expect
      .poll(() => page.evaluate((storageKey) => globalThis.localStorage.getItem(storageKey), themeStorageKey))
      .toBe('dark');

    await page.reload();
    await waitForWorkspaceShellReady(page);
    await expect(root).toHaveAttribute('data-coglatas-theme', 'dark');
    await expect(page.getByTestId('theme-toggle')).toHaveAccessibleName('Switch to light mode');
  });

  test('falls back to Angular index.html for unknown user-facing routes', async ({ page, request }) => {
    const response = await request.get('/app/not-a-real-angular-route');

    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('text/html');
    await expect(response.text()).resolves.toContain('<app-root');

    await page.goto('/app/not-a-real-angular-route');
    await expect(page.locator('app-root')).toBeVisible();
    await expect(page.getByTestId('page-placeholder')).toBeVisible();
    await expectHealthyAngularPage(page);
  });

  test('redirects unauthenticated private route access to login', async ({ page }) => {
    await page.route('**/api/auth/me', async (route) => {
      await route.fulfill({
        status: 401,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ error: 'Unauthorized' })
      });
    });

    await page.goto('/app/workspaces');

    await expect(page).toHaveURL(/\/app\/login$/);
    await expect(page.getByTestId('page-placeholder')).toBeVisible();
    await expect(page.locator('app-shell')).toHaveCount(0);
    await expectHealthyAngularPage(page);
  });

  test('keeps API paths out of Angular fallback routing', async ({ request }) => {
    const response = await request.get('/api/playwright-angular-smoke');

    expect(response.status()).toBe(404);
    expect(response.headers()['content-type']).toContain('application/json');
    await expect(response.json()).resolves.toEqual({ error: 'Endpoint not found.' });

    const body = await response.text();
    expect(body).not.toContain('<app-root');
    expect(body).not.toContain('<!doctype html>');
  });

  test('renders the mobile shell drawer without legacy route exposure', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/app/workspaces');

    await waitForWorkspaceShellReady(page, { mobile: true });
    await expect(page.getByTestId('account-rail')).toBeHidden();

    const mobileNavigation = page.getByTestId('mobile-navigation');
    const toggle = page.getByTestId('mobile-nav-toggle');
    await expect(mobileNavigation).toHaveAttribute('aria-hidden', 'true');
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await toggle.click();
    await expect(mobileNavigation).toHaveAttribute('aria-hidden', 'false');
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await expect(mobileNavigation.locator('a[href="/app/workspaces"]')).toBeVisible();

    for (const legacyRoute of ['/dashboard', '/messages', '/tenant-admin', '/platform-admin']) {
      await expect(page.locator(`a[href="${legacyRoute}"]`)).toHaveCount(0);
    }

    await expectHealthyAngularPage(page);
  });

  test('mobile navigation traps focus and returns focus on Escape', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 700 });
    await page.goto('/app/workspaces');

    await waitForWorkspaceShellReady(page, { mobile: true });
    const toggle = page.getByTestId('mobile-nav-toggle');
    await toggle.click();

    const mobileNavigation = page.getByTestId('mobile-navigation');
    await expect(mobileNavigation).toHaveAttribute('aria-hidden', 'false');
    await expect(mobileNavigation).toContainText(/.+/);
    await expect(mobileNavigation.locator('a[href="/app/workspaces"]')).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(mobileNavigation).toHaveAttribute('aria-hidden', 'true');
    await expect(toggle).toBeFocused();
  });

  test('right panel opens as a drawer on mobile and returns focus when closed', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/app/workspaces');

    await waitForWorkspaceShellReady(page, { mobile: true });
    const trigger = page.getByTestId('right-panel-toggle');
    await trigger.click();

    const panel = page.getByTestId('right-panel');
    await expect(panel).toBeVisible();
    await expect(panel).toHaveAttribute('role', 'dialog');
    await expect(trigger).toHaveAttribute('aria-expanded', 'true');

    await page.keyboard.press('Escape');
    await expect(trigger).toHaveAttribute('aria-expanded', 'false');
    await expect(trigger).toBeFocused();
  });

  test('allows keyboard traversal to primary shell areas', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/app/workspaces');

    await waitForWorkspaceShellReady(page);
    await pressTabUntilFocused(page, page.locator('a[href="/app/workspaces"]').first());
    await pressTabUntilFocused(page, page.getByTestId('workspace-switcher'));
    await pressTabUntilFocused(page, page.getByTestId('workspace-members-action'));
    await pressTabUntilFocused(page, page.getByTestId('right-panel-toggle'));
    await pressTabUntilFocused(page, page.getByTestId('account-action'));
    await pressTabUntilFocused(page, page.getByTestId('logout-action'));
  });

  test('keeps Workspace and global header actions keyboard reachable at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    await page.goto('/app/workspaces');
    await waitForWorkspaceShellReady(page, { mobile: true });

    const controls = [
      page.getByTestId('workspace-switcher'),
      page.getByTestId('workspace-members-action'),
      page.getByTestId('right-panel-toggle'),
      page.getByTestId('account-action'),
      page.getByTestId('logout-action')
    ];
    for (const control of controls) {
      await expect(control).toBeVisible();
      await pressTabUntilFocused(page, control, 20);
    }
    await expectNoDocumentHorizontalOverflow(page);
  });

  test('icon-only shell controls have accessible names', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/app/workspaces');

    await waitForWorkspaceShellReady(page, { mobile: true });
    await expect(page.getByTestId('mobile-nav-toggle')).toHaveAccessibleName(/menu|\u30e1\u30cb\u30e5\u30fc/i);
    await page.getByTestId('right-panel-toggle').click();
    await expect(page.getByTestId('right-panel-close')).toBeVisible();
    await expect(page.getByTestId('right-panel-close')).toHaveAccessibleName(/close right panel/i);
  });

  for (const route of coreResponsiveRoutes) {
    test(`does not horizontally overflow at 320px: ${route}`, async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 800 });
      await page.goto(route);
      await expectHealthyAngularPage(page);
      await expectNoDocumentHorizontalOverflow(page);
    });
  }

  test('renders permission-denied shared state without session details', async ({ page }) => {
    await page.goto('/app/permission-denied');

    await expect(page.locator('app-root')).toBeVisible();
    await expect(page.getByTestId('permission-denied-state')).toBeVisible();
    await expect(page.locator('app-shell')).toHaveCount(0);

    const body = page.locator('body');
    await expect(body).not.toContainText('Mock User A');
    await expect(body).not.toContainText('mock-user-a@example.invalid');
    await expect(body).not.toContainText('Support User');
    await expectHealthyAngularPage(page);
  });

  test('keeps long Project and Task context perceivable on a direct route at 320px', async ({ page }) => {
    const projectId = 'static-project-context';
    const taskId = 'static-task-context';
    const projectTitle = 'A very long parent Project title that must remain identifiable on the narrow Task detail hierarchy';
    const taskTitle = 'A very long current Task title that must remain identifiable on the narrow Task detail hierarchy';
    const api = await installDirectTaskContextApi(page, { projectId, projectTitle, taskId, taskTitle });
    await page.setViewportSize({ width: 320, height: 900 });

    await page.goto(`/app/projects/${projectId}/tasks/${taskId}`);

    const hierarchy = page.getByRole('navigation', { name: 'Project and task hierarchy' });
    const parentProject = page.getByTestId('parent-project-link');
    const currentTask = hierarchy.locator('[aria-current="page"]');
    await expect(hierarchy).toBeVisible();
    await expect(parentProject).toHaveText(projectTitle);
    await expect(parentProject).toHaveAttribute('title', projectTitle);
    await expect(parentProject).toHaveAttribute('href', `/app/projects/${projectId}`);
    await expect(currentTask).toHaveText(taskTitle);
    await expect(currentTask).toHaveAttribute('title', taskTitle);
    await expect(page.getByRole('heading', { level: 1, name: taskTitle })).toBeVisible();
    await expect(page.getByTestId('project-context').getByRole('heading', { name: projectTitle })).toBeVisible();

    const progress = page.getByTestId('task-progress-phase');
    await expect(progress.getByTestId('task-current-phase')).toHaveText('In progress');
    await expect(progress.getByText('Running', { exact: true })).toBeVisible();
    await expect(progress).not.toContainText('%');
    await expect(progress).not.toContainText('Failed');

    const activity = page.getByTestId('task-activity-log');
    const activitySummary = activity.locator('summary');
    await expect(activity).not.toHaveAttribute('open', '');
    expect(api.activityRequests()).toBe(0);
    await activitySummary.focus();
    await page.keyboard.press('Enter');
    await expect(activity).toHaveAttribute('open', '');
    await expect(activity.getByText('Status update', { exact: true })).toBeVisible();
    await expect(activity.getByText('Needs attention', { exact: true })).toBeVisible();
    await expect(activity).not.toContainText('Failed');
    await expect(activity.locator('li').first()).toHaveClass(/task-detail-page__activity-item--status/);
    await expect(activity.locator('time').first()).toHaveAttribute('datetime', '2026-08-24T03:00:00Z');
    expect(api.activityRequests()).toBe(1);
    await activity.getByRole('button', { name: 'Load more activity' }).click();
    await expect(activity.getByText('Decision recorded after review.', { exact: true })).toBeVisible();
    expect(api.activityRequests()).toBe(2);

    const [hierarchyBox, projectBox, taskBox] = await Promise.all([
      hierarchy.boundingBox(),
      parentProject.boundingBox(),
      currentTask.boundingBox()
    ]);
    expect(hierarchyBox).not.toBeNull();
    expect(projectBox).not.toBeNull();
    expect(taskBox).not.toBeNull();
    for (const contextBox of [projectBox!, taskBox!]) {
      expect(contextBox.width).toBeGreaterThan(24);
      expect(contextBox.x).toBeGreaterThanOrEqual(hierarchyBox!.x - 1);
      expect(contextBox.x + contextBox.width).toBeLessThanOrEqual(hierarchyBox!.x + hierarchyBox!.width + 1);
    }
    expect(taskBox!.y).toBeGreaterThan(projectBox!.y);

    expect(api.projectListRequests()).toBe(0);
    expect(api.parentProjectRequests()).toBe(1);
    expect(api.parentProjectAttempts()).toBeGreaterThanOrEqual(1);
    expect(api.parentProjectAttempts()).toBeLessThanOrEqual(2);
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
    await expectHealthyAngularPage(page);
  });

  test('keeps the server-authorized Task execution policy responsive without offering a runtime action', async ({ page }) => {
    const projectId = 'static-project-execution-scope';
    const taskId = 'static-task-execution-scope';
    await installDirectTaskContextApi(page, {
      projectId,
      projectTitle: 'Execution policy Project',
      taskId,
      taskTitle: 'Execution policy Task',
    });
    const executionScopeApi = await installTaskExecutionScopeApi(page, {
      projectId,
      taskId,
      projectPolicy: { webEnabled: false, projectFilesEnabled: true, version: 8, canManage: true },
      taskPolicy: {
        origin: 'TaskOverride',
        webEnabled: true,
        projectFilesEnabled: false,
        overrideVersion: 5,
        canManage: true,
        latestRun: {
          status: 'Accepted',
          snapshotScopeOrigin: 'ProjectDefault',
          snapshotWebEnabled: false,
          snapshotProjectFilesEnabled: true,
        },
      },
    });
    await page.setViewportSize({ width: 320, height: 900 });

    await page.goto(`/app/projects/${projectId}/tasks/${taskId}`);

    const scope = page.getByTestId('task-execution-scope');
    await expect(scope).toBeVisible();
    const contextSummary = scope.getByTestId('task-context-summary');
    await expect(contextSummary).toBeVisible();
    await expect(contextSummary).toContainText('1 of 2 source kinds allowed');
    await expect(contextSummary.getByTestId('task-context-summary-origin')).toContainText('Task override');
    await expect(contextSummary.getByTestId('task-context-summary-web')).toHaveText('Web: Allow');
    await expect(contextSummary.getByTestId('task-context-summary-files')).toHaveText('Project files: Exclude');
    await expect(contextSummary).toContainText('not a file, site, or app inventory count');
    await contextSummary.focus();
    await page.keyboard.press('Enter');
    await expect(scope.getByTestId('task-context-details')).toBeFocused();
    await expect(scope.getByTestId('task-execution-scope-origin')).toHaveText('Task override');
    await expect(scope.getByTestId('task-execution-scope-web')).toHaveText('Enabled');
    await expect(scope.getByTestId('task-execution-scope-web-policy')).toHaveText('Allow — eligible under this Task scope');
    await expect(scope.getByTestId('task-execution-scope-files')).toHaveText('Disabled');
    await expect(scope.getByTestId('task-execution-scope-files-policy')).toHaveText('Exclude — not eligible under this Task scope');
    await expect(scope.getByTestId('task-execution-scope-future-only')).toContainText('future run requests only');
    await expect(scope.getByTestId('task-execution-runtime-contract')).toContainText('Web execution is disabled');
    await expect(scope.getByTestId('task-execution-snapshot')).toContainText('Project default');
    await expect(scope.getByTestId('task-execution-snapshot')).toContainText('Execution request was durably accepted.');
    await expect(scope.getByRole('heading', { name: 'Source settings' })).toBeVisible();

    // This remains a policy-only UI. It deliberately has no source inventory,
    // provider picker, URL input, execution action, or browser-originated
    // runtime request from this screen.
    await expect(scope.getByRole('button', { name: /start|run|execute/i })).toHaveCount(0);
    await expect(scope.locator('a[href^="http"]')).toHaveCount(0);
    await expect(scope.locator('input[type="url"], input[type="text"], textarea, select')).toHaveCount(0);
    await expect(scope.getByTestId('task-execution-runtime-contract')).toContainText('Execution provider: First-party Project Files V1');

    const projectWebPolicy = scope.getByLabel(/Allow Web as a future source/).first();
    await projectWebPolicy.check();
    await scope.getByRole('button', { name: 'Save project default' }).click();
    await expect(scope.getByTestId('task-execution-scope-feedback')).toContainText('Project default source settings saved.');
    expect(executionScopeApi.projectDefaultUpdates()).toEqual([
      { webEnabled: true, projectFilesEnabled: true, expectedVersion: 8 },
    ]);
    expect(executionScopeApi.runtimeRequests()).toEqual([]);
    await expect(scope.getByTestId('task-execution-scope-origin')).toHaveText('Task override');
    await expect(scope.getByTestId('task-execution-scope-web')).toHaveText('Enabled');
    await expect(scope.getByTestId('task-execution-scope-web-policy')).toHaveText('Allow — eligible under this Task scope');
    await expect(scope.getByTestId('task-execution-scope-files')).toHaveText('Disabled');
    await expect(scope.getByTestId('task-execution-scope-files-policy')).toHaveText('Exclude — not eligible under this Task scope');

    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="task-execution-scope"]');
    await expectHealthyAngularPage(page);
  });

  test('edits and reviews the Task Brief in stable order without narrow-screen overflow', async ({ page }) => {
    await page.addInitScript((storageKey) => globalThis.localStorage.setItem(storageKey, 'light'), themeStorageKey);
    const projectId = 'static-project-brief';
    const taskId = 'static-task-brief';
    const api = await installDirectTaskContextApi(page, {
      projectId,
      projectTitle: 'Authorized Project context',
      taskId,
      taskTitle: 'Structured Task Brief',
      canEdit: true,
      goal: 'Reach editorial review',
      deliverable: 'Review-ready package',
      constraints: 'Keep-the-existing-public-URL-stable-without-breaking-long-unspaced-identifiers'
    });
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto(`/app/projects/${projectId}/tasks/${taskId}`);

    const brief = page.getByTestId('task-brief-fields');
    await expect(brief).toBeVisible();
    await expect(page.getByTestId('task-brief-goal-source')).toHaveText('Task-specific');
    await expect(page.getByTestId('task-brief-deliverable-source')).toHaveText('Task-specific');
    const reviewLabels = await page.getByTestId('task-brief-review').locator('dt').allTextContents();
    expect(reviewLabels).toEqual(['Goal', 'Deliverable', 'Constraints']);

    await page.getByTestId('task-brief-goal-input').fill('Reach final approval');
    await page.getByTestId('task-brief-deliverable-input').fill('');
    await page.getByTestId('task-brief-constraints-input').fill('Preserve the authorized Project boundary');
    await expect(page.getByTestId('task-brief-deliverable-source')).toHaveText('Not set');
    await expect(page.getByTestId('task-brief-review-deliverable')).toContainText('Not set');
    await page.getByTestId('task-save-button').click();

    await expect.poll(() => api.patchBodies().length).toBe(1);
    expect(api.patchBodies()[0]).toMatchObject({
      description: 'Task-specific direct-route context.',
      goal: 'Reach final approval',
      deliverable: null,
      constraints: 'Preserve the authorized Project boundary'
    });
    await expect(page.getByTestId('task-save-success')).toBeVisible();
    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="task-brief-fields"]');
    await page.getByTestId('theme-toggle').click();
    await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', 'dark');
    await expectNoAccessibilityViolations(page, '[data-testid="task-brief-fields"]');
    await expectHealthyAngularPage(page);
  });

  test('keeps Activity errors distinct from confirmed empty state and retains earlier pages', async ({ page }, testInfo) => {
    const projectId = 'static-project-activity-errors';
    const taskId = 'static-task-activity-errors';
    await installDirectTaskContextApi(
      page,
      { projectId, projectTitle: 'Activity error Project', taskId, taskTitle: 'Activity error Task' },
      {
        failFirstActivityPageOnce: true,
        failSecondActivityPageOnce: true,
        activityFailureStatus: testInfo.project.name === 'chromium-mobile' ? 409 : 500
      }
    );
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto(`/app/projects/${projectId}/tasks/${taskId}`);

    const activity = page.getByTestId('task-activity-log');
    await activity.locator('summary').click();
    let alert = activity.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(activity).not.toContainText('No Task activity has been recorded.');
    await expect(activity).not.toContainText('0 recorded');
    await alert.getByRole('button', { name: /Retry|Reload/ }).click();

    await expect(activity.getByText('Status update', { exact: true })).toBeVisible();
    await activity.getByRole('button', { name: 'Load more activity' }).click();
    alert = activity.getByRole('alert');
    await expect(alert).toBeVisible();
    await expect(activity.getByText('Implementation is ready for review.', { exact: true })).toBeVisible();
    await expect(activity).not.toContainText('No Task activity has been recorded.');
    await alert.getByRole('button', { name: /Retry|Reload/ }).click();

    await expect(activity.getByText('Decision recorded after review.', { exact: true })).toBeVisible();
    await expectNoDocumentHorizontalOverflow(page);
    await expectHealthyAngularPage(page);
  });

  test('uses the canonical Project Kanban for pointer, keyboard, conflict, rollback, and narrow flows', async ({ page }, testInfo) => {
    const api = await installProjectKanbanApi(page);
    if (testInfo.project.name === 'chromium-mobile') {
      await page.setViewportSize({ width: 390, height: 844 });
    } else {
      await page.setViewportSize({ width: 1280, height: 900 });
    }

    await page.goto('/app/projects/static-project-kanban');

    await expect(page.getByTestId('project-detail-page')).toBeVisible();
    await page.getByRole('tab', { name: 'Tasks', exact: true }).click();
    await expect(page.getByTestId('coglatas-kanban-board')).toBeVisible();
    await expect(page.getByText('Warning: WIP limit 1 exceeded.')).toBeVisible();
    await expect(page.getByText('Parent summary task')).toBeVisible();
    await expect(page.getByText('Derived progress: 50%')).toBeVisible();
    await expect(page.getByText('Derived dates: 2026-07-01 to 2026-07-31')).toBeVisible();
    await expect(page.getByText('Priority: High')).toBeVisible();
    await expect(page.getByText('Blocked', { exact: true })).toBeVisible();
    await expect(page.getByText(/Done shows 30 recent days/)).toBeVisible();

    const columns = page.locator('.coglatas-kanban__column');
    if (testInfo.project.name === 'chromium-mobile') {
      const todoBox = await columns.nth(0).boundingBox();
      const doneBox = await columns.nth(1).boundingBox();
      expect(todoBox).not.toBeNull();
      expect(doneBox).not.toBeNull();
      expect(doneBox!.y).toBeGreaterThan(todoBox!.y + todoBox!.height - 1);
      await expectNoDocumentHorizontalOverflow(page);
    }

    let card = page.locator('[data-kanban-card-id="static-task-kanban"]');
    const doneColumn = columns.filter({ has: page.getByRole('heading', { name: 'Done', exact: true }) });
    if (testInfo.project.name === 'chromium-desktop') {
      await card.dragTo(doneColumn);
    } else {
      await card.getByRole('button', { name: 'Move', exact: true }).click();
      await card.getByLabel('Target stage').selectOption('stage-done');
      await card.getByRole('button', { name: 'Apply move' }).click();
    }

    await expect(doneColumn.locator('[data-kanban-card-id="static-task-kanban"]')).toBeVisible();
    await expect(page.getByText('Move saved.', { exact: true })).toBeVisible();
    expect(api.moveBodies).toHaveLength(1);
    expect(api.moveBodies[0]).toMatchObject({
      targetWorkflowStageId: 'stage-done',
      expectedTaskVersion: 3,
      expectedBoardVersion: 7
    });
    expect(api.csrfHeaders).toEqual(['csrf-kanban']);

    card = doneColumn.locator('[data-kanban-card-id="static-task-kanban"]');
    await card.getByRole('button', { name: 'Move', exact: true }).click();
    await card.getByLabel('Target stage').selectOption('stage-todo');
    await card.getByRole('button', { name: 'Apply move' }).click();

    await expect(
      page.getByRole('region', { name: 'Canonical Project Task Kanban' })
        .getByRole('status')
        .filter({ hasText: 'Conflict resolved from the authoritative Project board.' })
    ).toBeVisible();
    card = doneColumn.locator('[data-kanban-card-id="static-task-kanban"]');
    await expect(card).toBeVisible();
    await expect(card).toBeFocused();

    await card.getByRole('button', { name: 'Move', exact: true }).click();
    await card.getByLabel('Target stage').selectOption('stage-todo');
    await card.getByRole('button', { name: 'Apply move' }).click();

    await expect(page.getByRole('alert')).toContainText('Move denied and rolled back.');
    await expect(doneColumn.locator('[data-kanban-card-id="static-task-kanban"]')).toBeVisible();
    expect(api.moveBodies).toHaveLength(3);
    expect(api.csrfHeaders).toEqual(['csrf-kanban', 'csrf-kanban', 'csrf-kanban']);

    await doneColumn.getByRole('button', { name: 'Open details' }).click();
    await expect(page).toHaveURL(/\/app\/projects\/static-project-kanban\/tasks\/static-task-kanban$/);
    expect(api.moveBodies).toHaveLength(3);
  });

  test('collects a cancellation reason before a pointer drag submits the canonical move', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-desktop', 'Pointer drag remediation is covered by the desktop browser project.');
    const api = await installProjectKanbanApi(page);
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/app/projects/static-project-kanban');
    await page.getByRole('tab', { name: 'Tasks', exact: true }).click();

    const card = page.locator('[data-kanban-card-id="static-task-kanban"]');
    const todoColumn = page.locator('.coglatas-kanban__column')
      .filter({ has: page.getByRole('heading', { name: 'Todo', exact: true }) });
    const cancelledColumn = page.locator('.coglatas-kanban__column')
      .filter({ has: page.getByRole('heading', { name: 'Cancelled', exact: true }) });

    await card.dragTo(cancelledColumn);

    await expect(todoColumn.locator('[data-kanban-card-id="static-task-kanban"]')).toBeVisible();
    await expect(cancelledColumn.locator('[data-kanban-card-id="static-task-kanban"]')).toHaveCount(0);
    await expect(card.getByLabel('Target stage')).toHaveValue('stage-cancelled');
    await expect(card.getByLabel('Position')).toHaveValue('end');
    const applyMove = card.getByRole('button', { name: 'Apply move' });
    await expect(applyMove).toBeDisabled();
    expect(api.moveBodies).toHaveLength(0);

    await card.getByLabel('Reason').fill('Cancelled after stakeholder review.');
    await expect(applyMove).toBeEnabled();
    await applyMove.click();

    await expect(cancelledColumn.locator('[data-kanban-card-id="static-task-kanban"]')).toBeVisible();
    await expect(page.getByText('Move saved.', { exact: true })).toBeVisible();
    expect(api.moveBodies).toHaveLength(1);
    expect(api.moveBodies[0]).toMatchObject({
      targetWorkflowStageId: 'stage-cancelled',
      targetBeforeTaskId: null,
      targetAfterTaskId: null,
      reason: 'Cancelled after stakeholder review.'
    });
    expect(api.csrfHeaders).toEqual(['csrf-kanban']);
  });

  test('keeps the maintained Project Task List when tasks.kanbanV1 is disabled', async ({ page }, testInfo) => {
    await page.addInitScript(() => {
      (window as Window & { __AIP_FEATURE_FLAGS__?: Record<string, boolean> }).__AIP_FEATURE_FLAGS__ = {
        'tasks.kanbanV1': false
      };
    });
    const api = await installProjectKanbanApi(page);

    await page.goto('/app/projects/static-project-kanban');
    await page.getByRole('tab', { name: 'Tasks', exact: true }).click();

    await expect(page.getByText('Project Kanban is disabled. The maintained Task List remains available.')).toBeVisible();
    const renderer = testInfo.project.name === 'chromium-mobile' ? 'mobile' : 'desktop';
    await expect(page.getByTestId(`task-state-${renderer === 'mobile' ? 'card' : 'row'}-static-task-kanban-${renderer}`)).toBeVisible();
    await expect(page.locator('coglatas-kanban')).toHaveCount(0);
    expect(api.kanbanGetCount()).toBe(0);
  });

  test('makes canonical Task state, update time, blocking, and Artifact availability scannable at 320px', async ({ page }, testInfo) => {
    const api = await installProjectKanbanApi(page);
    const mobile = testInfo.project.name === 'chromium-mobile';
    await page.setViewportSize(mobile ? { width: 320, height: 900 } : { width: 1280, height: 900 });

    await page.goto('/app/projects/static-project-kanban');
    await page.getByRole('tab', { name: 'List', exact: true }).click();

    const suffix = mobile ? 'mobile' : 'desktop';
    await expect(page.getByTestId('task-state-list')).toBeVisible();
    await expect(page.getByTestId(`task-stage-name-static-task-running-${suffix}`)).toContainText('Investigating');
    await expect(page.getByTestId(`task-category-static-task-running-${suffix}`)).toContainText('Running');
    await expect(page.getByTestId(`task-category-static-task-review-${suffix}`)).toContainText('Needs review');
    await expect(page.getByTestId(`task-category-static-task-completed-${suffix}`)).toContainText('Completed');
    await expect(page.getByTestId(`task-category-static-task-cancelled-${suffix}`)).toContainText('Cancelled');
    await expect(page.getByTestId(`task-blocked-static-task-kanban-${suffix}`)).toContainText('Blocked');
    await expect(page.getByTestId(`task-artifact-static-task-review-${suffix}`)).toContainText('Artifact available');
    await expect(page.getByTestId(`task-artifact-static-task-running-${suffix}`)).toContainText('No artifact');
    await expect(page.getByTestId(`task-updated-static-task-running-${suffix}`).locator('time'))
      .toHaveAttribute('datetime', '2026-08-21T08:15:00Z');
    await expect(page.getByTestId(`task-updated-static-task-review-${suffix}`).locator('time'))
      .toHaveAttribute('datetime', '2026-08-24T02:45:00Z');

    const openAction = page.getByTestId(`task-openDetail-static-task-review-${suffix}`);
    await expect(openAction).toBeVisible();
    await openAction.focus();
    await expect(openAction).toBeFocused();
    expect(api.taskListGetCount()).toBe(1);

    if (mobile) {
      const card = page.getByTestId('task-state-card-static-task-review-mobile');
      const cardBox = await card.boundingBox();
      const actionBox = await openAction.boundingBox();
      expect(cardBox).not.toBeNull();
      expect(actionBox).not.toBeNull();
      expect(cardBox!.x).toBeGreaterThanOrEqual(0);
      expect(cardBox!.x + cardBox!.width).toBeLessThanOrEqual(320);
      expect(actionBox!.height).toBeGreaterThanOrEqual(44);
    }

    await expectNoDocumentHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page);
    await expectHealthyAngularPage(page);
  });

  test('refreshes the Task state list from authoritative HTTP after a stage change', async ({ page }, testInfo) => {
    const api = await installProjectKanbanApi(page);
    await page.setViewportSize(testInfo.project.name === 'chromium-mobile'
      ? { width: 390, height: 844 }
      : { width: 1280, height: 900 });
    await page.goto('/app/projects/static-project-kanban');
    await page.getByRole('tab', { name: 'Tasks', exact: true }).click();

    const card = page.locator('[data-kanban-card-id="static-task-kanban"]');
    await card.getByRole('button', { name: 'Move', exact: true }).click();
    await card.getByLabel('Target stage').selectOption('stage-done');
    await card.getByRole('button', { name: 'Apply move' }).click();
    await expect(page.getByText('Move saved.', { exact: true })).toBeVisible();

    await expect.poll(api.taskListGetCount).toBeGreaterThan(1);
    await page.getByRole('tab', { name: 'List', exact: true }).click();
    const suffix = testInfo.project.name === 'chromium-mobile' ? 'mobile' : 'desktop';
    await expect(page.getByTestId(`task-stage-name-static-task-kanban-${suffix}`)).toContainText('Complete');
    await expect(page.getByTestId(`task-category-static-task-kanban-${suffix}`)).toContainText('Completed');
    await expect(page.getByTestId(`task-updated-static-task-kanban-${suffix}`).locator('time'))
      .toHaveAttribute('datetime', '2026-08-24T10:15:00Z');
    await expect(page.getByTestId(`task-artifact-static-task-kanban-${suffix}`)).toContainText('Artifact available');
    await expectHealthyAngularPage(page);
  });

  test('does not render protected Kanban data after an authorization denial', async ({ page }) => {
    const api = await installProjectKanbanApi(page, { denySnapshot: true });

    await page.goto('/app/projects/static-project-kanban');
    await page.getByRole('tab', { name: 'Tasks', exact: true }).click();

    await expect(page.getByText('Project Kanban is not available.')).toBeVisible();
    await expect(page.locator('[data-kanban-card-id]')).toHaveCount(0);
    await expect(page.locator('body')).not.toContainText('restricted-board-secret');
    expect(api.kanbanGetCount()).toBe(1);
  });

  test('uses the canonical Schedule tab forms for desktop and the 320px mobile projection', async ({ page }, testInfo) => {
    await page.addInitScript(() => {
      (window as Window & { __AIP_FEATURE_FLAGS__?: Record<string, boolean> }).__AIP_FEATURE_FLAGS__ = {
        'tasks.ganttV1': true
      };
    });
    const api = await installProjectGanttApi(page);
    const mobile = testInfo.project.name === 'chromium-mobile';
    await page.setViewportSize(mobile ? { width: 320, height: 800 } : { width: 1280, height: 900 });

    await page.goto('/app/projects/static-project-gantt');
    await page.getByRole('tab', { name: 'Schedule', exact: true }).click();

    await expect(page.getByTestId('project-schedule')).toBeVisible();
    await expect(page.getByTestId('coglatas-gantt-projection')).toBeVisible();
    await expect(page.getByText('Workspace timezone:')).toContainText('Asia/Tokyo');
    await expect(page.getByText('Derived parent Task', { exact: true })).toBeVisible();
    await expect(page.getByText('50% (derived)', { exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Schedule warnings' })
      .getByText(/DEPENDENCY_VIOLATION/)).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Milestones', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Unscheduled work', exact: true })).toBeVisible();

    if (mobile) {
      await expect(page.getByRole('heading', { name: 'Timeline chart', exact: true })).toHaveCount(0);
      await expectNoDocumentHorizontalOverflow(page);
    } else {
      await expect(page.getByRole('heading', { name: 'Timeline chart', exact: true })).toBeVisible();
      await expect(page.getByTestId('coglatas-syncfusion-gantt')).toBeVisible();
      await expect(page.getByTestId('coglatas-gantt-vendor-error')).not.toBeVisible();
    }

    let scheduleItem = ganttItem(page, 'task-gantt-schedule');
    const scheduleEdit = scheduleItem.getByRole('button', { name: 'Edit dates', exact: true });
    await scheduleEdit.click();
    await page.getByLabel('Planned start').fill('2026-08-03');
    await page.getByLabel('Planned end').fill('2026-08-08');
    await page.getByRole('button', { name: 'Apply schedule' }).click();
    scheduleItem = ganttItem(page, 'task-gantt-schedule');
    await expect(scheduleItem).toContainText('2026-08-03 to 2026-08-08');
    await expectLogicalGanttFocus(scheduleItem);

    await scheduleItem.getByRole('button', { name: 'Edit progress', exact: true }).click();
    await page.getByLabel('Progress percent').fill('40');
    await page.getByRole('button', { name: 'Apply progress' }).click();
    await expect(ganttItem(page, 'task-gantt-schedule')).toContainText('40%');

    const milestone = ganttItem(page, 'milestone-gantt-release');
    await milestone.getByRole('button', { name: 'Edit Milestone date', exact: true }).click();
    await page.getByRole('dialog', { name: 'Edit Milestone date' })
      .locator('input[name="milestoneDate"]')
      .fill('2026-08-31');
    await page.getByRole('button', { name: 'Apply schedule' }).click();
    await expect(ganttItem(page, 'milestone-gantt-release')).toContainText('2026-08-31');

    const successor = ganttItem(page, 'task-gantt-successor');
    await successor.getByRole('button', { name: 'Add FS predecessor', exact: true }).click();
    await page.getByLabel('Finish-to-Start predecessor').selectOption('task-gantt-predecessor');
    await page.getByRole('button', { name: 'Add dependency' }).click();
    const addedDependency = page.locator('[data-gantt-dependency-id="dependency-gantt-added"]');
    await expect(addedDependency).toContainText('Predecessor task');
    await expect(addedDependency).toContainText('Dependency successor');

    await addedDependency.getByRole('button', { name: 'Remove FS dependency' }).click();
    await page.getByRole('button', { name: 'Remove dependency', exact: true }).click();
    await expect(page.locator('[data-gantt-dependency-id="dependency-gantt-added"]')).toHaveCount(0);

    scheduleItem = ganttItem(page, 'task-gantt-schedule');
    await scheduleItem.getByRole('button', { name: 'Move to unscheduled', exact: true }).click();
    await page.getByRole('button', { name: 'Clear schedule', exact: true }).click();
    const unscheduled = page.getByRole('heading', { name: 'Unscheduled work', exact: true })
      .locator('..');
    await expect(unscheduled.locator('[data-gantt-item-id="task-gantt-schedule"]')).toBeVisible();
    await expect(ganttItem(page, 'task-gantt-schedule')).toContainText('Unscheduled');

    const requestsBeforeCancel = api.commandBodies.length;
    await ganttItem(page, 'task-gantt-unscheduled').getByRole('button', { name: 'Edit dates', exact: true }).click();
    await page.getByLabel('Planned start').press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(api.commandBodies).toHaveLength(requestsBeforeCancel);
    await expectLogicalGanttFocus(ganttItem(page, 'task-gantt-unscheduled'));

    expect(api.commandBodies).toEqual(expect.arrayContaining([
      expect.objectContaining({ kind: 'schedule', taskId: 'task-gantt-schedule', plannedStartDate: '2026-08-03', plannedEndDate: '2026-08-08' }),
      expect.objectContaining({ kind: 'progress', taskId: 'task-gantt-schedule', progressPercent: 40 }),
      expect.objectContaining({ kind: 'schedule', taskId: 'milestone-gantt-release', milestoneDate: '2026-08-31' }),
      expect.objectContaining({ kind: 'addDependency', successorTaskId: 'task-gantt-successor', predecessorTaskId: 'task-gantt-predecessor' }),
      expect.objectContaining({ kind: 'removeDependency', successorTaskId: 'task-gantt-successor', dependencyId: 'dependency-gantt-added' }),
      expect.objectContaining({ kind: 'schedule', taskId: 'task-gantt-schedule', plannedStartDate: null, plannedEndDate: null })
    ]));
    expect(api.csrfHeaders.every((header) => header === 'csrf-gantt')).toBe(true);
    expect(api.ganttGetCount()).toBeGreaterThan(1);
  });

  test('keeps the maintained read-only Schedule projection when tasks.ganttV1 is disabled', async ({ page }) => {
    await page.addInitScript(() => {
      (window as Window & { __AIP_FEATURE_FLAGS__?: Record<string, boolean> }).__AIP_FEATURE_FLAGS__ = {
        'tasks.ganttV1': false
      };
    });
    const api = await installProjectGanttApi(page);

    await page.goto('/app/projects/static-project-gantt');
    await page.getByRole('tab', { name: 'Schedule', exact: true }).click();

    await expect(page.getByText(
      'Canonical Gantt presentation is disabled. This maintained read-only list is derived from the same authoritative HTTP snapshot.',
      { exact: true }
    )).toBeVisible();
    await expect(page.getByText(/Schedule is read-only because the current API/)).toBeVisible();
    await expect(page.getByText(/Canonical schedule task/)).toBeVisible();
    await expect(page.locator('[data-gantt-item-id]')).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Edit dates|Edit progress|Add FS predecessor/ })).toHaveCount(0);
    expect(api.ganttGetCount()).toBe(1);
    expect(api.commandBodies).toHaveLength(0);
  });

  test('matches approved Angular P0 screenshot baselines', async ({ page }, testInfo) => {
    if (testInfo.project.name === 'chromium-desktop') {
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto('/app/workspaces');
      await waitForWorkspaceShellReady(page);
      await expectStableScreenshot(page, testInfo, 'desktop-shell-workspaces.png', {
        maxDiffPixelRatio: approvedThemeMigrationDiffRatio.desktop
      });
      return;
    }

    if (testInfo.project.name === 'chromium-mobile') {
      await page.setViewportSize({ width: 390, height: 844 });
      await page.goto('/app/workspaces');
      await waitForWorkspaceShellReady(page, { mobile: true });
      await page.getByTestId('mobile-nav-toggle').click();
      await expect(page.getByTestId('mobile-navigation')).toHaveAttribute('aria-hidden', 'false');
      await expectStableScreenshot(page, testInfo, 'mobile-shell-workspaces-drawer.png', {
        fullPage: false,
        maxDiffPixelRatio: approvedThemeMigrationDiffRatio.mobile
      });
      return;
    }

    throw new Error(`Unexpected Playwright project for Angular screenshots: ${testInfo.project.name}`);
  });
});

async function installProjectGanttApi(page: Page) {
  let snapshot = projectGanttSnapshot();
  let ganttGets = 0;
  const commandBodies: Record<string, unknown>[] = [];
  const csrfHeaders: string[] = [];

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (path === '/api/security/csrf-token' && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: 'csrf-gantt', headerName: 'X-CSRF-Token' })
      });
      return;
    }

    if (path === '/api/projects/static-project-gantt' && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'static-project-gantt',
          title: 'Canonical Gantt Project',
          status: 'Active',
          startDate: '2026-07-01',
          endDate: '2026-09-30',
          uiPermissions: { canCreateTask: true }
        })
      });
      return;
    }

    if (path === '/api/projects/static-project-gantt/tasks' && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [], totalCount: 0, hasMore: false })
      });
      return;
    }

    if (path === '/api/projects/static-project-gantt/kanban' && method === 'GET') {
      const board = projectKanbanSnapshot('stage-todo', 1, 1);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...board,
          board: {
            ...board.board,
            projectId: 'static-project-gantt',
            totalAuthorizedCardCount: 0,
            warnings: []
          },
          columns: board.columns.map((column) => ({
            ...column,
            currentAuthorizedCardCount: 0,
            hasWipWarning: false
          })),
          cards: []
        })
      });
      return;
    }

    if (path === '/api/projects/static-project-gantt/gantt' && method === 'GET') {
      ganttGets += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(snapshot)
      });
      return;
    }

    if (path === '/api/projects/static-project-gantt/workload' && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ members: [] }) });
      return;
    }

    if (path === '/api/projects/static-project-gantt/members' && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
      return;
    }

    const scheduleMatch = /^\/api\/tasks\/([^/]+)\/schedule$/.exec(path);
    if (scheduleMatch && method === 'PATCH') {
      const taskId = scheduleMatch[1];
      const body = request.postDataJSON() as Record<string, unknown>;
      commandBodies.push({ kind: 'schedule', taskId, ...body });
      csrfHeaders.push(request.headers()['x-csrf-token'] ?? '');
      const item = findGanttDto(snapshot, taskId);
      expect(body['expectedVersion']).toBe(item.version);
      const updated = {
        ...item,
        plannedStartDate: body['plannedStartDate'] as string | null,
        plannedEndDate: body['plannedEndDate'] as string | null,
        milestoneDate: body['milestoneDate'] as string | null,
        version: item.version + 1
      };
      if (updated.kind === 'Task' && updated.plannedStartDate === null && updated.plannedEndDate === null) {
        updated.warnings = mergeWarningDto(updated.warnings, ganttWarningDto(
          'UNSCHEDULED',
          'Task is unscheduled.',
          'Task',
          updated.taskId
        ));
      } else {
        updated.warnings = updated.warnings.filter((warning) => warning.code !== 'UNSCHEDULED');
      }
      snapshot = replaceGanttDto(snapshot, updated);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(ganttCommandDto(updated))
      });
      return;
    }

    const progressMatch = /^\/api\/tasks\/([^/]+)\/progress$/.exec(path);
    if (progressMatch && method === 'PATCH') {
      const taskId = progressMatch[1];
      const body = request.postDataJSON() as Record<string, unknown>;
      commandBodies.push({ kind: 'progress', taskId, ...body });
      csrfHeaders.push(request.headers()['x-csrf-token'] ?? '');
      const item = findGanttDto(snapshot, taskId);
      expect(body['expectedVersion']).toBe(item.version);
      const updated = {
        ...item,
        progressPercent: Number(body['progressPercent']),
        version: item.version + 1
      };
      snapshot = replaceGanttDto(snapshot, updated);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(ganttCommandDto(updated))
      });
      return;
    }

    const dependencyAddMatch = /^\/api\/tasks\/([^/]+)\/dependencies$/.exec(path);
    if (dependencyAddMatch && method === 'POST') {
      const successorTaskId = dependencyAddMatch[1];
      const body = request.postDataJSON() as Record<string, unknown>;
      commandBodies.push({
        kind: 'addDependency',
        successorTaskId,
        predecessorTaskId: body['predecessorTaskId'],
        expectedVersion: body['expectedVersion']
      });
      csrfHeaders.push(request.headers()['x-csrf-token'] ?? '');
      const successor = findGanttDto(snapshot, successorTaskId);
      expect(body['expectedVersion']).toBe(successor.version);
      const version = successor.version + 1;
      snapshot = replaceGanttDto(snapshot, { ...successor, version });
      snapshot = {
        ...snapshot,
        projectVersion: snapshot.projectVersion + 1,
        dependencies: [...snapshot.dependencies, {
          dependencyId: 'dependency-gantt-added',
          predecessorTaskId: String(body['predecessorTaskId']),
          successorTaskId,
          type: 'FinishToStart',
          editable: true,
          version,
          warnings: []
        }]
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'dependency-gantt-added',
          predecessorTaskId: String(body['predecessorTaskId']),
          successorTaskId,
          dependencyType: 'FinishToStart',
          createdAt: '2026-07-30T00:00:00Z',
          version,
          editable: true,
          warnings: []
        })
      });
      return;
    }

    const dependencyRemoveMatch = /^\/api\/tasks\/([^/]+)\/dependencies\/([^/]+)$/.exec(path);
    if (dependencyRemoveMatch && method === 'DELETE') {
      const [, successorTaskId, dependencyId] = dependencyRemoveMatch;
      const successor = findGanttDto(snapshot, successorTaskId);
      expect(url.searchParams.get('expectedVersion')).toBe(String(successor.version));
      commandBodies.push({
        kind: 'removeDependency',
        successorTaskId,
        dependencyId,
        expectedVersion: successor.version
      });
      csrfHeaders.push(request.headers()['x-csrf-token'] ?? '');
      snapshot = replaceGanttDto(snapshot, { ...successor, version: successor.version + 1 });
      snapshot = {
        ...snapshot,
        projectVersion: snapshot.projectVersion + 1,
        dependencies: snapshot.dependencies.filter((dependency) => dependency.dependencyId !== dependencyId)
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ status: 'OK' })
      });
      return;
    }

    await route.fallback();
  });

  return {
    commandBodies,
    csrfHeaders,
    ganttGetCount: () => ganttGets
  };
}

type MockGanttWarning = {
  code: string;
  message: string;
  severity: 'Info' | 'Warning';
  targetType: string;
  targetId: string | null;
  field: string | null;
  blocking: false;
};

type MockGanttItem = {
  taskId: string;
  kind: 'Task' | 'Milestone';
  parentTaskId: string | null;
  milestoneId: string | null;
  title: string;
  plannedStartDate: string | null;
  plannedEndDate: string | null;
  milestoneDate: string | null;
  progressPercent: number;
  progressIsDerived: boolean;
  workflowStageId: string | null;
  workflowStageName: string | null;
  stageCategory: 'Backlog' | 'Todo' | 'InProgress' | 'Review' | 'Done' | 'Cancelled';
  priority: 'Low' | 'Medium' | 'High' | 'Critical';
  isBlocked: boolean;
  primaryAssignee: { userId: string; displayName: string } | null;
  version: number;
  scheduleEditPermissions: MockGanttPermissions;
  warnings: MockGanttWarning[];
};

type MockGanttPermissions = {
  canEditSchedule: boolean;
  canEditProgress: boolean;
  canManageDependencies: boolean;
  canClearSchedule: boolean;
  canOpen: boolean;
};

type MockGanttSnapshot = {
  projectId: string;
  projectTitle: string;
  projectVersion: number;
  workflowVersion: number;
  calendarVersion: number | null;
  calendar: {
    timeZone: string;
    workingDays: string[];
    holidaysAvailable: boolean;
    limitations: string[];
  };
  scheduledItems: MockGanttItem[];
  unscheduledItems: MockGanttItem[];
  milestones: MockGanttItem[];
  dependencies: {
    dependencyId: string;
    predecessorTaskId: string;
    successorTaskId: string;
    type: 'FinishToStart' | 'StartToStart' | 'FinishToFinish' | 'StartToFinish';
    editable: boolean;
    version: number;
    warnings: MockGanttWarning[];
  }[];
  warnings: MockGanttWarning[];
  permissions: MockGanttPermissions;
  maximumItems: number;
  totalItems: number;
};

const ganttEditorPermissions: MockGanttPermissions = {
  canEditSchedule: true,
  canEditProgress: true,
  canManageDependencies: true,
  canClearSchedule: true,
  canOpen: true
};

function projectGanttSnapshot(): MockGanttSnapshot {
  const parentPermissions = {
    ...ganttEditorPermissions,
    canEditSchedule: false,
    canEditProgress: false,
    canClearSchedule: false
  };
  const milestonePermissions = {
    ...ganttEditorPermissions,
    canEditProgress: false,
    canManageDependencies: false,
    canClearSchedule: false
  };
  const parentWarning = ganttWarningDto(
    'PARENT_DERIVED',
    'Parent schedule and progress are derived from child Tasks.',
    'Task',
    'task-gantt-parent'
  );
  const dependencyWarning = ganttWarningDto(
    'DEPENDENCY_VIOLATION',
    'The successor begins before its predecessor finishes; dates were not moved.',
    'Dependency',
    'dependency-gantt-existing',
    'plannedStartDate'
  );
  const legacyWarning = ganttWarningDto(
    'LEGACY_DEPENDENCY_TYPE',
    'This legacy non-FS dependency is read-only.',
    'Dependency',
    'dependency-gantt-legacy',
    'type'
  );

  return {
    projectId: 'static-project-gantt',
    projectTitle: 'Canonical Gantt Project',
    projectVersion: 11,
    workflowVersion: 5,
    calendarVersion: null,
    calendar: {
      timeZone: 'Asia/Tokyo',
      workingDays: [],
      holidaysAvailable: false,
      limitations: ['Holiday dates are unavailable from the current canonical calendar service.']
    },
    scheduledItems: [
      ganttTaskDto({
        taskId: 'task-gantt-parent',
        title: 'Derived parent',
        plannedStartDate: '2026-07-01',
        plannedEndDate: '2026-07-10',
        progressPercent: 50,
        progressIsDerived: true,
        scheduleEditPermissions: parentPermissions,
        warnings: [parentWarning]
      }),
      ganttTaskDto({
        taskId: 'task-gantt-schedule',
        parentTaskId: 'task-gantt-parent',
        title: 'Canonical schedule task',
        plannedStartDate: '2026-07-03',
        plannedEndDate: '2026-07-06',
        progressPercent: 25,
        isBlocked: true,
        priority: 'Critical',
        version: 3,
        warnings: [dependencyWarning]
      }),
      ganttTaskDto({
        taskId: 'task-gantt-predecessor',
        parentTaskId: 'task-gantt-parent',
        title: 'Predecessor task',
        plannedStartDate: '2026-07-01',
        plannedEndDate: '2026-07-10',
        progressPercent: 75,
        version: 2
      }),
      ganttTaskDto({
        taskId: 'task-gantt-successor',
        title: 'Dependency successor',
        plannedStartDate: '2026-07-15',
        plannedEndDate: '2026-07-20',
        version: 4
      })
    ],
    unscheduledItems: [
      ganttTaskDto({
        taskId: 'task-gantt-unscheduled',
        title: 'Unscheduled task',
        version: 2,
        warnings: [ganttWarningDto('UNSCHEDULED', 'Task is unscheduled.', 'Task', 'task-gantt-unscheduled')]
      })
    ],
    milestones: [{
      ...ganttTaskDto({
        taskId: 'milestone-gantt-release',
        title: 'Release Milestone',
        version: 4,
        scheduleEditPermissions: milestonePermissions
      }),
      kind: 'Milestone',
      milestoneDate: '2026-07-31'
    }],
    dependencies: [
      {
        dependencyId: 'dependency-gantt-existing',
        predecessorTaskId: 'task-gantt-predecessor',
        successorTaskId: 'task-gantt-schedule',
        type: 'FinishToStart',
        editable: true,
        version: 3,
        warnings: [dependencyWarning]
      },
      {
        dependencyId: 'dependency-gantt-legacy',
        predecessorTaskId: 'task-gantt-predecessor',
        successorTaskId: 'task-gantt-unscheduled',
        type: 'StartToStart',
        editable: false,
        version: 2,
        warnings: [legacyWarning]
      }
    ],
    warnings: [dependencyWarning, legacyWarning],
    permissions: ganttEditorPermissions,
    maximumItems: 20,
    totalItems: 6
  };
}

function ganttTaskDto(overrides: Partial<MockGanttItem>): MockGanttItem {
  return {
    taskId: 'task-gantt',
    kind: 'Task',
    parentTaskId: null,
    milestoneId: null,
    title: 'Task',
    plannedStartDate: null,
    plannedEndDate: null,
    milestoneDate: null,
    progressPercent: 0,
    progressIsDerived: false,
    workflowStageId: 'stage-todo',
    workflowStageName: 'Todo',
    stageCategory: 'Todo',
    priority: 'High',
    isBlocked: false,
    primaryAssignee: { userId: 'user-gantt', displayName: 'Schedule Editor' },
    version: 1,
    scheduleEditPermissions: ganttEditorPermissions,
    warnings: [],
    ...overrides
  };
}

function ganttWarningDto(
  code: string,
  message: string,
  targetType: string,
  targetId: string,
  field: string | null = null
): MockGanttWarning {
  return {
    code,
    message,
    severity: 'Warning',
    targetType,
    targetId,
    field,
    blocking: false
  };
}

function mergeWarningDto(warnings: MockGanttWarning[], warning: MockGanttWarning): MockGanttWarning[] {
  return [...warnings.filter((candidate) => candidate.code !== warning.code), warning];
}

function findGanttDto(snapshot: MockGanttSnapshot, taskId: string): MockGanttItem {
  const item = [...snapshot.scheduledItems, ...snapshot.unscheduledItems, ...snapshot.milestones]
    .find((candidate) => candidate.taskId === taskId);
  if (!item) {throw new Error(`Unexpected mocked Gantt WorkItem: ${taskId}`);}
  return item;
}

function replaceGanttDto(snapshot: MockGanttSnapshot, updated: MockGanttItem): MockGanttSnapshot {
  const without = (items: MockGanttItem[]) => items.filter((item) => item.taskId !== updated.taskId);
  const scheduledItems = without(snapshot.scheduledItems);
  const unscheduledItems = without(snapshot.unscheduledItems);
  const milestones = without(snapshot.milestones);
  if (updated.kind === 'Milestone') {
    milestones.push(updated);
  } else if (updated.plannedStartDate === null && updated.plannedEndDate === null) {
    unscheduledItems.push(updated);
  } else {
    scheduledItems.push(updated);
  }
  return {
    ...snapshot,
    projectVersion: snapshot.projectVersion + 1,
    scheduledItems,
    unscheduledItems,
    milestones
  };
}

function ganttCommandDto(item: MockGanttItem) {
  return {
    taskId: item.taskId,
    kind: item.kind,
    plannedStartDate: item.plannedStartDate,
    plannedEndDate: item.plannedEndDate,
    milestoneDate: item.milestoneDate,
    progressPercent: item.progressPercent,
    version: item.version,
    warnings: item.warnings
  };
}

async function installDirectTaskContextApi(
  page: Page,
  context: {
    projectId: string;
    projectTitle: string;
    taskId: string;
    taskTitle: string;
    canEdit?: boolean;
    goal?: string | null;
    deliverable?: string | null;
    constraints?: string | null;
  },
  options: {
    failFirstActivityPageOnce?: boolean;
    failSecondActivityPageOnce?: boolean;
    activityFailureStatus?: 409 | 500;
  } = {}
) {
  let projectListRequests = 0;
  let parentProjectRequests = 0;
  let parentProjectAttempts = 0;
  let version = 1;
  let goal = context.goal ?? null;
  let deliverable = context.deliverable ?? null;
  let constraints = context.constraints ?? null;
  const patchBodies: Record<string, unknown>[] = [];
  const briefField = (value: string | null) => ({ value, source: value === null ? 'notSet' : 'taskSpecific' });
  const taskDto = () => ({
    id: context.taskId,
    tenantId: 'mock-tenant',
    workspaceId: 'static-workspace-1',
    projectId: context.projectId,
    kind: 0,
    parentTaskId: null,
    milestoneId: null,
    title: context.taskTitle,
    description: 'Task-specific direct-route context.',
    brief: {
      goal: briefField(goal),
      deliverable: briefField(deliverable),
      constraints: briefField(constraints)
    },
    workflowStageId: 'stage-in-progress',
    workflowStageName: 'In progress',
    status: 1,
    stageCategory: 1,
    isBlocked: false,
    priority: 'High',
    plannedStartDate: '2026-08-20',
    plannedEndDate: '2026-08-27',
    progressPercent: 40,
    progressIsDerived: false,
    primaryAssignee: { userId: 'mock-user-a', displayName: 'Mock User A' },
    reviewStatus: 0,
    version,
    uiPermissions: { canEdit: context.canEdit === true, canAssign: false, canChangeStatus: false, canDelete: false, allowedTransitions: [] }
  });
  let activityRequests = 0;
  const activityPageAttempts = new Map<number, number>();
  page.on('requestfinished', (request) => {
    if (request.method() === 'GET' && new URL(request.url()).pathname === `/api/projects/${context.projectId}`) {
      parentProjectRequests += 1;
    }
  });
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path === '/api/security/csrf-token' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: 'csrf-task-brief', headerName: 'X-CSRF-Token' })
      });
      return;
    }
    if (path === `/api/tasks/${context.taskId}` && request.method() === 'PATCH' && context.canEdit === true) {
      const body = request.postDataJSON() as Record<string, unknown>;
      patchBodies.push(body);
      if ('goal' in body) {goal = typeof body['goal'] === 'string' ? body['goal'] : null;}
      if ('deliverable' in body) {deliverable = typeof body['deliverable'] === 'string' ? body['deliverable'] : null;}
      if ('constraints' in body) {constraints = typeof body['constraints'] === 'string' ? body['constraints'] : null;}
      version += 1;
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(taskDto()) });
      return;
    }
    if (request.method() !== 'GET') {
      await route.fallback();
      return;
    }

    if (path === `/api/tasks/${context.taskId}`) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          task: taskDto(),
          relationships: { primaryAssignee: { userId: 'mock-user-a', displayName: 'Mock User A' }, collaborators: [], reviewer: null, version },
          permissions: {
            canCreateSubtask: false,
            canCreateChecklistItem: false,
            canUpdateChecklistItems: false,
            canDeleteChecklistItems: false,
            canReorderChecklist: false,
            canCreateComment: false,
            canMarkCommentImportant: false,
            canApplyLabels: false,
            canManageLabelDefinitions: false,
            canAssociateFiles: false,
            canRemoveFiles: false,
            canChangeWatch: false
          },
          checklist: [],
          labels: [],
          watchState: { isWatching: false, isExplicitOptOut: false, automaticSources: [], version: 1 },
          subtasks: { items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false },
          comments: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false },
          files: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }
        })
      });
      return;
    }

    if (path === `/api/projects/${context.projectId}/tasks`) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ ...taskDto(), brief: undefined }], page: 1, pageSize: 50, totalCount: 1, hasMore: false })
      });
      return;
    }

    if (path === `/api/tasks/${context.taskId}/activity`) {
      activityRequests += 1;
      const activityPage = Number(url.searchParams.get('page') ?? '1');
      const activityPageAttempt = (activityPageAttempts.get(activityPage) ?? 0) + 1;
      activityPageAttempts.set(activityPage, activityPageAttempt);
      if (activityPageAttempt === 1 && (activityPage === 1 ? options.failFirstActivityPageOnce : options.failSecondActivityPageOnce)) {
        const status = options.activityFailureStatus ?? 500;
        await route.fulfill({
          status,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: status === 409 ? 'Conflict' : 'Activity unavailable', status, detail: 'Task Activity could not be loaded.' })
        });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(activityPage === 1
          ? {
              items: [
                { id: 'activity-status', activityType: 'StatusUpdate', body: 'Implementation is ready for review.', occurredAt: '2026-08-24T03:00:00Z', author: { userId: 'mock-user-a', displayName: 'Mock User A' } },
                { id: 'activity-issue', activityType: 3, body: 'DependencyNeedsAttentionWithoutBreakingTheNarrowLayout012345678901234567890123456789.', occurredAt: '2026-08-24T02:00:00Z', author: { userId: 'mock-user-b', displayName: 'Mock User B' } }
              ],
              page: 1,
              pageSize: 2,
              totalCount: 3,
              hasMore: true
            }
          : {
              items: [
                { id: 'activity-decision', activityType: 'Decision', body: 'Decision recorded after review.', occurredAt: '2026-08-24T01:00:00Z', author: { userId: 'mock-user-c', displayName: 'Mock User C' } }
              ],
              page: 2,
              pageSize: 2,
              totalCount: 3,
              hasMore: false
            })
      });
      return;
    }

    if (path === `/api/projects/${context.projectId}`) {
      parentProjectAttempts += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: context.projectId,
          title: context.projectTitle,
          status: 1,
          startDate: '2026-08-01',
          endDate: '2026-08-31',
          updatedAt: '2026-08-24T00:00:00Z',
          uiPermissions: { canCreateTask: false }
        })
      });
      return;
    }

    if (path === '/api/projects') {
      projectListRequests += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false })
      });
      return;
    }

    await route.fallback();
  });

  return {
    projectListRequests: () => projectListRequests,
    parentProjectRequests: () => parentProjectRequests,
    parentProjectAttempts: () => parentProjectAttempts,
    patchBodies: () => patchBodies,
    activityRequests: () => activityRequests
  };
}

type TaskExecutionScopeApiProjectPolicy = {
  webEnabled: boolean;
  projectFilesEnabled: boolean;
  version: number;
  canManage: boolean;
};

type TaskExecutionScopeApiLatestRun = {
  status: 'Accepted' | 'Queued' | 'Running' | 'Succeeded' | 'Failed';
  snapshotScopeOrigin: 'ProjectDefault' | 'TaskOverride';
  snapshotWebEnabled: boolean;
  snapshotProjectFilesEnabled: boolean;
};

type TaskExecutionScopeApiTaskPolicy = {
  origin: 'ProjectDefault' | 'TaskOverride';
  webEnabled: boolean;
  projectFilesEnabled: boolean;
  overrideVersion: number | null;
  canManage: boolean;
  latestRun: TaskExecutionScopeApiLatestRun | null;
};

/**
 * Registered after the direct Task fixture so it owns only the additive
 * execution-policy routes and falls back to that existing Task fixture.
 */
async function installTaskExecutionScopeApi(
  page: Page,
  fixture: {
    projectId: string;
    taskId: string;
    projectPolicy: TaskExecutionScopeApiProjectPolicy;
    taskPolicy: TaskExecutionScopeApiTaskPolicy;
  },
) {
  let projectPolicy = { ...fixture.projectPolicy };
  const taskPolicy = { ...fixture.taskPolicy };
  const projectDefaultUpdates: Record<string, unknown>[] = [];
  const runtimeRequests: { method: string; path: string }[] = [];

  const projectResponse = () => ({
    policy: {
      webEnabled: projectPolicy.webEnabled,
      projectFilesEnabled: projectPolicy.projectFilesEnabled,
    },
    version: projectPolicy.version,
    canManage: projectPolicy.canManage,
  });
  const taskResponse = () => {
    const overridePolicy = taskPolicy.origin === 'TaskOverride'
      ? {
          webEnabled: taskPolicy.webEnabled,
          projectFilesEnabled: taskPolicy.projectFilesEnabled,
        }
      : null;
    return {
      effectivePolicy: overridePolicy ?? {
        webEnabled: projectPolicy.webEnabled,
        projectFilesEnabled: projectPolicy.projectFilesEnabled,
      },
      origin: taskPolicy.origin,
      projectDefaultVersion: projectPolicy.version,
      taskOverrideVersion: taskPolicy.origin === 'TaskOverride' ? taskPolicy.overrideVersion : null,
      taskOverridePolicy: overridePolicy,
      canManage: taskPolicy.canManage,
      latestRun: taskPolicy.latestRun,
    };
  };

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();

    if (path === `/api/projects/${fixture.projectId}/execution-scope`) {
      if (method === 'GET') {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(projectResponse()) });
        return;
      }

      if (method === 'PUT') {
        const body = request.postDataJSON() as Record<string, unknown>;
        if (
          typeof body['webEnabled'] !== 'boolean' ||
          typeof body['projectFilesEnabled'] !== 'boolean' ||
          body['expectedVersion'] !== projectPolicy.version
        ) {
          throw new Error('Unexpected Project execution-scope policy request.');
        }

        projectDefaultUpdates.push(body);
        projectPolicy = {
          ...projectPolicy,
          webEnabled: body['webEnabled'],
          projectFilesEnabled: body['projectFilesEnabled'],
          version: projectPolicy.version + 1,
        };
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(projectResponse()) });
        return;
      }
    }

    if (path === `/api/tasks/${fixture.taskId}/execution-scope` && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(taskResponse()) });
      return;
    }

    if (path === `/api/tasks/${fixture.taskId}/execution-runs`) {
      runtimeRequests.push({ method, path });
      throw new Error('The Issue #357 foundation UI must not request an execution runtime.');
    }

    await route.fallback();
  });

  return {
    projectDefaultUpdates: () => projectDefaultUpdates,
    runtimeRequests: () => runtimeRequests,
  };
}

async function installProjectKanbanApi(
  page: Page,
  options: { denySnapshot?: boolean } = {}
) {
  let kanbanGets = 0;
  let taskListGets = 0;
  let moveCount = 0;
  const moveBodies: Record<string, unknown>[] = [];
  const csrfHeaders: string[] = [];
  let authoritativeSnapshot = projectKanbanSnapshot('stage-todo', 7, 3);
  let authoritativeTasks = projectTaskListDtos();

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (path === '/api/security/csrf-token' && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: 'csrf-kanban', headerName: 'X-CSRF-Token' })
      });
      return;
    }

    if (path === '/api/projects/static-project-kanban' && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'static-project-kanban',
          title: 'Canonical Project',
          status: 'Active',
          startDate: '2026-07-01',
          endDate: '2026-08-31',
          uiPermissions: { canCreateTask: true }
        })
      });
      return;
    }

    if (path === '/api/projects/static-project-kanban/tasks' && method === 'GET') {
      taskListGets += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: authoritativeTasks,
          totalCount: authoritativeTasks.length,
          hasMore: false
        })
      });
      return;
    }

    if (path === '/api/projects/static-project-kanban/kanban' && method === 'GET') {
      kanbanGets += 1;
      if (options.denySnapshot) {
        await route.fulfill({
          status: 403,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Forbidden', status: 403, detail: 'Project Kanban is not available.' })
        });
      } else {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(authoritativeSnapshot) });
      }
      return;
    }

    if (path === '/api/projects/static-project-kanban/gantt' && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ milestones: [], tasks: [] }) });
      return;
    }
    if (path === '/api/projects/static-project-kanban/workload' && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ members: [] }) });
      return;
    }
    if (path === '/api/projects/static-project-kanban/members' && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
      return;
    }

    if (path === '/api/tasks/static-task-kanban/kanban-move' && method === 'POST') {
      moveCount += 1;
      const moveBody = request.postDataJSON() as Record<string, unknown>;
      moveBodies.push(moveBody);
      csrfHeaders.push(request.headers()['x-csrf-token'] ?? '');
      if (moveCount === 1) {
        const targetStage = projectKanbanStageId(moveBody['targetWorkflowStageId']);
        authoritativeSnapshot = projectKanbanSnapshot(targetStage, 8, 4);
        if (targetStage === 'stage-done') {
          authoritativeTasks = authoritativeTasks.map((task) => task.id === 'static-task-kanban'
            ? {
                ...task,
                workflowStageId: 'stage-done',
                workflowStageName: 'Complete',
                status: 'Completed',
                stageCategory: 'Done',
                updatedAt: '2026-08-24T10:15:00Z',
                version: 4,
                uiPermissions: { canUpdate: true, rowVersion: '4' }
              }
            : task);
        }
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ snapshot: authoritativeSnapshot, focusTaskId: 'static-task-kanban', warnings: [] })
        });
      } else if (moveCount === 2) {
        await route.fulfill({
          status: 409,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Conflict', status: 409, detail: 'The board version is stale.', code: 'KANBAN_CONFLICT' })
        });
      } else {
        await route.fulfill({
          status: 403,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Forbidden', status: 403, detail: 'Move not allowed.', code: 'KANBAN_FORBIDDEN' })
        });
      }
      return;
    }

    await route.fallback();
  });

  return {
    moveBodies,
    csrfHeaders,
    kanbanGetCount: () => kanbanGets,
    taskListGetCount: () => taskListGets
  };
}

function projectTaskListDtos() {
  const common = {
    projectId: 'static-project-kanban',
    priority: 'High',
    primaryAssignee: { userId: 'user-1', displayName: 'Ada' }
  };

  return [
    {
      ...common,
      id: 'static-task-kanban',
      title: 'Canonical card',
      workflowStageId: 'stage-todo',
      workflowStageName: 'Ready for research',
      status: 'NotStarted',
      stageCategory: 'Todo',
      isBlocked: true,
      hasArtifact: true,
      createdAt: '2026-08-20T09:00:00Z',
      updatedAt: '2026-08-23T12:30:00Z',
      version: 3,
      uiPermissions: { canUpdate: true, rowVersion: '3' }
    },
    {
      ...common,
      id: 'static-task-running',
      title: 'Running analysis',
      workflowStageId: 'stage-in-progress',
      workflowStageName: 'Investigating',
      status: 'InProgress',
      stageCategory: 'InProgress',
      isBlocked: false,
      hasArtifact: false,
      createdAt: '2026-08-21T08:15:00Z',
      updatedAt: null,
      version: 2,
      uiPermissions: { canUpdate: true, rowVersion: '2' }
    },
    {
      ...common,
      id: 'static-task-review',
      title: 'Review evidence',
      workflowStageId: 'stage-review',
      workflowStageName: 'Evidence review',
      status: 'WaitingReview',
      stageCategory: 'Review',
      isBlocked: false,
      hasArtifact: true,
      createdAt: '2026-08-19T05:30:00Z',
      updatedAt: '2026-08-24T02:45:00Z',
      version: 7,
      uiPermissions: { canUpdate: true, rowVersion: '7' }
    },
    {
      ...common,
      id: 'static-task-completed',
      title: 'Completed report',
      workflowStageId: 'stage-done',
      workflowStageName: 'Published',
      status: 'Completed',
      stageCategory: 'Done',
      isBlocked: false,
      hasArtifact: true,
      createdAt: '2026-08-18T01:00:00Z',
      updatedAt: '2026-08-22T03:00:00Z',
      version: 5,
      uiPermissions: { canUpdate: true, rowVersion: '5' }
    },
    {
      ...common,
      id: 'static-task-cancelled',
      title: 'Cancelled follow-up',
      workflowStageId: 'stage-cancelled',
      workflowStageName: 'Cancelled',
      status: 'Cancelled',
      stageCategory: 'Cancelled',
      isBlocked: false,
      hasArtifact: false,
      createdAt: '2026-08-17T04:00:00Z',
      updatedAt: '2026-08-21T04:00:00Z',
      version: 4,
      uiPermissions: { canUpdate: true, rowVersion: '4' }
    }
  ];
}

type ProjectKanbanStageId = 'stage-todo' | 'stage-done' | 'stage-cancelled';

function projectKanbanSnapshot(stageId: ProjectKanbanStageId, boardVersion: number, taskVersion: number) {
  const inTodo = stageId === 'stage-todo';
  const inDone = stageId === 'stage-done';
  const inCancelled = stageId === 'stage-cancelled';
  return {
    board: {
      projectId: 'static-project-kanban',
      version: boardVersion,
      timeZone: 'UTC',
      defaultSwimlane: 0,
      selectedSwimlane: 0,
      supportedSwimlanes: [0, 1, 2, 3, 4],
      supportedFilters: ['includeOlderCompleted'],
      includesOlderCompleted: false,
      doneWindowDays: 30,
      totalAuthorizedCardCount: 1,
      isTruncated: false,
      uiPermissions: { canConfigure: true },
      warnings: inTodo
        ? [{ code: 'KANBAN_WIP_LIMIT_EXCEEDED', message: 'Todo exceeds its warning limit.', workflowStageId: 'stage-todo', currentCount: 2, limit: 1 }]
        : []
    },
    columns: [
      {
        workflowStageId: 'stage-todo',
        displayName: 'Todo',
        category: 1,
        displayOrder: 1000,
        wipWarningLimit: 1,
        currentAuthorizedCardCount: inTodo ? 2 : 1,
        hasWipWarning: inTodo,
        uiPermissions: { canConfigure: true }
      },
      {
        workflowStageId: 'stage-done',
        displayName: 'Done',
        category: 4,
        displayOrder: 2000,
        wipWarningLimit: null,
        currentAuthorizedCardCount: inDone ? 1 : 0,
        hasWipWarning: false,
        uiPermissions: { canConfigure: true }
      },
      {
        workflowStageId: 'stage-cancelled',
        displayName: 'Cancelled',
        category: 5,
        displayOrder: 3000,
        wipWarningLimit: null,
        currentAuthorizedCardCount: inCancelled ? 1 : 0,
        hasWipWarning: false,
        uiPermissions: { canConfigure: true }
      }
    ],
    cards: [{
      taskId: 'static-task-kanban',
      summary: 'Canonical card',
      workflowStageId: stageId,
      boardOrder: 1000,
      parentTaskId: null,
      parentSummary: null,
      isParentSummary: true,
      isLeaf: false,
      completedChildCount: 1,
      childCount: 2,
      progressPercent: 50,
      plannedStartDate: '2026-07-01',
      plannedEndDate: '2026-07-31',
      primaryAssigneeUserId: 'user-1',
      primaryAssigneeLabel: 'Ada',
      targetGroupId: null,
      targetGroupLabel: 'Ungrouped',
      priority: 2,
      isBlocked: true,
      version: taskVersion,
      swimlaneKey: 'all',
      swimlaneLabel: 'All tasks',
      uiPermissions: {
        canOpen: true,
        canMove: true,
        allowedTargetWorkflowStageIds: ['stage-todo', 'stage-done', 'stage-cancelled']
      }
    }]
  };
}

function projectKanbanStageId(value: unknown): ProjectKanbanStageId {
  if (value === 'stage-todo' || value === 'stage-done' || value === 'stage-cancelled') {return value;}
  throw new Error(`Unexpected mocked Kanban target Stage: ${String(value)}`);
}

interface WorkspaceContextFixture {
  readonly id: string;
  readonly name: string;
  readonly currentUserRole?: string;
  readonly canOpenProjectCreate?: boolean;
  readonly canCreateProject?: boolean;
  readonly canAddFiles?: boolean;
  readonly runningProjectCount?: number;
  readonly needsReviewProjectCount?: number;
}

interface WorkspaceProjectCreateMockRequest {
  readonly body: Record<string, unknown>;
  readonly idempotencyKey: string;
  readonly csrfToken: string;
}

interface WorkspaceCreateMockRequest {
  readonly body: Record<string, unknown>;
  readonly rawBody: string;
  readonly idempotencyKey: string;
  readonly csrfToken: string;
}

interface WorkspaceCreateMockResponse {
  readonly status: number;
  readonly body: unknown;
  readonly workspace?: WorkspaceContextFixture;
}

interface AnnouncementEditorApiOptions {
  readonly firstPublishFailure?: 'unavailable' | 'audienceAuthorization';
  readonly holdAudienceRefresh?: boolean;
}

interface AnnouncementEditorApiHarness {
  readonly publishRequests: readonly Record<string, unknown>[];
  readonly audienceRefreshRequested: Promise<void>;
  releaseAudienceRefresh(): void;
}

interface WorkspaceContextApiOptions {
  readonly canCreate?: boolean;
  readonly onCreate?: (
    request: WorkspaceCreateMockRequest,
    attempt: number
  ) => WorkspaceCreateMockResponse | Promise<WorkspaceCreateMockResponse>;
}

interface WorkspaceContextApiHarness {
  readonly createRequests: readonly WorkspaceCreateMockRequest[];
  readonly workspaceListRequests: number;
}

interface CanonicalProjectCreateMockRequest {
  readonly body: Record<string, unknown>;
  readonly rawBody: string;
  readonly idempotencyKey: string;
  readonly csrfToken: string;
}

interface CanonicalProjectCreateActivationHarness {
  readonly createRequests: readonly CanonicalProjectCreateMockRequest[];
  readonly activationRequests: readonly Record<string, unknown>[];
  readonly activationCsrfTokens: readonly string[];
  readonly operationalGetPaths: readonly string[];
  readonly projectListRequests: readonly {
    readonly workspaceId: string | null;
    readonly includesCreatedProject: boolean;
  }[];
  readonly projectGetCount: () => number;
  readonly releaseFirstCreate: () => void;
  readonly allowFirstCreateSuccess: () => void;
}

function workspaceContextFixtures(): readonly WorkspaceContextFixture[] {
  return [
    {
      id: 'workspace-alpha',
      name: 'Workspace Alpha',
      runningProjectCount: 2,
      needsReviewProjectCount: 1
    },
    {
      id: 'workspace-beta',
      name: 'Workspace Beta',
      runningProjectCount: 0,
      needsReviewProjectCount: 0
    }
  ];
}

interface AuditGridFixture {
  readonly id: string;
  readonly createdAt: string;
  readonly action: string;
  readonly actorDisplayName: string;
  readonly targetType: string;
  readonly workspaceLabel: string;
  readonly severity: 'info' | 'warning' | 'critical';
  readonly result: 'success' | 'denied' | 'failed';
  readonly summary: string;
  readonly requestId: string | null;
}

interface AuditGridApiOptions {
  readonly canViewSensitiveMetadata?: boolean;
  readonly sensitiveMetadata?: Readonly<Record<string, Readonly<Record<string, unknown>>>>;
  readonly redactionApplied?: boolean;
  readonly applyListFilters?: boolean;
}

function auditGridFixtures(count: number): readonly AuditGridFixture[] {
  return Array.from({ length: count }, (_, index) => {
    const number = String(index + 1).padStart(3, '0');
    return {
      id: auditGridFixtureId(index),
      createdAt: `2026-08-25T08:${String(index % 60).padStart(2, '0')}:00Z`,
      action: index % 3 === 0 ? 'audit.detail.read' : index % 3 === 1 ? 'file.download.denied' : 'export.request.failed',
      actorDisplayName: 'Redacted actor',
      targetType: index % 2 === 0 ? 'AuditLog' : 'File',
      workspaceLabel: 'Static Workspace',
      severity: index % 3 === 0 ? 'info' : index % 3 === 1 ? 'warning' : 'critical',
      result: index % 3 === 0 ? 'success' : index % 3 === 1 ? 'denied' : 'failed',
      summary: `Audit row ${number} was opened with safe fields.`,
      requestId: null,
    };
  });
}

function auditGridFixtureId(index: number): string {
  return `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`;
}

async function installAuditGridApi(
  page: Page,
  rows: readonly AuditGridFixture[],
  options: AuditGridApiOptions = {},
): Promise<void> {
  await page.route('**/api/audit/capabilities', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({
        canView: true,
        canReview: true,
        canApprove: false,
        canExport: false,
        canViewSensitiveMetadata: options.canViewSensitiveMetadata === true,
      }),
    });
  });

  await page.route('**/api/admin/audit-grid**', async (route) => {
    const request = route.request();
    if (request.method() !== 'GET') {
      await route.fulfill({ status: 405 });
      return;
    }

    const url = new URL(request.url());
    if (url.pathname === '/api/admin/audit-grid') {
      const filteredRows = options.applyListFilters ? applyAuditFixtureFilters(rows, url.searchParams) : rows;
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ items: filteredRows, page: 1, pageSize: 100, totalCount: filteredRows.length }),
      });
      return;
    }

    const routeSuffix = url.pathname.slice('/api/admin/audit-grid/'.length);
    if (routeSuffix.endsWith('/sensitive-metadata')) {
      const auditId = routeSuffix.slice(0, -'/sensitive-metadata'.length);
      const metadata = options.sensitiveMetadata?.[auditId];
      await route.fulfill(
        options.canViewSensitiveMetadata === true && metadata
          ? {
              status: 200,
              contentType: 'application/json; charset=utf-8',
              body: JSON.stringify({
                auditId,
                metadata,
                redactionApplied: options.redactionApplied === true,
              }),
            }
          : {
              status: options.canViewSensitiveMetadata === true ? 404 : 403,
              contentType: 'application/json; charset=utf-8',
              body: JSON.stringify({ error: { code: 'AuditEventNotFound' } }),
            },
      );
      return;
    }

    const auditId = routeSuffix;
    const row = rows.find((item) => item.id === auditId);
    await route.fulfill(row
      ? {
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify(row),
        }
      : {
          status: 404,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ error: { code: 'AuditEventNotFound', message: 'The requested audit event is not available.' } }),
        });
  });
}

function applyAuditFixtureFilters(
  rows: readonly AuditGridFixture[],
  params: URLSearchParams,
): readonly AuditGridFixture[] {
  const q = params.get('q')?.toLowerCase();
  const action = params.get('action')?.toLowerCase();
  const actor = params.get('actor')?.toLowerCase();
  const entityType = params.get('entityType')?.toLowerCase();
  const severity = params.get('severity');
  const result = params.get('result');
  return rows.filter((row) =>
    (!q || [row.action, row.targetType, row.workspaceLabel, row.summary]
      .some((value) => value.toLowerCase().includes(q))) &&
    (!action || row.action.toLowerCase() === action) &&
    (!actor || row.actorDisplayName.toLowerCase().includes(actor)) &&
    (!entityType || row.targetType.toLowerCase() === entityType) &&
    (!severity || row.severity === severity) &&
    (!result || row.result === result));
}

async function installWorkspaceContextApi(
  page: Page,
  workspaces: readonly WorkspaceContextFixture[],
  currentWorkspace: WorkspaceContextFixture | null,
  options: WorkspaceContextApiOptions = {}
): Promise<WorkspaceContextApiHarness> {
  const authorizedWorkspaces = [...workspaces];
  const createRequests: WorkspaceCreateMockRequest[] = [];
  let workspaceListRequests = 0;

  const dashboardItems = () => authorizedWorkspaces.map((workspace) => ({
      ...workspace,
      description: `${workspace.name} Playwright fixture`,
      icon: null,
      status: 'Active',
      createdAt: '2026-07-06T00:00:00Z',
      updatedAt: '2026-07-06T00:00:00Z',
      currentUserRole: workspace.currentUserRole ?? 'Member',
      accessSource: 'WorkspaceMembership',
      canOpenWorkspace: true,
      canOpenMembers: true,
      canOpenProjects: true,
      canOpenProjectCreate: workspace.canOpenProjectCreate === true,
      canCreateProject: workspace.canCreateProject === true,
      canAddFiles: workspace.canAddFiles === true,
      unreadAnnouncementCount: 0,
      unreadConversationCount: 0,
      inProgressProjectCount:
        workspace.runningProjectCount === undefined || workspace.needsReviewProjectCount === undefined
          ? undefined
          : workspace.runningProjectCount + workspace.needsReviewProjectCount
    }));

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
        capabilities: ['workspace:view', 'announcements:view', 'projects:view', 'files:view', 'account:view', 'audit:view'],
        currentWorkspace,
        workspaces
      })
    });
  });

  await page.route('**/api/workspaces', async (route) => {
    const request = route.request();
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>;
      const record: WorkspaceCreateMockRequest = {
        body,
        rawBody: request.postData() ?? '',
        idempotencyKey: request.headers()['idempotency-key'] ?? '',
        csrfToken: request.headers()['x-csrf-token'] ?? ''
      };
      createRequests.push(record);
      const result = await options.onCreate?.(record, createRequests.length) ?? {
        status: 503,
        body: {
          requestId: 'workspace-create-unconfigured',
          error: {
            code: 'DependencyUnavailable',
            message: 'Workspace creation is unavailable in this fixture.',
            target: 'workspace',
            details: [],
            redactionApplied: false
          },
          traceId: 'workspace-create-unconfigured',
          status: 503
        }
      };
      if (result.workspace && !authorizedWorkspaces.some((workspace) => workspace.id === result.workspace?.id)) {
        authorizedWorkspaces.push(result.workspace);
      }
      await route.fulfill({
        status: result.status,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify(result.body)
      });
      return;
    }

    if (request.method() !== 'GET') {
      await route.fulfill({ status: 405 });
      return;
    }

    workspaceListRequests += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify(dashboardItems())
    });
  });

  await page.route('**/api/workspaces/capabilities', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({
        requestId: 'playwright-workspaces-capabilities',
        data: { canCreate: options.canCreate === true },
        warnings: []
      })
    });
  });

  await page.route('**/api/security/csrf-token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({ token: 'csrf-workspace-create', headerName: 'X-CSRF-Token' })
    });
  });

  return {
    createRequests,
    get workspaceListRequests() {
      return workspaceListRequests;
    }
  };
}

async function installAnnouncementMobileDetailApi(
  page: Page,
  workspaceId: string,
): Promise<{
  id: string;
  readRequests: { body: Record<string, unknown>; csrfToken: string }[];
}> {
  const id = '38500000-0000-4000-8000-000000000002';
  const readRequests: { body: Record<string, unknown>; csrfToken: string }[] = [];
  let isRead = false;
  const listItem = () => ({
    id,
    workspaceId,
    groupId: null,
    channelId: null,
    title: 'Mobile recipient detail',
    priority: 1,
    isPinned: true,
    requiresReadConfirmation: true,
    isRead,
    publishedAt: '2026-08-25T09:00:00Z',
    expiresAt: '2026-09-01T09:00:00Z',
  });
  const listItems = () => [
    ...Array.from({ length: 3 }, (_, index) => ({
      id: `38500000-0000-4000-8000-0000000001${String(index).padStart(2, '0')}`,
      workspaceId,
      groupId: null,
      channelId: null,
      title: `Mobile list context ${index + 1}`,
      priority: 0,
      isPinned: false,
      requiresReadConfirmation: false,
      isRead: true,
      publishedAt: '2026-08-25T09:00:00Z',
      expiresAt: null,
    })),
    listItem(),
    ...Array.from({ length: 20 }, (_, index) => ({
      id: `38500000-0000-4000-8000-0000000002${String(index).padStart(2, '0')}`,
      workspaceId,
      groupId: null,
      channelId: null,
      title: `Mobile list context ${index + 4}`,
      priority: 0,
      isPinned: false,
      requiresReadConfirmation: false,
      isRead: true,
      publishedAt: '2026-08-25T09:00:00Z',
      expiresAt: null,
    })),
  ];

  await page.route('**/api/security/csrf-token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({ token: 'csrf-announcement-read', headerName: 'X-CSRF-Token' }),
    });
  });
  await page.route('**/api/announcements**', async (route) => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (pathname === '/api/announcements/audiences' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify([]),
      });
      return;
    }
    if (pathname === '/api/announcements' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ items: listItems() }),
      });
      return;
    }
    const listedDetail = listItems().find((item) => pathname === `/api/announcements/${item.id}`);
    if (listedDetail && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          ...listedDetail,
          body: listedDetail.id === id ? 'A long recipient-facing body. '.repeat(48) : 'List context body.',
          createdAt: '2026-08-25T08:55:00Z',
          updatedAt: '2026-08-25T08:55:00Z',
        }),
      });
      return;
    }
    if (pathname === `/api/announcements/${id}/read` && request.method() === 'POST') {
      readRequests.push({
        body: request.postDataJSON() as Record<string, unknown>,
        csrfToken: request.headers()['x-csrf-token'] ?? '',
      });
      isRead = true;
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ status: 'OK' }),
      });
      return;
    }

    await route.fulfill({ status: 405 });
  });

  return { id, readRequests };
}

async function installAnnouncementEditorApi(
  page: Page,
  options: AnnouncementEditorApiOptions = {}
): Promise<AnnouncementEditorApiHarness> {
  const workspaceId = '38000000-0000-4000-8000-000000000001';
  const draftId = '38000000-0000-4000-8000-000000000003';
  const publishRequests: Record<string, unknown>[] = [];
  let persistedDraft = {
    title: '',
    body: '',
    priority: 0,
    requiresReadConfirmation: false
  };
  let audienceRequestCount = 0;
  let releaseAudienceRefresh!: () => void;
  let notifyAudienceRefreshRequested!: () => void;
  const audienceRefreshGate = new Promise<void>((resolve) => {
    releaseAudienceRefresh = resolve;
  });
  const audienceRefreshRequested = new Promise<void>((resolve) => {
    notifyAudienceRefreshRequested = resolve;
  });

  const draftResponse = (status: 'Draft' | 'Scheduled', version: number) => ({
    id: draftId,
    version,
    status,
    workspaceId,
    groupId: null,
    channelId: null,
    title: persistedDraft.title,
    body: persistedDraft.body,
    priority: persistedDraft.priority === 2
      ? 'Critical'
      : persistedDraft.priority === 1
        ? 'Important'
        : 'Normal',
    isPinned: false,
    requiresReadConfirmation: persistedDraft.requiresReadConfirmation,
    ...(status === 'Scheduled'
      ? {
          scheduledForUtc: '2026-08-24T10:00:00Z',
          scheduleTimeZoneId: 'UTC',
          scheduleLocalDateTime: '2026-08-24T10:00:00'
        }
      : {})
  });

  await page.route('**/api/announcements', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ items: [] })
      });
      return;
    }

    await route.fulfill({ status: 405 });
  });

  await page.route('**/api/announcement-drafts**', async (route) => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;

    if (pathname === '/api/announcement-drafts' && request.method() === 'POST') {
      const payload = request.postDataJSON() as { content?: Record<string, unknown> };
      const content = payload.content ?? {};
      persistedDraft = {
        title: typeof content['title'] === 'string' ? content['title'] : '',
        body: typeof content['body'] === 'string' ? content['body'] : '',
        priority: typeof content['priority'] === 'number' ? content['priority'] : 0,
        requiresReadConfirmation: content['requiresReadConfirmation'] === true
      };
      await route.fulfill({
        status: 201,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify(draftResponse('Draft', 1))
      });
      return;
    }

    if (
      pathname === `/api/announcement-drafts/${draftId}/publish` &&
      request.method() === 'POST'
    ) {
      publishRequests.push(request.postDataJSON() as Record<string, unknown>);
      if (publishRequests.length === 1) {
        const audienceDenied = options.firstPublishFailure === 'audienceAuthorization';
        await route.fulfill({
          status: audienceDenied ? 403 : 503,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            error: {
              code: audienceDenied
                ? 'ANNOUNCEMENT_DRAFT_AUDIENCE_DENIED'
                : 'ANNOUNCEMENT_DRAFT_UNAVAILABLE',
              message: audienceDenied
                ? 'Announcement audience is not authorized.'
                : 'internal upstream detail'
            }
          })
        });
        return;
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify(draftResponse('Scheduled', 2))
      });
      return;
    }

    await route.fulfill({ status: 405 });
  });

  await page.route('**/api/announcements/audiences', async (route) => {
    audienceRequestCount += 1;
    if (audienceRequestCount > 1 && options.holdAudienceRefresh) {
      notifyAudienceRefreshRequested();
      await audienceRefreshGate;
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify([])
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify([
        {
          key: `workspace:${  workspaceId}`,
          scopeType: 'workspace',
          workspaceId,
          groupId: null,
          channelId: null,
          displayName: 'Announcement evidence workspace',
          estimatedRecipientCount: 24
        }
      ])
    });
  });

  return {
    publishRequests,
    audienceRefreshRequested,
    releaseAudienceRefresh
  };
}

async function installCanonicalProjectCreateActivationApi(
  page: Page,
  scope: { workspaceId: string; projectId: string; groupId: string }
): Promise<CanonicalProjectCreateActivationHarness> {
  const ownerUserId = '40900000-0000-4000-8000-000000000004';
  const createRequests: CanonicalProjectCreateMockRequest[] = [];
  const activationRequests: Record<string, unknown>[] = [];
  const activationCsrfTokens: string[] = [];
  const operationalGetPaths: string[] = [];
  const projectListRequests: {
    workspaceId: string | null;
    includesCreatedProject: boolean;
  }[] = [];
  let releaseFirstCreate!: () => void;
  const firstCreateGate = new Promise<void>((resolve) => {
    releaseFirstCreate = resolve;
  });
  let firstCreateShouldFail = true;
  let projectGets = 0;
  let created = false;
  let activated = false;

  const projectDto = () => ({
    id: scope.projectId,
    workspaceId: scope.workspaceId,
    groupId: scope.groupId,
    ownerUserId,
    title: 'U-22 Canonical Project',
    description: 'Canonical create and activation browser evidence.',
    status: activated ? 1 : 0,
    visibility: 1,
    activationState: activated ? 2 : 1,
    activatedAtUtc: activated ? '2026-08-24T05:05:00Z' : null,
    activationVersion: activated ? 1 : null,
    versionNo: activated ? 2 : 1,
    startDate: '2026-09-10',
    endDate: '2026-09-20',
    createdAt: '2026-08-24T05:00:00Z',
    updatedAt: activated ? '2026-08-24T05:05:00Z' : null,
    uiPermissions: {
      canCreateTask: activated,
      canActivate: !activated
    }
  });

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (
      path === `/api/workspaces/${scope.workspaceId}/projects/create-options` &&
      method === 'GET'
    ) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          requestId: 'project-options-200',
          data: {
            workspaceId: scope.workspaceId,
            canCreateUngrouped: false,
            allowedVisibilities: [1],
            groups: [{ id: scope.groupId, name: 'Evidence Review Group' }]
          },
          warnings: []
        })
      });
      return;
    }

    if (path === `/api/workspaces/${scope.workspaceId}/projects` && method === 'POST') {
      const recorded: CanonicalProjectCreateMockRequest = {
        body: request.postDataJSON() as Record<string, unknown>,
        rawBody: request.postData() ?? '',
        idempotencyKey: request.headers()['idempotency-key'] ?? '',
        csrfToken: request.headers()['x-csrf-token'] ?? ''
      };
      createRequests.push(recorded);
      if (createRequests.length === 1 && firstCreateShouldFail) {
        await firstCreateGate;
        if (firstCreateShouldFail) {
          await route.fulfill({
            status: 503,
            contentType: 'application/json; charset=utf-8',
            body: JSON.stringify({
              requestId: 'project-create-503',
              error: {
                code: 'DependencyUnavailable',
                message: 'Project creation outcome is temporarily unavailable.',
                target: 'project',
                details: [],
                redactionApplied: false
              },
              traceId: 'project-create-503',
              status: 503
            })
          });
          return;
        }
      }

      created = true;
      await route.fulfill({
        status: 201,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          requestId: 'project-create-201',
          data: {
            id: scope.projectId,
            workspaceId: scope.workspaceId,
            groupId: scope.groupId,
            ownerUserId,
            title: 'U-22 Canonical Project',
            description: 'Canonical create and activation browser evidence.',
            status: 0,
            visibility: 1,
            activationState: 1,
            startDate: '2026-09-10',
            endDate: '2026-09-20',
            versionNo: 1,
            createdAt: '2026-08-24T05:00:00Z'
          },
          warnings: []
        })
      });
      return;
    }

    if (path === '/api/projects' && method === 'GET') {
      expect(url.searchParams.get('workspaceId')).toBe(scope.workspaceId);
      projectListRequests.push({
        workspaceId: url.searchParams.get('workspaceId'),
        includesCreatedProject: created
      });
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          items: created ? [projectDto()] : [],
          page: 1,
          pageSize: 50,
          totalCount: created ? 1 : 0,
          hasMore: false
        })
      });
      return;
    }

    if (path === `/api/projects/${scope.projectId}` && method === 'GET') {
      projectGets += 1;
      await route.fulfill({
        status: created ? 200 : 404,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify(created ? projectDto() : { title: 'Not Found', status: 404 })
      });
      return;
    }

    if (path === `/api/projects/${scope.projectId}/activate` && method === 'POST') {
      activationRequests.push(request.postDataJSON() as Record<string, unknown>);
      activationCsrfTokens.push(request.headers()['x-csrf-token'] ?? '');
      activated = true;
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          requestId: 'project-activate-200',
          data: { projectId: scope.projectId },
          warnings: []
        })
      });
      return;
    }

    const operationalSuffix = path.startsWith(`/api/projects/${scope.projectId}/`)
      ? path.slice(`/api/projects/${scope.projectId}`.length)
      : null;
    if (
      method === 'GET' &&
      operationalSuffix !== null &&
      ['/tasks', '/kanban', '/gantt', '/workload', '/members'].includes(operationalSuffix)
    ) {
      operationalGetPaths.push(operationalSuffix);
      if (operationalSuffix === '/tasks') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false })
        });
        return;
      }
      if (operationalSuffix === '/kanban') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(emptyCanonicalKanban(scope.projectId))
        });
        return;
      }
      if (operationalSuffix === '/gantt') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(emptyCanonicalGantt(scope.projectId))
        });
        return;
      }
      if (operationalSuffix === '/workload') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ members: [] })
        });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([{ userId: ownerUserId, displayName: 'Mock User A', role: 'Owner' }])
      });
      return;
    }

    await route.fallback();
  });

  return {
    createRequests,
    activationRequests,
    activationCsrfTokens,
    operationalGetPaths,
    projectListRequests,
    projectGetCount: () => projectGets,
    releaseFirstCreate,
    allowFirstCreateSuccess: () => {
      firstCreateShouldFail = false;
      releaseFirstCreate();
    }
  };
}

async function installTaskCreateStaticApi(
  page: Page,
  scope: { workspaceId: string; projectId: string; taskId: string },
) {
  const milestoneId = '41000000-0000-4000-8000-000000000004';
  const assigneeId = '41000000-0000-4000-8000-000000000005';
  const workflowStageId = '41000000-0000-4000-8000-000000000006';
  const createRequests: {
    body: Record<string, unknown>;
    idempotencyKey: string;
    csrfToken: string;
  }[] = [];
  const project = {
    id: scope.projectId,
    workspaceId: scope.workspaceId,
    groupId: null,
    ownerUserId: assigneeId,
    title: 'Task creation evidence Project',
    description: 'Static browser fixture for Task creation.',
    status: 1,
    visibility: 1,
    activationState: 2,
    activatedAtUtc: '2026-08-24T00:00:00Z',
    activationVersion: 1,
    versionNo: 1,
    startDate: null,
    endDate: null,
    createdAt: '2026-08-24T00:00:00Z',
    updatedAt: '2026-08-24T00:00:00Z',
    uiPermissions: { canCreateTask: true, canActivate: false },
  };

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (path === `/api/projects/${scope.projectId}` && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify(project),
      });
      return;
    }
    if (path === `/api/projects/${scope.projectId}/tasks/create-options` && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          requestId: 'task-create-options-200',
          data: {
            projectId: scope.projectId,
            workspaceId: scope.workspaceId,
            projectTitle: project.title,
            canCreateTask: true,
            canManageProject: true,
            milestones: [{ id: milestoneId, title: 'Named evidence milestone' }],
            assignees: [{ userId: assigneeId, displayName: 'Named project member' }],
            projectScope: {
              policy: { webEnabled: false, projectFilesEnabled: true },
              version: 1,
              canSetTaskOverride: true,
            },
          },
          warnings: [],
        }),
      });
      return;
    }
    if (path === `/api/projects/${scope.projectId}/tasks/create` && method === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>;
      createRequests.push({
        body,
        idempotencyKey: request.headers()['idempotency-key'] ?? '',
        csrfToken: request.headers()['x-csrf-token'] ?? '',
      });
      await route.fulfill({
        status: 201,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          requestId: 'task-create-201',
          data: {
            taskId: scope.taskId,
            projectId: scope.projectId,
            workspaceId: scope.workspaceId,
            milestoneId: body['milestoneId'] ?? null,
            primaryAssigneeUserId: body['primaryAssigneeUserId'] ?? null,
            title: body['title'],
            priority: body['priority'],
            status: 0,
            workflowStageId,
            version: 1,
            sourceScopeMode: body['sourceScopeMode'],
            taskOverridePolicy: body['taskOverridePolicy'] ?? null,
          },
          warnings: [],
        }),
      });
      return;
    }
    if (path === `/api/projects/${scope.projectId}/tasks` && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false }),
      });
      return;
    }
    if (path === `/api/projects/${scope.projectId}/kanban` && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(emptyCanonicalKanban(scope.projectId)) });
      return;
    }
    if (path === `/api/projects/${scope.projectId}/gantt` && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(emptyCanonicalGantt(scope.projectId)) });
      return;
    }
    if (path === `/api/projects/${scope.projectId}/workload` && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ members: [] }) });
      return;
    }
    if (path === `/api/projects/${scope.projectId}/members` && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
      return;
    }
    if (path === `/api/tasks/${scope.taskId}` && method === 'GET') {
      const created = createRequests.at(-1)?.body ?? {};
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          task: {
            id: scope.taskId,
            tenantId: 'static-tenant',
            workspaceId: scope.workspaceId,
            projectId: scope.projectId,
            kind: 0,
            parentTaskId: null,
            milestoneId: created['milestoneId'] ?? null,
            title: created['title'] ?? 'Accessible evidence Task',
            description: created['description'] ?? '',
            brief: {
              goal: { value: created['goal'] ?? null, source: 'taskSpecific' },
              deliverable: { value: created['deliverable'] ?? null, source: 'taskSpecific' },
              constraints: { value: created['constraints'] ?? null, source: 'taskSpecific' },
            },
            workflowStageId,
            workflowStageName: 'Todo',
            status: 0,
            stageCategory: 0,
            isBlocked: false,
            priority: created['priority'] ?? 1,
            plannedStartDate: created['startDate'] ?? null,
            plannedEndDate: created['dueDate'] ?? null,
            progressPercent: 0,
            progressIsDerived: false,
            primaryAssignee: created['primaryAssigneeUserId']
              ? { userId: created['primaryAssigneeUserId'], displayName: 'Named project member' }
              : null,
            reviewStatus: 0,
            version: 1,
            uiPermissions: {
              canEdit: true,
              canAssign: true,
              canChangeStatus: true,
              canDelete: false,
              allowedTransitions: [],
            },
          },
          relationships: { primaryAssignee: null, collaborators: [], reviewer: null, version: 1 },
          permissions: {
            canCreateSubtask: false,
            canCreateChecklistItem: false,
            canUpdateChecklistItems: false,
            canDeleteChecklistItems: false,
            canReorderChecklist: false,
            canCreateComment: false,
            canMarkCommentImportant: false,
            canApplyLabels: false,
            canManageLabelDefinitions: false,
            canAssociateFiles: false,
            canRemoveFiles: false,
            canChangeWatch: false,
          },
          checklist: [],
          labels: [],
          watchState: { isWatching: false, isExplicitOptOut: false, automaticSources: [], version: 1 },
          subtasks: { items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false },
          comments: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false },
          files: { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false },
        }),
      });
      return;
    }

    await route.fallback();
  });

  return { milestoneId, assigneeId, createRequests };
}

function emptyCanonicalKanban(projectId: string) {
  return {
    board: {
      projectId,
      version: 1,
      timeZone: 'UTC',
      defaultSwimlane: 0,
      selectedSwimlane: 0,
      supportedSwimlanes: [0, 1, 2, 3, 4],
      supportedFilters: ['includeOlderCompleted'],
      includesOlderCompleted: false,
      doneWindowDays: 30,
      totalAuthorizedCardCount: 0,
      isTruncated: false,
      uiPermissions: { canConfigure: true },
      warnings: []
    },
    columns: [
      {
        workflowStageId: '40900000-0000-4000-8000-000000000010',
        displayName: 'Todo',
        category: 1,
        displayOrder: 1000,
        wipWarningLimit: null,
        currentAuthorizedCardCount: 0,
        hasWipWarning: false,
        uiPermissions: { canConfigure: true }
      },
      {
        workflowStageId: '40900000-0000-4000-8000-000000000011',
        displayName: 'Done',
        category: 4,
        displayOrder: 2000,
        wipWarningLimit: null,
        currentAuthorizedCardCount: 0,
        hasWipWarning: false,
        uiPermissions: { canConfigure: true }
      }
    ],
    cards: []
  };
}

function emptyCanonicalGantt(projectId: string) {
  const permissions = {
    canEditSchedule: true,
    canEditProgress: true,
    canManageDependencies: true,
    canClearSchedule: true,
    canOpen: true
  };
  return {
    projectId,
    projectTitle: 'U-22 Canonical Project',
    projectVersion: 2,
    workflowVersion: 1,
    calendarVersion: null,
    calendar: {
      timeZone: 'UTC',
      workingDays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
      holidaysAvailable: false,
      limitations: []
    },
    scheduledItems: [],
    unscheduledItems: [],
    milestones: [],
    dependencies: [],
    warnings: [],
    permissions,
    maximumItems: 500,
    totalItems: 0
  };
}

function waitForWorkspaceCreateResponse(page: Page) {
  return page.waitForResponse((response) =>
    response.request().method() === 'POST' &&
    new URL(response.url()).pathname === '/api/workspaces'
  );
}

function ganttItem(page: Page, taskId: string): Locator {
  return page.locator(`[data-gantt-item-id="${taskId}"]`);
}

async function expectLogicalGanttFocus(item: Locator): Promise<void> {
  await expect.poll(() => item.evaluate((element) =>
    element === document.activeElement || element.contains(document.activeElement)
  )).toBe(true);
}

async function expectHealthyAngularPage(page: Page) {
  const body = page.locator('body');
  await expect(body).not.toContainText('Cannot GET /');
  await expect(body).not.toContainText('Application error');
  await expect(body).not.toContainText(/NG0\d+/);
  await expect(body).not.toContainText('TypeError');
  await expect(page.locator('app-root')).toBeAttached();
}

async function waitForWorkspaceShellReady(
  page: Page,
  options: { mobile?: boolean } = {}
) {
  await expect(page.getByTestId('app-shell')).toBeVisible();
  await expect(page.getByTestId('shell-body')).toBeVisible();
  await expect(page.getByTestId('workspace-dashboard')).toBeVisible();

  if (options.mobile) {
    await expect(page.getByTestId('mobile-header')).toBeVisible();
    await expect(page.getByTestId('mobile-nav-toggle')).toBeVisible();
  }
}

async function pressTabUntilFocused(
  page: Page,
  target: Locator,
  maxTabs = 12
) {
  for (let index = 0; index < maxTabs; index += 1) {
    if (await target.evaluate((element) => element === document.activeElement).catch(() => false)) {
      return;
    }

    await page.keyboard.press('Tab');
  }

  await expect(target).toBeFocused();
}

async function expectNoDocumentHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => {
    const documentElement = document.documentElement;
    const body = document.body;
    return {
      bodyScrollWidth: body.scrollWidth,
      documentScrollWidth: documentElement.scrollWidth,
      viewportWidth: documentElement.clientWidth
    };
  });

  expect(overflow.documentScrollWidth).toBeLessThanOrEqual(overflow.viewportWidth);
  expect(overflow.bodyScrollWidth).toBeLessThanOrEqual(overflow.viewportWidth);
}

async function expectStableScreenshot(
  page: Page,
  testInfo: TestInfo,
  name: string,
  options: { fullPage?: boolean; maxDiffPixelRatio?: number } = {}
) {
  await page.evaluate(() => document.fonts?.ready);
  testInfo.annotations.push({
    type: 'Angular P0 screenshot baseline',
    description: name
  });
  await expect(page).toHaveScreenshot(name, {
    animations: 'disabled',
    caret: 'hide',
    fullPage: options.fullPage ?? true,
    maxDiffPixelRatio: options.maxDiffPixelRatio,
    scale: 'css'
  });
}
