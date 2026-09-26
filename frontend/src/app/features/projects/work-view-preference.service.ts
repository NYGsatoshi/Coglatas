import { Injectable, InjectionToken, inject } from '@angular/core';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { createLocalOpaqueId } from '../../core/security/client-id';
import {
  MyTasksBlockedFilter,
  MyTasksPriorityFilter,
  MyTasksSavedFilter,
  MyTasksSavedFilterSnapshot,
  MyTasksStageCategoryFilter,
  MyTasksTab,
  MyTasksUrgencyGroup
} from './projects.types';

export type MyTasksProjection = 'list' | 'kanban';

export interface WorkViewPreferenceStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export type SavedFiltersStatus =
  | 'ready'
  | 'identityUnavailable'
  | 'storageUnavailable'
  | 'discarded'
  | 'invalidInput';

export interface SavedFiltersResult {
  readonly status: SavedFiltersStatus;
  readonly filters: readonly MyTasksSavedFilter[];
}

export const COGLATAS_WORK_VIEW_PREFERENCE_STORAGE = new InjectionToken<WorkViewPreferenceStorage | null>(
  'COGLATAS_WORK_VIEW_PREFERENCE_STORAGE',
  { providedIn: 'root', factory: browserLocalStorage }
);

const projectionPreferenceVersion = 'v1';
const savedFilterVersion = 1;
const savedFilterKeyVersion = 'v1';
const myTasksScreenId = 'my-tasks';
const maximumSavedFilters = 20;

interface StoredSavedFilters {
  readonly version: 1;
  readonly filters: readonly MyTasksSavedFilter[];
}

/**
 * Browser-only presentation preferences. Keys are partitioned by the current
 * authenticated Tenant and user. Saved records contain filter inputs only;
 * task rows, counts, display labels, permissions, and authorization state are
 * never persisted here and every applied filter is reauthorized by HTTP.
 */
@Injectable({ providedIn: 'root' })
export class WorkViewPreferenceService {
  private readonly auth = inject(AuthSessionFacade);
  private readonly storage = inject(COGLATAS_WORK_VIEW_PREFERENCE_STORAGE);

  loadMyTasksProjection(): MyTasksProjection {
    const identity = this.identity();
    if (!identity) {return 'list';}
    const value = this.readRaw(this.projectionKey(identity));
    // PR04 owns a cross-Project List only. A stale pre-canonical Kanban value
    // must never select an unsupported projection or block /tasks.
    if (value === 'kanban') {this.writeRaw(this.projectionKey(identity), 'list');}
    return 'list';
  }

  saveMyTasksProjection(projection: MyTasksProjection): void {
    const identity = this.identity();
    if (!identity) {return;}
    this.writeRaw(this.projectionKey(identity), projection === 'kanban' ? 'list' : projection);
  }

  loadMyTasksSavedFilters(): SavedFiltersResult {
    const identity = this.identity();
    if (!identity) {return { status: 'identityUnavailable', filters: [] };}
    if (!this.storage) {return { status: 'storageUnavailable', filters: [] };}

    const key = this.savedFiltersKey(identity);
    let raw: string | null;
    try {
      raw = this.storage.getItem(key);
    } catch {
      return { status: 'storageUnavailable', filters: [] };
    }
    if (raw === null) {return { status: 'ready', filters: [] };}

    const parsed = parseStoredSavedFilters(raw);
    if (parsed) {return { status: 'ready', filters: parsed.filters };}

    try {
      this.storage.removeItem(key);
    } catch {
      return { status: 'storageUnavailable', filters: [] };
    }
    return { status: 'discarded', filters: [] };
  }

