import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  Input,
  OnChanges,
  SimpleChanges,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';

import { MessagingApi, ParticipantStateDto } from '../messaging.api';

type ConversationSettingsStatus = 'idle' | 'loading' | 'ready' | 'saving' | 'error';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-conversation-settings-panel',
  standalone: true,
  imports: [RouterLink],
  template: `
    <button
      #triggerButton
      type="button"
      class="conversation-settings__trigger"
      data-testid="conversation-settings-trigger"
      aria-haspopup="dialog"
      [attr.aria-expanded]="open()"
      (click)="openPanel()"
    >
      Conversation settings
    </button>

    @if (open()) {
      <section
        class="conversation-settings__panel"
        role="dialog"
        aria-modal="false"
        aria-labelledby="conversation-settings-title"
        aria-describedby="conversation-settings-scope"
        data-testid="conversation-settings-panel"
      >
        <header class="conversation-settings__header">
          <div>
            <span class="conversation-settings__scope-badge">This conversation</span>
            <h2 id="conversation-settings-title">Conversation settings</h2>
          </div>
          <button
            #closeButton
            type="button"
            aria-label="Close conversation settings"
            data-testid="conversation-settings-close"
            (click)="closePanel()"
          >
            ×
          </button>
        </header>

        <p id="conversation-settings-scope" class="conversation-settings__scope">
          Changes here affect only “{{ conversationTitle }}”. Other conversations and global Message settings are not changed.
        </p>

        @if (status() === 'loading') {
          <p role="status">Loading the current conversation setting...</p>
        } @else if (status() === 'error') {
          <div role="alert" class="conversation-settings__error">
            <p>{{ message() }}</p>
            <button type="button" (click)="load()">Retry</button>
          </div>
        } @else if (status() === 'ready' || status() === 'saving') {
          <label class="conversation-settings__field" for="conversation-notification-level">
            <span>Notification level</span>
            <select
              id="conversation-notification-level"
              data-testid="conversation-notification-level"
              [value]="isMuted() ? 'muted' : 'all'"
              [disabled]="status() === 'saving'"
              aria-describedby="conversation-settings-scope conversation-notification-level-current"
              (change)="onNotificationLevelChange($event)"
            >
              <option value="all">All activity</option>
              <option value="muted">Muted</option>
            </select>
          </label>
          <p id="conversation-notification-level-current" class="conversation-settings__current">
            Current value: {{ isMuted() ? 'Muted' : 'All activity' }}. Scope: this conversation only.
          </p>
          @if (status() === 'saving') {
            <p role="status" aria-live="polite">Saving this conversation setting...</p>
          } @else if (message()) {
            <p role="status" aria-live="polite">{{ message() }}</p>
          }
        }

        <footer class="conversation-settings__footer">
          <a routerLink="/messages/settings">Global message settings</a>
          <span>Global controls are kept on a separate page.</span>
        </footer>
      </section>
    }
  `,
  styles: [`
    :host { position: relative; display: inline-block; }
    .conversation-settings__trigger,
    .conversation-settings__header button,
    .conversation-settings__error button {
      min-block-size: 2.5rem;
      border: 1px solid var(--coglatas-color-border-default);
      border-radius: 6px;
      padding: 0 .75rem;
      background: var(--coglatas-color-bg-control);
      color: var(--coglatas-color-text-primary);
      font: inherit;
      font-weight: 800;
      cursor: pointer;
    }
    .conversation-settings__panel { position: absolute; inset-inline-end: 0; top: calc(100% + .5rem); z-index: 50; width: min(26rem, calc(100vw - 2rem)); padding: 1rem; border: 1px solid var(--coglatas-color-border-default); border-radius: .75rem; background: var(--coglatas-color-bg-elevated); color: var(--coglatas-color-text-primary); box-shadow: var(--coglatas-shadow-floating); text-align: start; }
    .conversation-settings__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
    .conversation-settings__header h2 { margin: .35rem 0 0; font-size: 1.1rem; }
    .conversation-settings__scope-badge { display: inline-flex; border-radius: 999px; padding: .2rem .55rem; font-size: .75rem; font-weight: 700; background: var(--coglatas-color-bg-selected); color: var(--coglatas-color-text-secondary); }
    .conversation-settings__scope, .conversation-settings__current, .conversation-settings__footer { color: var(--coglatas-color-text-secondary); }
    .conversation-settings__field { display: grid; gap: .35rem; margin-top: 1rem; font-weight: 600; }
    .conversation-settings__field select { min-block-size: 2.5rem; border: 1px solid var(--coglatas-color-border-default); border-radius: 6px; padding: 0 .5rem; background: var(--coglatas-color-bg-control); color: var(--coglatas-color-text-primary); font: inherit; }
    .conversation-settings__error { margin-top: 1rem; }
    .conversation-settings__footer { display: grid; gap: .25rem; margin-top: 1rem; padding-top: 1rem; border-top: 1px solid var(--coglatas-color-border-default); font-size: .875rem; }
    .conversation-settings__footer a { color: var(--coglatas-color-action-primary); font-weight: 800; }
    .conversation-settings__panel :focus-visible, .conversation-settings__trigger:focus-visible { outline: 2px solid var(--coglatas-color-focus); outline-offset: 3px; }
  `],
})
export class ConversationSettingsPanelComponent implements OnChanges {
  private readonly api = inject(MessagingApi);
  private requestGeneration = 0;

