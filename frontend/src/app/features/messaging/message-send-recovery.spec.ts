import { HttpInterceptorFn, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EMPTY, tap } from 'rxjs';

import { FrontendApiError } from '../../core/api/api-error.model';
import { AuthSessionFacade, COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { authSessionInterceptor } from '../../core/auth/auth-session.interceptor';
import { CsrfTokenService } from '../../core/auth/csrf-token.service';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { MessagingFacade } from './messaging.facade';
import { DraftStorageService } from './draft-storage.service';

const currentUserId = DEFAULT_AUTH_SESSION.currentUser!.userId;

describe('Message send recovery', () => {
  let http: HttpTestingController;
  let facade: MessagingFacade;
  let clearState: (reason: ProtectedStateClearReason) => void;
  let normalizedFailures: FrontendApiError[];

  afterEach(() => {
    http?.verify();
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  async function configure(optimistic = true, normalizeErrors = false): Promise<void> {
    normalizedFailures = [];
    const captureNormalizedFailure: HttpInterceptorFn = (request, next) => next(request).pipe(
      tap({ error: (error: FrontendApiError) => normalizedFailures.push(error) }),
    );
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors(normalizeErrors ? [captureNormalizedFailure, authSessionInterceptor] : [])),
        provideHttpClientTesting(),
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
    if (normalizeErrors) {
      const auth = TestBed.inject(AuthSessionFacade);
      TestBed.inject(CsrfTokenService).ensureToken(auth.csrfCacheKey()).subscribe();
      http.expectOne('/api/security/csrf-token').flush({ token: 'test-csrf', headerName: 'X-CSRF-Token' });
    }
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

  it.each([false, true])('revalidates a legacy POST 400 before restoring the draft and identity with normalization %s', async (normalizeErrors) => {
    await configure(false, normalizeErrors);
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

  const revalidationStages = ['detail', 'messages'] as const;

  function failRevalidation(stage: typeof revalidationStages[number], status: number): void {
    http.expectOne('/api/conversations').flush({ items: [] });
    const detail = http.expectOne('/api/conversations/conversation-a');
    if (stage === 'detail') {
      detail.flush({ message: 'Private diagnostic' }, { status, statusText: 'Rejected' });
      return;
    }
    detail.flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'WorkspaceGeneral',
      title: 'Private title', members: [{ userId: currentUserId, canRead: true, canPost: true }],
    });
    http.expectOne('/api/conversations/conversation-a/messages')
      .flush({ message: 'Private diagnostic' }, { status, statusText: 'Rejected' });
  }

  it.each([400, 403, 404].flatMap((status) => revalidationStages.map((stage) => ({ status, stage }))))(
    'clears persisted draft and retry identity after normalized $status at $stage revalidation',
    async ({ status, stage }) => {
      await configure(true, true);
      const conversation = facade.page().conversation;
      const scope = { tenantId: conversation.tenantId, userId: currentUserId,
        workspaceId: conversation.workspaceId, conversationId: conversation.id };
      const storage = TestBed.inject(DraftStorageService);
      facade.setDraft('Private rejected draft');
      facade.sendDraft();
      const first = http.expectOne('/api/conversations/conversation-a/messages');
      expect(first.request.headers.get('X-CSRF-Token')).toBe('test-csrf');
      first.flush({}, { status: 400, statusText: 'Rejected' });
      failRevalidation(stage, status);
      await Promise.resolve();

      expect(normalizedFailures.at(-1)?.httpStatus).toBe(status);
      expect(normalizedFailures.at(-1)).not.toHaveProperty('status');
      expect.soft(facade.page().status).toBe('permissionDenied');
      expect.soft(storage.readDraft(scope)).toBe('');
      expect(facade.page().messages).toEqual([]);
      expect(facade.page().conversation.id).toBe('');
      expect(facade.page().draft).toBe('');
      expect(JSON.stringify(facade.page())).not.toContain('Private');
      facade.sendDraft();
      http.expectNone('/api/conversations/conversation-a/messages');

      facade.manualRefresh();
      flushConversation();
      expect.soft(facade.page().draft).toBe('');
      expect(facade.page().messages).toEqual([]);
      facade.setDraft('Private rejected draft');
      facade.sendDraft();
      const fresh = http.expectOne('/api/conversations/conversation-a/messages');
      expect.soft(fresh.request.body.clientRequestId).not.toBe(first.request.body.clientRequestId);
      fresh.flush(created('Private rejected draft', fresh.request.body.clientRequestId));
    },
  );

  it.each(revalidationStages)('retains draft and retry identity after a normalized transient %s revalidation failure', async (stage) => {
    await configure(false, true);
    const conversation = facade.page().conversation;
    const scope = { tenantId: conversation.tenantId, userId: currentUserId,
      workspaceId: conversation.workspaceId, conversationId: conversation.id };
    facade.setDraft('Transient interrupted draft');
    facade.sendDraft();
    const first = http.expectOne('/api/conversations/conversation-a/messages');
    first.flush({}, { status: 400, statusText: 'Rejected' });
    failRevalidation(stage, 503);
    await Promise.resolve();

    expect(facade.page().status).toBe('manualRefreshError');
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().draft).toBe('');
    expect(TestBed.inject(DraftStorageService).readDraft(scope)).toBe('Transient interrupted draft');
    facade.manualRefresh();
    flushConversation();
    expect(facade.page().draft).toBe('Transient interrupted draft');
    facade.sendDraft();
    const retry = http.expectOne('/api/conversations/conversation-a/messages');
    expect(retry.request.body.clientRequestId).toBe(first.request.body.clientRequestId);
    retry.flush(created('Transient interrupted draft', retry.request.body.clientRequestId));
  });

  it('classifies normalized detail denial outside rejected-send revalidation consistently', async () => {
    await configure(false, true);
    facade.manualRefresh();
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne('/api/conversations/conversation-a')
      .flush({}, { status: 403, statusText: 'Denied' });
    expect(facade.page().status).toBe('permissionDenied');
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().conversation.id).toBe('');
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
