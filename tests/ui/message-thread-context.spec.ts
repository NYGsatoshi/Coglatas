import { expect, test, type Page } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

const workspaceId = 'static-workspace-362';
const conversationId = 'thread-dm-362';
const rootMessageId = 'thread-root-362';
const zeroReplyRootId = 'thread-empty-root-362';
const deletedRootMessageId = 'thread-deleted-root-362';
const currentUserId = 'mock-user-a';
const channelId = 'thread-channel-settings';
const channelWorkspaceId = 'static-workspace-1';

interface ThreadApiControl {
  reads: string[];
  deniedRoot: string | null;
  holdRoot: string | null;
  release: (() => void) | null;
}

test.describe('Issue #362 mobile Message thread context', () => {
  test.beforeEach(async ({ page }) => {
    await installThreadApi(page);
    await page.setViewportSize({ width: 320, height: 800 });
  });

  test('uses a dedicated 320px thread pane with keyboard focus, an isolated draft, and no overflow', async ({ page }) => {
    await page.goto(`/app/dm/${conversationId}`);

    await expect(page.getByTestId('dm-page')).toBeVisible();
    const populatedEntry = page.getByTestId(`open-message-thread-${rootMessageId}`);
    const emptyEntry = page.getByTestId(`open-message-thread-${zeroReplyRootId}`);
    const deletedEntry = page.getByTestId(`open-message-thread-${deletedRootMessageId}`);
    await expect(populatedEntry).toContainText('↳');
    await expect(populatedEntry).toContainText('1 reply');
    await expect(emptyEntry).toContainText('Reply in thread');
    await expect(page.getByTestId('message-tombstone')).toContainText('Message deleted');
    await expect(deletedEntry).toContainText('1 reply');

    const mainDraft = page.getByTestId('message-draft');
    await mainDraft.fill('Keep this main-timeline draft');
    await populatedEntry.focus();
    await populatedEntry.press('Enter');

    const thread = page.getByTestId('thread-preview');
    await expect(thread).toBeVisible();
    await expect(thread).toBeFocused();
    await expect(page.getByTestId('dm-conversation-surface')).toBeHidden();
    await expect(page.getByTestId('thread-root-message')).toContainText('Pinned parent body');
    await expect(page.getByText('Replying in thread to Mock User B')).toBeVisible();
    await expectNoHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="thread-preview"]');

    const replyDraft = page.getByTestId('thread-reply-draft');
    await replyDraft.fill('Reply from the dedicated mobile pane');
    const postPromise = page.waitForRequest(
      (request) => request.method() === 'POST' &&
        request.url().endsWith(`/api/messages/${rootMessageId}/thread/messages`)
    );
    await replyDraft.press('Enter');
    const post = await postPromise;
    expect(post.headers()['x-csrf-token']).toBe('csrf-message-thread');
    expect(post.postDataJSON()).toMatchObject({
      body: 'Reply from the dedicated mobile pane',
      mentionedUserIds: []
    });
    expect(post.postDataJSON().clientRequestId).toMatch(/^[0-9a-f-]{36}$/i);
    await expect(thread).toContainText('2 replies');
    await expect(thread).toContainText('Reply from the dedicated mobile pane');
    await expectNoHorizontalOverflow(page);

    await thread.press('Escape');
    await expect(thread).toHaveCount(0);
    await expect(populatedEntry).toBeFocused();
    await expect(page.getByTestId('dm-conversation-surface')).toBeVisible();
    await expect(mainDraft).toHaveValue('Keep this main-timeline draft');
    await expectNoHorizontalOverflow(page);

    await deletedEntry.focus();
    await deletedEntry.press('Enter');
    const deletedThread = page.getByTestId('thread-preview');
    await expect(deletedThread).toBeVisible();
    await expect(deletedThread).toBeFocused();
    await expect(page.getByTestId('thread-root-message')).toContainText('Message deleted');
    await expect(page.getByTestId('thread-reply-draft')).toBeDisabled();
    await expect(page.getByTestId('thread-composer-disabled')).toContainText('parent message was deleted');
    await expect(page.getByText('Deleted parent secret')).toHaveCount(0);
    await expectNoHorizontalOverflow(page);
    await expectNoAccessibilityViolations(page, '[data-testid="thread-preview"]');

    await deletedThread.press('Escape');
    await expect(deletedThread).toHaveCount(0);
    await expect(deletedEntry).toBeFocused();
  });
});


