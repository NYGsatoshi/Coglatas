import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProtectedStateClearReason, RealtimeFacade } from '../../core/realtime/realtime.facade';
import { WorkspaceMembersFacade } from './members.facade';
import { WorkspaceMemberManagementFacade } from './member-management.facade';
import { isMemberSearchQuery } from './member-management.types';

const WORKSPACE = '00000000-0000-0000-0000-000000000101';
const OTHER = '00000000-0000-0000-0000-000000000102';
const USER = '00000000-0000-0000-0000-000000000201';
const BASE = `/api/workspaces/${WORKSPACE}/members`;
const OPTIONS = BASE + '/management/options';
const CANDIDATES = BASE + '/management/candidates';
const candidate = { userId: USER, displayName: 'Same display name', allowedRoles: [3, 4] };

describe('WorkspaceMemberManagementFacade', () => {
  let facade: WorkspaceMemberManagementFacade;
  let http: HttpTestingController;
  let clear: (reason: ProtectedStateClearReason) => void;
  let catchUp: () => void;
  let reload: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    reload = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: WorkspaceMembersFacade, useValue: { reload } },
      { provide: RealtimeFacade, useValue: {
        registerProtectedStateClearer: (_owner: string, callback: typeof clear) => { clear = callback; return () => undefined; },
        registerSubscription: () => () => undefined,
        registerCatchUp: (_owner: string, callback: typeof catchUp) => { catchUp = callback; return () => undefined; },
      } },
    ] });
    facade = TestBed.inject(WorkspaceMemberManagementFacade);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  function loadAccess(): void {
    facade.setWorkspace(WORKSPACE);
    const request = http.expectOne(OPTIONS);
    expect(request.request.withCredentials).toBe(true);
    request.flush({ workspaceId: WORKSPACE, canAddMember: true, target: null });
  }
  function openAdd(): void {
    loadAccess(); facade.open('add');
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: true, target: null });
  }
  function findCandidate(): void {
    facade.updateQuery('Sa'); facade.search();
    const request = http.expectOne((r) => r.url === CANDIDATES);
    expect(request.request.params.keys().sort()).toEqual(['q', 'take']);
    expect(request.request.params.get('q')).toBe('Sa');
    expect(request.request.params.get('take')).toBe('20');
    request.flush({ workspaceId: WORKSPACE, items: [{ ...candidate, email: 'never-copy@example.test', tenantRole: 0 }], hasMore: false, nextAfterUserId: null });
    facade.select(USER);
  }
  function targetOptions(mode: 'change' | 'remove'): void {
    loadAccess(); facade.open(mode, { userId: USER, displayName: 'Current member' });
    const request = http.expectOne((r) => r.url === OPTIONS && r.params.get('userId') === USER);
    request.flush({ workspaceId: WORKSPACE, canAddMember: true, target: { userId: USER, currentRole: 0, membershipStatus: 1, canChangeRole: true, allowedRoles: [0, 1, 2, 3, 4], canRemove: true } });
  }

  it('uses only the current server options and does not infer authority from a role', () => {
    facade.setWorkspace(WORKSPACE);
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: false, target: null, role: 'Owner' });
    facade.open('add');
    expect(facade.access().canAddMember).toBe(false);
    expect(facade.editor()).toBeNull();
    http.expectNone((r) => r.url.includes('/tenant/'));
  });

  it('requires explicit candidate, server-allowed numeric role and identity confirmation before one add', () => {
    openAdd(); findCandidate();
    expect(facade.editor()?.identity).toEqual({ userId: USER, displayName: candidate.displayName });
    expect(facade.editor()?.role).toBeNull();
    expect(JSON.stringify(facade.editor())).not.toContain('never-copy');
    facade.selectRole(0); facade.confirmIdentity(true);
    expect(facade.canSubmit()).toBe(false);
    facade.selectRole('3');
    expect(facade.canSubmit()).toBe(false);
    facade.selectRole(3);
    expect(facade.canSubmit()).toBe(false);
    facade.confirmIdentity(true); facade.submit(); facade.submit();
    const write = http.expectOne(BASE);
    expect(write.request.method).toBe('POST');
    expect(write.request.body).toEqual({ userId: USER, role: 3 });
    expect(write.request.withCredentials).toBe(true);
    write.flush({ userId: USER, displayName: candidate.displayName, role: 3 });
    expect(facade.editor()).toBeNull(); expect(facade.saved()).toBe(true);
    expect(reload).toHaveBeenCalledExactlyOnceWith(WORKSPACE);
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: true, target: null });
  });

  it('patches only the selected role, preserving status and the existing Owner-changing authority', () => {
    targetOptions('change');
    facade.selectRole(0); facade.confirmIdentity(true); facade.submit();
    const write = http.expectOne(BASE + '/' + USER);
    expect(write.request.method).toBe('PATCH');
    expect(write.request.body).toEqual({ role: 0 });
    write.flush({ userId: USER, role: 0 });
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: true, target: null });
  });

  it('requires current target permission and confirmation for the existing removal route', () => {
    targetOptions('remove');
    expect(facade.canSubmit()).toBe(false);
    facade.confirmIdentity(true); facade.submit();
    const write = http.expectOne(BASE + '/' + USER);
    expect(write.request.method).toBe('DELETE'); expect(write.request.body).toBeNull();
    write.flush({ status: 'OK' });
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: true, target: null });
  });

  it('clears confirmation when a role or search changes', () => {
    openAdd(); findCandidate(); facade.selectRole(3); facade.confirmIdentity(true);
    facade.selectRole(4); expect(facade.canSubmit()).toBe(false);
    facade.confirmIdentity(true); facade.updateQuery('Other');
    expect(facade.editor()?.identity).toBeNull(); expect(facade.editor()?.role).toBeNull();
    expect(facade.editor()?.candidates).toEqual([]);
  });

  it.each(['authorization', 'workspace', 'session', 'tenant'] as const)('cancels and erases candidates and pending writes at %s invalidation', (reason) => {
    openAdd(); findCandidate(); facade.selectRole(3); facade.confirmIdentity(true); facade.submit();
    const pending = http.expectOne(BASE);
    clear(reason);
    expect(pending.cancelled).toBe(true); expect(facade.editor()).toBeNull();
    expect(facade.access().canAddMember).toBe(false); expect(facade.saved()).toBe(false);
  });

  it('cancels late options and replaces authority on Workspace switching', () => {
    facade.setWorkspace(WORKSPACE); const pending = http.expectOne(OPTIONS);
    facade.setWorkspace(OTHER); expect(pending.cancelled).toBe(true);
    http.expectOne(`/api/workspaces/${OTHER}/members/management/options`).flush({ workspaceId: OTHER, canAddMember: false, target: null });
    expect(facade.access()).toEqual({ workspaceId: OTHER, status: 'ready', canAddMember: false });
  });

  it('refetches current access on catch-up without restoring a protected editor', () => {
    openAdd(); findCandidate(); clear('authorization'); catchUp();
    expect(facade.editor()).toBeNull();
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: false, target: null });
    expect(facade.access().canAddMember).toBe(false);
  });

  it.each([401, 403, 404, 0, 500])('never repeats a rejected or uncertain write (%s) and rechecks with GET only', (status) => {
    targetOptions('change'); facade.selectRole(3); facade.confirmIdentity(true); facade.submit();
    const write = http.expectOne(BASE + '/' + USER);
    if (status === 0) write.error(new ProgressEvent('error'));
    else write.flush({ displayName: 'Do not retain denied response' }, { status, statusText: 'Unavailable' });
    expect(facade.editor()?.status).toBe(status === 0 || status >= 500 ? 'uncertain' : 'denied');
    expect(facade.editor()?.identity).toBeNull(); expect(facade.canSubmit()).toBe(false);
    facade.submit(); http.expectNone((r) => r.method !== 'GET');
    facade.recheck(); const request = http.expectOne(OPTIONS); expect(request.request.method).toBe('GET');
    request.flush({ workspaceId: WORKSPACE, canAddMember: false, target: null });
  });

  it.each([
    { workspaceId: OTHER, canAddMember: true, target: null },
    { workspaceId: WORKSPACE, canAddMember: 'true', target: null },
    { workspaceId: WORKSPACE, canAddMember: true, target: { userId: USER } },
  ])('rejects a malformed management projection', (body) => {
    facade.setWorkspace(WORKSPACE); http.expectOne(OPTIONS).flush(body);
    expect(facade.access().status).toBe('error'); expect(facade.access().canAddMember).toBe(false);
  });

  it('rejects undefined role values and duplicate or mismatched candidate identities', () => {
    openAdd(); facade.updateQuery('Sa'); facade.search();
    http.expectOne((r) => r.url === CANDIDATES).flush({ workspaceId: WORKSPACE, items: [{ ...candidate, allowedRoles: [9] }], hasMore: false, nextAfterUserId: null });
    expect(facade.editor()?.searchStatus).toBe('error'); expect(facade.editor()?.candidates).toEqual([]);
    facade.search(); http.expectOne((r) => r.url === CANDIDATES).flush({ workspaceId: WORKSPACE, items: [candidate, candidate], hasMore: false, nextAfterUserId: null });
    expect(facade.editor()?.candidates).toEqual([]);
  });

  it('uses an opaque continuation only with the same explicit prefix and current Workspace', () => {
    openAdd(); facade.updateQuery('Sa'); facade.search();
    http.expectOne((r) => r.url === CANDIDATES).flush({ workspaceId: WORKSPACE, items: [candidate], hasMore: true, nextAfterUserId: USER });
    facade.search(true);
    const more = http.expectOne((r) => r.url === CANDIDATES && r.params.get('afterUserId') === USER);
    expect(more.request.params.get('q')).toBe('Sa');
    facade.updateQuery('New'); expect(more.cancelled).toBe(true); expect(facade.editor()?.candidates).toEqual([]);
  });

  it.each(['', ' ', 'A', 'a'.repeat(101), '\ud800a', '\u0000a', 'a\udc00', 'a\u0000b'])('does not enumerate candidates for an invalid prefix', (query) => {
    openAdd(); facade.updateQuery(query); facade.search(); http.expectNone((r) => r.url === CANDIDATES);
  });

  it('counts Unicode scalars rather than UTF-16 units for bounded display-name search', () => {
    expect(isMemberSearchQuery('😀😀')).toBe(true);
    expect(isMemberSearchQuery('😀'.repeat(100))).toBe(true);
    expect(isMemberSearchQuery('😀'.repeat(101))).toBe(false);
    expect(isMemberSearchQuery(' 名')).toBe(false);
  });
});
