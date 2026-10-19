import { I18nService } from '../../../core/i18n/i18n.service';
import { A11yModule } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Input,
  OnChanges,
  SimpleChanges,
  ViewChild,
  inject,
} from '@angular/core';

import { AuthSessionSnapshot } from '../../../core/auth/auth-session.facade';
import { WorkspaceSummary } from '../../../core/workspace/active-workspace.facade';
import { MembersTabComponent } from '../members-tab/members-tab.component';
import { NotificationsTabComponent } from '../notifications-tab/notifications-tab.component';
import { RightPanelFacade } from '../right-panel.facade';
import {
  RightPanelMode,
  RightPanelPermission,
  RightPanelScope,
  RightPanelTab,
} from '../right-panel.types';

const DEFAULT_RIGHT_PANEL_SCOPE: RightPanelScope = {
  workspaceId: '',
  projectId: '',
  conversationId: '',
};

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-right-panel',
  standalone: true,
  imports: [A11yModule, MembersTabComponent, NotificationsTabComponent],
  template: `
    @let vm = facade.viewModel();

    @if (vm.mode === 'drawer') {
      <div class="right-panel__scrim" aria-hidden="true" (click)="closePanel()"></div>
    }

    <div
      class="right-panel"
      data-testid="right-panel"
      [class.right-panel--collapsed]="vm.mode === 'collapsed'"
      [class.right-panel--expanded]="vm.mode === 'expanded'"
      [class.right-panel--drawer]="vm.mode === 'drawer'"
      [attr.role]="vm.mode === 'drawer' ? 'dialog' : 'complementary'"
      [attr.aria-modal]="vm.mode === 'drawer' ? 'true' : null"
      [attr.aria-label]="i18n.translate('panel.label')"
      tabindex="-1"
      [cdkTrapFocus]="vm.mode === 'drawer'"
      [cdkTrapFocusAutoCapture]="vm.mode === 'drawer'"
      (keydown.escape)="closeFromEscape($event)"
    >
      @if (vm.mode === 'collapsed') {
        <button
          #panelTrigger
          type="button"
          class="right-panel__rail-button"
          data-testid="right-panel-open"
          [attr.aria-label]="i18n.translate('panel.open')"
          [title]="i18n.translate('panel.open')"
          (click)="openFromTrigger(panelTrigger)"
        >
          <span aria-hidden="true">i</span>
        </button>
      } @else {
        <div class="right-panel__header">
          <div>
            <p class="right-panel__eyebrow">{{ i18n.translate('panel.currentScope') }}</p>
            <h2>{{ i18n.translate('panel.title') }}</h2>
          </div>
          <button
            type="button"
            class="right-panel__icon-button"
            data-testid="right-panel-close"
            [attr.aria-label]="i18n.translate('panel.close')"
            [title]="i18n.translate('panel.close')"
            (click)="closePanel()"
          >
            <span aria-hidden="true">x</span>
          </button>
        </div>

        <div class="right-panel__scope" [attr.aria-label]="i18n.translate('panel.scope')">
          <span>{{ vm.scope.workspaceId }}</span>
          <span>{{ vm.scope.projectId }}</span>
          <span>{{ vm.scope.conversationId }}</span>
        </div>

        @if (vm.notificationOpenInProgress) {
          <p class="right-panel__status" role="status">{{ i18n.translate('panel.opening') }}</p>
        }
        @if (vm.realtimeDegraded) {
          <div class="right-panel__status right-panel__status--degraded" role="status" data-testid="right-panel-realtime-degraded">
            <span>{{ i18n.translate('panel.degraded') }}</span>
            <button type="button" class="right-panel__status-action" (click)="refreshNotifications()">
              {{ i18n.translate('panel.refresh') }}
            </button>
          </div>
        }
        @if (vm.unavailableMessage) {
          <p class="right-panel__status right-panel__status--unavailable" role="status" data-testid="notification-open-unavailable">
            {{ i18n.translate('panel.targetUnavailable') }}
          </p>
        }

        <div class="right-panel__tabs" role="tablist" [attr.aria-label]="i18n.translate('panel.tabs')">
          <button
            type="button"
            role="tab"
            id="right-panel-tab-notifications"
            class="right-panel__tab"
            [class.right-panel__tab--active]="vm.selectedTab === 'notifications'"
            [attr.aria-selected]="vm.selectedTab === 'notifications'"
            aria-controls="right-panel-panel-notifications"
            (click)="selectTab('notifications')"
          >
            {{ i18n.translate('panel.notifications') }}
            @if (vm.unreadCount > 0) {
              <span class="right-panel__badge">{{ i18n.formatNumber(vm.unreadCount) }}</span>
            }
          </button>
          <button
            type="button"
            role="tab"
            id="right-panel-tab-members"
            class="right-panel__tab"
            [class.right-panel__tab--active]="vm.selectedTab === 'members'"
            [attr.aria-selected]="vm.selectedTab === 'members'"
            aria-controls="right-panel-panel-members"
            (click)="selectTab('members')"
          >
            {{ i18n.translate('panel.members') }}
          </button>
        </div>

        @if (vm.selectedTab === 'notifications') {
          <div
            id="right-panel-panel-notifications"
            role="tabpanel"
            aria-labelledby="right-panel-tab-notifications"
          >
            <app-notifications-tab
              [notifications]="vm.notifications"
              (targetSelected)="openNotificationTarget($event)"
              (markReadRequested)="markNotificationRead($event)"
            />
          </div>
        } @else {
          <div
            id="right-panel-panel-members"
            role="tabpanel"
            aria-labelledby="right-panel-tab-members"
          >
            <app-members-tab [members]="vm.members" [permission]="vm.permission" />
          </div>
        }
      }
    </div>
  `,
  styleUrl: './right-panel.component.scss',
})
export class RightPanelComponent implements OnChanges {
  readonly i18n = inject(I18nService);
  readonly facade = inject(RightPanelFacade);
  private lastTrigger: HTMLElement | null = null;

