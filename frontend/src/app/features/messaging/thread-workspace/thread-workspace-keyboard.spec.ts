import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../../core/feature-flags/frontend-feature-flags.service';
import { ProtectedStateClearReason, RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../../core/realtime/realtime.models';
import { MessagingFacade } from '../messaging.facade';
import { ThreadWorkspaceComponent } from './thread-workspace.component';

const rootIds = ['root-a', 'root-b', 'root-c'] as const;
type RootId = typeof rootIds[number];

function message(id: RootId): Record<string, unknown> {
  return {
    id, workspaceId: 'workspace-a', conversationId: 'conversation-a', authorUserId: 'peer',
    authorDisplayName: 'Synthetic peer', body: `Synthetic ${id}`, attachments: [],
    createdAt: '2026-10-30T12:00:00Z', isDeleted: false, version: 1
  };
}

describe('Thread Workspace public keyboard navigation', () => {
  let fixture: ComponentFixture<ThreadWorkspaceComponent>;
  let facade: MessagingFacade;
  let http: HttpTestingController;
  let root: HTMLElement;
  let clearProtectedState: (reason: ProtectedStateClearReason) => void;

  beforeEach(async () => {
    const events = new Subject<DurableRealtimeEvent>();
    await TestBed.configureTestingModule({
      imports: [ThreadWorkspaceComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: FrontendFeatureFlagsService, useValue: {
          realtimeSignalREnabled: () => false, optimisticMessagingEnabled: () => true
        } },
        { provide: RealtimeFacade, useValue: {
          durableEvents$: events.asObservable(),
          registerSubscription: () => () => undefined,
          registerCatchUp: () => () => undefined,
          registerProtectedStateClearer: (_owner: string, callback: (reason: ProtectedStateClearReason) => void) => {
            clearProtectedState = callback;
            return () => undefined;
          }
        } }
      ]
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('conversation-a', 'channel', 'workspace-a');
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne('/api/conversations/conversation-a').flush({
      id: 'conversation-a', workspaceId: 'workspace-a', type: 'ProjectChannel', title: 'Synthetic channel',
      members: [{ userId: DEFAULT_AUTH_SESSION.currentUser!.userId, displayName: 'Synthetic actor',
        canRead: true, canPost: true, canCreateThread: true, removedAt: null, leftAt: null }]
    });
    http.expectOne('/api/conversations/conversation-a/messages').flush({ items: rootIds.map(message) });
    for (const id of rootIds) {
      facade.openThread(id, `trigger-${id}`);
      flushThread(id);
    }
    fixture = TestBed.createComponent(ThreadWorkspaceComponent);
    root = fixture.nativeElement;
    await render();
  });

  afterEach(() => {
    fixture?.destroy();
    http?.verify();
    vi.restoreAllMocks();
  });

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function tab(id: RootId): HTMLButtonElement {
    return root.querySelector<HTMLButtonElement>(`#thread-tab-${id}`)!;
  }

  function flushThread(id: RootId): void {
    const request = http.expectOne(`/api/messages/${id}/thread`);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ rootMessage: message(id), replies: [], hasMore: false, maximumReplies: 100,
      summary: { threadRootMessageId: id, replyCount: 0, latestReplyAt: null, participantDisplayNames: [] } });
  }

  async function activate(id: RootId): Promise<void> {
    tab(id).click();
    flushThread(id);
    await render();
    expect(tab(id).getAttribute('aria-selected')).toBe('true');
  }

  async function navigate(from: RootId, key: string, to: RootId): Promise<KeyboardEvent> {
    tab(from).focus();
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    tab(from).dispatchEvent(event);
    flushThread(to);
    await render();
    return event;
  }

  it.each([
    { key: 'Home', from: 'root-c', to: 'root-a' },
    { key: 'End', from: 'root-a', to: 'root-c' },
    { key: 'ArrowRight', from: 'root-a', to: 'root-b' },
    { key: 'ArrowRight', from: 'root-c', to: 'root-a' }
  ] as const)('selects and reauthorizes $to with $key from $from, then focuses its roving tab', async ({ key, from, to }) => {
    if (from !== 'root-c') await activate(from);
    const focused = vi.spyOn(tab(to), 'focus');
    const event = await navigate(from, key, to);
    expect(event.defaultPrevented).toBe(true);
    expect(facade.thread().rootMessageId).toBe(to);
    expect(tab(to).getAttribute('tabindex')).toBe('0');
    expect(tab(from).getAttribute('tabindex')).toBe('-1');
    expect(focused).toHaveBeenCalledWith({ preventScroll: true });
    expect(document.activeElement).toBe(tab(to));
    expect(root.querySelector('[role="tabpanel"]')?.getAttribute('aria-labelledby')).toBe(`thread-tab-${to}`);
  });

  it.each([{ isComposing: true }, { keyCode: 229 }])(
    'leaves the selected tab and its request state alone during composition %j', async options => {
      tab('root-c').focus();
      const event = new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true, ...options });
      tab('root-c').dispatchEvent(event);
      await render();
      expect(event.defaultPrevented).toBe(false);
      expect(facade.thread().rootMessageId).toBe('root-c');
      expect(document.activeElement).toBe(tab('root-c'));
      http.expectNone('/api/messages/root-a/thread');
    }
  );

  it('preserves an unhandled native key without selecting or requesting another Thread', async () => {
    tab('root-c').focus();
    const event = new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true });
    tab('root-c').dispatchEvent(event);
    await render();
    expect(event.defaultPrevented).toBe(false);
    expect(facade.thread().rootMessageId).toBe('root-c');
    expect(document.activeElement).toBe(tab('root-c'));
  });

  function arrangeTabStrip(scrollLeft: number): HTMLElement {
    const strip = root.querySelector<HTMLElement>('[role="tablist"]')!;
    Object.defineProperties(strip, { clientLeft: { configurable: true, value: 2 }, clientWidth: { configurable: true, value: 120 } });
    strip.scrollLeft = scrollLeft;
    vi.spyOn(strip, 'getBoundingClientRect').mockImplementation(() => new DOMRect(10, 0, 124, 24));
    rootIds.forEach((id, index) => {
      vi.spyOn(tab(id), 'getBoundingClientRect')
        .mockImplementation(() => new DOMRect(12.4 + index * 80 - strip.scrollLeft, 0, 80, 24));
    });
    return strip;
  }

  it.each([
    { key: 'Home', from: 'root-c', to: 'root-a', initial: 40, expected: 0 },
    { key: 'End', from: 'root-a', to: 'root-c', initial: 0, expected: 121 },
    { key: 'ArrowRight', from: 'root-a', to: 'root-b', initial: 50, expected: 50 }
  ] as const)('reveals $to after $key while preserving an already visible tab', async ({ key, from, to, initial, expected }) => {
    if (from !== 'root-c') await activate(from);
    const strip = arrangeTabStrip(initial);
    await navigate(from, key, to);
    expect(document.activeElement).toBe(tab(to));
    expect(strip.scrollLeft).toBe(expected);
    const bounds = tab(to).getBoundingClientRect();
    expect(bounds.left).toBeGreaterThanOrEqual(12);
    expect(bounds.right).toBeLessThanOrEqual(132);
  });

  it('does not move focus back to a removed tab when authority clears before the scheduled render', async () => {
    const outside = document.createElement('button');
    document.body.append(outside);
    try {
      tab('root-c').dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true }));
      const pending = http.expectOne('/api/messages/root-a/thread');
      outside.focus();
      clearProtectedState('session');
      expect(pending.cancelled).toBe(true);
      await render();
      expect(root.querySelector('[role="tablist"]')).toBeNull();
      expect(document.activeElement).toBe(outside);
    } finally {
      outside.remove();
    }
  });
});
