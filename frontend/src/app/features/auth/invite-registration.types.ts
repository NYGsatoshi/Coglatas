import type { TranslationKey } from '../../core/i18n/i18n.service';

export type InviteTokenStatus =
  | 'missing'
  | 'validating'
  | 'valid'
  | 'invalid'
  | 'expired'
  | 'revoked'
  | 'alreadyAccepted'
  | 'backendTransactionGated'
  | 'registrationSuccessAutoSession'
  | 'registrationSuccessLoginRequired'
  | 'registrationFailure';

export interface InviteRegistrationViewModel {
  readonly status: InviteTokenStatus;
  readonly email: string | null;
  readonly role?: string | null;
  readonly tenantName?: string | null;
  readonly workspaceName?: string | null;
  readonly expiresAt?: string | null;
  readonly requestId?: string | null;
  readonly targetWorkspacePath?: string | null;
  readonly message?: string | null;
  readonly messageKey?: TranslationKey;
  readonly submitDisabled: boolean;
  readonly bootstrapActions: readonly InviteBootstrapAction[];
}

export interface InviteRegistrationScenario {
  readonly initialState: InviteRegistrationViewModel;
  readonly submitResult: InviteRegistrationViewModel;
}

export interface InviteRegistrationSubmitModel {
  readonly token: string;
  readonly email: string;
  readonly displayName: string;
  readonly password: string;
}

export interface InviteRegistrationFormSubmit {
  readonly displayName: string;
  readonly password: string;
}

export type InviteBootstrapAction =
  | 'clearAnonymousState'
  | 'fetchCurrentUser'
  | 'fetchCurrentTenant'
  | 'fetchNavigation'
  | 'fetchCsrfToken'
  | 'navigateTargetWorkspace'
  | 'navigateLogin';
