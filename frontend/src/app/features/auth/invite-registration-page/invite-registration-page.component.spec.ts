import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { routes } from '../../../app.routes';
import {
  COGLATAS_AUTH_SESSION_MOCK,
  ANONYMOUS_AUTH_SESSION
} from '../../../core/auth/auth-session.facade';
import { authSessionInterceptor } from '../../../core/auth/auth-session.interceptor';
import { COGLATAS_INVITE_REGISTRATION_SCENARIO, InviteRegistrationFacade } from '../invite-registration.facade';
import { INVITE_REGISTRATION_SCENARIOS } from '../invite-registration.mock';
import { InviteRegistrationScenario } from '../invite-registration.types';
import { InviteRegistrationFormComponent } from '../invite-registration-form/invite-registration-form.component';
import { InviteRegistrationPageComponent } from './invite-registration-page.component';

const routeWithToken = (token: string | null) => ({
  snapshot: {
    queryParamMap: convertToParamMap(token ? { token } : {})
  }
});

const renderPage = async (
  scenario: InviteRegistrationScenario = INVITE_REGISTRATION_SCENARIOS.defaultValid,
  token: string | null = 'safe-test-token'
): Promise<ComponentFixture<InviteRegistrationPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [InviteRegistrationPageComponent],
    providers: [
      provideRouter(routes),
      { provide: COGLATAS_INVITE_REGISTRATION_SCENARIO, useValue: scenario },
      { provide: ActivatedRoute, useValue: routeWithToken(token) }
    ]
  }).compileComponents();

  const fixture = TestBed.createComponent(InviteRegistrationPageComponent);
  fixture.detectChanges();
  return fixture;
};

const renderPageWithApi = async (
  token: string | null = 'safe-api-token'
): Promise<{ fixture: ComponentFixture<InviteRegistrationPageComponent>; httpMock: HttpTestingController }> => {
  await TestBed.configureTestingModule({
    imports: [InviteRegistrationPageComponent],
    providers: [
      provideRouter(routes),
      provideHttpClient(withInterceptors([authSessionInterceptor])),
      provideHttpClientTesting(),
      {
        provide: COGLATAS_AUTH_SESSION_MOCK,
        useValue: ANONYMOUS_AUTH_SESSION
      },
      { provide: ActivatedRoute, useValue: routeWithToken(token) }
    ]
  }).compileComponents();

  const fixture = TestBed.createComponent(InviteRegistrationPageComponent);
  fixture.detectChanges();
  return { fixture, httpMock: TestBed.inject(HttpTestingController) };
};

