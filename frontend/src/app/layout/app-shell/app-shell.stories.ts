import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { provideRouter } from '@angular/router';

import {
  COGLATAS_AUTH_SESSION_MOCK,
  DEFAULT_AUTH_SESSION
} from '../../core/auth/auth-session.facade';
import { PagePlaceholderComponent } from '../../core/routing/page-placeholder.component';
import {
  type WorkspaceSummary
} from '../../core/workspace/active-workspace.facade';
import { COGLATAS_WORKSPACES_DASHBOARD_MOCK } from '../../features/workspaces/workspaces.facade';
import {
  DEFAULT_WORKSPACE_DASHBOARD,
  MEMBER_WORKSPACE
} from '../../features/workspaces/workspaces.mock';
import { COGLATAS_APP_SHELL_MOCK } from './app-shell.facade';
import { AppShellComponent } from './app-shell.component';

const storyRoutes = [
  {
    path: '',
    component: PagePlaceholderComponent,
    data: { title: 'ワークスペース', summary: '未実装' }
  },
  {
    path: 'app/workspaces',
    component: PagePlaceholderComponent,
    data: { title: 'ワークスペース', summary: '未実装' }
  },
  {
    path: 'app/projects',
    component: PagePlaceholderComponent,
    data: { title: '制作', summary: '準備中' }
  },
  {
    path: 'app/files',
    component: PagePlaceholderComponent,
    data: { title: 'ファイル', summary: '未実装' }
  },
  {
    path: 'app/account',
    component: PagePlaceholderComponent,
    data: { title: 'アカウント', summary: '準備中' }
  },
  {
    path: 'app/admin/audit',
    component: PagePlaceholderComponent,
    data: { title: '監査', summary: '未実装' }
  }
];

const STORY_ACTIVE_WORKSPACE: WorkspaceSummary = {
  id: 'fictional-workspace-1',
  label: 'Sample Workspace Alpha',
  description: 'Storybook workspace mock'
};

const STORY_WORKSPACE_CARD = {
  ...MEMBER_WORKSPACE,
  id: STORY_ACTIVE_WORKSPACE.id,
  displayName: STORY_ACTIVE_WORKSPACE.label,
  runningProjectCount: 2,
  needsReviewProjectCount: 1,
  activeProjectCount: 3
} as const;

const STORY_WORKSPACE_DASHBOARD = {
  ...DEFAULT_WORKSPACE_DASHBOARD,
  workspaces: [STORY_WORKSPACE_CARD],
  pageCapabilities: []
} as const;

const meta: Meta<AppShellComponent> = {
  title: 'Shell/AppShell',
  component: AppShellComponent,
  decorators: [
    applicationConfig({
      providers: [
        provideRouter(storyRoutes),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK, useValue: STORY_WORKSPACE_DASHBOARD }
      ]
    })
  ],
  parameters: {
    layout: 'fullscreen'
  }
};

export default meta;

type Story = StoryObj<AppShellComponent>;

export const DefaultDesktop: Story = {};

export const RightPanelCollapsed: Story = {
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_APP_SHELL_MOCK, useValue: { rightPanelMode: 'collapsed' } }]
    })
  ]
};

export const RightPanelExpanded: Story = {
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_APP_SHELL_MOCK, useValue: { rightPanelMode: 'expanded' } }]
    })
  ]
};

export const NoWorkspaceSelected: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK,
          useValue: { ...STORY_WORKSPACE_DASHBOARD, workspaces: [] }
        }
      ]
    })
  ]
};

export const PermissionFilteredNavigation: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: COGLATAS_AUTH_SESSION_MOCK,
          useValue: {
            ...DEFAULT_AUTH_SESSION,
            capabilities: ['workspace:view', 'projects:view']
          }
        }
      ]
    })
  ]
};

export const SessionExpiredState: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: COGLATAS_AUTH_SESSION_MOCK,
          useValue: {
            ...DEFAULT_AUTH_SESSION,
            status: 'expired'
          }
        }
      ]
    })
  ]
};

export const MobileDrawerClosed: Story = {
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};

export const MobileDrawerOpen: Story = {
  play: async ({ canvasElement }) => {
    const button = canvasElement.querySelector<HTMLButtonElement>('.mobile-header__menu');
    button?.click();
  },
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};

export const TabletCompact: Story = {
  parameters: {
    viewport: { defaultViewport: 'tablet' }
  }
};

export const LongWorkspaceName: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK,
          useValue: {
            ...STORY_WORKSPACE_DASHBOARD,
            workspaces: [{
              ...STORY_WORKSPACE_CARD,
              displayName: '架空制作ワークスペース長文検証一号二号'
            }]
          }
        }
      ]
    })
  ]
};
