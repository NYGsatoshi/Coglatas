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

  it('preserves the same create identity after a 409 unreconciled replay', async () => {
    const { facade, http } = setup();
    beginCreate(facade);
    const first = http.expectOne((request) => request.method === 'POST');
    const key = first.request.headers.get('Idempotency-Key');
    expect(key).toMatch(/^[0-9a-f-]{36}$/i);
    first.flush({ error: 'IdempotencyConflict|Channel creation could not be confirmed.' },
      { status: 409, statusText: 'Conflict' });
    expect(facade.notice()).toBe('uncertain');
    expect(facade.dialogOpen()).toBe(true);
    expect(facade.name()).toBe('Council events');

    const refreshed = facade.refresh();
    http.expectOne((request) => request.method === 'GET').flush(page());
    await refreshed;
    facade.create();
    const retry = http.expectOne((request) => request.method === 'POST');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush({}, { status: 503, statusText: 'Unavailable' });
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

  it('requires a fresh current capability before reopening a create dialog cleared by authorization', async () => {
    const { facade, http, clear, catchUp } = setup();
    expect(facade.canCreate()).toBe(true);
    facade.openCreate();
    facade.name.set('Synthetic pending council channel');
    expect(facade.dialogOpen()).toBe(true);

    // A late protected-state invalidation can legitimately close a dialog
    // even after a previous current-authority directory allowed creation.
    clear();
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    expect(facade.directory()).toBeNull();
    expect(facade.status()).toBe('unavailable');
    expect(facade.canCreate()).toBe(false);
    facade.openCreate();
    expect(facade.dialogOpen()).toBe(false);
    http.expectNone((request) => request.method === 'POST');

    // Only a new current-scope response may restore the create capability.
    // It must not resurrect the old dialog, draft or unsent command.
    const recovered = catchUp();
    http.expectOne((request) => request.url === endpoint() && request.method === 'GET').flush(page(true));
    await recovered;
    expect(facade.canCreate()).toBe(true);
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    http.expectNone((request) => request.method === 'POST');

    facade.openCreate();
    expect(facade.dialogOpen()).toBe(true);
    facade.create();
    http.expectNone((request) => request.method === 'POST');
  });

  it('accepts a previously authorized create click during same-scope refresh without sending a command', async () => {
    const { facade, http, catchUp } = setup(true);
    expect(facade.canOpenCreate()).toBe(true);
    const refreshing = catchUp();
    expect(facade.canCreate()).toBe(false); // An in-flight read never grants create.
    expect(facade.canOpenCreate()).toBe(true); // Preserve a previously allowed native click.
    facade.openCreate();
    expect(facade.dialogOpen()).toBe(true);
    facade.name.set('Unsent channel during routine refresh');
    expect(facade.canSubmitCreate()).toBe(false);
    facade.create();
    http.expectNone((request) => request.method === 'POST');
    http.expectOne((request) => request.url === endpoint() && request.method === 'GET').flush(page(true));
    await refreshing;
    expect(facade.canCreate()).toBe(true);
    expect(facade.canSubmitCreate()).toBe(true);
    expect(facade.dialogOpen()).toBe(true);
    expect(facade.name()).toBe('Unsent channel during routine refresh');
    http.expectNone((request) => request.method === 'POST', 'read completion must not submit old intent');
  });

  it('never queues an ungranted initial create click while refreshing a denied directory', async () => {
    const { facade, http, catchUp } = setup(false);
    const pending = catchUp();
    expect(facade.canOpenCreate()).toBe(false);
    facade.openCreate();
    expect(facade.dialogOpen()).toBe(false);
    http.expectOne((request) => request.method === 'GET').flush(page(true));
    await pending;
    expect(facade.canOpenCreate()).toBe(true);
    expect(facade.dialogOpen()).toBe(false); // Only a new explicit user action may open.
    facade.openCreate();
    expect(facade.dialogOpen()).toBe(true);
    http.expectNone((request) => request.method === 'POST');
  });

  it('purges an opened in-flight refresh dialog on server denial or protected invalidation', async () => {
    const { facade, http, catchUp, clear } = setup(true);
    const pending = catchUp();
    facade.openCreate();
    facade.name.set('Never submit after denied refresh');
    http.expectOne((request) => request.method === 'GET').flush(page(false));
    await pending;
    expect(facade.canOpenCreate()).toBe(false);
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    http.expectNone((request) => request.method === 'POST');

    const permitted = catchUp();
    http.expectOne((request) => request.method === 'GET').flush(page(true));
    await permitted;
    const interrupted = catchUp();
    facade.openCreate();
    facade.name.set('Cancelled protected intent');
    const stale = http.expectOne((request) => request.method === 'GET');
    clear();
    expect(stale.cancelled).toBe(true);
    await interrupted;
    expect(facade.canOpenCreate()).toBe(false);
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    const reauthorized = catchUp();
    http.expectOne((request) => request.method === 'GET').flush(page(true));
    await reauthorized;
    expect(facade.canCreate()).toBe(true);
    expect(facade.dialogOpen()).toBe(false); // Fresh authority must not replay stale intent.
    http.expectNone((request) => request.method === 'POST');
  });

  it('holds local opening permission across overlapping same-scope refreshes only', async () => {
    const { facade, http, catchUp, workspace } = setup(true);
    const first = catchUp();
    const old = http.expectOne((request) => request.method === 'GET');
    const second = catchUp();
    expect(old.cancelled).toBe(true);
    expect(facade.canOpenCreate()).toBe(true);
    facade.openCreate();
    expect(facade.dialogOpen()).toBe(true);
    http.expectOne((request) => request.method === 'GET').flush(page(true));
    await Promise.all([first, second]);
    expect(facade.canSubmitCreate()).toBe(false); // Blank form stays unsubmitable.

    const switching = catchUp();
    const stale = http.expectOne((request) => request.method === 'GET');
    workspace.set({ id: nextWorkspaceId, label: 'Different council' });
    expect(facade.canOpenCreate()).toBe(false); // A scope change revokes the affordance immediately.
    TestBed.tick();
    expect(stale.cancelled).toBe(true);
    await switching;
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    http.expectOne((request) => request.url === endpoint(nextWorkspaceId)).flush(page(false, nextWorkspaceId));
    http.expectNone((request) => request.method === 'POST');
  });

  it('preserves a same-scope create dialog during routine catch-up but cannot submit without fresh capability', async () => {
    const { facade, http, catchUp } = setup();
    facade.openCreate();
    facade.name.set('Council catch-up draft');
    expect(facade.dialogOpen()).toBe(true);
    expect(facade.canSubmitCreate()).toBe(true);

    // Routine directory refresh is not a protected-state invalidation.
    // Keep the local dialog but forbid a POST until fresh current authority arrives.
    const refreshing = catchUp();
    expect(facade.status()).toBe('loading');
    expect(facade.directory()).toBeNull();
    expect(facade.dialogOpen()).toBe(true);
    expect(facade.name()).toBe('Council catch-up draft');
    expect(facade.canCreate()).toBe(false);
    expect(facade.canSubmitCreate()).toBe(false);
    facade.create();
    http.expectNone((request) => request.method === 'POST');
    http.expectOne((request) => request.url === endpoint() && request.method === 'GET').flush(page(true));
    await refreshing;
    expect(facade.status()).toBe('ready');
    expect(facade.dialogOpen()).toBe(true);
    expect(facade.name()).toBe('Council catch-up draft');
    expect(facade.canSubmitCreate()).toBe(true);

    // The server can instead revoke create capability in the fresh response.
    // That must discard the dialog and its local name, not silently submit.
    const denied = catchUp();
    http.expectOne((request) => request.url === endpoint() && request.method === 'GET').flush(page(false));
    await denied;
    expect(facade.canCreate()).toBe(false);
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    http.expectNone((request) => request.method === 'POST');
  });

  it('cancels a pending routine catch-up on protected invalidation and never revives its dialog', async () => {
    const { facade, http, catchUp, clear } = setup();
    facade.openCreate();
    facade.name.set('Synthetic uncommitted draft');
    expect(facade.dialogOpen()).toBe(true);
    const previous = catchUp();
    const obsolete = http.expectOne((request) => request.url === endpoint() && request.method === 'GET');
    clear();
    expect(obsolete.cancelled).toBe(true);
    await previous;
    expect(facade.status()).toBe('unavailable');
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
    expect(facade.canCreate()).toBe(false);
    facade.create();
    http.expectNone((request) => request.method === 'POST');

    // A new authorized directory may restore capability, never the old intent.
    const recovered = catchUp();
    http.expectOne((request) => request.url === endpoint() && request.method === 'GET').flush(page(true));
    await recovered;
    expect(facade.canCreate()).toBe(true);
    expect(facade.dialogOpen()).toBe(false);
    expect(facade.name()).toBe('');
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
