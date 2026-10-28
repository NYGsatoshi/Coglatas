import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../../core/feature-flags/frontend-feature-flags.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { DmPageComponent } from '../dm-page/dm-page.component';
import { MessagingFacade } from '../messaging.facade';
import { GroupRosterFocusIntent } from './group-member-panel.component';

describe('Group viewport handoff through real facade HTTP reauthorization', () => {
  let fixture: ComponentFixture<DmPageComponent>;
  let http: HttpTestingController;
  let catchUp: () => Promise<void>;
  let clearState: (reason: ProtectedStateClearReason) => void;
  let scroll: ReturnType<typeof vi.fn>;
  const params = new BehaviorSubject(convertToParamMap({ conversationId: 'group-a' }));
  const actor = DEFAULT_AUTH_SESSION.currentUser!.userId;

  beforeEach(async () => {
    scroll = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, writable: true, value: scroll });
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 0, 320, 420));
    params.next(convertToParamMap({ conversationId: 'group-a' }));
    await TestBed.configureTestingModule({
      imports: [DmPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: ActivatedRoute, useValue: { paramMap: params,
          snapshot: { queryParamMap: convertToParamMap({}) } } },
        { provide: FrontendFeatureFlagsService, useValue: {
          optimisticMessagingEnabled: () => true, realtimeSignalREnabled: () => true } },
        { provide: RealtimeFacade, useValue: {
          durableEvents$: new Subject(), connectionState: () => 'Connected',
          registerCatchUp: (_owner: string, callback: typeof catchUp) => { catchUp = callback; return () => {}; },
          registerProtectedStateClearer: (_owner: string, callback: typeof clearState) => { clearState = callback; return () => {}; },
          registerSubscription: () => () => {}
        } }
      ]
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DmPageComponent);
    flush();
    fixture.detectChanges();
    await Promise.resolve();
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { fixture.destroy(); vi.restoreAllMocks(); TestBed.resetTestingModule(); }
  });

  function flush(id = 'group-a', canRead = true): void {
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne(`/api/conversations/${id}`).flush({ id, workspaceId: 'workspace-a', type: 'Group', title: 'Group',
      members: [{ userId: actor, displayName: 'Protected member', role: 'ReadOnly', canRead,
        canPost: false, canManageMembers: false, canCreateThread: false }] });
    http.expectOne(`/api/conversations/${id}/messages`).flush({ items: [
      { id: 'message-a', authorUserId: actor, body: 'Protected body' }
    ] });
  }

  function summary(): HTMLElement { return fixture.nativeElement.querySelector('summary'); }
  function visibleSummary(): HTMLElement {
    const node = summary();
    vi.spyOn(node, 'getBoundingClientRect').mockReturnValue(new DOMRect(10, 120, 280, 44));
    return node;
  }
  function assertCleared(): void {
    expect(fixture.nativeElement.querySelector('[data-testid="group-members-panel"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Protected member');
    expect(fixture.nativeElement.textContent).not.toContain('Protected body');
    expect(TestBed.inject(MessagingFacade).page().messages).toEqual([]);
  }
  async function finish(completion: Promise<void>): Promise<ReturnType<typeof vi.fn>> {
    scroll.mockClear();
    flush(); await completion; fixture.detectChanges();
    await Promise.resolve();
    return scroll;
  }

  it.each(['before', 'during'] as const)('restores the visible summary after scroll returned and reauthorization finishes %s the visibility check', async phase => {
    const old = visibleSummary();
    // The handoff reads the mounted layout synchronously, before @if detach.
    const completion = catchUp(); fixture.detectChanges(); assertCleared();
    expect(old.isConnected).toBe(false);
    if (phase === 'during') expect(summary()).toBeNull();
    const scroll = await finish(completion);
    expect(summary()).not.toBe(old);
    expect(scroll).toHaveBeenCalledExactlyOnceWith({ block: 'center', inline: 'nearest', behavior: 'instant' });
    expect(document.activeElement).not.toBe(summary());
    // No replay without a newly observed visible authorized target.
    vi.spyOn(summary(), 'getBoundingClientRect').mockReturnValue(new DOMRect(10, 900, 280, 44));
    const second = catchUp(); fixture.detectChanges(); assertCleared();
    expect(await finish(second)).not.toHaveBeenCalled();
  });

  it('does not invent viewport intent when the roster was offscreen', async () => {
    vi.spyOn(summary(), 'getBoundingClientRect').mockReturnValue(new DOMRect(10, 900, 280, 44));
    const completion = catchUp(); fixture.detectChanges(); assertCleared();
    expect(await finish(completion)).not.toHaveBeenCalled();
  });

  it('waits for fresh readBody and membership instead of restoring revoked members', async () => {
    visibleSummary(); const completion = catchUp(); fixture.detectChanges(); assertCleared();
    flush('group-a', false); await completion; fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="group-members-panel"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Protected member');
    scroll.mockClear();
    TestBed.inject(MessagingFacade).manualRefresh(); flush(); fixture.detectChanges();
    await Promise.resolve(); expect(scroll).not.toHaveBeenCalled();
  });

  it('lets a newly opened real confirmation dialog supersede the previous roster interaction', async () => {
    visibleSummary();
    const leave = fixture.nativeElement.querySelector('[data-testid="group-leave"]') as HTMLElement;
    leave.focus(); leave.click(); fixture.detectChanges();
    await Promise.resolve();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
    const completion = catchUp(); fixture.detectChanges(); assertCleared();
    expect(await finish(completion)).not.toHaveBeenCalled();
    expect(document.activeElement).not.toBe(fixture.nativeElement.querySelector('[data-testid="group-leave"]'));
  });

  it('prevents overlapping catch-ups reviving a former route after denial', async () => {
    visibleSummary();
    const first = catchUp(), second = catchUp();
    fixture.detectChanges(); assertCleared();
    http.expectOne('/api/conversations');
    // Keep the selected request handle while the second callback waits.
    const detail = http.expectOne('/api/conversations/group-a');
    expect(detail.cancelled).toBe(false);
    detail.flush({}, { status: 403, statusText: 'Denied' });
    // The first list request is still pending: a newer HTTP result cannot be
    // credited while that earlier protected lifecycle remains unsettled.
    TestBed.inject(MessagingFacade).loadConversation('group-b', 'dm');
    flush('group-b'); await Promise.all([first, second]); fixture.detectChanges();
    await Promise.resolve(); expect(scroll).not.toHaveBeenCalled();
    expect(TestBed.inject(MessagingFacade).page().conversation.id).toBe('group-b');
    http.expectNone('/api/conversations/group-a');
  });

  it.each(['focusin', 'pointerdown', 'wheel', 'touchstart', 'keydown'] as const)(
    'discards the viewport after deliberate %s during the authorization gap', async event => {
      visibleSummary(); const completion = catchUp(); fixture.detectChanges();
      document.body.dispatchEvent(new Event(event, { bubbles: true }));
      expect(await finish(completion)).not.toHaveBeenCalled();
    });

  it.each([400, 401, 403, 404])('discards old intent and protected data after current HTTP %s', async status => {
    visibleSummary(); const completion = catchUp(); fixture.detectChanges(); assertCleared();
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne('/api/conversations/group-a').flush({}, { status, statusText: 'Denied' });
    await completion; fixture.detectChanges(); assertCleared();
    TestBed.inject(MessagingFacade).manualRefresh();
    scroll.mockClear(); flush(); fixture.detectChanges();
    await Promise.resolve(); expect(scroll).not.toHaveBeenCalled();
  });

  it('invalidates captured intent on route change and cancels the old response', async () => {
    visibleSummary(); const completion = catchUp(); fixture.detectChanges();
    const pending = http.expectOne('/api/conversations/group-a');
    http.expectOne('/api/conversations').flush({ items: [] });
    params.next(convertToParamMap({ conversationId: 'group-b' }));
    expect(pending.cancelled).toBe(true);
    flush('group-b'); await completion; fixture.detectChanges();
    scroll.mockClear();
    await Promise.resolve(); expect(scroll).not.toHaveBeenCalled();
  });

  it.each(['authorization', 'session', 'tenant', 'workspace'] as const)(
    'invalidates intent on a %s boundary before old teardown can recreate it', async reason => {
      visibleSummary(); summary().focus();
      const completion = catchUp();
      const oldList = http.expectOne('/api/conversations');
      const oldDetail = http.expectOne('/api/conversations/group-a');
      clearState(reason);
      expect(oldList.cancelled).toBe(true);
      expect(oldDetail.cancelled).toBe(true);
      fixture.detectChanges(); assertCleared();
      await completion;
      if (reason === 'authorization') TestBed.inject(MessagingFacade).manualRefresh();
      else TestBed.inject(MessagingFacade).loadConversation('group-a', 'dm');
      scroll.mockClear(); flush(); fixture.detectChanges();
      await Promise.resolve(); expect(scroll).not.toHaveBeenCalled();
      expect(document.activeElement).not.toBe(summary());
    });
});

