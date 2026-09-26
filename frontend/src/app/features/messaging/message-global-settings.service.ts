import { isPlatformBrowser } from '@angular/common';
import { computed, effect, inject, Injectable, PLATFORM_ID, signal } from '@angular/core';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';

interface StoredMessageGlobalSettings {
  readonly showUnreadBadges: boolean;
}

const STORAGE_VERSION = 'v2';

/**
 * Browser-only Message presentation preferences. Notification delivery lives
 * on the server; this storage is namespaced by the active tenant and user so a
 * shared browser never carries one account's display choice into another.
 */
@Injectable({ providedIn: 'root' })
export class MessageGlobalSettingsService {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly auth = inject(AuthSessionFacade);
  private readonly storageKey = computed(() => {
    const tenantId = this.auth.currentTenant()?.tenantId ?? 'tenant-unresolved';
    const userId = this.auth.currentUser()?.userId ?? 'anonymous';
    return `coglatas.messaging.global-settings.${STORAGE_VERSION}.${tenantId}.${userId}`;
  });

  readonly showUnreadBadges = signal(true);

  constructor() {
    effect(() => {
      const key = this.storageKey();
      this.showUnreadBadges.set(this.read(key).showUnreadBadges);
    });
  }

  setShowUnreadBadges(value: boolean): void {
    this.showUnreadBadges.set(value);
    this.write(this.storageKey(), { showUnreadBadges: value });
  }

  private read(key: string): StoredMessageGlobalSettings {
    if (!isPlatformBrowser(this.platformId)) {
      return { showUnreadBadges: true };
    }

    try {
      const raw = globalThis.localStorage.getItem(key);
      if (!raw) {
        return { showUnreadBadges: true };
      }
      const parsed = JSON.parse(raw) as Partial<StoredMessageGlobalSettings>;
      return { showUnreadBadges: parsed.showUnreadBadges !== false };
    } catch {
      return { showUnreadBadges: true };
    }
  }

  private write(key: string, value: StoredMessageGlobalSettings): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    try {
      globalThis.localStorage.setItem(key, JSON.stringify(value));
    } catch {
      // Storage may be unavailable in hardened/private browsing contexts.
      // The in-memory value still applies for the active app session.
    }
  }
}
