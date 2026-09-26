import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { RealtimeFacade } from '../realtime/realtime.facade';
import { ActiveWorkspaceFacade, WorkspaceSummary } from './active-workspace.facade';
import {
  COGLATAS_WORKSPACE_PREFERENCE_STORAGE,
  WorkspacePreferenceService,
  WorkspacePreferenceStorage,
} from './workspace-preference.service';
import {
  isWorkspaceSpecificRoute,
  WorkspaceSelectionFacade,
  WorkspaceSelectionIdentity,
  workspaceIdFromRoute,
} from './workspace-selection.facade';

@Component({ standalone: true, template: '' })
class EmptyRouteComponent {}

class MemoryStorage implements WorkspacePreferenceStorage {
  readonly values = new Map<string, string>();
  throwOnAccess = false;

  getItem(key: string): string | null {
    if (this.throwOnAccess) {throw new Error('storage denied');}
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    if (this.throwOnAccess) {throw new Error('storage denied');}
    this.values.set(key, value);
  }

  removeItem(key: string): void {
    if (this.throwOnAccess) {throw new Error('storage denied');}
    this.values.delete(key);
  }
}

const identity: WorkspaceSelectionIdentity = { tenantId: 'tenant-a', userId: 'user-a' };
const workspaceA: WorkspaceSummary = { id: 'workspace-a', label: 'Workspace A' };
const workspaceB: WorkspaceSummary = { id: 'workspace-b', label: 'Workspace B' };
const workspaceC: WorkspaceSummary = { id: 'workspace-c', label: 'Workspace C' };

