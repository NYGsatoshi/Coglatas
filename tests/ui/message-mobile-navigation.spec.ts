import { expect, test, type Page, type TestInfo } from '@playwright/test';

const workspaceId = 'static-workspace-1';
const currentUserId = 'mock-user-a';

const conversations = Array.from({ length: 30 }, (_, index) => {
  const channel = index === 0 || index === 10;
  const id = channel ? `mobile-channel-${index + 1}` : `mobile-dm-${index}`;
  return {
    id,
    workspaceId,
    type: channel ? 'ProjectChannel' : 'DirectMessage',
    title: channel ? `Mobile channel ${index + 1}` : `Mobile DM ${index}`,
    lastMessage: {
      id: `last-${id}`,
      conversationId: id,
      authorUserId: 'mock-user-b',
      authorDisplayName: 'Mock User B',
      body: `Preview ${index}`,
      createdAt: '2026-08-23T00:00:00Z',
      version: 1
    },
    unreadCount: index % 3,
    hasMention: index % 5 === 0,
    createdAt: '2026-08-22T00:00:00Z',
    updatedAt: `2026-08-23T00:${String(index).padStart(2, '0')}:00Z`
  };
});

test.describe('Issue #351 mobile Message hierarchy', () => {
  test.beforeEach(async ({ page }, testInfo) => {
    skipDesktop(testInfo);
    await installMessagingApi(page);
    await page.setViewportSize({ width: 320, height: 800 });
  });

  test('opens detail at the top and restores the Message list scroll position on Back', async ({ page }) => {
    await page.goto('/app/messages');

    await expect(page.getByTestId('messages-page')).toBeVisible();
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(conversations.length);
    await expectNoHorizontalOverflow(page);

    const conversation = page.getByTestId('conversation-list-item').nth(12);
    await conversation.scrollIntoViewIfNeeded();
    const rememberedTop = await effectiveScrollTop(page);
    expect(rememberedTop).toBeGreaterThan(0);
    await conversation.focus();
    await expect(conversation).toBeFocused();

    await conversation.press('Enter');

    await expect(page).toHaveURL(/\/app\/dm\/mobile-dm-12$/);
    await expect(page.getByTestId('dm-page')).toBeVisible();
    const backLink = page.getByTestId('messages-back-link');
    await expect(backLink).toBeVisible();
    await expect(backLink).toBeFocused();
    await expect.poll(() => effectiveScrollTop(page)).toBe(0);
    await expectNoHorizontalOverflow(page);

    const detailHistoryLength = await page.evaluate(() => window.history.length);
    await backLink.press('Enter');

    await expect(page).toHaveURL(/\/app\/messages$/);
    await expect(page.getByTestId('messages-page')).toBeVisible();
    await expect.poll(() => effectiveScrollTop(page)).toBe(rememberedTop);
    await expect(page.getByTestId('conversation-list-item').nth(12)).toBeFocused();
    expect(await page.evaluate(() => window.history.length)).toBe(detailHistoryLength);
  });

  test('keeps the composer input and Send reachable in a keyboard-sized viewport', async ({ page }) => {
    await page.goto('/app/dm/mobile-dm-1');

    await expect(page.getByTestId('dm-page')).toBeVisible();
    const textarea = page.getByTestId('message-draft');
    const send = page.getByTestId('send-message');
    await textarea.fill('Keyboard viewport message');
    await textarea.focus();

    await page.setViewportSize({ width: 320, height: 420 });
    await scrollEffectiveRootToEnd(page);

    await expect(textarea).toBeVisible();
    await expect(send).toBeVisible();
    await expect(send).toBeEnabled();

    const viewportContent = await page.locator('meta[name="viewport"]').getAttribute('content');
    expect(viewportContent).toContain('interactive-widget=resizes-content');
    expect(viewportContent).not.toContain('viewport-fit=cover');

    const viewportHeight = await page.evaluate(() => window.innerHeight);
    await expect
      .poll(async () => {
        const box = await send.boundingBox();
        return box ? box.y + box.height : Number.POSITIVE_INFINITY;
      })
      .toBeLessThanOrEqual(viewportHeight + 1);

    const textareaBox = await textarea.boundingBox();
    const sendBox = await send.boundingBox();
    expect(textareaBox).not.toBeNull();
    expect(sendBox).not.toBeNull();
    expect(textareaBox!.x).toBeGreaterThanOrEqual(0);
    expect(textareaBox!.x + textareaBox!.width).toBeLessThanOrEqual(321);
    expect(textareaBox!.y).toBeGreaterThanOrEqual(-1);
    expect(textareaBox!.y + textareaBox!.height).toBeLessThanOrEqual(viewportHeight + 1);
    expect(sendBox!.x).toBeGreaterThanOrEqual(0);
    expect(sendBox!.x + sendBox!.width).toBeLessThanOrEqual(321);
    expect(sendBox!.y).toBeGreaterThanOrEqual(-1);
    expect(sendBox!.y + sendBox!.height).toBeLessThanOrEqual(viewportHeight + 1);
    await expectNoHorizontalOverflow(page);

    const sendRequestPromise = page.waitForRequest(
      (request) => request.method() === 'POST' && request.url().endsWith('/api/conversations/mobile-dm-1/messages')
    );
    await send.focus();
    await send.press('Enter');
    const sendRequest = await sendRequestPromise;
    expect(sendRequest.postDataJSON()).toMatchObject({
      body: 'Keyboard viewport message'
    });
    expect(sendRequest.headers()['x-csrf-token']).toBe('csrf-message-mobile');
    await expect(textarea).toHaveValue('');
  });

  test('matches the AppShell mobile boundary and preserves desktop split-view scroll', async ({ page }) => {
    const channels = Array.from({ length: 30 }, (_, index) => {
      const id = `mobile-channel-${index + 1}`;
      return { ...conversations[0], id, title: `Mobile channel ${index + 1}`,
        lastMessage: { ...conversations[0].lastMessage, id: `last-${id}`, conversationId: id } };
    });
    // Exercise scroll with real channel rows rather than unrelated DM rows.
    await page.route('**/api/conversations', (route) => route.fulfill({
      status: 200, contentType: 'application/json', body: JSON.stringify({ items: [
        ...channels, conversations[1],
        { ...conversations[1], id: 'mobile-group', type: 'Group', title: 'Excluded Group DM' },
        { ...channels[0], id: 'other-workspace-channel', workspaceId: 'workspace-other', title: 'Excluded Workspace channel' },
      ] }),
    }));
    await page.setViewportSize({ width: 860, height: 800 });
    await page.goto('/app/workspaces/static-workspace-1/channels/mobile-channel-1');

    await expect(page.getByTestId('channel-messaging-page')).toBeVisible();
    await expect(page.getByTestId('messages-back-link')).toBeVisible();
    await expect(page.locator('.messaging-page__rail')).toBeHidden();
    await expect.poll(() => composerPosition(page)).toBe('sticky');

    await page.setViewportSize({ width: 861, height: 800 });
    await expect(page.getByTestId('messages-back-link')).toBeHidden();
    await expect(page.locator('.messaging-page__rail')).toBeVisible();
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(channels.length);
    await expect(page.getByTestId('conversation-list-item').filter({ hasText: 'Mobile DM' })).toHaveCount(0);
    await expect(page.getByTestId('conversation-list-item').filter({ hasText: 'Excluded' })).toHaveCount(0);
    await expect.poll(() => composerPosition(page)).toBe('static');

    const nextChannel = page.getByTestId('conversation-list-item').filter({ hasText: 'Mobile channel 11' });
    await nextChannel.scrollIntoViewIfNeeded();
    await nextChannel.focus();
    const beforeNavigation = await activeScrollState(page);
    expect(Math.max(beforeNavigation.app, beforeNavigation.document)).toBeGreaterThan(0);

    await nextChannel.press('Enter');

    await expect(page).toHaveURL(/\/app\/workspaces\/static-workspace-1\/channels\/mobile-channel-11$/);
    await expect
      .poll(async () => {
        const current = await activeScrollState(page);
        return Math.max(current.app, current.document);
      })
      .toBeGreaterThan(0);
  });
});

