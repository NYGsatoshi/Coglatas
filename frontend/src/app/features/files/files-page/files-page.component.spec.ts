import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { COGLATAS_ACTIVE_WORKSPACE_MOCK } from '../../../core/workspace/active-workspace.facade';
import { AttachmentPickerDialogComponent } from '../attachment-picker-dialog/attachment-picker-dialog.component';
import { FileRowComponent } from '../file-row/file-row.component';
import { COGLATAS_FILES_PAGE_MOCK } from '../files.facade';
import { DEFAULT_FILES, FILES_PAGE_SCENARIOS } from '../files.mock';
import { FilesPageViewModel } from '../files.types';
import { CoglatasFileUploaderComponent } from '../../../shared/ui/adapters/syncfusion/coglatas-file-uploader.component';
import { FilesPageComponent } from './files-page.component';

const WORKSPACE_ID = '11111111-1111-4111-8111-111111111111';
const FILE_OBJECT_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

const backendFile = {
  id: 'attachment-1',
  fileObjectId: FILE_OBJECT_ID,
  workspaceId: WORKSPACE_ID,
  originalFileName: 'note.txt',
  contentType: 'text/plain',
  sizeBytes: 12,
  status: 'Active',
  scanStatus: 'Skipped',
  uploadedByUserId: 'user-1',
  uploadedByDisplayName: 'Fixture User',
  createdAt: '2026-07-08T00:00:00Z',
  updatedAt: '2026-07-09T00:00:00Z',
  canDelete: true,
};

const renderMockFilesPage = async (page: FilesPageViewModel): Promise<ComponentFixture<FilesPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [FilesPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: COGLATAS_FILES_PAGE_MOCK, useValue: page },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(FilesPageComponent);
  fixture.detectChanges();
  return fixture;
};

