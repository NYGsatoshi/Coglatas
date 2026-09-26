import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { provideRouter } from '@angular/router';

import { COGLATAS_WORKSPACES_DASHBOARD_MOCK } from '../workspaces.facade';
import { WORKSPACE_DASHBOARD_SCENARIOS } from '../workspaces.mock';
import { WorkspaceDashboardPageComponent } from './workspace-dashboard-page.component';

const meta: Meta<WorkspaceDashboardPageComponent> = {
  title: 'Features/Workspaces/DashboardPage',
  component: WorkspaceDashboardPageComponent,
  parameters: {
    layout: 'fullscreen',
  },
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        {
          provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK,
          useValue: WORKSPACE_DASHBOARD_SCENARIOS.default,
        },
      ],
    }),
  ],
};

export default meta;

type Story = StoryObj<WorkspaceDashboardPageComponent>;

const withScenario = (scenario: keyof typeof WORKSPACE_DASHBOARD_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        {
          provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK,
          useValue: WORKSPACE_DASHBOARD_SCENARIOS[scenario],
        },
      ],
    }),
  ],
});

export const Default: Story = {};

export const Loading: Story = withScenario('loading');

export const Empty: Story = withScenario('empty');

export const Error: Story = withScenario('error');

export const PermissionDenied: Story = withScenario('permissionDenied');

export const ManyWorkspaces: Story = withScenario('many');

export const NoWorkspaceAccess: Story = withScenario('noWorkspaceAccess');

export const SystemAdminAccess: Story = withScenario('systemAdmin');

export const ZeroCounts: Story = withScenario('zeroCounts');

export const LongWorkspaceNames: Story = withScenario('longWorkspaceNames');

export const Mobile: Story = {
  ...withScenario('default'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' },
  },
};
