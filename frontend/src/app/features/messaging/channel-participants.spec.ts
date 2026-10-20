import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { I18nService } from '../../core/i18n/i18n.service';
import { ChannelMessagingPageComponent } from './channel-messaging-page/channel-messaging-page.component';
import { ConversationDetailDto } from './messaging.api';
import { MessagingFacade } from './messaging.facade';
import { mapConversationPage } from './messaging.mapper';

const viewerId = DEFAULT_AUTH_SESSION.currentUser?.userId ?? 'mock-user-a';
const members = [
  { userId: viewerId, displayName: 'Council Viewer', role: 0, canRead: true, canPost: true },
  { userId: 'member-b', displayName: 'Council Colleague', role: 1, canRead: true },
  { userId: 'member-c', displayName: 'Council Reader', role: 2, canRead: true },
  { userId: 'unknown-role', displayName: 'Current Guest', role: 99, canRead: true },
  { userId: 'denied', displayName: 'Denied Member', role: 1, canRead: false },
  { userId: 'left', displayName: 'Former Member', role: 1, canRead: true, leftAt: '2026-10-19T00:00:00Z' },
  { userId: 'removed', displayName: 'Removed Member', role: 1, canRead: true, removedAt: '2026-10-19T00:00:00Z' },
];
const detail = (id = 'channel-a', workspaceId = 'workspace-a'): ConversationDetailDto => ({
  id, workspaceId, type: 10, title: 'Council channel', members,
});
const map = (value: ConversationDetailDto) => mapConversationPage(value, [], {
  currentUserId: viewerId, currentTenantId: 'tenant-a', fallbackRouteKind: 'channel',
});

describe('Channel participant projection and retained-state boundaries', () => {
  beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => { localStorage.removeItem('coglatas.locale'); TestBed.resetTestingModule(); });

  it('includes the current reader and only active readable server members without inventing a role', () => {
    const value = detail();
    const page = map({ ...value, members: [...members, members[1], { userId: '', displayName: 'Invalid', canRead: true }] });
    expect(page.conversation).toHaveProperty('participants', [
      { userId: viewerId, displayName: 'Council Viewer', role: 'admin', isCurrentUser: true },
      { userId: 'member-b', displayName: 'Council Colleague', role: 'member', isCurrentUser: false },
      { userId: 'member-c', displayName: 'Council Reader', role: 'readOnly', isCurrentUser: false },
      { userId: 'unknown-role', displayName: 'Current Guest', isCurrentUser: false },
    ]);
    expect(page.conversation.participants?.find(member => member.userId === 'unknown-role')?.role).toBeUndefined();
    expect(page.conversation.mentionCandidates?.some(member => member.userId === viewerId)).toBe(false);
    expect(JSON.stringify(page.conversation)).not.toContain('Denied Member');
    expect(JSON.stringify(page.conversation)).not.toContain('Former Member');
    expect(JSON.stringify(page.conversation)).not.toContain('Removed Member');
  });

  for (const deniedViewer of [
    { canRead: false }, { leftAt: '2026-10-19T00:00:00Z' }, { removedAt: '2026-10-19T00:00:00Z' },
  ]) {
    it(`clears the participant projection when viewer authority is unavailable: ${Object.keys(deniedViewer)[0]}`, () => {
      expect(map({ ...detail(), members: [{ ...members[0], ...deniedViewer }, ...members.slice(1)] }).conversation)
        .toMatchObject({ participants: [] });
    });
  }

  const open = async () => {
    const params = new BehaviorSubject(convertToParamMap({ workspaceId: 'workspace-a', conversationId: 'channel-a' }));
    await TestBed.configureTestingModule({
      imports: [ChannelMessagingPageComponent],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
      ],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const workspace = TestBed.inject(ActiveWorkspaceFacade);
    workspace.setActiveWorkspace({ id: 'workspace-a', label: 'Workspace A' });
    const fixture = TestBed.createComponent(ChannelMessagingPageComponent);
    const root = fixture.nativeElement as HTMLElement;
    const complete = (value = detail()) => {
      http.expectOne('/api/conversations').flush({ items: [] });
      http.expectOne(`/api/conversations/${value.id}`).flush(value);
      http.expectOne(`/api/conversations/${value.id}/messages`).flush({ items: [] });
      fixture.detectChanges();
    };
    complete();
    return { params, http, workspace, fixture, root, complete, facade: TestBed.inject(MessagingFacade) };
  };

  it('renders actual names, numeric server roles and the current viewer in the scoped Channel inspector', async () => {
    const { root, http, fixture } = await open();
    const inspector = root.querySelector('[data-testid="channel-details-inspector"]');
    expect(inspector).not.toBeNull();
    expect(inspector?.textContent).toContain('Council Viewer');
    expect(inspector?.textContent).toContain('Admin');
    expect(inspector?.textContent).toContain('Read only');
    expect(inspector?.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(4);
    expect(inspector?.textContent).not.toContain('Denied Member');
    expect(inspector?.textContent).not.toContain('Former Member');
    expect(inspector?.textContent).not.toContain('Removed Member');
    expect(inspector?.textContent).not.toContain('@');
    TestBed.inject(I18nService).setLocale('ja');
    fixture.detectChanges();
    expect(inspector?.textContent).toContain('管理者');
    expect(inspector?.textContent).toContain('Council Viewer');
    expect(inspector?.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(4);
    http.verify();
  });

  it('removes previous participants immediately on reload and keeps them absent after denial', async () => {
    const { root, http, fixture, facade } = await open();
    facade.manualRefresh();
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(0);
    http.expectOne('/api/conversations').flush({ items: [] });
    http.expectOne('/api/conversations/channel-a').flush({}, { status: 403, statusText: 'Forbidden' });
    const pendingMessages = http.match('/api/conversations/channel-a/messages');
    for (const request of pendingMessages) if (!request.cancelled) request.flush({ items: [] });
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(0);
    expect(root.textContent).not.toContain('Council Colleague');
    http.verify();
  });

  it('hides prior members before a Workspace commit, through delayed loads and after scope clear', async () => {
    const { root, http, fixture, params, workspace, complete } = await open();
    params.next(convertToParamMap({ workspaceId: 'workspace-b', conversationId: 'channel-b' }));
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(0);
    http.expectNone('/api/conversations/channel-b');
    workspace.setActiveWorkspace({ id: 'workspace-b', label: 'Workspace B' });
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(0);
    complete({ ...detail('channel-b', 'workspace-b'), members: [{ ...members[0], displayName: 'Workspace B Viewer' }] });
    expect(root.textContent).not.toContain('Council Colleague');
    workspace.clearWorkspace();
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(0);
    expect(root.textContent).not.toContain('Workspace B Viewer');
    http.verify();
  });

  it('does not display an otherwise readable response outside the exact route and committed Workspace', async () => {
    const { root, http, fixture, facade, complete } = await open();
    facade.loadConversation('channel-a', 'channel', 'workspace-b');
    complete({ ...detail('channel-a', 'workspace-b'), members: [{ ...members[0], displayName: 'Other Workspace Reader' }] });
    expect(root.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(0);
    expect(root.textContent).not.toContain('Other Workspace Reader');
    facade.loadConversation('other-channel', 'channel', 'workspace-a');
    complete({ ...detail('other-channel'), members: [{ ...members[0], displayName: 'Other Channel Reader' }] });
    fixture.detectChanges();
    expect(root.querySelectorAll('[data-testid="channel-participant"]')).toHaveLength(0);
    expect(root.textContent).not.toContain('Other Channel Reader');
    http.verify();
  });
});