const renderLiveFilesPage = async (
  items: readonly unknown[] = [],
): Promise<{ fixture: ComponentFixture<FilesPageComponent>; http: HttpTestingController }> => {
  await TestBed.configureTestingModule({
    imports: [FilesPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      // Files HTTP fallback requires a valid session. The default mock is
      // anonymous, which correctly represents a session boundary rather than
      // a disabled realtime transport.
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: COGLATAS_ACTIVE_WORKSPACE_MOCK, useValue: { id: WORKSPACE_ID, label: 'Workspace' } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(FilesPageComponent);
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  flushFolderList(http);
  flushFileList(http, items);
  fixture.detectChanges();
  return { fixture, http };
};

const flushFolderList = (http: HttpTestingController): void => {
  const request = http.expectOne((candidate) => candidate.url === '/api/file-folders' && candidate.method === 'GET');
  expect(request.request.params.get('workspaceId')).toBe(WORKSPACE_ID);
  expect(request.request.withCredentials).toBe(true);
  request.flush([]);
};

const flushFileList = (http: HttpTestingController, items: readonly unknown[]): void => {
  const request = http.expectOne((candidate) => candidate.url === '/api/files' && candidate.method === 'GET');
  expect(request.request.params.get('workspaceId')).toBe(WORKSPACE_ID);
  expect(request.request.params.get('page')).toBe('1');
  expect(request.request.params.get('pageSize')).toBe('50');
  expect(request.request.withCredentials).toBe(true);
  request.flush({ items, page: 1, pageSize: 50, totalCount: items.length, hasMore: false });
};

const textContent = (fixture: ComponentFixture<FilesPageComponent>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

const downloadButton = (fixture: ComponentFixture<FilesPageComponent>): HTMLButtonElement =>
  (fixture.nativeElement as HTMLElement).querySelector('[data-testid="download-action"]') as HTMLButtonElement;

const openAuthorizedPdfPreview = (
  fixture: ComponentFixture<FilesPageComponent>,
  http: HttpTestingController,
): void => {
  const { componentInstance: component } = fixture;
  const file = component.page().recentFiles[0];
  if (!file) {
    throw new Error('Expected a PDF fixture.');
  }

  component.openPreview(file);
  const grant = http.expectOne(`/api/files/${FILE_OBJECT_ID}/download-grants`);
  expect(grant.request.body).toEqual({ purpose: 'files-page-preview' });
  grant.flush({ fileDownloadGrantId: 'preview-grant', fileObjectId: FILE_OBJECT_ID, token: 'preview-token' });

  http.expectOne('/api/file-download-grants/preview-grant/download')
    .flush(new Blob(['pdf'], { type: 'application/pdf' }));
};

const pdfPreviewLink = (fixture: ComponentFixture<FilesPageComponent>): HTMLAnchorElement => {
  const link = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="files-preview-pdf"]');
  if (!(link instanceof HTMLAnchorElement)) {
    throw new Error('Expected an authorized PDF preview link.');
  }
  return link;
};

describe('FilesPageComponent', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('uploads a valid file only through the backend and reloads recent files after success', async () => {
    const { fixture, http } = await renderLiveFilesPage([]);
    const dropZone = fixture.debugElement.query(By.directive(CoglatasFileUploaderComponent))
      .componentInstance as CoglatasFileUploaderComponent;

    dropZone.filesSelected.emit([new File(['hello'], 'note.txt', { type: 'text/plain' })]);
    fixture.detectChanges();

    const upload = http.expectOne((request) => request.url === '/api/files' && request.method === 'POST');
    expect(upload.request.withCredentials).toBe(true);
    const body = upload.request.body as FormData;
    expect(body.get('OwnerType')).toBe('Workspace');
    expect(body.get('OwnerId')).toBe(WORKSPACE_ID);
    expect((body.get('File') as File).name).toBe('note.txt');
    expect(textContent(fixture)).toContain('Uploading file to backend.');

    upload.flush({ id: 'attachment-1', fileObjectId: 'file-object-1', originalFileName: 'note.txt' });
    flushFileList(http, [backendFile]);
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Upload accepted by backend.');
    expect(textContent(fixture)).toContain('note.txt');
  }, 15_000);

  it('renders Files actions, filters, and destructive confirmation in Japanese without raw keys', async () => {
    window.localStorage.setItem('coglatas.locale', 'ja');
    const { fixture, http } = await renderLiveFilesPage([backendFile]);
    const host = fixture.nativeElement as HTMLElement;

    expect(document.documentElement.lang).toBe('ja');
    expect(textContent(fixture)).toContain('ファイルを検索・絞り込み');
    expect(textContent(fixture)).toContain('ファイルをアップロード');
    expect((host.querySelector('[data-testid="files-search-input"]') as HTMLInputElement).placeholder).toBe('ファイル名を検索');

    const type = host.querySelector('[data-testid="files-filter-type"]') as HTMLSelectElement;
    type.value = 'pdf';
    type.dispatchEvent(new Event('change'));
    (host.querySelector('[data-testid="files-search-surface"] form') as HTMLFormElement)
      .dispatchEvent(new Event('submit'));
    const search = http.expectOne((request) => request.url === '/api/search');
    search.flush({
      page: 1,
      pageSize: 50,
      totalCount: 1,
      items: [{
        type: 13,
        id: FILE_OBJECT_ID,
        title: 'report.pdf',
        workspaceId: WORKSPACE_ID,
        createdAt: '2026-08-20T00:00:00Z',
        authorDisplayName: 'Fixture User',
        contentType: 'application/pdf',
        sizeBytes: 2048,
        status: 'Active',
        scanStatus: 'Allowed',
      }],
    });
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('種類: PDF');
    expect(textContent(fixture)).toContain('現在閲覧できるファイルが1件見つかりました。');
    expect(host.querySelector('button[aria-label="フィルターを削除: 種類: PDF"]')).not.toBeNull();
    expect(textContent(fixture)).not.toContain('files.search.');

    const file = fixture.componentInstance.page().recentFiles[0];
    if (!file) {
      throw new Error('Expected a listed file.');
    }
    fixture.componentInstance.handleSelectionChanged({ rows: [file] });
    fixture.detectChanges();
    (host.querySelector('[data-testid="files-selected-delete"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('note.txtを削除しますか？');
    expect(textContent(fixture)).toContain('キャンセル');
    expect(textContent(fixture)).toContain('ファイルを削除');
  }, 15_000);

  it('keeps File search and Type, Modified, and Owner filters in one scoped surface with removable chips', async () => {
    const { fixture, http } = await renderLiveFilesPage([backendFile]);
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector('[data-testid="files-search-input"]') as HTMLInputElement;
    const type = host.querySelector('[data-testid="files-filter-type"]') as HTMLSelectElement;
    const modified = host.querySelector('[data-testid="files-filter-modified"]') as HTMLSelectElement;
    const owner = host.querySelector('[data-testid="files-filter-owner"]') as HTMLSelectElement;

    input.value = 'report';
    input.dispatchEvent(new Event('input'));
    type.value = 'pdf';
    type.dispatchEvent(new Event('change'));
    modified.value = 'last30Days';
    modified.dispatchEvent(new Event('change'));
    owner.value = 'me';
    owner.dispatchEvent(new Event('change'));
    (host.querySelector('[data-testid="files-search-surface"] form') as HTMLFormElement)
      .dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const search = http.expectOne(request => request.url === '/api/search');
    expect(search.request.params.get('workspaceId')).toBe(WORKSPACE_ID);
    expect(search.request.params.get('type')).toBe('File');
    expect(search.request.params.get('q')).toBe('report');
    expect(search.request.params.get('fileKind')).toBe('Pdf');
    expect(search.request.params.get('fromDate')).toBeTruthy();
    expect(search.request.params.get('authorUserId')).toBe(DEFAULT_AUTH_SESSION.currentUser?.userId);
    search.flush({
      page: 1,
      pageSize: 50,
      totalCount: 1,
      items: [{
        type: 13,
        id: FILE_OBJECT_ID,
        title: 'report.pdf',
        workspaceId: WORKSPACE_ID,
        createdAt: '2026-08-20T00:00:00Z',
        updatedAt: '2026-08-28T00:00:00Z',
        authorDisplayName: 'Fixture User',
        contentType: 'application/pdf',
        sizeBytes: 2048,
        status: 'Active',
        scanStatus: 'Allowed',
      }],
    });
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Results and counts include only files you are currently authorized to view.');
    expect(textContent(fixture)).toContain('Type: PDF');
    expect(textContent(fixture)).toContain('Modified: Last 30 days');
    expect(textContent(fixture)).toContain('Owner: Uploaded by me');
    expect(textContent(fixture)).toContain('1 currently authorized file matches.');
    expect(textContent(fixture)).toContain('report.pdf');

    input.value = 'unsubmitted draft';
    input.dispatchEvent(new Event('input'));
    owner.value = 'any';
    owner.dispatchEvent(new Event('change'));
    const removeType = host.querySelector('button[aria-label="Remove filter Type: PDF"]') as HTMLButtonElement;
    removeType.click();
    const refreshed = http.expectOne(request => request.url === '/api/search');
    expect(refreshed.request.params.has('fileKind')).toBe(false);
    expect(refreshed.request.params.get('q')).toBe('report');
    expect(refreshed.request.params.get('authorUserId')).toBe(DEFAULT_AUTH_SESSION.currentUser?.userId);
    refreshed.flush({ page: 1, pageSize: 50, totalCount: 0, items: [] });
    fixture.detectChanges();
    expect(host.querySelector('button[aria-label="Remove filter Type: PDF"]')).toBeNull();
  });

  it('fails closed without rendering a mismatched Workspace search row or server count', async () => {
    const { fixture, http } = await renderLiveFilesPage([]);
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector('[data-testid="files-search-input"]') as HTMLInputElement;
    input.value = 'report';
    input.dispatchEvent(new Event('input'));
    (host.querySelector('[data-testid="files-search-surface"] form') as HTMLFormElement)
      .dispatchEvent(new Event('submit'));
    http.expectOne(request => request.url === '/api/search').flush({
      page: 1,
      pageSize: 50,
      totalCount: 99,
      items: [{
        type: 13,
        id: FILE_OBJECT_ID,
        title: 'other-workspace-secret.pdf',
        workspaceId: '22222222-2222-4222-8222-222222222222',
        createdAt: '2026-08-20T00:00:00Z',
        contentType: 'application/pdf',
        sizeBytes: 1,
        status: 'Active',
      }],
    });
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('invalid or mismatched response');
    expect(textContent(fixture)).not.toContain('other-workspace-secret.pdf');
    expect(textContent(fixture)).not.toContain('99 files');
  });

  it('keeps retry state when backend upload fails', async () => {
    const { fixture, http } = await renderLiveFilesPage([]);
    const dropZone = fixture.debugElement.query(By.directive(CoglatasFileUploaderComponent))
      .componentInstance as CoglatasFileUploaderComponent;

    dropZone.filesSelected.emit([new File(['hello'], 'note.txt', { type: 'text/plain' })]);
    fixture.detectChanges();

    const upload = http.expectOne((request) => request.url === '/api/files' && request.method === 'POST');
    upload.flush({ error: 'File extension is not allowed.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('File extension is not allowed.');
    expect(textContent(fixture)).toContain('note.txt');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="file-upload-disabled"]')).toBeNull();
    http.expectNone((request) => request.url === '/api/files' && request.method === 'GET');
  });

  it('submits oversize files to the backend policy rather than inventing a client limit', async () => {
    const { fixture, http } = await renderLiveFilesPage([]);
    const dropZone = fixture.debugElement.query(By.directive(CoglatasFileUploaderComponent))
      .componentInstance as CoglatasFileUploaderComponent;

    dropZone.filesSelected.emit([{ name: 'oversized-video.mp4', size: 51 * 1024 * 1024, type: 'video/mp4' } as File]);
    fixture.detectChanges();

    const upload = http.expectOne((request) => request.url === '/api/files' && request.method === 'POST');
    upload.flush({ error: 'File exceeds the configured upload limit.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(textContent(fixture)).toContain('File exceeds the configured upload limit.');
  });

  it('submits file types to the backend policy rather than duplicating its allowlist', async () => {
    const { fixture, http } = await renderLiveFilesPage([]);
    const dropZone = fixture.debugElement.query(By.directive(CoglatasFileUploaderComponent))
      .componentInstance as CoglatasFileUploaderComponent;

    dropZone.filesSelected.emit([new File(['bad'], 'run.exe', { type: 'application/x-msdownload' })]);
    fixture.detectChanges();

    const upload = http.expectOne((request) => request.url === '/api/files' && request.method === 'POST');
    upload.flush({ error: 'File extension is not allowed.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(textContent(fixture)).toContain('File extension is not allowed.');
  });

  it('downloads through backend grant issuance and grant download', async () => {
    const { fixture, http } = await renderLiveFilesPage([backendFile]);
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const createObjectUrlSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:fixture');
    const revokeObjectUrlSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);

    downloadButton(fixture).click();
    fixture.detectChanges();

    const grant = http.expectOne(`/api/files/${FILE_OBJECT_ID}/download-grants`);
    expect(grant.request.method).toBe('POST');
    expect(grant.request.body).toEqual({ purpose: 'files-page-download' });
    expect(grant.request.withCredentials).toBe(true);
    grant.flush({ fileDownloadGrantId: 'grant-1', fileObjectId: FILE_OBJECT_ID, token: 'raw-token' });

    const download = http.expectOne('/api/file-download-grants/grant-1/download');
    expect(download.request.method).toBe('POST');
    expect(download.request.body).toEqual({ token: 'raw-token' });
    expect(download.request.withCredentials).toBe(true);
    download.flush(new Blob(['hello'], { type: 'text/plain' }), {
      headers: { 'content-disposition': 'attachment; filename="note.txt"' },
    });
    fixture.detectChanges();

    expect(clickSpy).toHaveBeenCalled();
    expect(createObjectUrlSpy).toHaveBeenCalled();
    expect(revokeObjectUrlSpy).toHaveBeenCalledWith('blob:fixture');
    expect(textContent(fixture)).toContain('Download started.');
  });

  it('switches to authorized contextual actions and confirms the named delete target', async () => {
    const { fixture, http } = await renderLiveFilesPage([backendFile]);
    const component = fixture.componentInstance;
    const file = component.page().recentFiles[0];
    if (!file) {
      throw new Error('Expected a listed file.');
    }

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="files-normal-toolbar"]')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="files-selected-delete"]')).toBeNull();

    component.handleSelectionChanged({ rows: [file] });
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="files-normal-toolbar"]')).toBeNull();
    expect(host.querySelector('[data-testid="files-selected-download"]')).not.toBeNull();
    const deleteButton = host.querySelector('[data-testid="files-selected-delete"]') as HTMLButtonElement;
    expect(deleteButton).not.toBeNull();

    deleteButton.click();
    fixture.detectChanges();
    expect(textContent(fixture)).toContain('Delete note.txt?');

    (host.querySelector('.coglatas-dialog__confirm') as HTMLButtonElement).click();
    fixture.detectChanges();
    const deletion = http.expectOne(`/api/files/${FILE_OBJECT_ID}`);
    expect(deletion.request.method).toBe('DELETE');
    expect(deletion.request.withCredentials).toBe(true);
    deletion.flush(null);
    flushFileList(http, []);
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('The file was deleted.');
    expect(host.querySelector('[data-testid="files-normal-toolbar"]')).not.toBeNull();
  }, 15_000);

  it('keeps a canonically redacted display label redacted in destructive confirmation', async () => {
    const { fixture } = await renderLiveFilesPage([
      { ...backendFile, originalFileName: '[redacted:file]' },
    ]);
    const component = fixture.componentInstance;
    const file = component.page().recentFiles[0];
    if (!file) {
      throw new Error('Expected a listed file.');
    }

    component.handleSelectionChanged({ rows: [file] });
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    (host.querySelector('[data-testid="files-selected-delete"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Delete [redacted:file]?');
    expect(textContent(fixture)).not.toContain('note.txt');
  });

  it('keeps the batch Delete action visible but disabled when the server capability is absent', async () => {
    const { fixture } = await renderLiveFilesPage([{ ...backendFile, canDelete: undefined }]);
    const component = fixture.componentInstance;
    const file = component.page().recentFiles[0];
    if (!file) {
      throw new Error('Expected a listed file.');
    }

    component.handleSelectionChanged({ rows: [file] });
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect((host.querySelector('[data-testid="files-selected-delete"]') as HTMLButtonElement).disabled).toBe(true);
    expect(host.querySelector('[data-testid="files-selected-download"]')).not.toBeNull();
  });

  it('captures all search results on the server and batch-deletes the opaque selection', async () => {
    const secondFileObjectId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    const { fixture, http } = await renderLiveFilesPage([backendFile]);
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector('[data-testid="files-search-input"]') as HTMLInputElement;
    input.value = 'report';
    input.dispatchEvent(new Event('input'));
    (host.querySelector('[data-testid="files-search-surface"] form') as HTMLFormElement)
      .dispatchEvent(new Event('submit'));

    const search = http.expectOne((request) => request.url === '/api/search');
    search.flush({
      page: 1,
      pageSize: 50,
      totalCount: 2,
      items: [
        {
          type: 13,
          id: FILE_OBJECT_ID,
          title: 'report-one.pdf',
          workspaceId: WORKSPACE_ID,
          createdAt: '2026-08-20T00:00:00Z',
          contentType: 'application/pdf',
          sizeBytes: 10,
          status: 'Active',
        },
        {
          type: 13,
          id: secondFileObjectId,
          title: 'report-two.pdf',
          workspaceId: WORKSPACE_ID,
          createdAt: '2026-08-21T00:00:00Z',
          contentType: 'application/pdf',
          sizeBytes: 20,
          status: 'Active',
        },
      ],
    });
    fixture.detectChanges();

    (host.querySelector('[data-testid="files-select-all-search-results"]') as HTMLButtonElement).click();
    const capture = http.expectOne(request =>
      request.url === '/api/files/selection-snapshots' && request.method === 'POST');
    expect(capture.request.params.get('workspaceId')).toBe(WORKSPACE_ID);
    expect(capture.request.params.get('q')).toBe('report');
    capture.flush({
      outcome: 'Captured',
      selectionSnapshotId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
      selectedCount: 2,
      maximumSelectionCount: 100,
      expiresAt: '2026-08-31T12:00:00Z',
    });
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('2 selected');
    expect(textContent(fixture)).toContain('Captured search-result selection');
    expect((host.querySelector('[data-testid="files-selected-download"]') as HTMLButtonElement).disabled).toBe(true);

    (host.querySelector('[data-testid="files-selected-delete"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(textContent(fixture)).toContain('Delete 2 captured search-result files?');
    expect(textContent(fixture)).toContain('restoration follows your organization’s recovery policy');

    (host.querySelector('.coglatas-dialog__confirm') as HTMLButtonElement).click();
    const deletion = http.expectOne('/api/files/selection-snapshots/bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb/delete');
    expect(deletion.request.method).toBe('POST');
    deletion.flush({ attemptedCount: 2, succeededCount: 1, failedCount: 1, items: [] });

    const refreshedSearch = http.expectOne((request) => request.url === '/api/search');
    refreshedSearch.flush({ page: 1, pageSize: 50, totalCount: 1, items: [] });
    flushFileList(http, []);
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('1 of 2 files were deleted.');
    expect(host.querySelector('[data-testid="files-normal-toolbar"]')).not.toBeNull();
  }, 15_000);

  it('selects a mobile checkbox range from the last selection anchor', async () => {
    const { fixture } = await renderLiveFilesPage([
      backendFile,
      { ...backendFile, id: 'attachment-2', fileObjectId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', originalFileName: 'two.txt' },
      { ...backendFile, id: 'attachment-3', fileObjectId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', originalFileName: 'three.txt' },
    ]);
    const component = fixture.componentInstance;
    const files = component.page().recentFiles;
    const first = files[0];
    const last = files[2];
    if (!first || !last) {
      throw new Error('Expected three listed files.');
    }

    component.handleMobileSelection({ file: first, selected: true });
    component.handleMobileSelection({ file: last, selected: true, range: true });
    fixture.detectChanges();

    expect(component.selectedCount()).toBe(3);
    expect(textContent(fixture)).toContain('3 selected');
  });

  it('shows a safe denied state when download grant issuance is denied', async () => {
    const { fixture, http } = await renderLiveFilesPage([backendFile]);

    downloadButton(fixture).click();
    fixture.detectChanges();

    const grant = http.expectOne(`/api/files/${FILE_OBJECT_ID}/download-grants`);
    grant.flush({ error: 'not allowed' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Download denied.');
    http.expectNone((request) => request.url.startsWith('/api/file-download-grants/'));
  });

  it('disables download while scan is pending or blocked', async () => {
    const pending = await renderMockFilesPage(FILES_PAGE_SCENARIOS.scanPending);
    expect(downloadButton(pending).disabled).toBe(true);
    expect(textContent(pending)).toContain('Download is disabled until scan state allows it.');

    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();

    const blocked = await renderMockFilesPage(FILES_PAGE_SCENARIOS.scanBlocked);
    expect(downloadButton(blocked).disabled).toBe(true);
    expect(textContent(blocked)).toContain('Download is blocked by file scan state.');
  });

  it('opens an authorized PDF in a separate protected browsing context without embedding it', async () => {
    const { fixture, http } = await renderLiveFilesPage([
      { ...backendFile, originalFileName: 'report.pdf', contentType: 'application/pdf', scanStatus: 'Allowed' },
    ]);
    const createObjectUrlSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:authorized-pdf');

    openAuthorizedPdfPreview(fixture, http);
    fixture.detectChanges();

    const pdfLink = pdfPreviewLink(fixture);
    expect(pdfLink.getAttribute('href')).toBe('blob:authorized-pdf');
    expect(pdfLink.getAttribute('target')).toBe('_blank');
    expect(pdfLink.getAttribute('rel')).toContain('noopener');
    expect((fixture.nativeElement as HTMLElement).querySelector('iframe')).toBeNull();
    expect(createObjectUrlSpy).toHaveBeenCalledOnce();
  });

  it('does not render preview, SVG image, public link, or streaming elements', async () => {
    const fixture = await renderMockFilesPage(FILES_PAGE_SCENARIOS.previewDisabled);
    const host = fixture.nativeElement as HTMLElement;
    const text = textContent(fixture);

    expect(host.querySelector('img')).toBeNull();
    expect(host.querySelector('iframe')).toBeNull();
    expect(host.querySelector('object')).toBeNull();
    expect(host.querySelector('embed')).toBeNull();
    expect(host.querySelector('video')).toBeNull();
    expect(host.querySelector('[data-testid="files-preview-pane"] svg')).toBeNull();
    expect(text).toContain('Preview is not available in MVP0.');
    expect(text).toContain('CDN links, public links, and external signed URL sharing are disabled.');
  });

  it('does not expose internal storage keys, paths, or raw scan metadata', async () => {
    const fixture = await renderMockFilesPage(FILES_PAGE_SCENARIOS.default);
    const text = textContent(fixture);

    expect(text).not.toContain('tenant-a/private/raw');
    expect(text).not.toContain('/var/lib/coglatas/private/raw');
    expect(text).not.toContain('engine=mock');
    expect(text).not.toContain('private-debug-value');
  });

  it('keeps attachment picker disabled in MVP0', async () => {
    const fixture = await renderMockFilesPage(FILES_PAGE_SCENARIOS.noCanonicalFileIdYet);
    const checkboxes = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>(
      '[data-testid="attachment-picker-checkbox"]',
    );
    const picker = fixture.debugElement.query(By.directive(AttachmentPickerDialogComponent))
      .componentInstance as AttachmentPickerDialogComponent;

    expect(Array.from(checkboxes).every((checkbox) => checkbox.disabled)).toBe(true);

    picker.toggleFile(DEFAULT_FILES[0], true);
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Attachment picker is not available in MVP0.');
    expect(textContent(fixture)).toContain('No file selected.');
  });

  it('keeps admin override disabled even when a reason is entered', async () => {
    const fixture = await renderMockFilesPage(FILES_PAGE_SCENARIOS.adminOverrideRequired);
    const row = fixture.debugElement.query(By.directive(FileRowComponent)).componentInstance as FileRowComponent;
    const action = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="admin-override-action"]',
    ) as HTMLButtonElement;

    row.updateAuditReason('Operational need');
    fixture.detectChanges();

    expect(action.disabled).toBe(true);
    expect(textContent(fixture)).toContain('Admin override is not wired for MVP0 downloads.');
  });
});
