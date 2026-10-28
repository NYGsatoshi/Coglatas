import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';
import { observeGroupKeyboardCase } from './helpers/group-keyboard-observation.mjs';

const workspaceId = 'static-workspace-1';
const groupId = '77777777-7777-4777-8777-777777777777';
const actor = 'mock-user-a';

test.describe('Group native viewport across later realtime catch-up (mock HTTP)', () => {
  for (const result of ['initial-synchronization', 'authorized-before-check', 'authorized-during-check', 'outside-focus', 'denied'] as const) {
    test(`keeps only current authority and viewport intent: ${result}`, async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 420 });
      const control = await installGroupApi(page);
      control.manager = false; control.readOnly = true; control.longRoster = true;
      await page.addInitScript(() => {
        const original = HTMLElement.prototype.scrollIntoView;
        const scope = window as typeof window & { rosterScrolls?: number };
        scope.rosterScrolls = 0;
        HTMLElement.prototype.scrollIntoView = function (options) {
          if (this.matches('[data-testid="group-members-panel"] summary, .group-members__list, [data-testid="group-leave"]')) {
            scope.rosterScrolls!++;
          }
          original.call(this, options);
        };
      });
      await page.route('**/api/ui/runtime-config.js', route => route.fulfill({
        contentType: 'application/javascript', body: 'window.__COGLATAS_FEATURE_FLAGS__={"realtime.signalR":true};'
      }));
      await page.route('**/hubs/app/negotiate?*', route => route.fulfill({ json: {
        negotiateVersion: 1, connectionId: 'synthetic-roster', connectionToken: 'synthetic-roster',
        availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text'] }]
      } }));
      let reconnect: (() => Promise<void>) | undefined;
      let releaseInitial: (() => void) | undefined;
      await page.routeWebSocket('**/hubs/app?*', socket => {
        reconnect = () => socket.close({ code: 1012, reason: 'Synthetic reconnect control' });
        socket.onMessage(raw => {
          for (const frame of raw.toString().split('\u001e').filter(Boolean)) {
            const message = JSON.parse(frame);
            if (message.protocol) { socket.send('{}\u001e'); continue; }
            if (message.type !== 1) continue;
            const complete = () => socket.send(JSON.stringify({ type: 3, invocationId: message.invocationId,
              result: { allowed: true, code: 'Allowed' } }) + '\u001e');
            if (result === 'initial-synchronization' && message.target === 'SubscribeUser') releaseInitial = complete;
            else complete();
          }
        });
      });
      let reads = 0;
      let holdRefresh = false;
      let releaseDetail = () => {};
      const detailGate = new Promise<void>(resolve => { releaseDetail = resolve; });
      await page.route(`**/api/conversations/${groupId}`, async route => {
        if (++reads > 1 && holdRefresh) await detailGate;
        await route.fallback();
      });
      await page.goto(`/app/dm/${groupId}`);
      const panel = page.getByTestId('group-members-panel');
      const summary = panel.locator('summary');
      if (result === 'initial-synchronization') {
        await expect.poll(() => !!releaseInitial).toBe(true);
        await expect(page.getByTestId('confirmed-message')).toHaveCount(1);
        // The initial HTTP projection cannot expose a temporary roster while
        // the first transport synchronization still owes its current catch-up.
        await expect(panel).toHaveCount(0);
        holdRefresh = true;
        releaseInitial!();
        await expect.poll(() => reads).toBeGreaterThanOrEqual(2);
        await expect(panel).toHaveCount(0);
        await expect(page.getByTestId('confirmed-message')).toHaveCount(0);
        releaseDetail();
        await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
        await expect(panel.getByTestId('group-member-row')).toHaveCount(43);
        await summary.scrollIntoViewIfNeeded();
        await expect(summary).toBeInViewport({ ratio: 1 });
        expect(await page.evaluate(() => (window as typeof window & { rosterScrolls?: number }).rosterScrolls)).toBe(0);
        return;
      }
      await expect(page.getByTestId('realtime-connection-state')).toContainText('Realtime updates connected.');
      await expect(panel.getByTestId('group-member-row')).toHaveCount(43);
      const old = await summary.elementHandle();
      // Exactly one native action, completed before the controlled catch-up.
      await summary.scrollIntoViewIfNeeded();
      await expect(summary).toBeInViewport({ ratio: 1 });
      holdRefresh = true;
      await reconnect!();
      await expect(panel).toHaveCount(0);
      await expect(page.getByTestId('confirmed-message')).toHaveCount(0);
      expect(await old!.evaluate(node => node.isConnected)).toBe(false);
      if (result === 'outside-focus') await page.getByTestId('manual-refresh').focus();
      if (result === 'denied') control.denied = true;
      if (result === 'authorized-during-check') {
        const visibility = expect(summary).toBeInViewport({ ratio: 1 });
        releaseDetail();
        await visibility;
      } else {
        releaseDetail();
      }
      if (result === 'denied') {
        await expect(page.getByTestId('dm-page')).toContainText('Conversation API request failed.');
        await expect(panel).toHaveCount(0);
        await expect(page.getByTestId('dm-page')).not.toContainText('Ordinary participant');
      } else {
        await expect(panel.getByTestId('group-member-row')).toHaveCount(43);
        if (result === 'outside-focus') {
          await expect(page.getByTestId('manual-refresh')).toBeFocused();
        } else {
          await expect(summary).toBeInViewport({ ratio: 1 });
          await summary.focus();
          await page.keyboard.press('Tab');
          const list = panel.locator('.group-members__list');
          await expect(list).toBeFocused();
          await list.scrollIntoViewIfNeeded();
          await page.keyboard.press('End');
          expect(await list.evaluate(node => Math.abs(node.scrollTop - (node.scrollHeight - node.clientHeight)))).toBeLessThanOrEqual(1);
          await page.keyboard.press('Home');
          expect(await list.evaluate(node => node.scrollTop)).toBe(0);
        }
      }
      expect(control.sends).toEqual([]);
      expect(control.adds).toEqual([]);
      expect(await page.evaluate(() => (window as typeof window & { rosterScrolls?: number }).rosterScrolls))
        .toBe(result.startsWith('authorized') ? 1 : 0);
    });
  }
});

