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
      await expectSendUnobstructed(page);
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
      await expectSendUnobstructed(page);
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

async function expectSendUnobstructed(page: Page): Promise<void> {
  const send = page.getByTestId('send-message');
  await expect(send).toBeEnabled();
  await send.scrollIntoViewIfNeeded();
  expect(await send.evaluate((button) => {
    const bounds = button.getBoundingClientRect();
    return [0.25, 0.5, 0.75].every((fraction) => {
      const target = document.elementFromPoint(bounds.x + bounds.width * fraction, bounds.y + bounds.height / 2);
      return target !== null && button.contains(target);
    });
  })).toBe(true);
}

test('keeps Channel messages, thread and Send reachable in a short mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 420 });
  await installThreadApi(page, channelId, 'WorkspaceChannel');
  const sentBodies: string[] = [];
  await page.route(`**/api/conversations/${channelId}/messages`, async (route) => {
    if (route.request().method() !== 'POST') { await route.fallback(); return; }
    const payload = route.request().postDataJSON() as { body: string; clientRequestId: string };
    sentBodies.push(payload.body);
    expect(route.request().headers()['x-csrf-token']).toBe('csrf-message-thread');
    await route.fulfill({ json: {
      id: 'short-viewport-confirmed', workspaceId: channelWorkspaceId, conversationId: channelId,
      authorUserId: currentUserId, authorDisplayName: 'Mock User A', body: payload.body,
      clientRequestId: payload.clientRequestId, createdAt: '2026-10-19T08:00:00Z', isDeleted: false, version: 1,
    } });
  });
  await page.goto(`/app/workspaces/${channelWorkspaceId}/channels/${channelId}`);
  await expect(page.getByTestId('channel-messaging-page')).toBeVisible();
  const draft = page.getByTestId('message-draft');
  await draft.fill('Separate main Channel draft');
  const entry = page.getByTestId(`open-message-thread-${rootMessageId}`);
  await entry.scrollIntoViewIfNeeded();
  await entry.click();
  const thread = page.getByTestId('thread-preview');
  await expect(thread).toBeFocused();
  const replyDraft = page.getByTestId('thread-reply-draft');
  await replyDraft.scrollIntoViewIfNeeded();
  await replyDraft.click();
  await replyDraft.fill('Short viewport thread draft');
  await expect(replyDraft).toHaveValue('Short viewport thread draft');
  await expectNoHorizontalOverflow(page);
  await thread.press('Escape');
  await expect(entry).toBeFocused();
  await expect(draft).toHaveValue('Separate main Channel draft');
  await draft.scrollIntoViewIfNeeded();
  await draft.click();
  await draft.fill('Short viewport Channel message');
  await draft.dispatchEvent('compositionstart');
  await draft.press('Enter');
  expect(sentBodies).toEqual([]);
  await draft.dispatchEvent('compositionend');
  await draft.fill('Short viewport Channel message');
  const send = page.getByTestId('send-message');
  await send.scrollIntoViewIfNeeded();
  await send.click();
  await expect(draft).toHaveValue('');
  expect(sentBodies).toEqual(['Short viewport Channel message']);
  const ownBody = page.getByTestId('message-body').filter({ hasText: 'Short viewport Channel message' });
  await expect(ownBody).toHaveCount(1);
  await ownBody.scrollIntoViewIfNeeded();
  const bodyVisibility = await ownBody.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const text = document.createRange();
    text.selectNodeContents(element);
    return {
      box: { x: box.x, y: box.y, width: box.width, height: box.height },
      textBoxes: Array.from(text.getClientRects(), (rect) => ({ x: rect.x, y: rect.y, width: rect.width, height: rect.height })),
      samples: [0.1, 0.5, 0.9].map((fraction) => {
        const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height * fraction);
        const hitBox = hit?.getBoundingClientRect();
        return { fraction, hitIsBody: hit !== null && (hit === element || element.contains(hit)),
          hitTag: hit?.tagName, hitClass: hit?.getAttribute('class'), hitTestId: hit?.getAttribute('data-testid'),
          hitBox: hitBox ? { x: hitBox.x, y: hitBox.y, width: hitBox.width, height: hitBox.height } : null };
      }),
    };
  });
  expect(bodyVisibility.samples.every((sample) => sample.hitIsBody), JSON.stringify(bodyVisibility)).toBe(true);
  await expectNoHorizontalOverflow(page);
  await page.setViewportSize({ width: 320, height: 800 });
  await expect.poll(() => page.locator('app-message-composer').evaluate((element) => getComputedStyle(element).position)).toBe('sticky');
  await page.setViewportSize({ width: 861, height: 420 });
  await expect.poll(() => page.locator('app-message-composer').evaluate((element) => getComputedStyle(element).position)).toBe('static');
  await expectNoHorizontalOverflow(page);
});

