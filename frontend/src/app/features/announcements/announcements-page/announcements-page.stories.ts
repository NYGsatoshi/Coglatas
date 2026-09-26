import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { provideRouter } from '@angular/router';

import { COGLATAS_ANNOUNCEMENTS_PAGE_MOCK } from '../announcements.facade';
import { ANNOUNCEMENT_PAGE_SCENARIOS } from '../announcements.mock';
import { AnnouncementsPageComponent } from './announcements-page.component';

const meta: Meta<AnnouncementsPageComponent> = {
  title: 'Features/Announcements/Page',
  component: AnnouncementsPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        { provide: COGLATAS_ANNOUNCEMENTS_PAGE_MOCK, useValue: ANNOUNCEMENT_PAGE_SCENARIOS.default }
      ]
    })
  ]
};

export default meta;

type Story = StoryObj<AnnouncementsPageComponent>;

const withScenario = (scenario: keyof typeof ANNOUNCEMENT_PAGE_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        { provide: COGLATAS_ANNOUNCEMENTS_PAGE_MOCK, useValue: ANNOUNCEMENT_PAGE_SCENARIOS[scenario] }
      ]
    })
  ]
});

export const Default: Story = {};

export const Loading: Story = withScenario('loading');

export const Empty: Story = withScenario('empty');

export const Error: Story = withScenario('error');

export const PermissionDenied: Story = withScenario('permissionDenied');

export const NoCreatePermission: Story = withScenario('noCreatePermission');

export const LongBody: Story = withScenario('longBody');

export const AudienceScopePreview: Story = withScenario('audienceScopePreview');

export const AttachmentDisabled: Story = withScenario('attachmentDisabled');

export const RecordAccessDenied: Story = withScenario('recordAccessDenied');

export const Mobile: Story = {
  ...withScenario('default'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};
