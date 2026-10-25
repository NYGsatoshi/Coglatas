import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { throwError } from 'rxjs';

import { AuthSessionFacade, COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { WorkspaceMembersFacade } from './members.facade';

describe('WorkspaceMembersFacade', () => {
  let facade: WorkspaceMembersFacade;
  let http: HttpTestingController;
  let clearProtectedState: ((reason: ProtectedStateClearReason) => void) | null;
  let catchUp: (() => Promise<void> | void) | null;

  beforeEach(() => {
    clearProtectedState = null;
    catchUp = null;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        {
          provide: RealtimeFacade,
          useValue: {
            registerProtectedStateClearer: (
              _owner: string,
              clear: (reason: ProtectedStateClearReason) => void,
            ) => {
              clearProtectedState = clear;
              return () => { clearProtectedState = null; };
            },
            registerSubscription: () => () => undefined,
            registerCatchUp: (_owner: string, callback: () => Promise<void> | void) => {
              catchUp = callback;
              return () => { catchUp = null; };
            },
          },
        },
      ],
    });

    facade = TestBed.inject(WorkspaceMembersFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('keeps the page accessor pure and loads live rows explicitly', () => {
    const page = computed(() => facade.getPage('workspace-alpha'));

    expect(() => page()).not.toThrow();
    expect(page().status).toBe('loading');
    http.expectNone('/api/workspaces/workspace-alpha/members');

    facade.ensureLoaded('workspace-alpha');

    const request = http.expectOne('/api/workspaces/workspace-alpha/members');
    expect(request.request.withCredentials).toBe(true);
    request.flush([
      {
        userId: 'user-a',
        displayName: 'Member A',
        role: 'Member',
        status: 'Active',
        joinedAt: '2026-08-23T00:00:00Z',
      },
    ]);

    expect(page().status).toBe('ready');
    expect(page().rows).toHaveLength(1);
    expect(page().rows[0].workspaceId).toBe('workspace-alpha');
  });

  it.each([
    ['Owner', 'Owner'], [0, 'Owner'], ['0', 'Owner'],
    ['Admin', 'Admin'], [1, 'Admin'], ['1', 'Admin'],
    ['Adviser', 'Adviser'], [2, 'Adviser'], ['2', 'Adviser'],
    ['Member', 'Member'], [3, 'Member'], ['3', 'Member'],
    ['ReadOnly', 'ReadOnly'], [4, 'ReadOnly'], ['4', 'ReadOnly'],
  ])('preserves canonical role %s for display without enabling member mutations', (wireRole, role) => {
    facade.ensureLoaded('workspace-alpha');
    const request = http.expectOne('/api/workspaces/workspace-alpha/members');
    expect(request.request.method).toBe('GET');
    request.flush([{ userId: 'user-role', displayName: 'Role sample', role: wireRole }]);

    const row = facade.getPage('workspace-alpha').rows[0];
    expect(row.role).toBe(role);
    expect(row.roleLabel).toBe(role);
    expect(row.rowActions.map((action) => action.id)).toEqual(['openMemberDetail']);
    expect(row.rowActions[0].disabled).toBe(false);
  });

  function loadDetailCandidates(): void {
    facade.ensureLoaded('workspace-alpha');
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-a', displayName: 'List name A', role: 'Member' },
      { userId: 'user-b', displayName: 'List name B', role: 'ReadOnly' },
    ]);
  }

  function openReadyDetail(): void {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    http.expectOne('/api/workspaces/workspace-alpha/members/user-a').flush({
      userId: 'user-a', displayName: 'Old protected detail', role: 'Owner',
    });
  }

  it.each(['pending', 'ready'] as const)(
    'reauthorizes only the selected member identity through routine catch-up after its detail is %s',
    async (stage) => {
      loadDetailCandidates();
      facade.openMemberDetail('workspace-alpha', 'user-a');
      const previous = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
      if (stage === 'ready') {
        previous.flush({ userId: 'user-a', displayName: 'Old detail A', role: 'Owner' });
      }

      const completion = catchUp?.();
      const during = facade.detail();
      const list = http.expectOne('/api/workspaces/workspace-alpha/members');
      list.flush([{ userId: 'user-a', displayName: 'Fresh list A', role: 'ReadOnly' }]);
      const details = http.match('/api/workspaces/workspace-alpha/members/user-a');
      const beforeDetail = facade.detail();
      for (const detail of details) {
        expect(detail.request.method).toBe('GET');
        expect(detail.request.withCredentials).toBe(true);
        detail.flush({ userId: 'user-a', displayName: 'Fresh authorized detail A', role: 'Adviser' });
      }
      await completion;

      expect(previous.cancelled).toBe(true);
      expect(during).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'loading' });
      expect(beforeDetail?.member).toBeUndefined();
      expect(details).toHaveLength(1);
      expect(facade.detail()).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'ready',
        member: { displayName: 'Fresh authorized detail A', role: 'Adviser' } });
      http.expectNone((request) => request.method !== 'GET');
      http.expectNone((request) => request.url.includes('/management'));
    },
  );

  it.each(['list', 'detail'] as const)('retains only current navigation identity when routine catch-ups overlap at the %s read', async (stage) => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    http.expectOne('/api/workspaces/workspace-alpha/members/user-a').flush({
      userId: 'user-a', displayName: 'Old protected detail', role: 'Owner',
    });
    const first = catchUp?.();
    let abandoned = http.expectOne('/api/workspaces/workspace-alpha/members');
    if (stage === 'detail') {
      abandoned.flush([{ userId: 'user-a', displayName: 'First fresh list A', role: 'Member' }]);
      abandoned = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
    }
    const second = catchUp?.();
    const during = facade.detail();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-a', displayName: 'Current listed A', role: 'ReadOnly' },
    ]);
    const details = http.match('/api/workspaces/workspace-alpha/members/user-a');
    for (const detail of details) {
      detail.flush({ userId: 'user-a', displayName: 'Current authorized A', role: 'ReadOnly' });
    }
    await Promise.all([first, second]);
    expect(abandoned.cancelled).toBe(true);
    expect(during).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'loading' });
    expect(details).toHaveLength(1);
    expect(facade.detail()).toMatchObject({ status: 'ready', member: { displayName: 'Current authorized A', role: 'ReadOnly' } });
    http.expectNone((request) => request.method !== 'GET');
  });

  it('honors a user close while the refreshed member list is pending', async () => {
    openReadyDetail();
    const completion = catchUp?.();
    const list = http.expectOne('/api/workspaces/workspace-alpha/members');
    facade.closeMemberDetail();
    list.flush([{ userId: 'user-a', displayName: 'Fresh listed A', role: 'Member' }]);
    await completion;
    expect(facade.detail()).toBeNull();
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
  });

  it('honors a newer member selection instead of completing an older automatic detail read', async () => {
    openReadyDetail();
    const completion = catchUp?.();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-a', displayName: 'Listed A', role: 'Member' },
      { userId: 'user-b', displayName: 'Listed B', role: 'ReadOnly' },
    ]);
    const previous = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
    facade.openMemberDetail('workspace-alpha', 'user-b');
    expect(previous.cancelled).toBe(true);
    http.expectOne('/api/workspaces/workspace-alpha/members/user-b').flush({
      userId: 'user-b', displayName: 'Fresh selected B', role: 'ReadOnly',
    });
    await completion;
    expect(facade.detail()).toMatchObject({ userId: 'user-b', status: 'ready', member: { displayName: 'Fresh selected B' } });
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
    http.expectNone((request) => request.method !== 'GET');
  });

  it('does not resurrect the previous member when another row is activated before the fresh list arrives', async () => {
    openReadyDetail();
    const completion = catchUp?.();
    const list = http.expectOne('/api/workspaces/workspace-alpha/members');
    facade.openMemberDetail('workspace-alpha', 'user-b');
    list.flush([
      { userId: 'user-a', displayName: 'Fresh listed A', role: 'Member' },
      { userId: 'user-b', displayName: 'Fresh listed B', role: 'ReadOnly' },
    ]);
    await completion;
    expect(facade.detail()).toBeNull();
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
    http.expectNone('/api/workspaces/workspace-alpha/members/user-b');
    facade.openMemberDetail('workspace-alpha', 'user-b');
    http.expectOne('/api/workspaces/workspace-alpha/members/user-b').flush({
      userId: 'user-b', displayName: 'Current explicitly selected B', role: 'ReadOnly',
    });
    expect(facade.detail()).toMatchObject({ userId: 'user-b', status: 'ready', member: { displayName: 'Current explicitly selected B' } });
  });

  it.each(['empty', 'other-target'] as const)('does not restore an identity missing from a fresh %s directory', async (result) => {
    openReadyDetail();
    const completion = catchUp?.();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush(result === 'empty' ? [] : [
      { userId: 'user-b', displayName: 'Remaining B', role: 'Member' },
    ]);
    await completion;
    expect(facade.detail()).toBeNull();
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
  });

  it.each([401, 403, 404])('purges both restored detail and directory when the fresh detail is denied (%s)', async (status) => {
    openReadyDetail();
    const completion = catchUp?.();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-a', displayName: 'Fresh listed A', role: 'Member' },
    ]);
    expect(facade.detail()?.member).toBeUndefined();
    http.expectOne('/api/workspaces/workspace-alpha/members/user-a').flush({}, { status, statusText: 'Denied' });
    await completion;
    expect(facade.detail()).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'permissionDenied' });
    expect(facade.getPage('workspace-alpha')).toMatchObject({ status: 'permissionDenied', rows: [] });
    http.expectNone((request) => request.method !== 'GET');
  });

  it.each([401, 403, 404, 500])('cannot restore protected detail when the refreshed list fails (%s)', async (status) => {
    openReadyDetail();
    const completion = catchUp?.();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush({}, { status, statusText: 'Failed' });
    await completion;
    expect(facade.detail()).toBeNull();
    expect(facade.getPage('workspace-alpha').rows).toEqual([]);
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
  });

  it.each(['authorization', 'workspace', 'session', 'tenant'] as const)(
    'discards routine detail navigation at a protected %s boundary during list refresh', async (reason) => {
      openReadyDetail();
      const completion = catchUp?.();
      const list = http.expectOne('/api/workspaces/workspace-alpha/members');
      clearProtectedState?.(reason);
      expect(list.cancelled).toBe(true);
      await completion;
      expect(facade.detail()).toBeNull();
      http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
      if (reason === 'authorization') {
        const recovery = catchUp?.();
        http.expectOne('/api/workspaces/workspace-alpha/members').flush([
          { userId: 'user-a', displayName: 'Newly authorized list A', role: 'Member' },
        ]);
        await recovery;
        expect(facade.detail()).toBeNull();
        http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
      }
    },
  );

  it.each(['authorization', 'workspace', 'session', 'tenant'] as const)(
    'cancels the new detail read at a protected %s boundary after the list reauthorizes', async (reason) => {
      openReadyDetail();
      const completion = catchUp?.();
      http.expectOne('/api/workspaces/workspace-alpha/members').flush([
        { userId: 'user-a', displayName: 'Fresh listed A', role: 'Member' },
      ]);
      const detail = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
      clearProtectedState?.(reason);
      expect(detail.cancelled).toBe(true);
      await completion;
      expect(facade.detail()).toBeNull();
      expect(facade.getPage('workspace-alpha').rows).toEqual([]);
    },
  );

  it.each([
    ['actor', 'list'], ['tenant', 'list'], ['actor', 'detail'], ['tenant', 'detail'],
  ] as const)('rejects prior-%s identity during a pending automatic %s read', async (boundary, stage) => {
    openReadyDetail();
    const completion = catchUp?.();
    const list = http.expectOne('/api/workspaces/workspace-alpha/members');
    const rows = [{ userId: 'user-a', displayName: 'Fresh listed A', role: 'Member' }];
    if (stage === 'detail') list.flush(rows);
    const detail = stage === 'detail' ? http.expectOne('/api/workspaces/workspace-alpha/members/user-a') : null;
    const auth = TestBed.inject(AuthSessionFacade);
    if (boundary === 'actor') {
      auth.refreshCurrentUser().subscribe();
      http.expectOne('/api/auth/me').flush({ userId: 'replacement-actor', displayName: 'Other actor', systemRole: 'TenantUser', status: 'Active' });
    } else {
      auth.refreshCurrentTenant().subscribe();
      http.expectOne('/api/tenants/current').flush({ ...DEFAULT_AUTH_SESSION.currentTenant!, tenantId: 'replacement-tenant' });
    }
    expect(facade.detail()).toBeNull();
    if (detail) detail.flush({ userId: 'user-a', displayName: 'Must not render for replacement identity', role: 'Owner' });
    else list.flush(rows);
    await completion;
    expect(facade.detail()).toBeNull();
    if (stage === 'list') expect(facade.getPage('workspace-alpha').rows).toEqual([]);
    http.expectNone((request) => request.method !== 'GET');
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
  });

  it('cannot carry routine detail navigation into a replacement Workspace route', async () => {
    openReadyDetail();
    const completion = catchUp?.();
    const oldList = http.expectOne('/api/workspaces/workspace-alpha/members');
    facade.ensureLoaded('workspace-beta');
    expect(oldList.cancelled).toBe(true);
    http.expectOne('/api/workspaces/workspace-beta/members').flush([
      { userId: 'user-a', displayName: 'Independently listed in another Workspace', role: 'Member' },
    ]);
    await completion;
    expect(facade.detail()).toBeNull();
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
    http.expectNone('/api/workspaces/workspace-beta/members/user-a');
  });

  it('keeps explicit manual reload clearing detail even during a routine restoration', async () => {
    openReadyDetail();
    const completion = catchUp?.();
    const routine = http.expectOne('/api/workspaces/workspace-alpha/members');
    facade.reload('workspace-alpha');
    expect(routine.cancelled).toBe(true);
    expect(facade.detail()).toBeNull();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-a', displayName: 'Fresh manually loaded A', role: 'Member' },
    ]);
    await completion;
    expect(facade.detail()).toBeNull();
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
  });

  it('reads member details from the ordinary endpoint and copies only the minimal identity and canonical role', () => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    expect(facade.detail()).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'loading' });
    const request = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ userId: 'user-a', displayName: 'Fresh detail A', role: 'Adviser', email: 'private@example.invalid', status: 'Active', joinedAt: '2020-01-01' });
    expect(facade.detail()).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'ready', member: { displayName: 'Fresh detail A', role: 'Adviser' } });
    expect(facade.getPage('workspace-alpha').rows.map((row) => [row.id, row.displayName, row.role])).toEqual([
      ['user-a', 'Fresh detail A', 'Adviser'], ['user-b', 'List name B', 'ReadOnly'],
    ]);
    http.expectNone((candidate) => candidate.url.includes('/management'));
  });

  it.each([401, 403, 404])('drops the previous body before a denied detail response (%s)', (status) => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    http.expectOne('/api/workspaces/workspace-alpha/members/user-a').flush({ userId: 'user-a', displayName: 'Protected A', role: 'Owner' });
    facade.openMemberDetail('workspace-alpha', 'user-b');
    expect(facade.detail()?.member).toBeUndefined();
    http.expectOne('/api/workspaces/workspace-alpha/members/user-b').flush({ displayName: 'Denied body' }, { status, statusText: 'Denied' });
    expect(facade.detail()).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-b', status: 'permissionDenied' });
    expect(facade.getPage('workspace-alpha').rows).toHaveLength(0);
  });

  it('discards the directory after a masked denial and restores only a fresh authorized list on explicit recheck', () => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    http.expectOne('/api/workspaces/workspace-alpha/members/user-a').flush({}, { status: 404, statusText: 'Unavailable' });
    expect(facade.getPage('workspace-alpha').status).toBe('permissionDenied');
    expect(facade.getPage('workspace-alpha').rows).toHaveLength(0);
    facade.reload('workspace-alpha');
    expect(facade.detail()).toBeNull();
    const recheck = http.expectOne('/api/workspaces/workspace-alpha/members');
    expect(recheck.request.method).toBe('GET');
    recheck.flush([{ userId: 'user-b', displayName: 'Fresh remaining B', role: 'ReadOnly' }]);
    expect(facade.getPage('workspace-alpha').rows.map((row) => row.displayName)).toEqual(['Fresh remaining B']);
  });

  it.each([401, 403, 404])('recognizes an interceptor-normalized denial for both member detail and list (%s)', (httpStatus) => {
    loadDetailCandidates();
    const read = vi.spyOn(TestBed.inject(HttpClient), 'get').mockReturnValue(throwError(() => ({
      code: 'NotFound', message: 'Unavailable', details: [], redactionApplied: true,
      httpStatus, localErrorId: 'synthetic-normalized-error',
    })));
    facade.openMemberDetail('workspace-alpha', 'user-a');
    expect(facade.detail()).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'permissionDenied' });
    facade.reload('workspace-alpha');
    expect(facade.detail()).toBeNull();
    expect(facade.getPage('workspace-alpha').status).toBe('permissionDenied');
    expect(facade.getPage('workspace-alpha').rows).toHaveLength(0);
    read.mockRestore();
  });

  it.each([
    { userId: 'other-user', displayName: 'Mismatched identity', role: 'Owner' },
    { userId: 'user-a', role: 'Owner' },
    null,
  ])('rejects malformed or mismatched detail without retaining the response body', (body) => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    http.expectOne('/api/workspaces/workspace-alpha/members/user-a').flush(body);
    expect(facade.detail()).toEqual({ workspaceId: 'workspace-alpha', userId: 'user-a', status: 'error' });
  });

  it('cancels a replaced detail request and renders only the latest selected identity', () => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    const oldRequest = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
    facade.openMemberDetail('workspace-alpha', 'user-b');
    expect(oldRequest.cancelled).toBe(true);
    http.expectOne('/api/workspaces/workspace-alpha/members/user-b').flush({ userId: 'user-b', displayName: 'Current B', role: 'ReadOnly' });
    expect(facade.detail()?.member?.displayName).toBe('Current B');
  });

  it.each(['authorization', 'workspace', 'session', 'tenant'] as ProtectedStateClearReason[])('clears and cancels detail at the protected %s boundary', (reason) => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    const request = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
    clearProtectedState?.(reason);
    expect(request.cancelled).toBe(true);
    expect(facade.detail()).toBeNull();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    expect(facade.detail()).toBeNull();
    http.expectNone('/api/workspaces/workspace-alpha/members/user-a');
  });

  it('clears detail during list refresh and Workspace switching and does not open unlisted members', () => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-beta', 'user-a');
    facade.openMemberDetail('workspace-alpha', 'not-listed');
    http.expectNone((request) => request.url.endsWith('/user-a') || request.url.endsWith('/not-listed'));
    facade.openMemberDetail('workspace-alpha', 'user-a');
    const detail = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
    facade.reload('workspace-alpha');
    expect(detail.cancelled).toBe(true);
    expect(facade.detail()).toBeNull();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([{ userId: 'user-b', displayName: 'Remaining B', role: 'Member' }]);
    facade.openMemberDetail('workspace-alpha', 'user-b');
    const next = http.expectOne('/api/workspaces/workspace-alpha/members/user-b');
    facade.ensureLoaded('workspace-beta');
    expect(next.cancelled).toBe(true);
    expect(facade.detail()).toBeNull();
    http.expectOne('/api/workspaces/workspace-beta/members').flush([]);
  });

  it('permits an explicit read retry after a transient error and cancels on close', () => {
    loadDetailCandidates();
    facade.openMemberDetail('workspace-alpha', 'user-a');
    http.expectOne('/api/workspaces/workspace-alpha/members/user-a').flush({}, { status: 500, statusText: 'Transient' });
    expect(facade.detail()?.status).toBe('error');
    facade.openMemberDetail('workspace-alpha', 'user-a');
    const retry = http.expectOne('/api/workspaces/workspace-alpha/members/user-a');
    expect(facade.detail()?.member).toBeUndefined();
    facade.closeMemberDetail();
    expect(retry.cancelled).toBe(true);
    expect(facade.detail()).toBeNull();
  });

  it.each([[null], [undefined], [''], ['Teacher'], ['Viewer'], ['Owner,Admin'], [5], [-1], [1.5], [false], [{}], [[]]])(
    'keeps unknown role %j unavailable instead of inventing Member', (wireRole) => {
      facade.ensureLoaded('workspace-alpha');
      http.expectOne('/api/workspaces/workspace-alpha/members').flush([
        { userId: 'user-unknown', displayName: 'Unknown role sample', role: wireRole },
      ]);
      expect(facade.getPage('workspace-alpha').rows[0].role).toBe('Unknown');
      expect(facade.getPage('workspace-alpha').rows[0].roleLabel).toBe('Unknown');
    },
  );

  it('cancels late member responses and reloads only through authorization recovery', () => {
    facade.ensureLoaded('workspace-alpha');
    const stale = http.expectOne('/api/workspaces/workspace-alpha/members');

    clearProtectedState?.('authorization');

    expect(stale.cancelled).toBe(true);
    expect(facade.getPage('workspace-alpha').rows).toEqual([]);
    expect(facade.getPage('workspace-alpha').status).toBe('loading');

    catchUp?.();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-current', displayName: 'Current Member', role: 'Member', status: 'Active' },
    ]);

    expect(facade.getPage('workspace-alpha').rows.map((row) => row.id)).toEqual([
      'user-current',
    ]);
  });

  it('keeps authorization catch-up pending until the authoritative member request settles', async () => {
    facade.ensureLoaded('workspace-alpha');
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([]);
    clearProtectedState?.('authorization');

    const completion = catchUp?.();
    expect(completion).toBeInstanceOf(Promise);
    let settled = false;
    void Promise.resolve(completion).then(() => { settled = true; });
    await Promise.resolve();
    expect(settled).toBe(false);

    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-current', displayName: 'Current Member', role: 'Member', status: 'Active' },
    ]);
    await completion;

    expect(settled).toBe(true);
    expect(facade.getPage('workspace-alpha').rows.map((row) => row.id)).toEqual([
      'user-current',
    ]);
  });

  it('fails a revoked mounted member route closed during degraded HTTP recovery', () => {
    facade.ensureLoaded('workspace-alpha');
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([
      { userId: 'user-old', displayName: 'Old Member', role: 'Member', status: 'Active' },
    ]);

    clearProtectedState?.('authorization');
    catchUp?.();
    http.expectOne('/api/workspaces/workspace-alpha/members').flush(
      { error: 'revoked' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(facade.getPage('workspace-alpha')).toMatchObject({
      status: 'permissionDenied',
      rows: [],
    });
  });

  it('discards the mounted member intent at a genuine Workspace boundary', () => {
    facade.ensureLoaded('workspace-alpha');
    http.expectOne('/api/workspaces/workspace-alpha/members').flush([]);

    clearProtectedState?.('workspace');

    expect(catchUp).toBeNull();
    expect(facade.getPage('workspace-alpha')).toMatchObject({ status: 'loading', rows: [] });
  });
});
