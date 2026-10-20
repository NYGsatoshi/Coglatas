import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { MessagingFacade } from './messaging.facade';
import { MessageDto } from './messaging.api';

const actor = DEFAULT_AUTH_SESSION.currentUser!.userId;
const older = '11111111-1111-4111-8111-111111111111';
const newer = '22222222-2222-4222-8222-222222222222';
let clear: (reason: 'authorization' | 'workspace') => void;

async function setup() {
  await TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
    { provide: RealtimeFacade, useValue: { durableEvents$: new Subject(),
      registerProtectedStateClearer: (_key: string, callback: typeof clear) => { clear = callback; return () => {}; },
      registerCatchUp: () => () => {}, registerSubscription: () => () => {} } }
  ] }).compileComponents();
  return { http: TestBed.inject(HttpTestingController), facade: TestBed.inject(MessagingFacade) };
}
function inbox(unread = 2) {
  return { items: [{ id: 'conversation-a', type: 'Group', workspaceId: 'workspace-a', unreadCount: unread }],
    view: 'All', counts: { all: 1, unread: unread > 0 ? 1 : 0, mentions: 0, later: 0 } };
}
function load(http: HttpTestingController, facade: MessagingFacade, messages: MessageDto[] = [
  { id: older, body: 'Older loaded', createdAt: '2026-10-20T00:00:00.0000001Z' },
  { id: newer, body: 'Newest loaded', createdAt: '2026-10-20T00:00:00.0000009Z' }
], type = 'Group', canRead = true, canPost = false) {
  facade.loadConversation('conversation-a', 'dm');
  http.expectOne('/api/conversations').flush(inbox());
  http.expectOne('/api/conversations/conversation-a').flush({ id: 'conversation-a', workspaceId: 'workspace-a', type,
    members: [{ userId: actor, displayName: 'Actor', role: canPost ? 'Member' : 'ReadOnly', canRead, canPost }] });
  http.expectOne('/api/conversations/conversation-a/messages').flush({ items: messages });
}

