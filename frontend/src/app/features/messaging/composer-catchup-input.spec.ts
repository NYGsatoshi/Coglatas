import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';

import { AuthSessionFacade, COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ChannelMessagingPageComponent } from './channel-messaging-page/channel-messaging-page.component';
import { DmPageComponent } from './dm-page/dm-page.component';
import { DraftStorageService } from './draft-storage.service';
import { MessagingFacade } from './messaging.facade';

const actor = DEFAULT_AUTH_SESSION.currentUser!.userId;
type Kind = 'dm' | 'channel';
type View = 'Member' | 'ReadOnly' | 'Removed';

async function setup(kind: Kind = 'dm') {
  const events = new Subject<DurableRealtimeEvent>();
  const catchUps = new Map<string, () => Promise<void> | void>();
  const clearers = new Map<string, (reason: ProtectedStateClearReason) => void>();
  await TestBed.configureTestingModule({
    imports: [kind === 'dm' ? DmPageComponent : ChannelMessagingPageComponent],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) },
        paramMap: of(convertToParamMap({ conversationId: 'conversation-a', workspaceId: 'workspace-a' })),
        queryParamMap: of(convertToParamMap({})) } },
      { provide: FrontendFeatureFlagsService, useValue: { optimisticMessagingEnabled: () => true, realtimeSignalREnabled: () => true } },
      { provide: RealtimeFacade, useValue: { durableEvents$: events.asObservable(), connectionState: signal('Connected'),
        registerCatchUp: (owner: string, callback: () => Promise<void> | void) => {
          catchUps.set(owner, callback); return () => { catchUps.delete(owner); };
        },
        registerProtectedStateClearer: (owner: string, callback: (reason: ProtectedStateClearReason) => void) => {
          clearers.set(owner, callback); return () => { clearers.delete(owner); };
        },
        registerSubscription: () => () => {} } }
    ]
  }).compileComponents();
  TestBed.inject(I18nService).setLocale('en');
  TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace({ id: 'workspace-a', label: 'Workspace' });
  const fixture = kind === 'dm' ? TestBed.createComponent(DmPageComponent) : TestBed.createComponent(ChannelMessagingPageComponent);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController), facade = TestBed.inject(MessagingFacade);
  return { fixture, http, facade, catchUp: async () => { await catchUps.get('messaging-route')!(); },
    clear: (reason: ProtectedStateClearReason) => clearers.get('messaging')!(reason), kind };
}

type Context = Awaited<ReturnType<typeof setup>>;
function flush(context: Context, view: View = 'Member', conversationId = 'conversation-a', workspaceId = 'workspace-a', userId = actor) {
  const { http, kind } = context;
  http.expectOne('/api/conversations').flush({ items: [{ id: conversationId, workspaceId,
    type: kind === 'dm' ? 'DirectMessage' : 'WorkspaceChannel', title: 'Synthetic conversation' }] });
  http.expectOne(`/api/conversations/${conversationId}`).flush({ id: conversationId, workspaceId,
    type: kind === 'dm' ? 'DirectMessage' : 'WorkspaceChannel', title: 'Synthetic conversation',
    members: [{ userId, role: view === 'ReadOnly' ? 'ReadOnly' : 'Member', canRead: true, canPost: view === 'Member',
      canCreateThread: view === 'Member', removedAt: view === 'Removed' ? '2026-10-24T00:00:00Z' : null, leftAt: null }] });
  http.expectOne(`/api/conversations/${conversationId}/messages`).flush({ items: [{ id: 'root-a', workspaceId, conversationId,
    authorUserId: 'peer', body: 'Server protected projection', createdAt: '2026-10-24T00:00:00Z' }] });
}
const root = (context: Context) => context.fixture.nativeElement as HTMLElement;
const input = (context: Context) => root(context).querySelector<HTMLTextAreaElement>('[data-testid="message-draft"]');
function type(node: HTMLTextAreaElement, value: string) { node.value = value; node.dispatchEvent(new Event('input', { bubbles: true })); }
function noPost(context: Context) { context.http.expectNone(request => request.method === 'POST'); }
function stored(context: Context) { return TestBed.inject(DraftStorageService).readDraft({ tenantId: DEFAULT_AUTH_SESSION.currentTenant!.tenantId,
  userId: actor, workspaceId: 'workspace-a', conversationId: 'conversation-a' }); }

