import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';

import { CoglatasFilterChipComponent } from '../../../shared/ui/coglatas-filter-chip/coglatas-filter-chip.component';
import { ConversationListComponent } from '../conversation-list/conversation-list.component';
import {
  MessageAttachmentFilterDto,
  MessageAuthorOptionsResponseDto,
  MessageReadFilterDto,
  MessageSearchRequestDto,
  MessageSearchResponseDto,
  MessagingApi
} from '../messaging.api';
import {
  MessagingConversationListItem,
  MessagingInboxView,
  MessagingInboxViewModel
} from '../messaging.types';

type MessageSearchStatus = 'idle' | 'invalid' | 'loading' | 'ready' | 'empty' | 'error';

interface MessageSearchResult {
  readonly messageId: string;
  readonly conversationId: string;
  readonly title: string;
  readonly snippet: string;
  readonly authorDisplayName: string;
  readonly route: string;
}

interface MessageAuthorOption {
  readonly userId: string;
  readonly displayName: string;
}

interface MessageAdvancedFilters {
  readonly author: MessageAuthorOption | null;
  readonly fromDate: string;
  readonly toDate: string;
  readonly read: MessageReadFilterDto;
  readonly attachment: MessageAttachmentFilterDto;
}

type AuthorSearchStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'error';
type AdvancedFilterKey = 'author' | 'fromDate' | 'toDate' | 'read' | 'attachment';

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const CONVERSATION_ROUTE_PATTERN = /^\/conversations\/([0-9a-f-]+)$/i;
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const ADVANCED_QUERY_KEYS = {
  author: 'messageFrom',
  fromDate: 'messageFromDate',
  toDate: 'messageToDate',
  read: 'messageRead',
  attachment: 'messageAttachment'
} as const;
const PRIVATE_MESSAGE_SEARCH_QUERY_KEYS = ['q', 'messageSearch', 'messageQuery'] as const;
const EMPTY_ADVANCED_FILTERS: MessageAdvancedFilters = {
  author: null,
  fromDate: '',
  toDate: '',
  read: 'All',
  attachment: 'All'
};
const EMPTY_INBOX: MessagingInboxViewModel = {
  view: 'All',
  counts: { all: 0, unread: 0, mentions: 0, later: 0 },
  status: 'loading'
};

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-message-search-filters',
  standalone: true,
  imports: [CoglatasFilterChipComponent, ConversationListComponent, RouterLink],
  template: `
    <section
      class="message-discovery"
      aria-label="Find conversations and messages"
      data-testid="message-search-filters"
    >
      <button
        #mobileToggle
        type="button"
        class="message-discovery__mobile-toggle"
        data-testid="message-filter-drawer-toggle"
        aria-controls="message-search-filter-controls"
        [attr.aria-expanded]="mobilePanelOpen()"
        (click)="toggleMobilePanel()"
      >
        Search and filter messages
      </button>

      <div
        id="message-search-filter-controls"
        class="message-discovery__controls"
        [class.message-discovery__controls--open]="mobilePanelOpen()"
        tabindex="-1"
        (keydown.escape)="closeMobilePanel($event)"
      >
        <form
          class="message-discovery__search"
          role="search"
          [attr.aria-busy]="searchStatus() === 'loading'"
          (submit)="submitSearch($event)"
        >
          <label for="message-search-input">Search messages</label>
          <div class="message-discovery__search-control">
            <input
              #searchInput
              id="message-search-input"
              type="search"
              data-testid="message-search-input"
              autocomplete="off"
              maxlength="120"
              placeholder="Search message text or conversation title"
              aria-describedby="message-search-scope message-search-status"
              [value]="query()"
              (input)="updateQuery($event)"
            />
            <button type="submit" data-testid="message-search-submit" [disabled]="searchStatus() === 'loading'">
              {{ searchStatus() === 'loading' ? 'Searching' : 'Search' }}
            </button>
          </div>
          <p id="message-search-scope" class="message-discovery__scope">
            Searches only messages and conversation titles you can currently access.
          </p>
          <p
            id="message-search-status"
            class="message-discovery__status"
            data-testid="message-search-status"
            aria-live="polite"
          >
            @switch (searchStatus()) {
              @case ('invalid') {
                Enter at least 2 characters.
              }
              @case ('loading') {
                Searching your accessible conversations.
              }
              @case ('ready') {
                {{ searchResults().length }} authorized message matches shown.
              }
              @case ('empty') {
                No matching messages were found.
              }
              @case ('error') {
                Message search is unavailable. Try again.
              }
            }
          </p>
          <div class="message-discovery__advanced-launcher">
            <button
              #advancedTrigger
              type="button"
              data-testid="message-advanced-filters-open"
              aria-haspopup="dialog"
              [attr.aria-expanded]="advancedDrawerOpen()"
              (click)="openAdvancedDrawer()"
            >
              Advanced filters
            </button>
            <span data-testid="message-advanced-summary">{{ advancedSummary() }}</span>
          </div>
        </form>

        <div class="message-discovery__quick-filters" role="group" aria-labelledby="message-quick-filter-label">
          <span id="message-quick-filter-label">Inbox views</span>
          <div class="message-discovery__quick-filter-buttons">
            <button
              #allFilter
              type="button"
              data-testid="message-filter-all"
              [class.message-discovery__quick-filter--active]="displayInboxView() === 'All'"
              [attr.aria-pressed]="displayInboxView() === 'All'"
              (click)="selectInboxView('All')"
            >
              All <span aria-hidden="true">{{ inboxState().counts.all }}</span>
              <span class="visually-hidden">conversations</span>
            </button>
            <button
              #unreadFilter
              type="button"
              data-testid="message-filter-unread"
              [class.message-discovery__quick-filter--active]="displayInboxView() === 'Unread'"
              [attr.aria-pressed]="displayInboxView() === 'Unread'"
              [disabled]="!inboxNavigationAvailable()"
              (click)="selectInboxView('Unread')"
            >
              Unread <span aria-hidden="true">{{ inboxState().counts.unread }}</span>
              <span class="visually-hidden">conversations</span>
            </button>
            <button
              #mentionsFilter
              type="button"
              data-testid="message-filter-mentions"
              [class.message-discovery__quick-filter--active]="displayInboxView() === 'Mentions'"
              [attr.aria-pressed]="displayInboxView() === 'Mentions'"
              [disabled]="!inboxNavigationAvailable()"
              (click)="selectInboxView('Mentions')"
            >
              Mentions <span aria-hidden="true">{{ inboxState().counts.mentions }}</span>
              <span class="visually-hidden">conversations</span>
            </button>
            <button
              #laterFilter
              type="button"
              data-testid="message-filter-later"
              [class.message-discovery__quick-filter--active]="displayInboxView() === 'Later'"
              [attr.aria-pressed]="displayInboxView() === 'Later'"
              [disabled]="!inboxNavigationAvailable()"
              (click)="selectInboxView('Later')"
            >
              Later <span aria-hidden="true">{{ inboxState().counts.later }}</span>
              <span class="visually-hidden">conversations</span>
            </button>
          </div>
          <p class="message-discovery__scope">Unread is based on your read cursor. Mentions and Later remain separate.</p>
          <p class="message-discovery__status" data-testid="message-inbox-status" aria-live="polite">
            @if (inboxState().status === 'loading') {
              Loading {{ displayInboxView() }} conversations.
            } @else if (inboxState().status === 'error') {
              {{ inboxState().error }}
            } @else if (inboxState().status === 'unavailable') {
              Conversation categories are unavailable. The authorized All list remains visible.
            }
          </p>
        </div>
      </div>

      @if (advancedDrawerOpen()) {
        <div class="message-discovery__drawer-overlay" role="presentation" (click)="handleDrawerOverlayClick($event)">
          <section
            #advancedDrawer
            class="message-discovery__drawer"
            role="dialog"
            aria-modal="true"
            aria-labelledby="message-advanced-title"
            aria-describedby="message-advanced-help"
            data-testid="message-advanced-filters-drawer"
            (keydown)="handleAdvancedDrawerKeydown($event)"
          >
            <header class="message-discovery__drawer-header">
              <div>
                <p class="message-discovery__drawer-eyebrow">Message search</p>
                <h2 id="message-advanced-title">Advanced filters</h2>
              </div>
              <button type="button" aria-label="Close advanced filters" (click)="cancelAdvancedDrawer()">Close</button>
            </header>

            <form class="message-discovery__advanced-form" (submit)="applyAdvancedFilters($event)">
              <div class="message-discovery__author-field">
                <label for="message-author-filter">From</label>
                <input
                  #authorInput
                  id="message-author-filter"
                  type="search"
                  autocomplete="off"
                  maxlength="120"
                  data-testid="message-advanced-author"
                  aria-describedby="message-author-status"
                  [value]="draftAuthorQuery()"
                  (input)="updateDraftAuthorQuery($event)"
                />
                <p id="message-author-status" class="message-discovery__status" aria-live="polite">
                  @switch (authorSearchStatus()) {
                    @case ('idle') { Enter at least 2 characters and choose an authorized sender. }
                    @case ('loading') { Finding authorized senders. }
                    @case ('empty') { No authorized sender matches. }
                    @case ('error') { Sender options are unavailable. Try again. }
                    @case ('ready') {
                      @if (draftAuthor()) { Selected {{ draftAuthor()?.displayName }}. } @else { Choose a sender. }
                    }
                  }
                </p>
                @if (authorOptions().length > 0 && !draftAuthor()) {
                  <ul class="message-discovery__author-options" aria-label="Authorized senders">
                    @for (author of authorOptions(); track author.userId) {
                      <li>
                        <button
                          type="button"
                          data-testid="message-author-option"
                          (click)="selectDraftAuthor(author)"
                        >
                          {{ author.displayName }}
                        </button>
                      </li>
                    }
                  </ul>
                }
              </div>

              <div class="message-discovery__date-fields">
                <label>
                  <span>From date</span>
                  <input
                    type="date"
                    data-testid="message-advanced-from-date"
                    [value]="draftFromDate()"
                    (input)="updateDraftFromDate($event)"
                  />
                </label>
                <label>
                  <span>To date</span>
                  <input
                    type="date"
                    data-testid="message-advanced-to-date"
                    [value]="draftToDate()"
                    (input)="updateDraftToDate($event)"
                  />
                </label>
              </div>

              <label>
                <span>Status</span>
                <select
                  data-testid="message-advanced-read"
                  [value]="draftRead()"
                  (change)="updateDraftRead($event)"
                >
                  <option value="All">Any read status</option>
                  <option value="Read">Read</option>
                  <option value="Unread">Unread</option>
                </select>
              </label>

              <label>
                <span>Attachment</span>
                <select
                  data-testid="message-advanced-attachment"
                  [value]="draftAttachment()"
                  (change)="updateDraftAttachment($event)"
                >
                  <option value="All">With or without</option>
                  <option value="With">With safe attachment</option>
                  <option value="Without">Without safe attachment</option>
                </select>
              </label>

              <p id="message-advanced-help" class="message-discovery__scope">
                Attachment status uses only clean, classified server file links. Legacy metadata-only rows count as without.
              </p>
              @if (advancedFilterError()) {
                <p class="message-discovery__error" data-testid="message-advanced-error" role="alert">
                  {{ advancedFilterError() }}
                </p>
              }

              <footer class="message-discovery__drawer-actions">
                <button type="button" data-testid="message-advanced-reset" (click)="resetAdvancedDraft()">Reset</button>
                <button type="button" (click)="cancelAdvancedDrawer()">Cancel</button>
                <button type="submit" data-testid="message-advanced-apply">Apply filters</button>
              </footer>
            </form>
          </section>
        </div>
      }

      @if (hasActiveConditions()) {
        <section class="message-discovery__active" aria-labelledby="message-active-filter-label">
          <div class="message-discovery__active-heading">
            <h2 id="message-active-filter-label">Active conditions</h2>
            <button type="button" data-testid="message-filters-clear-all" (click)="clearAll()">Clear all</button>
          </div>
          <div class="message-discovery__active-chips" data-testid="message-active-filters">
            @if (appliedQuery()) {
              <app-coglatas-filter-chip
                data-testid="message-active-search-chip"
                label="Search"
                [value]="appliedQuery()"
                (removed)="clearSearch(true)"
              />
            }
            @if (appliedAdvanced().author; as author) {
              <app-coglatas-filter-chip
                data-testid="message-active-author-chip"
                label="From"
                [value]="author.displayName"
                (removed)="clearAdvancedFilter('author')"
              />
            }
            @if (appliedAdvanced().fromDate) {
              <app-coglatas-filter-chip
                data-testid="message-active-from-date-chip"
                label="From date"
                [value]="appliedAdvanced().fromDate"
                (removed)="clearAdvancedFilter('fromDate')"
              />
            }
            @if (appliedAdvanced().toDate) {
              <app-coglatas-filter-chip
                data-testid="message-active-to-date-chip"
                label="To date"
                [value]="appliedAdvanced().toDate"
                (removed)="clearAdvancedFilter('toDate')"
              />
            }
            @if (appliedAdvanced().read !== 'All') {
              <app-coglatas-filter-chip
                data-testid="message-active-read-chip"
                label="Status"
                [value]="appliedAdvanced().read"
                (removed)="clearAdvancedFilter('read')"
              />
            }
            @if (appliedAdvanced().attachment !== 'All') {
              <app-coglatas-filter-chip
                data-testid="message-active-attachment-chip"
                label="Attachment"
                [value]="attachmentLabel(appliedAdvanced().attachment)"
                (removed)="clearAdvancedFilter('attachment')"
              />
            }
            @if (inboxState().view === 'Unread') {
              <app-coglatas-filter-chip
                data-testid="message-active-unread-chip"
                label="Inbox"
                value="Unread"
                (removed)="clearInboxView('Unread')"
              />
            }
            @if (inboxState().view === 'Mentions') {
              <app-coglatas-filter-chip
                data-testid="message-active-mentions-chip"
                label="Inbox"
                value="Mentions"
                (removed)="clearInboxView('Mentions')"
              />
            }
            @if (inboxState().view === 'Later') {
              <app-coglatas-filter-chip
                data-testid="message-active-later-chip"
                label="Inbox"
                value="Later"
                (removed)="clearInboxView('Later')"
              />
            }
          </div>
        </section>
      }

      @if (hasMessageSearchConditions()) {
        <section class="message-discovery__matches" aria-labelledby="message-search-results-title">
          <h2 id="message-search-results-title">Message matches</h2>
          @if (searchStatus() === 'loading') {
            <p class="message-discovery__muted" data-testid="message-search-loading">Searching messages...</p>
          } @else if (searchStatus() === 'error') {
            <div class="message-discovery__result-state" data-testid="message-search-error">
              <p>Message search is temporarily unavailable. No server error detail is shown.</p>
              <button type="button" (click)="retrySearch()">Try again</button>
            </div>
          } @else if (searchStatus() === 'empty') {
            <div class="message-discovery__result-state" data-testid="message-search-empty">
              <p>No messages match the active conditions. Change the search or clear the active conditions.</p>
              <div class="message-discovery__result-actions">
                <button type="button" data-testid="message-search-change" (click)="changeSearch()">
                  Change search
                </button>
                <button type="button" data-testid="message-search-clear-empty" (click)="clearAll()">Clear all</button>
              </div>
            </div>
          } @else if (searchStatus() === 'ready') {
            <ul class="message-discovery__results" data-testid="message-search-results">
              @for (result of searchResults(); track result.messageId) {
                <li>
                  <a class="message-discovery__result" data-testid="message-search-result" [routerLink]="result.route">
                    <span class="message-discovery__result-title">{{ result.title }}</span>
                    @if (result.snippet) {
                      <span class="message-discovery__result-snippet">{{ result.snippet }}</span>
                    }
                    @if (result.authorDisplayName) {
                      <span class="message-discovery__result-author">From {{ result.authorDisplayName }}</span>
                    }
                  </a>
                </li>
              }
            </ul>
          }
        </section>
      }

      <section class="message-discovery__conversations" aria-labelledby="message-conversation-results-title">
        <h2 id="message-conversation-results-title">Conversations</h2>
        @if (conversationState().length > 0) {
          <app-conversation-list
            [conversations]="conversationState()"
            [selectedConversationId]="selectedConversationId"
            [preserveListScroll]="preserveListScroll"
            [showUnreadBadges]="showUnreadBadges"
            [showLaterActions]="inboxState().status !== 'unavailable'"
            [laterPendingConversationId]="inboxState().laterPendingConversationId ?? null"
            (laterChanged)="requestLaterChange($event)"
          />
        } @else if (inboxState().view !== 'All' || inboxState().counts.all > 0) {
          <div class="message-discovery__result-state" data-testid="message-conversation-filter-empty">
            <p>No conversations are currently in the {{ inboxState().view }} view.</p>
            <button
              type="button"
              data-testid="message-conversation-filters-clear"
              (click)="clearInboxView(inboxState().view)"
            >
              Return to All conversations
            </button>
          </div>
        } @else {
          <ng-content select="[message-search-empty]" />
        }
      </section>
    </section>
  `,
  styleUrl: './message-search-filters.component.scss',
})
export class MessageSearchFiltersComponent implements OnDestroy {
  private readonly api = inject(MessagingApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private request: Subscription | null = null;
  private authorRequest: Subscription | null = null;
  private routeAuthorRequest: Subscription | null = null;
  private readonly routeSubscription: Subscription;
  private requestGeneration = 0;
  private authorRequestGeneration = 0;
  private routeHydrationGeneration = 0;
  private restoreLaterFilterAfterMutation = false;

  @ViewChild('searchInput') private searchInput?: ElementRef<HTMLInputElement>;
  @ViewChild('mobileToggle') private mobileToggle?: ElementRef<HTMLButtonElement>;
  @ViewChild('allFilter') private allFilter?: ElementRef<HTMLButtonElement>;
  @ViewChild('unreadFilter') private unreadFilter?: ElementRef<HTMLButtonElement>;
  @ViewChild('mentionsFilter') private mentionsFilter?: ElementRef<HTMLButtonElement>;
  @ViewChild('laterFilter') private laterFilter?: ElementRef<HTMLButtonElement>;
  @ViewChild('advancedTrigger') private advancedTrigger?: ElementRef<HTMLButtonElement>;
  @ViewChild('advancedDrawer') private advancedDrawer?: ElementRef<HTMLElement>;
  @ViewChild('authorInput') private authorInput?: ElementRef<HTMLInputElement>;

  readonly conversationState = signal<readonly MessagingConversationListItem[]>([]);
  readonly inboxState = signal<MessagingInboxViewModel>(EMPTY_INBOX);

  @Input({ required: true })
  set conversations(value: readonly MessagingConversationListItem[]) {
    this.conversationState.set(value ?? []);
  }

  @Input({ required: true })
  set inbox(value: MessagingInboxViewModel) {
    const previousPending = this.inboxState().laterPendingConversationId;
    this.inboxState.set(value ?? EMPTY_INBOX);
    if (previousPending && !value?.laterPendingConversationId && this.restoreLaterFilterAfterMutation) {
      this.restoreLaterFilterAfterMutation = false;
      this.scheduleFocus(this.laterFilter);
    }
  }

  @Input() selectedConversationId: string | null = null;
  @Input() preserveListScroll = false;
  @Input() showUnreadBadges = true;
  @Output() readonly inboxViewChanged = new EventEmitter<MessagingInboxView>();
  @Output() readonly conversationLaterChanged = new EventEmitter<{
    conversationId: string;
    isLater: boolean;
  }>();

  readonly query = signal('');
  readonly appliedQuery = signal('');
  readonly mobilePanelOpen = signal(false);
  readonly advancedDrawerOpen = signal(false);
  readonly searchStatus = signal<MessageSearchStatus>('idle');
  readonly searchResults = signal<readonly MessageSearchResult[]>([]);
  readonly appliedAdvanced = signal<MessageAdvancedFilters>(EMPTY_ADVANCED_FILTERS);
  readonly draftAuthor = signal<MessageAuthorOption | null>(null);
  readonly draftAuthorQuery = signal('');
  readonly draftFromDate = signal('');
  readonly draftToDate = signal('');
  readonly draftRead = signal<MessageReadFilterDto>('All');
  readonly draftAttachment = signal<MessageAttachmentFilterDto>('All');
  readonly authorSearchStatus = signal<AuthorSearchStatus>('idle');
  readonly authorOptions = signal<readonly MessageAuthorOption[]>([]);
  readonly advancedFilterError = signal<string | null>(null);
  readonly hasAdvancedConditions = computed(() => advancedFilterCount(this.appliedAdvanced()) > 0);
  readonly hasMessageSearchConditions = computed(
    () => Boolean(this.appliedQuery()) || this.hasAdvancedConditions()
  );
  readonly advancedSummary = computed(() => {
    const count = advancedFilterCount(this.appliedAdvanced());
    return count === 0 ? 'No advanced filters applied' : `${count} advanced ${count === 1 ? 'filter' : 'filters'} applied`;
  });
  readonly hasActiveConditions = computed(
    () => this.hasMessageSearchConditions() || this.inboxState().view !== 'All'
  );
  readonly displayInboxView = computed(
    () => this.inboxState().requestedView ?? this.inboxState().view
  );
  readonly inboxNavigationAvailable = computed(
    () => this.inboxState().status !== 'unavailable'
  );

  constructor() {
    this.routeSubscription = this.route.queryParamMap.subscribe((params) => this.restoreAdvancedFilters(params));
  }

  ngOnDestroy(): void {
    this.cancelRequest();
    this.cancelAuthorRequest();
    this.routeHydrationGeneration++;
    this.routeAuthorRequest?.unsubscribe();
    this.routeAuthorRequest = null;
    this.routeSubscription.unsubscribe();
  }

  toggleMobilePanel(): void {
    const open = !this.mobilePanelOpen();
    this.mobilePanelOpen.set(open);
    if (open) {
      setTimeout(() => this.searchInput?.nativeElement.focus());
    }
  }

  closeMobilePanel(event: KeyboardEvent): void {
    if (!this.mobilePanelOpen()) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    this.mobilePanelOpen.set(false);
    setTimeout(() => this.mobileToggle?.nativeElement.focus());
  }

  updateQuery(event: Event): void {
    const value = event.target instanceof HTMLInputElement ? event.target.value : '';
    this.query.set(value);
    if (this.appliedQuery() && value.trim() !== this.appliedQuery()) {
      this.cancelRequest();
      this.appliedQuery.set('');
      this.searchResults.set([]);
      if (this.hasAdvancedConditions()) {
        this.startSearch('', this.appliedAdvanced());
      } else {
        this.searchStatus.set('idle');
      }
    }
  }

  submitSearch(event: Event): void {
    event.preventDefault();
    const query = this.query().trim();
    if ((query.length > 0 && query.length < 2) || (!query && !this.hasAdvancedConditions())) {
      this.cancelRequest();
      this.appliedQuery.set('');
      this.searchResults.set([]);
      this.searchStatus.set('invalid');
      return;
    }

    this.startSearch(query, this.appliedAdvanced());
  }

  retrySearch(): void {
    const query = this.appliedQuery();
    if (query || this.hasAdvancedConditions()) {
      this.startSearch(query, this.appliedAdvanced());
    }
  }

  selectInboxView(view: MessagingInboxView): void {
    const inbox = this.inboxState();
    if (
      inbox.status === 'loading' ||
      (inbox.status === 'unavailable' && view !== 'All') ||
      (inbox.status === 'ready' && inbox.view === view)
    ) {
      return;
    }
    this.inboxViewChanged.emit(view);
  }

  clearSearch(focusInput = false): void {
    this.cancelRequest();
    this.query.set('');
    this.appliedQuery.set('');
    this.searchResults.set([]);
    if (this.hasAdvancedConditions()) {
      this.startSearch('', this.appliedAdvanced());
    } else {
      this.searchStatus.set('idle');
    }
    if (focusInput) {
      this.scheduleFocus(this.searchInput);
    }
  }

  changeSearch(): void {
    if (this.isMobileViewport()) {
      this.mobilePanelOpen.set(true);
    }
    this.clearSearch(true);
  }

  clearInboxView(returnFocusView: MessagingInboxView): void {
    if (this.inboxState().view !== 'All') {
      this.inboxViewChanged.emit('All');
    }
    this.scheduleFocus(this.filterElement(returnFocusView));
  }

  requestLaterChange(change: { conversationId: string; isLater: boolean }): void {
    this.restoreLaterFilterAfterMutation = this.inboxState().view === 'Later' && !change.isLater;
    this.conversationLaterChanged.emit(change);
  }

  clearAll(): void {
    this.beginLocalAdvancedMutation();
    this.cancelRequest();
    this.query.set('');
    this.appliedQuery.set('');
    this.searchResults.set([]);
    this.searchStatus.set('idle');
    this.commitAdvancedFilters(EMPTY_ADVANCED_FILTERS, false);
    this.writeAdvancedFiltersToUrl(EMPTY_ADVANCED_FILTERS);
    if (this.inboxState().view !== 'All') {
      this.inboxViewChanged.emit('All');
    }
    this.scheduleFocus(this.isMobileViewport() && !this.mobilePanelOpen() ? this.mobileToggle : this.searchInput);
  }

  openAdvancedDrawer(): void {
    const applied = this.appliedAdvanced();
    this.cancelAuthorRequest();
    this.draftAuthor.set(applied.author);
    this.draftAuthorQuery.set(applied.author?.displayName ?? '');
    this.draftFromDate.set(applied.fromDate);
    this.draftToDate.set(applied.toDate);
    this.draftRead.set(applied.read);
    this.draftAttachment.set(applied.attachment);
    this.authorOptions.set([]);
    this.authorSearchStatus.set(applied.author ? 'ready' : 'idle');
    this.advancedFilterError.set(null);
    this.advancedDrawerOpen.set(true);
    setTimeout(() => this.authorInput?.nativeElement.focus());
  }

  cancelAdvancedDrawer(): void {
    if (!this.advancedDrawerOpen()) {
      return;
    }
    this.cancelAuthorRequest();
    this.advancedDrawerOpen.set(false);
    setTimeout(() => this.advancedTrigger?.nativeElement.focus());
  }

  handleDrawerOverlayClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.cancelAdvancedDrawer();
    }
  }

  handleAdvancedDrawerKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.cancelAdvancedDrawer();
      return;
    }
    if (event.key !== 'Tab') {
      return;
    }

    const eventTarget = event.target instanceof HTMLElement ? event.target : null;
    const drawer = this.advancedDrawer?.nativeElement
      ?? eventTarget?.closest<HTMLElement>('[role="dialog"]')
      ?? (event.currentTarget instanceof HTMLElement ? event.currentTarget : null);
    const focusable = drawer
      ? Array.from(drawer.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled)'))
      : [];
    if (focusable.length === 0) {
      return;
    }
    const first = drawer?.querySelector<HTMLElement>('[aria-label="Close advanced filters"]') ?? focusable[0];
    const last = drawer?.querySelector<HTMLElement>('[data-testid="message-advanced-apply"]')
      ?? focusable[focusable.length - 1];
    const focused = eventTarget ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
    if (event.shiftKey && focused === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && focused === last) {
      event.preventDefault();
      first.focus();
    }
  }

  resetAdvancedDraft(): void {
    this.cancelAuthorRequest();
    this.draftAuthor.set(null);
    this.draftAuthorQuery.set('');
    this.draftFromDate.set('');
    this.draftToDate.set('');
    this.draftRead.set('All');
    this.draftAttachment.set('All');
    this.authorOptions.set([]);
    this.authorSearchStatus.set('idle');
    this.advancedFilterError.set(null);
    setTimeout(() => this.authorInput?.nativeElement.focus());
  }

  updateDraftAuthorQuery(event: Event): void {
    const value = inputValue(event);
    this.draftAuthorQuery.set(value);
    if (value.trim() !== this.draftAuthor()?.displayName) {
      this.draftAuthor.set(null);
    }
    this.advancedFilterError.set(null);
    this.cancelAuthorRequest();
    this.authorOptions.set([]);
    const query = value.trim();
    if (query.length < 2) {
      this.authorSearchStatus.set('idle');
      return;
    }

    const generation = ++this.authorRequestGeneration;
    this.authorSearchStatus.set('loading');
    const request = this.api.searchMessageAuthors(query).subscribe({
      next: (response) => {
        if (generation !== this.authorRequestGeneration || !this.advancedDrawerOpen()) {
          return;
        }
        const options = parseAuthorOptions(response);
        this.authorOptions.set(options);
        this.authorSearchStatus.set(options.length > 0 ? 'ready' : 'empty');
      },
      error: () => {
        if (generation === this.authorRequestGeneration && this.advancedDrawerOpen()) {
          this.authorOptions.set([]);
          this.authorSearchStatus.set('error');
        }
      }
    });
    this.authorRequest = request;
    request.add(() => {
      if (this.authorRequest === request) {
        this.authorRequest = null;
      }
    });
  }

  selectDraftAuthor(author: MessageAuthorOption): void {
    this.cancelAuthorRequest();
    this.draftAuthor.set(author);
    this.draftAuthorQuery.set(author.displayName);
    this.authorOptions.set([]);
    this.authorSearchStatus.set('ready');
    this.advancedFilterError.set(null);
    // The selected option is removed from the DOM. Keep keyboard focus inside
    // the modal by returning it to the surviving From control.
    setTimeout(() => this.authorInput?.nativeElement.focus());
  }

  updateDraftFromDate(event: Event): void {
    this.draftFromDate.set(inputValue(event));
    this.advancedFilterError.set(null);
  }

  updateDraftToDate(event: Event): void {
    this.draftToDate.set(inputValue(event));
    this.advancedFilterError.set(null);
  }

  updateDraftRead(event: Event): void {
    const value = inputValue(event);
    this.draftRead.set(value === 'Read' || value === 'Unread' ? value : 'All');
  }

  updateDraftAttachment(event: Event): void {
    const value = inputValue(event);
    this.draftAttachment.set(value === 'With' || value === 'Without' ? value : 'All');
  }

  applyAdvancedFilters(event: Event): void {
    event.preventDefault();
    const authorQuery = this.draftAuthorQuery().trim();
    if (authorQuery && !this.draftAuthor()) {
      this.advancedFilterError.set('Choose an authorized sender from the results or clear From.');
      this.authorInput?.nativeElement.focus();
      return;
    }
    const fromDate = this.draftFromDate();
    const toDate = this.draftToDate();
    if ((fromDate && !isCalendarDate(fromDate)) || (toDate && !isCalendarDate(toDate))) {
      this.advancedFilterError.set('Choose valid calendar dates.');
      return;
    }
    if (fromDate && toDate && fromDate > toDate) {
      this.advancedFilterError.set('From date must be on or before To date.');
      return;
    }

    const filters: MessageAdvancedFilters = {
      author: this.draftAuthor(),
      fromDate,
      toDate,
      read: this.draftRead(),
      attachment: this.draftAttachment()
    };
    const cancelledRouteHydration = this.beginLocalAdvancedMutation();
    this.commitAdvancedFilters(filters, true, cancelledRouteHydration);
    this.writeAdvancedFiltersToUrl(filters);
    this.cancelAdvancedDrawer();
  }

  clearAdvancedFilter(key: AdvancedFilterKey): void {
    const current = this.appliedAdvanced();
    const filters: MessageAdvancedFilters = {
      ...current,
      ...(key === 'author' ? { author: null } : {}),
      ...(key === 'fromDate' ? { fromDate: '' } : {}),
      ...(key === 'toDate' ? { toDate: '' } : {}),
      ...(key === 'read' ? { read: 'All' as const } : {}),
      ...(key === 'attachment' ? { attachment: 'All' as const } : {})
    };
    const cancelledRouteHydration = this.beginLocalAdvancedMutation();
    this.commitAdvancedFilters(filters, true, cancelledRouteHydration);
    this.writeAdvancedFiltersToUrl(filters);
    this.scheduleFocus(this.isMobileViewport() && !this.mobilePanelOpen() ? this.mobileToggle : this.advancedTrigger);
  }

  attachmentLabel(value: MessageAttachmentFilterDto): string {
    return value === 'With' ? 'With safe attachment' : 'Without safe attachment';
  }

  private restoreAdvancedFilters(params: ParamMap): void {
    const generation = ++this.routeHydrationGeneration;
    this.routeAuthorRequest?.unsubscribe();
    this.routeAuthorRequest = null;
    const parsed = parseAdvancedRoute(params);
    const privateKeys = PRIVATE_MESSAGE_SEARCH_QUERY_KEYS.filter((key) => params.has(key));
    const keysToRemove = [...parsed.invalidKeys, ...privateKeys];
    if (keysToRemove.length > 0) {
      this.removeAdvancedUrlKeys(keysToRemove);
    }

    if (!parsed.authorUserId) {
      this.commitAdvancedFilters(parsed.filters, true);
      return;
    }

    const knownAuthor = this.appliedAdvanced().author;
    if (knownAuthor?.userId.toLowerCase() === parsed.authorUserId.toLowerCase()) {
      this.commitAdvancedFilters({ ...parsed.filters, author: knownAuthor }, true);
      return;
    }

    this.cancelRequest();
    this.searchResults.set([]);
    this.searchStatus.set('loading');
    this.appliedAdvanced.set(parsed.filters);
    const request = this.api.resolveMessageAuthor(parsed.authorUserId).subscribe({
      next: (response) => {
        if (generation !== this.routeHydrationGeneration) {
          return;
        }
        const author = parseAuthorOptions(response)
          .find((option) => option.userId.toLowerCase() === parsed.authorUserId!.toLowerCase()) ?? null;
        if (!author) {
          this.removeAdvancedUrlKeys([ADVANCED_QUERY_KEYS.author]);
        }
        this.commitAdvancedFilters({ ...parsed.filters, author }, true, true);
      },
      error: () => {
        if (generation === this.routeHydrationGeneration) {
          this.removeAdvancedUrlKeys([ADVANCED_QUERY_KEYS.author]);
          this.commitAdvancedFilters(parsed.filters, true, true);
        }
      }
    });
    this.routeAuthorRequest = request;
    request.add(() => {
      if (this.routeAuthorRequest === request) {
        this.routeAuthorRequest = null;
      }
    });
  }

  private commitAdvancedFilters(
    filters: MessageAdvancedFilters,
    runSearch: boolean,
    forceSearch = false
  ): void {
    const changed = filterFingerprint(this.appliedAdvanced()) !== filterFingerprint(filters);
    this.appliedAdvanced.set(filters);
    if (!runSearch || (!changed && !forceSearch)) {
      return;
    }

    this.cancelRequest();
    this.searchResults.set([]);
    if (this.appliedQuery() || advancedFilterCount(filters) > 0) {
      this.startSearch(this.appliedQuery(), filters);
    } else {
      this.searchStatus.set('idle');
    }
  }

  private writeAdvancedFiltersToUrl(filters: MessageAdvancedFilters): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParamsHandling: 'merge',
      queryParams: {
        q: null,
        messageSearch: null,
        messageQuery: null,
        [ADVANCED_QUERY_KEYS.author]: filters.author?.userId ?? null,
        [ADVANCED_QUERY_KEYS.fromDate]: filters.fromDate || null,
        [ADVANCED_QUERY_KEYS.toDate]: filters.toDate || null,
        [ADVANCED_QUERY_KEYS.read]: filters.read === 'All' ? null : filters.read,
        [ADVANCED_QUERY_KEYS.attachment]: filters.attachment === 'All' ? null : filters.attachment
      }
    });
  }

  private beginLocalAdvancedMutation(): boolean {
    // Invalidate synchronously before changing local state. Router navigation
    // emits later, so a held deep-link author response must not be able to
    // restore the route snapshot between the local commit and that emission.
    const cancelledRouteHydration = this.routeAuthorRequest !== null;
    this.routeHydrationGeneration++;
    this.routeAuthorRequest?.unsubscribe();
    this.routeAuthorRequest = null;
    return cancelledRouteHydration;
  }

  private removeAdvancedUrlKeys(keys: readonly string[]): void {
    if (keys.length === 0) {
      return;
    }
    const queryParams = Object.fromEntries(keys.map((key) => [key, null]));
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParamsHandling: 'merge',
      queryParams,
      replaceUrl: true
    });
  }

  private cancelAuthorRequest(): void {
    this.authorRequestGeneration++;
    this.authorRequest?.unsubscribe();
    this.authorRequest = null;
  }

  private startSearch(query: string, advanced: MessageAdvancedFilters): void {
    const generation = ++this.requestGeneration;
    const advancedFingerprint = filterFingerprint(advanced);
    this.request?.unsubscribe();
    this.request = null;
    this.query.set(query);
    this.appliedQuery.set(query);
    this.searchResults.set([]);
    this.searchStatus.set('loading');

    const request = this.api.searchMessages(toSearchRequest(query, advanced)).subscribe({
      next: (response) => {
        if (!this.isCurrent(generation, query, advancedFingerprint)) {
          return;
        }

        const results = this.parseResponse(response);
        this.searchResults.set(results);
        this.searchStatus.set(results.length > 0 ? 'ready' : 'empty');
      },
      error: () => {
        if (this.isCurrent(generation, query, advancedFingerprint)) {
          this.searchResults.set([]);
          this.searchStatus.set('error');
        }
      }
    });
    this.request = request;
    request.add(() => {
      if (this.request === request) {
        this.request = null;
      }
    });
  }

  private parseResponse(response: MessageSearchResponseDto): MessageSearchResult[] {
    if (!Array.isArray(response?.items)) {
      return [];
    }

    const conversationsById = new Map(
      this.conversationState().map((conversation) => [conversation.id.toLowerCase(), conversation])
    );
    const results: MessageSearchResult[] = [];
    const seenMessageIds = new Set<string>();

    for (const raw of response.items) {
      if (!isRecord(raw) || (raw['type'] !== 5 && raw['type'] !== 'Message')) {
        continue;
      }

      const messageId = stringValue(raw['id']);
      const title = stringValue(raw['title'])?.trim();
      const route = stringValue(raw['route']);
      const routeMatch = route?.match(CONVERSATION_ROUTE_PATTERN);
      const conversationId = routeMatch?.[1];
      if (
        !messageId ||
        !UUID_PATTERN.test(messageId) ||
        !title ||
        !conversationId ||
        !UUID_PATTERN.test(conversationId) ||
        seenMessageIds.has(messageId.toLowerCase())
      ) {
        continue;
      }

      const knownConversation = conversationsById.get(conversationId.toLowerCase());
      seenMessageIds.add(messageId.toLowerCase());
      results.push({
        messageId,
        conversationId,
        title,
        snippet: stringValue(raw['snippet'])?.trim().slice(0, 240) ?? '',
        authorDisplayName: stringValue(raw['authorDisplayName'])?.trim().slice(0, 120) ?? '',
        route: knownConversation?.route ?? `/conversations/${conversationId}`
      });
    }

    return results;
  }

  private cancelRequest(): void {
    this.requestGeneration++;
    this.request?.unsubscribe();
    this.request = null;
  }

  private isCurrent(generation: number, query: string, advancedFingerprint: string): boolean {
    return generation === this.requestGeneration &&
      this.appliedQuery() === query &&
      filterFingerprint(this.appliedAdvanced()) === advancedFingerprint;
  }

  private filterElement(view: MessagingInboxView): ElementRef<HTMLButtonElement> | undefined {
    switch (view) {
      case 'Unread':
        return this.unreadFilter;
      case 'Mentions':
        return this.mentionsFilter;
      case 'Later':
        return this.laterFilter;
      default:
        return this.allFilter;
    }
  }

  private scheduleFocus(target?: ElementRef<HTMLElement>): void {
    setTimeout(() => {
      const focusTarget = this.isMobileViewport() && !this.mobilePanelOpen() ? this.mobileToggle : target;
      focusTarget?.nativeElement.focus();
    });
  }

  private isMobileViewport(): boolean {
    return (
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(max-width: 640px)').matches
    );
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function stringValue(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function inputValue(event: Event): string {
  const target = event.target;
  return target instanceof HTMLInputElement || target instanceof HTMLSelectElement ? target.value : '';
}

function advancedFilterCount(filters: MessageAdvancedFilters): number {
  return Number(Boolean(filters.author)) +
    Number(Boolean(filters.fromDate)) +
    Number(Boolean(filters.toDate)) +
    Number(filters.read !== 'All') +
    Number(filters.attachment !== 'All');
}

function filterFingerprint(filters: MessageAdvancedFilters): string {
  return [
    filters.author?.userId.toLowerCase() ?? '',
    filters.author?.displayName ?? '',
    filters.fromDate,
    filters.toDate,
    filters.read,
    filters.attachment
  ].join('|');
}

function toSearchRequest(query: string, filters: MessageAdvancedFilters): MessageSearchRequestDto {
  return {
    query: query || undefined,
    authorUserId: filters.author?.userId,
    fromDate: filters.fromDate ? localCalendarBoundary(filters.fromDate, false) : undefined,
    toDateExclusive: filters.toDate ? localCalendarBoundary(filters.toDate, true) : undefined,
    messageRead: filters.read,
    messageAttachment: filters.attachment
  };
}

function localCalendarBoundary(value: string, nextDay: boolean): string {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day + (nextDay ? 1 : 0), 0, 0, 0, 0).toISOString();
}

function isCalendarDate(value: string): boolean {
  if (!DATE_PATTERN.test(value)) {
    return false;
  }
  const [year, month, day] = value.split('-').map(Number);
  const parsed = new Date(year, month - 1, day);
  return year >= 1000 &&
    parsed.getFullYear() === year &&
    parsed.getMonth() === month - 1 &&
    parsed.getDate() === day;
}

function parseAuthorOptions(response: MessageAuthorOptionsResponseDto): MessageAuthorOption[] {
  if (!Array.isArray(response?.items)) {
    return [];
  }
  const seen = new Set<string>();
  const options: MessageAuthorOption[] = [];
  for (const raw of response.items) {
    if (!isRecord(raw)) {
      continue;
    }
    const userId = stringValue(raw['userId'])?.trim();
    const displayName = stringValue(raw['displayName'])?.trim().slice(0, 120);
    if (!userId || !UUID_PATTERN.test(userId) || !displayName || seen.has(userId.toLowerCase())) {
      continue;
    }
    seen.add(userId.toLowerCase());
    options.push({ userId, displayName });
  }
  return options;
}

function parseAdvancedRoute(params: ParamMap): {
  readonly filters: MessageAdvancedFilters;
  readonly authorUserId: string | null;
  readonly invalidKeys: readonly string[];
} {
  const invalidKeys: string[] = [];
  const rawAuthor = params.get(ADVANCED_QUERY_KEYS.author);
  const authorUserId = rawAuthor && UUID_PATTERN.test(rawAuthor) ? rawAuthor : null;
  if (rawAuthor !== null && !authorUserId) {
    invalidKeys.push(ADVANCED_QUERY_KEYS.author);
  }

  const rawFromDate = params.get(ADVANCED_QUERY_KEYS.fromDate);
  const fromDate = rawFromDate && isCalendarDate(rawFromDate) ? rawFromDate : '';
  if (rawFromDate !== null && !fromDate) {
    invalidKeys.push(ADVANCED_QUERY_KEYS.fromDate);
  }

  const rawToDate = params.get(ADVANCED_QUERY_KEYS.toDate);
  let toDate = rawToDate && isCalendarDate(rawToDate) ? rawToDate : '';
  if (rawToDate !== null && !toDate) {
    invalidKeys.push(ADVANCED_QUERY_KEYS.toDate);
  }
  if (fromDate && toDate && fromDate > toDate) {
    invalidKeys.push(ADVANCED_QUERY_KEYS.toDate);
    toDate = '';
  }

  const rawRead = params.get(ADVANCED_QUERY_KEYS.read);
  const read: MessageReadFilterDto = rawRead === 'Read' || rawRead === 'Unread' ? rawRead : 'All';
  if (rawRead !== null && read === 'All') {
    invalidKeys.push(ADVANCED_QUERY_KEYS.read);
  }

  const rawAttachment = params.get(ADVANCED_QUERY_KEYS.attachment);
  const attachment: MessageAttachmentFilterDto =
    rawAttachment === 'With' || rawAttachment === 'Without' ? rawAttachment : 'All';
  if (rawAttachment !== null && attachment === 'All') {
    invalidKeys.push(ADVANCED_QUERY_KEYS.attachment);
  }

  return {
    filters: { author: null, fromDate, toDate, read, attachment },
    authorUserId,
    invalidKeys
  };
}
