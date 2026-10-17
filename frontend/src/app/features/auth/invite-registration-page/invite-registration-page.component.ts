import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { take } from 'rxjs';

import { InviteRegistrationFacade } from '../invite-registration.facade';
import { InviteRegistrationFormSubmit, InviteRegistrationViewModel } from '../invite-registration.types';
import { InviteRegistrationFormComponent } from '../invite-registration-form/invite-registration-form.component';
import { InviteTokenStatePanelComponent } from '../invite-token-state-panel/invite-token-state-panel.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-invite-registration-page',
  standalone: true,
  imports: [InviteRegistrationFormComponent, InviteTokenStatePanelComponent],
  templateUrl: './invite-registration-page.component.html',
  styleUrl: './invite-registration-page.component.scss',
})
export class InviteRegistrationPageComponent {
  readonly i18n = inject(I18nService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly facade = inject(InviteRegistrationFacade);
  private readonly inviteToken = this.route.snapshot.queryParamMap.get('token');
  private readonly state = signal<InviteRegistrationViewModel>(initialState(this.inviteToken));

  readonly viewState = this.state.asReadonly();
  readonly canShowForm = computed(() => ['valid', 'registrationFailure'].includes(this.state().status));

  constructor() {
    this.facade
      .validateToken(this.inviteToken)
      .pipe(take(1))
      .subscribe((state) => this.state.set(state));
  }

  submitRegistration(model: InviteRegistrationFormSubmit): void {
    const currentState = this.state();
    if (!this.inviteToken || currentState.submitDisabled || !currentState.email) {
      return;
    }

    this.state.set({ ...currentState, submitDisabled: true });
    this.facade
      .register({
        token: this.inviteToken,
        email: currentState.email,
        displayName: model.displayName,
        password: model.password
      })
      .pipe(take(1))
      .subscribe((state) => {
        this.state.set(state);
        if (state.status === 'registrationSuccessAutoSession') {
          void this.router.navigateByUrl(state.targetWorkspacePath ?? '/workspaces');
        }
      });
  }
}

function initialState(token: string | null): InviteRegistrationViewModel {
  return token
    ? {
        status: 'validating',
        email: null,
        message: null,
        submitDisabled: true,
        bootstrapActions: []
      }
    : {
        status: 'missing',
        email: null,
        message: 'This invite link is incomplete. Ask for a new invite URL.',
        messageKey: 'invite.missing',
        submitDisabled: true,
        bootstrapActions: []
      };
}
