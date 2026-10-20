import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LucideCheckCheck } from '@lucide/angular';
import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { MessagingFacade } from '../messaging.facade';

@Component({
  selector: 'app-conversation-read-action',
  changeDetection: ChangeDetectionStrategy.Eager,
  standalone: true,
  imports: [LucideCheckCheck],
  template: `
    @if (facade.canMarkRead()) {
      <button type="button" data-testid="conversation-mark-read" [disabled]="facade.readAction().pending"
        [attr.aria-busy]="facade.readAction().pending" (click)="facade.markLoadedMessagesRead()">
        <svg lucideCheckCheck aria-hidden="true"></svg>
        {{ i18n.translate(facade.readAction().pending ? 'messaging.read.pending' : 'messaging.read.action') }}
      </button>
    }
    @if (facade.readAction().feedbackKey; as key) {
      <p data-testid="conversation-mark-read-status" [attr.role]="facade.readAction().failed ? 'alert' : 'status'">{{ i18n.translate(key) }}</p>
    }
  `,
  styles: `
    :host { display: grid; gap: 4px; min-width: 0; }
    button { display: inline-flex; justify-self: start; align-items: center; justify-content: center; gap: 6px; min-height: 44px; min-width: 44px; padding: 8px 12px; border: 1px solid var(--coglatas-color-border-default); border-radius: 6px; background: var(--coglatas-color-bg-control); color: var(--coglatas-color-text-primary); font: inherit; cursor: pointer; overflow-wrap: anywhere; }
    button:disabled { cursor: default; }
    button:focus-visible { outline: var(--coglatas-focus-outline); outline-offset: var(--coglatas-focus-offset); }
    svg { width: 16px; height: 16px; flex-shrink: 0; }
    p { margin: 0; max-width: 24rem; color: var(--coglatas-color-text-secondary); font-size: 0.8rem; overflow-wrap: anywhere; }
  `
})
export class ConversationReadActionComponent {
  readonly facade = inject(MessagingFacade);
  readonly i18n = injectMessagingI18n();
}