describe('Explicit current-conversation read cursor', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses exact server fractional time, ignores replies/deleted/optimistic rows, and refreshes server counts only after confirmation', async () => {
    const { http, facade } = await setup();
    load(http, facade, [
      { id: older, createdAt: '2026-10-20T00:00:00.0000001Z' },
      { id: newer, createdAt: '2026-10-20T00:00:00.0000009Z' },
      { id: '33333333-3333-4333-8333-333333333333', createdAt: '2026-10-21T00:00:00Z', threadRootMessageId: older },
      { id: '44444444-4444-4444-8444-444444444444', createdAt: '2026-10-22T00:00:00Z', isDeleted: true }
    ]);
    facade.markLoadedMessagesRead(); facade.markLoadedMessagesRead();
    const read = http.expectOne('/api/conversations/conversation-a/read');
    expect(read.request.body).toEqual({ lastReadMessageId: newer });
    expect(facade.readAction().pending).toBe(true);
    expect(facade.page().conversations[0].unreadCount).toBe(2);
    read.flush({});
    expect(facade.page().conversations[0].unreadCount).toBe(2);
    http.expectOne('/api/conversations').flush(inbox(1));
    await Promise.resolve();
    expect(facade.page().conversations[0].unreadCount).toBe(1);
    expect(facade.readAction().feedbackKey).toBe('messaging.read.loaded');
    expect(facade.page().messages).toHaveLength(4);
  });

  it.each(['DirectMessage', 'Group', 'WorkspaceChannel'])('allows a current readable %s participant without posting authority', async (type) => {
    const { http, facade } = await setup(); load(http, facade, undefined, type);
    expect(facade.canMarkRead()).toBe(true);
    facade.markLoadedMessagesRead(); http.expectOne('/api/conversations/conversation-a/read').flush({});
    http.expectOne('/api/conversations').flush(inbox(0)); await Promise.resolve();
    expect(facade.readAction().pending).toBe(false);
  });

  it('does not guess a cursor from empty, denied or malformed messages', async () => {
    const { http, facade } = await setup();
    load(http, facade, [{ body: 'No server ID', createdAt: '2026-10-20T00:00:00Z' }, { id: newer, createdAt: 'invalid' }]);
    expect(facade.canMarkRead()).toBe(false); facade.markLoadedMessagesRead();
    http.expectNone('/api/conversations/conversation-a/read');
    facade.manualRefresh();
    http.expectOne('/api/conversations').flush(inbox());
    http.expectOne('/api/conversations/conversation-a').flush({ id: 'conversation-a', type: 'DirectMessage', members: [] });
    http.expectOne('/api/conversations/conversation-a/messages').flush({ items: [{ id: newer, createdAt: '2026-10-20T00:00:00Z' }] });
    expect(facade.canMarkRead()).toBe(false);
  });

  it.each(['success', 'error'])('ignores a late pre-command list %s after the confirmed refresh', async (outcome) => {
    const { http, facade } = await setup();
    facade.loadConversation('conversation-a', 'dm');
    const initial = http.expectOne('/api/conversations');
    http.expectOne('/api/conversations/conversation-a').flush({ id: 'conversation-a', type: 'DirectMessage', members: [{ userId: actor, canRead: true }] });
    http.expectOne('/api/conversations/conversation-a/messages').flush({ items: [
      { id: newer, createdAt: '2026-10-20T00:00:00+00:00' },
      { id: older, createdAt: '2026-10-19T23:59:59.9999999Z' }
    ] });
    facade.markLoadedMessagesRead();
    const read = http.expectOne('/api/conversations/conversation-a/read');
    expect(read.request.body).toEqual({ lastReadMessageId: newer }); read.flush({});
    http.expectOne('/api/conversations').flush(inbox(0)); await Promise.resolve();
    if (outcome === 'success') { initial.flush(inbox(99)); }
    else { initial.flush({}, { status: 403, statusText: 'Old response' }); }
    expect(facade.page().conversations[0].unreadCount).toBe(0);
    expect(facade.inbox().counts.unread).toBe(0);
  });

  it.each([401, 403])('cancels pending initial detail and refuses resurrection after an early list denial %s', async (status) => {
    const { http, facade } = await setup();
    facade.loadConversation('conversation-a', 'dm');
    const detail = http.expectOne('/api/conversations/conversation-a');
    http.expectOne('/api/conversations').flush({}, { status, statusText: 'Denied' });
    expect(detail.cancelled).toBe(true);
    expect(facade.page().status).toBe('permissionDenied');
    expect(facade.page().messages).toEqual([]);
    expect(facade.canMarkRead()).toBe(false);
    expect(facade.readAction()).toEqual({ pending: false });
    http.expectNone('/api/conversations/conversation-a/messages');
  });

  it.each([401, 403])('cancels pending messages after initial detail but before an early list denial %s', async (status) => {
    const { http, facade } = await setup();
    facade.loadConversation('conversation-a', 'dm');
    http.expectOne('/api/conversations/conversation-a').flush({ id: 'conversation-a', type: 'DirectMessage', members: [{ userId: actor, canRead: true }] });
    const messages = http.expectOne('/api/conversations/conversation-a/messages');
    http.expectOne('/api/conversations').flush({}, { status, statusText: 'Denied' });
    expect(messages.cancelled).toBe(true);
    expect(facade.page().status).toBe('permissionDenied');
    expect(facade.page().conversation.id).toBe('');
    expect(facade.page().messages).toEqual([]);
    expect(facade.readAction()).toEqual({ pending: false });
  });

  it('refuses an empty cursor and never marks an optimistic pending send read', async () => {
    const { http, facade } = await setup(); load(http, facade, [], 'DirectMessage', true, true);
    expect(facade.canMarkRead()).toBe(false); facade.markLoadedMessagesRead();
    http.expectNone('/api/conversations/conversation-a/read');
    facade.setDraft('Unconfirmed pending send'); facade.sendDraft();
    const send = http.expectOne('/api/conversations/conversation-a/messages');
    expect(facade.page().messages.some((message) => message.deliveryState === 'sending')).toBe(true);
    expect(facade.canMarkRead()).toBe(false); facade.markLoadedMessagesRead();
    http.expectNone('/api/conversations/conversation-a/read');
    send.flush({ id: newer, conversationId: 'conversation-a', authorUserId: actor, body: 'Unconfirmed pending send', clientRequestId: send.request.body.clientRequestId, createdAt: '2026-10-20T00:00:00Z' });
    expect(facade.canMarkRead()).toBe(true);
  });

  it('discards the protected aggregate if the confirmed-command inbox refresh denies access', async () => {
    const { http, facade } = await setup(); load(http, facade);
    facade.markLoadedMessagesRead(); http.expectOne('/api/conversations/conversation-a/read').flush({});
    http.expectOne('/api/conversations').flush({}, { status: 403, statusText: 'Denied' });
    await Promise.resolve();
    expect(facade.page().status).toBe('permissionDenied');
    expect(facade.page().messages).toEqual([]);
    expect(facade.readAction()).toEqual({ pending: false });
  });

  it('keeps server counts unchanged and shows safe unconfirmed feedback after an unknown outcome', async () => {
    const { http, facade } = await setup(); load(http, facade);
    facade.markLoadedMessagesRead();
    http.expectOne('/api/conversations/conversation-a/read').flush({ error: 'Sensitive raw error' }, { status: 503, statusText: 'Unavailable' });
    expect(facade.readAction()).toEqual({ pending: false, failed: true, feedbackKey: 'messaging.read.failed' });
    expect(facade.page().conversations[0].unreadCount).toBe(2);
    http.expectNone('/api/conversations');
  });

  it.each([400, 401, 403, 404])('clears protected state on a masked or explicit read denial %s', async (status) => {
    const { http, facade } = await setup(); load(http, facade);
    facade.markLoadedMessagesRead();
    http.expectOne('/api/conversations/conversation-a/read').flush({}, { status, statusText: 'Denied' });
    expect(facade.page().status).toBe('permissionDenied');
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().conversation.groupMembers ?? []).toEqual([]);
    expect(facade.canMarkRead()).toBe(false);
    expect(facade.readAction()).toEqual({ pending: false });
  });

  it.each(['authorization', 'workspace'] as const)('cancels a pending command and clears feedback at the %s boundary', async (reason) => {
    const { http, facade } = await setup(); load(http, facade);
    facade.markLoadedMessagesRead(); const read = http.expectOne('/api/conversations/conversation-a/read');
    clear(reason);
    expect(read.cancelled).toBe(true); expect(facade.readAction()).toEqual({ pending: false });
    expect(facade.page().messages).toEqual([]); expect(facade.canMarkRead()).toBe(false);
  });
});
