import { devices, expect, type BrowserContext, type Page, type Request, type Route } from '@playwright/test';

interface SearchReconnectReceipt {
  readonly profile: 'desktop' | 'mobile';
  readonly searchReadStatus: number;
  readonly authorReadStatus: number;
  readonly recoveryReadStatus: number;
  readonly heldReadCount: number;
  readonly canceledReadCount: number;
  readonly oldHostRemoved: true;
  readonly drawerRestored: true;
  readonly oldQueryCleared: true;
}

/** Actual authenticated HTTP/SignalR only. The response barrier never invents API data. */
export async function exerciseRealSearchReconnect(primary: Page, baseURL: string): Promise<readonly SearchReconnectReceipt[]> {
  const origin = new URL(baseURL).origin;
  expect(new URL(primary.url()).origin).toBe(origin);
  const originalUrl = primary.url();
  const browser = primary.context().browser();
  expect(browser).not.toBeNull();
  let mobileContext: BrowserContext | undefined;
  try {
    const desktop = await exerciseProfile(primary, origin, 'desktop');
    // Reuse only the already verified synthetic session. No account or grant is created.
    mobileContext = await browser!.newContext({ ...devices['Pixel 5'], baseURL,
      storageState: { cookies: await primary.context().cookies(origin), origins: [{ origin,
        localStorage: [{ name: 'coglatas.locale', value: 'en' }] }] } });
    const mobile = await mobileContext.newPage();
    await mobile.setViewportSize({ width: 320, height: 800 });
    const session = await mobile.request.get('/api/auth/me');
    expect(session.status(), 'copied synthetic session remains currently authenticated').toBe(200);
    const sourceSession = await primary.request.get('/api/auth/me');
    expect(sourceSession.status()).toBe(200);
    const mobileActor = await session.json(), sourceActor = await sourceSession.json();
    expect(typeof sourceActor.userId === 'string' && sourceActor.userId.length > 0 &&
      mobileActor.userId === sourceActor.userId, 'mobile context uses the same verified synthetic actor').toBe(true);
    const receipt = await exerciseProfile(mobile, origin, 'mobile');
    return [desktop, receipt];
  } finally {
    await mobileContext?.close();
    await primary.context().setOffline(false);
    await primary.goto(originalUrl);
    await expect(primary.getByTestId('app-shell')).toBeVisible();
  }
}

