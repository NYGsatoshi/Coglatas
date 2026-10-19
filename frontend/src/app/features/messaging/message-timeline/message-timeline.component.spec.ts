import { TestBed } from '@angular/core/testing';

import { mapMessage } from '../messaging.mapper';
import { MessagingPageStatus } from '../messaging.types';
import { MessageTimelineComponent } from './message-timeline.component';

describe('Message timeline protected states', () => {
  beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => {
    localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it.each([
    { status: 'loading', canRead: false, testId: 'messages-loading-state', text: 'Loading conversation messages' },
    { status: 'sessionExpired', canRead: true, testId: 'messages-session-expired', text: 'Your session has ended' },
    { status: 'permissionDenied', canRead: true, testId: 'body-hidden-state', text: 'Message body unavailable' },
    { status: 'removedParticipant', canRead: true, testId: 'body-hidden-state', text: 'Message body unavailable' },
  ] as const)('shows $status without rendering an obsolete message body', async ({ status, canRead, testId, text }) => {
    await TestBed.configureTestingModule({ imports: [MessageTimelineComponent] }).compileComponents();
    const fixture = TestBed.createComponent(MessageTimelineComponent);
    fixture.componentRef.setInput('status', status satisfies MessagingPageStatus);
    fixture.componentRef.setInput('canReadBody', canRead);
    fixture.componentRef.setInput('messages', [mapMessage({
      id: 'message-a', conversationId: 'conversation-a', authorUserId: 'alice',
      body: 'Obsolete protected body', createdAt: '2026-10-19T00:00:00Z', isDeleted: false,
    }, 'alice')]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector(`[data-testid="${testId}"]`)?.textContent).toContain(text);
    expect(root.textContent).not.toContain('Obsolete protected body');
    expect(root.querySelector('[data-testid="no-messages-state"]')).toBeNull();
  });
});