describe('Mounted local composer input during routine catch-up', () => {
  afterEach(() => { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); sessionStorage.clear(); });

  for (const kind of ['dm', 'channel'] as const) {
    it(`retains the same ${kind} input and local edits while server projections and all post controls are unavailable`, async () => {
      const context = await setup(kind); flush(context); context.fixture.detectChanges();
      const node = input(context)!; type(node, 'Local draft before catch-up'); node.focus();
      const binding = context.facade.composerInput().binding;
      const completion = context.catchUp(); context.fixture.detectChanges();
      expect(context.facade.page().conversation.id).toBe(''); expect(context.facade.page().messages).toEqual([]);
      expect(context.facade.page().conversation.capabilities).toEqual([]);
      expect(root(context).textContent).not.toContain('Server protected projection');
      expect(input(context)).toBe(node); expect(node.disabled).toBe(false); expect(document.activeElement).toBe(node);
      expect(node.value).toBe('Local draft before catch-up'); expect(context.facade.composerInput().binding).toBe(binding);
      expect(context.facade.composerInput().retainLocalInput).toBe(true);
      expect(root(context).querySelector('[data-testid="composer-disabled-reason"]')?.textContent)
        .toContain('Conversation participation could not be verified.');
      expect(root(context).querySelector<HTMLButtonElement>('[data-testid="send-message"]')!.disabled).toBe(true);
      type(node, 'Native input while fresh authority is unknown');
      node.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      context.facade.sendComposerDraft({ binding, mentionedUserIds: [] }); noPost(context);
      flush(context); await completion; context.fixture.detectChanges();
      expect(input(context)).toBe(node); expect(node.value).toBe('Native input while fresh authority is unknown');
      expect(context.facade.page().draft).toBe(node.value); expect(stored(context)).toBe(node.value);
      expect(context.facade.composerInput().retainLocalInput).toBe(false);
      expect(root(context).querySelector<HTMLButtonElement>('[data-testid="send-message"]')!.disabled).toBe(false);
    });
  }

  for (const kind of ['dm', 'channel'] as const) {
    it(`keeps ${kind} IME composition on the same textarea while current authority is temporarily unknown`, async () => {
      const context = await setup(kind); flush(context); context.fixture.detectChanges();
      const node = input(context)!; node.focus();
      node.dispatchEvent(new Event('compositionstart', { bubbles: true }));
      const completion = context.catchUp(); context.fixture.detectChanges();
      expect(input(context)).toBe(node);
      expect(document.activeElement).toBe(node);
      expect(node.disabled).toBe(false);
      expect(root(context).querySelector<HTMLButtonElement>('[data-testid="send-message"]')!.disabled).toBe(true);

      type(node, '変換中の文章');
      const composingEnter = new KeyboardEvent('keydown', {
        key: 'Enter', isComposing: true, bubbles: true, cancelable: true
      });
      node.dispatchEvent(composingEnter);
      expect(composingEnter.defaultPrevented).toBe(false);
      noPost(context);

      node.dispatchEvent(new Event('compositionend', { bubbles: true }));
      const legacyEnter = new KeyboardEvent('keydown', {
        key: 'Enter', keyCode: 229, bubbles: true, cancelable: true
      });
      node.dispatchEvent(legacyEnter);
      expect(legacyEnter.defaultPrevented).toBe(false);
      noPost(context);

      flush(context); await completion; context.fixture.detectChanges();
      expect(input(context)).toBe(node);
      expect(node.value).toBe('変換中の文章');
      expect(context.facade.page().draft).toBe(node.value);
      expect(context.facade.composerInput().retainLocalInput).toBe(false);
      expect(root(context).querySelector<HTMLButtonElement>('[data-testid="send-message"]')!.disabled).toBe(false);

      node.dispatchEvent(new Event('compositionstart', { bubbles: true }));
      node.dispatchEvent(new KeyboardEvent('keydown', {
        key: 'Enter', isComposing: false, bubbles: true, cancelable: true
      }));
      noPost(context);
      node.dispatchEvent(new Event('compositionend', { bubbles: true }));
    });
  }

  for (const reason of ['session', 'tenant', 'authorization', 'workspace'] as const) {
    it(`ends input retention at the ${reason} boundary without replaying queued old-node input or changing storage policy`, async () => {
      const context = await setup(); flush(context); context.fixture.detectChanges(); const node = input(context)!;
      type(node, 'Local scoped draft'); const binding = context.facade.composerInput().binding;
      const completion = context.catchUp(); context.fixture.detectChanges(); type(node, 'Queued same-scope input');
      const requests = context.http.match(request => request.url.startsWith('/api/conversations'));
      context.clear(reason);
      type(node, 'Must not cross a protected boundary');
      context.facade.setComposerDraft({ value: 'Must not enter a new generation', binding });
      context.facade.sendComposerDraft({ binding, mentionedUserIds: [] });
      await completion; context.fixture.detectChanges();
      expect(requests.every(request => request.cancelled)).toBe(true);
      expect(context.facade.composerInput().retainLocalInput).toBe(false); expect(context.facade.composerInput().binding).toBeNull();
      expect(input(context)).toBeNull(); expect(context.facade.page().draft).toBe('');
      expect(stored(context)).toBe(reason === 'workspace' ? 'Queued same-scope input' : ''); noPost(context);
    });
  }

  for (const status of [400, 401, 403, 404, 500]) {
    it(`ends the local interval on authoritative messages ${status}, retaining only the existing denial/error storage distinction`, async () => {
      const context = await setup(); flush(context); context.fixture.detectChanges(); const node = input(context)!;
      const completion = context.catchUp(); context.fixture.detectChanges(); type(node, 'Input before failed revalidation');
      context.http.expectOne('/api/conversations').flush({ items: [] });
      context.http.expectOne('/api/conversations/conversation-a').flush({ id: 'conversation-a', workspaceId: 'workspace-a', type: 'DirectMessage',
        members: [{ userId: actor, canRead: true, canPost: true }] });
      context.http.expectOne('/api/conversations/conversation-a/messages').flush({}, { status, statusText: 'Rejected' });
      await completion; context.fixture.detectChanges();
      expect(context.facade.composerInput().retainLocalInput).toBe(false); expect(input(context)).toBeNull();
      expect(context.facade.page().messages).toEqual([]);
      expect(stored(context)).toBe(status === 500 ? 'Input before failed revalidation' : ''); noPost(context);
    });
  }

  it('never grants a previously ReadOnly composer temporary input and disables a fresh ReadOnly projection immediately', async () => {
    const context = await setup(); flush(context, 'ReadOnly'); context.fixture.detectChanges();
    expect(input(context)!.disabled).toBe(true);
    let completion = context.catchUp(); context.fixture.detectChanges();
    expect(context.facade.composerInput().retainLocalInput).toBe(false); expect(input(context)).toBeNull();
    flush(context); await completion; context.fixture.detectChanges();
    expect(input(context)!.disabled).toBe(false);
    completion = context.catchUp(); context.fixture.detectChanges(); const node = input(context)!;
    type(node, 'Local draft awaiting fresh membership'); const binding = context.facade.composerInput().binding;
    flush(context, 'ReadOnly'); await completion; context.fixture.detectChanges();
    expect(context.facade.composerInput().retainLocalInput).toBe(false); expect(node.disabled).toBe(true);
    context.facade.setComposerDraft({ value: 'Synthetic disabled input must be rejected', binding });
    context.facade.sendComposerDraft({ binding, mentionedUserIds: [] });
    expect(context.facade.page().draft).toBe('Local draft awaiting fresh membership');
    expect(stored(context)).toBe('Local draft awaiting fresh membership'); noPost(context);
  });

  it('ends local retention when fresh participation is removed', async () => {
    const context = await setup(); flush(context); context.fixture.detectChanges();
    const completion = context.catchUp(); context.fixture.detectChanges(); type(input(context)!, 'Local draft before removal');
    flush(context, 'Removed'); await completion; context.fixture.detectChanges();
    expect(context.facade.composerInput().retainLocalInput).toBe(false); expect(input(context)).toBeNull();
    // Existing successful-readable aggregate policy determines persisted local draft; this repair does not delete it.
    expect(stored(context)).toBe(''); noPost(context);
  });

  for (const next of ['conversation', 'workspace', 'same-scope-generation'] as const) {
    it(`rejects old DOM edits and Enter after coalesced ${next} replacement before Angular renders the new identity`, async () => {
      const context = await setup(); flush(context); context.fixture.detectChanges(); const oldNode = input(context)!;
      type(oldNode, 'Original scoped local draft');
      context.clear('workspace');
      const conversationId = next === 'conversation' ? 'conversation-b' : 'conversation-a';
      const workspaceId = next === 'workspace' ? 'workspace-b' : 'workspace-a';
      context.facade.loadConversation(conversationId, 'dm'); flush(context, 'Member', conversationId, workspaceId);
      const expected = context.facade.page().draft;
      // Deliberately do not render: the old component still owns the native listener and old input binding.
      type(oldNode, 'Stale node must not acquire the replacement scope');
      oldNode.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      expect(context.facade.page().draft).toBe(expected); noPost(context);
      context.fixture.detectChanges(); expect(input(context)).not.toBe(oldNode); expect(oldNode.isConnected).toBe(false);
    });
  }

  for (const changed of ['actor', 'tenant'] as const) {
    it(`rejects a previously mounted input after the ${changed} identity changes`, async () => {
      const context = await setup(); flush(context); context.fixture.detectChanges(); const node = input(context)!;
      const auth = TestBed.inject(AuthSessionFacade);
      const user = signal(DEFAULT_AUTH_SESSION.currentUser);
      const tenant = signal(DEFAULT_AUTH_SESSION.currentTenant);
      Object.defineProperty(auth, 'currentUser', { value: user }); Object.defineProperty(auth, 'currentTenant', { value: tenant });
      if (changed === 'actor') user.set({ ...DEFAULT_AUTH_SESSION.currentUser!, userId: 'other-actor' });
      else tenant.set({ ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId: 'other-tenant' });
      type(node, 'Old actor or Tenant input must be rejected');
      node.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      expect(context.facade.page().draft).toBe(''); expect(context.facade.composerInput().binding).toBeNull(); noPost(context);
      context.facade.loadConversation('conversation-b', 'dm');
      flush(context, 'Member', 'conversation-b', 'workspace-a', changed === 'actor' ? 'other-actor' : actor);
      const currentDraft = context.facade.page().draft;
      // The old DOM listener remains mounted until the next render, even though current authority is now fresh.
      type(node, 'Stale input after the replacement actor or Tenant has loaded');
      node.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      expect(context.facade.page().draft).toBe(currentDraft); noPost(context);
      context.fixture.detectChanges(); expect(input(context)).not.toBe(node);
    });
  }

  it('keeps pending sends disabled and lets the existing command settle before routine catch-up begins', async () => {
    const context = await setup(); flush(context); context.fixture.detectChanges();
    type(input(context)!, 'Existing real send intent'); context.facade.sendDraft();
    const send = context.http.expectOne('/api/conversations/conversation-a/messages');
    const completion = context.catchUp(); context.fixture.detectChanges();
    expect(input(context)!.disabled).toBe(true); expect(context.facade.composerInput().retainLocalInput).toBe(false);
    context.http.expectNone(request => request.method === 'GET');
    send.flush({ id: 'sent-a', workspaceId: 'workspace-a', conversationId: 'conversation-a', authorUserId: actor,
      body: 'Existing real send intent', clientRequestId: send.request.body.clientRequestId });
    await vi.waitFor(() => expect(context.facade.page().status).toBe('loading'));
    flush(context); await completion; context.fixture.detectChanges(); noPost(context);
    expect(context.facade.page().draft).toBe('');
  });
});
