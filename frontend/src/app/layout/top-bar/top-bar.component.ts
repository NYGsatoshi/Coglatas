import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AuthSessionStatus } from '../../core/auth/auth-session.facade';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkspaceSummary } from '../../core/workspace/active-workspace.facade';
import { RightPanelMode } from '../../shared/right-panel/right-panel.types';
import { WorkspaceSearchComponent } from '../workspace-search/workspace-search.component';
import { CoglatasThemeToggleComponent } from '../../shared/theme/coglatas-theme-toggle/coglatas-theme-toggle.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-top-bar',
  standalone: true,
  imports: [RouterLink, WorkspaceSearchComponent, CoglatasThemeToggleComponent],
  template: `
    <header class="top-bar" [class.top-bar--workspace-selected]="workspaceSelectionStatus === 'selected'"
      [class.top-bar--utilities-open]="mobileUtilitiesOpen"
      (keydown.escape)="closeMobileUtilities($event, utilitiesToggle)"
      data-testid="top-bar" [attr.aria-label]="i18n.translate('topBar.primaryHeader')">
      <section class="top-bar__workspace" aria-labelledby="workspace-context-label">
        <label id="workspace-context-label" class="top-bar__label" for="workspace-switcher">
          {{ i18n.translate('topBar.workspace') }}
        </label>
        <select
          id="workspace-switcher"
          data-testid="workspace-switcher"
          aria-describedby="workspace-selection-status"
          [disabled]="workspaceSelectionStatus === 'loading' || workspaceSelectionStatus === 'unavailable'"
          (change)="onWorkspaceChange($event)"
        >
          @if (!workspace || workspaceSelectionStatus === 'selectionRequired') {
            <option value="" [selected]="!workspace">{{ i18n.translate('topBar.selectWorkspace') }}</option>
          }
          @for (option of workspaceOptions; track option.id) {
            <option [value]="option.id" [selected]="option.id === workspace?.id">{{ option.label }}</option>
          }
        </select>
        <span
          id="workspace-selection-status"
          class="top-bar__selection-status"
          data-testid="workspace-selection-status"
          aria-live="polite"
        >
          @switch (workspaceSelectionStatus) {
            @case ('loading') { {{ i18n.translate('topBar.loadingWorkspaces') }} }
            @case ('selectionRequired') { {{ i18n.translate('topBar.chooseWorkspace') }} }
            @case ('unavailable') { {{ i18n.translate('topBar.workspaceUnavailable') }} }
            @default { {{ workspace?.label || i18n.translate('topBar.workspaceNotSelected') }} }
          }
        </span>
      </section>

      <button #utilitiesToggle type="button" class="top-bar__action top-bar__utilities-toggle"
        data-testid="mobile-utilities-toggle" aria-controls="workspace-utility-search workspace-utility-actions workspace-utility-status"
        [attr.aria-expanded]="mobileUtilitiesOpen" (click)="mobileUtilitiesOpen = !mobileUtilitiesOpen">
        {{ i18n.translate('topBar.utilities') }}
      </button>
      @if (sessionStatus === 'expired' || logoutError) {
        <div class="top-bar__status-area" role="alert">
          @if (sessionStatus === 'expired') {
            <span class="top-bar__status">{{ i18n.translate('topBar.sessionExpired') }}</span>
          }
          @if (logoutError) {
            <span class="top-bar__status top-bar__status--error" data-testid="logout-error">{{ logoutError }}</span>
          }
        </div>
      }

      <section id="workspace-utility-status" class="top-bar__research" [attr.aria-label]="i18n.translate('topBar.researchStatus')" aria-live="polite">
        <span class="top-bar__label">{{ i18n.translate('topBar.research') }}</span>
        <strong data-testid="workspace-research-status">
          @if (runningProjectCount !== null && needsReviewProjectCount !== null) {
            {{ i18n.formatNumber(runningProjectCount) }} {{ i18n.translate('topBar.running') }} <span aria-hidden="true">&middot;</span>
            {{ i18n.formatNumber(needsReviewProjectCount) }} {{ i18n.translate('topBar.needsReview') }}
          } @else {
            {{ i18n.translate('topBar.statusUnavailable') }}
          }
        </strong>
      </section>

      <app-workspace-search
        id="workspace-utility-search" class="top-bar__search"
        [workspaceId]="workspace?.id ?? null"
        [workspaceLabel]="workspace?.label ?? ''"
      />

      <div id="workspace-utility-actions" class="top-bar__actions">
        @if (workspace) {
          <nav class="top-bar__action-group" [attr.aria-label]="i18n.translate('topBar.workspaceActions')">
            @if (memberPreview.length > 0) {
              <div class="top-bar__avatar-stack" data-testid="workspace-member-preview" [attr.aria-label]="i18n.translate('topBar.memberPreview')">
                @for (member of memberPreview; track member.id) {
                  <span class="top-bar__avatar" [attr.title]="member.displayName" [attr.aria-label]="member.displayName">{{ initials(member.displayName) }}</span>
                }
              </div>
            }
            @if (hasExternalShares) {
              <span class="top-bar__external-badge" data-testid="workspace-external-badge">
                {{ i18n.translate('topBar.external') }}
                @if (externalShareCount !== null) { ({{ i18n.formatNumber(externalShareCount) }}) }
              </span>
            }
            @if (canOpenWorkspaceMembers) {
              <a
                class="top-bar__action"
                data-testid="workspace-members-action"
                [routerLink]="['/workspaces', workspace.id, 'members']"
              >{{ i18n.translate('topBar.members') }}</a>
            }
            @if (canInspectWorkspaceSharing) {
              <a
                class="top-bar__action top-bar__action--share"
                data-testid="workspace-sharing-action"
                [routerLink]="['/workspaces', workspace.id, 'members']"
              >{{ canManageWorkspaceSharing ? i18n.translate('topBar.manageSharing') : i18n.translate('topBar.sharingDetails') }}</a>
            }
          </nav>
        }
        <nav class="top-bar__action-group top-bar__action-group--global" [attr.aria-label]="i18n.translate('topBar.globalActions')">
          <app-coglatas-theme-toggle [inline]="true" />
          <button
            #rightPanelButton
            type="button"
            class="top-bar__action"
            data-testid="right-panel-toggle"
            aria-controls="right-panel-region"
            [attr.aria-expanded]="rightPanelMode === 'expanded' || rightPanelMode === 'drawer'"
            (click)="rightPanelToggle.emit(rightPanelButton)"
          >
            {{ rightPanelMode === 'expanded' || rightPanelMode === 'drawer' ? i18n.translate('topBar.closeNotifications') : i18n.translate('topBar.notifications') }}
          </button>
          <a class="top-bar__action" data-testid="account-action" routerLink="/account">{{ i18n.translate('topBar.account') }}</a>
          <button
            type="button"
            class="top-bar__action top-bar__action--logout"
            data-testid="logout-action"
            [disabled]="logoutPending"
            (click)="logoutRequested.emit()"
          >
            {{ logoutPending ? i18n.translate('topBar.loggingOut') : i18n.translate('topBar.logout') }}
          </button>
        </nav>
      </div>
    </header>
  `,
  styleUrl: './top-bar.component.scss',
})
export class TopBarComponent {
  readonly i18n = inject(I18nService);
  mobileUtilitiesOpen = false;
  @Input() workspace: WorkspaceSummary | null = null;
  @Input() workspaceOptions: readonly WorkspaceHeaderOption[] = [];
  @Input() workspaceSelectionStatus: WorkspaceHeaderSelectionStatus = 'unavailable';
  @Input() runningProjectCount: number | null = null;
  @Input() needsReviewProjectCount: number | null = null;
  @Input() canOpenWorkspaceMembers = false;
  @Input() hasExternalShares = false;
  @Input() externalShareCount: number | null = null;
  @Input() memberPreview: readonly { readonly id: string; readonly displayName: string }[] = [];
  @Input() canInspectWorkspaceSharing = false;
  @Input() canManageWorkspaceSharing = false;
  @Input() sessionStatus: AuthSessionStatus = 'active';
  @Input() rightPanelMode: RightPanelMode = 'collapsed';
  @Input() logoutPending = false;
  @Input() logoutError = '';
  @Output() workspaceSelected = new EventEmitter<string>();
  @Output() rightPanelToggle = new EventEmitter<HTMLElement>();
  @Output() logoutRequested = new EventEmitter<void>();

  closeMobileUtilities(event: KeyboardEvent, toggle: HTMLButtonElement): void {
    if (event.isComposing || event.keyCode === 229) {
      return;
    }

    if (this.mobileUtilitiesOpen && toggle.getClientRects().length > 0) {
      this.mobileUtilitiesOpen = false;
      toggle.focus();
    }
  }

  onWorkspaceChange(event: Event): void {
    const value = event.target instanceof HTMLSelectElement ? event.target.value : '';
    if (value && value !== this.workspace?.id) {
      this.workspaceSelected.emit(value);
    }
  }

  initials(displayName: string): string {
    return Array.from(displayName.trim()).slice(0, 2).join('').toUpperCase() || '?';
  }
}

export interface WorkspaceHeaderOption {
  readonly id: string;
  readonly label: string;
}

export type WorkspaceHeaderSelectionStatus =
  | 'loading'
  | 'selected'
  | 'selectionRequired'
  | 'unavailable';
