import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { MessageFollowUpFacade } from './message-follow-up.facade';

describe('MessageFollowUpFacade', () => {
  let facade: MessageFollowUpFacade;
  let httpMock: HttpTestingController;
  let clearProtectedState: (() => void) | undefined;

  beforeEach(() => {
    clearProtectedState = undefined;
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: RealtimeFacade,
        useValue: {
          registerProtectedStateClearer: (_owner: string, clear: () => void) => {
            clearProtectedState = clear;
            return () => { clearProtectedState = undefined; };
          }
        }
      }
    ] });
    facade = TestBed.inject(MessageFollowUpFacade);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('maps the authorized page and completes a row through idempotent DELETE', () => {
    facade.load();
    const list = httpMock.expectOne('/api/me/message-follow-ups?page=1&pageSize=20');
    expect(list.request.withCredentials).toBe(true);
    list.flush({
      page: 1,
      pageSize: 20,
      totalCount: 1,
      items: [{
        messageId: 'message-a',
        conversationId: 'conversation-a',
        workspaceId: 'workspace-a',
        conversationType: 'ProjectChannel',
        conversationTitle: 'Research',
        threadRootMessageId: null,
        authorDisplayName: 'Aiko',
        body: 'Review this finding',
        messageCreatedAt: '2026-08-29T10:00:00Z',
        savedAt: '2026-08-29T11:00:00Z'
      }]
    });

    expect(facade.view().status).toBe('ready');
    expect(facade.view().items[0]?.route).toBe('/workspaces/workspace-a/channels/conversation-a');

    facade.remove('message-a');
    const remove = httpMock.expectOne('/api/me/message-follow-ups/message-a');
    expect(remove.request.method).toBe('DELETE');
    expect(remove.request.withCredentials).toBe(true);
    remove.flush({ messageId: 'message-a', isSaved: false, savedAt: null });
    httpMock.expectOne('/api/me/message-follow-ups?page=1&pageSize=20').flush({
      page: 1,
      pageSize: 20,
      totalCount: 0,
      items: []
    });

    expect(facade.view().status).toBe('empty');
    expect(facade.view().items).toEqual([]);
  });

  it('fails closed when a revoked or malformed projection cannot be validated', () => {
    facade.load();
    httpMock.expectOne('/api/me/message-follow-ups?page=1&pageSize=20').flush({
      page: 1,
      pageSize: 20,
      totalCount: 1,
      items: [{ messageId: 'message-a', body: 'must not remain visible' }]
    });

    expect(facade.view().status).toBe('error');
    expect(facade.view().items).toEqual([]);
  });

  for (const conversationType of ['Group', 7]) {
    it(`opens a saved Group ${conversationType} message in DM instead of Teams`, () => {
      facade.load();
      httpMock.expectOne('/api/me/message-follow-ups?page=1&pageSize=20').flush({
        page: 1, pageSize: 20, totalCount: 1,
        items: [{
          messageId: 'message-a', conversationId: 'group-a', workspaceId: 'workspace-a',
          conversationType, conversationTitle: 'Council', authorDisplayName: 'Aiko',
          body: 'Saved message', messageCreatedAt: '2026-10-19T00:00:00Z', savedAt: '2026-10-19T01:00:00Z',
        }],
      });
      expect(facade.view().items[0]?.route).toBe('/dm/group-a');
    });
  }

  it('cancels pending reads and clears protected rows on a session or Tenant boundary', () => {
    facade.load();
    const request = httpMock.expectOne('/api/me/message-follow-ups?page=1&pageSize=20');

    clearProtectedState?.();

    expect(request.cancelled).toBe(true);
    expect(facade.view().status).toBe('loading');
    expect(facade.view().items).toEqual([]);
    expect(facade.view().totalCount).toBe(0);
  });
});
