import { ErrorHandler, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, AuthSessionFacade, AuthSessionSnapshot, DEFAULT_AUTH_SESSION } from '../auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../feature-flags/frontend-feature-flags.service';
import { NotificationOpenContextService } from '../notifications/notification-open-context.service';
import { ActiveWorkspaceFacade } from '../workspace/active-workspace.facade';
import { DurableRealtimeEvent, RealtimeSubscriptionRequest, RealtimeSubscriptionResult } from './realtime.models';
import { COGLATAS_REALTIME_TRANSPORT, RealtimeTransport, RealtimeTransportStatus } from './realtime-transport';
import { RealtimeFacade } from './realtime.facade';

// The deterministic default Tenant is a canonical, non-empty .NET Guid but
// intentionally does not encode RFC UUID version/variant bits.
const TENANT_ID = '11111111-1111-1111-1111-111111111111';
const RESOURCE_ID = '22222222-2222-4222-8222-222222222222';
const ACTIVE_WORKSPACE = { id: RESOURCE_ID, label: 'Current workspace' };

class FakeRealtimeTransport implements RealtimeTransport {
  readonly events = new Subject<unknown>();
  readonly invalidations = new Subject<void>();
  readonly statuses = new Subject<RealtimeTransportStatus>();
  readonly durableEvents$ = this.events.asObservable();
  readonly authorizationInvalidations$ = this.invalidations.asObservable();
  readonly statuses$ = this.statuses.asObservable();
  readonly subscribed: RealtimeSubscriptionRequest[] = [];
  readonly unsubscribed: RealtimeSubscriptionRequest[] = [];
  startCalls = 0;
  stopCalls = 0;
  result: RealtimeSubscriptionResult = { allowed: true, code: 'Subscribed' };
  deniedSubscriptionType: RealtimeSubscriptionRequest['subscriptionType'] | null = null;

  async start(): Promise<void> { this.startCalls += 1; }
  async stop(): Promise<void> { this.stopCalls += 1; }
  async subscribe(request: RealtimeSubscriptionRequest): Promise<RealtimeSubscriptionResult> {
    this.subscribed.push(request);
    if (request.subscriptionType === this.deniedSubscriptionType)
      {return { allowed: false, code: 'Forbidden' };}
    return this.result;
  }
  async unsubscribe(request: RealtimeSubscriptionRequest): Promise<RealtimeSubscriptionResult> {
    this.unsubscribed.push(request);
    return { allowed: true, code: 'Unsubscribed' };
  }
}

