import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, Subject } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, AuthSessionFacade, DEFAULT_AUTH_SESSION } from '../auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../feature-flags/frontend-feature-flags.service';
import { ActiveWorkspaceFacade } from '../workspace/active-workspace.facade';
import { COGLATAS_REALTIME_TRANSPORT, RealtimeTransport } from './realtime-transport';
import { RealtimeFacade } from './realtime.facade';
import { DurableRealtimeEvent } from './realtime.models';

const tenantId = '11111111-1111-1111-1111-111111111111';
const actorA = '55555555-5555-4555-8555-555555555555';
const actorB = '66666666-6666-4666-8666-666666666666';
const workspace = { id: '22222222-2222-4222-8222-222222222222', label: 'Current workspace' };
const user = (userId: string, displayName = 'Current actor') => ({ ...DEFAULT_AUTH_SESSION.currentUser!, userId, displayName });

describe('Realtime lifecycle through authoritative Auth status refresh', () => {
  let auth: AuthSessionFacade;
  let facade: RealtimeFacade;
  let http: HttpTestingController;
  let events: Subject<unknown>;
  let starts: number;
  let allowStart: boolean;

  beforeEach(async () => {
    starts = 0;
    allowStart = false;
    events = new Subject<unknown>();
    const statuses = new Subject<'connecting'>();
    const transport: RealtimeTransport = {
      durableEvents$: events.asObservable(), authorizationInvalidations$: new Subject<void>(), statuses$: statuses.asObservable(),
      start: async () => {
        starts++;
        statuses.next('connecting');
        if (!allowStart) throw new Error('Synthetic Hub unavailability');
      },
      stop: async () => undefined,
      subscribe: async () => ({ allowed: true, code: 'Subscribed' }),
      unsubscribe: async () => ({ allowed: true, code: 'Unsubscribed' }),
    };
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: { ...DEFAULT_AUTH_SESSION, currentUser: user(actorA),
        currentTenant: { ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId, isAvailable: true } } },
      { provide: COGLATAS_REALTIME_TRANSPORT, useValue: transport },
    ] });
    auth = TestBed.inject(AuthSessionFacade);
    http = TestBed.inject(HttpTestingController);
    facade = TestBed.inject(RealtimeFacade);
    TestBed.inject(FrontendFeatureFlagsService).setForTesting({ 'realtime.signalR': true });
    await settle();
  });

  afterEach(() => {
    // Settle any erroneous extra probe without causing another successful-user refresh loop.
    for (const request of http.match('/api/auth/status')) request.flush({}, { status: 503, statusText: 'Unavailable' });
    http.verify();
    TestBed.resetTestingModule();
  });

  it('settles HTTP fallback without restarting for a fresh same-actor status object and retains metadata', async () => {
    const originalUser = auth.currentUser();
    const originalTenant = auth.currentTenant();
    const clear = vi.fn();
    facade.registerProtectedStateClearer('status-refresh-control', clear);
    TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace(workspace);
    const refresh = http.expectOne('/api/auth/status');
    refresh.flush({ isAuthenticated: true, user: { ...user(actorA, 'Updated HTTP actor'), capabilities: ['files:view'] } });
    await settle();

    expect.soft(auth.currentUser()).not.toBe(originalUser);
    expect.soft(auth.currentUser()?.displayName).toBe('Updated HTTP actor');
    expect.soft(auth.currentUser()?.capabilities).toEqual(['files:view']);
    expect.soft(auth.currentTenant()).toBe(originalTenant);
    expect.soft(auth.isAuthenticated()).toBe(true);
    expect.soft(starts).toBe(1);
    expect.soft(facade.connectionState()).toBe('Degraded');
    expect.soft(facade.initialSynchronizationSettled()).toBe(true);
    expect.soft(clear).not.toHaveBeenCalled();
    expect.soft(TestBed.inject(ActiveWorkspaceFacade).activeWorkspace()).toEqual(workspace);
    http.expectNone('/api/auth/status');
  });

  it('still replaces an actual changed actor and rejects the old frame before lifecycle processing', async () => {
    const received: DurableRealtimeEvent[] = [];
    facade.durableEvents$.subscribe(value => received.push(value));
    const clear = vi.fn();
    facade.registerProtectedStateClearer('changed-status-actor', clear);
    TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace(workspace);
    allowStart = true;
    http.expectOne('/api/auth/status').flush({ isAuthenticated: true, user: user(actorB) });
    const frame: DurableRealtimeEvent = { eventId: '33333333-3333-4333-8333-333333333333',
      eventType: 'Projects.ProjectChanged.v1', payloadSchemaVersion: 1, occurredAt: '2026-10-31T00:00:00Z', tenantId,
      aggregateType: 'Project', aggregateId: workspace.id, aggregateVersion: 1,
      actor: { actorType: 'User', actorId: actorA }, correlationId: null, causationId: null, payload: {} };
    events.next(frame);
    expect(received).toEqual([]);
    await settle();

    expect(starts).toBe(2);
    expect(facade.connectionState()).toBe('Connected');
    expect(auth.currentUser()?.userId).toBe(actorB);
    expect(clear).toHaveBeenCalledWith('session');
    expect(TestBed.inject(ActiveWorkspaceFacade).activeWorkspace()).toBeNull();
    events.next(frame);
    expect(received.map(value => value.eventId)).toEqual([frame.eventId]);
    http.expectNone('/api/auth/status');
  });

  it('rejects a reentrant frame from a protected clearer during an authoritative terminal status response', async () => {
    allowStart = true;
    http.expectOne('/api/auth/status').flush({ isAuthenticated: true, user: user(actorB) });
    await settle();
    expect(facade.connectionState()).toBe('Connected');
    const received: DurableRealtimeEvent[] = [];
    facade.durableEvents$.subscribe(value => received.push(value));
    const frame: DurableRealtimeEvent = { eventId: '77777777-7777-4777-8777-777777777777',
      eventType: 'Projects.ProjectChanged.v1', payloadSchemaVersion: 1, occurredAt: '2026-10-31T00:00:00Z', tenantId,
      aggregateType: 'Project', aggregateId: workspace.id, aggregateVersion: 2,
      actor: { actorType: 'User', actorId: actorB }, correlationId: null, causationId: null, payload: {} };
    const clear = vi.fn(() => events.next(frame));
    facade.registerProtectedStateClearer('terminal-status-reentry', clear);
    TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace(workspace);

    const response = firstValueFrom(auth.refreshSessionContext());
    http.expectOne('/api/auth/status').flush({ isAuthenticated: false, user: null });
    await response;
    await settle();

    expect(clear).toHaveBeenCalledWith('session');
    expect(received).toEqual([]);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(facade.connectionState()).toBe('Degraded');
    expect(TestBed.inject(ActiveWorkspaceFacade).activeWorkspace()).toBeNull();
    http.expectNone('/api/auth/status');
  });
});

function settle(): Promise<void> {
  return new Promise(resolve => setTimeout(resolve));
}
