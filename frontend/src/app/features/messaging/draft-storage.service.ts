import { Injectable } from '@angular/core';

import { MessagingDraftScope } from './messaging.types';

@Injectable({ providedIn: 'root' })
export class DraftStorageService {
  private readonly prefix = 'coglatas.messaging.draft';

  readDraft(scope: MessagingDraftScope): string {
    try {
      return this.storage()?.getItem(this.keyFor(scope)) ?? '';
    } catch {
      return '';
    }
  }

  writeDraft(scope: MessagingDraftScope, value: string): void {
    const storage = this.storage();
    if (!storage) {
      return;
    }

    try {
      const key = this.keyFor(scope);
      if (value.length === 0) {
        storage.removeItem(key);
        return;
      }

      storage.setItem(key, value);
    } catch {
      // Draft persistence is optional UX state. Storage denial/quota must not
      // crash the protected messaging surface.
    }
  }

  clearDraft(scope: MessagingDraftScope): void {
    try {
      this.storage()?.removeItem(this.keyFor(scope));
    } catch {
      // Best effort; the user-partitioned key still prevents cross-user reads.
    }
  }

  clearAllDrafts(): void {
    const storage = this.storage();
    if (!storage) {
      return;
    }

    const keys: string[] = [];
    let length = 0;
    try {
      length = storage.length;
    } catch {
      return;
    }
    for (let index = 0; index < length; index++) {
      try {
        const key = storage.key(index);
        if (key?.startsWith(`${this.prefix}:`)) {
          keys.push(key);
        }
      } catch {
        // Continue clearing any other enumerable draft keys.
      }
    }
    for (const key of keys) {
      try {
        storage.removeItem(key);
      } catch {
        // Continue so one denied entry cannot abort the session-boundary pass.
      }
    }
  }

  keyFor(scope: MessagingDraftScope): string {
    const workspacePart = scope.workspaceId ?? 'dm';
    return `${this.prefix}:${scope.tenantId}:${scope.userId}:${workspacePart}:${scope.conversationId}`;
  }

  private storage(): Storage | null {
    try {
      return globalThis.sessionStorage ?? null;
    } catch {
      return null;
    }
  }
}
