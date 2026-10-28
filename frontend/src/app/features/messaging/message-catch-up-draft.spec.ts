import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';

import { AuthSessionFacade, COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { DmPageComponent } from './dm-page/dm-page.component';
import { DraftStorageService } from './draft-storage.service';
import { MessagingFacade } from './messaging.facade';

const currentUserId = DEFAULT_AUTH_SESSION.currentUser!.userId;

describe('Routine catch-up drafts without optional persistence', () => {
  let http: HttpTestingController;
  let facade: MessagingFacade;
  let fixture: ComponentFixture<DmPageComponent>;
  let catchUp: () => Promise<void> | void;
  let clearState: (reason: ProtectedStateClearReason) => void;

  afterEach(() => {
    http?.verify();
    fixture?.destroy();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  async function configure(storage: 'unavailable' | 'throwing'): Promise<void> {
    const events = new Subject<DurableRealtimeEvent>();
    await TestBed.configureTestingModule({
      imports: [DmPageComponent],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: ActivatedRoute, useValue: {
          paramMap: of(convertToParamMap({ conversationId: 'conversation-a' })),
          snapshot: { queryParamMap: convertToParamMap({}) },
        } },
        { provide: FrontendFeatureFlagsService, useValue: {
          optimisticMessagingEnabled: () => true,
          realtimeSignalREnabled: () => true,
        } },
        { provide: RealtimeFacade, useValue: {
          durableEvents$: events.asObservable(), connectionState: () => 'Connected',
          registerCatchUp: (_owner: string, callback: typeof catchUp) => {
            catchUp = callback;
            return () => undefined;
          },
          registerProtectedStateClearer: (_owner: string, callback: typeof clearState) => {
            clearState = callback;
            return () => undefined;
          },
          registerSubscription: () => () => undefined,
        } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    facade = TestBed.inject(MessagingFacade);
    if (storage === 'unavailable') {
      vi.stubGlobal('sessionStorage', undefined);
    } else {
      for (const method of ['getItem', 'setItem', 'removeItem'] as const) {
        vi.spyOn(Storage.prototype, method).mockImplementation(() => {
          throw new DOMException('Optional storage denied', 'SecurityError');
        });
      }
    }
    fixture = TestBed.createComponent(DmPageComponent);
    flushConversation();
    fixture.detectChanges();
  }

  function flushConversation(id = 'conversation-a', workspaceId = 'workspace-a'): void {
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne(`/api/conversations/${id}`).flush({
      id, workspaceId, type: 'DirectMessage', title: 'Authorized DM',
      isLocked: false, isArchived: false,
      members: [{ userId: currentUserId, canRead: true, canPost: true, canCreateThread: true }],
      createdAt: '2026-10-20T00:00:00Z',
    });
    http.expectOne(`/api/conversations/${id}/messages`).flush({ items: [] });
  }

  function composer(): HTMLTextAreaElement {
    const textarea = fixture.nativeElement.querySelector('[data-testid="message-draft"]');
    expect(textarea).not.toBeNull();
    return textarea as HTMLTextAreaElement;
  }

  function input(textarea: HTMLTextAreaElement, value: string): void {
    textarea.value = value;
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
  }

  it('retains the current editable DOM input during a pending registered routine catch-up', async () => {
    await configure('unavailable');
    const textarea = composer();
    input(textarea, 'Current-source local draft');
    textarea.focus();
    const completion = catchUp();
    fixture.detectChanges();
    try {
      expect(facade.page().status).toBe('loading');
      expect(facade.page().conversation.id).toBe('');
      expect(facade.page().messages).toEqual([]);
      expect(facade.page().conversation.capabilities).toEqual([]);
      http.expectNone((request) => request.method === 'POST');
      expect(textarea.isConnected).toBe(true);
      expect(composer()).toBe(textarea);
      expect(document.activeElement).toBe(textarea);
      expect(textarea.disabled).toBe(false);
      expect(fixture.nativeElement.querySelector('[data-testid="send-message"]').disabled).toBe(true);
      input(textarea, 'Current-source local continuation');
      http.expectNone((request) => request.method === 'POST');
    } finally {
      flushConversation();
      await completion;
      fixture.detectChanges();
    }
    expect(composer()).toBe(textarea);
    expect(composer().value).toBe('Current-source local continuation');
  });

  it.each(['unavailable', 'throwing'] as const)(
    'preserves an already rendered draft through authorized catch-up with %s storage', async (storage) => {
      await configure(storage);
      input(composer(), 'Local draft before catch-up');
      fixture.detectChanges();
      expect(composer().value).toBe('Local draft before catch-up');
      const completion = catchUp();
      fixture.detectChanges();
      expect(facade.page().status).toBe('loading');
      flushConversation();
      await completion;
      fixture.detectChanges();
      expect(facade.page().draft).toBe('Local draft before catch-up');
      expect(composer().value).toBe('Local draft before catch-up');
      http.expectNone((request) => request.method === 'POST');
    },
  );

  it.each(['unavailable', 'throwing'] as const)(
    'preserves queued rendered input while authorized catch-up is pending with %s storage', async (storage) => {
      await configure(storage);
      const textarea = composer();
      const completion = catchUp();
      expect(facade.page().status).toBe('loading');
      input(textarea, 'Input queued before the loading projection rendered');
      expect(facade.page().draft).toBe('Input queued before the loading projection rendered');
      fixture.detectChanges();
      flushConversation();
      await completion;
      fixture.detectChanges();
      expect(facade.page().draft).toBe('Input queued before the loading projection rendered');
      expect(composer().value).toBe('Input queued before the loading projection rendered');
      http.expectNone((request) => request.method === 'POST');
    },
  );

  it.each(['', 'Later edited draft'])(
    'honors the latest queued edit including an explicit clear: %j', async (latest) => {
      await configure('throwing');
      const textarea = composer();
      input(textarea, 'Original draft must not overwrite a later edit');
      const completion = catchUp();
      input(textarea, latest);
      flushConversation();
      await completion;
      fixture.detectChanges();
      expect(facade.page().draft).toBe(latest);
      expect(composer().value).toBe(latest);
      http.expectNone((request) => request.method === 'POST');
    },
  );

  it.each(['authorization', 'session', 'tenant', 'workspace'] as const)(
    'discards the transient draft and queued input at a %s boundary', async (reason) => {
      await configure('unavailable');
      const textarea = composer();
      input(textarea, 'Private original draft');
      const completion = catchUp();
      input(textarea, 'Private pending draft');
      const pending = http.match((request) => request.url.startsWith('/api/conversations'));
      expect(pending).toHaveLength(2);
      clearState(reason);
      const write = vi.spyOn(TestBed.inject(DraftStorageService), 'writeDraft');
      input(textarea, 'Stale input after the boundary');
      await completion;
      fixture.detectChanges();
      expect(pending.every((request) => request.cancelled)).toBe(true);
      expect(facade.page().draft).toBe('');
      expect(facade.page().messages).toEqual([]);
      expect(fixture.nativeElement.querySelector('[data-testid="message-draft"]')).toBeNull();
      expect(write).not.toHaveBeenCalled();
      http.expectNone((request) => request.method === 'POST');
    },
  );

  it.each([400, 401, 403, 404])(
    'discards the transient draft when fresh detail returns %s', async (status) => {
      await configure('unavailable');
      const textarea = composer();
      input(textarea, 'Private draft before a denied detail');
      const completion = catchUp();
      http.expectOne('/api/conversations').flush({ items: [] });
      http.expectOne('/api/conversations/conversation-a').flush({}, { status, statusText: 'Denied' });
      await completion;
      const write = vi.spyOn(TestBed.inject(DraftStorageService), 'writeDraft');
      input(textarea, 'Stale input after denied detail');
      fixture.detectChanges();
      expect(facade.page().draft).toBe('');
      expect(facade.page().messages).toEqual([]);
      expect(write).not.toHaveBeenCalled();
      http.expectNone('/api/conversations/conversation-a/messages');
      http.expectNone((request) => request.method === 'POST');
    },
  );

  it.each([400, 401, 403, 404])(
    'discards the transient draft when fresh messages return %s', async (status) => {
      await configure('unavailable');
      input(composer(), 'Private draft before a denied message read');
      const completion = catchUp();
      http.expectOne('/api/conversations').flush({ items: [] });
      http.expectOne('/api/conversations/conversation-a').flush({
        id: 'conversation-a', workspaceId: 'workspace-a', type: 'DirectMessage',
        members: [{ userId: currentUserId, canRead: true, canPost: true }],
      });
      http.expectOne('/api/conversations/conversation-a/messages').flush({}, { status, statusText: 'Denied' });
      await completion;
      fixture.detectChanges();
      expect(facade.page().status).toBe('permissionDenied');
      expect(facade.page().draft).toBe('');
      expect(facade.page().messages).toEqual([]);
      expect(fixture.nativeElement.querySelector('[data-testid="message-draft"]')).toBeNull();
      http.expectNone((request) => request.method === 'POST');
    },
  );

  it.each([401, 403])(
    'cancels pending detail and transient draft on list denial %s', async (status) => {
      await configure('unavailable');
      input(composer(), 'Private draft before list denial');
      const completion = catchUp();
      const detail = http.expectOne('/api/conversations/conversation-a');
      http.expectOne('/api/conversations').flush({}, { status, statusText: 'Denied' });
      await completion;
      fixture.detectChanges();
      expect(detail.cancelled).toBe(true);
      expect(facade.page().draft).toBe('');
      expect(facade.page().conversation.id).toBe('');
      expect(fixture.nativeElement.querySelector('[data-testid="message-draft"]')).toBeNull();
    },
  );

  it('does not move transient input into a different route or write a placeholder key', async () => {
    await configure('unavailable');
    const textarea = composer();
    input(textarea, 'Conversation A private draft');
    const completion = catchUp();
    const pendingA = http.match((request) => request.url.startsWith('/api/conversations'));
    facade.loadConversation('conversation-b', 'dm');
    const write = vi.spyOn(TestBed.inject(DraftStorageService), 'writeDraft');
    input(textarea, 'Stale A input after navigation');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="message-draft"]')).toBeNull();
    flushConversation('conversation-b');
    await completion;
    fixture.detectChanges();
    expect(pendingA.every((request) => request.cancelled)).toBe(true);
    expect(facade.page().conversation.id).toBe('conversation-b');
    expect(facade.page().draft).toBe('');
    expect(composer().value).toBe('');
    expect(write).not.toHaveBeenCalled();
  });

  it.each(['readDenied', 'removed'] as const)(
    'discards a transient draft when successful HTTP projects the actor as %s', async (state) => {
      await configure('throwing');
      const textarea = composer();
      input(textarea, 'Private draft before the fresh projection');
      fixture.detectChanges();
      const completion = catchUp();
      http.expectOne('/api/conversations').flush({ items: [] });
      http.expectOne('/api/conversations/conversation-a').flush({
        id: 'conversation-a', workspaceId: 'workspace-a', type: 'DirectMessage',
        members: [{ userId: currentUserId, canRead: state !== 'readDenied', canPost: true,
          removedAt: state === 'removed' ? '2026-10-20T01:00:00Z' : null }],
      });
      http.expectOne('/api/conversations/conversation-a/messages').flush({ items: [] });
      await completion;
      const write = vi.spyOn(TestBed.inject(DraftStorageService), 'writeDraft');
      input(textarea, 'Stale input after lost participation or read authority');
      fixture.detectChanges();
      expect(facade.page().draft).toBe('');
      expect(facade.page().conversation.capabilities).not.toContain('readBody');
      expect(write).not.toHaveBeenCalled();
      if (state === 'readDenied') {
        expect(composer().value).toBe('');
      }
      http.expectNone((request) => request.method === 'POST');
    },
  );

  it('does not restore the transient draft into a different Workspace projection', async () => {
    await configure('unavailable');
    const textarea = composer();
    const binding = facade.composerInput().binding;
    expect(binding).not.toBeNull();
    expect(fixture.componentInstance.canPost()).toBe(true);
    expect(textarea.disabled).toBe(false);
    input(textarea, 'Workspace A private draft');
    const completion = catchUp();
    fixture.detectChanges();
    expect(composer()).toBe(textarea);
    expect(composer().value).toBe('Workspace A private draft');
    expect(facade.composerInput().binding).toBe(binding);
    expect(facade.composerInput().retainLocalInput).toBe(true);
    expect(facade.page().conversation.id).toBe('');
    expect(facade.page().conversation.capabilities).toEqual([]);
    expect(facade.page().conversation.participants).toEqual([]);
    expect(facade.page().conversation.mentionCandidates).toEqual([]);
    expect(facade.page().messages).toEqual([]);
    expect(fixture.componentInstance.canPost()).toBe(false);
    expect(fixture.nativeElement.querySelector('[data-testid="send-message"]').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[data-testid="mention-candidate"]')).toBeNull();
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    facade.sendComposerDraft({ binding, mentionedUserIds: [] });
    http.expectNone((request) => request.method === 'POST');
    flushConversation('conversation-a', 'workspace-b');
    await completion;
    expect(facade.page().conversation.workspaceId).toBe('workspace-b');
    expect(facade.page().draft).toBe('');
    const write = vi.spyOn(TestBed.inject(DraftStorageService), 'writeDraft');
    expect(textarea.isConnected).toBe(true);
    expect(composer()).toBe(textarea);
    input(textarea, 'Queued input from the previous Workspace before render');
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    facade.sendComposerDraft({ binding, mentionedUserIds: [] });
    expect(facade.page().draft).toBe('');
    expect(write).not.toHaveBeenCalled();
    http.expectNone((request) => request.method === 'POST');
    fixture.detectChanges();
    expect(composer().value).toBe('');
    expect(composer()).not.toBe(textarea);
    input(textarea, 'Queued input from the previous Workspace after render');
    expect(facade.page().draft).toBe('');
    expect(write).not.toHaveBeenCalled();
    http.expectNone((request) => request.method === 'POST');
  });

  it('clears transient input when the current actor changes during catch-up', async () => {
    await configure('unavailable');
    const textarea = composer();
    input(textarea, 'Original actor private draft');
    const completion = catchUp();
    const pending = http.match((request) => request.url.startsWith('/api/conversations'));
    TestBed.inject(AuthSessionFacade).refreshCurrentUser().subscribe();
    http.expectOne('/api/auth/me').flush({ ...DEFAULT_AUTH_SESSION.currentUser, userId: 'other-actor' });
    const write = vi.spyOn(TestBed.inject(DraftStorageService), 'writeDraft');
    input(textarea, 'Stale input from the original actor');
    await completion;
    fixture.detectChanges();
    expect(pending.every((request) => request.cancelled)).toBe(true);
    expect(facade.page().draft).toBe('');
    expect(facade.page().conversation.id).toBe('');
    expect(write).not.toHaveBeenCalled();
  });

  it('keeps an uncertain send identity and requires an explicit retry after catch-up', async () => {
    await configure('unavailable');
    input(composer(), 'Uncertain command');
    facade.sendDraft();
    const first = http.expectOne('/api/conversations/conversation-a/messages');
    const clientRequestId = first.request.body.clientRequestId;
    first.flush({}, { status: 503, statusText: 'Unavailable' });
    const completion = catchUp();
    flushConversation();
    await completion;
    fixture.detectChanges();
    expect(composer().value).toBe('Uncertain command');
    http.expectNone((request) => request.method === 'POST');
    facade.sendDraft();
    const retry = http.expectOne('/api/conversations/conversation-a/messages');
    expect(retry.request.body.clientRequestId).toBe(clientRequestId);
    retry.flush({
      id: 'confirmed-retry', conversationId: 'conversation-a', workspaceId: 'workspace-a',
      authorUserId: currentUserId, body: 'Uncertain command', clientRequestId,
      createdAt: '2026-10-20T00:00:00Z', isDeleted: false, attachments: [],
    });
  });
});
