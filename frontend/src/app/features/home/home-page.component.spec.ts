import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routes } from '../../app.routes';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkspaceSelectionFacade, WorkspaceSelectionSnapshot } from '../../core/workspace/workspace-selection.facade';
import { AppShellFacade, DEFAULT_NAVIGATION_ITEMS } from '../../layout/app-shell/app-shell.facade';
import { WorkspacesFacade } from '../workspaces/workspaces.facade';
import { MEMBER_WORKSPACE, OWNER_WORKSPACE } from '../workspaces/workspaces.mock';
import { WorkspaceDashboardViewModel } from '../workspaces/workspaces.types';
import { HomePageComponent } from './home-page.component';

describe('HomePageComponent pilot entry', () => {
  const dashboard = signal<WorkspaceDashboardViewModel>({
    status: 'ready', title: 'Workspaces', subtitle: '', pageCapabilities: [],
    workspaces: [OWNER_WORKSPACE, MEMBER_WORKSPACE],
  });
  const selection = signal<WorkspaceSelectionSnapshot>({ status: 'selectionRequired', workspaceId: null, source: null });
  const navigation = signal(DEFAULT_NAVIGATION_ITEMS);
  const authenticated = signal(true);
  const activeWorkspace = signal<{ id: string; label: string } | null>(null);
  const selectWorkspace = vi.fn<() => Promise<boolean>>();
  const loadWorkspaces = vi.fn();

  beforeEach(async () => {
    dashboard.set({ status: 'ready', title: 'Workspaces', subtitle: '', pageCapabilities: [], workspaces: [OWNER_WORKSPACE, MEMBER_WORKSPACE] });
    selection.set({ status: 'selectionRequired', workspaceId: null, source: null });
    navigation.set(DEFAULT_NAVIGATION_ITEMS);
    authenticated.set(true);
    activeWorkspace.set({ id: MEMBER_WORKSPACE.id, label: MEMBER_WORKSPACE.displayName });
    selectWorkspace.mockReset().mockResolvedValue(true);
    loadWorkspaces.mockReset();
    await TestBed.configureTestingModule({
      imports: [HomePageComponent], providers: [provideRouter([]),
        { provide: WorkspacesFacade, useValue: { dashboard, loadWorkspaces } },
        { provide: WorkspaceSelectionFacade, useValue: { selection, selectWorkspace } },
        { provide: AppShellFacade, useValue: { viewModel: () => ({ navigationItems: navigation(), session: { isAuthenticated: authenticated() }, workspace: activeWorkspace() }) } },
      ],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('en');
  });
  afterEach(() => { window.localStorage.removeItem('coglatas.locale'); TestBed.resetTestingModule(); });

  function render() {
    const fixture = TestBed.createComponent(HomePageComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('adds an explicit Home while retaining root Workspace selection and independent pilot destinations', async () => {
    const children = routes.find((route) => route.path === '')!.children!;
    expect(children.find((route) => route.path === '')!.redirectTo).toBe('workspaces');
    expect(await children.find((route) => route.path === 'home')!.loadComponent!()).toBe(HomePageComponent);
    expect(DEFAULT_NAVIGATION_ITEMS.slice(0, 5).map((item) => item.id)).toEqual(['home', 'workspaces', 'teams', 'dm', 'files']);
    const fixture = render();
    expect([...fixture.nativeElement.querySelectorAll('.home-destination')].map((link: HTMLAnchorElement) => link.getAttribute('href'))).toEqual(['/workspaces', '/teams', '/dm', '/files']);
    expect(fixture.nativeElement.textContent).not.toMatch(/ProjectIDE|My tasks|Review requests|Open drafts/);
    expect(loadWorkspaces).not.toHaveBeenCalled();
  });

  it('does not select row zero or fabricate a context before authoritative selection', () => {
    const fixture = render();
    expect(fixture.componentInstance.selectedWorkspace()).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="home-context-workspace"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="home-context"]').textContent).toContain('Select a Workspace');
    expect(selectWorkspace).not.toHaveBeenCalled();
  });

  it('uses the selected current projection, preserves zero, and distinguishes unavailable counts', () => {
    selection.set({ status: 'selected', workspaceId: MEMBER_WORKSPACE.id, source: 'explicit' });
    dashboard.update((value) => ({ ...value, workspaces: [{ ...MEMBER_WORKSPACE, unreadConversationCount: 0 }] }));
    const fixture = render();
    expect(fixture.nativeElement.querySelector('[data-testid="home-context-workspace"]').textContent).toBe(MEMBER_WORKSPACE.displayName);
    expect(fixture.nativeElement.querySelector('li button').getAttribute('aria-label')).toBe(`Selected Workspace ${MEMBER_WORKSPACE.displayName}`);
    expect(fixture.componentInstance.unreadLabel(dashboard().workspaces[0])).toBe('0');
    dashboard.update((value) => ({ ...value, workspaces: [{ ...MEMBER_WORKSPACE, unreadConversationCount: null }] }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="home-context"]').textContent).toContain('Unavailable');
  });

  it('clears names, context and member links when the shared projection is denied or revalidating', () => {
    selection.set({ status: 'selected', workspaceId: MEMBER_WORKSPACE.id, source: 'explicit' });
    const fixture = render();
    expect(fixture.nativeElement.querySelector(`a[href="/workspaces/${MEMBER_WORKSPACE.id}/members"]`)).not.toBeNull();
    for (const status of ['loading', 'permissionDenied', 'error'] as const) {
      dashboard.update((value) => ({ ...value, status, workspaces: [] }));
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain(MEMBER_WORKSPACE.displayName);
      expect(fixture.nativeElement.querySelector('a[href*="/members"]')).toBeNull();
      expect(fixture.componentInstance.selectedWorkspace()).toBeNull();
    }
  });

  it('honors projected navigation and member/selection capabilities without inferring them from roles', async () => {
    navigation.set(DEFAULT_NAVIGATION_ITEMS.filter((item) => item.id !== 'files'));
    const denied = { ...OWNER_WORKSPACE, capabilities: [] };
    dashboard.update((value) => ({ ...value, workspaces: [denied] }));
    selection.set({ status: 'selected', workspaceId: denied.id, source: 'explicit' });
    activeWorkspace.set({ id: denied.id, label: denied.displayName });
    const fixture = render();
    expect(fixture.nativeElement.querySelector('[data-testid="home-open-files"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href*="/members"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('li button').disabled).toBe(true);
    await fixture.componentInstance.selectWorkspace(denied);
    expect(selectWorkspace).not.toHaveBeenCalled();
  });

  it('delegates selection once, waits for the shared selection, and reports a failed transition', async () => {
    let finish!: (result: boolean) => void;
    selectWorkspace.mockReturnValue(new Promise((resolve) => { finish = resolve; }));
    const fixture = render();
    const pending = fixture.componentInstance.selectWorkspace(MEMBER_WORKSPACE);
    await fixture.componentInstance.selectWorkspace(OWNER_WORKSPACE);
    expect(selectWorkspace).toHaveBeenCalledTimes(1);
    expect(selectWorkspace).toHaveBeenCalledWith(MEMBER_WORKSPACE.id);
    expect(fixture.componentInstance.selectedWorkspace()).toBeNull();
    finish(false); await pending; fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('could not be selected');
    expect(fixture.componentInstance.selectionPending()).toBe(false);
  });

  it('hides context during selection replacement and drops protected fields immediately on logout', () => {
    selection.set({ status: 'selected', workspaceId: MEMBER_WORKSPACE.id, source: 'explicit' });
    const fixture = render();
    expect(fixture.componentInstance.selectedWorkspace()?.id).toBe(MEMBER_WORKSPACE.id);
    selection.set({ status: 'loading', workspaceId: null, source: null });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="home-context-workspace"]')).toBeNull();
    selection.set({ status: 'selected', workspaceId: MEMBER_WORKSPACE.id, source: 'explicit' });
    activeWorkspace.set({ id: OWNER_WORKSPACE.id, label: OWNER_WORKSPACE.displayName });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="home-context-workspace"]')).toBeNull();
    activeWorkspace.set({ id: MEMBER_WORKSPACE.id, label: MEMBER_WORKSPACE.displayName });
    authenticated.set(false);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain(MEMBER_WORKSPACE.displayName);
    expect(fixture.nativeElement.textContent).not.toContain(OWNER_WORKSPACE.displayName);
    expect(fixture.nativeElement.querySelector('a[href*="/members"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('.home-destination')).toBeNull();
    fixture.componentInstance.refresh();
    expect(loadWorkspaces).not.toHaveBeenCalled();
  });

  it('renders empty and loading recovery without overlapping refreshes', () => {
    dashboard.update((value) => ({ ...value, status: 'noWorkspaceAccess', workspaces: [] }));
    const fixture = render();
    expect(fixture.nativeElement.querySelector('[data-testid="home-empty"]')).not.toBeNull();
    fixture.componentInstance.refresh(); expect(loadWorkspaces).toHaveBeenCalledTimes(1);
    dashboard.update((value) => ({ ...value, status: 'loading' }));
    fixture.detectChanges(); fixture.componentInstance.refresh();
    expect(loadWorkspaces).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('[data-testid="home-loading"]')).not.toBeNull();
  });

  it('announces complete current read states while removing protected Workspace context', () => {
    const i18n = TestBed.inject(I18nService);
    selection.set({ status: 'selected', workspaceId: MEMBER_WORKSPACE.id, source: 'explicit' });
    const fixture = render();
    expect(fixture.nativeElement.textContent).toContain(MEMBER_WORKSPACE.displayName);

    dashboard.update((value) => ({ ...value, status: 'loading', workspaces: [] }));
    fixture.detectChanges();
    const loading = fixture.nativeElement.querySelector('[data-testid="home-loading"]');
    expect(loading.textContent).toContain(i18n.translate('shared.loading'));

    for (const status of ['permissionDenied', 'error'] as const) {
      dashboard.update((value) => ({ ...value, status }));
      fixture.detectChanges();
      const unavailable = fixture.nativeElement.querySelector('[data-testid="home-unavailable"]');
      expect(unavailable.textContent).toContain(i18n.translate(status === 'error' ? 'workspace.loadError' : 'workspace.denied'));
      expect(unavailable.textContent).toContain(i18n.translate(status === 'error' ? 'workspace.retryLater' : 'workspace.deniedMessage'));
      expect(fixture.nativeElement.querySelector('[data-testid="home-context"]').textContent).toContain(i18n.translate('home.selectContext'));
      expect(fixture.nativeElement.textContent).not.toContain(MEMBER_WORKSPACE.displayName);
      expect(fixture.nativeElement.querySelector('a[href*="/members"]')).toBeNull();
    }
  });

  it('localizes Home and displays only the canonical projected role or an explicit unavailable value', () => {
    const fixture = render();
    expect(fixture.componentInstance.roleLabel({ ...OWNER_WORKSPACE, currentUserRole: null })).toBe('Role unavailable');
    expect(fixture.componentInstance.roleLabel({ ...OWNER_WORKSPACE, currentUserRole: 'ReadOnly' })).toBe('Read only');
    TestBed.inject(I18nService).setLocale('ja'); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toBe('ホーム');
    expect(fixture.componentInstance.roleLabel({ ...OWNER_WORKSPACE, currentUserRole: 'ReadOnly' })).toBe('閲覧のみ');
  });
});
