import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

const LIST_SCROLL_STORAGE_KEY = 'coglatas.messaging.list-scroll-y.v1';
const LIST_SCROLL_PENDING_KEY = 'coglatas.messaging.list-scroll-restore-pending.v1';
const LIST_SCROLL_HOST_KEY = 'coglatas.messaging.list-scroll-host.v1';
const LIST_FOCUS_CONVERSATION_KEY = 'coglatas.messaging.list-focus-conversation.v1';
const APP_SCROLL_HOST_ID = 'app-shell-main-content';
const DETAIL_BACK_LINK_ID = 'messages-mobile-back-link';
const DETAIL_BACK_LINK_FOCUS_ATTEMPTS = 8;
const MOBILE_HIERARCHY_QUERY = '(max-width: 860px)';

type MessageScrollHostId = 'app' | 'document';

interface MessageScrollHost {
  readonly id: MessageScrollHostId;
  readonly element: HTMLElement;
}

@Injectable({ providedIn: 'root' })
export class MessageNavigationStateService {
  private readonly document = inject(DOCUMENT);
  private storageUnavailable = false;

  rememberListScroll(conversationId?: string): void {
    const window = this.document.defaultView;
    const host = this.effectiveScrollHost();
    const storage = this.storage();
    if (!window || !host || !storage) {
      return;
    }

    try {
      storage.removeItem(LIST_SCROLL_PENDING_KEY);
      storage.setItem(LIST_SCROLL_STORAGE_KEY, String(Math.max(0, host.element.scrollTop)));
      storage.setItem(LIST_SCROLL_HOST_KEY, host.id);
      if (conversationId) {
        storage.setItem(LIST_FOCUS_CONVERSATION_KEY, conversationId);
      } else {
        storage.removeItem(LIST_FOCUS_CONVERSATION_KEY);
      }
      storage.setItem(LIST_SCROLL_PENDING_KEY, '1');
    } catch {
      this.clearPendingListScroll(storage);
      this.storageUnavailable = true;
    }
  }

  restoreListScroll(): void {
    const window = this.document.defaultView;
    const storage = this.storage();
    if (!window || !storage) {
      return;
    }

    let stored: string | null;
    let storedHostId: MessageScrollHostId | null;
    let focusConversationId: string | null;
    try {
      if (storage.getItem(LIST_SCROLL_PENDING_KEY) !== '1') {
        return;
      }
      stored = storage.getItem(LIST_SCROLL_STORAGE_KEY);
      storedHostId = this.scrollHostId(storage.getItem(LIST_SCROLL_HOST_KEY));
      focusConversationId = storage.getItem(LIST_FOCUS_CONVERSATION_KEY);
    } catch {
      this.clearPendingListScroll(storage);
      this.storageUnavailable = true;
      return;
    }

    const target = stored === null ? Number.NaN : Number(stored);
    if (!Number.isFinite(target) || target < 0) {
      this.clearPendingListScroll(storage);
      return;
    }

    let attemptsRemaining = 8;
    const restore = () => {
      const host = this.restoreScrollHost(storedHostId);
      if (!host) {
        this.clearPendingListScroll(storage);
        return;
      }

      host.element.scrollTo({ top: target, left: 0, behavior: 'auto' });
      attemptsRemaining -= 1;

      if (Math.abs(host.element.scrollTop - target) <= 1 || attemptsRemaining <= 0) {
        this.restoreListFocus(focusConversationId);
        this.clearPendingListScroll(storage);
        return;
      }

      window.requestAnimationFrame(restore);
    };

    window.requestAnimationFrame(restore);
  }

  resetDetailScroll(): void {
    const window = this.document.defaultView;
    if (!window || !this.isMobileHierarchy(window)) {
      return;
    }

    window.requestAnimationFrame(() => {
      for (const host of this.scrollHosts()) {
        host.element.scrollTo({ top: 0, left: 0, behavior: 'auto' });
      }

      this.focusDetailBackLink(window);
    });
  }

  /** Workspace navigation state is intentionally unscoped session data. */
  clearForWorkspaceBoundary(): void {
    this.clearPendingListScroll();
  }

  private effectiveScrollHost(): MessageScrollHost | null {
    const hosts = this.scrollHosts();
    return (
      hosts.find((host) => host.element.scrollTop > 0 && this.isScrollable(host.element)) ??
      hosts.find((host) => this.isScrollable(host.element)) ??
      hosts[0] ??
      null
    );
  }

  private restoreScrollHost(storedHostId: MessageScrollHostId | null): MessageScrollHost | null {
    const hosts = this.scrollHosts();
    const storedHost = hosts.find((host) => host.id === storedHostId);
    if (storedHost) {
      return storedHost;
    }

    return this.effectiveScrollHost();
  }

  private scrollHosts(): MessageScrollHost[] {
    const hosts: MessageScrollHost[] = [];
    const appHost = this.document.getElementById(APP_SCROLL_HOST_ID);
    const documentHost = this.document.scrollingElement;

    if (appHost instanceof HTMLElement) {
      hosts.push({ id: 'app', element: appHost });
    }

    if (documentHost instanceof HTMLElement && documentHost !== appHost) {
      hosts.push({ id: 'document', element: documentHost });
    }

    return hosts;
  }

  private isScrollable(host: HTMLElement): boolean {
    return host.scrollHeight > host.clientHeight + 1;
  }

  private scrollHostId(value: string | null): MessageScrollHostId | null {
    return value === 'app' || value === 'document' ? value : null;
  }

  private isMobileHierarchy(window: Window): boolean {
    if (typeof window.matchMedia === 'function') {
      return window.matchMedia(MOBILE_HIERARCHY_QUERY).matches;
    }

    return window.innerWidth <= 860;
  }

  private focusDetailBackLink(window: Window): void {
    let attemptsRemaining = DETAIL_BACK_LINK_FOCUS_ATTEMPTS;
    const focus = () => {
      const backLink = this.document.getElementById(DETAIL_BACK_LINK_ID);
      if (backLink instanceof HTMLElement) {
        backLink.focus({ preventScroll: true });
        return;
      }

      attemptsRemaining -= 1;
      if (attemptsRemaining > 0) {
        window.requestAnimationFrame(focus);
      }
    };

    focus();
  }

  private restoreListFocus(conversationId: string | null): void {
    if (!conversationId) {
      return;
    }

    const target = Array.from(
      this.document.querySelectorAll<HTMLElement>('[data-conversation-id]'),
    ).find((element) => element.dataset['conversationId'] === conversationId);
    target?.focus({ preventScroll: true });
  }

  private storage(): Storage | null {
    if (this.storageUnavailable) {
      return null;
    }

    try {
      return this.document.defaultView?.sessionStorage ?? null;
    } catch {
      this.storageUnavailable = true;
      return null;
    }
  }

  private clearPendingListScroll(storage = this.storage()): void {
    if (!storage) {
      return;
    }

    for (const key of [
      LIST_SCROLL_STORAGE_KEY,
      LIST_SCROLL_HOST_KEY,
      LIST_SCROLL_PENDING_KEY,
      LIST_FOCUS_CONVERSATION_KEY,
    ]) {
      try {
        storage.removeItem(key);
      } catch {
        // Browser privacy settings can deny storage even when the object is exposed.
        this.storageUnavailable = true;
      }
    }
  }
}
