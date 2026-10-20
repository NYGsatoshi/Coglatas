import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { MessagingFacade } from './messaging.facade';
import { MessagesPageComponent } from './messages-page/messages-page.component';
import { WorkspaceRecipientPickerComponent } from './workspace-recipient-picker/workspace-recipient-picker.component';

const actor = DEFAULT_AUTH_SESSION.currentUser!.userId;
const clearers = new Map<string, (reason: 'authorization') => void>();

async function setup(): Promise<HttpTestingController> {
  clearers.clear();
  await TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: RealtimeFacade, useValue: { durableEvents$: new Subject(),
        registerProtectedStateClearer: (key: string, callback: (reason: 'authorization') => void) => { clearers.set(key, callback); return () => {}; },
        registerCatchUp: () => () => {}, registerSubscription: () => () => {} } }
    ]
  }).compileComponents();
  return TestBed.inject(HttpTestingController);
}

function flushGroup(http: HttpTestingController, canManage = true, readOnlyManager = false): void {
  http.expectOne('/api/conversations').flush({ items: [{ id: 'group-a', type: 'Group', workspaceId: 'workspace-a' }] });
  http.expectOne('/api/conversations/group-a').flush({ id: 'group-a', workspaceId: 'workspace-a', type: 'Group', title: 'Council group',
    members: [
      { userId: actor, displayName: 'Current participant', role: readOnlyManager ? 'ReadOnly' : canManage ? 'Admin' : 'Member', canRead: true, canPost: true, canManageMembers: readOnlyManager },
      { userId: 'peer', displayName: 'Peer', role: 'Member', canRead: true, canPost: true }
    ] });
  http.expectOne('/api/conversations/group-a/messages').flush({ items: [{ id: 'message-a', body: 'Protected group body', authorUserId: 'peer' }] });
}

