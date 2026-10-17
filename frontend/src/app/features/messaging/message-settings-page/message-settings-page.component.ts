import { MessagingTextKey } from '../messaging.types';
import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AppConfirmDialogComponent } from '../../../shared/dialog/app-confirm-dialog/app-confirm-dialog.component';
import { MessageGlobalSettingsService } from '../message-global-settings.service';
import { MessageNotificationPreferenceDto, MessagingApi } from '../messaging.api';

type GlobalSettingsStatus = 'loading' | 'ready' | 'saving' | 'error';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-message-settings-page',
  standalone: true,
  imports: [RouterLink, AppConfirmDialogComponent],
  template: `
    <section class="message-settings" data-testid="global-message-settings">
      <header class="message-settings__header">
        <div>
          <p class="message-settings__eyebrow">{{ i18n.translate('messaging.title') }}</p>
          <h1>{{ i18n.translate('messaging.settings.globalTitle') }}</h1>
          <p id="global-message-settings-scope" class="message-settings__scope">
            {{ i18n.translate('messaging.settings.scope') }}
          </p>
        </div>
        <a routerLink="/messages">{{ i18n.translate('messaging.settings.back') }}</a>
      </header>

      @if (status() === 'loading') {
        <p role="status">{{ i18n.translate('messaging.settings.loading') }}</p>
      } @else if (status() === 'error') {
        <div class="message-settings__error" role="alert">
          <p>{{ errorMessage() }}</p>
          <button type="button" (click)="load()">{{ i18n.translate('common.retry') }}</button>
        </div>
      } @else {
        <section class="message-settings__card" aria-labelledby="global-notification-settings-title">
          <div class="message-settings__scope-badge">{{ i18n.translate('messaging.settings.global') }}</div>
          <h2 id="global-notification-settings-title">{{ i18n.translate('messaging.settings.delivery') }}</h2>
          <p class="message-settings__muted">
            {{ i18n.translate('messaging.settings.deliveryHelp') }}
          </p>

          <label class="message-settings__setting" for="message-notifications-enabled">
            <span>
              <strong>{{ i18n.translate('messaging.settings.notifications') }}</strong>
              <small id="message-notifications-help">
                {{ i18n.translate('messaging.settings.currentDelivery', { value: i18n.translate(messageNotificationsEnabled() ? 'messaging.on' : 'messaging.off') }) }}
              </small>
            </span>
            <input
              id="message-notifications-enabled"
              data-testid="global-message-notifications"
              type="checkbox"
              [checked]="pendingMessageNotificationsEnabled()"
              [disabled]="status() === 'saving'"
              aria-describedby="global-message-settings-scope message-notifications-help"
              (change)="onMessageNotificationChange($event)"
            />
          </label>
        </section>

        <section class="message-settings__card" aria-labelledby="global-display-settings-title">
          <div class="message-settings__scope-badge">{{ i18n.translate('messaging.settings.browser') }}</div>
          <h2 id="global-display-settings-title">{{ i18n.translate('messaging.settings.listDisplay') }}</h2>
          <p class="message-settings__muted">
            {{ i18n.translate('messaging.settings.browserHelp') }}
          </p>

          <label class="message-settings__setting" for="show-unread-badges">
            <span>
              <strong>{{ i18n.translate('messaging.settings.unreadBadges') }}</strong>
              <small id="show-unread-badges-help">
                {{ i18n.translate('messaging.settings.currentDisplay', { value: i18n.translate(settings.showUnreadBadges() ? 'messaging.on' : 'messaging.off') }) }}
              </small>
            </span>
            <input
              id="show-unread-badges"
              data-testid="global-show-unread-badges"
              type="checkbox"
              [checked]="pendingShowUnreadBadges()"
              [disabled]="status() === 'saving'"
              aria-describedby="global-message-settings-scope show-unread-badges-help"
              (change)="onUnreadBadgeChange($event)"
            />
          </label>
        </section>

        <section class="message-settings__conversation-scope" aria-labelledby="conversation-settings-separate-title">
          <h2 id="conversation-settings-separate-title">{{ i18n.translate('messaging.settings.thisConversation') }}</h2>
          <p>
            {{ i18n.translate('messaging.settings.separateHelp') }}
          </p>
        </section>

        <div class="message-settings__actions">
          <button
            type="button"
            data-testid="save-global-message-settings"
            [disabled]="status() !== 'ready' || !hasChanges()"
            (click)="requestSave()"
          >
            {{ i18n.translate(status() === 'saving' ? 'messaging.actions.saving' : 'messaging.settings.saveGlobal') }}
          </button>
        </div>

        @if (errorMessage()) {
          <p class="message-settings__error-text" role="alert">{{ errorMessage() }}</p>
        }
        @if (savedMessage()) {
          <p class="message-settings__status" role="status" aria-live="polite">{{ savedMessage() }}</p>
        }
      }

      @if (confirmOpen()) {
        <div data-testid="global-settings-confirmation">
          <app-confirm-dialog
            [open]="true"
            [title]="i18n.translate('messaging.settings.confirmTitle')"
            [message]="confirmationMessage()"
            [confirmLabel]="i18n.translate('messaging.settings.confirm')"
            [cancelLabel]="i18n.translate('common.cancel')"
            (confirm)="confirmSave()"
            (cancel)="cancelSave()"
          />
        </div>
      }
    </section>
  `,
  styles: [`
    :host { display: block; }
    .message-settings { max-width: 58rem; margin: 0 auto; padding: 1.5rem; color: var(--coglatas-color-text-primary); }
    .message-settings__header { display: flex; justify-content: space-between; gap: 1.5rem; align-items: flex-start; margin-bottom: 1.5rem; }
    .message-settings__eyebrow { margin: 0; color: var(--coglatas-color-text-muted); font-size: .75rem; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; }
    .message-settings__header h1 { margin: .25rem 0 .5rem; }
    .message-settings__header a { min-block-size: 2.5rem; display: inline-flex; align-items: center; color: var(--coglatas-color-action-primary); font-weight: 800; }
    .message-settings__scope, .message-settings__muted, .message-settings__conversation-scope { max-width: 44rem; color: var(--coglatas-color-text-secondary); }
    .message-settings__card { margin-bottom: 1rem; border: 1px solid var(--coglatas-color-border-default); border-radius: .75rem; padding: 1.25rem; background: var(--coglatas-color-bg-surface); }
    .message-settings__scope-badge { display: inline-flex; border-radius: 999px; padding: .2rem .55rem; font-size: .75rem; font-weight: 700; background: var(--coglatas-color-bg-selected); color: var(--coglatas-color-text-secondary); }
    .message-settings__setting { display: flex; justify-content: space-between; gap: 1rem; align-items: center; padding: 1rem 0 0; border-top: 1px solid var(--coglatas-color-border-default); }
    .message-settings__setting span { display: grid; gap: .25rem; }
    .message-settings__setting small { color: var(--coglatas-color-text-secondary); }
    .message-settings__setting input { inline-size: 1.25rem; block-size: 1.25rem; accent-color: var(--coglatas-color-action-primary); }
    .message-settings__conversation-scope { margin-bottom: 1rem; padding: 1rem 1.25rem; border-left: 3px solid var(--coglatas-color-border-default); }
    .message-settings__conversation-scope h2, .message-settings__conversation-scope p { margin-block: 0 .5rem; }
    .message-settings__actions { display: flex; justify-content: flex-end; gap: .75rem; }
    .message-settings button { min-block-size: 2.5rem; border: 1px solid var(--coglatas-color-border-default); border-radius: 6px; padding: 0 .85rem; background: var(--coglatas-color-bg-control); color: var(--coglatas-color-text-primary); font: inherit; font-weight: 800; cursor: pointer; }
    .message-settings button:disabled { cursor: not-allowed; opacity: .6; }
    .message-settings__status { margin-bottom: 0; }
    .message-settings__error { border: 1px solid var(--coglatas-color-border-default); border-radius: .75rem; padding: 1rem; }
    .message-settings__error-text { color: var(--coglatas-color-warning); }
    .message-settings :focus-visible { outline: 2px solid var(--coglatas-color-focus); outline-offset: 3px; }
    @media (max-width: 40rem) { .message-settings__header { flex-direction: column; } .message-settings__setting { align-items: flex-start; } }
  `],
})
export class MessageSettingsPageComponent {
  readonly i18n = inject(I18nService);
  readonly settings = inject(MessageGlobalSettingsService);
  private readonly api = inject(MessagingApi);

