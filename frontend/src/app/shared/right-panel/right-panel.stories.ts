import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';

import type { WorkspaceSummary } from '../../core/workspace/active-workspace.facade';
import { RightPanelComponent } from './right-panel/right-panel.component';
import { COGLATAS_RIGHT_PANEL_MOCK } from './right-panel.facade';
import {
  DEFAULT_RIGHT_PANEL_SCOPE,
  OTHER_RIGHT_PANEL_SCOPE,
  RIGHT_PANEL_MEMBERS,
  RIGHT_PANEL_NOTIFICATIONS
} from './right-panel.mock';
import { RightPanelNotification } from './right-panel.types';

const unsupportedNotification = RIGHT_PANEL_NOTIFICATIONS.find(
  (notification) => notification.id === 'notification-unsupported'
) as RightPanelNotification;

const STORY_ACTIVE_WORKSPACE: WorkspaceSummary = {
  id: 'fictional-workspace-1',
  label: 'Sample Workspace Alpha',
  description: 'Storybook workspace mock'
};

const longNotification: RightPanelNotification = {
  ...unsupportedNotification,
  id: 'notification-long-text',
  title:
    '非常に長い通知タイトルです。右パネルの限られた幅でも内容が崩れないように八十文字で安全に切り詰めます。',
  body:
    '非常に長い通知本文です。通知本文はHTMLではなく文字列として扱い、右パネルの限られた幅でも表示が崩れないように百六十文字で安全に切り詰めます。本文が長くてもスコープ外の情報や未対応ターゲットへのリンクは表示しません。'
};

const meta: Meta<RightPanelComponent> = {
  title: 'Shared/RightPanel',
  component: RightPanelComponent,
  args: {
    workspace: STORY_ACTIVE_WORKSPACE,
    activeScope: DEFAULT_RIGHT_PANEL_SCOPE,
    mode: 'expanded',
    selectedTab: 'notifications',
    permission: 'granted'
  }
};

export default meta;

type Story = StoryObj<RightPanelComponent>;

export const Default: Story = {};

export const RightPanelCollapsed: Story = {
  args: {
    mode: 'collapsed'
  }
};

export const RightPanelExpanded: Story = {
  args: {
    mode: 'expanded'
  }
};

export const NotificationsDefault: Story = {
  args: {
    selectedTab: 'notifications'
  }
};

export const NotificationsUnsupportedTarget: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: COGLATAS_RIGHT_PANEL_MOCK,
          useValue: {
            notifications: [unsupportedNotification],
            members: RIGHT_PANEL_MEMBERS,
            activeScope: DEFAULT_RIGHT_PANEL_SCOPE,
            mode: 'expanded',
            selectedTab: 'notifications'
          }
        }
      ]
    })
  ],
  args: {
    selectedTab: 'notifications'
  }
};

export const NotificationsLongText: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: COGLATAS_RIGHT_PANEL_MOCK,
          useValue: {
            notifications: [longNotification],
            members: RIGHT_PANEL_MEMBERS,
            activeScope: DEFAULT_RIGHT_PANEL_SCOPE,
            mode: 'expanded',
            selectedTab: 'notifications'
          }
        }
      ]
    })
  ]
};

export const MembersDefault: Story = {
  args: {
    selectedTab: 'members'
  }
};

export const MembersDifferentScope: Story = {
  args: {
    selectedTab: 'members',
    activeScope: OTHER_RIGHT_PANEL_SCOPE
  }
};

export const NoMembers: Story = {
  args: {
    selectedTab: 'members',
    activeScope: {
      workspaceId: 'fictional-workspace-empty',
      projectId: 'fictional-project-empty',
      conversationId: 'fictional-conversation-empty'
    }
  }
};

export const PermissionDenied: Story = {
  args: {
    selectedTab: 'members',
    permission: 'denied'
  }
};

export const MobileDrawer: Story = {
  parameters: {
    viewport: {
      defaultViewport: 'mobile1'
    }
  },
  args: {
    mode: 'drawer'
  }
};
