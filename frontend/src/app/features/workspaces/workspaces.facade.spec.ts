import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';
import { WorkspacesFacade } from './workspaces.facade';

const workspaceDto = {
  id: 'workspace-1',
  name: 'Backend Workspace',
  description: 'Authoritative dashboard card',
  icon: null,
  status: 0,
  createdAt: '2026-08-22T00:00:00Z',
  updatedAt: '2026-08-23T00:00:00Z',
  currentUserRole: 'Owner',
  accessSource: 'WorkspaceMembership',
  canOpenWorkspace: true,
  canOpenMembers: false,
  canOpenProjects: true,
  unreadAnnouncementCount: 0,
  unreadConversationCount: 5,
  inProgressProjectCount: 2,
  runningProjectCount: 1,
  needsReviewProjectCount: 1,
};

const createdWorkspaceId = '11111111-1111-4111-8111-111111111111';
const creatorUserId = '22222222-2222-4222-8222-222222222222';

const workspaceCreateEnvelope = {
  requestId: 'request-create-1',
  data: {
    id: createdWorkspaceId,
    name: 'Created Workspace',
    description: null,
    icon: '🔬',
    status: 0,
    createdByUserId: creatorUserId,
    createdAt: '2026-08-24T01:02:03Z',
    updatedAt: null,
  },
  warnings: [],
};

const createdWorkspaceDashboardDto = {
  ...workspaceDto,
  id: createdWorkspaceId,
  name: 'Created Workspace',
};

function workspaceCapabilities(canCreate: boolean) {
  return {
    requestId: 'request-capabilities',
    data: { canCreate },
    warnings: [],
  };
}

