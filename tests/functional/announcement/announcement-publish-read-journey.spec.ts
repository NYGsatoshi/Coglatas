/* eslint-disable max-lines, max-lines-per-function, require-atomic-updates -- FCI-06 keeps one canonical Announcement owner and bounded evidence together; test.step hand-offs intentionally assign captured resource IDs after awaits. */
import { randomUUID } from 'node:crypto';

import { expect, type APIRequestContext, type APIResponse, type Response as PlaywrightResponse, test } from '@playwright/test';

import { functionalMetadata } from '../fixtures/functional-metadata.mjs';
import { waitForAuthoritativeState } from '../helpers/authoritative-state';
import { loginViaApi, logoutViaApi } from '../helpers/auth';
import { csrfAwareRequest } from '../helpers/csrf';
import { assertSafeResponse } from '../helpers/safe-response';

const smokeEmail = process.env.COGLATAS_BROWSER_SMOKE_EMAIL ?? '';
const smokePassword = process.env.COGLATAS_BROWSER_SMOKE_PASSWORD ?? '';
const recipientEmail = 'browser-smoke-recipient@example.test';
const workspaceName = 'Browser Smoke Workspace';

interface AnnouncementEvidence {
  journeyId: 'FUNC-ANN-001';
  workspaceId?: string;
  draftId?: string;
  announcementId?: string;
  audienceRecipientCount?: number;
  createDraftStatus?: number;
  publishRequestStatus?: number;
  workerFinalStatus?: string;
  recipientListVisible?: boolean;
  recipientDetailVisible?: boolean;
  recipientReadPersisted?: boolean;
  recipientAcknowledged?: boolean;
  usedScheduledWallClockWait: false;
}

interface SyntheticCredentials {
  email: string;
  password: string;
}

