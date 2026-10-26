import { devices, expect, test, type BrowserContext, type Page, type Request, type Route, type WebSocketRoute } from '@playwright/test';

interface NativeSocket {
  readonly id: number;
  opened: boolean;
  closed: boolean;
  handshake: boolean;
  readonly tokenChanged: boolean;
}
interface SocketProbe { readonly records: NativeSocket[]; overflow: boolean; baselineId: number | null; }
declare global { interface Window { __COGLATAS_UI07_SOCKET_PROBE__?: SocketProbe; } }

interface SearchReconnectReceipt {
  readonly profile: 'desktop' | 'mobile';
  readonly searchReadStatus: number;
  readonly authorReadStatus: number;
  readonly recoveryReadStatus: number;
  readonly heldReadCount: number;
  readonly initialSearchResultCount: number;
  readonly initialAuthorOptionCount: number;
  readonly canceledReadCount: number;
  readonly pageErrorCount: number;
  readonly consoleErrorCount: number;
  readonly oldNativeSocketClosed: true;
  readonly replacementHandshakeObserved: true;
  readonly connectionTokenChanged: true;
  readonly oldHostRemoved: true;
  readonly drawerRestored: true;
  readonly oldQueryCleared: true;
}

/** Actual authenticated HTTP/SignalR only. Neither HTTP data nor Hub frames are fabricated. */
export async function exerciseRealSearchReconnect(primary: Page, baseURL: string,
  known: { readonly conversationId: string; readonly body: string }): Promise<readonly SearchReconnectReceipt[]> {
  const origin = new URL(baseURL).origin;
  expect(new URL(primary.url()).origin).toBe(origin);
  const browser = primary.context().browser();
  expect(browser).not.toBeNull();
  const owned: BrowserContext[] = [], receipts: SearchReconnectReceipt[] = [], cleanup = { failures: 0 };
  let failed = false, primaryError: unknown;
  try {
    const sourceSession = await primary.request.get('/api/auth/me');
    expect(sourceSession.status()).toBe(200);
    const sourceActor = await sourceSession.json();
    const sourceTenantResponse = await primary.request.get('/api/tenants/current');
    expect(sourceTenantResponse.status()).toBe(200);
    const sourceTenant = await sourceTenantResponse.json();
    const conversationResponse = await primary.request.get(`/api/conversations/${known.conversationId}`);
    expect(conversationResponse.status(), 'current actor can read the run-created Conversation').toBe(200);
    const conversation = await conversationResponse.json();
    expect(conversation.id === known.conversationId && conversation.type === 'DirectMessage').toBe(true);
    expect(typeof conversation.workspaceId === 'string' && /^[0-9a-f-]{36}$/i.test(conversation.workspaceId)).toBe(true);
    for (const profile of ['desktop', 'mobile'] as const) {
      // Own both faulted transports; the primary P0 page/session flow is untouched.
      const context = await browser!.newContext({ ...devices[profile === 'mobile' ? 'Pixel 5' : 'Desktop Chrome'], baseURL,
        storageState: { cookies: await primary.context().cookies(origin), origins: [{ origin,
          localStorage: [{ name: 'coglatas.locale', value: 'en' }] }] } });
      owned.push(context);
      const session = await context.request.get('/api/auth/me');
      expect(session.status(), 'copied synthetic session remains currently authenticated').toBe(200);
      const actor = await session.json();
      expect(typeof sourceActor.userId === 'string' && sourceActor.userId.length > 0 &&
        actor.userId === sourceActor.userId, 'owned context uses the verified synthetic actor').toBe(true);
      const tenantResponse = await context.request.get('/api/tenants/current');
      expect(tenantResponse.status()).toBe(200);
      const tenant = await tenantResponse.json();
      expect(typeof sourceTenant.tenantId === 'string' && tenant.tenantId === sourceTenant.tenantId &&
        tenant.isAvailable === true && tenant.isPlatformScope === false, 'owned context has the same current Tenant').toBe(true);
      await installNativeSocketObserver(context);
      const page = await context.newPage();
      if (profile === 'mobile') { await page.setViewportSize({ width: 320, height: 800 }); }
      receipts.push(await exerciseProfile(page, origin, profile, known.body, conversation.workspaceId, cleanup));
    }
  } catch (error) { failed = true; primaryError = error; }
  const closed = await Promise.allSettled(owned.map(context => context.close()));
  cleanup.failures += closed.filter(result => result.status === 'rejected').length;
  try {
    await test.info().attach('ui07-real-search-network.json', { contentType: 'application/json',
      body: JSON.stringify({ schemaVersion: 1, scope: 'REAL_BACKEND_UI07_NETWORK_CONTROL',
        complete: !failed && cleanup.failures === 0, cleanupFailureCount: cleanup.failures, profiles: receipts }) });
  } catch { cleanup.failures++; }
  if (failed) { throw primaryError; }
  expect(cleanup.failures, 'all owned network/context/evidence cleanup completed').toBe(0);
  return receipts;
}

