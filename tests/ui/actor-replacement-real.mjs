import { createRequire } from 'node:module';
import { createHash, randomUUID } from 'node:crypto';
import { writeFileSync } from 'node:fs';
const require = createRequire('/work/package.json');
const { chromium, request } = require('@playwright/test');
const hash = value => createHash('sha256').update(String(value)).digest('hex');
const baseURL = process.env.PLAYWRIGHT_BASE_URL;
const password = process.env.COGLATAS_BROWSER_SMOKE_PASSWORD;
const emailA = process.env.COGLATAS_BROWSER_SMOKE_EMAIL;
const emailB = 'browser-smoke-recipient@example.test';
const emailC = 'browser-smoke-pr05-manager@example.test';
const output = process.env.ACTOR_RESULT_FILE;
const source = { frontend: process.env.ACTOR_FRONTEND_SHA, backend: process.env.ACTOR_BACKEND_SHA };
const report = { schemaVersion: 1, scope: 'ISOLATED_REAL_BROWSER_SAME_CONTEXT_ACTOR_REPLACEMENT', source,
  startedAtUtc: new Date().toISOString(), qualification: 'NOT_EXECUTED', release: 'RELEASE_NO_GO', observations: {}, failedCriteria: [] };
let browser, primaryPage, phase = 'PREREQUISITES';
const contexts = [];
function check(condition, code) { if (!condition) { const error = new Error(code); error.safeCode = code; throw error; } }
function save() { report.completedAtUtc = new Date().toISOString(); writeFileSync(output, JSON.stringify(report, null, 2) + '\n', { mode: 0o600 }); }
function rows(body) { return Array.isArray(body) ? body : body?.items ?? []; }
function id(body) { return body?.userId ?? body?.id; }
async function api(apiContext, path, method = 'GET', data) {
  const headers = {};
  if (!['GET', 'HEAD'].includes(method)) {
    const csrfResponse = await apiContext.get('/api/security/csrf-token');
    check(csrfResponse.status() === 200, 'CSRF_READ_DENIED');
    const csrf = await csrfResponse.json();
    check(typeof csrf.headerName === 'string' && typeof csrf.token === 'string', 'CSRF_SHAPE');
    headers[csrf.headerName] = csrf.token;
  }
  const response = await apiContext.fetch(path, { method, data, headers });
  return response;
}
async function loginApi(email, credential) {
  const context = await request.newContext({ baseURL, timeout: 30_000 }); contexts.push(context);
  const response = await api(context, '/api/auth/login', 'POST', { email, password: credential });
  check(response.status() === 200, 'SYNTHETIC_LOGIN_DENIED');
  const user = await response.json();
  check(typeof id(user) === 'string', 'SYNTHETIC_USER_ID');
  return { context, user, userId: id(user) };
}
async function waitUntil(predicate, timeout = 30_000) {
  const deadline = Date.now() + timeout;
  do { if (await predicate()) return true; await new Promise(resolve => setTimeout(resolve, 50)); } while (Date.now() < deadline);
  return false;
}
function observe(page, expectedBodies) {
  const sockets = [], messages = [];
  page.on('websocket', socket => {
    if (new URL(socket.url()).pathname !== '/hubs/app') return;
    const record = { ordinal: sockets.length, closed: false }; sockets.push(record);
    socket.on('close', () => { record.closed = true; });
    socket.on('framereceived', frame => {
      const text = typeof frame.payload === 'string' ? frame.payload : frame.payload.toString('utf8');
      for (const part of text.split('\u001e')) {
        if (!part) continue;
        let invocation; try { invocation = JSON.parse(part); } catch { continue; }
        if (invocation.type !== 1 || invocation.target !== 'DurableEvent') continue;
        const event = invocation.arguments?.[0], message = event?.payload?.message;
        if (event?.eventType !== 'Messaging.MessageCreated.v1' || !message || !expectedBodies.has(message.body)) continue;
        messages.push({ socket: record, messageId: message.id, conversationId: message.conversationId, bodySha256: hash(message.body) });
      }
    });
  });
  return { sockets, messages };
}
async function loginUi(page, email, credential) {
  await page.getByTestId('login-email').fill(email);
  await page.getByTestId('login-password').fill(credential);
  const responsePromise = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/auth/login');
  await page.getByTestId('login-submit').click();
  check((await responsePromise).status() === 200, 'UI_LOGIN_DENIED');
  await page.getByTestId('app-shell').waitFor({ state: 'visible' });
}
async function openDirect(page, conversationId) {
  await page.locator('[data-testid="nav-dm"]:visible').first().click();
  await page.getByTestId('messages-page').waitFor({ state: 'visible' });
  const entry = page.locator(`[data-testid="conversation-list-item"][data-conversation-id="${conversationId}"]`);
  await entry.waitFor({ state: 'visible' }); await entry.click();
  await page.getByTestId('dm-page').waitFor({ state: 'visible' });
}
async function send(context, conversationId, body) {
  const response = await api(context, `/api/conversations/${conversationId}/messages`, 'POST', { body, clientRequestId: randomUUID() });
  check(response.status() === 200, 'AUTHORIZED_SEND_FAILED');
  const sent = await response.json(); check(typeof sent.id === 'string', 'SENT_ID_SHAPE'); return sent;
}
async function run() {
  check(baseURL === 'http://127.0.0.1:8080' && output?.startsWith('/control/'), 'ISOLATED_TARGET_REQUIRED');
  check(emailA?.endsWith('@example.test') && password && /^[a-f0-9]{40}$/.test(source.frontend ?? '') && /^[a-f0-9]{40}$/.test(source.backend ?? ''), 'SYNTHETIC_BOUND_SOURCE_REQUIRED');
  phase = 'DISABLE_FILE_INTAKE';
  const owner = await loginApi(process.env.COGLATAS_SEED_ADMIN_EMAIL, process.env.COGLATAS_SEED_ADMIN_PASSWORD);
  const settingsResponse = await api(owner.context, '/api/tenant/settings'); check(settingsResponse.status() === 200, 'OWNER_SETTINGS_READ');
  const settings = await settingsResponse.json();
  const flags = JSON.parse(settings.featureFlagsJson || '{}'); flags.FileSharing = false;
  check((await api(owner.context, '/api/tenant/settings', 'PATCH', { featureFlagsJson: JSON.stringify(flags) })).status() === 200, 'OWNER_SETTINGS_PATCH');
  const saved = await (await api(owner.context, '/api/tenant/settings')).json();
  const features = await (await api(owner.context, '/api/tenant/features')).json();
  check(JSON.parse(saved.featureFlagsJson).FileSharing === false && Array.isArray(features.enabledFeatures) && !features.enabledFeatures.some(x => String(x).toLowerCase() === 'filesharing'), 'UPLOAD_INTAKE_NOT_DISABLED');
  report.observations.fileSharingFalse = true;
  phase = 'SYNTHETIC_FIXTURE';
  const a = await loginApi(emailA, password), b = await loginApi(emailB, `${password}:recipient`), c = await loginApi(emailC, password);
  check(new Set([a.userId, b.userId, c.userId]).size === 3, 'DISTINCT_SYNTHETIC_ACTORS');
  const tenantIds = [];
  for (const actor of [a, b, c]) { const response = await api(actor.context, '/api/tenants/current'); check(response.status() === 200, 'CURRENT_TENANT_READ'); const tenant = await response.json(); check(tenant.isAvailable === true && typeof tenant.tenantId === 'string', 'CURRENT_TENANT_UNAVAILABLE'); tenantIds.push(tenant.tenantId); }
  check(new Set(tenantIds).size === 1, 'SAME_TENANT_REQUIRED'); report.observations.sameTenant = true;
  const directIds = [];
  for (const recipientUserId of [a.userId, b.userId]) { const response = await api(c.context, '/api/conversations/direct', 'POST', { recipientUserId }); check(response.status() === 200, 'DIRECT_FIXTURE_CREATE'); const conversation = await response.json(); check(typeof conversation.id === 'string', 'DIRECT_FIXTURE_ID'); directIds.push(conversation.id); }
  const [aConversation, bConversation] = directIds; check(aConversation !== bConversation, 'DISTINCT_DIRECT_SCOPES');
  const runId = randomUUID(), aInitial = `Synthetic actor boundary initial A ${runId}`, bInitial = `Synthetic actor boundary initial B ${runId}`;
  const aLive = `Synthetic actor boundary private A ${runId}`, bLive = `Synthetic actor boundary fresh B ${runId}`;
  const expectedBodies = new Set([aInitial, bInitial, aLive, bLive]);
  await send(c.context, aConversation, aInitial); await send(c.context, bConversation, bInitial);
  const denied = await api(b.context, `/api/conversations/${aConversation}/messages?limit=100`);
  check([400, 403, 404].includes(denied.status()), 'KNOWN_A_SCOPE_NOT_DENIED_TO_B');
  report.observations.knownAResourceDeniedToB = true;
  phase = 'ESTABLISH_REAL_BROWSER_A';
  browser = await chromium.launch({ headless: true });
  report.runtime = { node: process.version, playwright: require('@playwright/test/package.json').version, browser: browser.version() };
  const targetContext = await browser.newContext({ baseURL, viewport: { width: 1440, height: 900 } });
  await targetContext.addInitScript(() => { window.__COGLATAS_FEATURE_FLAGS__ = { 'realtime.signalR': true }; });
  const page = await targetContext.newPage(); primaryPage = page; const targetWire = observe(page, expectedBodies);
  await page.goto('/app/login'); await page.getByTestId('login-page').waitFor({ state: 'visible' });
  const loginHistoryIndex = await page.evaluate(() => history.length - 1);
  const sentinel = randomUUID(); await page.evaluate(value => { Object.defineProperty(window, '__actorBoundaryDocument', { value }); }, sentinel);
  const cdp = await targetContext.newCDPSession(page); await cdp.send('Page.enable');
  const mainFrameId = (await cdp.send('Page.getFrameTree')).frameTree.frame.id;
  let documentChanges = 0; cdp.on('Page.frameNavigated', event => { if (event.frame.id === mainFrameId) documentChanges++; });
  await loginUi(page, emailA, password); await openDirect(page, aConversation);
  check(await waitUntil(async () => (await page.getByTestId('realtime-connection-state').innerText()).includes('Realtime updates connected.')), 'A_REALTIME_NOT_CONNECTED');
  check(await waitUntil(() => page.getByTestId('message-timeline').getByText(aInitial, { exact: true }).count()), 'A_INITIAL_PROJECTION_MISSING');
  check(targetWire.sockets.some(socket => !socket.closed), 'A_NO_ACTUAL_WEBSOCKET');
  const observerContext = await browser.newContext({ baseURL, storageState: await a.context.storageState() });
  await observerContext.addInitScript(() => { window.__COGLATAS_FEATURE_FLAGS__ = { 'realtime.signalR': true }; });
  const observer = await observerContext.newPage(); const observerWire = observe(observer, expectedBodies);
  await observer.goto(`/app/dm/${aConversation}`); await observer.getByTestId('dm-page').waitFor({ state: 'visible' });
  check(await waitUntil(async () => (await observer.getByTestId('realtime-connection-state').innerText()).includes('Realtime updates connected.')), 'A_POSITIVE_OBSERVER_NOT_CONNECTED');
  check(await waitUntil(() => observer.getByTestId('message-timeline').getByText(aInitial, { exact: true }).count()), 'A_POSITIVE_OBSERVER_INITIAL_MISSING');
  const oldSockets = targetWire.sockets.filter(socket => !socket.closed);
  phase = 'SAME_DOCUMENT_HISTORY_TO_LOGIN';
  report.observations.historyLengthBefore = await page.evaluate(() => history.length);
  const historyDelta = loginHistoryIndex - (report.observations.historyLengthBefore - 1);
  check(historyDelta < 0, 'ORIGINAL_LOGIN_HISTORY_ENTRY_MISSING');
  report.observations.historyDeltaToLogin = historyDelta;
  // One native traversal avoids overlapping asynchronous router popstate work.
  await page.evaluate(delta => history.go(delta), historyDelta);
  await page.waitForURL(url => url.pathname === '/app/login');
  await page.getByTestId('login-page').waitFor({ state: 'visible' });
  check(documentChanges === 0 && await page.evaluate(() => window.__actorBoundaryDocument) === sentinel, 'DOCUMENT_RECREATED_BEFORE_REPLACEMENT');
  check(oldSockets.some(socket => !socket.closed), 'A_TRANSPORT_ALREADY_CLOSED_BEFORE_REPLACEMENT');
  const socketCountBefore = targetWire.sockets.length;
  phase = 'UI_LOGIN_B_WITHOUT_LOGOUT';
  await loginUi(page, emailB, `${password}:recipient`);
  const meResponse = await targetContext.request.get('/api/auth/me'); check(meResponse.status() === 200, 'B_CURRENT_ME_READ');
  report.observations.currentMeIsB = id(await meResponse.json()) === b.userId;
  await openDirect(page, bConversation);
  check(await waitUntil(() => page.getByTestId('message-timeline').getByText(bInitial, { exact: true }).count()), 'B_FRESH_INITIAL_PROJECTION_MISSING');
  report.observations.sameDocument = documentChanges === 0 && await page.evaluate(() => window.__actorBoundaryDocument) === sentinel;
  report.observations.oldSocketsClosed = await waitUntil(() => oldSockets.every(socket => socket.closed), 5_000);
  report.observations.freshSocketCreated = targetWire.sockets.length > socketCountBefore;
  report.observations.noOldInitialBodyInB = !(await page.locator('body').innerText()).includes(aInitial);
  phase = 'FRESH_B_REALTIME_POSITIVE';
  const bMessage = await send(c.context, bConversation, bLive);
  report.observations.bFreshBodyRendered = await waitUntil(() => page.getByTestId('message-timeline').getByText(bLive, { exact: true }).count(), 15_000);
  report.observations.bFreshWireOnReplacement = await waitUntil(() => targetWire.messages.some(event => event.messageId === bMessage.id && event.socket.ordinal >= socketCountBefore && event.bodySha256 === hash(bLive)), 15_000);
  const persistedB = await api(b.context, `/api/conversations/${bConversation}/messages?limit=100`);
  check(persistedB.status() === 200, 'B_PERSISTED_READ');
  report.observations.bPersistence = rows(await persistedB.json()).some(message => message.id === bMessage.id && message.body === bLive);
  phase = 'A_PRIVATE_NEGATIVE_WITH_ACTUAL_POSITIVE_RECEIVER';
  const aMessage = await send(c.context, aConversation, aLive);
  report.observations.aPositiveBodyRendered = await waitUntil(() => observer.getByTestId('message-timeline').getByText(aLive, { exact: true }).count(), 15_000);
  report.observations.aPositiveWire = await waitUntil(() => observerWire.messages.some(event => event.messageId === aMessage.id && event.bodySha256 === hash(aLive)), 15_000);
  const negativeDeadline = Date.now() + 15_000;
  let unauthorizedWire = false, unauthorizedUi = false, observationLive = true;
  while (Date.now() < negativeDeadline) {
    unauthorizedWire ||= targetWire.messages.some(event => event.messageId === aMessage.id);
    unauthorizedUi ||= (await page.locator('body').innerText()).includes(aLive);
    observationLive &&= !page.isClosed() && !observer.isClosed() && targetWire.sockets.some(socket => !socket.closed) && observerWire.sockets.some(socket => !socket.closed);
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  report.observations.aPrivateWireExcluded = !unauthorizedWire;
  report.observations.aPrivateUiExcluded = !unauthorizedUi;
  report.observations.passiveObservationLive = observationLive;
  report.observations.passiveObservationMilliseconds = 15_000;
  report.observations.noDocumentNavigation = documentChanges === 0;
  report.proofDigests = { aBody: hash(aLive), bBody: hash(bLive), tenant: hash(tenantIds[0]), aUser: hash(a.userId), bUser: hash(b.userId) };
  const criteria = ['fileSharingFalse','sameTenant','knownAResourceDeniedToB','currentMeIsB','sameDocument','oldSocketsClosed','freshSocketCreated','noOldInitialBodyInB','bFreshBodyRendered','bFreshWireOnReplacement','bPersistence','aPositiveBodyRendered','aPositiveWire','aPrivateWireExcluded','aPrivateUiExcluded','passiveObservationLive','noDocumentNavigation'];
  report.failedCriteria = criteria.filter(key => report.observations[key] !== true);
  report.qualification = report.failedCriteria.length === 0 ? 'BOUNDED_PASS' : 'FAIL';
}
const watchdog = setTimeout(() => { report.qualification = 'INCOMPLETE'; report.failure = { phase, code: 'SCENARIO_WATCHDOG_TIMEOUT' }; save(); process.exit(1); }, 240_000);
try { await run(); }
catch (error) {
  let diagnostic = String(error.message ?? '').split('\n')[0];
  for (const secret of [`${password}:recipient`, `${password}:operator`, password, process.env.COGLATAS_SEED_ADMIN_PASSWORD].filter(Boolean)) diagnostic = diagnostic.replaceAll(secret, '<synthetic-secret>');
  diagnostic = diagnostic.replace(/(?:https?|wss?):\/\/\S+/gi, '<redacted-url>').replace(/[^\s]+@example\.test/gi, '<synthetic-account>');
  report.qualification = 'INCOMPLETE'; report.failure = { phase, code: /^[A-Z0-9_]+$/.test(error.safeCode ?? '') ? error.safeCode : 'UNEXPECTED_RUNTIME_ERROR', kind: error.name, diagnostic: diagnostic.slice(0, 180) };
  if (primaryPage && !primaryPage.isClosed()) report.observations.failurePath = new URL(primaryPage.url()).pathname.replace(/[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}/gi, '{opaque}');
}
finally { clearTimeout(watchdog); await browser?.close(); for (const context of contexts) await context.dispose(); save(); }
console.log(JSON.stringify({ scope: report.scope, source, qualification: report.qualification, failedCriteria: report.failedCriteria, failure: report.failure, release: report.release }));
process.exitCode = report.qualification === 'BOUNDED_PASS' ? 0 : 1;
