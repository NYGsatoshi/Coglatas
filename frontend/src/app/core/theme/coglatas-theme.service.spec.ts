import { TestBed } from '@angular/core/testing';

import { CoglatasThemeService } from './coglatas-theme.service';

describe('CoglatasThemeService', () => {
  const originalMatchMedia = window.matchMedia;

  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-coglatas-theme');
    document.documentElement.removeAttribute('data-coglatas-density');
    document.documentElement.style.removeProperty('color-scheme');
    document.body.classList.remove('e-dark-mode');
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    document.body.classList.remove('e-dark-mode');
    Object.defineProperty(window, 'matchMedia', { configurable: true, value: originalMatchMedia });
  });

  const setMedia = (matches: boolean): void => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({ matches, addEventListener: vi.fn() })
    });
  };

  it('uses dark as the default and exposes it on the root and vendor theme boundary', () => {
    const service = TestBed.inject(CoglatasThemeService);
    expect(service.theme()).toBe('dark');
    expect(document.documentElement.dataset['coglatasTheme']).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    expect(document.body.classList.contains('e-dark-mode')).toBe(true);
  });

  it('uses OS light preference before an explicit choice', () => {
    setMedia(true);
    const service = TestBed.inject(CoglatasThemeService);
    expect(service.theme()).toBe('light');
    expect(document.body.classList.contains('e-dark-mode')).toBe(false);
  });

  it('uses an explicit local preference over OS preference and ignores invalid values', () => {
    localStorage.setItem('coglatas.ui.theme.v1', 'dark');
    setMedia(true);
    expect(TestBed.inject(CoglatasThemeService).theme()).toBe('dark');

    TestBed.resetTestingModule();
    localStorage.setItem('coglatas.ui.theme.v1', 'invalid');
    TestBed.configureTestingModule({});
    expect(TestBed.inject(CoglatasThemeService).theme()).toBe('light');
  });

  it('switches and persists the theme without navigation', () => {
    setMedia(true);
    const service = TestBed.inject(CoglatasThemeService);
    const path = location.pathname;

    service.toggleTheme();

    expect(service.theme()).toBe('dark');
    expect(localStorage.getItem('coglatas.ui.theme.v1')).toBe('dark');
    expect(document.documentElement.dataset['coglatasTheme']).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    expect(document.body.classList.contains('e-dark-mode')).toBe(true);
    expect(service.density()).toBe('comfortable');
    expect(document.documentElement.dataset['coglatasDensity']).toBe('comfortable');
    expect(location.pathname).toBe(path);

    service.toggleTheme();
    expect(service.theme()).toBe('light');
    expect(document.body.classList.contains('e-dark-mode')).toBe(false);
  });
});