describe('Approved Group DM current-authority frontend commands', () => {
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); });

  it('posts through the existing message contract once and never opens a Group thread', async () => {
    const http = await setup(); const facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('group-a', 'dm'); flushGroup(http);
    facade.openThread('message-a', 'thread-trigger');
    http.expectNone('/api/messages/message-a/thread');
    facade.setDraft('One group command'); facade.sendDraft(); facade.sendDraft();
    const send = http.expectOne('/api/conversations/group-a/messages');
    expect(send.request.method).toBe('POST'); expect(send.request.body.clientRequestId).toBeTruthy();
    send.flush({ id: 'sent-a', conversationId: 'group-a', authorUserId: actor, body: 'One group command', clientRequestId: send.request.body.clientRequestId });
    expect(facade.page().draft).toBe('');
    expect(facade.page().messages.filter((message) => message.id === 'sent-a')).toHaveLength(1);
  });

  it('waits for one add command then clears the aggregate and loads fresh server members', async () => {
    const http = await setup(); const facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('group-a', 'dm'); flushGroup(http);
    facade.addGroupMember('new-peer'); facade.addGroupMember('new-peer');
    const add = http.expectOne('/api/conversations/group-a/members');
    expect(add.request.body).toEqual({ userId: 'new-peer' });
    expect(facade.groupAction().pending).toBe('add');
    add.flush({ userId: 'new-peer', role: 'Member' });
    expect(facade.page().status).toBe('loading');
    expect(facade.page().conversation.groupMembers ?? []).toEqual([]);
    expect(facade.page().messages).toEqual([]);
    flushGroup(http);
    expect(facade.groupAction().pending).toBeNull();
    expect(facade.page().conversation.groupMembers?.map((member) => member.userId)).toEqual([actor, 'peer']);
  });

  it('treats a legacy mutation 400 as unconfirmed and hides protected state before fresh denied reads', async () => {
    const http = await setup(); const facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('group-a', 'dm'); flushGroup(http);
    facade.setDraft('A sensitive draft'); facade.removeGroupMember('peer');
    http.expectOne('/api/conversations/group-a/members/peer').flush({ error: 'Masked' }, { status: 400, statusText: 'Denied' });
    expect(facade.page().status).toBe('loading'); expect(facade.page().messages).toEqual([]);
    expect(facade.page().conversation.groupMembers ?? []).toEqual([]); expect(facade.page().draft).toBe('');
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne('/api/conversations/group-a').flush({ error: 'Masked' }, { status: 400, statusText: 'Denied' });
    http.expectNone('/api/conversations/group-a/messages');
    expect(facade.page().conversation.groupMembers ?? []).toEqual([]); expect(facade.page().messages).toEqual([]);
    expect(facade.groupAction().errorKey).toBe('messaging.group.members.failed');
  });

  it('cancels pending management on an authorization boundary without replay or stale participants', async () => {
    const http = await setup(); const facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('group-a', 'dm'); flushGroup(http);
    facade.addGroupMember('new-peer'); const pending = http.expectOne('/api/conversations/group-a/members');
    clearers.get('messaging')!('authorization');
    expect(pending.cancelled).toBe(true);
    expect(facade.groupAction().pending).toBeNull(); expect(facade.page().messages).toEqual([]);
    expect(facade.page().conversation.groupMembers ?? []).toEqual([]);
    http.expectNone((request) => request.method === 'POST');
  });

  it('does not send ordinary-member management but allows the participant leave command', async () => {
    const http = await setup(); const facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('group-a', 'dm'); flushGroup(http, false);
    facade.addGroupMember('new-peer'); facade.removeGroupMember('peer');
    http.expectNone('/api/conversations/group-a/members'); http.expectNone('/api/conversations/group-a/members/peer');
    facade.leaveGroup(); const leave = http.expectOne('/api/conversations/group-a/leave'); expect(leave.request.body).toEqual({});
    leave.flush({});
    expect(facade.page().messages).toEqual([]);
    expect(facade.groupAction().withdrawnConversationId).toBe('group-a');
    http.expectOne('/api/conversations').flush({ items: [] });
  });

  it('allows the explicit ReadOnly management flag while keeping posting denied', async () => {
    const http = await setup(); const facade = TestBed.inject(MessagingFacade);
    facade.loadConversation('group-a', 'dm'); flushGroup(http, true, true);
    facade.setDraft('ReadOnly draft'); facade.sendDraft(); http.expectNone('/api/conversations/group-a/messages');
    facade.addGroupMember('new-peer');
    http.expectOne('/api/conversations/group-a/members').flush({ userId: 'new-peer' });
    flushGroup(http, true, true);
    expect(facade.page().conversation.capabilities).not.toContain('postMessage');
  });
});

describe('Exact Workspace Group recipient picker', () => {
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); });

  it('uses only explicit active memberships and supports filtered selected-chip removal', async () => {
    const http = await setup(); const fixture = TestBed.createComponent(WorkspaceRecipientPickerComponent);
    fixture.componentRef.setInput('workspaceId', 'workspace-a'); fixture.componentRef.setInput('excludedUserIds', [actor]); fixture.detectChanges();
    http.expectOne('/api/workspaces/workspace-a/members').flush([
      { userId: actor, displayName: 'Current actor', status: 1 }, { userId: 'first', displayName: 'First', status: 'Active' },
      { userId: 'second', displayName: 'Second', status: 1 }, { userId: 'suspended', displayName: 'Suspended', status: 2 },
      { userId: 'unknown', displayName: 'Unknown' }, { userId: 'first', displayName: 'Duplicate', status: 1 }
    ]);
    fixture.detectChanges();
    expect(fixture.componentInstance.matches().map((person) => person.userId)).toEqual(['first', 'second']);
    const first = fixture.componentInstance.matches()[0]; fixture.componentInstance.toggle(first);
    fixture.componentInstance.query.set('Second'); fixture.componentInstance.toggle(first);
    expect(fixture.componentInstance.selected()).toEqual([]);
    http.expectNone((request) => request.url === '/api/conversations/recipients');
  });

  it('cancels old Workspace reads, clears choices on denial and never falls back to the shared directory', async () => {
    const http = await setup(); const fixture = TestBed.createComponent(WorkspaceRecipientPickerComponent);
    fixture.componentRef.setInput('workspaceId', 'workspace-a'); fixture.detectChanges();
    const stale = http.expectOne('/api/workspaces/workspace-a/members');
    fixture.componentRef.setInput('workspaceId', 'workspace-b'); fixture.detectChanges(); expect(stale.cancelled).toBe(true);
    http.expectOne('/api/workspaces/workspace-b/members').flush({ error: 'Denied' }, { status: 400, statusText: 'Denied' });
    fixture.detectChanges(); expect(fixture.componentInstance.matches()).toEqual([]); expect(fixture.componentInstance.selected()).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).not.toBeNull();
    http.expectNone((request) => request.url === '/api/conversations/recipients');
  });
});

