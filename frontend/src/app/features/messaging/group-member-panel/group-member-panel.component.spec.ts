import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { GroupMemberPanelComponent, GroupRosterBoundaryIntent, GroupRosterFocusIntent } from './group-member-panel.component';
import { type MessagingConversationViewModel } from '../messaging.types';

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
    const finalRow = list.querySelector('li:last-child') as HTMLElement;
    finalRow.scrollIntoView = vi.fn();
    return { fixture, list, finalRow };
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
    expect(next.finalRow.scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
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


@Component({
  standalone: true,
  imports: [GroupMemberPanelComponent],
  template: `
    <button type="button" data-testid="outside-roster">Outside</button>
    @if (mounted()) {
      <app-group-member-panel [conversation]="conversation" />
    }
  `
})
class ConditionalRosterFocusHost {
  readonly mounted = signal(true);
  conversation!: MessagingConversationViewModel;
}

describe('Fresh-authority roster focus across actual Angular conditional teardown', () => {
  const authorized = (overrides: Partial<MessagingConversationViewModel> = {}): MessagingConversationViewModel => ({
    id: 'group-focus-one', kind: 'dm', isGroup: true, tenantId: 'tenant-focus-one',
    workspaceId: 'workspace-focus-one', title: 'Synthetic Group', subtitle: '',
    viewerIsParticipant: true, viewerWasRemoved: false,
    capabilities: ['readBody', 'leaveGroup'], attachment: { mode: 'disabled', label: '' },
    groupMembers: [{ userId: 'reader-focus-one', displayName: 'Synthetic reader',
      isCurrentUser: true, role: 'readOnly', membership: 'active' }],
    ...overrides
  });

  async function hostWith(view = authorized()) {
    await TestBed.configureTestingModule({
      imports: [ConditionalRosterFocusHost],
      providers: [GroupRosterBoundaryIntent, GroupRosterFocusIntent]
    }).compileComponents();
    const host = TestBed.createComponent(ConditionalRosterFocusHost);
    host.componentInstance.conversation = view;
    host.detectChanges();
    return host;
  }

  it.each(['summary', '.group-members__list', '[data-testid="group-leave"]'] as const)(
    'restores %s focus only after an authorized @if teardown and remount', async (selector) => {
      const host = await hostWith();
      const original = host.nativeElement.querySelector(selector) as HTMLElement;
      original.focus();
      expect(document.activeElement).toBe(original);
      host.componentInstance.mounted.set(false);
      host.detectChanges();
      expect(host.nativeElement.querySelector('[data-testid="group-members-panel"]')).toBeNull();
      expect(host.nativeElement.textContent).not.toContain('Synthetic reader');
      host.componentInstance.mounted.set(true);
      host.detectChanges();
      await Promise.resolve();
      const replacement = host.nativeElement.querySelector(selector) as HTMLElement;
      expect(replacement).not.toBe(original);
      expect(document.activeElement).toBe(replacement);
      host.destroy();
    }
  );

  it('does not steal focus when the user moves to an outside control', async () => {
    const host = await hostWith();
    (host.nativeElement.querySelector('.group-members__list') as HTMLElement).focus();
    const outside = host.nativeElement.querySelector('[data-testid="outside-roster"]') as HTMLElement;
    outside.focus();
    expect(document.activeElement).toBe(outside);
    host.componentInstance.mounted.set(false);
    host.detectChanges();
    host.componentInstance.mounted.set(true);
    host.detectChanges();
    await Promise.resolve();
    expect(document.activeElement).toBe(outside);
    host.destroy();
  });

  it('rejects a different Workspace and never replays a consumed focus intent', async () => {
    const host = await hostWith();
    const original = host.nativeElement.querySelector('.group-members__list') as HTMLElement;
    original.focus();
    host.componentInstance.mounted.set(false);
    host.detectChanges();
    host.componentInstance.conversation = authorized({ workspaceId: 'workspace-focus-other' });
    host.componentInstance.mounted.set(true);
    host.detectChanges();
    await Promise.resolve();
    expect(document.activeElement).not.toBe(host.nativeElement.querySelector('.group-members__list'));
    host.componentInstance.mounted.set(false);
    host.detectChanges();
    host.componentInstance.conversation = authorized();
    host.componentInstance.mounted.set(true);
    host.detectChanges();
    await Promise.resolve();
    expect(document.activeElement).not.toBe(host.nativeElement.querySelector('.group-members__list'));
    host.destroy();
  });

  it('does not recover old focus or protected rows after denial', async () => {
    const host = await hostWith();
    (host.nativeElement.querySelector('summary') as HTMLElement).focus();
    host.componentInstance.mounted.set(false);
    host.detectChanges();
    host.componentInstance.conversation = authorized({
      capabilities: [], groupMembers: [], viewerIsParticipant: false
    });
    host.componentInstance.mounted.set(true);
    host.detectChanges();
    await Promise.resolve();
    expect(document.activeElement).not.toBe(host.nativeElement.querySelector('summary'));
    expect(host.nativeElement.textContent).not.toContain('Synthetic reader');
    host.componentInstance.mounted.set(false);
    host.detectChanges();
    host.componentInstance.conversation = authorized();
    host.componentInstance.mounted.set(true);
    host.detectChanges();
    await Promise.resolve();
    expect(document.activeElement).not.toBe(host.nativeElement.querySelector('summary'));
    host.destroy();
  });
});
