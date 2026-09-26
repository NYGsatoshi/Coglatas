import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { of } from 'rxjs';

import { COGLATAS_WORKSPACE_MEMBERS_MOCK } from '../members.facade';
import { WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID, WORKSPACE_MEMBERS_SCENARIOS } from '../members.mock';
import { WorkspaceMembersPageComponent } from './workspace-members-page.component';

const routeStub = {
  snapshot: {
    paramMap: convertToParamMap({ workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID })
  },
  paramMap: of(convertToParamMap({ workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID })),
};

const meta: Meta<WorkspaceMembersPageComponent> = {
  title: 'Features/Workspaces/MembersPage',
  component: WorkspaceMembersPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [
        { provide: ActivatedRoute, useValue: routeStub },
        { provide: COGLATAS_WORKSPACE_MEMBERS_MOCK, useValue: WORKSPACE_MEMBERS_SCENARIOS.default }
      ]
    })
  ]
};

export default meta;

type Story = StoryObj<WorkspaceMembersPageComponent>;

const withScenario = (scenario: keyof typeof WORKSPACE_MEMBERS_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [
        { provide: ActivatedRoute, useValue: routeStub },
        { provide: COGLATAS_WORKSPACE_MEMBERS_MOCK, useValue: WORKSPACE_MEMBERS_SCENARIOS[scenario] }
      ]
    })
  ]
});

export const Default: Story = {};

export const Loading: Story = withScenario('loading');

export const Empty: Story = withScenario('empty');

export const Error: Story = withScenario('error');

export const PermissionDenied: Story = withScenario('permissionDenied');

export const ManyRowsBoundedPage: Story = withScenario('manyRowsBoundedPage');

export const LongText: Story = withScenario('longNames');

export const NoEditCapability: Story = withScenario('noRoleChangeCapability');

export const RemoveRequiresAuditReason: Story = withScenario('default');

export const Mobile: Story = {
  ...withScenario('default'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};
