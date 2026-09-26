import { HttpBackend, HttpClient, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, defer, finalize, map, Observable, of, switchMap, tap, throwError } from 'rxjs';

import { AppCapability } from '../../shared/navigation/navigation.models';
import { TenantScopedStateFacade } from '../tenant/tenant-scoped-state.facade';
import { ActiveWorkspaceFacade } from '../workspace/active-workspace.facade';
import {
  AuthStatusResponseDto,
  CurrentTenantResponseDto,
  CurrentUserResponseDto,
  LoginResponseDto,
  mapAuthStatusResponse,
  mapCurrentTenantResponse,
  mapCurrentUserResponse
} from './auth-session.api';
import { CsrfTokenService } from './csrf-token.service';
import { WorkspaceSummary } from '../workspace/active-workspace.facade';

export type AuthSessionStatus = 'anonymous' | 'active' | 'expired';

export interface AuthCurrentUser {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string;
  readonly systemRole: string;
  readonly status: string;
  readonly capabilities: readonly string[];
  readonly currentWorkspace: WorkspaceSummary | null;
  readonly workspaces: readonly WorkspaceSummary[];
}

export interface AuthCurrentTenant {
  readonly tenantId: string;
  readonly tenantSlug?: string | null;
  readonly isAvailable: boolean;
  readonly isPlatformScope: boolean;
  readonly displayName?: string | null;
  readonly status?: string | null;
  readonly currentUserRole?: string | null;
  readonly appMode?: string | number;
  readonly allowTenantSwitching: boolean;
}

export interface AuthNavigationState {
  readonly capabilities: readonly AppCapability[];
  readonly isLoaded: boolean;
}

export interface AuthSessionSnapshot {
  readonly status: AuthSessionStatus;
  readonly currentUser: AuthCurrentUser | null;
  readonly currentTenant: AuthCurrentTenant | null;
  readonly isAuthenticated: boolean;
  readonly displayName: string;
  readonly supportingUsers: readonly string[];
  readonly capabilities: readonly AppCapability[];
  readonly navigation: AuthNavigationState;
}

export const DEFAULT_AUTH_SESSION: AuthSessionSnapshot = {
  status: 'active',
  currentUser: {
    userId: 'mock-user-a',
    displayName: 'Mock User A',
    email: 'mock-user-a@example.invalid',
    systemRole: 'TenantUser',
    status: 'Active',
    capabilities: ['workspace:view', 'announcements:view', 'projects:view', 'files:view', 'account:view', 'audit:view'],
    currentWorkspace: null,
    workspaces: []
  },
  currentTenant: {
    tenantId: 'mock-tenant',
    tenantSlug: 'mock',
    isAvailable: true,
    isPlatformScope: false,
    displayName: 'Mock Tenant',
    status: 'Active',
    currentUserRole: 'Admin',
    appMode: 'OnPremSingleTenant',
    allowTenantSwitching: true
  },
  isAuthenticated: true,
  displayName: 'Mock User A',
  supportingUsers: ['Support User', 'Review User', 'Operations User'],
  capabilities: ['workspace:view', 'announcements:view', 'projects:view', 'files:view', 'account:view', 'audit:view'],
  navigation: {
    capabilities: ['workspace:view', 'announcements:view', 'projects:view', 'files:view', 'account:view', 'audit:view'],
    isLoaded: true
  }
};

export const ANONYMOUS_AUTH_SESSION: AuthSessionSnapshot = createSessionSnapshot(null, null, 'anonymous', []);

export const COGLATAS_AUTH_SESSION_MOCK = new InjectionToken<AuthSessionSnapshot>('COGLATAS_AUTH_SESSION_MOCK');

@Injectable({ providedIn: 'root' })
export class AuthSessionFacade {
  private readonly initialSession = inject(COGLATAS_AUTH_SESSION_MOCK, { optional: true }) ?? ANONYMOUS_AUTH_SESSION;
  private readonly httpBackend = inject(HttpBackend, { optional: true });
  private readonly router = inject(Router, { optional: true });
  private readonly csrfTokens = inject(CsrfTokenService);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly tenantScopedState = inject(TenantScopedStateFacade);
  private readonly sessionState = signal<AuthSessionSnapshot>(this.initialSession);
  private readonly loadingState = signal(false);

  readonly session = this.sessionState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly currentUser = computed(() => this.sessionState().currentUser);
  readonly currentTenant = computed(() => this.sessionState().currentTenant);
  readonly isAuthenticated = computed(() => this.sessionState().isAuthenticated);
  readonly navigation = computed(() => this.sessionState().navigation);

