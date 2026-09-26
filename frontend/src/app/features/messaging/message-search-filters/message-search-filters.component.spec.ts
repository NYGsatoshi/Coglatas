import { provideHttpClient } from '@angular/common/http';
import { Location } from '@angular/common';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { MessageSearchFiltersComponent } from './message-search-filters.component';
import { MessagingConversationListItem, MessagingInboxViewModel } from '../messaging.types';

const workspaceId = '11111111-1111-4111-8111-111111111111';
const unreadMentionId = '22222222-2222-4222-8222-222222222222';
const unreadOnlyId = '33333333-3333-4333-8333-333333333333';
const mentionOnlyId = '44444444-4444-4444-8444-444444444444';
const messageId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const authorId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const inboxAll: MessagingInboxViewModel = {
  view: 'All',
  counts: { all: 3, unread: 2, mentions: 2, later: 0 },
  status: 'ready'
};

const conversations: readonly MessagingConversationListItem[] = [
  {
    id: unreadMentionId,
    kind: 'dm',
    title: 'Unread mention',
    route: `/dm/${unreadMentionId}`,
    lastActivityLabel: '10:00',
    safePreviewLabel: '',
    viewerIsParticipant: true,
    unreadCount: 2,
    hasMention: true
  },
  {
    id: unreadOnlyId,
    kind: 'channel',
    title: 'Unread only',
    route: `/workspaces/${workspaceId}/channels/${unreadOnlyId}`,
    lastActivityLabel: '09:00',
    safePreviewLabel: 'A safe preview',
    viewerIsParticipant: true,
    unreadCount: 1,
    hasMention: false
  },
  {
    id: mentionOnlyId,
    kind: 'channel',
    title: 'Mention only',
    route: `/workspaces/${workspaceId}/channels/${mentionOnlyId}`,
    lastActivityLabel: '08:00',
    safePreviewLabel: 'Another safe preview',
    viewerIsParticipant: true,
    unreadCount: 0,
    hasMention: true
  }
];