const textContent = (fixture: ComponentFixture<InviteRegistrationPageComponent>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

const getForm = (fixture: ComponentFixture<InviteRegistrationPageComponent>): InviteRegistrationFormComponent =>
  fixture.debugElement.query(By.directive(InviteRegistrationFormComponent)).componentInstance as InviteRegistrationFormComponent;

describe('InviteRegistrationPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('configures the /app/register/invite Angular route segment', () => {
    expect(routes.some((route) => route.path === 'register/invite')).toBe(true);
  });

  it('shows a safe unusable-link state when the token is absent', async () => {
    const fixture = await renderPage(INVITE_REGISTRATION_SCENARIOS.defaultValid, null);

    expect(textContent(fixture)).toContain('This invite link is incomplete. Ask for a new invite URL.');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="invite-registration-form"]')).toBeNull();
  });

  it('does not render the token value', async () => {
    const token = 'SENSITIVE-INVITE-TOKEN-123';
    const fixture = await renderPage(INVITE_REGISTRATION_SCENARIOS.defaultValid, token);

    expect((fixture.nativeElement as HTMLElement).innerHTML).not.toContain(token);
    expect(textContent(fixture)).not.toContain(token);
  });

  it('renders email as display-only', async () => {
    const fixture = await renderPage();

    const email = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('[data-testid="invite-email"]');
    expect(email?.readOnly).toBe(true);
    expect(email?.value).toBe('mock-invitee@example.invalid');
  });

  it('validates a real invite token and renders the registration form', async () => {
    const { fixture, httpMock } = await renderPageWithApi('safe-api-token');

    httpMock.expectOne('/api/invites/validate?token=safe-api-token').flush({
      valid: true,
      email: 'new-user@example.invalid',
      role: 'Member',
      tenantName: 'Coglatas Portal',
      workspaceName: 'Default Workspace',
      expiresAt: '2026-07-13T00:00:00Z'
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="invite-registration-form"]')).not.toBeNull();
    expect(textContent(fixture)).toContain('new-user@example.invalid');
    expect(textContent(fixture)).toContain('Member');
    expect(textContent(fixture)).toContain('Default Workspace');
    httpMock.verify();
  });

  it('shows a clear unusable-link state for an invalid token', async () => {
    const { fixture, httpMock } = await renderPageWithApi('bad-token');

    httpMock.expectOne('/api/invites/validate?token=bad-token').flush(
      {
        title: 'Invite validation failed.',
        detail: 'Invite is invalid.'
      },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();

    const panel = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="invite-token-state-panel"]');
    expect(panel?.getAttribute('data-status')).toBe('invalid');
    expect(textContent(fixture)).toContain('Invite is invalid.');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="invite-registration-form"]')).toBeNull();
    httpMock.verify();
  });

  it('shows distinct expired, revoked, and already-used token states', async () => {
    const expired = await renderPageWithApi('expired-token');
    expired.httpMock.expectOne('/api/invites/validate?token=expired-token').flush(
      { detail: 'Invite has expired.' },
      { status: 400, statusText: 'Bad Request' }
    );
    expired.fixture.detectChanges();
    expect(
      (expired.fixture.nativeElement as HTMLElement)
        .querySelector('[data-testid="invite-token-state-panel"]')
        ?.getAttribute('data-status')
    ).toBe('expired');
    expired.httpMock.verify();

    TestBed.resetTestingModule();
    const revoked = await renderPageWithApi('revoked-token');
    revoked.httpMock.expectOne('/api/invites/validate?token=revoked-token').flush(
      { detail: 'Invite was revoked.' },
      { status: 400, statusText: 'Bad Request' }
    );
    revoked.fixture.detectChanges();
    expect(
      (revoked.fixture.nativeElement as HTMLElement)
        .querySelector('[data-testid="invite-token-state-panel"]')
        ?.getAttribute('data-status')
    ).toBe('revoked');
    revoked.httpMock.verify();

    TestBed.resetTestingModule();
    const used = await renderPageWithApi('used-token');
    used.httpMock.expectOne('/api/invites/validate?token=used-token').flush(
      { detail: 'Invite has already been used.' },
      { status: 400, statusText: 'Bad Request' }
    );
    used.fixture.detectChanges();
    expect(
      (used.fixture.nativeElement as HTMLElement)
        .querySelector('[data-testid="invite-token-state-panel"]')
        ?.getAttribute('data-status')
    ).toBe('alreadyAccepted');
    used.httpMock.verify();
  });

  it('accepts a valid invite, bootstraps the authenticated session, and navigates to workspaces', async () => {
    const { fixture, httpMock } = await renderPageWithApi('safe-api-token');
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    httpMock.expectOne('/api/invites/validate?token=safe-api-token').flush({
      valid: true,
      email: 'new-user@example.invalid',
      role: 'Member',
      tenantName: 'Coglatas Portal',
      workspaceName: 'Default Workspace',
      expiresAt: '2026-07-13T00:00:00Z'
    });
    fixture.detectChanges();

    const form = getForm(fixture);
    form.form.setValue({
      displayName: 'New User',
      password: 'Password123',
      confirmPassword: 'Password123'
    });
    form.submit();

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-register',
      headerName: 'X-CSRF-Token'
    });

    const acceptRequest = httpMock.expectOne('/api/invites/accept');
    expect(acceptRequest.request.withCredentials).toBe(true);
    expect(acceptRequest.request.headers.get('X-CSRF-Token')).toBe('csrf-register');
    expect(acceptRequest.request.body).toEqual({
      token: 'safe-api-token',
      displayName: 'New User',
      password: 'Password123'
    });
    acceptRequest.flush({ ok: true });

    httpMock.expectOne('/api/auth/me').flush({
      userId: 'user-a',
      displayName: 'New User',
      email: 'new-user@example.invalid',
      systemRole: 'User',
      status: 'Active',
      capabilities: ['workspace:view'],
      currentWorkspace: {
        id: 'workspace-a',
        name: 'Workspace A'
      },
      workspaces: [
        {
          id: 'workspace-a',
          name: 'Workspace A'
        }
      ]
    });
    httpMock.expectOne('/api/tenants/current').flush({
      tenantId: 'tenant-a',
      tenantSlug: 'tenant-a',
      isAvailable: true,
      isPlatformScope: false,
      displayName: 'Tenant A',
      status: 'Active',
      currentUserRole: 'Member',
      appMode: 'SaaS',
      allowTenantSwitching: true
    });
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith('/workspaces');
    expect(TestBed.inject(InviteRegistrationFacade).bootstrapActions()).toEqual([
      'clearAnonymousState',
      'fetchCurrentUser',
      'fetchCurrentTenant',
      'fetchNavigation',
      'fetchCsrfToken',
      'navigateTargetWorkspace'
    ]);
    httpMock.verify();
  });

  it('does not create fake signed-in UI when invite acceptance fails', async () => {
    const { fixture, httpMock } = await renderPageWithApi('safe-api-token');
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    httpMock.expectOne('/api/invites/validate?token=safe-api-token').flush({
      valid: true,
      email: 'new-user@example.invalid',
      role: 'Member',
      tenantName: 'Coglatas Portal',
      workspaceName: 'Default Workspace',
      expiresAt: '2026-07-13T00:00:00Z'
    });
    fixture.detectChanges();

    const form = getForm(fixture);
    form.form.setValue({
      displayName: 'New User',
      password: 'Password123',
      confirmPassword: 'Password123'
    });
    form.submit();

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-register',
      headerName: 'X-CSRF-Token'
    });

    httpMock.expectOne('/api/invites/accept').flush(
      {
        detail: 'Invite was revoked.'
      },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Invite was revoked.');
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(TestBed.inject(InviteRegistrationFacade).bootstrapActions()).toEqual([]);
    httpMock.expectNone('/api/auth/me');
    httpMock.verify();
  });

  it('does not trim password before submit model construction', async () => {
    const fixture = await renderPage();
    const form = getForm(fixture);

    form.form.setValue({
      displayName: ' Mock Invitee ',
      password: '  mock-password  ',
      confirmPassword: '  mock-password  '
    });
    form.submit();
    fixture.detectChanges();

    expect(TestBed.inject(InviteRegistrationFacade).submittedModel()?.password).toBe('  mock-password  ');
    expect(TestBed.inject(InviteRegistrationFacade).submittedModel()?.displayName).toBe('Mock Invitee');
  });

  it('blocks submit when confirm password does not match', async () => {
    const fixture = await renderPage();
    const form = getForm(fixture);

    form.form.setValue({
      displayName: 'Mock Invitee',
      password: 'mock-password-a',
      confirmPassword: 'mock-password-b'
    });
    form.submit();
    fixture.detectChanges();

    expect(TestBed.inject(InviteRegistrationFacade).submittedModel()).toBeNull();
    expect(textContent(fixture)).toContain('Passwords must match.');
  });

  it('does not render registration submit in legacy backend transaction gated state', async () => {
    const fixture = await renderPage(INVITE_REGISTRATION_SCENARIOS.backendTransactionGated);

    const submit = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="invite-submit"]');
    expect(submit).toBeNull();
    expect(textContent(fixture)).toContain('This invite flow is blocked by an older backend contract.');
  });

  it('does not reveal target tenant or workspace details in invalid, expired, or revoked states', async () => {
    const invalid = await renderPage(INVITE_REGISTRATION_SCENARIOS.invalidToken);
    expect(textContent(invalid)).not.toContain('Mock Tenant');
    expect(textContent(invalid)).not.toContain('Mock Workspace');

    TestBed.resetTestingModule();
    const expired = await renderPage(INVITE_REGISTRATION_SCENARIOS.expiredToken);
    expect(textContent(expired)).not.toContain('Mock Tenant');
    expect(textContent(expired)).not.toContain('Mock Workspace');

    TestBed.resetTestingModule();
    const revoked = await renderPage(INVITE_REGISTRATION_SCENARIOS.revokedToken);
    expect(textContent(revoked)).not.toContain('Mock Tenant');
    expect(textContent(revoked)).not.toContain('Mock Workspace');
  });

  it('keeps mobile layout free of hidden alternate actions', async () => {
    const fixture = await renderPage();
    (fixture.nativeElement as HTMLElement).style.width = '320px';
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="invite-admin-action"]')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="invite-login-action"]')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('button').length).toBe(1);
  });
});