  clearSessionState(status: AuthSessionStatus = 'anonymous'): void {
    this.sessionState.set(createSessionSnapshot(null, null, status, []));
    this.activeWorkspace.clearWorkspace();
    this.tenantScopedState.clearTenantScopedState();
    this.csrfTokens.clearToken();
  }

  handleTerminal401(): void {
    this.clearSessionState('expired');
    void this.router?.navigateByUrl('/session-expired');
  }

  markSessionExpired(): void {
    this.clearSessionState('expired');
  }

  logoutLocally(): void {
    this.clearSessionState('anonymous');
  }

  logout(): Observable<AuthSessionSnapshot> {
    const http = this.createBackendHttpClient();
    if (!http) {
      throw new Error('Auth logout endpoint is unavailable in this Angular context.');
    }

    return this.csrfTokens.ensureToken(this.csrfCacheKey()).pipe(
      switchMap((csrfToken) =>
        http.post(
          '/api/auth/logout',
          {},
          {
            withCredentials: true,
            headers: {
              [csrfToken.headerName]: csrfToken.token
            }
          }
        )
      ),
      map(() => this.completeLogout()),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          return of(this.completeLogout());
        }

        return throwError(() => error);
      })
    );
  }

  bootstrap(): Observable<AuthSessionSnapshot> {
    return defer(() => {
      this.loadingState.set(true);
      const http = this.createBackendHttpClient();
      const bootstrapRequest = http
        ? http.get<CurrentUserResponseDto>('/api/auth/me', { withCredentials: true }).pipe(
            map((response) => mapCurrentUserResponse(response)),
            tap((user) => this.patchUser(user)),
            switchMap(() => this.refreshCurrentTenant()),
            map(() => this.sessionState()),
            catchError((error: unknown) => {
              if (!isExpectedUnauthenticatedError(error)) {
                console.error('Auth bootstrap failed', error);
              }

              this.clearSessionState('anonymous');
              return of(this.sessionState());
            })
          )
        : of(this.sessionState());

      return bootstrapRequest.pipe(finalize(() => this.loadingState.set(false)));
    });
  }

  login(email: string, password: string): Observable<AuthSessionSnapshot> {
    const http = this.createBackendHttpClient();
    if (!http) {
      throw new Error('Auth login endpoint is unavailable in this Angular context.');
    }

    return this.csrfTokens.ensureToken(this.csrfCacheKey()).pipe(
      switchMap((csrfToken) =>
        http.post<LoginResponseDto>(
          '/api/auth/login',
          { email, password },
          {
            withCredentials: true,
            headers: {
              [csrfToken.headerName]: csrfToken.token
            }
          }
        )
      ),
      map((response) => mapCurrentUserResponse(response)),
      tap((user) => {
        // Antiforgery tokens issued before login are bound to the anonymous
        // principal. Discard that cache before any authenticated unsafe call
        // (including SignalR negotiate) obtains its token.
        this.csrfTokens.clearToken();
        this.patchUser(user);
      }),
      switchMap(() => this.refreshCurrentTenant()),
      map(() => this.sessionState())
    );
  }

  refreshCurrentUser(): Observable<AuthSessionSnapshot | null> {
    const http = this.createBackendHttpClient();
    if (!http) {
      return of(null);
    }

    return http.get<CurrentUserResponseDto>('/api/auth/me', { withCredentials: true }).pipe(
      map((response) => mapCurrentUserResponse(response)),
      tap((user) => this.patchUser(user)),
      map(() => this.sessionState()),
      catchError((error: unknown) => {
        if (isExpectedUnauthenticatedError(error)) {
          this.clearSessionState('anonymous');
          return of(this.sessionState());
        }

        return of(null);
      })
    );
  }

  validateServerSession(): Observable<AuthSessionSnapshot> {
    return this.bootstrap();
  }

  refreshSessionContext(): Observable<AuthSessionSnapshot | null> {
    const http = this.createBackendHttpClient();
    if (!http) {
      return of(null);
    }

    return http.get<AuthStatusResponseDto>('/api/auth/status', { withCredentials: true }).pipe(
      map((response) => mapAuthStatusResponse(response)),
      tap((status) => {
        if (status.isAuthenticated && status.user) {
          this.patchUser(status.user);
          return;
        }

        this.clearSessionState('anonymous');
      }),
      map(() => this.sessionState()),
      catchError((error: unknown) => {
        if (isExpectedUnauthenticatedError(error)) {
          this.clearSessionState('anonymous');
          return of(this.sessionState());
        }

        return of(null);
      })
    );
  }

  refreshCurrentTenant(): Observable<AuthCurrentTenant | null> {
    const http = this.createBackendHttpClient();
    if (!http) {
      return of(null);
    }

    return http.get<CurrentTenantResponseDto>('/api/tenants/current', { withCredentials: true }).pipe(
      map((response) => mapCurrentTenantResponse(response)),
      tap((tenant) => this.patchTenant(tenant)),
      catchError((error: unknown) => {
        // This request intentionally bypasses interceptors through HttpBackend.
        // A 401/403 is therefore a terminal authentication boundary that must
        // clear protected HTTP-owned state explicitly. Transient/network/server
        // failures remain a tenant-hydration degradation and preserve state.
        if (isExpectedUnauthenticatedError(error)) {
          this.clearSessionState('anonymous');
        }

        return of(null);
      })
    );
  }

  csrfCacheKey(): string {
    const tenant = this.sessionState().currentTenant;
    return tenant?.tenantId ?? tenant?.tenantSlug ?? 'tenant-unresolved';
  }

  setMockSession(session: AuthSessionSnapshot): void {
    this.sessionState.set(session);
  }

  private patchUser(user: AuthCurrentUser): void {
    this.sessionState.update((session) =>
      createSessionSnapshot(
        user,
        session.currentTenant,
        'active',
        deriveCapabilities(user, session.currentTenant)
      )
    );
  }

  private patchTenant(tenant: AuthCurrentTenant): void {
    this.sessionState.update((session) =>
      createSessionSnapshot(
        session.currentUser,
        tenant,
        session.currentUser ? 'active' : 'anonymous',
        session.currentUser ? deriveCapabilities(session.currentUser, tenant) : []
      )
    );
  }

  private completeLogout(): AuthSessionSnapshot {
    this.clearSessionState('anonymous');
    void this.router?.navigateByUrl('/login');
    return this.sessionState();
  }

  private createBackendHttpClient(): HttpClient | null {
    return this.httpBackend ? new HttpClient(this.httpBackend) : null;
  }
}

