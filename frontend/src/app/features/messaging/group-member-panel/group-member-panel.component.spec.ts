import { TestBed } from '@angular/core/testing';
import { GroupMemberPanelComponent, GroupRosterFocusIntent } from './group-member-panel.component';
import { type MessagingConversationViewModel } from '../messaging.types';

describe('Group roster boundary keyboard scope', () => {
  it('handles only direct boundary keys, preserving child controls, modifiers and IME', async () => {
    await TestBed.configureTestingModule({ imports: [GroupMemberPanelComponent], providers: [GroupRosterFocusIntent] }).compileComponents();
    const fixture = TestBed.createComponent(GroupMemberPanelComponent);
    fixture.componentRef.setInput('conversation', {
      id: 'group', kind: 'dm', isGroup: true, tenantId: 'tenant', title: 'Group', subtitle: '',
      viewerIsParticipant: true, viewerWasRemoved: false, capabilities: ['manageGroupMembers'],
      attachment: { mode: 'disabled', label: '' },
      groupMembers: [{ userId: 'peer', displayName: 'Peer', role: 'member', isCurrentUser: false, membership: 'active' }]
    });
    fixture.detectChanges();
    const list = fixture.nativeElement.querySelector('ul') as HTMLElement;
    const child = list.querySelector('button')!;
    const scrollTo = vi.fn();
    list.scrollTo = scrollTo;
    for (const options of [{ ctrlKey: true }, { metaKey: true }, { altKey: true }, { shiftKey: true },
      { isComposing: true }, { keyCode: 229 }]) {
      const event = new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true, ...options });
      list.dispatchEvent(event);
      expect(event.defaultPrevented).toBe(false);
    }
    for (const key of ['Home', 'End']) {
      const childEvent = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      child.dispatchEvent(childEvent);
      expect(childEvent.defaultPrevented).toBe(false);
    }
    const consumed = new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true });
    consumed.preventDefault();
    list.dispatchEvent(consumed);
    expect(consumed.defaultPrevented).toBe(true);
    const arrow = new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true });
    list.dispatchEvent(arrow);
    expect(arrow.defaultPrevented).toBe(false);
    expect(scrollTo).not.toHaveBeenCalled();
    for (const key of ['End', 'Home']) {
      const boundary = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      list.dispatchEvent(boundary);
      expect(boundary.defaultPrevented).toBe(true);
    }
    expect(scrollTo).toHaveBeenCalledTimes(2);
    fixture.destroy();
  });
});


describe('Group roster focus intent across an authorized aggregate revalidation', () => {
  const authorized = (overrides: Partial<MessagingConversationViewModel> = {}): MessagingConversationViewModel => ({
    id: 'group-focus-a', kind: 'dm', isGroup: true, tenantId: 'tenant-focus-a', workspaceId: 'workspace-focus-a',
    title: 'Synthetic group', subtitle: '', viewerIsParticipant: true, viewerWasRemoved: false,
    capabilities: ['readBody', 'leaveGroup'], attachment: { mode: 'disabled', label: '' },
    groupMembers: [{ userId: 'actor-focus-a', displayName: 'Synthetic actor', role: 'readOnly',
      isCurrentUser: true, membership: 'active' }],
    ...overrides
  });
  async function setup(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [GroupMemberPanelComponent], providers: [GroupRosterFocusIntent]
    }).compileComponents();
  }
  function mount(view: MessagingConversationViewModel) {
    const fixture = TestBed.createComponent(GroupMemberPanelComponent);
    fixture.componentRef.setInput('conversation', view);
    fixture.detectChanges();
    return fixture;
  }

  it.each(['summary', '.group-members__list'] as const)(
    'restores %s focus only after the same Group has been reauthorized', async (selector) => {
      await setup();
      const original = mount(authorized());
      const focused = original.nativeElement.querySelector(selector) as HTMLElement;
      focused.focus();
      expect(document.activeElement).toBe(focused);
      original.destroy();
      const restored = mount(authorized());
      await Promise.resolve();
      expect(document.activeElement).toBe(restored.nativeElement.querySelector(selector));
      restored.destroy();
    }
  );

  it('does not retain any participant UI or restore focus after authorization denial', async () => {
    await setup();
    const original = mount(authorized());
    const list = original.nativeElement.querySelector('.group-members__list') as HTMLElement;
    list.focus(); original.destroy();
    const denied = mount(authorized({ capabilities: [], groupMembers: [],
      viewerIsParticipant: false }));
    await Promise.resolve();
    expect(document.activeElement).not.toBe(denied.nativeElement.querySelector('.group-members__list'));
    denied.destroy();
    const restored = mount(authorized());
    await Promise.resolve();
    expect(document.activeElement).not.toBe(restored.nativeElement.querySelector('.group-members__list'));
    restored.destroy();
  });

  it('does not transfer a pending focus into another Workspace or actor', async () => {
    await setup();
    const original = mount(authorized());
    (original.nativeElement.querySelector('.group-members__list') as HTMLElement).focus();
    original.destroy();
    const otherScope = mount(authorized({ workspaceId: 'workspace-focus-b' }));
    await Promise.resolve();
    expect(document.activeElement).not.toBe(otherScope.nativeElement.querySelector('.group-members__list'));
    otherScope.destroy();
    const originalAgain = mount(authorized());
    await Promise.resolve();
    expect(document.activeElement).not.toBe(originalAgain.nativeElement.querySelector('.group-members__list'));
    originalAgain.destroy();
  });

  it('does not steal keyboard focus the user moved outside the roster', async () => {
    await setup();
    const original = mount(authorized());
    (original.nativeElement.querySelector('summary') as HTMLElement).focus();
    original.destroy();
    const outside = document.createElement('button');
    document.body.appendChild(outside);
    try {
      outside.focus();
      const restored = mount(authorized());
      await Promise.resolve();
      expect(document.activeElement).toBe(outside);
      restored.destroy();
    } finally { outside.remove(); }
  });
});