describe('RealtimeFacade', () => {
  let facade: RealtimeFacade;
  let transport: FakeRealtimeTransport;
  let auth: AuthSessionFacade;
  let flags: FrontendFeatureFlagsService;
  let activeWorkspace: ActiveWorkspaceFacade;
  let notificationOpenContext: NotificationOpenContextService;

  beforeEach(() => {
    transport = new FakeRealtimeTransport();
    TestBed.configureTestingModule({
      providers: [
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: { ...DEFAULT_AUTH_SESSION, status: 'anonymous', currentUser: null, currentTenant: null, isAuthenticated: false } },
        { provide: COGLATAS_REALTIME_TRANSPORT, useValue: transport }
      ]
    });
    facade = TestBed.inject(RealtimeFacade);
    auth = TestBed.inject(AuthSessionFacade);
    flags = TestBed.inject(FrontendFeatureFlagsService);
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    notificationOpenContext = TestBed.inject(NotificationOpenContextService);
  });

  afterEach(() => TestBed.resetTestingModule());

  it('keeps HTTP-only mode when the rollout flag is disabled', async () => {
    auth.setMockSession(activeSession());
    await settle();

    expect(transport.startCalls).toBe(0);
    expect(facade.connectionState()).toBe('Degraded');
  });

  it('RealtimeDisabledDoesNotClearActiveWorkspace', async () => {
    await settle();
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);

    auth.setMockSession(activeSession());
    await settle();

    expect(activeWorkspace.activeWorkspace()).toEqual(ACTIVE_WORKSPACE);
    expect(notificationOpenContext.takeDigestWorkspace()).toBe(RESOURCE_ID);
    expect(transport.startCalls).toBe(0);
  });

  it('TenantHydrationDoesNotClearActiveWorkspaceOrProtectedState', async () => {
    await settle();
    flags.setForTesting({ 'realtime.signalR': true });
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);
    let clearCalls = 0;
    facade.registerProtectedStateClearer('files-http-state', () => { clearCalls += 1; });

    // AuthSession patches the authenticated user and its workspace before the
    // current-Tenant request resolves.
    auth.setMockSession(tenantHydratingSession());
    await settle();

    expect(activeWorkspace.activeWorkspace()).toEqual(ACTIVE_WORKSPACE);
    expect(notificationOpenContext.takeDigestWorkspace()).toBe(RESOURCE_ID);
    expect(clearCalls).toBe(0);
    expect(transport.startCalls).toBe(0);
    expect(facade.connectionState()).toBe('Degraded');

    auth.setMockSession(activeSession());
    await waitForConnection(facade);

    expect(activeWorkspace.activeWorkspace()).toEqual(ACTIVE_WORKSPACE);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('RealtimeDisabledDoesNotInvokeProtectedStateClearers', async () => {
    await settle();
    let clearCalls = 0;
    facade.registerProtectedStateClearer('files-http-state', () => { clearCalls += 1; });

    auth.setMockSession(activeSession());
    await settle();

    expect(clearCalls).toBe(0);
  });

  it('RealtimeDisabledPreservesDesiredSubscriptions', async () => {
    await settle();
    facade.registerSubscription('project-detail', { subscriptionType: 'project', resourceId: RESOURCE_ID });

    auth.setMockSession(activeSession());
    await settle();
    expect(transport.startCalls).toBe(0);

    flags.setForTesting({ 'realtime.signalR': true });
    await waitForConnection(facade);

    expect(transport.subscribed).toEqual([
      { subscriptionType: 'user' },
      { subscriptionType: 'project', resourceId: RESOURCE_ID },
    ]);
  });

  it('RealtimeDisabledStillAllowsHttpOnlyFeatureState', async () => {
    await settle();
    let filesFromHttp = ['file-1'];
    facade.registerProtectedStateClearer('files-http-state', () => { filesFromHttp = []; });

    auth.setMockSession(activeSession());
    await settle();

    expect(filesFromHttp).toEqual(['file-1']);
    expect(facade.connectionState()).toBe('Degraded');
  });

  it('RealtimeEnableAfterDisabledReauthorizesDesiredSubscriptions', async () => {
    facade.registerSubscription('workspace-shell', { subscriptionType: 'workspace', resourceId: RESOURCE_ID });
    await enableAndAuthenticate();
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'workspace')).toHaveLength(1);

    flags.setForTesting({ 'realtime.signalR': false });
    await settle();
    expect(facade.connectionState()).toBe('Degraded');

    flags.setForTesting({ 'realtime.signalR': true });
    await waitForConnection(facade);

    expect(transport.subscribed.filter((request) => request.subscriptionType === 'workspace')).toEqual([
      { subscriptionType: 'workspace', resourceId: RESOURCE_ID },
      { subscriptionType: 'workspace', resourceId: RESOURCE_ID },
    ]);
  });

  it('owns exactly one connection and subscribes to the server-derived current user target', async () => {
    await enableAndAuthenticate();

    expect(transport.startCalls).toBe(1);
    expect(transport.subscribed).toEqual([{ subscriptionType: 'user' }]);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('reauthorizes subscriptions and runs catch-up before returning to Connected', async () => {
    const order: string[] = [];
    facade.registerSubscription('conversation-page', { subscriptionType: 'conversation', resourceId: RESOURCE_ID });
    facade.registerCatchUp('conversation-page', async () => { order.push('catch-up'); });
    await enableAndAuthenticate();

    expect(transport.subscribed).toEqual([
      { subscriptionType: 'user' },
      { subscriptionType: 'conversation', resourceId: RESOURCE_ID }
    ]);
    expect(order).toEqual(['catch-up']);
    expect(facade.connectionState()).toBe('Connected');

    transport.statuses.next('reconnecting');
    expect(facade.connectionState()).toBe('Reconnecting');
    transport.statuses.next('reconnected');
    await settle();
    expect(transport.subscribed.slice(-2)).toEqual([
      { subscriptionType: 'user' },
      { subscriptionType: 'conversation', resourceId: RESOURCE_ID }
    ]);
    expect(order).toEqual(['catch-up', 'catch-up']);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('authorizes a resource registered while synchronization is already in progress', async () => {
    const firstProjectId = '33333333-3333-4333-8333-333333333333';
    const lateConversationId = '44444444-4444-4444-8444-444444444444';
    let releaseProject!: () => void;
    const projectGate = new Promise<void>((resolve) => { releaseProject = resolve; });
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType === 'project' && request.resourceId === firstProjectId) {
        transport.subscribed.push(request);
        await projectGate;
        return { allowed: true, code: 'Subscribed' };
      }
      return originalSubscribe(request);
    };
    facade.registerSubscription('project-route', {
      subscriptionType: 'project',
      resourceId: firstProjectId,
    });

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await vi.waitFor(() => expect(transport.subscribed).toContainEqual({
      subscriptionType: 'project',
      resourceId: firstProjectId,
    }));
    expect(facade.connectionState()).not.toBe('Connected');

    facade.registerSubscription('conversation-route', {
      subscriptionType: 'conversation',
      resourceId: lateConversationId,
    });
    releaseProject();
    await waitForConnection(facade);

    expect(transport.subscribed).toContainEqual({
      subscriptionType: 'conversation',
      resourceId: lateConversationId,
    });
  });

  it('discards a synchronization invalidated during resource authorization and starts a fresh epoch', async () => {
    let releaseFirstProjectAuthorization!: () => void;
    const firstProjectAuthorization = new Promise<void>((resolve) => {
      releaseFirstProjectAuthorization = resolve;
    });
    let projectAttempts = 0;
    let catchUps = 0;
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType !== 'project') {
        return originalSubscribe(request);
      }

      transport.subscribed.push(request);
      projectAttempts++;
      if (projectAttempts === 1) {
        await firstProjectAuthorization;
      }
      return { allowed: true, code: 'Subscribed' };
    };
    facade.registerSubscription('project-route', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID,
    });
    facade.registerCatchUp('project-route', () => {
      catchUps++;
    });

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await waitForSubscriptionCount(transport, 'project', 1);

    transport.invalidations.next();
    expect(facade.connectionState()).toBe('Reconnecting');
    releaseFirstProjectAuthorization();

    await waitForSubscriptionCount(transport, 'project', 2);
    await waitForConnection(facade);

    expect(transport.startCalls).toBe(2);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'user')).toHaveLength(2);
    expect(projectAttempts).toBe(2);
    expect(catchUps).toBe(1);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('stops the transport for a stale allowed user group before starting the fresh epoch', async () => {
    let releaseFirstUserAuthorization!: () => void;
    const firstUserAuthorization = new Promise<void>((resolve) => {
      releaseFirstUserAuthorization = resolve;
    });
    let userAttempts = 0;
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType !== 'user') {
        return originalSubscribe(request);
      }

      transport.subscribed.push(request);
      userAttempts++;
      if (userAttempts === 1) {
        await firstUserAuthorization;
      }
      return { allowed: true, code: 'Subscribed' };
    };

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await waitForSubscriptionCount(transport, 'user', 1);

    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    releaseFirstUserAuthorization();

    await waitForSubscriptionCount(transport, 'user', 2);
    await waitForConnection(facade);

    expect(transport.unsubscribed).not.toContainEqual({ subscriptionType: 'user' });
    expect(transport.stopCalls).toBe(1);
    expect(transport.startCalls).toBe(2);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('removes a stale Connected-time grant before reauthorizing the same group', async () => {
    await enableAndAuthenticate();
    let releaseFirstProjectAuthorization!: () => void;
    const firstProjectAuthorization = new Promise<void>((resolve) => {
      releaseFirstProjectAuthorization = resolve;
    });
    const order: string[] = [];
    let projectAttempts = 0;
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType !== 'project') {
        return originalSubscribe(request);
      }

      transport.subscribed.push(request);
      projectAttempts++;
      order.push(`subscribe-${projectAttempts}`);
      if (projectAttempts === 1) {
        await firstProjectAuthorization;
      }
      return { allowed: true, code: 'Subscribed' };
    };
    transport.unsubscribe = async (request) => {
      transport.unsubscribed.push(request);
      if (request.subscriptionType === 'project') {
        order.push('unsubscribe');
      }
      return { allowed: true, code: 'Unsubscribed' };
    };

    facade.registerSubscription('late-project', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID,
    });
    await waitForSubscriptionCount(transport, 'project', 1);

    transport.invalidations.next();
    await waitForSubscriptionCount(transport, 'user', 2);
    await settle();

    expect(projectAttempts).toBe(1);
    expect(facade.connectionState()).not.toBe('Connected');

    releaseFirstProjectAuthorization();
    await waitForSubscriptionCount(transport, 'project', 2);
    await waitForConnection(facade);

    expect(order).toEqual(['subscribe-1', 'unsubscribe', 'subscribe-2']);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('resumes after the rollout flag is re-enabled while a stale authorization is still pending', async () => {
    let releaseFirstProjectAuthorization!: () => void;
    const firstProjectAuthorization = new Promise<void>((resolve) => {
      releaseFirstProjectAuthorization = resolve;
    });
    let projectAttempts = 0;
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType !== 'project') {
        return originalSubscribe(request);
      }

      transport.subscribed.push(request);
      projectAttempts++;
      if (projectAttempts === 1) {
        await firstProjectAuthorization;
      }
      return { allowed: true, code: 'Subscribed' };
    };
    facade.registerSubscription('project-route', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID,
    });

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await waitForSubscriptionCount(transport, 'project', 1);

    flags.setForTesting({ 'realtime.signalR': false });
    await settle();
    flags.setForTesting({ 'realtime.signalR': true });
    await settle();
    releaseFirstProjectAuthorization();

    await waitForSubscriptionCount(transport, 'project', 2);
    await waitForConnection(facade);

    expect(transport.startCalls).toBe(2);
    expect(projectAttempts).toBe(2);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('does not publish Connected from a catch-up invalidated by browser offline', async () => {
    const online = vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);
    let releaseFirstCatchUp!: () => void;
    const firstCatchUp = new Promise<void>((resolve) => {
      releaseFirstCatchUp = resolve;
    });
    let catchUps = 0;
    facade.registerCatchUp('pending-feature', async () => {
      catchUps++;
      if (catchUps === 1) {
        await firstCatchUp;
      }
    });

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await vi.waitFor(() => expect(catchUps).toBe(1));

    online.mockReturnValue(false);
    window.dispatchEvent(new Event('offline'));
    releaseFirstCatchUp();
    await settle();

    expect(facade.connectionState()).toBe('Offline');
    expect(catchUps).toBe(1);

    online.mockReturnValue(true);
    window.dispatchEvent(new Event('online'));
    await waitForConnection(facade);

    expect(transport.startCalls).toBe(2);
    expect(catchUps).toBe(2);
    expect(facade.connectionState()).toBe('Connected');
    online.mockRestore();
  });

  it('runs a catch-up registered during synchronization after its new subscription denial is known', async () => {
    const deniedProjectId = '55555555-5555-4555-8555-555555555555';
    const observedDenials: string[][] = [];
    transport.deniedSubscriptionType = 'project';
    facade.registerCatchUp('mounting-feature', () => {
      facade.registerSubscription('late-project', {
        subscriptionType: 'project',
        resourceId: deniedProjectId,
      });
      facade.registerCatchUp('late-project', (context) => {
        observedDenials.push([...context.deniedOwners]);
      });
    });

    await enableAndAuthenticate();

    expect(observedDenials).toEqual([['late-project']]);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('does not carry a removed resource denial into an allowed replacement with the same owner', async () => {
    const deniedProjectId = '56565656-5656-4656-8656-565656565656';
    const allowedProjectId = '57575757-5757-4757-8757-575757575757';
    let releaseEarlierCatchUp!: () => void;
    const earlierCatchUp = new Promise<void>((resolve) => { releaseEarlierCatchUp = resolve; });
    let signalEarlierCatchUp!: () => void;
    const earlierCatchUpStarted = new Promise<void>((resolve) => { signalEarlierCatchUp = resolve; });
    const observed: Array<{ route: string; denied: boolean }> = [];
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType === 'project' && request.resourceId === deniedProjectId) {
        transport.subscribed.push(request);
        return { allowed: false, code: 'Forbidden' };
      }
      return originalSubscribe(request);
    };

    const releaseDeniedSubscription = facade.registerSubscription('project-detail', {
      subscriptionType: 'project',
      resourceId: deniedProjectId,
    });
    facade.registerCatchUp('earlier-feature', async () => {
      signalEarlierCatchUp();
      await earlierCatchUp;
    });
    const releaseDeniedCatchUp = facade.registerCatchUp('project-detail', (context) => {
      observed.push({ route: 'denied-project', denied: context.deniedOwners.has('project-detail') });
    });

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await earlierCatchUpStarted;

    releaseDeniedSubscription();
    releaseDeniedCatchUp();
    facade.registerSubscription('project-detail', {
      subscriptionType: 'project',
      resourceId: allowedProjectId,
    });
    facade.registerCatchUp('project-detail', (context) => {
      observed.push({ route: 'allowed-project', denied: context.deniedOwners.has('project-detail') });
    });
    releaseEarlierCatchUp();

    await waitForSubscriptionCount(transport, 'project', 2);
    await waitForConnection(facade);

    expect(observed).toEqual([{ route: 'allowed-project', denied: false }]);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('ReconnectRestoresDesiredProjectSubscription', async () => {
    facade.registerSubscription('project-detail', { subscriptionType: 'project', resourceId: RESOURCE_ID });
    await enableAndAuthenticate();

    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    await waitForConnection(facade);

    expect(transport.subscribed.filter((request) => request.subscriptionType === 'project')).toEqual([
      { subscriptionType: 'project', resourceId: RESOURCE_ID },
      { subscriptionType: 'project', resourceId: RESOURCE_ID },
    ]);
  });

  it('ReconnectRestoresDesiredWorkspaceSubscription', async () => {
    facade.registerSubscription('workspace-shell', { subscriptionType: 'workspace', resourceId: RESOURCE_ID });
    await enableAndAuthenticate();

    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    await waitForConnection(facade);

    expect(transport.subscribed.filter((request) => request.subscriptionType === 'workspace')).toEqual([
      { subscriptionType: 'workspace', resourceId: RESOURCE_ID },
      { subscriptionType: 'workspace', resourceId: RESOURCE_ID },
    ]);
  });

  it('TransportReconnectDoesNotClearActiveWorkspace', async () => {
    await enableAndAuthenticate();
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);
    const authorizationRevision = facade.authorizationRevision();

    transport.statuses.next('reconnecting');
    expect(facade.authorizationRevision()).toBe(authorizationRevision);
    transport.statuses.next('reconnected');
    await waitForConnection(facade);

    expect(facade.authorizationRevision()).toBe(authorizationRevision);
    expect(activeWorkspace.activeWorkspace()).toEqual(ACTIVE_WORKSPACE);
    expect(notificationOpenContext.takeDigestWorkspace()).toBe(RESOURCE_ID);
  });

  it('AuthorizationInvalidationAdvancesAuthorizationRevisionSynchronously', async () => {
    await enableAndAuthenticate();
    const authorizationRevision = facade.authorizationRevision();

    transport.invalidations.next();

    expect(facade.authorizationRevision()).toBe(authorizationRevision + 1);
    expect(facade.connectionState()).toBe('Reconnecting');
    await waitForConnection(facade);
  });

  it('HubDegradationDoesNotClearActiveWorkspace', async () => {
    await enableAndAuthenticate();
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);

    transport.statuses.next('closed');
    await settle();

    expect(facade.connectionState()).toBe('Degraded');
    expect(activeWorkspace.activeWorkspace()).toEqual(ACTIVE_WORKSPACE);
    expect(notificationOpenContext.takeDigestWorkspace()).toBe(RESOURCE_ID);
  });

  it('reports a denied subscription owner to catch-up before reconnect completes', async () => {
    const deniedOwners: string[][] = [];
    facade.registerSubscription('project-detail', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID
    });
    facade.registerCatchUp('project-detail', (context) => {
      deniedOwners.push([...context.deniedOwners]);
    });
    await enableAndAuthenticate();

    transport.deniedSubscriptionType = 'project';
    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    await settle();
    await settle();

    expect(deniedOwners).toEqual([[], ['project-detail']]);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('AuthorizationInvalidationPreservesDesiredSubscriptions', async () => {
    const deniedOwners: string[][] = [];
    facade.registerSubscription('project-detail', { subscriptionType: 'project', resourceId: RESOURCE_ID });
    facade.registerCatchUp('project-detail', (context) => {
      deniedOwners.push([...context.deniedOwners]);
    });
    await enableAndAuthenticate();

    transport.deniedSubscriptionType = 'project';
    transport.invalidations.next();
    await waitForConnection(facade);

    expect(transport.subscribed.filter((request) => request.subscriptionType === 'project')).toHaveLength(2);
    expect(deniedOwners).toEqual([[], ['project-detail']]);

    transport.deniedSubscriptionType = null;
    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    await waitForConnection(facade);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'project')).toHaveLength(3);
  });

  it('DeniedSubscriptionDoesNotRestoreProtectedState', async () => {
    let protectedTitle = 'Restricted task title';
    const deniedOwners: string[][] = [];
    facade.registerSubscription('project-detail', { subscriptionType: 'project', resourceId: RESOURCE_ID });
    facade.registerProtectedStateClearer('project-detail', () => { protectedTitle = ''; });
    facade.registerCatchUp('project-detail', (context) => {
      deniedOwners.push([...context.deniedOwners]);
      if (!context.deniedOwners.has('project-detail')) {protectedTitle = 'refetched title';}
    });
    await enableAndAuthenticate();
    protectedTitle = 'Restricted task title';

    transport.deniedSubscriptionType = 'project';
    transport.invalidations.next();
    await waitForConnection(facade);

    expect(deniedOwners.at(-1)).toEqual(['project-detail']);
    expect(protectedTitle).toBe('');
  });

  it('notifies every denied owner even when an earlier catch-up fails', async () => {
    const deniedOwners: string[][] = [];
    const diagnostics: string[] = [];
    facade.diagnostics$.subscribe((diagnostic) => diagnostics.push(diagnostic.code));
    facade.registerSubscription('project-detail', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID
    });
    await enableAndAuthenticate();
    facade.registerCatchUp('failing-feature', async () => {
      throw new Error('HTTP catch-up failed.');
    });
    facade.registerCatchUp('project-detail', (context) => {
      deniedOwners.push([...context.deniedOwners]);
    });

    transport.deniedSubscriptionType = 'project';
    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    await settle();
    await settle();

    expect(deniedOwners).toEqual([['project-detail']]);
    expect(diagnostics).toContain('CatchUpFailed');
  });

  it('does not send guessed group names and only maps approved logical targets to the transport', async () => {
    facade.registerSubscription('project-page', { subscriptionType: 'project', resourceId: RESOURCE_ID });
    await enableAndAuthenticate();

    expect(JSON.stringify(transport.subscribed)).not.toContain('project:');
    expect(JSON.stringify(transport.subscribed)).not.toContain('tenant:');
  });

  it('removes the final subscription owner and does not leak route subscriptions', async () => {
    await enableAndAuthenticate();
    const removeA = facade.registerSubscription('route-a', { subscriptionType: 'workspace', resourceId: RESOURCE_ID });
    const removeB = facade.registerSubscription('route-b', { subscriptionType: 'workspace', resourceId: RESOURCE_ID });
    await settle();
    removeA();
    expect(transport.unsubscribed).toEqual([]);
    removeB();
    await settle();
    expect(transport.unsubscribed).toEqual([{ subscriptionType: 'workspace', resourceId: RESOURCE_ID }]);
  });

  it('WorkspaceBoundaryClearsProtectedStateAndResourceSubscriptionsWithoutStoppingSameTenantTransport', async () => {
    const projectId = '33333333-3333-4333-8333-333333333333';
    const conversationId = '44444444-4444-4444-8444-444444444444';
    const clearOrder: string[] = [];
    let projectCatchUps = 0;
    let tenantCatchUps = 0;
    facade.registerProtectedStateClearer('protected-feature', () => clearOrder.push('clear'));
    facade.registerSubscription('workspace-shell', { subscriptionType: 'workspace', resourceId: RESOURCE_ID });
    facade.registerSubscription('project-detail', { subscriptionType: 'project', resourceId: projectId });
    facade.registerSubscription('conversation-page', { subscriptionType: 'conversation', resourceId: conversationId });
    facade.registerSubscription('tenant-events', { subscriptionType: 'tenant' });
    facade.registerCatchUp('project-detail', () => { projectCatchUps += 1; });
    facade.registerCatchUp('tenant-events', () => { tenantCatchUps += 1; });
    await enableAndAuthenticate();
    await settle();
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);
    const stopCallsBeforeBoundary = transport.stopCalls;

    facade.clearForWorkspaceBoundary();
    await settle();

    expect(clearOrder).toEqual(['clear']);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(notificationOpenContext.takeDigestWorkspace()).toBeNull();
    expect(facade.connectionState()).toBe('Connected');
    expect(transport.stopCalls).toBe(stopCallsBeforeBoundary);
    expect(transport.unsubscribed).toEqual(expect.arrayContaining([
      { subscriptionType: 'workspace', resourceId: RESOURCE_ID },
      { subscriptionType: 'project', resourceId: projectId },
      { subscriptionType: 'conversation', resourceId: conversationId },
    ]));
    expect(transport.unsubscribed).not.toContainEqual({ subscriptionType: 'tenant' });

    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    await waitForConnection(facade);

    expect(transport.subscribed.filter((request) => request.subscriptionType === 'workspace')).toHaveLength(1);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'project')).toHaveLength(1);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'conversation')).toHaveLength(1);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'tenant')).toHaveLength(2);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'user')).toHaveLength(2);
    expect(projectCatchUps).toBe(1);
    expect(tenantCatchUps).toBe(2);
  });

  it('WorkspaceBoundaryUnsubscribesResourceAuthorizationThatCompletesAfterIntentWasCleared', async () => {
    const lateWorkspaceId = '66666666-6666-4666-8666-666666666666';
    await enableAndAuthenticate();

    facade.registerSubscription('late-workspace-route', {
      subscriptionType: 'workspace',
      resourceId: lateWorkspaceId,
    });
    facade.clearForWorkspaceBoundary();
    await settle();

    expect(transport.unsubscribed).toContainEqual({
      subscriptionType: 'workspace',
      resourceId: lateWorkspaceId,
    });

    transport.statuses.next('reconnecting');
    transport.statuses.next('reconnected');
    await waitForConnection(facade);
    expect(transport.subscribed.filter((request) =>
      request.subscriptionType === 'workspace' && request.resourceId === lateWorkspaceId
    )).toHaveLength(1);
  });

  it('clears subscriptions and bounded state at tenant, logout, session-expiry, and authorization boundaries', async () => {
    await enableAndAuthenticate();
    facade.registerSubscription('route', { subscriptionType: 'project', resourceId: RESOURCE_ID });
    facade.clearForTenantBoundary();
    await settle();
    expect(transport.stopCalls).toBeGreaterThan(0);

    auth.markSessionExpired();
    await settle();
    expect(facade.connectionState()).toBe('Degraded');
    transport.invalidations.next();
    expect(facade.connectionState()).toBe('Reconnecting');
  });

  it('LogoutDoesNotReusePreviousTenantSubscriptions', async () => {
    facade.registerSubscription('project-detail', { subscriptionType: 'project', resourceId: RESOURCE_ID });
    await enableAndAuthenticate();
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'project')).toHaveLength(1);

    auth.markSessionExpired();
    await settle();
    auth.setMockSession(activeSession());
    await waitForConnection(facade);

    expect(transport.subscribed.filter((request) => request.subscriptionType === 'project')).toHaveLength(1);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'user')).toHaveLength(2);
  });

  it('LogoutAbsorbsAConnectedTimeAuthorizationRejectedByTransportStop', async () => {
    await enableAndAuthenticate();
    const handledErrors: unknown[] = [];
    vi.spyOn(TestBed.inject(ErrorHandler), 'handleError').mockImplementation((error) => {
      handledErrors.push(error);
    });
    let rejectAuthorization!: (reason: Error) => void;
    let signalAuthorizationStarted!: () => void;
    const authorizationStarted = new Promise<void>((resolve) => {
      signalAuthorizationStarted = resolve;
    });
    const pendingAuthorization = new Promise<RealtimeSubscriptionResult>((_, reject) => {
      rejectAuthorization = reject;
    });
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType !== 'project') {
        return originalSubscribe(request);
      }

      transport.subscribed.push(request);
      signalAuthorizationStarted();
      return pendingAuthorization;
    };

    facade.registerSubscription('pending-project', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID,
    });
    await authorizationStarted;
    auth.markSessionExpired();
    rejectAuthorization(new Error('Invocation canceled due to the underlying connection being closed.'));
    await settle();
    await settle();

    expect(handledErrors).toEqual([]);
    expect(transport.startCalls).toBe(1);
    expect(transport.stopCalls).toBe(1);
    expect(facade.connectionState()).toBe('Degraded');
  });

  it('ConnectedTimeAuthorizationFailureResetsAndReauthorizesTheTransport', async () => {
    await enableAndAuthenticate();
    const handledErrors: unknown[] = [];
    vi.spyOn(TestBed.inject(ErrorHandler), 'handleError').mockImplementation((error) => {
      handledErrors.push(error);
    });
    let projectAttempts = 0;
    const originalSubscribe = transport.subscribe.bind(transport);
    transport.subscribe = async (request) => {
      if (request.subscriptionType !== 'project') {
        return originalSubscribe(request);
      }

      projectAttempts++;
      if (projectAttempts === 1) {
        transport.subscribed.push(request);
        throw new Error('The connection closed before the authorization response completed.');
      }
      return originalSubscribe(request);
    };

    facade.registerSubscription('recovering-project', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID,
    });

    await vi.waitFor(() => expect(transport.startCalls).toBe(2));
    await waitForConnection(facade);

    expect(projectAttempts).toBe(2);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'user')).toHaveLength(2);
    expect(transport.stopCalls).toBe(1);
    expect(handledErrors).toEqual([]);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('ResourceReleaseFailureResetsTheLiveTransportWithoutRestoringTheReleasedGroup', async () => {
    await enableAndAuthenticate();
    const handledErrors: unknown[] = [];
    vi.spyOn(TestBed.inject(ErrorHandler), 'handleError').mockImplementation((error) => {
      handledErrors.push(error);
    });
    transport.unsubscribe = async (request) => {
      transport.unsubscribed.push(request);
      throw new Error('Invocation canceled due to the underlying connection being closed.');
    };
    const release = facade.registerSubscription('closing-project', {
      subscriptionType: 'project',
      resourceId: RESOURCE_ID,
    });
    await waitForSubscriptionCount(transport, 'project', 1);

    release();
    await vi.waitFor(() => expect(transport.startCalls).toBe(2));
    await waitForConnection(facade);

    expect(transport.unsubscribed).toContainEqual({
      subscriptionType: 'project',
      resourceId: RESOURCE_ID,
    });
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'project')).toHaveLength(1);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'user')).toHaveLength(2);
    expect(transport.stopCalls).toBe(1);
    expect(handledErrors).toEqual([]);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('LogoutClearsActiveWorkspaceAndProtectedState', async () => {
    await enableAndAuthenticate();
    let protectedFiles = ['file-1'];
    facade.registerProtectedStateClearer('files-http-state', () => { protectedFiles = []; });
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);

    auth.markSessionExpired();
    await settle();

    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(notificationOpenContext.takeDigestWorkspace()).toBeNull();
    expect(protectedFiles).toEqual([]);
    expect(facade.connectionState()).toBe('Degraded');
  });

  it('LogoutClearersDoNotBecomeRealtimeEffectDependencies', async () => {
    await enableAndAuthenticate();
    const featureState = signal(0);
    let clearCalls = 0;
    facade.registerProtectedStateClearer('feature-state', () => {
      clearCalls += 1;
      if (featureState() === 0) {
        featureState.set(1);
      }
    });

    auth.markSessionExpired();
    await settle();

    expect(featureState()).toBe(1);
    expect(clearCalls).toBe(1);
  });

  it('TenantSwitchDoesNotReusePreviousTenantSubscriptions', async () => {
    facade.registerSubscription('workspace-shell', { subscriptionType: 'workspace', resourceId: RESOURCE_ID });
    await enableAndAuthenticate();
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'workspace')).toHaveLength(1);

    auth.setMockSession(activeSession('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'));
    await waitForConnection(facade);
    await waitForSubscriptionCount(transport, 'user', 2);

    expect(transport.subscribed.filter((request) => request.subscriptionType === 'workspace')).toHaveLength(1);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'user')).toHaveLength(2);
  });

  it('ignores the expected closed status while a Tenant-switch stop is still draining', async () => {
    await enableAndAuthenticate();
    let signalStopStarted!: () => void;
    const stopStarted = new Promise<void>((resolve) => { signalStopStarted = resolve; });
    let finishStop!: () => void;
    const stopGate = new Promise<void>((resolve) => { finishStop = resolve; });
    transport.stop = async () => {
      transport.stopCalls++;
      signalStopStarted();
      await stopGate;
    };

    auth.setMockSession(activeSession('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'));
    await stopStarted;
    await settle();

    transport.statuses.next('closed');
    finishStop();
    await waitForSubscriptionCount(transport, 'user', 2);
    await waitForConnection(facade);

    expect(transport.startCalls).toBe(2);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('drops an old-session frame while stop drains and replays a frame after the replacement transport starts', async () => {
    const received: DurableRealtimeEvent[] = [];
    facade.durableEvents$.subscribe((value) => received.push(value));
    await enableAndAuthenticate();

    let signalStopStarted!: () => void;
    const stopStarted = new Promise<void>((resolve) => { signalStopStarted = resolve; });
    let finishStop!: () => void;
    const stopGate = new Promise<void>((resolve) => { finishStop = resolve; });
    transport.stop = async () => {
      transport.stopCalls++;
      signalStopStarted();
      await stopGate;
    };
    let signalReplacementCatchUp!: () => void;
    const replacementCatchUpStarted = new Promise<void>((resolve) => { signalReplacementCatchUp = resolve; });
    let finishReplacementCatchUp!: () => void;
    const replacementCatchUpGate = new Promise<void>((resolve) => { finishReplacementCatchUp = resolve; });

    auth.setMockSession({
      ...DEFAULT_AUTH_SESSION,
      status: 'anonymous',
      currentUser: null,
      currentTenant: null,
      isAuthenticated: false,
    });
    await stopStarted;
    facade.registerCatchUp('replacement-feature', async () => {
      signalReplacementCatchUp();
      await replacementCatchUpGate;
    });
    auth.setMockSession({
      ...activeSession(),
      currentUser: { ...DEFAULT_AUTH_SESSION.currentUser!, userId: 'mock-user-b' },
      displayName: 'Mock User B',
    });
    await settle();

    transport.events.next(event({
      eventId: '81818181-8181-4181-8181-818181818181',
      aggregateVersion: 2,
    }));
    finishStop();
    await replacementCatchUpStarted;
    transport.events.next(event({
      eventId: '82828282-8282-4282-8282-828282828282',
      aggregateVersion: 3,
    }));

    expect(received).toEqual([]);
    finishReplacementCatchUp();
    await waitForConnection(facade);
    expect(received.map((item) => item.eventId)).toEqual(['82828282-8282-4282-8282-828282828282']);
  });

  it('does not let an obsolete authorization recovery revive a newer Tenant-hydration stop', async () => {
    await enableAndAuthenticate();
    const refresh = new Subject<AuthSessionSnapshot | null>();
    const refreshSpy = vi.spyOn(auth, 'refreshSessionContext').mockReturnValue(refresh.asObservable());

    transport.invalidations.next();
    await vi.waitFor(() => expect(refreshSpy).toHaveBeenCalledTimes(1));

    auth.setMockSession(tenantHydratingSession());
    await settle();
    refresh.next(null);
    refresh.complete();
    await settle();

    expect(transport.startCalls).toBe(1);
    expect(facade.connectionState()).toBe('Degraded');
    refreshSpy.mockRestore();
  });

  it('TenantSwitchClearsPreviousTenantWorkspaceState', async () => {
    auth.setMockSession(activeSession());
    await settle();
    let protectedFiles = ['file-1'];
    facade.registerProtectedStateClearer('files-http-state', () => { protectedFiles = []; });
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);

    auth.setMockSession(activeSession('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'));
    await settle();

    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(notificationOpenContext.takeDigestWorkspace()).toBeNull();
    expect(protectedFiles).toEqual([]);
  });

  it('AuthorizationInvalidationClearsProtectedStateBeforeCatchUp', async () => {
    const order: string[] = [];
    const received: DurableRealtimeEvent[] = [];
    facade.registerProtectedStateClearer('right-panel', () => order.push('clear'));
    facade.registerCatchUp('right-panel', () => { order.push('catch-up'); });
    facade.durableEvents$.subscribe((value) => received.push(value));
    await enableAndAuthenticate();
    activeWorkspace.setMockWorkspace(ACTIVE_WORKSPACE);
    notificationOpenContext.setDigestWorkspace(RESOURCE_ID);
    order.length = 0;

    transport.events.next(event({
      eventId: '77777777-7777-4777-8777-777777777777',
      eventType: 'Security.AuthorizationStateChanged.v1',
      aggregateType: 'AuthorizationState',
      payload: { affectedUserId: RESOURCE_ID, scopeType: 'workspace', change: 'archived' },
    }));

    expect(order).toEqual(['clear']);
    expect(received).toEqual([]);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(notificationOpenContext.takeDigestWorkspace()).toBeNull();
    await waitForConnection(facade);
    expect(order).toEqual(['clear', 'catch-up']);
    expect(transport.subscribed.filter((request) => request.subscriptionType === 'user')).toHaveLength(2);
    expect(facade.connectionState()).toBe('Connected');
  });

  it('drops queued durable events while authorization revalidation is pending', async () => {
    const received: DurableRealtimeEvent[] = [];
    facade.durableEvents$.subscribe((value) => received.push(value));
    await enableAndAuthenticate();

    transport.invalidations.next();
    expect(facade.connectionState()).toBe('Reconnecting');
    transport.events.next(event({
      eventId: '78787878-7878-4787-8787-787878787878',
      aggregateVersion: 99,
    }));

    expect(received).toEqual([]);
    await waitForConnection(facade);
    expect(received).toEqual([]);
  });

  it('processes authorization invalidation while a catch-up is still pending', async () => {
    let releaseFirstCatchUp!: () => void;
    const firstCatchUp = new Promise<void>((resolve) => { releaseFirstCatchUp = resolve; });
    let catchUps = 0;
    facade.registerCatchUp('held-feature', async () => {
      catchUps++;
      if (catchUps === 1) {await firstCatchUp;}
    });

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await vi.waitFor(() => expect(catchUps).toBe(1));
    const authorizationRevision = facade.authorizationRevision();

    transport.events.next(event({
      eventId: '79797979-7979-4797-8797-797979797979',
      eventType: 'Security.AuthorizationStateChanged.v1',
      aggregateType: 'AuthorizationState',
      payload: { affectedUserId: RESOURCE_ID, scopeType: 'workspace', change: 'archived' },
    }));

    expect(facade.authorizationRevision()).toBe(authorizationRevision + 1);
    expect(facade.connectionState()).toBe('Reconnecting');
    releaseFirstCatchUp();
    await waitForConnection(facade);
    expect(catchUps).toBe(2);
  });

  it('replays a new-connection event received while a later catch-up is pending', async () => {
    const received: DurableRealtimeEvent[] = [];
    let releaseLaterCatchUp!: () => void;
    const laterCatchUp = new Promise<void>((resolve) => { releaseLaterCatchUp = resolve; });
    let signalLaterCatchUp!: () => void;
    const laterCatchUpStarted = new Promise<void>((resolve) => { signalLaterCatchUp = resolve; });
    facade.registerCatchUp('already-reconciled-feature', () => undefined);
    facade.registerCatchUp('later-feature', async () => {
      signalLaterCatchUp();
      await laterCatchUp;
    });
    facade.durableEvents$.subscribe((value) => received.push(value));

    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await laterCatchUpStarted;
    transport.events.next(event({
      eventId: '80808080-8080-4080-8080-808080808080',
      aggregateVersion: 2,
    }));

    expect(received).toEqual([]);
    releaseLaterCatchUp();
    await waitForConnection(facade);
    expect(received.map((item) => item.eventId)).toEqual(['80808080-8080-4080-8080-808080808080']);
  });

  it('rejects unknown schemas and duplicate event IDs without exposing payloads', async () => {
    const received: DurableRealtimeEvent[] = [];
    const diagnostics: string[] = [];
    facade.durableEvents$.subscribe((event) => received.push(event));
    facade.diagnostics$.subscribe((diagnostic) => diagnostics.push(diagnostic.code));
    await enableAndAuthenticate();

    transport.events.next({ ...event(), eventType: 'Unknown.Event.v99', payload: { privateBody: 'do not persist' } });
    transport.events.next(event());
    transport.events.next(event());

    expect(received).toHaveLength(1);
    expect(diagnostics).toEqual(['UnsupportedEvent', 'DuplicateEvent']);
    expect(JSON.stringify(sessionStorage)).not.toContain('privateBody');
    expect(JSON.stringify(localStorage)).not.toContain('privateBody');
  });

  it('rejects stale aggregate versions and honors registered feature stale guards', async () => {
    const received: DurableRealtimeEvent[] = [];
    facade.durableEvents$.subscribe((value) => received.push(value));
    facade.registerStaleEventGuard('feature', (value) => value.eventType !== 'Files.FileChanged.v1');
    await enableAndAuthenticate();

    transport.events.next(event({ aggregateVersion: 2 }));
    transport.events.next(event({ eventId: '33333333-3333-4333-8333-333333333333', aggregateVersion: 1 }));
    transport.events.next(event({ eventId: '44444444-4444-4444-8444-444444444444', eventType: 'Files.FileChanged.v1' }));

    expect(received).toHaveLength(1);
  });

  it('delivers every unversioned MessageThread refetch invalidation through the aggregate stale guard', async () => {
    const received: DurableRealtimeEvent[] = [];
    const diagnostics: string[] = [];
    facade.durableEvents$.subscribe((value) => received.push(value));
    facade.diagnostics$.subscribe((value) => diagnostics.push(value.code));
    await enableAndAuthenticate();

    // Seed the real aggregate-version cache with a higher legacy value. Null
    // must still bypass that cache because ThreadChanged is a refetch hint,
    // not a versioned aggregate snapshot.
    transport.events.next(event({
      eventId: '36200000-0000-4000-8000-000000000099',
      eventType: 'Messaging.ThreadChanged.v1',
      aggregateType: 'MessageThread',
      aggregateId: '36200000-0000-4000-8000-000000000010',
      aggregateVersion: 99,
      payload: {
        conversationId: '36200000-0000-4000-8000-000000000020',
        threadRootMessageId: '36200000-0000-4000-8000-000000000010',
        replyCount: 2,
        change: 'legacySeed',
        requiresRefetch: true
      }
    }));
    received.length = 0;

    for (const [index, change] of ['replyUpdated', 'replyDeleted', 'replyCreated', 'replyCreated'].entries()) {
      transport.events.next(event({
        eventId: `36200000-0000-4000-8000-00000000000${index}`,
        eventType: 'Messaging.ThreadChanged.v1',
        aggregateType: 'MessageThread',
        aggregateId: '36200000-0000-4000-8000-000000000010',
        aggregateVersion: null,
        payload: {
          conversationId: '36200000-0000-4000-8000-000000000020',
          threadRootMessageId: '36200000-0000-4000-8000-000000000010',
          replyCount: 2,
          change,
          requiresRefetch: true
        }
      }));
    }

    expect(received.map((value) => value.payload['change'])).toEqual([
      'replyUpdated',
      'replyDeleted',
      'replyCreated',
      'replyCreated'
    ]);
    expect(diagnostics).not.toContain('StaleEvent');
  });

  it('falls back to degraded mode when the connection cannot start while preserving session state', async () => {
    transport.start = async () => { throw new Error('network unavailable'); };
    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await settle();

    expect(facade.connectionState()).toBe('Degraded');
    expect(auth.isAuthenticated()).toBe(true);
  });

  async function enableAndAuthenticate(): Promise<void> {
    flags.setForTesting({ 'realtime.signalR': true });
    auth.setMockSession(activeSession());
    await settle();
  }
});

