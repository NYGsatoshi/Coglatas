import { I18nService } from '../../core/i18n/i18n.service';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';

import {
  COGLATAS_AUTH_SESSION_MOCK,
  AuthSessionFacade,
  DEFAULT_AUTH_SESSION
} from '../../core/auth/auth-session.facade';
import { authSessionInterceptor } from '../../core/auth/auth-session.interceptor';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';
import { WorkspacePreferenceService } from '../../core/workspace/workspace-preference.service';
import { WorkspacesFacade } from '../workspaces/workspaces.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { ChannelMessagingPageComponent } from './channel-messaging-page/channel-messaging-page.component';
import { DmPageComponent } from './dm-page/dm-page.component';
import { MessageThreadDto } from './messaging.api';
import { COGLATAS_MESSAGING_PAGE_MOCK, MessagingFacade } from './messaging.facade';
import {
  MessagingMessageViewModel,
  MessagingPageViewModel,
  MessagingThreadViewModel
} from './messaging.types';
import { ThreadPreviewComponent } from './thread-preview/thread-preview.component';
import { ThreadWorkspaceComponent } from './thread-workspace/thread-workspace.component';

const currentUserId = DEFAULT_AUTH_SESSION.currentUser?.userId ?? 'mock-user-a';

// These existing contract assertions intentionally select the English UI.
beforeEach(() => { localStorage.setItem('coglatas.locale', 'en'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('Issue 362 message thread contract', () => {
  afterEach(() => {
    sessionStorage.clear();
    document.getElementById('thread-trigger-root-a')?.remove();
    TestBed.inject(HttpTestingController, null)?.verify();
    TestBed.resetTestingModule();
  });

  it('keeps the main composer separate, posts to the exact thread endpoint, and reuses the idempotency key on retry', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);

    facade.setDraft('Main timeline draft');
    facade.setThreadDraft('Thread-only draft');
    facade.sendThreadDraft();

    const firstPost = httpMock.expectOne('/api/messages/root-a/thread/messages');
    expect(firstPost.request.method).toBe('POST');
    expect(firstPost.request.withCredentials).toBe(true);
    expect(firstPost.request.body).toMatchObject({
      body: 'Thread-only draft',
      mentionedUserIds: []
    });
    const clientRequestId = firstPost.request.body.clientRequestId as string;
    expect(clientRequestId).toMatch(/^[0-9a-f-]{36}$/i);
    firstPost.flush({}, { status: 503, statusText: 'Unavailable' });

    expect(facade.thread()).toMatchObject({
      status: 'ready',
      draft: 'Thread-only draft',
      sending: false,
      pendingClientRequestId: clientRequestId
    });
    facade.sendThreadDraft();
    const retryPost = httpMock.expectOne('/api/messages/root-a/thread/messages');
    expect(retryPost.request.body.clientRequestId).toBe(clientRequestId);
    retryPost.flush({
      message: threadReply('reply-b', 'Thread-only draft', clientRequestId),
      summary: threadSummary(2, ['Mock User B', 'Mock User A'])
    });

    expect(facade.page().draft).toBe('Main timeline draft');
    expect(facade.page().messages.map((message) => message.id)).toEqual(['root-a']);
    expect(facade.page().messages[0].thread?.replyCount).toBe(2);
    expect(facade.thread()).toMatchObject({
      status: 'ready',
      draft: '',
      sending: false,
      pendingClientRequestId: undefined,
      summary: { replyCount: 2 }
    });
    expect(facade.thread().replies.map((reply) => reply.id)).toEqual(['reply-a', 'reply-b']);
  });

  it('loads and preserves an exact saved-reply anchor outside the latest thread window', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);

    facade.openThread('root-a', 'thread-trigger-root-a', 'reply-oldest');
    const initial = httpMock.expectOne((request) =>
      request.url === '/api/messages/root-a/thread' &&
      request.params.get('anchorReplyMessageId') === 'reply-oldest'
    );
    expect(initial.request.method).toBe('GET');
    expect(initial.request.withCredentials).toBe(true);
    initial.flush(threadDto({
      replies: [threadReply('reply-oldest'), threadReply('reply-newest')],
      summary: threadSummary(101, ['Mock User B']),
      hasMore: true
    }));

    expect(facade.thread()).toMatchObject({
      status: 'ready',
      hasMore: true,
      maximumReplies: 100,
      anchorReplyMessageId: 'reply-oldest',
      summary: { replyCount: 101 }
    });
    expect(facade.thread().replies.map((reply) => reply.id)).toContain('reply-oldest');

    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      replyCount: 102,
      requiresRefetch: true
    }));
    const refresh = httpMock.expectOne((request) =>
      request.url === '/api/messages/root-a/thread' &&
      request.params.get('anchorReplyMessageId') === 'reply-oldest'
    );
    refresh.flush(threadDto({
      replies: [threadReply('reply-oldest'), threadReply('reply-latest')],
      summary: threadSummary(102, ['Mock User B']),
      hasMore: true
    }));

    expect(facade.thread().replies.map((reply) => reply.id)).toContain('reply-oldest');
  });

  it('fails closed when an anchored thread response omits the exact non-deleted reply', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);

    facade.openThread('root-a', 'thread-trigger-root-a', 'reply-oldest');
    httpMock.expectOne((request) =>
      request.url === '/api/messages/root-a/thread' &&
      request.params.get('anchorReplyMessageId') === 'reply-oldest'
    ).flush(threadDto({ replies: [threadReply('reply-newest')] }));

    expect(facade.thread()).toMatchObject({
      status: 'error',
      rootMessageId: 'root-a',
      replies: []
    });
    expect(facade.thread().anchorReplyMessageId).toBeUndefined();

    facade.openThread('root-a', 'thread-trigger-root-a');
    const ordinary = httpMock.expectOne((request) =>
      request.url === '/api/messages/root-a/thread' &&
      !request.params.has('anchorReplyMessageId')
    );
    ordinary.flush(threadDto());
    expect(facade.thread()).toMatchObject({ status: 'ready', rootMessageId: 'root-a' });
  });

  it('retains the protected projection, oversized draft, and retry key after a POST 400 revalidates successfully', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    const oversizedDraft = 'x'.repeat(12_001);
    facade.setThreadDraft(oversizedDraft);
    facade.sendThreadDraft();

    const rejectedPost = httpMock.expectOne('/api/messages/root-a/thread/messages');
    const clientRequestId = rejectedPost.request.body.clientRequestId as string;
    rejectedPost.flush({}, { status: 400, statusText: 'Bad Request' });

    expect(facade.thread()).toMatchObject({
      status: 'ready',
      rootMessage: { id: 'root-a', body: 'Pinned parent body' },
      draft: oversizedDraft,
      sending: false,
      pendingClientRequestId: clientRequestId
    });
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    expect(facade.thread()).toMatchObject({
      status: 'ready',
      rootMessage: { id: 'root-a', body: 'Pinned parent body' },
      draft: oversizedDraft,
      pendingClientRequestId: clientRequestId
    });

    facade.sendThreadDraft();
    const retryPost = httpMock.expectOne('/api/messages/root-a/thread/messages');
    expect(retryPost.request.body.clientRequestId).toBe(clientRequestId);
    retryPost.flush({}, { status: 503, statusText: 'Unavailable' });
  });

  it('clears all protected thread state when POST 400 revalidation is denied', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Rejected reply');
    facade.sendThreadDraft();

    httpMock.expectOne('/api/messages/root-a/thread/messages').flush(
      {},
      { status: 400, statusText: 'Bad Request' }
    );
    httpMock.expectOne('/api/messages/root-a/thread').flush(
      {},
      { status: 403, statusText: 'Forbidden' }
    );

    const deniedThread = facade.thread();
    expect(deniedThread).toMatchObject({
      status: 'permissionDenied',
      replies: [],
      draft: ''
    });
    expect(deniedThread.rootMessage).toBeUndefined();
    expect(deniedThread.summary).toBeUndefined();
    expect(deniedThread.pendingClientRequestId).toBeUndefined();
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().conversation.mentionCandidates ?? []).toEqual([]);
  });

  it('does not let a pre-retry 400 revalidation overwrite an accepted same-key retry', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Retry this reply');
    facade.sendThreadDraft();

    const rejectedPost = httpMock.expectOne('/api/messages/root-a/thread/messages');
    const clientRequestId = rejectedPost.request.body.clientRequestId as string;
    rejectedPost.flush({}, { status: 400, statusText: 'Bad Request' });
    const staleRevalidation = httpMock.expectOne('/api/messages/root-a/thread');

    facade.sendThreadDraft();
    const retryPost = httpMock.expectOne('/api/messages/root-a/thread/messages');
    expect(retryPost.request.body.clientRequestId).toBe(clientRequestId);
    retryPost.flush({
      message: threadReply('reply-retry', 'Retry this reply', clientRequestId),
      summary: threadSummary(2, ['Mock User B', 'Mock User A'])
    });
    staleRevalidation.flush(threadDto());

    expect(facade.thread()).toMatchObject({
      draft: '',
      pendingClientRequestId: undefined,
      summary: { replyCount: 2 }
    });
    expect(facade.thread().replies.map((reply) => reply.id)).toEqual(['reply-a', 'reply-retry']);

    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      requiresRefetch: true
    }));
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto({
      replies: [threadReply('reply-a'), threadReply('reply-retry'), threadReply('reply-later')],
      summary: threadSummary(3, ['Mock User B', 'Mock User A'])
    }));
    expect(facade.thread().summary?.replyCount).toBe(3);
  });

  it('keeps reply events out of the main timeline and reconciles names through ordered authorized refetches', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    openThread(httpMock, facade);

    events.next(realtimeEvent('Messaging.MessageCreated.v1', {
      conversationId: 'conversation-a',
      message: {
        id: 'reply-realtime',
        conversationId: 'conversation-a',
        threadRootMessageId: 'root-a',
        body: 'Realtime reply body',
        createdAt: '2026-08-27T03:00:00Z',
        sender: { userId: 'user-c', displayName: 'New Participant' }
      }
    }));

    expect(facade.page().messages.map((message) => message.id)).toEqual(['root-a']);
    const replyRefresh = httpMock.expectOne('/api/messages/root-a/thread');
    replyRefresh.flush(threadDto({
      replies: [threadReply('reply-a'), threadReply('reply-realtime', 'Realtime reply body')],
      summary: threadSummary(2, ['Mock User B', 'New Participant'])
    }));
    expect(facade.page().messages[0].thread?.participantDisplayNames).toEqual([
      'Mock User B',
      'New Participant'
    ]);

    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      replyCount: 3,
      requiresRefetch: true
    }));
    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      replyCount: 4,
      requiresRefetch: true
    }));
    const orderedRefreshes = httpMock.match('/api/messages/root-a/thread');
    expect(orderedRefreshes).toHaveLength(2);
    orderedRefreshes[1].flush(threadDto({
      replies: [
        threadReply('reply-a'),
        threadReply('reply-realtime'),
        threadReply('reply-newer-a'),
        threadReply('reply-newer-b')
      ],
      summary: threadSummary(4, ['Newest Participant'])
    }));
    orderedRefreshes[0].flush(threadDto({
      replies: [threadReply('reply-a'), threadReply('reply-realtime'), threadReply('reply-stale')],
      summary: threadSummary(3, ['Stale Participant'])
    }));

    expect(facade.page().messages[0].thread).toMatchObject({
      replyCount: 4,
      participantDisplayNames: ['Newest Participant']
    });
    expect(facade.thread().summary).toMatchObject({
      replyCount: 4,
      participantDisplayNames: ['Newest Participant']
    });
  });

  it('does not let an older initial load replace a newer realtime reconciliation', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    facade.openThread('root-a', 'thread-trigger-root-a');
    const initialLoad = httpMock.expectOne('/api/messages/root-a/thread');

    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      replyCount: 2,
      requiresRefetch: true
    }));
    const realtimeRefresh = httpMock.expectOne('/api/messages/root-a/thread');
    realtimeRefresh.flush(threadDto({
      replies: [threadReply('reply-a'), threadReply('reply-newer')],
      summary: threadSummary(2, ['Newest Participant'])
    }));
    initialLoad.flush(threadDto({
      replies: [threadReply('reply-stale')],
      summary: threadSummary(1, ['Stale Participant'])
    }));

    expect(facade.thread().summary).toMatchObject({
      replyCount: 2,
      participantDisplayNames: ['Newest Participant']
    });
    expect(facade.thread().replies.map((reply) => reply.id)).toEqual(['reply-a', 'reply-newer']);
  });

  it('keeps an authoritative local-delete tombstone terminal across delayed update events', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade, {
      authorUserId: currentUserId,
      authorDisplayName: 'Mock User A',
      body: 'Local root secret',
      clientRequestId: 'delete-root-key'
    });

    facade.requestMessageDelete('root-a');
    facade.confirmMessageDelete('root-a');
    const deleteRequest = httpMock.expectOne('/api/messages/root-a');
    expect(deleteRequest.request.method).toBe('DELETE');
    deleteRequest.flush({ status: 'OK' });

    expect(facade.page().messages[0]).toMatchObject({
      id: 'root-a',
      body: '',
      isDeleted: true,
      thread: { replyCount: 1 }
    });
    expect(JSON.stringify(facade.page().messages[0])).not.toContain('Local root secret');
    for (const [id, clientRequestId] of [
      ['root-a', undefined],
      ['late-created-same-key', 'delete-root-key']
    ] as const) {
      events.next(realtimeEvent('Messaging.MessageCreated.v1', {
        conversationId: 'conversation-a',
        message: {
          id,
          conversationId: 'conversation-a',
          clientRequestId,
          body: `Late create secret ${id}`,
          createdAt: '2026-08-27T03:30:00Z',
          version: 2,
          sender: { userId: currentUserId, displayName: 'Mock User A' }
        }
      }));
    }
    expect(facade.page().messages).toHaveLength(1);
    expect(facade.page().messages[0]).toMatchObject({ id: 'root-a', body: '', isDeleted: true });
    expect(JSON.stringify(facade.page().messages[0])).not.toContain('Late create secret');
    events.next(realtimeEvent('Messaging.MessageCreated.v1', {
      conversationId: 'conversation-a',
      message: {
        id: 'other-author-same-key',
        conversationId: 'conversation-a',
        clientRequestId: 'delete-root-key',
        body: 'Another author may reuse this key',
        createdAt: '2026-08-27T03:31:00Z',
        version: 1,
        sender: { userId: 'mock-user-b', displayName: 'Mock User B' }
      }
    }));
    expect(facade.page().messages.map((message) => message.id)).toEqual([
      'root-a',
      'other-author-same-key'
    ]);
    const authoritativeRoot = deletedRootMessage({
      authorUserId: currentUserId,
      authorDisplayName: 'Mock User A',
      version: 3
    });
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto({ rootMessage: authoritativeRoot }));

    expect(facade.page().messages[0]).toMatchObject({ body: '', isDeleted: true, version: 3 });
    for (const delayedVersion of [2, 4]) {
      events.next(realtimeEvent('Messaging.MessageUpdated.v1', {
        conversationId: 'conversation-a',
        messageId: 'root-a',
        messageVersion: delayedVersion,
        body: `Delayed restored secret v${delayedVersion}`,
        updatedAt: '2026-08-27T04:00:00Z'
      }));
    }
    expect(facade.page().messages[0]).toMatchObject({ body: '', isDeleted: true, version: 3 });
    expect(JSON.stringify(facade.page().messages[0])).not.toContain('Delayed restored secret');

    facade.openThread('root-a', 'thread-trigger-root-a');
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto({ rootMessage: authoritativeRoot }));
    expect(facade.thread()).toMatchObject({
      status: 'ready',
      rootMessage: { id: 'root-a', body: '', isDeleted: true, version: 3 },
      summary: { replyCount: 1 }
    });
  });

  it('retains and authoritatively reconciles a realtime tombstone when deletion overtakes its thread summary', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade, {}, 0);

    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      requiresRefetch: true
    }));
    const overtakenSummary = httpMock.expectOne('/api/messages/root-a/thread');
    events.next(realtimeEvent('Messaging.MessageDeleted.v1', {
      conversationId: 'conversation-a',
      messageId: 'root-a',
      messageVersion: 2
    }));
    const deletionRevalidation = httpMock.expectOne('/api/messages/root-a/thread');

    expect(facade.page().messages[0]).toMatchObject({ body: '', isDeleted: true });
    overtakenSummary.flush(threadDto({ summary: threadSummary(1, ['Stale participant']) }));
    deletionRevalidation.flush(threadDto({ rootMessage: deletedRootMessage() }));

    expect(facade.page().messages[0]).toMatchObject({
      id: 'root-a',
      body: '',
      isDeleted: true,
      version: 2,
      thread: { replyCount: 1, participantDisplayNames: ['Mock User B'] }
    });
    expect(JSON.stringify(facade.page().messages[0])).not.toContain('Pinned parent body');
  });

  it('does not treat a transient deletion revalidation failure as proof of zero replies', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade, {}, 0);

    events.next(realtimeEvent('Messaging.MessageDeleted.v1', {
      conversationId: 'conversation-a',
      messageId: 'root-a',
      messageVersion: 2
    }));
    httpMock.expectOne('/api/messages/root-a/thread').flush(
      {},
      { status: 503, statusText: 'Unavailable' }
    );
    expect(facade.page()).toMatchObject({
      realtimeDegraded: true,
      messages: [{ id: 'root-a', body: '', isDeleted: true }]
    });

    events.next(realtimeEvent('Messaging.MessageCreated.v1', {
      conversationId: 'conversation-a',
      message: {
        id: 'root-a',
        conversationId: 'conversation-a',
        body: 'Late create during degraded refresh',
        createdAt: '2026-08-27T03:30:00Z',
        version: 3,
        sender: { userId: 'mock-user-b', displayName: 'Mock User B' }
      }
    }));
    events.next(realtimeEvent('Messaging.MessageUpdated.v1', {
      conversationId: 'conversation-a',
      messageId: 'root-a',
      messageVersion: 99,
      body: 'Late update during degraded refresh',
      updatedAt: '2026-08-27T03:31:00Z'
    }));
    expect(facade.page().messages[0]).toMatchObject({ id: 'root-a', body: '', isDeleted: true, version: 2 });
    expect(JSON.stringify(facade.page().messages[0])).not.toContain('during degraded refresh');

    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      requiresRefetch: true
    }));
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto({
      rootMessage: deletedRootMessage(),
      replies: [],
      summary: threadSummary(0, [])
    }));
    expect(facade.page().messages).toEqual([]);
  });

  it('maps a deleted threaded root returned by a conversation reload without restoring its body', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade, { isDeleted: true, body: 'Deleted reload secret' });

    expect(facade.page().messages[0]).toMatchObject({
      id: 'root-a',
      body: '',
      isDeleted: true,
      thread: { replyCount: 1 }
    });
    expect(JSON.stringify(facade.page().messages[0])).not.toContain('Deleted reload secret');
  });

  it('rejects a malformed reply identity from the authoritative POST projection', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Malformed response test');
    facade.sendThreadDraft();

    httpMock.expectOne('/api/messages/root-a/thread/messages').flush({
      message: { ...threadReply('', 'Malformed response test'), id: undefined },
      summary: threadSummary(2, ['Mock User B'])
    });

    const failedThread = facade.thread();
    expect(failedThread).toMatchObject({
      status: 'error',
      replies: []
    });
    expect(failedThread.rootMessage).toBeUndefined();
    expect(failedThread.summary).toBeUndefined();
    expect(facade.page().messages[0].thread).toBeUndefined();
  });

  it('clears parent, replies, count, and participant names together after an authorization failure', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    openThread(httpMock, facade);

    expect(facade.thread().rootMessage?.body).toBe('Pinned parent body');
    expect(facade.thread().summary?.participantDisplayNames).toEqual(['Mock User B']);
    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a',
      threadRootMessageId: 'root-a',
      requiresRefetch: true
    }));
    httpMock.expectOne('/api/messages/root-a/thread').flush(
      {},
      { status: 403, statusText: 'Forbidden' }
    );

    const deniedThread = facade.thread();
    expect(deniedThread).toMatchObject({
      status: 'permissionDenied',
      replies: []
    });
    expect(deniedThread.rootMessage).toBeUndefined();
    expect(deniedThread.summary).toBeUndefined();
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().conversation.mentionCandidates ?? []).toEqual([]);
    expect(JSON.stringify(facade.thread())).not.toContain('Mock User B');
    expect(JSON.stringify(facade.thread())).not.toContain('Pinned parent body');
  });

  it('returns focus to the native timeline trigger when the thread closes', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    const trigger = document.createElement('button');
    trigger.id = 'thread-trigger-root-a';
    document.body.append(trigger);
    openThread(httpMock, facade, trigger.id);

    facade.closeThread();
    await Promise.resolve();

    expect(facade.thread().status).toBe('closed');
    expect(document.activeElement).toBe(trigger);
  });

  it('retries focus against the rendered trigger when the mobile close render replaces it', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    const hiddenTrigger = document.createElement('button');
    hiddenTrigger.id = 'thread-trigger-root-a';
    const timeline = document.createElement('section');
    timeline.id = 'message-timeline';
    timeline.tabIndex = -1;
    document.body.append(hiddenTrigger, timeline);
    openThread(httpMock, facade, hiddenTrigger.id);

    const animationFrames: FrameRequestCallback[] = [];
    const originalRequestAnimationFrame = window.requestAnimationFrame;
    const originalHiddenTriggerFocus = hiddenTrigger.focus.bind(hiddenTrigger);
    const originalTimelineFocus = timeline.focus.bind(timeline);
    window.requestAnimationFrame = (callback: FrameRequestCallback): number => {
      animationFrames.push(callback);
      return animationFrames.length;
    };
    hiddenTrigger.focus = () => undefined;
    timeline.focus = () => undefined;

    try {
      facade.closeThread();
      await Promise.resolve();

      expect(animationFrames).toHaveLength(1);
      const renderedTrigger = document.createElement('button');
      renderedTrigger.id = hiddenTrigger.id;
      hiddenTrigger.remove();
      document.body.append(renderedTrigger);

      animationFrames.shift()?.(0);

      expect(document.activeElement).toBe(renderedTrigger);
      expect(animationFrames).toEqual([]);
      renderedTrigger.remove();
    } finally {
      window.requestAnimationFrame = originalRequestAnimationFrame;
      hiddenTrigger.focus = originalHiddenTriggerFocus;
      timeline.focus = originalTimelineFocus;
      hiddenTrigger.remove();
      timeline.remove();
    }
  });
});

