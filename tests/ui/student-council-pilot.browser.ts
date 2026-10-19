import { createHash, randomUUID } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { expect, test, type Page, type Response } from '@playwright/test';
import { isUuid } from './public-https-golden-path-helpers.mjs';
import { readPilotConfiguration, validateCheckpoint, validateRestartRecord, validateExerciseReceipt, isRedactedDenial, sanitizedReceipt, assertBoundOrigin, scopeReadPaths } from './student-council-pilot-helpers.mjs';
import { installChromiumOriginGuard } from './student-council-pilot-origin-guard.mjs';

const fixture = JSON.parse(await readFile(process.env.COGLATAS_STUDENT_PILOT_FIXTURE!, 'utf8'));
const configuration = readPilotConfiguration(process.env, fixture);
const evidence = process.env.COGLATAS_STUDENT_PILOT_EVIDENCE_DIR!;
const allowed = configuration.fixture.allowed;
const guardedPages = new WeakSet<Page>();

// This suffix intentionally excludes the gate from the default *.spec.ts suite.
// No route.fulfill, API mocks, seed SQL, or production provisioning is used.
test('Student Council core pilot uses real HTTPS, persisted messaging and current authorization', async ({ page, context, browser }) => {
  const operatorContext = await browser.newContext({ baseURL: configuration.baseURL, locale: 'en-US', serviceWorkers: 'block' });
  const revokeeContext = await browser.newContext({ baseURL: configuration.baseURL, locale: 'en-US', serviceWorkers: 'block' });
  let crossedOrigin = false;
  await bindPageOrigin(page, () => { crossedOrigin = true; });
  try {
    await test.step('public HTTPS and browser login', async () => {
      const httpURL = new URL(configuration.baseURL);
      httpURL.protocol = 'http:';
      const redirect = await context.request.get(httpURL.toString(), { maxRedirects: 0 });
      const location = redirect.headers().location;
      check([301, 302, 307, 308].includes(redirect.status()) && !!location && new URL(location, httpURL).origin === configuration.baseURL, 'public HTTPS redirect');
      const ready = await context.request.get('/health/ready', { maxRedirects: 0 });
      check(ready.status() === 200, 'public readiness');
      await login(page, 'member', configuration.fixture.memberUserId);
      const cookies = await context.cookies();
      for (const name of ['.Coglatas.Auth', '.Coglatas.Csrf']) check(cookies.some((c) => c.name === name && c.secure && c.httpOnly && c.sameSite === 'Lax'), 'secure cookies');
    });

    const operator = await operatorContext.newPage();
    await bindPageOrigin(operator, () => { crossedOrigin = true; });
    await login(operator, 'operator', configuration.fixture.operatorUserId);
    await test.step('known fixture controls and negative matrix', async () => {
      await positiveScope(page, allowed);
      // Prove same-Tenant deny targets exist using a separately authorized actor.
      await positiveScope(operator, configuration.fixture.deniedSameTenant);
      const settings = await api(operator, '/api/tenant/settings');
      const features = await api(operator, '/api/tenant/features');
      check(settings.status === 200 && features.status === 200, 'operator Tenant configuration control');
      let flags: Record<string, unknown>;
      try { flags = JSON.parse(json(settings).featureFlagsJson); } catch { throw new Error('Pilot requires readable Tenant feature overrides.'); }
      check(flags.FileSharing === false && Array.isArray(json(features).enabledFeatures) && !json(features).enabledFeatures.some((key: unknown) => typeof key === 'string' && key.toLowerCase() === 'filesharing'), 'server upload-intake FileSharing override');
      const list = await api(page, '/api/workspaces');
      check(list.status === 200, 'Workspace list');
      const listIds = rows(json(list)).map((row) => row.id);
      check(listIds.includes(allowed.workspaceId) && !listIds.includes(configuration.fixture.deniedSameTenant.workspaceId) && !listIds.includes(configuration.fixture.deniedCrossTenant.workspaceId), 'Workspace list isolation');
      for (const scope of [configuration.fixture.deniedSameTenant, configuration.fixture.deniedCrossTenant]) await deniedScope(page, scope);
      const csrfDenied = await api(page, `/api/conversations/${allowed.channelId}/messages`, 'POST', { body: 'Synthetic missing CSRF control', clientRequestId: randomUUID() }, false);
      check(isRedactedDenial(csrfDenied, [allowed.channelId]), 'missing CSRF denial');
      const group = await api(page, '/api/conversations', 'POST', { type: 7, title: 'Synthetic unsupported Group control', memberUserIds: [configuration.fixture.operatorUserId], workspaceId: allowed.workspaceId });
      check(isRedactedDenial(group, [allowed.workspaceId]), 'unsupported Group denial');
      await assertFileIntakeDenied(page);
    });

    let checkpoint: Record<string, any>;
    if (configuration.phase === 'exercise') {
      const runId = randomUUID();
      checkpoint = await test.step('Channel, MessageThread and DirectDM browser writes', async () => {
        const root = await sendFromBrowser(page, allowed.channelId, 'channel', `Synthetic pilot ${runId} channel`);
        await page.getByTestId(`open-message-thread-${root.id}`).click();
        await expect(page.getByTestId('thread-root-message')).toBeVisible();
        const replyResponse = page.waitForResponse((r) => matchesResponse(r, `/api/messages/${root.id}/thread/messages`));
        await page.getByTestId('thread-reply-draft').fill(`Synthetic pilot ${runId} reply`);
        await page.getByTestId('send-thread-reply').click();
        const response = await replyResponse;
        check(response.status() === 200, 'thread reply persistence');
        const reply = (await response.json()).message;
        check(isUuid(reply?.id) && reply.threadRootMessageId === root.id && reply.conversationId === allowed.channelId, 'canonical MessageThread identity');
        await expect(page.getByTestId('thread-replies')).toContainText(`Synthetic pilot ${runId} reply`);
        const direct = await sendFromBrowser(page, allowed.directId, 'dm', `Synthetic pilot ${runId} direct`);
        return { version: 1, phase: 'exercise', binding: configuration.binding, commit: configuration.commit, groupDm: 'NO_GO', runId, rootMessageId: root.id, replyMessageId: reply.id, directMessageId: direct.id };
      });
    } else {
      checkpoint = validateCheckpoint(JSON.parse(await readFile(join(evidence, 'checkpoint.json'), 'utf8')), configuration);
      validateExerciseReceipt(JSON.parse(await readFile(join(evidence, 'exercise-receipt.json'), 'utf8')), checkpoint);
      validateRestartRecord(JSON.parse(await readFile(join(evidence, 'restart.json'), 'utf8')), checkpoint);
    }

    await test.step('reload, File read/download and logout persistence', async () => {
      await verifyDurableMessages(page, checkpoint);
      await navigate(page, `/app/workspaces/${allowed.workspaceId}/channels/${allowed.channelId}?focusMessageId=${checkpoint.replyMessageId}&threadRootMessageId=${checkpoint.rootMessageId}`);
      await page.reload();
      await expect(page.getByTestId('channel-messaging-page')).toBeVisible();
      await expect(page.getByTestId('thread-root-message')).toContainText(`Synthetic pilot ${checkpoint.runId} channel`);
      await expect(page.getByTestId('thread-replies')).toContainText(`Synthetic pilot ${checkpoint.runId} reply`);
      await verifyDurableMessages(page, checkpoint);
      await fileThroughBrowser(page);
      await page.getByTestId('logout-action').click();
      await expect(page.getByTestId('login-page')).toBeVisible();
      for (const path of scopeReadPaths(allowed)) check((await api(page, path)).status === 401, 'logout protected resource denial');
      await login(page, 'member', configuration.fixture.memberUserId);
      await verifyDurableMessages(page, checkpoint);
      await assertFileBytes(page, allowed.fileId);
    });
    if (configuration.phase === 'exercise') {
      await test.step('same-session revocation and stale grant denial', async () => {
        const revokee = await revokeeContext.newPage();
        await bindPageOrigin(revokee, () => { crossedOrigin = true; });
        await login(revokee, 'revokee', configuration.fixture.revocation.userId);
        await revocationBoundary(operator, revokee);
      });
      check(!crossedOrigin, 'all exercise requests stayed on the configured origin');
      checkpoint.completedAtUtc = new Date().toISOString();
      await privateJson('checkpoint.json', checkpoint);
    }
    check(!crossedOrigin, 'all browser requests stayed on the configured origin');
    await privateJson(`${configuration.phase}-receipt.json`, sanitizedReceipt(configuration, configuration.phase));
  } finally {
    await operatorContext.close();
    await revokeeContext.close();
  }
});