test.describe('FCI-06 Announcement real-backend owner journey', () => {
  test.setTimeout(120_000);

  test.beforeAll(() => {
    if (process.env.COGLATAS_REAL_BACKEND_SMOKE !== '1') {
      throw new Error(
        'FUNC-ANN-001 requires COGLATAS_REAL_BACKEND_SMOKE=1 and the deterministic full-stack harness.'
      );
    }

    const baseURL = process.env.PLAYWRIGHT_BASE_URL;
    if (!baseURL || /^(?:http:\/\/)?(?:127\.0\.0\.1|localhost):4173(?:\/|$)/iu.test(baseURL)) {
      throw new Error('FUNC-ANN-001 requires the real ASP.NET Core backend, not the static Angular server.');
    }

    if (!smokeEmail.toLowerCase().endsWith('@example.test') || !smokePassword) {
      throw new Error('FUNC-ANN-001 requires deterministic synthetic @example.test credentials.');
    }
  });

  test.beforeEach(({ browserName }, testInfo) => {
    test.skip(
      browserName !== 'chromium' || testInfo.project.name !== 'functional-chromium',
      'FUNC-ANN-001 mutates one isolated deterministic backend and runs only in the canonical Functional Chromium project.'
    );
  });

  test(
    'FUNC-ANN-001 reviews, publishes, reads, and acknowledges an authorized announcement',
    functionalMetadata({
      journeyId: 'FUNC-ANN-001',
      gates: ['functional-full', 'functional-extended'],
      domains: ['auth', 'workspace', 'announcement'],
      priority: 'p1',
      backend: 'real',
      polarity: 'positive'
    }),
    async ({ page }, testInfo) => {
      const api = page.context().request;
      const runToken = randomUUID().slice(0, 12);
      const announcementTitle = `FCI-06 collaboration announcement ${runToken}`;
      const announcementBody = `Synthetic FCI-06 recipient content ${runToken}`;
      const evidence: AnnouncementEvidence = {
        journeyId: 'FUNC-ANN-001',
        usedScheduledWallClockWait: false
      };
      let workspaceId = '';
      let draftId = '';
      let announcementId = '';

      try {
        await test.step('FUNC-ANN-001 / ANN-01 actor A resolves an authorized audience', async () => {
          await loginViaApi(api, actorACredentials());
          workspaceId = await resolveWorkspaceId(api);
          evidence.workspaceId = workspaceId;

          const audience = await resolveWorkspaceAudience(api, workspaceId);
          evidence.audienceRecipientCount = readNumber(audience, 'estimatedRecipientCount');
        });

        await test.step('FUNC-ANN-001 / ANN-02 author uses the production Angular review and preview flow', async () => {
          const audience = await resolveWorkspaceAudience(api, workspaceId);
          const audienceKey = requireStringField(audience, 'key');

          await page.goto('/app/announcements');
          await expect(page.getByTestId('create-announcement-action')).toBeVisible();
          await page.getByTestId('create-announcement-action').click();

          const editor = page.getByTestId('announcement-editor');
          await expect(editor).toBeVisible();
          await editor.getByTestId('announcement-editor-title').fill(announcementTitle);
          await editor.getByTestId('announcement-editor-body').fill(announcementBody);
          await editor.getByTestId('announcement-next-step').click();
          await editor.getByTestId('announcement-editor-audience').selectOption(audienceKey);
          await expect(editor.getByTestId('announcement-audience-summary')).toContainText(workspaceName);
          await editor.getByTestId('announcement-next-step').click();
          await editor.getByTestId('announcement-editor-priority').selectOption('normal');
          await editor.getByTestId('announcement-editor-read-confirmation').check();
          await editor.getByTestId('announcement-editor-delivery-now').check();

          await editor.getByTestId('announcement-preview-action').click();
          const preview = page.getByTestId('announcement-local-preview');
          await expect(preview).toBeVisible();
          await expect(preview).toContainText(announcementTitle);
          await expect(preview).toContainText(announcementBody);
          await editor.getByTestId('announcement-edit-action').click();

          await editor.getByTestId('announcement-next-step').click();
          const review = editor.getByTestId('announcement-review-summary');
          await expect(review).toBeVisible();
          await expect(review).toContainText(announcementTitle);
          await expect(review).toContainText(workspaceName);
          await editor.getByTestId('announcement-publish-action').click();
          const confirmationDialog = page.getByRole('dialog', {
            name: 'Confirm publication — Confirm delivery'
          });
          await expect(confirmationDialog).toBeVisible();
          await expect(confirmationDialog.getByTestId('announcement-publication-confirmation')).toBeVisible();
        });

        await test.step('FUNC-ANN-001 / ANN-03 confirmation creates a draft and bounded worker publication reaches Published', async () => {
          const recipientCount = evidence.audienceRecipientCount ?? 0;
          const draftCreateResponsePromise = page.waitForResponse((response) =>
            response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/announcement-drafts'
          );
          const publishResponsePromise = page.waitForResponse((response) =>
            response.request().method() === 'POST' &&
            /^\/api\/announcement-drafts\/[0-9a-f-]+\/publish$/iu.test(new URL(response.url()).pathname)
          );

          const confirmationDialog = page.getByRole('dialog', {
            name: 'Confirm publication — Confirm delivery'
          });
          const confirmButton = confirmationDialog.getByRole('button', {
            name: new RegExp(`^Publish to ${recipientCount} recipients now$`, 'u')
          });
          await expect(confirmButton).toBeVisible();
          await confirmButton.click();

          const draftCreateResponse = await draftCreateResponsePromise;
          evidence.createDraftStatus = draftCreateResponse.status();
          expect(draftCreateResponse.status(), await boundedResponsePreview(draftCreateResponse)).toBe(201);
          const draftCreated = asRecord(await draftCreateResponse.json(), 'announcement draft create response');
          draftId = requireStringField(draftCreated, 'id');
          evidence.draftId = draftId;

          const publishResponse = await publishResponsePromise;
          evidence.publishRequestStatus = publishResponse.status();
          expect(publishResponse.status(), await boundedResponsePreview(publishResponse)).toBe(200);

          const published = await waitForAuthoritativeState(
            async () => readAnnouncementDraft(api, draftId),
            {
              label: 'announcement immediate publication worker',
              timeoutMs: 20_000,
              isReady: (draft) =>
                readOptionalString(draft, 'status') === 'Published' &&
                readOptionalString(draft, 'publishedAnnouncementId') !== null
            }
          );
          evidence.workerFinalStatus = readOptionalString(published, 'status') ?? undefined;
          announcementId = requireStringField(published, 'publishedAnnouncementId');
          evidence.announcementId = announcementId;
        });

        await test.step('FUNC-ANN-001 / ANN-04 actor B reaches the published list/detail and persists read/ack state', async () => {
          await logoutViaApi(api);
          await loginViaApi(api, actorBCredentials());

          const recipientList = await readAnnouncementList(api, workspaceId);
          const listItem = recipientList.find((item) => readOptionalString(item, 'id') === announcementId);
          expect(listItem, 'published announcement appears in recipient authoritative list').toBeDefined();
          evidence.recipientListVisible = true;

          await page.goto(`/app/announcements/${announcementId}`);
          const detail = page.getByTestId('announcement-detail');
          await expect(detail).toBeVisible();
          await expect(detail).toContainText(announcementTitle);
          await expect(detail).toContainText(announcementBody);
          evidence.recipientDetailVisible = true;

          const markRead = await csrfAwareRequest(api, 'POST', `/api/announcements/${announcementId}/read`);
          await assertSafeResponse(markRead, { label: 'announcement recipient mark-read', expectedStatus: 200 });

          const acknowledge = await csrfAwareRequest(api, 'POST', `/api/announcements/${announcementId}/acknowledge`);
          await assertSafeResponse(acknowledge, { label: 'announcement recipient acknowledgement', expectedStatus: 200 });
          evidence.recipientAcknowledged = true;

          const readDetail = await waitForAuthoritativeState(
            () => readAnnouncementDetail(api, announcementId),
            {
              label: 'announcement recipient read state',
              isReady: (value) => value.isRead === true
            }
          );
          expect(readDetail.isRead).toBe(true);

          await logoutViaApi(api);
          await loginViaApi(api, actorBCredentials());
          const afterReentry = await readAnnouncementDetail(api, announcementId);
          expect(afterReentry.isRead).toBe(true);
          evidence.recipientReadPersisted = true;
        });
      } finally {
        await testInfo.attach('func-ann-001-evidence.json', {
          body: Buffer.from(JSON.stringify(evidence, null, 2)),
          contentType: 'application/json'
        });
      }
    }
  );
});

