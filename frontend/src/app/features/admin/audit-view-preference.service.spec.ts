import { TestBed } from '@angular/core/testing';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { EMPTY_AUDIT_FILTERS } from './admin.types';
import {
  COGLATAS_AUDIT_VIEW_STORAGE,
  AuditViewPreferenceService,
  AuditViewStorage,
} from './audit-view-preference.service';

class MemoryAuditViewStorage implements AuditViewStorage {
  readonly values = new Map<string, string>();
  getItem(key: string): string | null { return this.values.get(key) ?? null; }
  setItem(key: string, value: string): void { this.values.set(key, value); }
  removeItem(key: string): void { this.values.delete(key); }
}

describe('AuditViewPreferenceService', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('stores only strict filter inputs in the current Tenant and user partition', () => {
    const storage = new MemoryAuditViewStorage();
    configure(storage);
    const service = TestBed.inject(AuditViewPreferenceService);

    const result = service.save('Failed exports', {
      ...EMPTY_AUDIT_FILTERS,
      q: 'retention',
      severity: 'critical',
      status: 'failed',
      range: '7d',
    });

    expect(result.status).toBe('ready');
    expect(result.views).toHaveLength(1);
    const [[key, raw]] = [...storage.values.entries()];
    expect(key).toContain(':mock-tenant:mock-user-a');
    expect(raw).toContain('Failed exports');
    expect(raw).toContain('retention');
    expect(raw).not.toContain('totalCount');
    expect(raw).not.toContain('rows');
    expect(raw).not.toContain('capabilit');
  });

  it('does not expose a saved view to another authenticated user', () => {
    const storage = new MemoryAuditViewStorage();
    configure(storage);
    expect(TestBed.inject(AuditViewPreferenceService).save('My view', EMPTY_AUDIT_FILTERS).status).toBe('ready');

    TestBed.resetTestingModule();
    configure(storage, 'another-user');
    expect(TestBed.inject(AuditViewPreferenceService).load()).toEqual({ status: 'ready', views: [] });
  });

  it('discards a malformed record instead of partially applying it', () => {
    const storage = new MemoryAuditViewStorage();
    storage.values.set(
      'coglatas.audit.saved-views.v1:mock-tenant:mock-user-a',
      JSON.stringify({ version: 1, views: [{ id: 'audit-invalid', name: 'Unsafe', snapshot: { q: 'x' }, totalCount: 99 }] }),
    );
    configure(storage);

    expect(TestBed.inject(AuditViewPreferenceService).load()).toEqual({ status: 'discarded', views: [] });
    expect(storage.values.size).toBe(0);
  });
});

function configure(storage: MemoryAuditViewStorage, userId = 'mock-user-a'): void {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: COGLATAS_AUTH_SESSION_MOCK,
        useValue: {
          ...DEFAULT_AUTH_SESSION,
          currentUser: { ...DEFAULT_AUTH_SESSION.currentUser!, userId },
        },
      },
      { provide: COGLATAS_AUDIT_VIEW_STORAGE, useValue: storage },
    ],
  });
}
