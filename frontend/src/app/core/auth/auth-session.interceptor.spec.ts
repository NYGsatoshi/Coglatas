import { Component } from '@angular/core';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { FrontendApiError } from '../api/api-error.model';
import {
  HttpRequestDispatchSignal,
  HTTP_REQUEST_DISPATCH_SIGNAL,
} from '../api/http-request-dispatch.context';
import {
  COGLATAS_AUTH_SESSION_MOCK,
  AuthSessionFacade,
  DEFAULT_AUTH_SESSION,
} from './auth-session.facade';
import { authSessionInterceptor } from './auth-session.interceptor';

@Component({
  standalone: true,
  template: '',
})
class EmptyRouteComponent {}

describe('auth session interceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let authSession: AuthSessionFacade;

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
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    authSession = TestBed.inject(AuthSessionFacade);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('attaches X-CSRF-Token to unsafe first-party API requests', () => {
    http.post('/api/projects', { name: 'Project A' }).subscribe();

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-123',
      headerName: 'X-CSRF-Token',
    });

    const mutation = httpMock.expectOne('/api/projects');
    expect(mutation.request.withCredentials).toBe(true);
    expect(mutation.request.headers.get('X-CSRF-Token')).toBe('csrf-123');
    mutation.flush({ status: 'OK' });
  });

  it('does not attach CSRF token to GET requests', () => {
    http.get('/api/projects').subscribe();

    httpMock.expectNone('/api/security/csrf-token');
    const request = httpMock.expectOne('/api/projects');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.headers.has('X-CSRF-Token')).toBe(false);
    request.flush([]);
  });

  it('does not send CSRF tokens to third-party origins', () => {
    http.post('https://third-party.example/api/projects', { name: 'Project A' }).subscribe();

    httpMock.expectNone('/api/security/csrf-token');
    const request = httpMock.expectOne('https://third-party.example/api/projects');
    expect(request.request.withCredentials).toBe(false);
    expect(request.request.headers.has('X-CSRF-Token')).toBe(false);
    request.flush({ status: 'OK' });
  });

  it('prevents mutation requests when CSRF token fetch fails', () => {
    let capturedError: FrontendApiError | null = null;

    http.post('/api/projects', { name: 'Project A' }).subscribe({
      error: (error: FrontendApiError) => {
        capturedError = error;
      },
    });

    httpMock
      .expectOne('/api/security/csrf-token')
      .flush({ error: 'CSRF unavailable' }, { status: 500, statusText: 'Server Error' });

    httpMock.expectNone('/api/projects');
    expect(capturedError?.httpStatus).toBe(500);
  });

  it('retries unsafe CSRF failures once with a refreshed token', () => {
    let capturedError: FrontendApiError | null = null;

    http.patch('/api/projects/project-1', { name: 'Project B' }).subscribe({
      error: (error: FrontendApiError) => {
        capturedError = error;
      },
    });

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-old',
      headerName: 'X-CSRF-Token',
    });

    const firstMutation = httpMock.expectOne('/api/projects/project-1');
    expect(firstMutation.request.headers.get('X-CSRF-Token')).toBe('csrf-old');
    firstMutation.flush({ error: 'CSRF token expired' }, { status: 403, statusText: 'Forbidden' });

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-new',
      headerName: 'X-CSRF-Token',
    });

    const retryMutation = httpMock.expectOne('/api/projects/project-1');
    expect(retryMutation.request.headers.get('X-CSRF-Token')).toBe('csrf-new');
    retryMutation.flush(
      { error: 'CSRF token expired again' },
      { status: 403, statusText: 'Forbidden' },
    );

    httpMock.expectNone('/api/security/csrf-token');
    expect(capturedError?.httpStatus).toBe(403);
  });

  it('notifies a transport-dispatch signal once when a CSRF retry reuses the request context', () => {
    const dispatched = vi.fn();
    const context = new HttpContext().set(
      HTTP_REQUEST_DISPATCH_SIGNAL,
      new HttpRequestDispatchSignal(dispatched),
    );

    http.post('/api/projects', { name: 'Project A' }, { context }).subscribe();
    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-old',
      headerName: 'X-CSRF-Token',
    });

    const firstMutation = httpMock.expectOne('/api/projects');
    expect(dispatched).toHaveBeenCalledOnce();
    firstMutation.flush({ error: 'CSRF token expired' }, { status: 403, statusText: 'Forbidden' });

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-new',
      headerName: 'X-CSRF-Token',
    });
    const retryMutation = httpMock.expectOne('/api/projects');
    expect(dispatched).toHaveBeenCalledOnce();
    retryMutation.flush({ status: 'OK' });
  });

  it('retries 400 CSRF validation failures once with a refreshed token', () => {
    let capturedError: FrontendApiError | null = null;

    http.post('/api/admin/invites', { email: 'new-user@example.invalid', role: 3 }).subscribe({
      error: (error: FrontendApiError) => {
        capturedError = error;
      },
    });

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-old',
      headerName: 'X-CSRF-Token',
    });

    const firstMutation = httpMock.expectOne('/api/admin/invites');
    expect(firstMutation.request.withCredentials).toBe(true);
    expect(firstMutation.request.headers.get('X-CSRF-Token')).toBe('csrf-old');
    firstMutation.flush(
      { title: 'CSRF token expired' },
      { status: 400, statusText: 'Bad Request' },
    );

    httpMock.expectOne('/api/security/csrf-token').flush({
      token: 'csrf-new',
      headerName: 'X-CSRF-Token',
    });

    const retryMutation = httpMock.expectOne('/api/admin/invites');
    expect(retryMutation.request.withCredentials).toBe(true);
    expect(retryMutation.request.headers.get('X-CSRF-Token')).toBe('csrf-new');
    retryMutation.flush(
      { title: 'CSRF token expired again' },
      { status: 400, statusText: 'Bad Request' },
    );

    httpMock.expectNone('/api/security/csrf-token');
    expect(capturedError?.httpStatus).toBe(400);
  });

  it('clears session state on terminal 401 after current-user refresh fails', () => {
    let capturedError: FrontendApiError | null = null;

    http.get('/api/projects').subscribe({
      error: (error: FrontendApiError) => {
        capturedError = error;
      },
    });

    httpMock
      .expectOne('/api/projects')
      .flush({ error: 'Expired' }, { status: 401, statusText: 'Unauthorized' });
    httpMock
      .expectOne('/api/auth/me')
      .flush({ error: 'Expired' }, { status: 401, statusText: 'Unauthorized' });

    expect(capturedError?.httpStatus).toBe(401);
    expect(authSession.session().status).toBe('expired');
    expect(authSession.session().currentUser).toBeNull();
    expect(authSession.session().currentTenant).toBeNull();
  });

  it('does not logout on 403 permission denial', () => {
    let capturedError: FrontendApiError | null = null;

    http.get('/api/projects').subscribe({
      error: (error: FrontendApiError) => {
        capturedError = error;
      },
    });

    httpMock
      .expectOne('/api/projects')
      .flush({ error: 'Permission denied' }, { status: 403, statusText: 'Forbidden' });

    expect(capturedError?.httpStatus).toBe(403);
    expect(authSession.session().status).toBe('active');
    expect(authSession.session().currentUser).not.toBeNull();
  });
});
