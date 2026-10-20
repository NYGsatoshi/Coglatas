import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { LucideLogOut, LucideUserMinus, LucideUsers } from '@lucide/angular';
import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import { MessagingConversationViewModel, MessagingGroupActionState, MessagingGroupMember, MessagingTextKey } from '../messaging.types';
import { WorkspaceRecipientOption, WorkspaceRecipientPickerComponent } from '../workspace-recipient-picker/workspace-recipient-picker.component';

@Component({
  selector: 'app-group-member-panel',
  standalone: true,
  imports: [CoglatasDialogComponent, WorkspaceRecipientPickerComponent, LucideLogOut, LucideUserMinus, LucideUsers],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <details open class="group-members" data-testid="group-members-panel">
      <summary><svg lucideUsers aria-hidden="true"></svg>{{ i18n.translate('messaging.participants.members') }} ({{ activeMemberCount }})</summary>
      <div class="group-members__body">
        <p class="group-members__help">{{ i18n.translate('messaging.group.history') }}</p>
        <ul class="group-members__list" tabindex="0" [attr.aria-label]="i18n.translate('messaging.participants.members')">
          @for (member of conversation.groupMembers ?? []; track member.userId) {
            <li data-testid="group-member-row">
              <div><strong>{{ member.displayName }}</strong>
                @if (member.isCurrentUser) { <span>{{ i18n.translate('messaging.participants.you') }}</span> }
                <small>{{ i18n.translate(member.membership === 'removed' ? 'messaging.group.removed' : member.membership === 'left' ? 'messaging.group.left' : 'messaging.group.active') }}
                  @if (roleKey(member); as key) { · {{ i18n.translate(key) }} }
                </small>
              </div>
              @if (canManage && member.membership === 'active') {
                <button type="button" [disabled]="!!action.pending" data-testid="group-remove-member"
                  [attr.aria-label]="i18n.translate('messaging.group.removeName', { name: member.displayName })" (click)="confirmMember.set(member)">
                  <svg lucideUserMinus aria-hidden="true"></svg>
                </button>
              }
            </li>
          }
        </ul>
        @if (action.errorKey) { <p role="alert">{{ i18n.translate(action.errorKey) }}</p> }
        @if (action.pending) { <p role="status">{{ i18n.translate('common.working') }}</p> }
        @if (canManage) {
          <button type="button" [disabled]="!!action.pending" [attr.aria-expanded]="adding()" (click)="toggleAdding()" data-testid="group-add-member-open">
            {{ i18n.translate('messaging.group.addMember') }}
          </button>
          @if (adding()) {
            <app-workspace-recipient-picker [workspaceId]="conversation.workspaceId ?? ''" [excludedUserIds]="activeMemberIds"
              [multiple]="false" [disabled]="!!action.pending" (recipientsChanged)="selected.set($event)" />
            <button type="button" [disabled]="selected().length !== 1 || !!action.pending" (click)="addSelected()" data-testid="group-add-member-submit">
              {{ i18n.translate('messaging.group.addMember') }}
            </button>
          }
        }
        @if (conversation.capabilities.includes('leaveGroup')) {
          <button type="button" [disabled]="!!action.pending" (click)="confirmLeave.set(true)" data-testid="group-leave">
            <svg lucideLogOut aria-hidden="true"></svg>{{ i18n.translate('messaging.group.leave') }}
          </button>
        }
      </div>
    </details>
    <app-coglatas-dialog [open]="confirmMember() !== null || confirmLeave()" [busy]="!!action.pending" [destructive]="true"
      [title]="i18n.translate(confirmLeave() ? 'messaging.group.leave' : 'messaging.group.removeMember')"
      [confirmLabel]="i18n.translate(confirmLeave() ? 'messaging.group.leave' : 'messaging.group.removeMember')"
      (cancel)="cancelConfirmation()" (confirm)="confirmMutation()">
      <p>{{ confirmMember()?.displayName ?? conversation.title }}</p>
      <p>{{ i18n.translate('messaging.group.withdrawalHelp') }}</p>
      <p>{{ i18n.translate('messaging.group.managerHelp') }}</p>
    </app-coglatas-dialog>
  `,
  styleUrl: './group-member-panel.component.scss'
})
export class GroupMemberPanelComponent {
  readonly i18n = injectMessagingI18n();
  private readonly conversationState = signal<MessagingConversationViewModel | null>(null);
  @Input({ required: true }) set conversation(value: MessagingConversationViewModel) { this.conversationState.set(value); }
  get conversation(): MessagingConversationViewModel { return this.conversationState()!; }
  @Input() action: MessagingGroupActionState = { pending: null };
  @Output() memberAdded = new EventEmitter<string>();
  @Output() memberRemoved = new EventEmitter<string>();
  @Output() left = new EventEmitter<void>();
  readonly adding = signal(false);
  readonly selected = signal<readonly WorkspaceRecipientOption[]>([]);
  readonly confirmMember = signal<MessagingGroupMember | null>(null);
  readonly confirmLeave = signal(false);
  get canManage(): boolean { return this.conversation.capabilities.includes('manageGroupMembers'); }
  private readonly activeIds = computed(() => (this.conversationState()?.groupMembers ?? []).filter((member) => member.membership === 'active').map((member) => member.userId));
  get activeMemberIds(): readonly string[] { return this.activeIds(); }
  get activeMemberCount(): number { return this.activeMemberIds.length; }

  roleKey(member: MessagingGroupMember): MessagingTextKey | null {
    return member.role === 'admin' ? 'messaging.participants.admin' : member.role === 'member' ? 'messaging.participants.member'
      : member.role === 'readOnly' ? 'messaging.participants.readOnly' : null;
  }

  toggleAdding(): void {
    if (!this.action.pending) { this.selected.set([]); this.adding.update((open) => !open); }
  }

  addSelected(): void {
    if (this.canManage && !this.action.pending && this.selected().length === 1) { this.memberAdded.emit(this.selected()[0].userId); }
  }

  cancelConfirmation(): void {
    if (!this.action.pending) { this.confirmMember.set(null); this.confirmLeave.set(false); }
  }

  confirmMutation(): void {
    if (this.action.pending) { return; }
    if (this.confirmLeave() && this.conversation.capabilities.includes('leaveGroup')) { this.left.emit(); }
    else if (this.confirmMember() && this.canManage && this.activeMemberIds.includes(this.confirmMember()!.userId)) {
      this.memberRemoved.emit(this.confirmMember()!.userId);
    }
  }
}
