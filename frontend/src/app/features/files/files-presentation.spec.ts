import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { I18nService } from '../../core/i18n/i18n.service';
import { fileErrorMessage, fileMessage, renderFileMessage } from './files-presentation';

describe('Files presentation provenance', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'ja'));
  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('ignores a stale translation key when custom text replaces a tagged message', () => {
    const i18n = TestBed.inject(I18nService);
    const tagged = fileMessage(i18n, 'files.upload.progress');
    const replacement = { ...tagged, message: 'Supplied status 日本語' };
    i18n.setLocale('en');
    expect(renderFileMessage(i18n, tagged)).toBe('Uploading file to backend.');
    expect(renderFileMessage(i18n, replacement)).toBe('Supplied status 日本語');
  });

  it('does not retain a translated denial after custom text or an empty value replaces it', () => {
    const i18n = TestBed.inject(I18nService);
    const tagged = fileErrorMessage(i18n, new HttpErrorResponse({ status: 403 }), 'files.search.unavailable');
    i18n.setLocale('en');
    expect(renderFileMessage(i18n, { ...tagged, message: 'Supplied error 原文' })).toBe('Supplied error 原文');
    expect(renderFileMessage(i18n, { ...tagged, message: '' })).toBe('');
    expect(renderFileMessage(i18n, { ...tagged, message: undefined })).toBe('');
  });
});
