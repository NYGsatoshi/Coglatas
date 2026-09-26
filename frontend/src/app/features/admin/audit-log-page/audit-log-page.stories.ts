import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';

import { COGLATAS_ADMIN_AUDIT_MOCK } from '../admin.facade';
import { AUDIT_LOG_SCENARIOS } from '../admin.mock';
import { AuditLogPageComponent } from './audit-log-page.component';

const meta: Meta<AuditLogPageComponent> = {
  title: 'Features/Admin/AuditLogPage',
  component: AuditLogPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_ADMIN_AUDIT_MOCK, useValue: AUDIT_LOG_SCENARIOS.default }]
    })
  ]
};

export default meta;

type Story = StoryObj<AuditLogPageComponent>;

const withScenario = (scenario: keyof typeof AUDIT_LOG_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_ADMIN_AUDIT_MOCK, useValue: AUDIT_LOG_SCENARIOS[scenario] }]
    })
  ]
});

export const AuditDefault: Story = {};

export const AuditLoading: Story = withScenario('loading');

export const AuditEmpty: Story = withScenario('empty');

export const AuditPermissionDenied: Story = withScenario('permissionDenied');

export const AuditManyRowsBoundedPage: Story = withScenario('manyRowsBoundedPage');

export const AuditLongMessage: Story = withScenario('longMessage');

export const AuditRedactedDetailDrawer: Story = withScenario('redactedDetailDrawer');

export const Mobile: Story = {
  ...withScenario('default'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};
