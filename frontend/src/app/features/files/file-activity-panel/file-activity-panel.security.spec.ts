import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { FILES_PAGE_SCENARIOS } from '../files.mock';
import { FileActivityPanelComponent } from './file-activity-panel.component';

const CONFIG = {
    fileId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    fileSizeBytes: 128,
    versionId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    versionNumber: 2,
  } as const,
  SCENARIO_FILE = FILES_PAGE_SCENARIOS.default.recentFiles.at(0);

if (!SCENARIO_FILE) {
  throw new Error('Expected the Files fixture to contain a recent file.');
}

const setupVersion = async (options: Readonly<{ contentType: string; fileName: string }>) => {
  await TestBed.configureTestingModule({
    imports: [FileActivityPanelComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }).compileComponents();
  const state = {
    createObjectUrl: vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:historical-pdf'),
    fixture: TestBed.createComponent(FileActivityPanelComponent),
    http: TestBed.inject(HttpTestingController),
  };
  state.fixture.componentRef.setInput('file', {
    ...SCENARIO_FILE,
    canonicalFileId: CONFIG.fileId,
    capabilities: ['download'],
    contentType: 'text/plain',
    downloadPolicy: 'available',
    id: 'activity-file',
    kind: 'document',
    originalFileName: 'research-notes.txt',
    scanStatus: 'allowed',
    sizeBytes: CONFIG.fileSizeBytes,
  });
  state.fixture.detectChanges();
  state.http.expectOne(`/api/files/${CONFIG.fileId}/activity`).flush({
    fileObjectId: CONFIG.fileId,
    items: [{
      actorDisplayName: 'File Editor',
      id: CONFIG.versionId,
      kind: 'versionCreated',
      occurredAt: '2026-09-01T13:00:00Z',
      version: {
        contentType: options.contentType,
        createdAt: '2026-09-01T13:00:00Z',
        fileName: options.fileName,
        isCurrent: true,
        sizeBytes: CONFIG.fileSizeBytes,
        versionId: CONFIG.versionId,
        versionNumber: CONFIG.versionNumber,
      },
    }],
  });
  state.fixture.detectChanges();
  (state.fixture.nativeElement instanceof HTMLElement ? state.fixture.nativeElement : null)
    ?.querySelector<HTMLButtonElement>('[data-testid="files-activity-view-version"]')
    ?.click();
  state.fixture.detectChanges();
  return {
    ...state,
    host: state.fixture.nativeElement instanceof HTMLElement ? state.fixture.nativeElement : null,
  };
};

beforeEach(() => {
  window.localStorage.setItem('coglatas.locale', 'en');
});

afterEach(() => {
  window.localStorage.removeItem('coglatas.locale');
  TestBed.inject(HttpTestingController).verify();
  vi.restoreAllMocks();
  TestBed.resetTestingModule();
});

it('opens a historical PDF only after the returned Blob MIME matches the PDF metadata', async () => {
  const state = await setupVersion({ contentType: 'application/pdf', fileName: 'brief.pdf' });
  state.http.expectOne(`/api/files/${CONFIG.fileId}/versions/${CONFIG.versionId}/content`)
    .flush(new Blob(['pdf'], { type: 'application/pdf' }));
  state.fixture.detectChanges();
  expect(state.host?.querySelector('[data-testid="files-version-preview-pdf-link"]')).not.toBeNull();
  expect(state.host?.querySelector('[data-testid="files-version-preview-pdf-link"]')?.getAttribute('href')).toBe('blob:historical-pdf');
  expect(state.host?.querySelector('[data-testid="files-version-preview-pdf-link"]')?.getAttribute('target')).toBe('_blank');
  expect(state.host?.querySelector('[data-testid="files-version-preview-pdf-link"]')?.getAttribute('rel')).toContain('noopener');
  expect(state.host?.querySelector('iframe')).toBeNull();
  expect(state.createObjectUrl).toHaveBeenCalledOnce();
});

it('fails closed when historical PDF metadata does not match the returned Blob MIME', async () => {
  const state = await setupVersion({ contentType: 'application/pdf', fileName: 'brief.pdf' });
  state.http.expectOne(`/api/files/${CONFIG.fileId}/versions/${CONFIG.versionId}/content`)
    .flush(new Blob(['<html>active</html>'], { type: 'text/html' }));
  state.fixture.detectChanges();
  expect(state.host?.querySelector('[data-testid="files-version-preview-pdf-link"]')).toBeNull();
  expect(state.host?.textContent).toContain('The returned file type did not match this version.');
  expect(state.createObjectUrl).not.toHaveBeenCalled();
});

it('does not fetch or mint a Blob URL for an unsupported historical active-content type', async () => {
  const state = await setupVersion({ contentType: 'application/xhtml+xml', fileName: 'payload.xhtml' });
  state.http.expectNone(`/api/files/${CONFIG.fileId}/versions/${CONFIG.versionId}/content`);
  expect(state.host?.textContent).toContain('Preview is not available for this file type.');
  expect(state.host?.querySelector('a[href^="blob:"]')).toBeNull();
  expect(state.createObjectUrl).not.toHaveBeenCalled();
});