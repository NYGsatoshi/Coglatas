// Real Angular lifecycle with mocked HTTP and transport; not backend/RLS acceptance.
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { AuthSessionFacade, COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { I18nService } from '../../../core/i18n/i18n.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { MessagingFacade } from '../messaging.facade';
import { MessagesPageComponent } from './messages-page.component';

async function setup(workspaceId?: string) {
  let catchUp: (() => Promise<void> | void) | undefined;
  const clearers = new Map<string, (reason: ProtectedStateClearReason) => void>();
  await TestBed.configureTestingModule({
    imports: [MessagesPageComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: RealtimeFacade, useValue: {
        durableEvents$: new Subject(),
        registerProtectedStateClearer: (owner: string, clear: (reason: ProtectedStateClearReason) => void) => {
          clearers.set(owner, clear); return () => {};
        },
        registerSubscription: () => () => {},
        registerCatchUp: (_owner: string, callback: () => Promise<void> | void) => {
          catchUp = callback; return () => {};
        },
      } },
    ],
  }).compileComponents();
  TestBed.inject(I18nService).setLocale('en');
  if (workspaceId) {
    TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace({ id: workspaceId, label: 'Current workspace' });
  }
  const fixture = TestBed.createComponent(MessagesPageComponent);
  document.body.appendChild(fixture.nativeElement);
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/conversations').flush({ items: [] });
  fixture.detectChanges();
  const facade = TestBed.inject(MessagingFacade);
  const identity = facade.createDialogScopeIdentity();
  expect(identity).not.toBeNull();
  const root = fixture.nativeElement as HTMLElement;
  const dialog = () => root.querySelector('[data-testid="new-message-dialog"]') !== null;
  const search = () => root.querySelector('[data-testid="recipient-search"]') !== null;
  root.querySelector<HTMLButtonElement>('[data-testid="new-message-button"]')!.click();
  fixture.detectChanges();
  expect(dialog()).toBe(true);
  expect(search()).toBe(true);
  return { fixture, http, facade, identity, dialog, search, clearers,
    catchUp: () => catchUp!(),
    finish: () => { fixture.destroy(); fixture.nativeElement.remove(); http.verify(); },
  };
}

