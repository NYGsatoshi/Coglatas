import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ProtectedStateClearer, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { FileFolderStore } from './file-folders.service';

const WORKSPACE = '11111111-1111-4111-8111-111111111111';
const OTHER_WORKSPACE = '22222222-2222-4222-8222-222222222222';
const FOLDER = '33333333-3333-4333-8333-333333333333';
const FILE = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('FileFolderStore context boundaries', () => {
  let store: FileFolderStore;
  let http: HttpTestingController;
  let clearProtected: ProtectedStateClearer;
  const unregister = vi.fn();

  const navigation = (workspaceId = WORKSPACE) => ({
    workspaceId,
    rootVersion: 5,
    folders: [{ id: FOLDER, workspaceId, parentFolderId: null, name: 'Protected folder', sortOrder: 0, version: 2 }],
  });
  const folderRequest = (workspaceId = WORKSPACE) => http.expectOne((request) =>
    request.url === '/api/file-folders' && request.params.get('workspaceId') === workspaceId);
  const loadFolders = () => {
    store.load(WORKSPACE);
    folderRequest().flush(navigation());
  };
  const startFileMove = () => {
    const onSuccess = vi.fn();
    store.moveFile(FILE, FOLDER).subscribe({ next: onSuccess, error: vi.fn() });
    http.expectOne(`/api/files/${FILE}/location`).flush({ fileObjectId: FILE, workspaceId: WORKSPACE, version: 3 });
    return { onSuccess, request: http.expectOne(`/api/files/${FILE}/move`) };
  };

  beforeEach(() => {
    unregister.mockClear();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(), FileFolderStore,
      { provide: RealtimeFacade, useValue: {
        registerProtectedStateClearer: (_owner: string, clear: ProtectedStateClearer) => {
          clearProtected = clear;
          return unregister;
        },
      } },
    ] });
    store = TestBed.inject(FileFolderStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('clears old folder names and root version before the next Workspace responds or fails', () => {
    loadFolders();
    store.load(OTHER_WORKSPACE);
    expect(store.folders()).toEqual([]);
    expect(store.rootVersion()).toBe(0);
    expect(store.loading()).toBe(true);
    folderRequest(OTHER_WORKSPACE).flush({}, { status: 503, statusText: 'Unavailable' });
    expect(store.failed()).toBe(true);
    expect(store.tree()).toEqual([]);
    store.load(OTHER_WORKSPACE, true);
    folderRequest(OTHER_WORKSPACE).flush({ workspaceId: OTHER_WORKSPACE, rootVersion: 7, folders: [] });
    expect(store.failed()).toBe(false);
    expect(store.rootVersion()).toBe(7);
  });

  it('cancels a superseded folder read without settling the replacement loading state', () => {
    store.load(WORKSPACE);
    const previous = folderRequest();
    store.load(OTHER_WORKSPACE);
    expect(previous.cancelled).toBe(true);
    expect(store.loading()).toBe(true);
    folderRequest(OTHER_WORKSPACE).flush({ workspaceId: OTHER_WORKSPACE, rootVersion: 8, folders: [] });
    expect(store.loading()).toBe(false);
    expect(store.rootVersion()).toBe(8);
  });

  it.each(['session', 'tenant', 'workspace', 'authorization'] as const)('synchronously clears metadata and pending reads on %s invalidation', (reason) => {
    loadFolders();
    store.load(WORKSPACE, true);
    const pending = folderRequest();
    clearProtected(reason);
    expect(pending.cancelled).toBe(true);
    expect(store.folders()).toEqual([]);
    expect(store.rootVersion()).toBe(0);
    expect(store.loading()).toBe(false);
    expect(store.failed()).toBe(false);
    http.expectNone('/api/file-folders');
  });

  it('cancels a file location read before it can dispatch a move into an old Workspace', () => {
    loadFolders();
    const onSuccess = vi.fn();
    store.moveFile(FILE, FOLDER).subscribe(onSuccess);
    const location = http.expectOne(`/api/files/${FILE}/location`);
    store.load(OTHER_WORKSPACE);
    expect(location.cancelled).toBe(true);
    expect(onSuccess).not.toHaveBeenCalled();
    http.expectNone(`/api/files/${FILE}/move`);
    http.expectNone((request) => request.url === '/api/file-folders' && request.params.get('workspaceId') === WORKSPACE);
    folderRequest(OTHER_WORKSPACE).flush({ workspaceId: OTHER_WORKSPACE, rootVersion: 9, folders: [] });
    expect(store.rootVersion()).toBe(9);
  });

  it.each([
    { kind: 'file', outcome: 'success' }, { kind: 'file', outcome: 'error' },
    { kind: 'folder', outcome: 'success' }, { kind: 'folder', outcome: 'error' },
  ])('ignores a late $outcome from a $kind move after Workspace replacement', ({ kind, outcome }) => {
    loadFolders();
    const onSuccess = vi.fn();
    let pending;
    if (kind === 'file') {
      const operation = startFileMove();
      pending = operation.request;
      operation.onSuccess.mockImplementation(onSuccess);
    } else {
      store.moveFolder(FOLDER, null).subscribe({ next: onSuccess, error: vi.fn() });
      pending = http.expectOne(`/api/file-folders/${FOLDER}/move`);
    }
    store.load(OTHER_WORKSPACE);
    folderRequest(OTHER_WORKSPACE).flush({ workspaceId: OTHER_WORKSPACE, rootVersion: 11, folders: [] });
    // An already-cancelled HTTP response cannot be delivered by the test backend.
    // Otherwise exercise both late response paths before checking the active scope.
    if (!pending.cancelled) {
      if (outcome === 'success') {
        pending.flush({});
      } else {
        pending.flush({}, { status: 409, statusText: 'Conflict' });
      }
    }
    http.expectNone((request) => request.url === '/api/file-folders' && request.params.get('workspaceId') === WORKSPACE);
    expect(pending.cancelled).toBe(true);
    expect(onSuccess).not.toHaveBeenCalled();
    expect(store.rootVersion()).toBe(11);
    expect(store.failed()).toBe(false);
    expect(store.loading()).toBe(false);
  });

  it('does not restore folders when a dispatched move is cancelled by authorization invalidation', () => {
    loadFolders();
    const { request, onSuccess } = startFileMove();
    clearProtected('authorization');
    expect(request.cancelled).toBe(true);
    expect(onSuccess).not.toHaveBeenCalled();
    http.expectNone('/api/file-folders');
    expect(store.tree()).toEqual([]);
  });

  it('rejects an operation subscribed after its original context was invalidated, even after the same Workspace reloads', () => {
    loadFolders();
    const operation = store.moveFile(FILE, FOLDER);
    clearProtected('authorization');
    loadFolders();
    const onError = vi.fn();
    operation.subscribe({ error: onError });
    expect(onError).toHaveBeenCalledOnce();
    http.expectNone(`/api/files/${FILE}/location`);
    http.expectNone('/api/file-folders');
  });

  it('refreshes only the current Workspace after a current move conflicts and never reports success', () => {
    loadFolders();
    const onSuccess = vi.fn();
    const onError = vi.fn();
    store.moveFolder(FOLDER, null).subscribe({ next: onSuccess, error: onError });
    http.expectOne(`/api/file-folders/${FOLDER}/move`).flush({}, { status: 409, statusText: 'Conflict' });
    expect(onError).toHaveBeenCalledOnce();
    expect(onSuccess).not.toHaveBeenCalled();
    folderRequest().flush({ workspaceId: WORKSPACE, rootVersion: 12, folders: [] });
    expect(store.rootVersion()).toBe(12);
  });

  it('rejects moves while folder versions are loading or unavailable', () => {
    store.load(WORKSPACE);
    const onError = vi.fn();
    store.moveFile(FILE, null).subscribe({ error: onError });
    folderRequest().flush({}, { status: 500, statusText: 'Unavailable' });
    store.moveFile(FILE, null).subscribe({ error: onError });
    expect(onError).toHaveBeenCalledTimes(2);
    http.expectNone(`/api/files/${FILE}/location`);
  });
});
