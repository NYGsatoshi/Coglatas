import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';

import { COGLATAS_ACCOUNT_MOCK } from '../account.facade';
import { ACCOUNT_MOCK_SCENARIOS } from '../account.mock';
import { AccountPageComponent } from './account-page.component';

const meta: Meta<AccountPageComponent> = {
  title: 'Features/Account/AccountPage',
  component: AccountPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_ACCOUNT_MOCK, useValue: ACCOUNT_MOCK_SCENARIOS.default }]
    })
  ]
};

export default meta;

type Story = StoryObj<AccountPageComponent>;

const withScenario = (scenario: keyof typeof ACCOUNT_MOCK_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_ACCOUNT_MOCK, useValue: ACCOUNT_MOCK_SCENARIOS[scenario] }]
    })
  ]
});

const withPasswordResult = (result: 'success' | 'failure'): Story => ({
  ...withScenario(result === 'success' ? 'default' : 'passwordFailure'),
  play: async ({ canvasElement }) => {
    const current = canvasElement.querySelector<HTMLInputElement>('[data-testid="current-password"]');
    const next = canvasElement.querySelector<HTMLInputElement>('[data-testid="new-password"]');
    const confirm = canvasElement.querySelector<HTMLInputElement>('[data-testid="confirm-new-password"]');
    const submit = canvasElement.querySelector<HTMLButtonElement>('button[type="submit"]');
    if (current && next && confirm && submit) {
      current.value = 'current password';
      current.dispatchEvent(new Event('input', { bubbles: true }));
      next.value = 'new password';
      next.dispatchEvent(new Event('input', { bubbles: true }));
      confirm.value = 'new password';
      confirm.dispatchEvent(new Event('input', { bubbles: true }));
      submit.click();
    }
  }
});

export const Default: Story = {};

export const Loading: Story = withScenario('loading');

export const Error: Story = withScenario('error');

export const PermissionDenied: Story = withScenario('permissionDenied');

export const OwnEmailVisible: Story = withScenario('default');

export const NoEmailAvailable: Story = withScenario('noEmailAvailable');

export const PasswordValidationError: Story = {
  ...withScenario('default'),
  play: async ({ canvasElement }) => {
    canvasElement.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
  }
};

export const PasswordChangeSuccess: Story = withPasswordResult('success');

export const PasswordChangeFailure: Story = withPasswordResult('failure');

export const SessionsDefault: Story = withScenario('default');

export const SessionRevokeUnavailable: Story = withScenario('sessionRevokeUnavailable');

export const Mobile: Story = {
  ...withScenario('default'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};
