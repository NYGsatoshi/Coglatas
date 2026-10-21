import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';

import {
  NavigationItem,
  NavigationMoveDirection,
  NavigationMoveRequest
} from '../../shared/navigation/navigation.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { FeatureMenuIconComponent } from './feature-menu-icon.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-feature-menu',
  standalone: true,
  imports: [RouterLink, FeatureMenuIconComponent],
  template: `
    <nav
      class="feature-menu"
      [class.feature-menu--compact]="compact"
      [class.feature-menu--collapsible]="collapsible"
      [class.feature-menu--collapsed]="collapsible && collapsed"
      [attr.aria-label]="navigationAriaLabel"
    >
      @if (!collapsed) {
        <a class="feature-menu__brand" routerLink="/home" (click)="itemSelected.emit()">
          <img src="brand/coglatas-paper-plane.svg" width="28" height="28" alt="" aria-hidden="true" />Coglatas
        </a>
      }
      <div class="feature-menu__heading">
        @if (!collapsible || !collapsed) {
          <span>{{ i18n.translate('featureMenu.menu') }}</span>
        }
        @if (collapsible) {
          <button
            type="button"
            class="feature-menu__toggle"
            data-testid="feature-menu-toggle"
            [attr.aria-expanded]="!collapsed"
            [attr.aria-label]="collapsed ? i18n.translate('featureMenu.showMenu') : i18n.translate('featureMenu.hideMenu')"
            [title]="collapsed ? i18n.translate('featureMenu.showMenu') : i18n.translate('featureMenu.hideMenu')"
            (click)="toggleCollapsed()"
          >
            <span aria-hidden="true">{{ collapsed ? '›' : '‹' }}</span>
          </button>
        }
      </div>

      @if (collapsible && collapsed) {
        <div class="feature-menu__rail" data-testid="feature-menu-rail">
          @for (item of navigationItems; track item.id) {
            <a
              class="feature-menu__item feature-menu__item--rail"
              [class.feature-menu__item--active]="isActive(item)"
              [attr.aria-current]="isActive(item) ? 'page' : null"
              [routerLink]="item.route"
              [attr.data-testid]="'nav-' + item.id"
              [title]="label(item)"
              (click)="itemSelected.emit()"
            >
              @defer (on immediate) {
                <app-feature-menu-icon [destination]="item.id" aria-hidden="true" />
              } @placeholder {
                <span class="feature-menu__icon-slot" aria-hidden="true"></span>
              }
              <span class="feature-menu__sr-only">{{ label(item) }}</span>
            </a>
          }

          @if (pinnedItems.length > 0) {
            <div class="feature-menu__rail-divider" aria-hidden="true"></div>
            @for (item of pinnedItems; track item.id) {
              <a
                class="feature-menu__item feature-menu__item--rail feature-menu__item--pinned"
                [class.feature-menu__item--active]="isActive(item)"
                [attr.aria-current]="isActive(item) ? 'page' : null"
                [routerLink]="item.route"
                [attr.data-testid]="'nav-' + item.id"
                [title]="i18n.translate('featureMenu.pinPrefix', { item: label(item) })"
                (click)="itemSelected.emit()"
              >
                @defer (on immediate) {
                  <app-feature-menu-icon [destination]="item.id" aria-hidden="true" />
                } @placeholder {
                  <span class="feature-menu__icon-slot" aria-hidden="true"></span>
                }
                <span class="feature-menu__sr-only">{{ i18n.translate('featureMenu.pinPrefix', { item: label(item) }) }}</span>
              </a>
            }
          }
        </div>
      } @else {
        <section class="feature-menu__section" data-testid="primary-navigation-section" [attr.aria-label]="navigationAriaLabel">
          <h2 class="feature-menu__section-title">{{ i18n.translate('featureMenu.main') }}</h2>
          <div class="feature-menu__items">
            @for (item of navigationItems; track item.id) {
              <a
                class="feature-menu__item"
                [class.feature-menu__item--active]="isActive(item)"
                [attr.aria-current]="isActive(item) ? 'page' : null"
                [routerLink]="item.route"
                [attr.data-testid]="'nav-' + item.id"
                (click)="itemSelected.emit()"
              >
                @defer (on immediate) {
                  <app-feature-menu-icon [destination]="item.id" aria-hidden="true" />
                } @placeholder {
                  <span class="feature-menu__icon-slot" aria-hidden="true"></span>
                }
                <span>{{ label(item) }}</span>
              </a>
            }
          </div>
        </section>

        <section class="feature-menu__section feature-menu__section--pinned" data-testid="pinned-navigation-section" [attr.aria-label]="i18n.translate('featureMenu.pinnedNavigation')">
          <div class="feature-menu__section-heading">
            <h2 class="feature-menu__section-title">{{ i18n.translate('featureMenu.pinned') }}</h2>
            @if (pinnedItems.length > 0) {
              <button
                type="button"
                class="feature-menu__section-toggle"
                data-testid="pinned-section-toggle"
                [attr.aria-expanded]="!pinnedCollapsed"
                [attr.aria-label]="pinnedCollapsed ? i18n.translate('featureMenu.showPinned') : i18n.translate('featureMenu.hidePinned')"
                (click)="pinnedCollapsedChange.emit(!pinnedCollapsed)"
              >
                {{ pinnedCollapsed ? i18n.translate('featureMenu.showPinned') : i18n.translate('featureMenu.hidePinned') }}
              </button>
            }
          </div>

          @if (!pinnedCollapsed) {
            @if (pinnedItems.length === 0) {
              <p class="feature-menu__empty">{{ i18n.translate('featureMenu.noPinned') }}</p>
            } @else {
              <div class="feature-menu__items feature-menu__items--pinned">
                @for (item of pinnedItems; track item.id; let index = $index) {
                  <div class="feature-menu__pinned-row">
                    <a
                      class="feature-menu__item feature-menu__item--pinned"
                      [class.feature-menu__item--active]="isActive(item)"
                      [attr.aria-current]="isActive(item) ? 'page' : null"
                      [routerLink]="item.route"
                      [attr.data-testid]="'nav-' + item.id"
                      (click)="itemSelected.emit()"
                    >
                      @defer (on immediate) {
                        <app-feature-menu-icon [destination]="item.id" aria-hidden="true" />
                      } @placeholder {
                        <span class="feature-menu__icon-slot" aria-hidden="true"></span>
                      }
                      <span>{{ label(item) }}</span>
                    </a>
                    @if (pinnedItems.length > 1) {
                      <div class="feature-menu__pinned-actions">
                        <button
                          type="button"
                          class="feature-menu__move"
                          [attr.data-testid]="'pinned-move-up-' + item.id"
                          [attr.aria-label]="i18n.translate('featureMenu.moveUp', { item: label(item) })"
                          [disabled]="index === 0"
                          (click)="requestPinnedMove(item.id, 'up')"
                        >
                          <span aria-hidden="true">↑</span>
                        </button>
                        <button
                          type="button"
                          class="feature-menu__move"
                          [attr.data-testid]="'pinned-move-down-' + item.id"
                          [attr.aria-label]="i18n.translate('featureMenu.moveDown', { item: label(item) })"
                          [disabled]="index === pinnedItems.length - 1"
                          (click)="requestPinnedMove(item.id, 'down')"
                        >
                          <span aria-hidden="true">↓</span>
                        </button>
                      </div>
                    }
                  </div>
                }
              </div>
            }
          }
        </section>
      }
    </nav>
  `,
  styleUrl: './feature-menu.component.scss',
})
export class FeatureMenuComponent {
  readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly currentUrl = toSignal(this.router.events.pipe(
    filter((event): event is NavigationEnd => event instanceof NavigationEnd),
    map((event) => event.urlAfterRedirects),
  ), { initialValue: this.router.url });
  @Input() navigationItems: readonly NavigationItem[] = [];
  @Input() pinnedItems: readonly NavigationItem[] = [];
  @Input() pinnedCollapsed = false;
  @Input() compact = false;
  @Input() collapsible = false;
  @Input() collapsed = false;
  @Input() navigationAriaLabel = 'Primary navigation';
  @Output() itemSelected = new EventEmitter<void>();
  @Output() collapsedChange = new EventEmitter<boolean>();
  @Output() pinnedCollapsedChange = new EventEmitter<boolean>();
  @Output() pinnedMove = new EventEmitter<NavigationMoveRequest>();

  isActive(item: NavigationItem): boolean {
    const path = this.currentUrl().split(/[?#]/, 1)[0];
    const scopedArea = /^\/workspaces\/[^/]+\/(channels|files|projects)(?:\/|$)/.exec(path)?.[1];
    if (scopedArea) {
      const destination = scopedArea === 'channels' ? 'teams' : scopedArea;
      return item.id === destination;
    }
    return path === item.route || path.startsWith(`${item.route}/`);
  }

  toggleCollapsed(): void {
    if (!this.collapsible) {
      return;
    }

    this.collapsedChange.emit(!this.collapsed);
  }

  requestPinnedMove(itemId: string, direction: NavigationMoveDirection): void {
    this.pinnedMove.emit({ itemId, direction });
  }

  label(item: NavigationItem): string {
    return this.i18n.navigationLabel(item.id, item.label);
  }

  railLabel(item: NavigationItem): string {
    const label = this.label(item);
    const words = label.trim().split(/\s+/).filter(Boolean);
    if (words.length > 1) {
      return words.slice(0, 2).map((word) => word.slice(0, 1)).join('').toUpperCase();
    }

    return label.slice(0, 2).toUpperCase();
  }
}
