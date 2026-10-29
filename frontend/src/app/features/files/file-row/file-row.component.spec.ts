import { ComponentFixture, TestBed } from '@angular/core/testing';

import { I18nService } from '../../../core/i18n/i18n.service';
import { FileViewModel } from '../files.types';
import { FileRowComponent } from './file-row.component';

const downloadableFile = (overrides: Partial<FileViewModel> = {}): FileViewModel => ({
  id: 'synthetic-row-first', canonicalFileId: 'synthetic-canonical-first',
  originalFileName: 'first.txt', contentType: 'text/plain', sizeBytes: 12,
  scanStatus: 'allowed', uploadedByDisplay: 'Fixture User',
  createdAtLabel: '2026-10-29', modifiedAtLabel: '2026-10-29', kind: 'document',
  downloadPolicy: 'available', capabilities: ['download'], canDelete: false,
  sharing: { accessState: 'private', canManageSharing: false },
  downloadState: 'idle', ...overrides,
});

const denialTransitions = [
  { name: 'pending scan', update: { scanStatus: 'pending' },
    reason: 'Download is disabled until scan state allows it.' },
  { name: 'unavailable scan', update: { scanStatus: 'unavailable' },
    reason: 'Download is disabled until scan state allows it.' },
  { name: 'blocked scan', update: { scanStatus: 'blocked' },
    reason: 'Download is blocked by file scan state.' },
  { name: 'denied server policy', update: { downloadPolicy: 'denied' },
    reason: 'You do not have permission to download this file.' },
  { name: 'removed download capability', update: { capabilities: [] },
    reason: 'You do not have permission to download this file.' },
  { name: 'pending authorization', update: { downloadState: 'pending' },
    reason: 'Download authorization is pending.' },
] satisfies readonly { name: string; update: Partial<FileViewModel>; reason: string }[];

const locales = {
  en: { download: 'Download', denied: 'You do not have permission to download this file.' },
  ja: { download: '\u30c0\u30a6\u30f3\u30ed\u30fc\u30c9',
    denied: '\u3053\u306e\u30d5\u30a1\u30a4\u30eb\u3092\u30c0\u30a6\u30f3\u30ed\u30fc\u30c9\u3059\u308b\u6a29\u9650\u304c\u3042\u308a\u307e\u305b\u3093\u3002' },
};

describe('FileRowComponent input lifecycle', () => {
  let fixture: ComponentFixture<FileRowComponent>;
  let i18n: I18nService;
  let requested: string[];

  beforeEach(async () => {
    window.localStorage.removeItem('coglatas.locale');
    await TestBed.configureTestingModule({ imports: [FileRowComponent] }).compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLocale('en');
    fixture = TestBed.createComponent(FileRowComponent);
    requested = [];
    fixture.componentInstance.downloadRequested.subscribe(id => requested.push(id));
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    window.localStorage.removeItem('coglatas.locale');
  });

  const render = (file: FileViewModel): HTMLElement => {
    fixture.componentRef.setInput('file', file);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const downloadAction = (): HTMLButtonElement => {
    const button = (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="download-action"]');
    if (!button) throw new Error('Expected the rendered download action.');
    return button;
  };

  it.each(denialTransitions)('stops a reused row after $name and requires an explicit fresh action', ({ update, reason }) => {
    const first = downloadableFile();
    const host = render(first);
    expect(downloadAction().disabled).toBe(false);
    downloadAction().click();
    expect(requested).toEqual(['synthetic-canonical-first']);

    render({ ...first, ...update });
    expect(downloadAction().disabled).toBe(true);
    expect(host.querySelector('[data-testid="download-disabled-reason"]')?.textContent).toBe(reason);
    downloadAction().click();
    fixture.componentInstance.requestDownload();
    expect(requested).toEqual(['synthetic-canonical-first']);

    render(downloadableFile({ id: 'synthetic-row-next', canonicalFileId: 'synthetic-canonical-next',
      originalFileName: 'next.txt' }));
    expect(downloadAction().disabled).toBe(false);
    expect(host.querySelector('[data-testid="file-name"]')?.textContent?.trim()).toBe('next.txt');
    expect(host.querySelector('[data-testid="download-disabled-reason"]')).toBeNull();
    expect(requested).toEqual(['synthetic-canonical-first']);
    downloadAction().click();
    expect(requested).toEqual(['synthetic-canonical-first', 'synthetic-canonical-next']);
  });

  it('permits a manual retry after a failed authorization without auto-dispatch or the presentation row id', () => {
    const pending = downloadableFile({ downloadState: 'pending' });
    const host = render(pending);
    expect(downloadAction().disabled).toBe(true);
    downloadAction().click();
    expect(requested).toEqual([]);

    render({ ...pending, downloadState: 'failed', downloadMessage: 'Previous authorization failed.' });
    expect(downloadAction().disabled).toBe(false);
    expect(host.querySelector('[data-testid="download-state"]')?.textContent).toBe('Previous authorization failed.');
    expect(requested).toEqual([]);
    downloadAction().click();
    expect(requested).toEqual(['synthetic-canonical-first']);
  });

  it.each(['en', 'ja'] as const)('updates a denied reused row from %s to the other locale without dispatch', initial => {
    i18n.setLocale(initial);
    const host = render(downloadableFile({ downloadPolicy: 'denied', capabilities: [] }));
    expect(downloadAction().textContent?.trim()).toBe(locales[initial].download);
    expect(host.querySelector('[data-testid="download-disabled-reason"]')?.textContent).toBe(locales[initial].denied);

    const next = initial === 'en' ? 'ja' : 'en';
    i18n.setLocale(next);
    fixture.detectChanges();
    expect(downloadAction().textContent?.trim()).toBe(locales[next].download);
    expect(host.querySelector('[data-testid="download-disabled-reason"]')?.textContent).toBe(locales[next].denied);
    expect(downloadAction().disabled).toBe(true);
    downloadAction().click();
    expect(requested).toEqual([]);
  });

  it.each([false, true])('keeps the unimplemented override path inert after input replacement (capability=%s)', hasCapability => {
    const host = render(downloadableFile());
    downloadAction().click();
    expect(requested).toEqual(['synthetic-canonical-first']);
    const overrides: { fileId: string; reason: string }[] = [];
    fixture.componentInstance.adminOverrideRequested.subscribe(value => overrides.push(value));

    render(downloadableFile({ scanStatus: 'blocked', downloadPolicy: 'adminOverrideRequired',
      capabilities: hasCapability ? ['adminOverrideBlockedDownload'] : [] }));
    expect(downloadAction().disabled).toBe(true);
    expect(host.querySelector('[data-testid="admin-override-panel"]')).not.toBeNull();
    const reason = host.querySelector<HTMLTextAreaElement>('[data-testid="admin-override-reason"]');
    const action = host.querySelector<HTMLButtonElement>('[data-testid="admin-override-action"]');
    if (hasCapability) {
      if (!reason || !action) throw new Error('Expected the inert override presentation.');
      reason.value = 'Synthetic review reason';
      reason.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
      expect(action.disabled).toBe(true);
      action.click();
    } else {
      expect(reason).toBeNull();
      expect(action).toBeNull();
      expect(host.querySelector('[data-testid="admin-override-no-capability"]')).not.toBeNull();
    }
    downloadAction().click();
    expect(requested).toEqual(['synthetic-canonical-first']);
    expect(overrides).toEqual([]);
  });
});
