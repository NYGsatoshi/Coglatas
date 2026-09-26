import { TestBed } from '@angular/core/testing';

import { I18nService } from './i18n.service';

describe('I18nService', () => {
  beforeEach(() => {
    window.localStorage.removeItem('coglatas.locale');
  });

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('uses Japanese by default and persists an explicit English selection', () => {
    const service = TestBed.inject(I18nService);
    TestBed.tick();

    expect(service.locale()).toBe('ja');
    expect(service.translate('account.eyebrow')).toBe('アカウント');
    expect(document.documentElement.lang).toBe('ja');

    service.setLocale('en');
    TestBed.tick();

    expect(service.translate('account.eyebrow')).toBe('Account');
    expect(document.documentElement.lang).toBe('en');
    expect(window.localStorage.getItem('coglatas.locale')).toBe('en');
  });

  it('restores a previously saved language preference', () => {
    window.localStorage.setItem('coglatas.locale', 'en');

    const service = TestBed.inject(I18nService);
    TestBed.tick();

    expect(service.locale()).toBe('en');
    expect(service.translate('login.title')).toBe('Sign in');
  });

  it('supplies Japanese Files, API error, and grid labels from the same locale source', () => {
    const service = TestBed.inject(I18nService);
    TestBed.tick();

    expect(service.translate('files.actions.download')).toBe('ダウンロード');
    expect(service.fileScanStatusLabel('blocked')).toBe('ブロック済み');
    expect(service.fileContentTypeLabel('application/pdf')).toBe('PDF');
    expect(service.fileContentTypeLabel('application/pdf; charset=utf-8')).toBe('PDF');
    expect(service.fileContentTypeLabel('application/zip')).toBe('アーカイブ');
    expect(service.apiErrorMessage({ httpStatus: 403, message: 'Forbidden' })).toBe('この操作を行う権限がありません。');
    expect(service.agGridLocaleText()['noRowsToShow']).toBe('表示する行がありません');
    expect(service.agGridLocaleText()['ariaRowToggleSelection']).toBe('行の選択を切り替え');
    expect(service.agGridLocaleText()['ariaSortableColumn']).toBe('Enterキーを押して並べ替え');
    expect(service.agGridLocaleText()['pageSizeSelectorLabel']).toBe('1ページあたりの件数');
    expect(service.syncfusionLocale()).toBe('ja');
  });
});