function activeSession(tenantId = TENANT_ID): AuthSessionSnapshot {
  return {
    ...DEFAULT_AUTH_SESSION,
    status: 'active',
    isAuthenticated: true,
    currentTenant: { ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId, isAvailable: true }
  };
}

function tenantHydratingSession(): AuthSessionSnapshot {
  return {
    ...activeSession(),
    currentTenant: null,
    currentUser: {
      ...DEFAULT_AUTH_SESSION.currentUser!,
      currentWorkspace: ACTIVE_WORKSPACE,
      workspaces: [ACTIVE_WORKSPACE],
    },
  };
}

function event(overrides: Partial<DurableRealtimeEvent> = {}): DurableRealtimeEvent {
  return {
    eventId: '33333333-3333-4333-8333-333333333333',
    eventType: 'Projects.ProjectChanged.v1',
    payloadSchemaVersion: 1,
    occurredAt: '2026-07-18T00:00:00.000Z',
    tenantId: TENANT_ID,
    aggregateType: 'Project',
    aggregateId: RESOURCE_ID,
    aggregateVersion: 1,
    actor: { actorType: 'User', actorId: '55555555-5555-4555-8555-555555555555' },
    correlationId: null,
    causationId: null,
    payload: {},
    ...overrides
  };
}

function settle(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve));
}

async function waitForConnection(facade: RealtimeFacade): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (facade.connectionState() === 'Connected') {return;}
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

async function waitForSubscriptionCount(
  transport: FakeRealtimeTransport,
  subscriptionType: RealtimeSubscriptionRequest['subscriptionType'],
  count: number,
): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (transport.subscribed.filter((request) => request.subscriptionType === subscriptionType).length >= count) {return;}
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}
