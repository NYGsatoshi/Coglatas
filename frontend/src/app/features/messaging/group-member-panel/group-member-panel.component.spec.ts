import { TestBed } from '@angular/core/testing';
import { GroupMemberPanelComponent, GroupRosterBoundaryIntent } from './group-member-panel.component';

describe('Group roster boundary keyboard scope', () => {
  it('handles only direct boundary keys, preserving child controls, modifiers and IME', async () => {
    await TestBed.configureTestingModule({ imports: [GroupMemberPanelComponent] }).compileComponents();
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


describe('Group roster boundary across a same-scope authorized remount', () => {
  const view = (workspaceId = 'workspace-one', readAllowed = true) => ({
    id: 'group-one', kind: 'dm' as const, isGroup: true, tenantId: 'tenant-one', workspaceId,
    title: 'Synthetic group', subtitle: '', viewerIsParticipant: true, viewerWasRemoved: false,
    capabilities: readAllowed ? ['readBody', 'leaveGroup'] : ['leaveGroup'],
    attachment: { mode: 'disabled', label: '' },
    groupMembers: [{ userId: 'reader-one', displayName: 'Synthetic member', isCurrentUser: true,
      role: 'readOnly' as const, membership: 'active' as const }]
  });

  async function setup() {
    await TestBed.configureTestingModule({
      imports: [GroupMemberPanelComponent], providers: [GroupRosterBoundaryIntent]
    }).compileComponents();
  }

  function mount(conversation = view()) {
    const fixture = TestBed.createComponent(GroupMemberPanelComponent);
    fixture.componentRef.setInput('conversation', conversation);
    fixture.detectChanges();
    const list = fixture.nativeElement.querySelector('.group-members__list') as HTMLElement;
    Object.defineProperty(list, 'scrollHeight', { configurable: true, value: 220 });
    Object.defineProperty(list, 'clientHeight', { configurable: true, value: 40 });
    list.scrollTo = vi.fn();
    return { fixture, list };
  }

  function rememberEnd() {
    const old = mount();
    old.list.focus();
    old.list.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true, cancelable: true }));
    expect(old.list.scrollTo).toHaveBeenCalledWith({ top: 180, behavior: 'instant' });
    (old.fixture.nativeElement.querySelector('[data-testid="group-leave"]') as HTMLElement).focus();
    old.fixture.destroy();
  }

  it('restores End in the fresh authorized scrollport without retaining old members or nodes', async () => {
    await setup();
    rememberEnd();
    const next = mount();
    await Promise.resolve();
    expect(next.list.scrollTo).toHaveBeenCalledOnce();
    expect(next.list.scrollTo).toHaveBeenCalledWith({ top: 180, behavior: 'instant' });
    next.fixture.destroy();
  });

  it('does not restore a boundary into another Workspace or later same-scope mount', async () => {
    await setup();
    rememberEnd();
    const changed = mount(view('workspace-two'));
    await Promise.resolve();
    expect(changed.list.scrollTo).not.toHaveBeenCalled();
    changed.fixture.destroy();
    const oldScope = mount();
    await Promise.resolve();
    expect(oldScope.list.scrollTo).not.toHaveBeenCalled();
    oldScope.fixture.destroy();
  });

  it('discards the pending boundary when the new authorization lacks readBody', async () => {
    await setup();
    rememberEnd();
    const denied = mount(view('workspace-one', false));
    await Promise.resolve();
    expect(denied.list.scrollTo).not.toHaveBeenCalled();
    denied.fixture.destroy();
    const authorizedAgain = mount();
    await Promise.resolve();
    expect(authorizedAgain.list.scrollTo).not.toHaveBeenCalled();
    authorizedAgain.fixture.destroy();
  });

  it('does not move the scrollport when the user focuses a control outside the remounted Group', async () => {
    await setup();
    rememberEnd();
    const outside = document.createElement('button');
    document.body.appendChild(outside);
    try {
      outside.focus();
      const next = mount();
      await Promise.resolve();
      expect(next.list.scrollTo).not.toHaveBeenCalled();
      next.fixture.destroy();
    } finally { outside.remove(); }
  });
});
