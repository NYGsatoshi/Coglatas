import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Injectable, Input, OnDestroy, Output, computed, inject, signal } from '@angular/core';
import { LucideLogOut, LucideUserMinus, LucideUsers } from '@lucide/angular';
import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import { MessagingConversationViewModel, MessagingGroupActionState, MessagingGroupMember, MessagingTextKey } from '../messaging.types';
import { WorkspaceRecipientOption, WorkspaceRecipientPickerComponent } from '../workspace-recipient-picker/workspace-recipient-picker.component';


type RosterBoundary = 'Home' | 'End';
type RosterScope = { tenantId: string; workspaceId: string; conversationId: string; userId: string };

// Only a transient keyboard boundary is retained across an authorized remount.
// No old rows, permissions, DOM nodes, messages or scroll coordinates are held.
@Injectable()
export class GroupRosterBoundaryIntent {
  private pending: { scope: RosterScope; boundary: RosterBoundary; at: number } | null = null;

  clear(): void { this.pending = null; }

  remember(scope: RosterScope, boundary: RosterBoundary): void {
    this.pending = { scope, boundary, at: Date.now() };
  }

  claim(scope: RosterScope): RosterBoundary | null {
    const prior = this.pending;
    this.pending = null;
    if (!prior || Date.now() < prior.at || Date.now() - prior.at > 2000) return null;
    const old = prior.scope;
    return old.tenantId === scope.tenantId && old.workspaceId === scope.workspaceId &&
      old.conversationId === scope.conversationId && old.userId === scope.userId
      ? prior.boundary : null;
  }
}

type RosterFocusTarget = 'summary' | 'list' | 'leave';

// Retains an ephemeral keyboard target, never an old DOM node or security projection.
@Injectable()
export class GroupRosterFocusIntent {
  private pending: { scope: RosterScope; target: RosterFocusTarget; at: number } | null = null;
  private invalidationEpoch = 0;

  get epoch(): number { return this.invalidationEpoch; }
  clear(): void { this.pending = null; this.invalidationEpoch++; }

  remember(scope: RosterScope, target: RosterFocusTarget, capturedEpoch: number): void {
    // A hard denial/route switch can invalidate focus after capture but before
    // Angular calls ngOnDestroy. Never recreate a dismissed intent.
    if (capturedEpoch !== this.invalidationEpoch) return;
    this.pending = { scope, target, at: Date.now() };
  }

  claim(scope: RosterScope): RosterFocusTarget | null {
    const prior = this.pending;
    this.pending = null;
    if (!prior || Date.now() < prior.at || Date.now() - prior.at > 2000) return null;
    const old = prior.scope;
    return old.tenantId === scope.tenantId && old.workspaceId === scope.workspaceId &&
      old.conversationId === scope.conversationId && old.userId === scope.userId
      ? prior.target : null;
  }
}