async function login(page: Page, account: 'member' | 'operator' | 'revokee', userId: string) {
  check(guardedPages.has(page), 'credential page has its redirect-aware origin guard');
  const response = await navigate(page, '/app/login');
  const headers = response?.headers() ?? {};
  check(/max-age=[1-9]\d*/iu.test(headers['strict-transport-security'] ?? ''), 'HSTS');
  check(headers['x-content-type-options'] === 'nosniff' && headers['x-frame-options'] === 'DENY', 'browser security headers');
  await page.evaluate(() => localStorage.setItem('coglatas.locale', 'en'));
  await page.reload();
  assertBoundOrigin(page.url(), configuration.baseURL);
  await expect(page.getByTestId('login-page')).toBeVisible();
  const pending = page.waitForResponse((r) => matchesResponse(r, '/api/auth/login'));
  assertBoundOrigin(page.url(), configuration.baseURL);
  await page.getByTestId('login-email').fill(configuration.accounts[account].email);
  assertBoundOrigin(page.url(), configuration.baseURL);
  await page.getByTestId('login-password').fill(configuration.accounts[account].password);
  assertBoundOrigin(page.url(), configuration.baseURL);
  await page.getByTestId('login-submit').click();
  check((await pending).status() === 200, 'synthetic browser login');
  await expect(page.getByTestId('app-shell')).toBeVisible();
  const me = await api(page, '/api/auth/me');
  check(me.status === 200 && json(me).userId === userId, 'authenticated fixture identity');
}