function skipDesktop(testInfo: TestInfo): void {
  test.skip(
    testInfo.project.name !== 'chromium-mobile',
    'Issue #351 is a mobile hierarchy and software-keyboard acceptance flow.'
  );
}

async function effectiveScrollTop(page: Page): Promise<number> {
  return page.evaluate(() => {
    const appHost = document.getElementById('app-shell-main-content');
    const documentHost = document.scrollingElement;
    const hosts = [appHost, documentHost].filter((host): host is HTMLElement => host instanceof HTMLElement);
    const scrollRoot =
      hosts.find((host) => host.scrollTop > 0) ??
      hosts.find((host) => host.scrollHeight > host.clientHeight + 1) ??
      hosts[0] ??
      null;
    return scrollRoot?.scrollTop ?? 0;
  });
}

async function activeScrollState(page: Page): Promise<{ app: number; document: number }> {
  return page.evaluate(() => ({
    app: document.getElementById('app-shell-main-content')?.scrollTop ?? 0,
    document: document.scrollingElement?.scrollTop ?? 0
  }));
}

async function composerPosition(page: Page): Promise<string> {
  return page.getByTestId('message-composer').evaluate((element) => {
    const host = element.closest('app-message-composer');
    return host ? window.getComputedStyle(host).position : '';
  });
}

