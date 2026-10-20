import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { MessagingTextKey } from '../messaging.types';
import { A11yModule } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';

import { MessageGlobalSettingsService } from '../message-global-settings.service';
import { MessageNavigationStateService } from '../message-navigation-state.service';
import { MessageSearchFiltersComponent } from '../message-search-filters/message-search-filters.component';
import { ConversationRecipientDto, MessagingApi } from '../messaging.api';
import { MessagingFacade } from '../messaging.facade';
import { WorkspaceRecipientOption, WorkspaceRecipientPickerComponent } from '../workspace-recipient-picker/workspace-recipient-picker.component';

type RecipientSearchStatus = 'idle' | 'loading' | 'ready' | 'empty' | 'error';

interface RecipientOption {
  readonly userId: string;
  readonly displayName: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-messages-page',
  standalone: true,
  imports: [A11yModule, FormsModule, MessageSearchFiltersComponent, RouterLink, WorkspaceRecipientPickerComponent],
  template: `
    <section class="messages-page" data-testid="messages-page">
      <header class="messages-page__header">
        <div>
          <p class="messages-page__eyebrow">{{ i18n.translate('messaging.title') }}</p>
          <h1>{{ i18n.translate(directoryTitleKey) }}</h1>
        </div>
        <div class="messages-page__header-actions">
          <a routerLink="/messages/saved" data-testid="saved-messages-link">{{ i18n.translate('messaging.saved.title') }}</a>
          <a
            routerLink="/messages/settings"
            data-testid="global-message-settings-link"
            [attr.aria-label]="i18n.translate('messaging.settings.globalAria')"
          >
            {{ i18n.translate('messaging.settings.globalTitle') }}
          </a>
          <button
            type="button"
            class="messages-page__primary"
            data-testid="new-message-button"
            [attr.aria-label]="i18n.translate('messaging.newMessageAria')"
            [disabled]="!canCreateConversation()"
            (click)="openCreateDialog()"
          >
            {{ i18n.translate('messaging.newMessage') }}
          </button>
        </div>
      </header>

      <nav class="messages-page__directories" [attr.aria-label]="i18n.translate('messaging.directory.label')">
        <a routerLink="/dm" [attr.aria-current]="directory === 'dm' ? 'page' : null">{{ i18n.translate('messaging.directory.dm') }}</a>
        <a routerLink="/teams" [attr.aria-current]="directory === 'teams' ? 'page' : null">{{ i18n.translate('messaging.directory.teams') }}</a>
        <a routerLink="/messages" [attr.aria-current]="directory === 'all' ? 'page' : null">{{ i18n.translate('messaging.directory.all') }}</a>
      </nav>
      @if (directory !== 'all') {
        <p class="messages-page__muted" data-testid="conversation-directory-scope">{{ i18n.translate('messaging.directory.scope') }}</p>
      }

      @if (page().inlineError) {
        <p class="messages-page__error" data-testid="messages-list-error">{{ page().inlineError }}</p>
      }

      @if (page().status === 'loading') {
        <p class="messages-page__empty" role="status" data-testid="messages-list-loading">{{ i18n.translate('messaging.inbox.loading') }}</p>
      } @else if (page().status === 'permissionDenied') {
        <section class="messages-page__state" data-testid="messages-permission-denied">
          <h2>{{ i18n.translate('messaging.inbox.unavailable') }}</h2>
          <p>{{ i18n.translate('messaging.inbox.permissionHelp') }}</p>
        </section>
      } @else if (page().status === 'manualRefreshError') {
        <section class="messages-page__state" data-testid="messages-list-failed">
          <h2>{{ i18n.translate('messaging.inbox.loadFailed') }}</h2>
          <p>{{ i18n.translate('messaging.inbox.loadFailedHelp') }}</p>
          <button type="button" (click)="facade.manualRefresh()">{{ i18n.translate('common.retry') }}</button>
        </section>
      } @else {
        <app-message-search-filters
          [conversations]="directoryConversations()"
          [directoryScope]="directory !== 'all'"
          [inbox]="facade.inbox()"
          [preserveListScroll]="true"
          [showUnreadBadges]="globalSettings.showUnreadBadges()"
          (inboxViewChanged)="facade.selectInboxView($event)"
          (conversationLaterChanged)="facade.setConversationLater($event.conversationId, $event.isLater)"
        >
          @if (directoryConversations().length === 0) {
            <section message-search-empty class="messages-page__state" data-testid="messages-list-empty">
              <h2>{{ i18n.translate(directory === 'all' ? 'messaging.inbox.empty' : 'messaging.directory.empty') }}</h2>
              <p>{{ i18n.translate(directory === 'all' ? 'messaging.inbox.emptyHelp' : 'messaging.directory.emptyHelp') }}</p>
              <button
                type="button"
                class="messages-page__primary"
                [attr.aria-label]="i18n.translate('messaging.newMessageAria')"
                (click)="openCreateDialog()"
              >
                {{ i18n.translate('messaging.newMessage') }}
              </button>
            </section>
          }
        </app-message-search-filters>
        @if (facade.listPaging().totalPages > 1) {
          <nav class="messages-page__pagination" [attr.aria-label]="i18n.translate('messaging.directory.pages')" data-testid="conversation-pagination">
            <button type="button" [disabled]="facade.listPaging().page <= 1 || facade.inbox().status === 'loading'" (click)="facade.selectConversationPage(facade.listPaging().page - 1)">{{ i18n.translate('messaging.directory.previous') }}</button>
            <span role="status">{{ i18n.translate('messaging.directory.page', { page: facade.listPaging().page, total: facade.listPaging().totalPages }) }}</span>
            <button type="button" [disabled]="facade.listPaging().page >= facade.listPaging().totalPages || facade.inbox().status === 'loading'" (click)="facade.selectConversationPage(facade.listPaging().page + 1)">{{ i18n.translate('messaging.directory.next') }}</button>
          </nav>
        }
      }

      <section class="messages-page__hint" data-testid="messages-selection-hint">
        <h2>{{ i18n.translate('messaging.inbox.choose') }}</h2>
        <p>{{ i18n.translate('messaging.inbox.chooseHelp') }}</p>
      </section>

      @if (createDialogOpen()) {
        <div class="messages-page__overlay" role="presentation">
          <section
            class="messages-page__dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="new-message-title"
            data-testid="new-message-dialog"
            cdkTrapFocus
            [cdkTrapFocusAutoCapture]="true"
            (keydown.escape)="onCreateEscape($event)"
          >
            <header class="messages-page__dialog-header">
              <h2 id="new-message-title">{{ i18n.translate('messaging.newMessage') }}</h2>
              <button type="button" [attr.aria-label]="i18n.translate('common.close')" (click)="closeCreateDialog()">×</button>
            </header>
            <div class="messages-page__dialog-content">
            <fieldset class="messages-page__mode" [disabled]="creating()">
              <legend>{{ i18n.translate('messaging.group.conversationType') }}</legend>
              <label><input type="radio" name="conversation-type" value="direct" [ngModel]="createMode()" (ngModelChange)="changeCreateMode($event)" />{{ i18n.translate('messaging.directMessage') }}</label>
              <label><input type="radio" name="conversation-type" value="group" [ngModel]="createMode()" (ngModelChange)="changeCreateMode($event)" data-testid="group-create-mode" />{{ i18n.translate('messaging.group.title') }}</label>
            </fieldset>
            @if (createMode() === 'group') {
              <label class="messages-page__field"><span>{{ i18n.translate('messaging.group.optionalTitle') }}</span>
                <input type="text" maxlength="200" [ngModel]="groupTitle()" (ngModelChange)="groupTitle.set($event)" [disabled]="creating()" data-testid="group-title" />
              </label>
              <app-workspace-recipient-picker [workspaceId]="facade.activeWorkspaceId() ?? ''" [disabled]="creating()"
                [excludedUserIds]="excludedCreator()" [resetKey]="groupPickerResetKey()" (recipientsChanged)="onGroupRecipientsChanged($event)" />
              <p class="messages-page__muted">{{ i18n.translate('messaging.group.minimum') }}</p>
            } @else {
            <label class="messages-page__field">
              <span>{{ i18n.translate('messaging.recipient.label') }}</span>
              <input
                #recipientSearch
                type="search"
                data-testid="recipient-search"
                [ngModel]="recipientQuery()"
                (ngModelChange)="onRecipientQueryChange($event)"
                [placeholder]="i18n.translate('messaging.recipient.placeholder')"
                autocomplete="off"
                [disabled]="creating()"
                cdkFocusInitial
              />
            </label>

            @if (selectedRecipients().length > 0) {
              <ul class="messages-page__selected" [attr.aria-label]="i18n.translate('messaging.recipient.selected')">
                @for (recipient of selectedRecipients(); track recipient.userId) {
                  <li>
                    <span>{{ recipient.displayName }}</span>
                    <button type="button" [disabled]="creating()" data-testid="remove-recipient"
                      [attr.aria-label]="i18n.translate('messaging.recipient.remove', { name: recipient.displayName })"
                      (click)="removeRecipientAndFocusSearch(recipient.userId)">×</button>
                  </li>
                }
              </ul>
            }
            @if (selectedRecipients().length > 1) {
              <p class="messages-page__muted" data-testid="direct-selection-help">{{ i18n.translate('messaging.group.directSelectionHelp') }}</p>
            }

            @if (recipientStatus() === 'idle') {
              <p class="messages-page__muted" data-testid="recipient-search-idle">{{ i18n.translate('messaging.recipient.idle') }}</p>
            } @else if (recipientStatus() === 'loading') {
              <p class="messages-page__muted" data-testid="recipient-search-loading">{{ i18n.translate('messaging.recipient.loading') }}</p>
            } @else if (recipientStatus() === 'error') {
              <p class="messages-page__error" data-testid="recipient-search-error">
                {{ i18n.translate('messaging.recipient.error') }}
              </p>
            } @else if (recipientStatus() === 'empty') {
              <p class="messages-page__muted" data-testid="recipient-search-empty">{{ i18n.translate('messaging.recipient.empty') }}</p>
            } @else {
              <div class="messages-page__recipients" role="group" [attr.aria-label]="i18n.translate('messaging.recipient.options')">
                @for (recipient of recipients(); track recipient.userId) {
                  <button
                    type="button"
                    class="messages-page__recipient"
                    [class.messages-page__recipient--selected]="isRecipientSelected(recipient.userId)"
                    [attr.aria-pressed]="isRecipientSelected(recipient.userId)"
                    [disabled]="creating()"
                    data-testid="recipient-option"
                    (click)="selectRecipient(recipient.userId)"
                  >
                    {{ recipient.displayName }}
                  </button>
                }
              </div>
            }

            }
            @if (createError()) {
              <p class="messages-page__error" role="alert" data-testid="create-conversation-error">{{ createError() }}</p>
            }
            </div>

            <footer class="messages-page__dialog-actions">
              <button type="button" (click)="closeCreateDialog()" [disabled]="creating()">{{ i18n.translate('common.cancel') }}</button>
              <button
                type="button"
                class="messages-page__primary"
                data-testid="create-conversation-submit"
                [disabled]="!canSubmitCreate()"
                (click)="createConversation()"
              >
                {{ i18n.translate(creating() ? 'messaging.recipient.creating' : createMode() === 'group' ? 'messaging.group.create' : 'messaging.recipient.create') }}
              </button>
            </footer>
          </section>
        </div>
      }
    </section>
  `,
  styleUrl: './messages-page.component.scss',
})
export class MessagesPageComponent {
  readonly i18n = injectMessagingI18n();
  readonly facade = inject(MessagingFacade);
  readonly globalSettings = inject(MessageGlobalSettingsService);
  private readonly api = inject(MessagingApi);
  private readonly router = inject(Router);
  private readonly navigationState = inject(MessageNavigationStateService);
  private readonly route = inject(ActivatedRoute);
  readonly directory = this.route.snapshot?.data?.['conversationDirectory'] as 'dm' | 'teams' | undefined ?? 'all';
  readonly directoryTitleKey: MessagingTextKey = ({
    all: 'messaging.conversations', dm: 'messaging.directory.dm', teams: 'messaging.directory.teams',
  } as const)[this.directory];
  readonly page = this.facade.page;
  readonly directoryConversations = computed(() => {
    const conversations = this.page().conversations;
    if (this.directory === 'all') {
      return conversations;
    }
    if (this.directory === 'dm') {
      return conversations.filter((conversation) => conversation.kind === 'dm');
    }
    const workspaceId = this.facade.activeWorkspaceId();
    return workspaceId ? conversations.filter((conversation) =>
      conversation.kind === 'channel' && conversation.workspaceId === workspaceId) : [];
  });
  readonly canCreateConversation = computed(() => !['loading', 'permissionDenied', 'sessionExpired'].includes(this.page().status));
  readonly createDialogOpen = signal(false);
  readonly recipientQuery = signal('');
  readonly recipientStatus = signal<RecipientSearchStatus>('idle');
  readonly recipients = signal<readonly RecipientOption[]>([]);
  readonly selectedRecipients = signal<readonly RecipientOption[]>([]);
  readonly creating = signal(false);
  readonly createMode = signal<'direct' | 'group'>('direct');
  readonly groupTitle = signal('');
  readonly groupPickerResetKey = signal(0);
  readonly excludedCreator = computed(() => this.facade.actorUserId() ? [this.facade.actorUserId()!] : []);
  private readonly createErrorKey = signal<MessagingTextKey | null>(null);
  readonly createError = computed(() => { const key = this.createErrorKey(); return key ? this.i18n.translate(key) : null; });
  readonly canSubmitCreate = computed(() => this.canCreateConversation() && !this.creating() &&
    (this.createMode() === 'group'
      ? !!this.facade.activeWorkspaceId() && this.selectedRecipients().length >= 2 && this.groupTitle().length <= 200
      : this.selectedRecipients().length === 1));
  private createWorkspaceId: string | null = null;
  private recipientRequest?: Subscription;
  private createRequest?: Subscription;
  @ViewChild('recipientSearch') private recipientSearch?: ElementRef<HTMLInputElement>;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.recipientRequest?.unsubscribe();
      this.createRequest?.unsubscribe();
    });
    effect(() => {
      if (this.page().status !== 'loading') {
        this.navigationState.restoreListScroll();
      }
      if (!this.canCreateConversation()) {
        this.createRequest?.unsubscribe();
        this.creating.set(false);
        this.closeCreateDialog();
      }
      if (this.createDialogOpen() && this.facade.activeWorkspaceId() !== this.createWorkspaceId) {
        this.createRequest?.unsubscribe(); this.creating.set(false); this.closeCreateDialog();
      }
    });
    this.facade.loadConversationListPage();
  }

  openCreateDialog(): void {
    if (!this.canCreateConversation()) { return; }
    this.createWorkspaceId = this.facade.activeWorkspaceId();
    this.createDialogOpen.set(true);
    this.createErrorKey.set(null);
    this.createMode.set('direct');
    this.groupTitle.set('');
  }

  onCreateEscape(event: KeyboardEvent): void {
    if (!event.isComposing && event.keyCode !== 229) { this.closeCreateDialog(); }
  }

  changeCreateMode(mode: 'direct' | 'group'): void {
    if (this.creating() || mode === this.createMode()) { return; }
    this.recipientRequest?.unsubscribe();
    this.createMode.set(mode); this.selectedRecipients.set([]); this.recipients.set([]);
    this.recipientQuery.set(''); this.recipientStatus.set('idle'); this.groupTitle.set(''); this.createErrorKey.set(null);
  }

  onGroupRecipientsChanged(recipients: readonly WorkspaceRecipientOption[]): void {
    this.selectedRecipients.set(recipients);
    if (recipients.length > 0) { this.createErrorKey.set(null); }
  }

  closeCreateDialog(): void {
    if (this.creating()) {
      return;
    }

    this.createDialogOpen.set(false);
    this.recipientRequest?.unsubscribe();
    this.recipientQuery.set('');
    this.recipientStatus.set('idle');
    this.recipients.set([]);
    this.selectedRecipients.set([]);
    this.createErrorKey.set(null);
    this.groupTitle.set('');
    this.createMode.set('direct');
    this.createWorkspaceId = null;
  }

  onRecipientQueryChange(value: string): void {
    if (this.creating()) { return; }
    this.recipientRequest?.unsubscribe();
    this.recipientQuery.set(value);
    this.createErrorKey.set(null);
    const query = value.trim();
    if (!query) {
      this.recipientStatus.set('idle');
      this.recipients.set([]);
      return;
    }

    this.recipientStatus.set('loading');
    this.recipients.set([]);
    this.recipientRequest = this.api.searchRecipients(query).subscribe({
      next: (response) => {
        const recipients = response.map(mapRecipient).filter((recipient): recipient is RecipientOption => recipient !== null);
        this.recipients.set(recipients);
        this.recipientStatus.set(recipients.length > 0 ? 'ready' : 'empty');
      },
      error: (error: { status?: number; httpStatus?: number }) => {
        this.recipients.set([]);
        if (isAccessFailure(error)) { this.selectedRecipients.set([]); }
        this.recipientStatus.set('error');
      }
    });
  }

  selectRecipient(userId: string): void {
    if (this.creating()) { return; }
    if (this.isRecipientSelected(userId)) {
      this.removeRecipient(userId);
      return;
    }
    const recipient = this.recipients().find((candidate) => candidate.userId === userId);
    if (!recipient) { return; }
    this.selectedRecipients.update((selected) => [...selected, recipient]);
    this.createErrorKey.set(null);
  }

  isRecipientSelected(userId: string): boolean {
    return this.selectedRecipients().some((recipient) => recipient.userId === userId);
  }

  removeRecipient(userId: string): void {
    if (this.creating()) { return; }
    this.selectedRecipients.update((selected) => selected.filter((recipient) => recipient.userId !== userId));
    this.createErrorKey.set(null);
  }

  removeRecipientAndFocusSearch(userId: string): void {
    if (this.creating()) { return; }
    this.removeRecipient(userId);
    this.recipientSearch?.nativeElement.focus();
  }

  createConversation(): void {
    const selected = this.selectedRecipients();
    if (!this.canSubmitCreate()) {
      return;
    }
    const recipientUserId = selected[0].userId;
    const group = this.createMode() === 'group';
    const workspaceId = this.facade.activeWorkspaceId();

    this.creating.set(true);
    this.createErrorKey.set(null);
    const command = group ? this.api.createGroupConversation(workspaceId!, selected.map((recipient) => recipient.userId), this.groupTitle())
      : this.api.createDirectConversation(recipientUserId);
    this.createRequest = command.subscribe({
      next: (conversation) => {
        const conversationId = stringValue(conversation.id);
        const validType = group ? (conversation.type === 'Group' || conversation.type === 7) && conversation.workspaceId === workspaceId &&
          conversation.projectId == null && conversation.parentConversationId == null
          : conversation.type === 'DirectMessage' || conversation.type === 0;
        if (!conversationId || !validType) {
          this.creating.set(false);
          this.createErrorKey.set(group ? 'messaging.group.createUnconfirmed' : 'messaging.recipient.invalid');
          return;
        }

        this.creating.set(false);
        this.closeCreateDialog();
        void this.router.navigateByUrl(`/dm/${conversationId}`);
      },
      error: (error: { status?: number; httpStatus?: number }) => {
        this.creating.set(false);
        if (isAccessFailure(error)) {
          this.selectedRecipients.set([]);
          this.recipients.set([]);
          if (group) { this.groupPickerResetKey.update((key) => key + 1); }
        }
        this.createErrorKey.set(group ? isAccessFailure(error) ? 'messaging.group.createUnavailable' : 'messaging.group.createUnconfirmed' : 'messaging.recipient.createFailed');
      }
    });
  }
}

function isAccessFailure(error: { status?: number; httpStatus?: number }): boolean {
  return [400, 401, 403, 404].includes(error.httpStatus ?? error.status ?? 0);
}

function mapRecipient(recipient: ConversationRecipientDto): RecipientOption | null {
  const userId = stringValue(recipient.userId);
  const displayName = stringValue(recipient.displayName);
  return userId && displayName ? { userId, displayName } : null;
}

function stringValue(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}
