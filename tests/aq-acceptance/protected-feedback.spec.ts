import { expect, test, type WebSocketRoute } from '@playwright/test';
import { loginViaApi } from '../functional/helpers/auth';
import { csrfAwareRequest } from '../functional/helpers/csrf';

function requiredFixtureSetting(key: string): string {
  const value = process.env[key];
  if (!value) throw new Error(`Missing AQ protected fixture setting ${key}.`);
  return value;
}

test('feedback clears an old answer and rereads the current visibility on reconnect', async ({ page, context, playwright, browserName }) => {
  const presentation = encodeURIComponent(requiredFixtureSetting('COGLATAS_AQ_PRESENTATION_ID'));
  const text = `Synthetic feedback for ${browserName}`;
  const feedbackPath = `/api/v1/core/presentations/${presentation}/feedback`;
  const answer = page.getByText(text, { exact: false });
  const connection = page.getByTestId('realtime-connection-state');
  let browserSocket: WebSocketRoute | undefined;
  let serverSocket: WebSocketRoute | undefined;
  let socketCount = 0;
  const reconnect = Promise.withResolvers<void>();
  const refresh = Promise.withResolvers<void>();
  const refreshStarted = Promise.withResolvers<void>();
  let checkRefresh = false;

  // Forward the real negotiation, subscriptions, and events. Hold just the next
  // connection open until the server has persisted the visibility change.
  await page.routeWebSocket(url => url.pathname === '/hubs/app', async socket => {
    socketCount += 1;
    if (socketCount > 1) await reconnect.promise;
    browserSocket = socket;
    serverSocket = socket.connectToServer();
  });
  await page.route(url => url.pathname === feedbackPath, async route => {
    if (checkRefresh) {
      refreshStarted.resolve();
      await refresh.promise;
    }
    await route.continue();
  });

  const operator = await playwright.request.newContext({ baseURL: requiredFixtureSetting('COGLATAS_AQ_SYNTHETIC_ORIGIN') });
  try {
    const credentials = {
      email: requiredFixtureSetting('COGLATAS_AQ_SYNTHETIC_EMAIL'),
      password: requiredFixtureSetting('COGLATAS_AQ_SYNTHETIC_PASSWORD'),
    };
    await loginViaApi(context.request, credentials);
    await loginViaApi(operator, credentials);
    await page.goto(`/app/aq/feedback/${presentation}`);
    await expect(connection).toHaveText('Realtime updates connected.');
    await expect(answer).toBeVisible();
    expect(socketCount).toBe(1);
    if (!serverSocket || !browserSocket) throw new Error('The browser did not negotiate a live WebSocket.');

    const current = await context.request.get(feedbackPath);
    expect(current.status()).toBe(200);
    const { data } = await current.json() as {
      data: { items: { answers: { id: string; value?: string; visibilityVersionNo?: number }[] }[] };
    };
    const target = data.items.flatMap(item => item.answers).find(item => item.value === text);
    if (!target?.visibilityVersionNo) throw new Error('The authorized fixture cannot moderate its synthetic answer.');

    await serverSocket.close({ code: 1012, reason: 'Synthetic network interruption' });
    await browserSocket.close({ code: 1012, reason: 'Synthetic network interruption' });
    await expect(connection).toHaveText('Reconnecting realtime updates.');
    const hide = await csrfAwareRequest(operator, 'PATCH',
      `/api/v1/core/feedback/answers/${encodeURIComponent(target.id)}/visibility`, {
        data: { expectedVersionNo: target.visibilityVersionNo, action: 'hide', reason: 'Synthetic reconnect visibility' },
      });
    expect(hide.status()).toBe(200);

    checkRefresh = true;
    const reread = page.waitForResponse(response =>
      new URL(response.url()).pathname === feedbackPath && response.request().method() === 'GET');
    reconnect.resolve();
    await refreshStarted.promise;
    await expect(answer).toHaveCount(0);
    refresh.resolve();
    const updated = await reread;
    expect(updated.status()).toBe(200);
    expect(JSON.stringify(await updated.json())).not.toContain(text);
    await expect(connection).toHaveText('Realtime updates connected.');
    await expect(page.getByText('Synthetic public comment · 非表示', { exact: true }).first()).toBeVisible();
    await expect(answer).toHaveCount(0);
    expect(socketCount).toBeGreaterThanOrEqual(2);
  } finally {
    reconnect.resolve();
    refresh.resolve();
    await operator.dispose();
  }
});
