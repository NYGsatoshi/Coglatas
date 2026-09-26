import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { COGLATAS_ACTIVE_WORKSPACE_MOCK } from '../../../core/workspace/active-workspace.facade';
import { COGLATAS_FILES_PAGE_MOCK } from '../files.facade';
import { FILES_PAGE_SCENARIOS } from '../files.mock';
import { FilesPageViewModel, FileViewModel } from '../files.types';
import { FilesPageComponent } from './files-page.component';

const WORKSPACE_ID = '11111111-1111-4111-8111-111111111111';
const FILE_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

const inspectorFile: FileViewModel = {
  ...FILES_PAGE_SCENARIOS.default.recentFiles[0]!,
  id: 'inspector-file',
  canonicalFileId: FILE_ID,
  originalFileName: 'research-archive.zip',
  contentType: 'application/zip',
  sizeBytes: 4096,
  scanStatus: 'allowed',
  uploadedByDisplay: 'Authorized owner',
  createdAtLabel: '2026/08/20',
  modifiedAtLabel: '2026/08/29',
  kind: 'zip',
  downloadPolicy: 'available',
  capabilities: ['download'],
  internalStorageKey: 'tenant/private/storage-key',
  internalPath: 'C:\\private\\file.zip',
  rawScanMetadata: 'scanner-secret-payload',
};

async function renderFilesPage(
  file: FileViewModel = inspectorFile,
): Promise<ComponentFixture<FilesPageComponent>> {
  const page: FilesPageViewModel = {
    ...FILES_PAGE_SCENARIOS.default,
    recentFiles: [file],
    pickerFiles: [file],
    totalCount: 1,
  };

  await TestBed.configureTestingModule({
    imports: [FilesPageComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: COGLATAS_FILES_PAGE_MOCK, useValue: page },
      { provide: COGLATAS_ACTIVE_WORKSPACE_MOCK, useValue: { id: WORKSPACE_ID, label: 'Workspace' } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(FilesPageComponent);
  fixture.detectChanges();
  fixture.componentInstance.openPreview(file);
  fixture.detectChanges();
  return fixture;
}

describe('FilesPageComponent issue #356', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('keeps Preview, Details, and Activity in one inspector with Preview selected initially', async () => {
    const fixture = await renderFilesPage();
    const host = fixture.nativeElement as HTMLElement;
    const tabs = host.querySelectorAll<HTMLButtonElement>('[role="tab"]');

    expect(tabs).toHaveLength(3);
    expect(Array.from(tabs).map((tab) => tab.textContent?.trim())).toEqual([
      'Preview',
      'Details',
      'Activity',
    ]);
    expect(tabs[0]?.getAttribute('aria-selected')).toBe('true');
    expect(tabs[0]?.tabIndex).toBe(0);
    expect(tabs[1]?.tabIndex).toBe(-1);
    expect(host.querySelector('[data-testid="files-inspector-panel-preview"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="files-inspector-panel-details"]')).toBeNull();
    expect(host.querySelectorAll('[data-testid="files-preview-pane"]')).toHaveLength(1);
  }, 15_000);

  it('shows essential metadata first and keeps bounded secondary metadata collapsed', async () => {
    const fixture = await renderFilesPage();
    const component = fixture.componentInstance;
    component.selectInspectorTab('details');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const panel = host.querySelector('[data-testid="files-inspector-panel-details"]') as HTMLElement;
    const disclosure = panel.querySelector('details') as HTMLDetailsElement;
    const text = panel.textContent ?? '';

    expect(text).toContain('Archive');
    expect(text).toContain('4 KB');
    expect(text).toContain('Authorized owner');
    expect(text).toContain('2026/08/29');
    expect(text).toContain('Workspace');
    expect(text).toContain('Authorized download');
    expect(disclosure.open).toBe(false);
    expect(disclosure.textContent).toContain(FILE_ID);
    expect(panel.querySelector('input, textarea, select, [contenteditable="true"]')).toBeNull();
    expect(text).not.toContain('tenant/private/storage-key');
    expect(text).not.toContain('C:\\private\\file.zip');
    expect(text).not.toContain('scanner-secret-payload');
  }, 15_000);

  it('supports Arrow, Home, and End keyboard navigation with one roving tab stop', async () => {
    const fixture = await renderFilesPage();
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const preview = host.querySelector('[data-testid="files-inspector-tab-preview"]') as HTMLButtonElement;
    const details = host.querySelector('[data-testid="files-inspector-tab-details"]') as HTMLButtonElement;
    const activity = host.querySelector('[data-testid="files-inspector-tab-activity"]') as HTMLButtonElement;

    preview.focus();
    preview.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    await Promise.resolve();
    expect(fixture.componentInstance.inspectorTab()).toBe('details');
    expect(document.activeElement).toBe(details);

    details.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    fixture.detectChanges();
    await Promise.resolve();
    expect(fixture.componentInstance.inspectorTab()).toBe('activity');
    expect(document.activeElement).toBe(activity);

    TestBed.inject(HttpTestingController)
      .expectOne(`/api/files/${FILE_ID}/activity`)
      .flush({ fileObjectId: FILE_ID, items: [] });
    fixture.detectChanges();

    activity.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    fixture.detectChanges();
    await Promise.resolve();
    expect(fixture.componentInstance.inspectorTab()).toBe('preview');
    expect(document.activeElement).toBe(preview);
    expect(Array.from(host.querySelectorAll<HTMLButtonElement>('[role="tab"]')).filter((tab) => tab.tabIndex === 0))
      .toEqual([preview]);

    fixture.destroy();
  }, 15_000);

  it('hands Activity to the authorized File endpoint without querying the broader Audit API', async () => {
    const fixture = await renderFilesPage();
    fixture.componentInstance.selectInspectorTab('activity');
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    const activityRequest = http.expectOne(`/api/files/${FILE_ID}/activity`);
    expect(activityRequest.request.method).toBe('GET');
    activityRequest.flush({ fileObjectId: FILE_ID, items: [] });
    fixture.detectChanges();

    const panel = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="files-inspector-panel-activity"]',
    ) as HTMLElement;
    expect(panel.textContent).toContain('No file activity or version history is available');
    expect(panel.textContent).toContain('does not query the broader Audit log');
    http.expectNone((request) => request.url.includes('/api/audit'));
    http.expectNone((request) => request.url.includes('/versions/'));
  }, 15_000);

  it('returns to Preview when another file opens or the inspector closes', async () => {
    const fixture = await renderFilesPage();
    const component = fixture.componentInstance;

    component.selectInspectorTab('details');
    expect(component.inspectorTab()).toBe('details');
    component.openPreview(inspectorFile);
    expect(component.inspectorTab()).toBe('preview');
    component.selectInspectorTab('activity');
    component.closePreview(false);
    expect(component.inspectorTab()).toBe('preview');
    expect(component.previewOpen()).toBe(false);
  }, 15_000);
});