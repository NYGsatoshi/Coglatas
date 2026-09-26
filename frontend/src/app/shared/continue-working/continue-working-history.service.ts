import { inject, Injectable, InjectionToken } from '@angular/core';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';

export type ContinueWorkingKind = 'project' | 'file';

export interface ContinueWorkingHistoryEntry {
  readonly kind: ContinueWorkingKind;
  readonly resourceId: string;
  readonly lastOpenedUtc: string;
}

export interface ContinueWorkingScope {
  readonly tenantId: string;
  readonly userId: string;
  readonly workspaceId: string;
}

export interface ContinueWorkingStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export type ContinueWorkingHistoryRead =
  | { readonly status: 'ready'; readonly entries: readonly ContinueWorkingHistoryEntry[] }
  | { readonly status: 'discarded'; readonly entries: readonly ContinueWorkingHistoryEntry[] }
  | { readonly status: 'storageUnavailable'; readonly entries: readonly ContinueWorkingHistoryEntry[] };

export const COGLATAS_CONTINUE_WORKING_STORAGE = new InjectionToken<ContinueWorkingStorage | null>(
  'COGLATAS_CONTINUE_WORKING_STORAGE',
  { providedIn: 'root', factory: browserLocalStorage },
);

export const COGLATAS_CONTINUE_WORKING_NOW = new InjectionToken<() => Date>(
  'COGLATAS_CONTINUE_WORKING_NOW',
  { providedIn: 'root', factory: () => () => new Date() },
);

const historyVersion = 1;
const historyKeyVersion = 'v1';
const maximumStoredEntries = 8;
const maximumRawRecordLength = 8_192;

interface StoredHistory {
  readonly version: 1;
  readonly items: readonly ContinueWorkingHistoryEntry[];
}

/**
 * Browser-only UX history. The durable record is deliberately opaque: it
 * contains a kind, resource UUID, and local recency timestamp only. Every
 * protected label and action is reauthorized by the server before rendering.
 */
@Injectable({ providedIn: 'root' })
export class ContinueWorkingHistoryService {
  private readonly auth = inject(AuthSessionFacade);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly storage = inject(COGLATAS_CONTINUE_WORKING_STORAGE);
  private readonly now = inject(COGLATAS_CONTINUE_WORKING_NOW);

  resolveCurrentScope(workspaceId: string | null | undefined): ContinueWorkingScope | null {
    const session = this.auth.session();
    const tenantId = session.currentTenant?.tenantId;
    const userId = session.currentUser?.userId;
    const activeWorkspaceId = normalizeUuid(this.activeWorkspace.activeWorkspace()?.id);
    const requestedWorkspaceId = normalizeUuid(workspaceId);
    if (
      session.status !== 'active' ||
      !session.isAuthenticated ||
      !session.currentTenant?.isAvailable ||
      session.currentTenant.isPlatformScope ||
      !isIdentityPart(tenantId) ||
      !isIdentityPart(userId) ||
      !activeWorkspaceId ||
      activeWorkspaceId !== requestedWorkspaceId
    ) {
      return null;
    }

    return {
      tenantId: tenantId.trim(),
      userId: userId.trim(),
      workspaceId: activeWorkspaceId,
    };
  }

  read(scope: ContinueWorkingScope): ContinueWorkingHistoryRead {
    if (!this.storage) {
      return { status: 'storageUnavailable', entries: [] };
    }

    const key = historyKey(scope);
    let raw: string | null;
    try {
      raw = this.storage.getItem(key);
    } catch {
      return { status: 'storageUnavailable', entries: [] };
    }
    if (raw === null) {
      return { status: 'ready', entries: [] };
    }

    const parsed = parseStoredHistory(raw);
    if (!parsed) {
      try {
        this.storage.removeItem(key);
      } catch {
        return { status: 'storageUnavailable', entries: [] };
      }
      return { status: 'discarded', entries: [] };
    }

    const canonical = serialize(parsed);
    if (canonical !== raw) {
      try {
        this.storage.setItem(key, canonical);
      } catch {
        return { status: 'storageUnavailable', entries: [] };
      }
    }
    return { status: 'ready', entries: parsed.items };
  }

  touchProject(resourceId: string, workspaceId: string | null | undefined): ContinueWorkingHistoryEntry | null {
    return this.touch('project', resourceId, workspaceId);
  }

  touchFile(resourceId: string, workspaceId: string | null | undefined): ContinueWorkingHistoryEntry | null {
    return this.touch('file', resourceId, workspaceId);
  }

