import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import {
  COGLATAS_AUTH_SESSION_MOCK,
  DEFAULT_AUTH_SESSION
} from '../../core/auth/auth-session.facade';
import { COGLATAS_WORKSPACES_DASHBOARD_MOCK } from '../../features/workspaces/workspaces.facade';
import { MEMBER_WORKSPACE } from '../../features/workspaces/workspaces.mock';
import { COGLATAS_RIGHT_PANEL_MOCK } from '../../shared/right-panel/right-panel.facade';
import { AppShellComponent } from './app-shell.component';

@Component({
  standalone: true,
  template: '<p>準備中</p>'
})
class TestRouteComponent {}

describe('AppShellComponent', () => {
  async function configure(capabilities = DEFAULT_AUTH_SESSION.capabilities): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([
          { path: 'workspaces', component: TestRouteComponent },
          { path: 'projects', component: TestRouteComponent },
          { path: 'projects/:projectId', component: TestRouteComponent },
          { path: 'tasks', component: TestRouteComponent }
        ]),
        {
          provide: COGLATAS_AUTH_SESSION_MOCK,
          useValue: {
            ...DEFAULT_AUTH_SESSION,
            capabilities
          }
        },
        {
          provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK,
          useValue: {
            status: 'ready',
            title: 'Workspaces',
            subtitle: 'Authorized Workspaces',
            workspaces: [MEMBER_WORKSPACE],
            pageCapabilities: []
          }
        },
        { provide: COGLATAS_RIGHT_PANEL_MOCK, useValue: {} }
      ]
    }).compileComponents();
  }

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('separates Projects and My Tasks into Pinned for normal users with projects:view', async () => {
    await configure(['workspace:view', 'announcements:view', 'projects:view', 'files:view', 'account:view']);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const desktopMenu = element.querySelector<HTMLElement>('.app-shell__feature-menu');
    const primary = desktopMenu?.querySelector<HTMLElement>('[data-testid="primary-navigation-section"]');
    const pinned = desktopMenu?.querySelector<HTMLElement>('[data-testid="pinned-navigation-section"]');

    expect(primary?.querySelector('[data-testid="nav-workspaces"]')).toBeTruthy();
    expect(primary?.querySelector('[data-testid="nav-projects"]')).toBeNull();
    expect(pinned?.querySelector('[data-testid="nav-projects"]')).toBeTruthy();
    expect(pinned?.querySelector('[data-testid="nav-my-tasks"]')).toBeTruthy();
    expect(pinned?.querySelector('a[href="/projects"]')).toBeTruthy();
    expect(pinned?.querySelector('a[href="/tasks"]')).toBeTruthy();
    expect(element.querySelector('a[href="/admin/audit"]')).toBeNull();
  });

  it('hides Projects and My Tasks when projects:view is absent', async () => {
    await configure(['workspace:view', 'files:view', 'account:view']);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('a[href="/projects"]')).toBeNull();
    expect(element.querySelector('a[href="/tasks"]')).toBeNull();
  });

  it('exposes My Tasks in mobile navigation with the same filtering', async () => {
    await configure(['workspace:view', 'projects:view']);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.componentInstance.openMobileDrawer();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const mobileDrawer = element.querySelector('.app-shell__mobile-drawer');

    expect(mobileDrawer?.querySelector('a[href="/projects"]')).toBeTruthy();
    expect(mobileDrawer?.querySelector('[data-testid="nav-my-tasks"]')).toBeTruthy();
    expect(mobileDrawer?.querySelector('a[href="/tasks"]')).toBeTruthy();
    expect(mobileDrawer?.querySelector('a[href="/admin/audit"]')).toBeNull();
  });

  it('keeps major destinations reachable after the desktop sidebar collapses to a rail', async () => {
    await configure(['workspace:view', 'projects:view', 'files:view']);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.componentInstance.setFeatureMenuCollapsed(true);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const desktopMenu = element.querySelector<HTMLElement>('.app-shell__feature-menu');

    expect(desktopMenu?.querySelector('[data-testid="feature-menu-rail"]')).toBeTruthy();
    expect(desktopMenu?.querySelector('a[href="/workspaces"]')).toBeTruthy();
    expect(desktopMenu?.querySelector('a[href="/files"]')).toBeTruthy();
    expect(desktopMenu?.querySelector('a[href="/projects"]')).toBeTruthy();
  });

  it('keeps the current location visible for nested project routes', async () => {
    await configure(['workspace:view', 'projects:view']);

    const fixture = TestBed.createComponent(AppShellComponent);
    const router = TestBed.inject(Router);
    fixture.detectChanges();

    await router.navigateByUrl('/projects/project-123');
    await fixture.whenStable();
    fixture.detectChanges();

    const projectLink = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      '.app-shell__feature-menu [data-testid="nav-projects"]'
    );
    expect(projectLink?.classList.contains('feature-menu__item--active')).toBe(true);
    expect(projectLink?.getAttribute('aria-current')).toBe('page');
  });

  it('reorders Pinned shortcuts through non-drag controls', async () => {
    await configure(['workspace:view', 'projects:view']);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.orderedPinnedNavigationItems().map((item) => item.id)).toEqual([
      'projects',
      'my-tasks'
    ]);

    fixture.componentInstance.movePinnedItem({ itemId: 'projects', direction: 'down' });
    fixture.detectChanges();

    expect(fixture.componentInstance.orderedPinnedNavigationItems().map((item) => item.id)).toEqual([
      'my-tasks',
      'projects'
    ]);

    const desktopPinnedLinks = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        '.app-shell__feature-menu [data-testid="pinned-navigation-section"] a'
      )
    ).map((link) => link.getAttribute('data-testid'));
    expect(desktopPinnedLinks).toEqual(['nav-my-tasks', 'nav-projects']);
  });

  it('closes mobile navigation on route change', async () => {
    await configure();

    const fixture = TestBed.createComponent(AppShellComponent);
    const router = TestBed.inject(Router);
    fixture.componentInstance.openMobileDrawer();

    await router.navigateByUrl('/projects');
    fixture.detectChanges();

    expect(fixture.componentInstance.mobileDrawerOpen()).toBe(false);
  });

  it('shows the Workspace context and separate global actions without a fake search control', async () => {
    await configure();

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="logout-action"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="workspace-switcher"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="workspace-members-action"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="workspace-research-status"]')?.textContent).toContain('0 進行中');
    expect(element.querySelector('[data-testid="workspace-research-status"]')?.textContent).toContain('0 要レビュー');
    expect(element.querySelector('nav[aria-label="共通の操作"]')).not.toBeNull();
    expect(element.querySelector('a[href="#app-shell-main-content"]')).not.toBeNull();
    expect(element.querySelector('main#app-shell-main-content')).not.toBeNull();
    expect(element.querySelector('[data-testid="page-search"]')).toBeNull();
  });

  it('renders without backend calls', async () => {
    await configure();
    const fetchSpy = vi.spyOn(globalThis, 'fetch');

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('メニュー');
    expect(fixture.nativeElement.textContent).toContain('ピン留め');
    expect(fetchSpy).not.toHaveBeenCalled();

    fetchSpy.mockRestore();
  });
});
