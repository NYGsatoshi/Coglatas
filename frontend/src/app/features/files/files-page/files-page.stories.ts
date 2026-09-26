import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';

import { COGLATAS_FILES_PAGE_MOCK } from '../files.facade';
import { FILES_PAGE_SCENARIOS } from '../files.mock';
import { FilesPageComponent } from './files-page.component';

const meta: Meta<FilesPageComponent> = {
  title: 'Features/Files/FilesPage',
  component: FilesPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_FILES_PAGE_MOCK, useValue: FILES_PAGE_SCENARIOS.default }]
    })
  ]
};

export default meta;

type Story = StoryObj<FilesPageComponent>;

const withScenario = (scenario: keyof typeof FILES_PAGE_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_FILES_PAGE_MOCK, useValue: FILES_PAGE_SCENARIOS[scenario] }]
    })
  ]
});

export const Default: Story = {};

export const UploadPending: Story = withScenario('uploadPending');

export const UploadProgress: Story = withScenario('uploadProgress');

export const UploadFailed: Story = withScenario('uploadFailed');

export const FileTooLarge: Story = withScenario('fileTooLarge');

export const ScanPending: Story = withScenario('scanPending');

export const ScanBlocked: Story = withScenario('scanBlocked');

export const ScanAllowed: Story = withScenario('scanAllowed');

export const DownloadDenied: Story = withScenario('downloadDenied');

export const NoCanonicalFileIdYet: Story = withScenario('noCanonicalFileIdYet');

export const QuotaExceeded: Story = withScenario('quotaExceeded');

export const QuotaExceptionRequested: Story = withScenario('quotaExceptionRequested');

export const QuotaExceptionApproved: Story = withScenario('quotaExceptionApproved');

export const QuotaExceptionRejected: Story = withScenario('quotaExceptionRejected');

export const AdminOverrideRequired: Story = withScenario('adminOverrideRequired');

export const PreviewDisabled: Story = withScenario('previewDisabled');

export const Mobile: Story = {
  ...withScenario('mobile'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};