async function positiveScope(page: Page, scope: Record<string, any>) {
  for (const path of scopeReadPaths(scope)) check((await api(page, path)).status === 200, 'known authorized fixture read');
  const channel = json(await api(page, `/api/conversations/${scope.channelId}`));
  const direct = json(await api(page, `/api/conversations/${scope.directId}`));
  const thread = json(await api(page, `/api/messages/${scope.rootMessageId}/thread`));
  check(channel.type === 'WorkspaceChannel' && channel.workspaceId === scope.workspaceId && channel.projectId === null, 'Conversation-backed Workspace Channel');
  check(direct.type === 'DirectMessage' && direct.workspaceId === scope.workspaceId && direct.projectId === null, 'unbound DirectDM fixture scope');
  const messages = json(await api(page, `/api/conversations/${scope.directId}/messages?anchorMessageId=${scope.directMessageId}`));
  check(rows(messages).some((message) => message.id === scope.directMessageId && message.conversationId === scope.directId && typeof message.body === 'string' && message.body.trim().length > 0 && message.isDeleted === false), 'known persisted Direct body');
  check(thread.rootMessage?.conversationId === scope.channelId && thread.rootMessage?.threadRootMessageId === null, 'same-Conversation MessageThread');
  check(unwrapped(json(await api(page, `/api/files/${scope.fileId}`))).workspaceId === scope.workspaceId, 'Workspace File scope');
}

