import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { MessageFollowUpsPageComponent } from './message-follow-ups-page.component';

// These existing contract assertions intentionally select the English UI.
beforeEach(() => { localStorage.setItem('coglatas.locale', 'en'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('MessageFollowUpsPageComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('renders distinct saved-message semantics and an exact focus link', async () => {
    await TestBed.configureTestingModule({
      imports: [MessageFollowUpsPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: RealtimeFacade, useValue: { registerProtectedStateClearer: () => () => undefined } }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(MessageFollowUpsPageComponent);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/me/message-follow-ups?page=1&pageSize=20').flush({
      page: 1,
      pageSize: 20,
      totalCount: 1,
      items: [{
        messageId: 'reply-a',
        conversationId: 'conversation-a',
        workspaceId: 'workspace-a',
        conversationType: 'ProjectChannel',
        conversationTitle: 'Research',
        threadRootMessageId: 'root-a',
        authorDisplayName: 'Aiko',
        body: 'Follow up on this reply',
        messageCreatedAt: '2026-08-29T10:00:00Z',
        savedAt: '2026-08-29T11:00:00Z'
      }]
    });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Saved work is separate from unread status and conversation Later.');
    expect(root.textContent).toContain('Reminders are not scheduled');
    const link = root.querySelector<HTMLAnchorElement>('[data-testid="open-saved-message"]');
    expect(link?.getAttribute('href')).toContain('/workspaces/workspace-a/channels/conversation-a');
    expect(link?.getAttribute('href')).toContain('focusMessageId=reply-a');
    expect(link?.getAttribute('href')).toContain('threadRootMessageId=root-a');
    expect(root.querySelector<HTMLButtonElement>('[data-testid="complete-saved-message"]')?.getAttribute('aria-label'))
      .toBe('Complete saved message from Aiko');
  });
});
