import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { DmPageComponent } from './dm-page/dm-page.component';
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

  function flushConversation(id = 'conversation-a'): void {
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne(`/api/conversations/${id}`).flush({
      id, workspaceId: 'workspace-a', type: 'Direct', title: 'Authorized DM',
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
});
