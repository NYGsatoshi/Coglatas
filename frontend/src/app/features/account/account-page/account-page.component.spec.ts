import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { COGLATAS_ACCOUNT_MOCK } from '../account.facade';
import { ACCOUNT_MOCK_SCENARIOS } from '../account.mock';
import { AccountMockScenario } from '../account.types';
import { AccountPageComponent } from './account-page.component';

const renderAccount = async (
  scenario: AccountMockScenario = ACCOUNT_MOCK_SCENARIOS.default
): Promise<ComponentFixture<AccountPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [AccountPageComponent],
    providers: [{ provide: COGLATAS_ACCOUNT_MOCK, useValue: scenario }]
  }).compileComponents();

  const fixture = TestBed.createComponent(AccountPageComponent);
  fixture.detectChanges();
  return fixture;
};

const renderLiveAccount = async (): Promise<{
  fixture: ComponentFixture<AccountPageComponent>;
  httpMock: HttpTestingController;
}> => {
  await TestBed.configureTestingModule({
    imports: [AccountPageComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()]
  }).compileComponents();

  const fixture = TestBed.createComponent(AccountPageComponent);
  fixture.detectChanges();

  const httpMock = TestBed.inject(HttpTestingController);
  httpMock.expectOne('/api/auth/me').flush({
    displayName: 'Current User',
    email: 'current@example.test',
    systemRole: 'TenantUser',
    status: 'Active'
  });
  fixture.detectChanges();

  return { fixture, httpMock };
};

