import { randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { expect, test, type APIRequestContext, type BrowserContext } from '@playwright/test';

type Member = { userId: string; role: number; canRead: boolean; canPost: boolean; canManageMembers: boolean;
  canCreateThread: boolean; leftAt: string | null; removedAt: string | null };
type Message = { id: string; authorUserId: string; body: string };
const prefix = 'COGLATAS_UI07_ROSTER_';

function fixture() {
  const read = (name: string) => process.env[`${prefix}${name}`] ?? '';
  const id = (name: string) => {
    const value = read(name);
    if (!/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/.test(value)) throw new Error('Missing owned roster identity.');
    return value;
  };
  const base = process.env.PLAYWRIGHT_BASE_URL ?? '';
  if (process.env.COGLATAS_REAL_BACKEND_SMOKE !== '1' || process.env.COGLATAS_REAL_BACKEND_UI07_READONLY_ROSTER !== '1' ||
      !/^http:\/\/(?:127\.0\.0\.1|localhost):8080\/?$/.test(base) ||
      !/^[a-f0-9]{40}$/.test(read('SOURCE_SHA')) || read('SOURCE_SHA') !== process.env.TARGET_SHA || read('MEMBER_COUNT') !== '43') {
    throw new Error('Owned real-backend roster execution admission failed.');
  }
  const actors = (['OWNER', 'READER'] as const).map(actor => {
    const email = read(`${actor}_EMAIL`), password = read(`${actor}_PASSWORD`);
    if (!/^ui07-roster-[a-f0-9-]+-\d+@example\.test$/.test(email) || password.length < 32) {
      throw new Error('Missing synthetic roster credential setup.');
    }
    return { id: id(`${actor}_ID`), email, password };
  });
  if (actors[0].id === actors[1].id || actors[0].email === actors[1].email) throw new Error('Roster actors must be distinct.');
  return { workspaceId: id('WORKSPACE_ID'), conversationId: id('CONVERSATION_ID'), owner: actors[0], reader: actors[1] };
}

async function json(request: APIRequestContext, path: string) {
  let response;
  try { response = await request.get(path); }
  catch { throw new Error('Real scoped read transport failed; sensitive diagnostics suppressed.'); }
  expect(response.status(), 'real scoped read status').toBe(200);
  try { return await response.json(); }
  catch { throw new Error('Real scoped read returned invalid JSON; response body suppressed.'); }
}

async function command(request: APIRequestContext, path: string, body: unknown) {
  // Auth/session material never enters evidence, assertion descriptions or traces.
  try {
    const csrfResponse = await request.get('/api/security/csrf-token');
    if (csrfResponse.status() !== 200) throw new Error();
    const csrf = await csrfResponse.json() as { headerName?: string; token?: string };
    if (!csrf.headerName || !csrf.token) throw new Error();
    return await request.post(path, { data: body, headers: { [csrf.headerName]: csrf.token } });
  } catch { throw new Error('Real CSRF command setup or transport failed; sensitive diagnostics suppressed.'); }
}

async function login(context: BrowserContext, actor: { id: string; email: string; password: string }, workspaceId: string) {
  const response = await command(context.request, '/api/auth/login', { email: actor.email, password: actor.password });
  if (response.status() !== 200) throw new Error('Synthetic roster login failed; credential response suppressed.');
  const me = await json(context.request, '/api/auth/me') as { userId: string; workspaces: { id: string }[] };
  expect(me.userId === actor.id, 'real cookie identifies the owned actor').toBe(true);
  expect(me.workspaces.map(workspace => workspace.id), 'only the owned Workspace is admitted').toEqual([workspaceId]);
}

test('real ReadOnly overflowing Group roster preserves keyboard boundaries and server authority', async ({ browser, context, page }, testInfo) => {
  const evidence = {
    sourceSha: /^[a-f0-9]{40}$/.test(process.env.TARGET_SHA ?? '') ? process.env.TARGET_SHA : null,
    browserProject: testInfo.project.name, retry: testInfo.retry, phase: 'ADMISSION', status: 'NOT_VERIFIED',
    memberCount: null as number | null, readOnly: null as boolean | null,
    canPost: null as boolean | null, canManageMembers: null as boolean | null,
    ownerPostStatus: null as number | null, readerPostStatus: null as number | null,
    readerManageStatus: null as number | null, ownerContextClosed: null as boolean | null,
    metrics: null as { height: number; scrollHeight: number; tabIndex: number; focusableChildren: number } | null,
    outer: null as { isAside: boolean; height: number; scrollHeight: number } | null,
    end: null as { top: number; max: number } | null, home: null as number | null,
    viewport: page.viewportSize(),
  };
  let ownerContext: BrowserContext | undefined, primaryFailure = false;
  let pageErrors = 0;
  const failedUiStatuses: number[] = [];
  page.on('pageerror', () => { pageErrors++; });
  page.on('response', response => {
    if (new URL(response.url()).pathname.startsWith('/api/') && response.status() >= 400) failedUiStatuses.push(response.status());
  });
  try {
    const owned = fixture(), path = `/api/conversations/${owned.conversationId}`;
    ownerContext = await browser.newContext({ baseURL: process.env.PLAYWRIGHT_BASE_URL });
    evidence.phase = 'AUTHENTICATION';
    await login(ownerContext, owned.owner, owned.workspaceId);
    await login(context, owned.reader, owned.workspaceId);
    evidence.phase = 'SERVER_AUTHORITY';
    const workspaceMembers = await json(context.request, `/api/workspaces/${owned.workspaceId}/members`) as { userId: string; role: number; status: number }[];
    expect(workspaceMembers.length).toBe(43);
    expect(new Set(workspaceMembers.map(member => member.userId)).size).toBe(43);
    expect(workspaceMembers.every(member => member.status === 1), 'all Workspace admissions are active').toBe(true);
    expect(workspaceMembers.some(member => member.userId === owned.reader.id && member.role === 4)).toBe(true);
    const detail = await json(context.request, path) as { type: string; workspaceId: string; members: Member[] };
    expect(detail.type).toBe('Group'); expect(detail.workspaceId).toBe(owned.workspaceId);
    const members = await json(context.request, `${path}/members`) as Member[];
    evidence.memberCount = members.length;
    const readerMember = members.find(member => member.userId === owned.reader.id);
    evidence.readOnly = readerMember?.role === 2;
    evidence.canPost = typeof readerMember?.canPost === 'boolean' ? readerMember.canPost : null;
    evidence.canManageMembers = typeof readerMember?.canManageMembers === 'boolean' ? readerMember.canManageMembers : null;
    expect(members.length).toBe(43);
    expect(new Set(members.map(member => member.userId)).size).toBe(43);
    expect(members.every(member => member.leftAt === null && member.removedAt === null && member.canRead)).toBe(true);
    expect(members.find(member => member.userId === owned.reader.id)).toMatchObject({ role: 2, canRead: true,
      canPost: false, canManageMembers: false, canCreateThread: false });
    expect(members.find(member => member.userId === owned.owner.id)).toMatchObject({ role: 0, canPost: true, canManageMembers: true });
    const marker = `Synthetic roster positive ${randomUUID()}`;
    const created = await command(ownerContext.request, `${path}/messages`, { body: marker, clientRequestId: randomUUID() });
    evidence.ownerPostStatus = created.status();
    expect(created.status(), 'authorized positive control').toBe(200);
    const message = await created.json() as Message;
    const before = await json(context.request, `${path}/messages`) as { items: Message[]; totalCount: number };
    expect(before.items.some(item => item.id === message.id && item.body === marker && item.authorUserId === owned.owner.id)).toBe(true);
    const denied = await command(context.request, `${path}/messages`, { body: `Synthetic denied ${randomUUID()}`, clientRequestId: randomUUID() });
    evidence.readerPostStatus = denied.status();
    expect(denied.status(), 'visible ReadOnly participant receives posting Forbidden').toBe(403);
    expect((await denied.json()).error === 'You are not allowed to send messages.').toBe(true);
    const deniedManagement = await command(context.request, `${path}/members`, { userId: owned.owner.id });
    evidence.readerManageStatus = deniedManagement.status();
    expect(deniedManagement.status(), 'ordinary ReadOnly management denial').toBe(400);
    expect((await deniedManagement.json()).error === 'You are not allowed to manage this conversation.').toBe(true);
    const after = await json(context.request, `${path}/messages`) as { items: Message[]; totalCount: number };
    expect(after.totalCount).toBe(before.totalCount);
    expect(after.items.map(item => item.id)).toEqual(before.items.map(item => item.id));
    expect((await json(context.request, `${path}/members`) as Member[]).length).toBe(43);

    evidence.phase = 'BROWSER_READONLY_PROJECTION';
    await page.goto(`/app/dm/${owned.conversationId}`);
    const panel = page.getByTestId('group-members-panel'), list = panel.locator('.group-members__list');
    const aside = page.locator('.dm-page__group-details');
    const rows = panel.getByTestId('group-member-row');
    await expect(rows).toHaveCount(43);
    await expect(page.getByTestId('message-draft')).toBeDisabled();
    await expect(panel.getByTestId('group-add-member-open')).toHaveCount(0);
    await expect(panel.getByTestId('group-remove-member')).toHaveCount(0);
    await expect(panel.getByTestId('group-leave')).toBeEnabled();
    await expect(page.getByTestId('confirmed-message').filter({ hasText: marker })).toHaveCount(1);
    const metrics = await list.evaluate(element => ({ height: element.clientHeight, scrollHeight: element.scrollHeight,
      tabIndex: (element as HTMLElement).tabIndex, focusableChildren: element.querySelectorAll('button,input,a,select,textarea,[tabindex]').length }));
    evidence.metrics = metrics;
    expect(metrics.scrollHeight).toBeGreaterThan(metrics.height);
    expect(metrics.tabIndex).toBe(0); expect(metrics.focusableChildren).toBe(0);
    const outer = await aside.evaluate(element => {
      for (let candidate: Element | null = element; candidate; candidate = candidate.parentElement) {
        if (/(auto|scroll)/.test(getComputedStyle(candidate).overflowY) && candidate.scrollHeight > candidate.clientHeight) {
          return { kind: candidate === element ? 'aside' : 'ancestor', height: candidate.clientHeight, scrollHeight: candidate.scrollHeight };
        }
      }
      const documentScroller = document.scrollingElement!;
      return { kind: 'document', height: documentScroller.clientHeight, scrollHeight: documentScroller.scrollHeight };
    });
    evidence.outer = { isAside: outer.kind === 'aside', height: outer.height, scrollHeight: outer.scrollHeight };
    expect(outer.scrollHeight).toBeGreaterThan(outer.height);
    if (testInfo.project.name === 'readonly-roster-desktop') expect(outer.kind).toBe('aside');
    await panel.locator('summary').focus();
    await page.keyboard.press('Tab'); await expect(list).toBeFocused();
    evidence.phase = 'END_BOUNDARY';
    await page.keyboard.press('End');
    const end = await list.evaluate(element => ({ top: element.scrollTop, max: element.scrollHeight - element.clientHeight }));
    evidence.end = end;
    expect(end.max).toBeGreaterThan(0); expect(Math.abs(end.top - end.max), 'End completes without a delayed scroll animation').toBeLessThanOrEqual(1);
    await page.keyboard.press('Tab'); await expect(panel.getByTestId('group-leave')).toBeFocused();
    await expect(rows.last().locator('strong')).toBeInViewport({ ratio: 1 });
    await expect(rows.last().locator('small')).toBeInViewport({ ratio: 1 });
    await page.keyboard.press('Shift+Tab'); await expect(list).toBeFocused();
    evidence.phase = 'HOME_BOUNDARY';
    await page.keyboard.press('Home');
    const home = await list.evaluate(element => element.scrollTop);
    evidence.home = home;
    expect(home, 'Home completes without a delayed scroll animation').toBe(0);
    await page.keyboard.press('Shift+Tab'); await expect(panel.locator('summary')).toBeFocused();
    await expect(rows.first().locator('strong')).toBeInViewport({ ratio: 1 });
    await expect(rows.first().locator('small')).toBeInViewport({ ratio: 1 });
    expect(pageErrors).toBe(0); expect(failedUiStatuses).toEqual([]);
    evidence.phase = 'ASSERTIONS_COMPLETE'; evidence.status = 'PASS';
  } catch (error) { primaryFailure = true; throw error; }
  finally {
    let cleanupFailed = false, attachmentFailed = false;
    if (ownerContext) {
      try { await ownerContext.close(); evidence.ownerContextClosed = true; }
      catch { cleanupFailed = true; evidence.ownerContextClosed = false; evidence.status = 'NOT_VERIFIED'; }
    }
    try {
      const receiptPath = testInfo.outputPath('receipt-source.json');
      await mkdir(dirname(receiptPath), { recursive: true });
      // This auth-free projection must remain readable by the host-side collector
      // when the browser container and Actions runner use different Unix users.
      await writeFile(receiptPath, JSON.stringify(evidence), { flag: 'wx', mode: 0o644 });
      await testInfo.attach('readonly-roster-structural-receipt.json', {
        contentType: 'application/json', path: receiptPath,
      });
    } catch { attachmentFailed = true; }
    if (cleanupFailed || attachmentFailed) {
      testInfo.annotations.push({ type: 'qualification', description: 'Roster cleanup or structural receipt capture failed.' });
      if (!primaryFailure) throw new Error('Roster cleanup or structural receipt capture failed.');
    }
  }
});
