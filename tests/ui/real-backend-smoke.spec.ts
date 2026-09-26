import { randomUUID } from 'node:crypto';
import { expect, type Locator, type Page, type Response as PlaywrightResponse, test } from '@playwright/test';
import {
  classifyUnexpectedApiFailures,
  classifyUnexpectedConsoleErrors,
  isExpectedFailure,
  selectPr03cExpectedRevocationRefreshFailures,
} from './real-backend-failure-correlation.mjs';

const smokeEmail = process.env.COGLATAS_BROWSER_SMOKE_EMAIL ?? '';
const smokePassword = process.env.COGLATAS_BROWSER_SMOKE_PASSWORD ?? '';

const smokeWorkspaceName = 'Browser Smoke Workspace';
const smokeAnnouncementTitle = 'Browser smoke announcement';
const smokeProjectTitle = 'Browser Smoke Project';
const smokeTaskTitle = 'Browser smoke task';
const smokeRecipientName = 'Browser Smoke Recipient';
const smokeTaskLabelName = 'Browser smoke label';
const smokeTaskFileName = 'browser-smoke-task.txt';
const pr05ManagerEmail = 'browser-smoke-pr05-manager@example.test';
const pr06ViewerEmail = 'browser-smoke-recipient@example.test';
const pr05ProjectTitle = 'PR05 Browser Acceptance Project';
const pr05ProjectSlug = 'browser-smoke-pr05-kanban';
const pr05ResponseGateCookieName = 'CoglatasBrowserSmokeResponseGate';
const pr05ResponseGateHeaderName = 'x-coglatas-browser-smoke-response-gate';
const pr05ResponseGatePath = '/internal/browser-smoke/response-gates';
const pr05TaskTitles = {
  move: 'PR05 real move card',
  reorder: 'PR05 stable reorder card',
  neighbor: 'PR05 stable neighbor card',
  cancellation: 'PR05 cancellation card',
  conflict: 'PR05 stale conflict card'
} as const;
const pr06ProjectTitle = 'PR06 Browser Acceptance Project';
const pr06ProjectSlug = 'browser-smoke-pr06-gantt';
const pr06TaskTitles = {
  parent: 'PR06 derived parent',
  schedule: 'PR06 schedule task',
  predecessor: 'PR06 predecessor task',
  unscheduled: 'PR06 unscheduled task',
  conflict: 'PR06 conflict task',
  successor: 'PR06 dependency successor'
} as const;
const pr06MilestoneTitle = 'PR06 release milestone';
const pr07NotificationFixturePath = '/internal/browser-smoke/notifications';
const pr07ProjectTitle = 'PR07 Browser Smoke Notifications Project';
const pr07TaskTitle = 'PR07 authorized notification task';
const pr07NotificationTitle = 'PR07D authorized delivery smoke notification';

