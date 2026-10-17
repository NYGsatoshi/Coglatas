import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { InviteRegistrationViewModel } from '../invite-registration.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-invite-token-state-panel',
  standalone: true,
  templateUrl: './invite-token-state-panel.component.html',
  styleUrl: './invite-token-state-panel.component.scss',
})
export class InviteTokenStatePanelComponent {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) state!: InviteRegistrationViewModel;

  get isBlockingState(): boolean {
    return ['missing', 'invalid', 'expired', 'revoked', 'alreadyAccepted', 'backendTransactionGated'].includes(
      this.state.status
    );
  }
}
