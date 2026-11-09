import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { AuthSessionFacade, COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { I18nService } from '../../../core/i18n/i18n.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { MessageSearchFiltersComponent } from '../message-search-filters/message-search-filters.component';
import { MessagesPageComponent } from './messages-page.component';
import { MessagingFacade } from '../messaging.facade';

async function setupSearch() {
  let callback: (() => Promise<void> | void) | undefined;
  const clearers = new Map<string, (reason: ProtectedStateClearReason) => void>();
  await TestBed.configureTestingModule({ imports: [MessagesPageComponent], providers: [
    provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
    { provide: RealtimeFacade, useValue: { durableEvents$: new Subject(),
      registerProtectedStateClearer: (owner: string, clear: (reason: ProtectedStateClearReason) => void) => { clearers.set(owner, clear); return () => {}; },
      registerSubscription: () => () => {},
      registerCatchUp: (_owner: string, catchUp: () => Promise<void> | void) => { callback = catchUp; return () => {}; } } }
  ] }).compileComponents();
  TestBed.inject(I18nService).setLocale('en');
  const fixture = TestBed.createComponent(MessagesPageComponent);
  document.body.appendChild(fixture.nativeElement);
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/conversations').flush({ items: [] });
  fixture.detectChanges();
  const search = () => fixture.debugElement.query(By.directive(MessageSearchFiltersComponent))?.componentInstance as MessageSearchFiltersComponent | undefined;
  const open = () => { search()!.toggleMobilePanel(); search()!.openAdvancedDrawer(); fixture.detectChanges(); };
  return { fixture, http, search, open, clearers, facade: TestBed.inject(MessagingFacade), catchUp: () => callback!(),
    finish: () => { fixture.destroy(); fixture.nativeElement.remove(); http.verify(); } };
}

describe('Inbox search presentation during current-scope catch-up', () => {
  it('restores only an explicitly opened drawer after a routine fresh list read', async () => {
    let catchUp: (() => Promise<void> | void) | undefined;
    await TestBed.configureTestingModule({ imports: [MessagesPageComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: RealtimeFacade, useValue: { durableEvents$: new Subject(),
        registerProtectedStateClearer: () => () => {}, registerSubscription: () => () => {},
        registerCatchUp: (_owner: string, callback: () => Promise<void> | void) => { catchUp = callback; return () => {}; } } }
    ] }).compileComponents();
    TestBed.inject(I18nService).setLocale('en');
    const fixture = TestBed.createComponent(MessagesPageComponent);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/conversations').flush({ items: [] });
    fixture.detectChanges();
    const search = fixture.debugElement.query(By.directive(MessageSearchFiltersComponent)).componentInstance as MessageSearchFiltersComponent;
    const original = fixture.nativeElement.querySelector('[data-testid="message-search-filters"]') as HTMLElement;
    (fixture.nativeElement.querySelector('[data-testid="message-advanced-filters-open"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(search.advancedDrawerOpen()).toBe(true);
    const pending = catchUp!();
    fixture.detectChanges();
    const retained = original.isConnected || fixture.nativeElement.contains(original);
    http.expectOne('/api/conversations').flush({ items: [] });
    await pending;
    fixture.detectChanges();
    const current = fixture.debugElement.query(By.directive(MessageSearchFiltersComponent)).componentInstance;
    const result = { retained, sameInstance: current === search, drawerOpen: current.advancedDrawerOpen() };
    fixture.destroy();
    http.verify();
    expect(result).toEqual({ retained: false, sameInstance: false, drawerOpen: true });
  });

  it('discards old query, results and author work while restoring only fresh chrome', async () => {
    const context = await setupSearch(); context.open();
    const original = context.search()!;
    original.query.set('Synthetic private query');
    original.submitSearch(new Event('submit', { cancelable: true }));
    context.http.expectOne((request) => request.url === '/api/search').flush({ items: [{
      type: 5, id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', title: 'Protected title', snippet: 'Protected snippet',
      route: '/conversations/bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', authorDisplayName: 'Protected author'
    }] });
    expect(original.searchResults()).toHaveLength(1);
    original.retrySearch();
    const staleSearch = context.http.expectOne((request) => request.url === '/api/search');
    const input = context.fixture.nativeElement.querySelector('[data-testid="message-advanced-author"]') as HTMLInputElement;
    input.value = 'Protected author'; input.dispatchEvent(new Event('input', { bubbles: true }));
    const staleAuthor = context.http.expectOne((request) => request.url === '/api/search/message-authors');
    const pending = context.catchUp(); context.fixture.detectChanges();
    expect(staleSearch.cancelled).toBe(true); expect(staleAuthor.cancelled).toBe(true);
    expect(context.search()).toBeUndefined();
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending; context.fixture.detectChanges();
    const current = context.search()!;
    expect(current).not.toBe(original);
    expect(current.mobilePanelOpen()).toBe(true); expect(current.advancedDrawerOpen()).toBe(true);
    expect(current.query()).toBe(''); expect(current.appliedQuery()).toBe(''); expect(current.searchResults()).toEqual([]);
    expect(current.draftAuthorQuery()).toBe(''); expect(current.draftAuthor()).toBeNull(); expect(current.authorOptions()).toEqual([]);
    expect(context.fixture.nativeElement.textContent).not.toContain('Protected');
    context.http.expectNone((request) => request.url === '/api/search' || request.url === '/api/search/message-authors');
    context.finish();
  });

  it('preserves chrome across overlapping routine reads without reviving closed intent', async () => {
    const context = await setupSearch(); context.open();
    const first = context.catchUp(); context.fixture.detectChanges();
    const superseded = context.http.expectOne('/api/conversations');
    const second = context.catchUp(); expect(superseded.cancelled).toBe(true);
    context.http.expectOne('/api/conversations').flush({ items: [] }); await Promise.all([first, second]); context.fixture.detectChanges();
    expect(context.search()!.advancedDrawerOpen()).toBe(true);
    context.search()!.cancelAdvancedDrawer(); context.fixture.detectChanges();
    const third = context.catchUp(); context.fixture.detectChanges();
    context.http.expectOne('/api/conversations').flush({ items: [] }); await third; context.fixture.detectChanges();
    expect(context.search()!.advancedDrawerOpen()).toBe(false);
    context.finish();
  });

  it('preserves the existing same-scope search child and local input when selecting an inbox view', async () => {
    const context = await setupSearch(); context.open();
    const original = context.search()!; original.query.set('Local current-scope query');
    context.facade.selectInboxView('Unread'); context.fixture.detectChanges();
    expect(context.search()).toBe(original);
    context.http.expectOne('/api/conversations?view=Unread').flush({ items: [] }); context.fixture.detectChanges();
    expect(context.search()).toBe(original); expect(original.mobilePanelOpen()).toBe(true);
    expect(original.query()).toBe('Local current-scope query'); context.finish();
  });

  it('destroys old search and author work when routine loading and fresh ready coalesce before rendering', async () => {
    const context = await setupSearch(); context.open();
    const original = context.search()!;
    original.query.set('Old protected query'); original.submitSearch(new Event('submit', { cancelable: true }));
    const oldSearch = context.http.expectOne((request) => request.url === '/api/search');
    const author = context.fixture.nativeElement.querySelector('[data-testid="message-advanced-author"]') as HTMLInputElement;
    author.value = 'Old author'; author.dispatchEvent(new Event('input', { bubbles: true }));
    const oldAuthor = context.http.expectOne((request) => request.url === '/api/search/message-authors');
    const pending = context.catchUp();
    // Deliberately never render the intermediate loading projection.
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending;
    context.fixture.detectChanges();
    expect(context.search()).not.toBe(original); expect(oldSearch.cancelled).toBe(true); expect(oldAuthor.cancelled).toBe(true);
    expect(context.search()!.query()).toBe(''); expect(context.search()!.searchResults()).toEqual([]);
    expect(context.search()!.draftAuthorQuery()).toBe(''); expect(context.search()!.authorOptions()).toEqual([]);
    expect(context.search()!.mobilePanelOpen()).toBe(true); expect(context.search()!.advancedDrawerOpen()).toBe(true);
    context.finish();
  });

  it('rejects a stale child presentation output before the next routine render', async () => {
    const context = await setupSearch(); context.open();
    const binding = context.facade.listPresentationBinding(), revision = context.facade.listRefreshRevision();
    const pending = context.catchUp();
    context.fixture.componentInstance.rememberSearchPresentation({ mobilePanelOpen: false, advancedDrawerOpen: false }, binding, revision);
    expect(context.fixture.componentInstance.searchPresentation()?.value.advancedDrawerOpen).toBe(true);
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending; context.fixture.detectChanges();
    expect(context.search()!.advancedDrawerOpen()).toBe(true); context.finish();
  });

  for (const reason of ['authorization', 'workspace', 'tenant', 'session'] as const) {
    it(`rejects old presentation after a ${reason} boundary and fresh recovery`, async () => {
      const context = await setupSearch(); context.open();
      const oldBinding = context.facade.listPresentationBinding();
      const oldRevision = context.facade.listRefreshRevision();
      const pending = context.catchUp(); context.fixture.detectChanges();
      const denied = context.http.expectOne('/api/conversations');
      context.clearers.get('messaging')!(reason);
      expect(denied.cancelled).toBe(true);
      context.fixture.componentInstance.rememberSearchPresentation({ mobilePanelOpen: true, advancedDrawerOpen: true }, oldBinding, oldRevision);
      context.facade.loadConversationListPage();
      context.http.expectOne('/api/conversations').flush({ items: [] }); await pending; context.fixture.detectChanges();
      expect(context.search()!.mobilePanelOpen()).toBe(false); expect(context.search()!.advancedDrawerOpen()).toBe(false);
      context.finish();
    });
  }

  for (const status of [403, 500]) {
    it(`does not restore old chrome after HTTP ${status} and later successful recovery`, async () => {
      const context = await setupSearch(); context.open();
      const pending = context.catchUp(); context.fixture.detectChanges();
      context.http.expectOne('/api/conversations').flush({ error: 'Unavailable' }, { status, statusText: 'Unavailable' });
      await pending; context.fixture.detectChanges(); expect(context.search()).toBeUndefined();
      const recovery = context.catchUp(); context.http.expectOne('/api/conversations').flush({ items: [] }); await recovery;
      context.fixture.detectChanges(); expect(context.search()!.advancedDrawerOpen()).toBe(false);
      context.finish();
    });
  }

  for (const scope of ['actor', 'tenant', 'workspace'] as const) {
    it(`rejects a last-rendered presentation binding after actual ${scope} replacement`, async () => {
      const context = await setupSearch(); context.open();
      const binding = context.facade.listPresentationBinding();
      const revision = context.facade.listRefreshRevision();
      if (scope === 'workspace') {
        TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace({ id: 'replacement', label: 'Replacement' });
      } else if (scope === 'actor') {
        TestBed.inject(AuthSessionFacade).refreshCurrentUser().subscribe();
        context.http.expectOne('/api/auth/me').flush({ ...DEFAULT_AUTH_SESSION.currentUser!, userId: 'replacement' });
      } else {
        TestBed.inject(AuthSessionFacade).refreshCurrentTenant().subscribe();
        context.http.expectOne('/api/tenants/current').flush({ ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId: 'replacement' });
      }
      expect(context.facade.listPresentationBinding()).not.toBe(binding);
      context.fixture.componentInstance.rememberSearchPresentation({ mobilePanelOpen: true, advancedDrawerOpen: true }, binding, revision);
      context.facade.loadConversationListPage(); context.http.expectOne('/api/conversations').flush({ items: [] });
      context.fixture.detectChanges();
      expect(context.search()!.mobilePanelOpen()).toBe(false); expect(context.search()!.advancedDrawerOpen()).toBe(false);
      context.finish();
    });
  }

  for (const newerFocus of [false, true]) {
    it(`restores authorized drawer focus only without a newer user focus choice (${newerFocus})`, async () => {
      const context = await setupSearch(); context.open();
      await new Promise((resolve) => setTimeout(resolve, 0));
      const pending = context.catchUp(); context.fixture.detectChanges();
      const elsewhere = document.createElement('button'); document.body.appendChild(elsewhere);
      if (newerFocus) { elsewhere.focus(); }
      context.http.expectOne('/api/conversations').flush({ items: [] }); await pending; context.fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));
      const input = context.fixture.nativeElement.querySelector('[data-testid="message-advanced-author"]');
      expect(document.activeElement).toBe(newerFocus ? elsewhere : input);
      elsewhere.remove(); context.finish();
    });
  }

  it('does not apply a queued restoration focus task after a hard boundary before the next render', async () => {
    const context = await setupSearch(); context.open();
    await new Promise((resolve) => setTimeout(resolve, 0));
    const pending = context.catchUp(); context.fixture.detectChanges();
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending; context.fixture.detectChanges();
    const restoredInput = context.fixture.nativeElement.querySelector('[data-testid="message-advanced-author"]');
    context.clearers.get('messaging')!('authorization');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(document.activeElement).not.toBe(restoredInput);
    context.fixture.detectChanges(); context.finish();
  });

  it('does not focus an obsolete restored child when another routine refresh starts before rendering', async () => {
    const context = await setupSearch(); context.open();
    await new Promise((resolve) => setTimeout(resolve, 0));
    const first = context.catchUp(); context.fixture.detectChanges();
    context.http.expectOne('/api/conversations').flush({ items: [] }); await first; context.fixture.detectChanges();
    const obsoleteInput = context.fixture.nativeElement.querySelector('[data-testid="message-advanced-author"]');
    const second = context.catchUp();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(document.activeElement).not.toBe(obsoleteInput);
    context.http.expectOne('/api/conversations').flush({ items: [] }); await second; context.fixture.detectChanges();
    context.finish();
  });

  it('cancels a held route-author resolution and resolves the URL again only in the fresh child', async () => {
    const context = await setupSearch();
    await TestBed.inject(Router).navigateByUrl('/?messageFrom=cccccccc-cccc-4ccc-8ccc-cccccccccccc');
    const oldResolution = context.http.expectOne((request) => request.url === '/api/search/message-authors');
    context.open();
    const pending = context.catchUp(); context.fixture.detectChanges();
    expect(oldResolution.cancelled).toBe(true);
    context.http.expectNone((request) => request.url === '/api/search/message-authors');
    context.http.expectOne('/api/conversations').flush({ items: [] }); await pending; context.fixture.detectChanges();
    const currentResolution = context.http.expectOne((request) => request.url === '/api/search/message-authors');
    expect(currentResolution).not.toBe(oldResolution);
    currentResolution.flush({ items: [{ userId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', displayName: 'Fresh authorized author' }] });
    context.http.expectOne((request) => request.url === '/api/search').flush({ items: [] });
    expect(context.search()!.appliedAdvanced().author?.displayName).toBe('Fresh authorized author');
    context.finish();
  });
});
