import { Component } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authSessionInterceptor } from '../auth/auth-session.interceptor';
import {
  COGLATAS_AUTH_SESSION_MOCK,
  AuthSessionFacade,
  DEFAULT_AUTH_SESSION,
} from '../auth/auth-session.facade';
import type { AuthCurrentTenant } from '../auth/auth-session.facade';
import { ActiveWorkspaceFacade } from '../workspace/active-workspace.facade';
import {
  COGLATAS_RIGHT_PANEL_MOCK,
  RightPanelFacade,
} from '../../shared/right-panel/right-panel.facade';
import { TenantSwitchFacade } from './tenant-switch.facade';

@Component({
  standalone: true,
  template: '',
})
class EmptyRouteComponent {}

describe('TenantSwitchFacade', () => {
  let httpMock: HttpTestingController;
  let tenantSwitch: TenantSwitchFacade;
  let authSession: AuthSessionFacade;
  let activeWorkspace: ActiveWorkspaceFacade;
  let rightPanel: RightPanelFacade;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'session-expired', component: EmptyRouteComponent }]),
        provideHttpClient(withInterceptors([authSessionInterceptor])),
        provideHttpClientTesting(),
        {
          provide: COGLATAS_AUTH_SESSION_MOCK,
          useValue: DEFAULT_AUTH_SESSION,
        },
        {
          provide: COGLATAS_RIGHT_PANEL_MOCK,
          useValue: {
            notifications: [],
            members: [],
          },
        },
      ],
    });

    TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tenantSwitch = TestBed.inject(TenantSwitchFacade);
    authSession = TestBed.inject(AuthSessionFacade);
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    rightPanel = TestBed.inject(RightPanelFacade);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('uses CSRF-protected tenant switch and refreshes verified session context', () => {
    const emitted: Array<AuthCurrentTenant | null> = [];
    rightPanel.setMode('expanded');
    rightPanel.setSelectedTab('members');

    tenantSwitch.switchTenant('tenant-next').subscribe((tenant) => emitted.push(tenant));

    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(rightPanel.mode()).toBe('collapsed');
    expect(rightPanel.selectedTab()).toBe('notifications');

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-tenant-switch',
      headerName: 'X-CSRF-Token',
    });

    const switchRequest = httpMock.expectOne('/api/tenants/switch');
    expect(switchRequest.request.method).toBe('POST');
    expect(switchRequest.request.withCredentials).toBe(true);
    expect(switchRequest.request.headers.get('X-CSRF-Token')).toBe('csrf-tenant-switch');
    expect(switchRequest.request.body).toEqual({ tenantId: 'tenant-next' });
    switchRequest.flush({
      id: 'tenant-next',
      name: 'Tenant Next',
      slug: 'next',
      displayName: 'Tenant Next',
      status: 1,
    });

    httpMock.expectOne('/api/auth/status').flush({
      isAuthenticated: true,
      user: {
        userId: 'user-next',
        displayName: 'Next User',
        email: 'next@example.test',
        systemRole: 2,
        status: 1,
      },
    });

    httpMock.expectOne('/api/tenants/current').flush({
      tenantId: 'tenant-next',
      tenantSlug: 'next',
      isAvailable: true,
      isPlatformScope: false,
      displayName: 'Tenant Next',
      status: 1,
      currentUserRole: 3,
      appMode: 0,
      allowTenantSwitching: true,
    });

    expect(emitted[0]?.tenantId).toBe('tenant-next');
    expect(authSession.currentUser()?.userId).toBe('user-next');
    expect(authSession.currentTenant()?.currentUserRole).toBe('3');
  });

  it('does not send the tenant switch mutation when CSRF token acquisition fails', () => {
    const emitted: Array<AuthCurrentTenant | null> = [];

    tenantSwitch.switchTenant('tenant-next').subscribe((tenant) => emitted.push(tenant));

    httpMock
      .expectOne('/api/security/csrf-token')
      .flush({ error: 'CSRF unavailable' }, { status: 500, statusText: 'Server Error' });

    httpMock.expectNone('/api/tenants/switch');
    expect(emitted).toEqual([null]);
  });
});
