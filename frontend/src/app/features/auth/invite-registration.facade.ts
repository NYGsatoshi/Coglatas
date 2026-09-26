import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, InjectionToken, signal } from '@angular/core';
import { catchError, map, Observable, of, switchMap } from 'rxjs';

import { AuthSessionFacade, AuthSessionSnapshot } from '../../core/auth/auth-session.facade';
import {
  InviteBootstrapAction,
  InviteRegistrationScenario,
  InviteRegistrationSubmitModel,
  InviteRegistrationViewModel
} from './invite-registration.types';

export const COGLATAS_INVITE_REGISTRATION_SCENARIO = new InjectionToken<InviteRegistrationScenario>(
  'COGLATAS_INVITE_REGISTRATION_SCENARIO'
);

interface InviteValidationDto {
  readonly valid?: unknown;
  readonly email?: unknown;
  readonly role?: unknown;
  readonly tenantName?: unknown;
  readonly workspaceName?: unknown;
  readonly expiresAt?: unknown;
}

@Injectable({
  providedIn: 'root'
})
export class InviteRegistrationFacade {
  private readonly http = inject(HttpClient, { optional: true });
  private readonly authSession = inject(AuthSessionFacade, { optional: true });
  private readonly scenario = inject(COGLATAS_INVITE_REGISTRATION_SCENARIO, { optional: true });
  private readonly submittedModelState = signal<InviteRegistrationSubmitModel | null>(null);
  private readonly bootstrapActionState = signal<readonly InviteBootstrapAction[]>([]);

  readonly submittedModel = this.submittedModelState.asReadonly();
  readonly bootstrapActions = this.bootstrapActionState.asReadonly();

  validateToken(token: string | null): Observable<InviteRegistrationViewModel> {
    if (!token) {
      return of({
        status: 'missing',
        email: null,
        message: 'This invite link is incomplete. Ask for a new invite URL.',
        submitDisabled: true,
        bootstrapActions: []
      });
    }

    if (this.scenario) {
      return of(this.scenario.initialState);
    }

    if (!this.http) {
      return of({
        status: 'invalid',
        email: null,
        message: 'Invite validation is unavailable in this Angular context.',
        submitDisabled: true,
        bootstrapActions: []
      });
    }

    return this.http
      .get<InviteValidationDto>('/api/invites/validate', {
        params: { token },
        withCredentials: true
      })
      .pipe(
        map((response) => toValidState(response)),
        catchError((error: HttpErrorResponse) => of(toInvalidState(error)))
      );
  }

  register(model: InviteRegistrationSubmitModel): Observable<InviteRegistrationViewModel> {
    this.submittedModelState.set(model);

    if (this.scenario) {
      this.bootstrapActionState.set(this.scenario.submitResult.bootstrapActions);
      return of(this.scenario.submitResult);
    }

    if (!this.http || !this.authSession) {
      this.bootstrapActionState.set([]);
      return of({
        status: 'registrationFailure',
        email: model.email,
        message: 'Invite acceptance is unavailable in this Angular context.',
        submitDisabled: true,
        bootstrapActions: []
      });
    }

    return this.http
      .post(
        '/api/invites/accept',
        {
          token: model.token,
          displayName: model.displayName,
          password: model.password
        },
        { withCredentials: true }
      )
      .pipe(
        switchMap(() =>
          this.authSession.bootstrap().pipe(
            map((snapshot) => toPostAcceptState(model.email, snapshot, this.bootstrapActionState))
          )
        ),
        catchError((error: HttpErrorResponse) => {
          this.bootstrapActionState.set([]);
          return of({
            status: 'registrationFailure',
            email: model.email,
            requestId: requestIdFrom(error),
            message:
              errorMessageFrom(error) ??
              'Invite acceptance failed. Confirm the invite status and try again.',
            submitDisabled: false,
            bootstrapActions: []
          } satisfies InviteRegistrationViewModel);
        })
      );
  }
}

function toValidState(response: InviteValidationDto): InviteRegistrationViewModel {
  if (response.valid !== true || !stringValue(response.email)) {
    return {
      status: 'invalid',
      email: null,
      message: 'This invite is not valid. Ask for a new invite URL.',
      submitDisabled: true,
      bootstrapActions: []
    };
  }

  return {
    status: 'valid',
    email: stringValue(response.email),
    role: stringValue(response.role),
    tenantName: stringValue(response.tenantName),
    workspaceName: stringValue(response.workspaceName),
    expiresAt: stringValue(response.expiresAt),
    message: null,
    submitDisabled: false,
    bootstrapActions: []
  };
}

function toInvalidState(error: HttpErrorResponse): InviteRegistrationViewModel {
  const message = errorMessageFrom(error) ?? 'This invite is not valid. Ask for a new invite URL.';

  return {
    status: inviteStatusFromMessage(message),
    email: null,
    requestId: requestIdFrom(error),
    message,
    submitDisabled: true,
    bootstrapActions: []
  };
}

function toPostAcceptState(
  email: string,
  snapshot: AuthSessionSnapshot,
  bootstrapActionState: { set(value: readonly InviteBootstrapAction[]): void }
): InviteRegistrationViewModel {
  const bootstrapActions: readonly InviteBootstrapAction[] = [
    'clearAnonymousState',
    'fetchCurrentUser',
    'fetchCurrentTenant',
    'fetchNavigation',
    'fetchCsrfToken',
    'navigateTargetWorkspace'
  ];

  const hasWorkspaceAccess = (snapshot.currentUser?.workspaces.length ?? 0) > 0;
  if (snapshot.isAuthenticated && hasWorkspaceAccess) {
    bootstrapActionState.set(bootstrapActions);
    return {
      status: 'registrationSuccessAutoSession',
      email,
      message: 'Invite accepted. Redirecting to workspaces.',
      submitDisabled: true,
      targetWorkspacePath: '/workspaces',
      bootstrapActions
    };
  }

  if (snapshot.isAuthenticated) {
    bootstrapActionState.set([]);
    return {
      status: 'registrationFailure',
      email,
      message: 'Invite was accepted, but workspace access could not be verified.',
      submitDisabled: true,
      bootstrapActions: []
    };
  }

  const loginActions: readonly InviteBootstrapAction[] = ['clearAnonymousState', 'navigateLogin'];
  bootstrapActionState.set(loginActions);

  return {
    status: 'registrationSuccessLoginRequired',
    email,
    message: 'Invite accepted, but sign-in could not be confirmed. Sign in to continue.',
    submitDisabled: true,
    bootstrapActions: loginActions
  };
}

function inviteStatusFromMessage(message: string): InviteRegistrationViewModel['status'] {
  const normalized = message.toLowerCase();

  if (normalized.includes('revoked')) {
    return 'revoked';
  }

  if (normalized.includes('expired')) {
    return 'expired';
  }

  if (normalized.includes('already') || normalized.includes('used')) {
    return 'alreadyAccepted';
  }

  return 'invalid';
}

function errorMessageFrom(error: HttpErrorResponse): string | null {
  const body = error.error as { error?: unknown; detail?: unknown; title?: unknown } | null;
  return stringValue(body?.error) || stringValue(body?.detail) || stringValue(body?.title) || error.message || null;
}

function requestIdFrom(error: HttpErrorResponse): string | null {
  const body = error.error as { requestId?: unknown; traceId?: unknown } | null;
  return stringValue(body?.requestId) || stringValue(body?.traceId) || null;
}

function stringValue(value: unknown): string {
  return typeof value === 'string' ? value : '';
}