describe('ThreadPreviewComponent', () => {
  let fixture: ComponentFixture<ThreadPreviewComponent>;

  afterEach(() => TestBed.resetTestingModule());

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ThreadPreviewComponent] }).compileComponents();
    fixture = TestBed.createComponent(ThreadPreviewComponent);
    fixture.componentRef.setInput('thread', threadViewModel());
    fixture.componentRef.setInput('canPost', true);
    fixture.componentRef.setInput('canCreateThread', true);
    fixture.detectChanges();
  });

  it('switches the mounted thread labels while preserving reply draft, root, selection and focus', async () => {
    await Promise.resolve();
    const i18n = TestBed.inject(I18nService);
    const root = fixture.nativeElement as HTMLElement;
    const textarea = root.querySelector<HTMLTextAreaElement>('[data-testid="thread-reply-draft"]')!;
    const send = vi.spyOn(fixture.componentInstance.send, 'emit');
    const draftChange = vi.spyOn(fixture.componentInstance.draftChange, 'emit');
    const close = vi.spyOn(fixture.componentInstance.close, 'emit');
    const thread = fixture.componentInstance.thread;
    textarea.focus();
    textarea.setSelectionRange(2, 5);

    i18n.setLocale('ja');
    fixture.detectChanges();
    await Promise.resolve();
    expect(root.querySelector('[data-testid="send-thread-reply"]')?.textContent?.trim()).toBe('返信');
    expect(root.querySelector('[data-testid="thread-close"]')?.getAttribute('aria-label')).toBe('スレッドを閉じる');
    expect(root.querySelector('[data-testid="thread-root-message"]')?.textContent).toContain('Pinned parent body');
    expect(root.querySelector('[data-testid="thread-bounded-notice"]')?.textContent).toContain('101件');
    expect(fixture.componentInstance.thread).toBe(thread);
    expect(textarea.value).toBe(thread.draft);
    expect(document.activeElement).toBe(textarea);
    expect(textarea.selectionStart).toBe(2);
    expect(send).not.toHaveBeenCalled();
    expect(draftChange).not.toHaveBeenCalled();
    expect(close).not.toHaveBeenCalled();

    i18n.setLocale('en');
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="send-thread-reply"]')?.textContent?.trim()).toBe('Reply');
    expect(root.querySelector('[data-testid="thread-reply-draft"]')).toBe(textarea);
    expect(textarea.value).toBe(thread.draft);
    expect(document.activeElement).toBe(textarea);
  });

  it('pins the parent, discloses bounded loading, and uses a separate reply context', async () => {
    await Promise.resolve();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-testid="thread-root-message"]')?.textContent).toContain('Pinned parent body');
    expect(root.querySelector('[data-testid="thread-bounded-notice"]')?.textContent).toContain(
      'Showing the latest 100 of 101 replies'
    );
    expect(root.querySelector('label[for="thread-reply-draft"]')?.textContent).toContain(
      'Replying in thread to Mock User B'
    );
    expect(document.activeElement).toBe(root.querySelector('[data-testid="thread-preview"]'));
  });

  it('truthfully identifies an exact selected reply inside the bounded projection', () => {
    fixture.componentRef.setInput('thread', {
      ...threadViewModel(),
      anchorReplyMessageId: 'reply-a'
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement)
      .querySelector('[data-testid="thread-bounded-notice"]')?.textContent)
      .toContain('including the selected reply');
  });

  it('supports Enter, Shift+Enter, IME, Escape, and native back/close controls', () => {
    const send = vi.spyOn(fixture.componentInstance.send, 'emit');
    const close = vi.spyOn(fixture.componentInstance.close, 'emit');
    const textarea = (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLTextAreaElement>('[data-testid="thread-reply-draft"]')!;

    textarea.value = 'Keyboard reply';
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, bubbles: true, cancelable: true }));
    expect(send).not.toHaveBeenCalled();
    textarea.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    expect(send).not.toHaveBeenCalled();
    fixture.componentInstance.submit(new Event('submit', { cancelable: true }));
    expect(send).not.toHaveBeenCalled();
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    expect(close).not.toHaveBeenCalled();
    textarea.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }));
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 229, bubbles: true, cancelable: true }));
    expect(send).not.toHaveBeenCalled();
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    expect(send).toHaveBeenCalledWith([]);

    const panel = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-testid="thread-preview"]')!;
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    expect(close).toHaveBeenCalledTimes(1);
    const back = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="thread-back"]')!;
    const closeButton = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="thread-close"]')!;
    expect(back.type).toBe('button');
    expect(closeButton.type).toBe('button');
    back.click();
    closeButton.click();
    expect(close).toHaveBeenCalledTimes(3);
  });

  it.each(['thread-back', 'thread-close'])('does not steal focus from %s when loading completes', async (testId) => {
    const pendingFixture = TestBed.createComponent(ThreadPreviewComponent);
    pendingFixture.componentRef.setInput('thread', {
      ...threadViewModel(),
      status: 'loading',
      rootMessage: undefined,
      replies: [],
      summary: undefined
    });
    pendingFixture.componentRef.setInput('canPost', true);
    pendingFixture.componentRef.setInput('canCreateThread', true);
    pendingFixture.detectChanges();
    await Promise.resolve();
    const control = (pendingFixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`)!;
    control.focus();

    pendingFixture.componentRef.setInput('thread', threadViewModel());
    pendingFixture.detectChanges();
    await Promise.resolve();

    expect(document.activeElement).toBe(control);
    pendingFixture.destroy();
  });

  it('keeps keyboard focus inside the panel when deletion disables the focused reply draft', async () => {
    await Promise.resolve();
    const root = fixture.nativeElement as HTMLElement;
    const textarea = root.querySelector<HTMLTextAreaElement>('[data-testid="thread-reply-draft"]')!;
    const back = root.querySelector<HTMLButtonElement>('[data-testid="thread-back"]')!;
    const close = vi.spyOn(fixture.componentInstance.close, 'emit');
    textarea.focus();
    expect(document.activeElement).toBe(textarea);

    const current = threadViewModel();
    fixture.componentRef.setInput('thread', {
      ...current,
      rootMessage: { ...current.rootMessage!, isDeleted: true, body: '', version: 3 }
    });
    fixture.detectChanges();
    await Promise.resolve();

    expect(document.activeElement).toBe(back);
    back.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('renders durable reply tombstones and disables posting to a deleted parent', () => {
    const current = threadViewModel();
    fixture.componentRef.setInput('thread', {
      ...current,
      rootMessage: { ...current.rootMessage!, isDeleted: true, body: '' },
      replies: [{ ...current.replies[0], isDeleted: true, body: '' }]
    });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('.thread__tombstone')).toHaveLength(2);
    expect(root.querySelector<HTMLTextAreaElement>('[data-testid="thread-reply-draft"]')?.disabled).toBe(true);
    expect(root.querySelector('[data-testid="thread-composer-disabled"]')?.textContent).toContain(
      'parent message was deleted'
    );
  });
});

describe('Message thread route wiring', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    { component: ChannelMessagingPageComponent, kind: 'channel' as const, expectedDm: false },
    { component: DmPageComponent, kind: 'dm' as const, expectedDm: true },
  ])('uses explicit $kind route provenance for own-message presentation while keeping actions and content', async ({ component, kind, expectedDm }) => {
    const fixture = await renderRouteThread<unknown>(component, kind, 0, false, true);
    const root = fixture.nativeElement as HTMLElement;
    const message = root.querySelector<HTMLElement>('[data-testid="confirmed-message"]')!;
    expect(message.classList.contains('message--dm')).toBe(expectedDm);
    expect(message.classList.contains('message--own')).toBe(true);
    expect(message.querySelector('[data-testid="message-body"]')?.textContent).toBe('Pinned parent body');
    expect(root.querySelector('[data-testid="open-message-thread-root-a"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="save-message-for-later-root-a"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="message-more-actions-root-a"]')).not.toBeNull();
  });

  it('opens and closes the contextual channel panel from the visible reply-count entry', async () => {
    const fixture = await renderRouteThread(ChannelMessagingPageComponent, 'channel', 2);
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('[data-testid="open-message-thread-root-a"]')!;

    expect(trigger.textContent).toContain('↳');
    expect(trigger.textContent).toContain('2 replies');
    trigger.click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="thread-preview"]')).not.toBeNull();
    root.querySelector<HTMLButtonElement>('[data-testid="thread-close"]')!.click();
    fixture.detectChanges();
    await Promise.resolve();

    expect(root.querySelector('[data-testid="thread-preview"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('opens and closes the shared DM panel from the zero-reply entry', async () => {
    const fixture = await renderRouteThread(DmPageComponent, 'dm', 0);
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('[data-testid="open-message-thread-root-a"]')!;

    expect(trigger.textContent).toContain('↳');
    expect(trigger.textContent).toContain('Reply in thread');
    trigger.click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="thread-preview"]')).not.toBeNull();
    root.querySelector<HTMLButtonElement>('[data-testid="thread-back"]')!.click();
    fixture.detectChanges();
    await Promise.resolve();

    expect(root.querySelector('[data-testid="thread-preview"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('keeps a deleted threaded parent readable and keyboard-reopenable without rendering its body', async () => {
    const fixture = await renderRouteThread(DmPageComponent, 'dm', 1, true);
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('[data-testid="open-message-thread-root-a"]')!;

    expect(root.querySelector('[data-testid="message-tombstone"]')?.textContent).toContain('Message deleted');
    expect(root.textContent).not.toContain('Pinned parent body');
    expect(trigger.textContent).toContain('1 reply');
    trigger.focus();
    trigger.click();
    fixture.detectChanges();
    await Promise.resolve();
    const panel = root.querySelector<HTMLElement>('[data-testid="thread-preview"]')!;
    expect(panel).not.toBeNull();
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    await Promise.resolve();

    expect(root.querySelector('[data-testid="thread-preview"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});

describe('Session-only dedicated thread workspace', () => {
  afterEach(() => {
    sessionStorage.clear();
    TestBed.inject(HttpTestingController, null)?.verify();
    TestBed.resetTestingModule();
  });

  it('reauthorizes every tab switch and restores only the exact root draft after its GET succeeds', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.setThreadDraft('Private A');
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    facade.setThreadDraft('Private B');
    facade.selectThread('root-a');
    expect(facade.thread()).toMatchObject({ status: 'loading', draft: '', replies: [] });
    expect(facade.thread().rootMessage).toBeUndefined();
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    expect(facade.thread().draft).toBe('Private A');
    expect(facade.threadTabs().map((tab) => tab.identity)).toEqual([
      { tenantId: 'mock-tenant', userId: currentUserId, workspaceId: 'workspace-a', conversationId: 'conversation-a', kind: 'channel', rootMessageId: 'root-a' },
      { tenantId: 'mock-tenant', userId: currentUserId, workspaceId: 'workspace-a', conversationId: 'conversation-a', kind: 'channel', rootMessageId: 'root-b' }
    ]);
    expect(JSON.stringify(facade.threadTabs())).not.toContain('Private');
    expect(sessionStorage.length).toBe(0);
    facade.selectThread('root-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    expect(facade.thread().draft).toBe('Private B');
  });

  it('ignores an older tab load arriving after a different root becomes active', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    facade.openThread('root-a', 'trigger-a');
    const old = httpMock.expectOne('/api/messages/root-a/thread');
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    facade.setThreadDraft('Active B');
    old.flush(threadDto());
    expect(facade.thread()).toMatchObject({ rootMessageId: 'root-b', draft: 'Active B' });
    expect(facade.thread().replies.every((reply) => reply.threadRootMessageId === 'root-b')).toBe(true);
  });

  it('settles an inactive send without clearing the active draft or allowing in-flight draft edits', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.setThreadDraft('Send A');
    facade.sendThreadDraft();
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    facade.setThreadDraft('Too late to edit a dispatched command');
    expect(facade.thread().draft).toBe('Send A');
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    facade.setThreadDraft('Keep B');
    post.flush({ message: threadReply('reply-sent', 'Send A', post.request.body.clientRequestId), summary: threadSummary(2, ['Mock User B']) });
    expect(facade.thread()).toMatchObject({ rootMessageId: 'root-b', draft: 'Keep B', sending: false });
    facade.selectThread('root-a');
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    expect(facade.thread()).toMatchObject({ rootMessageId: 'root-a', draft: '', sending: false });
  });

  it('restores a background send failure and its exact retry identity when reopening that tab', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.setThreadDraft('Retry A');
    facade.sendThreadDraft();
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    post.flush({}, { status: 503, statusText: 'Unavailable' });
    facade.selectThread('root-a');
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    expect(facade.thread()).toMatchObject({ status: 'ready', draft: 'Retry A', sending: false, pendingClientRequestId: post.request.body.clientRequestId });
    expect(facade.thread().error).toContain('could not be sent');
    facade.sendThreadDraft();
    const retry = httpMock.expectOne('/api/messages/root-a/thread/messages');
    expect(retry.request.body.clientRequestId).toBe(post.request.body.clientRequestId);
    retry.flush({ message: threadReply('reply-sent'), summary: threadSummary(2, ['Mock User B']) });
  });

  it('refetches the active projection when an earlier send settles while its tab is reauthorizing', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.setThreadDraft('Send A');
    facade.sendThreadDraft();
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    facade.selectThread('root-a');
    const stale = httpMock.expectOne('/api/messages/root-a/thread');
    post.flush({ message: threadReply('reply-sent'), summary: threadSummary(2, ['Mock User B']) });
    const fresh = httpMock.expectOne('/api/messages/root-a/thread');
    stale.flush(threadDto());
    expect(facade.thread().status).toBe('loading');
    fresh.flush(threadDto({ replies: [threadReply('reply-a'), threadReply('reply-sent')], summary: threadSummary(2, ['Mock User B']) }));
    expect(facade.thread()).toMatchObject({ status: 'ready', draft: '', sending: false });
  });

  it('waits for an in-flight POST before reconnect reauthorization and preserves its retry identity on uncertain failure', async () => {
    let catchUp: (() => Promise<void> | void) | undefined;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Uncertain send');
    facade.sendThreadDraft();
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    const requestId = post.request.body.clientRequestId;
    const completion = catchUp!();
    httpMock.expectNone('/api/conversations/conversation-a');
    expect(post.cancelled).toBe(false);
    post.flush({}, { status: 503, statusText: 'Unavailable' });
    // Catch-up waits through two promise continuations before dispatching its GET.
    for (let index = 0; index < 8; index++) { await Promise.resolve(); }
    httpMock.expectOne('/api/conversations').flush({ items: [] });
    httpMock.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel',
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-a/messages').flush({ items: [rootMessage()] });
    await completion;
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    expect(facade.thread()).toMatchObject({ draft: 'Uncertain send', sending: false, pendingClientRequestId: requestId });
    facade.sendThreadDraft();
    const retry = httpMock.expectOne('/api/messages/root-a/thread/messages');
    expect(retry.request.body.clientRequestId).toBe(requestId);
    retry.flush({ message: threadReply('reply-sent'), summary: threadSummary(2, ['Mock User B']) });
  });

  it('discards a closed tab without a server delete and does not resurrect it on late send success', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Send then close');
    facade.sendThreadDraft();
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    facade.closeThreadTab('root-a');
    post.flush({ message: threadReply('reply-sent'), summary: threadSummary(2, ['Mock User B']) });
    expect(facade.thread().status).toBe('closed');
    expect(facade.threadTabs()).toEqual([]);
    httpMock.expectNone((request) => request.method === 'DELETE');
  });

  it.each(['before-action', 'after-action'] as const)(
    'fails closed for a denied pre-existing Thread GET settling %s while selecting the same active tab',
    async (order) => {
      const events = new Subject<DurableRealtimeEvent>();
      const { httpMock, facade } = await configureFacade(events);
      openConversation(httpMock, facade);
      openThread(httpMock, facade);
      facade.setThreadDraft('Only the authorized actor may see this draft');
      facade.setThreadWorkspaceExpanded(true);

      // A normal server-derived Thread event can start a second GET before
      // the user activates a previously selected tab. It must not supply
      // authority to a new request and may independently revoke the view.
      events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
        conversationId: 'conversation-a', threadRootMessageId: 'root-a',
        replyCount: 1, requiresRefetch: true
      }));
      const older = httpMock.expectOne('/api/messages/root-a/thread');
      expect(older.request.method).toBe('GET');
      expect(older.request.withCredentials).toBe(true);

      if (order === 'before-action') {
        // An earlier 400 is itself authoritative denial; there is no longer
        // any eligible private tab to click, so no "fresh" GET may be credited.
        older.flush({}, { status: 400, statusText: 'Bad Request' });
        expect(facade.threadTabs()).toHaveLength(0);
        expect(facade.thread()).toMatchObject({ status: 'permissionDenied', rootMessageId: null });
        facade.selectThread('root-a');
        httpMock.expectNone('/api/messages/root-a/thread');
      } else {
        // A selected-tab activation makes a *distinct* fresh Request while
        // the old GET remains pending. Its generation supersedes the old
        // denial, but fresh authority must still be obtained independently.
        facade.selectThread('root-a');
        const fresh = httpMock.expectOne('/api/messages/root-a/thread');
        expect(fresh.request.method).toBe('GET');
        expect(fresh.request.withCredentials).toBe(true);
        expect(facade.thread()).toMatchObject({ status: 'loading', draft: '', replies: [] });
        older.flush({}, { status: 400, statusText: 'Bad Request' });
        expect(facade.thread()).toMatchObject({ status: 'loading', rootMessageId: 'root-a' });
        expect(facade.thread().rootMessage).toBeUndefined();
        fresh.flush({}, { status: 400, statusText: 'Bad Request' });
        expect(facade.thread()).toMatchObject({ status: 'permissionDenied', rootMessageId: null });
      }

      expect(facade.thread().rootMessage).toBeUndefined();
      expect(facade.thread().replies).toEqual([]);
      expect(facade.thread().draft).toBe('');
      expect(facade.threadTabs()).toEqual([]);
      expect(facade.threadWorkspaceExpanded()).toBe(false);
      expect(facade.page().conversation.capabilities).toEqual([]);
      expect(facade.page().messages).toEqual([]);
      httpMock.expectNone((request) => request.method === 'POST');
    }
  );

  it('purges every cached draft and tab after denied switch reauthorization', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.setThreadDraft('Private A');
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    facade.setThreadDraft('Private B');
    facade.setThreadWorkspaceExpanded(true);
    facade.selectThread('root-a');
    httpMock.expectOne('/api/messages/root-a/thread').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(facade.thread()).toMatchObject({ status: 'permissionDenied', draft: '', replies: [] });
    expect(facade.thread().rootMessage).toBeUndefined();
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.threadWorkspaceExpanded()).toBe(false);
  });

  it('rejects a same-conversation thread response carrying another Workspace identity', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    facade.openThread('root-a', 'trigger-a');
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto({ rootMessage: { ...rootMessage(), workspaceId: 'other-workspace' } }));
    expect(facade.thread()).toMatchObject({ status: 'error', draft: '', replies: [] });
    expect(facade.threadTabs()).toEqual([]);
  });

  it.each(['session', 'tenant', 'authorization', 'workspace'] as const)('clears tab identities, selection, and drafts at the %s boundary', async (reason) => {
    let clear: ((reason: ProtectedStateClearReason) => void) | undefined;
    const { httpMock, facade } = await configureFacade(undefined, { clearState: (callback) => { clear = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Boundary secret');
    facade.setThreadWorkspaceExpanded(true);
    clear!(reason);
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.thread()).toMatchObject({ status: 'closed', rootMessageId: null, draft: '', replies: [] });
    expect(facade.threadWorkspaceExpanded()).toBe(false);
  });

  it.each([
    ['selected', true], ['closed', true], ['closed-captured', true],
    ['denied-401', true], ['denied-403', true], ['selected', false]
  ] as const)(
    'preserves newer Thread intent while catch-up waits for its list: %s existing=%s', async (outcome, hadPreviousThread) => {
    let catchUp: (() => Promise<void> | void) | undefined;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    if (hadPreviousThread) {
      openThread(httpMock, facade);
      facade.setThreadDraft('Captured A draft');
    }
    const fixture = TestBed.createComponent(ThreadWorkspaceComponent);
    const completion = catchUp!();
    const list = httpMock.expectOne('/api/conversations');
    httpMock.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel',
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-a/messages').flush({ items: [rootMessage(), { ...rootMessage(), id: 'root-b' }] });
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    facade.setThreadDraft('Newly selected draft');
    expect(facade.thread()).toMatchObject({ status: 'ready', rootMessageId: 'root-b' });
    if (outcome === 'closed') { facade.closeThread(false); }
    if (outcome === 'closed-captured') { facade.closeThreadTab('root-a'); }
    if (outcome.startsWith('denied-')) {
      const status = outcome === 'denied-401' ? 401 : 403;
      list.flush({}, { status, statusText: 'Denied' });
    } else {
      list.flush({ items: [] });
    }
    await completion;
    fixture.detectChanges();
    const selected = outcome === 'selected' || outcome === 'closed-captured';
    expect(facade.thread()).toMatchObject(selected
      ? { status: 'ready', rootMessageId: 'root-b', draft: 'Newly selected draft' }
      : { status: 'closed', rootMessageId: null, draft: '', replies: [] });
    const retainedA = hadPreviousThread && !outcome.startsWith('denied-') && outcome !== 'closed-captured';
    expect(facade.threadTabs().map((tab) => tab.identity.rootMessageId)).toEqual([
      ...(retainedA ? ['root-a'] : []), ...(selected ? ['root-b'] : []),
    ]);
    const root = fixture.nativeElement.querySelector('[data-testid="thread-root-message"]') as HTMLElement | null;
    if (selected) {
      expect(root?.textContent).toContain('Pinned parent body');
      expect((fixture.nativeElement.querySelector('[data-testid="thread-reply-draft"]') as HTMLTextAreaElement).value).toBe('Newly selected draft');
    } else {
      expect(root).toBeNull();
    }
    if (outcome.startsWith('denied-')) {
      expect(facade.page()).toMatchObject({ status: 'permissionDenied', draft: '', messages: [] });
      expect(facade.page().conversation.capabilities).toEqual([]);
    }
    httpMock.expectNone('/api/messages/root-a/thread');
    if (retainedA) {
      facade.openThread('root-a', 'trigger-a');
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[data-testid="thread-root-message"]')).toBeNull();
      const freshA = httpMock.expectOne('/api/messages/root-a/thread');
      expect(freshA.request.withCredentials).toBe(true);
      freshA.flush(threadDto());
      fixture.detectChanges();
      expect(facade.thread()).toMatchObject({ status: 'ready', rootMessageId: 'root-a', draft: 'Captured A draft' });
      expect((fixture.nativeElement.querySelector('[data-testid="thread-reply-draft"]') as HTMLTextAreaElement).value).toBe('Captured A draft');
    }
    fixture.destroy();
  });

  it('re-authorizes the mounted Thread root when routine catch-up overlaps its first authorized GET', async () => {
    let catchUp: (() => Promise<void> | void) | undefined;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    const fixture = TestBed.createComponent(ThreadWorkspaceComponent);

    facade.openThread('root-a', 'thread-trigger-root-a');
    const first = httpMock.expectOne('/api/messages/root-a/thread');
    expect(first.request.method).toBe('GET');
    expect(facade.thread().status).toBe('loading');

    // Model the observed authorized 200 / missing-panel boundary while an
    // independent, normal realtime catch-up is queued. Do not grant authority
    // from the original response or complete the revalidation with mocks alone.
    const completion = catchUp!();
    httpMock.expectNone('/api/conversations/conversation-a');
    expect(first.cancelled).toBe(false);
    first.flush(threadDto());
    for (let index = 0; index < 8; index++) { await Promise.resolve(); }

    expect(facade.page().conversation.id).toBe('');
    expect(facade.thread()).toMatchObject({ status: 'closed', rootMessageId: null });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="thread-root-message"]')).toBeNull();
    httpMock.expectOne('/api/conversations').flush({ items: [] });
    httpMock.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel', title: 'General',
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-a/messages').flush({ items: [rootMessage()] });
    await completion;

    // An HTTP 200 for the original request is not a UI acceptance result.
    // A separate, current-generation authorized GET must actually drive the
    // visible root; the stale response cannot restore it by itself.
    expect(facade.thread()).toMatchObject({ status: 'loading', rootMessageId: 'root-a' });
    expect(facade.threadTabs()).toHaveLength(1);
    // The tab may remain discoverable as navigation intent, but a pending
    // current-authority read may not render any previously authorized body.
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="thread-root-message"]')).toBeNull();
    const current = httpMock.expectOne('/api/messages/root-a/thread');
    expect(current.request.method).toBe('GET');
    expect(current.request.withCredentials).toBe(true);
    current.flush(threadDto());
    fixture.detectChanges();

    expect(facade.thread()).toMatchObject({ status: 'ready', rootMessageId: 'root-a' });
    const root = fixture.nativeElement.querySelector('[data-testid="thread-root-message"]') as HTMLElement | null;
    expect(root).not.toBeNull();
    expect(root?.textContent).toContain('Pinned parent body');
    expect(facade.threadTabs()).toHaveLength(1);
    httpMock.expectNone((request) => request.method === 'POST');
    fixture.destroy();
  });

  it('ignores two HTTP 200 Thread GETs superseded by a same-scope catch-up until a fresh authorized root mounts', async () => {
    let catchUp: (() => Promise<void> | void) | undefined;
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events, {
      catchUp: (callback) => { catchUp = callback; }
    });
    openConversation(httpMock, facade);
    const fixture = TestBed.createComponent(ThreadWorkspaceComponent);

    facade.openThread('root-a', 'thread-trigger-root-a');
    const first = httpMock.expectOne('/api/messages/root-a/thread');
    events.next(realtimeEvent('Messaging.ThreadChanged.v1', {
      conversationId: 'conversation-a', threadRootMessageId: 'root-a',
      replyCount: 2, requiresRefetch: true
    }));
    const second = httpMock.expectOne('/api/messages/root-a/thread');
    // Catch-up waits for the two already-dispatched protected requests. It must
    // neither reuse their old body as current authority nor lose root intent.
    const completion = catchUp!();
    expect(first.cancelled).toBe(false);
    expect(second.cancelled).toBe(false);
    first.flush(threadDto({ replies: [threadReply('reply-stale')] }));
    second.flush(threadDto({
      replies: [threadReply('reply-a'), threadReply('reply-newer')],
      summary: threadSummary(2, ['Newest Participant'])
    }));
    for (let index = 0; index < 8; index++) { await Promise.resolve(); }
    expect(facade.thread()).toMatchObject({ status: 'closed', rootMessageId: null });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="thread-root-message"]')).toBeNull();

    httpMock.expectOne('/api/conversations').flush({ items: [] });
    httpMock.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel', title: 'General',
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-a/messages').flush({ items: [rootMessage()] });
    await completion;

    expect(facade.thread()).toMatchObject({ status: 'loading', rootMessageId: 'root-a' });
    expect(facade.threadTabs().map((tab) => tab.identity.rootMessageId)).toEqual(['root-a']);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="thread-root-message"]')).toBeNull();

    // Only this third, current-generation authorized read may unlock the body.
    const fresh = httpMock.expectOne('/api/messages/root-a/thread');
    expect(fresh.request.withCredentials).toBe(true);
    fresh.flush(threadDto());
    fixture.detectChanges();
    expect(facade.thread()).toMatchObject({ status: 'ready', rootMessageId: 'root-a' });
    const rendered = fixture.nativeElement.querySelector('[data-testid="thread-root-message"]');
    expect(rendered?.textContent).toContain('Pinned parent body');
    httpMock.expectNone((request) => request.method === 'POST');
    fixture.destroy();
  });

  it('preserves memory-only tab intent across transport catch-up only after fresh conversation and thread authorization', async () => {
    let catchUp: (() => Promise<void> | void) | undefined;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Reconnect draft');
    facade.setThreadWorkspaceExpanded(true);
    const completion = catchUp!();
    expect(facade.threadTabs()).toEqual([]);
    httpMock.expectOne('/api/conversations').flush({ items: [] });
    httpMock.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel', title: 'General',
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-a/messages').flush({ items: [rootMessage()] });
    await completion;
    expect(facade.thread()).toMatchObject({ status: 'loading', draft: '' });
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    expect(facade.thread()).toMatchObject({ status: 'ready', draft: 'Reconnect draft' });
    expect(facade.threadWorkspaceExpanded()).toBe(true);
  });

  it('serializes an overlapping catch-up that begins while the earlier aggregate still has protected HTTP requests', async () => {
    let catchUp!: () => Promise<void> | void;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Same-authority local Thread draft');
    const first = catchUp();
    const overlap = catchUp();
    flushCaughtUpConversation(httpMock);
    await first;
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    await vi.waitFor(() => expect(facade.page().status).toBe('loading'));
    flushCaughtUpConversation(httpMock);
    await overlap;
    expect(facade.threadTabs().map((tab) => tab.identity.rootMessageId)).toEqual(['root-a']);
    expect(facade.thread()).toMatchObject({ status: 'loading', rootMessageId: 'root-a', draft: '' });
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    expect(facade.thread()).toMatchObject({ status: 'ready', rootMessageId: 'root-a',
      draft: 'Same-authority local Thread draft' });
    httpMock.expectNone((request) => request.method === 'POST');
  });

  it('retains same-scope Thread intent when another catch-up starts after HTTP teardown but before restoration resumes', async () => {
    let catchUp!: () => Promise<void> | void;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Same-authority local Thread draft');
    facade.setThreadWorkspaceExpanded(true);
    const first = catchUp();
    flushCaughtUpConversation(httpMock);
    // HTTP subscriptions have completed, but the first async continuation has
    // not restored its locally captured tabs yet. Exercise that exact gap.
    const overlap = catchUp();
    await first;
    const firstAuthorizedThread = httpMock.match('/api/messages/root-a/thread');
    // An uncoordinated implementation has already replaced the aggregate and
    // issues no first restoration GET. Still settle that aggregate so a failed
    // control cannot hide outstanding HTTP requests behind teardown errors.
    if (firstAuthorizedThread.length > 0) {
      for (const request of firstAuthorizedThread) { request.flush(threadDto()); }
      await vi.waitFor(() => expect(facade.page().status).toBe('loading'));
    }
    flushCaughtUpConversation(httpMock);
    await overlap;
    const observed = { tabs: facade.threadTabs().map((tab) => tab.identity.rootMessageId),
      thread: facade.thread(), expanded: facade.threadWorkspaceExpanded() };
    // Consume the current authoritative request only if the implementation
    // retained the intent. The assertion below still fails on an empty result.
    const pending = httpMock.match('/api/messages/root-a/thread');
    for (const request of pending) { request.flush(threadDto()); }
    expect(firstAuthorizedThread).toHaveLength(1);
    expect(observed.tabs).toEqual(['root-a']);
    expect(observed.thread).toMatchObject({ status: 'loading', rootMessageId: 'root-a', draft: '' });
    expect(observed.expanded).toBe(true);
    expect(pending).toHaveLength(1);
    expect(facade.thread()).toMatchObject({ status: 'ready', rootMessageId: 'root-a',
      draft: 'Same-authority local Thread draft' });
    httpMock.expectNone((request) => request.method === 'POST');
  });

  it.each(['workspace', 'authorization', 'session', 'tenant'] as const)(
    'cannot restore captured Thread intent after a %s boundary in the HTTP completion gap', async (reason) => {
    let catchUp!: () => Promise<void> | void;
    let clearState!: (reason: ProtectedStateClearReason) => void;
    const { httpMock, facade } = await configureFacade(undefined, {
      catchUp: (callback) => { catchUp = callback; }, clearState: (callback) => { clearState = callback; }
    });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Must not cross a Workspace boundary');
    const first = catchUp();
    flushCaughtUpConversation(httpMock);
    const queued = catchUp();
    const abandoned = httpMock.match((request) => request.url.startsWith('/api/conversations'));
    clearState(reason);
    await Promise.all([first, queued]);
    expect(abandoned.every((request) => request.cancelled)).toBe(true);
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.thread()).toMatchObject({ status: 'closed', rootMessageId: null, draft: '', replies: [] });
    expect(facade.page().messages).toEqual([]);
    httpMock.expectNone('/api/messages/root-a/thread');
    httpMock.expectNone((request) => request.method === 'POST');
  });

  it.each([['currentUser', 'actor-b'], ['currentTenant', 'tenant-b']] as const)(
    'rejects a queued catch-up after actual %s replacement in the completion gap', async (refresh, identity) => {
    let catchUp!: () => Promise<void> | void;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Actor A private Thread draft');
    const first = catchUp();
    flushCaughtUpConversation(httpMock);
    const queued = catchUp();
    const abandoned = httpMock.match((request) => request.url.startsWith('/api/conversations'));
    refreshActor(httpMock, refresh, identity);
    await Promise.all([first, queued]);
    expect(abandoned.every((request) => request.cancelled)).toBe(true);
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.thread()).toMatchObject({ status: 'closed', draft: '', replies: [] });
    expect(facade.page().messages).toEqual([]);
    httpMock.expectNone('/api/messages/root-a/thread');
    httpMock.expectNone((request) => request.method === 'POST');
  });

  it('rejects a queued catch-up when the route enters another Conversation and Workspace in the completion gap', async () => {
    let catchUp!: () => Promise<void> | void;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Previous route private Thread draft');
    const first = catchUp();
    flushCaughtUpConversation(httpMock);
    const queued = catchUp();
    const abandoned = httpMock.match((request) => request.url.startsWith('/api/conversations'));
    facade.loadConversation('conversation-b', 'channel', 'workspace-b');
    httpMock.expectOne('/api/conversations').flush({ items: [] });
    httpMock.expectOne('/api/conversations/conversation-b').flush({
      id: 'conversation-b', workspaceId: 'workspace-b', type: 'ProjectChannel',
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-b/messages').flush({ items: [] });
    await Promise.all([first, queued]);
    expect(abandoned.every((request) => request.cancelled)).toBe(true);
    expect(facade.page().conversation).toMatchObject({ id: 'conversation-b', workspaceId: 'workspace-b' });
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.thread()).toMatchObject({ status: 'closed', draft: '', replies: [] });
    httpMock.expectNone('/api/messages/root-a/thread');
    httpMock.expectNone((request) => request.method === 'POST');
  });

  it('cannot restore a queued Thread after authoritative aggregate denial and cannot send while authority is pending', async () => {
    let catchUp!: () => Promise<void> | void;
    const { httpMock, facade } = await configureFacade(undefined, { catchUp: (callback) => { catchUp = callback; } });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Only the still-authorized actor may restore this');
    const first = catchUp();
    const queued = catchUp();
    facade.setDraft('Local pending text');
    facade.sendDraft();
    facade.sendThreadDraft();
    httpMock.expectNone((request) => request.method === 'POST');
    expect(facade.page().conversation.capabilities).toEqual([]);
    httpMock.expectOne('/api/conversations').flush({ items: [] });
    httpMock.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel',
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-a/messages').flush({}, { status: 403, statusText: 'Forbidden' });
    await Promise.all([first, queued]);
    expect(facade.page().status).toBe('permissionDenied');
    expect(facade.page().messages).toEqual([]);
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.thread()).toMatchObject({ status: 'closed', draft: '', replies: [] });
    httpMock.expectNone('/api/messages/root-a/thread');
    httpMock.expectNone((request) => request.method === 'POST');
  });

  it.each([false, true])('keeps the real enclosing Workspace lifecycle valid when a committed Workspace is %s', async (selected) => {
    const { httpMock, facade } = await configureFacade(undefined, { realWorkspaceLifecycle: true });
    const preferences = TestBed.inject(WorkspacePreferenceService);
    preferences.clear('mock-tenant', currentUserId);
    preferences.clear('mock-tenant', 'actor-b');
    TestBed.inject(WorkspacesFacade);
    flushWorkspaceOptions(httpMock, selected ? 1 : 2);
    TestBed.tick();
    const selection = TestBed.inject(WorkspaceSelectionFacade);
    const activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    expect(selection.selection().status).toBe(selected ? 'selected' : 'selectionRequired');
    expect(activeWorkspace.activeWorkspace()?.id ?? null).toBe(selected ? 'workspace-a' : null);
    // This spy calls the real production clearer/registry; it is not disabled.
    const workspaceClear = vi.spyOn(TestBed.inject(RealtimeFacade), 'clearForWorkspaceBoundary');
    openConversation(httpMock, facade, {}, 1, 'dm');
    openThread(httpMock, facade);
    facade.setThreadDraft('Actor A private thread draft');
    facade.selectThread('root-a');
    const pending = httpMock.expectOne('/api/messages/root-a/thread');
    refreshActor(httpMock, 'sessionContext', 'actor-b');
    TestBed.tick();
    const cancelledBeforeResponse = pending.cancelled;
    if (!cancelledBeforeResponse) {
      pending.flush(threadDto());
    }
    flushWorkspaceOptions(httpMock, 2);
    expect(workspaceClear).toHaveBeenCalledTimes(selected ? 1 : 0);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(facade.thread()).toMatchObject({ status: 'closed', draft: '', replies: [] });
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.page().messages).toEqual([]);
    expect(cancelledBeforeResponse).toBe(true);
    preferences.clear('mock-tenant', currentUserId);
    preferences.clear('mock-tenant', 'actor-b');
  });

  it.each(['currentUser', 'sessionContext'] as const)('redacts active protected state synchronously after same-Tenant actor replacement through %s', async (refresh) => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setDraft('Actor A main draft');
    facade.setThreadDraft('Actor A private thread draft');
    facade.setThreadWorkspaceExpanded(true);
    refreshActor(httpMock, refresh, 'actor-b');
    expect(TestBed.inject(AuthSessionFacade).currentTenant()?.tenantId).toBe('mock-tenant');
    expect(TestBed.inject(AuthSessionFacade).isAuthenticated()).toBe(true);
    // Read immediately, before Angular runs the identity observer.
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().draft).toBe('');
    expect(facade.page().conversation.mentionCandidates ?? []).toEqual([]);
    expect(facade.thread()).toMatchObject({ status: 'closed', rootMessageId: null, draft: '', replies: [] });
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.threadWorkspaceExpanded()).toBe(false);
    TestBed.tick();
    expect(sessionStorage.length).toBe(0);
  });

  it.each(['currentUser', 'sessionContext'] as const)('rejects a pending Thread GET response after actor replacement through %s before observer execution', async (refresh) => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Actor A private thread draft');
    facade.selectThread('root-a');
    const pending = httpMock.expectOne('/api/messages/root-a/thread');
    refreshActor(httpMock, refresh, 'actor-b');
    pending.flush(threadDto());
    expect(facade.thread()).toMatchObject({ status: 'closed', rootMessageId: null, draft: '', replies: [] });
    expect(facade.page().messages).toEqual([]);
    expect(facade.threadTabs()).toEqual([]);
    TestBed.tick();
    expect(sessionStorage.length).toBe(0);
  });

  it.each(['currentUser', 'sessionContext'] as const)('blocks old-actor Thread and main-composer dispatch before the %s observer executes', async (refresh) => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setDraft('Actor A main draft');
    facade.setThreadDraft('Actor A private thread draft');
    refreshActor(httpMock, refresh, 'actor-b');
    facade.sendThreadDraft();
    facade.sendDraft();
    facade.setThreadDraft('Late input from the old composer');
    httpMock.expectNone((request) => request.method === 'POST');
    expect(facade.thread().draft).toBe('');
    expect(facade.page().draft).toBe('');
    expect(sessionStorage.length).toBe(0);
  });

  it('cancels pending GET and POST operations at a same-Tenant actor boundary without a committed Workspace', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Actor A dispatched reply');
    facade.sendThreadDraft();
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    facade.selectThread('root-a');
    const get = httpMock.expectOne('/api/messages/root-a/thread');
    refreshActor(httpMock, 'sessionContext', 'actor-b');
    TestBed.tick();
    expect(post.cancelled).toBe(true);
    expect(get.cancelled).toBe(true);
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.page().messages).toEqual([]);
  });

  it('rejects a prior-actor send completion before the identity observer runs', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Actor A dispatched reply');
    facade.sendThreadDraft();
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    refreshActor(httpMock, 'currentUser', 'actor-b');
    post.flush({ message: threadReply('reply-sent'), summary: threadSummary(2, ['Mock User B']) });
    expect(facade.thread()).toMatchObject({ status: 'closed', draft: '', replies: [] });
    expect(facade.page().messages).toEqual([]);
    expect(facade.threadTabs()).toEqual([]);
  });

  it('allows the replacement actor to open a freshly authorized conversation without the previous actor draft', async () => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Actor A private thread draft');
    refreshActor(httpMock, 'currentUser', 'actor-b');
    facade.loadConversation('conversation-a', 'dm');
    httpMock.expectOne('/api/conversations').flush({ items: [] });
    httpMock.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'DirectMessage',
      members: [{ userId: 'actor-b', canRead: true, canPost: true, canCreateThread: true }]
    });
    httpMock.expectOne('/api/conversations/conversation-a/messages').flush({ items: [rootMessage()] });
    openThread(httpMock, facade);
    expect(facade.thread()).toMatchObject({ status: 'ready', draft: '' });
    expect(facade.threadTabs()[0].identity.userId).toBe('actor-b');
    facade.setThreadDraft('Actor B new draft');
    expect(facade.thread().draft).toBe('Actor B new draft');
    expect(JSON.stringify(facade.page())).not.toContain('Actor A private');
  });

  it.each(['currentUser', 'sessionContext'] as const)('preserves legitimate same-actor drafts and a pending GET through %s refresh', async (refresh) => {
    const { httpMock, facade } = await configureFacade();
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Same actor draft');
    facade.selectThread('root-a');
    const get = httpMock.expectOne('/api/messages/root-a/thread');
    refreshActor(httpMock, refresh, currentUserId);
    TestBed.tick();
    expect(get.cancelled).toBe(false);
    get.flush(threadDto());
    expect(facade.thread()).toMatchObject({ status: 'ready', draft: 'Same actor draft' });
    expect(facade.threadTabs()).toHaveLength(1);
  });

  it.each([
    ['initial', 400], ['initial', 401], ['initial', 403], ['initial', 404],
    ['refresh', 400], ['refresh', 401], ['refresh', 403], ['refresh', 404]
  ] as const)('purges protected state for normalized %s Thread GET status %s through the real interceptor', async (phase, status) => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events, { interceptErrors: true });
    if (status === 401) {
      vi.spyOn(TestBed.inject(AuthSessionFacade), 'refreshCurrentUser').mockReturnValue(of(DEFAULT_AUTH_SESSION));
    }
    openConversation(httpMock, facade);
    facade.setDraft('Protected main draft');
    if (phase === 'refresh') {
      openThread(httpMock, facade);
      facade.setThreadDraft('Protected reply draft');
      events.next(realtimeEvent('Messaging.ThreadChanged.v1', { conversationId: 'conversation-a', threadRootMessageId: 'root-a' }));
    } else {
      facade.openThread('root-a', 'trigger-a');
    }
    httpMock.expectOne('/api/messages/root-a/thread').flush({ error: 'Thread unavailable' }, { status, statusText: 'Denied' });
    expect(facade.thread()).toMatchObject({ status: 'permissionDenied', rootMessageId: null, draft: '', replies: [] });
    expect(facade.threadTabs()).toEqual([]);
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().draft).toBe('');
    expect(facade.page().conversation.mentionCandidates ?? []).toEqual([]);
    expect(facade.messageAction().messageId).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });

  it.each([401, 403, 404])('purges protected state for normalized Thread POST status %s through the real interceptor', async (status) => {
    const { httpMock, facade } = await configureFacade(undefined, { interceptErrors: true });
    if (status === 401) {
      vi.spyOn(TestBed.inject(AuthSessionFacade), 'refreshCurrentUser').mockReturnValue(of(DEFAULT_AUTH_SESSION));
    }
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setDraft('Protected main draft');
    facade.setThreadDraft('Protected reply draft');
    facade.sendThreadDraft();
    httpMock.expectOne('/api/security/csrf-token').flush({ token: 'synthetic-thread-csrf', headerName: 'X-CSRF-Token' });
    httpMock.expectOne('/api/messages/root-a/thread/messages').flush({ error: 'Thread unavailable' }, { status, statusText: 'Denied' });
    expect(facade.thread()).toMatchObject({ status: 'permissionDenied', rootMessageId: null, draft: '', replies: [] });
    expect(facade.page().messages).toEqual([]);
    expect(facade.page().draft).toBe('');
    expect(facade.threadTabs()).toEqual([]);
    expect(sessionStorage.length).toBe(0);
  });

  it('retains the draft/retry key for a normalized POST 400 and purges only after its GET reauthorization is denied', async () => {
    const { httpMock, facade } = await configureFacade(undefined, { interceptErrors: true });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Validation draft');
    facade.sendThreadDraft();
    httpMock.expectOne('/api/security/csrf-token').flush({ token: 'synthetic-thread-csrf', headerName: 'X-CSRF-Token' });
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    post.flush({ error: 'Reply rejected' }, { status: 400, statusText: 'Bad Request' });
    expect(facade.thread()).toMatchObject({ status: 'ready', draft: 'Validation draft', sending: false, pendingClientRequestId: post.request.body.clientRequestId });
    expect(facade.thread().error).toContain('rejected');
    httpMock.expectOne('/api/messages/root-a/thread').flush({ error: 'Thread unavailable' }, { status: 403, statusText: 'Forbidden' });
    expect(facade.thread()).toMatchObject({ status: 'permissionDenied', rootMessageId: null, draft: '', replies: [] });
    expect(facade.page().messages).toEqual([]);
  });

  it.each([0, 503])('retains the draft and same-key retry after a normalized transport status %s', async (status) => {
    const { httpMock, facade } = await configureFacade(undefined, { interceptErrors: true });
    openConversation(httpMock, facade);
    openThread(httpMock, facade);
    facade.setThreadDraft('Transport retry');
    facade.sendThreadDraft();
    httpMock.expectOne('/api/security/csrf-token').flush({ token: 'synthetic-thread-csrf', headerName: 'X-CSRF-Token' });
    const post = httpMock.expectOne('/api/messages/root-a/thread/messages');
    if (status === 0) {
      post.error(new ProgressEvent('error'));
    } else {
      post.flush({}, { status, statusText: 'Unavailable' });
    }
    expect(facade.thread()).toMatchObject({ status: 'ready', draft: 'Transport retry', sending: false, pendingClientRequestId: post.request.body.clientRequestId });
    facade.sendThreadDraft();
    const retry = httpMock.expectOne('/api/messages/root-a/thread/messages');
    expect(retry.request.body.clientRequestId).toBe(post.request.body.clientRequestId);
    retry.flush({ message: threadReply('reply-sent'), summary: threadSummary(2, ['Mock User B']) });
    expect(facade.thread().draft).toBe('');
  });

  it.each([
    { locale: 'en' as const, tab: 'Thread 1', close: 'Close thread 2' },
    { locale: 'ja' as const, tab: 'スレッド 1', close: 'スレッド 2 を閉じる' }
  ])('renders translated tab numbers and valid tablist ownership in $locale', async ({ locale, tab, close }) => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    const fixture = TestBed.createComponent(ThreadWorkspaceComponent);
    TestBed.inject(I18nService).setLocale(locale);
    fixture.detectChanges();
    await Promise.resolve();
    const root = fixture.nativeElement as HTMLElement;
    const tablist = root.querySelector('[role="tablist"]')!;
    expect(Array.from(tablist.children).map((element) => element.getAttribute('role'))).toEqual(['tab', 'tab']);
    expect(tablist.querySelector('button button')).toBeNull();
    expect(root.querySelector('#thread-tab-root-a')?.textContent).toContain(`${tab} · Pinned parent body`);
    const closeControl = root.querySelector<HTMLButtonElement>('[data-testid="thread-tab-close-active"]')!;
    expect(closeControl.getAttribute('aria-label')).toBe(close);
    expect(tablist.contains(closeControl)).toBe(false);
    expect(tablist.textContent).not.toMatch(/[{}]/u);
    expect(closeControl.getAttribute('aria-label')).not.toMatch(/[{}]/u);
    closeControl.click();
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    fixture.detectChanges();
    await Promise.resolve();
    expect(facade.threadTabs().map((entry) => entry.identity.rootMessageId)).toEqual(['root-a']);
    expect(document.activeElement?.id).toBe('thread-tab-root-a');
  });

  it('shows only loaded root metadata with current and session-open states, then clears it on revocation', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    let clearState!: (reason: ProtectedStateClearReason) => void;
    const { httpMock, facade } = await configureFacade(events, { clearState: (callback) => { clearState = callback; } });
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.setThreadWorkspaceExpanded(true);
    const fixture = TestBed.createComponent(ThreadWorkspaceComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const current = root.querySelector<HTMLButtonElement>('[data-testid="thread-workspace-open-root-a"]')!;
    const unopened = root.querySelector<HTMLButtonElement>('[data-testid="thread-workspace-open-root-b"]')!;
    expect(current.getAttribute('aria-current')).toBe('true');
    expect(current.textContent).toContain('1 reply');
    expect(current.textContent).toContain('Open in this view');
    expect(current.querySelector('time')?.getAttribute('datetime')).toBe('2026-08-27T02:00:00Z');
    expect(unopened.getAttribute('aria-current')).toBeNull();
    expect(unopened.querySelector('time')).toBeNull();
    expect(unopened.textContent).not.toMatch(/0 replies|Open in this view/u);
    expect(root.textContent).toContain('not a complete thread directory');
    unopened.click();
    const pending = httpMock.expectOne('/api/messages/root-b/thread');
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="thread-root-message"]')).toBeNull();
    expect(root.querySelector('[data-testid="thread-workspace-open-root-b"]')?.getAttribute('aria-current')).toBe('true');
    pending.flush(otherThreadDto());
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="thread-workspace-open-root-b"]')?.textContent).toContain('Open in this view');
    clearState('session');
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid^="thread-workspace-open-"]')).toHaveLength(0);
    expect(root.textContent).not.toContain('Pinned parent body');
  });

  it('disconnects tab resize observation when protected thread tabs are cleared', async () => {
    const observe = vi.fn();
    const disconnect = vi.fn();
    vi.stubGlobal('ResizeObserver', class {
      observe = observe;
      disconnect = disconnect;
    });
    try {
      const events = new Subject<DurableRealtimeEvent>();
      let clearState!: (reason: ProtectedStateClearReason) => void;
      const { httpMock, facade } = await configureFacade(events, { clearState: (callback) => { clearState = callback; } });
      openConversation(httpMock, facade);
      openThread(httpMock, facade);
      const fixture = TestBed.createComponent(ThreadWorkspaceComponent);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(observe).toHaveBeenCalledWith(fixture.nativeElement.querySelector('[role="tablist"]'));
      clearState('session');
      fixture.detectChanges();
      await fixture.whenStable();
      expect(fixture.nativeElement.querySelector('[role="tablist"]')).toBeNull();
      expect(disconnect).toHaveBeenCalledTimes(1);
      fixture.destroy();
      expect(disconnect).toHaveBeenCalledTimes(1);
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('offers keyboard tabs, close-to-neighbor, loaded-root navigation, and honest local-draft status', async () => {
    const events = new Subject<DurableRealtimeEvent>();
    const { httpMock, facade } = await configureFacade(events);
    openConversation(httpMock, facade);
    addSecondRoot(events);
    openThread(httpMock, facade);
    facade.openThread('root-b', 'trigger-b');
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    const fixture = TestBed.createComponent(ThreadWorkspaceComponent);
    fixture.detectChanges();
    await Promise.resolve();
    const root = fixture.nativeElement as HTMLElement;
    const tabB = root.querySelector<HTMLButtonElement>('#thread-tab-root-b')!;
    tabB.focus();
    tabB.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true, cancelable: true }));
    httpMock.expectOne('/api/messages/root-a/thread').flush(threadDto());
    fixture.detectChanges();
    await Promise.resolve();
    expect(root.querySelector('#thread-tab-root-a')?.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement?.id).toBe('thread-tab-root-a');
    root.querySelector<HTMLButtonElement>('[data-testid="thread-workspace-toggle"]')!.click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="thread-workspace-open-root-b"]')).not.toBeNull();
    expect(root.textContent).toContain('not saved to the server or synchronized');
    root.querySelector('#thread-tab-root-a')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true, cancelable: true }));
    httpMock.expectOne('/api/messages/root-b/thread').flush(otherThreadDto());
    fixture.detectChanges();
    await Promise.resolve();
    expect(facade.threadTabs().map((tab) => tab.identity.rootMessageId)).toEqual(['root-b']);
    expect(document.activeElement?.id).toBe('thread-tab-root-b');
  });
});

function flushWorkspaceOptions(httpMock: HttpTestingController, count: 1 | 2): void {
  httpMock.expectOne('/api/workspaces/capabilities').flush({ requestId: 'synthetic-capabilities', data: { canCreate: false }, warnings: [] });
  httpMock.expectOne('/api/workspaces').flush(['workspace-a', 'workspace-b'].slice(0, count).map((id) => ({
    id, name: id, canOpenWorkspace: true, currentUserRole: 'Member', accessSource: 'WorkspaceMembership'
  })));
}

function refreshActor(httpMock: HttpTestingController, refresh: 'currentUser' | 'sessionContext' | 'currentTenant', userId: string): void {
  const auth = TestBed.inject(AuthSessionFacade);
  if (refresh === 'currentTenant') {
    auth.refreshCurrentTenant().subscribe();
    httpMock.expectOne('/api/tenants/current').flush({ ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId: userId });
    expect(auth.currentTenant()?.tenantId).toBe(userId);
    return;
  }
  const user = { userId, displayName: 'Refreshed actor', systemRole: 'TenantUser', status: 'Active', capabilities: [], workspaces: [] };
  if (refresh === 'currentUser') {
    auth.refreshCurrentUser().subscribe();
    httpMock.expectOne('/api/auth/me').flush(user);
  } else {
    auth.refreshSessionContext().subscribe();
    httpMock.expectOne('/api/auth/status').flush({ isAuthenticated: true, user });
  }
  expect(auth.currentUser()?.userId).toBe(userId);
}

function addSecondRoot(events: Subject<DurableRealtimeEvent>): void {
  events.next(realtimeEvent('Messaging.MessageCreated.v1', {
    conversationId: 'conversation-a', message: { ...rootMessage(), id: 'root-b' }
  }));
}

function otherThreadDto(): MessageThreadDto {
  return threadDto({
    rootMessage: { ...rootMessage(), id: 'root-b' },
    replies: [{ ...threadReply('reply-b'), threadRootMessageId: 'root-b' }],
    summary: { ...threadSummary(1, ['Mock User B']), threadRootMessageId: 'root-b' }
  });
}

async function configureFacade(events = new Subject<DurableRealtimeEvent>(), options: {
  catchUp?: (callback: () => Promise<void> | void) => void;
  clearState?: (callback: (reason: ProtectedStateClearReason) => void) => void;
  interceptErrors?: boolean;
  realWorkspaceLifecycle?: boolean;
} = {}): Promise<{
  readonly httpMock: HttpTestingController;
  readonly facade: MessagingFacade;
}> {
  await TestBed.configureTestingModule({
    imports: [ThreadWorkspaceComponent],
    providers: [
      provideHttpClient(withInterceptors(options.interceptErrors ? [authSessionInterceptor] : [])),
      provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      {
        provide: FrontendFeatureFlagsService,
        useValue: {
          realtimeSignalREnabled: () => !!options.catchUp,
          optimisticMessagingEnabled: () => true
        }
      },
      ...(options.realWorkspaceLifecycle ? [] : [{
        provide: RealtimeFacade,
        useValue: {
          durableEvents$: events.asObservable(),
          registerProtectedStateClearer: (_owner: string, callback: (reason: ProtectedStateClearReason) => void) => {
            options.clearState?.(callback);
            return () => undefined;
          },
          registerSubscription: () => () => undefined,
          registerCatchUp: (_owner: string, callback: () => Promise<void> | void) => {
            options.catchUp?.(callback);
            return () => undefined;
          }
        }
      }])
    ]
  }).compileComponents();
  return {
    httpMock: TestBed.inject(HttpTestingController),
    facade: TestBed.inject(MessagingFacade)
  };
}

function openConversation(
  httpMock: HttpTestingController,
  facade: MessagingFacade,
  rootOverrides: Record<string, unknown> = {},
  replyCount = 1,
  routeKind: 'channel' | 'dm' = 'channel'
): void {
  if (routeKind === 'dm') {
    facade.loadConversation('conversation-a', 'dm');
  } else {
    facade.loadConversation('conversation-a', 'channel', 'workspace-a');
  }
  httpMock.expectOne('/api/conversations').flush({ items: [] });
  httpMock.expectOne('/api/conversations/conversation-a').flush({
    id: 'conversation-a',
    workspaceId: 'workspace-a',
    type: routeKind === 'dm' ? 'DirectMessage' : 'ProjectChannel',
    title: 'General',
    isLocked: false,
    isArchived: false,
    members: [{
      userId: currentUserId,
      displayName: 'Mock User A',
      canRead: true,
      canPost: true,
      canCreateThread: true,
      removedAt: null,
      leftAt: null
    }]
  });
  httpMock.expectOne('/api/conversations/conversation-a/messages').flush({
    items: [{
      ...rootMessage(),
      ...rootOverrides,
      thread: replyCount > 0 ? threadSummary(replyCount, ['Mock User B']) : undefined
    }]
  });
}

function flushCaughtUpConversation(httpMock: HttpTestingController): void {
  httpMock.expectOne('/api/conversations').flush({ items: [] });
  httpMock.expectOne('/api/conversations/conversation-a').flush({
    id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel', title: 'General',
    members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }]
  });
  httpMock.expectOne('/api/conversations/conversation-a/messages').flush({ items: [rootMessage()] });
}

function openThread(
  httpMock: HttpTestingController,
  facade: MessagingFacade,
  triggerElementId = 'thread-trigger-root-a'
): void {
  facade.openThread('root-a', triggerElementId);
  const request = httpMock.expectOne('/api/messages/root-a/thread');
  expect(request.request.method).toBe('GET');
  expect(request.request.withCredentials).toBe(true);
  request.flush(threadDto());
}

function threadDto(overrides: Partial<MessageThreadDto> = {}): MessageThreadDto {
  return {
    rootMessage: rootMessage(),
    replies: [threadReply('reply-a')],
    summary: threadSummary(1, ['Mock User B']),
    hasMore: false,
    maximumReplies: 100,
    ...overrides
  };
}

function rootMessage(): Record<string, unknown> {
  return {
    id: 'root-a',
    workspaceId: 'workspace-a',
    conversationId: 'conversation-a',
    authorUserId: 'mock-user-b',
    authorDisplayName: 'Mock User B',
    body: 'Pinned parent body',
    attachments: [],
    createdAt: '2026-08-27T01:00:00Z',
    isDeleted: false,
    version: 1
  };
}

function deletedRootMessage(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    ...rootMessage(),
    body: '',
    attachments: [],
    isDeleted: true,
    version: 2,
    ...overrides
  };
}

function threadReply(
  id: string,
  body = 'Existing reply',
  clientRequestId?: string
): Record<string, unknown> {
  return {
    id,
    workspaceId: 'workspace-a',
    conversationId: 'conversation-a',
    authorUserId: 'mock-user-b',
    authorDisplayName: 'Mock User B',
    body,
    attachments: [],
    createdAt: '2026-08-27T02:00:00Z',
    isDeleted: false,
    version: 1,
    clientRequestId,
    threadRootMessageId: 'root-a'
  };
}

function threadSummary(replyCount: number, participantDisplayNames: readonly string[]): Record<string, unknown> {
  return {
    threadRootMessageId: 'root-a',
    replyCount,
    latestReplyAt: replyCount > 0 ? '2026-08-27T02:00:00Z' : null,
    participantDisplayNames
  };
}

function realtimeEvent(
  eventType: DurableRealtimeEvent['eventType'],
  payload: Record<string, unknown>
): DurableRealtimeEvent {
  return {
    eventId: `event-${eventType}-${Math.random()}`,
    eventType,
    payloadSchemaVersion: 1,
    occurredAt: '2026-08-27T03:00:00Z',
    tenantId: TestBed.inject(AuthSessionFacade).currentTenant()!.tenantId,
    aggregateType: 'Message',
    aggregateId: 'root-a',
    aggregateVersion: 1,
    actor: { actorType: 'User', actorId: 'mock-user-b' },
    correlationId: null,
    causationId: null,
    payload
  };
}

function threadViewModel(): MessagingThreadViewModel {
  return {
    status: 'ready',
    rootMessageId: 'root-a',
    rootMessage: messageViewModel('root-a', 'Pinned parent body'),
    replies: [messageViewModel('reply-a', 'Existing reply', 'root-a')],
    summary: {
      threadRootMessageId: 'root-a',
      replyCount: 101,
      latestReplyAt: '2026-08-27T02:00:00Z',
      participantDisplayNames: ['Mock User B']
    },
    hasMore: true,
    maximumReplies: 100,
    draft: 'Thread draft',
    sending: false
  };
}

function messageViewModel(
  id: string,
  body: string,
  threadRootMessageId?: string
): MessagingMessageViewModel {
  return {
    id,
    authorLabel: 'Mock User B',
    authorRoleLabel: 'member',
    isOwnMessage: false,
    body,
    isDeleted: false,
    createdAt: '2026-08-27T01:00:00Z',
    sentAtLabel: '8/27/2026, 10:00:00 AM',
    deliveryState: 'confirmed',
    retryAllowed: false,
    threadRootMessageId
  };
}

async function renderRouteThread<T>(
  component: Type<T>,
  routeKind: 'channel' | 'dm',
  replyCount: number,
  isDeleted = false,
  isOwnMessage = false
): Promise<ComponentFixture<T>> {
  const events = new Subject<DurableRealtimeEvent>();
  const routeParams = routeKind === 'channel'
    ? { workspaceId: 'workspace-a', conversationId: 'conversation-a' }
    : { conversationId: 'conversation-a' };
  await TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: COGLATAS_MESSAGING_PAGE_MOCK, useValue: routePage(routeKind, replyCount, isDeleted, isOwnMessage) },
      {
        provide: ActivatedRoute,
        useValue: { paramMap: of(convertToParamMap(routeParams)) }
      },
      {
        provide: FrontendFeatureFlagsService,
        useValue: {
          realtimeSignalREnabled: () => false,
          optimisticMessagingEnabled: () => true
        }
      },
      {
        provide: RealtimeFacade,
        useValue: {
          connectionState: signal('Connected'),
          durableEvents$: events.asObservable(),
          registerProtectedStateClearer: () => () => undefined,
          registerSubscription: () => () => undefined,
          registerCatchUp: () => () => undefined
        }
      }
    ]
  }).compileComponents();
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return fixture;
}

function routePage(
  routeKind: 'channel' | 'dm',
  replyCount: number,
  isDeleted = false,
  isOwnMessage = false
): MessagingPageViewModel {
  return {
    routeKind,
    status: 'ready',
    title: routeKind === 'dm' ? 'Thread DM' : 'Thread channel',
    conversation: {
      id: 'conversation-a',
      kind: routeKind,
      tenantId: 'tenant-a',
      workspaceId: 'workspace-a',
      title: routeKind === 'dm' ? 'Thread DM' : 'Thread channel',
      subtitle: 'Thread route test',
      viewerIsParticipant: true,
      viewerWasRemoved: false,
      capabilities: ['readBody', 'postMessage', 'createThread'],
      mentionCandidates: [],
      attachment: { mode: 'disabled', label: 'Attachments disabled.' }
    },
    conversations: [],
    messages: [{
      ...messageViewModel('root-a', isDeleted ? '' : 'Pinned parent body'),
      isOwnMessage,
      isDeleted,
      thread: replyCount > 0
        ? {
            threadRootMessageId: 'root-a',
            replyCount,
            latestReplyAt: '2026-08-27T02:00:00Z',
            participantDisplayNames: ['Mock User B']
          }
        : undefined
    }],
    draft: '',
    sending: false,
    sendState: { status: 'idle' },
    hasNewMessagesWhileReading: false,
    readCursorBehavior: 'latestVisibleMessage',
    pagingWindow: { visibleMessageIds: ['root-a'], preloadBefore: 0, preloadAfter: 0 }
  };
}