const textContent = (fixture: ComponentFixture<AccountPageComponent>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

const query = <T extends HTMLElement>(fixture: ComponentFixture<AccountPageComponent>, selector: string): T | null =>
  (fixture.nativeElement as HTMLElement).querySelector<T>(selector);

const updateInput = (fixture: ComponentFixture<AccountPageComponent>, selector: string, value: string): void => {
  const input = query<HTMLInputElement>(fixture, selector);
  if (!input) {
    throw new Error(`Missing input ${selector}`);
  }

  input.value = value;
  input.dispatchEvent(new Event('input'));
};

describe('AccountPageComponent', () => {
  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('renders own email on the self account page', async () => {
    const fixture = await renderAccount();

    expect(query(fixture, '[data-testid="own-email"]')?.textContent).toContain('self.account@example.test');
  });

  it('does not render other users email addresses', async () => {
    const fixture = await renderAccount();

    expect(textContent(fixture)).not.toContain('other.member@example.test');
    expect(textContent(fixture)).not.toContain('hidden.other@example.test');
  });

  it('does not render session secrets or tokens', async () => {
    const fixture = await renderAccount();

    expect(textContent(fixture)).not.toContain('mock-refresh-token-should-not-render');
    expect(textContent(fixture)).not.toContain('mock-auth-cookie-should-not-render');
    expect(textContent(fixture)).not.toContain('mock-device-fingerprint-should-not-render');
    expect(textContent(fixture)).not.toContain('203.0.113.10');
  });

  it('does not trim password values in the submit model', async () => {
    const fixture = await renderAccount();

    updateInput(fixture, '[data-testid="current-password"]', ' current password ');
    updateInput(fixture, '[data-testid="new-password"]', ' new password ');
    updateInput(fixture, '[data-testid="confirm-new-password"]', ' new password ');
    query<HTMLButtonElement>(fixture, 'button[type="submit"]')?.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.lastPasswordSubmit()).toEqual({
      currentPassword: ' current password ',
      newPassword: ' new password ',
      confirmNewPassword: ' new password '
    });
  });

  it('does not show password success or clear fields when the backend rejects the change', async () => {
    const { fixture, httpMock } = await renderLiveAccount();

    updateInput(fixture, '[data-testid="current-password"]', ' current password ');
    updateInput(fixture, '[data-testid="new-password"]', ' new password ');
    updateInput(fixture, '[data-testid="confirm-new-password"]', ' new password ');
    query<HTMLButtonElement>(fixture, 'button[type="submit"]')?.click();
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="password-change-pending"]')).not.toBeNull();
    expect(query(fixture, '[data-testid="password-change-success"]')).toBeNull();

    const request = httpMock.expectOne('/api/auth/change-password');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      currentPassword: ' current password ',
      newPassword: ' new password '
    });
    request.flush({ error: 'Current password is invalid.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="password-change-success"]')).toBeNull();
    expect(query(fixture, '[data-testid="password-change-failure"]')).not.toBeNull();
    expect(query<HTMLInputElement>(fixture, '[data-testid="current-password"]')?.value).toBe(' current password ');
    expect(query<HTMLInputElement>(fixture, '[data-testid="new-password"]')?.value).toBe(' new password ');
    expect(query<HTMLInputElement>(fixture, '[data-testid="confirm-new-password"]')?.value).toBe(' new password ');
    httpMock.verify();
  });

  it('shows password success and clears fields only after backend success', async () => {
    const { fixture, httpMock } = await renderLiveAccount();

    updateInput(fixture, '[data-testid="current-password"]', 'current');
    updateInput(fixture, '[data-testid="new-password"]', 'next');
    updateInput(fixture, '[data-testid="confirm-new-password"]', 'next');
    query<HTMLButtonElement>(fixture, 'button[type="submit"]')?.click();
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="password-change-success"]')).toBeNull();

    httpMock.expectOne('/api/auth/change-password').flush({ status: 'OK' });
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="password-change-success"]')).not.toBeNull();
    expect(query<HTMLInputElement>(fixture, '[data-testid="current-password"]')?.value).toBe('');
    expect(query<HTMLInputElement>(fixture, '[data-testid="new-password"]')?.value).toBe('');
    expect(query<HTMLInputElement>(fixture, '[data-testid="confirm-new-password"]')?.value).toBe('');
    httpMock.verify();
  });

  it('blocks submit when confirm password does not match', async () => {
    const fixture = await renderAccount();

    updateInput(fixture, '[data-testid="current-password"]', 'current');
    updateInput(fixture, '[data-testid="new-password"]', 'new-one');
    updateInput(fixture, '[data-testid="confirm-new-password"]', 'new-two');
    query<HTMLButtonElement>(fixture, 'button[type="submit"]')?.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.lastPasswordSubmit()).toBeNull();
    expect(textContent(fixture)).toContain('新しいパスワードが一致しません。');
  });

  it('hides revoke action when capability is absent', async () => {
    const fixture = await renderAccount(ACCOUNT_MOCK_SCENARIOS.sessionRevokeUnavailable);

    expect(textContent(fixture)).toContain('このセッションを終了');
    expect(query<HTMLButtonElement>(fixture, '.sessions__revoke')).toBeNull();
    expect(query(fixture, '[data-testid="session-revoke-unavailable"]')).not.toBeNull();
  });

  it('mobile layout does not expose hidden actions', async () => {
    const fixture = await renderAccount(ACCOUNT_MOCK_SCENARIOS.sessionRevokeUnavailable);

    (fixture.nativeElement as HTMLElement).style.width = '320px';
    fixture.detectChanges();

    expect(query<HTMLButtonElement>(fixture, '.sessions__revoke')).toBeNull();
    expect(textContent(fixture)).not.toContain('mock-token-never-render');
  });

  it('switches the Account screen to English and persists the selection', async () => {
    const fixture = await renderAccount();
    const languageSelector = query<HTMLSelectElement>(fixture, '[data-testid="display-language"]');
    if (!languageSelector) {
      throw new Error('Missing display language selector');
    }

    languageSelector.value = 'en';
    languageSelector.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(textContent(fixture)).toContain('Language');
    expect(textContent(fixture)).toContain('Change password');
    expect(document.documentElement.lang).toBe('en');
    expect(window.localStorage.getItem('coglatas.locale')).toBe('en');
  });
});