  readonly status = signal<GlobalSettingsStatus>('loading');
  readonly messageNotificationsEnabled = signal(true);
  readonly pendingMessageNotificationsEnabled = signal(true);
  readonly pendingShowUnreadBadges = signal(this.settings.showUnreadBadges());
  readonly confirmOpen = signal(false);
  private readonly savedMessageKey = signal<MessagingTextKey | null>(null);
  readonly savedMessage = computed(() => { const key = this.savedMessageKey(); return key ? this.i18n.translate(key) : null; });
  private readonly errorMessageKey = signal<MessagingTextKey | null>(null);
  readonly errorMessage = computed(() => { const key = this.errorMessageKey(); return key ? this.i18n.translate(key) : null; });

  readonly hasChanges = computed(
    () =>
      this.pendingMessageNotificationsEnabled() !== this.messageNotificationsEnabled() ||
      this.pendingShowUnreadBadges() !== this.settings.showUnreadBadges()
  );

  readonly confirmationMessage = computed(() => {
    return this.i18n.translate(this.pendingMessageNotificationsEnabled() ? 'messaging.settings.confirmEnabled' : 'messaging.settings.confirmDisabled');
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.status.set('loading');
    this.errorMessageKey.set(null);
    this.savedMessageKey.set(null);
    this.api.getMessageNotificationPreference().subscribe({
      next: (response) => {
        const enabled = readMessageNotificationPreference(response);
        if (enabled === null) {
          this.status.set('error');
          this.errorMessageKey.set('messaging.settings.incomplete');
          return;
        }
        this.messageNotificationsEnabled.set(enabled);
        this.pendingMessageNotificationsEnabled.set(enabled);
        this.pendingShowUnreadBadges.set(this.settings.showUnreadBadges());
        this.status.set('ready');
      },
      error: () => {
        this.status.set('error');
        this.errorMessageKey.set('messaging.settings.loadFailed');
      }
    });
  }

  onMessageNotificationChange(event: Event): void {
    this.pendingMessageNotificationsEnabled.set((event.target as HTMLInputElement).checked);
    this.savedMessageKey.set(null);
    this.errorMessageKey.set(null);
  }

  onUnreadBadgeChange(event: Event): void {
    this.pendingShowUnreadBadges.set((event.target as HTMLInputElement).checked);
    this.savedMessageKey.set(null);
    this.errorMessageKey.set(null);
  }

  requestSave(): void {
    if (this.status() === 'ready' && this.hasChanges()) {
      this.confirmOpen.set(true);
    }
  }

  cancelSave(): void {
    this.confirmOpen.set(false);
  }

  confirmSave(): void {
    this.confirmOpen.set(false);
    if (this.status() !== 'ready' || !this.hasChanges()) {
      return;
    }

    const requestedNotificationsEnabled = this.pendingMessageNotificationsEnabled();
    const notificationChanged = requestedNotificationsEnabled !== this.messageNotificationsEnabled();
    if (!notificationChanged) {
      this.applyBrowserDisplaySetting();
      this.savedMessageKey.set('messaging.settings.saved');
      return;
    }

    this.status.set('saving');
    this.errorMessageKey.set(null);
    this.api.updateMessageNotificationPreference(requestedNotificationsEnabled).subscribe({
      next: (response) => {
        const saved = readMessageNotificationPreference(response);
        if (saved === null || saved !== requestedNotificationsEnabled) {
          this.status.set('ready');
          this.errorMessageKey.set('messaging.settings.unverified');
          return;
        }

        this.messageNotificationsEnabled.set(saved);
        this.pendingMessageNotificationsEnabled.set(saved);
        this.applyBrowserDisplaySetting();
        this.status.set('ready');
        this.savedMessageKey.set('messaging.settings.saved');
      },
      error: () => {
        this.status.set('ready');
        this.errorMessageKey.set('messaging.settings.saveFailed');
      }
    });
  }

  private applyBrowserDisplaySetting(): void {
    if (this.pendingShowUnreadBadges() !== this.settings.showUnreadBadges()) {
      this.settings.setShowUnreadBadges(this.pendingShowUnreadBadges());
    }
  }
}

function readMessageNotificationPreference(response: MessageNotificationPreferenceDto): boolean | null {
  return typeof response.messageNotificationsEnabled === 'boolean'
    ? response.messageNotificationsEnabled
    : null;
}