test.describe('Dedicated Thread Workspace mock browser regression', () => {
  for (const width of [320, 390, 768, 1366, 1536]) {
    for (const theme of ['light', 'dark'] as const) {
      const locale = width === 390 || width === 1366 ? 'ja' : 'en';
      test(`isolates tabs, keyboard focus and IME at ${width}px / ${theme} / ${locale}`, async ({ page }, testInfo) => {
        const control = await installThreadApi(page);
        await page.setViewportSize({ width, height: 1024 });
        await page.addInitScript(({ theme, locale }) => {
          localStorage.setItem('coglatas.ui.theme.v1', theme);
          localStorage.setItem('coglatas.locale', locale);
        }, { theme, locale });
        const unsafeRequests: string[] = [];
        page.on('request', (request) => {
          const pathname = new URL(request.url()).pathname;
          if (pathname.startsWith('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && !pathname.endsWith('/read')) {
            unsafeRequests.push(`${request.method()} ${pathname}`);
          }
        });
        await page.goto(`/app/dm/${conversationId}`);
        await expect(page.getByTestId('dm-page')).toBeVisible();
        await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', theme);
        await page.getByTestId('message-draft').fill('QA main draft stays separate');
        const entry = page.getByTestId(`open-message-thread-${rootMessageId}`);
        await entry.focus();
        await entry.press('Enter');
        await expect(page.getByTestId('thread-preview')).toBeFocused();
        await expect(page.getByTestId('thread-root-message')).toContainText('Pinned parent body');
        const firstDraft = 'QA first unsent thread draft';
        const secondDraft = 'QA second unsent thread draft';
        await page.getByTestId('thread-reply-draft').fill(firstDraft);
        await page.getByTestId('thread-workspace-toggle').click();
        await expect(page.getByTestId('thread-workspace-toggle')).toHaveAttribute('aria-expanded', 'true');
        await page.getByTestId(`thread-workspace-open-${zeroReplyRootId}`).click();
        await expect(page.getByRole('tab')).toHaveCount(2);
        await expect(page.getByTestId('thread-root-message')).toContainText('Root without replies');
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue('');
        await page.getByTestId('thread-reply-draft').fill(secondDraft);

        const firstTab = page.getByTestId(`thread-tab-${rootMessageId}`);
        const secondTab = page.getByTestId(`thread-tab-${zeroReplyRootId}`);
        await expect(firstTab).not.toContainText('{number}');
        await expect(secondTab).not.toContainText('{number}');
        await expect(page.getByTestId('thread-tab-close-active')).toHaveAccessibleName(locale === 'ja' ? 'スレッド 2 を閉じる' : 'Close thread 2');
        const firstReads = control.reads.filter((root) => root === rootMessageId).length;
        control.holdRoot = rootMessageId;
        await firstTab.click();
        await expect.poll(() => control.reads.filter((root) => root === rootMessageId).length).toBe(firstReads + 1);
        await expect(page.getByTestId('thread-root-message')).toHaveCount(0);
        await expect(page.getByTestId('thread-reply-draft')).toHaveCount(0);
        control.holdRoot = null;
        control.release?.();
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue(firstDraft);
        await expect(page.getByTestId('thread-tab-close-active')).toHaveAccessibleName(locale === 'ja' ? 'スレッド 1 を閉じる' : 'Close thread 1');
        await firstTab.focus();
        await firstTab.press('ArrowRight');
        await expect(secondTab).toBeFocused();
        await expect(secondTab).toHaveAttribute('aria-selected', 'true');
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue(secondDraft);
        await secondTab.press('Home');
        await expect(firstTab).toBeFocused();
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue(firstDraft);
        await firstTab.press('End');
        await expect(secondTab).toBeFocused();
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue(secondDraft);

        const draft = page.getByTestId('thread-reply-draft');
        await draft.focus();
        // Synthetic Japanese composition events check browser handlers, not a native OS IME.
        await draft.dispatchEvent('compositionstart', { data: '未送信' });
        await draft.dispatchEvent('keydown', { key: 'Enter', code: 'Enter', isComposing: true, keyCode: 229 });
        await draft.dispatchEvent('keydown', { key: 'Escape', code: 'Escape', isComposing: true, keyCode: 229 });
        await expect(page.getByRole('tab')).toHaveCount(2);
        await expect(draft).toHaveValue(secondDraft);
        await draft.dispatchEvent('compositionend', { data: '未送信' });
        await expectNoHorizontalOverflow(page);
        const screenshotPath = testInfo.outputPath(`thread-workspace-${width}-${theme}-${locale}.png`);
        await page.screenshot({ path: screenshotPath, fullPage: true });
        await testInfo.attach('thread-workspace-diagnostic.png', { path: screenshotPath, contentType: 'image/png' });
        await expectNoAccessibilityViolations(page, '[data-testid="thread-workspace"]');

        await secondTab.focus();
        await secondTab.press('Delete');
        await expect(page.getByRole('tab')).toHaveCount(1);
        await expect(firstTab).toBeFocused();
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue(firstDraft);
        await page.getByTestId(`thread-workspace-open-${zeroReplyRootId}`).click();
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue('');
        await page.getByTestId('thread-preview').press('Escape');
        await expect(page.getByRole('tab')).toHaveCount(1);
        await expect(page.getByTestId('thread-reply-draft')).toHaveValue(firstDraft);
        await page.getByTestId('thread-preview').press('Escape');
        await expect(page.getByTestId('thread-workspace')).toHaveCount(0);
        await expect(entry).toBeFocused();
        await expect(page.getByTestId('message-draft')).toHaveValue('QA main draft stays separate');
        expect(unsafeRequests).toEqual([]);
        const persistedValues = await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)].join('\n'));
        expect(persistedValues).not.toContain(firstDraft);
        expect(persistedValues).not.toContain(secondDraft);
        await expectNoHorizontalOverflow(page);
      });
    }
  }

  for (const width of [320, 1366]) {
    test(`purges protected tabs, bodies and drafts after an authoritative 403 at ${width}px`, async ({ page }) => {
      const control = await installThreadApi(page);
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`/app/dm/${conversationId}`);
      await page.getByTestId('message-draft').fill('QA main draft requiring purge');
      await page.getByTestId(`open-message-thread-${rootMessageId}`).click();
      await page.getByTestId('thread-reply-draft').fill('QA first protected draft');
      await page.getByTestId('thread-workspace-toggle').click();
      await page.getByTestId(`thread-workspace-open-${zeroReplyRootId}`).click();
      await page.getByTestId('thread-reply-draft').fill('QA second protected draft');
      control.deniedRoot = rootMessageId;
      await page.getByTestId(`thread-tab-${rootMessageId}`).click();
      await expect(page.getByTestId('thread-load-failure')).toBeVisible();
      await expect(page.getByRole('tab')).toHaveCount(0);
      await expect(page.getByTestId('thread-root-message')).toHaveCount(0);
      await expect(page.getByTestId('thread-reply-draft')).toHaveCount(0);
      await expect(page.getByTestId('thread-workspace-toggle')).toHaveAttribute('aria-expanded', 'false');
      for (const protectedText of ['Pinned parent body', 'Root without replies', 'Existing thread reply', 'QA first protected draft', 'QA second protected draft', 'QA main draft requiring purge']) {
        await expect(page.getByText(protectedText, { exact: true })).toHaveCount(0);
      }
      await expect(page.getByTestId('message-draft')).toHaveCount(0);
      const persistedValues = await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)].join('\n'));
      for (const draftText of ['QA main draft requiring purge', 'QA first protected draft', 'QA second protected draft']) {
        expect(persistedValues).not.toContain(draftText);
      }
      await expectNoHorizontalOverflow(page);
    });
  }
});