describe('Explicit Group creation', () => {
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); });

  it('requires two Workspace recipients and submits the existing Group7 contract once before DM navigation', async () => {
    const http = await setup(); TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace({ id: 'workspace-a', label: 'Workspace A' });
    const fixture = TestBed.createComponent(MessagesPageComponent); const component = fixture.componentInstance;
    http.expectOne('/api/conversations').flush({ items: [] }); fixture.detectChanges();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    component.openCreateDialog(); component.changeCreateMode('group'); fixture.detectChanges();
    http.expectOne('/api/workspaces/workspace-a/members').flush([{ userId: 'first', displayName: 'First', status: 1 }, { userId: 'second', displayName: 'Second', status: 1 }]);
    component.onGroupRecipientsChanged([{ userId: 'first', displayName: 'First' }]); expect(component.canSubmitCreate()).toBe(false);
    component.onGroupRecipientsChanged([{ userId: 'first', displayName: 'First' }, { userId: 'second', displayName: 'Second' }]);
    component.groupTitle.set('  Council planning  '); component.createConversation(); component.createConversation();
    const create = http.expectOne('/api/conversations'); expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ type: 7, workspaceId: 'workspace-a', memberUserIds: ['first', 'second'], title: 'Council planning' });
    create.flush({ id: 'new-group', workspaceId: 'workspace-a', type: 'Group', projectId: null, parentConversationId: null });
    expect(navigate).toHaveBeenCalledWith('/dm/new-group');
    http.expectNone('/api/conversations/direct');
  });

  for (const outcome of ['network-error', 'wrong-workspace'] as const) {
    it(`does not claim failure or auto-resend an unconfirmed Group creation after ${outcome}`, async () => {
      const http = await setup(); TestBed.inject(ActiveWorkspaceFacade).setActiveWorkspace({ id: 'workspace-a', label: 'Workspace A' });
      const fixture = TestBed.createComponent(MessagesPageComponent); const component = fixture.componentInstance;
      http.expectOne('/api/conversations').flush({ items: [] }); fixture.detectChanges();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      component.openCreateDialog(); component.changeCreateMode('group'); fixture.detectChanges();
      http.expectOne('/api/workspaces/workspace-a/members').flush([]);
      component.onGroupRecipientsChanged([{ userId: 'first', displayName: 'First' }, { userId: 'second', displayName: 'Second' }]);
      component.createConversation(); const command = http.expectOne('/api/conversations');
      if (outcome === 'network-error') { command.error(new ProgressEvent('error')); }
      else { command.flush({ id: 'group-other', workspaceId: 'workspace-b', type: 'Group' }); }
      fixture.detectChanges();
      expect(component.createDialogOpen()).toBe(true);
      expect(component.createError()).toContain('could not be confirmed');
      expect(navigate).not.toHaveBeenCalled();
      http.expectNone((request) => request.method === 'POST');
    });
  }
});
