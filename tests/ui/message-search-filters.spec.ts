import { expect, test, type Page, type TestInfo } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

// Match the authorized current Workspace returned by the static server.
const workspaceId = 'static-workspace-1';
const unreadMentionId = '22222222-2222-4222-8222-222222222222';
const unreadOnlyId = '33333333-3333-4333-8333-333333333333';
const mentionOnlyId = '44444444-4444-4444-8444-444444444444';
const messageId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const authorId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

const conversations = [
  {
    id: unreadMentionId,
    workspaceId,
    type: 'DirectMessage',
    title: 'Unread mention',
    lastMessage: { body: 'Budget preview' },
    unreadCount: 2,
    hasMention: true,
    createdAt: '2026-08-29T01:00:00Z'
  },
  {
    id: unreadOnlyId,
    workspaceId,
    type: 'ProjectChannel',
    title: 'Unread only',
    lastMessage: { body: 'Unread preview' },
    unreadCount: 1,
    hasMention: false,
    createdAt: '2026-08-29T00:30:00Z'
  },
  {
    id: mentionOnlyId,
    workspaceId,
    type: 'ProjectChannel',
    title: 'Mention only',
    lastMessage: { body: 'Mention preview' },
    unreadCount: 0,
    hasMention: true,
    createdAt: '2026-08-29T00:00:00Z'
  }
];