async function deniedScope(page: Page, scope: Record<string, any>) {
  const ids = [scope.workspaceId, scope.channelId, scope.directId, scope.directMessageId, scope.rootMessageId, scope.fileId];
  for (const path of [...scopeReadPaths(scope), `/api/files/${scope.fileId}/download`]) check(isRedactedDenial(await api(page, path), ids), 'resource read isolation');
  for (const [path, body] of [
    [`/api/conversations/${scope.channelId}/messages`, { body: 'Synthetic denied Channel write', clientRequestId: randomUUID() }],
    [`/api/conversations/${scope.directId}/messages`, { body: 'Synthetic denied DirectDM write', clientRequestId: randomUUID() }],
    [`/api/messages/${scope.rootMessageId}/thread/messages`, { body: 'Synthetic denied thread reply', clientRequestId: randomUUID() }],
    [`/api/files/${scope.fileId}/download-grants`, {}]
  ] as const) check(isRedactedDenial(await api(page, path, 'POST', body), ids), 'resource write isolation');
}

async function sendFromBrowser(page: Page, id: string, kind: 'channel' | 'dm', body: string) {
  await navigate(page, kind === 'dm' ? `/app/dm/${id}` : `/app/workspaces/${allowed.workspaceId}/channels/${id}`);
  await expect(page.getByTestId(kind === 'dm' ? 'dm-page' : 'channel-messaging-page')).toBeVisible();
  const pending = page.waitForResponse((r) => matchesResponse(r, `/api/conversations/${id}/messages`));
  await page.getByTestId('message-draft').fill(body);
  await page.getByTestId('send-message').click();
  const response = await pending;
  check(response.status() === 200, 'browser message send');
  const message = await response.json();
  check(isUuid(message.id) && message.conversationId === id && message.body === body && message.threadRootMessageId === null, 'server message identity');
  await expect(page.getByTestId('message-body').filter({ hasText: body })).toBeVisible();
  return message;
}

async function verifyDurableMessages(page: Page, checkpoint: Record<string, any>) {
  const result = await api(page, `/api/messages/${checkpoint.rootMessageId}/thread`);
  check(result.status === 200, 'durable thread read');
  const thread = json(result);
  check(thread.rootMessage?.id === checkpoint.rootMessageId && thread.rootMessage?.body === `Synthetic pilot ${checkpoint.runId} channel`, 'durable Channel body');
  check(rows(thread.replies).some((m) => m.id === checkpoint.replyMessageId && m.threadRootMessageId === checkpoint.rootMessageId && m.body === `Synthetic pilot ${checkpoint.runId} reply`), 'durable reply body');
  const direct = await api(page, `/api/conversations/${allowed.directId}/messages?anchorMessageId=${checkpoint.directMessageId}`);
  check(direct.status === 200 && rows(json(direct)).some((m) => m.id === checkpoint.directMessageId && m.body === `Synthetic pilot ${checkpoint.runId} direct`), 'durable DirectDM body');
}

async function fileThroughBrowser(page: Page) {
  const metadata = unwrapped(json(await api(page, `/api/files/${allowed.fileId}`)));
  await navigate(page, `/app/workspaces/${allowed.workspaceId}/files`);
  await expect(page.getByTestId('files-page')).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: metadata.originalFileName });
  await expect(row).toHaveCount(1);
  const pending = page.waitForEvent('download');
  await row.getByRole('button', { name: 'Download', exact: true }).click();
  const download = await pending;
  check(await download.failure() === null, 'browser File download');
  const stream = await download.createReadStream();
  check(stream !== null, 'browser File stream');
  const hash = createHash('sha256');
  let size = 0;
  for await (const chunk of stream!) { size += chunk.length; hash.update(chunk); }
  check(size === allowed.fileSizeBytes && hash.digest('hex') === allowed.fileSha256, 'browser File bytes');
  await download.delete();
}

async function assertFileBytes(page: Page, id: string) {
  const grantResponse = await api(page, `/api/files/${id}/download-grants`, 'POST', {});
  check(grantResponse.status === 200, 'File grant issuance');
  const grant = unwrapped(json(grantResponse));
  check(isUuid(grant.fileDownloadGrantId) && grant.fileObjectId === id && typeof grant.token === 'string', 'canonical File grant');
  const bytes = await api(page, `/api/file-download-grants/${grant.fileDownloadGrantId}/download`, 'POST', { token: grant.token }, true, true);
  check(bytes.status === 200 && bytes.size === allowed.fileSizeBytes && bytes.sha256 === allowed.fileSha256, 'grant-backed File bytes');
}