async function exerciseProfile(page: Page, origin: string, profile: 'desktop' | 'mobile'): Promise<SearchReconnectReceipt> {
  await page.goto('/app/messages');
  await expect(page.getByTestId('messages-page')).toBeVisible();
  const indicator = page.getByTestId('realtime-connection-state');
  await expect(indicator).toContainText('Realtime updates connected.');
  const host = page.getByTestId('message-search-filters');
  await expect(host).toBeVisible();
  const input = page.getByTestId('message-search-input');
  const mobileToggle = page.getByTestId('message-filter-drawer-toggle');
  if (profile === 'mobile') {
    await mobileToggle.focus();
    await expect(mobileToggle).toBeFocused();
    await mobileToggle.press('Enter');
    await expect(mobileToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(input).toBeVisible();
    await expect(input).toBeFocused();
  }
  await input.fill('Synthetic UI07 reconnect query');
  const searchRead = page.waitForResponse(response => new URL(response.url()).origin === origin &&
    new URL(response.url()).pathname === '/api/search' && response.request().method() === 'GET');
  await input.press('Enter');
  const search = await searchRead;
  expect(search.status(), 'genuine authenticated search query').toBe(200);
  expect(Array.isArray((await search.json()).items), 'actual search response contract').toBe(true);
  await expect(page.getByTestId('message-active-search-chip')).toBeVisible();
  const opener = page.getByTestId('message-advanced-filters-open');
  await opener.focus();
  await expect(opener).toBeFocused();
  await opener.press('Enter');
  const drawer = page.getByTestId('message-advanced-filters-drawer');
  await expect(drawer).toBeVisible();
  const author = page.getByTestId('message-advanced-author');
  const authorRead = page.waitForResponse(response => new URL(response.url()).origin === origin &&
    new URL(response.url()).pathname === '/api/search/message-authors' && response.request().method() === 'GET');
  await author.fill('Browser');
  const authors = await authorRead;
  expect(authors.status(), 'genuine authenticated author discovery').toBe(200);
  expect(Array.isArray((await authors.json()).items), 'actual author response contract').toBe(true);
  await expect(author).toHaveValue('Browser');
  const originalHost = await host.elementHandle();
  expect(originalHost).not.toBeNull();

  let armed = false, released = false, routeFailure = false, canceledReadCount = 0;
  const heldRequests = new Set<Request>();
  const statuses: number[] = [];
  const running = new Set<Promise<void>>();
  let release!: () => void;
  const barrier = new Promise<void>(resolve => { release = () => { released = true; resolve(); }; });
  const matches = (url: URL) => url.origin === origin && url.pathname === '/api/conversations';
  const handler = (route: Route): Promise<void> => {
    const operation = (async () => {
      const request = route.request();
      try {
        if (!armed || released || request.method() !== 'GET') { await route.continue(); return; }
        heldRequests.add(request);
        const response = await route.fetch();
        statuses.push(response.status());
        await barrier;
        await route.fulfill({ response });
      } catch {
        if (request.failure()?.errorText === 'net::ERR_ABORTED') { canceledReadCount++; }
        else { routeFailure = true; }
      }
    })();
    running.add(operation);
    void operation.finally(() => running.delete(operation));
    return operation;
  };
  await page.route(matches, handler);
  try {
    await page.context().setOffline(true);
    await expect(indicator).toContainText('Offline.');
    armed = true;
    await page.context().setOffline(false);
    // Only the production reconnect/registered catch-up can issue this new read.
    // No reload, manual refresh, direct list fetch or client-internal call occurs.
    await expect.poll(() => statuses.length + Number(routeFailure)).toBeGreaterThan(0);
    expect(routeFailure, 'genuine recovery request did not fail at the barrier').toBe(false);
    expect(statuses.every(status => status === 200), 'all held genuine reads are currently authorized').toBe(true);
    await expect(page.getByTestId('messages-list-loading')).toBeVisible();
    await expect(host).toHaveCount(0);
    expect(await originalHost!.evaluate(element => element.isConnected), 'old protected search host is removed').toBe(false);
    const recoveryRead = page.waitForResponse(response => heldRequests.has(response.request()) &&
      response.request().method() === 'GET' && matches(new URL(response.url())));
    release();
    const recovered = await recoveryRead;
    expect(recovered.status(), 'a distinct post-resume genuine response reaches the browser').toBe(200);
    expect(Array.isArray((await recovered.json()).items), 'fresh real inbox response contract').toBe(true);
    await expect(drawer).toBeVisible();
    await expect(opener).toHaveAttribute('aria-expanded', 'true');
    if (profile === 'mobile') { await expect(mobileToggle).toHaveAttribute('aria-expanded', 'true'); }
    await expect(input).toHaveValue('');
    await expect(author).toHaveValue('');
    await expect(page.getByTestId('message-active-search-chip')).toHaveCount(0);
    await expect(page.getByTestId('message-author-option')).toHaveCount(0);
    await expect(indicator).toContainText('Realtime updates connected.');
    await Promise.all([...running]);
    expect(routeFailure, 'owned response barriers complete without unexpected delivery errors').toBe(false);
    expect(statuses.every(status => status === 200)).toBe(true);
    return { profile, searchReadStatus: search.status(), authorReadStatus: authors.status(),
      recoveryReadStatus: recovered.status(), heldReadCount: heldRequests.size, canceledReadCount,
      oldHostRemoved: true, drawerRestored: true, oldQueryCleared: true };
  } finally {
    release();
    await page.context().setOffline(false);
    await page.unroute(matches, handler);
    await Promise.all([...running]);
    await originalHost?.dispose();
  }
}
