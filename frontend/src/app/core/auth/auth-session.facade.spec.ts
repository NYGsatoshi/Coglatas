import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { ActiveWorkspaceFacade } from '../workspace/active-workspace.facade';
import { COGLATAS_AUTH_SESSION_MOCK, AuthSessionFacade, AuthSessionSnapshot, DEFAULT_AUTH_SESSION } from './auth-session.facade';
import { CsrfTokenService } from './csrf-token.service';

@Component({
  standalone: true,
  template: '',
})
class LoginRouteComponent {}

describe('AuthSessionFacade logout', () => {
  let authSession: AuthSessionFacade;
  let activeWorkspace: ActiveWorkspaceFacade;
  let csrfTokens: CsrfTokenService;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'login', component: LoginRouteComponent }]),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      ],
    });

    authSession = TestBed.inject(AuthSessionFacade);
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    csrfTokens = TestBed.inject(CsrfTokenService);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('posts logout to the backend, clears scoped state and redirects to login on success', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const emitted: string[] = [];

    csrfTokens.ensureToken('mock-tenant').subscribe();
    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-before-logout',
      headerName: 'X-CSRF-Token',
    });

    activeWorkspace.setActiveWorkspace({ id: 'workspace-a', label: 'Workspace A' });
    authSession.logout().subscribe((snapshot) => emitted.push(snapshot.status));

    const logoutRequest = httpMock.expectOne('/api/auth/logout');
    expect(logoutRequest.request.method).toBe('POST');
    expect(logoutRequest.request.withCredentials).toBe(true);
    expect(logoutRequest.request.headers.get('X-CSRF-Token')).toBe('csrf-before-logout');
    logoutRequest.flush({ status: 'OK' });

    expect(emitted).toEqual(['anonymous']);
    expect(authSession.session().isAuthenticated).toBe(false);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(navigateSpy).toHaveBeenCalledWith('/login');

    csrfTokens.ensureToken('mock-tenant').subscribe();
    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-after-logout',
      headerName: 'X-CSRF-Token',
    });
  });

  it('does not clear the active session when logout fails before terminal anonymous state', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const errors: unknown[] = [];

    authSession.logout().subscribe({ error: (error) => errors.push(error) });

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-logout',
      headerName: 'X-CSRF-Token',
    });
    httpMock
      .expectOne('/api/auth/logout')
      .flush({ error: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(errors.length).toBe(1);
    expect(authSession.session().isAuthenticated).toBe(true);
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('discards the anonymous CSRF token after login so authenticated mutations obtain a new token', () => {
    csrfTokens.ensureToken('mock-tenant').subscribe();
    httpMock.expectOne('/api/security/csrf-token').flush({ token: 'csrf-anonymous', headerName: 'X-CSRF-Token' });

    authSession.login('member@example.test', 'correct-password').subscribe();
    const loginRequest = httpMock.expectOne('/api/auth/login');
    expect(loginRequest.request.headers.get('X-CSRF-Token')).toBe('csrf-anonymous');
    loginRequest.flush({
      userId: 'user-1', displayName: 'Member', email: 'member@example.test', systemRole: 'User', status: 'Active',
      capabilities: ['projects:view'], currentWorkspace: null, workspaces: []
    });
    httpMock.expectOne('/api/tenants/current').flush({
      tenantId: 'tenant-1', tenantSlug: 'default', isAvailable: true, isPlatformScope: false,
      displayName: 'Default', status: 'Active', currentUserRole: 'Member', appMode: 'OnPremSingleTenant', allowTenantSwitching: false
    });

    csrfTokens.ensureToken('tenant-1').subscribe();
    httpMock.expectOne('/api/security/csrf-token').flush({ token: 'csrf-authenticated', headerName: 'X-CSRF-Token' });
  });

  it('retains normal-user capabilities from the backend without adding admin grants', () => {
    const snapshots: AuthSessionSnapshot[] = [];

    authSession.bootstrap().subscribe((snapshot) => snapshots.push(snapshot));

    httpMock.expectOne('/api/auth/me').flush({
      userId: 'user-normal',
      displayName: 'Normal User',
      email: 'normal@example.test',
      systemRole: 'User',
      status: 'Active',
      capabilities: ['workspace:view', 'announcements:view', 'projects:view', 'files:view', 'account:view'],
      currentWorkspace: null,
      workspaces: []
    });
    httpMock.expectOne('/api/tenants/current').flush({
      tenantId: 'tenant-1',
      tenantSlug: 'default',
      isAvailable: true,
      isPlatformScope: false,
      displayName: 'Default',
      status: 'Active',
      currentUserRole: 'Member',
      appMode: 'OnPremSingleTenant',
      allowTenantSwitching: false
    });

    expect(snapshots.at(-1)?.capabilities).toEqual([
      'workspace:view',
      'announcements:view',
      'projects:view',
      'files:view',
      'account:view'
    ]);
    expect(snapshots.at(-1)?.capabilities).not.toContain('admin:access');
    expect(snapshots.at(-1)?.capabilities).not.toContain('invite:create');
  });

  it('does not choose the first auth Workspace when multiple authorized Workspaces require selection', () => {
    authSession.bootstrap().subscribe();

    httpMock.expectOne('/api/auth/me').flush({
      userId: 'user-multi',
      displayName: 'Multi Workspace User',
      email: 'multi@example.test',
      systemRole: 'User',
      status: 'Active',
      capabilities: ['workspace:view'],
      currentWorkspace: null,
      workspaces: [
        { id: 'workspace-first', name: 'First API row' },
        { id: 'workspace-second', name: 'Second API row' },
      ],
    });
    httpMock.expectOne('/api/tenants/current').flush({
      tenantId: 'tenant-1',
      tenantSlug: 'default',
      isAvailable: true,
      isPlatformScope: false,
      displayName: 'Default',
      status: 'Active',
      currentUserRole: 'Member',
      appMode: 'OnPremSingleTenant',
      allowTenantSwitching: false,
    });

    expect(authSession.currentUser()?.workspaces.map((workspace) => workspace.id)).toEqual([
      'workspace-first',
      'workspace-second',
    ]);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
  });

  it.each([401, 403])(
    'clears hydrated protected state when current tenant refresh returns terminal auth status %s',
    (status) => {
      const snapshots: AuthSessionSnapshot[] = [];
      const workspace = { id: 'workspace-a', label: 'Workspace A' };
      authSession.clearSessionState('anonymous');

      authSession.bootstrap().subscribe((snapshot) => snapshots.push(snapshot));

      httpMock.expectOne('/api/auth/me').flush({
        userId: 'user-1',
        displayName: 'Member',
        email: 'member@example.test',
        systemRole: 'User',
        status: 'Active',
        capabilities: ['workspace:view', 'projects:view', 'account:view'],
        currentWorkspace: workspace,
        workspaces: [workspace]
      });

      expect(authSession.isAuthenticated()).toBe(true);
      expect(authSession.currentTenant()).toBeNull();
      // WorkspaceSelectionFacade owns activation after the authorized
      // dashboard reconciles; auth hydration must not bypass its boundary.
      expect(activeWorkspace.activeWorkspace()).toBeNull();

      httpMock.expectOne('/api/tenants/current').flush(
        { error: 'Authentication no longer valid' },
        { status, statusText: status === 401 ? 'Unauthorized' : 'Forbidden' }
      );

      expect(snapshots.at(-1)?.status).toBe('anonymous');
      expect(authSession.isAuthenticated()).toBe(false);
      expect(authSession.currentUser()).toBeNull();
      expect(authSession.currentTenant()).toBeNull();
      expect(activeWorkspace.activeWorkspace()).toBeNull();
    }
  );

  it('preserves hydrated workspace state when current tenant refresh fails transiently', () => {
    const snapshots: AuthSessionSnapshot[] = [];
    const workspace = { id: 'workspace-a', label: 'Workspace A' };
    authSession.clearSessionState('anonymous');

    authSession.bootstrap().subscribe((snapshot) => snapshots.push(snapshot));

    httpMock.expectOne('/api/auth/me').flush({
      userId: 'user-1',
      displayName: 'Member',
      email: 'member@example.test',
      systemRole: 'User',
      status: 'Active',
      capabilities: ['workspace:view', 'projects:view', 'account:view'],
      currentWorkspace: workspace,
      workspaces: [workspace]
    });

    httpMock.expectOne('/api/tenants/current').flush(
      { error: 'Temporary tenant service failure' },
      { status: 500, statusText: 'Server Error' }
    );

    expect(snapshots.at(-1)?.status).toBe('active');
    expect(authSession.isAuthenticated()).toBe(true);
    expect(authSession.currentUser()?.userId).toBe('user-1');
    expect(authSession.currentTenant()).toBeNull();
    expect(activeWorkspace.activeWorkspace()).toBeNull();
  });
});