async function installNativeSocketObserver(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    const OriginalWebSocket = window.WebSocket;
    const probe: SocketProbe = { records: [], overflow: false, baselineId: null };
    window.__COGLATAS_UI07_SOCKET_PROBE__ = probe;
    const tokens = new Map<number, string | null>();
    // Transparent native API observation, without product objects or transport commands.
    class ObservedWebSocket extends OriginalWebSocket {
      constructor(url: string | URL, protocols?: string | string[]) {
        super(url, protocols);
        const target = new URL(String(url), location.href);
        if (target.host !== location.host || target.pathname !== '/hubs/app') { return; }
        if (probe.records.length === 16) { probe.overflow = true; return; }
        const token = target.searchParams.get('id');
        const baseline = probe.baselineId === null ? null : tokens.get(probe.baselineId);
        const record: NativeSocket = { id: probe.records.length + 1, opened: false, closed: false, handshake: false,
          tokenChanged: Boolean(baseline && token && baseline !== token) };
        tokens.set(record.id, token);
        probe.records.push(record);
        this.addEventListener('open', () => { record.opened = true; });
        this.addEventListener('close', () => { record.closed = true; });
        this.addEventListener('message', event => {
          if (typeof event.data === 'string' && event.data.split('\x1e').includes('{}')) { record.handshake = true; }
        });
      }
    }
    window.WebSocket = ObservedWebSocket;
  });
}

async function sockets(page: Page): Promise<SocketProbe> {
  return page.evaluate(() => window.__COGLATAS_UI07_SOCKET_PROBE__ ?? { records: [], overflow: true, baselineId: null });
}