test.describe('Channel settings alongside thread context', () => {
  for (const width of [1366, 960, 320]) {
    test(`keeps settings controls reachable and restores focus without losing active drafts at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await installThreadApi(page, channelId, 'WorkspaceChannel');
      const mutations: string[] = [];
      page.on('request', (request) => {
        const path = new URL(request.url()).pathname;
        if (path.startsWith('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && !path.endsWith('/read')) {
          mutations.push(`${request.method()} ${path}`);
        }
      });
      await page.goto(`/app/workspaces/${channelWorkspaceId}/channels/${channelId}`);
      await expect(page.getByTestId('channel-messaging-page')).toBeVisible();
      if (width > 860) {
        await expect(page.locator('.app-shell__feature-menu').getByTestId('nav-teams')).toHaveAttribute('aria-current', 'page');
        await expect(page.locator('.app-shell__feature-menu').getByTestId('nav-workspaces')).not.toHaveAttribute('aria-current', 'page');
      }
      const mainDraft = page.getByTestId('message-draft');
      await mainDraft.fill('Preserve main draft while adjusting settings');
      const entry = page.getByTestId(`open-message-thread-${rootMessageId}`);
      await entry.focus();
      await entry.press('Enter');
      const thread = page.getByTestId('thread-preview');
      await expect(thread).toBeFocused();
      const replyDraft = page.getByTestId('thread-reply-draft');
      if (width === 320) {
        await expect(page.getByTestId('conversation-surface')).toBeHidden();
        await thread.press('Escape');
        await expect(entry).toBeFocused();
      } else {
        await replyDraft.fill('Preserve the isolated thread draft');
      }

      const trigger = page.getByTestId('conversation-settings-trigger');
      await trigger.click();
      const panel = page.getByTestId('conversation-settings-panel');
      const close = page.getByTestId('conversation-settings-close');
      const level = page.getByTestId('conversation-notification-level');
      await expect(close).toBeFocused();
      await expect(level).toHaveValue('all');
      // Coalesced close/reopen activation must focus the reused dialog view.
      await panel.evaluate((element) => {
        const close = element.querySelector<HTMLButtonElement>('[data-testid="conversation-settings-close"]')!;
        const trigger = element.closest('app-conversation-settings-panel')!
          .querySelector<HTMLButtonElement>('[data-testid="conversation-settings-trigger"]')!;
        close.click();
        trigger.focus();
        trigger.click();
      });
      await expect(close).toBeFocused();
      await expect.poll(() => level.evaluate((control) => {
        const rect = control.getBoundingClientRect();
        return document.elementFromPoint(rect.left + 5, rect.top + rect.height / 2) === control;
      })).toBe(true);
      await level.click();
      await page.keyboard.press('Escape');
      if (await panel.isVisible()) {
        await close.click();
      }
      await expect(trigger).toBeFocused();
      await trigger.press('Enter');
      await expect(close).toBeFocused();
      await page.keyboard.press('Tab');
      await expect(level).toBeFocused();
      await page.keyboard.press('Escape');
      await expect(panel).toHaveCount(0);
      await expect(trigger).toBeFocused();
      await expect(mainDraft).toHaveValue('Preserve main draft while adjusting settings');

      if (width === 320) {
        await entry.focus();
        await entry.press('Enter');
        await expect(thread).toBeFocused();
      } else {
        await expect(replyDraft).toHaveValue('Preserve the isolated thread draft');
      }
      await thread.press('Escape');
      await expect(entry).toBeFocused();
      await expect(mainDraft).toHaveValue('Preserve main draft while adjusting settings');
      await expectNoHorizontalOverflow(page);
      expect(mutations).toEqual([]);
    });
  }
});

test.describe('Student council Approved Dark channel and thread diagnostics', () => {
  for (const width of [1536, 1366, 960, 768, 390, 320]) {
    test(`keeps channel and thread usable at ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 1024 });
      await page.addInitScript(() => {
        localStorage.setItem('coglatas.ui.theme.v1', 'dark');
        localStorage.setItem('coglatas.locale', 'en');
      });
      await installThreadApi(page, channelId, 'WorkspaceChannel');
      const posts: string[] = [];
      page.on('request', (request) => {
        if (request.method() === 'POST' && /\/messages$/.test(new URL(request.url()).pathname)) {
          posts.push(request.url());
        }
      });
      await page.goto(`/app/workspaces/${channelWorkspaceId}/channels/${channelId}`);
      await expect(page.getByTestId('channel-messaging-page')).toBeVisible();
      const conversation = page.getByTestId('conversation-surface');
      const mainDraft = page.getByTestId('message-draft');
      await mainDraft.fill('Synthetic pilot draft');
      await mainDraft.dispatchEvent('compositionstart', { data: '確定' });
      await mainDraft.dispatchEvent('keydown', { key: 'Enter', isComposing: true, keyCode: 229 });
      await mainDraft.dispatchEvent('compositionend', { data: '確定' });
      await mainDraft.dispatchEvent('keydown', { key: 'Enter', keyCode: 229 });
      await expect(mainDraft).toHaveValue('Synthetic pilot draft');
      expect(posts).toEqual([]);
      await expectNoHorizontalOverflow(page);
      const channelPath = testInfo.outputPath(`pilot-channel-${width}-dark.png`);
      await page.screenshot({ path: channelPath, fullPage: true });
      await testInfo.attach('pilot-channel-diagnostic', { path: channelPath, contentType: 'image/png' });

      const trigger = page.getByTestId(`open-message-thread-${rootMessageId}`);
      await trigger.click();
      const thread = page.getByTestId('thread-preview');
      await expect(thread).toBeFocused();
      if (width <= 860) {
        await expect(conversation).toBeHidden();
      } else {
        await expect(conversation).toBeVisible();
        const channelBounds = await conversation.boundingBox();
        const threadBounds = await page.getByTestId('thread-workspace').boundingBox();
        expect(threadBounds!.y).toBeLessThan(channelBounds!.y + 80);
        if (width <= 1199) await expect(page.locator('.messaging-page__rail')).toBeHidden();
      }
      await page.getByTestId('thread-reply-draft').fill('Synthetic isolated thread draft');
      await expectNoHorizontalOverflow(page);
      await expectNoAccessibilityViolations(page, '[data-testid="thread-workspace"]');
      const threadPath = testInfo.outputPath(`pilot-channel-thread-${width}-dark.png`);
      await page.screenshot({ path: threadPath, fullPage: true });
      await testInfo.attach('pilot-channel-thread-diagnostic', { path: threadPath, contentType: 'image/png' });
      await thread.press('Escape');
      await expect(trigger).toBeFocused();
      await expect(mainDraft).toHaveValue('Synthetic pilot draft');
      expect(posts).toEqual([]);
    });
  }
});

