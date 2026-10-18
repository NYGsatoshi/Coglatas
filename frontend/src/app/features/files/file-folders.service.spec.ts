import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';

import { FileFolderStore } from './file-folders.service';

const WORKSPACE_ID = '11111111-1111-4111-8111-111111111111';
const ROOT_FOLDER_ID = '22222222-2222-4222-8222-222222222222';
const CHILD_FOLDER_ID = '33333333-3333-4333-8333-333333333333';
const FILE_OBJECT_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('FileFolderStore', () => {
  let store: FileFolderStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        FileFolderStore,
        { provide: RealtimeFacade, useValue: { registerProtectedStateClearer: vi.fn() } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(FileFolderStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('loads a Workspace-scoped hierarchy and root version', () => {
    store.load(WORKSPACE_ID);

    const request = http.expectOne((candidate) =>
      candidate.url === '/api/file-folders' && candidate.method === 'GET');
    expect(request.request.params.get('workspaceId')).toBe(WORKSPACE_ID);
    expect(request.request.withCredentials).toBe(true);
    request.flush({
      workspaceId: WORKSPACE_ID,
      rootVersion: 5,
      folders: [
        {
          id: CHILD_FOLDER_ID,
          workspaceId: WORKSPACE_ID,
          parentFolderId: ROOT_FOLDER_ID,
          name: 'Child',
          sortOrder: 0,
          version: 2,
        },
        {
          id: ROOT_FOLDER_ID,
          workspaceId: WORKSPACE_ID,
          parentFolderId: null,
          name: 'Root',
          sortOrder: 0,
          version: 1,
        },
      ],
    });

    expect(store.failed()).toBe(false);
    expect(store.loading()).toBe(false);
    expect(store.rootVersion()).toBe(5);
    expect(store.tree()).toEqual([
      {
        id: ROOT_FOLDER_ID,
        name: 'Root',
        children: [{ id: CHILD_FOLDER_ID, name: 'Child', children: [] }],
      },
    ]);
  });

  it('moves a canonical file with both source and destination versions', () => {
    store.load(WORKSPACE_ID);
    http.expectOne((candidate) => candidate.url === '/api/file-folders').flush({
      workspaceId: WORKSPACE_ID,
      rootVersion: 9,
      folders: [
        {
          id: ROOT_FOLDER_ID,
          workspaceId: WORKSPACE_ID,
          parentFolderId: null,
          name: 'Root',
          sortOrder: 0,
          version: 4,
        },
      ],
    });

    let completed = false;
    store.moveFile(FILE_OBJECT_ID, ROOT_FOLDER_ID).subscribe(() => {
      completed = true;
    });

    const location = http.expectOne(`/api/files/${FILE_OBJECT_ID}/location`);
    expect(location.request.method).toBe('GET');
    expect(location.request.withCredentials).toBe(true);
    location.flush({
      fileObjectId: FILE_OBJECT_ID,
      workspaceId: WORKSPACE_ID,
      folderId: null,
      version: 3,
    });

    const move = http.expectOne(`/api/files/${FILE_OBJECT_ID}/move`);
    expect(move.request.method).toBe('POST');
    expect(move.request.withCredentials).toBe(true);
    expect(move.request.body).toEqual({
      destinationFolderId: ROOT_FOLDER_ID,
      expectedVersion: 3,
      expectedDestinationVersion: 4,
    });
    move.flush({
      fileObjectId: FILE_OBJECT_ID,
      workspaceId: WORKSPACE_ID,
      folderId: ROOT_FOLDER_ID,
      version: 4,
    });

    const refresh = http.expectOne((candidate) =>
      candidate.url === '/api/file-folders' && candidate.method === 'GET');
    refresh.flush({ workspaceId: WORKSPACE_ID, rootVersion: 10, folders: [] });
    expect(completed).toBe(true);
  });

  it('uses the root version when moving a folder to Workspace root', () => {
    store.load(WORKSPACE_ID);
    http.expectOne((candidate) => candidate.url === '/api/file-folders').flush({
      workspaceId: WORKSPACE_ID,
      rootVersion: 11,
      folders: [
        {
          id: ROOT_FOLDER_ID,
          workspaceId: WORKSPACE_ID,
          parentFolderId: CHILD_FOLDER_ID,
          name: 'Source',
          sortOrder: 0,
          version: 7,
        },
        {
          id: CHILD_FOLDER_ID,
          workspaceId: WORKSPACE_ID,
          parentFolderId: null,
          name: 'Current parent',
          sortOrder: 1,
          version: 2,
        },
      ],
    });

    let completed = false;
    store.moveFolder(ROOT_FOLDER_ID, null).subscribe(() => {
      completed = true;
    });

    const move = http.expectOne(`/api/file-folders/${ROOT_FOLDER_ID}/move`);
    expect(move.request.method).toBe('POST');
    expect(move.request.withCredentials).toBe(true);
    expect(move.request.body).toEqual({
      destinationParentFolderId: null,
      expectedVersion: 7,
      expectedDestinationVersion: 11,
    });
    move.flush({
      id: ROOT_FOLDER_ID,
      workspaceId: WORKSPACE_ID,
      parentFolderId: null,
      name: 'Source',
      sortOrder: 0,
      version: 8,
    });

    http.expectOne((candidate) => candidate.url === '/api/file-folders').flush({
      workspaceId: WORKSPACE_ID,
      rootVersion: 12,
      folders: [],
    });
    expect(completed).toBe(true);
  });
});
