import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { I18nService } from '../../core/i18n/i18n.service';
import { localizeMessagingPage } from './messaging.i18n';
import { mapConversationPage } from './messaging.mapper';

describe('Messaging localized presentation', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    localStorage.removeItem('coglatas.locale');
  });

  it('localizes fallback names, safe errors, and timestamps without rewriting server-authored content', () => {
    localStorage.setItem('coglatas.locale', 'ja');
    const i18n = TestBed.inject(I18nService);
    const source = mapConversationPage(
      {
        id: 'conversation-a',
        workspaceId: 'workspace-a',
        type: 'ProjectChannel',
        title: 'Conversation',
        members: [{ userId: 'me', canRead: true, canPost: true }],
      },
      [
        {
          id: 'message-a',
          authorDisplayName: 'Unknown user',
          body: 'messaging.title / メモ',
          createdAt: '2026-10-17T08:00:00Z',
        },
        { id: 'message-b', body: 'Leave this body unchanged', createdAt: 'invalid timestamp' },
      ],
      { currentUserId: 'me', currentTenantId: 'tenant-a', fallbackRouteKind: 'channel' },
    );
    const view = computed(() =>
      localizeMessagingPage(
        {
          ...source,
          draft: 'Unsent / 下書き',
          sendState: {
            status: 'failed',
            message: 'Message API request failed.',
            messageKey: 'messaging.errors.messageApi',
          },
        },
        i18n,
      ),
    );

    expect(view().conversation.title).toBe('Conversation');
    expect(view().messages[0].authorLabel).toBe('Unknown user');
    expect(view().messages[0].body).toBe('messaging.title / メモ');
    expect(view().messages[1].authorLabel).toBe('不明なユーザー');
    expect(view().messages[1].sentAtLabel).toBe('');
    expect(view().sendState).toMatchObject({
      status: 'failed',
      message: 'メッセージの処理に失敗しました。',
    });
    expect(view().messages[0].sentAtLabel).toBe(
      i18n.formatDateTime('2026-10-17T08:00:00Z', { dateStyle: 'medium', timeStyle: 'short' }),
    );

    i18n.setLocale('en');
    expect(view().messages[1].authorLabel).toBe('Unknown user');
    expect(view().sendState).toMatchObject({
      status: 'failed',
      message: 'Message API request failed.',
    });
    expect(view().messages[0].sentAtLabel).toBe(
      i18n.formatDateTime('2026-10-17T08:00:00Z', { dateStyle: 'medium', timeStyle: 'short' }),
    );
    expect(view().draft).toBe('Unsent / 下書き');
    expect(source.messages[0].body).toBe('messaging.title / メモ');
    expect(source.messages[1].authorLabel).toBe('Unknown user');
    expect(source.conversation.title).toBe('Conversation');
  });
});