test.describe('Student council Approved Dark DM diagnostics', () => {
  for (const width of [1536, 1366, 768, 390, 320]) {
    test(`keeps DM and thread usable at ${width}px`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 1024 });
      await page.addInitScript(() => {
        localStorage.setItem('coglatas.ui.theme.v1', 'dark');
        localStorage.setItem('coglatas.locale', 'en');
      });
      await installThreadApi(page);
      const posts: string[] = [];
      page.on('request', (request) => {
        if (request.method() === 'POST' && /\/messages$/.test(new URL(request.url()).pathname)) posts.push(request.url());
      });
      await page.goto(`/app/dm/${conversationId}`);
      await expect(page.getByTestId('dm-conversation-surface')).toBeVisible();
      const draft = page.getByTestId('message-draft');
      await draft.fill('Synthetic private DM draft');
      await draft.dispatchEvent('compositionstart', { data: '確定' });
      await draft.dispatchEvent('keydown', { key: 'Enter', isComposing: true, keyCode: 229 });
      await draft.dispatchEvent('compositionend', { data: '確定' });
      await draft.dispatchEvent('keydown', { key: 'Enter', keyCode: 229 });
      await expect(draft).toHaveValue('Synthetic private DM draft');
      expect(posts).toEqual([]);
      await expectNoHorizontalOverflow(page);
      await expectNoAccessibilityViolations(page, '[data-testid="dm-page"]');
      await page.screenshot({ path: testInfo.outputPath(`pilot-dm-${width}-dark.png`) });
      const trigger = page.getByTestId(`open-message-thread-${rootMessageId}`);
      await trigger.focus();
      await trigger.press('Enter');
      await expect(page.getByTestId('thread-preview')).toBeFocused();
      await page.getByTestId('thread-reply-draft').fill('Synthetic isolated DM reply draft');
      await expectNoHorizontalOverflow(page);
      await expectNoAccessibilityViolations(page, '[data-testid="thread-workspace"]');
      await page.screenshot({ path: testInfo.outputPath(`pilot-dm-thread-${width}-dark.png`) });
      await page.getByTestId('thread-preview').press('Escape');
      await expect(trigger).toBeFocused();
      await expect(draft).toHaveValue('Synthetic private DM draft');
      expect(posts).toEqual([]);
    });
  }
});

