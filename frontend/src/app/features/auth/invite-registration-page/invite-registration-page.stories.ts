import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';

import { COGLATAS_INVITE_REGISTRATION_SCENARIO } from '../invite-registration.facade';
import { INVITE_REGISTRATION_SCENARIOS } from '../invite-registration.mock';
import { InviteRegistrationScenario } from '../invite-registration.types';
import { InviteRegistrationPageComponent } from './invite-registration-page.component';

const mockRoute = (token: string | null) => ({
  snapshot: {
    queryParamMap: convertToParamMap(token ? { token } : {})
  }
});

const meta: Meta<InviteRegistrationPageComponent> = {
  title: 'Features/Auth/InviteRegistrationPage',
  component: InviteRegistrationPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [
        { provide: COGLATAS_INVITE_REGISTRATION_SCENARIO, useValue: INVITE_REGISTRATION_SCENARIOS.defaultValid },
        { provide: ActivatedRoute, useValue: mockRoute('storybook-token') }
      ]
    })
  ]
};

export default meta;

type Story = StoryObj<InviteRegistrationPageComponent>;

const withScenario = (scenario: InviteRegistrationScenario, token: string | null = 'storybook-token'): Story => ({
  decorators: [
    applicationConfig({
      providers: [
        { provide: COGLATAS_INVITE_REGISTRATION_SCENARIO, useValue: scenario },
        { provide: ActivatedRoute, useValue: mockRoute(token) }
      ]
    })
  ]
});

export const DefaultValid: Story = {};

export const MissingToken: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.missingToken, null);

export const Validating: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.validating);

export const InvalidToken: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.invalidToken);

export const ExpiredToken: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.expiredToken);

export const AlreadyAccepted: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.alreadyAccepted);

export const BackendTransactionGated: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.backendTransactionGated);

export const ValidationError: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.validationError);

export const RegistrationSuccessAutoSession: Story = withScenario(
  INVITE_REGISTRATION_SCENARIOS.registrationSuccessAutoSession
);

export const RegistrationSuccessLoginRequired: Story = withScenario(
  INVITE_REGISTRATION_SCENARIOS.registrationSuccessLoginRequired
);

export const ServerErrorWithRequestId: Story = withScenario(INVITE_REGISTRATION_SCENARIOS.serverErrorWithRequestId);

export const Mobile: Story = {
  ...withScenario(INVITE_REGISTRATION_SCENARIOS.defaultValid),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};

