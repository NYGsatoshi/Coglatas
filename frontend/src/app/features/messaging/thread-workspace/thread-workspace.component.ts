import { afterNextRender, afterRenderEffect, ChangeDetectionStrategy, Component, computed, ElementRef, inject, Injector, Input, viewChild } from '@angular/core';
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
  private readonly injector = inject(Injector);
  private readonly tabStrip = viewChild<ElementRef<HTMLElement>>('tabStrip');
  @Input() canPost = false;
  @Input() canCreateThread = false;
  @Input() mentionCandidates: readonly MessagingMentionCandidate[] = [];
  @Input() focusMessageId: string | null = null;

  readonly activeTabIndex = computed(() => this.facade.threadTabs().findIndex((tab) =>
    tab.identity.rootMessageId === this.facade.thread().rootMessageId));

  readonly availableRoots = computed(() => {
    const page = this.facade.page();
    if ((page.status !== 'ready' && page.status !== 'empty') ||
        !page.conversation.viewerIsParticipant || page.conversation.viewerWasRemoved ||
        !page.conversation.capabilities.includes('readBody')) {
      return [];
    }
    return page.messages.filter((message) => !message.threadRootMessageId &&
      message.deliveryState === 'confirmed' && (!message.isDeleted || (message.thread?.replyCount ?? 0) > 0));
  });

  constructor() {
    afterRenderEffect((onCleanup) => {
      const strip = this.tabStrip()?.nativeElement;
      const activeIndex = this.activeTabIndex();
      if (!strip || activeIndex < 0) {
        return;
      }
      this.revealSelectedTab(strip);
      if (typeof ResizeObserver !== 'undefined') {
        const observer = new ResizeObserver(() => this.revealSelectedTab(strip));
        observer.observe(strip);
        onCleanup(() => observer.disconnect());
      }
    });
  }

  isOpen(rootMessageId: string): boolean {
    return this.facade.threadTabs().some((tab) => tab.identity.rootMessageId === rootMessageId);
  }

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
    afterNextRender(() => {
      const strip = this.tabStrip()?.nativeElement;
      const tab = strip?.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]');
      if (tab && strip) {
        tab.focus({ preventScroll: true });
        this.revealSelectedTab(strip);
      }
    }, { injector: this.injector });
  }

  private revealSelectedTab(strip: HTMLElement): void {
    const tab = strip.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]');
    if (!tab) {
      return;
    }
    const bounds = tab.getBoundingClientRect();
    const left = strip.getBoundingClientRect().left + strip.clientLeft;
    const right = left + strip.clientWidth;
    if (bounds.left < left) {
      strip.scrollLeft -= Math.ceil(left - bounds.left);
    } else if (bounds.right > right) {
      strip.scrollLeft += Math.ceil(bounds.right - right);
    }
  }
}
