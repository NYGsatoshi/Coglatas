import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthSessionFacade, COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { COGLATAS_ACTIVE_WORKSPACE_MOCK } from '../../../core/workspace/active-workspace.facade';
import { FilesPageComponent } from './files-page.component';

const WORKSPACE_ID = '11111111-1111-4111-8111-111111111111';
const FILE_OBJECT_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

const renderFuturePresentation = async () => {
  await TestBed.configureTestingModule({
    imports: [FilesPageComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: COGLATAS_ACTIVE_WORKSPACE_MOCK, useValue: { id: WORKSPACE_ID, label: 'Synthetic Workspace' } },
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(FilesPageComponent);
  // Isolated presentation exercise only: production retains its literal false.
  // HttpTestingController intercepts every request; no backend intake is enabled.
  Object.defineProperty(fixture.componentInstance, 'uploadsAvailable', { value: true });
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  http.expectOne(request => request.url === '/api/file-folders').flush({ workspaceId: WORKSPACE_ID, rootVersion: 0, folders: [] });
  http.expectOne(request => request.url === '/api/files').flush({ items: [], page: 1, pageSize: 50, totalCount: 0, hasMore: false });
  fixture.detectChanges();
  const host = fixture.nativeElement as HTMLElement;
  const input = () => host.querySelector<HTMLInputElement>('input[type="file"]')!;
  const select = (files: readonly File[]) => {
    Object.defineProperty(input(), 'files', { value: files, configurable: true });
    input().dispatchEvent(new Event('change'));
    fixture.detectChanges();
    return http.expectOne(request => request.url === '/api/files' && request.method === 'POST');
  };
  const button = (label: string) => {
    const match = Array.from(host.querySelectorAll<HTMLButtonElement>('app-coglatas-file-uploader button'))
      .find(item => item.textContent?.trim() === label);
    if (!match) { throw new Error('Missing uploader button: ' + label); }
    return match;
  };
  return { fixture, http, host, input, select, button };
};

describe('Files page non-enabled upload preparation with intercepted HTTP', () => {
  beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => {
    localStorage.removeItem('coglatas.locale');
    try { TestBed.inject(HttpTestingController).verify(); }
    finally { TestBed.resetTestingModule(); }
  });

  it('uses one single-file uploader and keeps Cancel usable for unknown-total progress', async () => {
    const { fixture, http, host, input, select, button } = await renderFuturePresentation();
    expect(host.querySelectorAll('input[type="file"]')).toHaveLength(1);
    expect(input().multiple).toBe(false);
    const first = new File(['first'], 'first.txt', { type: 'text/plain' });
    const second = new File(['second'], 'second.txt', { type: 'text/plain' });
    const upload = select([first, second]);
    expect(upload.request.withCredentials).toBe(true);
    expect((upload.request.body as FormData).get('File')).toBe(first);
    expect((upload.request.body as FormData).get('OwnerId')).toBe(WORKSPACE_ID);
    http.expectNone(request => request.url === '/api/files' && request.method === 'POST');
    upload.event({ type: HttpEventType.UploadProgress, loaded: 2 });
    fixture.detectChanges();
    expect(input().disabled).toBe(true);
    expect(host.querySelector('progress')?.hasAttribute('value')).toBe(false);
    expect(button('Cancel').disabled).toBe(false);
    button('Cancel').click();
    fixture.detectChanges();
    expect(upload.cancelled).toBe(true);
    expect(input().disabled).toBe(false);
    expect(host.querySelector('progress')).toBeNull();
    expect(Array.from(host.querySelectorAll('app-coglatas-file-uploader button')).some(item => item.textContent?.trim() === 'Retry')).toBe(false);
    const next = select([second]);
    button('Cancel').click();
    fixture.detectChanges();
    expect(next.cancelled).toBe(true);
  }, 15_000);

  it('maps explicit failed Retry to the same file once and refreshes inventory on success', async () => {
    const { fixture, http, host, select, button } = await renderFuturePresentation();
    const file = new File(['content'], 'retry.txt', { type: 'text/plain' });
    select([file]).flush({ message: 'Synthetic failure' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    const retry = button('Retry');
    expect(retry.disabled).toBe(false);
    retry.click();
    const retried = http.expectOne(request => request.url === '/api/files' && request.method === 'POST');
    expect((retried.request.body as FormData).get('File')).toBe(file);
    retry.click();
    http.expectNone(request => request.url === '/api/files' && request.method === 'POST');
    retried.event({ type: HttpEventType.UploadProgress, loaded: 3, total: 7 });
    fixture.detectChanges();
    expect(host.querySelector('progress')?.getAttribute('value')).toBe('43');
    retried.flush({ originalFileName: file.name, fileObjectId: FILE_OBJECT_ID });
    const inventory = http.expectOne(request => request.url === '/api/files' && request.method === 'GET');
    inventory.flush({ items: [{
      id: 'synthetic-attachment', fileObjectId: FILE_OBJECT_ID, workspaceId: WORKSPACE_ID,
      originalFileName: file.name, contentType: file.type, sizeBytes: file.size,
      status: 'Active', scanStatus: 'Skipped', uploadedByUserId: 'synthetic-user',
      uploadedByDisplayName: 'Synthetic User', createdAt: '2026-10-30T00:00:00Z', updatedAt: '2026-10-30T00:00:00Z',
    }], page: 1, pageSize: 50, totalCount: 1, hasMore: false });
    fixture.detectChanges();
    expect(fixture.componentInstance.page().recentFiles[0].originalFileName).toBe(file.name);
    expect(fixture.componentInstance.page().upload.state).toBe('succeeded');
    expect(host.querySelector('progress')).toBeNull();
  }, 15_000);

  it.each([401, 403])('does not offer a dead Retry for non-retained HTTP %s failures', async status => {
    const { fixture, http, host, select } = await renderFuturePresentation();
    select([new File(['denied'], 'denied.txt')]).flush({}, { status, statusText: 'Denied' });
    fixture.detectChanges();
    expect(fixture.componentInstance.page().uploadQueue[0].state).toBe('failed');
    expect(Array.from(host.querySelectorAll('app-coglatas-file-uploader button')).some(item => item.textContent?.trim() === 'Retry')).toBe(false);
    http.expectNone(request => request.method === 'POST');
  }, 15_000);

  it('blocks an older transient Retry while keeping the current Cancel available', async () => {
    const { fixture, http, select, button } = await renderFuturePresentation();
    const first = new File(['first'], 'retained.txt');
    select([first]).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    const active = select([new File(['active'], 'active.txt')]);
    expect(button('Retry').disabled).toBe(true);
    expect(button('Cancel').disabled).toBe(false);
    button('Retry').click();
    http.expectNone(request => request.method === 'POST');
    button('Cancel').click();
    fixture.detectChanges();
    expect(active.cancelled).toBe(true);
    expect(button('Retry').disabled).toBe(false);
    button('Retry').click();
    fixture.detectChanges();
    const retried = http.expectOne(request => request.method === 'POST');
    expect((retried.request.body as FormData).get('File')).toBe(first);
    button('Cancel').click();
    fixture.detectChanges();
    expect(retried.cancelled).toBe(true);
  }, 15_000);

  it('clears in-flight presentation and prevents another intercepted POST after session invalidation', async () => {
    const { fixture, http, host, input, select } = await renderFuturePresentation();
    const upload = select([new File(['private'], 'session-private.txt')]);
    TestBed.inject(AuthSessionFacade).logoutLocally();
    fixture.detectChanges();
    expect(upload.cancelled).toBe(true);
    expect(input().disabled).toBe(true);
    expect(host.textContent).not.toContain('session-private.txt');
    expect(host.querySelector('progress')).toBeNull();
    http.expectNone(request => request.method === 'POST');
  }, 15_000);
});
