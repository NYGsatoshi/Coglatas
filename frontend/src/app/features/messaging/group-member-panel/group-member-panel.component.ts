import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Injectable, Input, Output, computed, inject, signal } from '@angular/core';
import { LucideLogOut, LucideUserMinus, LucideUsers } from '@lucide/angular';
import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import { MessagingConversationViewModel, MessagingGroupActionState, MessagingGroupMember, MessagingTextKey } from '../messaging.types';
import { WorkspaceRecipientOption, WorkspaceRecipientPickerComponent } from '../workspace-recipient-picker/workspace-recipient-picker.component';

type GroupRosterFocusTarget = 'summary' | 'list' | 'leave';
type GroupRosterFocusScope = {
  readonly tenantId: string;
  readonly userId: string;
  readonly workspaceId: string;
  readonly conversationId: string;
};

// Stores only short-lived keyboard focus intent; no participant data or old authorization.
@Injectable()
export class GroupRosterFocusIntent {
  private pending: { readonly scope: GroupRosterFocusScope; readonly target: GroupRosterFocusTarget; readonly recordedAt: number } | null = null;

  clear(): void { this.pending = null; }

  remember(scope: GroupRosterFocusScope, target: GroupRosterFocusTarget): void {
    this.pending = { scope, target, recordedAt: Date.now() };
  }

  claim(scope: GroupRosterFocusScope): GroupRosterFocusTarget | null {
    const pending = this.pending;
    this.pending = null;
    if (!pending) return null;
    const elapsed = Date.now() - pending.recordedAt;
    if (elapsed < 0 || elapsed > 2000) return null;
    const prior = pending.scope;
    return prior.tenantId === scope.tenantId && prior.userId === scope.userId &&
      prior.workspaceId === scope.workspaceId && prior.conversationId === scope.conversationId
      ? pending.target : null;
  }
}

@Component({
  selector: 'app-group-member-panel',
  standalone: true,
  imports: [CoglatasDialogComponent, WorkspaceRecipientPickerComponent, LucideLogOut, LucideUserMinus, LucideUsers],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <details open class="group-members" data-testid="group-members-panel"
      (focusin)="rememberRosterFocus($event)" (focusout)="forgetRosterFocus($event)">
      <summary><svg lucideUsers aria-hidden="true"></svg>{{ i18n.translate('messaging.participants.members') }} ({{ activeMemberCount }})</summary>
      <div class="group-members__body">
        <p class="group-members__help">{{ i18n.translate('messaging.group.history') }}</p>
        <ul class="group-members__list" tabindex="0" [attr.aria-label]="i18n.translate('messaging.participants.members')" (keydown)="scrollRosterBoundary($event)">
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
export class GroupMemberPanelComponent implements AfterViewInit {
  readonly i18n = injectMessagingI18n();
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly focusIntent = inject(GroupRosterFocusIntent);
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


  // Only an already authorized live Group view can record or consume this intent.
  private authorizedFocusScope(): GroupRosterFocusScope | null {
    const conversation = this.conversationState();
    if (!conversation || conversation.kind !== 'dm' || !conversation.isGroup ||
        !conversation.viewerIsParticipant || conversation.viewerWasRemoved ||
        !conversation.capabilities.includes('readBody') || !conversation.tenantId ||
        !conversation.workspaceId || !conversation.id) return null;
    const current = conversation.groupMembers?.find(member => member.isCurrentUser && member.membership === 'active');
    if (!current) return null;
    return { tenantId: conversation.tenantId, userId: current.userId,
      workspaceId: conversation.workspaceId, conversationId: conversation.id };
  }

  // Capture while the node is still mounted: Angular structural removal can
  // detach its active element *before* OnDestroy would run.
  rememberRosterFocus(event: FocusEvent): void {
    const scope = this.authorizedFocusScope();
    const focused = event.target;
    if (!scope || !(focused instanceof Element)) { this.focusIntent.clear(); return; }
    const target: GroupRosterFocusTarget | null = focused.matches('summary') ? 'summary'
      : focused.matches('.group-members__list') ? 'list'
      : focused.matches('[data-testid="group-leave"]') ? 'leave' : null;
    if (target) this.focusIntent.remember(scope, target);
    else this.focusIntent.clear();
  }

  forgetRosterFocus(event: FocusEvent): void {
    const next = event.relatedTarget;
    // A real focus move away invalidates the intent. Detachment often blurs to
    // null or document.body; retain that anonymous intent until reauthorization.
    if (next instanceof Element && next !== document.body &&
        !this.host.nativeElement.contains(next)) this.focusIntent.clear();
  }

  ngAfterViewInit(): void {
    queueMicrotask(() => {
      // A component destroyed within the same turn must not consume a newer panel's intent.
      if (typeof document === 'undefined' || !this.host.nativeElement.isConnected) return;
      const scope = this.authorizedFocusScope();
      if (!scope) { this.focusIntent.clear(); return; }
      const target = this.focusIntent.claim(scope);
      if (!target || document.activeElement !== document.body ||
          document.visibilityState !== 'visible') return;
      const panel = this.host.nativeElement.querySelector('[data-testid="group-members-panel"]');
      const selector = target === 'summary' ? 'summary'
        : target === 'list' ? '.group-members__list' : '[data-testid="group-leave"]';
      // Restore only onto a freshly authorized, now connected control.
      (panel?.querySelector(selector) as HTMLElement | null)?.focus();
    });
  }

  scrollRosterBoundary(event: KeyboardEvent): void {
    if (event.target !== event.currentTarget || event.defaultPrevented || event.isComposing || event.keyCode === 229
      || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || (event.key !== 'Home' && event.key !== 'End')) { return; }
    const list = event.currentTarget as HTMLElement;
    // Cancel native animated boundary scrolling so a rapid reverse key is not
    // discarded while the previous End/Home animation is still settling.
    event.preventDefault();
    list.scrollTo({ top: event.key === 'Home' ? 0 : list.scrollHeight - list.clientHeight, behavior: 'instant' });
  }

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
