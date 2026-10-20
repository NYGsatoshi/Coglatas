import { ComponentFixture, TestBed } from '@angular/core/testing';

import { I18nService } from '../../../core/i18n/i18n.service';
import { fileMessage } from '../files-presentation';
import { FileQuotaViewModel } from '../files.types';
import { FileQuotaStateComponent } from './file-quota-state.component';

describe('FileQuotaStateComponent', () => {
  let fixture: ComponentFixture<FileQuotaStateComponent>;
  let i18n: I18nService;

  beforeEach(async () => {
    window.localStorage.removeItem('coglatas.locale');
    await TestBed.configureTestingModule({ imports: [FileQuotaStateComponent] }).compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLocale('en');
    fixture = TestBed.createComponent(FileQuotaStateComponent);
  });

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  const render = (quota: FileQuotaViewModel): HTMLElement => {
    fixture.componentRef.setInput('quota', quota);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const unavailable = (): FileQuotaViewModel => ({
    state: 'available', usedBytes: 0, limitBytes: 0,
    ...fileMessage(i18n, 'files.quota.unavailable'),
  });

  it('shows an unavailable status without a fabricated usage or capacity', () => {
    const element = render(unavailable());

    expect(element.textContent?.trim()).toBe('Storage usage unavailable.');
    expect(element.querySelector('progress')).toBeNull();
    expect(element.querySelector('strong')).toBeNull();
    expect(element.textContent).not.toContain('MVP0');
  });

  it('updates the unavailable product label when the display language changes', () => {
    const element = render(unavailable());
    i18n.setLocale('ja');
    fixture.detectChanges();

    expect(element.textContent?.trim()).toBe('ストレージ使用量を表示できません。');
    expect(element.textContent).not.toContain('MVP0');
  });

  it('preserves known usage, capacity, progress and the supplied status', () => {
    const element = render({
      state: 'available', usedBytes: 3 * 1024 * 1024, limitBytes: 5 * 1024 * 1024,
      message: 'Storage remains available.',
    });
    const progress = element.querySelector('progress');

    expect(element.querySelector('strong')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('3 MB / 5 MB');
    expect(progress?.value).toBe(3 * 1024 * 1024);
    expect(progress?.max).toBe(5 * 1024 * 1024);
    expect(progress?.getAttribute('aria-label')).toBe('Storage capacity');
    expect(element.textContent).toContain('Storage remains available.');
  });
});
