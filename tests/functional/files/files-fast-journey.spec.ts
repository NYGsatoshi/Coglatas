/* eslint-disable max-lines, max-lines-per-function -- FCI-05 is an end-to-end owner journey; keep the complete mutation/cleanup contract visible in one spec. */
import { randomUUID } from 'node:crypto';
import { expect, type APIRequestContext, type APIResponse, type Response, test } from '@playwright/test';

import { functionalMetadata } from '../fixtures/functional-metadata.mjs';
import { loginViaApi } from '../helpers/auth';
import { csrfAwareRequest } from '../helpers/csrf';
import { assertSafeResponse, safeResponsePreview } from '../helpers/safe-response';

const smokeEmail = process.env.COGLATAS_BROWSER_SMOKE_EMAIL ?? '';
const smokePassword = process.env.COGLATAS_BROWSER_SMOKE_PASSWORD ?? '';
const smokeWorkspaceTitle = 'Browser Smoke Workspace';

test.describe('FCI-05 Files real-backend fast journey', () => {
  test.setTimeout(120_000);

  test.beforeAll(() => {
    if (process.env.COGLATAS_REAL_BACKEND_SMOKE !== '1') {
      throw new Error('FCI-05 requires COGLATAS_REAL_BACKEND_SMOKE=1 and the canonical Functional Compose harness.');
    }
    if (!process.env.PLAYWRIGHT_BASE_URL || !smokeEmail.toLowerCase().endsWith('@example.test') || !smokePassword) {
      throw new Error('FCI-05 requires the isolated real-backend Functional fixture profile.');
    }
  });

  test.beforeEach(({ browserName }, testInfo) => {
    test.skip(
      browserName !== 'chromium' || testInfo.project.name !== 'chromium-desktop',
      'FCI-05 mutates isolated test storage and therefore runs once per Functional Compose project.',
    );
  });

  test(
    'FUNC-FILE-002 uploads, browses, inspects, downloads, reloads, and safely removes one FileObject',
    functionalMetadata({
      journeyId: 'FUNC-FILE-002',
      gates: ['functional-fast', 'functional-full', 'functional-extended'],
      domains: ['auth', 'workspace', 'files'],
      priority: 'p0',
      backend: 'real',
      polarity: 'positive',
    }),
    async ({ page }, testInfo) => {
      const api = page.context().request;
      const runToken = randomUUID();
      const fileName = `fci05-${runToken}.txt`;
      const failedFileName = `fci05-invalid-${runToken}.txt`;
      const fileContent = `FCI-05 isolated Files evidence ${runToken}\n`;
      let fileObjectId: string | null = null;
      let cleanupSucceeded = false;

      const evidence: Record<string, unknown> = {
        journeyId: 'FUNC-FILE-002',
        fileName,
        workspaceId: null,
        fileObjectId: null,
        uploadStatus: null,
        freshReadStatus: null,
        downloadStatus: null,
        reloadReadStatus: null,
        sharingAccessState: null,
        failedMutationStatus: null,
        deletedReadStatus: null,
        deletedGrantStatus: null,
        cleanupSucceeded: false,
      };

      try {
        await loginViaApi(api, { email: smokeEmail, password: smokePassword });
        const workspaceId = await resolveWorkspaceId(api, smokeWorkspaceTitle);
        evidence.workspaceId = workspaceId;

        // A rejected upload must not manufacture a FileObject or storage-visible metadata.
        const rejectedUpload = await csrfAwareRequest(api, 'POST', '/api/files', {
          multipart: Object.fromEntries([
            ['OwnerType', 'Workspace'],
            ['OwnerId', workspaceId],
            ['File', { name: failedFileName, mimeType: 'text/plain', buffer: Buffer.alloc(0) }],
          ]),
        });
        expect(rejectedUpload.status(), await safeResponsePreview(rejectedUpload)).toBe(400);
        evidence.failedMutationStatus = rejectedUpload.status();
        expect(await fileNamesForWorkspace(api, workspaceId)).not.toContain(failedFileName);

        await page.goto(`/app/workspaces/${workspaceId}/files`);
        await expect(page.getByTestId('files-page')).toBeVisible();

        const uploadResponsePromise = page.waitForResponse((response) =>
          response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/files',
        );
        await page.locator('app-coglatas-file-uploader input[type="file"]').setInputFiles({
          name: fileName,
          mimeType: 'text/plain',
          buffer: Buffer.from(fileContent, 'utf8'),
        });
        const uploadResponse = await uploadResponsePromise;
        expect(uploadResponse.status(), await boundedPageResponsePreview(uploadResponse)).toBe(200);
        evidence.uploadStatus = uploadResponse.status();

        const uploadBody = asRecord(await uploadResponse.json(), 'File upload response');
        fileObjectId = requireStringField(uploadBody, 'fileObjectId', 'FileObjectId');
        evidence.fileObjectId = fileObjectId;

        const freshRead = await api.get(`/api/files/${fileObjectId}`);
        await assertSafeResponse(freshRead, { label: 'FCI-05 fresh FileObject read', expectedStatus: 200 });
        const freshBody = asRecord(await freshRead.json(), 'fresh FileObject read');
        expect(requireStringField(freshBody, 'originalFileName', 'OriginalFileName')).toBe(fileName);
        assertNoStorageLeak(freshBody);
        evidence.freshReadStatus = freshRead.status();

        const listAfterUpload = await readFileList(api, workspaceId);
        const uploadedListItem = listAfterUpload.find((item) =>
          readOptionalString(item, 'fileObjectId', 'FileObjectId') === fileObjectId,
        );
        if (!uploadedListItem) {
          throw new Error('Fresh list did not contain the uploaded FileObject.');
        }
        expect(readOptionalString(uploadedListItem, 'originalFileName', 'OriginalFileName')).toBe(fileName);
        assertNoStorageLeak(uploadedListItem);

        const previewAction = page.getByRole('button', { name: `Preview ${fileName}` });
        await expect(previewAction).toBeVisible({ timeout: 20_000 });
        await previewAction.click();
        const inspector = page.getByTestId('files-preview-pane');
        await expect(inspector).toBeVisible();
        await expect(inspector.getByRole('heading', { name: fileName })).toBeVisible();

        const sharingResponse = await api.get(`/api/files/${fileObjectId}/sharing`);
        await assertSafeResponse(sharingResponse, { label: 'FCI-05 File sharing read', expectedStatus: 200 });
        const sharing = asRecord(await sharingResponse.json(), 'File sharing response');
        const accessState = requireStringField(sharing, 'accessState', 'AccessState');
        evidence.sharingAccessState = accessState;
        await expect(inspector.getByTestId('files-preview-access-state')).toContainText(accessState);
        assertNoStorageLeak(sharing);

        await inspector.getByTestId('files-inspector-tab-details').click();
        await expect(inspector.getByTestId('files-inspector-panel-details')).toContainText(fileName);
        await inspector.getByTestId('files-inspector-tab-preview').click();
        await inspector.getByTestId('files-preview-more').click();

        const grantResponsePromise = page.waitForResponse((response) =>
          response.request().method() === 'POST' &&
          new URL(response.url()).pathname === `/api/files/${fileObjectId}/download-grants`,
        );
        const downloadResponsePromise = page.waitForResponse((response) => {
          const path = new URL(response.url()).pathname;
          return response.request().method() === 'POST' &&
            /^\/api\/file-download-grants\/[0-9a-f-]{36}\/download$/iu.test(path);
        });
        await inspector.getByTestId('files-preview-download').click();
        const [grantResponse, downloadResponse] = await Promise.all([grantResponsePromise, downloadResponsePromise]);
        expect(grantResponse.status(), await boundedPageResponsePreview(grantResponse)).toBe(200);
        expect(downloadResponse.status(), await boundedPageResponsePreview(downloadResponse)).toBe(200);
        expect((await downloadResponse.body()).toString('utf8')).toBe(fileContent);
        evidence.downloadStatus = downloadResponse.status();

        await page.reload();
        await expect(page.getByTestId('files-page')).toBeVisible();
        await expect(page.getByRole('button', { name: `Preview ${fileName}` })).toBeVisible({ timeout: 20_000 });

        const reloadRead = await api.get(`/api/files/${fileObjectId}`);
        await assertSafeResponse(reloadRead, { label: 'FCI-05 reload-backed FileObject read', expectedStatus: 200 });
        const reloadBody = asRecord(await reloadRead.json(), 'reload-backed FileObject read');
        expect(requireStringField(reloadBody, 'fileObjectId', 'FileObjectId')).toBe(fileObjectId);
        expect(requireStringField(reloadBody, 'originalFileName', 'OriginalFileName')).toBe(fileName);
        assertNoStorageLeak(reloadBody);
        evidence.reloadReadStatus = reloadRead.status();

        const deleteResponse = await csrfAwareRequest(
          api,
          'DELETE',
          `/api/files/${fileObjectId}?reason=fci-05-cleanup`,
        );
        await assertSafeResponse(deleteResponse, { label: 'FCI-05 cleanup delete', expectedStatus: 200 });
        cleanupSucceeded = true;
        evidence.cleanupSucceeded = true;

        expect(await fileNamesForWorkspace(api, workspaceId)).not.toContain(fileName);

        const deletedRead = await api.get(`/api/files/${fileObjectId}`);
        await assertSafeResponse(deletedRead, {
          label: 'FCI-05 deleted FileObject denial',
          expectedStatus: [400, 404],
        });
        const deletedReadPreview = await safeResponsePreview(deletedRead);
        expect(deletedReadPreview).not.toContain(fileName);
        assertNoSensitiveText(deletedReadPreview);
        evidence.deletedReadStatus = deletedRead.status();

        const deletedGrant = await csrfAwareRequest(
          api,
          'POST',
          `/api/files/${fileObjectId}/download-grants`,
          { data: { purpose: 'fci-05-deleted-denial' } },
        );
        await assertSafeResponse(deletedGrant, {
          label: 'FCI-05 deleted FileObject grant denial',
          expectedStatus: [400, 404],
        });
        const deletedGrantPreview = await safeResponsePreview(deletedGrant);
        expect(deletedGrantPreview).not.toContain(fileName);
        assertNoSensitiveText(deletedGrantPreview);
        evidence.deletedGrantStatus = deletedGrant.status();
      } finally {
        if (!cleanupSucceeded && fileObjectId) {
          try {
            const cleanup = await csrfAwareRequest(
              api,
              'DELETE',
              `/api/files/${fileObjectId}?reason=fci-05-finally-cleanup`,
            );
            cleanupSucceeded = cleanup.status() === 200;
            evidence.cleanupSucceeded = cleanupSucceeded;
          } catch {
            // The isolated Compose project is still volume-cleaned by the FCI-02 harness.
          }
        }

        await testInfo.attach('fci-05-files-fast-evidence.json', {
          body: JSON.stringify(evidence, null, 2),
          contentType: 'application/json',
        });
      }
    },
  );
});