async function installThreadApi(page: Page, scopedConversationId = conversationId, type = 'DirectMessage'): Promise<ThreadApiControl> {
  const control: ThreadApiControl = { reads: [], deniedRoot: null, holdRoot: null, release: null };
  let replies = [threadReply('thread-reply-362', 'Existing thread reply')];
  const scopedWorkspaceId = type === 'WorkspaceChannel' ? channelWorkspaceId : workspaceId;
  const inConversation = (message: Record<string, unknown>) => ({ ...message, workspaceId: scopedWorkspaceId, conversationId: scopedConversationId });

  await page.route('**/api/security/csrf-token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'csrf-message-thread', headerName: 'X-CSRF-Token' })
    });
  });

  await page.route('**/api/messages/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const threadMatch = path.match(/^\/api\/messages\/([^/]+)\/thread$/);
    const postMatch = path.match(/^\/api\/messages\/([^/]+)\/thread\/messages$/);

    if (threadMatch && request.method() === 'GET') {
      control.reads.push(threadMatch[1]);
      if (control.holdRoot === threadMatch[1]) {
        await new Promise<void>((resolve) => { control.release = resolve; });
        control.release = null;
      }
      if (control.deniedRoot === threadMatch[1]) {
        await route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ error: 'Permission denied.' }) });
        return;
      }
    }

    if (threadMatch && request.method() === 'GET' && threadMatch[1] === rootMessageId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...threadProjection(replies), rootMessage: inConversation(rootMessage(rootMessageId, 'Pinned parent body')), replies: replies.map(inConversation) })
      });
      return;
    }

    if (threadMatch && request.method() === 'GET' && threadMatch[1] === deletedRootMessageId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          rootMessage: inConversation(rootMessage(deletedRootMessageId, 'Deleted parent secret', true)),
          replies: [inConversation(threadReply('thread-deleted-root-reply-362', 'Durable reply', undefined, deletedRootMessageId))],
          summary: threadSummary(1, deletedRootMessageId),
          hasMore: false,
          maximumReplies: 100
        })
      });
      return;
    }

    if (threadMatch && request.method() === 'GET' && threadMatch[1] === zeroReplyRootId) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
        rootMessage: inConversation(rootMessage(zeroReplyRootId, 'Root without replies')),
        replies: [], summary: threadSummary(0, zeroReplyRootId), hasMore: false, maximumReplies: 100
      }) });
      return;
    }

    if (postMatch && request.method() === 'POST' && postMatch[1] === rootMessageId) {
      const payload = request.postDataJSON() as { body?: string; clientRequestId?: string };
      const created = threadReply(
        'thread-reply-created-362',
        payload.body ?? '',
        payload.clientRequestId
      );
      replies = [...replies, created];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          message: created,
          summary: threadSummary(replies.length)
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.route('**/api/conversations**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const detailMatch = path.match(/^\/api\/conversations\/([^/]+)$/);
    const messagesMatch = path.match(/^\/api\/conversations\/([^/]+)\/messages$/);

    if (path === '/api/conversations' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [{
            id: scopedConversationId,
            workspaceId: scopedWorkspaceId,
            type,
            title: 'Thread DM',
            unreadCount: 0,
            createdAt: '2026-08-27T00:00:00Z'
          }]
        })
      });
      return;
    }

    if (messagesMatch && request.method() === 'GET' && messagesMatch[1] === scopedConversationId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            { ...rootMessage(rootMessageId, 'Pinned parent body'), thread: threadSummary(replies.length) },
            rootMessage(zeroReplyRootId, 'Root without replies'),
            {
              ...rootMessage(deletedRootMessageId, 'Deleted parent secret', true),
              thread: threadSummary(1, deletedRootMessageId)
            }
          ].map(inConversation)
        })
      });
      return;
    }

    if (detailMatch && request.method() === 'GET' && detailMatch[1] === scopedConversationId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: scopedConversationId,
          workspaceId: scopedWorkspaceId,
          type,
          title: 'Thread DM',
          isLocked: false,
          isArchived: false,
          createdAt: '2026-08-27T00:00:00Z',
          members: [
            {
              userId: currentUserId,
              displayName: 'Mock User A',
              canRead: true,
              canPost: true,
              canCreateThread: true,
              leftAt: null,
              removedAt: null
            },
            {
              userId: 'mock-user-b',
              displayName: 'Mock User B',
              canRead: true,
              canPost: true,
              canCreateThread: true,
              leftAt: null,
              removedAt: null
            }
          ]
        })
      });
      return;
    }

    if (path === `/api/conversations/${scopedConversationId}/state` && request.method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
        conversationId: scopedConversationId, userId: currentUserId, isMuted: false, isLater: false, unreadCount: 0
      }) });
      return;
    }

    await route.fallback();
  });
  return control;
}