  removeEntries(
    scope: ContinueWorkingScope,
    entries: readonly Pick<ContinueWorkingHistoryEntry, 'kind' | 'resourceId'>[],
  ): boolean {
    if (entries.length === 0) {
      return true;
    }
    const current = this.read(scope);
    if (current.status === 'storageUnavailable') {
      return false;
    }
    const removed = new Set(entries.map((entry) => entryKey(entry.kind, entry.resourceId)));
    return this.write(scope, current.entries.filter((entry) => !removed.has(entryKey(entry.kind, entry.resourceId))));
  }

  private touch(
    kind: ContinueWorkingKind,
    resourceId: string,
    workspaceId: string | null | undefined,
  ): ContinueWorkingHistoryEntry | null {
    const scope = this.resolveCurrentScope(workspaceId);
    const normalizedResourceId = normalizeUuid(resourceId);
    if (!scope || !normalizedResourceId) {
      return null;
    }

    const current = this.read(scope);
    if (current.status === 'storageUnavailable') {
      return null;
    }
    const now = this.now();
    if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
      return null;
    }
    const touched: ContinueWorkingHistoryEntry = {
      kind,
      resourceId: normalizedResourceId,
      lastOpenedUtc: now.toISOString(),
    };
    const next = [
      touched,
      ...current.entries.filter((entry) => entryKey(entry.kind, entry.resourceId) !== entryKey(kind, normalizedResourceId)),
    ].slice(0, maximumStoredEntries);
    return this.write(scope, next) ? touched : null;
  }

  private write(scope: ContinueWorkingScope, entries: readonly ContinueWorkingHistoryEntry[]): boolean {
    if (!this.storage) {
      return false;
    }
    const value: StoredHistory = {
      version: historyVersion,
      items: normalizeEntries(entries),
    };
    try {
      this.storage.setItem(historyKey(scope), serialize(value));
      return true;
    } catch {
      return false;
    }
  }
}

function parseStoredHistory(raw: string): StoredHistory | null {
  if (raw.length === 0 || raw.length > maximumRawRecordLength) {
    return null;
  }
  try {
    const value: unknown = JSON.parse(raw);
    if (
      !isRecord(value) ||
      !hasExactKeys(value, ['version', 'items']) ||
      value['version'] !== historyVersion ||
      !Array.isArray(value['items']) ||
      value['items'].length > 64
    ) {
      return null;
    }

    const items = value['items'].map(parseEntry);
    if (!items.every((entry): entry is ContinueWorkingHistoryEntry => entry !== null)) {
      return null;
    }
    return { version: historyVersion, items: normalizeEntries(items) };
  } catch {
    return null;
  }
}

function parseEntry(value: unknown): ContinueWorkingHistoryEntry | null {
  if (!isRecord(value) || !hasExactKeys(value, ['kind', 'resourceId', 'lastOpenedUtc'])) {
    return null;
  }
  const kind = value['kind'];
  const resourceId = normalizeUuid(value['resourceId']);
  const lastOpenedUtc = normalizeTimestamp(value['lastOpenedUtc']);
  if ((kind !== 'project' && kind !== 'file') || !resourceId || !lastOpenedUtc) {
    return null;
  }
  return { kind, resourceId, lastOpenedUtc };
}

function normalizeEntries(entries: readonly ContinueWorkingHistoryEntry[]): ContinueWorkingHistoryEntry[] {
  const newest = [...entries].sort((left, right) => right.lastOpenedUtc.localeCompare(left.lastOpenedUtc));
  const seen = new Set<string>();
  const normalized: ContinueWorkingHistoryEntry[] = [];
  for (const entry of newest) {
    const key = entryKey(entry.kind, entry.resourceId);
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    normalized.push(entry);
    if (normalized.length === maximumStoredEntries) {
      break;
    }
  }
  return normalized;
}

function historyKey(scope: ContinueWorkingScope): string {
  return `coglatas.continue-working.${historyKeyVersion}:${encodeURIComponent(scope.tenantId)}:${encodeURIComponent(scope.userId)}:${encodeURIComponent(scope.workspaceId)}`;
}

function serialize(value: StoredHistory): string {
  return JSON.stringify(value);
}

function entryKey(kind: ContinueWorkingKind, resourceId: string): string {
  return `${kind}:${resourceId.toLowerCase()}`;
}

function normalizeUuid(value: unknown): string | null {
  return typeof value === 'string' && uuidPattern.test(value.trim())
    ? value.trim().toLowerCase()
    : null;
}

function normalizeTimestamp(value: unknown): string | null {
  if (typeof value !== 'string' || value.length > 40) {
    return null;
  }
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) ? new Date(milliseconds).toISOString() : null;
}

function isIdentityPart(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= 512;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
  const keys = Object.keys(value).sort();
  const sortedExpected = [...expected].sort();
  return keys.length === sortedExpected.length && keys.every((key, index) => key === sortedExpected[index]);
}

function browserLocalStorage(): ContinueWorkingStorage | null {
  if (typeof window === 'undefined') {
    return null;
  }
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