describe('Direct-dialog lifecycle during current-scope inbox catch-up', () => {
  it('keeps a healthy authenticated direct dialog without a refresh', async () => {
    const context = await setup();
    context.fixture.detectChanges();
    const state = { dialog: context.dialog(), search: context.search(),
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.finish();
    expect(state).toEqual({ dialog: true, search: true, sameIdentity: true });
  });

  it('retains the direct dialog through a held same-scope authorized inbox refresh', async () => {
    const context = await setup();
    const pending = context.catchUp();
    context.fixture.detectChanges();
    const during = { dialog: context.dialog(), search: context.search(),
      status: context.facade.page().status,
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.http.expectOne('/api/conversations').flush({ items: [] });
    await pending;
    context.fixture.detectChanges();
    const after = { dialog: context.dialog(), search: context.search(),
      status: context.facade.page().status,
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.finish();
    expect({ during, after }).toEqual({
      during: { dialog: true, search: true, status: 'loading', sameIdentity: true },
      after: { dialog: true, search: true, status: 'empty', sameIdentity: true },
    });
  });

  it('retains the direct dialog when same-scope loading and success coalesce before rendering', async () => {
    const context = await setup();
    const pending = context.catchUp();
    context.http.expectOne('/api/conversations').flush({ items: [] });
    await pending;
    context.fixture.detectChanges();
    const state = { dialog: context.dialog(), search: context.search(),
      sameIdentity: context.facade.createDialogScopeIdentity() === context.identity };
    context.finish();
    expect(state).toEqual({ dialog: true, search: true, sameIdentity: true });
  });

  it('discards the direct dialog after a real facade forbidden inbox response', async () => {
    const context = await setup();
    const pending = context.catchUp();
    context.http.expectOne('/api/conversations').flush({ error: 'Unavailable' }, {
      status: 403, statusText: 'Forbidden',
    });
    await pending;
    context.fixture.detectChanges();
    const state = { dialog: context.dialog(), search: context.search(),
      status: context.facade.page().status };
    context.finish();
    expect(state).toEqual({ dialog: false, search: false, status: 'permissionDenied' });
  });

  it('retains local query but clears old recipient identities and forbids work until fresh success', async () => {
    const context = await setup();
    const component = context.fixture.componentInstance;
    component.onRecipientQueryChange('Local query');
    context.http.expectOne('/api/conversations/recipients?query=Local%20query').flush([
      { userId: 'old-recipient', displayName: 'Old protected name' },
    ]);
    component.selectRecipient('old-recipient');
    expect(component.canSubmitCreate()).toBe(true);
    const pending = context.catchUp(); context.fixture.detectChanges();
    expect(context.dialog()).toBe(true);
    expect(component.recipientQuery()).toBe('Local query');
    expect(component.recipients()).toEqual([]);
    expect(component.selectedRecipients()).toEqual([]);
    expect((context.fixture.nativeElement as HTMLElement).textContent).not.toContain('Old protected name');
    // NgModel applies its disabled input in a scheduled Angular microtask.
    await context.fixture.whenStable();
    expect(context.fixture.nativeElement.querySelector('[data-testid="recipient-search"]').disabled).toBe(true);
    component.onRecipientQueryChange('Ignored'); component.createConversation();
    context.http.expectNone((request) => request.url !== '/api/conversations');
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending;
    context.fixture.detectChanges();
    expect(component.canSubmitCreate()).toBe(false);
    await context.fixture.whenStable();
    expect(context.fixture.nativeElement.querySelector('[data-testid="recipient-search"]').disabled).toBe(false);
    component.onRecipientQueryChange('Local query');
    context.http.expectOne('/api/conversations/recipients?query=Local%20query').flush([
      { userId: 'fresh-recipient', displayName: 'Fresh current name' },
    ]);
    component.selectRecipient('fresh-recipient');
    expect(component.selectedRecipients().map((person) => person.userId)).toEqual(['fresh-recipient']);
    expect(component.canSubmitCreate()).toBe(true); context.finish();
  });

  it('rejects a recipient response delivered after refresh begins but before cancellation renders', async () => {
    const context = await setup(); const component = context.fixture.componentInstance;
    component.onRecipientQueryChange('Pending');
    const oldSearch = context.http.expectOne('/api/conversations/recipients?query=Pending');
    const pending = context.catchUp();
    oldSearch.flush([{ userId: 'stale', displayName: 'Stale callback name' }]);
    expect(component.recipients()).toEqual([]);
    context.fixture.detectChanges();
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending;
    context.fixture.detectChanges();
    expect(component.recipientQuery()).toBe('Pending');
    expect(component.recipients()).toEqual([]); expect(context.dialog()).toBe(true);
    context.finish();
  });

  it('does not navigate or repeat a create when its response arrives across routine revalidation', async () => {
    const context = await setup(); const component = context.fixture.componentInstance;
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    component.onRecipientQueryChange('Current');
    context.http.expectOne('/api/conversations/recipients?query=Current').flush([
      { userId: 'recipient', displayName: 'Current recipient' },
    ]);
    component.selectRecipient('recipient'); component.createConversation();
    const oldCreate = context.http.expectOne('/api/conversations/direct');
    const pending = context.catchUp();
    oldCreate.flush({ id: 'completed-old-command', type: 'DirectMessage' });
    expect(navigate).not.toHaveBeenCalled();
    context.fixture.detectChanges();
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending;
    context.fixture.detectChanges();
    expect(context.dialog()).toBe(true); expect(component.creating()).toBe(false);
    expect(component.canSubmitCreate()).toBe(false);
    context.http.expectNone((request) => request.method === 'POST');
    // This tests stale UI activation, not whether HTTP cancellation rolls back a committed command.
    context.finish();
  });

  for (const reason of ['authorization', 'session', 'tenant', 'workspace'] as const) {
    it(`fully closes a ${reason} boundary even when fresh recovery coalesces before rendering`, async () => {
      const context = await setup(); const component = context.fixture.componentInstance;
      component.onRecipientQueryChange('Selected');
      context.http.expectOne('/api/conversations/recipients?query=Selected').flush([
        { userId: 'old', displayName: 'Old selected name' },
      ]);
      component.selectRecipient('old'); expect(component.canSubmitCreate()).toBe(true);
      component.onRecipientQueryChange('Old local intent');
      const oldSearch = context.http.expectOne('/api/conversations/recipients?query=Old%20local%20intent');
      const binding = context.facade.listPresentationBinding();
      context.clearers.get('messaging')!(reason);
      context.facade.loadConversationListPage();
      context.http.expectOne('/api/conversations').flush({ items: [] });
      expect(context.facade.listPresentationBinding()).not.toBe(binding);
      expect(component.canSubmitCreate()).toBe(false);
      component.createConversation(); context.http.expectNone((request) => request.method === 'POST');
      context.fixture.detectChanges();
      expect(oldSearch.cancelled).toBe(true);
      expect(context.dialog()).toBe(false); expect(component.recipientQuery()).toBe('');
      expect(component.selectedRecipients()).toEqual([]); context.finish();
    });
  }

  for (const scope of ['actor', 'tenant', 'workspace'] as const) {
    it(`closes on actual ${scope} replacement without retaining the old query`, async () => {
      const context = await setup();
      const component = context.fixture.componentInstance;
      component.onRecipientQueryChange('Old scope query');
      const oldSearch = context.http.expectOne('/api/conversations/recipients?query=Old%20scope%20query');
      expect(component.recipientQuery()).toBe('Old scope query');
      if (scope === 'workspace') {
        TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace({ id: 'replacement', label: 'Replacement' });
      } else if (scope === 'actor') {
        TestBed.inject(AuthSessionFacade).refreshCurrentUser().subscribe();
        context.http.expectOne('/api/auth/me').flush({ ...DEFAULT_AUTH_SESSION.currentUser!, userId: 'replacement' });
      } else {
        TestBed.inject(AuthSessionFacade).refreshCurrentTenant().subscribe();
        context.http.expectOne('/api/tenants/current').flush({ ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId: 'replacement' });
      }
      context.facade.loadConversationListPage();
      context.http.expectOne('/api/conversations').flush({ items: [] }); context.fixture.detectChanges();
      expect(context.dialog()).toBe(false); expect(context.search()).toBe(false);
      expect(oldSearch.cancelled).toBe(true);
      expect(component.recipientQuery()).toBe('');
      context.finish();
    });
  }

  it('cancels pending recipient work when the dialog route is unmounted', async () => {
    const context = await setup();
    context.fixture.componentInstance.onRecipientQueryChange('Pending unmount');
    const request = context.http.expectOne('/api/conversations/recipients?query=Pending%20unmount');
    context.finish(); expect(request.cancelled).toBe(true);
  });

  it('closes after an unsuccessful routine inbox response without resuming lookup work', async () => {
    const context = await setup(); const pending = context.catchUp(); context.fixture.detectChanges();
    context.http.expectOne('/api/conversations').flush({ error: 'Unavailable' }, { status: 500, statusText: 'Unavailable' });
    await pending; context.fixture.detectChanges();
    expect(context.dialog()).toBe(false); expect(context.fixture.componentInstance.canCreateConversation()).toBe(false);
    context.http.expectNone((request) => request.method === 'POST'); context.finish();
  });

  for (const renderWhilePending of [true, false]) {
    it('closes after a settled wrong-view projection (render while pending: ' + renderWhilePending + ')', async () => {
      const context = await setup();
      const pending = context.catchUp();
      if (renderWhilePending) {
        context.fixture.detectChanges();
        expect(context.dialog()).toBe(true);
      }
      context.http.expectOne('/api/conversations').flush({
        view: 'Later', counts: { all: 0, unread: 0, mentions: 0, later: 0 }, items: [],
      });
      await pending;
      context.fixture.detectChanges();
      const settled = { inboxStatus: context.facade.inbox().status, dialog: context.dialog() };
      context.finish();
      expect(settled).toEqual({ inboxStatus: 'error', dialog: false });
    });
  }

  it('retains local Group title but remounts protected member choices only after fresh success', async () => {
    const context = await setup('workspace-a'); const component = context.fixture.componentInstance;
    component.changeCreateMode('group'); component.groupTitle.set('Local group title'); context.fixture.detectChanges();
    const staleMembers = context.http.expectOne('/api/workspaces/workspace-a/members');
    const pending = context.catchUp(); context.fixture.detectChanges();
    expect(staleMembers.cancelled).toBe(true);
    expect(context.fixture.nativeElement.querySelector('app-workspace-recipient-picker')).toBeNull();
    expect(component.groupTitle()).toBe('Local group title'); expect(component.canSubmitCreate()).toBe(false);
    context.http.expectNone('/api/workspaces/workspace-a/members');
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending; context.fixture.detectChanges();
    context.http.expectOne('/api/workspaces/workspace-a/members').flush([
      { userId: 'fresh', displayName: 'Fresh member', status: 'Active' },
    ]);
    expect(component.groupTitle()).toBe('Local group title'); expect(context.dialog()).toBe(true);
    expect(component.selectedRecipients()).toEqual([]); context.finish();
  });
});
