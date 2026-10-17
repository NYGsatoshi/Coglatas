import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { routes } from '../../../app.routes';
import { I18nService } from '../../../core/i18n/i18n.service';
import { LoginPageComponent } from './login-page.component';

const renderPage = (): ComponentFixture<LoginPageComponent> => {
  const fixture = TestBed.createComponent(LoginPageComponent);
  fixture.detectChanges();
  return fixture;
};

describe('LoginPageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('configures the first /login route as the login page', () => {
    const loginRoute = routes.find((route) => route.path === 'login');

    expect(loginRoute?.loadComponent).toBeTruthy();
    expect(loginRoute?.component).toBeUndefined();
  });

  it('submits credentials with a CSRF token and does not trim the password', async () => {
    const fixture = renderPage();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    setInput(fixture, '[data-testid="login-email"]', ' user@example.invalid ');
    setInput(fixture, '[data-testid="login-password"]', '  Password123  ');
    click(fixture, '[data-testid="login-submit"]');

    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-login',
      headerName: 'X-CSRF-Token'
    });

    const loginRequest = httpMock.expectOne('/api/auth/login');
    expect(loginRequest.request.method).toBe('POST');
    expect(loginRequest.request.withCredentials).toBe(true);
    expect(loginRequest.request.headers.get('X-CSRF-Token')).toBe('csrf-login');
    expect(loginRequest.request.body).toEqual({
      email: 'user@example.invalid',
      password: '  Password123  '
    });
    loginRequest.flush({
      userId: 'user-a',
      displayName: 'User A',
      email: 'user@example.invalid',
      systemRole: 'TenantUser',
      status: 'Active',
      expiresAt: '2026-07-05T00:00:00Z'
    });

    httpMock.expectOne('/api/tenants/current').flush({
      tenantId: 'tenant-a',
      tenantSlug: 'tenant-a',
      isAvailable: true,
      isPlatformScope: false,
      displayName: 'Tenant A',
      status: 'Active',
      currentUserRole: 'Admin',
      appMode: 'OnPremSingleTenant',
      allowTenantSwitching: true
    });

    await fixture.whenStable();
    expect(navigateSpy).toHaveBeenCalledWith('/workspaces');
  });

  it('updates the source-backed brand copy without replacing entered credentials or submitting', () => {
    const fixture = renderPage();
    const element = fixture.nativeElement as HTMLElement;
    const email = element.querySelector<HTMLInputElement>('[data-testid="login-email"]');
    const password = element.querySelector<HTMLInputElement>('[data-testid="login-password"]');
    setInput(fixture, '[data-testid="login-email"]', 'draft@example.invalid');
    setInput(fixture, '[data-testid="login-password"]', '  unsent password  ');
    expect(element.querySelector('.login-page__description')?.textContent).toContain('あなたのプロジェクトと会話に戻ります。');

    TestBed.inject(I18nService).setLocale('en');
    fixture.detectChanges();

    expect(element.querySelector('#login-brand-title')?.textContent).toBe('Plan. Analyze. Review.');
    expect(element.querySelector('.login-page__description')?.textContent).toBe('Return to your Projects and conversations.');
    expect(element.querySelector('[data-testid="login-email"]')).toBe(email);
    expect(element.querySelector('[data-testid="login-password"]')).toBe(password);
    expect(email?.value).toBe('draft@example.invalid');
    expect(password?.value).toBe('  unsent password  ');
    expect(element.querySelectorAll('button').length).toBe(1);
    TestBed.inject(HttpTestingController).expectNone('/api/security/csrf-token');
    TestBed.inject(HttpTestingController).expectNone('/api/auth/login');
  });

  it('shows a generic error when login is rejected', async () => {
    const fixture = renderPage();

    setInput(fixture, '[data-testid="login-email"]', 'user@example.invalid');
    setInput(fixture, '[data-testid="login-password"]', 'wrong-password');
    click(fixture, '[data-testid="login-submit"]');

    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-login',
      headerName: 'X-CSRF-Token'
    });
    httpMock.expectOne('/api/auth/login').flush({ error: 'Invalid credentials.' }, { status: 401, statusText: 'Unauthorized' });

    fixture.detectChanges();
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('メールアドレスまたはパスワードを確認してください。');
  });
});

function setInput(fixture: ComponentFixture<LoginPageComponent>, selector: string, value: string): void {
  const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(selector);
  if (!input) {
    throw new Error(`Missing input: ${selector}`);
  }

  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function click(fixture: ComponentFixture<LoginPageComponent>, selector: string): void {
  const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(selector);
  if (!button) {
    throw new Error(`Missing button: ${selector}`);
  }

  button.click();
  fixture.detectChanges();
}
