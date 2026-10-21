import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18nService, TranslationKey } from '../../core/i18n/i18n.service';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';
import { AppShellFacade } from '../../layout/app-shell/app-shell.facade';
import { FeatureMenuIconComponent } from '../../layout/feature-menu/feature-menu-icon.component';
import { WorkspacesFacade } from '../workspaces/workspaces.facade';
import { WorkspaceCardViewModel, WorkspaceDashboardViewModel } from '../workspaces/workspaces.types';

@Component({
  selector: 'app-home-page',
  standalone: true,
  imports: [RouterLink, FeatureMenuIconComponent],
  templateUrl: './home-page.component.html',
  styleUrl: './home-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePageComponent {
  readonly i18n = inject(I18nService);
  private readonly workspaces = inject(WorkspacesFacade);
  private readonly workspaceSelection = inject(WorkspaceSelectionFacade);
  private readonly shell = inject(AppShellFacade);

  readonly dashboard = computed<WorkspaceDashboardViewModel>(() => {
    const dashboard = this.workspaces.dashboard();
    return this.shell.viewModel().session.isAuthenticated ? dashboard
      : { ...dashboard, status: 'permissionDenied', workspaces: [], pageCapabilities: [] };
  });
  readonly selection = this.workspaceSelection.selection;
  readonly selectionPending = signal(false);
  readonly selectionFailed = signal(false);
  readonly destinations = computed(() => (this.shell.viewModel().session.isAuthenticated
    ? this.shell.viewModel().navigationItems : []).filter((item) =>
    ['workspaces', 'teams', 'dm', 'files'].includes(item.id)).map((item) => ({
      ...item, descriptionKey: `home.destination.${item.id}` as TranslationKey,
    })));
  readonly selectedWorkspace = computed(() => {
    const selection = this.selection();
    return this.dashboard().status === 'ready' && selection.status === 'selected' &&
      this.shell.viewModel().workspace?.id === selection.workspaceId
      ? this.dashboard().workspaces.find((workspace) => workspace.id === selection.workspaceId) ?? null
      : null;
  });

  roleLabel(workspace: WorkspaceCardViewModel): string {
    if (workspace.accessSource === 'SystemAdmin') {
      return this.i18n.translate('workspace.systemAdminAccess');
    }
    return workspace.currentUserRole
      ? this.i18n.translate(`workspace.role.${workspace.currentUserRole}`)
      : this.i18n.translate('workspace.roleUnavailable');
  }

  unreadLabel(workspace: WorkspaceCardViewModel): string {
    return workspace.availability.unreadConversations && workspace.unreadConversationCount !== null
      ? this.i18n.formatNumber(workspace.unreadConversationCount)
      : this.i18n.translate('workspace.unavailable');
  }

  async selectWorkspace(workspace: WorkspaceCardViewModel): Promise<void> {
    if (this.selectionPending() || this.dashboard().status !== 'ready' ||
      !workspace.capabilities.includes('openWorkspace')) { return; }
    this.selectionPending.set(true);
    this.selectionFailed.set(false);
    try {
      this.selectionFailed.set(!await this.workspaceSelection.selectWorkspace(workspace.id));
    } catch {
      this.selectionFailed.set(true);
    } finally {
      this.selectionPending.set(false);
    }
  }

  refresh(): void {
    if (this.shell.viewModel().session.isAuthenticated && this.dashboard().status !== 'loading') {
      void this.workspaces.loadWorkspaces();
    }
  }
}
