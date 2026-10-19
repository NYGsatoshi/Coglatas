import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { MessagingFacade } from './messaging.facade';

const currentUserId = DEFAULT_AUTH_SESSION.currentUser!.userId;

describe('Message send recovery', () => {
  let http: HttpTestingController;
  let facade: MessagingFacade;
  let clearState: (reason: ProtectedStateClearReason) => void;

  afterEach(() => {
    http?.verify();
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  async function configure(optimistic = true): Promise<void> {
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: FrontendFeatureFlagsService, useValue: {
          optimisticMessagingEnabled: () => optimistic,
          realtimeSignalREnabled: () => false,
        } },
        { provide: RealtimeFacade, useValue: {
          durableEvents$: EMPTY,
          registerProtectedStateClearer: (_owner: string, callback: typeof clearState) => {
            clearState = callback;
            return () => undefined;
          },
        } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('conversation-a', 'channel', 'workspace-a');
    flushConversation();
  }

  function flushConversation(id = 'conversation-a'): void {
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne(`/api/conversations/${id}`).flush({
      id, workspaceId: 'workspace-a', type: 'WorkspaceGeneral', title: 'Authorized channel',
      isLocked: false, isArchived: false,
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }],
      createdAt: '2026-10-19T00:00:00Z',
    });
    http.expectOne(`/api/conversations/${id}/messages`).flush({ items: [] });
  }

  function created(body: string, clientRequestId: string): Record<string, unknown> {
    return {
      id: 'message-confirmed', workspaceId: 'workspace-a', conversationId: 'conversation-a',
      authorUserId: currentUserId, body, clientRequestId, attachments: [],
      createdAt: '2026-10-19T00:00:00Z', isDeleted: false,
    };
  }

  it.each([true, false])('reuses an uncertain send identity with optimistic rendering %s', async (optimistic) => {
    await configure(optimistic);
    facade.setDraft('Persist exactly once');
    facade.sendDraft(['bob', 'alice', 'bob']);
    const first = http.expectOne('/api/conversations/conversation-a/messages');
    const identity = first.request.body.clientRequestId;
    first.flush({ error: 'Private diagnostic' }, { status: 503, statusText: 'Unavailable' });

    expect(facade.page().draft).toBe('Persist exactly once');
    facade.sendDraft(['alice', 'bob']);
    const retry = http.expectOne('/api/conversations/conversation-a/messages');
    expect(retry.request.body).toEqual(first.request.body);
    facade.sendDraft(['alice', 'bob']);
    http.expectNone('/api/conversations/conversation-a/messages');
    expect(facade.page().messages).toHaveLength(optimistic ? 1 : 0);

    retry.flush(created('Persist exactly once', identity));
    expect(facade.page().messages).toHaveLength(1);
    expect(facade.page().draft).toBe('');
    expect(facade.page().sendState.status).toBe('sent');
    expect(JSON.stringify(facade.page())).not.toContain('Private diagnostic');
  });

  it('uses a new identity after the body or canonical mentions change', async () => {
    await configure(false);
    facade.setDraft('Original');
    facade.sendDraft(['alice']);
    const original = http.expectOne('/api/conversations/conversation-a/messages');
    original.flush({}, { status: 500, statusText: 'Unavailable' });
    facade.setDraft('Edited');
    facade.sendDraft(['alice']);
    const edited = http.expectOne('/api/conversations/conversation-a/messages');
    expect(edited.request.body.clientRequestId).not.toBe(original.request.body.clientRequestId);
    edited.flush({}, { status: 500, statusText: 'Unavailable' });
    facade.sendDraft(['bob']);
    const mentions = http.expectOne('/api/conversations/conversation-a/messages');
    expect(mentions.request.body.clientRequestId).not.toBe(edited.request.body.clientRequestId);
    mentions.flush(created('Edited', mentions.request.body.clientRequestId));
  });

  it.each([false, true])('retries a failed row once and preserves a different draft %s', async (changedDraft) => {
    await configure();
    facade.setDraft('Uncertain row');
    facade.sendDraft();
    const first = http.expectOne('/api/conversations/conversation-a/messages');
    first.flush({}, { status: 503, statusText: 'Unavailable' });
    const row = facade.page().messages[0];
    if (changedDraft) facade.setDraft('Next message');
    facade.retryMessage(row.id);
    const retry = http.expectOne('/api/conversations/conversation-a/messages');
    facade.retryMessage(row.id);
    http.expectNone('/api/conversations/conversation-a/messages');
    expect(retry.request.body.clientRequestId).toBe(first.request.body.clientRequestId);
    retry.flush(created('Uncertain row', first.request.body.clientRequestId));
    expect(facade.page().draft).toBe(changedDraft ? 'Next message' : '');
    expect(facade.page().inlineError).toBeUndefined();
    expect(facade.page().messages).toHaveLength(1);
  });

  it('keeps an interrupted send identity through same-conversation refresh', async () => {
    await configure(false);
    facade.setDraft('Uncertain before refresh');
    facade.sendDraft();
    const interrupted = http.expectOne('/api/conversations/conversation-a/messages');
    facade.manualRefresh();
    expect(interrupted.cancelled).toBe(true);
    flushConversation();
    facade.sendDraft();
    const retry = http.expectOne('/api/conversations/conversation-a/messages');
    expect(retry.request.body.clientRequestId).toBe(interrupted.request.body.clientRequestId);
    retry.flush(created('Uncertain before refresh', retry.request.body.clientRequestId));
  });

  it.each([401, 403, 404])('clears protected content and retry state after POST %s', async (status) => {
    await configure();
    facade.setDraft('Private draft');
    facade.sendDraft();
    http.expectOne('/api/conversations/conversation-a/messages')
      .flush({ error: 'Private diagnostic' }, { status, statusText: 'Denied' });
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().draft).toBe('');
    expect(facade.page().conversation.id).toBe('');
    expect(facade.page().status).toBe('permissionDenied');
    expect(JSON.stringify(facade.page())).not.toContain('Private');
    facade.sendDraft();
    http.expectNone('/api/conversations/conversation-a/messages');
  });

  it('discards retry identity at an authorization boundary', async () => {
    await configure(false);
    facade.setDraft('Same visible text');
    facade.sendDraft();
    const first = http.expectOne('/api/conversations/conversation-a/messages');
    first.flush({}, { status: 503, statusText: 'Unavailable' });
    clearState('authorization');
    facade.manualRefresh();
    flushConversation();
    expect(facade.page().draft).toBe('');
    facade.setDraft('Same visible text');
    facade.sendDraft();
    const fresh = http.expectOne('/api/conversations/conversation-a/messages');
    expect(fresh.request.body.clientRequestId).not.toBe(first.request.body.clientRequestId);
    fresh.flush(created('Same visible text', fresh.request.body.clientRequestId));
  });

  it('clears the captured draft scope when authorization changes during legacy-400 revalidation', async () => {
    await configure(false);
    facade.setDraft('Revoked draft during recovery');
    facade.sendDraft();
    const first = http.expectOne('/api/conversations/conversation-a/messages');
    first.flush({}, { status: 400, statusText: 'Rejected' });
    const pendingList = http.expectOne('/api/conversations');
    const pendingDetail = http.expectOne('/api/conversations/conversation-a');
    clearState('authorization');
    expect(pendingList.cancelled).toBe(true);
    expect(pendingDetail.cancelled).toBe(true);
    facade.manualRefresh();
    flushConversation();
    await Promise.resolve();
    expect(facade.page().draft).toBe('');
    facade.setDraft('Revoked draft during recovery');
    facade.sendDraft();
    const fresh = http.expectOne('/api/conversations/conversation-a/messages');
    expect(fresh.request.body.clientRequestId).not.toBe(first.request.body.clientRequestId);
    fresh.flush(created('Revoked draft during recovery', fresh.request.body.clientRequestId));
  });

  it('revalidates a legacy POST 400 before restoring the draft and reusing its identity', async () => {
    await configure(false);
    facade.setDraft('Rejected draft');
    facade.sendDraft();
    const first = http.expectOne('/api/conversations/conversation-a/messages');
    first.flush({}, { status: 400, statusText: 'Rejected' });
    expect(facade.page().status).toBe('loading');
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().draft).toBe('');
    flushConversation();
    await vi.waitFor(() => expect(facade.page().sendState.status).toBe('failed'));
    expect(facade.page().draft).toBe('Rejected draft');
    expect(facade.page().sendState.status).toBe('failed');
    facade.sendDraft();
    const retry = http.expectOne('/api/conversations/conversation-a/messages');
    expect(retry.request.body.clientRequestId).toBe(first.request.body.clientRequestId);
    retry.flush(created('Rejected draft', retry.request.body.clientRequestId));
  });

  it.each([false, true])('keeps content hidden when a legacy POST 400 revalidation is denied after detail %s', async (detailSucceeded) => {
    await configure();
    facade.setDraft('Revoked private draft');
    facade.sendDraft();
    http.expectOne('/api/conversations/conversation-a/messages')
      .flush({}, { status: 400, statusText: 'Rejected' });
    http.expectOne('/api/conversations').flush({ items: [] });
    const detail = http.expectOne('/api/conversations/conversation-a');
    if (detailSucceeded) {
      detail.flush({
        id: 'conversation-a', workspaceId: 'workspace-a', type: 'WorkspaceGeneral',
        title: 'Private title', members: [{ userId: currentUserId, canRead: true, canPost: true }],
      });
      http.expectOne('/api/conversations/conversation-a/messages')
        .flush({}, { status: 403, statusText: 'Denied' });
    } else {
      detail.flush({}, { status: 404, statusText: 'Hidden' });
    }
    await Promise.resolve();
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().draft).toBe('');
    expect(facade.page().conversation.id).toBe('');
    expect(facade.page().status).toBe('permissionDenied');
    facade.sendDraft();
    http.expectNone('/api/conversations/conversation-a/messages');
    facade.manualRefresh();
    flushConversation();
    expect(facade.page().draft).toBe('');
  });

  it('cancels legacy-400 revalidation when the selected conversation changes', async () => {
    await configure(false);
    facade.setDraft('Conversation A private draft');
    facade.sendDraft();
    const post = http.expectOne('/api/conversations/conversation-a/messages');
    post.flush({}, { status: 400, statusText: 'Rejected' });
    const oldList = http.expectOne('/api/conversations');
    const oldDetail = http.expectOne('/api/conversations/conversation-a');
    facade.loadConversation('conversation-b', 'channel', 'workspace-a');
    expect(oldList.cancelled).toBe(true);
    expect(oldDetail.cancelled).toBe(true);
    flushConversation('conversation-b');
    await Promise.resolve();
    expect(facade.page().conversation.id).toBe('conversation-b');
    expect(facade.page().draft).toBe('');
    expect(JSON.stringify(facade.page())).not.toContain('Conversation A private draft');
    facade.setDraft('Conversation B draft');
    facade.sendDraft();
    const fresh = http.expectOne('/api/conversations/conversation-b/messages');
    expect(fresh.request.body.clientRequestId).not.toBe(post.request.body.clientRequestId);
    fresh.flush({ ...created('Conversation B draft', fresh.request.body.clientRequestId), conversationId: 'conversation-b' });
  });
});