describe('Viewport intent scope, lifetime and one-time consumption', () => {
  const scope = { tenantId: 'tenant', workspaceId: 'workspace', conversationId: 'group', userId: 'actor' };
  it.each(['tenantId', 'workspaceId', 'conversationId', 'userId'] as const)('rejects a different %s and consumes the rejected intent', key => {
    const intent = new GroupRosterFocusIntent();
    intent.rememberViewport(scope, 'summary');
    expect(intent.claimViewport({ ...scope, [key]: 'other' })).toBeNull();
    expect(intent.claimViewport(scope)).toBeNull();
  });
  it.each([3001, 999])('rejects expired or backwards-clock intent at %s', time => {
    const intent = new GroupRosterFocusIntent();
    const now = vi.spyOn(Date, 'now').mockReturnValue(1000);
    try {
      intent.rememberViewport(scope, 'summary');
      now.mockReturnValue(time);
      expect(intent.claimViewport(scope)).toBeNull();
    } finally { now.mockRestore(); intent.ngOnDestroy(); }
  });
  it('clears pending document listeners and focus on page destruction', () => {
    const intent = new GroupRosterFocusIntent();
    intent.rememberViewport(scope, 'summary');
    intent.remember(scope, 'list', intent.epoch);
    intent.ngOnDestroy();
    expect(intent.claimViewport(scope)).toBeNull();
    expect(intent.claim(scope)).toBeNull();
  });
});
