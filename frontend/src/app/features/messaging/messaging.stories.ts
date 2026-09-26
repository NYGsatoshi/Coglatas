import { applicationConfig, moduleMetadata, type Meta, type StoryObj } from '@storybook/angular';
import { provideRouter } from '@angular/router';

import { ChannelMessagingPageComponent } from './channel-messaging-page/channel-messaging-page.component';
import { DmPageComponent } from './dm-page/dm-page.component';
import { COGLATAS_MESSAGING_PAGE_MOCK } from './messaging.facade';
import { MESSAGING_PAGE_SCENARIOS } from './messaging.mock';

const meta: Meta<ChannelMessagingPageComponent> = {
  title: 'Features/Messaging/Pages',
  component: ChannelMessagingPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        { provide: COGLATAS_MESSAGING_PAGE_MOCK, useValue: MESSAGING_PAGE_SCENARIOS.channelDefault }
      ]
    })
  ]
};

export default meta;

type ChannelStory = StoryObj<ChannelMessagingPageComponent>;
type DmStory = StoryObj<DmPageComponent>;

const withChannelScenario = (scenario: keyof typeof MESSAGING_PAGE_SCENARIOS): ChannelStory => ({
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        { provide: COGLATAS_MESSAGING_PAGE_MOCK, useValue: MESSAGING_PAGE_SCENARIOS[scenario] }
      ]
    })
  ]
});

export const ChannelDefault: ChannelStory = withChannelScenario('channelDefault');

export const DmDefault: DmStory = {
  render: () => ({ props: {}, template: '<app-dm-page />' }),
  decorators: [
    moduleMetadata({
      imports: [DmPageComponent]
    }),
    applicationConfig({
      providers: [
        provideRouter([]),
        { provide: COGLATAS_MESSAGING_PAGE_MOCK, useValue: MESSAGING_PAGE_SCENARIOS.dmDefault }
      ]
    })
  ]
};

export const NoMessages: ChannelStory = withChannelScenario('noMessages');

export const ComposerDisabled: ChannelStory = withChannelScenario('composerDisabled');

export const RemovedParticipant: ChannelStory = withChannelScenario('removedParticipant');

export const ManualRefreshError: ChannelStory = withChannelScenario('manualRefreshError');

export const LongMessage: ChannelStory = withChannelScenario('longMessage');

export const FailedOutgoingRetry: ChannelStory = withChannelScenario('failedOutgoingRetry');

export const NoAttachmentsUntilCanonicalFileId: ChannelStory = withChannelScenario('noAttachmentsUntilCanonicalFileId');

export const NewMessagesWhileReading: ChannelStory = withChannelScenario('newMessagesWhileReading');

export const Mobile: ChannelStory = {
  ...withChannelScenario('mobile'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};
