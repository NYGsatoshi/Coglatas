import { ChangeDetectionStrategy, Component, computed, inject, Input } from '@angular/core';
import { LucideX } from '@lucide/angular';

import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { MessagingFacade } from '../messaging.facade';
import { MessagingMentionCandidate } from '../messaging.types';
import { ThreadPreviewComponent } from '../thread-preview/thread-preview.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-thread-workspace',
  standalone: true,
  imports: [LucideX, ThreadPreviewComponent],
  templateUrl: './thread-workspace.component.html',
  styleUrl: './thread-workspace.component.scss',
})
export class ThreadWorkspaceComponent {
  readonly facade = inject(MessagingFacade);
  readonly i18n = injectMessagingI18n();
  @Input() canPost = false;
  @Input() canCreateThread = false;
  @Input() mentionCandidates: readonly MessagingMentionCandidate[] = [];
  @Input() focusMessageId: string | null = null;

  readonly availableRoots = computed(() => this.facade.page().messages.filter((message) =>
    !message.threadRootMessageId && message.deliveryState === 'confirmed' &&
    (!message.isDeleted || (message.thread?.replyCount ?? 0) > 0)));

  selectTab(rootMessageId: string): void {
    this.facade.selectThread(rootMessageId);
  }

  openRoot(rootMessageId: string): void {
    this.facade.openThread(rootMessageId, `open-message-thread-${this.safeId(rootMessageId)}`);
  }

  closeTab(rootMessageId: string): void {
    this.facade.closeThreadTab(rootMessageId);
    this.focusActiveTab();
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    if (event.isComposing || event.keyCode === 229) {
      return;
    }
    const tabs = this.facade.threadTabs();
    let next: number;
    switch (event.key) {
      case 'ArrowLeft': next = (index - 1 + tabs.length) % tabs.length; break;
      case 'ArrowRight': next = (index + 1) % tabs.length; break;
      case 'Home': next = 0; break;
      case 'End': next = tabs.length - 1; break;
      case 'Delete':
        event.preventDefault();
        this.closeTab(tabs[index].identity.rootMessageId);
        return;
      default: return;
    }
    event.preventDefault();
    this.selectTab(tabs[next].identity.rootMessageId);
    this.focusActiveTab();
  }

  retry(): void {
    const thread = this.facade.thread();
    if (thread.rootMessageId) {
      this.facade.openThread(thread.rootMessageId, thread.triggerElementId ?? 'message-timeline');
    }
  }

  tabLabel(rootMessageId: string, index: number): string {
    const label = this.i18n.translate('messaging.workspace.tab', { number: this.i18n.formatNumber(index + 1) });
    const root = this.facade.page().messages.find((message) => message.id === rootMessageId);
    const preview = root && !root.isDeleted ? Array.from(root.body.replace(/\s+/gu, ' ').trim()).slice(0, 40).join('') : '';
    return preview ? `${label} · ${preview}` : label;
  }

  safeId(value: string): string {
    return value.replace(/[^a-zA-Z0-9_-]/g, '-');
  }

  private focusActiveTab(): void {
    queueMicrotask(() => {
      const rootMessageId = this.facade.thread().rootMessageId;
      if (rootMessageId) {
        document.getElementById(`thread-tab-${this.safeId(rootMessageId)}`)?.focus();
      }
    });
  }
}
