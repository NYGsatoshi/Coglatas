import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ConversationListComponent } from './conversation-list.component';
import { mapConversationListItem } from '../messaging.mapper';
import { MessagingConversationListItem } from '../messaging.types';

beforeEach(() => { localStorage.setItem('coglatas.locale', 'ja'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('ConversationListComponent state hierarchy', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders selected, unread, and mention as independent accessible states', async () => {
    await TestBed.configureTestingModule({
      imports: [ConversationListComponent],
      providers: [provideRouter([])]
    }).compileComponents();

    const fixture = TestBed.createComponent(ConversationListComponent);
    const conversations: readonly MessagingConversationListItem[] = [
      {
        id: 'conversation-selected',
        kind: 'channel',
        title: 'Selected conversation',
        route: '/workspaces/workspace-a/channels/conversation-selected',
        lastActivityLabel: '09:15',
        safePreviewLabel: 'Selected preview',
        viewerIsParticipant: true,
        unreadCount: 3,
        hasMention: true
      },
      {
        id: 'conversation-read-mentioned',
        kind: 'channel',
        title: 'Read but mentioned',
        route: '/workspaces/workspace-a/channels/conversation-read-mentioned',
        lastActivityLabel: '09:10',
        safePreviewLabel: 'Mention remains after read',
        viewerIsParticipant: true,
        unreadCount: 0,
        hasMention: true
      },
      {
        id: 'conversation-unread-only',
        kind: 'channel',
        title: 'Unread only',
        route: '/workspaces/workspace-a/channels/conversation-unread-only',
        lastActivityLabel: '09:05',
        safePreviewLabel: 'Unread without mention',
        viewerIsParticipant: true,
        unreadCount: 1,
        hasMention: false
      }
    ];

    fixture.componentRef.setInput('conversations', conversations);
    fixture.componentRef.setInput('selectedConversationId', 'conversation-selected');
    fixture.detectChanges();

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[data-testid="conversation-list-item"]')
    );
    expect(rows).toHaveLength(3);

    const selected = rows[0];
    expect(selected.getAttribute('aria-current')).toBe('page');
    expect(selected.querySelector('[data-testid="conversation-selected"]')?.textContent).toContain('選択中');
    expect(selected.querySelector('[data-testid="conversation-unread"]')?.textContent).toContain('未読 3件');
    expect(selected.querySelector('[data-testid="conversation-mention"]')?.textContent).toContain('あなた宛て');
    expect(selected.querySelector('[data-testid="conversation-mention"]')?.getAttribute('aria-label')).toBe(
      'あなたへのメンションがあります'
    );

    const readButMentioned = rows[1];
    expect(readButMentioned.getAttribute('aria-current')).toBeNull();
    expect(readButMentioned.querySelector('[data-testid="conversation-unread"]')).toBeNull();
    expect(readButMentioned.querySelector('[data-testid="conversation-mention"]')).not.toBeNull();

    const unreadOnly = rows[2];
    expect(unreadOnly.querySelector('[data-testid="conversation-unread"]')).not.toBeNull();
    expect(unreadOnly.querySelector('[data-testid="conversation-mention"]')).toBeNull();
  });

  it('keeps mention attention when the mapped read count is zero', () => {
    const item = mapConversationListItem({
      id: 'conversation-a',
      workspaceId: 'workspace-a',
      type: 'ProjectChannel',
      title: 'General',
      unreadCount: 0,
      hasMention: true,
      createdAt: '2026-08-22T00:00:00Z'
    });

    expect(item.unreadCount).toBe(0);
    expect(item.hasMention).toBe(true);
  });
});
