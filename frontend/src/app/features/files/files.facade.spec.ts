import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { ContinueWorkingHistoryService } from '../../shared/continue-working/continue-working-history.service';
import { FilesFacade } from './files.facade';

const FILE_OBJECT_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const OTHER_FILE_OBJECT_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const WORKSPACE_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';

describe('FilesFacade paging query state', () => {
  let facade: FilesFacade;
  let http: HttpTestingController;
  let clearProtectedState: (() => void) | undefined;
  let activeWorkspaceState: ReturnType<typeof signal<{ readonly id: string; readonly label: string } | null>>;
  const continueWorkingHistory = { touchFile: vi.fn() };

  beforeEach(() => {
    window.localStorage.setItem('coglatas.locale', 'en');
    clearProtectedState = undefined;
    activeWorkspaceState = signal<{ readonly id: string; readonly label: string } | null>(null);
    continueWorkingHistory.touchFile.mockReset();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: ActiveWorkspaceFacade, useValue: { activeWorkspace: activeWorkspaceState } },
      { provide: ContinueWorkingHistoryService, useValue: continueWorkingHistory },
      {
        provide: RealtimeFacade,
        useValue: {
          durableEvents$: new Subject(),
          registerProtectedStateClearer: (_owner: string, clear: () => void) => {
            clearProtectedState = clear;
            return () => { clearProtectedState = undefined; };
          },
        },
      }
    ] });
    facade = TestBed.inject(FilesFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    http.verify();
    TestBed.resetTestingModule();
  });

  it('keeps existing picker candidates and retries the failed second page', () => {
    facade.loadPickerFilesForWorkspace('workspace-a');
    http.expectOne(request => request.url === '/api/files' && request.params.get('page') === '1').flush({ items: [file('file-1')], page: 1, pageSize: 20, totalCount: 2, hasMore: true });
    facade.loadMorePickerFiles();
    http.expectOne(request => request.url === '/api/files' && request.params.get('page') === '2').flush({ message: 'offline', traceId: 'picker-2' }, { status: 500, statusText: 'Server Error' });
    expect(facade.pickerStateForTask()).toMatchObject({ status: 'error', failedPage: 2, files: [expect.objectContaining({ id: 'file-1' })] });

    facade.retryPickerFiles();
    http.expectOne(request => request.url === '/api/files' && request.params.get('page') === '2').flush({ items: [file('file-1'), file('file-2')], page: 2, pageSize: 20, totalCount: 2, hasMore: false });
    expect(facade.pickerStateForTask().files.map(item => item.id)).toEqual(['file-1', 'file-2']);
  });

  it('keeps the active workspace page separate and reports picker authorization denial', () => {
    facade.loadPickerFilesForWorkspace('workspace-a');
    http.expectOne('/api/files?workspaceId=workspace-a&page=1&pageSize=20').flush({ message: 'denied', traceId: 'picker-403' }, { status: 403, statusText: 'Forbidden' });
    expect(facade.pickerStateForTask()).toMatchObject({ status: 'permissionDenied', workspaceId: 'workspace-a', requestId: 'picker-403' });
  });

  it('reaches the final server page when the workspace contains more than 1,000 files', () => {
    facade.loadPageFilesForWorkspace('workspace-a');
    const first = http.expectOne(request =>
      request.url === '/api/files' &&
      request.params.get('workspaceId') === 'workspace-a' &&
      request.params.get('page') === '1' &&
      request.params.get('pageSize') === '50'
    );
    first.flush({
      items: Array.from({ length: 50 }, (_, index) => file(`file-${index + 1}`)),
      page: 1,
      pageSize: 50,
      totalCount: 1_001,
      hasMore: true,
    });

    expect(facade.page()).toMatchObject({ page: 1, pageSize: 50, totalCount: 1_001, hasMore: true });
    expect(facade.page().recentFiles).toHaveLength(50);

    facade.goToPage(21);
    const last = http.expectOne(request =>
      request.url === '/api/files' &&
      request.params.get('workspaceId') === 'workspace-a' &&
      request.params.get('page') === '21' &&
      request.params.get('pageSize') === '50'
    );
    last.flush({ items: [file('file-1001')], page: 21, pageSize: 50, totalCount: 1_001, hasMore: false });

    expect(facade.page()).toMatchObject({ page: 21, pageSize: 50, totalCount: 1_001, hasMore: false });
    expect(facade.page().recentFiles.map(item => item.id)).toEqual(['file-1001']);
  });

  it('synchronously clears Workspace projections and cancels late page and picker responses', () => {
    facade.loadPageFilesForWorkspace('workspace-a');
    facade.loadPickerFilesForWorkspace('workspace-a');
    const pending = http.match((request) => request.url === '/api/files');
    expect(pending).toHaveLength(2);

    clearProtectedState?.();

    expect(pending.every((request) => request.cancelled)).toBe(true);
    expect(facade.page()).toMatchObject({
      recentFiles: [],
      pickerFiles: [],
      totalCount: 0,
      hasMore: false,
      upload: { state: 'idle', canUpload: false },
    });
    expect(facade.pickerStateForTask()).toMatchObject({
      status: 'idle',
      workspaceId: null,
      files: [],
      totalCount: 0,
    });
  });

  it('uses backend File search as the result and count owner for every applied facet', () => {
    activeWorkspaceState.set({ id: WORKSPACE_ID, label: 'Workspace A' });
    facade.loadPageFilesForWorkspace(WORKSPACE_ID);
    http.expectOne(request => request.url === '/api/files').flush({
      items: [file('inventory-file')], page: 1, pageSize: 50, totalCount: 1, hasMore: false,
    });

    facade.searchFilesForWorkspace(WORKSPACE_ID, {
      query: 'report', kind: 'pdf', modified: 'last30Days', owner: 'me',
    }, USER_ID);

    const request = http.expectOne(candidate => candidate.url === '/api/search');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.params.get('type')).toBe('File');
    expect(request.request.params.get('workspaceId')).toBe(WORKSPACE_ID);
    expect(request.request.params.get('q')).toBe('report');
    expect(request.request.params.get('fileKind')).toBe('Pdf');
    expect(request.request.params.get('fromDate')).toBeTruthy();
    expect(request.request.params.get('authorUserId')).toBe(USER_ID);
    request.flush(searchResponse(WORKSPACE_ID));

    expect(facade.search()).toMatchObject({
      status: 'ready', workspaceId: WORKSPACE_ID, totalCount: 73, page: 1, hasMore: true,
    });
    expect(facade.search().files.map(item => item.canonicalFileId)).toEqual([FILE_OBJECT_ID]);
    expect(facade.page().recentFiles.map(item => item.id)).toEqual(['inventory-file']);
  });

  it('fails closed on a mismatched search record instead of exposing its row or count', () => {
    activeWorkspaceState.set({ id: WORKSPACE_ID, label: 'Workspace A' });
    facade.loadPageFilesForWorkspace(WORKSPACE_ID);
    http.expectOne(request => request.url === '/api/files').flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });

    facade.searchFilesForWorkspace(WORKSPACE_ID, {
      query: 'report', kind: 'all', modified: 'any', owner: 'any',
    }, USER_ID);
    http.expectOne(request => request.url === '/api/search').flush(searchResponse('22222222-2222-4222-8222-222222222222'));

    expect(facade.search()).toMatchObject({ status: 'error', files: [], totalCount: 0 });
    expect(facade.search().message).toContain('mismatched');
  });

  it('cancels File search and synchronously removes query, rows, and counts on authorization invalidation', () => {
    activeWorkspaceState.set({ id: WORKSPACE_ID, label: 'Workspace A' });
    facade.loadPageFilesForWorkspace(WORKSPACE_ID);
    http.expectOne(request => request.url === '/api/files').flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    facade.searchFilesForWorkspace(WORKSPACE_ID, {
      query: 'sensitive report', kind: 'pdf', modified: 'any', owner: 'any',
    }, USER_ID);
    const pending = http.expectOne(request => request.url === '/api/search');

    clearProtectedState?.();

    expect(pending.cancelled).toBe(true);
    expect(facade.search()).toMatchObject({
      status: 'idle', workspaceId: null, files: [], totalCount: 0,
      filters: { query: '', kind: 'all', modified: 'any', owner: 'any' },
    });
  });

  it('deletes selected files serially and reports a non-atomic partial outcome', () => {
    facade.loadPageFilesForWorkspace('workspace-a');
    http.expectOne(request => request.url === '/api/files' && request.method === 'GET').flush({
      items: [file('file-1', true), file('file-2', true)],
      page: 1,
      pageSize: 50,
      totalCount: 2,
      hasMore: false,
    });

    facade.deleteFiles(facade.page().recentFiles);

    const first = http.expectOne('/api/files/object-file-1');
    expect(first.request.method).toBe('DELETE');
    expect(first.request.withCredentials).toBe(true);
    http.expectNone('/api/files/object-file-2');
    first.flush(null);

    const second = http.expectOne('/api/files/object-file-2');
    expect(second.request.method).toBe('DELETE');
    second.flush({ message: 'denied' }, { status: 403, statusText: 'Forbidden' });

    expect(facade.deleteState()).toMatchObject({ state: 'partial', succeededCount: 1, failedCount: 1 });
    expect(facade.deleteState().message).toContain('not an atomic batch');
    expect(facade.page().recentFiles.map((item) => item.id)).toEqual(['file-2']);

    http.expectOne(request => request.url === '/api/files' && request.method === 'GET').flush({
      items: [file('file-2', true)],
      page: 1,
      pageSize: 50,
      totalCount: 1,
      hasMore: false,
    });
  });

  it('does not issue a delete when the projected capability is false', () => {
    facade.loadPageFilesForWorkspace('workspace-a');
    http.expectOne(request => request.url === '/api/files' && request.method === 'GET').flush({
      items: [file('file-1', false)],
      page: 1,
      pageSize: 50,
      totalCount: 1,
      hasMore: false,
    });

    facade.deleteFiles(facade.page().recentFiles);

    expect(facade.deleteState()).toMatchObject({ state: 'failed', succeededCount: 0, failedCount: 1 });
    http.expectNone(request => request.method === 'DELETE');
  });

  it.each([
    ['missing', undefined],
    ['mismatched', OTHER_FILE_OBJECT_ID],
  ])('rejects a Files-page grant with a %s FileObject identity before Blob download', (_, grantedFileObjectId) => {
    facade.loadPageFilesForWorkspace('workspace-a');
    http.expectOne(request => request.url === '/api/files' && request.method === 'GET').flush({
      items: [file('file-1', false, FILE_OBJECT_ID)],
      page: 1,
      pageSize: 50,
      totalCount: 1,
      hasMore: false,
    });

    facade.downloadFile(FILE_OBJECT_ID);
    http.expectOne(`/api/files/${FILE_OBJECT_ID}/download-grants`).flush({
      fileDownloadGrantId: 'grant-a',
      ...(grantedFileObjectId === undefined ? {} : { fileObjectId: grantedFileObjectId }),
      token: 'raw-token',
    });

    http.expectNone(request => request.url.includes('/api/file-download-grants/'));
    expect(continueWorkingHistory.touchFile).not.toHaveBeenCalled();
    expect(facade.page().recentFiles[0]).toMatchObject({
      downloadState: 'failed',
      downloadMessage: 'Download grant response was incomplete or mismatched.',
    });
  });

  it.each([
    ['missing', undefined],
    ['blank', '   '],
  ])('fails closed before requesting a Task attachment grant when its authorized FileObject identity is %s', (_, expectedFileObjectId) => {
    activeWorkspaceState.set({ id: 'workspace-a', label: 'Workspace A' });
    const onState = vi.fn();

    const operation = facade.downloadAttachment('attachment-a', 'evidence.pdf', {
      workspaceId: 'workspace-a',
      fileObjectId: expectedFileObjectId,
      onState,
    });

    expect(operation).toBeNull();
    http.expectNone('/api/attachments/attachment-a/download-grants');
    http.expectNone(request => request.url.includes('/api/attachment-download-grants/'));
    expect(continueWorkingHistory.touchFile).not.toHaveBeenCalled();
    expect(onState).toHaveBeenCalledWith(
      'failed',
      'Download is unavailable because its authorized file identity is missing.',
    );
  });

  it.each([
    ['missing', undefined],
    ['mismatched', OTHER_FILE_OBJECT_ID],
  ])('rejects a Task attachment grant whose FileObject identity is %s against the authorized Task projection', (_, grantedFileObjectId) => {
    activeWorkspaceState.set({ id: 'workspace-a', label: 'Workspace A' });
    facade.downloadAttachment('attachment-a', 'evidence.pdf', {
      workspaceId: 'workspace-a',
      fileObjectId: FILE_OBJECT_ID,
    });

    http.expectOne('/api/attachments/attachment-a/download-grants').flush({
      fileDownloadGrantId: 'grant-a',
      ...(grantedFileObjectId === undefined ? {} : { fileObjectId: grantedFileObjectId }),
      token: 'raw-token',
    });

    http.expectNone(request => request.url.includes('/api/attachment-download-grants/'));
    expect(continueWorkingHistory.touchFile).not.toHaveBeenCalled();
  });

  it('does not touch another Workspace bucket when the active Workspace changes before an attachment Blob completes', () => {
    activeWorkspaceState.set({ id: 'workspace-a', label: 'Workspace A' });
    const onState = vi.fn();
    const onPermissionDenied = vi.fn();
    facade.downloadAttachment('attachment-a', 'evidence.pdf', {
      workspaceId: 'workspace-a',
      fileObjectId: FILE_OBJECT_ID,
      isCurrent: () => true,
      onState,
      onPermissionDenied,
    });
    http.expectOne('/api/attachments/attachment-a/download-grants').flush({
      fileDownloadGrantId: 'grant-a',
      fileObjectId: FILE_OBJECT_ID,
      token: 'raw-token',
    });
    const download = http.expectOne('/api/attachment-download-grants/grant-a/download');
    onState.mockClear();

    activeWorkspaceState.set({ id: 'workspace-b', label: 'Workspace B' });
    download.flush(
      new Blob([JSON.stringify({ message: 'denied' })], { type: 'application/json' }),
      { status: 403, statusText: 'Forbidden' },
    );

    expect(continueWorkingHistory.touchFile).not.toHaveBeenCalled();
    expect(onState).not.toHaveBeenCalled();
    expect(onPermissionDenied).not.toHaveBeenCalled();
  });

  it('ignores a late attachment grant denial after the active Workspace changes', () => {
    activeWorkspaceState.set({ id: 'workspace-a', label: 'Workspace A' });
    const onState = vi.fn();
    const onPermissionDenied = vi.fn();
    facade.downloadAttachment('attachment-a', 'evidence.pdf', {
      workspaceId: 'workspace-a',
      fileObjectId: FILE_OBJECT_ID,
      isCurrent: () => true,
      onState,
      onPermissionDenied,
    });
    const grant = http.expectOne('/api/attachments/attachment-a/download-grants');
    onState.mockClear();

    activeWorkspaceState.set({ id: 'workspace-b', label: 'Workspace B' });
    grant.flush({ message: 'denied' }, { status: 403, statusText: 'Forbidden' });

    expect(onState).not.toHaveBeenCalled();
    expect(onPermissionDenied).not.toHaveBeenCalled();
    expect(continueWorkingHistory.touchFile).not.toHaveBeenCalled();
  });

  it('touches the exact captured Task Workspace only after an attachment Blob download succeeds', () => {
    activeWorkspaceState.set({ id: 'workspace-a', label: 'Workspace A' });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:attachment');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    facade.downloadAttachment('attachment-a', 'evidence.pdf', {
      workspaceId: 'workspace-a',
      fileObjectId: FILE_OBJECT_ID.toUpperCase(),
      isCurrent: () => true,
    });
    http.expectOne('/api/attachments/attachment-a/download-grants').flush({
      fileDownloadGrantId: 'grant-a',
      fileObjectId: FILE_OBJECT_ID,
      token: 'raw-token',
    });

    expect(continueWorkingHistory.touchFile).not.toHaveBeenCalled();
    http.expectOne('/api/attachment-download-grants/grant-a/download').flush(
      new Blob(['evidence'], { type: 'application/pdf' }),
      { headers: { 'content-disposition': 'attachment; filename="evidence.pdf"' } },
    );

    expect(click).toHaveBeenCalled();
    expect(continueWorkingHistory.touchFile).toHaveBeenCalledWith(FILE_OBJECT_ID, 'workspace-a');
  });

  function file(id: string, canDelete = false, fileObjectId = `object-${id}`) {
    return {
      id,
      fileObjectId,
      originalFileName: `${id}.txt`,
      contentType: 'text/plain',
      sizeBytes: 1,
      status: 'Active',
      scanStatus: 'Allowed',
      uploadedByDisplayName: 'Tester',
      createdAt: '2026-07-24T00:00:00Z',
      updatedAt: '2026-07-25T00:00:00Z',
      canDelete,
    };
  }

  function searchResponse(workspaceId: string) {
    return {
      page: 1,
      pageSize: 50,
      totalCount: 73,
      items: [{
        type: 13,
        id: FILE_OBJECT_ID,
        title: 'report.pdf',
        workspaceId,
        createdAt: '2026-08-20T00:00:00Z',
        updatedAt: '2026-08-28T00:00:00Z',
        authorDisplayName: 'Current User',
        contentType: 'application/pdf',
        sizeBytes: 2048,
        status: 'Active',
        scanStatus: 'Allowed',
      }],
    };
  }
});