async function resolveWorkspaceId(api: APIRequestContext, workspaceName: string): Promise<string> {
  const response = await api.get('/api/workspaces');
  await assertSafeResponse(response, { label: 'FCI-05 Workspace list', expectedStatus: 200 });
  const body: unknown = await response.json();
  if (!Array.isArray(body)) {
    throw new Error('FCI-05 Workspace list was not an array.');
  }
  const workspace = body
    .map((item) => asRecord(item, 'Workspace list item'))
    .find((item) => readOptionalString(item, 'name', 'Name') === workspaceName);
  if (!workspace) {
    throw new Error(`FCI-05 seeded Workspace '${workspaceName}' was not found.`);
  }
  return requireStringField(workspace, 'id', 'Id');
}

async function readFileList(api: APIRequestContext, workspaceId: string): Promise<Record<string, unknown>[]> {
  const response = await api.get(`/api/files?workspaceId=${encodeURIComponent(workspaceId)}&page=1&pageSize=100`);
  await assertSafeResponse(response, { label: 'FCI-05 File list', expectedStatus: 200 });
  const body = asRecord(await response.json(), 'File list response');
  const items = body.items ?? body.Items;
  if (!Array.isArray(items)) {
    throw new Error('FCI-05 File list response is missing items.');
  }
  return items.map((item) => asRecord(item, 'File list item'));
}

async function fileNamesForWorkspace(api: APIRequestContext, workspaceId: string): Promise<string[]> {
  const items = await readFileList(api, workspaceId);
  return items
    .map((item) => readOptionalString(item, 'originalFileName', 'OriginalFileName'))
    .filter((value): value is string => Boolean(value));
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

function assertNoStorageLeak(value: unknown): void {
  assertNoSensitiveText(JSON.stringify(value));
}

function assertNoSensitiveText(text: string): void {
  const normalized = text.toLowerCase();
  for (const forbidden of ['storagekey', 'storage_key', 'filepath', 'file_path', '/srv/', '/var/lib/', 'real_backend_smoke_uploads']) {
    expect(normalized).not.toContain(forbidden);
  }
}

async function boundedPageResponsePreview(response: APIResponse | Response): Promise<string> {
  try {
    const text = await response.text();
    return text.length <= 1024 ? text : `${text.slice(0, 1024)}…[TRUNCATED]`;
  } catch {
    return '[response body unavailable]';
  }
}
