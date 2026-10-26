import { TestBed } from '@angular/core/testing';
import { GroupMemberPanelComponent } from './group-member-panel.component';

describe('Group roster boundary keyboard scope', () => {
  it('leaves child controls, modified keys and IME events to their native handlers', async () => {
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
    const childEvent = new KeyboardEvent('keydown', { key: 'End', bubbles: true, cancelable: true });
    child.dispatchEvent(childEvent);
    expect(childEvent.defaultPrevented).toBe(false);
    const arrow = new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true });
    list.dispatchEvent(arrow);
    expect(arrow.defaultPrevented).toBe(false);
    expect(scrollTo).not.toHaveBeenCalled();
    fixture.destroy();
  });
});
