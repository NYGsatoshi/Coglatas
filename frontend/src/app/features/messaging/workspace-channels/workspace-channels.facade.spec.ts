import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthSessionFacade } from '../../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { WorkspaceChannelsFacade } from './workspace-channels.facade';

const workspaceId = '10000000-0000-4000-8000-000000000001';
const nextWorkspaceId = '10000000-0000-4000-8000-000000000002';
const actorId = '20000000-0000-4000-8000-000000000001';
const nextActorId = '20000000-0000-4000-8000-000000000002';
const tenantId = '30000000-0000-4000-8000-000000000001';
const channelId = '40000000-0000-4000-8000-000000000001';
const endpoint = (id = workspaceId) => `/api/workspaces/${id}/chat-channels`;
const row = () => ({ id: channelId, workspaceId, name: 'Council events', visibility: 'PublicWithinScope',
  isMember: false, canJoin: true, canManage: false, isDefault: false });
const page = (canCreate = true, id = workspaceId) => ({ workspaceId: id, canCreate, items: [], page: 1, pageSize: 50, totalCount: 0 });

function setup(canCreate = true) {
  const actor = signal({ userId: actorId });
  const tenant = signal({ tenantId, isAvailable: true, isPlatformScope: false });
  const authenticated = signal(true);
  const workspace = signal<{ id: string; label: string } | null>({ id: workspaceId, label: 'Council' });
  let clear: () => void = () => { throw new Error('Protected-state clearer was not registered.'); };
  let catchUp: () => Promise<void> = () => Promise.reject(new Error('Catch-up was not registered.'));
  const unregisterClear = vi.fn();
  const unregisterCatchUp = vi.fn();
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), WorkspaceChannelsFacade,
      { provide: AuthSessionFacade, useValue: { currentUser: actor, currentTenant: tenant, isAuthenticated: authenticated } },
      { provide: ActiveWorkspaceFacade, useValue: { activeWorkspace: workspace } },
      { provide: RealtimeFacade, useValue: {
        registerProtectedStateClearer: (_owner: string, callback: () => void) => { clear = callback; return unregisterClear; },
        registerCatchUp: (_owner: string, callback: () => Promise<void>) => { catchUp = callback; return unregisterCatchUp; }
      } }
    ]
  });
  const facade = TestBed.inject(WorkspaceChannelsFacade);
  const http = TestBed.inject(HttpTestingController);
  TestBed.tick();
  http.expectOne((request) => request.url === endpoint() && request.method === 'GET').flush(page(canCreate));
  return { facade, http, actor, tenant, authenticated, workspace,
    clear: () => clear(), catchUp: () => catchUp(), unregisterClear, unregisterCatchUp };
}

function beginCreate(facade: WorkspaceChannelsFacade): void {
  facade.openCreate();
  facade.name.set('Council events');
  facade.create();
}