test.describe('Student Council Group DM mock frontend contract', () => {
  for (const width of [1536, 1366, 768, 390, 320]) {
    const locale = width === 390 ? 'ja' : 'en';
    test(`creates and sends with workspace recipients, keyboard and IME at ${width}px / ${locale}`, async ({ page }, testInfo) => {
      const control = await installGroupApi(page);
      control.existingGroup = false;
      await page.setViewportSize({ width, height: 1024 });
      await page.addInitScript((locale) => { localStorage.setItem('coglatas.ui.theme.v1', 'dark'); localStorage.setItem('coglatas.locale', locale); }, locale);
      await page.goto('/app/dm');
      await page.getByTestId('new-message-button').click();
      const dialog = page.getByTestId('new-message-dialog');
      await page.getByTestId('group-create-mode').check();
      await expect(page.getByTestId('group-recipient-option')).toHaveCount(3);
      await expect(dialog).not.toContainText('Suspended candidate');
      const title = page.getByTestId('group-title');
      await title.fill('Council preparation');
      const sameTitle = await title.elementHandle();
      await title.dispatchEvent('compositionstart', { data: '生徒会' });
      await title.dispatchEvent('keydown', { key: 'Escape', code: 'Escape', isComposing: true, keyCode: 229 });
      await expect(dialog).toBeVisible();
      expect(await sameTitle!.evaluate((element) => element.isConnected)).toBe(true);
      await title.dispatchEvent('compositionend', { data: '生徒会' });
      await page.getByTestId('group-recipient-option').filter({ hasText: 'Council peer' }).click();
      await expect(page.getByTestId('create-conversation-submit')).toBeDisabled();
      await page.getByTestId('group-recipient-option').filter({ hasText: 'New participant' }).click();
      const submit = page.getByTestId('create-conversation-submit');
      await expect(submit).toBeEnabled();
      const bounds = await submit.boundingBox();
      expect(bounds!.height).toBeGreaterThanOrEqual(44);
      expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(1024);
      await expectNoOverflow(page);
      await expectNoAccessibilityViolations(page, '[data-testid="new-message-dialog"]');
      await page.screenshot({ path: testInfo.outputPath(`group-create-${width}-dark.png`) });
      await submit.click();
      await expect(page).toHaveURL(new RegExp(`/app/dm/${groupId}$`));
      expect(control.creates).toEqual([{ type: 7, workspaceId, memberUserIds: ['peer', 'new-peer'], title: 'Council preparation' }]);
      expect(control.sharedRecipientReads).toBe(0);
      await expect(page.getByTestId('dm-page')).toContainText('Council preparation');
      await expect(page.getByTestId('group-members-panel')).toContainText('New participant');
      await expect(page.getByTestId('group-member-row')).toHaveCount(3);
      await expect(page.locator('[data-testid^="open-message-thread-"]')).toHaveCount(0);
      const draft = page.getByTestId('message-draft');
      await draft.fill('Synthetic Group IME draft');
      await draft.dispatchEvent('compositionstart');
      await draft.press('Enter');
      expect(control.sends).toHaveLength(0);
      await draft.dispatchEvent('compositionend');
      await draft.fill('Synthetic Group confirmed message');
      await page.getByTestId('send-message').click();
      await expect(draft).toHaveValue('');
      await expect(page.getByTestId('confirmed-message').filter({ hasText: 'Synthetic Group confirmed message' })).toHaveCount(1);
      expect(control.sends).toHaveLength(1);
      expect(control.sends[0]).toMatchObject({ body: 'Synthetic Group confirmed message', mentionedUserIds: [] });
      expect(control.sends[0].clientRequestId).toMatch(/^[0-9a-f-]{36}$/i);
      await page.reload();
      await expect(page.getByTestId('confirmed-message').filter({ hasText: 'Synthetic Group confirmed message' })).toHaveCount(1);
      expect(control.threadRequests).toEqual([]);
      await expectNoOverflow(page);
      await page.screenshot({ path: testInfo.outputPath(`group-conversation-${width}-dark.png`), fullPage: true });
      const panel = page.getByTestId('group-members-panel');
      const summary = panel.locator('summary');
      const leave = panel.getByTestId('group-leave');
      await summary.scrollIntoViewIfNeeded();
      await expect(summary).toBeInViewport();
      expect((await summary.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      await leave.scrollIntoViewIfNeeded();
      await expect(leave).toBeInViewport();
      expect(await leave.evaluate((element) => {
        const bounds = element.getBoundingClientRect();
        return element.contains(document.elementFromPoint(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2));
      })).toBe(true);
      await expectNoAccessibilityViolations(page, '[data-testid="group-members-panel"]');
      await page.screenshot({ path: testInfo.outputPath(`group-members-${width}-dark.png`) });
      await summary.focus();
      await page.keyboard.press('Enter');
      await expect(panel).not.toHaveAttribute('open', '');
      await expect(leave).toBeHidden();
      await page.keyboard.press('Enter');
      await expect(leave).toBeVisible();
      await expect(summary).toBeFocused();
      await testInfo.attach('group-css-viewport.json', { body: JSON.stringify(await page.evaluate(() => ({ width: innerWidth, height: innerHeight, devicePixelRatio, locale: document.documentElement.lang }))), contentType: 'application/json' });
    });
  }

  test('re-adds a former manager as Member, removes an Admin without hierarchy, and allows sole-manager leave', async ({ page }) => {
    await page.setViewportSize({ width: 1000, height: 800 });
    const control = await installGroupApi(page);
    await page.goto(`/app/dm/${groupId}`);
    const panel = page.getByTestId('group-members-panel');
    await expect(panel).toBeVisible();
    const detailsBounds = await page.locator('.dm-page__group-details').boundingBox();
    const conversationBounds = await page.getByTestId('dm-conversation-surface').boundingBox();
    expect(detailsBounds!.width).toBe(300);
    expect(detailsBounds!.x).toBeGreaterThan(conversationBounds!.x + conversationBounds!.width);
    await expect(page.getByTestId('dm-conversation-navigator')).toBeHidden();
    await page.setViewportSize({ width: 320, height: 800 });
    await expect(panel.getByTestId('group-member-row').filter({ hasText: 'Former manager' })).toContainText('Left');
    await page.getByTestId('group-add-member-open').click();
    await page.getByTestId('group-recipient-option').filter({ hasText: 'Former manager' }).click();
    await page.getByTestId('group-add-member-submit').click();
    await expect(panel.getByTestId('group-member-row').filter({ hasText: 'Former manager' })).toContainText('Member');
    await expect(panel.locator('summary')).toBeFocused();
    expect(control.adds).toEqual(['former']);
    const peer = panel.getByTestId('group-member-row').filter({ hasText: 'Council peer' });
    await expect(peer).toContainText('Admin');
    await peer.getByTestId('group-remove-member').click();
    const confirmation = page.getByRole('dialog', { name: 'Remove member', exact: true });
    await expect(confirmation).toContainText('last manager may leave or be removed');
    await confirmation.getByRole('button', { name: 'Remove member', exact: true }).click();
    await expect(panel.getByTestId('group-member-row').filter({ hasText: 'Council peer' })).toContainText('Removed');
    expect(control.removes).toEqual(['peer']);
    await page.getByTestId('group-leave').click();
    await page.getByRole('dialog', { name: 'Leave group', exact: true }).getByRole('button', { name: 'Leave group', exact: true }).click();
    await expect(page).toHaveURL(/\/app\/dm$/);
    await expect(page.getByTestId('group-members-panel')).toHaveCount(0);
    expect(control.leaves).toBe(1);
    expect(control.threadRequests).toEqual([]);
  });

  for (const verification of ['keyboard', 'axe'] as const) {
    test(`keeps an ordinary ReadOnly long roster accessible with ${verification}`, async ({ page }, testInfo) => {
      const body = async () => {
        await page.setViewportSize({ width: 1536, height: 600 });
        const control = await installGroupApi(page);
        control.manager = false; control.readOnly = true; control.longRoster = true;
        await page.goto(`/app/dm/${groupId}`);
        const panel = page.getByTestId('group-members-panel');
        const list = panel.locator('.group-members__list');
        const aside = page.locator('.dm-page__group-details');
        await expect(panel.getByTestId('group-member-row')).toHaveCount(43);
        await expect(panel.getByTestId('group-remove-member')).toHaveCount(0);
        const metrics = await list.evaluate((element) => ({ scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
          tabIndex: (element as HTMLElement).tabIndex, focusableChildren: element.querySelectorAll('button,input,a,select,textarea,[tabindex]').length }));
        expect(metrics.scrollHeight).toBeGreaterThan(metrics.clientHeight);
        const asideMetrics = await aside.evaluate((element) => ({ scrollHeight: element.scrollHeight, clientHeight: element.clientHeight }));
        expect(asideMetrics.scrollHeight).toBeGreaterThan(asideMetrics.clientHeight);
        await testInfo.attach('ordinary-group-overflow.json', { body: JSON.stringify({ list: metrics, aside: asideMetrics }), contentType: 'application/json' });
        if (verification === 'axe') {
          await expectNoAccessibilityViolations(page, '.dm-page__group-details');
        } else {
          await panel.locator('summary').focus();
          await page.keyboard.press('Tab');
          await expect(list).toBeFocused();
          await page.keyboard.press('End');
          await expect.poll(() => list.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
          await page.keyboard.press('Tab');
          await expect(panel.getByTestId('group-leave')).toBeFocused();
          await expect.poll(() => aside.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
          await expect(panel.getByTestId('group-member-row').last().locator('strong')).toBeInViewport({ ratio: 1 });
        await expect(panel.getByTestId('group-member-row').last().locator('small')).toBeInViewport({ ratio: 1 });
          await page.keyboard.press('Shift+Tab');
          await expect(list).toBeFocused();
          await page.keyboard.press('Home');
          await expect.poll(() => list.evaluate((element) => element.scrollTop)).toBe(0);
          await page.keyboard.press('Shift+Tab');
          await expect(panel.locator('summary')).toBeFocused();
          await expect(panel.getByTestId('group-member-row').first().locator('strong')).toBeInViewport({ ratio: 1 });
        await expect(panel.getByTestId('group-member-row').first().locator('small')).toBeInViewport({ ratio: 1 });
        }
        expect(control.adds).toEqual([]); expect(control.sends).toEqual([]); expect(control.threadRequests).toEqual([]);
      };
      if (verification === 'keyboard') { await observeGroupKeyboardCase(page, testInfo, body); }
      else { await body(); }
    });
  }

  test('honors rapid focused roster Home and End reversals without a pending native scroll', async ({ page }) => {
    await page.setViewportSize({ width: 1536, height: 600 });
    const control = await installGroupApi(page);
    control.manager = false; control.readOnly = true; control.longRoster = true;
    await page.goto(`/app/dm/${groupId}`);
    const panel = page.getByTestId('group-members-panel');
    const list = panel.locator('.group-members__list');
    await expect(panel.getByTestId('group-member-row')).toHaveCount(43);
    await panel.locator('summary').focus();
    await page.keyboard.press('Tab');
    await expect(list).toBeFocused();
    // Each completed key must cancel the preceding boundary scroll, including
    // reversal before a native smooth-scroll animation would have settled.
    for (const key of ['End', 'Home', 'End', 'Home'] as const) {
      await page.keyboard.press(key);
      const position = await list.evaluate((element) => ({ top: element.scrollTop,
        maximum: element.scrollHeight - element.clientHeight }));
      expect(position.maximum).toBeGreaterThan(0);
      expect(position.top).toBe(key === 'Home' ? 0 : position.maximum);
      await expect(list).toBeFocused();
    }
    await expect(panel.getByTestId('group-remove-member')).toHaveCount(0);
    expect(control.adds).toEqual([]); expect(control.sends).toEqual([]); expect(control.threadRequests).toEqual([]);
  });

  test('keeps a 43-member ReadOnly roster keyboard-reachable at 320x420', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 420 });
    const control = await installGroupApi(page);
    control.manager = false; control.readOnly = true; control.longRoster = true;
    await page.goto(`/app/dm/${groupId}`);
    const panel = page.getByTestId('group-members-panel');
    const summary = panel.locator('summary');
    const list = panel.locator('.group-members__list');
    await expect(panel.getByTestId('group-member-row')).toHaveCount(43);
    await expect(page.getByTestId('message-draft')).toBeDisabled();
    await summary.scrollIntoViewIfNeeded();
    await expect(summary).toBeInViewport({ ratio: 1 });
    await summary.focus();
    await page.keyboard.press('Tab');
    await expect(list).toBeFocused();
    await list.scrollIntoViewIfNeeded();
    await expect(list).toBeInViewport();
    await expect(list).toBeFocused();
    await page.keyboard.press('End');
    const end = await list.evaluate((element) => ({
      top: element.scrollTop, max: element.scrollHeight - element.clientHeight
    }));
    expect(end.max).toBeGreaterThan(0);
    expect(end.top).toBe(end.max);
    await page.keyboard.press('Tab');
    await expect(panel.getByTestId('group-leave')).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(list).toBeFocused();
    await page.keyboard.press('Home');
    expect(await list.evaluate((element) => element.scrollTop)).toBe(0);
    await page.keyboard.press('Shift+Tab');
    await expect(summary).toBeFocused();
    expect(control.adds).toEqual([]);
    expect(control.sends).toEqual([]);
    expect(control.threadRequests).toEqual([]);
  });

  test('keeps pending Group creation and nested recipient overflow accessible at 320x420', async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 320, height: 420 });
    const control = await installGroupApi(page);
    control.existingGroup = false; control.holdCreate = true; control.manyWorkspaceMembers = true;
    await page.goto('/app/dm');
    await page.getByTestId('new-message-button').click();
    await page.getByTestId('group-create-mode').check();
    await expect(page.getByTestId('group-recipient-option')).toHaveCount(43);
    await page.getByTestId('group-recipient-option').filter({ hasText: 'Council peer' }).click();
    await page.getByTestId('group-recipient-option').filter({ hasText: 'New participant' }).click();
    await page.getByTestId('create-conversation-submit').click();
    try {
      await expect.poll(() => control.creates.length).toBe(1);
      await expect(page.getByTestId('create-conversation-submit')).toBeDisabled();
      await expect(page.getByTestId('group-title')).toBeDisabled();
      const content = page.locator('.messages-page__dialog-content');
      const options = page.locator('.workspace-recipients__options');
      const metrics = await content.evaluate((element) => ({ scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
        tabIndex: (element as HTMLElement).tabIndex }));
      const optionMetrics = await options.evaluate((element) => ({ scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
        tabIndex: (element as HTMLElement).tabIndex, enabledButtons: element.querySelectorAll('button:not(:disabled)').length }));
      expect(metrics.scrollHeight).toBeGreaterThan(metrics.clientHeight);
      expect(optionMetrics.scrollHeight).toBeGreaterThan(optionMetrics.clientHeight);
      expect(optionMetrics.enabledButtons).toBe(0);
      await testInfo.attach('pending-group-overflow.json', { body: JSON.stringify({ content: metrics, options: optionMetrics }), contentType: 'application/json' });
      await page.screenshot({ path: testInfo.outputPath('pending-group-create-320-420.png') });
      await expectNoAccessibilityViolations(page, '[data-testid="new-message-dialog"]');
      await page.getByTestId('new-message-dialog').getByRole('button', { name: 'Close', exact: true }).focus();
      await page.keyboard.press('Tab');
      await expect(content).toBeFocused();
      await page.keyboard.press('End');
      await expect.poll(() => content.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
      await page.keyboard.press('Tab');
      await expect(options).toBeFocused();
      await page.keyboard.press('End');
      await expect.poll(() => options.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
      await page.keyboard.press('Shift+Tab');
      await expect(content).toBeFocused();
      await page.keyboard.press('End');
      await expect.poll(() => content.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
      await expect.poll(() => options.evaluate((element) => {
        const last = element.querySelector('button:last-child');
        if (!last) { return false; }
        const range = document.createRange(); range.selectNodeContents(last);
        const text = range.getBoundingClientRect();
        const clips = [element, element.closest('.messages-page__dialog-content')!].map((container) => {
          const bounds = container.getBoundingClientRect();
          return { left: bounds.left + container.clientLeft, top: bounds.top + container.clientTop,
            right: bounds.left + container.clientLeft + container.clientWidth, bottom: bounds.top + container.clientTop + container.clientHeight };
        });
        return text.width > 0 && text.height > 0 && clips.every((clip) => text.left >= clip.left && text.right <= clip.right && text.top >= clip.top && text.bottom <= clip.bottom)
          && text.top >= 0 && text.bottom <= innerHeight;
      })).toBe(true);
      expect(control.creates).toHaveLength(1);
    } finally {
      control.releaseCreate();
    }
  });

  test('retains a safe create error and fresh workspace choices after canonical rejection', async ({ page }) => {
    const control = await installGroupApi(page); control.rejectCreate = true;
    await page.goto('/app/dm'); await page.getByTestId('new-message-button').click();
    await page.getByRole('radio', { name: 'Group DM', exact: true }).check();
    await page.getByTestId('group-recipient-option').filter({ hasText: 'Council peer' }).click();
    await page.getByTestId('group-recipient-option').filter({ hasText: 'New participant' }).click();
    await page.getByTestId('create-conversation-submit').click();
    await expect(page.getByTestId('create-conversation-error')).toBeVisible();
    await expect(page.getByTestId('new-message-dialog')).not.toContainText('Protected raw rejection');
    await expect(page.getByTestId('create-conversation-submit')).toBeDisabled();
    await expect(page.getByTestId('group-recipient-option')).toHaveCount(3);
    expect(control.creates).toHaveLength(1); expect(control.sharedRecipientReads).toBe(0);
  });

  test('keeps ordinary ReadOnly management hidden and clears participants and bodies after revocation', async ({ page }) => {
    const control = await installGroupApi(page); control.manager = false; control.readOnly = true;
    await page.goto(`/app/dm/${groupId}`);
    await expect(page.getByTestId('message-draft')).toBeDisabled();
    await expect(page.getByTestId('group-add-member-open')).toHaveCount(0);
    await expect(page.getByTestId('group-remove-member')).toHaveCount(0);
    await expect(page.getByTestId('group-leave')).toBeEnabled();
    await expect(page.getByTestId('dm-page')).toContainText('Protected Group body');
    control.denied = true;
    await page.getByTestId('manual-refresh').click();
    await expect(page.getByTestId('group-members-panel')).toHaveCount(0);
    await expect(page.getByTestId('message-body')).toHaveCount(0);
    await expect(page.getByTestId('dm-page')).not.toContainText('Former manager');
    expect(control.sends).toHaveLength(0); expect(control.adds).toEqual([]); expect(control.threadRequests).toEqual([]);
  });
});

async function expectNoOverflow(page: Page): Promise<void> {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
}

async function installGroupApi(page: Page) {
  let releaseCreate = () => {};
  const createGate = new Promise<void>((resolve) => { releaseCreate = resolve; });
  const control = { creates: [] as Record<string, unknown>[], sends: [] as Record<string, unknown>[], adds: [] as string[], removes: [] as string[],
    leaves: 0, denied: false, existingGroup: true, rejectCreate: false, longRoster: false, holdCreate: false, manyWorkspaceMembers: false, releaseCreate: () => releaseCreate(), manager: true, readOnly: false, sharedRecipientReads: 0, threadRequests: [] as string[] };
  const workspaceMembers = [
    { userId: actor, displayName: 'Current participant', status: 1 },
    { userId: 'peer', displayName: 'Council peer', status: 1 },
    { userId: 'new-peer', displayName: 'New participant', status: 'Active' },
    { userId: 'former', displayName: 'Former manager', status: 1 },
    { userId: 'suspended', displayName: 'Suspended candidate', status: 2 }
  ];
  let title = 'Council Group';
  let members = [
    { userId: actor, displayName: 'Current participant', role: 'Admin', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null as string | null, removedAt: null as string | null },
    { userId: 'peer', displayName: 'Council peer', role: 'Admin', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null as string | null, removedAt: null as string | null },
    { userId: 'former', displayName: 'Former manager', role: 'Admin', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: '2026-10-19T00:00:00Z' as string | null, removedAt: null as string | null }
  ];
  let messages = [{ id: 'group-root', workspaceId, conversationId: groupId, authorUserId: 'peer', authorDisplayName: 'Council peer', body: 'Protected Group body',
    createdAt: '2026-10-19T01:00:00Z', clientRequestId: 'original-group-key', isDeleted: false, version: 1 }];
  const detail = () => ({ id: groupId, workspaceId, type: 'Group', title, projectId: null, parentConversationId: null, rootConversationId: null,
    isLocked: false, isArchived: false, members: [...members, ...(control.longRoster ? Array.from({ length: 40 }, (_, index) => ({ userId: `ordinary-${index}`, displayName: `Ordinary participant ${index}`, role: 'Member', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null, removedAt: null })) : [])].map((member) => member.userId === actor
      ? { ...member, role: control.readOnly ? 'ReadOnly' : control.manager ? 'Admin' : 'Member', canManageMembers: control.manager } : member) });
  await page.route('**/api/security/csrf-token', (route) => route.fulfill({ json: { token: 'csrf-group-ui', headerName: 'X-CSRF-Token' } }));
  await page.route('**/api/**', async (route) => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (path === `/api/workspaces/${workspaceId}/members` && method === 'GET') { await route.fulfill({ json: control.manyWorkspaceMembers ? [...workspaceMembers, ...Array.from({ length: 40 }, (_, index) => ({ userId: `workspace-peer-${index}`, displayName: `Workspace participant ${index}`, status: 1 }))] : workspaceMembers }); return; }
    if (path === '/api/conversations/recipients') { control.sharedRecipientReads++; await route.fulfill({ json: [] }); return; }
    if (/^\/api\/messages\/.*\/thread/.test(path)) { control.threadRequests.push(`${method} ${path}`); await route.fulfill({ status: 400, json: { error: 'Held' } }); return; }
    if (path === '/api/conversations' && method === 'GET') {
      await route.fulfill({ json: { items: control.denied || !control.existingGroup ? [] : [{ id: groupId, workspaceId, type: 'Group', title, unreadCount: 1 }], page: 1, pageSize: 20, totalCount: control.denied || !control.existingGroup ? 0 : 1 } }); return;
    }
    if (path === '/api/conversations' && method === 'POST') {
      const payload = request.postDataJSON(); control.creates.push(payload);
      expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
      if (control.holdCreate) { await createGate; }
      if (control.rejectCreate) { await route.fulfill({ status: 400, json: { error: 'Protected raw rejection' } }); return; }
      control.existingGroup = true; title = payload.title || 'Group DM'; messages = [];
      members = [
        { userId: actor, displayName: 'Current participant', role: 'Admin', canRead: true, canPost: true, canManageMembers: true, canCreateThread: false, leftAt: null, removedAt: null },
        ...payload.memberUserIds.map((userId: string) => ({ userId, displayName: workspaceMembers.find((member) => member.userId === userId)!.displayName,
          role: 'Member', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null, removedAt: null }))
      ];
      await route.fulfill({ json: detail() }); return;
    }
    if (path === `/api/conversations/${groupId}` && method === 'GET') {
      await route.fulfill(control.denied ? { status: 400, json: { error: 'Conversation not found.' } } : { json: detail() }); return;
    }
    if (path === `/api/conversations/${groupId}/messages`) {
      if (method === 'POST') {
        const payload = request.postDataJSON(); control.sends.push(payload); expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
        const message = { id: 'created-group-message', workspaceId, conversationId: groupId, createdAt: '2026-10-20T00:00:00Z', isDeleted: false, version: 1,
          authorUserId: actor, authorDisplayName: 'Current participant', body: payload.body, clientRequestId: payload.clientRequestId };
        messages = [...messages, message]; await route.fulfill({ json: message });
      } else { await route.fulfill({ json: { items: messages } }); }
      return;
    }
    if (path === `/api/conversations/${groupId}/members` && method === 'POST') {
      const userId = request.postDataJSON().userId; control.adds.push(userId); expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
      const person = workspaceMembers.find((member) => member.userId === userId && (member.status === 1 || member.status === 'Active'));
      if (!person || members.some((member) => member.userId === userId && !member.leftAt && !member.removedAt)) {
        await route.fulfill({ status: 400, json: { error: 'Membership change unavailable.' } }); return;
      }
      if (!members.some((member) => member.userId === userId)) {
        members.push({ userId, displayName: person.displayName, role: 'Member', canRead: true, canPost: true, canManageMembers: false, canCreateThread: false, leftAt: null, removedAt: null });
      }
      members = members.map((member) => member.userId === userId ? { ...member, role: 'Member', canManageMembers: false, leftAt: null, removedAt: null } : member);
      await route.fulfill({ json: members.find((member) => member.userId === userId) }); return;
    }
    if (path.startsWith(`/api/conversations/${groupId}/members/`) && method === 'DELETE') {
      const userId = path.split('/').at(-1)!; control.removes.push(userId); expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui');
      members = members.map((member) => member.userId === userId ? { ...member, removedAt: '2026-10-20T00:00:00Z' } : member);
      await route.fulfill({ json: {} }); return;
    }
    if (path === `/api/conversations/${groupId}/leave` && method === 'POST') {
      control.leaves++; expect(request.headers()['x-csrf-token']).toBe('csrf-group-ui'); control.denied = true; await route.fulfill({ json: {} }); return;
    }
    await route.fallback();
  });
  return control;
}
