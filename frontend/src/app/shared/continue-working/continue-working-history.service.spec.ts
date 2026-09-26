import { TestBed } from '@angular/core/testing';

import { COGLATAS_AUTH_SESSION_MOCK, AuthSessionSnapshot } from '../../core/auth/auth-session.facade';
import { COGLATAS_ACTIVE_WORKSPACE_MOCK } from '../../core/workspace/active-workspace.facade';
import {
  COGLATAS_CONTINUE_WORKING_NOW,
  COGLATAS_CONTINUE_WORKING_STORAGE,
  ContinueWorkingHistoryService,
  ContinueWorkingStorage,
} from './continue-working-history.service';

const TENANT_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const WORKSPACE_ID = '33333333-3333-4333-8333-333333333333';
const PROJECT_ID = '44444444-4444-4444-8444-444444444444';
const FILE_ID = '55555555-5555-4555-8555-555555555555';

class MemoryStorage implements ContinueWorkingStorage {
  readonly values = new Map<string, string>();
  getThrows = false;
  setThrows = false;
  removeThrows = false;

  getItem(key: string): string | null {
    if (this.getThrows) {throw new DOMException('read denied', 'SecurityError');}
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    if (this.setThrows) {throw new DOMException('write denied', 'SecurityError');}
    this.values.set(key, value);
  }
  removeItem(key: string): void {
    if (this.removeThrows) {throw new DOMException('remove denied', 'SecurityError');}
    this.values.delete(key);
  }
}

describe('ContinueWorkingHistoryService', () => {
  let storage: MemoryStorage;

  beforeEach(() => {
    storage = new MemoryStorage();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('stores only the strict opaque record in the Tenant/user/Workspace partition', () => {
    const service = configure();

    expect(service.touchProject(PROJECT_ID, WORKSPACE_ID)).toEqual({
      kind: 'project',
      resourceId: PROJECT_ID,
      lastOpenedUtc: '2026-08-28T01:02:03.000Z',
    });
    expect(service.touchFile(FILE_ID, WORKSPACE_ID)?.kind).toBe('file');

    const [[key, raw]] = [...storage.values.entries()];
    expect(key).toBe(`coglatas.continue-working.v1:${TENANT_ID}:${USER_ID}:${WORKSPACE_ID}`);
    expect(JSON.parse(raw)).toEqual({
      version: 1,
      items: [
        { kind: 'file', resourceId: FILE_ID, lastOpenedUtc: '2026-08-28T01:02:03.000Z' },
        { kind: 'project', resourceId: PROJECT_ID, lastOpenedUtc: '2026-08-28T01:02:03.000Z' },
      ],
    });
    expect(raw).not.toMatch(/title|filename|status|permission|capabilit|token|content|collaborator/iu);
  });

  it('normalizes duplicate history and caps hydration input at eight opaque entries', () => {
    const service = configure();
    const scope = service.resolveCurrentScope(WORKSPACE_ID)!;
    const items = Array.from({ length: 10 }, (_, index) => ({
      kind: 'project',
      resourceId: `${String(index + 1).padStart(8, '0')}-1111-4111-8111-111111111111`,
      lastOpenedUtc: `2026-08-${String(10 + index).padStart(2, '0')}T00:00:00.000Z`,
    }));
    items.push({ ...items[9], lastOpenedUtc: '2026-08-28T00:00:00.000Z' });
    storage.values.set(key(), JSON.stringify({ version: 1, items }));

    const result = service.read(scope);

    expect(result.status).toBe('ready');
    expect(result.entries).toHaveLength(8);
    expect(new Set(result.entries.map((entry) => `${entry.kind}:${entry.resourceId}`)).size).toBe(8);
    expect(JSON.parse(storage.values.get(key())!).items).toHaveLength(8);
  });

  it.each([
    ['malformed JSON', '{'],
    ['unknown version', JSON.stringify({ version: 2, items: [] })],
    ['protected metadata', JSON.stringify({ version: 1, items: [{ kind: 'file', resourceId: FILE_ID, lastOpenedUtc: '2026-08-28T00:00:00Z', title: 'secret.pdf' }] })],
    ['invalid UUID', JSON.stringify({ version: 1, items: [{ kind: 'file', resourceId: 'file-secret', lastOpenedUtc: '2026-08-28T00:00:00Z' }] })],
  ])('discards %s rather than applying it', (_name, raw) => {
    const service = configure();
    const scope = service.resolveCurrentScope(WORKSPACE_ID)!;
    storage.values.set(key(), raw);

    expect(service.read(scope)).toEqual({ status: 'discarded', entries: [] });
    expect(storage.values.has(key())).toBe(false);
  });

  it('fails closed for unresolved scope, cross-Workspace touch, and storage failure', () => {
    const service = configure();
    expect(service.touchProject(PROJECT_ID, '66666666-6666-4666-8666-666666666666')).toBeNull();
    expect(storage.values.size).toBe(0);

    storage.setThrows = true;
    expect(service.touchProject(PROJECT_ID, WORKSPACE_ID)).toBeNull();
    expect(storage.values.size).toBe(0);

    storage.setThrows = false;
    storage.getThrows = true;
    expect(service.read(service.resolveCurrentScope(WORKSPACE_ID)!)).toEqual({
      status: 'storageUnavailable',
      entries: [],
    });
  });

  it('preserves a previous identity bucket while another user receives an isolated bucket', () => {
    configure().touchProject(PROJECT_ID, WORKSPACE_ID);
    TestBed.resetTestingModule();
    const firstKey = key();
    const secondUser = '77777777-7777-4777-8777-777777777777';
    configure(secondUser).touchFile(FILE_ID, WORKSPACE_ID);

    expect(storage.values.has(firstKey)).toBe(true);
    expect(storage.values.has(`coglatas.continue-working.v1:${TENANT_ID}:${secondUser}:${WORKSPACE_ID}`)).toBe(true);
    expect(storage.values.size).toBe(2);
  });

  function configure(userId = USER_ID): ContinueWorkingHistoryService {
    TestBed.configureTestingModule({ providers: [
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: session(userId) },
      { provide: COGLATAS_ACTIVE_WORKSPACE_MOCK, useValue: { id: WORKSPACE_ID, label: 'Workspace' } },
      { provide: COGLATAS_CONTINUE_WORKING_STORAGE, useValue: storage },
      { provide: COGLATAS_CONTINUE_WORKING_NOW, useValue: () => new Date('2026-08-28T01:02:03.000Z') },
    ] });
    return TestBed.inject(ContinueWorkingHistoryService);
  }

  function key(): string {
    return `coglatas.continue-working.v1:${TENANT_ID}:${USER_ID}:${WORKSPACE_ID}`;
  }
});

function session(userId: string): AuthSessionSnapshot {
  return {
    status: 'active',
    isAuthenticated: true,
    displayName: 'History user',
    supportingUsers: [],
    capabilities: ['projects:view', 'files:view'],
    currentUser: {
      userId,
      displayName: 'History user',
      email: 'history@example.test',
      systemRole: 'TenantUser',
      status: 'Active',
      capabilities: ['projects:view', 'files:view'],
      currentWorkspace: { id: WORKSPACE_ID, label: 'Workspace' },
      workspaces: [{ id: WORKSPACE_ID, label: 'Workspace' }],
    },
    currentTenant: {
      tenantId: TENANT_ID,
      isAvailable: true,
      isPlatformScope: false,
      allowTenantSwitching: false,
    },
    navigation: { capabilities: ['projects:view', 'files:view'], isLoaded: true },
  };
}