describe('MessageSearchFiltersComponent', () => {
  let fixture: ComponentFixture<MessageSearchFiltersComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    window.localStorage.setItem('coglatas.locale', 'en');
    await TestBed.configureTestingModule({
      imports: [MessageSearchFiltersComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(MessageSearchFiltersComponent);
    fixture.componentRef.setInput('conversations', conversations);
    fixture.componentRef.setInput('inbox', inboxAll);
    fixture.detectChanges();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('requests mutually exclusive server views and renders only authoritative rows and counts', async () => {
    const root = fixture.nativeElement as HTMLElement;
    const requested: string[] = [];
    fixture.componentInstance.inboxViewChanged.subscribe((view) => requested.push(view));
    expect(conversationRows(root)).toHaveLength(3);
    expect(testElement(root, 'message-filter-all').textContent).toContain('3');
    expect(testElement(root, 'message-filter-unread').textContent).toContain('2');

    click(root, '[data-testid="message-filter-unread"]');
    expect(requested).toEqual(['Unread']);
    expect(conversationRows(root)).toHaveLength(3);
    fixture.componentRef.setInput('conversations', conversations.slice(0, 2));
    fixture.componentRef.setInput('inbox', {
      view: 'Unread',
      counts: { all: 3, unread: 2, mentions: 2, later: 0 },
      status: 'ready'
    } satisfies MessagingInboxViewModel);
    fixture.detectChanges();
    expect(conversationRows(root)).toHaveLength(2);
    expect(testElement(root, 'message-filter-unread').getAttribute('aria-pressed')).toBe('true');
    expect(testElement(root, 'message-active-unread-chip')).not.toBeNull();
    expect(
      testElement(root, 'message-active-unread-chip').querySelector('[data-testid="filter-chip"]')
    ).not.toBeNull();
    expect(
      testElement(root, 'message-active-unread-chip').querySelector('button')?.getAttribute('aria-label')
    ).toBe('Remove filter Inbox: Unread');

    click(root, '[data-testid="message-filter-mentions"]');
    expect(requested).toEqual(['Unread', 'Mentions']);
    fixture.componentRef.setInput('conversations', [conversations[0], conversations[2]]);
    fixture.componentRef.setInput('inbox', {
      view: 'Mentions',
      counts: { all: 3, unread: 2, mentions: 2, later: 0 },
      status: 'ready'
    } satisfies MessagingInboxViewModel);
    fixture.detectChanges();
    expect(conversationRows(root)).toHaveLength(2);
    expect(testElement(root, 'message-active-mentions-chip')).not.toBeNull();
    expect(root.querySelector('[data-testid="message-active-unread-chip"]')).toBeNull();

    click(root, '[data-testid="message-active-mentions-chip"] button');
    await nextTask();
    expect(requested).toEqual(['Unread', 'Mentions', 'All']);
    expect(document.activeElement).toBe(testElement(root, 'message-filter-mentions'));

    click(root, '[data-testid="message-filters-clear-all"]');
    await nextTask();
    expect(root.textContent).not.toContain('Has file');
    expect(document.activeElement).toBe(testElement(root, 'message-search-input'));
  });

  it('emits a private Later change and exposes one pending mutation without changing unread state', () => {
    const root = fixture.nativeElement as HTMLElement;
    const changes: Array<{ conversationId: string; isLater: boolean }> = [];
    fixture.componentInstance.conversationLaterChanged.subscribe((change) => changes.push(change));

    click(root, '[data-testid="conversation-later-toggle"]');
    expect(changes).toEqual([{ conversationId: unreadMentionId, isLater: true }]);

    fixture.componentRef.setInput('inbox', {
      ...inboxAll,
      status: 'loading',
      requestedView: 'All',
      laterPendingConversationId: unreadMentionId
    } satisfies MessagingInboxViewModel);
    fixture.detectChanges();
    const toggles = Array.from(root.querySelectorAll<HTMLButtonElement>('[data-testid="conversation-later-toggle"]'));
    expect(toggles.every((button) => button.disabled)).toBe(true);
    expect(toggles[0].textContent).toContain('Saving');
    expect(conversationRows(root)[0].textContent).toContain('Unread');
  });

  it('uses only the canonical Message search and renders validated authorized results', () => {
    const root = fixture.nativeElement as HTMLElement;
    submitSearch(root, 'budget');

    const request = httpMock.expectOne((candidate) => candidate.url === '/api/search');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.params.get('q')).toBe('budget');
    expect(request.request.params.get('type')).toBe('Message');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({
      items: [
        {
          type: 5,
          id: messageId,
          title: 'Unread mention',
          snippet: 'Budget decision is ready',
          route: `/conversations/${unreadMentionId}`,
          workspaceId,
          authorDisplayName: 'Authorized author'
        },
        {
          type: 'Task',
          id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
          title: 'Wrong type',
          route: `/conversations/${unreadMentionId}`
        },
        {
          type: 'Message',
          id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
          title: 'Unsafe route',
          route: 'https://example.test/leak'
        }
      ]
    });
    fixture.detectChanges();

    const results = root.querySelectorAll<HTMLAnchorElement>('[data-testid="message-search-result"]');
    expect(results).toHaveLength(1);
    expect(results[0].getAttribute('href')).toBe(`/dm/${unreadMentionId}`);
    expect(results[0].textContent).toContain('Budget decision is ready');
    expect(results[0].textContent).toContain('Authorized author');
    expect(testElement(root, 'message-active-search-chip').textContent).toContain('budget');
    expect(testElement(root, 'message-search-status').textContent).toContain('1 authorized message matches shown');
    expect(conversationRows(root)).toHaveLength(3);
  });

  it('offers an adjustment path for zero results and restores focus for keyboard editing', async () => {
    const root = fixture.nativeElement as HTMLElement;
    submitSearch(root, 'missing');
    httpMock.expectOne((candidate) => candidate.url === '/api/search').flush({ items: [] });
    fixture.detectChanges();

    expect(testElement(root, 'message-search-empty').textContent).toContain('Change the search or clear');
    click(root, '[data-testid="message-search-change"]');
    fixture.detectChanges();
    await nextTask();

    const input = testElement(root, 'message-search-input') as HTMLInputElement;
    expect(input.value).toBe('');
    expect(document.activeElement).toBe(input);
    expect(root.querySelector('[data-testid="message-search-empty"]')).toBeNull();
    expect(conversationRows(root)).toHaveLength(3);
  });

  it('cancels an obsolete search when the query changes and redacts server failures', () => {
    const root = fixture.nativeElement as HTMLElement;
    submitSearch(root, 'first query');
    const obsolete = httpMock.expectOne((candidate) => candidate.url === '/api/search');

    setSearchInput(root, 'second query');
    fixture.detectChanges();
    expect(obsolete.cancelled).toBe(true);
    expect(root.querySelector('[data-testid="message-active-search-chip"]')).toBeNull();

    submitSearch(root, 'second query');
    httpMock
      .expectOne((candidate) => candidate.url === '/api/search')
      .flush({ detail: 'private database stack and title' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const error = testElement(root, 'message-search-error').textContent ?? '';
    expect(error).toContain('temporarily unavailable');
    expect(error).not.toContain('private database stack');
  });

  it('applies compound server filters, renders removable summaries, and keeps focus contained', async () => {
    const root = fixture.nativeElement as HTMLElement;
    click(root, '[data-testid="message-advanced-filters-open"]');
    fixture.detectChanges();
    await nextTask();

    const authorInput = testElement(root, 'message-advanced-author') as HTMLInputElement;
    expect(document.activeElement).toBe(authorInput);
    setInput(authorInput, 'Authorized');
    const authorRequest = httpMock.expectOne((candidate) => candidate.url === '/api/search/message-authors');
    expect(authorRequest.request.params.get('q')).toBe('Authorized');
    expect(authorRequest.request.params.get('limit')).toBe('20');
    authorRequest.flush({
      items: [
        { userId: authorId, displayName: 'Authorized Sender' },
        { userId: 'not-a-guid', displayName: 'Rejected Sender' }
      ]
    });
    fixture.detectChanges();

    const authorOption = testElement(root, 'message-author-option') as HTMLButtonElement;
    authorOption.focus();
    authorOption.click();
    fixture.detectChanges();
    await nextTask();
    expect(document.activeElement).toBe(authorInput);
    expect(root.textContent).not.toContain('Rejected Sender');

    setInput(testElement(root, 'message-advanced-from-date') as HTMLInputElement, '2026-08-20');
    setInput(testElement(root, 'message-advanced-to-date') as HTMLInputElement, '2026-08-30');
    setSelect(testElement(root, 'message-advanced-read') as HTMLSelectElement, 'Unread');
    setSelect(testElement(root, 'message-advanced-attachment') as HTMLSelectElement, 'With');

    const drawer = testElement(root, 'message-advanced-filters-drawer');
    const close = drawer.querySelector<HTMLButtonElement>('[aria-label="Close advanced filters"]')!;
    const apply = testElement(root, 'message-advanced-apply') as HTMLButtonElement;
    apply.focus();
    apply.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(close);
    close.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(apply);

    drawer.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const search = httpMock.expectOne((candidate) => candidate.url === '/api/search');
    expect(search.request.params.has('q')).toBe(false);
    expect(search.request.params.get('authorUserId')).toBe(authorId);
    expect(search.request.params.get('fromDate')).toBe(new Date(2026, 7, 20, 0, 0, 0, 0).toISOString());
    expect(search.request.params.get('toDateExclusive')).toBe(new Date(2026, 7, 31, 0, 0, 0, 0).toISOString());
    expect(search.request.params.get('messageRead')).toBe('Unread');
    expect(search.request.params.get('messageAttachment')).toBe('With');
    search.flush({ items: [] });
    fixture.detectChanges();
    await nextTask();

    expect(testElement(root, 'message-active-author-chip').textContent).toContain('Authorized Sender');
    expect(testElement(root, 'message-active-from-date-chip').textContent).toContain('2026-08-20');
    expect(testElement(root, 'message-active-to-date-chip').textContent).toContain('2026-08-30');
    expect(testElement(root, 'message-active-read-chip').textContent).toContain('Unread');
    expect(testElement(root, 'message-active-attachment-chip').textContent).toContain('With safe attachment');
    expect(testElement(root, 'message-advanced-summary').textContent).toContain('5 advanced filters applied');

    click(root, '[data-testid="message-active-author-chip"] button');
    const withoutAuthor = httpMock.expectOne((candidate) => candidate.url === '/api/search');
    expect(withoutAuthor.request.params.has('authorUserId')).toBe(false);
    expect(withoutAuthor.request.params.get('messageRead')).toBe('Unread');
    withoutAuthor.flush({ items: [] });
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="message-active-author-chip"]')).toBeNull();
  });

  it('defers URL author identity until authorized resolution and removes an unavailable identity', async () => {
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);
    await router.navigateByUrl(`/?messageFrom=${authorId}&messageRead=Unread`);
    fixture.detectChanges();

    expect(rootText(fixture)).not.toContain(authorId);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="message-active-author-chip"]')).toBeNull();
    const resolve = httpMock.expectOne((candidate) => candidate.url === '/api/search/message-authors');
    expect(resolve.request.params.get('selectedUserId')).toBe(authorId);
    resolve.flush({ items: [] });
    fixture.detectChanges();

    const search = httpMock.expectOne((candidate) => candidate.url === '/api/search');
    expect(search.request.params.get('messageRead')).toBe('Unread');
    expect(search.request.params.has('authorUserId')).toBe(false);
    search.flush({ items: [] });
    await waitForCondition(() => !location.path().includes('messageFrom'));
    expect(location.path()).toContain('messageRead=Unread');
  });

  it('cancels held route-author hydration and searches even when the remaining filter is unchanged', async () => {
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);
    await router.navigateByUrl(`/?messageFrom=${authorId}&messageRead=Unread`);
    fixture.detectChanges();

    const heldResolve = httpMock.expectOne((candidate) => candidate.url === '/api/search/message-authors');
    const root = fixture.nativeElement as HTMLElement;
    expect(testElement(root, 'message-active-read-chip').textContent).toContain('Unread');

    click(root, '[data-testid="message-advanced-filters-open"]');
    fixture.detectChanges();
    testElement(root, 'message-advanced-filters-drawer')
      .querySelector('form')
      ?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(heldResolve.cancelled).toBe(true);
    expect(() => heldResolve.flush({ items: [{ userId: authorId, displayName: 'Stale Sender' }] }))
      .toThrowError(/cancelled/i);
    const replacementSearch = httpMock.expectOne((candidate) => candidate.url === '/api/search');
    expect(replacementSearch.request.params.get('messageRead')).toBe('Unread');
    expect(replacementSearch.request.params.has('authorUserId')).toBe(false);
    replacementSearch.flush({ items: [] });
    await waitForCondition(() =>
      location.path().includes('messageRead=Unread') && !location.path().includes('messageFrom'));
    fixture.detectChanges();

    expect(location.path()).not.toContain('messageFrom');
    expect(root.textContent).not.toContain('Stale Sender');
    expect(root.querySelector('[data-testid="message-active-author-chip"]')).toBeNull();
    expect(testElement(root, 'message-active-read-chip').textContent).toContain('Unread');
  });

  it('scrubs private free-text URL state through apply and browser history replay', async () => {
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);
    const secret = 'private-message-marker-367';
    await router.navigateByUrl(`/?q=${secret}`);
    await waitForCondition(() => !location.path().includes(secret));
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(location.path()).not.toContain('q=');
    expect((testElement(root, 'message-search-input') as HTMLInputElement).value).toBe('');
    expect(root.textContent).not.toContain(secret);
    expect(root.querySelector('[data-testid="message-active-search-chip"]')).toBeNull();

    click(root, '[data-testid="message-advanced-filters-open"]');
    fixture.detectChanges();
    setSelect(testElement(root, 'message-advanced-read') as HTMLSelectElement, 'Unread');
    testElement(root, 'message-advanced-filters-drawer')
      .querySelector('form')
      ?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    const initialSearch = httpMock.expectOne((candidate) => candidate.url === '/api/search');
    expect(initialSearch.request.params.has('q')).toBe(false);
    initialSearch.flush({ items: [] });
    await waitForCondition(() => location.path().includes('messageRead=Unread'));
    expect(location.path()).not.toContain(secret);

    location.back();
    await waitForCondition(() => !location.path().includes('messageRead'));
    await router.navigateByUrl(location.path() || '/', { replaceUrl: true });
    await waitForCondition(() => fixture.componentInstance.appliedAdvanced().read === 'All');
    fixture.detectChanges();
    expect(location.path()).not.toContain(secret);
    expect(root.textContent).not.toContain(secret);

    location.forward();
    await waitForCondition(() => location.path().includes('messageRead=Unread'));
    await router.navigateByUrl(location.path(), { replaceUrl: true });
    await waitForCondition(() => fixture.componentInstance.appliedAdvanced().read === 'Unread');
    fixture.detectChanges();
    const replaySearch = httpMock.expectOne((candidate) => candidate.url === '/api/search');
    expect(replaySearch.request.params.has('q')).toBe(false);
    replaySearch.flush({ items: [] });
    fixture.detectChanges();
    expect(location.path()).not.toContain(secret);
    expect(root.textContent).not.toContain(secret);
  });
});

function submitSearch(root: HTMLElement, value: string): void {
  setSearchInput(root, value);
  root.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
}

function setSearchInput(root: HTMLElement, value: string): void {
  const input = testElement(root, 'message-search-input') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

function setInput(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

function setSelect(select: HTMLSelectElement, value: string): void {
  select.value = value;
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

function click(root: HTMLElement, selector: string): void {
  root.querySelector<HTMLButtonElement>(selector)?.click();
}

function testElement(root: HTMLElement, testId: string): HTMLElement {
  const element = root.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
  if (!element) {
    throw new Error(`Expected data-testid=${testId}`);
  }
  return element;
}

function conversationRows(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>('[data-testid="conversation-list-item"]'));
}

function nextTask(): Promise<void> {
  return new Promise<void>((resolve) => setTimeout(resolve));
}

function rootText(fixture: ComponentFixture<MessageSearchFiltersComponent>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

async function waitForCondition(condition: () => boolean): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt++) {
    if (condition()) {
      return;
    }
    await nextTask();
  }
  throw new Error('Timed out waiting for route state.');
}
