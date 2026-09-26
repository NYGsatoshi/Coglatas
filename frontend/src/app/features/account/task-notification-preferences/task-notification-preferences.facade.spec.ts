import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  COGLATAS_AUTH_SESSION_MOCK,
  AuthSessionFacade,
  DEFAULT_AUTH_SESSION,
} from '../../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { COGLATAS_ACTIVE_WORKSPACE_MOCK } from '../../../core/workspace/active-workspace.facade';
import { TaskNotificationPreferencesFacade } from './task-notification-preferences.facade';

const WORKSPACE = { id: 'workspace-1', label: 'Workspace 1' };
const ENDPOINT = '/api/me/workspaces/workspace-1/task-notification-preferences';

describe('TaskNotificationPreferencesFacade', () => {
  let facade: TaskNotificationPreferencesFacade;
  let httpMock: HttpTestingController;
  let clearers: Array<() => void>;
  let auth: AuthSessionFacade;

  beforeEach(() => {
    clearers = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: COGLATAS_AUTH_SESSION_MOCK,
          useValue: {
            ...DEFAULT_AUTH_SESSION,
            currentUser: {
              ...DEFAULT_AUTH_SESSION.currentUser!,
              currentWorkspace: WORKSPACE,
              workspaces: [WORKSPACE],
            },
          },
        },
        { provide: COGLATAS_ACTIVE_WORKSPACE_MOCK, useValue: WORKSPACE },
        {
          provide: RealtimeFacade,
          useValue: {
            registerProtectedStateClearer: (_owner: string, clear: () => void) => {
              clearers.push(clear);
              return () => undefined;
            },
          },
        },
      ],
    });

    facade = TestBed.inject(TaskNotificationPreferencesFacade);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthSessionFacade);
    TestBed.flushEffects();
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('PreferenceConflictRefetchesAuthoritativeServerState', () => {
    httpMock.expectOne(ENDPOINT).flush(preferenceResponse('09:00', '09:00', 4));

    facade.save('10:30');
    const patch = httpMock.expectOne(ENDPOINT);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ deadlineDigestLocalTime: '10:30', expectedVersion: 4 });
    patch.flush({ error: 'conflict' }, { status: 409, statusText: 'Conflict' });

    httpMock.expectOne(ENDPOINT).flush(preferenceResponse(null, '08:45', 5));
    expect(facade.viewModel()).toEqual(expect.objectContaining({
      status: 'ready',
      storedDeadlineDigestLocalTime: null,
      effectiveDeadlineDigestLocalTime: '08:45',
      version: 5,
    }));
  });

  it('InvalidPreferenceIsNotRounded', () => {
    httpMock.expectOne(ENDPOINT).flush(preferenceResponse('09:00', '09:00', 4));

    facade.save('10:07');

    httpMock.expectNone(ENDPOINT);
    expect(facade.viewModel()).toEqual(expect.objectContaining({
      status: 'error',
      storedDeadlineDigestLocalTime: '09:00',
      message: expect.stringContaining('15-minute'),
    }));
  });

  it('AuthorizationInvalidationClearsWorkspacePreferenceState', () => {
    httpMock.expectOne(ENDPOINT).flush(preferenceResponse('09:00', '09:00', 4));
    expect(facade.viewModel().status).toBe('ready');

    clearers.forEach((clear) => clear());

    expect(facade.viewModel()).toEqual(expect.objectContaining({ status: 'idle', workspaceId: null }));
  });

  it('TenantSwitchClearsPreferenceStateBeforeAuthoritativeReload', () => {
    httpMock.expectOne(ENDPOINT).flush(preferenceResponse('09:00', '09:00', 4));

    auth.setMockSession({
      ...DEFAULT_AUTH_SESSION,
      currentTenant: {
        ...DEFAULT_AUTH_SESSION.currentTenant!,
        tenantId: 'tenant-2',
        isAvailable: true,
      },
      currentUser: {
        ...DEFAULT_AUTH_SESSION.currentUser!,
        currentWorkspace: WORKSPACE,
        workspaces: [WORKSPACE],
      },
    });
    TestBed.flushEffects();

    expect(facade.viewModel()).toEqual(expect.objectContaining({
      status: 'loading',
      storedDeadlineDigestLocalTime: null,
      effectiveDeadlineDigestLocalTime: null,
      version: null,
    }));
    httpMock.expectOne(ENDPOINT).flush(preferenceResponse(null, '08:00', 1));
  });
});

function preferenceResponse(stored: string | null, effective: string, version: number): Record<string, unknown> {
  return {
    deadlineDigestLocalTime: stored,
    effectiveDeadlineDigestLocalTime: effective,
    workspaceTimeZoneId: 'Asia/Tokyo',
    version,
  };
}