describe('WorkspaceSelectionFacade', () => {
  let selection: WorkspaceSelectionFacade;
  let activeWorkspace: ActiveWorkspaceFacade;
  let preferences: WorkspacePreferenceService;
  let realtime: {
    clearForWorkspaceBoundary: ReturnType<typeof vi.fn>;
    runAuthoritativeHttpCatchUps: ReturnType<typeof vi.fn>;
  };
  let router: Router;

  beforeEach(() => {
    realtime = {
      clearForWorkspaceBoundary: vi.fn(),
      runAuthoritativeHttpCatchUps: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'workspaces', component: EmptyRouteComponent },
          { path: 'workspaces/:workspaceId/members', component: EmptyRouteComponent },
          { path: 'projects/:projectId', component: EmptyRouteComponent },
          { path: 'account', component: EmptyRouteComponent },
        ]),
        { provide: COGLATAS_WORKSPACE_PREFERENCE_STORAGE, useClass: MemoryStorage },
        { provide: RealtimeFacade, useValue: realtime },
      ],
    });

    selection = TestBed.inject(WorkspaceSelectionFacade);
    activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    preferences = TestBed.inject(WorkspacePreferenceService);
    router = TestBed.inject(Router);
  });

  afterEach(() => TestBed.resetTestingModule());

  it('uses a valid route Workspace before a different valid local preference', () => {
    preferences.write(identity.tenantId, identity.userId, workspaceA.id);

    const resolved = selection.reconcileAuthorizedWorkspaces(
      [workspaceA, workspaceB],
      identity,
      workspaceB.id,
    );

    expect(resolved).toEqual({
      status: 'selected',
      workspaceId: workspaceB.id,
      source: 'route',
    });
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceB);
    expect(preferences.read(identity.tenantId, identity.userId)).toBe(workspaceB.id);
  });

  it('uses a valid tenant-and-user preference without depending on API row order', () => {
    preferences.write(identity.tenantId, identity.userId, workspaceB.id);

    const resolved = selection.reconcileAuthorizedWorkspaces(
      [workspaceA, workspaceB],
      identity,
      null,
    );

    expect(resolved.source).toBe('preference');
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceB);
  });

  it('auto-selects only when exactly one authorized active Workspace exists', () => {
    const resolved = selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);

    expect(resolved).toEqual({
      status: 'selected',
      workspaceId: workspaceA.id,
      source: 'single',
    });
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceA);
  });

  it('requires explicit selection for multiple Workspaces with no valid route or preference', () => {
    const resolved = selection.reconcileAuthorizedWorkspaces(
      [workspaceA, workspaceB],
      identity,
      null,
    );

    expect(resolved).toEqual({
      status: 'selectionRequired',
      workspaceId: null,
      source: null,
    });
    expect(activeWorkspace.activeWorkspace()).toBeNull();
  });

  it('discards a stale preference and clears protected state instead of retaining hidden metadata', () => {
    preferences.write(identity.tenantId, identity.userId, 'revoked-workspace');
    activeWorkspace.setActiveWorkspace({ id: 'revoked-workspace', label: 'Do not retain' });

    const resolved = selection.reconcileAuthorizedWorkspaces(
      [workspaceA, workspaceB],
      identity,
      null,
    );

    expect(resolved.status).toBe('selectionRequired');
    expect(preferences.read(identity.tenantId, identity.userId)).toBeNull();
    expect(realtime.clearForWorkspaceBoundary).toHaveBeenCalled();
    expect(activeWorkspace.activeWorkspace()).toBeNull();
  });

  it('fails closed when the Workspace named by a mounted route is revoked', () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA, workspaceB], identity, workspaceA.id);
    realtime.clearForWorkspaceBoundary.mockClear();

    const resolved = selection.reconcileAuthorizedWorkspaces(
      [workspaceB],
      identity,
      workspaceA.id,
    );

    expect(resolved).toEqual({
      status: 'unavailable',
      workspaceId: null,
      source: null,
    });
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(realtime.clearForWorkspaceBoundary).toHaveBeenCalledOnce();
    expect(preferences.read(identity.tenantId, identity.userId)).toBeNull();
  });

  it('clears old scope when the authenticated tenant or user identity changes', () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);
    realtime.clearForWorkspaceBoundary.mockClear();

    selection.beginLoading({ tenantId: 'tenant-b', userId: 'user-b' });

    expect(realtime.clearForWorkspaceBoundary).toHaveBeenCalledTimes(1);
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(selection.selection()).toEqual({
      status: 'loading',
      workspaceId: null,
      source: null,
    });
  });

  it('hides selection controls without destroying same-identity scope while the list reloads', async () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);
    realtime.clearForWorkspaceBoundary.mockClear();

    selection.beginLoading(identity);

    expect(realtime.clearForWorkspaceBoundary).not.toHaveBeenCalled();
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceA);
    expect(selection.selection()).toEqual({
      status: 'loading',
      workspaceId: null,
      source: null,
    });
    await expect(selection.selectWorkspace(workspaceA.id)).resolves.toBe(false);
  });

  it('activates the first authorized scope without clearing already-mounted deep-link state', () => {
    const clearSpy = realtime.clearForWorkspaceBoundary;
    const setSpy = vi.spyOn(activeWorkspace, 'setActiveWorkspace');

    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);

    expect(clearSpy).not.toHaveBeenCalled();
    expect(setSpy).toHaveBeenCalledWith(workspaceA);
    expect(realtime.runAuthoritativeHttpCatchUps).not.toHaveBeenCalled();
  });

  it('rehydrates protected HTTP state only after an actual Workspace switch commits', async () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA, workspaceB], identity, workspaceA.id);
    const setSpy = vi.spyOn(activeWorkspace, 'setActiveWorkspace');
    let selectionAtCatchUp: ReturnType<typeof selection.selection> | null = null;
    realtime.runAuthoritativeHttpCatchUps.mockImplementation(() => {
      selectionAtCatchUp = selection.selection();
      return Promise.resolve();
    });
    realtime.clearForWorkspaceBoundary.mockClear();
    realtime.runAuthoritativeHttpCatchUps.mockClear();

    await expect(selection.selectWorkspace(workspaceB.id)).resolves.toBe(true);

    expect(realtime.clearForWorkspaceBoundary).toHaveBeenCalledOnce();
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceB);
    expect(selection.selection()).toEqual({
      status: 'selected',
      workspaceId: workspaceB.id,
      source: 'explicit',
    });
    expect(realtime.runAuthoritativeHttpCatchUps).toHaveBeenCalledOnce();
    expect(selectionAtCatchUp).toEqual({
      status: 'selected',
      workspaceId: workspaceB.id,
      source: 'explicit',
    });
    expect(setSpy.mock.invocationCallOrder.at(-1)).toBeLessThan(
      realtime.runAuthoritativeHttpCatchUps.mock.invocationCallOrder[0],
    );

    await expect(selection.selectWorkspace(workspaceB.id)).resolves.toBe(true);
    expect(realtime.runAuthoritativeHttpCatchUps).toHaveBeenCalledOnce();
  });

  it('navigates to the neutral Workspace route before switching off scoped content', async () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA, workspaceB], identity, workspaceA.id);
    await router.navigateByUrl('/projects/project-a');
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');
    const clearSpy = realtime.clearForWorkspaceBoundary;
    const setSpy = vi.spyOn(activeWorkspace, 'setActiveWorkspace');
    clearSpy.mockClear();

    await expect(selection.selectWorkspace(workspaceB.id)).resolves.toBe(true);

    expect(navigateSpy).toHaveBeenCalledWith('/workspaces');
    expect(clearSpy).toHaveBeenCalledTimes(1);
    expect(setSpy).toHaveBeenCalledWith(workspaceB);
    expect(clearSpy.mock.invocationCallOrder[0]).toBeLessThan(setSpy.mock.invocationCallOrder[0]);
    expect(selection.selection()).toEqual({
      status: 'selected',
      workspaceId: workspaceB.id,
      source: 'explicit',
    });
    expect(preferences.read(identity.tenantId, identity.userId)).toBe(workspaceB.id);
  });

  it('rejects an explicit stale Workspace ID without changing current scope', async () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);

    await expect(selection.selectWorkspace('not-authorized')).resolves.toBe(false);
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceA);
  });

  it('marks a list authorization failure unavailable and discards its preference', () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);
    expect(preferences.read(identity.tenantId, identity.userId)).toBe(workspaceA.id);

    selection.markUnavailable(true);

    expect(selection.selection().status).toBe('unavailable');
    expect(preferences.read(identity.tenantId, identity.userId)).toBeNull();
    expect(activeWorkspace.activeWorkspace()).toBeNull();
  });

  it('refreshes the selected Workspace label when the authorized card ID is unchanged', () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);
    realtime.clearForWorkspaceBoundary.mockClear();

    selection.reconcileAuthorizedWorkspaces(
      [{ ...workspaceA, label: 'Workspace A renamed' }],
      identity,
      null,
    );

    expect(activeWorkspace.activeWorkspace()).toEqual({
      id: workspaceA.id,
      label: 'Workspace A renamed',
    });
    expect(realtime.clearForWorkspaceBoundary).not.toHaveBeenCalled();
  });

  it('preserves route intent across authorization pending then same-Workspace restoration', () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);
    realtime.clearForWorkspaceBoundary.mockClear();

    selection.markAuthorizationPending();
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);

    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceA);
    expect(realtime.clearForWorkspaceBoundary).not.toHaveBeenCalled();
  });

  it('keeps an explicit authorized selection in memory when storage is unavailable', async () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA, workspaceB], identity, null);
    const storage = TestBed.inject(COGLATAS_WORKSPACE_PREFERENCE_STORAGE) as MemoryStorage;
    storage.throwOnAccess = true;

    await expect(selection.selectWorkspace(workspaceB.id)).resolves.toBe(true);
    const retained = selection.reconcileAuthorizedWorkspaces(
      [workspaceA, workspaceB],
      identity,
      null,
    );
    expect(retained).toMatchObject({ workspaceId: workspaceB.id, source: 'explicit' });

    realtime.clearForWorkspaceBoundary.mockClear();
    const revoked = selection.reconcileAuthorizedWorkspaces(
      [workspaceA, workspaceC],
      identity,
      null,
    );
    expect(revoked.status).toBe('selectionRequired');
    expect(activeWorkspace.activeWorkspace()).toBeNull();
    expect(realtime.clearForWorkspaceBoundary).toHaveBeenCalledOnce();
  });

  it('preserves a mounted authorized scope across a transient list failure and retry', () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);
    realtime.clearForWorkspaceBoundary.mockClear();

    selection.markTransientFailure();

    expect(selection.selection().status).toBe('unavailable');
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceA);
    expect(realtime.clearForWorkspaceBoundary).not.toHaveBeenCalled();

    selection.reconcileAuthorizedWorkspaces([workspaceA], identity, null);
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceA);
    expect(realtime.clearForWorkspaceBoundary).not.toHaveBeenCalled();
  });

  it('activates the latest authorized card after neutral navigation awaits guards', async () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA, workspaceB], identity, workspaceA.id);
    await router.navigateByUrl('/projects/project-a');
    let finishNeutralNavigation!: (value: boolean) => void;
    const navigation = new Promise<boolean>((resolve) => { finishNeutralNavigation = resolve; });
    vi.spyOn(router, 'navigateByUrl').mockReturnValue(navigation);

    const switching = selection.selectWorkspace(workspaceB.id);
    selection.reconcileAuthorizedWorkspaces(
      [workspaceA, { ...workspaceB, label: 'Workspace B renamed' }],
      identity,
      workspaceA.id,
    );
    finishNeutralNavigation(true);

    await expect(switching).resolves.toBe(true);
    expect(activeWorkspace.activeWorkspace()).toEqual({
      id: workspaceB.id,
      label: 'Workspace B renamed',
    });
  });

  it('does not commit a Workspace after its caller operation is superseded', async () => {
    selection.reconcileAuthorizedWorkspaces([workspaceA, workspaceB], identity, workspaceA.id);
    await router.navigateByUrl('/projects/project-a');
    let finishNeutralNavigation!: (value: boolean) => void;
    const navigation = new Promise<boolean>((resolve) => { finishNeutralNavigation = resolve; });
    vi.spyOn(router, 'navigateByUrl').mockReturnValue(navigation);
    let operationCurrent = true;

    const switching = selection.selectWorkspace(workspaceB.id, () => operationCurrent);
    operationCurrent = false;
    finishNeutralNavigation(true);

    await expect(switching).resolves.toBe(false);
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceA);
    expect(selection.selection().workspaceId).toBe(workspaceA.id);
  });

  it('does not let an already-stale caller cancel a newer pending selection', async () => {
    selection.reconcileAuthorizedWorkspaces(
      [workspaceA, workspaceB, workspaceC],
      identity,
      workspaceA.id,
    );
    await router.navigateByUrl('/projects/project-a');
    let finishNeutralNavigation!: (value: boolean) => void;
    const navigation = new Promise<boolean>((resolve) => { finishNeutralNavigation = resolve; });
    vi.spyOn(router, 'navigateByUrl').mockReturnValue(navigation);

    const currentSwitch = selection.selectWorkspace(workspaceC.id);
    await expect(selection.selectWorkspace(workspaceB.id, () => false)).resolves.toBe(false);
    finishNeutralNavigation(true);

    await expect(currentSwitch).resolves.toBe(true);
    expect(activeWorkspace.activeWorkspace()).toEqual(workspaceC);
    expect(selection.selection().workspaceId).toBe(workspaceC.id);
  });
});

describe('Workspace route context helpers', () => {
  it('extracts only explicit Workspace route context', () => {
    expect(workspaceIdFromRoute('/workspaces/workspace%20a/members?tab=active')).toBe(
      'workspace a',
    );
    expect(workspaceIdFromRoute('/workspaces')).toBeNull();
    expect(workspaceIdFromRoute('/projects/project-a')).toBeNull();
    expect(workspaceIdFromRoute('/workspaces/%E0%A4%A/members')).toBeNull();
  });

  it('distinguishes Workspace-scoped content from global account content', () => {
    expect(isWorkspaceSpecificRoute('/workspaces/workspace-a/members')).toBe(true);
    expect(isWorkspaceSpecificRoute('/projects/project-a?tab=schedule')).toBe(true);
    expect(isWorkspaceSpecificRoute('/files')).toBe(true);
    expect(isWorkspaceSpecificRoute('/account')).toBe(false);
    expect(isWorkspaceSpecificRoute('/workspaces')).toBe(false);
  });
});
