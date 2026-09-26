import { computed, Injectable, InjectionToken, inject } from '@angular/core';

import { AuthSessionFacade, AuthSessionSnapshot } from '../../core/auth/auth-session.facade';
import { ActiveWorkspaceFacade, WorkspaceSummary } from '../../core/workspace/active-workspace.facade';
import {
  WorkspaceSelectionFacade,
  WorkspaceSelectionStatus,
} from '../../core/workspace/workspace-selection.facade';
import { WorkspacesFacade } from '../../features/workspaces/workspaces.facade';
import {
  filterNavigationItems,
  NavigationItem,
  partitionNavigationItems
} from '../../shared/navigation/navigation.models';
import { RightPanelFacade } from '../../shared/right-panel/right-panel.facade';
import { RightPanelMode } from '../../shared/right-panel/right-panel.types';

export interface AppShellViewModel {
  readonly session: AuthSessionSnapshot;
  readonly workspace: WorkspaceSummary | null;
  readonly workspaceOptions: readonly { readonly id: string; readonly label: string }[];
  readonly workspaceSelectionStatus: WorkspaceSelectionStatus;
  readonly runningProjectCount: number | null;
  readonly needsReviewProjectCount: number | null;
  readonly canOpenWorkspaceMembers: boolean;
  readonly hasExternalShares: boolean;
  readonly externalShareCount: number | null;
  readonly memberPreview: readonly { readonly id: string; readonly displayName: string }[];
  readonly canInspectWorkspaceSharing: boolean;
  readonly canManageWorkspaceSharing: boolean;
  readonly navigationItems: readonly NavigationItem[];
  readonly primaryNavigationItems: readonly NavigationItem[];
  readonly pinnedNavigationItems: readonly NavigationItem[];
  readonly rightPanelMode: RightPanelMode;
}

export interface AppShellMockState {
  readonly navigationItems?: readonly NavigationItem[];
  readonly rightPanelMode?: RightPanelMode;
}

export const DEFAULT_NAVIGATION_ITEMS: readonly NavigationItem[] = [
  {
    id: 'workspaces',
    label: 'Workspaces',
    route: '/workspaces',
    requiredCapability: 'workspace:view'
  },
  {
    id: 'messages',
    label: 'Messages',
    route: '/messages'
  },
  {
    id: 'announcements',
    label: 'Announcements',
    route: '/announcements',
    requiredCapability: 'announcements:view'
  },
  {
    id: 'files',
    label: 'Files',
    route: '/files',
    requiredCapability: 'files:view'
  },
  {
    id: 'account',
    label: 'Account',
    route: '/account',
    requiredCapability: 'account:view'
  },
  {
    id: 'audit',
    label: 'Audit',
    route: '/admin/audit',
    requiredCapability: 'audit:view'
  },
  {
    id: 'invites',
    label: 'Invites',
    route: '/admin/invites',
    requiredCapability: 'invite:read'
  },
  {
    id: 'projects',
    label: 'Projects',
    route: '/projects',
    requiredCapability: 'projects:view',
    placement: 'pinned'
  },
  {
    id: 'my-tasks',
    label: 'My Tasks',
    route: '/tasks',
    requiredCapability: 'projects:view',
    placement: 'pinned'
  }
];

export const COGLATAS_APP_SHELL_MOCK = new InjectionToken<AppShellMockState>('COGLATAS_APP_SHELL_MOCK');

@Injectable({ providedIn: 'root' })
export class AppShellFacade {
  private readonly authSession = inject(AuthSessionFacade);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly workspaces = inject(WorkspacesFacade);
  private readonly workspaceSelection = inject(WorkspaceSelectionFacade);
  private readonly rightPanel = inject(RightPanelFacade);
  private readonly mockState = inject(COGLATAS_APP_SHELL_MOCK, { optional: true });

  readonly viewModel = computed<AppShellViewModel>(() => {
    const session = this.authSession.session();
    const allItems = this.mockState?.navigationItems ?? DEFAULT_NAVIGATION_ITEMS;
    const navigationItems = filterNavigationItems(allItems, session.capabilities);
    const sections = partitionNavigationItems(navigationItems);
    const workspace = this.activeWorkspace.activeWorkspace();
    const workspaceCards = this.workspaces.dashboard().workspaces;
    const selectedWorkspaceCard = workspace
      ? workspaceCards.find((card) => card.id === workspace.id) ?? null
      : null;

    return {
      session,
      workspace,
      workspaceOptions: workspaceCards.map((card) => ({ id: card.id, label: card.displayName })),
      workspaceSelectionStatus: this.workspaceSelection.selection().status,
      runningProjectCount: selectedWorkspaceCard?.runningProjectCount ?? null,
      needsReviewProjectCount: selectedWorkspaceCard?.needsReviewProjectCount ?? null,
      canOpenWorkspaceMembers:
        selectedWorkspaceCard?.capabilities.includes('openMembers') ?? false,
      hasExternalShares: selectedWorkspaceCard?.hasExternalShares ?? false,
      externalShareCount: selectedWorkspaceCard?.externalShareCount ?? null,
      memberPreview: selectedWorkspaceCard?.memberPreview ?? [],
      canInspectWorkspaceSharing:
        selectedWorkspaceCard?.capabilities.includes('inspectSharing') ?? false,
      canManageWorkspaceSharing:
        selectedWorkspaceCard?.capabilities.includes('manageSharing') ?? false,
      navigationItems,
      primaryNavigationItems: sections.primaryItems,
      pinnedNavigationItems: sections.pinnedItems,
      rightPanelMode: this.rightPanel.mode()
    };
  });

  setRightPanelMode(mode: RightPanelMode): void {
    this.rightPanel.setMode(mode);
  }

  toggleRightPanel(): void {
    this.rightPanel.togglePanel();
  }

  selectWorkspace(workspaceId: string): Promise<boolean> {
    return this.workspaceSelection.selectWorkspace(workspaceId);
  }
}
