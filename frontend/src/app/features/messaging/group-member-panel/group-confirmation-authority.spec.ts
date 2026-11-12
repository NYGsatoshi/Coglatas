import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GroupMemberPanelComponent } from './group-member-panel.component';
import { MessagingConversationViewModel, MessagingGroupActionState } from '../messaging.types';

function groupView(): MessagingConversationViewModel {
  return {
    id: 'group-one', kind: 'dm', isGroup: true, tenantId: 'tenant-one', workspaceId: 'workspace-one',
    title: 'Synthetic Group', subtitle: '', viewerIsParticipant: true, viewerWasRemoved: false,
    capabilities: ['readBody', 'manageGroupMembers', 'leaveGroup'], attachment: { mode: 'disabled', label: '' },
    groupMembers: [
      { userId: 'manager', displayName: 'Manager', role: 'admin', membership: 'active', isCurrentUser: true },
      { userId: 'peer', displayName: 'Peer', role: 'member', membership: 'active', isCurrentUser: false }
    ]
  };
}

@Component({
  standalone: true,
  imports: [GroupMemberPanelComponent],
  template: `<app-group-member-panel [conversation]="conversation()" [action]="action()"
    (memberRemoved)="removed.push($event)" (left)="leaves = leaves + 1" />`
})
class GroupConfirmationHost {
  readonly conversation = signal(groupView());
  readonly action = signal<MessagingGroupActionState>({ pending: null });
  readonly removed: string[] = [];
  leaves = 0;
}

describe('Group confirmation uses current authority and membership', () => {
  let fixture: ComponentFixture<GroupConfirmationHost>;
  let host: GroupConfirmationHost;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [GroupConfirmationHost] }).compileComponents();
    fixture = TestBed.createComponent(GroupConfirmationHost);
    host = fixture.componentInstance;
    root = fixture.nativeElement;
    await render();
  });

  afterEach(() => fixture.destroy());

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function confirmButton(): HTMLButtonElement {
    return root.querySelector<HTMLButtonElement>('[role="dialog"] .coglatas-dialog__confirm')!;
  }

  async function openPeerRemoval(): Promise<void> {
    const row = [...root.querySelectorAll<HTMLElement>('[data-testid="group-member-row"]')]
      .find(element => element.querySelector('strong')?.textContent === 'Peer');
    expect(row).toBeDefined();
    row!.querySelector<HTMLButtonElement>('[data-testid="group-remove-member"]')!.click();
    await render();
    expect(root.querySelector('[role="dialog"]')?.textContent).toContain('Peer');
  }

  it('emits only the selected current active member after the real confirmation click', async () => {
    await openPeerRemoval();
    expect(host.removed).toEqual([]);
    confirmButton().click();
    await render();
    expect(host.removed).toEqual(['peer']);
    expect(host.leaves).toBe(0);
  });

  it('blocks confirmation while a mutation is pending and permits it only after pending clears', async () => {
    await openPeerRemoval();
    host.action.set({ pending: 'remove' });
    await render();
    expect(root.querySelector('[role="dialog"]')?.getAttribute('aria-busy')).toBe('true');
    expect(confirmButton().disabled).toBe(true);
    confirmButton().click();
    expect(host.removed).toEqual([]);
    host.action.set({ pending: null });
    await render();
    expect(confirmButton().disabled).toBe(false);
    confirmButton().click();
    expect(host.removed).toEqual(['peer']);
  });

  it('keeps a busy confirmation open, then cancels without publishing a mutation', async () => {
    await openPeerRemoval();
    host.action.set({ pending: 'remove' });
    await render();
    const cancel = root.querySelector<HTMLButtonElement>('[role="dialog"] .coglatas-dialog__close')!;
    expect(cancel.disabled).toBe(true);
    cancel.click();
    await render();
    expect(root.querySelector('[role="dialog"]')).not.toBeNull();
    host.action.set({ pending: null });
    await render();
    cancel.click();
    await render();
    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(host.removed).toEqual([]);
    expect(host.leaves).toBe(0);
  });

  it('rejects a confirmation opened before management capability was revoked', async () => {
    await openPeerRemoval();
    host.conversation.update(value => ({ ...value, capabilities: ['readBody', 'leaveGroup'] }));
    await render();
    expect(root.querySelector('[data-testid="group-remove-member"]')).toBeNull();
    confirmButton().click();
    expect(host.removed).toEqual([]);
    expect(host.leaves).toBe(0);
  });

  it.each(['left', 'removed', 'absent'] as const)(
    'rejects the old removal confirmation when its member becomes %s', async membership => {
      await openPeerRemoval();
      host.conversation.update(value => ({
        ...value,
        groupMembers: membership === 'absent'
          ? value.groupMembers!.filter(member => member.userId !== 'peer')
          : value.groupMembers!.map(member => member.userId === 'peer' ? { ...member, membership } : member)
      }));
      await render();
      confirmButton().click();
      expect(host.removed).toEqual([]);
      expect(host.leaves).toBe(0);
    }
  );

  it('requires current leave capability even when the confirmation was opened while it was allowed', async () => {
    root.querySelector<HTMLButtonElement>('[data-testid="group-leave"]')!.click();
    await render();
    host.conversation.update(value => ({ ...value, capabilities: ['readBody', 'manageGroupMembers'] }));
    await render();
    expect(root.querySelector('[data-testid="group-leave"]')).toBeNull();
    confirmButton().click();
    expect(host.leaves).toBe(0);
    expect(host.removed).toEqual([]);
    host.conversation.set(groupView());
    await render();
    confirmButton().click();
    expect(host.leaves).toBe(1);
    expect(host.removed).toEqual([]);
  });
});
