import { TestBed } from '@angular/core/testing';

import { COGLATAS_AUTH_SESSION_MOCK, AuthSessionSnapshot } from '../../core/auth/auth-session.facade';
import { MyTasksSavedFilterSnapshot } from './projects.types';
import {
  COGLATAS_WORK_VIEW_PREFERENCE_STORAGE,
  WorkViewPreferenceService,
  WorkViewPreferenceStorage
} from './work-view-preference.service';

const projectId = '11111111-1111-4111-8111-111111111111';
const snapshot: MyTasksSavedFilterSnapshot = {
  selectedTab: 'reviews',
  projectId,
  stageCategory: 'review',
  priority: 'high',
  blocked: 'false',
  search: 'approval evidence',
  timeGroup: 'today'
};

const session = (tenantId: string, userId: string): AuthSessionSnapshot => ({
  status: 'active', isAuthenticated: true, displayName: userId, supportingUsers: [], capabilities: [],
  currentUser: { userId, displayName: userId, email: `${userId}@example.test`, systemRole: 'TenantUser', status: 'Active', capabilities: [], currentWorkspace: null, workspaces: [] },
  currentTenant: { tenantId, isAvailable: true, isPlatformScope: false, allowTenantSwitching: false },
  navigation: { capabilities: [], isLoaded: true }
});

class PreferenceStorage implements WorkViewPreferenceStorage {
  readonly values = new Map<string, string>();
  getCalls = 0;
  setCalls = 0;
  removeCalls = 0;
  getThrows = false;
  setThrows = false;
  removeThrows = false;

  getItem(key: string): string | null {
    this.getCalls++;
    if (this.getThrows) {throw new Error('read unavailable');}
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    this.setCalls++;
    if (this.setThrows) {throw new Error('write unavailable');}
    this.values.set(key, value);
  }
  removeItem(key: string): void {
    this.removeCalls++;
    if (this.removeThrows) {throw new Error('remove unavailable');}
    this.values.delete(key);
  }
}

