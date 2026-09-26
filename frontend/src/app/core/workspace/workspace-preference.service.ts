import { inject, Injectable, InjectionToken } from '@angular/core';

export interface WorkspacePreferenceStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export const COGLATAS_WORKSPACE_PREFERENCE_STORAGE = new InjectionToken<WorkspacePreferenceStorage | null>(
  'COGLATAS_WORKSPACE_PREFERENCE_STORAGE',
  {
    providedIn: 'root',
    factory: browserLocalStorage,
  },
);

@Injectable({ providedIn: 'root' })
export class WorkspacePreferenceService {
  private readonly storage = inject(COGLATAS_WORKSPACE_PREFERENCE_STORAGE);

  read(tenantId: string, userId: string): string | null {
    if (!this.storage || !isValidIdentityPart(tenantId) || !isValidIdentityPart(userId)) {
      return null;
    }

    try {
      const value = this.storage.getItem(preferenceKey(tenantId, userId));
      return typeof value === 'string' && value.length > 0 ? value : null;
    } catch {
      return null;
    }
  }

  write(tenantId: string, userId: string, workspaceId: string): boolean {
    if (
      !this.storage ||
      !isValidIdentityPart(tenantId) ||
      !isValidIdentityPart(userId) ||
      workspaceId.length === 0
    ) {
      return false;
    }

    try {
      this.storage.setItem(preferenceKey(tenantId, userId), workspaceId);
      return true;
    } catch {
      return false;
    }
  }

  clear(tenantId: string, userId: string): boolean {
    if (!this.storage || !isValidIdentityPart(tenantId) || !isValidIdentityPart(userId)) {
      return false;
    }

    try {
      this.storage.removeItem(preferenceKey(tenantId, userId));
      return true;
    } catch {
      return false;
    }
  }
}

function preferenceKey(tenantId: string, userId: string): string {
  return `coglatas.workspace.last-used:${encodeURIComponent(tenantId)}:${encodeURIComponent(userId)}`;
}

function isValidIdentityPart(value: string): boolean {
  return value.length > 0;
}

function browserLocalStorage(): WorkspacePreferenceStorage | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    return window.localStorage;
  } catch {
    return null;
  }
}