test.describe('MVP0 real backend browser smoke', () => {
  test.setTimeout(120_000);

  test.beforeAll(() => {
    if (process.env.COGLATAS_REAL_BACKEND_SMOKE !== '1') {
      throw new Error('This real-backend smoke requires COGLATAS_REAL_BACKEND_SMOKE=1. Use `npm run test:ui:real-backend`; do not run it against the static Angular mock server.');
    }

    const baseURL = process.env.PLAYWRIGHT_BASE_URL;
    if (!baseURL || /^(?:http:\/\/)?(?:127\.0\.0\.1|localhost):4173(?:\/|$)/i.test(baseURL)) {
      throw new Error('The real-backend smoke requires a non-static PLAYWRIGHT_BASE_URL. Use `npm run test:ui:real-backend`, which runs Playwright inside Compose at http://coglatas-backend:8080.');
    }

    if (!smokeEmail || !smokeEmail.toLowerCase().endsWith('@example.test') || !smokePassword) {
      throw new Error('The real-backend smoke requires defined synthetic @example.test seed credentials. Use `npm run test:ui:real-backend`.');
    }
  });

  test.beforeEach(async ({}, testInfo) => {
    test.skip(
      testInfo.project.name !== 'chromium-desktop',
      'Real-backend smoke runs once against the desktop browser project because it uses a shared seeded backend account.'
    );
  });

  test('exercises mandatory authenticated MVP0 flows through ASP.NET Core backend', async ({ page }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: smokeEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: []
    };

    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') {
        evidence.consoleErrors.push(message.text());
      }
    });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      const hubNegotiate = page.waitForRequest((request) => new URL(request.url()).pathname === '/hubs/app/negotiate');
      await loginAndVerifySession(page, evidence);
      await verifyRealtimeRuntimeConfig(page, evidence);
      await hubNegotiate;
      await verifyRealtimeTransportReconnect(page, evidence);
      await assertCsrfRejectsMissingToken(page, evidence);
      await openWorkspaces(page, evidence);
      await createDirectMessageAndVerifyPersistence(page, evidence);
      await openAnnouncementDetail(page, evidence);
      await openProjectTaskDetail(page, evidence);
      await createProjectTaskThroughUi(page, evidence);
      await submitInvalidPasswordChange(page, evidence);
      await logoutAndVerifyAccessRevoked(page, evidence);

      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence);
      expectUnexpectedApiFailures(evidence);
    } finally {
      await testInfo.attach('real-backend-smoke-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('creates and activates a Workspace through the delegated real-backend capability', async ({ page }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: smokeEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: []
    };
    const workspaceName = `U22 Browser Workspace ${randomUUID().slice(0, 8)}`;
    const workspaceDescription = 'Synthetic U-22 Workspace creation evidence';
    let createdWorkspaceId: string | null = null;

    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') {evidence.consoleErrors.push(message.text());}
    });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(page, evidence);

      const tenantContext = await recordFetchJson(
        page,
        evidence,
        'workspace-create-tenant-member-boundary',
        '/api/tenants/current',
        {
          validate: (body) =>
            body &&
            typeof body === 'object' &&
            (body as Record<string, unknown>).isAvailable === true &&
            (body as Record<string, unknown>).isPlatformScope === false &&
            (body as Record<string, unknown>).currentUserRole === 3
        }
      ) as Record<string, unknown>;
      expect(tenantContext.currentUserRole, 'delegated creator remains a Tenant Member').toBe(3);

      await recordFetchJson(
        page,
        evidence,
        'workspace-create-capability',
        '/api/workspaces/capabilities',
        {
          validate: (body) =>
            body &&
            typeof body === 'object' &&
            (body as Record<string, any>).data?.canCreate === true &&
            Array.isArray((body as Record<string, unknown>).warnings)
        }
      );

      await page.goto('/app/workspaces');
      await expect(page.getByTestId('workspace-dashboard')).toBeVisible();
      const createAction = page.getByTestId('create-workspace-action');
      await expect(createAction).toBeVisible();
      await createAction.click();
      await expect(page.getByRole('dialog', { name: 'Create Workspace' })).toBeVisible();
      await page.getByTestId('workspace-create-name').fill(workspaceName);
      await page.getByTestId('workspace-create-description').fill(workspaceDescription);

      const createResponsePromise = waitForApiResponse(page, 'POST', '/api/workspaces');
      await page.getByRole('button', { name: 'Create Workspace' }).click();
      const createResponse = await createResponsePromise;
      const createText = await createResponse.text();
      const createBody = parseJson(createText) as Record<string, any>;
      createdWorkspaceId = typeof createBody?.data?.id === 'string' ? createBody.data.id : null;

      const createRequest = createResponse.request();
      const createHeaders = await createRequest.allHeaders();
      const idempotencyKey = createHeaders['idempotency-key'] ?? '';
      const csrfHeaderPresent = typeof createHeaders['x-csrf-token'] === 'string' && createHeaders['x-csrf-token'].length > 0;
      const createRequestBody = createRequest.postDataJSON() as Record<string, unknown>;
      evidence.steps.push({
        name: 'workspace-create-ui-command',
        method: createRequest.method(),
        path: new URL(createResponse.url()).pathname,
        status: createResponse.status(),
        body: {
          request: createRequestBody,
          idempotencyKeyPresent: idempotencyKey.length > 0,
          csrfHeaderPresent
        },
        bodyPreview: preview(createText)
      });

      expect(createResponse.status(), `Workspace create response: ${createText}`).toBe(201);
      expect(createResponse.ok(), `Workspace create response: ${createText}`).toBe(true);
      expect(createdWorkspaceId, 'created Workspace id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(createBody).toMatchObject({
        data: {
          id: createdWorkspaceId,
          name: workspaceName,
          description: workspaceDescription,
          icon: null,
          status: 0,
          createdByUserId: evidence.userId
        },
        warnings: []
      });
      expect(createRequestBody).toEqual({
        name: workspaceName,
        description: workspaceDescription,
        icon: null
      });
      expect(idempotencyKey).toMatch(/^[\x20-\x7e]{8,128}$/u);
      expect(csrfHeaderPresent, 'Workspace create uses the real Angular CSRF interceptor').toBe(true);

      await expect(page).toHaveURL(/\/app\/workspaces$/);
      await expect(page.getByTestId('workspace-card').filter({ hasText: workspaceName })).toBeVisible();
      await expect(page.getByTestId('workspace-switcher')).toHaveValue(createdWorkspaceId!);
      await expect(page.getByTestId('workspace-created-announcement')).toContainText(workspaceName);

      const members = await recordFetchJson(
        page,
        evidence,
        'workspace-create-owner-membership',
        `/api/workspaces/${createdWorkspaceId}/members`,
        {
          validate: (body) =>
            Array.isArray(body) &&
            body.some((member: Record<string, unknown>) =>
              member.userId === evidence.userId &&
              member.role === 0 &&
              member.status === 1)
        }
      ) as Record<string, unknown>[];
      expect(
        members.filter((member) =>
          member.userId === evidence.userId &&
          member.role === 0 &&
          member.status === 1),
        'creator has exactly one active Workspace Owner membership'
      ).toHaveLength(1);

      const conversations = await recordFetchJson(
        page,
        evidence,
        'workspace-create-general-conversation-list',
        '/api/conversations?page=1&pageSize=100',
        {
          validate: (body) => isPagedResponse(body)
        }
      ) as Record<string, any>;
      const workspaceGeneral = conversations.items.filter((conversation: Record<string, unknown>) =>
        conversation.workspaceId === createdWorkspaceId &&
        conversation.projectId === null &&
        conversation.type === 'WorkspaceChannel' &&
        conversation.title === 'general'
      );
      expect(
        workspaceGeneral,
        'the authorized projection exposes exactly one canonical general Workspace channel'
      ).toHaveLength(1);

      const generalDetail = await recordFetchJson(
        page,
        evidence,
        'workspace-create-general-conversation-detail',
        `/api/conversations/${workspaceGeneral[0].id}`,
        {
          validate: (body) =>
            body?.workspaceId === createdWorkspaceId &&
            body.projectId === null &&
            body.type === 'WorkspaceChannel' &&
            body.title === 'general' &&
            Array.isArray(body.members) &&
            body.members.some((member: Record<string, unknown>) =>
              member.userId === evidence.userId &&
              member.role === 0 &&
              member.canRead === true &&
              member.canPost === true &&
              member.canManageMembers === true)
        }
      ) as Record<string, any>;
      expect(generalDetail.members).toHaveLength(1);
    } finally {
      if (createdWorkspaceId) {
        const cleanup = await requestWithCsrf(
          page,
          'POST',
          `/api/workspaces/${createdWorkspaceId}/archive`
        );
        evidence.steps.push({
          name: 'workspace-create-cleanup-archive',
          method: 'POST',
          path: `/api/workspaces/${createdWorkspaceId}/archive`,
          status: cleanup.status,
          bodyPreview: preview(cleanup.text)
        });
        expect(cleanup.status, `Workspace cleanup archive: ${cleanup.text}`).toBe(200);
        expect(cleanup.csrfHeaderPresent, 'Workspace cleanup uses a real CSRF token').toBe(true);

        await expect.poll(async () => {
          const list = await fetchJsonFromPage(page, '/api/workspaces');
          return list.status === 200 &&
            Array.isArray(list.body) &&
            !list.body.some((workspace: Record<string, unknown>) => workspace.id === createdWorkspaceId);
        }).toBe(true);
      }

      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence);
      expectUnexpectedApiFailures(evidence);
      await testInfo.attach('workspace-create-real-backend-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('creates a Draft Project and explicitly activates it through the real backend', async ({ page }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: smokeEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: []
    };
    const projectTitle = `U22 Draft Activation ${randomUUID().slice(0, 8)}`;
    const projectDescription = 'Synthetic U-22 canonical Project creation and activation evidence';
    let workspaceId: string | null = null;
    let createdProjectId: string | null = null;
    const projectOperationalRequests: string[] = [];
    let observedProjectCreatePosts = 0;

    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') {evidence.consoleErrors.push(message.text());}
    });
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (workspaceId && request.method() === 'POST' && path === `/api/workspaces/${workspaceId}/projects`) {
        observedProjectCreatePosts += 1;
      }

      if (!createdProjectId || request.method() !== 'GET') {return;}
      if ([
        `/api/projects/${createdProjectId}/tasks`,
        `/api/projects/${createdProjectId}/kanban`,
        `/api/projects/${createdProjectId}/gantt`,
        `/api/projects/${createdProjectId}/workload`,
        `/api/projects/${createdProjectId}/members`
      ].includes(path)) {
        projectOperationalRequests.push(path);
      }
    });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(page, evidence);

      const workspaces = await recordFetchJson(
        page,
        evidence,
        'project-create-workspace-capability',
        '/api/workspaces',
        {
          validate: (body) =>
            Array.isArray(body) &&
            body.some((workspace: Record<string, unknown>) =>
              workspace.name === smokeWorkspaceName &&
              (workspace.status === 0 || workspace.status === 'Active') &&
              workspace.canOpenProjectCreate === true)
        }
      ) as Record<string, any>[];
      const primaryWorkspace = workspaces.find((workspace) => workspace.name === smokeWorkspaceName)!;
      workspaceId = String(primaryWorkspace.id);
      evidence.workspaceId = workspaceId;
      expect(workspaceId, 'primary Workspace id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(primaryWorkspace.canOpenProjectCreate, 'server-projected Project create affordance').toBe(true);

      const directOptions = await recordFetchJson(
        page,
        evidence,
        'project-create-options',
        `/api/workspaces/${workspaceId}/projects/create-options`,
        {
          validate: (body) => {
            const data = (body as Record<string, any>)?.data;
            return hasString(body, 'requestId') &&
              data?.workspaceId === workspaceId &&
              typeof data?.canCreateUngrouped === 'boolean' &&
              Array.isArray(data?.allowedVisibilities) &&
              data.allowedVisibilities.includes(1) &&
              Array.isArray(data?.groups) &&
              Array.isArray((body as Record<string, unknown>)?.warnings);
          }
        }
      ) as Record<string, any>;
      const group = directOptions.data.groups.find(
        (candidate: Record<string, unknown>) => candidate.name === 'Browser Smoke PR04 Queue'
      ) ?? directOptions.data.groups[0];
      expect(group, 'an authorized named Group is available for canonical Project creation').toBeTruthy();
      expect(group.id, 'authorized Group id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(group.name, 'authorized Group is presented by name').toBeTruthy();

      await page.goto('/app/workspaces');
      await expect(page.getByTestId('workspace-dashboard')).toBeVisible();
      const workspaceSwitcher = page.getByTestId('workspace-switcher');
      await workspaceSwitcher.selectOption(workspaceId);
      await expect(workspaceSwitcher).toHaveValue(workspaceId);

      await page.goto('/app/projects');
      await expect(page.getByTestId('projects-overview-page')).toBeVisible();
      const createAction = page.getByTestId('projects-create-project');
      await expect(createAction).toBeVisible();
      const uiOptionsResponse = waitForApiResponse(
        page,
        'GET',
        `/api/workspaces/${workspaceId}/projects/create-options`
      );
      await createAction.click();
      await recordOkJson(await uiOptionsResponse, evidence, 'project-create-options-ui', (body) =>
        hasString(body, 'requestId') &&
        (body as Record<string, any>)?.data?.workspaceId === workspaceId &&
        Array.isArray((body as Record<string, any>)?.data?.groups)
      );

      const dialog = page.getByRole('dialog', { name: 'Create Project' });
      await expect(dialog).toBeVisible();
      await expect(page.getByTestId('project-create-title')).toBeFocused();
      await expect(page.getByTestId('project-create-group')).toContainText(String(group.name));
      await expect(dialog).not.toContainText(String(group.id));
      await page.getByTestId('project-create-title').fill(projectTitle);
      await page.getByTestId('project-create-description').fill(projectDescription);
      await page.getByTestId('project-create-group').selectOption(String(group.id));
      await page.getByTestId('project-create-visibility').selectOption({ label: 'Members only' });
      await page.getByTestId('project-create-start-date').fill('2026-09-01');
      await page.getByTestId('project-create-end-date').fill('2026-09-30');

      const firstCreateOutcome = waitForProjectCreateOutcome(
        page,
        `/api/workspaces/${workspaceId}/projects`
      );
      await dialog.getByRole('button', { name: 'Create Project' }).click();
      let createResponse: PlaywrightResponse;
      const resolvedFirstCreateOutcome = await firstCreateOutcome;
      if (resolvedFirstCreateOutcome.kind === 'stopped') {
        expect(observedProjectCreatePosts, 'no Project POST was dispatched before reauthorization').toBe(0);

        const reauthorizedOptions = waitForApiResponse(
          page,
          'GET',
          `/api/workspaces/${workspaceId}/projects/create-options`
        );
        await page.getByTestId('project-create-options-retry').click();
        await expect(page.getByTestId('project-create-form')).toBeVisible();
        expect(observedProjectCreatePosts, 'options reauthorization never posts automatically').toBe(0);
        const reauthorizedOptionsResponse = await reauthorizedOptions;
        expect(reauthorizedOptionsResponse.ok(), 'reauthorized Project create options').toBe(true);

        const retryCreateResponse = waitForApiResponse(
          page,
          'POST',
          `/api/workspaces/${workspaceId}/projects`
        );
        await dialog.getByRole('button', { name: 'Create Project' }).click();
        createResponse = await retryCreateResponse;
      } else {
        createResponse = resolvedFirstCreateOutcome.response;
      }
      expect(observedProjectCreatePosts, 'one explicit Project POST is observed').toBe(1);
      const createText = await createResponse.text();
      const createBody = parseJson(createText) as Record<string, any>;
      const createRequest = createResponse.request();
      const createHeaders = await createRequest.allHeaders();
      const createRequestBody = createRequest.postDataJSON() as Record<string, unknown>;
      createdProjectId = typeof createBody?.data?.id === 'string' ? createBody.data.id : null;
      evidence.steps.push({
        name: 'project-create-ui-command',
        method: createRequest.method(),
        path: new URL(createResponse.url()).pathname,
        status: createResponse.status(),
        body: {
          request: createRequestBody,
          idempotencyKeyPresent: Boolean(createHeaders['idempotency-key']),
          csrfHeaderPresent: Boolean(createHeaders['x-csrf-token'])
        },
        bodyPreview: preview(createText)
      });

      expect(createResponse.status(), `Project create response: ${createText}`).toBe(201);
      expect(createdProjectId, 'created Project id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(createRequestBody).toEqual({
        title: projectTitle,
        description: projectDescription,
        groupId: group.id,
        visibility: 1,
        startDate: '2026-09-01',
        endDate: '2026-09-30'
      });
      expect(createHeaders['idempotency-key']).toMatch(/^[\x20-\x7e]{8,128}$/u);
      expect(createHeaders['x-csrf-token'], 'Project create uses the real Angular CSRF interceptor').toBeTruthy();
      expect(createBody).toMatchObject({
        data: {
          id: createdProjectId,
          workspaceId,
          groupId: group.id,
          ownerUserId: evidence.userId,
          title: projectTitle,
          description: projectDescription,
          status: 0,
          visibility: 1,
          activationState: 1,
          startDate: '2026-09-01',
          endDate: '2026-09-30',
          versionNo: 1
        },
        warnings: []
      });

      await expect(page).toHaveURL(`/app/projects/${createdProjectId}`);
      await expect(page.getByTestId('project-draft-overview')).toBeVisible();
      await expect(page.getByRole('tab')).toHaveCount(1);
      expect(projectOperationalRequests, 'Draft detail performs zero operational reads').toEqual([]);
      const draftResourcePaths = await page.evaluate((projectId) =>
        performance.getEntriesByType('resource')
          .map((entry) => new URL(entry.name, window.location.href).pathname)
          .filter((path) => [
            `/api/projects/${projectId}/tasks`,
            `/api/projects/${projectId}/kanban`,
            `/api/projects/${projectId}/gantt`,
            `/api/projects/${projectId}/workload`,
            `/api/projects/${projectId}/members`
          ].includes(path)), createdProjectId);
      expect(draftResourcePaths, 'browser resource history confirms zero Draft operational reads').toEqual([]);

      const draft = await recordFetchJson(
        page,
        evidence,
        'project-create-authoritative-draft',
        `/api/projects/${createdProjectId}`,
        {
          validate: (body) =>
            (body as Record<string, any>)?.id === createdProjectId &&
            (body as Record<string, any>)?.workspaceId === workspaceId &&
            (body as Record<string, any>)?.status === 0 &&
            (body as Record<string, any>)?.activationState === 1 &&
            (body as Record<string, any>)?.versionNo === 1 &&
            (body as Record<string, any>)?.activatedAtUtc === null &&
            (body as Record<string, any>)?.uiPermissions?.canActivate === true
        }
      ) as Record<string, any>;
      expect(draft.ownerUserId).toBe(evidence.userId);

      const members = await recordFetchJson(
        page,
        evidence,
        'project-create-owner-membership',
        `/api/projects/${createdProjectId}/members`,
        {
          validate: (body) =>
            Array.isArray(body) &&
            body.filter((member: Record<string, unknown>) =>
              member.userId === evidence.userId && member.role === 0).length === 1
        }
      ) as Record<string, unknown>[];
      expect(members, 'creator is the sole initial Project member').toHaveLength(1);

      const conversationsBeforeActivation = await recordFetchJson(
        page,
        evidence,
        'project-create-no-general-before-activation',
        '/api/conversations?page=1&pageSize=100',
        { validate: (body) => isPagedResponse(body) }
      ) as Record<string, any>;
      expect(
        conversationsBeforeActivation.items.filter(
          (conversation: Record<string, unknown>) => conversation.projectId === createdProjectId
        ),
        'Draft has no ProjectGeneral conversation before activation'
      ).toHaveLength(0);

      const activateAction = page.getByTestId('activate-project');
      await expect(activateAction).toBeVisible();
      const activationResponsePromise = waitForApiResponse(
        page,
        'POST',
        `/api/projects/${createdProjectId}/activate`
      );
      await activateAction.click();
      const activationResponse = await activationResponsePromise;
      const activationText = await activationResponse.text();
      const activationBody = parseJson(activationText) as Record<string, any>;
      const activationRequest = activationResponse.request();
      const activationHeaders = await activationRequest.allHeaders();
      const activationRequestBody = activationRequest.postDataJSON() as Record<string, unknown>;
      evidence.steps.push({
        name: 'project-activate-ui-command',
        method: activationRequest.method(),
        path: new URL(activationResponse.url()).pathname,
        status: activationResponse.status(),
        body: {
          request: activationRequestBody,
          csrfHeaderPresent: Boolean(activationHeaders['x-csrf-token'])
        },
        bodyPreview: preview(activationText)
      });
      expect(activationResponse.status(), `Project activation response: ${activationText}`).toBe(200);
      expect(activationRequestBody).toEqual({ expectedVersion: 1 });
      expect(activationHeaders['x-csrf-token'], 'Project activation uses the real Angular CSRF interceptor').toBeTruthy();
      expect(activationBody).toMatchObject({
        data: { projectId: createdProjectId },
        warnings: []
      });
      expect(activationBody.requestId).toEqual(expect.any(String));

      await expect(page.locator('.project-detail-page__activation-status')).toContainText(
        'Project activated. Operational views were loaded from authoritative state.'
      );
      await expect(page.getByTestId('project-draft-overview')).toHaveCount(0);
      await expect(page.getByRole('tab', { name: 'Tasks' })).toBeVisible();

      const activated = await recordFetchJson(
        page,
        evidence,
        'project-activate-authoritative-state',
        `/api/projects/${createdProjectId}`,
        {
          validate: (body) =>
            (body as Record<string, any>)?.id === createdProjectId &&
            (body as Record<string, any>)?.status === 1 &&
            (body as Record<string, any>)?.activationState === 2 &&
            (body as Record<string, any>)?.versionNo === 2 &&
            hasString(body, 'activatedAtUtc') &&
            (body as Record<string, any>)?.activationVersion === 1 &&
            (body as Record<string, any>)?.uiPermissions?.canActivate === false
        }
      ) as Record<string, any>;
      expect(activated.ownerUserId).toBe(evidence.userId);

      const kanban = await recordFetchJson(
        page,
        evidence,
        'project-activate-generated-workflow',
        `/api/projects/${createdProjectId}/kanban`,
        {
          validate: (body) =>
            (body as Record<string, any>)?.board?.projectId === createdProjectId &&
            typeof (body as Record<string, any>)?.board?.version === 'number' &&
            Array.isArray((body as Record<string, any>)?.columns) &&
            (body as Record<string, any>).columns.length > 0 &&
            (body as Record<string, any>).columns.every(
              (column: Record<string, unknown>) =>
                typeof column.workflowStageId === 'string' && typeof column.displayName === 'string'
            )
        }
      ) as Record<string, any>;
      expect(kanban.columns.length, 'activation generated a readable canonical Task workflow').toBeGreaterThan(0);

      const conversationsAfterActivation = await recordFetchJson(
        page,
        evidence,
        'project-activate-general-conversation',
        '/api/conversations?page=1&pageSize=100',
        { validate: (body) => isPagedResponse(body) }
      ) as Record<string, any>;
      const projectGeneral = conversationsAfterActivation.items.filter(
        (conversation: Record<string, unknown>) =>
          conversation.projectId === createdProjectId &&
          conversation.workspaceId === workspaceId &&
          conversation.type === 'ProjectChannel' &&
          conversation.title === 'general'
      );
      expect(projectGeneral, 'activation generated exactly one authorized ProjectGeneral').toHaveLength(1);
      const generalDetail = await recordFetchJson(
        page,
        evidence,
        'project-activate-general-detail',
        `/api/conversations/${projectGeneral[0].id}`,
        {
          validate: (body) =>
            (body as Record<string, any>)?.projectId === createdProjectId &&
            (body as Record<string, any>)?.type === 'ProjectChannel' &&
            (body as Record<string, any>)?.title === 'general' &&
            Array.isArray((body as Record<string, any>)?.members) &&
            (body as Record<string, any>).members.some(
              (member: Record<string, unknown>) =>
                member.userId === evidence.userId && member.canRead === true && member.canPost === true
            )
        }
      ) as Record<string, any>;
      expect(generalDetail.members).toHaveLength(1);
      const activatedResourcePaths = await page.evaluate((projectId) =>
        performance.getEntriesByType('resource')
          .map((entry) => new URL(entry.name, window.location.href).pathname)
          .filter((path) => path.startsWith(`/api/projects/${projectId}/`)), createdProjectId);
      expect(
        activatedResourcePaths.some((path) => path === `/api/projects/${createdProjectId}/tasks`),
        'operational Task reads start only after activation'
      ).toBe(true);
    } finally {
      if (createdProjectId) {
        await page.goto('/app/workspaces').catch(() => undefined);
        const cleanup = await requestWithCsrf(
          page,
          'POST',
          `/api/projects/${createdProjectId}/archive`
        );
        evidence.steps.push({
          name: 'project-create-cleanup-archive',
          method: 'POST',
          path: `/api/projects/${createdProjectId}/archive`,
          status: cleanup.status,
          bodyPreview: preview(cleanup.text)
        });
        expect(cleanup.status, `Project cleanup archive: ${cleanup.text}`).toBe(200);
        expect(cleanup.csrfHeaderPresent, 'Project cleanup uses a real CSRF token').toBe(true);

        const cleanupWorkspaceId = workspaceId;
        expect(cleanupWorkspaceId, 'Project cleanup retains its Workspace scope').toMatch(/^[0-9a-f-]{36}$/i);
        await expect.poll(async () => {
          const list = await fetchJsonFromPage(
            page,
            `/api/projects?workspaceId=${encodeURIComponent(cleanupWorkspaceId!)}`
          );
          return list.status === 200 &&
            isPagedResponse(list.body) &&
            !list.body.items.some(
              (project: Record<string, unknown>) => project.id === createdProjectId
            );
        }).toBe(true);
        await recordFetchJson(
          page,
          evidence,
          'project-create-cleanup-scoped-list',
          `/api/projects?workspaceId=${encodeURIComponent(cleanupWorkspaceId!)}`,
          {
            validate: (body) =>
              isPagedResponse(body) &&
              !(body as Record<string, any>).items.some(
                (project: Record<string, unknown>) => project.id === createdProjectId
              )
          }
        );
      }

      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence);
      expectUnexpectedApiFailures(evidence);
      await testInfo.attach('project-create-activation-real-backend-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('U-22 completes the same-lineage Workspace, Project, and Task journey through the real backend', async ({ page }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: smokeEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: []
    };
    const suffix = randomUUID().slice(0, 8);
    const workspaceName = `U22 Journey Workspace ${suffix}`;
    const workspaceDescription = 'Synthetic U-22 same-lineage Workspace evidence';
    const projectTitle = `U22 Journey Project ${suffix}`;
    const projectDescription = 'Synthetic U-22 ungrouped Project evidence';
    const taskTitle = `U22 Journey Task ${suffix}`;
    const taskDescription = 'A real-backend Task created from its active Project.';
    const goal = 'Document the requested U-22 outcome.';
    const deliverable = 'A concise, reviewable U-22 Task result.';
    const constraints = 'Remain policy-only; do not start a runtime or retrieve sources.';
    const startDate = '2026-09-10';
    const dueDate = '2026-09-20';
    let createdWorkspaceId: string | null = null;
    let createdProjectId: string | null = null;
    let createdTaskId: string | null = null;
    let observedProjectCreatePosts = 0;
    let observedTaskCreatePosts = 0;
    const executionRunRequests: string[] = [];

    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => {
      if (message.type() === 'error') {evidence.consoleErrors.push(message.text());}
    });
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (createdWorkspaceId && request.method() === 'POST' && path === `/api/workspaces/${createdWorkspaceId}/projects`) {
        observedProjectCreatePosts += 1;
      }
      if (createdProjectId && request.method() === 'POST' && path === `/api/projects/${createdProjectId}/tasks/create`) {
        observedTaskCreatePosts += 1;
      }
      if (createdTaskId && path === `/api/tasks/${createdTaskId}/execution-runs`) {
        executionRunRequests.push(`${request.method()} ${path}`);
      }
    });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(page, evidence);

      await page.goto('/app/workspaces');
      await expect(page.getByTestId('workspace-dashboard')).toBeVisible();
      await page.getByTestId('create-workspace-action').click();
      const workspaceDialog = page.getByRole('dialog', { name: 'Create Workspace' });
      await expect(workspaceDialog).toBeVisible();
      await page.getByTestId('workspace-create-name').fill(workspaceName);
      await page.getByTestId('workspace-create-description').fill(workspaceDescription);

      const workspaceCreateResponsePromise = waitForApiResponse(page, 'POST', '/api/workspaces');
      await workspaceDialog.getByRole('button', { name: 'Create Workspace' }).click();
      const workspaceCreateResponse = await workspaceCreateResponsePromise;
      const workspaceCreateText = await workspaceCreateResponse.text();
      const workspaceCreateBody = parseJson(workspaceCreateText) as Record<string, any>;
      const workspaceCreateRequest = workspaceCreateResponse.request();
      const workspaceCreateHeaders = await workspaceCreateRequest.allHeaders();
      const workspaceCreateRequestBody = workspaceCreateRequest.postDataJSON() as Record<string, unknown>;
      createdWorkspaceId = typeof workspaceCreateBody?.data?.id === 'string' ? workspaceCreateBody.data.id : null;
      evidence.workspaceId = createdWorkspaceId ?? undefined;
      evidence.steps.push({
        name: 'u22-journey-workspace-create-ui-command',
        method: workspaceCreateRequest.method(),
        path: new URL(workspaceCreateResponse.url()).pathname,
        status: workspaceCreateResponse.status(),
        body: {
          request: workspaceCreateRequestBody,
          idempotencyKeyPresent: Boolean(workspaceCreateHeaders['idempotency-key']),
          csrfHeaderPresent: Boolean(workspaceCreateHeaders['x-csrf-token'])
        },
        bodyPreview: preview(workspaceCreateText)
      });
      expect(workspaceCreateResponse.status(), `U-22 Workspace create response: ${workspaceCreateText}`).toBe(201);
      expect(createdWorkspaceId, 'U-22 created Workspace id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(workspaceCreateRequestBody).toEqual({ name: workspaceName, description: workspaceDescription, icon: null });
      expect(workspaceCreateHeaders['idempotency-key'], 'Workspace create idempotency key').toMatch(/^[\x20-\x7e]{8,128}$/u);
      expect(workspaceCreateHeaders['x-csrf-token'], 'Workspace create uses the real Angular CSRF interceptor').toBeTruthy();
      expect(workspaceCreateBody).toMatchObject({
        data: { id: createdWorkspaceId, name: workspaceName, description: workspaceDescription, status: 0, createdByUserId: evidence.userId },
        warnings: []
      });
      await expect(page.getByTestId('workspace-switcher')).toHaveValue(createdWorkspaceId!);
      await expect(page.getByTestId('workspace-card').filter({ hasText: workspaceName })).toBeVisible();

      const projectOptionsPath = `/api/workspaces/${createdWorkspaceId}/projects/create-options`;
      await recordFetchJson(
        page,
        evidence,
        'u22-journey-project-create-options',
        projectOptionsPath,
        {
          validate: (body) => {
            const data = (body as Record<string, any>)?.data;
            return hasString(body, 'requestId') &&
              data?.workspaceId === createdWorkspaceId &&
              data?.canCreateUngrouped === true &&
              Array.isArray(data?.allowedVisibilities) &&
              data.allowedVisibilities.includes(1) &&
              Array.isArray(data?.groups) &&
              Array.isArray((body as Record<string, unknown>)?.warnings);
          }
        }
      );

      await page.goto('/app/projects');
      await expect(page.getByTestId('projects-overview-page')).toBeVisible();
      const projectOptionsResponsePromise = waitForApiResponse(page, 'GET', projectOptionsPath);
      await page.getByTestId('projects-create-project').click();
      await recordOkJson(await projectOptionsResponsePromise, evidence, 'u22-journey-project-create-options-ui', (body) =>
        (body as Record<string, any>)?.data?.workspaceId === createdWorkspaceId &&
        (body as Record<string, any>)?.data?.canCreateUngrouped === true
      );

      const projectDialog = page.getByRole('dialog', { name: 'Create Project' });
      await expect(projectDialog).toBeVisible();
      await expect(page.getByTestId('project-create-title')).toBeFocused();
      await page.getByTestId('project-create-title').fill(projectTitle);
      await page.getByTestId('project-create-description').fill(projectDescription);
      // A newly created Workspace has no Groups. The selector is intentionally
      // absent in that server-authorized empty-options state, and the canonical
      // Project contract permits creation at the Workspace root.
      const projectGroup = page.getByTestId('project-create-group');
      if (await projectGroup.count()) {
        await projectGroup.selectOption('');
      }
      await page.getByTestId('project-create-visibility').selectOption({ label: 'Members only' });

      const projectCreateOutcome = waitForProjectCreateOutcome(
        page,
        `/api/workspaces/${createdWorkspaceId}/projects`
      );
      await projectDialog.getByRole('button', { name: 'Create Project' }).click();
      let projectCreateResponse: PlaywrightResponse;
      const resolvedProjectCreateOutcome = await projectCreateOutcome;
      if (resolvedProjectCreateOutcome.kind === 'stopped') {
        expect(observedProjectCreatePosts, 'project create has not been retried automatically').toBe(0);
        const reauthorizedOptionsResponse = waitForApiResponse(page, 'GET', projectOptionsPath);
        await page.getByTestId('project-create-options-retry').click();
        await expect(page.getByTestId('project-create-form')).toBeVisible();
        await recordOkJson(await reauthorizedOptionsResponse, evidence, 'u22-journey-project-create-options-reauthorized', (body) =>
          (body as Record<string, any>)?.data?.workspaceId === createdWorkspaceId &&
          (body as Record<string, any>)?.data?.canCreateUngrouped === true
        );
        const retryProjectCreateResponse = waitForApiResponse(
          page,
          'POST',
          `/api/workspaces/${createdWorkspaceId}/projects`
        );
        await projectDialog.getByRole('button', { name: 'Create Project' }).click();
        projectCreateResponse = await retryProjectCreateResponse;
      } else {
        projectCreateResponse = resolvedProjectCreateOutcome.response;
      }

      const projectCreateText = await projectCreateResponse.text();
      const projectCreateBody = parseJson(projectCreateText) as Record<string, any>;
      const projectCreateRequest = projectCreateResponse.request();
      const projectCreateHeaders = await projectCreateRequest.allHeaders();
      const projectCreateRequestBody = projectCreateRequest.postDataJSON() as Record<string, unknown>;
      createdProjectId = typeof projectCreateBody?.data?.id === 'string' ? projectCreateBody.data.id : null;
      evidence.projectId = createdProjectId ?? undefined;
      evidence.steps.push({
        name: 'u22-journey-project-create-ui-command',
        method: projectCreateRequest.method(),
        path: new URL(projectCreateResponse.url()).pathname,
        status: projectCreateResponse.status(),
        body: {
          request: projectCreateRequestBody,
          idempotencyKeyPresent: Boolean(projectCreateHeaders['idempotency-key']),
          csrfHeaderPresent: Boolean(projectCreateHeaders['x-csrf-token'])
        },
        bodyPreview: preview(projectCreateText)
      });
      expect(observedProjectCreatePosts, 'one explicit ungrouped Project create is observed').toBe(1);
      expect(projectCreateResponse.status(), `U-22 Project create response: ${projectCreateText}`).toBe(201);
      expect(createdProjectId, 'U-22 created Project id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(projectCreateRequestBody).toEqual({
        title: projectTitle,
        description: projectDescription,
        groupId: null,
        visibility: 1,
        startDate: null,
        endDate: null
      });
      expect(projectCreateHeaders['idempotency-key'], 'Project create idempotency key').toMatch(/^[\x20-\x7e]{8,128}$/u);
      expect(projectCreateHeaders['x-csrf-token'], 'Project create uses the real Angular CSRF interceptor').toBeTruthy();
      expect(projectCreateBody).toMatchObject({
        data: {
          id: createdProjectId,
          workspaceId: createdWorkspaceId,
          groupId: null,
          ownerUserId: evidence.userId,
          title: projectTitle,
          description: projectDescription,
          status: 0,
          visibility: 1,
          activationState: 1,
          versionNo: 1
        },
        warnings: []
      });
      // Creating a Workspace can deliver its own authorization invalidation
      // immediately after this Project POST. The Project-create facade treats
      // that boundary fail-closed: it retains only the committed identity and
      // offers a GET/navigation-only recovery instead of assuming the old
      // authorization projection remains valid. Either automatic navigation
      // or that explicit recovery is a valid product outcome, but neither may
      // send a second create command.
      let projectOpenState: 'opened' | 'pending' | 'waiting' = 'waiting';
      await expect
        .poll(
          async () => {
            if (page.url().endsWith(`/app/projects/${createdProjectId}`)) {
              projectOpenState = 'opened';
              return projectOpenState;
            }

            projectOpenState = (await page.getByTestId('project-create-pending').isVisible())
              ? 'pending'
              : 'waiting';
            return projectOpenState;
          },
          { timeout: 15_000 },
        )
        .not.toBe('waiting');

      if (projectOpenState === 'pending') {
        await expect(page.getByTestId('project-create-pending')).toBeVisible();
        expect(observedProjectCreatePosts, 'navigation recovery does not repeat Project create').toBe(1);
        const recoveryConfirmation = waitForApiResponse(
          page,
          'GET',
          `/api/projects/${createdProjectId}`,
        );
        await projectDialog.getByRole('button', { name: 'Open Project' }).click();
        await recordOkJson(
          await recoveryConfirmation,
          evidence,
          'u22-journey-project-create-navigation-recovery',
          (body) =>
            (body as Record<string, unknown>)?.['id'] === createdProjectId &&
            (body as Record<string, unknown>)?.['workspaceId'] === createdWorkspaceId,
        );
        await expect(page).toHaveURL(`/app/projects/${createdProjectId}`);
      }
      await expect(page.getByTestId('project-draft-overview')).toBeVisible();

      const projectActivationResponsePromise = waitForApiResponse(page, 'POST', `/api/projects/${createdProjectId}/activate`);
      await page.getByTestId('activate-project').click();
      const projectActivationResponse = await projectActivationResponsePromise;
      const projectActivationText = await projectActivationResponse.text();
      const projectActivationBody = parseJson(projectActivationText) as Record<string, any>;
      const projectActivationRequest = projectActivationResponse.request();
      const projectActivationHeaders = await projectActivationRequest.allHeaders();
      evidence.steps.push({
        name: 'u22-journey-project-activate-ui-command',
        method: projectActivationRequest.method(),
        path: new URL(projectActivationResponse.url()).pathname,
        status: projectActivationResponse.status(),
        body: {
          request: projectActivationRequest.postDataJSON(),
          csrfHeaderPresent: Boolean(projectActivationHeaders['x-csrf-token'])
        },
        bodyPreview: preview(projectActivationText)
      });
      expect(projectActivationResponse.status(), `U-22 Project activation response: ${projectActivationText}`).toBe(200);
      expect(projectActivationRequest.postDataJSON()).toEqual({ expectedVersion: 1 });
      expect(projectActivationHeaders['x-csrf-token'], 'Project activation uses the real Angular CSRF interceptor').toBeTruthy();
      expect(projectActivationBody).toMatchObject({ data: { projectId: createdProjectId }, warnings: [] });
      await expect(page.getByRole('tab', { name: 'Tasks' })).toBeVisible();
      await recordFetchJson(page, evidence, 'u22-journey-project-activated', `/api/projects/${createdProjectId}`, {
        validate: (body) =>
          (body as Record<string, any>)?.id === createdProjectId &&
          (body as Record<string, any>)?.workspaceId === createdWorkspaceId &&
          (body as Record<string, any>)?.groupId === null &&
          (body as Record<string, any>)?.status === 1 &&
          (body as Record<string, any>)?.activationState === 2 &&
          (body as Record<string, any>)?.uiPermissions?.canCreateTask === true
      });

      const taskOptionsPath = `/api/projects/${createdProjectId}/tasks/create-options`;
      const taskOptionsResponse = await recordFetchJson(page, evidence, 'u22-journey-task-create-options', taskOptionsPath, {
        validate: (body) => {
          const data = (body as Record<string, any>)?.data;
          return hasString(body, 'requestId') &&
            data?.projectId === createdProjectId &&
            data?.workspaceId === createdWorkspaceId &&
            data?.projectTitle === projectTitle &&
            data?.canCreateTask === true &&
            data?.canManageProject === true &&
            Array.isArray(data?.milestones) &&
            Array.isArray(data?.assignees) &&
            data?.projectScope?.policy?.webEnabled === false &&
            data?.projectScope?.policy?.projectFilesEnabled === false &&
            data?.projectScope?.version === 1 &&
            data?.projectScope?.canSetTaskOverride === true &&
            Array.isArray((body as Record<string, unknown>)?.warnings);
        }
      }) as Record<string, any>;
      expect(taskOptionsResponse.data.milestones, 'fresh U-22 Project has no Milestone selected').toEqual([]);

      const taskOptionsUiResponsePromise = waitForApiResponse(page, 'GET', taskOptionsPath);
      await page.getByTestId('project-create-task').click();
      await recordOkJson(await taskOptionsUiResponsePromise, evidence, 'u22-journey-task-create-options-ui', (body) =>
        (body as Record<string, any>)?.data?.projectId === createdProjectId &&
        (body as Record<string, any>)?.data?.projectScope?.policy?.webEnabled === false &&
        (body as Record<string, any>)?.data?.projectScope?.policy?.projectFilesEnabled === false
      );
      await expect(page).toHaveURL(`/app/projects/${createdProjectId}/tasks/new`);
      await expect(page.getByTestId('task-create-title')).toBeFocused();
      await expect(page.getByTestId('task-create-page')).toContainText('does not start a runtime or retrieve sources');
      await expect(page.getByRole('button', { name: 'Start', exact: true })).toHaveCount(0);
      await expect(page.locator('[name="webUrl"], [name="provider"], [name="projectId"], [name="workspaceId"]')).toHaveCount(0);
      await expect(page.locator('#task-create-sourceScopeMode-inherit')).toBeChecked();

      const qualityChecklist = page.getByTestId('task-create-quality-checklist');
      await expect(qualityChecklist).toContainText('Advisory only: 1 of 4 items are covered.');
      await expect(qualityChecklist).toContainText('Project default policy: Web disabled; Project files disabled.');
      const addGoal = page.getByTestId('task-create-quality-goal').getByRole('button', { name: 'Add Goal' });
      await addGoal.focus();
      await page.keyboard.press('Enter');
      await expect(page.getByTestId('task-brief-goal-input')).toBeFocused();

      await page.getByTestId('task-create-title').fill(`  ${taskTitle}  `);
      await page.getByTestId('task-create-description').fill(taskDescription);
      await page.getByTestId('task-create-priority').selectOption('high');
      await page.getByTestId('task-create-start-date').fill(startDate);
      await page.getByTestId('task-create-due-date').fill(dueDate);
      await page.getByTestId('task-brief-goal-input').fill(goal);
      await page.getByTestId('task-brief-deliverable-input').fill(deliverable);
      await page.getByTestId('task-brief-constraints-input').fill(constraints);
      await expect(qualityChecklist).toContainText('Advisory only: 4 of 4 items are covered.');

      const taskCreatePath = `/api/projects/${createdProjectId}/tasks/create`;
      const taskCreateResponsePromise = waitForApiResponse(page, 'POST', taskCreatePath);
      await page.getByTestId('task-create-submit').click();
      const taskCreateResponse = await taskCreateResponsePromise;
      const taskCreateText = await taskCreateResponse.text();
      const taskCreateBody = parseJson(taskCreateText) as Record<string, any>;
      const taskCreateRequest = taskCreateResponse.request();
      const taskCreateHeaders = await taskCreateRequest.allHeaders();
      const taskCreateRequestBody = taskCreateRequest.postDataJSON() as Record<string, unknown>;
      createdTaskId = typeof taskCreateBody?.data?.taskId === 'string' ? taskCreateBody.data.taskId : null;
      evidence.taskId = createdTaskId ?? undefined;
      evidence.steps.push({
        name: 'u22-journey-task-create-ui-command',
        method: taskCreateRequest.method(),
        path: new URL(taskCreateResponse.url()).pathname,
        status: taskCreateResponse.status(),
        body: {
          request: taskCreateRequestBody,
          idempotencyKeyPresent: Boolean(taskCreateHeaders['idempotency-key']),
          csrfHeaderPresent: Boolean(taskCreateHeaders['x-csrf-token'])
        },
        bodyPreview: preview(taskCreateText)
      });
      expect(observedTaskCreatePosts, 'one explicit canonical Task create is observed').toBe(1);
      expect(taskCreateResponse.status(), `U-22 Task create response: ${taskCreateText}`).toBe(201);
      expect(createdTaskId, 'U-22 created Task id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(taskCreateRequestBody).toEqual({
        title: taskTitle,
        description: taskDescription,
        priority: 2,
        startDate,
        dueDate,
        goal,
        deliverable,
        constraints,
        sourceScopeMode: 'Inherit'
      });
      expect(taskCreateHeaders['idempotency-key'], 'Task create idempotency key').toMatch(/^task-create-[\x20-\x7e]+$/u);
      expect(taskCreateHeaders['x-csrf-token'], 'Task create uses the real Angular CSRF interceptor').toBeTruthy();
      expect(taskCreateBody).toMatchObject({
        data: {
          taskId: createdTaskId,
          projectId: createdProjectId,
          workspaceId: createdWorkspaceId,
          milestoneId: null,
          primaryAssigneeUserId: null,
          title: taskTitle,
          priority: 2,
          status: 0,
          workflowStageId: expect.stringMatching(/^[0-9a-f-]{36}$/i),
          version: 1,
          sourceScopeMode: 'Inherit',
          taskOverridePolicy: null
        },
        warnings: []
      });

      await expect(page).toHaveURL(`/app/projects/${createdProjectId}/tasks/${createdTaskId}`);
      await expect(page.getByTestId('task-detail-page')).toBeVisible();
      await expect(page.getByRole('heading', { name: taskTitle })).toBeVisible();
      const progress = page.getByTestId('task-progress-phase');
      await expect(progress.getByTestId('task-current-phase')).toHaveText('Backlog');
      await expect(progress.getByText('Waiting', { exact: true })).toBeVisible();
      const sourceScope = page.getByTestId('task-execution-scope');
      await expect(sourceScope).toBeVisible();
      await expect(sourceScope.getByTestId('task-execution-scope-origin')).toHaveText('Project default');
      await expect(sourceScope.getByTestId('task-execution-scope-web')).toHaveText('Disabled');
      await expect(sourceScope.getByTestId('task-execution-scope-files')).toHaveText('Disabled');
      await expect(sourceScope.getByTestId('task-execution-runtime-contract'))
        .toContainText('Execution provider: First-party Project Files V1');
      await expect(sourceScope.getByRole('button', { name: 'Start', exact: true })).toHaveCount(0);

      await recordFetchJson(page, evidence, 'u22-journey-task-authoritative-detail', `/api/tasks/${createdTaskId}`, {
        validate: (body) => {
          const task = (body as Record<string, any>)?.task;
          return task?.id === createdTaskId &&
            task?.workspaceId === createdWorkspaceId &&
            task?.projectId === createdProjectId &&
            task?.title === taskTitle &&
            task?.description === taskDescription &&
            task?.priority === 'High' &&
            task?.workflowStageName === 'Backlog' &&
            task?.stageCategory === 0 &&
            task?.progressPercent === 0 &&
            task?.brief?.goal?.value === goal &&
            task?.brief?.deliverable?.value === deliverable &&
            task?.brief?.constraints?.value === constraints;
        }
      });

      const activityPath = `/api/tasks/${createdTaskId}/activity`;
      const activityResponsePromise = waitForApiResponse(page, 'GET', activityPath);
      const activity = page.getByTestId('task-activity-log');
      await activity.locator('summary').click();
      await recordOkJson(await activityResponsePromise, evidence, 'u22-journey-task-activity-confirmed-empty', (body) =>
        Array.isArray(body?.items) &&
        body.items.length === 0 &&
        body.page === 1 &&
        body.pageSize === 20 &&
        body.totalCount === 0 &&
        body.hasMore === false
      );
      await expect(activity.locator('summary')).toContainText('0 recorded');
      await expect(activity).toContainText('No Task activity has been recorded.');

      await page.reload();
      await expect(page.getByTestId('task-detail-page')).toBeVisible();
      await expect(page.getByRole('heading', { name: taskTitle })).toBeVisible();
      await expect(page.getByTestId('task-current-phase')).toHaveText('Backlog');
      expect(executionRunRequests, 'U-22 policy-only Task detail never requests an execution run').toEqual([]);
      await expect(page.getByTestId('task-progress-phase').getByText('Waiting', { exact: true })).toBeVisible();

      await page.goto(`/app/projects/${createdProjectId}/tasks/${createdTaskId}`);
      await expect(page.getByTestId('task-detail-page')).toBeVisible();
      await expect(page.getByRole('heading', { name: taskTitle })).toBeVisible();
      await expect(page.getByTestId('task-current-phase')).toHaveText('Backlog');
    } finally {
      if (createdProjectId) {
        // Leave the Task route before archiving its Project so the browser does
        // not legitimately refetch a resource that this test is removing.
        await page.goto('/app/workspaces').catch(() => undefined);
        const projectCleanup = await requestWithCsrf(page, 'POST', `/api/projects/${createdProjectId}/archive`);
        evidence.steps.push({
          name: 'u22-journey-project-cleanup-archive',
          method: 'POST',
          path: `/api/projects/${createdProjectId}/archive`,
          status: projectCleanup.status,
          bodyPreview: preview(projectCleanup.text)
        });
        expect(projectCleanup.status, `U-22 Project cleanup archive: ${projectCleanup.text}`).toBe(200);
        expect(projectCleanup.csrfHeaderPresent, 'U-22 Project cleanup uses a real CSRF token').toBe(true);
      }
      if (createdWorkspaceId) {
        const workspaceCleanup = await requestWithCsrf(page, 'POST', `/api/workspaces/${createdWorkspaceId}/archive`);
        evidence.steps.push({
          name: 'u22-journey-workspace-cleanup-archive',
          method: 'POST',
          path: `/api/workspaces/${createdWorkspaceId}/archive`,
          status: workspaceCleanup.status,
          bodyPreview: preview(workspaceCleanup.text)
        });
        expect(workspaceCleanup.status, `U-22 Workspace cleanup archive: ${workspaceCleanup.text}`).toBe(200);
        expect(workspaceCleanup.csrfHeaderPresent, 'U-22 Workspace cleanup uses a real CSRF token').toBe(true);
        await expect.poll(async () => {
          const list = await fetchJsonFromPage(page, '/api/workspaces');
          return list.status === 200 &&
            Array.isArray(list.body) &&
            !list.body.some((workspace: Record<string, unknown>) => workspace.id === createdWorkspaceId);
        }).toBe(true);
      }

      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence);
      expectUnexpectedApiFailures(evidence);
      await testInfo.attach('u22-same-lineage-real-backend-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('keeps authenticated HTTP requests available when the Hub cannot connect', async ({ page }, testInfo) => {
    await page.addInitScript(() => {
      const nativeFetch = window.fetch.bind(window);
      window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
        if (new URL(url, window.location.href).pathname.startsWith('/hubs/app')) {
          return Promise.reject(new TypeError('Synthetic Hub unavailability'));
        }

        return nativeFetch(input, init);
      };
    });
    const evidence: SmokeEvidence = { baseURL: String(testInfo.project.use.baseURL ?? ''), email: smokeEmail, steps: [], pageErrors: [], consoleErrors: [], failedApiResponses: [] };

    await loginAndVerifySession(page, evidence);
    await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates are delayed');
    await recordFetchJson(page, evidence, 'degraded-http-auth-status', '/api/auth/status', {
      validate: (body) => body && typeof body === 'object' && (body as Record<string, unknown>).isAuthenticated === true
    });

    const myTasksResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
    await page.getByTestId('nav-my-tasks').first().click();
    await recordOkJson(await myTasksResponse, evidence, 'degraded-my-tasks-http-list', (body) => isPagedResponse(body));
    const refreshResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
    await page.getByTestId('my-tasks-refresh').click();
    await recordOkJson(await refreshResponse, evidence, 'degraded-my-tasks-manual-refresh', (body) => isPagedResponse(body));
  });

  test('TASK-V1-PR04 exercises canonical My Tasks scopes, relationships, filters, paging, and UI against PostgreSQL', async ({ page }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''), email: smokeEmail, steps: [], pageErrors: [], consoleErrors: [], failedApiResponses: []
    };
    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => { if (message.type() === 'error') {evidence.consoleErrors.push(message.text());} });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(page, evidence);
      const workspaces = await recordFetchJson(page, evidence, 'pr04-workspaces', '/api/workspaces', {
        validate: (body) => Array.isArray(body) &&
          body.some((item: unknown) => hasStringValue(item, 'name', smokeWorkspaceName)) &&
          body.some((item: unknown) => hasStringValue(item, 'name', 'Browser Smoke Workspace Two'))
      }) as Record<string, unknown>[];
      const primaryWorkspace = workspaces.find((item) => item.name === smokeWorkspaceName)!;
      const secondWorkspace = workspaces.find((item) => item.name === 'Browser Smoke Workspace Two')!;
      const primaryWorkspaceId = String(primaryWorkspace.id);
      const secondWorkspaceId = String(secondWorkspace.id);
      evidence.workspaceId = primaryWorkspaceId;

      const omitted = await fetchJsonFromPage(page, '/api/me/tasks?view=assigned');
      evidence.steps.push({ name: 'pr04-multiple-workspace-requires-explicit-id', method: 'GET', path: '/api/me/tasks', status: omitted.status });
      expect(omitted.status).toBe(400);
      expect(omitted.body?.error?.code).toBe('MY_TASKS_INVALID_WORKSPACE_SCOPE');

      const currentPath = `/api/me/tasks?view=assigned&workspaceId=${primaryWorkspaceId}&page=1&pageSize=100`;
      const current = await recordFetchJson(page, evidence, 'pr04-current-workspace', currentPath, {
        validate: (body) => isPagedResponse(body) &&
          body.workspaceId === primaryWorkspaceId &&
          body.items.some((item: unknown) => hasStringValue(item, 'title', smokeTaskTitle)) &&
          !body.items.some((item: unknown) => hasStringValue(item, 'title', 'PR04 second workspace assigned'))
      }) as Record<string, any>;
      expect(new Set(current.items.map((item: Record<string, unknown>) => item.taskId)).size).toBe(current.items.length);

      const allPath = '/api/me/tasks?view=assigned&scope=allWorkspaces&page=1&pageSize=100';
      const all = await recordFetchJson(page, evidence, 'pr04-all-workspaces', allPath, {
        validate: (body) => isPagedResponse(body) &&
          body.availableWorkspaceCount === 2 &&
          body.items.some((item: unknown) => hasStringValue(item, 'title', smokeTaskTitle)) &&
          body.items.some((item: unknown) => hasStringValue(item, 'title', 'PR04 second workspace assigned'))
      }) as Record<string, any>;
      expect(all.items.every((item: Record<string, unknown>) => typeof item.workspaceTitle === 'string' && item.workspaceTitle.length > 0)).toBe(true);

      const expectedByView: Record<string, string> = {
        assigned: smokeTaskTitle,
        participating: 'PR04 participating task',
        reviews: 'PR04 review task',
        created: 'PR04 created task',
        watching: 'PR04 watching task',
        teamQueue: 'PR04 team queue task',
        completed: 'PR04 completed task'
      };
      for (const [view, title] of Object.entries(expectedByView)) {
        const body = await recordFetchJson(
          page,
          evidence,
          `pr04-view-${view}`,
          `/api/me/tasks?view=${view}&workspaceId=${primaryWorkspaceId}&page=1&pageSize=100`,
          {
            validate: (value) => isPagedResponse(value) &&
              value.items.some((item: unknown) => hasStringValue(item, 'title', title))
          }
        ) as Record<string, any>;
        expect(new Set(body.items.map((item: Record<string, unknown>) => item.taskId)).size, `${view} contains no duplicate Task`).toBe(body.items.length);
      }

      const filteredPath = `/api/me/tasks?view=assigned&workspaceId=${primaryWorkspaceId}&projectId=${current.items[0].projectId}&stageCategory=todo&priority=critical&blocked=true&search=${encodeURIComponent('PR04 critical blocked match')}&timeGroup=today&page=1&pageSize=10`;
      const filtered = await recordFetchJson(page, evidence, 'pr04-filter-combination', filteredPath, {
        validate: (body) => isPagedResponse(body) &&
          body.totalCount === 1 &&
          body.items.length === 1 &&
          hasStringValue(body.items[0], 'title', 'PR04 critical blocked match')
      }) as Record<string, any>;
      expect(filtered.items[0].timeGroup).toBe('Today');

      const firstPage = await recordFetchJson(
        page,
        evidence,
        'pr04-page-1',
        `/api/me/tasks?view=assigned&workspaceId=${primaryWorkspaceId}&page=1&pageSize=10`,
        { validate: (body) => isPagedResponse(body) && body.page === 1 && body.pageSize === 10 && body.totalCount > 10 }
      ) as Record<string, any>;
      const secondPage = await recordFetchJson(
        page,
        evidence,
        'pr04-page-2',
        `/api/me/tasks?view=assigned&workspaceId=${primaryWorkspaceId}&page=2&pageSize=10`,
        { validate: (body) => isPagedResponse(body) && body.page === 2 && body.pageSize === 10 }
      ) as Record<string, any>;
      const firstPageIds = new Set(firstPage.items.map((item: Record<string, unknown>) => item.taskId));
      expect(secondPage.items.some((item: Record<string, unknown>) => firstPageIds.has(item.taskId))).toBe(false);

      const counts = await recordFetchJson(
        page,
        evidence,
        'pr04-row-count-consistency',
        `/api/me/tasks/counts?view=assigned&workspaceId=${primaryWorkspaceId}`,
        {
          validate: (body) => body && typeof body === 'object' &&
            Array.isArray((body as Record<string, unknown>).views) &&
            Array.isArray((body as Record<string, unknown>).timeGroups)
        }
      ) as Record<string, any>;
      expect(counts.views.find((item: Record<string, unknown>) => item.view === 'Assigned')?.count).toBe(current.totalCount);

      // With multiple authorized Workspaces, WS-02 deliberately requires an
      // explicit choice. Select through the canonical header before a full
      // navigation so the persisted preference, rather than array position,
      // restores the same scope on /tasks.
      await page.goto('/app/workspaces');
      const workspaceSwitcher = page.getByTestId('workspace-switcher');
      await expect(workspaceSwitcher).toHaveValue('');
      await workspaceSwitcher.selectOption(primaryWorkspaceId);
      await expect(workspaceSwitcher).toHaveValue(primaryWorkspaceId);

      const initialUiRequest = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.goto('/app/tasks');
      const initialUiResponse = await initialUiRequest;
      expect(new URL(initialUiResponse.url()).searchParams.get('workspaceId')).toBe(primaryWorkspaceId);
      await expect(page.getByTestId('my-tasks-workspace-select')).toHaveValue(primaryWorkspaceId);
      await expect(page.getByRole('tab')).toHaveCount(7);
      await expect(page.getByRole('tab', { name: /^Assigned to Me/ })).toHaveAttribute('aria-selected', 'true');

      const participatingResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByRole('tab', { name: /^Assigned to Me/ }).press('ArrowRight');
      await recordOkJson(await participatingResponse, evidence, 'pr04-keyboard-tab-participating', (body) =>
        isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', 'PR04 participating task')));
      await expect(page.getByRole('tab', { name: /^Participating/ })).toHaveAttribute('aria-selected', 'true');

      const assignedResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByRole('tab', { name: /^Assigned to Me/ }).click();
      await recordOkJson(await assignedResponse, evidence, 'pr04-ui-assigned', (body) => isPagedResponse(body));

      const secondWorkspaceResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-workspace-select').selectOption(secondWorkspaceId);
      const secondWorkspaceList = await recordOkJson(await secondWorkspaceResponse, evidence, 'pr04-ui-workspace-switch', (body) =>
        isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', 'PR04 second workspace assigned')));
      expect(secondWorkspaceList.page).toBe(1);
      await expect(page.getByText('PR04 second workspace assigned', { exact: true })).toBeVisible();

      const primaryWorkspaceResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-workspace-select').selectOption(primaryWorkspaceId);
      await recordOkJson(await primaryWorkspaceResponse, evidence, 'pr04-ui-workspace-switch-back', (body) =>
        isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', smokeTaskTitle)));

      const searchResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-search').fill('PR04 critical blocked match');
      await recordOkJson(await searchResponse, evidence, 'pr04-ui-search-debounce', (body) =>
        isPagedResponse(body) && body.totalCount === 1);
      await expect(page.getByText('PR04 critical blocked match', { exact: true })).toBeVisible();

      const priorityResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-priority-filter').selectOption('critical');
      await recordOkJson(await priorityResponse, evidence, 'pr04-ui-priority-filter', (body) =>
        isPagedResponse(body) && body.totalCount === 1);
      const blockedResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-blocked-filter').selectOption('true');
      await recordOkJson(await blockedResponse, evidence, 'pr04-ui-blocked-filter', (body) =>
        isPagedResponse(body) && body.totalCount === 1);
      const stageResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-stage-filter').selectOption('todo');
      await recordOkJson(await stageResponse, evidence, 'pr04-ui-stage-filter', (body) =>
        isPagedResponse(body) && body.totalCount === 1);
      const urgencyResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-urgency-filter').selectOption('today');
      await recordOkJson(await urgencyResponse, evidence, 'pr04-ui-urgency-filter', (body) =>
        isPagedResponse(body) && body.totalCount === 1);
      const projectResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-project-filter').fill(String(current.items[0].projectId));
      await page.getByTestId('my-tasks-project-filter').press('Tab');
      await recordOkJson(await projectResponse, evidence, 'pr04-ui-project-filter', (body) =>
        isPagedResponse(body) && body.totalCount === 1);

      await page.reload();
      await expect(page.getByTestId('my-tasks-page-size')).toBeVisible();
      const pageSizeResponse = page.waitForResponse((response) => {
        if (response.request().method() !== 'GET') {return false;}
        const url = new URL(response.url());
        return url.pathname === '/api/me/tasks' && url.searchParams.get('pageSize') === '10';
      });
      await page.getByTestId('my-tasks-page-size').selectOption('10');
      await recordOkJson(await pageSizeResponse, evidence, 'pr04-ui-page-size', (body) =>
        isPagedResponse(body) && body.page === 1 && body.pageSize === 10 && body.totalCount > 10);
      const nextResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-next-page').click();
      await recordOkJson(await nextResponse, evidence, 'pr04-ui-next-page', (body) => isPagedResponse(body) && body.page === 2);
      const previousResponse = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-previous-page').click();
      await recordOkJson(await previousResponse, evidence, 'pr04-ui-previous-page', (body) => isPagedResponse(body) && body.page === 1);

      const secondBeforeRevocation = waitForApiResponse(page, 'GET', '/api/me/tasks');
      await page.getByTestId('my-tasks-workspace-select').selectOption(secondWorkspaceId);
      await recordOkJson(await secondBeforeRevocation, evidence, 'pr04-revocation-visible-before', (body) =>
        isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', 'PR04 second workspace assigned')));
      await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
      const authorizationRefresh = page.waitForResponse((response) => {
        const url = new URL(response.url());
        return response.request().method() === 'GET' &&
          url.pathname === '/api/me/tasks' &&
          url.searchParams.get('workspaceId') === primaryWorkspaceId;
      });
      const revokeSecond = await requestWithCsrf(page, 'DELETE', `/api/workspaces/${secondWorkspaceId}/members/${evidence.userId}`);
      evidence.steps.push({
        name: 'pr04-second-workspace-membership-revoked',
        method: 'DELETE',
        path: `/api/workspaces/${secondWorkspaceId}/members/${evidence.userId}`,
        status: revokeSecond.status
      });
      expect(revokeSecond.status).toBe(200);

      // AuthorizationStateChanged can replace the selected Workspace as soon as
      // this request completes. Chromium may discard the response body during
      // that SPA transition, so treat this response as transport evidence only;
      // the authoritative post-revocation DTO is read and validated immediately
      // below with a direct same-origin fetch.
      const authorizationRefreshResponse = await authorizationRefresh;
      evidence.steps.push({
        name: 'pr04-authorization-state-refresh',
        method: authorizationRefreshResponse.request().method(),
        path: new URL(authorizationRefreshResponse.url()).pathname,
        status: authorizationRefreshResponse.status(),
        bodyPreview: '[not read: authorization refresh may replace the selected Workspace]'
      });
      expect(authorizationRefreshResponse.status(), 'authorization refresh status').toBe(200);
      expect(authorizationRefreshResponse.ok(), 'authorization refresh succeeds').toBe(true);
      await expect(page.getByText('PR04 second workspace assigned', { exact: true })).toHaveCount(0);
      await expect(page.getByTestId('my-tasks-workspace-select').locator(`option[value="${secondWorkspaceId}"]`)).toHaveCount(0);

      const afterRevocation = await recordFetchJson(
        page,
        evidence,
        'pr04-revocation-all-workspaces-row-clear',
        '/api/me/tasks?view=assigned&scope=allWorkspaces&page=1&pageSize=100',
        {
          validate: (body) => isPagedResponse(body) &&
            body.availableWorkspaceCount === 1 &&
            !body.items.some((item: unknown) => hasStringValue(item, 'title', 'PR04 second workspace assigned'))
        }
      ) as Record<string, any>;
      const countsAfterRevocation = await recordFetchJson(
        page,
        evidence,
        'pr04-revocation-count-clear',
        '/api/me/tasks/counts?view=assigned&scope=allWorkspaces',
        {
          validate: (body) => body && typeof body === 'object' &&
            body.availableWorkspaceCount === 1 &&
            Array.isArray(body.views)
        }
      ) as Record<string, any>;
      expect(countsAfterRevocation.views.find((item: Record<string, unknown>) => item.view === 'Assigned')?.count)
        .toBe(afterRevocation.totalCount);

      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence);
      expectUnexpectedApiFailures(evidence);
    } finally {
      await testInfo.attach('task-v1-pr04-real-backend-evidence.json', { body: JSON.stringify(evidence, null, 2), contentType: 'application/json' });
    }
  });

  test('TASK-V1-PR05 exercises the canonical Project Kanban from the real browser through PostgreSQL', async ({ page, browser }, testInfo) => {
    const evidence: Pr05KanbanEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: pr05ManagerEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: [],
      seed: {
        projectSlug: pr05ProjectSlug,
        projectTitle: pr05ProjectTitle,
        taskTitles: Object.values(pr05TaskTitles)
      },
      apiInterception: 'none',
      featureFallback: 'Covered by the retained mocked Angular browser scenario; the real backend authorization path is not feature-flagged.',
      commands: []
    };
    let ownerContext: Awaited<ReturnType<typeof browser.newContext>> | null = null;
    let kanbanPostCount = 0;

    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => { if (message.type() === 'error') {evidence.consoleErrors.push(message.text());} });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));
    page.on('request', (request) => {
      if (request.method() === 'POST' && /^\/api\/tasks\/[0-9a-f-]+\/kanban-move$/i.test(new URL(request.url()).pathname)) {
        kanbanPostCount += 1;
      }
    });

    try {
      await loginAndVerifySession(page, evidence, { email: pr05ManagerEmail, password: smokePassword });
      await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.', { timeout: 30_000 });

      const featureEnabled = await page.evaluate(() =>
        (window as Window & { __AIP_FEATURE_FLAGS__?: Record<string, boolean> })
          .__AIP_FEATURE_FLAGS__?.['tasks.kanbanV1'] === true);
      evidence.featureFlagEnabled = featureEnabled;
      expect(featureEnabled, 'the hosted runtime config enables the PR05 Kanban presentation').toBe(true);

      const tenant = await recordFetchJson(page, evidence, 'pr05-current-tenant', '/api/tenants/current', {
        validate: (body) => hasString(body, 'tenantId') && body.isAvailable === true && body.isPlatformScope === false
      }) as Record<string, any>;
      evidence.tenantId = String(tenant.tenantId);

      const workspaces = await recordFetchJson(page, evidence, 'pr05-workspaces', '/api/workspaces', {
        validate: (body) => Array.isArray(body) && body.some((item: unknown) => hasStringValue(item, 'name', smokeWorkspaceName))
      }) as Record<string, any>[];
      const workspace = workspaces.find((item) => item.name === smokeWorkspaceName)!;
      evidence.workspaceId = String(workspace.id);
      expect(evidence.workspaceId, 'synthetic Workspace id').toMatch(/^[0-9a-f-]{36}$/i);

      const projects = await recordFetchJson(page, evidence, 'pr05-projects', '/api/projects?page=1&pageSize=100', {
        validate: (body) => isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', pr05ProjectTitle))
      }) as Record<string, any>;
      const project = projects.items.find((item: Record<string, unknown>) => item.title === pr05ProjectTitle)!;
      evidence.projectId = String(project.id);
      expect(project.workspaceId, 'Project belongs to the expected synthetic Workspace').toBe(evidence.workspaceId);
      expect(evidence.projectId, 'synthetic Project id').toMatch(/^[0-9a-f-]{36}$/i);

      const projectMembers = await recordFetchJson(
        page,
        evidence,
        'pr05-project-members',
        `/api/projects/${evidence.projectId}/members`,
        {
          validate: (body) => Array.isArray(body) &&
            body.some((member: unknown) => hasStringValue(member, 'email', pr05ManagerEmail)) &&
            body.some((member: unknown) => hasStringValue(member, 'displayName', smokeRecipientName))
        }
      ) as Record<string, any>[];
      const managerMember = projectMembers.find((member) => member.email === pr05ManagerEmail)!;
      expect(String(managerMember.userId), 'logged-in Manager has the seeded Project membership').toBe(evidence.userId);

      const initialSnapshotResponse = waitForApiResponse(page, 'GET', `/api/projects/${evidence.projectId}/kanban`);
      await page.goto(`/app/projects/${evidence.projectId}`);
      const initialResponse = await initialSnapshotResponse;
      let snapshot = await recordOkJson(
        initialResponse,
        evidence,
        'pr05-initial-kanban-snapshot',
        isPr05KanbanSnapshot
      ) as Pr05KanbanSnapshotDto;

      const todo = pr05Stage(snapshot, 'Todo');
      const done = pr05Stage(snapshot, 'Done');
      const cancelled = pr05Stage(snapshot, 'Cancelled');
      const initialCards = Object.fromEntries(
        Object.entries(pr05TaskTitles).map(([key, title]) => [key, pr05Card(snapshot, title)])
      ) as Record<keyof typeof pr05TaskTitles, Pr05KanbanCardDto>;

      evidence.initialSnapshot = {
        responseUrl: initialResponse.url(),
        status: initialResponse.status(),
        tenantId: evidence.tenantId!,
        workspaceId: evidence.workspaceId!,
        projectId: String(snapshot.board.projectId),
        boardVersion: snapshot.board.version,
        taskIds: Object.fromEntries(Object.entries(initialCards).map(([key, card]) => [key, card.taskId])),
        taskVersions: Object.fromEntries(Object.entries(initialCards).map(([key, card]) => [key, card.version])),
        stages: {
          todo: todo.workflowStageId,
          done: done.workflowStageId,
          cancelled: cancelled.workflowStageId
        },
        totalAuthorizedCardCount: snapshot.board.totalAuthorizedCardCount,
        isTruncated: snapshot.board.isTruncated,
        canConfigure: snapshot.board.uiPermissions.canConfigure
      };
      expect(snapshot.board.projectId).toBe(evidence.projectId);
      expect(snapshot.board.totalAuthorizedCardCount).toBe(5);
      expect(snapshot.board.isTruncated).toBe(false);
      expect(snapshot.board.uiPermissions.canConfigure).toBe(true);
      expect(snapshot.cards.every((card) =>
        card.version > 0 &&
        card.uiPermissions.canOpen &&
        card.uiPermissions.canMove &&
        card.uiPermissions.allowedTargetWorkflowStageIds.includes(done.workflowStageId) &&
        card.uiPermissions.allowedTargetWorkflowStageIds.includes(cancelled.workflowStageId)
      ), 'card versions and backend-computed open/move/transition permissions').toBe(true);

      await expect(page).toHaveURL(new RegExp(`/app/projects/${evidence.projectId}$`));
      await expect(page.getByTestId('project-detail-page')).toBeVisible();
      await expect(page.getByRole('tab', { name: 'Tasks', exact: true })).toHaveAttribute('aria-selected', 'true');
      await expect(page.getByTestId('coglatas-kanban-board')).toBeVisible();
      await expect(page.getByRole('heading', { name: pr05ProjectTitle })).toBeVisible();
      await expect(page.getByText('Warning: WIP limit 4 exceeded.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Configure board' })).toBeVisible();
      for (const card of Object.values(initialCards)) {
        const locator = pr05CardLocator(page, card.taskId);
        await expect(locator).toBeVisible();
        await expect(locator).toHaveAttribute('aria-label', new RegExp(`^${escapeRegExp(card.summary)}, current stage Todo$`));
        await expect(locator.getByRole('button', { name: 'Move', exact: true })).toBeVisible();
        await expect(locator.getByRole('button', { name: 'Open details', exact: true })).toBeVisible();
      }

      // Stable same-Stage reorder: the browser sends an explicit canonical
      // "before" neighbor intent and the persisted boardOrder survives reload.
      let reorderCard = pr05Card(snapshot, pr05TaskTitles.reorder);
      const moveCardBeforeReorder = pr05Card(snapshot, pr05TaskTitles.move);
      const reorderLocator = pr05CardLocator(page, reorderCard.taskId);
      await reorderLocator.getByRole('button', { name: 'Move', exact: true }).click();
      await reorderLocator.getByLabel('Target stage').selectOption(todo.workflowStageId);
      await reorderLocator.getByLabel('Position').selectOption(`before:${moveCardBeforeReorder.taskId}`);
      const reorderResponsePromise = waitForApiResponse(page, 'POST', `/api/tasks/${reorderCard.taskId}/kanban-move`);
      await reorderLocator.getByRole('button', { name: 'Apply move' }).click();
      const reorderCommand = await recordPr05KanbanCommand(await reorderResponsePromise, evidence, 'stable-reorder', 200);
      expect(reorderCommand.request).toMatchObject({
        targetWorkflowStageId: todo.workflowStageId,
        targetBeforeTaskId: moveCardBeforeReorder.taskId,
        targetAfterTaskId: null,
        expectedTaskVersion: reorderCard.version,
        expectedBoardVersion: snapshot.board.version,
        reason: null
      });
      snapshot = reorderCommand.body.snapshot;
      reorderCard = pr05Card(snapshot, pr05TaskTitles.reorder);
      await expect(
        page.getByRole('region', { name: 'Canonical Project Task Kanban' })
          .getByRole('status')
          .filter({ hasText: 'Move saved.' })
      ).toBeVisible();
      await expectPr05StageOrder(page, snapshot, 'Todo');
      expect(pr05StageTaskIds(snapshot, todo.workflowStageId).indexOf(reorderCard.taskId))
        .toBeLessThan(pr05StageTaskIds(snapshot, todo.workflowStageId).indexOf(moveCardBeforeReorder.taskId));

      const reorderReloadResponse = waitForApiResponse(page, 'GET', `/api/projects/${evidence.projectId}/kanban`);
      await page.reload();
      snapshot = await recordOkJson(
        await reorderReloadResponse,
        evidence,
        'pr05-reorder-after-reload',
        isPr05KanbanSnapshot
      ) as Pr05KanbanSnapshotDto;
      await expectPr05StageOrder(page, snapshot, 'Todo');
      evidence.reorderPersistence = {
        taskId: reorderCard.taskId,
        beforeTaskId: moveCardBeforeReorder.taskId,
        boardVersion: snapshot.board.version,
        boardOrder: pr05Card(snapshot, pr05TaskTitles.reorder).boardOrder,
        persistedOrder: pr05StageTaskIds(snapshot, todo.workflowStageId)
      };
      expect(evidence.reorderPersistence.persistedOrder.indexOf(reorderCard.taskId))
        .toBeLessThan(evidence.reorderPersistence.persistedOrder.indexOf(moveCardBeforeReorder.taskId));

      // Canonical cross-Stage move uses the empty neighbor pair as the bounded
      // snapshot-independent "end of Stage" intent.
      let moveCard = pr05Card(snapshot, pr05TaskTitles.move);
      let moveLocator = pr05CardLocator(page, moveCard.taskId);
      await moveLocator.getByRole('button', { name: 'Move', exact: true }).click();
      await moveLocator.getByLabel('Target stage').selectOption(done.workflowStageId);
      await expect(moveLocator.getByLabel('Position')).toHaveValue('end');
      const moveResponsePromise = waitForApiResponse(page, 'POST', `/api/tasks/${moveCard.taskId}/kanban-move`);
      await moveLocator.getByRole('button', { name: 'Apply move' }).click();
      const moveCommand = await recordPr05KanbanCommand(await moveResponsePromise, evidence, 'stage-move-to-done', 200);
      expect(moveCommand.request).toMatchObject({
        targetWorkflowStageId: done.workflowStageId,
        targetBeforeTaskId: null,
        targetAfterTaskId: null,
        expectedTaskVersion: moveCard.version,
        expectedBoardVersion: snapshot.board.version,
        reason: null
      });
      snapshot = moveCommand.body.snapshot;
      moveCard = pr05Card(snapshot, pr05TaskTitles.move);
      await expect(pr05ColumnLocator(page, 'Done').locator(`[data-kanban-card-id="${moveCard.taskId}"]`)).toBeVisible();
      await expect(page.getByText('Move saved.', { exact: true })).toBeVisible();
      await expectPr05StageOrder(page, snapshot, 'Done');

      const moveReloadResponse = waitForApiResponse(page, 'GET', `/api/projects/${evidence.projectId}/kanban`);
      await page.reload();
      snapshot = await recordOkJson(
        await moveReloadResponse,
        evidence,
        'pr05-stage-move-after-reload',
        isPr05KanbanSnapshot
      ) as Pr05KanbanSnapshotDto;
      moveCard = pr05Card(snapshot, pr05TaskTitles.move);
      expect(moveCard.workflowStageId).toBe(done.workflowStageId);
      await expect(pr05ColumnLocator(page, 'Done').locator(`[data-kanban-card-id="${moveCard.taskId}"]`)).toBeVisible();
      evidence.movePersistence = {
        taskId: moveCard.taskId,
        workflowStageId: moveCard.workflowStageId,
        boardVersion: snapshot.board.version,
        taskVersion: moveCard.version,
        boardOrder: moveCard.boardOrder
      };

      // Cancelled requires a reason. Escape and the explicit Cancel button
      // close the interaction, restore focus, and must not dispatch POST.
      let cancellationCard = pr05Card(snapshot, pr05TaskTitles.cancellation);
      let cancellationLocator = pr05CardLocator(page, cancellationCard.taskId);
      const postsBeforeCancellation = kanbanPostCount;
      await cancellationLocator.getByRole('button', { name: 'Move', exact: true }).click();
      await cancellationLocator.getByLabel('Target stage').selectOption(cancelled.workflowStageId);
      await expect(cancellationLocator.getByLabel('Reason')).toBeVisible();
      await expect(cancellationLocator.getByRole('button', { name: 'Apply move' })).toBeDisabled();
      expect(kanbanPostCount).toBe(postsBeforeCancellation);
      await cancellationLocator.getByLabel('Reason').press('Escape');
      await expect(cancellationLocator.getByLabel('Reason')).toHaveCount(0);
      await expect(cancellationLocator).toBeFocused();
      expect(kanbanPostCount).toBe(postsBeforeCancellation);

      await cancellationLocator.getByRole('button', { name: 'Move', exact: true }).click();
      await cancellationLocator.getByLabel('Target stage').selectOption(cancelled.workflowStageId);
      await cancellationLocator.getByRole('button', { name: 'Cancel', exact: true }).click();
      await expect(cancellationLocator.getByLabel('Reason')).toHaveCount(0);
      await expect(cancellationLocator).toBeFocused();
      expect(kanbanPostCount).toBe(postsBeforeCancellation);

      const cancellationReason = 'Cancelled by the synthetic PR05 real-browser acceptance.';
      await cancellationLocator.getByRole('button', { name: 'Move', exact: true }).click();
      await cancellationLocator.getByLabel('Target stage').selectOption(cancelled.workflowStageId);
      await cancellationLocator.getByLabel('Reason').fill(cancellationReason);
      const cancellationResponsePromise = waitForApiResponse(page, 'POST', `/api/tasks/${cancellationCard.taskId}/kanban-move`);
      await cancellationLocator.getByRole('button', { name: 'Apply move' }).click();
      const cancellationCommand = await recordPr05KanbanCommand(await cancellationResponsePromise, evidence, 'reason-required-cancelled', 200);
      expect(cancellationCommand.request).toMatchObject({
        targetWorkflowStageId: cancelled.workflowStageId,
        targetBeforeTaskId: null,
        targetAfterTaskId: null,
        expectedTaskVersion: cancellationCard.version,
        expectedBoardVersion: snapshot.board.version,
        reason: cancellationReason
      });
      snapshot = cancellationCommand.body.snapshot;
      cancellationCard = pr05Card(snapshot, pr05TaskTitles.cancellation);
      expect(cancellationCard.workflowStageId).toBe(cancelled.workflowStageId);
      await expect(pr05ColumnLocator(page, 'Cancelled').locator(`[data-kanban-card-id="${cancellationCard.taskId}"]`)).toBeVisible();
      await expect(page.getByText('Move saved.', { exact: true })).toBeVisible();
      await expectPr05StageOrder(page, snapshot, 'Cancelled');

      const cancellationReloadResponse = waitForApiResponse(page, 'GET', `/api/projects/${evidence.projectId}/kanban`);
      await page.reload();
      snapshot = await recordOkJson(
        await cancellationReloadResponse,
        evidence,
        'pr05-cancelled-after-reload',
        isPr05KanbanSnapshot
      ) as Pr05KanbanSnapshotDto;
      cancellationCard = pr05Card(snapshot, pr05TaskTitles.cancellation);
      expect(cancellationCard.workflowStageId).toBe(cancelled.workflowStageId);
      await expect(pr05ColumnLocator(page, 'Cancelled').locator(`[data-kanban-card-id="${cancellationCard.taskId}"]`)).toBeVisible();
      await expectPr05StageOrder(page, snapshot, 'Cancelled');
      evidence.reasonRequired = {
        taskId: cancellationCard.taskId,
        escapeSentPost: false,
        cancelSentPost: false,
        focusRestored: true,
        submittedReason: cancellationReason,
        persistedWorkflowStageId: cancellationCard.workflowStageId
      };

      // Hold the UI move form open so a real, separate HTTP move is queued by
      // realtime rather than updating the local snapshot. The subsequent UI
      // POST therefore carries the genuinely stale board version and receives
      // the backend's real 409 response.
      const staleSnapshot = snapshot;
      const conflictCard = pr05Card(staleSnapshot, pr05TaskTitles.conflict);
      const neighborCard = pr05Card(staleSnapshot, pr05TaskTitles.neighbor);
      const conflictLocator = pr05CardLocator(page, conflictCard.taskId);
      await conflictLocator.getByRole('button', { name: 'Move', exact: true }).click();
      await conflictLocator.getByLabel('Target stage').selectOption(done.workflowStageId);

      const directNeighborIntent = {
        targetWorkflowStageId: todo.workflowStageId,
        targetBeforeTaskId: null,
        targetAfterTaskId: null,
        expectedTaskVersion: neighborCard.version,
        expectedBoardVersion: staleSnapshot.board.version,
        reason: null
      };
      const directNeighborMove = await requestWithCsrf(
        page,
        'POST',
        `/api/tasks/${neighborCard.taskId}/kanban-move`,
        directNeighborIntent
      );
      expect(directNeighborMove.status, directNeighborMove.text).toBe(200);
      expect(directNeighborMove.csrfHeaderPresent, 'direct concurrent mutation includes the real CSRF header').toBe(true);
      const concurrentSnapshot = (parseJson(directNeighborMove.text) as Pr05KanbanCommandResponseDto).snapshot;
      expect(isPr05KanbanSnapshot(concurrentSnapshot), 'direct mutation authoritative snapshot').toBe(true);
      expect(concurrentSnapshot.board.version).toBeGreaterThan(staleSnapshot.board.version);
      evidence.commands.push({
        name: 'concurrent-real-reorder',
        responseUrl: new URL(`/api/tasks/${neighborCard.taskId}/kanban-move`, page.url()).href,
        status: directNeighborMove.status,
        taskId: neighborCard.taskId,
        expectedTaskVersion: neighborCard.version,
        expectedBoardVersion: staleSnapshot.board.version,
        authoritativeBoardVersion: concurrentSnapshot.board.version,
        targetWorkflowStageId: todo.workflowStageId,
        targetBeforeTaskId: null,
        targetAfterTaskId: null,
        csrfHeaderPresent: directNeighborMove.csrfHeaderPresent
      });
      await expect(page.getByText('A live update is queued until the active board operation ends.', { exact: true }))
        .toBeVisible({ timeout: 30_000 });

      const stalePostResponsePromise = waitForApiResponse(page, 'POST', `/api/tasks/${conflictCard.taskId}/kanban-move`);
      const authoritativeRefetchPromise = page.waitForResponse((response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === `/api/projects/${evidence.projectId}/kanban` &&
        response.status() === 200
      );
      await page.evaluate((taskId) => {
        const state = { transitions: [] as string[], observer: null as MutationObserver | null };
        const recordStage = () => {
          const card = document.querySelector(`[data-kanban-card-id="${taskId}"]`);
          const stage = card?.closest('.coglatas-kanban__column')?.querySelector('h3')?.textContent?.trim();
          if (stage && state.transitions.at(-1) !== stage) {state.transitions.push(stage);}
        };
        recordStage();
        state.observer = new MutationObserver(recordStage);
        state.observer.observe(document.body, { childList: true, subtree: true, attributes: true });
        (window as Window & { __pr05ConflictStageObserver?: typeof state }).__pr05ConflictStageObserver = state;
      }, conflictCard.taskId);
      await conflictLocator.getByRole('button', { name: 'Apply move' }).click();
      const conflictCommand = await recordPr05KanbanCommand(await stalePostResponsePromise, evidence, 'stale-board-conflict', 409);
      expect(conflictCommand.request).toMatchObject({
        targetWorkflowStageId: done.workflowStageId,
        targetBeforeTaskId: null,
        targetAfterTaskId: null,
        expectedTaskVersion: conflictCard.version,
        expectedBoardVersion: staleSnapshot.board.version,
        reason: null
      });
      expect(conflictCommand.body.error?.code).toBe('KANBAN_STALE_BOARD');
      expect(JSON.stringify(conflictCommand.body), '409 denial does not disclose protected records').not.toMatch(
        /tenantId|workspaceId|PR05 stable neighbor card|browser-smoke-pr04-queue/i
      );
      snapshot = await recordOkJson(
        await authoritativeRefetchPromise,
        evidence,
        'pr05-conflict-authoritative-refetch',
        isPr05KanbanSnapshot
      ) as Pr05KanbanSnapshotDto;
      await expect(
        page.getByRole('region', { name: 'Canonical Project Task Kanban' })
          .getByRole('status')
          .filter({ hasText: 'Conflict resolved from the authoritative Project board.' })
      ).toBeVisible();
      await expect(pr05ColumnLocator(page, 'Todo').locator(`[data-kanban-card-id="${conflictCard.taskId}"]`)).toBeVisible();
      await expect(pr05CardLocator(page, conflictCard.taskId)).toBeFocused();
      await expectPr05StageOrder(page, snapshot, 'Todo');
      expect(pr05StageTaskIds(snapshot, todo.workflowStageId).at(-1)).toBe(neighborCard.taskId);
      const optimisticStageTransitions = await page.evaluate(() => {
        const state = (window as Window & {
          __pr05ConflictStageObserver?: { transitions: string[]; observer: MutationObserver | null };
        }).__pr05ConflictStageObserver;
        state?.observer?.disconnect();
        return state?.transitions ?? [];
      });
      const optimisticDoneIndex = optimisticStageTransitions.indexOf('Done');
      expect(optimisticDoneIndex, 'the stale command first renders the optimistic Done state').toBeGreaterThan(-1);
      expect(
        optimisticStageTransitions.slice(optimisticDoneIndex + 1),
        'the real 409 rolls the optimistic card back to Todo'
      ).toContain('Todo');
      evidence.staleConflict = {
        taskId: conflictCard.taskId,
        staleTaskVersion: conflictCard.version,
        staleBoardVersion: staleSnapshot.board.version,
        concurrentBoardVersion: concurrentSnapshot.board.version,
        status: conflictCommand.response.status(),
        code: conflictCommand.body.error?.code,
        refetchedBoardVersion: snapshot.board.version,
        rolledBackWorkflowStageId: pr05Card(snapshot, pr05TaskTitles.conflict).workflowStageId,
        optimisticStageTransitions,
        focusRestored: true
      };

      // Start observing before revocation. Racing this protected-DOM clear
      // against the real 404 proves the authorization event clears the
      // snapshot before HTTP revalidation completes.
      const protectedDataClear = page.evaluate((protectedTitles) =>
        new Promise<'protected-data-cleared'>((resolve) => {
          const isClear = () => {
            const text = document.body.innerText;
            return document.querySelectorAll('[data-kanban-card-id]').length === 0 &&
              protectedTitles.every((title) => !text.includes(title));
          };
          if (isClear()) {
            resolve('protected-data-cleared');
            return;
          }

          const observer = new MutationObserver(() => {
            if (!isClear()) {return;}
            observer.disconnect();
            resolve('protected-data-cleared');
          });
          observer.observe(document.body, { childList: true, subtree: true, characterData: true });
        }), Object.values(pr05TaskTitles));

      ownerContext = await browser.newContext({ baseURL: String(testInfo.project.use.baseURL ?? '') });
      const ownerPage = await ownerContext.newPage();
      const ownerEvidence: SmokeEvidence = {
        baseURL: evidence.baseURL,
        email: smokeEmail,
        steps: [],
        pageErrors: [],
        consoleErrors: [],
        failedApiResponses: []
      };
      await loginAndVerifySession(ownerPage, ownerEvidence);
      expect(ownerEvidence.userId, 'the revoking Owner is a separate synthetic actor').not.toBe(evidence.userId);

      // The Test-only one-shot response gate holds delivery only after the
      // real controller has produced an authorized 200. Authorization,
      // persistence, response content, and the browser request stay real.
      const responseGateId = randomUUID().replaceAll('-', '');
      const gatedKanbanPath = `/api/projects/${evidence.projectId}/kanban`;
      const gateArm = await requestWithCsrf(
        page,
        'POST',
        `${pr05ResponseGatePath}/${responseGateId}/arm`,
        { method: 'GET', path: gatedKanbanPath }
      );
      expect(gateArm.status, gateArm.text).toBe(200);
      expect(gateArm.csrfHeaderPresent, 'response gate arm uses the real CSRF middleware').toBe(true);
      await page.context().addCookies([{
        name: pr05ResponseGateCookieName,
        value: responseGateId,
        url: new URL('/', page.url()).href
      }]);

      const refreshRequest = page.waitForRequest((request) =>
        request.method() === 'GET' &&
        new URL(request.url()).pathname === gatedKanbanPath
      );
      const heldAuthorizedRefreshResponse = page.waitForResponse((response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === gatedKanbanPath &&
        response.status() === 200 &&
        response.headers()[pr05ResponseGateHeaderName] === responseGateId
      );
      await page.getByRole('button', { name: 'Refresh', exact: true }).click();
      await refreshRequest;
      await expect.poll(async () => {
        const gate = await fetchJsonFromPage(page, `${pr05ResponseGatePath}/${responseGateId}`);
        return gate.status === 200 ? gate.body : { state: `HTTP ${gate.status}`, statusCode: null };
      }, {
        message: 'the real authorized Kanban response reaches the one-shot response gate',
        timeout: 10_000
      }).toMatchObject({ state: 'waiting', statusCode: 200 });

      const deniedRefreshResponse = page.waitForResponse((response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === gatedKanbanPath &&
        response.status() === 404
      );
      const projectDetailPath = `/api/projects/${evidence.projectId}`;
      const deniedProjectDetailResponse = page.waitForResponse((response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === projectDetailPath
      );
      const firstRevocationObservationPromise = Promise.race([
        protectedDataClear,
        deniedRefreshResponse.then(() => 'denial-response' as const)
      ]);
      const revoke = await requestWithCsrf(
        ownerPage,
        'DELETE',
        `/api/projects/${evidence.projectId}/members/${evidence.userId}`
      );
      expect(revoke.status, revoke.text).toBe(200);
      expect(revoke.csrfHeaderPresent, 'Owner revocation includes a real CSRF header').toBe(true);

      const firstRevocationObservation = await firstRevocationObservationPromise;
      expect(firstRevocationObservation, 'protected board DOM is cleared before the denial response arrives')
        .toBe('protected-data-cleared');
      const deniedResponse = await deniedRefreshResponse;
      const deniedText = await deniedResponse.text();
      evidence.steps.push({
        name: 'pr05-authorization-state-safe-kanban-denial',
        method: 'GET',
        path: new URL(deniedResponse.url()).pathname,
        status: deniedResponse.status(),
        bodyPreview: preview(deniedText)
      });
      expectCanonicalKanbanDenial(deniedText, 'KANBAN_NOT_FOUND');
      const deniedProjectResponse = await deniedProjectDetailResponse;
      const deniedProjectText = await deniedProjectResponse.text();
      const expectedProjectDetailDenial: SmokeFailedApiResponse = {
        method: deniedProjectResponse.request().method(),
        path: new URL(deniedProjectResponse.url()).pathname,
        status: deniedProjectResponse.status()
      };
      evidence.steps.push({
        name: 'pr05-project-detail-safe-denial-after-revocation',
        method: expectedProjectDetailDenial.method,
        path: expectedProjectDetailDenial.path,
        status: expectedProjectDetailDenial.status,
        bodyPreview: preview(deniedProjectText)
      });
      const deniedProjectBody = expectSafeProjectDetailDenial(deniedProjectText, [
        evidence.projectId!, evidence.tenantId!, evidence.workspaceId!, pr05ProjectTitle,
        ...Object.values(pr05TaskTitles)
      ]);
      expect(deniedProjectResponse.status(), 'revoked Project detail uses the established safe denial contract')
        .toBe(400);
      expect(evidence.failedApiResponses).toContainEqual(expectedProjectDetailDenial);
      await expect(page.getByRole('status', {
        name: 'Project access was denied during reconnect. Protected Project data was cleared.',
        exact: true
      })).toBeVisible();
      await expect(page.locator('[data-kanban-card-id]')).toHaveCount(0);
      await expect(page.locator('.coglatas-kanban__column')).toHaveCount(0);
      await expect(page.getByText('Warning: WIP limit 4 exceeded.', { exact: true })).toHaveCount(0);
      for (const title of Object.values(pr05TaskTitles)) {
        await expect(page.getByText(title, { exact: true })).toHaveCount(0);
      }
      await expect(page.locator('body')).not.toContainText(/Parent:|Derived progress:|Derived dates:/);
      await expect(page.locator('body')).not.toContainText(evidence.tenantId!);
      await expect(page.locator('body')).not.toContainText(evidence.workspaceId!);
      await expect(page.locator('body')).not.toContainText('Security.AuthorizationStateChanged.v1');

      await page.evaluate((protectedTitles) => {
        const state = { restored: false, observer: null as MutationObserver | null };
        const observeProtectedData = () => {
          const text = document.body.innerText;
          if (
            document.querySelectorAll('[data-kanban-card-id]').length > 0 ||
            protectedTitles.some((title) => text.includes(title))
          ) {
            state.restored = true;
          }
        };
        state.observer = new MutationObserver(observeProtectedData);
        state.observer.observe(document.body, { childList: true, subtree: true, characterData: true });
        (window as Window & { __pr05RevocationObserver?: typeof state }).__pr05RevocationObserver = state;
        observeProtectedData();
      }, Object.values(pr05TaskTitles));

      const gateRelease = await requestWithCsrf(
        page,
        'POST',
        `${pr05ResponseGatePath}/${responseGateId}/release`
      );
      expect(gateRelease.status, gateRelease.text).toBe(200);
      expect(gateRelease.csrfHeaderPresent, 'response gate release uses the real CSRF middleware').toBe(true);
      const staleAuthorizedResponse = await heldAuthorizedRefreshResponse;
      expect(staleAuthorizedResponse.status()).toBe(200);
      expect(await staleAuthorizedResponse.finished(), 'the held real 200 finishes after protected data was cleared')
        .toBeNull();
      evidence.steps.push({
        name: 'pr05-stale-authorized-kanban-response-after-revocation',
        method: 'GET',
        path: new URL(staleAuthorizedResponse.url()).pathname,
        status: staleAuthorizedResponse.status()
      });
      await page.evaluate(() => new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      const protectedDataRestoredByStaleResponse = await page.evaluate(() => {
        const state = (window as Window & {
          __pr05RevocationObserver?: { restored: boolean; observer: MutationObserver | null };
        }).__pr05RevocationObserver;
        state?.observer?.disconnect();
        return state?.restored ?? true;
      });
      expect(
        protectedDataRestoredByStaleResponse,
        'the stale authorized 200 must not repopulate protected board DOM'
      ).toBe(false);
      await expect(page.locator('[data-kanban-card-id]')).toHaveCount(0);

      const subsequentDenial = await fetchFromPage(page, `/api/projects/${evidence.projectId}/kanban`);
      evidence.steps.push({
        name: 'pr05-subsequent-kanban-safe-denial',
        method: 'GET',
        path: `/api/projects/${evidence.projectId}/kanban`,
        status: subsequentDenial.status,
        bodyPreview: preview(subsequentDenial.text)
      });
      expect(subsequentDenial.status).toBe(404);
      expectCanonicalKanbanDenial(subsequentDenial.text, 'KANBAN_NOT_FOUND');
      await expect(page.locator('[data-kanban-card-id]')).toHaveCount(0);
      evidence.authorizationRevocation = {
        revokingActorUserId: ownerEvidence.userId!,
        revokedUserId: evidence.userId!,
        revokeStatus: revoke.status,
        csrfHeaderPresent: revoke.csrfHeaderPresent,
        overlappingRefreshStatus: staleAuthorizedResponse.status(),
        projectDetailDenialStatus: deniedProjectResponse.status(),
        projectDetailDenialCode: String(deniedProjectBody.code),
        staleAuthorizedResponseUrl: staleAuthorizedResponse.url(),
        responseGateStatusCode: 200,
        denialUrl: deniedResponse.url(),
        denialStatus: deniedResponse.status(),
        subsequentDenialStatus: subsequentDenial.status,
        protectedDataClearedBeforeRevalidation: firstRevocationObservation === 'protected-data-cleared',
        protectedDataRestoredByStaleResponse
      };

      expect(kanbanPostCount, 'real browser and direct concurrency Kanban POST count').toBe(5);
      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence, [expectedProjectDetailDenial]);
      expectUnexpectedApiFailures(evidence, [expectedProjectDetailDenial]);
    } finally {
      await ownerContext?.close();
      await testInfo.attach('task-v1-pr05-real-backend-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('TASK-V1-PR06 exercises canonical Gantt commands, rollback, degradation, and revocation through PostgreSQL', async ({ page, browser }, testInfo) => {
    const evidence: Pr06GanttEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: pr05ManagerEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: [],
      seed: {
        projectSlug: pr06ProjectSlug,
        projectTitle: pr06ProjectTitle,
        taskTitles: Object.values(pr06TaskTitles),
        milestoneTitle: pr06MilestoneTitle
      },
      apiInterception: 'none',
      hubTransportFaultInjection: 'A separate authenticated browser context rejects only /hubs/app transport; all /api HTTP remains real.',
      commands: []
    };
    let ownerContext: Awaited<ReturnType<typeof browser.newContext>> | null = null;
    let degradedContext: Awaited<ReturnType<typeof browser.newContext>> | null = null;
    let viewerContext: Awaited<ReturnType<typeof browser.newContext>> | null = null;

    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => { if (message.type() === 'error') {evidence.consoleErrors.push(message.text());} });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(page, evidence, { email: pr05ManagerEmail, password: smokePassword });
      await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.', { timeout: 30_000 });

      const featureEnabled = await page.evaluate(() =>
        (window as Window & { __AIP_FEATURE_FLAGS__?: Record<string, boolean> })
          .__AIP_FEATURE_FLAGS__?.['tasks.ganttV1'] === true);
      evidence.featureFlagEnabled = featureEnabled;
      expect(featureEnabled, 'the hosted runtime config enables the PR06 Schedule presentation').toBe(true);

      const projects = await recordFetchJson(page, evidence, 'pr06-projects', '/api/projects?page=1&pageSize=100', {
        validate: (body) => isPagedResponse(body) &&
          body.items.some((item: unknown) => hasStringValue(item, 'title', pr06ProjectTitle))
      }) as Record<string, any>;
      const project = projects.items.find((item: Record<string, unknown>) => item.title === pr06ProjectTitle)!;
      evidence.projectId = String(project.id);
      evidence.workspaceId = String(project.workspaceId);
      expect(evidence.projectId, 'synthetic PR06 Project id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(evidence.workspaceId, 'synthetic PR06 Workspace id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(
        String(project.groupId),
        'the PR06 revocation fixture is group-scoped so removing Project membership revokes view access'
      ).toMatch(/^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f-]{36}$/i);

      const members = await recordFetchJson(
        page,
        evidence,
        'pr06-project-members',
        `/api/projects/${evidence.projectId}/members`,
        {
          validate: (body) => Array.isArray(body) &&
            body.some((member: unknown) => hasStringValue(member, 'email', pr05ManagerEmail)) &&
            body.some((member: unknown) => hasStringValue(member, 'email', pr06ViewerEmail)) &&
            body.some((member: unknown) => hasStringValue(member, 'email', smokeEmail))
        }
      ) as Record<string, any>[];
      const managerMember = members.find((member) => member.email === pr05ManagerEmail)!;
      const viewerMember = members.find((member) => member.email === pr06ViewerEmail)!;
      const ownerMember = members.find((member) => member.email === smokeEmail)!;
      expect(String(managerMember.userId)).toBe(evidence.userId);
      expect(String(viewerMember.userId)).not.toBe(evidence.userId);
      expect(String(ownerMember.userId)).not.toBe(evidence.userId);

      const ganttPath = `/api/projects/${evidence.projectId}/gantt`;
      const initialSnapshotResponse = waitForApiResponse(page, 'GET', ganttPath);
      await page.goto(`/app/projects/${evidence.projectId}`);
      let snapshot = await recordOkJson(
        await initialSnapshotResponse,
        evidence,
        'pr06-initial-gantt-snapshot',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;

      const initialItems = Object.fromEntries(
        Object.entries(pr06TaskTitles).map(([key, title]) => [key, pr06GanttItem(snapshot, title)])
      ) as Record<keyof typeof pr06TaskTitles, Pr06GanttItemDto>;
      const milestone = pr06GanttItem(snapshot, pr06MilestoneTitle);
      const allInitialIds = [
        ...snapshot.scheduledItems,
        ...snapshot.unscheduledItems,
        ...snapshot.milestones
      ].map((item) => item.taskId);
      expect(new Set(allInitialIds).size, 'snapshot must not duplicate canonical WorkItems').toBe(allInitialIds.length);
      expect(snapshot.totalItems).toBe(allInitialIds.length);
      expect(snapshot.totalItems).toBeLessThanOrEqual(snapshot.maximumItems);
      expect(snapshot.calendar.timeZone, 'the server returns an actual Workspace timezone identifier').toBeTruthy();
      expect(snapshot.calendar.holidaysAvailable).toBe(false);
      expect(snapshot.calendar.limitations.length).toBeGreaterThan(0);
      expect(snapshot.permissions).toMatchObject({
        canEditSchedule: true,
        canEditProgress: true,
        canManageDependencies: true,
        canClearSchedule: true,
        canOpen: true
      });
      expect(initialItems.parent.progressIsDerived).toBe(true);
      expect(initialItems.parent.warnings.some((warning) => warning.code === 'PARENT_DERIVED')).toBe(true);
      expect(initialItems.parent.scheduleEditPermissions).toMatchObject({
        canEditSchedule: false,
        canEditProgress: false,
        canClearSchedule: false
      });
      expect(initialItems.unscheduled.warnings.some((warning) => warning.code === 'UNSCHEDULED')).toBe(true);
      expect(milestone.kind).toBe('Milestone');
      expect(milestone.milestoneDate).toBeTruthy();
      expect(milestone.plannedStartDate).toBeNull();
      expect(milestone.plannedEndDate).toBeNull();
      expect(snapshot.dependencies.some((dependency) =>
        dependency.type === 'FinishToStart' && dependency.editable)).toBe(true);
      expect(snapshot.dependencies.some((dependency) =>
        dependency.type !== 'FinishToStart' &&
        dependency.editable === false &&
        dependency.warnings.some((warning) => warning.code === 'LEGACY_DEPENDENCY_TYPE'))).toBe(true);
      expect(snapshot.warnings.some((warning) => warning.code === 'DEPENDENCY_VIOLATION')).toBe(true);

      viewerContext = await browser.newContext({ baseURL: evidence.baseURL });
      const viewerPage = await viewerContext.newPage();
      const viewerEvidence: SmokeEvidence = {
        baseURL: evidence.baseURL,
        email: pr06ViewerEmail,
        steps: [],
        pageErrors: [],
        consoleErrors: [],
        failedApiResponses: []
      };
      viewerPage.on('pageerror', (error) => viewerEvidence.pageErrors.push(error.message));
      viewerPage.on('console', (message) => {
        if (message.type() === 'error') {viewerEvidence.consoleErrors.push(message.text());}
      });
      viewerPage.on('response', (response) => recordFailedApiResponse(response, viewerEvidence));
      await loginAndVerifySession(
        viewerPage,
        viewerEvidence,
        { email: pr06ViewerEmail, password: `${smokePassword}:recipient` }
      );
      expect(viewerEvidence.userId).toBe(String(viewerMember.userId));
      const viewerSnapshotPromise = waitForApiResponse(viewerPage, 'GET', ganttPath);
      await viewerPage.goto(`/app/projects/${evidence.projectId}`);
      const viewerSnapshotResponse = await viewerSnapshotPromise;
      const viewerSnapshot = await recordOkJson(
        viewerSnapshotResponse,
        viewerEvidence,
        'pr06-viewer-gantt-snapshot',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      expect(viewerSnapshot.permissions).toMatchObject({
        canEditSchedule: false,
        canEditProgress: false,
        canManageDependencies: false,
        canClearSchedule: false,
        canOpen: true
      });
      expect([
        ...viewerSnapshot.scheduledItems,
        ...viewerSnapshot.unscheduledItems,
        ...viewerSnapshot.milestones
      ].every((item) =>
        !item.scheduleEditPermissions.canEditSchedule &&
        !item.scheduleEditPermissions.canEditProgress &&
        !item.scheduleEditPermissions.canManageDependencies &&
        !item.scheduleEditPermissions.canClearSchedule
      ), 'viewer item permissions remain read-only').toBe(true);
      expect(
        viewerSnapshot.dependencies.every((dependency) => !dependency.editable),
        'viewer dependency permissions remain read-only'
      ).toBe(true);
      await viewerPage.getByRole('tab', { name: 'Schedule', exact: true }).click();
      await expect(viewerPage.getByText('Schedule is read-only for the current actor.')).toBeVisible();
      const viewerEditActions = viewerPage.getByRole('button', {
        name: /^(Edit dates|Edit Milestone date|Edit progress|Move to unscheduled|Add FS predecessor|Remove FS dependency)$/
      });
      await expect(viewerEditActions).toHaveCount(0);
      expect(viewerEvidence.pageErrors, 'viewer browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(viewerEvidence);
      expectUnexpectedApiFailures(viewerEvidence);
      evidence.viewerReadOnly = {
        userId: viewerEvidence.userId!,
        snapshotStatus: viewerSnapshotResponse.status(),
        permissions: viewerSnapshot.permissions,
        editActionCount: 0,
        pageErrors: [...viewerEvidence.pageErrors],
        consoleErrors: [...viewerEvidence.consoleErrors],
        failedApiResponses: [...viewerEvidence.failedApiResponses]
      };
      await viewerContext.close();
      viewerContext = null;

      const canonicalTasks = await recordFetchJson(
        page,
        evidence,
        'pr06-shared-canonical-task-list',
        `/api/projects/${evidence.projectId}/tasks?page=1&pageSize=100`,
        {
          validate: (body) => isPagedResponse(body) &&
            Object.values(pr06TaskTitles).every((title) =>
              body.items.some((item: unknown) => hasStringValue(item, 'title', title)))
        }
      ) as Record<string, any>;
      const canonicalTaskIds = new Set(canonicalTasks.items.map((item: Record<string, unknown>) => String(item.id)));
      expect(Object.values(initialItems).every((item) => canonicalTaskIds.has(item.taskId)),
        'Gantt Task rows use the same canonical TaskItem ids as Project List').toBe(true);

      await page.getByRole('tab', { name: 'Schedule', exact: true }).click();
      await expect(page.getByTestId('project-schedule')).toBeVisible();
      await expect(page.getByTestId('coglatas-gantt-projection')).toBeVisible();
      await expect(page.getByText(`Workspace timezone:`)).toContainText(snapshot.calendar.timeZone);
      await expect(pr06GanttItemLocator(page, initialItems.parent.taskId)).toContainText('Derived parent Task');
      await expect(pr06GanttItemLocator(page, initialItems.parent.taskId)
        .getByRole('button', { name: /Edit dates|Edit progress|Move to unscheduled/ })).toHaveCount(0);
      const scheduleWarnings = page.getByRole('region', { name: 'Schedule warnings', exact: true });
      await expect(scheduleWarnings).toContainText('DEPENDENCY_VIOLATION');
      await expect(scheduleWarnings).toContainText('LEGACY_DEPENDENCY_TYPE');

      const scheduleDetailBefore = await recordFetchJson(
        page,
        evidence,
        'pr06-schedule-task-before',
        `/api/tasks/${initialItems.schedule.taskId}`,
        { validate: (body) => taskDetailVersion(body) === initialItems.schedule.version && taskDeadlineAt(body) !== null }
      ) as Record<string, any>;
      const deadlineAtBefore = taskDeadlineAt(scheduleDetailBefore);
      const predecessorDatesBefore = {
        plannedStartDate: initialItems.predecessor.plannedStartDate,
        plannedEndDate: initialItems.predecessor.plannedEndDate
      };

      let scheduleLocator = pr06GanttItemLocator(page, initialItems.schedule.taskId);
      await scheduleLocator.getByRole('button', { name: 'Edit dates', exact: true }).click();
      await page.getByLabel('Planned start').fill('2031-03-01');
      await page.getByLabel('Planned end').fill('2031-03-05');
      const scheduleCommandResponse = waitForApiResponse(page, 'PATCH', `/api/tasks/${initialItems.schedule.taskId}/schedule`);
      const scheduleRefetchResponse = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Apply schedule', exact: true }).click();
      const scheduleCommand = await recordPr06Command(
        await scheduleCommandResponse,
        evidence,
        'manual-schedule-update',
        200
      );
      expect(scheduleCommand.request).toMatchObject({
        plannedStartDate: '2031-03-01',
        plannedEndDate: '2031-03-05',
        milestoneDate: null,
        expectedVersion: initialItems.schedule.version
      });
      snapshot = await recordOkJson(
        await scheduleRefetchResponse,
        evidence,
        'pr06-schedule-update-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      let scheduleTask = pr06GanttItem(snapshot, pr06TaskTitles.schedule);
      expect(scheduleTask.plannedStartDate).toBe('2031-03-01');
      expect(scheduleTask.plannedEndDate).toBe('2031-03-05');
      expect(scheduleTask.version).toBeGreaterThan(initialItems.schedule.version);
      const predecessorAfterSchedule = pr06GanttItem(snapshot, pr06TaskTitles.predecessor);
      expect({
        plannedStartDate: predecessorAfterSchedule.plannedStartDate,
        plannedEndDate: predecessorAfterSchedule.plannedEndDate
      }, 'manual schedule edits do not cascade to dependency neighbors').toEqual(predecessorDatesBefore);
      await expect(pr06GanttItemLocator(page, scheduleTask.taskId)).toContainText('2031-03-01 to 2031-03-05');
      await expectLogicalPr06GanttFocus(pr06GanttItemLocator(page, scheduleTask.taskId));

      const scheduleDetailAfter = await recordFetchJson(
        page,
        evidence,
        'pr06-schedule-task-after',
        `/api/tasks/${scheduleTask.taskId}`,
        {
          validate: (body) =>
            taskDetailVersion(body) === scheduleTask.version &&
            taskDeadlineAt(body) === deadlineAtBefore
        }
      );
      expect(taskDeadlineAt(scheduleDetailAfter), 'Gantt movement must not change DeadlineAt').toBe(deadlineAtBefore);

      const milestoneBefore = pr06GanttItem(snapshot, pr06MilestoneTitle);
      await pr06GanttItemLocator(page, milestoneBefore.taskId)
        .getByRole('button', { name: 'Edit Milestone date', exact: true }).click();
      await page.getByRole('dialog', { name: 'Edit Milestone date' })
        .locator('input[name="milestoneDate"]')
        .fill('2031-03-20');
      const milestoneCommandResponse = waitForApiResponse(page, 'PATCH', `/api/tasks/${milestoneBefore.taskId}/schedule`);
      const milestoneRefetchResponse = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Apply schedule', exact: true }).click();
      const milestoneCommand = await recordPr06Command(
        await milestoneCommandResponse,
        evidence,
        'manual-milestone-date-update',
        200
      );
      expect(milestoneCommand.request).toMatchObject({
        plannedStartDate: null,
        plannedEndDate: null,
        milestoneDate: '2031-03-20',
        expectedVersion: milestoneBefore.version
      });
      snapshot = await recordOkJson(
        await milestoneRefetchResponse,
        evidence,
        'pr06-milestone-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      const milestoneAfter = pr06GanttItem(snapshot, pr06MilestoneTitle);
      expect(milestoneAfter.milestoneDate).toBe('2031-03-20');
      expect(milestoneAfter.plannedStartDate).toBeNull();
      expect(milestoneAfter.plannedEndDate).toBeNull();

      let progressTask = pr06GanttItem(snapshot, pr06TaskTitles.predecessor);
      await pr06GanttItemLocator(page, progressTask.taskId)
        .getByRole('button', { name: 'Edit progress', exact: true }).click();
      await page.getByLabel('Progress percent').fill('35');
      const progressCommandResponse = waitForApiResponse(page, 'PATCH', `/api/tasks/${progressTask.taskId}/progress`);
      const progressRefetchResponse = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Apply progress', exact: true }).click();
      const progressCommand = await recordPr06Command(
        await progressCommandResponse,
        evidence,
        'manual-leaf-progress-update',
        200
      );
      expect(progressCommand.request).toMatchObject({
        progressPercent: 35,
        expectedVersion: progressTask.version
      });
      snapshot = await recordOkJson(
        await progressRefetchResponse,
        evidence,
        'pr06-progress-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      progressTask = pr06GanttItem(snapshot, pr06TaskTitles.predecessor);
      expect(progressTask.progressPercent).toBe(35);
      await expect(pr06GanttItemLocator(page, progressTask.taskId)).toContainText('35%');

      let successor = pr06GanttItem(snapshot, pr06TaskTitles.successor);
      const successorDatesBeforeDependency = {
        plannedStartDate: successor.plannedStartDate,
        plannedEndDate: successor.plannedEndDate
      };
      const dependencyPredecessor = pr06GanttItem(snapshot, pr06TaskTitles.predecessor);
      await pr06GanttItemLocator(page, successor.taskId)
        .getByRole('button', { name: 'Add FS predecessor', exact: true }).click();
      await page.getByLabel('Finish-to-Start predecessor').selectOption(dependencyPredecessor.taskId);
      const dependencyAddResponse = waitForApiResponse(page, 'POST', `/api/tasks/${successor.taskId}/dependencies`);
      const dependencyAddRefetch = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Add dependency', exact: true }).click();
      const dependencyAdd = await recordPr06Command(
        await dependencyAddResponse,
        evidence,
        'fs-dependency-add',
        200
      );
      expect(dependencyAdd.request).toMatchObject({
        predecessorTaskId: dependencyPredecessor.taskId,
        dependencyType: 'FinishToStart',
        expectedVersion: successor.version
      });
      snapshot = await recordOkJson(
        await dependencyAddRefetch,
        evidence,
        'pr06-dependency-add-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      const addedDependency = snapshot.dependencies.find((dependency) =>
        dependency.predecessorTaskId === dependencyPredecessor.taskId &&
        dependency.successorTaskId === successor.taskId);
      expect(addedDependency, 'the real FS dependency appears in the authoritative snapshot').toBeTruthy();
      expect(addedDependency!.type).toBe('FinishToStart');
      expect(addedDependency!.editable).toBe(true);
      const successorAfterDependency = pr06GanttItem(snapshot, pr06TaskTitles.successor);
      const successorDatesAfterDependency = {
        plannedStartDate: successorAfterDependency.plannedStartDate,
        plannedEndDate: successorAfterDependency.plannedEndDate
      };
      expect(
        successorDatesAfterDependency,
        'adding an FS dependency must not automatically move the successor'
      ).toEqual(successorDatesBeforeDependency);
      evidence.dependencyNoCascade = {
        successorTaskId: successor.taskId,
        before: successorDatesBeforeDependency,
        after: successorDatesAfterDependency
      };
      await expect(page.locator(`[data-gantt-dependency-id="${addedDependency!.dependencyId}"]`)).toBeVisible();

      successor = pr06GanttItem(snapshot, pr06TaskTitles.successor);
      const dependencyLocator = page.locator(`[data-gantt-dependency-id="${addedDependency!.dependencyId}"]`);
      await dependencyLocator.getByRole('button', { name: 'Remove FS dependency', exact: true }).click();
      const dependencyDeleteResponse = waitForApiResponse(
        page,
        'DELETE',
        `/api/tasks/${successor.taskId}/dependencies/${addedDependency!.dependencyId}`
      );
      const dependencyDeleteRefetch = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Remove dependency', exact: true }).click();
      const dependencyDelete = await recordPr06Command(
        await dependencyDeleteResponse,
        evidence,
        'fs-dependency-remove',
        200
      );
      expect(new URL(dependencyDelete.response.url()).searchParams.get('expectedVersion'))
        .toBe(String(successor.version));
      snapshot = await recordOkJson(
        await dependencyDeleteRefetch,
        evidence,
        'pr06-dependency-remove-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      expect(snapshot.dependencies.some((dependency) => dependency.dependencyId === addedDependency!.dependencyId)).toBe(false);
      await expect(page.locator(`[data-gantt-dependency-id="${addedDependency!.dependencyId}"]`)).toHaveCount(0);

      scheduleTask = pr06GanttItem(snapshot, pr06TaskTitles.schedule);
      scheduleLocator = pr06GanttItemLocator(page, scheduleTask.taskId);
      await scheduleLocator.getByRole('button', { name: 'Move to unscheduled', exact: true }).click();
      const clearCommandResponse = waitForApiResponse(page, 'PATCH', `/api/tasks/${scheduleTask.taskId}/schedule`);
      const clearRefetchResponse = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Clear schedule', exact: true }).click();
      const clearCommand = await recordPr06Command(
        await clearCommandResponse,
        evidence,
        'manual-schedule-clear',
        200
      );
      expect(clearCommand.request).toMatchObject({
        plannedStartDate: null,
        plannedEndDate: null,
        milestoneDate: null,
        expectedVersion: scheduleTask.version
      });
      snapshot = await recordOkJson(
        await clearRefetchResponse,
        evidence,
        'pr06-schedule-clear-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      scheduleTask = pr06GanttItem(snapshot, pr06TaskTitles.schedule);
      expect(snapshot.unscheduledItems.some((item) => item.taskId === scheduleTask.taskId)).toBe(true);
      expect(scheduleTask.warnings.some((warning) => warning.code === 'UNSCHEDULED')).toBe(true);
      await expect(page.getByRole('heading', { name: 'Unscheduled work', exact: true })
        .locator('..')
        .locator(`[data-gantt-item-id="${scheduleTask.taskId}"]`)).toBeVisible();

      degradedContext = await browser.newContext({ baseURL: evidence.baseURL });
      await degradedContext.addInitScript(() => {
        const nativeFetch = window.fetch.bind(window);
        window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
          const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
          if (new URL(url, window.location.href).pathname.startsWith('/hubs/app')) {
            return Promise.reject(new TypeError('Synthetic PR06 Hub unavailability'));
          }
          return nativeFetch(input, init);
        };
      });
      const degradedPage = await degradedContext.newPage();
      const degradedEvidence: SmokeEvidence = {
        baseURL: evidence.baseURL,
        email: pr05ManagerEmail,
        steps: [],
        pageErrors: [],
        consoleErrors: [],
        failedApiResponses: []
      };
      degradedPage.on('pageerror', (error) => degradedEvidence.pageErrors.push(error.message));
      degradedPage.on('console', (message) => {
        if (message.type() === 'error') {degradedEvidence.consoleErrors.push(message.text());}
      });
      degradedPage.on('response', (response) => recordFailedApiResponse(response, degradedEvidence));
      await loginAndVerifySession(degradedPage, degradedEvidence, { email: pr05ManagerEmail, password: smokePassword });
      await expect(degradedPage.getByTestId('realtime-connection-state')).toContainText('Realtime updates are delayed');
      const degradedSnapshotResponse = waitForApiResponse(degradedPage, 'GET', ganttPath);
      await degradedPage.goto(`/app/projects/${evidence.projectId}`);
      const degradedSnapshot = await recordOkJson(
        await degradedSnapshotResponse,
        degradedEvidence,
        'pr06-degraded-gantt-snapshot',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      await degradedPage.getByRole('tab', { name: 'Schedule', exact: true }).click();
      await expect(degradedPage.getByText(/HTTP edits and manual refresh remain authoritative/)).toBeVisible();
      const degradedSuccessor = pr06GanttItem(degradedSnapshot, pr06TaskTitles.successor);
      await pr06GanttItemLocator(degradedPage, degradedSuccessor.taskId)
        .getByRole('button', { name: 'Edit progress', exact: true }).click();
      await degradedPage.getByLabel('Progress percent').fill('15');
      const degradedProgressResponse = waitForApiResponse(
        degradedPage,
        'PATCH',
        `/api/tasks/${degradedSuccessor.taskId}/progress`
      );
      const degradedProgressRefetch = waitForSuccessfulApiResponse(degradedPage, 'GET', ganttPath);
      await degradedPage.getByRole('button', { name: 'Apply progress', exact: true }).click();
      const degradedProgress = await recordPr06Command(
        await degradedProgressResponse,
        evidence,
        'signalr-degraded-http-progress',
        200
      );
      expect(degradedProgress.request).toMatchObject({
        progressPercent: 15,
        expectedVersion: degradedSuccessor.version
      });
      const degradedAuthoritative = await recordOkJson(
        await degradedProgressRefetch,
        degradedEvidence,
        'pr06-degraded-progress-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      expect(pr06GanttItem(degradedAuthoritative, pr06TaskTitles.successor).progressPercent).toBe(15);
      const degradedManualRefresh = waitForSuccessfulApiResponse(degradedPage, 'GET', ganttPath);
      await degradedPage.getByRole('button', { name: 'Refresh schedule', exact: true }).click();
      await recordOkJson(
        await degradedManualRefresh,
        degradedEvidence,
        'pr06-degraded-manual-http-refresh',
        isPr06GanttSnapshot
      );
      expect(degradedEvidence.pageErrors, 'SignalR-degraded browser page errors').toEqual([]);
      expectOnlyExpectedSyntheticHubConsoleErrors(degradedEvidence);
      expectUnexpectedApiFailures(degradedEvidence);
      evidence.degradedHttp = {
        connectionState: 'delayed',
        commandStatus: degradedProgress.response.status(),
        manualRefreshStatus: 200,
        apiInterception: 'none',
        pageErrors: [...degradedEvidence.pageErrors],
        consoleErrors: [...degradedEvidence.consoleErrors],
        failedApiResponses: [...degradedEvidence.failedApiResponses]
      };
      await degradedContext.close();
      degradedContext = null;

      const mainRefreshAfterDegraded = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Refresh schedule', exact: true }).click();
      snapshot = await recordOkJson(
        await mainRefreshAfterDegraded,
        evidence,
        'pr06-main-refresh-after-degraded-command',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      expect(pr06GanttItem(snapshot, pr06TaskTitles.successor).progressPercent).toBe(15);

      // Keep the semantic edit dialog open while a separate real command
      // advances the same Task version. The UI then sends the stale version,
      // renders its optimistic dates, receives a real 409, rolls back, and
      // refetches the authoritative dates while preserving the user intent.
      const conflictBefore = pr06GanttItem(snapshot, pr06TaskTitles.conflict);
      const conflictLocator = pr06GanttItemLocator(page, conflictBefore.taskId);
      await conflictLocator.getByRole('button', { name: 'Edit dates', exact: true }).click();
      await page.getByLabel('Planned start').fill('2031-05-01');
      await page.getByLabel('Planned end').fill('2031-05-02');
      const concurrentConflict = await requestWithCsrf(
        page,
        'PATCH',
        `/api/tasks/${conflictBefore.taskId}/schedule`,
        {
          plannedStartDate: '2031-04-01',
          plannedEndDate: '2031-04-02',
          milestoneDate: null,
          expectedVersion: conflictBefore.version
        }
      );
      expect(concurrentConflict.status, concurrentConflict.text).toBe(200);
      expect(concurrentConflict.csrfHeaderPresent).toBe(true);
      const concurrentConflictBody = parseJson(concurrentConflict.text);
      expect(concurrentConflictBody.version).toBeGreaterThan(conflictBefore.version);
      evidence.commands.push({
        name: 'concurrent-real-schedule-update',
        method: 'PATCH',
        path: `/api/tasks/${conflictBefore.taskId}/schedule`,
        status: concurrentConflict.status,
        request: {
          plannedStartDate: '2031-04-01',
          plannedEndDate: '2031-04-02',
          milestoneDate: null,
          expectedVersion: conflictBefore.version
        },
        csrfHeaderPresent: concurrentConflict.csrfHeaderPresent
      });
      await expect(page.getByText(/authoritative refresh is queued until the active schedule interaction finishes/i))
        .toBeVisible({ timeout: 30_000 });

      await page.evaluate((taskId) => {
        const state = { transitions: [] as string[], observer: null as MutationObserver | null };
        const record = () => {
          const text = document.querySelector(`[data-gantt-item-id="${taskId}"]`)?.textContent ?? '';
          const dates = /(\d{4}-\d{2}-\d{2}) to (\d{4}-\d{2}-\d{2})/.exec(text);
          const value = dates ? `${dates[1]} to ${dates[2]}` : '';
          if (value && state.transitions.at(-1) !== value) {state.transitions.push(value);}
        };
        record();
        state.observer = new MutationObserver(record);
        state.observer.observe(document.body, { childList: true, subtree: true, characterData: true });
        (window as Window & { __pr06ConflictObserver?: typeof state }).__pr06ConflictObserver = state;
      }, conflictBefore.taskId);
      const staleConflictResponse = waitForApiResponse(page, 'PATCH', `/api/tasks/${conflictBefore.taskId}/schedule`);
      const conflictRefetchResponse = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.getByRole('button', { name: 'Apply schedule', exact: true }).click();
      const staleConflict = await recordPr06Command(
        await staleConflictResponse,
        evidence,
        'stale-schedule-conflict',
        409
      );
      expect(staleConflict.request).toMatchObject({
        plannedStartDate: '2031-05-01',
        plannedEndDate: '2031-05-02',
        milestoneDate: null,
        expectedVersion: conflictBefore.version
      });
      expect(staleConflict.body.error?.code).toBe('GANTT_STALE_VERSION');
      expect(staleConflict.text).not.toMatch(/tenantId|workspaceId|PR06 dependency successor|browser-smoke-pr06-gantt/i);
      snapshot = await recordOkJson(
        await conflictRefetchResponse,
        evidence,
        'pr06-conflict-authoritative-refetch',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      const conflictAfter = pr06GanttItem(snapshot, pr06TaskTitles.conflict);
      expect(conflictAfter.plannedStartDate).toBe('2031-04-01');
      expect(conflictAfter.plannedEndDate).toBe('2031-04-02');
      await expect(page.getByRole('status')
        .filter({ hasText: /The edit intent is preserved against the authoritative schedule/ })).toBeVisible();
      await expect(page.getByRole('button', { name: /Retry preserved edit against the latest version/ })).toBeVisible();
      await expectLogicalPr06GanttFocus(pr06GanttItemLocator(page, conflictAfter.taskId));
      const optimisticDateTransitions = await page.evaluate(() => {
        const state = (window as Window & {
          __pr06ConflictObserver?: { transitions: string[]; observer: MutationObserver | null };
        }).__pr06ConflictObserver;
        state?.observer?.disconnect();
        return state?.transitions ?? [];
      });
      const optimisticIndex = optimisticDateTransitions.indexOf('2031-05-01 to 2031-05-02');
      expect(optimisticIndex, 'the stale command renders the optimistic dates before the backend responds').toBeGreaterThan(-1);
      expect(optimisticDateTransitions.slice(optimisticIndex + 1),
        'the real 409 rolls the schedule back to the authoritative concurrent dates')
        .toContain('2031-04-01 to 2031-04-02');
      evidence.staleConflict = {
        taskId: conflictBefore.taskId,
        staleVersion: conflictBefore.version,
        concurrentVersion: Number(concurrentConflictBody.version),
        code: staleConflict.body.error?.code,
        status: staleConflict.response.status(),
        authoritativeVersion: conflictAfter.version,
        authoritativeDates: {
          plannedStartDate: conflictAfter.plannedStartDate,
          plannedEndDate: conflictAfter.plannedEndDate
        },
        optimisticDateTransitions,
        intentPreserved: true,
        focusRestored: true
      };

      const reloadSnapshotResponse = waitForSuccessfulApiResponse(page, 'GET', ganttPath);
      await page.reload();
      snapshot = await recordOkJson(
        await reloadSnapshotResponse,
        evidence,
        'pr06-reload-persistence',
        isPr06GanttSnapshot
      ) as Pr06GanttSnapshotDto;
      await page.getByRole('tab', { name: 'Schedule', exact: true }).click();
      expect(snapshot.unscheduledItems.some((item) => item.title === pr06TaskTitles.schedule)).toBe(true);
      expect(pr06GanttItem(snapshot, pr06TaskTitles.predecessor).progressPercent).toBe(35);
      expect(pr06GanttItem(snapshot, pr06TaskTitles.successor).progressPercent).toBe(15);
      expect(pr06GanttItem(snapshot, pr06MilestoneTitle).milestoneDate).toBe('2031-03-20');
      expect(pr06GanttItem(snapshot, pr06TaskTitles.conflict).plannedStartDate).toBe('2031-04-01');
      expect(snapshot.dependencies.some((dependency) => dependency.dependencyId === addedDependency!.dependencyId)).toBe(false);
      evidence.reloadPersistence = {
        scheduleTaskUnscheduled: true,
        progressPercent: 35,
        degradedProgressPercent: 15,
        milestoneDate: '2031-03-20',
        conflictDates: ['2031-04-01', '2031-04-02'],
        removedDependencyAbsent: true
      };

      const protectedDataClear = page.evaluate((protectedTitles) =>
        new Promise<'protected-data-cleared'>((resolve) => {
          const isClear = () => {
            const text = document.body.innerText;
            return document.querySelectorAll('[data-gantt-item-id]').length === 0 &&
              protectedTitles.every((title) => !text.includes(title));
          };
          if (isClear()) {
            resolve('protected-data-cleared');
            return;
          }
          const observer = new MutationObserver(() => {
            if (!isClear()) {return;}
            observer.disconnect();
            resolve('protected-data-cleared');
          });
          observer.observe(document.body, { childList: true, subtree: true, characterData: true });
        }), [...Object.values(pr06TaskTitles), pr06MilestoneTitle]);

      ownerContext = await browser.newContext({ baseURL: evidence.baseURL });
      const ownerPage = await ownerContext.newPage();
      const ownerEvidence: SmokeEvidence = {
        baseURL: evidence.baseURL,
        email: smokeEmail,
        steps: [],
        pageErrors: [],
        consoleErrors: [],
        failedApiResponses: []
      };
      ownerPage.on('pageerror', (error) => ownerEvidence.pageErrors.push(error.message));
      ownerPage.on('console', (message) => {
        if (message.type() === 'error') {ownerEvidence.consoleErrors.push(message.text());}
      });
      ownerPage.on('response', (response) => recordFailedApiResponse(response, ownerEvidence));
      await loginAndVerifySession(ownerPage, ownerEvidence);
      expect(ownerEvidence.userId, 'the revoking Owner is a distinct authenticated user').not.toBe(evidence.userId);
      expect(ownerEvidence.userId).toBe(String(ownerMember.userId));

      const responseGateId = randomUUID().replaceAll('-', '');
      const gateArm = await requestWithCsrf(
        page,
        'POST',
        `${pr05ResponseGatePath}/${responseGateId}/arm`,
        { method: 'GET', path: ganttPath }
      );
      expect(gateArm.status, gateArm.text).toBe(200);
      expect(gateArm.csrfHeaderPresent).toBe(true);
      await page.context().addCookies([{
        name: pr05ResponseGateCookieName,
        value: responseGateId,
        url: new URL('/', page.url()).href
      }]);

      const heldAuthorizedRefreshResponse = page.waitForResponse((response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === ganttPath &&
        response.status() === 200 &&
        response.headers()[pr05ResponseGateHeaderName] === responseGateId
      );
      const heldRefreshRequest = page.waitForRequest((request) =>
        request.method() === 'GET' && new URL(request.url()).pathname === ganttPath);
      await page.getByRole('button', { name: 'Refresh schedule', exact: true }).click();
      await heldRefreshRequest;
      await expect.poll(async () => {
        const gate = await fetchJsonFromPage(page, `${pr05ResponseGatePath}/${responseGateId}`);
        return gate.status === 200 ? gate.body : { state: `HTTP ${gate.status}`, statusCode: null };
      }, {
        message: 'the real authorized Gantt response reaches the one-shot response gate',
        timeout: 10_000
      }).toMatchObject({ state: 'waiting', statusCode: 200 });

      const deniedRefreshResponse = page.waitForResponse((response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === ganttPath &&
        response.status() === 404
      );
      const projectDetailPath = `/api/projects/${evidence.projectId}`;
      const deniedProjectDetailResponse = page.waitForResponse((response) =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname === projectDetailPath
      );
      const firstRevocationObservationPromise = Promise.race([
        protectedDataClear,
        deniedRefreshResponse.then(() => 'denial-response' as const)
      ]);
      const revoke = await requestWithCsrf(
        ownerPage,
        'DELETE',
        `/api/projects/${evidence.projectId}/members/${evidence.userId}`
      );
      expect(revoke.status, revoke.text).toBe(200);
      expect(revoke.csrfHeaderPresent).toBe(true);
      const firstRevocationObservation = await firstRevocationObservationPromise;
      expect(firstRevocationObservation,
        'protected schedule DOM is cleared before the authoritative denial response is delivered')
        .toBe('protected-data-cleared');
      const deniedResponse = await deniedRefreshResponse;
      const deniedText = await deniedResponse.text();
      evidence.steps.push({
        name: 'pr06-authorization-state-safe-gantt-denial',
        method: 'GET',
        path: new URL(deniedResponse.url()).pathname,
        status: deniedResponse.status(),
        bodyPreview: preview(deniedText)
      });
      expectCanonicalGanttDenial(deniedText, 'GANTT_PROJECT_NOT_FOUND');
      const deniedProjectResponse = await deniedProjectDetailResponse;
      const deniedProjectText = await deniedProjectResponse.text();
      const expectedProjectDetailDenial: SmokeFailedApiResponse = {
        method: deniedProjectResponse.request().method(),
        path: new URL(deniedProjectResponse.url()).pathname,
        status: deniedProjectResponse.status()
      };
      evidence.steps.push({
        name: 'pr06-project-detail-safe-denial-after-revocation',
        method: expectedProjectDetailDenial.method,
        path: expectedProjectDetailDenial.path,
        status: expectedProjectDetailDenial.status,
        bodyPreview: preview(deniedProjectText)
      });
      const deniedProjectBody = expectSafeProjectDetailDenial(deniedProjectText, [
        evidence.projectId!, evidence.workspaceId!, pr06ProjectTitle,
        ...Object.values(pr06TaskTitles), pr06MilestoneTitle
      ]);
      expect(deniedProjectResponse.status(), 'revoked Project detail uses the established safe denial contract')
        .toBe(400);
      expect(evidence.failedApiResponses).toContainEqual(expectedProjectDetailDenial);
      await expect(page.locator('[data-gantt-item-id]')).toHaveCount(0);
      await expect(page.locator('[data-gantt-dependency-id]')).toHaveCount(0);
      for (const title of [...Object.values(pr06TaskTitles), pr06MilestoneTitle]) {
        await expect(page.getByText(title, { exact: true })).toHaveCount(0);
      }
      await expect(page.locator('body')).not.toContainText(/DEPENDENCY_VIOLATION|LEGACY_DEPENDENCY_TYPE|PARENT_DERIVED/);

      await page.evaluate((protectedTitles) => {
        const state = { restored: false, observer: null as MutationObserver | null };
        const observe = () => {
          const text = document.body.innerText;
          if (
            document.querySelectorAll('[data-gantt-item-id]').length > 0 ||
            protectedTitles.some((title) => text.includes(title))
          ) {
            state.restored = true;
          }
        };
        state.observer = new MutationObserver(observe);
        state.observer.observe(document.body, { childList: true, subtree: true, characterData: true });
        (window as Window & { __pr06RevocationObserver?: typeof state }).__pr06RevocationObserver = state;
        observe();
      }, [...Object.values(pr06TaskTitles), pr06MilestoneTitle]);

      const gateRelease = await requestWithCsrf(
        page,
        'POST',
        `${pr05ResponseGatePath}/${responseGateId}/release`
      );
      expect(gateRelease.status, gateRelease.text).toBe(200);
      const staleAuthorizedResponse = await heldAuthorizedRefreshResponse;
      expect(staleAuthorizedResponse.status()).toBe(200);
      expect(await staleAuthorizedResponse.finished()).toBeNull();
      evidence.steps.push({
        name: 'pr06-stale-authorized-gantt-response-after-revocation',
        method: 'GET',
        path: new URL(staleAuthorizedResponse.url()).pathname,
        status: staleAuthorizedResponse.status()
      });
      await page.evaluate(() => new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      const protectedDataRestoredByStaleResponse = await page.evaluate(() => {
        const state = (window as Window & {
          __pr06RevocationObserver?: { restored: boolean; observer: MutationObserver | null };
        }).__pr06RevocationObserver;
        state?.observer?.disconnect();
        return state?.restored ?? true;
      });
      expect(protectedDataRestoredByStaleResponse,
        'the held authorized Gantt 200 must not restore protected schedule data').toBe(false);
      await expect(page.locator('[data-gantt-item-id]')).toHaveCount(0);

      const subsequentDenial = await fetchFromPage(page, ganttPath);
      evidence.steps.push({
        name: 'pr06-subsequent-gantt-safe-denial',
        method: 'GET',
        path: ganttPath,
        status: subsequentDenial.status,
        bodyPreview: preview(subsequentDenial.text)
      });
      expect(subsequentDenial.status).toBe(404);
      expectCanonicalGanttDenial(subsequentDenial.text, 'GANTT_PROJECT_NOT_FOUND');
      expect(ownerEvidence.pageErrors, 'revoking Owner browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(ownerEvidence);
      expectUnexpectedApiFailures(ownerEvidence);
      evidence.authorizationRevocation = {
        revokingActorUserId: ownerEvidence.userId!,
        revokedUserId: evidence.userId!,
        revokeStatus: revoke.status,
        overlappingRefreshStatus: staleAuthorizedResponse.status(),
        projectDetailDenialStatus: deniedProjectResponse.status(),
        projectDetailDenialCode: String(deniedProjectBody.code),
        denialStatus: deniedResponse.status(),
        subsequentDenialStatus: subsequentDenial.status,
        protectedDataClearedBeforeRevalidation: firstRevocationObservation === 'protected-data-cleared',
        protectedDataRestoredByStaleResponse,
        revokingPageErrors: [...ownerEvidence.pageErrors],
        revokingConsoleErrors: [...ownerEvidence.consoleErrors],
        revokingFailedApiResponses: [...ownerEvidence.failedApiResponses]
      };

      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence, [expectedProjectDetailDenial]);
      expectUnexpectedApiFailures(evidence, [expectedProjectDetailDenial]);
    } finally {
      await degradedContext?.close();
      await ownerContext?.close();
      await viewerContext?.close();
      await testInfo.attach('task-v1-pr06-real-backend-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('TASK-V1-PR07-D reauthorizes notification delivery, opens current Task routes, and clears revoked state through the real backend', async ({ page, browser }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: pr06ViewerEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: []
    };
    let ownerContext: Awaited<ReturnType<typeof browser.newContext>> | null = null;
    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => { if (message.type() === 'error') {evidence.consoleErrors.push(message.text());} });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(
        page,
        evidence,
        { email: pr06ViewerEmail, password: `${smokePassword}:recipient` }
      );
      await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.', { timeout: 30_000 });

      const projects = await recordFetchJson(page, evidence, 'pr07-project-fixture', '/api/projects?page=1&pageSize=100', {
        validate: (body) => isPagedResponse(body) &&
          body.items.some((item: unknown) => hasStringValue(item, 'title', pr07ProjectTitle))
      }) as Record<string, any>;
      const project = projects.items.find((item: Record<string, unknown>) => item.title === pr07ProjectTitle)!;
      const projectId = String(project.id);
      const workspaceId = String(project.workspaceId);
      const recipientUserId = evidence.userId!;
      expect(projectId).toMatch(/^[0-9a-f-]{36}$/i);
      expect(workspaceId).toMatch(/^[0-9a-f-]{36}$/i);

      if (await page.getByTestId('right-panel-open').count()) {
        await page.getByTestId('right-panel-open').click();
      }
      await expect(page.getByRole('heading', { name: 'Notifications and members' })).toBeVisible();

      // The immediate delivery below happens only after the existing client
      // reconnects and re-subscribes to its current authorized User route.
      await verifyRealtimeTransportReconnect(page, evidence);

      ownerContext = await browser.newContext({ baseURL: evidence.baseURL });
      const ownerPage = await ownerContext.newPage();
      const ownerEvidence: SmokeEvidence = {
        baseURL: evidence.baseURL,
        email: smokeEmail,
        steps: [],
        pageErrors: [],
        consoleErrors: [],
        failedApiResponses: []
      };
      ownerPage.on('pageerror', (error) => ownerEvidence.pageErrors.push(error.message));
      ownerPage.on('console', (message) => { if (message.type() === 'error') {ownerEvidence.consoleErrors.push(message.text());} });
      ownerPage.on('response', (response) => recordFailedApiResponse(response, ownerEvidence));
      await loginAndVerifySession(ownerPage, ownerEvidence);

      const stageNotification = async (dispatchDelaySeconds: number) => {
        const response = await requestWithCsrf(
          ownerPage,
          'POST',
          `${pr07NotificationFixturePath}/task`,
          { dispatchDelaySeconds }
        );
        evidence.steps.push({
          name: dispatchDelaySeconds === 0 ? 'pr07-stage-immediate-task-notification' : 'pr07-stage-delayed-task-notification',
          method: 'POST',
          path: `${pr07NotificationFixturePath}/task`,
          status: response.status
        });
        expect(response.status, response.text).toBe(200);
        expect(response.csrfHeaderPresent).toBe(true);
        const body = parseJson(response.text) as Record<string, unknown>;
        const notificationId = String(body.notificationId ?? '');
        const eventId = String(body.eventId ?? '');
        const fixtureProjectId = String(body.projectId ?? '');
        const taskId = String(body.taskId ?? '');
        expect(notificationId).toMatch(/^[0-9a-f-]{36}$/i);
        expect(eventId).toMatch(/^[0-9a-f-]{36}$/i);
        expect(fixtureProjectId).toBe(projectId);
        expect(taskId).toMatch(/^[0-9a-f-]{36}$/i);
        expect(Number(body.dispatchDelaySeconds)).toBe(dispatchDelaySeconds);
        return { notificationId, eventId, taskId };
      };

      const immediate = await stageNotification(0);
      const immediateItem = page.locator('app-notification-item', { hasText: pr07NotificationTitle }).first();
      await expect(immediateItem).toBeVisible({ timeout: 30_000 });
      await expect(immediateItem.getByTestId('notification-target-link')).toBeVisible();

      const openResponse = waitForApiResponse(page, 'POST', `/api/notifications/${immediate.notificationId}/open`);
      const expectedTaskUrl = new RegExp(`/app/projects/${projectId}/tasks/${immediate.taskId}$`);
      const taskNavigation = page.waitForURL(expectedTaskUrl);
      await immediateItem.getByTestId('notification-target-link').click();
      const opened = await openResponse;
      const openedText = await opened.text();
      const openedBody = parseJson(openedText) as Record<string, unknown>;
      evidence.steps.push({
        name: 'pr07-open-authorized-task-notification',
        method: 'POST',
        path: `/api/notifications/${immediate.notificationId}/open`,
        status: opened.status(),
        bodyPreview: preview(openedText)
      });
      expect(opened.status(), openedText).toBe(200);
      expect(openedBody.outcome).toBe('Opened');
      expect(openedBody.route).toBe(`/projects/${projectId}/tasks/${immediate.taskId}`);
      expect(openedBody.context).toMatchObject({ workspaceId });
      await taskNavigation;
      await expect(page.getByRole('heading', { name: pr07TaskTitle })).toBeVisible();

      // Keep a visible protected notification in the real RightPanel while
      // access changes; the next notification is deliberately held in the
      // existing outbox until after the membership revocation commits.
      await page.goto('/app/workspaces');
      if (await page.getByTestId('right-panel-open').count()) {
        await page.getByTestId('right-panel-open').click();
      }
      await expect(page.locator('app-notification-item', { hasText: pr07NotificationTitle })).toHaveCount(1);
      const delayed = await stageNotification(8);

      const protectedNotificationCleared = expect(
        page.locator('app-notification-item', { hasText: pr07NotificationTitle })
      ).toHaveCount(0, { timeout: 30_000 });
      const revoke = await requestWithCsrf(
        ownerPage,
        'DELETE',
        `/api/projects/${projectId}/members/${recipientUserId}`
      );
      evidence.steps.push({
        name: 'pr07-project-membership-revoked-before-delayed-delivery',
        method: 'DELETE',
        path: `/api/projects/${projectId}/members/${recipientUserId}`,
        status: revoke.status
      });
      expect(revoke.status, revoke.text).toBe(200);
      expect(revoke.csrfHeaderPresent).toBe(true);
      await protectedNotificationCleared;

      const revokedNotificationOpenPath = `/api/notifications/${delayed.notificationId}/open`;
      const unavailable = await requestWithCsrf(
        page,
        'POST',
        revokedNotificationOpenPath
      );
      const expectedRevokedNotificationDenial: SmokeFailedApiResponse = {
        method: 'POST',
        path: revokedNotificationOpenPath,
        status: 404
      };
      const unavailableBody = parseJson(unavailable.text) as Record<string, unknown>;
      evidence.steps.push({
        name: 'pr07-open-revoked-task-notification-is-unavailable',
        method: 'POST',
        path: revokedNotificationOpenPath,
        status: unavailable.status,
        bodyPreview: preview(unavailable.text)
      });
      expect(unavailable.status, unavailable.text).toBe(404);
      expect(unavailableBody).toMatchObject({ outcome: 'Unavailable', route: null });
      expect(unavailable.text).not.toContain(pr07TaskTitle);
      expect(unavailable.text).not.toContain(projectId);

      const hidden = await fetchJsonFromPage(page, '/api/notifications?page=1&pageSize=100');
      evidence.steps.push({
        name: 'pr07-revoked-task-notifications-are-hidden-from-list',
        method: 'GET',
        path: '/api/notifications',
        status: hidden.status,
        bodyPreview: preview(JSON.stringify(hidden.body))
      });
      expect(hidden.status).toBe(200);
      expect(Array.isArray(hidden.body?.items)).toBe(true);
      expect(hidden.body.items.some((item: Record<string, unknown>) =>
        item.id === immediate.notificationId || item.id === delayed.notificationId)).toBe(false);

      await expect.poll(async () => {
        const status = await fetchJsonFromPage(ownerPage, `${pr07NotificationFixturePath}/events/${delayed.eventId}`);
        return status.status === 200 ? status.body : { status: `HTTP ${status.status}` };
      }, {
        message: 'the delayed task notification is terminally suppressed after current authorization is revoked',
        timeout: 35_000
      }).toMatchObject({
        eventId: delayed.eventId,
        status: 'Delivered',
        attemptCount: 0,
        outcomeCode: 'NoAuthorizedRecipient'
      });
      await expect(page.locator('app-notification-item', { hasText: pr07NotificationTitle })).toHaveCount(0);

      expect(ownerEvidence.pageErrors, 'PR07-D owner browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(ownerEvidence);
      expectUnexpectedApiFailures(ownerEvidence);
      expect(evidence.pageErrors, 'PR07-D recipient browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence, [expectedRevokedNotificationDenial]);
      expectUnexpectedApiFailures(evidence, [expectedRevokedNotificationDenial]);
    } finally {
      await ownerContext?.close();
      await testInfo.attach('task-v1-pr07d-real-backend-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('Issue #378 queues durable immediate delivery, worker-publishes after reauthorization, and retains a revoked audience', async ({ page, browser }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''),
      email: smokeEmail,
      steps: [],
      pageErrors: [],
      consoleErrors: [],
      failedApiResponses: []
    };
    const immediateTitle = `Issue 378 immediate publication ${randomUUID().slice(0, 8)}`;
    const immediateBody = 'This synthetic announcement proves the confirmed immediate publication path.';
    const revokedWorkspaceName = `Issue 378 revoked audience ${randomUUID().slice(0, 8)}`;
    const revokedTitle = `Issue 378 revoked publication ${randomUUID().slice(0, 8)}`;
    const revokedBody = 'This due draft must not publish after its author loses audience authorization.';
    let temporaryWorkspaceId: string | null = null;
    let temporaryWorkspaceMembershipRevoked = false;
    let staleContext: Awaited<ReturnType<typeof browser.newContext>> | null = null;
    let stalePage: Page | null = null;
    let staleEvidence: SmokeEvidence | null = null;
    let managerContext: Awaited<ReturnType<typeof browser.newContext>> | null = null;
    let managerPage: Page | null = null;
    let managerEvidence: SmokeEvidence | null = null;

    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => { if (message.type() === 'error') {evidence.consoleErrors.push(message.text());} });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(page, evidence);
      const currentUserId = evidence.userId!;
      expect(currentUserId, 'authenticated smoke user id').toMatch(/^[0-9a-f-]{36}$/i);

      const audienceOptions = await recordFetchJson(page, evidence, 'issue-378-authorized-audiences', '/api/announcements/audiences', {
        validate: (body) => Array.isArray(body) && body.some((candidate: unknown) =>
          isWorkspaceAnnouncementAudience(candidate, smokeWorkspaceName))
      }) as readonly unknown[];
      const primaryAudience = audienceOptions.find((candidate) =>
        isWorkspaceAnnouncementAudience(candidate, smokeWorkspaceName));
      expect(primaryAudience, 'seeded Workspace is a server-authorized announcement audience').toBeTruthy();

      const primaryWorkspaceId = primaryAudience!.workspaceId;
      const primaryAudienceKey = primaryAudience!.key;
      const primaryRecipientCount = primaryAudience!.estimatedRecipientCount;

      await page.goto('/app/announcements');
      await expect(page.getByTestId('announcements-page')).toBeVisible();
      await expect(page.getByTestId('create-announcement-action')).toBeVisible();
      await page.getByTestId('create-announcement-action').click();

      const editor = page.getByTestId('announcement-editor');
      await expect(editor).toBeVisible();
      await editor.getByTestId('announcement-editor-title').fill(immediateTitle);
      await editor.getByTestId('announcement-editor-body').fill(immediateBody);
      await editor.getByTestId('announcement-next-step').click();
      await editor.getByTestId('announcement-editor-audience').selectOption(primaryAudienceKey);
      await editor.getByTestId('announcement-next-step').click();
      await editor.getByTestId('announcement-editor-priority').selectOption('critical');
      await editor.locator('#announcement-read-confirmation').check();
      await editor.getByTestId('announcement-next-step').click();

      let directBrowserAnnouncementPosts = 0;
      const observeDirectBrowserAnnouncementPosts = (request: { method(): string; url(): string }) => {
        if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/announcements') {
          directBrowserAnnouncementPosts += 1;
        }
      };
      page.on('request', observeDirectBrowserAnnouncementPosts);
      await editor.getByTestId('announcement-publish-action').click();
      const confirmationDialog = page.getByRole('dialog', { name: 'Confirm delivery' });
      await expect(confirmationDialog).toBeVisible();
      await expect(confirmationDialog).toHaveAttribute('aria-modal', 'true');
      await expect(confirmationDialog.getByTestId('announcement-confirmation-title')).toHaveText(immediateTitle);
      await expect(confirmationDialog.getByTestId('announcement-confirmation-body')).toHaveText(immediateBody);
      await expect(confirmationDialog.getByTestId('announcement-confirmation-audience')).toHaveText(smokeWorkspaceName);
      await expect(confirmationDialog.getByTestId('announcement-confirmation-recipient-count'))
        .toContainText(String(primaryRecipientCount));
      await expect(confirmationDialog.getByTestId('announcement-confirmation-priority')).toHaveText('CRITICAL');
      await expect(confirmationDialog.getByTestId('announcement-confirmation-delivery')).toHaveText('Publish immediately');
      expect(directBrowserAnnouncementPosts, 'review opens before any direct Announcement creation request').toBe(0);

      const immediateDraftCreateResponse = waitForApiResponse(page, 'POST', '/api/announcement-drafts');
      const immediateDraftPublishResponse = waitForApiResponse(page, 'POST', /\/api\/announcement-drafts\/[0-9a-f-]+\/publish$/i);
      await confirmationDialog.getByRole('button', {
        name: new RegExp(`^Publish to ${primaryRecipientCount} recipients now$`)
      }).click();
      const immediateDraftCreate = await immediateDraftCreateResponse;
      const immediateCreateRequest = immediateDraftCreate.request().postDataJSON() as Record<string, unknown>;
      const immediateCreateHeaders = await immediateDraftCreate.request().allHeaders();
      expect(immediateDraftCreate.status(), 'immediate durable draft creation response').toBe(201);
      expect(immediateCreateHeaders['x-csrf-token'], 'immediate durable draft creation CSRF header').toBeTruthy();
      expect(immediateCreateHeaders['idempotency-key'], 'immediate durable draft creation idempotency header')
        .toMatch(/^announcement-draft-create-/);
      expect(immediateCreateRequest, 'immediate durable draft creation request').toEqual({
        content: {
          target: {
            workspaceId: primaryWorkspaceId,
            groupId: null,
            channelId: null
          },
          title: immediateTitle,
          body: immediateBody,
          priority: 2,
          isPinned: false,
          requiresReadConfirmation: true,
          expiresAt: null
        }
      });
      const immediateCreatedDraft = await recordOkJson(immediateDraftCreate, evidence, 'issue-378-immediate-draft-created', (body) =>
        hasString(body, 'id') &&
        body.status === 'Draft' &&
        body.version === 1 &&
        hasStringValue(body, 'title', immediateTitle) &&
        hasStringValue(body, 'body', immediateBody) &&
        body.priority === 2 &&
        body.workspaceId === primaryWorkspaceId &&
        body.groupId === null &&
        body.channelId === null &&
        body.requiresReadConfirmation === true
      ) as Record<string, unknown>;
      const immediateDraftId = String(immediateCreatedDraft.id);
      expect(immediateDraftId, 'accepted immediate durable draft id').toMatch(/^[0-9a-f-]{36}$/i);

      const immediateDraftPublish = await immediateDraftPublishResponse;
      const immediatePublishRequest = immediateDraftPublish.request().postDataJSON() as Record<string, unknown>;
      const immediatePublishHeaders = await immediateDraftPublish.request().allHeaders();
      expect(immediateDraftPublish.status(), 'accepted immediate durable schedule response').toBe(200);
      expect(immediatePublishHeaders['x-csrf-token'], 'accepted immediate durable schedule CSRF header').toBeTruthy();
      expect(immediatePublishHeaders['idempotency-key'], 'accepted immediate durable schedule idempotency header')
        .toMatch(/^announcement-draft-transition-/);
      expect(immediatePublishRequest, 'accepted immediate durable schedule request').toEqual({ expectedVersion: 1 });
      await recordOkJson(immediateDraftPublish, evidence, 'issue-378-immediate-draft-queued', (body) =>
        hasStringValue(body, 'id', immediateDraftId) &&
        body.status === 'Scheduled' &&
        body.version === 2 &&
        hasString(body, 'scheduledForUtc') &&
        hasStringValue(body, 'scheduleTimeZoneId', 'UTC') &&
        body.publishedAnnouncementId === null
      );
      await expect(confirmationDialog).toHaveCount(0);
      await expect(page.getByText(
        'Publication queued. It will appear after the server reauthorizes the audience and publishes it.',
        { exact: true }
      )).toBeVisible();

      let immediatePublishedDraft: Record<string, unknown> | null = null;
      await expect.poll(async () => {
        const response = await fetchJsonFromPage(page, `/api/announcement-drafts/${immediateDraftId}`);
        if (response.status !== 200 || !response.body || typeof response.body !== 'object') {
          return { status: `HTTP ${response.status}` };
        }

        immediatePublishedDraft = response.body as Record<string, unknown>;
        return {
          status: immediatePublishedDraft.status,
          publishedAnnouncementId: immediatePublishedDraft.publishedAnnouncementId
        };
      }, {
        message: 'the server-owned worker advances the durable immediate schedule to Published',
        timeout: 15_000
      }).toMatchObject({
        status: 'Published',
        publishedAnnouncementId: expect.stringMatching(/^[0-9a-f-]{36}$/i)
      });
      const immediateAnnouncementId = String(immediatePublishedDraft?.publishedAnnouncementId ?? '');
      expect(immediateAnnouncementId, 'worker-created announcement id').toMatch(/^[0-9a-f-]{36}$/i);
      await recordFetchJson(page, evidence, 'issue-378-worker-published-draft-persists', `/api/announcement-drafts/${immediateDraftId}`, {
        validate: (body) =>
          hasStringValue(body, 'id', immediateDraftId) &&
          body.status === 'Published' &&
          hasStringValue(body, 'publishedAnnouncementId', immediateAnnouncementId) &&
          hasStringValue(body, 'title', immediateTitle) &&
          hasStringValue(body, 'body', immediateBody)
      });
      await recordFetchJson(page, evidence, 'issue-378-worker-created-announcement-persists', `/api/announcements/${immediateAnnouncementId}`, {
        validate: (body) =>
          hasStringValue(body, 'id', immediateAnnouncementId) &&
          hasStringValue(body, 'title', immediateTitle) &&
          hasStringValue(body, 'body', immediateBody) &&
          body.priority === 2 &&
          body.workspaceId === primaryWorkspaceId &&
          body.requiresReadConfirmation === true
      });
      page.off('request', observeDirectBrowserAnnouncementPosts);
      expect(directBrowserAnnouncementPosts, 'the browser never creates an Announcement directly').toBe(0);

      // The revocation proof must model a stale browser without suppressing
      // the production fail-closed authorization invalidation on connected
      // clients. This isolated context rejects only the Hub transport; every
      // HTTP request below (including CSRF, DELETE, POST, and GET) is real.
      staleContext = await browser.newContext({ baseURL: evidence.baseURL });
      await staleContext.addInitScript(() => {
        const nativeFetch = window.fetch.bind(window);
        window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
          const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
          if (new URL(url, window.location.href).pathname.startsWith('/hubs/app')) {
            return Promise.reject(new TypeError('Synthetic Issue 378 Hub unavailability'));
          }
          return nativeFetch(input, init);
        };
      });
      stalePage = await staleContext.newPage();
      staleEvidence = {
        baseURL: evidence.baseURL,
        email: smokeEmail,
        steps: [],
        pageErrors: [],
        consoleErrors: [],
        failedApiResponses: [],
      };
      stalePage.on('pageerror', (error) => staleEvidence!.pageErrors.push(error.message));
      stalePage.on('console', (message) => {
        if (message.type() === 'error') {staleEvidence!.consoleErrors.push(message.text());}
      });
      stalePage.on('response', (response) => recordFailedApiResponse(response, staleEvidence!));

      await loginAndVerifySession(stalePage, staleEvidence);
      await expect(stalePage.getByTestId('realtime-connection-state')).toContainText('Realtime updates are delayed');
      const staleCurrentUserId = staleEvidence.userId!;
      expect(staleCurrentUserId, 'stale-review context preserves the authenticated user').toBe(currentUserId);

      const temporaryWorkspace = await requestWithCsrf(
        stalePage,
        'POST',
        '/api/workspaces',
        {
          name: revokedWorkspaceName,
          description: 'Synthetic isolated audience for Issue #378 authorization revalidation.'
        },
        { 'Idempotency-Key': randomUUID() }
      );
      staleEvidence.steps.push({
        name: 'issue-378-create-isolated-revocation-workspace',
        method: 'POST',
        path: '/api/workspaces',
        status: temporaryWorkspace.status
      });
      expect(temporaryWorkspace.status, temporaryWorkspace.text).toBe(201);
      expect(temporaryWorkspace.csrfHeaderPresent, 'temporary Workspace create CSRF header').toBe(true);
      const temporaryWorkspaceBody = parseJson(temporaryWorkspace.text) as Record<string, any>;
      temporaryWorkspaceId = String(temporaryWorkspaceBody?.data?.id ?? '');
      expect(temporaryWorkspaceId, 'temporary Workspace id').toMatch(/^[0-9a-f-]{36}$/i);

      const audiencesAfterWorkspaceCreate = await recordFetchJson(
        stalePage,
        staleEvidence,
        'issue-378-authorized-audiences-after-workspace-create',
        '/api/announcements/audiences',
        {
          validate: (body) => Array.isArray(body) && body.some((candidate: unknown) =>
            isWorkspaceAnnouncementAudience(candidate, revokedWorkspaceName))
        }
      ) as readonly unknown[];
      const revokedAudience = audiencesAfterWorkspaceCreate.find((candidate) =>
        isWorkspaceAnnouncementAudience(candidate, revokedWorkspaceName));
      expect(revokedAudience, 'newly created Workspace is initially a server-authorized audience').toBeTruthy();
      expect(revokedAudience!.workspaceId).toBe(temporaryWorkspaceId);

      // Keep a second, currently authorized administrator in the isolated
      // Workspace. After the author loses access, this observer can prove
      // that the due worker did not create an Announcement for the retained
      // target. No browser mock or worker shortcut is involved.
      managerContext = await browser.newContext({ baseURL: evidence.baseURL });
      managerPage = await managerContext.newPage();
      managerEvidence = {
        baseURL: evidence.baseURL,
        email: pr05ManagerEmail,
        steps: [],
        pageErrors: [],
        consoleErrors: [],
        failedApiResponses: []
      };
      managerPage.on('pageerror', (error) => managerEvidence!.pageErrors.push(error.message));
      managerPage.on('console', (message) => {
        if (message.type() === 'error') {managerEvidence!.consoleErrors.push(message.text());}
      });
      managerPage.on('response', (response) => recordFailedApiResponse(response, managerEvidence!));
      await loginAndVerifySession(managerPage, managerEvidence, {
        email: pr05ManagerEmail,
        password: smokePassword
      });
      const managerUserId = managerEvidence.userId!;
      expect(managerUserId, 'authorized reauthorization observer id').toMatch(/^[0-9a-f-]{36}$/i);

      const addManager = await requestWithCsrf(
        stalePage,
        'POST',
        `/api/workspaces/${temporaryWorkspaceId}/members`,
        { userId: managerUserId, role: 1 }
      );
      staleEvidence.steps.push({
        name: 'issue-378-add-authorized-revocation-observer',
        method: 'POST',
        path: `/api/workspaces/${temporaryWorkspaceId}/members`,
        status: addManager.status
      });
      expect(addManager.status, addManager.text).toBe(200);
      expect(addManager.csrfHeaderPresent, 'authorized observer add CSRF header').toBe(true);

      const revokedDraftCreate = await requestWithCsrf(
        stalePage,
        'POST',
        '/api/announcement-drafts',
        {
          content: {
            target: {
              workspaceId: temporaryWorkspaceId,
              groupId: null,
              channelId: null
            },
            title: revokedTitle,
            body: revokedBody,
            priority: 0,
            isPinned: false,
            requiresReadConfirmation: false,
            expiresAt: null
          }
        },
        { 'Idempotency-Key': `issue-378-revoked-create-${randomUUID()}` }
      );
      staleEvidence.steps.push({
        name: 'issue-378-create-authorized-durable-revocation-draft',
        method: 'POST',
        path: '/api/announcement-drafts',
        status: revokedDraftCreate.status
      });
      expect(revokedDraftCreate.status, revokedDraftCreate.text).toBe(201);
      expect(revokedDraftCreate.csrfHeaderPresent, 'authorized revocation draft create CSRF header').toBe(true);
      const revokedDraftCreatedBody = parseJson(revokedDraftCreate.text) as Record<string, unknown>;
      const revokedDraftId = String(revokedDraftCreatedBody.id ?? '');
      const revokedDraftVersion = Number(revokedDraftCreatedBody.version);
      expect(revokedDraftId, 'authorized durable revocation draft id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(revokedDraftCreatedBody.status).toBe('Draft');
      expect(revokedDraftVersion).toBe(1);

      // Use a real near-future UTC schedule so the worker has a committed
      // durable identity to claim after authorization is revoked. The
      // datetime is intentionally unsuffixed: the API requires an IANA
      // local wall-clock value and resolves it server-side.
      const revokedDueLocalDateTime = new Date(Date.now() + 12_000).toISOString().replace('Z', '');
      const revokedDraftSchedule = await requestWithCsrf(
        stalePage,
        'POST',
        `/api/announcement-drafts/${revokedDraftId}/schedule`,
        {
          expectedVersion: revokedDraftVersion,
          localDateTime: revokedDueLocalDateTime,
          timeZoneId: 'UTC',
          ambiguousTimeOffsetMinutes: null
        },
        { 'Idempotency-Key': `issue-378-revoked-schedule-${randomUUID()}` }
      );
      staleEvidence.steps.push({
        name: 'issue-378-schedule-authorized-durable-revocation-draft',
        method: 'POST',
        path: `/api/announcement-drafts/${revokedDraftId}/schedule`,
        status: revokedDraftSchedule.status
      });
      expect(revokedDraftSchedule.status, revokedDraftSchedule.text).toBe(200);
      expect(revokedDraftSchedule.csrfHeaderPresent, 'authorized revocation draft schedule CSRF header').toBe(true);
      const revokedDraftScheduledBody = parseJson(revokedDraftSchedule.text) as Record<string, unknown>;
      expect(revokedDraftScheduledBody).toMatchObject({
        id: revokedDraftId,
        version: 2,
        status: 'Scheduled',
        scheduleTimeZoneId: 'UTC'
      });
      expect(typeof revokedDraftScheduledBody.scheduledForUtc, 'accepted revocation due UTC instant').toBe('string');

      const membershipRevocation = await requestWithCsrf(
        stalePage,
        'DELETE',
        `/api/workspaces/${temporaryWorkspaceId}/members/${staleCurrentUserId}`
      );
      staleEvidence.steps.push({
        name: 'issue-378-revoke-selected-workspace-audience-before-due-publication',
        method: 'DELETE',
        path: `/api/workspaces/${temporaryWorkspaceId}/members/${staleCurrentUserId}`,
        status: membershipRevocation.status
      });
      expect(membershipRevocation.status, membershipRevocation.text).toBe(200);
      expect(membershipRevocation.csrfHeaderPresent, 'selected audience revocation CSRF header').toBe(true);
      temporaryWorkspaceMembershipRevoked = true;

      const revokedDraftRead = await fetchFromPage(stalePage, `/api/announcement-drafts/${revokedDraftId}`);
      staleEvidence.steps.push({
        name: 'issue-378-revoked-author-draft-read-is-redacted',
        method: 'GET',
        path: `/api/announcement-drafts/${revokedDraftId}`,
        status: revokedDraftRead.status,
        bodyPreview: preview(revokedDraftRead.text)
      });
      expect(revokedDraftRead.status, revokedDraftRead.text).toBe(400);
      expect(revokedDraftRead.text, 'revoked draft read must not disclose the selected Workspace or retained content')
        .not.toContain(revokedWorkspaceName);
      expect(revokedDraftRead.text, 'revoked draft read must not disclose the retained title').not.toContain(revokedTitle);

      const revokedDueNotBefore = Date.now() + 13_000;
      await expect.poll(async () => {
        if (Date.now() < revokedDueNotBefore) {
          return 'waiting-for-due-worker-reauthorization';
        }

        const response = await fetchJsonFromPage(managerPage!, '/api/announcements?page=1&pageSize=100');
        if (response.status !== 200 || !isPagedResponse(response.body)) {
          return `HTTP ${response.status}`;
        }
        return response.body.items.some((item) => hasStringValue(item, 'title', revokedTitle))
          ? 'published-after-revocation'
          : 'not-published-after-revocation';
      }, {
        message: 'the due worker reauthorizes the retained target and does not publish after its author is revoked',
        timeout: 25_000
      }).toBe('not-published-after-revocation');
      await recordFetchJson(
        managerPage!,
        managerEvidence!,
        'issue-378-manager-confirms-revoked-draft-produced-no-announcement',
        '/api/announcements?page=1&pageSize=100',
        {
          validate: (body) =>
            isPagedResponse(body) &&
            !body.items.some((item) => hasStringValue(item, 'title', revokedTitle))
        }
      );

      const expectedRevokedDraftReadFailure: SmokeFailedApiResponse = {
        method: 'GET',
        path: `/api/announcement-drafts/${revokedDraftId}`,
        status: 400
      };

      expect(evidence.pageErrors, 'Issue #378 durable immediate browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence);
      expectUnexpectedApiFailures(evidence);
      expect(staleEvidence.pageErrors, 'Issue #378 revoked-author browser page errors').toEqual([]);
      expectOnlyExpectedSyntheticHubConsoleErrors(staleEvidence, [expectedRevokedDraftReadFailure]);
      expectUnexpectedApiFailures(staleEvidence, [expectedRevokedDraftReadFailure]);
      expect(managerEvidence!.pageErrors, 'Issue #378 reauthorization-observer browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(managerEvidence!);
      expectUnexpectedApiFailures(managerEvidence!);
    } finally {
      // The temporary target must not affect later shared-fixture tests. Once
      // the author is revoked, its still-authorized administrator performs
      // the cleanup; otherwise the original author can safely archive it.
      if (temporaryWorkspaceId) {
        const cleanupPage = managerPage ?? (!temporaryWorkspaceMembershipRevoked ? stalePage : null);
        const cleanupEvidence = managerEvidence ?? staleEvidence;
        if (cleanupPage) {
          try {
            const cleanup = await requestWithCsrf(cleanupPage, 'POST', `/api/workspaces/${temporaryWorkspaceId}/archive`);
            cleanupEvidence?.steps.push({
              name: 'issue-378-temporary-revocation-workspace-cleanup',
              method: 'POST',
              path: `/api/workspaces/${temporaryWorkspaceId}/archive`,
              status: cleanup.status,
            });
          } catch {
            cleanupEvidence?.steps.push({
              name: 'issue-378-temporary-revocation-workspace-cleanup',
              method: 'POST',
              path: `/api/workspaces/${temporaryWorkspaceId}/archive`,
              status: 0,
            });
          }
        }
      }
      await staleContext?.close();
      await managerContext?.close();
      if (staleEvidence) {
        await testInfo.attach('issue-378-announcement-revoked-author-real-backend-evidence.json', {
          body: JSON.stringify(staleEvidence, null, 2),
          contentType: 'application/json'
        });
      }
      if (managerEvidence) {
        await testInfo.attach('issue-378-announcement-reauthorization-observer-real-backend-evidence.json', {
          body: JSON.stringify(managerEvidence, null, 2),
          contentType: 'application/json'
        });
      }
      await testInfo.attach('issue-378-announcement-publication-real-backend-evidence.json', {
        body: JSON.stringify(evidence, null, 2),
        contentType: 'application/json'
      });
    }
  });

  test('TASK-V1-PR03C uses the real backend for task detail, mutations, revocation, and File grant reauthorization', async ({ page }, testInfo) => {
    const evidence: SmokeEvidence = {
      baseURL: String(testInfo.project.use.baseURL ?? ''), email: smokeEmail, steps: [], pageErrors: [], consoleErrors: [], failedApiResponses: []
    };
    page.on('pageerror', (error) => evidence.pageErrors.push(error.message));
    page.on('console', (message) => { if (message.type() === 'error') {evidence.consoleErrors.push(message.text());} });
    page.on('response', (response) => recordFailedApiResponse(response, evidence));

    try {
      await loginAndVerifySession(page, evidence);
      await openPr03cTaskDetail(page, evidence);
      const taskId = evidence.taskId!;
      const projectId = evidence.projectId!;
      const workspaceId = evidence.workspaceId!;
      const userId = evidence.userId!;

      const detail = await recordFetchJson(page, evidence, 'pr03c-task-detail-aggregate', `/api/tasks/${taskId}`, {
        validate: (body) => isPr03cTaskDetail(body, taskId, projectId)
      });
      const detailRecord = detail as Record<string, any>;
      const attachmentId = String(detailRecord.files.items[0].id);
      expect(JSON.stringify(detail), 'task detail must not disclose storage or grant secrets').not.toMatch(/storageKey|filePath|tokenHash|signedUrl|internal\/task-file/i);

      await expect(page.getByTestId('task-detail-version')).not.toHaveText('0');
      await expect(page.getByRole('heading', { name: 'Subtasks' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Checklist' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Comments' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Labels' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Watch' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Files' })).toBeVisible();
      await expect(page.getByText(smokeTaskLabelName, { exact: true }).first()).toBeVisible();
      await expect(page.getByText('Not watching', { exact: true })).toBeVisible();
      await expect(page.getByText(smokeTaskFileName, { exact: true })).toBeVisible();
      await expect(page.getByText('Access: Available', { exact: true })).toBeVisible();

      const grant = await requestWithCsrf(page, 'POST', `/api/attachments/${attachmentId}/download-grants`, { purpose: 'pr03c-acceptance' });
      evidence.steps.push({ name: 'pr03c-file-grant-before-revocation', method: 'POST', path: `/api/attachments/${attachmentId}/download-grants`, status: grant.status, bodyPreview: '[redacted]' });
      expect(grant.status, 'authorized actor receives a fresh Attachment download grant').toBe(200);
      const grantBody = parseJson(grant.text) as Record<string, unknown>;
      const grantId = String(grantBody.fileDownloadGrantId ?? '');
      const grantToken = String(grantBody.token ?? '');
      expect(grantId, 'download grant id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(grantToken, 'download grant token').not.toBe('');
      const browserStorage = await page.evaluate(() => JSON.stringify({
        local: { ...localStorage },
        session: { ...sessionStorage }
      }));
      expect(browserStorage, 'download grant token must remain ephemeral').not.toContain(grantToken);

      const directOpen = await fetchBinaryFromPage(page, `/api/attachments/${attachmentId}/download`);
      evidence.steps.push({ name: 'pr03c-file-open-before-revocation', method: 'GET', path: `/api/attachments/${attachmentId}/download`, status: directOpen.status });
      expect(directOpen.status, 'authorized actor can open the physical synthetic Attachment').toBe(200);
      expect(directOpen.text, 'authorized direct open returns the seeded bytes').toBe('Synthetic PR03C browser smoke file.\n');

      const grantedDownload = await requestBinaryWithCsrf(page, `/api/attachment-download-grants/${grantId}/download`, { token: grantToken });
      evidence.steps.push({ name: 'pr03c-file-grant-download-before-revocation', method: 'POST', path: `/api/attachment-download-grants/${grantId}/download`, status: grantedDownload.status });
      expect(grantedDownload.status, 'fresh grant downloads the physical synthetic Attachment').toBe(200);
      expect(grantedDownload.text, 'authorized grant returns the seeded bytes').toBe('Synthetic PR03C browser smoke file.\n');

      const retainedGrant = await requestWithCsrf(page, 'POST', `/api/attachments/${attachmentId}/download-grants`, { purpose: 'pr03c-revocation-probe' });
      expect(retainedGrant.status, 'revocation probe receives a fresh one-time grant').toBe(200);
      const retainedGrantBody = parseJson(retainedGrant.text) as Record<string, unknown>;
      const retainedGrantId = String(retainedGrantBody.fileDownloadGrantId ?? '');
      const retainedGrantToken = String(retainedGrantBody.token ?? '');
      expect(retainedGrantId, 'retained download grant id').toMatch(/^[0-9a-f-]{36}$/i);
      expect(retainedGrantToken, 'retained download grant token').not.toBe('');

      const checklistText = `PR03C checklist ${Date.now()}`;
      const checklistResponse = waitForApiResponse(page, 'POST', `/api/tasks/${taskId}/checklist`);
      await page.locator('#checklist-text').fill(checklistText);
      await page.getByRole('button', { name: 'Add checklist item' }).click();
      await recordOkJson(await checklistResponse, evidence, 'pr03c-checklist-create', (body) => hasString(body, 'id') && hasStringValue(body, 'text', checklistText));
      await expect(page.getByText(checklistText, { exact: true })).toBeVisible();
      const checklistSection = page.locator('section[aria-labelledby="checklist-heading"]');
      const checklistDelete = waitForApiResponse(page, 'DELETE', new RegExp(`/api/tasks/${taskId}/checklist/`));
      await checklistSection.getByRole('button', { name: 'Delete' }).click();
      await recordOkJson(await checklistDelete, evidence, 'pr03c-checklist-delete', (body) => body && typeof body === 'object');
      await expect(page.getByText(checklistText, { exact: true })).toHaveCount(0);

      const mentionResponse = waitForApiResponse(page, 'GET', new RegExp(`/api/tasks/${taskId}/mention-candidates$`));
      await page.locator('#task-comment-body').fill('@Browser');
      await recordOkJson(await mentionResponse, evidence, 'pr03c-mention-candidates', (body) => Array.isArray(body) && body.some((candidate: unknown) => hasStringValue(candidate, 'displayName', smokeRecipientName)));
      await page.getByRole('button', { name: `@${smokeRecipientName}` }).click();
      const commentResponse = waitForApiResponse(page, 'POST', `/api/tasks/${taskId}/comments`);
      await page.getByRole('button', { name: 'Post comment' }).click();
      const comment = await recordOkJson(await commentResponse, evidence, 'pr03c-comment-create', (body) => hasString(body, 'id') && Array.isArray((body as Record<string, unknown>).mentions));
      const commentId = String((comment as Record<string, unknown>).id);
      await expect(page.getByText(`@${smokeRecipientName}`, { exact: true })).toBeVisible();
      const commentDelete = waitForApiResponse(page, 'DELETE', `/api/task-comments/${commentId}`);
      await page.locator('section[aria-labelledby="comments-heading"]').getByRole('button', { name: 'Delete' }).click();
      await recordOkJson(await commentDelete, evidence, 'pr03c-comment-delete', (body) => body && typeof body === 'object');

      const mismatchProjectId = '00000000-0000-0000-0000-000000000001';
      await page.goto(`/app/projects/${mismatchProjectId}/tasks/${taskId}`);
      const safeUnavailable = page.getByTestId('permission-denied-state');
      await expect(safeUnavailable).toBeVisible();
      await expect(safeUnavailable).toHaveAttribute('role', 'status');
      await expect(safeUnavailable.getByRole('heading', {
        name: 'Task detail is no longer available with your current permission.'
      })).toBeVisible();
      await expect(page.getByRole('heading', { name: smokeTaskTitle })).toHaveCount(0);
      await expect(page.getByText(smokeTaskLabelName, { exact: true })).toHaveCount(0);
      await expect(page.getByText(smokeTaskFileName, { exact: true })).toHaveCount(0);

      await page.goto(`/app/projects/${projectId}/tasks/${taskId}`);
      await expect(page.getByRole('heading', { name: smokeTaskTitle })).toBeVisible();
      const postRevocationFailureStart = evidence.failedApiResponses.length;
      const removeMembership = await requestWithCsrf(page, 'DELETE', `/api/workspaces/${workspaceId}/members/${userId}`);
      evidence.steps.push({ name: 'pr03c-workspace-membership-revoked', method: 'DELETE', path: `/api/workspaces/${workspaceId}/members/${userId}`, status: removeMembership.status });
      expect(removeMembership.status, 'test setup revokes active Workspace access through the real backend').toBe(200);

      await page.reload();
      await expect(page.getByRole('heading', { name: smokeTaskTitle })).toHaveCount(0);
      await expect(page.getByText(smokeTaskLabelName, { exact: true })).toHaveCount(0);
      await expect(page.getByText(smokeTaskFileName, { exact: true })).toHaveCount(0);
      await expect(page.locator('#task-comment-body')).toHaveCount(0);

      const revokedProjects = await fetchJsonFromPage(page, '/api/projects?page=1&pageSize=100');
      evidence.steps.push({
        name: 'pr03c-project-list-cleared-after-workspace-revocation',
        method: 'GET',
        path: '/api/projects',
        status: revokedProjects.status
      });
      expect(revokedProjects.status, 'Project list remains safely queryable after Workspace revocation').toBe(200);
      expect(isPagedResponse(revokedProjects.body), 'revoked Project list keeps the canonical page shape').toBe(true);
      expect(
        revokedProjects.body.items.some((item: Record<string, unknown>) => item.workspaceId === workspaceId),
        'ProjectMember rows must not outlive active access to their Workspace'
      ).toBe(false);
      expect(
        revokedProjects.body.items.some((item: Record<string, unknown>) => item.id === projectId),
        'the revoked Project must not remain visible'
      ).toBe(false);
      expect(JSON.stringify(revokedProjects.body), 'revoked Project list must not disclose protected Project metadata')
        .not.toMatch(/Browser Smoke Project|PR05 Browser Acceptance Project/i);

      const deniedTask = await fetchFromPage(page, `/api/tasks/${taskId}`);
      evidence.steps.push({ name: 'pr03c-task-denied-after-revocation', method: 'GET', path: `/api/tasks/${taskId}`, status: deniedTask.status, bodyPreview: preview(deniedTask.text) });
      expect(deniedTask.status, 'revoked actor must receive the canonical task safe-not-found response').toBe(404);
      expectCanonicalDenial(deniedTask.text, 'TASK_NOT_FOUND');

      const revokedMyTasks = await fetchJsonFromPage(page, '/api/me/tasks?view=assigned&scope=allWorkspaces');
      evidence.steps.push({ name: 'pr03c-my-tasks-cleared-after-revocation', method: 'GET', path: '/api/me/tasks', status: revokedMyTasks.status });
      expect(revokedMyTasks.status, 'My Tasks remains safely queryable after membership revocation').toBe(200);
      expect(revokedMyTasks.body?.items, 'revoked Workspace rows must disappear from My Tasks').toEqual([]);
      expect(revokedMyTasks.body?.totalCount, 'revoked Workspace rows must not contribute to My Tasks total').toBe(0);
      expect(revokedMyTasks.body?.availableWorkspaceCount, 'revoked Workspace must not remain available').toBe(0);

      const revokedMyTaskCounts = await fetchJsonFromPage(page, '/api/me/tasks/counts?scope=allWorkspaces');
      evidence.steps.push({ name: 'pr03c-my-task-counts-cleared-after-revocation', method: 'GET', path: '/api/me/tasks/counts', status: revokedMyTaskCounts.status });
      expect(revokedMyTaskCounts.status, 'My Tasks counts remain safely queryable after membership revocation').toBe(200);
      expect(revokedMyTaskCounts.body?.availableWorkspaceCount, 'revoked Workspace must not remain in count scope').toBe(0);
      expect(
        (revokedMyTaskCounts.body?.views ?? []).every((view: Record<string, unknown>) => view.count === 0),
        'revoked Workspace rows must not contribute to any relationship count'
      ).toBe(true);

      const deniedGrant = await requestWithCsrf(page, 'POST', `/api/attachments/${attachmentId}/download-grants`, { purpose: 'pr03c-after-revocation' });
      evidence.steps.push({ name: 'pr03c-file-grant-denied-after-revocation', method: 'POST', path: `/api/attachments/${attachmentId}/download-grants`, status: deniedGrant.status, bodyPreview: preview(deniedGrant.text) });
      expect(deniedGrant.status, 'revoked actor must receive the canonical grant safe-not-found response').toBe(404);
      expectCanonicalDenial(deniedGrant.text, 'FILE_DOWNLOAD_GRANT_NOT_FOUND');
      expect(deniedGrant.text, 'denial must not disclose the protected File metadata').not.toMatch(/browser-smoke-task|storageKey|filePath|tokenHash|internal\/task-file/i);

      const deniedOpen = await fetchBinaryFromPage(page, `/api/attachments/${attachmentId}/download`);
      evidence.steps.push({ name: 'pr03c-file-open-denied-after-revocation', method: 'GET', path: `/api/attachments/${attachmentId}/download`, status: deniedOpen.status });
      expect(deniedOpen.status, 'revoked actor cannot open the physical Attachment').toBe(400);
      expect(deniedOpen.text, 'revoked direct open must not return the seeded bytes').not.toContain('Synthetic PR03C browser smoke file.');

      const deniedRetainedGrant = await requestBinaryWithCsrf(page, `/api/attachment-download-grants/${retainedGrantId}/download`, { token: retainedGrantToken });
      evidence.steps.push({ name: 'pr03c-retained-grant-denied-after-revocation', method: 'POST', path: `/api/attachment-download-grants/${retainedGrantId}/download`, status: deniedRetainedGrant.status });
      expect(deniedRetainedGrant.status, 'membership revocation invalidates a previously issued unused grant').toBe(400);
      expect(deniedRetainedGrant.text, 'revoked retained grant must not return the seeded bytes').not.toContain('Synthetic PR03C browser smoke file.');

      // Revocation can race with stale project task-list and task execution-scope refreshes already queued by the SPA.
      // They must fail closed, and are expected only after this scenario removes Workspace access.
      const expectedRevocationRefreshFailures = selectPr03cExpectedRevocationRefreshFailures(
        evidence,
        postRevocationFailureStart,
        taskId,
      );

      expect(evidence.pageErrors, 'browser page errors').toEqual([]);
      expectUnexpectedConsoleErrors(evidence, expectedRevocationRefreshFailures);
      expectUnexpectedApiFailures(evidence, expectedRevocationRefreshFailures);
    } finally {
      await testInfo.attach('task-v1-pr03c-real-backend-evidence.json', { body: JSON.stringify(evidence, null, 2), contentType: 'application/json' });
    }
  });
});

async function loginAndVerifySession(
  page: Page,
  evidence: SmokeEvidence,
  credentials: { email: string; password: string } = { email: smokeEmail, password: smokePassword }
) {
  await page.goto('/app/login');
  await expect(page.getByTestId('login-page')).toBeVisible();
  await recordFetchJson(page, evidence, 'csrf-token', '/api/security/csrf-token', {
    sensitive: true,
    validate: (body) => hasString(body, 'token') && hasString(body, 'headerName')
  });

  await page.getByTestId('login-email').fill(credentials.email);
  await page.getByTestId('login-password').fill(credentials.password);

  const [loginResponse] = await Promise.all([
    waitForApiResponse(page, 'POST', '/api/auth/login'),
    page.getByTestId('login-submit').click()
  ]);

  const loginBody = await recordOkJson(loginResponse, evidence, 'login', (body) =>
    hasString(body, 'userId') &&
    hasStringValue(body, 'email', credentials.email) &&
    Array.isArray((body as Record<string, unknown>).workspaces)
  );
  evidence.userId = String(loginBody.userId);

  await expect(page).toHaveURL(/\/app\/workspaces$/);
  await expect(page.getByTestId('app-shell')).toBeVisible();
  await expect(page.getByTestId('nav-projects').first()).toBeVisible();
  await expect(page.getByTestId('nav-my-tasks').first()).toBeVisible();

  await recordFetchJson(page, evidence, 'auth-me', '/api/auth/me', {
    validate: (body) =>
      hasStringValue(body, 'userId', evidence.userId ?? '') &&
      hasStringValue(body, 'email', credentials.email) &&
      Array.isArray((body as Record<string, unknown>).workspaces) &&
      hasCapability(body, 'projects:view')
  });
}

async function recordPr06Command(
  response: PlaywrightResponse,
  evidence: Pr06GanttEvidence,
  name: string,
  expectedStatus: 200 | 409
): Promise<{
  response: PlaywrightResponse;
  request: Record<string, any>;
  body: any;
  text: string;
}> {
  const text = await response.text();
  const body = parseJson(text);
  const request = response.request().postData()
    ? response.request().postDataJSON() as Record<string, any>
    : {};
  const headers = await response.request().allHeaders();
  const csrfHeaderPresent = Object.entries(headers)
    .some(([headerName, value]) => headerName.toLowerCase() === 'x-csrf-token' && value.length > 0);
  const method = response.request().method();
  const path = new URL(response.url()).pathname;

  evidence.steps.push({
    name: `pr06-${name}`,
    method,
    path,
    status: response.status(),
    bodyPreview: preview(text)
  });
  expect(response.status(), `${name} response ${response.status()}: ${text}`).toBe(expectedStatus);
  expect(csrfHeaderPresent, `${name} uses the real Angular/browser CSRF token`).toBe(true);
  if (expectedStatus === 200) {
    const valid =
      (method === 'PATCH' &&
        typeof body?.taskId === 'string' &&
        typeof body?.version === 'number' &&
        Array.isArray(body?.warnings)) ||
      (method === 'POST' &&
        typeof body?.id === 'string' &&
        typeof body?.predecessorTaskId === 'string' &&
        typeof body?.successorTaskId === 'string' &&
        body?.dependencyType === 'FinishToStart') ||
      (method === 'DELETE' && body?.status === 'OK');
    expect(valid, `${name} canonical command response: ${text}`).toBe(true);
  } else {
    expect(
      body && typeof body === 'object' &&
      typeof body.requestId === 'string' &&
      body.error && typeof body.error === 'object' &&
      typeof body.error.code === 'string' &&
      Array.isArray(body.error.details),
      `${name} safe conflict response: ${text}`
    ).toBe(true);
  }

  evidence.commands.push({
    name,
    method,
    path,
    status: response.status(),
    request,
    csrfHeaderPresent
  });
  return { response, request, body, text };
}

function isPr06GanttSnapshot(body: unknown): body is Pr06GanttSnapshotDto {
  if (!body || typeof body !== 'object') {return false;}
  const snapshot = body as Record<string, any>;
  const collections = [
    snapshot.scheduledItems,
    snapshot.unscheduledItems,
    snapshot.milestones,
    snapshot.dependencies,
    snapshot.warnings
  ];
  if (
    typeof snapshot.projectId !== 'string' ||
    typeof snapshot.projectTitle !== 'string' ||
    typeof snapshot.projectVersion !== 'number' ||
    typeof snapshot.workflowVersion !== 'number' ||
    !snapshot.calendar ||
    typeof snapshot.calendar.timeZone !== 'string' ||
    !Array.isArray(snapshot.calendar.workingDays) ||
    typeof snapshot.calendar.holidaysAvailable !== 'boolean' ||
    !Array.isArray(snapshot.calendar.limitations) ||
    collections.some((collection) => !Array.isArray(collection)) ||
    !snapshot.permissions ||
    typeof snapshot.permissions.canEditSchedule !== 'boolean' ||
    typeof snapshot.permissions.canEditProgress !== 'boolean' ||
    typeof snapshot.permissions.canManageDependencies !== 'boolean' ||
    typeof snapshot.permissions.canClearSchedule !== 'boolean' ||
    typeof snapshot.permissions.canOpen !== 'boolean' ||
    typeof snapshot.maximumItems !== 'number' ||
    typeof snapshot.totalItems !== 'number'
  ) {
    return false;
  }

  const itemIsCanonical = (item: unknown) => {
    if (!item || typeof item !== 'object') {return false;}
    const value = item as Record<string, any>;
    return typeof value.taskId === 'string' &&
      (value.kind === 'Task' || value.kind === 'Milestone') &&
      typeof value.title === 'string' &&
      typeof value.progressPercent === 'number' &&
      typeof value.progressIsDerived === 'boolean' &&
      typeof value.version === 'number' &&
      value.scheduleEditPermissions &&
      typeof value.scheduleEditPermissions.canEditSchedule === 'boolean' &&
      typeof value.scheduleEditPermissions.canEditProgress === 'boolean' &&
      typeof value.scheduleEditPermissions.canManageDependencies === 'boolean' &&
      typeof value.scheduleEditPermissions.canClearSchedule === 'boolean' &&
      typeof value.scheduleEditPermissions.canOpen === 'boolean' &&
      Array.isArray(value.warnings);
  };
  const dependencyIsCanonical = (dependency: unknown) => {
    if (!dependency || typeof dependency !== 'object') {return false;}
    const value = dependency as Record<string, any>;
    return typeof value.dependencyId === 'string' &&
      typeof value.predecessorTaskId === 'string' &&
      typeof value.successorTaskId === 'string' &&
      typeof value.type === 'string' &&
      typeof value.editable === 'boolean' &&
      typeof value.version === 'number' &&
      Array.isArray(value.warnings);
  };
  return snapshot.scheduledItems.every(itemIsCanonical) &&
    snapshot.unscheduledItems.every(itemIsCanonical) &&
    snapshot.milestones.every(itemIsCanonical) &&
    snapshot.dependencies.every(dependencyIsCanonical);
}

function pr06GanttItem(snapshot: Pr06GanttSnapshotDto, title: string): Pr06GanttItemDto {
  const item = [...snapshot.scheduledItems, ...snapshot.unscheduledItems, ...snapshot.milestones]
    .find((candidate) => candidate.title === title);
  expect(item, `${title} is present in the real canonical Gantt response`).toBeTruthy();
  return item!;
}

function pr06GanttItemLocator(page: Page, taskId: string): Locator {
  return page.locator(`[data-gantt-item-id="${taskId}"]`);
}

async function expectLogicalPr06GanttFocus(item: Locator): Promise<void> {
  await expect.poll(() => item.evaluate((element) =>
    element === document.activeElement || element.contains(document.activeElement)
  )).toBe(true);
}

function taskDetailVersion(body: unknown): number | null {
  if (!body || typeof body !== 'object') {return null;}
  const task = (body as Record<string, any>).task;
  return task && typeof task.version === 'number' ? task.version : null;
}

function taskDeadlineAt(body: unknown): string | null {
  if (!body || typeof body !== 'object') {return null;}
  const task = (body as Record<string, any>).task;
  return task && typeof task.deadlineAt === 'string' ? task.deadlineAt : null;
}

function expectCanonicalGanttDenial(text: string, expectedCode: string): void {
  const body = parseJson(text) as Record<string, any>;
  expect(typeof body.requestId, 'safe Gantt denial requestId').toBe('string');
  expect(body.error?.code, 'safe Gantt denial code').toBe(expectedCode);
  expect(body.error?.redactionApplied, 'safe Gantt denial redaction marker').toBe(true);
  expect(Array.isArray(body.error?.details), 'safe Gantt denial details').toBe(true);
  expect(text, 'safe Gantt denial must not expose protected schedule metadata').not.toMatch(
    /tenantId|workspaceId|PR06 (?:derived|schedule|predecessor|unscheduled|conflict|dependency|release)|browser-smoke-pr06-gantt|DeadlineAt|SQL/i
  );
}

async function recordPr05KanbanCommand(
  response: PlaywrightResponse,
  evidence: Pr05KanbanEvidence,
  name: string,
  expectedStatus: 200 | 409
): Promise<{
  response: PlaywrightResponse;
  request: Record<string, any>;
  body: any;
}> {
  const text = await response.text();
  const body = parseJson(text);
  const request = response.request().postDataJSON() as Record<string, any>;
  const headers = await response.request().allHeaders();
  const csrfHeaderPresent = Object.entries(headers)
    .some(([headerName, value]) => headerName.toLowerCase() === 'x-csrf-token' && value.length > 0);

  evidence.steps.push({
    name: `pr05-${name}`,
    method: response.request().method(),
    path: new URL(response.url()).pathname,
    status: response.status(),
    bodyPreview: preview(text)
  });
  expect(response.status(), `${name} response ${response.status()}: ${text}`).toBe(expectedStatus);
  expect(csrfHeaderPresent, `${name} uses the Angular CSRF interceptor`).toBe(true);
  if (expectedStatus === 200) {
    expect(isPr05KanbanCommandResponse(body), `${name} authoritative command response: ${text}`).toBe(true);
  } else {
    expect(
      body && typeof body === 'object' &&
      typeof body.requestId === 'string' &&
      body.error && typeof body.error === 'object' &&
      typeof body.error.code === 'string',
      `${name} safe conflict response: ${text}`
    ).toBe(true);
  }

  const authoritativeBoardVersion =
    expectedStatus === 200 && isPr05KanbanCommandResponse(body)
      ? body.snapshot.board.version
      : undefined;
  evidence.commands.push({
    name,
    responseUrl: response.url(),
    status: response.status(),
    taskId: new URL(response.url()).pathname.split('/')[3] ?? '',
    expectedTaskVersion: Number(request.expectedTaskVersion),
    expectedBoardVersion: Number(request.expectedBoardVersion),
    authoritativeBoardVersion,
    targetWorkflowStageId: String(request.targetWorkflowStageId),
    targetBeforeTaskId: request.targetBeforeTaskId === null ? null : String(request.targetBeforeTaskId),
    targetAfterTaskId: request.targetAfterTaskId === null ? null : String(request.targetAfterTaskId),
    reason: typeof request.reason === 'string' ? request.reason : null,
    csrfHeaderPresent
  });
  return { response, request, body };
}

function isPr05KanbanCommandResponse(body: unknown): body is Pr05KanbanCommandResponseDto {
  if (!body || typeof body !== 'object') {return false;}
  const response = body as Record<string, unknown>;
  return isPr05KanbanSnapshot(response.snapshot) &&
    (response.focusTaskId === null || typeof response.focusTaskId === 'string') &&
    Array.isArray(response.warnings);
}

function isPr05KanbanSnapshot(body: unknown): body is Pr05KanbanSnapshotDto {
  if (!body || typeof body !== 'object') {return false;}
  const snapshot = body as Record<string, any>;
  if (
    !snapshot.board ||
    typeof snapshot.board !== 'object' ||
    typeof snapshot.board.projectId !== 'string' ||
    typeof snapshot.board.version !== 'number' ||
    typeof snapshot.board.totalAuthorizedCardCount !== 'number' ||
    typeof snapshot.board.isTruncated !== 'boolean' ||
    typeof snapshot.board.uiPermissions?.canConfigure !== 'boolean' ||
    !Array.isArray(snapshot.board.warnings) ||
    !Array.isArray(snapshot.columns) ||
    !Array.isArray(snapshot.cards)
  ) {
    return false;
  }

  return snapshot.columns.every((column: unknown) => {
    if (!column || typeof column !== 'object') {return false;}
    const value = column as Record<string, any>;
    return typeof value.workflowStageId === 'string' &&
      typeof value.displayName === 'string' &&
      typeof value.category !== 'undefined' &&
      typeof value.displayOrder === 'number' &&
      typeof value.currentAuthorizedCardCount === 'number' &&
      typeof value.hasWipWarning === 'boolean' &&
      typeof value.uiPermissions?.canConfigure === 'boolean';
  }) && snapshot.cards.every((card: unknown) => {
    if (!card || typeof card !== 'object') {return false;}
    const value = card as Record<string, any>;
    return typeof value.taskId === 'string' &&
      typeof value.summary === 'string' &&
      typeof value.workflowStageId === 'string' &&
      typeof value.boardOrder === 'number' &&
      typeof value.version === 'number' &&
      typeof value.uiPermissions?.canOpen === 'boolean' &&
      typeof value.uiPermissions?.canMove === 'boolean' &&
      Array.isArray(value.uiPermissions?.allowedTargetWorkflowStageIds);
  });
}

function pr05Stage(snapshot: Pr05KanbanSnapshotDto, displayName: 'Todo' | 'Done' | 'Cancelled'): Pr05KanbanColumnDto {
  const stage = snapshot.columns.find((candidate) => candidate.displayName === displayName);
  expect(stage, `${displayName} Workflow Stage is present in the real board response`).toBeTruthy();
  return stage!;
}

function pr05Card(snapshot: Pr05KanbanSnapshotDto, title: string): Pr05KanbanCardDto {
  const card = snapshot.cards.find((candidate) => candidate.summary === title);
  expect(card, `${title} is present in the real board response`).toBeTruthy();
  return card!;
}

function pr05CardLocator(page: Page, taskId: string): Locator {
  return page.locator(`[data-kanban-card-id="${taskId}"]`);
}

function pr05ColumnLocator(page: Page, displayName: 'Todo' | 'Done' | 'Cancelled'): Locator {
  return page.locator('.coglatas-kanban__column')
    .filter({ has: page.getByRole('heading', { name: displayName, exact: true }) });
}

function pr05StageTaskIds(snapshot: Pr05KanbanSnapshotDto, workflowStageId: string): string[] {
  return snapshot.cards
    .filter((card) => card.workflowStageId === workflowStageId)
    .sort((left, right) => left.boardOrder - right.boardOrder || left.taskId.localeCompare(right.taskId))
    .map((card) => card.taskId);
}

async function expectPr05StageOrder(
  page: Page,
  snapshot: Pr05KanbanSnapshotDto,
  displayName: 'Todo' | 'Done' | 'Cancelled'
): Promise<void> {
  const stage = pr05Stage(snapshot, displayName);
  const expectedIds = pr05StageTaskIds(snapshot, stage.workflowStageId);
  const cards = pr05ColumnLocator(page, displayName).locator('[data-kanban-card-id]');
  await expect(cards).toHaveCount(expectedIds.length);
  const actualIds = await cards.evaluateAll((elements) =>
    elements.map((element) => (element as HTMLElement).dataset['kanbanCardId'] ?? '')
  );
  expect(actualIds, `${displayName} DOM order matches the authoritative boardOrder`).toEqual(expectedIds);
}

function expectCanonicalKanbanDenial(text: string, expectedCode: string): void {
  const body = parseJson(text) as Record<string, any>;
  expect(typeof body.requestId, 'safe Kanban denial requestId').toBe('string');
  expect(body.error?.code, 'safe Kanban denial error code').toBe(expectedCode);
  expect(text, 'safe Kanban denial must not expose protected board metadata').not.toMatch(
    /PR05 (?:real|stable|cancellation|stale)|tenantId|workspaceId|workflowStageId|boardVersion|sortKey|policy stamp|internal\//i
  );
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

async function verifyRealtimeTransportReconnect(page: Page, evidence: SmokeEvidence) {
  const indicator = page.getByTestId('realtime-connection-state');
  await expect(indicator).toContainText('Realtime updates connected.', { timeout: 30_000 });

  await page.context().setOffline(true);
  await expect(indicator).toContainText('Offline.', { timeout: 10_000 });
  evidence.steps.push({ name: 'realtime-forced-disconnect', status: 0 });

  await page.context().setOffline(false);
  await expect(indicator).toContainText('Realtime updates connected.', { timeout: 30_000 });
  evidence.steps.push({ name: 'realtime-reconnected-and-reauthorized', status: 200 });
}

async function assertCsrfRejectsMissingToken(page: Page, evidence: SmokeEvidence) {
  const probe = await page.evaluate(async () => {
    const response = await fetch('/api/auth/change-password', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        currentPassword: 'wrong-current-password',
        newPassword: 'E2eSmoke!99999'
      })
    });

    return {
      status: response.status,
      text: await response.text()
    };
  });

  evidence.steps.push({
    name: 'csrf-missing-token-rejected',
    method: 'POST',
    path: '/api/auth/change-password',
    status: probe.status,
    bodyPreview: preview(probe.text)
  });
  expect(probe.status, 'unsafe request without CSRF token must be rejected before application validation').toBe(403);
  expect(probe.text, 'CSRF rejection body').toContain('CSRF');
}

async function createDirectMessageAndVerifyPersistence(page: Page, evidence: SmokeEvidence) {
  await page.getByRole('link', { name: 'Messages' }).first().click();
  await expect(page).toHaveURL(/\/app\/messages$/);
  await expect(page.getByTestId('messages-page')).toBeVisible();
  await expect(page.getByTestId('new-message-button')).toBeVisible();

  await page.getByTestId('new-message-button').click();
  await expect(page.getByTestId('new-message-dialog')).toBeVisible();

  const [recipientsResponse] = await Promise.all([
    waitForApiResponse(page, 'GET', '/api/conversations/recipients'),
    page.getByTestId('recipient-search').fill('Recipient')
  ]);
  const recipientsBody = await recordOkJson(recipientsResponse, evidence, 'message-recipients', (body) =>
    Array.isArray(body) && body.some((item: unknown) => hasStringValue(item, 'displayName', smokeRecipientName))
  );
  const recipient = recipientsBody.find((item: Record<string, unknown>) => item.displayName === smokeRecipientName);
  expect(recipient, 'seeded message recipient').toBeTruthy();

  await expect(page.getByTestId('recipient-option').filter({ hasText: smokeRecipientName })).toBeVisible();
  await page.getByTestId('recipient-option').filter({ hasText: smokeRecipientName }).click();

  const [createResponse] = await Promise.all([
    waitForApiResponse(page, 'POST', '/api/conversations/direct'),
    page.getByTestId('create-conversation-submit').click()
  ]);
  const createBody = await recordOkJson(createResponse, evidence, 'message-conversation-create', (body) =>
    hasString(body, 'id') &&
    hasStringValue(body, 'title', smokeRecipientName) &&
    hasStringValue(body, 'type', 'DirectMessage')
  );
  evidence.conversationId = String(createBody.id);

  await expect(page).toHaveURL(new RegExp(`/app/dm/${evidence.conversationId}$`));
  await expect(page.getByTestId('dm-page')).toBeVisible();
  await expect(page.getByRole('heading', { name: smokeRecipientName })).toBeVisible();

  const messageBody = `Real backend DM ${Date.now()}`;
  evidence.messageBody = messageBody;
  await page.getByTestId('message-draft').fill(messageBody);

  const [messageResponse] = await Promise.all([
    waitForApiResponse(page, 'POST', `/api/conversations/${evidence.conversationId}/messages`),
    page.getByTestId('send-message').click()
  ]);
  const messageResponseBody = await recordOkJson(messageResponse, evidence, 'message-send', (body) =>
    hasString(body, 'id') &&
    hasStringValue(body, 'conversationId', evidence.conversationId ?? '') &&
    hasStringValue(body, 'body', messageBody)
  );
  evidence.messageId = String(messageResponseBody.id);

  await expect(page.getByTestId('confirmed-message').filter({ hasText: messageBody })).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('dm-page')).toBeVisible();
  await expect(page.getByTestId('confirmed-message').filter({ hasText: messageBody })).toBeVisible();
  // Conversation registration performs an authoritative realtime catch-up after reload.
  // Wait for that stable projection before opening local message actions.
  await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.', {
    timeout: 30_000
  });

  await recordFetchJson(page, evidence, 'message-list-after-reload', `/api/conversations/${evidence.conversationId}/messages`, {
    validate: (body) =>
      isPagedResponse(body) &&
      body.items.some((item: unknown) =>
        hasStringValue(item, 'id', evidence.messageId ?? '') &&
        hasStringValue(item, 'body', messageBody)
      )
  });

  const messageRow = page.locator(`#message-${evidence.messageId}`);
  const more = page.getByTestId(`message-more-actions-${evidence.messageId}`);
  await more.click();
  await page.getByTestId(`report-message-${evidence.messageId}`).click();
  const [reportResponse] = await Promise.all([
    waitForApiResponse(page, 'POST', `/api/messages/${evidence.messageId}/report`),
    page.getByRole('button', { name: 'Record report request' }).click()
  ]);
  expect(reportResponse.request().headers()['x-csrf-token'], 'message report CSRF header').toBeTruthy();
  expect(reportResponse.request().postDataJSON(), 'message report request DTO').toEqual({ reasonCode: 'reported' });
  await recordOkJson(reportResponse, evidence, 'message-report', (body) =>
    hasStringValue(body, 'status', 'OK')
  );
  await expect(page.getByTestId('message-action-status')).toContainText('Report request recorded.');

  const editedMessageBody = `${messageBody} edited`;
  await more.click();
  await page.getByTestId(`edit-message-${evidence.messageId}`).click();
  await expect(page.getByTestId(`message-edit-input-${evidence.messageId}`)).toBeFocused();
  await page.getByTestId(`message-edit-input-${evidence.messageId}`).fill(editedMessageBody);
  const [editResponse] = await Promise.all([
    waitForApiResponse(page, 'PATCH', `/api/messages/${evidence.messageId}`),
    page.getByTestId(`save-message-edit-${evidence.messageId}`).click()
  ]);
  expect(editResponse.request().headers()['x-csrf-token'], 'message edit CSRF header').toBeTruthy();
  expect(editResponse.request().postDataJSON(), 'message edit request DTO').toEqual({ body: editedMessageBody });
  await recordOkJson(editResponse, evidence, 'message-edit', (body) =>
    hasStringValue(body, 'id', evidence.messageId ?? '') &&
    hasStringValue(body, 'body', editedMessageBody) &&
    hasString(body, 'editedAt')
  );
  await expect(messageRow).toContainText(editedMessageBody);
  await expect(messageRow.getByTestId('message-edited-marker')).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('dm-page')).toBeVisible();
  await expect(page.getByTestId('confirmed-message').filter({ hasText: editedMessageBody })).toBeVisible();
  // Do not open the local overflow until the follow-up catch-up has settled either.
  await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.', {
    timeout: 30_000
  });

  const reloadedMore = page.getByTestId(`message-more-actions-${evidence.messageId}`);
  await reloadedMore.click();
  await page.getByTestId(`delete-message-${evidence.messageId}`).click();
  await expect(page.getByRole('dialog', { name: 'Delete message?' })).toBeVisible();
  const [deleteResponse] = await Promise.all([
    waitForApiResponse(page, 'DELETE', `/api/messages/${evidence.messageId}`),
    page.getByRole('button', { name: 'Delete message' }).click()
  ]);
  expect(deleteResponse.request().headers()['x-csrf-token'], 'message delete CSRF header').toBeTruthy();
  await recordOkJson(deleteResponse, evidence, 'message-delete', (body) =>
    hasStringValue(body, 'status', 'OK')
  );
  await expect(page.locator(`#message-${evidence.messageId}`)).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId('dm-page')).toBeVisible();
  await recordFetchJson(page, evidence, 'message-list-after-delete', `/api/conversations/${evidence.conversationId}/messages`, {
    validate: (body) =>
      isPagedResponse(body) &&
      body.items.every((item: unknown) => !hasStringValue(item, 'id', evidence.messageId ?? ''))
  });
}

async function openWorkspaces(page: Page, evidence: SmokeEvidence) {
  await page.getByRole('link', { name: 'Workspaces' }).first().click();
  await expect(page).toHaveURL(/\/app\/workspaces$/);
  await expect(page.getByTestId('workspace-dashboard')).toBeVisible();

  await recordFetchJson(page, evidence, 'workspaces-list', '/api/workspaces', {
    validate: (body) =>
      Array.isArray(body) && body.some((item: unknown) => hasStringValue(item, 'name', smokeWorkspaceName))
  });
  await expect(page.getByTestId('workspace-card').filter({ hasText: smokeWorkspaceName }).first()).toBeVisible();
}

async function openAnnouncementDetail(page: Page, evidence: SmokeEvidence) {
  await page.getByRole('link', { name: 'Announcements' }).first().click();
  await expect(page).toHaveURL(/\/app\/announcements$/);
  await expect(page.getByTestId('announcements-page')).toBeVisible();

  const listBody = await recordFetchJson(page, evidence, 'announcements-list', '/api/announcements', {
    validate: (body) =>
      isPagedResponse(body) &&
      body.items.some((item: unknown) => hasStringValue(item, 'title', smokeAnnouncementTitle))
  });
  const announcement = listBody.items.find((item: Record<string, unknown>) => item.title === smokeAnnouncementTitle);
  expect(announcement, 'seeded announcement record').toBeTruthy();
  expect(announcement?.requiresReadConfirmation, 'seeded announcement requires recipient read confirmation').toBe(true);
  expect(announcement?.isRead, 'seeded smoke user begins unread').toBe(false);
  evidence.announcementId = String(announcement!.id);

  const announcementItem = page.getByTestId('announcement-list-item').filter({ hasText: smokeAnnouncementTitle }).first();
  await expect(announcementItem).toBeVisible();
  await announcementItem.click();

  await expect(page.getByTestId('announcement-detail-title')).toContainText(smokeAnnouncementTitle);
  await expect(page.getByTestId('announcement-body-text')).toContainText('Synthetic announcement body');

  const markReadAction = page.getByTestId('announcement-mark-read-action');
  await expect(markReadAction).toBeVisible();
  const markReadResponse = waitForApiResponse(
    page,
    'POST',
    `/api/announcements/${evidence.announcementId}/read`,
  );
  await markReadAction.click();
  const markRead = await markReadResponse;
  expect(markRead.request().postDataJSON(), 'mark-read request body').toEqual({});
  expect(markRead.request().headers()['x-csrf-token'], 'mark-read CSRF header').toBeTruthy();
  await recordOkJson(markRead, evidence, 'announcement-mark-read', (body) =>
    hasStringValue(body, 'status', 'OK'),
  );
  await expect(markReadAction).toHaveCount(0);
  await expect(page.getByTestId('announcement-read-status')).toHaveAttribute('role', 'status');
  await expect(page.getByTestId('announcement-read-status')).toBeFocused();

  await recordFetchJson(page, evidence, 'announcement-detail', `/api/announcements/${evidence.announcementId}`, {
    validate: (body) =>
      hasStringValue(body, 'id', evidence.announcementId ?? '') &&
      hasStringValue(body, 'title', smokeAnnouncementTitle) &&
      hasString(body, 'body') &&
      (body as Record<string, unknown>).isRead === true
  });

  await page.reload();
  await expect(page.getByTestId('announcement-detail-title')).toContainText(smokeAnnouncementTitle);
  const reloadedList = await recordFetchJson(page, evidence, 'announcements-list-after-read', '/api/announcements', {
    validate: (body) =>
      isPagedResponse(body) &&
      body.items.some((item: unknown) =>
        hasStringValue(item, 'id', evidence.announcementId ?? '') &&
        (item as Record<string, unknown>).isRead === true,
      )
  });
  expect(
    reloadedList.items.find((item: Record<string, unknown>) => item.id === evidence.announcementId)?.isRead,
    'mark-read persists in the reloaded list projection',
  ).toBe(true);
}

async function openProjectTaskDetail(page: Page, evidence: SmokeEvidence) {
  await page.getByRole('link', { name: 'Projects' }).first().click();
  await expect(page).toHaveURL(/\/app\/projects$/);
  await expect(page.getByTestId('projects-overview-page')).toBeVisible();

  const projectsBody = await recordFetchJson(page, evidence, 'projects-list', '/api/projects', {
    validate: (body) =>
      isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', smokeProjectTitle))
  });
  const project = projectsBody.items.find((item: Record<string, unknown>) => item.title === smokeProjectTitle);
  expect(project, 'seeded project record').toBeTruthy();
  evidence.projectId = String(project!.id);
  await expect(page.getByTestId('project-summary-card').filter({ hasText: smokeProjectTitle }).first()).toBeVisible();

  const tasksBody = await recordFetchJson(page, evidence, 'project-tasks', `/api/projects/${evidence.projectId}/tasks`, {
    validate: (body) =>
      isPagedResponse(body) && body.items.some((item: unknown) =>
        hasStringValue(item, 'title', smokeTaskTitle) &&
        hasString(item, 'workflowStageName') &&
        hasCanonicalTaskStageCategory(item) &&
        hasString(item, 'createdAt') &&
        typeof (item as Record<string, unknown>).hasArtifact === 'boolean')
  });
  const task = tasksBody.items.find((item: Record<string, unknown>) => item.title === smokeTaskTitle);
  expect(task, 'seeded task record').toBeTruthy();
  evidence.taskId = String(task!.id);
  expect(task!.hasArtifact, 'seeded Task-linked Artifact is projected only as availability').toBe(true);
  expect(task!.isBlocked, 'blocking remains independent of the canonical Stage category').toBe(false);
  const taskTimestamp = String(task!.updatedAt ?? task!.createdAt);

  // The projects overview intentionally lists project summaries only. Follow
  // the real project navigation and select its Task list before asserting a
  // task-grid row; the API assertion above remains the direct list contract.
  const projectCard = page.getByTestId('project-summary-card').filter({ hasText: smokeProjectTitle }).first();
  await projectCard.getByRole('link', { name: `Open ${smokeProjectTitle}` }).click();
  await expect(page).toHaveURL(new RegExp(`/app/projects/${evidence.projectId}$`));
  await expect(page.getByTestId('project-detail-page')).toBeVisible();
  await page.getByRole('tab', { name: 'List', exact: true }).click();

  const taskRow = page.locator('[role="row"]').filter({ hasText: smokeTaskTitle }).first();
  await expect(taskRow).toBeVisible();
  await expect(page.getByTestId(`task-stage-name-${evidence.taskId}-desktop`))
    .toHaveText(String(task!.workflowStageName));
  await expect(page.getByTestId(`task-category-${evidence.taskId}-desktop`)).toBeVisible();
  await expect(page.getByTestId(`task-blocked-${evidence.taskId}-desktop`)).toHaveText('Not blocked');
  await expect(page.getByTestId(`task-artifact-${evidence.taskId}-desktop`)).toHaveText('Artifact available');
  await expect(page.getByTestId(`task-updated-${evidence.taskId}-desktop`).locator('time'))
    .toHaveAttribute('datetime', taskTimestamp);
  await clickTaskOpenDetail(page, taskRow);

  await expect(page.getByTestId('task-detail-page')).toBeVisible();
  await expect(page.getByRole('heading', { name: smokeTaskTitle })).toBeVisible();
  await recordFetchJson(page, evidence, 'task-detail', `/api/tasks/${evidence.taskId}`, {
    validate: (body) =>
      isPr03cTaskDetail(body, evidence.taskId ?? '', evidence.projectId ?? '')
  });

  await openMyTasksFromNavigation(page, evidence);
}

async function createProjectTaskThroughUi(page: Page, evidence: SmokeEvidence): Promise<void> {
  const projectId = evidence.projectId;
  expect(projectId, 'the seeded Project id is available for Task creation').toMatch(/^[0-9a-f-]{36}$/i);

  const optionsPath = `/api/projects/${projectId}/tasks/create-options`;
  const directOptions = await recordFetchJson(page, evidence, 'task-create-options-direct', optionsPath, {
    validate: (body) => {
      const data = (body as Record<string, any>)?.data;
      return hasString(body, 'requestId') &&
        data?.projectId === projectId &&
        typeof data?.workspaceId === 'string' &&
        typeof data?.projectTitle === 'string' &&
        data?.canCreateTask === true &&
        typeof data?.canManageProject === 'boolean' &&
        Array.isArray(data?.milestones) &&
        Array.isArray(data?.assignees) &&
        typeof data?.projectScope?.policy?.webEnabled === 'boolean' &&
        typeof data?.projectScope?.policy?.projectFilesEnabled === 'boolean' &&
        Number.isSafeInteger(data?.projectScope?.version) &&
        typeof data?.projectScope?.canSetTaskOverride === 'boolean' &&
        Array.isArray((body as Record<string, unknown>)?.warnings);
    },
  }) as Record<string, any>;
  const options = directOptions.data as Record<string, any>;
  evidence.workspaceId = String(options.workspaceId);

  await page.goto(`/app/projects/${projectId}`);
  await expect(page.getByTestId('project-detail-page')).toBeVisible();
  const newTask = page.getByTestId('project-create-task');
  await expect(newTask).toBeVisible();
  const uiOptionsResponse = waitForApiResponse(page, 'GET', optionsPath);
  await newTask.click();
  await recordOkJson(await uiOptionsResponse, evidence, 'task-create-options-ui', (body) =>
    (body as Record<string, any>)?.data?.projectId === projectId &&
    (body as Record<string, any>)?.data?.workspaceId === options.workspaceId &&
    Array.isArray((body as Record<string, any>)?.data?.milestones) &&
    Array.isArray((body as Record<string, any>)?.data?.assignees),
  );

  await expect(page).toHaveURL(`/app/projects/${projectId}/tasks/new`);
  const title = `Browser smoke canonical Task ${randomUUID().slice(0, 8)}`;
  const goal = 'Review the server-authorized source scope.';
  const deliverable = 'A concise Task creation decision.';
  const constraints = 'No source retrieval or runtime start.';
  const sourceWeb = options.projectScope.policy.webEnabled ? 'enabled' : 'disabled';
  const sourceProjectFiles = options.projectScope.policy.projectFilesEnabled ? 'enabled' : 'disabled';
  const sourcePolicyText = `Project default policy: Web ${sourceWeb}; Project files ${sourceProjectFiles}.`;
  await expect(page.getByTestId('task-create-title')).toBeFocused();
  await expect(page.getByTestId('task-create-page')).toContainText('does not start a runtime or retrieve sources');
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toHaveCount(0);
  await expect(page.locator('[name="webUrl"], [name="provider"], [name="projectId"], [name="workspaceId"]')).toHaveCount(0);
  const qualityChecklist = page.getByTestId('task-create-quality-checklist');
  await expect(qualityChecklist).toContainText('Advisory only: 1 of 4 items are covered.');
  await expect(qualityChecklist).toContainText(sourcePolicyText);
  const addGoal = page.getByTestId('task-create-quality-goal').getByRole('button', { name: 'Add Goal' });
  await addGoal.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('task-brief-goal-input')).toBeFocused();
  await page.getByTestId('task-create-title').fill(`  ${title}  `);
  await page.getByTestId('task-brief-goal-input').fill(goal);
  await page.getByTestId('task-brief-deliverable-input').fill(deliverable);
  await page.getByTestId('task-brief-constraints-input').fill(constraints);
  await expect(qualityChecklist).toContainText('Advisory only: 4 of 4 items are covered.');

  const firstMilestone = Array.isArray(options.milestones) ? options.milestones[0] : null;
  if (firstMilestone) {
    await expect(page.getByTestId('task-create-milestone')).toContainText(String(firstMilestone.title));
    await page.getByTestId('task-create-milestone').selectOption(String(firstMilestone.id));
  }
  const firstAssignee = Array.isArray(options.assignees) ? options.assignees[0] : null;
  if (firstAssignee) {
    await expect(page.getByTestId('task-create-primary-assignee')).toContainText(String(firstAssignee.displayName));
  }
  await expect(page.locator('#task-create-sourceScopeMode-inherit')).toBeChecked();
  await expect(page.locator('#task-create-sourceScopeMode-override')).toHaveCount(
    options.canManageProject && options.projectScope.canSetTaskOverride ? 1 : 0,
  );

  const createPath = `/api/projects/${projectId}/tasks/create`;
  const createResponsePromise = waitForApiResponse(page, 'POST', createPath);
  await page.getByTestId('task-create-submit').click();
  const createResponse = await createResponsePromise;
  const createText = await createResponse.text();
  const createBody = parseJson(createText) as Record<string, any>;
  const createRequest = createResponse.request();
  const requestHeaders = await createRequest.allHeaders();
  const requestBody = createRequest.postDataJSON() as Record<string, unknown>;
  const createdTaskId = typeof createBody?.data?.taskId === 'string' ? createBody.data.taskId : '';
  evidence.steps.push({
    name: 'task-create-ui-command',
    method: createRequest.method(),
    path: new URL(createResponse.url()).pathname,
    status: createResponse.status(),
    body: {
      request: requestBody,
      idempotencyKeyPresent: Boolean(requestHeaders['idempotency-key']),
      csrfHeaderPresent: Boolean(requestHeaders['x-csrf-token']),
    },
    bodyPreview: preview(createText),
  });

  expect(createResponse.status(), `Task create response: ${createText}`).toBe(201);
  expect(createdTaskId, 'created Task id').toMatch(/^[0-9a-f-]{36}$/i);
  expect(createBody).toMatchObject({
    data: {
      taskId: createdTaskId,
      projectId,
      workspaceId: options.workspaceId,
      title,
      priority: 1,
      sourceScopeMode: 'Inherit',
      taskOverridePolicy: null,
    },
    warnings: [],
  });
  const expectedRequest: Record<string, unknown> = {
    title,
    priority: 1,
    goal,
    deliverable,
    constraints,
    sourceScopeMode: 'Inherit',
  };
  if (firstMilestone) {
    expectedRequest['milestoneId'] = String(firstMilestone.id);
  }
  expect(requestBody).toEqual(expectedRequest);
  expect(requestBody).not.toHaveProperty('taskOverridePolicy');
  expect(requestBody).not.toHaveProperty('webUrl');
  expect(requestBody).not.toHaveProperty('provider');
  expect(requestHeaders['idempotency-key']).toMatch(/^task-create-[\x20-\x7e]+$/u);
  expect(requestHeaders['x-csrf-token'], 'Task create uses the real Angular CSRF interceptor').toBeTruthy();

  await expect(page).toHaveURL(`/app/projects/${projectId}/tasks/${createdTaskId}`);
  await expect(page.getByTestId('task-detail-page')).toBeVisible();
  const persisted = await recordFetchJson(page, evidence, 'task-create-authoritative-detail', `/api/tasks/${createdTaskId}`, {
    validate: (body) => {
      const task = (body as Record<string, any>)?.task;
      return task?.id === createdTaskId &&
        task?.projectId === projectId &&
        task?.workspaceId === options.workspaceId &&
        task?.title === title &&
        // The canonical create response serializes the request enum as a
        // number, while the established Task-detail projection exposes the
        // human-readable priority string.
        task?.priority === 'Medium' &&
        task?.brief?.goal?.value === goal &&
        task?.brief?.deliverable?.value === deliverable &&
        task?.brief?.constraints?.value === constraints;
    },
  }) as Record<string, any>;
  expect(persisted.task.milestoneId ?? null).toBe(firstMilestone ? String(firstMilestone.id) : null);
  const runtimeRequests = await page.evaluate((id) =>
    performance.getEntriesByType('resource')
      .map((entry) => new URL(entry.name, window.location.href).pathname)
      .filter((path) => path === `/api/tasks/${id}/execution-runs`),
    createdTaskId,
  );
  expect(runtimeRequests, 'Task creation does not request a runtime').toEqual([]);
}

async function openPr03cTaskDetail(page: Page, evidence: SmokeEvidence) {
  const projectsBody = await recordFetchJson(page, evidence, 'pr03c-projects-list', '/api/projects', {
    validate: (body) => isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', smokeProjectTitle))
  });
  const project = projectsBody.items.find((item: Record<string, unknown>) => item.title === smokeProjectTitle);
  expect(project, 'PR03C seeded project').toBeTruthy();
  evidence.projectId = String(project!.id);

  const tasksBody = await recordFetchJson(page, evidence, 'pr03c-project-tasks', `/api/projects/${evidence.projectId}/tasks`, {
    validate: (body) => isPagedResponse(body) && body.items.some((item: unknown) => hasStringValue(item, 'title', smokeTaskTitle))
  });
  const task = tasksBody.items.find((item: Record<string, unknown>) => item.title === smokeTaskTitle);
  expect(task, 'PR03C seeded task').toBeTruthy();
  evidence.taskId = String(task!.id);

  const detail = await recordFetchJson(page, evidence, 'pr03c-task-detail-contract', `/api/tasks/${evidence.taskId}`, {
    validate: (body) => isPr03cTaskDetail(body, evidence.taskId ?? '', evidence.projectId ?? '')
  });
  evidence.workspaceId = String((detail as Record<string, any>).task.workspaceId);
  expect(evidence.workspaceId, 'task workspaceId is part of the canonical Task DTO').toMatch(/^[0-9a-f-]{36}$/i);

  // The direct Task route remains tenant/workspace-authorized. Establish the
  // seeded Task's Workspace explicitly instead of relying on another serial
  // smoke scenario's local selection or list ordering.
  const workspaceSwitcher = page.getByTestId('workspace-switcher');
  await expect(workspaceSwitcher).toBeVisible();
  await expect(workspaceSwitcher.locator(`option[value="${evidence.workspaceId}"]`)).toHaveCount(1);
  if (await workspaceSwitcher.inputValue() !== evidence.workspaceId) {
    await workspaceSwitcher.selectOption(evidence.workspaceId);
    await expect(workspaceSwitcher).toHaveValue(evidence.workspaceId);
  }

  await page.goto(`/app/projects/${evidence.projectId}/tasks/${evidence.taskId}`);
  await expect(page.getByTestId('task-detail-page')).toBeVisible();
  await expect(page.getByRole('heading', { name: smokeTaskTitle })).toBeVisible();
}

async function openMyTasksFromNavigation(page: Page, evidence: SmokeEvidence) {
  let projectListRequests = 0;
  page.on('request', (request) => {
    if (request.method() === 'GET' && new URL(request.url()).pathname === '/api/projects') {
      projectListRequests += 1;
    }
  });

  const [myTasksResponse] = await Promise.all([
    waitForApiResponse(page, 'GET', '/api/me/tasks'),
    page.getByTestId('nav-my-tasks').first().click()
  ]);
  await expect(page).toHaveURL(/\/app\/tasks$/);
  await expect(page.getByTestId('my-tasks-page')).toBeVisible();
  await expect(page.getByTestId('projects-load-error')).toHaveCount(0);

  const myTasksBody = await recordOkJson(myTasksResponse, evidence, 'my-tasks-list-ui-request', (body) =>
    isPagedResponse(body) &&
    body.items.some((item: unknown) =>
      hasStringValue(item, 'taskId', evidence.taskId ?? '') &&
      hasStringValue(item, 'projectId', evidence.projectId ?? '') &&
      hasStringValue(item, 'projectTitle', smokeProjectTitle) &&
      hasStringValue(item, 'title', smokeTaskTitle)
    )
  );
  const assignedTask = myTasksBody.items.find((item: Record<string, unknown>) => item.title === smokeTaskTitle);
  expect(assignedTask, 'seeded assigned My Tasks row').toBeTruthy();
  expect(projectListRequests, 'My Tasks must not request /api/projects while loading').toBe(0);
  evidence.steps.push({
    name: 'my-tasks-independent-from-project-list',
    method: 'GET',
    path: '/api/projects',
    status: projectListRequests
  });

  const taskButton = page.getByRole('button', { name: /^Browser smoke task(?:\s|$)/ }).first();
  await expect(taskButton).toBeVisible();
  await taskButton.click();
  await expect(page).toHaveURL(new RegExp(`/app/projects/${evidence.projectId}/tasks/${evidence.taskId}$`));
  await expect(page.getByTestId('task-detail-page')).toBeVisible();
  await expect(page.getByRole('heading', { name: smokeTaskTitle })).toBeVisible();
}

async function clickTaskOpenDetail(page: Page, taskRow: Locator): Promise<void> {
  const action = taskRow.locator('[data-testid^="task-openDetail-"]');
  if (!(await action.isVisible())) {
    await page.locator('.ag-body-horizontal-scroll-viewport').evaluate((viewport) => {
      viewport.scrollLeft = viewport.scrollWidth;
    });
  }

  await expect(action).toBeVisible();
  await action.click();
}

async function submitInvalidPasswordChange(page: Page, evidence: SmokeEvidence) {
  await page.getByRole('link', { name: 'Account' }).first().click();
  await expect(page).toHaveURL(/\/app\/account$/);
  await expect(page.getByTestId('account-page')).toBeVisible();
  await expect(page.getByTestId('password-panel')).toBeVisible();

  await page.getByTestId('current-password').fill('wrong-current-password');
  await page.getByTestId('new-password').fill('E2eSmoke!99999');
  await page.getByTestId('confirm-new-password').fill('E2eSmoke!99999');

  const [passwordResponse] = await Promise.all([
    waitForApiResponse(page, 'POST', '/api/auth/change-password'),
    page.getByRole('button', { name: 'Change password' }).click()
  ]);
  await recordFailureJson(passwordResponse, evidence, 'password-change-validation-failure', 400, (body) =>
    hasString(body, 'error')
  );
  await expect(page.getByTestId('password-change-failure')).toBeVisible();
  await expect(page.getByTestId('password-change-success')).toHaveCount(0);
}

async function logoutAndVerifyAccessRevoked(page: Page, evidence: SmokeEvidence) {
  // The browser response hides Set-Cookie and its body can no longer be read
  // safely once the Angular logout completion starts the lazy public route.
  // Assert the actual rendered public UI and browser location instead of
  // Playwright's document-navigation observer: this is an in-document Angular
  // route, not a new document request.
  const logoutResponse = waitForApiResponse(page, 'POST', '/api/auth/logout');
  await page.getByTestId('logout-action').click();
  const response = await logoutResponse;
  recordLogoutResponse(response, evidence);

  await expect(page.getByTestId('login-page')).toBeVisible();
  await expectBrowserPathname(page, '/app/login', 'successful logout must navigate the browser to login');
  await expect(page.getByTestId('app-shell')).toHaveCount(0);
  await expectAuthenticationCookieToBeCleared(page);

  const meProbe = await fetchFromPage(page, '/api/auth/me');
  evidence.steps.push({
    name: 'post-logout-auth-me',
    method: 'GET',
    path: '/api/auth/me',
    status: meProbe.status,
    bodyPreview: preview(meProbe.text)
  });
  expect(meProbe.status, 'protected current-user API must reject after logout').toBe(401);

  const statusProbe = await fetchJsonFromPage(page, '/api/auth/status');
  evidence.steps.push({
    name: 'post-logout-auth-status',
    method: 'GET',
    path: '/api/auth/status',
    status: statusProbe.status,
    body: statusProbe.body
  });
  expect(statusProbe.status).toBe(200);
  expect(statusProbe.body.isAuthenticated).toBe(false);

  const projectsProbe = await fetchFromPage(page, '/api/projects');
  evidence.steps.push({
    name: 'post-logout-protected-projects',
    method: 'GET',
    path: '/api/projects',
    status: projectsProbe.status,
    bodyPreview: preview(projectsProbe.text)
  });
  expect(projectsProbe.status, 'protected project API must reject after logout').toBe(401);

  await page.goto('/app/projects');
  await expect(page.getByTestId('login-page')).toBeVisible();
  await expectBrowserPathname(page, '/app/login', 'protected route must redirect the browser to login after logout');
  await expect(page.getByTestId('projects-overview-page')).toHaveCount(0);
}

async function expectBrowserPathname(page: Page, expectedPathname: string, message: string): Promise<void> {
  // This is a read-only browser observation, not an Angular service or storage
  // injection. It verifies the URL after the visible route has rendered.
  const pathname = await page.evaluate(() => window.location.pathname);
  expect(pathname, message).toBe(expectedPathname);
}

function waitForApiResponse(
  page: Page,
  method: string,
  path: string | RegExp,
  options?: { readonly timeout?: number }
): Promise<PlaywrightResponse> {
  return page.waitForResponse((response) => {
    if (response.request().method() !== method) {
      return false;
    }

    const pathname = new URL(response.url()).pathname;
    return typeof path === 'string' ? pathname === path : path.test(pathname);
  }, options);
}

type ProjectCreateOutcome =
  | { readonly kind: 'response'; readonly response: PlaywrightResponse }
  | { readonly kind: 'stopped' };

async function waitForProjectCreateOutcome(page: Page, path: string): Promise<ProjectCreateOutcome> {
  const timeout = 15_000;
  const response = waitForApiResponse(page, 'POST', path, { timeout }).then(
    (value) => ({ kind: 'response' as const, response: value }),
    () => null,
  );
  const stoppedBeforeDispatch = page
    .getByTestId('project-create-create-status')
    .filter({ hasText: 'stopped before it was sent' })
    .waitFor({ state: 'visible', timeout })
    .then(
      () => ({ kind: 'stopped' as const }),
      () => null,
    );

  // An authorization refresh can replace the transient recovery message before
  // a response wait expires. Observe both valid outcomes concurrently, while
  // handling the losing waiter so it cannot produce an unhandled rejection.
  const first = await Promise.race([response, stoppedBeforeDispatch]);
  if (first) {
    return first;
  }

  const [responseOutcome, stoppedOutcome] = await Promise.all([response, stoppedBeforeDispatch]);
  const outcome = responseOutcome ?? stoppedOutcome;
  if (outcome) {
    return outcome;
  }

  throw new Error('Timed out waiting for the Project create response or authorization-clear recovery.');
}

function waitForSuccessfulApiResponse(
  page: Page,
  method: string,
  path: string | RegExp
): Promise<PlaywrightResponse> {
  return page.waitForResponse((response) => {
    if (!response.ok() || response.request().method() !== method) {
      return false;
    }

    const pathname = new URL(response.url()).pathname;
    return typeof path === 'string' ? pathname === path : path.test(pathname);
  });
}

async function recordOkJson(
  response: PlaywrightResponse,
  evidence: SmokeEvidence,
  name: string,
  validate: (body: any) => boolean
) {
  const text = await response.text();
  const body = parseJson(text);

  evidence.steps.push({
    name,
    method: response.request().method(),
    path: new URL(response.url()).pathname,
    status: response.status(),
    bodyPreview: preview(text)
  });

  expect(response.ok(), `${name} response ${response.status()}: ${text}`).toBe(true);
  expect(validate(body), `${name} response DTO shape: ${text}`).toBe(true);
  return body;
}

function recordLogoutResponse(response: PlaywrightResponse, evidence: SmokeEvidence): void {
  const headers = response.headers();
  const contentType = headers['content-type'] ?? '';

  evidence.steps.push({
    name: 'logout',
    method: response.request().method(),
    path: new URL(response.url()).pathname,
    status: response.status(),
    bodyPreview: '[not read: successful logout immediately routes to login]'
  });

  expect(response.ok(), `logout response status: ${response.status()}`).toBe(true);
  expect(response.status(), 'logout response status').toBe(200);
  expect(contentType, 'logout response content type').toContain('application/json');

  // Angular clears the session and routes to /login as soon as this response succeeds.
  // Playwright cannot safely read a response body after that SPA navigation; the exact
  // { status: 'OK' } DTO and Set-Cookie expiry header are covered by
  // AuthSecurityHttpTests. Browser responses intentionally hide Set-Cookie, so this
  // acceptance verifies the resulting browser cookie state below.
}

async function expectAuthenticationCookieToBeCleared(page: Page): Promise<void> {
  const cookies = await page.context().cookies();
  expect(
    cookies.some((cookie) => cookie.name === '.Coglatas.Auth'),
    'logout must remove the authentication cookie from the browser context'
  ).toBe(false);
}

async function recordFailureJson(
  response: PlaywrightResponse,
  evidence: SmokeEvidence,
  name: string,
  expectedStatus: number,
  validate: (body: any) => boolean
) {
  const text = await response.text();
  const body = parseJson(text);

  evidence.steps.push({
    name,
    method: response.request().method(),
    path: new URL(response.url()).pathname,
    status: response.status(),
    bodyPreview: preview(text)
  });

  expect(response.status(), `${name} response status: ${text}`).toBe(expectedStatus);
  expect(validate(body), `${name} response DTO shape: ${text}`).toBe(true);
  return body;
}

async function recordFetchJson(
  page: Page,
  evidence: SmokeEvidence,
  name: string,
  path: string,
  options: {
    validate: (body: any) => boolean;
    sensitive?: boolean;
  }
) {
  const result = await fetchFromPage(page, path);
  const body = parseJson(result.text);

  evidence.steps.push({
    name,
    method: 'GET',
    path,
    status: result.status,
    bodyPreview: options.sensitive ? '[redacted]' : preview(result.text)
  });

  expect(result.ok, `${name} response ${result.status}: ${options.sensitive ? '[redacted]' : result.text}`).toBe(true);
  expect(options.validate(body), `${name} response DTO shape`).toBe(true);
  return body;
}

async function fetchFromPage(page: Page, path: string): Promise<{ ok: boolean; status: number; text: string }> {
  return page.evaluate(async (url) => {
    const response = await fetch(url, { credentials: 'include' });
    return {
      ok: response.ok,
      status: response.status,
      text: await response.text()
    };
  }, path);
}

async function fetchJsonFromPage(page: Page, path: string): Promise<{ status: number; body: any }> {
  const result = await fetchFromPage(page, path);
  return {
    status: result.status,
    body: parseJson(result.text)
  };
}

async function fetchBinaryFromPage(page: Page, path: string): Promise<{ status: number; text: string }> {
  const response = await page.context().request.get(new URL(path, page.url()).href);
  return { status: response.status(), text: await response.text() };
}

async function requestBinaryWithCsrf(page: Page, path: string, body: unknown): Promise<{ status: number; text: string }> {
  const csrf = await page.evaluate(async () => {
    const response = await fetch('/api/security/csrf-token', { credentials: 'include' });
    return response.json() as Promise<{ token?: string; headerName?: string }>;
  });
  expect(csrf.token, 'binary request CSRF token').toBeTruthy();
  expect(csrf.headerName, 'binary request CSRF header name').toBeTruthy();
  const response = await page.context().request.post(new URL(path, page.url()).href, {
    data: body,
    headers: { [csrf.headerName!]: csrf.token! }
  });
  return { status: response.status(), text: await response.text() };
}

/** Acquires the anti-forgery token in the browser and never serializes it into evidence. */
async function requestWithCsrf(
  page: Page,
  method: 'POST' | 'PATCH' | 'DELETE',
  path: string,
  body?: unknown,
  additionalHeaders: Readonly<Record<string, string>> = {}
): Promise<{ status: number; text: string; csrfHeaderPresent: boolean }> {
  return page.evaluate(async ({ method, path, body, additionalHeaders }) => {
    const csrfResponse = await fetch('/api/security/csrf-token', { credentials: 'include' });
    const csrf = await csrfResponse.json() as { token?: string; headerName?: string };
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...additionalHeaders
    };
    if (csrf.token && csrf.headerName) {headers[csrf.headerName] = csrf.token;}
    const response = await fetch(path, {
      method,
      credentials: 'include',
      headers,
      ...(body === undefined ? {} : { body: JSON.stringify(body) })
    });
    return {
      status: response.status,
      text: await response.text(),
      csrfHeaderPresent: Boolean(csrf.token && csrf.headerName && headers[csrf.headerName])
    };
  }, { method, path, body, additionalHeaders });
}

function recordFailedApiResponse(response: PlaywrightResponse, evidence: SmokeEvidence) {
  const url = new URL(response.url());
  if (!url.pathname.startsWith('/api/') || response.status() < 400) {
    return;
  }

  evidence.failedApiResponses.push({
    method: response.request().method(),
    path: url.pathname,
    status: response.status()
  });
}

function expectUnexpectedApiFailures(
  evidence: SmokeEvidence,
  scenarioExpectedFailures: readonly SmokeFailedApiResponse[] = []
) {
  const { unexpected, remainingScenarioExpected } = classifyUnexpectedApiFailures(
    evidence,
    scenarioExpectedFailures,
  );
  expect(unexpected, 'unexpected failed API responses').toEqual([]);
  expect(remainingScenarioExpected, 'scenario-expected failed API responses were not observed').toEqual([]);
}

function expectUnexpectedConsoleErrors(
  evidence: SmokeEvidence,
  scenarioExpectedFailures: readonly SmokeFailedApiResponse[] = []
) {
  const unexpected = classifyUnexpectedConsoleErrors(evidence, scenarioExpectedFailures);
  expect(unexpected, 'unexpected browser console errors').toEqual([]);
}

function sameFailure(left: SmokeFailedApiResponse, right: SmokeFailedApiResponse): boolean {
  return left.method === right.method && left.path === right.path && left.status === right.status;
}

function expectSafeProjectDetailDenial(
  text: string,
  protectedValues: readonly string[]
): Record<string, unknown> {
  const body = parseJson(text) as Record<string, unknown>;
  expect(body).toMatchObject({ code: 'BadRequest', message: 'Project not found.' });
  expect(typeof body.traceId, 'safe Project denial traceId').toBe('string');
  for (const protectedValue of protectedValues) {
    expect(text, 'safe Project denial must not expose protected Project data').not.toContain(protectedValue);
  }
  return body;
}

function expectOnlyExpectedSyntheticHubConsoleErrors(
  evidence: SmokeEvidence,
  scenarioExpectedFailures: readonly SmokeFailedApiResponse[] = []
) {
  const expectedNetworkFailures = new Map<number, number>();
  const remainingScenarioExpected = [...scenarioExpectedFailures];
  for (const failure of evidence.failedApiResponses) {
    const expectedIndex = remainingScenarioExpected.findIndex((candidate) => sameFailure(failure, candidate));
    if (expectedIndex < 0) {continue;}
    remainingScenarioExpected.splice(expectedIndex, 1);
    expectedNetworkFailures.set(failure.status, (expectedNetworkFailures.get(failure.status) ?? 0) + 1);
  }

  const unexpected = evidence.consoleErrors.filter((message) => {
    if (/Synthetic (?:PR06|Issue 378) Hub unavailability|Failed to complete negotiation with the server|Failed to start the connection/i
      .test(message)) {
      return false;
    }

    const match = /Failed to load resource:.*status of (\d{3})/i.exec(message);
    if (!match) {return true;}
    const status = Number(match[1]);
    const remaining = expectedNetworkFailures.get(status) ?? 0;
    if (remaining === 0) {return true;}
    expectedNetworkFailures.set(status, remaining - 1);
    return false;
  });
  expect(unexpected, 'unexpected SignalR-degraded browser console errors').toEqual([]);
}

async function verifyRealtimeRuntimeConfig(page: Page, evidence: SmokeEvidence): Promise<void> {
  const enabled = await page.evaluate(() => window.__AIP_FEATURE_FLAGS__?.['realtime.signalR'] === true);
  evidence.steps.push({ name: 'realtime-runtime-config-enabled', method: 'GET', path: '/api/ui/runtime-config.js', status: enabled ? 200 : 500 });
  expect(enabled, 'same-origin runtime configuration must enable the realtime rollout').toBe(true);
}

function expectCanonicalDenial(text: string, expectedCode: string): void {
  const body = parseJson(text) as Record<string, any>;
  expect(typeof body.requestId, 'safe denial requestId').toBe('string');
  expect(body.error?.code, 'safe denial error code').toBe(expectedCode);
  expect(text, 'safe denial must not expose protected metadata').not.toMatch(/browser-smoke-task|browser smoke label|storageKey|filePath|tokenHash|policy stamp|internal\//i);
}

function isPagedResponse(body: unknown): body is { items: Record<string, unknown>[] } {
  return typeof body === 'object' && body !== null && Array.isArray((body as Record<string, unknown>).items);
}

function isPr03cTaskDetail(body: unknown, taskId: string, projectId: string): boolean {
  if (typeof body !== 'object' || body === null) {return false;}
  const detail = body as Record<string, any>;
  const task = detail.task;
  return hasStringValue(task, 'id', taskId) &&
    hasStringValue(task, 'projectId', projectId) &&
    hasStringValue(task, 'title', smokeTaskTitle) &&
    hasString(task, 'workspaceId') &&
    typeof task.version === 'number' &&
    typeof task.priority === 'string' &&
    typeof task.stageCategory === 'number' &&
    typeof task.reviewStatus === 'number' &&
    detail.relationships && typeof detail.relationships === 'object' &&
    detail.permissions && typeof detail.permissions === 'object' &&
    Array.isArray(detail.checklist) && Array.isArray(detail.labels) &&
    detail.watchState && typeof detail.watchState === 'object' &&
    isPagedResponse(detail.subtasks) && isPagedResponse(detail.comments) && isPagedResponse(detail.files) &&
    detail.files.items.length > 0 &&
    hasStringValue(detail.files.items[0], 'fileName', smokeTaskFileName) &&
    typeof detail.files.items[0].scanStatus === 'string' &&
    typeof detail.files.items[0].canOpen === 'boolean' &&
    typeof detail.files.items[0].canRequestDownloadGrant === 'boolean';
}

function hasString(body: unknown, key: string): body is Record<string, unknown> {
  return typeof body === 'object' && body !== null && typeof (body as Record<string, unknown>)[key] === 'string';
}

function hasStringValue(body: unknown, key: string, expected: string): boolean {
  return hasString(body, key) && (body as Record<string, unknown>)[key] === expected;
}

function isWorkspaceAnnouncementAudience(
  value: unknown,
  displayName: string
): value is {
  key: string;
  workspaceId: string;
  displayName: string;
  estimatedRecipientCount: number;
} {
  const recipientCount = (value as Record<string, unknown>)?.estimatedRecipientCount;
  return (
    hasStringValue(value, 'scopeType', 'workspace') &&
    hasStringValue(value, 'displayName', displayName) &&
    hasString(value, 'key') &&
    hasString(value, 'workspaceId') &&
    typeof recipientCount === 'number' &&
    Number.isInteger(recipientCount) &&
    recipientCount >= 0
  );
}

function hasCanonicalTaskStageCategory(body: unknown): boolean {
  if (!hasString(body, 'stageCategory')) {return false;}
  return ['Backlog', 'Todo', 'InProgress', 'Review', 'Done', 'Cancelled']
    .includes(String((body as Record<string, unknown>).stageCategory));
}

function hasCapability(body: unknown, capability: string): boolean {
  return (
    typeof body === 'object' &&
    body !== null &&
    Array.isArray((body as Record<string, unknown>).capabilities) &&
    (body as Record<string, unknown>).capabilities.includes(capability)
  );
}

function parseJson(text: string): any {
  try {
    return JSON.parse(text);
  } catch {
    return {};
  }
}

function preview(text: string): string {
  return text.slice(0, 320);
}

interface SmokeEvidence {
  baseURL: string;
  email: string;
  userId?: string;
  conversationId?: string;
  messageId?: string;
  messageBody?: string;
  announcementId?: string;
  projectId?: string;
  taskId?: string;
  workspaceId?: string;
  steps: SmokeEvidenceStep[];
  pageErrors: string[];
  consoleErrors: string[];
  failedApiResponses: SmokeFailedApiResponse[];
}

interface SmokeEvidenceStep {
  name: string;
  method?: string;
  path?: string;
  status?: number;
  body?: unknown;
  bodyPreview?: string;
}

interface SmokeFailedApiResponse {
  method: string;
  path: string;
  status: number;
}

interface Pr06GanttEvidence extends SmokeEvidence {
  featureFlagEnabled?: boolean;
  readonly seed: {
    readonly projectSlug: string;
    readonly projectTitle: string;
    readonly taskTitles: readonly string[];
    readonly milestoneTitle: string;
  };
  readonly apiInterception: 'none';
  readonly hubTransportFaultInjection: string;
  readonly commands: Pr06GanttCommandEvidence[];
  viewerReadOnly?: {
    userId: string;
    snapshotStatus: number;
    permissions: Pr06GanttPermissionsDto;
    editActionCount: 0;
    pageErrors: string[];
    consoleErrors: string[];
    failedApiResponses: SmokeFailedApiResponse[];
  };
  dependencyNoCascade?: {
    successorTaskId: string;
    before: {
      plannedStartDate: string | null;
      plannedEndDate: string | null;
    };
    after: {
      plannedStartDate: string | null;
      plannedEndDate: string | null;
    };
  };
  degradedHttp?: {
    connectionState: 'delayed';
    commandStatus: number;
    manualRefreshStatus: number;
    apiInterception: 'none';
    pageErrors: string[];
    consoleErrors: string[];
    failedApiResponses: SmokeFailedApiResponse[];
  };
  staleConflict?: {
    taskId: string;
    staleVersion: number;
    concurrentVersion: number;
    code: string | undefined;
    status: number;
    authoritativeVersion: number;
    authoritativeDates: {
      plannedStartDate: string | null;
      plannedEndDate: string | null;
    };
    optimisticDateTransitions: string[];
    intentPreserved: true;
    focusRestored: true;
  };
  reloadPersistence?: {
    scheduleTaskUnscheduled: true;
    progressPercent: number;
    degradedProgressPercent: number;
    milestoneDate: string;
    conflictDates: [string, string];
    removedDependencyAbsent: true;
  };
  authorizationRevocation?: {
    revokingActorUserId: string;
    revokedUserId: string;
    revokeStatus: number;
    overlappingRefreshStatus: number;
    projectDetailDenialStatus: number;
    projectDetailDenialCode: string;
    denialStatus: number;
    subsequentDenialStatus: number;
    protectedDataClearedBeforeRevalidation: boolean;
    protectedDataRestoredByStaleResponse: boolean;
    revokingPageErrors: string[];
    revokingConsoleErrors: string[];
    revokingFailedApiResponses: SmokeFailedApiResponse[];
  };
}

interface Pr06GanttCommandEvidence {
  name: string;
  method: string;
  path: string;
  status: number;
  request: Record<string, unknown>;
  csrfHeaderPresent: boolean;
}

interface Pr06GanttWarningDto {
  code: string;
  message: string;
  severity: 'Info' | 'Warning';
  targetType: string;
  targetId: string | null;
  field: string | null;
  blocking: false;
}

interface Pr06GanttPermissionsDto {
  canEditSchedule: boolean;
  canEditProgress: boolean;
  canManageDependencies: boolean;
  canClearSchedule: boolean;
  canOpen: boolean;
}

interface Pr06GanttItemDto {
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
  stageCategory: string;
  priority: string;
  isBlocked: boolean;
  primaryAssignee: { userId: string; displayName: string } | null;
  version: number;
  scheduleEditPermissions: Pr06GanttPermissionsDto;
  warnings: Pr06GanttWarningDto[];
}

interface Pr06GanttDependencyDto {
  dependencyId: string;
  predecessorTaskId: string;
  successorTaskId: string;
  type: 'FinishToStart' | 'StartToStart' | 'FinishToFinish' | 'StartToFinish';
  editable: boolean;
  version: number;
  warnings: Pr06GanttWarningDto[];
}

interface Pr06GanttSnapshotDto {
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
  scheduledItems: Pr06GanttItemDto[];
  unscheduledItems: Pr06GanttItemDto[];
  milestones: Pr06GanttItemDto[];
  dependencies: Pr06GanttDependencyDto[];
  warnings: Pr06GanttWarningDto[];
  permissions: Pr06GanttPermissionsDto;
  maximumItems: number;
  totalItems: number;
}

interface Pr05KanbanEvidence extends SmokeEvidence {
  tenantId?: string;
  featureFlagEnabled?: boolean;
  readonly seed: {
    readonly projectSlug: string;
    readonly projectTitle: string;
    readonly taskTitles: readonly string[];
  };
  readonly apiInterception: 'none';
  readonly featureFallback: string;
  readonly commands: Pr05KanbanCommandEvidence[];
  initialSnapshot?: {
    responseUrl: string;
    status: number;
    tenantId: string;
    workspaceId: string;
    projectId: string;
    boardVersion: number;
    taskIds: Record<string, string>;
    taskVersions: Record<string, number>;
    stages: Record<'todo' | 'done' | 'cancelled', string>;
    totalAuthorizedCardCount: number;
    isTruncated: boolean;
    canConfigure: boolean;
  };
  reorderPersistence?: {
    taskId: string;
    beforeTaskId: string;
    boardVersion: number;
    boardOrder: number;
    persistedOrder: string[];
  };
  movePersistence?: {
    taskId: string;
    workflowStageId: string;
    boardVersion: number;
    taskVersion: number;
    boardOrder: number;
  };
  reasonRequired?: {
    taskId: string;
    escapeSentPost: false;
    cancelSentPost: false;
    focusRestored: true;
    submittedReason: string;
    persistedWorkflowStageId: string;
  };
  staleConflict?: {
    taskId: string;
    staleTaskVersion: number;
    staleBoardVersion: number;
    concurrentBoardVersion: number;
    status: number;
    code: string | undefined;
    refetchedBoardVersion: number;
    rolledBackWorkflowStageId: string;
    optimisticStageTransitions: string[];
    focusRestored: true;
  };
  authorizationRevocation?: {
    revokingActorUserId: string;
    revokedUserId: string;
    revokeStatus: number;
    csrfHeaderPresent: boolean;
    overlappingRefreshStatus: number;
    projectDetailDenialStatus: number;
    projectDetailDenialCode: string;
    staleAuthorizedResponseUrl: string;
    responseGateStatusCode: number;
    denialUrl: string;
    denialStatus: number;
    subsequentDenialStatus: number;
    protectedDataClearedBeforeRevalidation: boolean;
    protectedDataRestoredByStaleResponse: boolean;
  };
}

interface Pr05KanbanCommandEvidence {
  name: string;
  responseUrl: string;
  status: number;
  taskId: string;
  expectedTaskVersion: number;
  expectedBoardVersion: number;
  authoritativeBoardVersion?: number;
  targetWorkflowStageId: string;
  targetBeforeTaskId: string | null;
  targetAfterTaskId: string | null;
  reason?: string | null;
  csrfHeaderPresent: boolean;
}

interface Pr05KanbanSnapshotDto {
  board: {
    projectId: string;
    version: number;
    totalAuthorizedCardCount: number;
    isTruncated: boolean;
    uiPermissions: { canConfigure: boolean };
    warnings: unknown[];
  };
  columns: Pr05KanbanColumnDto[];
  cards: Pr05KanbanCardDto[];
}

interface Pr05KanbanColumnDto {
  workflowStageId: string;
  displayName: string;
  category: number | string;
  displayOrder: number;
  wipWarningLimit: number | null;
  currentAuthorizedCardCount: number;
  hasWipWarning: boolean;
  uiPermissions: { canConfigure: boolean };
}

interface Pr05KanbanCardDto {
  taskId: string;
  summary: string;
  workflowStageId: string;
  boardOrder: number;
  version: number;
  uiPermissions: {
    canOpen: boolean;
    canMove: boolean;
    allowedTargetWorkflowStageIds: string[];
  };
}

interface Pr05KanbanCommandResponseDto {
  snapshot: Pr05KanbanSnapshotDto;
  focusTaskId: string | null;
  warnings: unknown[];
}