describe('WorkViewPreferenceService', () => {
  let storage: PreferenceStorage;

  afterEach(() => TestBed.resetTestingModule());

  it('normalizes a stale My Tasks Kanban preference to the canonical List', () => {
    const service = configure(session('tenant-a', 'user-a'));
    service.saveMyTasksProjection('kanban');

    expect(service.loadMyTasksProjection()).toBe('list');
    expect(storage.values.get('coglatas.work-view.v1.tenant-a.user-a.my-tasks')).toBe('list');
  });

  it('round-trips only the strict versioned filter snapshot in the current Tenant/user namespace', () => {
    const service = configure(session('tenant-a', 'user-a'));
    const saved = service.saveMyTasksFilter('Review evidence', snapshot);

    expect(saved.status).toBe('ready');
    expect(service.loadMyTasksSavedFilters().filters).toEqual(saved.filters);
    const [key, raw] = [...storage.values.entries()].find(([candidate]) => candidate.includes('saved-filters'))!;
    expect(key).toBe('coglatas.work-view.saved-filters.v1:tenant-a:user-a:my-tasks');
    expect(JSON.parse(raw)).toEqual({
      version: 1,
      filters: [{ id: saved.filters[0].id, name: 'Review evidence', snapshot }]
    });
    expect(raw).not.toMatch(/rows|counts|title|workspaceTitle|permissions|authorization/iu);
  });

  it('does not expose one user or Tenant saved filter to another', () => {
    const first = configure(session('tenant-a', 'user-a'));
    expect(first.saveMyTasksFilter('Private filter', snapshot).status).toBe('ready');
    TestBed.resetTestingModule();

    TestBed.configureTestingModule({ providers: [
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: session('tenant-b', 'user-b') },
      { provide: COGLATAS_WORK_VIEW_PREFERENCE_STORAGE, useValue: storage }
    ] });
    expect(TestBed.inject(WorkViewPreferenceService).loadMyTasksSavedFilters()).toEqual({ status: 'ready', filters: [] });
  });

  it('fails empty without touching storage when Tenant or user identity is unresolved', () => {
    const anonymous: AuthSessionSnapshot = {
      ...session('tenant-a', 'user-a'), status: 'anonymous', isAuthenticated: false, currentUser: null, currentTenant: null
    };
    const service = configure(anonymous);

    expect(service.loadMyTasksSavedFilters()).toEqual({ status: 'identityUnavailable', filters: [] });
    expect(service.saveMyTasksFilter('Not saved', snapshot)).toEqual({ status: 'identityUnavailable', filters: [] });
    expect(storage.getCalls).toBe(0);
    expect(storage.setCalls).toBe(0);
  });

  it.each([
    ['malformed JSON', '{'],
    ['unknown version', JSON.stringify({ version: 2, filters: [] })],
    ['unknown enum', stored([{ id: 'saved-12345678', name: 'Bad stage', snapshot: { ...snapshot, stageCategory: 'secretStage' } }])],
    ['unexpected protected field', stored([{ id: 'saved-12345678', name: 'Leaky', snapshot: { ...snapshot, taskTitle: 'must not persist' } }])],
    ['duplicate IDs', stored([
      { id: 'saved-12345678', name: 'One', snapshot },
      { id: 'saved-12345678', name: 'Two', snapshot: { ...snapshot, selectedTab: 'assigned' } }
    ])],
    ['case-insensitive duplicate names', stored([
      { id: 'saved-12345678', name: 'Review', snapshot },
      { id: 'saved-87654321', name: 'review', snapshot: { ...snapshot, selectedTab: 'assigned' } }
    ])]
  ])('discards %s instead of applying it', (_caseName, raw) => {
    const service = configure(session('tenant-a', 'user-a'));
    const key = 'coglatas.work-view.saved-filters.v1:tenant-a:user-a:my-tasks';
    storage.values.set(key, raw);

    expect(service.loadMyTasksSavedFilters()).toEqual({ status: 'discarded', filters: [] });
    expect(storage.values.has(key)).toBe(false);
  });

  it('reports read/remove failures without throwing or exposing a saved record', () => {
    const service = configure(session('tenant-a', 'user-a'));
    storage.getThrows = true;
    expect(service.loadMyTasksSavedFilters()).toEqual({ status: 'storageUnavailable', filters: [] });

    storage.getThrows = false;
    storage.removeThrows = true;
    storage.values.set('coglatas.work-view.saved-filters.v1:tenant-a:user-a:my-tasks', '{');
    expect(service.loadMyTasksSavedFilters()).toEqual({ status: 'storageUnavailable', filters: [] });
  });

  it('keeps the last persisted list when a later setItem fails', () => {
    const service = configure(session('tenant-a', 'user-a'));
    const first = service.saveMyTasksFilter('First', snapshot);
    expect(first.status).toBe('ready');
    const persisted = [...storage.values.values()].find((value) => value.includes('"version":1'));

    storage.setThrows = true;
    const failed = service.saveMyTasksFilter('Second', { ...snapshot, selectedTab: 'assigned' });

    expect(failed.status).toBe('storageUnavailable');
    expect(failed.filters).toEqual(first.filters);
    expect([...storage.values.values()].find((value) => value.includes('"version":1'))).toBe(persisted);
    expect(service.loadMyTasksSavedFilters().filters).toEqual(first.filters);
  });

  it('updates a case-insensitive same-name entry instead of creating an ambiguous duplicate', () => {
    const service = configure(session('tenant-a', 'user-a'));
    const first = service.saveMyTasksFilter('Review', snapshot);
    const updated = service.saveMyTasksFilter('review', { ...snapshot, selectedTab: 'assigned' });

    expect(updated.status).toBe('ready');
    expect(updated.filters).toHaveLength(1);
    expect(updated.filters[0].id).toBe(first.filters[0].id);
    expect(updated.filters[0].name).toBe('review');
    expect(updated.filters[0].snapshot.selectedTab).toBe('assigned');
  });

  function configure(auth: AuthSessionSnapshot): WorkViewPreferenceService {
    storage = new PreferenceStorage();
    TestBed.configureTestingModule({ providers: [
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: auth },
      { provide: COGLATAS_WORK_VIEW_PREFERENCE_STORAGE, useValue: storage }
    ] });
    return TestBed.inject(WorkViewPreferenceService);
  }
});

function stored(filters: readonly unknown[]): string {
  return JSON.stringify({ version: 1, filters });
}