async function assertFileIntakeDenied(page: Page) {
  assertBoundOrigin(page.url(), configuration.baseURL);
  const result = await page.evaluate(async ({ workspaceId, expectedOrigin }) => {
    if (location.origin !== expectedOrigin) throw new Error('Pilot File intake left its configured origin.');
    const csrfResponse = await fetch(new URL('/api/security/csrf-token', expectedOrigin), { credentials: 'include', redirect: 'error' });
    if (new URL(csrfResponse.url).origin !== expectedOrigin) throw new Error('Pilot CSRF response crossed its configured origin.');
    const csrf = await csrfResponse.json();
    const form = new FormData();
    form.append('OwnerType', '6'); // AttachmentOwnerType.Workspace
    form.append('OwnerId', workspaceId);
    form.append('File', new File(['Synthetic pilot upload intake control\n'], 'pilot-intake-control.txt', { type: 'text/plain' }));
    if (location.origin !== expectedOrigin) throw new Error('Pilot File intake left its configured origin.');
    const response = await fetch(new URL('/api/files', expectedOrigin), { method: 'POST', credentials: 'include', redirect: 'error', headers: { [csrf.headerName]: csrf.token }, body: form });
    if (new URL(response.url).origin !== expectedOrigin) throw new Error('Pilot File response crossed its configured origin.');
    return { status: response.status, text: await response.text() };
  }, { workspaceId: allowed.workspaceId, expectedOrigin: configuration.baseURL });
  check(isRedactedDenial(result, [allowed.workspaceId]), 'valid File upload intake denial');
}

async function revocationBoundary(operator: Page, revokee: Page) {
  const scope = configuration.fixture.revocation;
  await positiveScope(revokee, scope);
  const rootBody = json(await api(revokee, `/api/messages/${scope.rootMessageId}/thread`)).rootMessage?.body;
  check(typeof rootBody === 'string' && rootBody.length > 0, 'synthetic revocation root');
  const grant = unwrapped(json(await api(revokee, `/api/files/${scope.fileId}/download-grants`, 'POST', {})));
  check(isUuid(grant.fileDownloadGrantId) && typeof grant.token === 'string', 'pre-revocation grant');
  await navigate(revokee, `/app/workspaces/${scope.workspaceId}/channels/${scope.channelId}`);
  await expect(revokee.getByText(rootBody, { exact: true })).toBeVisible();
  const memberPath = `/api/workspaces/${scope.workspaceId}/members/${scope.userId}`;
  const membership = unwrapped(json(await api(operator, `${memberPath}/management`)));
  check(membership.role === 3 && membership.status === 1, 'revokee is an ordinary active Member');
  // DELETE suspends the existing membership. PATCH reactivates that same row;
  // no raw SQL, bootstrap reset, account deletion or new grant is used.
  try {
    check((await api(operator, memberPath, 'DELETE')).status === 200, 'synthetic membership revocation');
    const ids = [scope.workspaceId, scope.channelId, scope.directId, scope.directMessageId, scope.rootMessageId, scope.fileId];
    for (const path of scopeReadPaths(scope)) check(isRedactedDenial(await api(revokee, path), ids), 'same-session revocation read denial');
    check(isRedactedDenial(await api(revokee, `/api/conversations/${scope.channelId}/messages`, 'POST', { body: 'Synthetic revoked send', clientRequestId: randomUUID() }), ids), 'same-session revoked write denial');
    check(isRedactedDenial(await api(revokee, `/api/conversations/${scope.directId}/messages`, 'POST', { body: 'Synthetic revoked Direct send', clientRequestId: randomUUID() }), ids), 'same-session revoked Direct write denial');
    check(isRedactedDenial(await api(revokee, `/api/file-download-grants/${grant.fileDownloadGrantId}/download`, 'POST', { token: grant.token }), ids), 'stale File grant revocation');
    await expect(revokee.getByText(rootBody, { exact: true })).not.toBeVisible();
    await revokee.reload();
    for (const path of scopeReadPaths(scope)) check(isRedactedDenial(await api(revokee, path), ids), 'revocation after reload');
  } finally {
    check((await api(operator, memberPath, 'PATCH', { role: 3, status: 1 })).status === 200, 'synthetic membership restoration');
    check((await api(revokee, `/api/workspaces/${scope.workspaceId}`)).status === 200, 'restored membership control');
  }
}