  saveMyTasksFilter(name: string, snapshot: MyTasksSavedFilterSnapshot): SavedFiltersResult {
    const identity = this.identity();
    if (!identity) {return { status: 'identityUnavailable', filters: [] };}
    const normalizedName = name.trim();
    const normalizedSnapshot = normalizeSnapshot(snapshot);
    if (!isValidName(normalizedName) || !normalizedSnapshot) {
      return { status: 'invalidInput', filters: [] };
    }

    const current = this.loadMyTasksSavedFilters();
    if (current.status !== 'ready' && current.status !== 'discarded') {return current;}
    const existing = current.filters.find((filter) => filter.name.toLowerCase() === normalizedName.toLowerCase());
    if (!existing && current.filters.length >= maximumSavedFilters) {
      return { status: 'invalidInput', filters: current.filters };
    }

    const saved: MyTasksSavedFilter = {
      id: existing?.id ?? createSavedFilterId(),
      name: normalizedName,
      snapshot: normalizedSnapshot
    };
    const filters = existing
      ? current.filters.map((filter) => filter.id === existing.id ? saved : filter)
      : [...current.filters, saved];
    return this.writeSavedFilters(identity, filters, current.filters);
  }

  deleteMyTasksFilter(filterId: string): SavedFiltersResult {
    const identity = this.identity();
    if (!identity) {return { status: 'identityUnavailable', filters: [] };}
    const current = this.loadMyTasksSavedFilters();
    if (current.status !== 'ready') {return current;}
    if (!current.filters.some((filter) => filter.id === filterId)) {
      return { status: 'invalidInput', filters: current.filters };
    }
    return this.writeSavedFilters(identity, current.filters.filter((filter) => filter.id !== filterId), current.filters);
  }

  private writeSavedFilters(
    identity: { readonly tenantId: string; readonly userId: string },
    filters: readonly MyTasksSavedFilter[],
    persistedFilters: readonly MyTasksSavedFilter[]
  ): SavedFiltersResult {
    if (!this.storage) {return { status: 'storageUnavailable', filters: persistedFilters };}
    const value: StoredSavedFilters = { version: savedFilterVersion, filters };
    try {
      this.storage.setItem(this.savedFiltersKey(identity), JSON.stringify(value));
      return { status: 'ready', filters };
    } catch {
      return { status: 'storageUnavailable', filters: persistedFilters };
    }
  }

  private identity(): { readonly tenantId: string; readonly userId: string } | null {
    const session = this.auth.session();
    const tenant = session.currentTenant;
    const user = session.currentUser;
    if (
      session.status !== 'active' ||
      !session.isAuthenticated ||
      !tenant?.isAvailable ||
      tenant.isPlatformScope ||
      !isIdentityPart(tenant.tenantId) ||
      !isIdentityPart(user?.userId)
    ) {return null;}
    return { tenantId: tenant.tenantId, userId: user.userId };
  }

  private readRaw(key: string): string | null {
    if (!this.storage) {return null;}
    try { return this.storage.getItem(key); } catch { return null; }
  }

  private writeRaw(key: string, value: string): boolean {
    if (!this.storage) {return false;}
    try {
      this.storage.setItem(key, value);
      return true;
    } catch {
      return false;
    }
  }

  private projectionKey(identity: { readonly tenantId: string; readonly userId: string }): string {
    return `coglatas.work-view.${projectionPreferenceVersion}.${identity.tenantId}.${identity.userId}.${myTasksScreenId}`;
  }

  private savedFiltersKey(identity: { readonly tenantId: string; readonly userId: string }): string {
    return `coglatas.work-view.saved-filters.${savedFilterKeyVersion}:${encodeURIComponent(identity.tenantId)}:${encodeURIComponent(identity.userId)}:${myTasksScreenId}`;
  }
}

function parseStoredSavedFilters(raw: string): StoredSavedFilters | null {
  try {
    const value: unknown = JSON.parse(raw);
    if (!isRecord(value) || !hasExactKeys(value, ['version', 'filters']) || value['version'] !== savedFilterVersion || !Array.isArray(value['filters'])) {
      return null;
    }
    if (value['filters'].length > maximumSavedFilters) {return null;}
    const filters = value['filters'].map(parseSavedFilter);
    if (!filters.every((filter): filter is MyTasksSavedFilter => filter !== null)) {return null;}
    const ids = new Set(filters.map((filter) => filter.id));
    const names = new Set(filters.map((filter) => filter.name.toLowerCase()));
    return ids.size === filters.length && names.size === filters.length
      ? { version: savedFilterVersion, filters }
      : null;
  } catch {
    return null;
  }
}

