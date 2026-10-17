import { A11yModule } from '@angular/cdk/a11y';
import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { I18nService } from '../../core/i18n/i18n.service';
import { RealtimeConnectionIndicatorComponent } from '../../core/realtime/realtime-connection-indicator.component';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { NavigationMoveRequest } from '../../shared/navigation/navigation.models';
import { RightPanelComponent } from '../../shared/right-panel/right-panel/right-panel.component';
import { AccountRailComponent } from '../account-rail/account-rail.component';
import { FeatureMenuComponent } from '../feature-menu/feature-menu.component';
import { MobileHeaderComponent } from '../mobile-header/mobile-header.component';
import { TopBarComponent } from '../top-bar/top-bar.component';
import { AppShellFacade } from './app-shell.facade';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-shell',
  standalone: true,
  imports: [
    AccountRailComponent,
    A11yModule,
    FeatureMenuComponent,
    MobileHeaderComponent,
    RealtimeConnectionIndicatorComponent,
    RightPanelComponent,
    RouterOutlet,
    TopBarComponent
  ],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements AfterViewChecked {
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly authSession = inject(AuthSessionFacade);
  readonly i18n = inject(I18nService);
  readonly realtime = inject(RealtimeFacade);
  readonly facade = inject(AppShellFacade);
  private readonly mobileDrawer = viewChild<ElementRef<HTMLElement>>('mobileDrawer');

  readonly viewModel = this.facade.viewModel;
  readonly mobileDrawerOpen = signal(false);
  readonly featureMenuCollapsed = signal(false);
  readonly pinnedSectionCollapsed = signal(false);
  readonly pinnedOrder = signal<readonly string[]>([]);
  readonly orderedPinnedNavigationItems = computed(() => {
    const items = this.viewModel().pinnedNavigationItems;
    const requestedOrder = this.pinnedOrder();
    if (requestedOrder.length === 0) {
      return items;
    }

    const itemsById = new Map(items.map((item) => [item.id, item]));
    const ordered = requestedOrder
      .map((itemId) => itemsById.get(itemId))
      .filter((item): item is (typeof items)[number] => item !== undefined);
    const included = new Set(ordered.map((item) => item.id));

    return [...ordered, ...items.filter((item) => !included.has(item.id))];
  });
  readonly logoutPending = signal(false);
  private readonly logoutFailed = signal(false);
  readonly logoutError = computed(() => this.logoutFailed() ? this.i18n.translate('shell.logoutFailed') : '');
  readonly rightPanelReturnFocus = signal<HTMLElement | null>(null);
  private mobileDrawerReturnFocus: HTMLElement | null = null;
  private mobileDrawerFocused = false;

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => {
        this.closeMobileDrawer(false);
      });
  }

  selectWorkspace(workspaceId: string): void {
    void this.facade.selectWorkspace(workspaceId);
  }

  setFeatureMenuCollapsed(collapsed: boolean): void {
    this.featureMenuCollapsed.set(collapsed);
  }

  setPinnedSectionCollapsed(collapsed: boolean): void {
    this.pinnedSectionCollapsed.set(collapsed);
  }

  movePinnedItem(request: NavigationMoveRequest): void {
    const items = [...this.orderedPinnedNavigationItems()];
    const currentIndex = items.findIndex((item) => item.id === request.itemId);
    if (currentIndex < 0) {
      return;
    }

    const targetIndex = request.direction === 'up' ? currentIndex - 1 : currentIndex + 1;
    if (targetIndex < 0 || targetIndex >= items.length) {
      return;
    }

    [items[currentIndex], items[targetIndex]] = [items[targetIndex], items[currentIndex]];
    this.pinnedOrder.set(items.map((item) => item.id));
  }

  logout(): void {
    if (this.logoutPending()) {
      return;
    }

    this.logoutFailed.set(false);
    this.logoutPending.set(true);
    this.authSession.logout().subscribe({
      next: () => {
        this.logoutPending.set(false);
      },
      error: () => {
        this.logoutPending.set(false);
        this.logoutFailed.set(true);
      }
    });
  }

  toggleMobileDrawer(): void {
    if (this.mobileDrawerOpen()) {
      this.closeMobileDrawer();
      return;
    }

    this.openMobileDrawer();
  }

  openMobileDrawer(): void {
    const activeElement = document.activeElement;
    this.mobileDrawerReturnFocus = activeElement instanceof HTMLElement ? activeElement : null;
    this.mobileDrawerFocused = false;
    this.mobileDrawerOpen.set(true);
  }

  closeMobileDrawer(returnFocus = true): void {
    if (!this.mobileDrawerOpen()) {
      return;
    }

    this.mobileDrawerOpen.set(false);
    this.mobileDrawerFocused = false;

    if (returnFocus) {
      const target = this.mobileDrawerReturnFocus;
      queueMicrotask(() => target?.focus());
    }

    this.mobileDrawerReturnFocus = null;
  }

  toggleRightPanelFromTopBar(trigger: HTMLElement): void {
    this.rightPanelReturnFocus.set(trigger);
    if (this.isTabletOrMobile()) {
      const currentMode = this.viewModel().rightPanelMode;
      this.facade.setRightPanelMode(currentMode === 'drawer' ? 'collapsed' : 'drawer');
      return;
    }

    this.facade.toggleRightPanel();
  }

  ngAfterViewChecked(): void {
    if (!this.mobileDrawerOpen() || this.mobileDrawerFocused) {
      return;
    }

    const drawer = this.mobileDrawer()?.nativeElement;
    const firstFocusable = drawer?.querySelector<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])'
    );
    (firstFocusable ?? drawer)?.focus();
    this.mobileDrawerFocused = true;
  }

  @HostListener('document:keydown.escape', ['$event'])
  closeMobileDrawerFromEscape(event: KeyboardEvent): void {
    if (!this.mobileDrawerOpen()) {
      return;
    }

    event.preventDefault();
    this.closeMobileDrawer();
  }

  private isTabletOrMobile(): boolean {
    return typeof window !== 'undefined' && window.matchMedia('(max-width: 860px)').matches;
  }
}
