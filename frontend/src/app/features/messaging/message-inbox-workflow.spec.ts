import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { MessagingFacade } from './messaging.facade';

const conversationId = '11111111-1111-4111-8111-111111111111';

describe('Issue #355 authoritative Message inbox workflow', () => {
  let facade: MessagingFacade;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    const events = new Subject<DurableRealtimeEvent>();
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
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
            durableEvents$: events.asObservable(),
            registerProtectedStateClearer: () => () => undefined,
            registerSubscription: () => () => undefined,
            registerCatchUp: () => () => undefined
          }
        }
      ]
    }).compileComponents();
    facade = TestBed.inject(MessagingFacade);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('uses exact server counts and server-filtered rows for mutually exclusive views', () => {
    facade.loadConversationListPage();
    const initial = httpMock.expectOne('/api/conversations');
    expect(initial.request.params.keys()).toEqual([]);
    initial.flush(inboxResponse('All', false, { all: 5, unread: 2, mentions: 1, later: 3 }));

    expect(facade.inbox()).toEqual({
      view: 'All',
      counts: { all: 5, unread: 2, mentions: 1, later: 3 },
      status: 'ready'
    });
    expect(facade.page().conversations).toHaveLength(1);

    facade.selectInboxView('Unread');
    expect(facade.inbox()).toMatchObject({ view: 'All', requestedView: 'Unread', status: 'loading' });
    expect(facade.page().conversations).toHaveLength(1);
    const unread = httpMock.expectOne(
      (request) => request.url === '/api/conversations' && request.params.get('view') === 'Unread'
    );
    unread.flush(inboxResponse('Unread', false, { all: 5, unread: 1, mentions: 1, later: 3 }));

    expect(facade.inbox()).toEqual({
      view: 'Unread',
      counts: { all: 5, unread: 1, mentions: 1, later: 3 },
      status: 'ready'
    });
    expect(facade.page().conversations[0].isLater).toBe(false);
  });

  it('changes only private Later state and refreshes the authoritative selected view', () => {
    loadInitialInbox();

    facade.setConversationLater(conversationId, true);
    const mutation = httpMock.expectOne(`/api/conversations/${conversationId}/state`);
    expect(mutation.request.method).toBe('PATCH');
    expect(mutation.request.body).toEqual({ isLater: true });
    expect(facade.inbox().laterPendingConversationId).toBe(conversationId);
    mutation.flush({ conversationId, isLater: true });

    const refresh = httpMock.expectOne('/api/conversations');
    refresh.flush(inboxResponse('All', true, { all: 1, unread: 1, mentions: 1, later: 1 }));

    expect(facade.inbox()).toEqual({
      view: 'All',
      counts: { all: 1, unread: 1, mentions: 1, later: 1 },
      status: 'ready'
    });
    expect(facade.page().conversations[0]).toMatchObject({ id: conversationId, isLater: true, unreadCount: 2 });
  });

  it('clears protected rows and revalidates after a participant-state failure', () => {
    loadInitialInbox();

    facade.setConversationLater(conversationId, true);
    httpMock
      .expectOne(`/api/conversations/${conversationId}/state`)
      .flush({ detail: 'private title and membership' }, { status: 400, statusText: 'Bad Request' });

    expect(facade.page().conversations).toEqual([]);
    expect(facade.page().status).toBe('loading');
    const revalidation = httpMock.expectOne('/api/conversations');
    revalidation.flush({}, { status: 403, statusText: 'Forbidden' });
    expect(facade.page()).toMatchObject({ status: 'permissionDenied', conversations: [] });
    expect(JSON.stringify(facade.inbox())).not.toContain('private title');
  });

  function loadInitialInbox(): void {
    facade.loadConversationListPage();
    httpMock
      .expectOne('/api/conversations')
      .flush(inboxResponse('All', false, { all: 1, unread: 1, mentions: 1, later: 0 }));
  }
});

function inboxResponse(
  view: 'All' | 'Unread' | 'Mentions' | 'Later',
  isLater: boolean,
  counts: { all: number; unread: number; mentions: number; later: number }
): Record<string, unknown> {
  return {
    items: [{
      id: conversationId,
      workspaceId: '22222222-2222-4222-8222-222222222222',
      type: 'ProjectChannel',
      title: 'Authorized conversation',
      unreadCount: 2,
      hasMention: true,
      isLater,
      createdAt: '2026-08-29T00:00:00Z'
    }],
    page: 1,
    pageSize: 20,
    totalCount: 1,
    view,
    counts
  };
}