function threadProjection(replies: readonly Record<string, unknown>[]): Record<string, unknown> {
  return {
    rootMessage: rootMessage(rootMessageId, 'Pinned parent body'),
    replies,
    summary: threadSummary(replies.length),
    hasMore: false,
    maximumReplies: 100
  };
}

function threadSummary(replyCount: number, threadRootMessageId = rootMessageId): Record<string, unknown> {
  return {
    threadRootMessageId,
    replyCount,
    latestReplyAt: replyCount > 0 ? '2026-08-27T02:00:00Z' : null,
    participantDisplayNames: replyCount > 1 ? ['Mock User B', 'Mock User A'] : ['Mock User B']
  };
}

function rootMessage(id: string, body: string, isDeleted = false): Record<string, unknown> {
  return {
    id,
    workspaceId,
    conversationId,
    authorUserId: 'mock-user-b',
    authorDisplayName: 'Mock User B',
    body,
    attachments: [],
    createdAt: '2026-08-27T01:00:00Z',
    isDeleted,
    version: 1
  };
}

function threadReply(
  id: string,
  body: string,
  clientRequestId?: string,
  threadRootMessageId = rootMessageId
): Record<string, unknown> {
  return {
    id,
    workspaceId,
    conversationId,
    authorUserId: id.includes('created') ? currentUserId : 'mock-user-b',
    authorDisplayName: id.includes('created') ? 'Mock User A' : 'Mock User B',
    body,
    attachments: [],
    createdAt: id.includes('created') ? '2026-08-27T03:00:00Z' : '2026-08-27T02:00:00Z',
    isDeleted: false,
    version: 1,
    clientRequestId,
    threadRootMessageId
  };
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
