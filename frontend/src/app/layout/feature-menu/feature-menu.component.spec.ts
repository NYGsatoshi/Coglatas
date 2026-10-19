import { DeferBlockState, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';
import { NavigationItem } from '../../shared/navigation/navigation.models';
import { FeatureMenuComponent } from './feature-menu.component';

const navigationItems: readonly NavigationItem[] = [
  { id: 'workspaces', label: 'Workspaces', route: '/workspaces' },
  { id: 'files', label: 'Files', route: '/files' },
];

const pinnedItems: readonly NavigationItem[] = [
  { id: 'projects', label: 'Projects', route: '/projects', placement: 'pinned' },
  { id: 'tasks', label: 'My Tasks', route: '/tasks', placement: 'pinned' },
];

describe('FeatureMenuComponent', () => {
  // Cold deferred Lucide module transformation belongs to fixture setup.
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FeatureMenuComponent],
      providers: [provideRouter([{ path: '**', component: FeatureMenuComponent }])],
    }).compileComponents();
  }, 30_000);

  async function createFeatureMenu(collapsed = false) {
    const fixture = TestBed.createComponent(FeatureMenuComponent);
    fixture.componentRef.setInput('navigationItems', navigationItems);
    fixture.componentRef.setInput('pinnedItems', pinnedItems);
    fixture.componentRef.setInput('collapsible', true);
    fixture.componentRef.setInput('collapsed', collapsed);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  for (const collapsed of [false, true]) {
    it.each([
      ['/workspaces/workspace-a/channels/channel-a?focusMessageId=message-a', 'teams'],
      ['/workspaces/workspace-a/files', 'files'],
      ['/workspaces/workspace-a/projects', 'projects'],
      ['/workspaces/workspace-a/members', 'workspaces'],
      ['/dm/group-a', 'dm'],
    ])(`selects the owning destination for %s with collapsed menu ${collapsed}`, async (url, destination) => {
      const fixture = await createFeatureMenu(collapsed);
      fixture.componentRef.setInput('navigationItems', [
        ...navigationItems,
        { id: 'teams', label: 'Teams', route: '/teams' },
        { id: 'dm', label: 'DM', route: '/dm' },
      ]);
      await TestBed.inject(Router).navigateByUrl(url);
      fixture.detectChanges();
      const active = fixture.nativeElement.querySelectorAll('.feature-menu__item--active') as NodeListOf<HTMLElement>;
      expect(active).toHaveLength(1);
      expect(active[0].dataset['testid']).toBe(`nav-${destination}`);
      expect(active[0].getAttribute('aria-current')).toBe('page');
    });
  }

  it('emits a collapse request from the desktop menu toggle', async () => {
    const fixture = await createFeatureMenu(false);
    let requestedState: boolean | undefined;
    fixture.componentInstance.collapsedChange.subscribe((value) => {
      requestedState = value;
    });

    const element = fixture.nativeElement as HTMLElement;
    const menu = element.querySelector<HTMLElement>('.feature-menu');
    const toggle = element.querySelector<HTMLButtonElement>('[data-testid="feature-menu-toggle"]');

    expect(menu?.classList.contains('feature-menu--collapsible')).toBe(true);
    expect(toggle).toBeTruthy();
    expect(toggle?.getAttribute('aria-expanded')).toBe('true');
    expect(element.querySelector('[data-testid="nav-workspaces"]')).toBeTruthy();

    toggle?.click();

    expect(requestedState).toBe(true);
  });

  it('visually separates primary navigation from pinned shortcuts', async () => {
    const fixture = await createFeatureMenu(false);
    const element = fixture.nativeElement as HTMLElement;
    const primary = element.querySelector<HTMLElement>('[data-testid="primary-navigation-section"]');
    const pinned = element.querySelector<HTMLElement>('[data-testid="pinned-navigation-section"]');

    expect(primary).toBeTruthy();
    expect(pinned).toBeTruthy();
    expect(primary?.querySelector('[data-testid="nav-workspaces"]')).toBeTruthy();
    expect(primary?.querySelector('[data-testid="nav-projects"]')).toBeNull();
    expect(pinned?.querySelector('[data-testid="nav-projects"]')).toBeTruthy();
    expect(pinned?.textContent).toContain('ピン留め');
  });

  it('keeps primary and pinned destinations reachable in the collapsed rail', async () => {
    const fixture = await createFeatureMenu(true);
    const element = fixture.nativeElement as HTMLElement;
    const toggle = element.querySelector<HTMLButtonElement>('[data-testid="feature-menu-toggle"]');
    const primaryLink = element.querySelector<HTMLAnchorElement>('[data-testid="nav-workspaces"]');
    const pinnedLink = element.querySelector<HTMLAnchorElement>('[data-testid="nav-projects"]');

    expect(toggle).toBeTruthy();
    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
    expect(toggle?.getAttribute('aria-label')).toBe('メニューを開く');
    expect(element.querySelector('[data-testid="feature-menu-rail"]')).toBeTruthy();
    expect(primaryLink?.getAttribute('href')).toBe('/workspaces');
    expect(primaryLink?.getAttribute('title')).toBe('ワークスペース');
    expect(pinnedLink?.getAttribute('href')).toBe('/projects');
    expect(pinnedLink?.getAttribute('title')).toBe('ピン留め: プロジェクト');
  });

  it('keeps localized names and destinations beside decorative icons in the collapsed rail', async () => {
    const fixture = await createFeatureMenu(true);
    const element = fixture.nativeElement as HTMLElement;
    const workspaceLink = element.querySelector<HTMLAnchorElement>('[data-testid="nav-workspaces"]');
    const projectLink = element.querySelector<HTMLAnchorElement>('[data-testid="nav-projects"]');
    expect(workspaceLink?.textContent).toContain('ワークスペース');
    expect(workspaceLink?.getAttribute('href')).toBe('/workspaces');
    for (const block of await fixture.getDeferBlocks()) { await block.render(DeferBlockState.Complete); }
    expect(workspaceLink?.querySelector('svg.lucide-house')?.getAttribute('aria-hidden')).toBe('true');
    expect(projectLink?.querySelector('svg.lucide-layout-grid')?.getAttribute('aria-hidden')).toBe('true');
    expect(workspaceLink?.textContent).toContain('ワークスペース');

    TestBed.inject(I18nService).setLocale('en');
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="nav-workspaces"]')).toBe(workspaceLink);
    expect(workspaceLink?.getAttribute('title')).toBe('Workspaces');
    expect(workspaceLink?.textContent).toContain('Workspaces');
    expect(workspaceLink?.getAttribute('href')).toBe('/workspaces');
    expect(projectLink?.getAttribute('href')).toBe('/projects');
    expect(projectLink?.querySelector('app-feature-menu-icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('allows the Pinned section to be collapsed independently', async () => {
    const fixture = await createFeatureMenu(false);
    let requestedState: boolean | undefined;
    fixture.componentInstance.pinnedCollapsedChange.subscribe((value) => {
      requestedState = value;
    });

    const toggle = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="pinned-section-toggle"]'
    );
    toggle?.click();

    expect(requestedState).toBe(true);
  });

  it('offers keyboard-operable move controls instead of requiring drag and drop', async () => {
    const fixture = await createFeatureMenu(false);
    let request: { itemId: string; direction: string } | undefined;
    fixture.componentInstance.pinnedMove.subscribe((value) => {
      request = value;
    });

    const element = fixture.nativeElement as HTMLElement;
    const moveDown = element.querySelector<HTMLButtonElement>('[data-testid="pinned-move-down-projects"]');
    const moveUp = element.querySelector<HTMLButtonElement>('[data-testid="pinned-move-up-projects"]');

    expect(moveUp?.disabled).toBe(true);
    expect(moveDown?.disabled).toBe(false);
    expect(moveDown?.getAttribute('aria-label')).toBe('プロジェクトを下へ移動');

    moveDown?.click();

    expect(request).toEqual({ itemId: 'projects', direction: 'down' });
  });

  it('does not add desktop collapse layout or controls to the compact mobile menu', async () => {
    const fixture = TestBed.createComponent(FeatureMenuComponent);
    fixture.componentRef.setInput('navigationItems', navigationItems);
    fixture.componentRef.setInput('pinnedItems', pinnedItems);
    fixture.componentRef.setInput('compact', true);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const menu = element.querySelector<HTMLElement>('.feature-menu');

    expect(menu?.classList.contains('feature-menu--compact')).toBe(true);
    expect(menu?.classList.contains('feature-menu--collapsible')).toBe(false);
    expect(menu?.classList.contains('feature-menu--collapsed')).toBe(false);
    expect(element.querySelector('[data-testid="feature-menu-toggle"]')).toBeNull();
    expect(element.querySelector('[data-testid="nav-workspaces"]')).toBeTruthy();
    expect(element.querySelector('[data-testid="nav-projects"]')).toBeTruthy();
  });
});
