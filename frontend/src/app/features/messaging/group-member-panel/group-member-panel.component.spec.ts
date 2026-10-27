import { TestBed } from '@angular/core/testing';
import { GroupMemberPanelComponent } from './group-member-panel.component';

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