test.describe('Issues #355/#359/#367 Message search and inbox views', () => {
  test.beforeEach(async ({ page }) => {
    await installMessagingDiscoveryApi(page);
  });

  test('separates DM and Teams directories and traps and restores new-message keyboard focus', async ({ page }, testInfo) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.setViewportSize({ width: 320, height: 800 });
    await page.goto('/app/dm');
    await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', 'dark');
    await expect(page.getByRole('heading', { name: 'DM', exact: true })).toBeVisible();
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(1);
    await expect(page.getByTestId('conversation-list-item')).toContainText('Unread mention');
    await expect(page.getByTestId('conversation-directory-scope')).toContainText('current inbox page');

    const directories = page.getByRole('navigation', { name: 'Conversation directories' });
    await directories.getByRole('link', { name: 'Teams', exact: true }).press('Enter');
    await expect(page).toHaveURL(/\/app\/teams$/);
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(2);
    await expect(directories.getByRole('link', { name: 'Teams', exact: true })).toHaveAttribute('aria-current', 'page');

    const trigger = page.getByTestId('new-message-button');
    await trigger.press('Enter');
    const input = page.getByTestId('recipient-search');
    await expect(input).toBeFocused();
    await input.press('Shift+Tab');
    await expect(page.getByTestId('new-message-dialog').getByRole('button', { name: 'Close', exact: true })).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(page.getByTestId('new-message-dialog').getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('new-message-dialog')).toHaveCount(0);
    await expect(trigger).toBeFocused();
    await expectNoHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="messages-page"]');
    await page.screenshot({ path: testInfo.outputPath('teams-dark-320-diagnostic.png'), fullPage: true });
  });

  test('pages the existing authorized inbox and removes previous rows when the next page is denied', async ({ page }) => {
    let denyPrevious = false;
    await page.route('**/api/conversations**', async (route) => {
      const url = new URL(route.request().url());
      if (url.pathname !== '/api/conversations') { await route.fallback(); return; }
      const pageNumber = Number(url.searchParams.get('page') ?? 1);
      if (denyPrevious) { await route.fulfill({ status: 403, body: '{}' }); return; }
      await route.fulfill({ json: {
        items: [conversations[pageNumber === 1 ? 0 : 1]], page: pageNumber, pageSize: 1, totalCount: 2,
        view: 'All', counts: { all: 2, unread: 2, mentions: 1, later: 0 }
      } });
    });
    await page.goto('/app/dm');
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(1);
    await page.getByRole('button', { name: 'Next page', exact: true }).press('Enter');
    await expect(page.getByTestId('messages-list-empty')).toContainText('No matching conversations on this page');
    await expect(page.getByTestId('conversation-pagination')).toContainText('Inbox page 2 of 2');
    denyPrevious = true;
    await page.getByRole('button', { name: 'Previous page', exact: true }).press('Enter');
    await expect(page.getByTestId('messages-permission-denied')).toBeVisible();
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(0);
    await expect(page.getByTestId('messages-page')).not.toContainText('Unread mention');
    await expect(page.getByTestId('new-message-button')).toBeDisabled();
  });

  test('keeps search and removable filters keyboard-accessible at 320px without accessibility violations', async ({
    page
  }, testInfo) => {
    skipUnlessMobile(testInfo);
    await page.setViewportSize({ width: 320, height: 800 });

    const searchRequests: URL[] = [];
    const inboxRequests: URL[] = [];
    const laterMutationBodies: unknown[] = [];
    const laterMutationCsrfTokens: string[] = [];
    page.on('request', (request) => {
      const url = new URL(request.url());
      if (url.pathname === '/api/search') {
        searchRequests.push(url);
      } else if (url.pathname === '/api/conversations' && request.method() === 'GET') {
        inboxRequests.push(url);
      } else if (url.pathname.endsWith('/state') && request.method() === 'PATCH') {
        laterMutationBodies.push(request.postDataJSON());
        laterMutationCsrfTokens.push(request.headers()['x-csrf-token'] ?? '');
      }
    });

    await page.goto('/app/messages');

    const surface = page.getByTestId('message-search-filters');
    const drawerToggle = page.getByTestId('message-filter-drawer-toggle');
    const searchInput = page.getByTestId('message-search-input');
    await expect(surface).toBeVisible();
    await expect(drawerToggle).toBeVisible();
    await expect(searchInput).toBeHidden();
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(3);

    await drawerToggle.focus();
    await drawerToggle.press('Enter');
    await expect(drawerToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(searchInput).toBeFocused();

    const unreadFilter = page.getByTestId('message-filter-unread');
    await unreadFilter.focus();
    await unreadFilter.press('Enter');
    await expect(unreadFilter).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByTestId('message-active-unread-chip')).toBeVisible();
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(2);
    expect(inboxRequests.at(-1)?.searchParams.get('view')).toBe('Unread');

    await searchInput.fill('budget');
    await searchInput.press('Enter');
    await expect(page.getByTestId('message-search-result')).toHaveCount(1);
    await expect(page.getByTestId('message-search-result')).toContainText('Budget decision is ready');
    await expect(page.getByTestId('message-search-result')).toHaveAttribute('href', `/app/dm/${unreadMentionId}`);
    await expect(page.getByTestId('message-active-search-chip')).toContainText('budget');
    expect(searchRequests).toHaveLength(1);
    expect(searchRequests[0].searchParams.get('q')).toBe('budget');
    expect(searchRequests[0].searchParams.get('type')).toBe('Message');
    expect(searchRequests[0].searchParams.get('pageSize')).toBe('50');

    const removeUnread = page.getByTestId('message-active-unread-chip').getByRole('button');
    await removeUnread.focus();
    await removeUnread.press('Enter');
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(3);
    await expect(page.getByTestId('message-active-unread-chip')).toHaveCount(0);
    await expect(unreadFilter).toBeFocused();

    const laterFilter = page.getByTestId('message-filter-later');
    await laterFilter.focus();
    await laterFilter.press('Enter');
    await expect(laterFilter).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByTestId('message-active-later-chip')).toBeVisible();
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(1);
    expect(inboxRequests.at(-1)?.searchParams.get('view')).toBe('Later');

    const removeLater = page.getByTestId('conversation-later-toggle');
    await expect(removeLater).toHaveAttribute('aria-pressed', 'true');
    await removeLater.focus();
    await removeLater.press('Enter');
    await expect(page.getByTestId('message-conversation-filter-empty')).toContainText('Later view');
    await expect(laterFilter).toBeFocused();
    expect(laterMutationBodies).toEqual([{ isLater: false }]);
    expect(laterMutationCsrfTokens).toEqual(['csrf-message-later']);
    await expect(page.getByTestId('message-filter-unread')).toHaveAttribute('aria-pressed', 'false');

    await page.getByTestId('message-filter-all').press('Enter');
    await expect(page.getByTestId('conversation-list-item')).toHaveCount(3);

    await searchInput.focus();
    await searchInput.press('Escape');
    await expect(drawerToggle).toHaveAttribute('aria-expanded', 'false');
    await expect(drawerToggle).toBeFocused();
    await expect(page.getByTestId('message-active-search-chip')).toBeVisible();

    await drawerToggle.press('Enter');
    await searchInput.fill('missing');
    await searchInput.press('Enter');
    await expect(page.getByTestId('message-search-empty')).toContainText('Change the search or clear');

    const changeSearch = page.getByTestId('message-search-change');
    await changeSearch.focus();
    await changeSearch.press('Enter');
    await expect(searchInput).toHaveValue('');
    await expect(searchInput).toBeFocused();
    await expect(page.getByTestId('message-search-empty')).toHaveCount(0);
    await expect(surface).not.toContainText('Has file');

    await expectNoHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="message-search-filters"]');
  });

  test('keeps search and quick filters expanded on desktop', async ({ page }, testInfo) => {
    skipUnlessDesktop(testInfo);
    await page.goto('/app/messages');

    await expect(page.getByTestId('message-search-input')).toBeVisible();
    await expect(page.getByTestId('message-filter-unread')).toBeVisible();
    await expect(page.getByTestId('message-filter-mentions')).toBeVisible();
    await expect(page.getByTestId('message-filter-later')).toContainText('1');
    await expect(page.getByTestId('message-filter-drawer-toggle')).toBeHidden();
  });

  test('keeps advanced filters private, keyboard-contained, and canonical at 320px', async ({
    page
  }, testInfo) => {
    skipUnlessMobile(testInfo);
    await page.setViewportSize({ width: 320, height: 800 });

    const searchRequests: URL[] = [];
    page.on('request', (request) => {
      const url = new URL(request.url());
      if (url.pathname === '/api/search') {
        searchRequests.push(url);
      }
    });

    const privateMarker = 'private-message-marker-367';
    await page.goto(`/app/messages?q=${privateMarker}`);
    await expect.poll(() => new URL(page.url()).searchParams.has('q')).toBe(false);

    const surface = page.getByTestId('message-search-filters');
    await expect(surface).not.toContainText(privateMarker);
    await page.getByTestId('message-filter-drawer-toggle').press('Enter');
    await expect(page.getByTestId('message-search-input')).toHaveValue('');

    const advancedTrigger = page.getByTestId('message-advanced-filters-open');
    await advancedTrigger.focus();
    await advancedTrigger.press('Enter');
    const drawer = page.getByTestId('message-advanced-filters-drawer');
    const authorInput = page.getByTestId('message-advanced-author');
    await expect(drawer).toBeVisible();
    await expect(authorInput).toBeFocused();

    await authorInput.fill('Authorized');
    const authorOption = page.getByTestId('message-author-option');
    await expect(authorOption).toContainText('Authorized Sender');
    await authorInput.press('Tab');
    await expect(authorOption).toBeFocused();
    await authorOption.press('Enter');
    await expect(authorInput).toBeFocused();

    await page.getByTestId('message-advanced-from-date').fill('2026-08-20');
    await page.getByTestId('message-advanced-to-date').fill('2026-08-30');
    await page.getByTestId('message-advanced-read').selectOption('Unread');
    await page.getByTestId('message-advanced-attachment').selectOption('With');

    const closeDrawer = page.getByRole('button', { name: 'Close advanced filters' });
    const apply = page.getByTestId('message-advanced-apply');
    await apply.focus();
    await apply.press('Tab');
    await expect(closeDrawer).toBeFocused();
    await closeDrawer.press('Shift+Tab');
    await expect(apply).toBeFocused();
    await expectNoHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="message-advanced-filters-drawer"]');

    await apply.press('Enter');
    await expect(drawer).toHaveCount(0);
    await expect(page.getByTestId('message-active-author-chip')).toContainText('Authorized Sender');
    await expect(page.getByTestId('message-active-from-date-chip')).toContainText('Aug 20, 2026');
    await expect(page.getByTestId('message-active-to-date-chip')).toContainText('Aug 30, 2026');
    await expect(page.getByTestId('message-active-read-chip')).toContainText('Unread');
    await expect(page.getByTestId('message-active-attachment-chip')).toContainText('With safe attachment');

    await expect.poll(() => searchRequests.length).toBe(1);
    const advancedRequest = searchRequests[0];
    const expectedDates = await page.evaluate(() => ({
      from: new Date(2026, 7, 20, 0, 0, 0, 0).toISOString(),
      toExclusive: new Date(2026, 7, 31, 0, 0, 0, 0).toISOString()
    }));
    expect(advancedRequest.searchParams.has('q')).toBe(false);
    expect(advancedRequest.searchParams.get('type')).toBe('Message');
    expect(advancedRequest.searchParams.get('authorUserId')).toBe(authorId);
    expect(advancedRequest.searchParams.get('fromDate')).toBe(expectedDates.from);
    expect(advancedRequest.searchParams.get('toDateExclusive')).toBe(expectedDates.toExclusive);
    expect(advancedRequest.searchParams.get('messageRead')).toBe('Unread');
    expect(advancedRequest.searchParams.get('messageAttachment')).toBe('With');
    expect(new URL(page.url()).searchParams.has('q')).toBe(false);
    expect(page.url()).not.toContain(privateMarker);

    await page.goBack();
    await expect.poll(() => new URL(page.url()).searchParams.has('messageRead')).toBe(false);
    expect(new URL(page.url()).searchParams.has('q')).toBe(false);
    await expect(surface).not.toContainText(privateMarker);
    await page.goForward();
    await expect.poll(() => new URL(page.url()).searchParams.get('messageRead')).toBe('Unread');
    await expect(page.getByTestId('message-active-author-chip')).toContainText('Authorized Sender');
    expect(new URL(page.url()).searchParams.has('q')).toBe(false);
    await expect(surface).not.toContainText(privateMarker);

    await advancedTrigger.press('Enter');
    await expect(authorInput).toBeFocused();
    await authorInput.press('Escape');
    await expect(drawer).toHaveCount(0);
    await expect(advancedTrigger).toBeFocused();

    await page.getByTestId('message-active-author-chip').getByRole('button').press('Enter');
    await expect(page.getByTestId('message-active-author-chip')).toHaveCount(0);
    await expect(page.getByTestId('message-active-read-chip')).toBeVisible();
    await expect.poll(() => new URL(page.url()).searchParams.has('messageFrom')).toBe(false);

    await page.getByTestId('message-filters-clear-all').press('Enter');
    await expect(page.getByTestId('message-active-filters')).toHaveCount(0);
    await expect.poll(() => new URL(page.url()).searchParams.has('messageRead')).toBe(false);
    await expectNoHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="message-search-filters"]');
  });

  test.describe('advanced local-calendar conversion', () => {
    test.use({ timezoneId: 'America/New_York' });

    test('uses the next local midnight across a daylight-saving transition', async ({ page }, testInfo) => {
      skipUnlessDesktop(testInfo);
      await page.goto('/app/messages');
      await page.getByTestId('message-advanced-filters-open').press('Enter');
      await page.getByTestId('message-advanced-from-date').fill('2026-03-08');
      await page.getByTestId('message-advanced-to-date').fill('2026-03-08');

      const requestPromise = page.waitForRequest((request) => {
        const url = new URL(request.url());
        return url.pathname === '/api/search' && url.searchParams.has('fromDate');
      });
      await page.getByTestId('message-advanced-apply').press('Enter');
      const requestUrl = new URL((await requestPromise).url());

      expect(requestUrl.searchParams.get('fromDate')).toBe('2026-03-08T05:00:00.000Z');
      expect(requestUrl.searchParams.get('toDateExclusive')).toBe('2026-03-09T04:00:00.000Z');
      expect(
        Date.parse(requestUrl.searchParams.get('toDateExclusive')!) -
        Date.parse(requestUrl.searchParams.get('fromDate')!)
      ).toBe(23 * 60 * 60 * 1000);
    });
  });
});