@Component({
  selector: 'app-group-member-panel',
  standalone: true,
  imports: [CoglatasDialogComponent, WorkspaceRecipientPickerComponent, LucideLogOut, LucideUserMinus, LucideUsers],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <details open class="group-members" data-testid="group-members-panel" (focusin)="recordFocusedRosterTarget($event)" (focusout)="discardBoundaryOnExit($event)">
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
export class GroupMemberPanelComponent implements AfterViewInit, OnDestroy {
  readonly i18n = injectMessagingI18n();
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly boundaryIntent = inject(GroupRosterBoundaryIntent, { optional: true });
  private readonly focusIntent = inject(GroupRosterFocusIntent, { optional: true });
  private lastBoundary: { key: RosterBoundary; at: number } | null = null;
  private focusedRoster: { scope: RosterScope; target: RosterFocusTarget; epoch: number } | null = null;
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


  private authorizedScope(): RosterScope | null {
    const current = this.conversationState();
    if (!current || current.kind !== 'dm' || !current.isGroup || !current.viewerIsParticipant ||
        current.viewerWasRemoved || !current.capabilities.includes('readBody') ||
        !current.tenantId || !current.workspaceId || !current.id) return null;
    const actor = current.groupMembers?.find(member => member.isCurrentUser && member.membership === 'active');
    return actor ? { tenantId: current.tenantId, workspaceId: current.workspaceId,
      conversationId: current.id, userId: actor.userId } : null;
  }

  recordFocusedRosterTarget(event: FocusEvent): void {
    const scope = this.authorizedScope();
    const element = event.target;
    const epoch = this.focusIntent?.epoch;
    if (!scope || !(element instanceof Element) || epoch === undefined) {
      this.focusedRoster = null;
      this.focusIntent?.clear();
      return;
    }
    const target: RosterFocusTarget | null = element.matches('summary') ? 'summary'
      : element.matches('.group-members__list') ? 'list'
      : element.matches('[data-testid="group-leave"]') ? 'leave' : null;
    // Remember only the role while attached. Start the short TTL at teardown,
    // even when the user has kept this control focused for several seconds.
    this.focusedRoster = target ? { scope, target, epoch } : null;
  }

  discardBoundaryOnExit(event: FocusEvent): void {
    const next = event.relatedTarget;
    // Tab to the Leave button remains inside the panel. Explicit focus outside
    // the panel must not later cause a stale scroll after it is remounted.
    const details = this.host.nativeElement.querySelector('[data-testid="group-members-panel"]');
    // The confirmation dialog is hosted next to <details>. It must count as
    // an explicit exit just like a control outside this component.
    if (next instanceof Element && next !== document.body && !details?.contains(next)) {
      this.lastBoundary = null;
      this.focusedRoster = null;
      this.focusIntent?.clear();
    }
  }

  ngOnDestroy(): void {
    const scope = this.authorizedScope();
    const focused = this.focusedRoster;
    this.focusedRoster = null;
    // The role was captured before Angular detached the old element. Its
    // authorization epoch must still be current at the actual teardown.
    if (scope && focused &&
        focused.scope.tenantId === scope.tenantId &&
        focused.scope.userId === scope.userId &&
        focused.scope.workspaceId === scope.workspaceId &&
        focused.scope.conversationId === scope.conversationId) {
      this.focusIntent?.remember(scope, focused.target, focused.epoch);
    } else if (!scope) {
      this.focusIntent?.clear();
    }
    if (!this.lastBoundary || Date.now() < this.lastBoundary.at ||
        Date.now() - this.lastBoundary.at > 2000) return;
    if (scope) this.boundaryIntent?.remember(scope, this.lastBoundary.key);
  }

  ngAfterViewInit(): void {
    queueMicrotask(() => {
      if (typeof document === 'undefined' || !this.host.nativeElement.isConnected) return;
      const scope = this.authorizedScope();
      if (!scope) {
        this.boundaryIntent?.clear();
        this.focusIntent?.clear();
        return;
      }
      const boundary = this.boundaryIntent?.claim(scope);
      const focusTarget = this.focusIntent?.claim(scope);
      if (!boundary && !focusTarget) return;
      // A user-selected control outside the panel always takes precedence.
      if (document.activeElement !== document.body &&
          !this.host.nativeElement.contains(document.activeElement)) return;
      if (focusTarget && document.visibilityState === 'visible') {
        const selector = focusTarget === 'summary' ? 'summary'
          : focusTarget === 'list' ? '.group-members__list' : '[data-testid="group-leave"]';
        // Only the fresh, newly authorized control receives focus; never an old node.
        this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus({ preventScroll: true });
      }
      if (!boundary) return;
      const list = this.host.nativeElement.querySelector<HTMLElement>('.group-members__list');
      if (!list || list.scrollHeight <= list.clientHeight) return;
      list.scrollTo({ top: boundary === 'Home' ? 0 : list.scrollHeight - list.clientHeight, behavior: 'instant' });
      if (boundary === 'End') {
        // A fresh nested scrollport alone is not enough: the desktop aside may
        // also have reset during remount. Keep the final row visible through
        // the authorized enclosing scrollport without retaining old content.
        list.querySelector<HTMLElement>('li:last-child')?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
      }
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
    if (this.authorizedScope()) this.lastBoundary = { key: event.key === 'Home' ? 'Home' : 'End', at: Date.now() };
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
