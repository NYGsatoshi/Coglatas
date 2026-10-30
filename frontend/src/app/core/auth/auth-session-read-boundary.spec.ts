import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ActiveWorkspaceFacade } from '../workspace/active-workspace.facade';
import { AuthSessionFacade } from './auth-session.facade';
import { authSessionInterceptor } from './auth-session.interceptor';

const user = (userId: string) => ({
  userId, displayName: userId, email: `${userId}@example.invalid`, systemRole: 'User', status: 'Active',
  capabilities: userId === 'fresh-user' ? ['files:view'] : ['audit:view'], currentWorkspace: null, workspaces: [],
});
const tenant = (tenantId: string) => ({
  tenantId, tenantSlug: tenantId, isAvailable: true, isPlatformScope: false,
  displayName: tenantId, status: 'Active', currentUserRole: 'Member', appMode: 'OnPremSingleTenant', allowTenantSwitching: false,
});

describe('Auth session read boundary ordering', () => {
  let auth: AuthSessionFacade;
  let http: HttpTestingController;
  let client: HttpClient;
  let activeWorkspace: ActiveWorkspaceFacade;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authSessionInterceptor])), provideHttpClientTesting(), provideRouter([]),
    ] });
    auth = TestBed.inject(AuthSessionFacade);
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  function beginLogin() {
    const result = firstValueFrom(auth.login('fresh-user@example.invalid', 'synthetic-password'));
    http.expectOne('/api/security/csrf-token').flush({ token: 'synthetic-csrf', headerName: 'X-CSRF-Token' });
    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    expect(login.request.headers.get('X-CSRF-Token')).toBe('synthetic-csrf');
    return { result, login };
  }

  async function freshLogin(): Promise<void> {
    const { result, login } = beginLogin();
    login.flush(user('fresh-user'));
    http.expectOne('/api/tenants/current').flush(tenant('fresh-tenant'));
    expect((await result).currentUser?.userId).toBe('fresh-user');
  }

  function expectFreshSession(): void {
    expect.soft(auth.isAuthenticated()).toBe(true);
    expect.soft(auth.currentUser()?.userId).toBe('fresh-user');
    expect.soft(auth.currentTenant()?.tenantId).toBe('fresh-tenant');
    expect.soft(auth.session().capabilities).toEqual(['files:view']);
  }

  it.each([
    ['bootstrap', 'terminal401'], ['bootstrap', 'localLogout'],
    ['currentUser', 'terminal401'], ['currentUser', 'localLogout'],
    ['sessionContext', 'terminal401'], ['sessionContext', 'localLogout'],
  ] as const)('does not revive a cleared session from late %s success after %s', async (kind, boundary) => {
    const result = firstValueFrom(kind === 'bootstrap' ? auth.bootstrap()
      : kind === 'currentUser' ? auth.refreshCurrentUser() : auth.refreshSessionContext());
    const read = http.expectOne(kind === 'sessionContext' ? '/api/auth/status' : '/api/auth/me');
    activeWorkspace.setActiveWorkspace({ id: 'old-workspace', label: 'Old Workspace' });
    if (boundary === 'terminal401') auth.handleTerminal401(); else auth.logoutLocally();

    read.flush(kind === 'sessionContext' ? { isAuthenticated: true, user: user('old-user') } : user('old-user'));
    // Consume any erroneous follow-up so both implementations settle normally.
    const unexpectedHydration = http.match('/api/tenants/current');
    unexpectedHydration.forEach(request => request.flush(tenant('old-tenant')));
    const snapshot = await result;

    expect.soft(unexpectedHydration).toHaveLength(0);
    expect.soft(snapshot?.status).toBe(boundary === 'terminal401' ? 'expired' : 'anonymous');
    expect.soft(auth.isAuthenticated()).toBe(false);
    expect.soft(auth.currentUser()).toBeNull();
    expect.soft(auth.currentTenant()).toBeNull();
    expect.soft(auth.session().capabilities).toEqual([]);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
  });

  it('keeps a newer bootstrap loading owner while an older request settles', async () => {
    const oldResult = firstValueFrom(auth.bootstrap());
    const oldRead = http.expectOne('/api/auth/me');
    auth.clearSessionState('expired');
    const freshResult = firstValueFrom(auth.bootstrap());
    const freshRead = http.expectOne('/api/auth/me');
    expect(auth.loading()).toBe(true);

    oldRead.flush(user('old-user'));
    const unexpectedHydration = http.match('/api/tenants/current');
    unexpectedHydration.forEach(request => request.flush(tenant('old-tenant')));
    await oldResult;
    expect.soft(unexpectedHydration).toHaveLength(0);
    expect.soft(auth.loading()).toBe(true);

    freshRead.flush(user('fresh-user'));
    http.expectOne('/api/tenants/current').flush(tenant('fresh-tenant'));
    expect((await freshResult).isAuthenticated).toBe(true);
    expectFreshSession();
    expect(auth.loading()).toBe(false);
  });

  it.each(['success', '401', '403'] as const)('does not hydrate or clear a fresh login from an old tenant read (%s)', async outcome => {
    const oldResult = firstValueFrom(auth.refreshCurrentTenant());
    const oldRead = http.expectOne('/api/tenants/current');
    auth.logoutLocally();
    await freshLogin();

    if (outcome === 'success') oldRead.flush(tenant('old-tenant'));
    else oldRead.flush({ error: 'Old admission no longer valid' }, { status: Number(outcome), statusText: 'Denied' });

    expect.soft(await oldResult).toBeNull();
    expectFreshSession();
  });

  it('ignores an obsolete anonymous status response after a fresh authenticated login', async () => {
    const oldResult = firstValueFrom(auth.refreshSessionContext());
    const oldRead = http.expectOne('/api/auth/status');
    await freshLogin();

    oldRead.flush({ isAuthenticated: false, user: null });

    expect.soft((await oldResult)?.currentUser?.userId).toBe('fresh-user');
    expectFreshSession();
  });

  it('does not let an obsolete bootstrap error clear a newer authenticated login', async () => {
    const oldResult = firstValueFrom(auth.bootstrap());
    const oldRead = http.expectOne('/api/auth/me');
    const logged = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      await freshLogin();
      oldRead.flush({ error: 'Old request unavailable' }, { status: 500, statusText: 'Unavailable' });
      expect.soft((await oldResult).currentUser?.userId).toBe('fresh-user');
      expectFreshSession();
      expect(logged).not.toHaveBeenCalled();
    } finally { logged.mockRestore(); }
  });

  it('preserves the fresh login through a stale interceptor probe 401 and still fails closed on a new terminal 401', async () => {
    const errors: unknown[] = [];
    client.get('/api/protected-old').subscribe({ error: error => errors.push(error) });
    http.expectOne('/api/protected-old').flush({ error: 'Expired' }, { status: 401, statusText: 'Unauthorized' });
    const oldProbe = http.expectOne('/api/auth/me');
    await freshLogin();

    oldProbe.flush({ error: 'Old admission expired' }, { status: 401, statusText: 'Unauthorized' });

    expect.soft(errors).toHaveLength(1);
    expectFreshSession();
    client.get('/api/protected-fresh').subscribe({ error: error => errors.push(error) });
    http.expectOne('/api/protected-fresh').flush({ error: 'Expired' }, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/me').flush({ error: 'Expired' }, { status: 401, statusText: 'Unauthorized' });
    expect(errors).toHaveLength(2);
    expect(auth.session().status).toBe('expired');
    expect(auth.currentUser()).toBeNull();
    expect(auth.currentTenant()).toBeNull();
    expect(auth.session().capabilities).toEqual([]);
  });

  it('does not restore tenant metadata when bootstrap hydration returns after a terminal clear', async () => {
    const result = firstValueFrom(auth.bootstrap());
    http.expectOne('/api/auth/me').flush(user('old-user'));
    const hydration = http.expectOne('/api/tenants/current');
    auth.clearSessionState('expired');

    hydration.flush(tenant('old-tenant'));

    expect((await result).status).toBe('expired');
    expect(auth.currentUser()).toBeNull();
    expect(auth.currentTenant()).toBeNull();
    expect(auth.session().capabilities).toEqual([]);
  });

  it('does not let an obsolete status probe 403 clear a newer authenticated login', async () => {
    const result = firstValueFrom(auth.refreshSessionContext());
    const read = http.expectOne('/api/auth/status');
    await freshLogin();

    read.flush({ error: 'Old admission denied' }, { status: 403, statusText: 'Forbidden' });

    expect.soft((await result)?.currentUser?.userId).toBe('fresh-user');
    expectFreshSession();
  });

  it('does not dispatch a login after its pending CSRF lookup was superseded by a terminal clear', async () => {
    const result = firstValueFrom(auth.login('fresh-user@example.invalid', 'synthetic-password'));
    const csrf = http.expectOne('/api/security/csrf-token');
    auth.clearSessionState('expired');

    csrf.flush({ token: 'synthetic-csrf', headerName: 'X-CSRF-Token' });
    const obsoleteWrites = http.match('/api/auth/login');
    obsoleteWrites.forEach(request => request.flush(user('old-user')));
    const unexpectedHydration = http.match('/api/tenants/current');
    unexpectedHydration.forEach(request => request.flush(tenant('old-tenant')));
    const snapshot = await result;

    expect.soft(obsoleteWrites).toHaveLength(0);
    expect.soft(unexpectedHydration).toHaveLength(0);
    expect.soft(snapshot.status).toBe('expired');
    expect.soft(auth.currentUser()).toBeNull();
    expect(auth.currentTenant()).toBeNull();
  });

  it('does not apply a dispatched login response after a later terminal clear', async () => {
    const { result, login } = beginLogin();
    auth.clearSessionState('expired');

    login.flush(user('old-user'));
    const unexpectedHydration = http.match('/api/tenants/current');
    unexpectedHydration.forEach(request => request.flush(tenant('old-tenant')));
    const snapshot = await result;

    expect.soft(unexpectedHydration).toHaveLength(0);
    expect.soft(snapshot.status).toBe('expired');
    expect.soft(auth.currentUser()).toBeNull();
    expect.soft(auth.currentTenant()).toBeNull();
    expect(auth.session().capabilities).toEqual([]);
  });
  it('clears the current authenticated projection when a fresh status response is anonymous', async () => {
    await freshLogin();
    const result = firstValueFrom(auth.refreshSessionContext());
    http.expectOne('/api/auth/status').flush({ isAuthenticated: false, user: null });
    expect((await result)?.status).toBe('anonymous');
    expect(auth.currentUser()).toBeNull();
    expect(auth.currentTenant()).toBeNull();
    expect(auth.session().capabilities).toEqual([]);
  });
  it.each(['bootstrap', 'currentUser', 'sessionContext', 'anonymousStatus'] as const)(
    'does not accept an old %s response started during the pending login after the new session is established', async kind => {
      const pendingLogin = beginLogin();
      const result = firstValueFrom(kind === 'bootstrap' ? auth.bootstrap()
        : kind === 'currentUser' ? auth.refreshCurrentUser() : auth.refreshSessionContext());
      const read = http.expectOne(kind === 'bootstrap' || kind === 'currentUser' ? '/api/auth/me' : '/api/auth/status');
      pendingLogin.login.flush(user('fresh-user'));
      http.expectOne('/api/tenants/current').flush(tenant('fresh-tenant'));
      await pendingLogin.result;

      read.flush(kind === 'anonymousStatus' ? { isAuthenticated: false, user: null }
        : kind === 'sessionContext' ? { isAuthenticated: true, user: user('old-user') } : user('old-user'));
      const unexpectedHydration = http.match('/api/tenants/current');
      unexpectedHydration.forEach(request => request.flush(tenant('old-tenant')));
      const snapshot = await result;

      expect.soft(unexpectedHydration).toHaveLength(0);
      expect.soft(snapshot?.currentUser?.userId).toBe('fresh-user');
      expectFreshSession();
    },
  );

  it('rejects a stale interceptor probe 401 that started during the pending login', async () => {
    const pendingLogin = beginLogin();
    const errors: unknown[] = [];
    client.get('/api/protected-during-login').subscribe({ error: error => errors.push(error) });
    http.expectOne('/api/protected-during-login').flush({ error: 'Old admission expired' }, { status: 401, statusText: 'Unauthorized' });
    const oldProbe = http.expectOne('/api/auth/me');
    pendingLogin.login.flush(user('fresh-user'));
    http.expectOne('/api/tenants/current').flush(tenant('fresh-tenant'));
    await pendingLogin.result;

    oldProbe.flush({ error: 'Old admission expired' }, { status: 401, statusText: 'Unauthorized' });

    expect(errors).toHaveLength(1);
    expectFreshSession();
  });
  it('captures a read boundary on subscription so an explicitly fresh read can authenticate', async () => {
    const read = auth.refreshCurrentUser();
    auth.clearSessionState('expired');
    const result = firstValueFrom(read);
    http.expectOne('/api/auth/me').flush(user('fresh-user'));
    expect((await result)?.currentUser?.userId).toBe('fresh-user');
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.session().capabilities).toEqual(['files:view']);
  });
});