describe('Workspace channel directory state ownership', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('uses server create capability rather than the existence of a Workspace', () => {
    const { facade, http } = setup(false);
    expect(facade.canCreate()).toBe(false);
    beginCreate(facade);
    expect(facade.dialogOpen()).toBe(false);
    http.expectNone((request) => request.method === 'POST');
  });

  it('keeps one request identity through uncertain retry and manual directory refresh', async () => {
    const { facade, http } = setup();
    beginCreate(facade);
    const first = http.expectOne((request) => request.method === 'POST');
    const key = first.request.headers.get('Idempotency-Key');
    expect(key).toMatch(/^[0-9a-f-]{36}$/i);
    facade.create();
    http.expectNone((request) => request.method === 'POST');
    first.flush({}, { status: 503, statusText: 'Unavailable' });
    expect(facade.notice()).toBe('uncertain');
    const refreshed = facade.refresh();
    http.expectOne((request) => request.method === 'GET').flush(page());
    await refreshed;
    facade.create();
    const retry = http.expectOne((request) => request.method === 'POST');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toEqual({ name: 'Council events', visibility: 'PublicWithinScope' });
    retry.flush({}, { status: 503, statusText: 'Unavailable' });
    facade.name.set('Changed events');
    facade.create();
    const changed = http.expectOne((request) => request.method === 'POST');
    expect(changed.request.headers.get('Idempotency-Key')).not.toBe(key);
    changed.flush({}, { status: 503, statusText: 'Unavailable' });
  });

  it('treats a confirmed creation as committed even when the following directory read fails', () => {
    const { facade, http } = setup();
    beginCreate(facade);
    http.expectOne((request) => request.method === 'POST').flush({ ...row(), isMember: true, canJoin: false, canManage: true });
    expect(facade.confirmedId()).toBe(channelId);
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    http.expectOne((request) => request.method === 'GET').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(facade.status()).toBe('error');
    expect(facade.confirmedId()).toBe(channelId);
    facade.create();
    http.expectNone((request) => request.method === 'POST');
  });

  it('clears protected rows and the create draft synchronously before authorization catch-up', async () => {
    const { facade, http, clear, catchUp } = setup();
    beginCreate(facade);
    const pending = http.expectOne((request) => request.method === 'POST');
    clear();
    expect(pending.cancelled).toBe(true);
    expect(facade.directory()).toBeNull();
    expect(facade.name()).toBe('');
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.pending()).toBeNull();
    const recovery = catchUp();
    http.expectOne((request) => request.method === 'GET').flush(page(false));
    await recovery;
    expect(facade.canCreate()).toBe(false);
  });

  it('cancels an old Workspace response and reads only the newly committed scope', async () => {
    const { facade, http, workspace } = setup();
    const previous = facade.refresh();
    const oldRequest = http.expectOne((request) => request.method === 'GET');
    workspace.set({ id: nextWorkspaceId, label: 'Different council' });
    TestBed.tick();
    expect(oldRequest.cancelled).toBe(true);
    expect(facade.directory()).toBeNull();
    expect(facade.search()).toBe('');
    http.expectOne((request) => request.url === endpoint(nextWorkspaceId)).flush(page(false, nextWorkspaceId));
    await previous;
    expect(facade.directory()?.workspaceId).toBe(nextWorkspaceId);
    expect(facade.canCreate()).toBe(false);
  });

  it('does not retain a draft after same-Tenant actor replacement', () => {
    const { facade, http, actor } = setup();
    beginCreate(facade);
    const pending = http.expectOne((request) => request.method === 'POST');
    actor.set({ userId: nextActorId });
    TestBed.tick();
    expect(pending.cancelled).toBe(true);
    expect(facade.name()).toBe('');
    expect(facade.confirmedId()).toBeNull();
    expect(facade.dialogOpen()).toBe(false);
    http.expectOne((request) => request.method === 'GET').flush(page(false));
    expect(facade.canCreate()).toBe(false);
  });

  it('purges a denied command instead of restoring its former channel projection', () => {
    const { facade, http } = setup();
    beginCreate(facade);
    http.expectOne((request) => request.method === 'POST').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(facade.status()).toBe('unavailable');
    expect(facade.notice()).toBe('denied');
    expect(facade.directory()).toBeNull();
    expect(facade.name()).toBe('');
    expect(facade.dialogOpen()).toBe(false);
    http.expectNone((request) => request.method === 'POST');
  });

  it('joins only an exact current joinable row and never assumes participation optimistically', async () => {
    const { facade, http } = setup();
    facade.join(channelId);
    http.expectNone((request) => request.method === 'POST');
    const refresh = facade.refresh();
    http.expectOne((request) => request.method === 'GET').flush({ ...page(), items: [row()], totalCount: 1 });
    await refresh;
    facade.join(channelId);
    const join = http.expectOne(`${endpoint()}/${channelId}/join`);
    expect(facade.directory()?.items[0].isMember).toBe(false);
    expect(facade.confirmedId()).toBeNull();
    join.flush({ ...row(), isMember: true, canJoin: false });
    expect(facade.confirmedId()).toBe(channelId);
    http.expectOne((request) => request.method === 'GET').flush({ ...page(), items: [{ ...row(), isMember: true, canJoin: false }], totalCount: 1 });
    expect(facade.directory()?.items[0].isMember).toBe(true);
  });

  it('purges state on logout and dispatches no further directory request', () => {
    const { facade, http, authenticated } = setup();
    facade.openCreate();
    facade.name.set('Private draft');
    authenticated.set(false);
    TestBed.tick();
    expect(facade.directory()).toBeNull();
    expect(facade.name()).toBe('');
    expect(facade.scope()).toBeNull();
    void facade.refresh();
    http.expectNone((request) => request.url.includes('/chat-channels'));
  });
});