describe('WorkspacesFacade live dashboard projection', () => {
  let facade: WorkspacesFacade;
  let http: HttpTestingController;
  let workspaceSelection: {
    beginLoading: ReturnType<typeof vi.fn>;
    reconcileAuthorizedWorkspaces: ReturnType<typeof vi.fn>;
    markUnavailable: ReturnType<typeof vi.fn>;
    markAuthorizationPending: ReturnType<typeof vi.fn>;
    markTransientFailure: ReturnType<typeof vi.fn>;
    selectWorkspace: ReturnType<typeof vi.fn>;
    transitionRevision: WritableSignal<number>;
    selection: WritableSignal<{
      status: 'selected' | 'loading' | 'selectionRequired' | 'unavailable';
      workspaceId: string | null;
      source: 'explicit' | 'single' | 'route' | 'preference' | null;
    }>;
  };
  let authSessionState: WritableSignal<Record<string, unknown>>;
  let realtimeState: WritableSignal<'Connected' | 'Reconnecting' | 'Degraded'>;
  let authorizationRevision: WritableSignal<number>;
  let catchUp: (() => Promise<void> | void) | null;

  beforeEach(() => {
    const selectionState = signal<{
      status: 'selected' | 'loading' | 'selectionRequired' | 'unavailable';
      workspaceId: string | null;
      source: 'explicit' | 'single' | 'route' | 'preference' | null;
    }>({ status: 'selected', workspaceId: 'workspace-1', source: 'single' });
    const transitionRevisionState = signal(0);
    workspaceSelection = {
      beginLoading: vi.fn(),
      reconcileAuthorizedWorkspaces: vi.fn(() => ({
        status: 'selected',
        workspaceId: 'workspace-1',
        source: 'single',
      })),
      markUnavailable: vi.fn(),
      markAuthorizationPending: vi.fn(),
      markTransientFailure: vi.fn(),
      selectWorkspace: vi.fn().mockImplementation(async (workspaceId: string) => {
        if (selectionState().workspaceId !== workspaceId) {
          transitionRevisionState.update((revision) => revision + 1);
        }
        selectionState.set({ status: 'selected', workspaceId, source: 'explicit' });
        return true;
      }),
      transitionRevision: transitionRevisionState,
      selection: selectionState,
    };
    realtimeState = signal<'Connected' | 'Reconnecting' | 'Degraded'>('Connected');
    authorizationRevision = signal(0);
    authSessionState = signal({
      isAuthenticated: true,
      currentTenant: { tenantId: 'tenant-a' },
      currentUser: { userId: 'user-a' },
      capabilities: [],
    });
    catchUp = null;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: WorkspaceSelectionFacade, useValue: workspaceSelection },
        {
          provide: RealtimeFacade,
          useValue: {
            connectionState: realtimeState,
            authorizationRevision,
            registerCatchUp: vi.fn((_owner: string, callback: () => Promise<void> | void) => {
              catchUp = callback;
              return () => undefined;
            }),
            runAuthoritativeHttpCatchUps: vi.fn().mockResolvedValue(undefined),
          },
        },
        {
          provide: AuthSessionFacade,
          useValue: {
            session: authSessionState,
          },
        },
      ],
    });

    facade = TestBed.inject(WorkspacesFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('maps the complete list and enveloped page capability without placeholders', () => {
    const capabilitiesRequest = http.expectOne('/api/workspaces/capabilities');
    expect(capabilitiesRequest.request.withCredentials).toBe(true);
    capabilitiesRequest.flush({
      requestId: 'request-capabilities',
      data: { canCreate: true },
      warnings: [],
    });

    const listRequest = http.expectOne('/api/workspaces');
    expect(listRequest.request.withCredentials).toBe(true);
    listRequest.flush([workspaceDto]);

    expect(facade.dashboard()).toMatchObject({
      status: 'ready',
      pageCapabilities: ['createWorkspace'],
    });
    expect(facade.dashboard().message).toBeUndefined();
    expect(facade.dashboard().workspaces[0]).toMatchObject({
      id: 'workspace-1',
      currentUserRole: 'Owner',
      accessSource: 'WorkspaceMembership',
      roleLabel: '管理者',
      unreadAnnouncementCount: 0,
      unreadConversationCount: 5,
      activeProjectCount: 2,
      runningProjectCount: 1,
      needsReviewProjectCount: 1,
      capabilities: ['openWorkspace', 'openProjects'],
    });
    expect(workspaceSelection.reconcileAuthorizedWorkspaces).toHaveBeenCalledWith(
      [{ id: 'workspace-1', label: 'Backend Workspace' }],
      { tenantId: 'tenant-a', userId: 'user-a' },
      null,
    );
  });

  it('neutralizes a mounted explicit Workspace route when that Workspace is revoked', () => {
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/workspaces/revoked-workspace/members');
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    workspaceSelection.reconcileAuthorizedWorkspaces.mockReturnValue({
      status: 'unavailable',
      workspaceId: null,
      source: null,
    });

    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    expect(workspaceSelection.reconcileAuthorizedWorkspaces).toHaveBeenCalledWith(
      [{ id: 'workspace-1', label: 'Backend Workspace' }],
      { tenantId: 'tenant-a', userId: 'user-a' },
      'revoked-workspace',
    );
    expect(navigate).toHaveBeenCalledWith('/workspaces');
  });

  it('shows the no-access state while retaining backend-authorized create capability', () => {
    http.expectOne('/api/workspaces').flush([]);
    http.expectOne('/api/workspaces/capabilities').flush({
      requestId: 'request-capabilities',
      data: { canCreate: true },
      warnings: [],
    });

    expect(facade.dashboard()).toMatchObject({
      status: 'noWorkspaceAccess',
      workspaces: [],
      pageCapabilities: ['createWorkspace'],
      message: '最初のWorkspaceを作成して、リサーチを始められます。',
    });
    expect(workspaceSelection.reconcileAuthorizedWorkspaces).toHaveBeenCalledWith(
      [],
      { tenantId: 'tenant-a', userId: 'user-a' },
      null,
    );
  });

  it.each([401, 403])('maps HTTP %s to a safe permission-denied state', (status) => {
    http
      .expectOne('/api/workspaces')
      .flush(
        { requestId: 'request-denied', error: { code: 'CapabilityDenied' } },
        { status, statusText: 'Denied' },
      );
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });

    expect(facade.dashboard()).toMatchObject({
      status: 'permissionDenied',
      workspaces: [],
      pageCapabilities: [],
    });
    expect(workspaceSelection.markUnavailable).toHaveBeenCalledWith(true);
  });

  it('does not fabricate cards or counts when the dashboard projection fails', () => {
    http
      .expectOne('/api/workspaces')
      .flush(
        { requestId: 'request-failed', error: { code: 'DependencyUnavailable' } },
        { status: 503, statusText: 'Service Unavailable' },
      );
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));

    expect(facade.dashboard()).toMatchObject({
      status: 'error',
      workspaces: [],
      pageCapabilities: [],
    });
    expect(workspaceSelection.markTransientFailure).toHaveBeenCalledOnce();
  });

  it('fails page create capability closed without hiding an authorized card', () => {
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    http
      .expectOne('/api/workspaces/capabilities')
      .flush(
        { requestId: 'request-capability-failed' },
        { status: 503, statusText: 'Service Unavailable' },
      );

    expect(facade.dashboard()).toMatchObject({
      status: 'ready',
      pageCapabilities: [],
    });
    expect(facade.dashboard().workspaces).toHaveLength(1);
  });

  it('treats a malformed successful list as an error rather than no Workspace access', () => {
    http.expectOne('/api/workspaces').flush({ items: [workspaceDto] });
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });

    expect(facade.dashboard()).toMatchObject({
      status: 'error',
      workspaces: [],
      pageCapabilities: [],
    });
    expect(workspaceSelection.markTransientFailure).toHaveBeenCalledOnce();
  });

  it('cancels an older Workspace list and ignores its late capability response', () => {
    const firstList = http.expectOne('/api/workspaces');
    const firstCapabilities = http.expectOne('/api/workspaces/capabilities');

    facade.loadWorkspaces();

    const currentList = http.expectOne('/api/workspaces');
    const currentCapabilities = http.expectOne('/api/workspaces/capabilities');
    currentCapabilities.flush({ data: { canCreate: false } });
    currentList.flush([{ ...workspaceDto, id: 'workspace-current', name: 'Current' }]);

    firstCapabilities.flush(workspaceCapabilities(true));
    expect(firstList.cancelled).toBe(true);

    expect(facade.dashboard().pageCapabilities).toEqual([]);
    expect(facade.dashboard().workspaces.map((workspace) => workspace.id)).toEqual([
      'workspace-current',
    ]);
    expect(workspaceSelection.reconcileAuthorizedWorkspaces).toHaveBeenLastCalledWith(
      [{ id: 'workspace-current', label: 'Current' }],
      { tenantId: 'tenant-a', userId: 'user-a' },
      null,
    );
  });

  it('hides stale cards on authorization recheck and reloads from the catch-up boundary', () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    expect(facade.dashboard().status).toBe('ready');

    authorizationRevision.set(1);
    TestBed.flushEffects();

    expect(facade.dashboard()).toMatchObject({ status: 'loading', workspaces: [] });
    expect(workspaceSelection.markAuthorizationPending).toHaveBeenCalledOnce();

    catchUp?.();
    const refreshedCapabilities = http.expectOne('/api/workspaces/capabilities');
    const refreshedList = http.expectOne('/api/workspaces');
    refreshedCapabilities.flush({ data: { canCreate: false } });
    refreshedList.flush([{ ...workspaceDto, id: 'workspace-after-revoke', name: 'Still Authorized' }]);

    expect(facade.dashboard().workspaces.map((workspace) => workspace.id)).toEqual([
      'workspace-after-revoke',
    ]);
    expect(workspaceSelection.reconcileAuthorizedWorkspaces).toHaveBeenLastCalledWith(
      [{ id: 'workspace-after-revoke', label: 'Still Authorized' }],
      { tenantId: 'tenant-a', userId: 'user-a' },
      null,
    );
  });

  it('keeps the Workspace authorization catch-up pending until the list response settles', async () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    authorizationRevision.set(1);
    TestBed.flushEffects();

    const completion = catchUp?.();
    expect(completion).toBeInstanceOf(Promise);
    let settled = false;
    void Promise.resolve(completion).then(() => { settled = true; });
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    await Promise.resolve();
    expect(settled).toBe(false);

    http.expectOne('/api/workspaces').flush([
      { ...workspaceDto, id: 'workspace-reauthorized' },
    ]);
    await completion;

    expect(settled).toBe(true);
    expect(facade.dashboard().workspaces[0].id).toBe('workspace-reauthorized');
  });

  it('cancels and settles an obsolete Workspace catch-up before a second authorization pass', async () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    authorizationRevision.set(1);
    TestBed.flushEffects();

    const firstCompletion = catchUp?.();
    const firstCapabilities = http.expectOne('/api/workspaces/capabilities');
    const firstList = http.expectOne('/api/workspaces');
    authorizationRevision.set(2);
    TestBed.flushEffects();

    expect(firstList.cancelled).toBe(true);
    await firstCompletion;

    const secondCompletion = catchUp?.();
    const secondCapabilities = http.expectOne('/api/workspaces/capabilities');
    const secondList = http.expectOne('/api/workspaces');
    firstCapabilities.flush({ data: { canCreate: false } });
    secondCapabilities.flush({ data: { canCreate: false } });
    secondList.flush([{ ...workspaceDto, id: 'workspace-second-pass' }]);
    await secondCompletion;

    expect(facade.dashboard().workspaces[0].id).toBe('workspace-second-pass');
  });

  it('preserves the authorized dashboard and selection on an ordinary transport reconnect', () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    workspaceSelection.beginLoading.mockClear();
    workspaceSelection.markUnavailable.mockClear();
    workspaceSelection.reconcileAuthorizedWorkspaces.mockClear();

    realtimeState.set('Reconnecting');
    TestBed.flushEffects();
    catchUp?.();

    expect(facade.dashboard()).toMatchObject({
      status: 'ready',
      workspaces: [{ id: 'workspace-1' }],
    });
    expect(workspaceSelection.beginLoading).not.toHaveBeenCalled();
    expect(workspaceSelection.markUnavailable).not.toHaveBeenCalled();
    expect(workspaceSelection.reconcileAuthorizedWorkspaces).not.toHaveBeenCalled();
    http.expectNone('/api/workspaces');
    http.expectNone('/api/workspaces/capabilities');
  });

  it('falls back to an authoritative HTTP reload when realtime reauthorization degrades', () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    authorizationRevision.set(1);
    realtimeState.set('Reconnecting');
    TestBed.flushEffects();
    realtimeState.set('Degraded');
    TestBed.flushEffects();

    const refreshedCapabilities = http.expectOne('/api/workspaces/capabilities');
    const refreshedList = http.expectOne('/api/workspaces');
    refreshedCapabilities.flush({ data: { canCreate: false } });
    refreshedList.flush([{ ...workspaceDto, id: 'workspace-http-authorized' }]);

    expect(facade.dashboard().workspaces.map((workspace) => workspace.id)).toEqual([
      'workspace-http-authorized',
    ]);
    expect(TestBed.inject(RealtimeFacade).runAuthoritativeHttpCatchUps).toHaveBeenCalledOnce();
  });

  it('rehydrates protected HTTP state after a tenant identity change while realtime is disabled', () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    const realtime = TestBed.inject(RealtimeFacade) as unknown as {
      runAuthoritativeHttpCatchUps: ReturnType<typeof vi.fn>;
    };
    realtime.runAuthoritativeHttpCatchUps.mockClear();

    // The disabled rollout keeps the transport degraded, so no SignalR
    // reconnection will invoke feature catch-ups. The new Tenant's fresh
    // Workspace HTTP response must be the recovery authority instead.
    realtimeState.set('Degraded');
    authSessionState.set({
      isAuthenticated: true,
      currentTenant: { tenantId: 'tenant-b' },
      currentUser: { userId: 'user-b' },
      capabilities: [],
    });
    TestBed.flushEffects();

    const refreshedCapabilities = http.expectOne('/api/workspaces/capabilities');
    const refreshedList = http.expectOne('/api/workspaces');
    refreshedCapabilities.flush({ data: { canCreate: false } });
    expect(realtime.runAuthoritativeHttpCatchUps).not.toHaveBeenCalled();
    refreshedList.flush([{ ...workspaceDto, id: 'workspace-tenant-b' }]);

    expect(facade.dashboard().workspaces.map((workspace) => workspace.id)).toEqual([
      'workspace-tenant-b',
    ]);
    expect(workspaceSelection.reconcileAuthorizedWorkspaces).toHaveBeenLastCalledWith(
      [{ id: 'workspace-tenant-b', label: 'Backend Workspace' }],
      { tenantId: 'tenant-b', userId: 'user-b' },
      null,
    );
    expect(realtime.runAuthoritativeHttpCatchUps).toHaveBeenCalledOnce();
  });

  it('keeps reconnect catch-up pending on the same revision until a degraded fallback settles', async () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    authorizationRevision.set(1);
    realtimeState.set('Reconnecting');
    TestBed.flushEffects();
    realtimeState.set('Degraded');
    TestBed.flushEffects();

    const fallbackCapabilities = http.expectOne('/api/workspaces/capabilities');
    const fallbackList = http.expectOne('/api/workspaces');
    realtimeState.set('Reconnecting');
    const completion = catchUp?.();
    expect(completion).toBeInstanceOf(Promise);
    let settled = false;
    void Promise.resolve(completion).then(() => { settled = true; });
    fallbackCapabilities.flush({ data: { canCreate: false } });
    await Promise.resolve();
    expect(settled).toBe(false);

    fallbackList.flush([{ ...workspaceDto, id: 'workspace-fallback-current' }]);
    await completion;

    expect(settled).toBe(true);
    expect(facade.dashboard().workspaces[0].id).toBe('workspace-fallback-current');
  });

  it('canonicalizes the POST body, supplies a printable idempotency key, and blocks double submit', async () => {
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const first = facade.createWorkspace({
      name: '  Research Team  ',
      description: '   ',
      icon: '  🔬  ',
    });
    const post = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    expect(post.request.withCredentials).toBe(true);
    expect(post.request.body).toEqual({
      name: 'Research Team',
      description: null,
      icon: '🔬',
    });
    expect(post.request.body).not.toHaveProperty('id');
    expect(post.request.headers.get('Idempotency-Key')).toMatch(
      /^[\x20-\x7e]{8,128}$/u,
    );

    await expect(
      facade.createWorkspace({ name: 'Research Team', description: null, icon: '🔬' }),
    ).resolves.toBe(false);
    http.expectNone(
      (request) => request.url === '/api/workspaces' && request.method === 'POST' && request !== post.request,
    );

    post.flush(
      { requestId: 'request-unavailable', error: { code: 'DependencyUnavailable' } },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await expect(first).resolves.toBe(false);
    expect(facade.workspaceCreate().status).toBe('error');
  });

  it('reuses the same key for an unchanged uncertain retry and rotates it only when payload changes', async () => {
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const first = facade.createWorkspace({
      name: 'Research Team',
      description: null,
      icon: null,
    });
    const firstPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    const firstKey = firstPost.request.headers.get('Idempotency-Key');
    firstPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await first;

    facade.resetWorkspaceCreatePresentation();
    const second = facade.createWorkspace({
      name: '  Research Team ',
      description: '  ',
      icon: null,
    });
    const secondPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    expect(secondPost.request.headers.get('Idempotency-Key')).toBe(firstKey);
    secondPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await second;

    facade.resetWorkspaceCreatePresentation();
    const changed = facade.createWorkspace({
      name: 'Different Team',
      description: null,
      icon: null,
    });
    const changedPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    expect(changedPost.request.headers.get('Idempotency-Key')).not.toBe(firstKey);
    changedPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await changed;
  });

  it('keeps the idempotency key after a malformed HTTP 201 response', async () => {
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const first = facade.createWorkspace({
      name: 'Research Team',
      description: null,
      icon: null,
    });
    const firstPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    const firstKey = firstPost.request.headers.get('Idempotency-Key');
    firstPost.flush(
      { requestId: 'request-create-1', warnings: [] },
      { status: 201, statusText: 'Created' },
    );
    await expect(first).resolves.toBe(false);
    expect(facade.workspaceCreate()).toMatchObject({ status: 'error' });

    facade.resetWorkspaceCreatePresentation();
    const retry = facade.createWorkspace({
      name: 'Research Team',
      description: null,
      icon: null,
    });
    const retryPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    expect(retryPost.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await retry;
  });

  it('treats a 201 response delivered through the HTTP error channel as uncertain', async () => {
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const first = facade.createWorkspace({
      name: 'Research Team',
      description: null,
      icon: null,
    });
    const firstPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    const firstKey = firstPost.request.headers.get('Idempotency-Key');
    firstPost.error(new ProgressEvent('error'), { status: 201, statusText: 'Created' });

    await expect(first).resolves.toBe(false);
    expect(facade.workspaceCreate()).toMatchObject({
      status: 'error',
      message: expect.stringContaining('may have been created'),
    });

    facade.resetWorkspaceCreatePresentation();
    const retry = facade.createWorkspace({
      name: 'Research Team',
      description: null,
      icon: null,
    });
    const retryPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );
    expect(retryPost.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await retry;
  });

  it('activates only after the committed Workspace appears in the authoritative list', async () => {
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/workspaces');
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const result = facade.createWorkspace({
      name: 'Created Workspace',
      description: null,
      icon: '🔬',
    });
    http
      .expectOne((request) => request.url === '/api/workspaces' && request.method === 'POST')
      .flush(workspaceCreateEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    expect(facade.workspaceCreate()).toMatchObject({
      status: 'submitting',
      createdWorkspaceId,
    });
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto, createdWorkspaceDashboardDto]);

    await expect(result).resolves.toBe(true);
    expect(workspaceSelection.selectWorkspace).toHaveBeenCalledWith(
      createdWorkspaceId,
      expect.any(Function),
    );
    expect(
      workspaceSelection.reconcileAuthorizedWorkspaces.mock.invocationCallOrder.at(-1),
    ).toBeLessThan(workspaceSelection.selectWorkspace.mock.invocationCallOrder[0]);
    expect(facade.workspaceCreate()).toMatchObject({
      status: 'succeeded',
      requestId: 'request-create-1',
      createdWorkspaceId,
    });
  });

  it('uses GET/selection-only recovery when a committed Workspace is initially absent', async () => {
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/workspaces');
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const create = facade.createWorkspace({
      name: 'Created Workspace',
      description: null,
      icon: '🔬',
    });
    http
      .expectOne((request) => request.url === '/api/workspaces' && request.method === 'POST')
      .flush(workspaceCreateEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    await expect(create).resolves.toBe(false);
    expect(facade.workspaceCreate()).toMatchObject({
      status: 'committedPendingActivation',
      createdWorkspaceId,
    });
    expect(workspaceSelection.selectWorkspace).not.toHaveBeenCalled();

    await expect(
      facade.createWorkspace({
        name: 'Created Workspace',
        description: null,
        icon: '🔬',
      }),
    ).resolves.toBe(false);
    http.expectNone((request) => request.url === '/api/workspaces' && request.method === 'POST');

    const retry = facade.retryWorkspaceActivation();
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto, createdWorkspaceDashboardDto]);
    await expect(retry).resolves.toBe(true);
    http.expectNone((request) => request.url === '/api/workspaces' && request.method === 'POST');
    expect(workspaceSelection.selectWorkspace).toHaveBeenCalledOnce();
  });

  it('keeps a committed Workspace pending after list failure and never re-posts it', async () => {
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const create = facade.createWorkspace({
      name: 'Created Workspace',
      description: null,
      icon: '🔬',
    });
    http
      .expectOne((request) => request.url === '/api/workspaces' && request.method === 'POST')
      .flush(workspaceCreateEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http
      .expectOne('/api/workspaces')
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    await expect(create).resolves.toBe(false);

    const retry = facade.retryWorkspaceActivation();
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http
      .expectOne('/api/workspaces')
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    await expect(retry).resolves.toBe(false);
    http.expectNone((request) => request.url === '/api/workspaces' && request.method === 'POST');
    expect(facade.workspaceCreate().status).toBe('committedPendingActivation');
  });

  it('follows an authorization-revision replacement list during post-create reconciliation', async () => {
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/workspaces');
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const create = facade.createWorkspace({
      name: 'Created Workspace',
      description: null,
      icon: '🔬',
    });
    http
      .expectOne((request) => request.url === '/api/workspaces' && request.method === 'POST')
      .flush(workspaceCreateEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    const obsoleteCapabilities = http.expectOne('/api/workspaces/capabilities');
    const obsoleteList = http.expectOne('/api/workspaces');

    authorizationRevision.set(1);
    TestBed.flushEffects();
    expect(obsoleteList.cancelled).toBe(true);
    obsoleteCapabilities.flush(workspaceCapabilities(true));
    await Promise.resolve();
    await Promise.resolve();

    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto, createdWorkspaceDashboardDto]);
    await expect(create).resolves.toBe(true);
    expect(workspaceSelection.selectWorkspace).toHaveBeenCalledWith(
      createdWorkspaceId,
      expect.any(Function),
    );
  });

  it('drops a late create response when the authenticated identity changes', async () => {
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    const create = facade.createWorkspace({
      name: 'Created Workspace',
      description: null,
      icon: '🔬',
    });
    const post = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );

    authSessionState.set({
      isAuthenticated: true,
      currentTenant: { tenantId: 'tenant-a' },
      currentUser: { userId: 'user-b' },
      capabilities: [],
    });
    TestBed.flushEffects();
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    post.flush(workspaceCreateEnvelope, { status: 201, statusText: 'Created' });
    await expect(create).resolves.toBe(false);
    expect(workspaceSelection.selectWorkspace).not.toHaveBeenCalled();
    expect(facade.workspaceCreate().status).toBe('idle');
  });

  it('fails create closed without backend capability and refreshes it after a 403', async () => {
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    await expect(
      facade.createWorkspace({ name: 'Denied', description: null, icon: null }),
    ).resolves.toBe(false);
    http.expectNone((request) => request.url === '/api/workspaces' && request.method === 'POST');

    void facade.loadWorkspaces();
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);
    facade.resetWorkspaceCreatePresentation();
    const denied = facade.createWorkspace({ name: 'Denied', description: null, icon: null });
    const deniedPost = http.expectOne(
      (request) => request.url === '/api/workspaces' && request.method === 'POST',
    );

    // A concurrent list refresh may already have a capability response in
    // flight when the command proves that authority was denied. That older
    // response must not re-enable the action after the denial refresh.
    void facade.loadWorkspaces();
    const staleCapability = http.expectOne('/api/workspaces/capabilities');
    const concurrentList = http.expectOne('/api/workspaces');
    deniedPost.flush(
        {
          requestId: 'request-denied',
          error: {
            code: 'CapabilityDenied',
            message: 'You are not allowed to create workspaces.',
            target: 'workspace',
            details: [],
            redactionApplied: false,
          },
          status: 403,
        },
        { status: 403, statusText: 'Forbidden' },
      );
    await expect(denied).resolves.toBe(false);
    expect(facade.dashboard().pageCapabilities).toEqual([]);
    http.expectOne('/api/workspaces/capabilities').flush({ data: { canCreate: false } });
    staleCapability.flush(workspaceCapabilities(true));
    concurrentList.flush([workspaceDto]);
    expect(facade.dashboard().pageCapabilities).toEqual([]);
    expect(facade.workspaceCreate()).toMatchObject({
      status: 'error',
      requestId: 'request-denied',
    });
  });

  it('maps safe validation targets and does not send invalid local fields', async () => {
    http.expectOne('/api/workspaces/capabilities').flush(workspaceCapabilities(true));
    http.expectOne('/api/workspaces').flush([workspaceDto]);

    await expect(
      facade.createWorkspace({ name: '   ', description: null, icon: null }),
    ).resolves.toBe(false);
    expect(facade.workspaceCreate().fieldErrors).toEqual([
      { field: 'name', message: 'Enter a Workspace name.', messageKey: 'workspace.nameRequired' },
    ]);
    http.expectNone((request) => request.url === '/api/workspaces' && request.method === 'POST');

    facade.resetWorkspaceCreatePresentation();
    const serverValidation = facade.createWorkspace({
      name: 'Valid locally',
      description: null,
      icon: null,
    });
    http
      .expectOne((request) => request.url === '/api/workspaces' && request.method === 'POST')
      .flush(
        {
          requestId: 'request-validation',
          error: {
            code: 'ValidationFailed',
            message: 'Internal validation text is not presentation authority.',
            target: 'body.name',
            details: [],
            redactionApplied: false,
          },
          status: 400,
        },
        { status: 400, statusText: 'Bad Request' },
      );
    await serverValidation;
    expect(facade.workspaceCreate()).toMatchObject({
      status: 'error',
      requestId: 'request-validation',
      fieldErrors: [{ field: 'name', message: 'Review the Workspace name.' }],
      message: 'Review the highlighted fields and try again.',
    });
  });
});