function parseSavedFilter(value: unknown): MyTasksSavedFilter | null {
  if (!isRecord(value) || !hasExactKeys(value, ['id', 'name', 'snapshot'])) {return null;}
  if (typeof value['id'] !== 'string' || !/^saved-[A-Za-z0-9-]{8,80}$/u.test(value['id']) || typeof value['name'] !== 'string' || !isValidName(value['name'])) {return null;}
  const snapshot = parseSnapshot(value['snapshot']);
  return snapshot ? { id: value['id'], name: value['name'], snapshot } : null;
}

function normalizeSnapshot(value: MyTasksSavedFilterSnapshot): MyTasksSavedFilterSnapshot | null {
  return parseSnapshot({
    selectedTab: value.selectedTab,
    projectId: value.projectId.trim(),
    stageCategory: value.stageCategory,
    priority: value.priority,
    blocked: value.blocked,
    search: value.search.trim(),
    timeGroup: value.timeGroup
  });
}

function parseSnapshot(value: unknown): MyTasksSavedFilterSnapshot | null {
  if (!isRecord(value) || !hasExactKeys(value, ['selectedTab', 'projectId', 'stageCategory', 'priority', 'blocked', 'search', 'timeGroup'])) {return null;}
  const selectedTab = value['selectedTab'];
  const projectId = value['projectId'];
  const stageCategory = value['stageCategory'];
  const priority = value['priority'];
  const blocked = value['blocked'];
  const search = value['search'];
  const timeGroup = value['timeGroup'];
  if (
    !myTasksTabs.includes(selectedTab as MyTasksTab) ||
    typeof projectId !== 'string' || (projectId !== '' && !guidPattern.test(projectId)) ||
    !stageCategories.includes(stageCategory as MyTasksStageCategoryFilter) ||
    !priorities.includes(priority as MyTasksPriorityFilter) ||
    !blockedValues.includes(blocked as MyTasksBlockedFilter) ||
    typeof search !== 'string' || search.length > 200 || search !== search.trim() ||
    !(timeGroup === null || urgencyGroups.includes(timeGroup as MyTasksUrgencyGroup))
  ) {return null;}
  return {
    selectedTab: selectedTab as MyTasksTab,
    projectId,
    stageCategory: stageCategory as MyTasksStageCategoryFilter,
    priority: priority as MyTasksPriorityFilter,
    blocked: blocked as MyTasksBlockedFilter,
    search,
    timeGroup: timeGroup as MyTasksUrgencyGroup | null
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
  const keys = Object.keys(value).sort();
  return keys.length === expected.length && [...expected].sort().every((key, index) => keys[index] === key);
}

function isValidName(value: string): boolean {
  return value.length > 0 && value.length <= 80 && value === value.trim();
}

function isIdentityPart(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function createSavedFilterId(): string {
  return createLocalOpaqueId('saved');
}

function browserLocalStorage(): WorkViewPreferenceStorage | null {
  if (typeof window === 'undefined') {return null;}
  try { return window.localStorage; } catch { return null; }
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const myTasksTabs: readonly MyTasksTab[] = ['assigned', 'participating', 'reviews', 'created', 'watching', 'teamQueue', 'completed'];
const stageCategories: readonly MyTasksStageCategoryFilter[] = ['', 'backlog', 'todo', 'inProgress', 'review', 'done', 'cancelled'];
const priorities: readonly MyTasksPriorityFilter[] = ['', 'low', 'medium', 'high', 'critical'];
const blockedValues: readonly MyTasksBlockedFilter[] = ['', 'true', 'false'];
const urgencyGroups: readonly MyTasksUrgencyGroup[] = ['overdue', 'today', 'next7Days', 'later', 'noDeadline'];