interface ApiResult { status: number; text: string; size?: number; sha256?: string; }
async function api(page: Page, path: string, method = 'GET', body?: unknown, csrf = true, binary = false): Promise<ApiResult> {
  assertBoundOrigin(page.url(), configuration.baseURL);
  assertBoundOrigin(new URL(path, configuration.baseURL).toString(), configuration.baseURL);
  return page.evaluate(async ({ path, method, body, csrf, binary, expectedOrigin }) => {
    if (location.origin !== expectedOrigin) throw new Error('Pilot API request left its configured origin.');
    const headers: Record<string, string> = {};
    if (method !== 'GET' && csrf) {
      const response = await fetch(new URL('/api/security/csrf-token', expectedOrigin), { credentials: 'include', redirect: 'error' });
      if (new URL(response.url).origin !== expectedOrigin) throw new Error('Pilot CSRF response crossed its configured origin.');
      const token = await response.json();
      if (token.headerName && token.token) headers[token.headerName] = token.token;
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (location.origin !== expectedOrigin) throw new Error('Pilot API request left its configured origin.');
    const response = await fetch(new URL(path, expectedOrigin), { method, headers, credentials: 'include', redirect: 'error', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
    if (new URL(response.url).origin !== expectedOrigin) throw new Error('Pilot API response crossed its configured origin.');
    if (binary && response.ok) {
      const bytes = await response.arrayBuffer();
      const digest = await crypto.subtle.digest('SHA-256', bytes);
      return { status: response.status, text: '', size: bytes.byteLength, sha256: Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('') };
    }
    return { status: response.status, text: await response.text() };
  }, { path, method, body, csrf, binary, expectedOrigin: configuration.baseURL });
}
function json(value: ApiResult): Record<string, any> { try { return JSON.parse(value.text); } catch { throw new Error('Pilot response was not the required JSON contract.'); } }
function unwrapped(value: Record<string, any>) { return value.data ?? value; }
function rows(value: any): Record<string, any>[] { return Array.isArray(value) ? value : Array.isArray(value?.items) ? value.items : []; }
function check(condition: boolean, label: string): asserts condition { if (!condition) throw new Error(`Pilot check failed: ${label}.`); }
async function navigate(page: Page, path: string) {
  try {
    const url = new URL(path, configuration.baseURL).toString();
    assertBoundOrigin(url, configuration.baseURL);
    const response = await page.goto(url);
    assertBoundOrigin(page.url(), configuration.baseURL);
    if (response) assertBoundOrigin(response.url(), configuration.baseURL);
    return response;
  } catch { throw new Error('Pilot application navigation failed or left its configured origin.'); }
}
function matchesResponse(response: Response, path: string): boolean {
  const url = new URL(response.url());
  return url.origin === configuration.baseURL && url.pathname === path && response.request().method() === 'POST';
}
async function bindPageOrigin(page: Page, onViolation: () => void) {
  // Pause actual Chromium requests at every redirect hop; no responses are
  // fabricated, mocked or rewritten. Guard failure is a qualification failure.
  await installChromiumOriginGuard(page, configuration.baseURL, onViolation);
  guardedPages.add(page);
}
async function privateJson(name: string, value: unknown) { await writeFile(join(evidence, name), JSON.stringify(value, null, 2) + '\n', { mode: 0o600, flag: 'wx' }); }