  @ViewChild('triggerButton', { read: ElementRef })
  private triggerButton?: ElementRef<HTMLButtonElement>;

  @ViewChild('closeButton', { read: ElementRef })
  private set closeButton(button: ElementRef<HTMLButtonElement> | undefined) {
    if (!button) {
      return;
    }

    queueMicrotask(() => {
      if (this.open()) {
        button.nativeElement.focus();
      }
    });
  }

  @Input({ required: true }) conversationId = '';
  @Input({ required: true }) conversationTitle = '';

  readonly open = signal(false);
  readonly status = signal<ConversationSettingsStatus>('idle');
  readonly isMuted = signal(false);
  readonly message = signal<string | null>(null);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['conversationId']) {
      this.requestGeneration++;
      if (this.open()) {
        this.closePanel();
      } else {
        this.open.set(false);
      }
      this.status.set('idle');
      this.isMuted.set(false);
      this.message.set(null);
    }
  }

  openPanel(): void {
    if (!this.conversationId) {
      return;
    }
    this.open.set(true);
    if (this.status() === 'idle') {
      this.load();
    }
  }

  closePanel(): void {
    if (!this.open()) {
      return;
    }

    this.open.set(false);
    queueMicrotask(() => this.triggerButton?.nativeElement.focus());
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: KeyboardEvent): void {
    if (!this.open()) {
      return;
    }

    event.preventDefault();
    this.closePanel();
  }

  load(): void {
    if (!this.conversationId) {
      return;
    }

    const conversationId = this.conversationId;
    const generation = ++this.requestGeneration;
    this.status.set('loading');
    this.message.set(null);
    this.api.getParticipantState(conversationId).subscribe({
      next: (response) => {
        if (generation !== this.requestGeneration || conversationId !== this.conversationId) {
          return;
        }
        const state = readState(response, conversationId);
        if (state === null) {
          this.status.set('error');
          this.message.set('The conversation setting response was incomplete.');
          return;
        }
        this.isMuted.set(state);
        this.status.set('ready');
      },
      error: () => {
        if (generation !== this.requestGeneration || conversationId !== this.conversationId) {
          return;
        }
        this.status.set('error');
        this.message.set('The current conversation setting could not be loaded.');
      }
    });
  }

  onNotificationLevelChange(event: Event): void {
    const nextMuted = (event.target as HTMLSelectElement).value === 'muted';
    if (this.status() !== 'ready' || nextMuted === this.isMuted() || !this.conversationId) {
      return;
    }

    const conversationId = this.conversationId;
    const generation = ++this.requestGeneration;
    this.status.set('saving');
    this.message.set(null);
    this.api.updateParticipantState(conversationId, nextMuted).subscribe({
      next: (response) => {
        if (generation !== this.requestGeneration || conversationId !== this.conversationId) {
          return;
        }
        const state = readState(response, conversationId);
        if (state === null) {
          this.status.set('error');
          this.message.set('The saved conversation setting could not be verified.');
          return;
        }
        this.isMuted.set(state);
        this.status.set('ready');
        this.message.set(`Notification level saved for this conversation only: ${state ? 'Muted' : 'All activity'}.`);
      },
      error: () => {
        if (generation !== this.requestGeneration || conversationId !== this.conversationId) {
          return;
        }
        this.status.set('error');
        this.message.set('The conversation setting could not be saved. No other conversation was changed.');
      }
    });
  }
}

function readState(response: ParticipantStateDto, expectedConversationId: string): boolean | null {
  const responseConversationId = typeof response.conversationId === 'string' ? response.conversationId : null;
  if (responseConversationId !== expectedConversationId || typeof response.isMuted !== 'boolean') {
    return null;
  }
  return response.isMuted;
}