async function exerciseProfile(page: Page, origin: string, profile: 'desktop' | 'mobile', knownMessageBody: string,
  workspaceId: string, cleanup: { failures: number }): Promise<SearchReconnectReceipt> {
  let pageErrors = 0, consoleErrors = 0, failedHttp = 0;
  page.on('pageerror', () => { pageErrors++; });
  page.on('console', message => { if (message.type() === 'error') { consoleErrors++; } });
  page.on('response', response => {
    const url = new URL(response.url());
    if (url.origin === origin && (url.pathname.startsWith('/api/') || url.pathname === '/hubs/app/negotiate') &&
        response.status() >= 400) { failedHttp++; }
  });
  const transports: { client: WebSocketRoute; server: WebSocketRoute }[] = [];
  await page.routeWebSocket(url => url.host === new URL(origin).host && url.pathname === '/hubs/app', client => {
    // Default forwarding preserves every real authenticated Hub frame byte-for-byte.
    transports.push({ client, server: client.connectToServer() });
  });
  await page.goto('/app/workspaces');
  await expect(page.getByTestId('workspace-dashboard')).toBeVisible();
  await page.getByTestId('workspace-switcher').selectOption(workspaceId);
  await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
  // Existing SPA navigation retains the verified Workspace and observer epoch.
  if (profile === 'mobile') {
    await page.getByTestId('mobile-nav-toggle').click();
    await expect(page.getByTestId('mobile-nav-toggle')).toHaveAttribute('aria-expanded', 'true');
  }
  const navigation = page.locator(profile === 'mobile' ? '#mobile-navigation' : '.app-shell__feature-menu');
  await navigation.getByTestId('nav-dm').click();
  await expect(page.getByTestId('messages-page')).toBeVisible();
  const indicator = page.getByTestId('realtime-connection-state');
  await expect(indicator).toContainText('Realtime updates connected.');
  await expect.poll(async () => (await sockets(page)).records.some(record => record.opened && record.handshake && !record.closed)).toBe(true);
  const host = page.getByTestId('message-search-filters');
  await expect(host).toBeVisible();
  const input = page.getByTestId('message-search-input'), mobileToggle = page.getByTestId('message-filter-drawer-toggle');
  if (profile === 'mobile') {
    await mobileToggle.focus(); await expect(mobileToggle).toBeFocused(); await mobileToggle.press('Enter');
    await expect(mobileToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(input).toBeVisible(); await expect(input).toBeFocused();
  }
  await input.fill(knownMessageBody);
  const searchRead = page.waitForResponse(response => new URL(response.url()).origin === origin &&
    new URL(response.url()).pathname === '/api/search' && response.request().method() === 'GET');
  await input.press('Enter');
  const search = await searchRead;
  expect(search.status(), 'genuine authenticated search query').toBe(200);
  const searchItems = (await search.json()).items;
  expect(Array.isArray(searchItems)).toBe(true);
  expect(searchItems.length, 'the existing real message is a non-vacuous search control').toBeGreaterThan(0);
  await expect(page.getByTestId('message-search-result').filter({ hasText: knownMessageBody })).toHaveCount(1);
  await expect(page.getByTestId('message-active-search-chip')).toBeVisible();
  const opener = page.getByTestId('message-advanced-filters-open');
  await opener.focus(); await expect(opener).toBeFocused(); await opener.press('Enter');
  const drawer = page.getByTestId('message-advanced-filters-drawer');
  await expect(drawer).toBeVisible();
  const author = page.getByTestId('message-advanced-author');
  const authorRead = page.waitForResponse(response => new URL(response.url()).origin === origin &&
    new URL(response.url()).pathname === '/api/search/message-authors' && response.request().method() === 'GET');
  await author.fill('Browser');
  const authors = await authorRead;
  expect(authors.status(), 'genuine authenticated author discovery').toBe(200);
  const authorItems = (await authors.json()).items;
  expect(Array.isArray(authorItems)).toBe(true);
  expect(authorItems.length, 'the current real message has a discoverable authorized Browser author').toBeGreaterThan(0);
  await expect(page.getByTestId('message-author-option')).not.toHaveCount(0);
  await expect(author).toHaveValue('Browser');
  const originalHost = await host.elementHandle(); expect(originalHost).not.toBeNull();
  const initial = (await sockets(page)).records.filter(record => record.opened && record.handshake && !record.closed).at(-1)!;
  expect(initial, 'a real native Hub instance is current immediately before the fault').toBeDefined();
  expect(transports[initial.id - 1], 'the native Hub instance has one owned real-server bridge').toBeDefined();
  await page.evaluate(id => { window.__COGLATAS_UI07_SOCKET_PROBE__!.baselineId = id; }, initial.id);

  let armed = false, released = false, routeFailure = false, canceledReadCount = 0, eligibleReads = 0;
  const eligibleRequests = new Set<Request>(), statuses: number[] = [], running = new Set<Promise<void>>();
  let release!: () => void;
  const barrier = new Promise<void>(resolve => { release = () => { released = true; resolve(); }; });
  const matches = (url: URL) => url.origin === origin && url.pathname === '/api/conversations';
  const handler = (route: Route): Promise<void> => {
    const operation = (async () => {
      const request = route.request();
      try {
        if (!armed || released || request.method() !== 'GET') { await route.continue(); return; }
        const current = await sockets(page);
        const eligible = current.records.some(record => record.id > initial.id && record.opened &&
          record.handshake && record.tokenChanged && !record.closed);
        const response = await route.fetch(); statuses.push(response.status());
        if (eligible) { eligibleRequests.add(request); eligibleReads++; }
        await barrier;
        await route.fulfill({ response });
      } catch {
        if (request.failure()?.errorText === 'net::ERR_ABORTED') { canceledReadCount++; }
        else { routeFailure = true; }
      }
    })();
    running.add(operation); void operation.finally(() => running.delete(operation)); return operation;
  };
  await page.route(matches, handler);
  let failed = false, primaryError: unknown, receipt: SearchReconnectReceipt | undefined;
  try {
    await page.context().setOffline(true); await expect(indicator).toContainText('Offline.');
    const transport = transports[initial.id - 1];
    // Browser WebSocket.close accepts 1000 or 3000-4999, not protocol-only 1001.
    await Promise.all([transport.server.close({ code: 4001 }), transport.client.close({ code: 4001 })]);
    armed = true; await page.context().setOffline(false);
    await expect.poll(async () => (await sockets(page)).records.find(record => record.id === initial.id)?.closed).toBe(true);
    await expect.poll(async () => (await sockets(page)).records.some(record => record.id > initial.id &&
      record.opened && record.handshake && record.tokenChanged && !record.closed)).toBe(true);
    // route.fetch sends the genuine server read after the distinct native handshake.
    // The automatic browser request may already have reached the barrier earlier.
    // No reload, manual refresh, direct list fetch or product-internal call occurs.
    await expect.poll(() => eligibleReads + Number(routeFailure)).toBeGreaterThan(0);
    expect(routeFailure).toBe(false); expect(statuses.every(status => status === 200)).toBe(true);
    await expect(page.getByTestId('messages-list-loading')).toBeVisible(); await expect(host).toHaveCount(0);
    expect(await originalHost!.evaluate(element => element.isConnected)).toBe(false);
    const recoveryRead = page.waitForResponse(response => eligibleRequests.has(response.request()) &&
      response.request().method() === 'GET' && matches(new URL(response.url())));
    release();
    const recovered = await recoveryRead;
    expect(recovered.status(), 'fresh post-handshake genuine response reaches the browser').toBe(200);
    expect(Array.isArray((await recovered.json()).items)).toBe(true);
    await expect(drawer).toBeVisible(); await expect(opener).toHaveAttribute('aria-expanded', 'true');
    if (profile === 'mobile') { await expect(mobileToggle).toHaveAttribute('aria-expanded', 'true'); }
    await expect(input).toHaveValue(''); await expect(author).toHaveValue('');
    await expect(page.getByTestId('message-active-search-chip')).toHaveCount(0);
    await expect(page.getByTestId('message-search-result')).toHaveCount(0);
    await expect(page.getByTestId('message-author-option')).toHaveCount(0);
    await expect(indicator).toContainText('Realtime updates connected.');
    await Promise.all([...running]); expect(routeFailure).toBe(false);
    expect(statuses.every(status => status === 200)).toBe(true);
    expect((await sockets(page)).overflow, 'finite native socket observation is complete').toBe(false);
    expect(pageErrors, 'owned browser pages have no uncaught errors').toBe(0);
    expect(failedHttp, 'no API or Hub authentication failure is ignored').toBe(0);
    receipt = { profile, searchReadStatus: search.status(), authorReadStatus: authors.status(),
      recoveryReadStatus: recovered.status(), heldReadCount: statuses.length, canceledReadCount,
      initialSearchResultCount: searchItems.length, initialAuthorOptionCount: authorItems.length,
      pageErrorCount: pageErrors, consoleErrorCount: consoleErrors, oldNativeSocketClosed: true,
      replacementHandshakeObserved: true, connectionTokenChanged: true,
      oldHostRemoved: true, drawerRestored: true, oldQueryCleared: true };
  } catch (error) { failed = true; primaryError = error; }
  release();
  const cleaned = await Promise.allSettled([page.context().setOffline(false), page.unroute(matches, handler),
    Promise.all([...running]), originalHost!.dispose()]);
  const cleanupFailures = cleaned.filter(result => result.status === 'rejected').length;
  cleanup.failures += cleanupFailures;
  if (failed) { throw primaryError; }
  expect(cleanupFailures, 'all owned HTTP/network cleanup completed').toBe(0);
  return receipt!;
}