function skipUnlessMobile(testInfo: TestInfo): void {
  test.skip(
    testInfo.project.name !== 'chromium-mobile',
    'Issues #355/#359/#367 use the 320px mobile drawer as their representative responsive acceptance flow.'
  );
}

function skipUnlessDesktop(testInfo: TestInfo): void {
  test.skip(testInfo.project.name !== 'chromium-desktop', 'This check covers the always-expanded desktop controls.');
}

async function installMessagingDiscoveryApi(page: Page): Promise<void> {
  const laterConversationIds = new Set([mentionOnlyId]);
  await page.route('**/api/security/csrf-token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'csrf-message-later', headerName: 'X-CSRF-Token' })
    });
  });

  await page.route('**/api/conversations**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'GET' && url.pathname === '/api/conversations') {
      const view = url.searchParams.get('view') ?? 'All';
      const authorized = conversations.map((conversation) => ({
        ...conversation,
        isLater: laterConversationIds.has(conversation.id)
      }));
      const items = authorized.filter((conversation) => {
        if (view === 'Unread') {return conversation.unreadCount > 0;}
        if (view === 'Mentions') {return conversation.hasMention;}
        if (view === 'Later') {return conversation.isLater;}
        return true;
      });
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items,
          page: 1,
          pageSize: 20,
          totalCount: items.length,
          view,
          counts: {
            all: authorized.length,
            unread: authorized.filter((conversation) => conversation.unreadCount > 0).length,
            mentions: authorized.filter((conversation) => conversation.hasMention).length,
            later: authorized.filter((conversation) => conversation.isLater).length
          }
        })
      });
      return;
    }

    const stateMatch = url.pathname.match(/^\/api\/conversations\/([^/]+)\/state$/);
    if (request.method() === 'PATCH' && stateMatch) {
      const body = request.postDataJSON() as { isLater?: unknown };
      if (body.isLater === true) {laterConversationIds.add(stateMatch[1]);}
      if (body.isLater === false) {laterConversationIds.delete(stateMatch[1]);}
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ conversationId: stateMatch[1], isLater: body.isLater })
      });
      return;
    }

    await route.fallback();
  });

  await page.route('**/api/search**', async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname === '/api/search/message-authors') {
      const query = url.searchParams.get('q');
      const selectedUserId = url.searchParams.get('selectedUserId');
      const items = query?.toLowerCase().includes('authorized') || selectedUserId === authorId
        ? [{ userId: authorId, displayName: 'Authorized Sender' }]
        : [];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items })
      });
      return;
    }

    const query = url.searchParams.get('q');
    const items =
      query === 'budget'
        ? [
            {
              type: 'Message',
              id: messageId,
              title: 'Unread mention',
              snippet: 'Budget decision is ready',
              route: `/conversations/${unreadMentionId}`,
              workspaceId,
              authorDisplayName: 'Authorized author',
              createdAt: '2026-08-29T01:15:00Z'
            }
          ]
        : [];
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        query,
        page: 1,
        pageSize: 50,
        totalCount: items.length,
        items
      })
    });
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
