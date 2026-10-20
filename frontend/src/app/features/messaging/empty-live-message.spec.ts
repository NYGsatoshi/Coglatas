import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { ChannelMessagingPageComponent } from './channel-messaging-page/channel-messaging-page.component';
import { DmPageComponent } from './dm-page/dm-page.component';
import { MessagingFacade } from './messaging.facade';

const conversationId = 'conversation-a';
const userId = DEFAULT_AUTH_SESSION.currentUser!.userId;
type Boundary = 'session' | 'tenant' | 'authorization' | 'workspace';

async function openEmpty(kind: 'Group' | 'DirectMessage' | 'WorkspaceChannel' = 'Group') {
  const events = new Subject<DurableRealtimeEvent>();
  let clear: (reason: Boundary) => void = () => undefined;
  await TestBed.configureTestingModule({
    imports: [kind === 'WorkspaceChannel' ? ChannelMessagingPageComponent : DmPageComponent],
    providers: [
      provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: ActivatedRoute, useValue: {
        snapshot: { queryParamMap: convertToParamMap({}) },
        paramMap: of(convertToParamMap({ conversationId, workspaceId: 'workspace-a' })),
      } },
      { provide: FrontendFeatureFlagsService, useValue: {
        realtimeSignalREnabled: () => false, optimisticMessagingEnabled: () => true,
      } },
      { provide: RealtimeFacade, useValue: {
        connectionState: signal('Connected'), durableEvents$: events.asObservable(),
        registerProtectedStateClearer: (_owner: string, callback: (reason: Boundary) => void) => {
          clear = callback;
          return () => undefined;
        },
      } },
    ],
  }).compileComponents();
  const fixture = kind === 'WorkspaceChannel' ? TestBed.createComponent(ChannelMessagingPageComponent)
    : TestBed.createComponent(DmPageComponent);
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/conversations').flush({ items: [] });
  http.expectOne(`/api/conversations/${conversationId}`).flush({
    id: conversationId, workspaceId: 'workspace-a', type: kind, title: 'Empty current conversation',
    projectId: null, parentConversationId: null, rootConversationId: null,
    members: [{ userId, displayName: 'Current participant', role: 1, canRead: true, canPost: true,
      canCreateThread: false, leftAt: null, removedAt: null }],
  });
  http.expectOne(`/api/conversations/${conversationId}/messages`).flush({ items: [] });
  fixture.detectChanges();
  return { fixture, http, events, clear: (reason: Boundary) => clear(reason), facade: TestBed.inject(MessagingFacade) };
}

function createdEvent(overrides: { tenantId?: string; conversationId?: string } = {}): DurableRealtimeEvent {
  return {
    eventId: 'created-message-event', eventType: 'Messaging.MessageCreated.v1', payloadSchemaVersion: 1,
    occurredAt: '2026-10-20T03:59:22Z', tenantId: overrides.tenantId ?? DEFAULT_AUTH_SESSION.currentTenant!.tenantId,
    aggregateType: 'Message', aggregateId: 'first-live-message', aggregateVersion: 1,
    actor: { actorType: 'User', actorId: 'other-participant' }, correlationId: null, causationId: null,
    payload: { conversationId: overrides.conversationId ?? conversationId, message: {
      id: 'first-live-message', conversationId: overrides.conversationId ?? conversationId,
      sender: { userId: 'other-participant', displayName: 'Other participant' },
      body: 'First incoming live message', createdAt: '2026-10-20T03:59:22Z', threadRootMessageId: null, version: 1,
    } },
  };
}

describe('First live Message in an empty authorized conversation', () => {
  beforeEach(() => { localStorage.setItem('coglatas.locale', 'en'); });
  afterEach(() => {
    TestBed.inject(HttpTestingController, null)?.verify();
    TestBed.resetTestingModule();
    sessionStorage.clear();
    localStorage.removeItem('coglatas.locale');
  });

  it.each(['Group', 'DirectMessage', 'WorkspaceChannel'] as const)(
    'renders the first incoming %s Message without an HTTP refresh or sender action', async (kind) => {
      const { fixture, http, events, facade } = await openEmpty(kind);
      const root = fixture.nativeElement as HTMLElement;
      expect(facade.page().status).toBe('empty');
      expect(root.querySelector('[data-testid="no-messages-state"]')).not.toBeNull();
      events.next(createdEvent());
      fixture.detectChanges();
      expect(facade.page().messages.map(message => message.id)).toEqual(['first-live-message']);
      expect(root.querySelector('[data-testid="confirmed-message"]')?.textContent).toContain('First incoming live message');
      expect(root.querySelector('[data-testid="no-messages-state"]')).toBeNull();
      expect(facade.page().status).toBe('ready');
      events.next(createdEvent());
      fixture.detectChanges();
      expect(root.querySelectorAll('[data-testid="confirmed-message"]')).toHaveLength(1);
      http.expectNone(() => true);
    },
  );

  it.each([{ tenantId: 'other-tenant' }, { conversationId: 'other-conversation' }])(
    'keeps an empty Group hidden from an incoming wrong-scope event %j', async (scope) => {
      const { fixture, http, events, facade } = await openEmpty();
      events.next(createdEvent(scope));
      fixture.detectChanges();
      expect(facade.page().status).toBe('empty');
      expect(facade.page().messages).toEqual([]);
      expect(fixture.nativeElement.querySelectorAll('[data-testid="confirmed-message"]')).toHaveLength(0);
      http.expectNone(() => true);
    },
  );

  it.each([400, 401, 403, 404])('does not revive a Group body after a current HTTP %s denial', async (status) => {
    const { fixture, http, events, facade } = await openEmpty();
    facade.loadConversation('denied-conversation', 'dm', null);
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne('/api/conversations/denied-conversation').flush({}, { status, statusText: 'Denied' });
    events.next(createdEvent({ conversationId: 'denied-conversation' }));
    events.next(createdEvent());
    fixture.detectChanges();
    expect(facade.page().status).toBe('permissionDenied');
    expect(facade.page().messages).toEqual([]);
    expect(fixture.nativeElement.querySelectorAll('[data-testid="confirmed-message"]')).toHaveLength(0);
    http.expectNone(() => true);
  });

  it.each(['session', 'tenant', 'authorization', 'workspace'] as const)(
    'ignores a late first Message after the %s boundary clears its protected projection', async (reason) => {
      const { fixture, http, events, clear, facade } = await openEmpty();
      clear(reason);
      events.next(createdEvent());
      fixture.detectChanges();
      expect(facade.page().status).toBe('loading');
      expect(facade.page().conversation.id).toBe('');
      expect(facade.page().messages).toEqual([]);
      expect(fixture.nativeElement.querySelectorAll('[data-testid="confirmed-message"]')).toHaveLength(0);
      http.expectNone(() => true);
    },
  );
});