function actorACredentials(): SyntheticCredentials {
  return { email: smokeEmail, password: smokePassword };
}

function actorBCredentials(): SyntheticCredentials {
  return { email: recipientEmail, password: `${smokePassword}:recipient` };
}

async function resolveWorkspaceId(api: APIRequestContext): Promise<string> {
  const response = await api.get('/api/workspaces');
  await assertSafeResponse(response, { label: 'announcement Workspace lookup', expectedStatus: 200 });
  const body = await response.json();
  if (!Array.isArray(body)) {
    throw new Error('Announcement Workspace lookup did not return an array.');
  }
  const workspace = body
    .map((value) => asRecord(value, 'Workspace list item'))
    .find((value) => readOptionalString(value, 'name') === workspaceName);
  if (!workspace) {
    throw new Error(`Synthetic Workspace ${workspaceName} is unavailable.`);
  }
  return requireStringField(workspace, 'id');
}

async function resolveWorkspaceAudience(
  api: APIRequestContext,
  workspaceId: string
): Promise<Record<string, unknown>> {
  const response = await api.get('/api/announcements/audiences');
  await assertSafeResponse(response, { label: 'announcement audience lookup', expectedStatus: 200 });
  const body = await response.json();
  if (!Array.isArray(body)) {
    throw new Error('Announcement audience lookup did not return an array.');
  }
  const audience = body
    .map((value) => asRecord(value, 'announcement audience option'))
    .find((value) => readOptionalString(value, 'workspaceId') === workspaceId);
  if (!audience) {
    throw new Error(`Authorized Workspace announcement audience ${workspaceId} is unavailable.`);
  }
  return audience;
}

async function readAnnouncementDraft(api: APIRequestContext, draftId: string): Promise<Record<string, unknown>> {
  const response = await api.get(`/api/announcement-drafts/${draftId}`);
  await assertSafeResponse(response, { label: 'announcement draft authoritative read', expectedStatus: 200 });
  return asRecord(await response.json(), 'announcement draft authoritative read');
}

async function readAnnouncementList(
  api: APIRequestContext,
  workspaceId: string
): Promise<Record<string, unknown>[]> {
  const response = await api.get(`/api/announcements?page=1&pageSize=100&workspaceId=${workspaceId}`);
  await assertSafeResponse(response, { label: 'recipient announcement list', expectedStatus: 200 });
  return readPagedItems(await response.json(), 'recipient announcement list');
}

async function readAnnouncementDetail(
  api: APIRequestContext,
  announcementId: string
): Promise<Record<string, unknown>> {
  const response = await api.get(`/api/announcements/${announcementId}`);
  await assertSafeResponse(response, { label: 'recipient announcement detail', expectedStatus: 200 });
  return asRecord(await response.json(), 'recipient announcement detail');
}

function readPagedItems(value: unknown, label: string): Record<string, unknown>[] {
  const response = asRecord(value, label);
  const items = response.items ?? response.Items;
  if (!Array.isArray(items)) {
    throw new Error(`${label} did not contain an items array.`);
  }
  return items.map((item) => asRecord(item, `${label} item`));
}

function asRecord(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} was not a JSON object.`);
  }
  return value as Record<string, unknown>;
}

function requireStringField(record: Record<string, unknown>, ...keys: string[]): string {
  const value = readOptionalString(record, ...keys);
  if (!value) {
    throw new Error(`Required string field is missing: ${keys.join(' / ')}`);
  }
  return value;
}

function readOptionalString(record: Record<string, unknown>, ...keys: string[]): string | null {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }
  return null;
}

function readNumber(record: Record<string, unknown>, key: string): number {
  const value = record[key];
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new Error(`Required numeric field is missing: ${key}`);
  }
  return value;
}

async function boundedResponsePreview(response: APIResponse | PlaywrightResponse): Promise<string> {
  try {
    const text = await response.text();
    return text.length <= 1024 ? text : `${text.slice(0, 1024)}…[TRUNCATED]`;
  } catch {
    return '[response body unavailable]';
  }
}