test('keeps a compact composer with keyboard-accessible mention and help disclosure', async ({ page }, testInfo) => {
  const width = testInfo.project.name === 'chromium-mobile' ? 320 : 1536;
  await page.setViewportSize({ width, height: 800 });
  await installThreadApi(page, channelId, 'WorkspaceChannel');
  await page.goto(`/app/workspaces/${channelWorkspaceId}/channels/${channelId}`);
  const composer = page.getByTestId('message-composer');
  await expect(page.getByTestId('channel-messaging-page')).toBeVisible();
  const draft = page.getByTestId('message-draft');
  await draft.fill('Keep this draft');
  expect((await composer.boundingBox())?.height).toBeLessThanOrEqual(128);
  const options = page.getByTestId('composer-options-toggle');
  const mention = page.getByTestId('mention-candidate').first();
  await expect(mention).toBeHidden();
  await expect(page.getByTestId('attachment-disabled')).toBeHidden();
  await options.focus();
  await options.press('Enter');
  await expect(mention).toBeVisible();
  expect((await options.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  expect((await mention.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  await expect(page.getByTestId('attachment-disabled')).toContainText('Attachments are disabled');
  await expect(page.locator('#composer-keyboard-hint')).toBeVisible();
  await mention.click();
  await expect(draft).toHaveValue(/Keep this draft @/);
  await options.press('Enter');
  await expect(mention).toBeHidden();
  await expect(options).toBeFocused();
  await expect(draft).toHaveValue(/Keep this draft @/);
  await expect(page.getByTestId('send-message')).toBeEnabled();
  expect((await page.getByTestId('send-message').boundingBox())?.height).toBeGreaterThanOrEqual(44);
  await expectNoHorizontalOverflow(page);
});

test('shows server Channel members in a desktop inspector or keyboard disclosure and replaces them with Thread context', async ({ page }, testInfo) => {
  const mobile = testInfo.project.name === 'chromium-mobile';
  await page.setViewportSize({ width: mobile ? 320 : 1536, height: 800 });
  await installThreadApi(page, channelId, 'WorkspaceChannel');
  await installParticipantDetail(page);
  await page.goto(`/app/workspaces/${channelWorkspaceId}/channels/${channelId}`);
  const inspector = page.getByTestId('channel-details-inspector');
  const disclosure = page.getByTestId('channel-members-disclosure');
  if (mobile) {
    await expect(inspector).toBeHidden();
    await expect(disclosure).toBeVisible();
    const summary = disclosure.locator('summary');
    expect((await summary.boundingBox())?.height).toBeGreaterThanOrEqual(44);
    await expect(disclosure.getByTestId('channel-participants-list')).toBeHidden();
    await summary.focus();
    await summary.press('Enter');
  } else {
    await expect(inspector).toBeVisible();
    await expect(disclosure).toBeHidden();
    expect((await inspector.boundingBox())?.width).toBe(300);
  }
  const visibleMembers = mobile ? disclosure : inspector;
  await expect(visibleMembers.getByTestId('channel-participant')).toHaveCount(2);
  await expect(visibleMembers).toContainText('Council Viewer');
  await expect(visibleMembers).toContainText('You');
  await expect(visibleMembers).toContainText('Admin');
  await expect(visibleMembers).toContainText('Council Reader');
  await expect(visibleMembers).toContainText('Read only');
  await expect(page.getByText('Removed private member', { exact: true })).toHaveCount(0);
  await expectNoAccessibilityViolations(page, mobile ? '[data-testid="channel-members-disclosure"]' : '[data-testid="channel-details-inspector"]');
  const draft = page.getByTestId('message-draft');
  await draft.fill('Keep the Channel draft beside member details');
  const trigger = page.getByTestId(`open-message-thread-${rootMessageId}`);
  await trigger.focus();
  await trigger.press('Enter');
  await expect(page.getByTestId('thread-preview')).toBeVisible();
  await expect(inspector).toHaveCount(0);
  await expect(disclosure).toHaveCount(0);
  await page.getByTestId('thread-preview').press('Escape');
  await expect(page.getByTestId('thread-preview')).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await expect(draft).toHaveValue('Keep the Channel draft beside member details');
  await expect(mobile ? disclosure : inspector).toBeVisible();
  await expectNoHorizontalOverflow(page);
});

test('clears Channel member names during a delayed refresh and refuses them after revoked read access', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: testInfo.project.name === 'chromium-mobile' ? 320 : 1536, height: 800 });
  await installThreadApi(page, channelId, 'WorkspaceChannel');
  const control = await installParticipantDetail(page);
  await page.goto(`/app/workspaces/${channelWorkspaceId}/channels/${channelId}`);
  await expect(page.getByTestId('channel-participant')).toHaveCount(4);
  control.hold = true;
  await page.getByTestId('manual-refresh').click();
  await expect.poll(() => control.release !== null).toBe(true);
  await expect(page.getByTestId('channel-participant')).toHaveCount(0);
  await expect(page.getByText('Council Reader', { exact: true })).toHaveCount(0);
  control.denied = true;
  control.release!();
  await expect(page.getByTestId('body-hidden-state')).toBeVisible();
  await expect(page.getByTestId('channel-participant')).toHaveCount(0);
  await expect(page.getByText('Council Reader', { exact: true })).toHaveCount(0);
  await expect(page.getByTestId('message-body')).toHaveCount(0);
  await expectNoHorizontalOverflow(page);
});

async function installParticipantDetail(page: Page) {
  const control = { hold: false, denied: false, release: null as (() => void) | null };
  await page.route(`**/api/conversations/${channelId}`, async (route) => {
    expect(route.request().method()).toBe('GET');
    if (control.hold) {
      await new Promise<void>(resolve => { control.release = resolve; });
      control.release = null;
    }
    if (control.denied) {
      await route.fulfill({ status: 403, json: { error: 'Permission denied.' } });
      return;
    }
    await route.fulfill({ json: {
      id: channelId, workspaceId: channelWorkspaceId, type: 'WorkspaceChannel', title: 'Council channel',
      members: [
        { userId: currentUserId, displayName: 'Council Viewer', role: 0, canRead: true, canPost: true, canCreateThread: true },
        { userId: 'mock-user-b', displayName: 'Council Reader', role: 2, canRead: true },
        { userId: 'removed-user', displayName: 'Removed private member', role: 1, canRead: true, removedAt: '2026-10-19T00:00:00Z' },
      ],
    } });
  });
  return control;
}

test.describe('Compact Thread workspace presentation', () => {
  for (const width of [320, 1536]) {
    test(`keeps loaded cards, fresh authorization and compact reply recovery operable at ${width}px`, async ({ page }, testInfo) => {
      const control = await installThreadApi(page, channelId, 'WorkspaceChannel');
      await page.setViewportSize({ width, height: 1024 });
      await page.addInitScript(() => {
        localStorage.setItem('coglatas.locale', 'en');
        localStorage.setItem('coglatas.ui.theme.v1', 'dark');
      });
      await page.goto(`/app/workspaces/${channelWorkspaceId}/channels/${channelId}`);
      await page.getByTestId('message-draft').fill('Keep the main draft');
      await page.getByTestId(`open-message-thread-${rootMessageId}`).click();
      await expect(page.getByTestId('thread-root-message')).toContainText('Pinned parent body');
      await page.getByTestId('thread-workspace-toggle').click();
      const active = page.getByTestId(`thread-workspace-open-${rootMessageId}`);
      const unopened = page.getByTestId(`thread-workspace-open-${zeroReplyRootId}`);
      await expect(active).toHaveAttribute('aria-current', 'true');
      await expect(active).toContainText('1 reply');
      await expect(active).toContainText('Open in this view');
      await expect(active.locator('time')).toHaveAttribute('datetime', '2026-08-27T02:00:00Z');
      await expect(unopened).not.toContainText('0 replies');
      await expect(unopened.locator('time')).toHaveCount(0);
      await expect(page.getByTestId(`thread-workspace-open-${deletedRootMessageId}`)).not.toContainText('Deleted parent secret');
      await expect(page.getByTestId('thread-workspace')).toContainText('not a complete thread directory');

      await unopened.focus();
      await unopened.press('Enter');
      await expect(page.getByTestId('thread-root-message')).toContainText('Root without replies');
      control.holdRoot = rootMessageId;
      await active.click();
      await expect(page.getByTestId('thread-root-message')).toHaveCount(0);
      await expect(page.getByTestId('thread-reply-draft')).toHaveCount(0);
      await expect.poll(() => control.release !== null).toBe(true);
      control.holdRoot = null;
      control.release?.();
      const draft = page.getByTestId('thread-reply-draft');
      await expect(draft).toBeVisible();
      await draft.fill('Japanese composition reply');
      await draft.focus();
      const posts: string[] = [];
      let releasePost: (() => void) | undefined;
      await page.route(`**/api/messages/${rootMessageId}/thread/messages`, async (route) => {
        if (route.request().method() !== 'POST') { await route.fallback(); return; }
        posts.push(route.request().postDataJSON().clientRequestId);
        if (posts.length === 1) {
          await new Promise<void>((resolve) => { releasePost = resolve; });
          await route.fulfill({ status: 503, contentType: 'application/json', body: '{}' });
        } else {
          await route.fallback();
        }
      });
      // Browser composition events cover handler behavior; they do not emulate a native OS IME.
      await draft.dispatchEvent('compositionstart', { data: '未確定' });
      await draft.dispatchEvent('keydown', { key: 'Enter', isComposing: true, keyCode: 229 });
      await draft.dispatchEvent('keydown', { key: 'Escape', isComposing: true, keyCode: 229 });
      await draft.dispatchEvent('compositionend', { data: '未確定' });
      await draft.dispatchEvent('keydown', { key: 'Enter', keyCode: 229 });
      expect(posts).toHaveLength(0);
      await expect(draft).toBeFocused();
      await draft.press('Shift+Enter');
      await expect(draft).toHaveValue('Japanese composition reply\n');
      await draft.fill('Retry this exact reply');
      const composer = page.getByTestId('thread-composer');
      const reply = page.getByTestId('send-thread-reply');
      expect((await composer.boundingBox())!.height).toBeLessThanOrEqual(144);
      expect((await reply.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      await draft.press('Enter');
      await expect(reply).toBeDisabled();
      await expect(reply).toHaveAttribute('aria-busy', 'true');
      await expect(draft).toBeDisabled();
      await expect.poll(() => releasePost !== undefined).toBe(true);
      releasePost!();
      await expect(composer.getByRole('alert')).toBeVisible();
      await expect(draft).toHaveValue('Retry this exact reply');
      await expect(reply).toBeEnabled();
      await reply.click();
      await expect(draft).toHaveValue('');
      await expect(page.getByTestId('thread-replies')).toContainText('Retry this exact reply');
      expect(posts).toHaveLength(2);
      expect(posts[1]).toBe(posts[0]);
      await expect(page.getByTestId('message-draft')).toHaveValue('Keep the main draft');
      await expectNoHorizontalOverflow(page);
      await expectNoAccessibilityViolations(page, '[data-testid="thread-workspace"]');
      const screenshotPath = testInfo.outputPath(`compact-thread-${width}.png`);
      await page.screenshot({ path: screenshotPath, fullPage: true });
      await testInfo.attach('compact-thread.png', { path: screenshotPath, contentType: 'image/png' });
      control.deniedRoot = zeroReplyRootId;
      await page.getByTestId(`thread-tab-${zeroReplyRootId}`).click();
      await expect(page.getByTestId('thread-load-failure')).toContainText('This thread is no longer available.');
      await expect(page.getByTestId('thread-root-message')).toHaveCount(0);
      await expect(page.getByTestId('thread-replies')).toHaveCount(0);
      await expect(page.getByTestId('thread-reply-draft')).toHaveCount(0);
      await expect(page.getByRole('tab')).toHaveCount(0);
      await expect(page.locator('[data-testid^="thread-workspace-open-"]')).toHaveCount(0);
      await expect(page.getByText('Pinned parent body', { exact: true })).toHaveCount(0);
      await expect(page.getByTestId('message-draft')).toHaveCount(0);
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
          message: inConversation(created),
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
