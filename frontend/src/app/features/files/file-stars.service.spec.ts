import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Subject } from 'rxjs';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ContinueWorkingHistoryService, ContinueWorkingScope } from '../../shared/continue-working/continue-working-history.service';
import { COGLATAS_FILE_STARS_STORAGE, LocalFileStarsRepository } from './file-stars.repository';
import { LocalFileStarsService } from './file-stars.service';
import { DEFAULT_FILES } from './files.mock';

const SCOPE: ContinueWorkingScope = { tenantId: 'tenant-a', userId: 'user-a', workspaceId: '11111111-1111-4111-8111-111111111111' };
const IDS = ['aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'];
const detail = (id = IDS[0]!) => ({ id, workspaceId: SCOPE.workspaceId, originalFileName: '研究📄'.repeat(80) + '.txt',
  contentType: 'text/plain', sizeBytes: 3, status: 'Active', createdAt: '2026-10-19T00:00:00Z' });

describe('LocalFileStarsService', () => {
  let service: LocalFileStarsService;
  let repository: LocalFileStarsRepository;
  let http: HttpTestingController;
  let scope: ReturnType<typeof signal<ContinueWorkingScope | null>>;
  let clear: () => void;
  let records: Map<string, string>;
  let writeFails: boolean;
  let events: Subject<{ eventType: string }>;
  beforeEach(() => {
    scope = signal<ContinueWorkingScope | null>(SCOPE);
    records = new Map(); writeFails = false;
    events = new Subject();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: ContinueWorkingHistoryService, useValue: { resolveCurrentScope: () => scope() } },
      { provide: ActiveWorkspaceFacade, useValue: { activeWorkspace: () => ({ id: scope()?.workspaceId }) } },
      { provide: RealtimeFacade, useValue: {
        durableEvents$: events.asObservable(),
        registerProtectedStateClearer: (_owner: string, callback: () => void) => { clear = callback; return () => undefined; },
      } },
      { provide: COGLATAS_FILE_STARS_STORAGE, useValue: {
        getItem: (key: string) => records.get(key) ?? null,
        setItem: (key: string, value: string) => { if (writeFails) { throw new Error('storage fixture'); } records.set(key, value); },
        removeItem: (key: string) => records.delete(key),
      } },
    ] });
    service = TestBed.inject(LocalFileStarsService);
    repository = TestBed.inject(LocalFileStarsRepository);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  function visit(ids: readonly string[] = [IDS[0]!]) {
    repository.write(SCOPE, ids);
    service.setVisible(true);
  }

  it('reauthorizes exact IDs and Workspace before displaying Unicode names without granting missing capabilities', () => {
    visit();
    expect(service.view().status).toBe('loading');
    const request = http.expectOne('/api/files/' + IDS[0]);
    expect(request.request.withCredentials).toBe(true);
    request.flush(detail());
    const file = service.view().files[0]!;
    expect(file.originalFileName).toBe(detail().originalFileName);
    expect(file.canonicalFileId).toBe(IDS[0]);
    expect(file.scanStatus).toBe('unavailable');
    expect(file.capabilities).toEqual([]);
    expect(file.canDelete).toBe(false);
    expect([...records.values()].join('')).not.toContain('研究');
    http.expectNone((request) => request.method !== 'GET');
  });

  it.each([400, 403, 404])('purges denied metadata and the inaccessible preference on HTTP %i', (status) => {
    visit(); http.expectOne('/api/files/' + IDS[0]).flush(detail());
    service.refresh();
    expect(service.view().files).toEqual([]);
    http.expectOne('/api/files/' + IDS[0]).flush({ message: 'private diagnostic' }, { status, statusText: 'Denied' });
    expect(service.view().status).toBe('empty');
    expect(service.view().files).toEqual([]);
    expect(repository.read(SCOPE).ids).toEqual([]);
  });

  it('hides old names on transient failure, retains only IDs, and recovers on retry', () => {
    visit(); http.expectOne('/api/files/' + IDS[0]).flush(detail());
    service.refresh();
    expect(service.view().files).toEqual([]);
    http.expectOne('/api/files/' + IDS[0]).error(new ProgressEvent('offline'));
    expect(service.view()).toEqual({ status: 'error', files: [], retryAvailable: true });
    expect(repository.read(SCOPE).ids).toEqual([IDS[0]]);
    service.refresh(); http.expectOne('/api/files/' + IDS[0]).flush(detail());
    expect(service.view().status).toBe('ready');
  });

  it.each([
    { ...detail(), id: IDS[1] }, { ...detail(), workspaceId: '22222222-2222-4222-8222-222222222222' },
    { ...detail(), status: 'Deleted' }, { ...detail(), deletedAt: '2026-10-19' },
    { ...detail(), originalFileName: '' }, { ...detail(), sizeBytes: -1 },
  ])('rejects mismatched, deleted or malformed detail responses', (response) => {
    visit(); http.expectOne('/api/files/' + IDS[0]).flush(response);
    expect(service.view().files).toEqual([]);
    expect(repository.read(SCOPE).ids).toEqual([]);
  });

  it('cancels previous reads on retry, leaving Starred, and authorization clearing', () => {
    visit(); const first = http.expectOne('/api/files/' + IDS[0]);
    service.refresh(); expect(first.cancelled).toBe(true);
    const second = http.expectOne('/api/files/' + IDS[0]);
    service.setVisible(false); expect(second.cancelled).toBe(true);
    expect(service.view().files).toEqual([]);
    service.setVisible(true); const third = http.expectOne('/api/files/' + IDS[0]);
    clear(); expect(third.cancelled).toBe(true);
    expect(service.view().files).toEqual([]);
  });

  it('synchronously hides a previous account projection before boundary effects run', () => {
    visit(); http.expectOne('/api/files/' + IDS[0]).flush(detail());
    scope.set({ ...SCOPE, userId: 'user-b' });
    expect(service.view().files).toEqual([]);
    TestBed.tick();
    expect(service.view().status).toBe('empty');
    expect(repository.read(SCOPE).ids).toEqual([IDS[0]]);
  });

  it('limits hydration to three concurrent existing detail requests', () => {
    visit(IDS);
    const first = http.match((request) => request.url.startsWith('/api/files/'));
    expect(first.length).toBe(3);
    first[0]!.flush(detail(IDS[0]));
    const fourth = http.expectOne('/api/files/' + IDS[3]);
    first[1]!.flush(detail(IDS[1])); first[2]!.flush(detail(IDS[2])); fourth.flush(detail(IDS[3]));
    expect(service.view().files.map((file) => file.canonicalFileId)).toEqual(IDS);
  });

  it('clears the full projection on a terminal session denial', () => {
    visit(IDS.slice(0, 3));
    const pending = http.expectOne('/api/files/' + IDS[2]);
    http.expectOne('/api/files/' + IDS[0]).flush(detail());
    http.expectOne('/api/files/' + IDS[1]).flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(pending.cancelled).toBe(true);
    expect(service.view()).toEqual({ status: 'error', files: [], retryAvailable: true });
  });

  it.each(['user', 'workspace'] as const)('cancels a held old-%s response before loading the new scope', (boundary) => {
    visit(); const pending = http.expectOne('/api/files/' + IDS[0]);
    scope.set(boundary === 'user' ? { ...SCOPE, userId: 'user-b' }
      : { ...SCOPE, workspaceId: '22222222-2222-4222-8222-222222222222' });
    expect(service.view().files).toEqual([]);
    TestBed.tick();
    expect(pending.cancelled).toBe(true);
    expect(service.view().status).toBe('empty');
    expect(repository.read(SCOPE).ids).toEqual([IDS[0]]);
    http.expectNone((request) => request.url.startsWith('/api/files/'));
  });

  it('does not report a saved star when browser persistence fails', () => {
    writeFails = true;
    service.toggle({ ...DEFAULT_FILES[0]!, canonicalFileId: IDS[0] });
    expect(service.notice()).toBe('storageUnavailable');
    expect(service.isStarred({ ...DEFAULT_FILES[0]!, canonicalFileId: IDS[0] })).toBe(false);
    expect(records.size).toBe(0);
  });

  it('discards visible metadata before refreshing a File invalidation', () => {
    visit(); http.expectOne('/api/files/' + IDS[0]).flush(detail());
    events.next({ eventType: 'Files.FileChanged.v1' });
    expect(service.view().files).toEqual([]);
    http.expectOne('/api/files/' + IDS[0]).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(service.view().status).toBe('empty');
  });
});
