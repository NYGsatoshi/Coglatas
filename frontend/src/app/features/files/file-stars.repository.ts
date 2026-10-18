import { Injectable, InjectionToken, inject } from '@angular/core';

import { ContinueWorkingScope, ContinueWorkingStorage } from '../../shared/continue-working/continue-working-history.service';

export const LOCAL_FILE_STARS_LIMIT = 32;
export const COGLATAS_FILE_STARS_STORAGE = new InjectionToken<ContinueWorkingStorage | null>(
  'COGLATAS_FILE_STARS_STORAGE',
  { providedIn: 'root', factory: () => {
    try { return typeof window === 'undefined' ? null : window.localStorage; } catch { return null; }
  } },
);

export interface LocalFileStarsRead {
  readonly status: 'ready' | 'discarded' | 'unavailable';
  readonly ids: readonly string[];
}

/** Browser preference adapter only. No server persistence or synchronization contract. */
@Injectable({ providedIn: 'root' })
export class LocalFileStarsRepository {
  private readonly storage = inject(COGLATAS_FILE_STARS_STORAGE);

  read(scope: ContinueWorkingScope): LocalFileStarsRead {
    if (!this.storage) { return { status: 'unavailable', ids: [] }; }
    try {
      const raw = this.storage.getItem(storageKey(scope));
      if (raw === null) { return { status: 'ready', ids: [] }; }
      const ids = parseRecord(raw);
      if (ids) { return { status: 'ready', ids }; }
      this.storage.removeItem(storageKey(scope));
      return { status: 'discarded', ids: [] };
    } catch { return { status: 'unavailable', ids: [] }; }
  }

  write(scope: ContinueWorkingScope, ids: readonly string[]): boolean {
    if (!this.storage || ids.length > LOCAL_FILE_STARS_LIMIT ||
      ids.some((id) => canonicalFileStarId(id) !== id) || new Set(ids).size !== ids.length) { return false; }
    try {
      this.storage.setItem(storageKey(scope), JSON.stringify({ version: 1, ids }));
      return true;
    } catch { return false; }
  }
}

export function canonicalFileStarId(value: unknown): string | null {
  return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value)
    ? value.toLowerCase() : null;
}

export function fileStarsScopeKey(scope: ContinueWorkingScope | null): string {
  return scope ? JSON.stringify([scope.tenantId, scope.userId, scope.workspaceId]) : '';
}

function storageKey(scope: ContinueWorkingScope): string {
  return `coglatas.local-file-stars.v1:${fileStarsScopeKey(scope)}`;
}

function parseRecord(raw: string): readonly string[] | null {
  if (raw.length > 4_096) { return null; }
  try {
    const value: unknown = JSON.parse(raw);
    if (!value || typeof value !== 'object' || Array.isArray(value)) { return null; }
    const record = value as Record<string, unknown>;
    if (Object.keys(record).sort().join(',') !== 'ids,version' || record['version'] !== 1 ||
      !Array.isArray(record['ids']) || record['ids'].length > LOCAL_FILE_STARS_LIMIT) { return null; }
    const ids = record['ids'];
    return ids.every((id) => canonicalFileStarId(id) === id) && new Set(ids).size === ids.length ? ids : null;
  } catch { return null; }
}