async function scrollEffectiveRootToEnd(page: Page): Promise<void> {
  await page.evaluate(() => {
    const appHost = document.getElementById('app-shell-main-content');
    const documentHost = document.scrollingElement;
    const hosts = [appHost, documentHost].filter((host): host is HTMLElement => host instanceof HTMLElement);
    const scrollRoot =
      hosts.find((host) => host.scrollTop > 0) ??
      hosts.find((host) => host.scrollHeight > host.clientHeight + 1) ??
      hosts[0] ??
      null;
    scrollRoot?.scrollTo({
      top: scrollRoot.scrollHeight,
      left: 0,
      behavior: 'auto'
    });
  });
}

async function installMessagingApi(page: Page): Promise<void> {
  await page.route('**/api/security/csrf-token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'csrf-message-mobile', headerName: 'X-CSRF-Token' })
    });
  });

  await page.route('**/api/conversations**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    if (path === '/api/conversations' && method === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: conversations })
      });
      return;
    }

    const messageMatch = path.match(/^\/api\/conversations\/([^/]+)\/messages$/);
    if (messageMatch && method === 'POST') {
      const conversationId = messageMatch[1];
      const payload = request.postDataJSON() as {
        body?: string;
        clientRequestId?: string;
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: `sent-${conversationId}`,
          workspaceId,
          conversationId,
          authorUserId: currentUserId,
          authorDisplayName: 'Mock User A',
          body: payload.body ?? '',
          attachments: [],
          clientRequestId: payload.clientRequestId,
          createdAt: '2026-08-23T00:01:00Z',
          isDeleted: false,
          version: 1
        })
      });
      return;
    }

    if (messageMatch && method === 'GET') {
      const conversationId = messageMatch[1];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            {
              id: `message-${conversationId}`,
              workspaceId,
              conversationId,
              authorUserId: 'mock-user-b',
              authorDisplayName: 'Mock User B',
              body: 'Mobile acceptance message',
              createdAt: '2026-08-23T00:00:00Z',
              version: 1
            }
          ]
        })
      });
      return;
    }

    const readMatch = path.match(/^\/api\/conversations\/([^/]+)\/read$/);
    if (readMatch && method === 'POST') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: '{}'
      });
      return;
    }

    const detailMatch = path.match(/^\/api\/conversations\/([^/]+)$/);
    if (detailMatch && method === 'GET') {
      const conversationId = detailMatch[1];
      const channel = conversationId.startsWith('mobile-channel');
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: conversationId,
          workspaceId,
          type: channel ? 'ProjectChannel' : 'DirectMessage',
          title: channel ? 'Mobile channel' : 'Mobile DM 1',
          isArchived: false,
          isLocked: false,
          createdAt: '2026-08-22T00:00:00Z',
          updatedAt: '2026-08-23T00:00:00Z',
          members: [
            {
              userId: currentUserId,
              displayName: 'Mock User A',
              canRead: true,
              canPost: true,
              leftAt: null,
              removedAt: null
            },
            {
              userId: 'mock-user-b',
              displayName: 'Mock User B',
              canRead: true,
              canPost: true,
              leftAt: null,
              removedAt: null
            }
          ],
          mentionCandidates: [
            {
              userId: 'mock-user-b',
              displayName: 'Mock User B'
            }
          ]
        })
      });
      return;
    }

    await route.fallback();
  });
}

async function expectNoHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => ({
    document: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    app: (() => {
      const host = document.getElementById('app-shell-main-content');
      return host ? host.scrollWidth - host.clientWidth : 0;
    })()
  }));

  expect(overflow.document).toBeLessThanOrEqual(1);
  expect(overflow.app).toBeLessThanOrEqual(1);
}