  @Input() session: AuthSessionSnapshot | null = null;
  @Input() workspace: WorkspaceSummary | null = null;
  @Input() mode: RightPanelMode | null = null;
  @Input() selectedTab: RightPanelTab | null = null;
  @Input() activeScope: RightPanelScope | null = null;
  @Input() permission: RightPanelPermission | null = null;
  @Input() returnFocusTo: HTMLElement | null = null;

  @ViewChild('panelTrigger') panelTrigger?: ElementRef<HTMLButtonElement>;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['mode'] && this.mode) {
      this.facade.setMode(this.mode);
    }

    if (changes['permission'] && this.permission) {
      this.facade.setPermission(this.permission);
    }

    if (changes['selectedTab'] && this.selectedTab) {
      this.facade.setSelectedTab(this.selectedTab);
    }

    if (changes['activeScope'] || changes['workspace']) {
      this.facade.setActiveScope(this.resolveScope());
    }
  }

  openFromTrigger(trigger: HTMLElement): void {
    this.lastTrigger = trigger;
    this.facade.setMode('expanded');
  }

  closePanel(): void {
    const returnTarget =
      this.returnFocusTo ?? this.lastTrigger ?? this.panelTrigger?.nativeElement ?? null;
    this.facade.closePanel();
    this.returnFocusToTrigger(returnTarget);
  }

  closeFromEscape(event: KeyboardEvent): void {
    if (this.facade.mode() !== 'drawer') {
      return;
    }

    event.preventDefault();
    this.closePanel();
  }

  selectTab(tab: RightPanelTab): void {
    this.facade.setSelectedTab(tab);
  }

  openNotificationTarget(notificationId: string): void {
    this.facade.displayNotificationTarget(notificationId);
  }

  markNotificationRead(notificationId: string): void {
    this.facade.markNotificationRead(notificationId);
  }

  refreshNotifications(): void {
    this.facade.refreshNotificationsNow();
  }

  private resolveScope(): RightPanelScope {
    if (this.activeScope) {
      return this.activeScope;
    }

    if (this.workspace) {
      return {
        ...DEFAULT_RIGHT_PANEL_SCOPE,
        workspaceId: this.workspace.id,
      };
    }

    return DEFAULT_RIGHT_PANEL_SCOPE;
  }

  private returnFocusToTrigger(target: HTMLElement | null): void {
    setTimeout(() => target?.focus());
  }
}
