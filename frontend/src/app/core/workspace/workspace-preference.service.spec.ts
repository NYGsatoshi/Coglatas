import { TestBed } from '@angular/core/testing';

import {
  COGLATAS_WORKSPACE_PREFERENCE_STORAGE,
  WorkspacePreferenceService,
  WorkspacePreferenceStorage,
} from './workspace-preference.service';

class MemoryStorage implements WorkspacePreferenceStorage {
  readonly values = new Map<string, string>();

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }
}

describe('WorkspacePreferenceService', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('partitions the opaque last-used Workspace ID by tenant and user', () => {
    const storage = new MemoryStorage();
    TestBed.configureTestingModule({
      providers: [{ provide: COGLATAS_WORKSPACE_PREFERENCE_STORAGE, useValue: storage }],
    });
    const preferences = TestBed.inject(WorkspacePreferenceService);

    expect(preferences.write('tenant/a', 'user:1', 'opaque-workspace-a')).toBe(true);
    expect(preferences.write('tenant/a', 'user:2', 'opaque-workspace-b')).toBe(true);
    expect(preferences.write('tenant/b', 'user:1', 'opaque-workspace-c')).toBe(true);

    expect(preferences.read('tenant/a', 'user:1')).toBe('opaque-workspace-a');
    expect(preferences.read('tenant/a', 'user:2')).toBe('opaque-workspace-b');
    expect(preferences.read('tenant/b', 'user:1')).toBe('opaque-workspace-c');
    expect(storage.values.size).toBe(3);
  });

  it('clears only the requested identity preference', () => {
    const storage = new MemoryStorage();
    TestBed.configureTestingModule({
      providers: [{ provide: COGLATAS_WORKSPACE_PREFERENCE_STORAGE, useValue: storage }],
    });
    const preferences = TestBed.inject(WorkspacePreferenceService);

    preferences.write('tenant-a', 'user-a', 'workspace-a');
    preferences.write('tenant-a', 'user-b', 'workspace-b');

    expect(preferences.clear('tenant-a', 'user-a')).toBe(true);
    expect(preferences.read('tenant-a', 'user-a')).toBeNull();
    expect(preferences.read('tenant-a', 'user-b')).toBe('workspace-b');
  });

  it('fails closed when browser storage cannot be read, written, or removed', () => {
    const unavailableStorage: WorkspacePreferenceStorage = {
      getItem: () => {
        throw new DOMException('Storage denied', 'SecurityError');
      },
      setItem: () => {
        throw new DOMException('Storage denied', 'SecurityError');
      },
      removeItem: () => {
        throw new DOMException('Storage denied', 'SecurityError');
      },
    };
    TestBed.configureTestingModule({
      providers: [{ provide: COGLATAS_WORKSPACE_PREFERENCE_STORAGE, useValue: unavailableStorage }],
    });
    const preferences = TestBed.inject(WorkspacePreferenceService);

    expect(preferences.read('tenant-a', 'user-a')).toBeNull();
    expect(preferences.write('tenant-a', 'user-a', 'workspace-a')).toBe(false);
    expect(preferences.clear('tenant-a', 'user-a')).toBe(false);
  });

  it('does not address storage with an unresolved identity or empty Workspace ID', () => {
    const storage = new MemoryStorage();
    const getSpy = vi.spyOn(storage, 'getItem');
    const setSpy = vi.spyOn(storage, 'setItem');
    TestBed.configureTestingModule({
      providers: [{ provide: COGLATAS_WORKSPACE_PREFERENCE_STORAGE, useValue: storage }],
    });
    const preferences = TestBed.inject(WorkspacePreferenceService);

    expect(preferences.read('', 'user-a')).toBeNull();
    expect(preferences.write('tenant-a', '', 'workspace-a')).toBe(false);
    expect(preferences.write('tenant-a', 'user-a', '')).toBe(false);
    expect(getSpy).not.toHaveBeenCalled();
    expect(setSpy).not.toHaveBeenCalled();
  });
});
