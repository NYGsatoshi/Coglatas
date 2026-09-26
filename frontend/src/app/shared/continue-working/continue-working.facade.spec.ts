import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { COGLATAS_AUTH_SESSION_MOCK, AuthSessionSnapshot } from '../../core/auth/auth-session.facade';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { COGLATAS_ACTIVE_WORKSPACE_MOCK, ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import {
  COGLATAS_CONTINUE_WORKING_NOW,
  COGLATAS_CONTINUE_WORKING_STORAGE,
  ContinueWorkingStorage,
} from './continue-working-history.service';
import { ContinueWorkingFacade } from './continue-working.facade';

const TENANT_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const WORKSPACE_ID = '33333333-3333-4333-8333-333333333333';
const OTHER_WORKSPACE_ID = '99999999-9999-4999-8999-999999999999';
const PROJECT_IDS = [
  '40000000-0000-4000-8000-000000000001',
  '40000000-0000-4000-8000-000000000002',
  '40000000-0000-4000-8000-000000000003',
  '40000000-0000-4000-8000-000000000004',
  '40000000-0000-4000-8000-000000000005',
] as const;
const DISPLAY_PROJECT_IDS = Array.from(
  { length: 8 },
  (_, index) => `41000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
);
const FILE_ID = '50000000-0000-4000-8000-000000000001';
const OTHER_FILE_ID = '50000000-0000-4000-8000-000000000002';
const GRANT_ID = '60000000-0000-4000-8000-000000000001';

class MemoryStorage implements ContinueWorkingStorage {
  readonly values = new Map<string, string>();
  getItem(key: string): string | null { return this.values.get(key) ?? null; }
  setItem(key: string, value: string): void { this.values.set(key, value); }
  removeItem(key: string): void { this.values.delete(key); }
}

describe('ContinueWorkingFacade', () => {
  let storage: MemoryStorage;
  let http: HttpTestingController;
  let facade: ContinueWorkingFacade;
  let activeWorkspace: ActiveWorkspaceFacade;
  let clearProtected: ((reason: ProtectedStateClearReason) => void) | undefined;
  let catchUp: (() => void) | undefined;

  beforeEach(() => {
    storage = new MemoryStorage();
    clearProtected = undefined;
    catchUp = undefined;
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: session() },
      { provide: COGLATAS_ACTIVE_WORKSPACE_MOCK, useValue: { id: WORKSPACE_ID, label: 'Workspace' } },
      { provide: COGLATAS_CONTINUE_WORKING_STORAGE, useValue: storage },
      { provide: COGLATAS_CONTINUE_WORKING_NOW, useValue: () => new Date('2026-08-28T03:00:00.000Z') },
      {
        provide: RealtimeFacade,
        useValue: {
          registerProtectedStateClearer: (_owner: string, clear: (reason: ProtectedStateClearReason) => void) => {
            clearProtected = clear;
            return () => { clearProtected = undefined; };
          },
          registerCatchUp: (_owner: string, callback: () => void) => {
            catchUp = callback;
            return () => { catchUp = undefined; };
          },
        },
      },
    ] });
    http = TestBed.inject(HttpTestingController);
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('hydrates no more than three exact resources concurrently and preserves local recency order', () => {
    seed(PROJECT_IDS.map((resourceId, index) => history('project', resourceId, 20 - index)));
    activate();

    const initial = http.match((request) => request.method === 'GET' && request.url.startsWith('/api/projects/'));
    expect(initial).toHaveLength(3);
    expect(initial.map(idFromRequest)).toEqual(PROJECT_IDS.slice(0, 3));
    initial[0].flush(project(PROJECT_IDS[0], 'Research 1', 'Active'));
    const fourth = http.expectOne(`/api/projects/${PROJECT_IDS[3]}`);
    initial[1].flush(project(PROJECT_IDS[1], 'Research 2', 'Review'));
    const fifth = http.expectOne(`/api/projects/${PROJECT_IDS[4]}`);
    initial[2].flush(project(PROJECT_IDS[2], 'Research 3', 'Planning'));
    fourth.flush(project(PROJECT_IDS[3], 'Research 4', 'Complete'));
    fifth.flush(project(PROJECT_IDS[4], 'Research 5', 'Suspended'));

    expect(facade.view().status).toBe('ready');
    expect(facade.view().items.map((item) => item.resourceId)).toEqual(PROJECT_IDS);
    expect(facade.view().items.map((item) => item.status)).toEqual([
      'running', 'needsReview', 'draft', 'completed', 'paused',
    ]);
  });

  it('hydrates the capped eight-entry history but displays only the six newest authorized items', () => {
    seed(DISPLAY_PROJECT_IDS.map((resourceId, index) => history('project', resourceId, 20 - index)));
    activate();

    let hydrated = 0;
    while (hydrated < DISPLAY_PROJECT_IDS.length) {
      const pending = http.match((request) => request.method === 'GET' && request.url.startsWith('/api/projects/'));
      expect(pending.length).toBeGreaterThan(0);
      expect(pending.length).toBeLessThanOrEqual(3);
      for (const request of pending) {
        const id = idFromRequest(request);
        request.flush(project(id, `Research ${hydrated + 1}`, 'Active'));
        hydrated += 1;
      }
    }

    expect(facade.view().items).toHaveLength(6);
    expect(facade.view().items.map((item) => item.resourceId)).toEqual(DISPLAY_PROJECT_IDS.slice(0, 6));
    expect(JSON.parse(rawHistory()).items).toHaveLength(8);
  });

  it('renders only current server metadata, maps a redacted filename to generic File, and prunes scope mismatch', () => {
    seed([
      history('project', PROJECT_IDS[0], 2),
      history('file', FILE_ID, 1),
      history('project', PROJECT_IDS[1], 0),
    ]);
    activate();

    http.expectOne(`/api/projects/${PROJECT_IDS[0]}`).flush(project(PROJECT_IDS[0], 'Server Research', 'Archived'));
    http.expectOne(`/api/files/${FILE_ID}`).flush(file(FILE_ID, '[redacted:file]', 'Quarantined'));
    http.expectOne(`/api/projects/${PROJECT_IDS[1]}`).flush({
      ...project(PROJECT_IDS[1], 'Wrong Workspace', 'Active'),
      workspaceId: OTHER_WORKSPACE_ID,
    });

    expect(facade.view().items).toEqual([
      expect.objectContaining({ kind: 'project', title: 'Server Research', status: 'archived' }),
      expect.objectContaining({ kind: 'file', title: 'File', status: 'needsAttention' }),
    ]);
    expect(rawHistory()).not.toContain(PROJECT_IDS[1]);
    expect(rawHistory()).not.toContain('Server Research');
    expect(rawHistory()).not.toContain('[redacted:file]');
  });

  it('retains an opaque transient entry but renders no cached metadata', () => {
    seed([history('file', FILE_ID, 1)]);
    activate();

    http.expectOne(`/api/files/${FILE_ID}`).flush(
      { message: 'offline' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(facade.view()).toMatchObject({ status: 'error', items: [], retryAvailable: true });
    expect(facade.view().message).toContain('No cached labels');
    expect(rawHistory()).toContain(FILE_ID);
  });

  it('omits and prunes a denied Project 404 and masked File 400 without leaking response metadata', () => {
    seed([
      history('project', PROJECT_IDS[0], 2),
      history('file', FILE_ID, 1),
    ]);
    activate();

    http.expectOne(`/api/projects/${PROJECT_IDS[0]}`).flush(
      { error: { code: 'NotFound', message: 'Hidden Research title and count 41' } },
      { status: 404, statusText: 'Not Found' },
    );
    http.expectOne(`/api/files/${FILE_ID}`).flush(
      { error: { code: 'FileMetadataFailed', message: 'Hidden filename.pdf and count 23' } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(facade.view()).toMatchObject({ status: 'empty', items: [], retryAvailable: false });
    expect(JSON.stringify(facade.view())).not.toMatch(/Hidden Research|Hidden filename|41|23/iu);
    expect(rawHistory()).not.toContain(PROJECT_IDS[0]);
    expect(rawHistory()).not.toContain(FILE_ID);
  });

  it('clears immediately on realtime authorization invalidation, cancels a late response, and rehydrates on catch-up', () => {
    seed([history('project', PROJECT_IDS[0], 1)]);
    activate();
    const stale = http.expectOne(`/api/projects/${PROJECT_IDS[0]}`);

    clearProtected?.('authorization');

    expect(stale.cancelled).toBe(true);
    expect(facade.view()).toMatchObject({ status: 'loading', items: [] });
    catchUp?.();
    const replacement = http.expectOne(`/api/projects/${PROJECT_IDS[0]}`);
    replacement.flush(project(PROJECT_IDS[0], 'Reauthorized Research', 'Active'));
    expect(facade.view().items[0]?.title).toBe('Reauthorized Research');
  });

  it('cancels hydration and exposes no old projection across an active Workspace boundary while preserving the old bucket', () => {
    seed([history('project', PROJECT_IDS[0], 1)]);
    activate();
    const stale = http.expectOne(`/api/projects/${PROJECT_IDS[0]}`);

    activeWorkspace.setActiveWorkspace({ id: OTHER_WORKSPACE_ID, label: 'Other Workspace' });
    TestBed.flushEffects();

    expect(stale.cancelled).toBe(true);
    expect(facade.view().items).toEqual([]);
    expect(rawHistory()).toContain(PROJECT_IDS[0]);
  });

  it('rejects a mismatched grant File ID and never persists or projects its raw token', () => {
    seed([history('file', FILE_ID, 1)]);
    activate();
    http.expectOne(`/api/files/${FILE_ID}`).flush(file(FILE_ID, 'evidence.pdf', 'Active'));

    facade.downloadFile(FILE_ID);
    const grant = http.expectOne(`/api/files/${FILE_ID}/download-grants`);
    expect(grant.request.body).toEqual({ purpose: 'continue-working-download' });
    grant.flush({
      fileDownloadGrantId: GRANT_ID,
      fileObjectId: OTHER_FILE_ID,
      expiresAt: '2026-08-28T03:05:00Z',
      token: 'raw-secret-token',
    });

    http.expectNone((request) => request.url.includes('/file-download-grants/'));
    expect(facade.view().downloadMessage).toContain('could not be verified');
    expect(JSON.stringify(facade.view())).not.toContain('raw-secret-token');
    expect(rawHistory()).not.toContain('raw-secret-token');
  });

  it('downloads through a closure-only grant token and touches File recency only after the Blob succeeds', () => {
    seed([history('file', FILE_ID, 1)]);
    activate();
    http.expectOne(`/api/files/${FILE_ID}`).flush(file(FILE_ID, 'evidence.pdf', 'Active'));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:continue-working');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);

    facade.downloadFile(FILE_ID);
    http.expectOne(`/api/files/${FILE_ID}/download-grants`).flush({
      fileDownloadGrantId: GRANT_ID,
      fileObjectId: FILE_ID,
      expiresAt: '2026-08-28T03:05:00Z',
      token: 'raw-secret-token',
    });
    const download = http.expectOne(`/api/file-download-grants/${GRANT_ID}/download`);
    expect(download.request.body).toEqual({ token: 'raw-secret-token' });
    expect(rawHistory()).not.toContain('2026-08-28T03:00:00.000Z');
    download.flush(new Blob(['evidence'], { type: 'application/pdf' }), {
      headers: { 'content-disposition': 'attachment; filename="evidence.pdf"' },
    });

    expect(click).toHaveBeenCalled();
    expect(facade.view()).toMatchObject({ downloadingFileId: null, downloadMessage: 'Download started.' });
    expect(facade.view().items[0]?.lastOpenedUtc).toBe('2026-08-28T03:00:00.000Z');
    expect(rawHistory()).toContain('2026-08-28T03:00:00.000Z');
    expect(rawHistory()).not.toContain('raw-secret-token');
    expect(rawHistory()).not.toContain('evidence.pdf');
  });

  it('re-reads metadata after a grant 400 and does not misclassify current scan/policy denial as revocation', () => {
    seed([history('file', FILE_ID, 1)]);
    activate();
    http.expectOne(`/api/files/${FILE_ID}`).flush(file(FILE_ID, 'evidence.pdf', 'Active'));

    facade.downloadFile(FILE_ID);
    http.expectOne(`/api/files/${FILE_ID}/download-grants`).flush(
      { message: 'scan pending' },
      { status: 400, statusText: 'Bad Request' },
    );
    http.expectOne(`/api/files/${FILE_ID}`).flush(file(FILE_ID, 'evidence.pdf', 'Active'));

    expect(facade.view().items).toHaveLength(1);
    expect(facade.view().downloadMessage).toContain('server policy');
    expect(rawHistory()).toContain(FILE_ID);
  });

  function activate(): void {
    facade = TestBed.inject(ContinueWorkingFacade);
    facade.activate(WORKSPACE_ID);
    TestBed.flushEffects();
  }

  function seed(items: readonly unknown[]): void {
    storage.values.set(historyKey(), JSON.stringify({ version: 1, items }));
  }

  function rawHistory(): string {
    return storage.values.get(historyKey()) ?? '';
  }
});

function history(kind: 'project' | 'file', resourceId: string, minute: number) {
  return {
    kind,
    resourceId,
    lastOpenedUtc: `2026-08-28T02:${String(minute).padStart(2, '0')}:00.000Z`,
  };
}

function project(id: string, title: string, status: string) {
  return {
    id,
    workspaceId: WORKSPACE_ID,
    title,
    status,
    createdAt: '2026-08-20T00:00:00Z',
    updatedAt: '2026-08-27T00:00:00Z',
  };
}

function file(id: string, originalFileName: string, status: string) {
  return {
    id,
    workspaceId: WORKSPACE_ID,
    originalFileName,
    status,
    createdAt: '2026-08-21T00:00:00Z',
    updatedAt: '2026-08-27T01:00:00Z',
    deletedAt: null,
  };
}

function idFromRequest(request: TestRequest): string {
  return request.request.url.split('/').pop() ?? '';
}

function historyKey(): string {
  return `coglatas.continue-working.v1:${TENANT_ID}:${USER_ID}:${WORKSPACE_ID}`;
}

function session(): AuthSessionSnapshot {
  return {
    status: 'active', isAuthenticated: true, displayName: 'Continue user', supportingUsers: [],
    capabilities: ['projects:view', 'files:view'],
    currentUser: {
      userId: USER_ID, displayName: 'Continue user', email: 'continue@example.test', systemRole: 'TenantUser', status: 'Active',
      capabilities: ['projects:view', 'files:view'], currentWorkspace: { id: WORKSPACE_ID, label: 'Workspace' }, workspaces: [{ id: WORKSPACE_ID, label: 'Workspace' }],
    },
    currentTenant: { tenantId: TENANT_ID, isAvailable: true, isPlatformScope: false, allowTenantSwitching: false },
    navigation: { capabilities: ['projects:view', 'files:view'], isLoaded: true },
  };
}
