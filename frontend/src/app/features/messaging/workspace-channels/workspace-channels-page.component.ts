import { A11yModule } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LucideHash, LucideLock, LucidePlus, LucideSearch } from '@lucide/angular';
import { I18nService } from '../../../core/i18n/i18n.service';
import { WorkspaceChannelInviteComponent } from './workspace-channel-invite.component';
import { WorkspaceChannelsFacade } from './workspace-channels.facade';
import { WORKSPACE_CHANNELS_EN, WORKSPACE_CHANNELS_JA } from './workspace-channels.messages';

@Component({
  selector: 'app-workspace-channels-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [A11yModule, FormsModule, RouterLink, LucideHash, LucideLock, LucidePlus, LucideSearch,
    WorkspaceChannelInviteComponent],
  providers: [WorkspaceChannelsFacade],
  templateUrl: './workspace-channels-page.component.html',
  styleUrl: './workspace-channels-page.component.scss'
})
export class WorkspaceChannelsPageComponent {
  readonly facade = inject(WorkspaceChannelsFacade);
  readonly i18n = inject(I18nService);
  readonly labels = computed(() => this.i18n.locale() === 'ja' ? WORKSPACE_CHANNELS_JA : WORKSPACE_CHANNELS_EN);
  private composing = false;

  setComposing(value: boolean): void { this.composing = value; }

  guardEnter(event: KeyboardEvent): void {
    if (event.isComposing || this.composing || event.keyCode === 229) event.preventDefault();
  }

  submitCreate(): void {
    if (!this.composing) this.facade.create();
  }

  submitSearch(): void {
    if (!this.composing) void this.facade.refresh();
  }

  closeCreate(event?: KeyboardEvent): void {
    if (!this.composing && !event?.isComposing && event?.keyCode !== 229) this.facade.closeCreate();
  }
}