function createSessionSnapshot(
  user: AuthCurrentUser | null,
  tenant: AuthCurrentTenant | null,
  status: AuthSessionStatus,
  capabilities: readonly AppCapability[]
): AuthSessionSnapshot {
  return {
    status,
    currentUser: user,
    currentTenant: tenant,
    isAuthenticated: status === 'active' && user !== null,
    displayName: user?.displayName ?? '',
    supportingUsers: [],
    capabilities,
    navigation: {
      capabilities,
      isLoaded: capabilities.length > 0
    }
  };
}

function isExpectedUnauthenticatedError(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403);
}

function deriveCapabilities(
  user: AuthCurrentUser | null,
  tenant: AuthCurrentTenant | null
): readonly AppCapability[] {
  if (!user) {
    return [];
  }

  const apiCapabilities = user.capabilities.filter(isAppCapability);
  if (apiCapabilities.length > 0) {
    return [...new Set(apiCapabilities)];
  }

  const capabilities = new Set<AppCapability>(['account:view']);
  if (tenant?.isAvailable || isPlatformAdmin(user.systemRole)) {
    capabilities.add('workspace:view');
    capabilities.add('announcements:view');
    capabilities.add('projects:view');
    capabilities.add('files:view');
  }

  if (isAdmin(user.systemRole) || isTenantAdmin(tenant?.currentUserRole)) {
    capabilities.add('audit:view');
  }

  if (isPlatformAdmin(user.systemRole)) {
    capabilities.add('admin:access');
    capabilities.add('invite:read');
    capabilities.add('invite:create');
  }

  return [...capabilities];
}

function isAppCapability(value: string): value is AppCapability {
  return (
    value === 'workspace:view' ||
    value === 'announcements:view' ||
    value === 'projects:view' ||
    value === 'files:view' ||
    value === 'account:view' ||
    value === 'audit:view' ||
    value === 'admin:access' ||
    value === 'invite:read' ||
    value === 'invite:create'
  );
}

function isPlatformAdmin(role: string): boolean {
  return role === 'PlatformAdmin' || role === 'SystemAdmin' || role === '5';
}

function isAdmin(role: string): boolean {
  return role === 'Admin' || role === '3' || isPlatformAdmin(role);
}

function isTenantAdmin(role: string | null | undefined): boolean {
  return role === 'Owner' || role === 'Admin' || role === '0' || role === '1';
}
