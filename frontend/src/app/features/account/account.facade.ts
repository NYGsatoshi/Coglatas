import { HttpClient } from '@angular/common/http';
import { inject, Injectable, InjectionToken, signal } from '@angular/core';
import { catchError, map, Observable, of } from 'rxjs';

import {
  AccountMockScenario,
  AccountPageViewModel,
  AccountSessionViewModel,
  AccountStatus,
  AccountStatusViewModel,
  PasswordChangeResult,
  PasswordChangeSubmit,
} from './account.types';

export const COGLATAS_ACCOUNT_MOCK = new InjectionToken<AccountMockScenario>('COGLATAS_ACCOUNT_MOCK');

interface CurrentUserDto {
  readonly displayName?: unknown;
  readonly email?: unknown;
  readonly systemRole?: unknown;
  readonly status?: unknown;
}

@Injectable({
  providedIn: 'root',
})
export class AccountFacade {
  private readonly http = inject(HttpClient);
  private readonly scenario = inject(COGLATAS_ACCOUNT_MOCK, { optional: true });
  private readonly pageState = signal<AccountPageViewModel>(
    this.scenario ? this.fromScenario(this.scenario) : this.emptyPage('loading'),
  );

  constructor() {
    if (!this.scenario) {
      this.loadAccount();
    }
  }

  getPage(): AccountPageViewModel {
    return this.pageState();
  }

  changePassword(submit: PasswordChangeSubmit): Observable<PasswordChangeResult> {
    if (this.scenario) {
      return of(this.scenario.passwordChangeResult);
    }

    return this.http
      .post(
        '/api/auth/change-password',
        {
          currentPassword: submit.currentPassword,
          newPassword: submit.newPassword,
        },
        { withCredentials: true },
      )
      .pipe(
        map(() => 'success' as const),
        catchError(() => of('failure' as const)),
      );
  }

  private loadAccount(): void {
    this.http.get<CurrentUserDto>('/api/auth/me', { withCredentials: true }).subscribe({
      next: (user) => {
        const accountStatus = accountStatusFromApi(user.status);
        this.pageState.set({
          status: 'ready',
          title: 'Account',
          profile: {
            displayName: stringValue(user.displayName) ?? 'Current user',
            ownEmail: stringValue(user.email),
            accountStatus,
            accountStatusLabel: accountStatusLabel(accountStatus),
            roleSummary: stringValue(user.systemRole) ?? '',
            tenantSummary: 'Current tenant',
            workspaceSummary: 'Live API',
          },
          accountStatus: buildAccountStatus(accountStatus),
          sessions: [],
        });
      },
      error: (error: { status?: number }) => {
        this.pageState.set({
          ...this.emptyPage(
            error.status === 401 || error.status === 403 ? 'permissionDenied' : 'error',
          ),
          message:
            error.status === 401 || error.status === 403
              ? 'Authentication is required.'
              : 'Account API request failed.',
        });
      },
    });
  }

  private fromScenario(scenario: AccountMockScenario): AccountPageViewModel {
    return {
      status: scenario.status,
      title: 'Account',
      message: scenario.message,
      profile: scenario.profile,
      accountStatus: scenario.profile
        ? buildAccountStatus(scenario.profile.accountStatus)
        : undefined,
      sessions: scenario.sessions.map((session): AccountSessionViewModel => {
        return {
          id: session.id,
          deviceLabel: session.deviceLabel ?? 'Unknown device',
          createdAtLabel: session.createdAtLabel,
          lastUsedAtLabel: session.lastUsedAtLabel,
          isCurrent: session.isCurrent,
          canRevoke: session.canRevoke,
          revokeUnavailableReason: session.revokeUnavailableReason,
        };
      }),
    };
  }

  private emptyPage(status: AccountPageViewModel['status']): AccountPageViewModel {
    return {
      status,
      title: 'Account',
      sessions: [],
    };
  }
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function accountStatusFromApi(value: unknown): AccountStatus {
  const normalized = String(value ?? '').toLowerCase();
  if (normalized === '1' || normalized === 'suspended' || normalized === 'disabled') {
    return 'disabled';
  }
  if (normalized === '3' || normalized === 'archived' || normalized === 'deleted') {
    return 'deleted';
  }
  return 'active';
}

function accountStatusLabel(status: AccountStatus): string {
  if (status === 'disabled') {
    return 'Disabled';
  }
  if (status === 'deleted') {
    return 'Deleted';
  }
  return 'Active';
}

function buildAccountStatus(status: AccountStatus): AccountStatusViewModel {
  return {
    accountStatus: status,
    accountStatusLabel: accountStatusLabel(status),
    message:
      status === 'active'
        ? 'Account is active.'
        : status === 'disabled'
          ? 'Account is disabled.'
          : 'Account is deleted.',
  };
}
