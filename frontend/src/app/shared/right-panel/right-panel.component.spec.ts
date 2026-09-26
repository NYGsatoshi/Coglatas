import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { vi } from 'vitest';

import { NotificationOpenContextService } from '../../core/notifications/notification-open-context.service';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { NotificationItemComponent } from './notification-item/notification-item.component';
import { RightPanelComponent } from './right-panel/right-panel.component';
import { COGLATAS_RIGHT_PANEL_MOCK, mapNotificationRoute, RightPanelFacade } from './right-panel.facade';
import {
  DEFAULT_RIGHT_PANEL_SCOPE,
  OTHER_RIGHT_PANEL_SCOPE,
  RIGHT_PANEL_MEMBERS,
  RIGHT_PANEL_NOTIFICATIONS,
} from './right-panel.mock';
import { RightPanelMember, RightPanelNotification } from './right-panel.types';

const storageKey = 'coglatas.rightPanel.mode';
const rightPanelMockState = {
  mode: 'expanded',
  selectedTab: 'members',
  activeScope: DEFAULT_RIGHT_PANEL_SCOPE,
  members: RIGHT_PANEL_MEMBERS,
  notifications: RIGHT_PANEL_NOTIFICATIONS,
};

describe('RightPanelComponent', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  async function createRightPanel(): Promise<
    ReturnType<typeof TestBed.createComponent<RightPanelComponent>>
  > {
    await TestBed.configureTestingModule({
      imports: [RightPanelComponent],
      providers: [provideRouter([]), { provide: COGLATAS_RIGHT_PANEL_MOCK, useValue: rightPanelMockState }],
    }).compileComponents();

    const fixture = TestBed.createComponent(RightPanelComponent);
    fixture.componentRef.setInput('mode', 'expanded');
    fixture.componentRef.setInput('selectedTab', 'members');
    fixture.componentRef.setInput('activeScope', DEFAULT_RIGHT_PANEL_SCOPE);
    fixture.detectChanges();
    return fixture;
  }

  it('members list changes when active scope changes', async () => {
    const fixture = await createRightPanel();
    let text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('サンプル参加者A');

    fixture.componentInstance.facade.setActiveScope(OTHER_RIGHT_PANEL_SCOPE);
    fixture.detectChanges();

    text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('別スコープ参加者');
    expect(text).not.toContain('サンプル参加者A');
  });

  it('members from another scope are not rendered', async () => {
    const fixture = await createRightPanel();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('サンプル参加者A');
    expect(text).not.toContain('別スコープ参加者');
  });

  it('email is not rendered', async () => {
    const memberWithEmail = {
      ...RIGHT_PANEL_MEMBERS[0],
      email: 'member-a@example.test',
    } as RightPanelMember & { email: string };

    await TestBed.configureTestingModule({
      imports: [RightPanelComponent],
      providers: [
        provideRouter([]),
        {
          provide: COGLATAS_RIGHT_PANEL_MOCK,
          useValue: {
            mode: 'expanded',
            selectedTab: 'members',
            activeScope: DEFAULT_RIGHT_PANEL_SCOPE,
            members: [memberWithEmail],
            notifications: RIGHT_PANEL_NOTIFICATIONS,
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RightPanelComponent);
    fixture.componentRef.setInput('mode', 'expanded');
    fixture.componentRef.setInput('selectedTab', 'members');
    fixture.componentRef.setInput('activeScope', DEFAULT_RIGHT_PANEL_SCOPE);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('サンプル参加者A');
    expect(text).not.toContain('member-a@example.test');
    expect(text).not.toContain('@example.test');
  });

  it('unsupported notification targets are not clickable', async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationItemComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotificationItemComponent);
    const unsupported = RIGHT_PANEL_NOTIFICATIONS.find(
      (notification) => notification.id === 'notification-unsupported',
    ) as RightPanelNotification;
    fixture.componentRef.setInput('notification', unsupported);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('a')).toBeNull();
    expect(element.querySelector('[data-testid="notification-target-unavailable"]')?.textContent).toContain('未対応');
  });

  it('notification body is not rendered as HTML', async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationItemComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotificationItemComponent);
    const htmlBody = RIGHT_PANEL_NOTIFICATIONS.find(
      (notification) => notification.id === 'notification-html-body',
    ) as RightPanelNotification;
    fixture.componentRef.setInput('notification', htmlBody);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('strong')).toBeNull();
    expect(element.textContent).toContain('<strong>強調タグ</strong>');
  });

  it('panel collapsed state stored in sessionStorage', async () => {
    await TestBed.configureTestingModule({
      imports: [RightPanelComponent],
      providers: [provideRouter([]), { provide: COGLATAS_RIGHT_PANEL_MOCK, useValue: rightPanelMockState }],
    }).compileComponents();

    const facade = TestBed.inject(RightPanelFacade);
    facade.setMode('expanded');
    expect(sessionStorage.getItem(storageKey)).toBe('expanded');

    facade.setMode('collapsed');
    expect(sessionStorage.getItem(storageKey)).toBe('collapsed');
  });

  it('session clear removes panel state', async () => {
    await TestBed.configureTestingModule({
      imports: [RightPanelComponent],
      providers: [provideRouter([]), { provide: COGLATAS_RIGHT_PANEL_MOCK, useValue: rightPanelMockState }],
    }).compileComponents();

    const facade = TestBed.inject(RightPanelFacade);
    facade.setMode('expanded');
    facade.setSelectedTab('members');
    facade.clearPanelState();

    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(facade.viewModel().mode).toBe('collapsed');
    expect(facade.viewModel().selectedTab).toBe('notifications');
  });

  it('mobile drawer does not expose hidden actions while collapsed', async () => {
    const fixture = await createRightPanel();
    fixture.componentRef.setInput('mode', 'collapsed');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="right-panel-open"]')).toBeTruthy();
    expect(element.querySelector('a')).toBeNull();
    expect(element.querySelector('.notification__mark')).toBeNull();
    expect(element.textContent).not.toContain('対象を表示');
  });
});

describe('RightPanelFacade notifications', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('maps only known valid notification targets to Angular routes', () => {
    expect(mapNotificationRoute({ type: 'announcement', id: 'announcement-1' })).toBe(
      '/announcements/announcement-1',
    );
    expect(
      mapNotificationRoute(
        { type: 'channelConversation', id: 'conversation-1' },
        { workspaceId: 'workspace-1', projectId: '', conversationId: '' },
      ),
    ).toBe('/workspaces/workspace-1/channels/conversation-1');
    expect(mapNotificationRoute({ type: 'dmConversation', id: 'conversation-2' })).toBe(
      '/dm/conversation-2',
    );
    expect(
      mapNotificationRoute({ type: 'task', id: 'task-1', route: '/projects/stale/tasks/task-1' }),
    ).toBeUndefined();
    expect(
      mapNotificationRoute({ type: 'taskDeadlineDigest', id: 'digest-1', route: '/tasks' }),
    ).toBeUndefined();
    expect(mapNotificationRoute({ type: 'unsupported', id: 'legacy-1' })).toBeUndefined();
  });

  it('AnnouncementNotificationStillUsesLegacyMappedRoute', () => {
    configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    httpMock.expectOne('/api/notifications').flush({
      items: [legacyNotificationDto('Announcement', 'announcement-1', '/announcements/announcement-1')],
    });

    facade.displayNotificationTarget('notification-1');

    httpMock.expectNone('/api/notifications/notification-1/open');
    expect(facade.viewModel().notifications[0].read).toBe(false);
    httpMock.expectOne('/api/notifications/notification-1/read').flush({});
    expect(navigate).toHaveBeenCalledWith('/announcements/announcement-1');
    expect(facade.viewModel().notifications[0].read).toBe(true);
    httpMock.verify();
  });

  it('ProjectNotificationStillUsesLegacyMappedRoute', () => {
    configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    httpMock.expectOne('/api/notifications').flush({
      items: [legacyNotificationDto('Project', 'project-1', '/projects')],
    });

    facade.displayNotificationTarget('notification-1');

    httpMock.expectNone('/api/notifications/notification-1/open');
    httpMock.expectOne('/api/notifications/notification-1/read').flush({});
    expect(navigate).toHaveBeenCalledWith('/projects');
    expect(facade.viewModel().notifications[0].read).toBe(true);
    httpMock.verify();
  });

  it('ConversationNotificationStillUsesLegacyMappedRoute', () => {
    configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const route = '/workspaces/workspace-1/channels/conversation-1';
    httpMock.expectOne('/api/notifications').flush({
      items: [legacyNotificationDto('ChannelConversation', 'conversation-1', route)],
    });

    facade.displayNotificationTarget('notification-1');

    httpMock.expectNone('/api/notifications/notification-1/open');
    httpMock.expectOne('/api/notifications/notification-1/read').flush({});
    expect(navigate).toHaveBeenCalledWith(route);
    expect(facade.viewModel().notifications[0].read).toBe(true);
    httpMock.verify();
  });

  it('does not hide API notifications solely because local scope is empty', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/notifications').flush({
      items: [
        {
          id: 'notification-1',
          title: 'Announcement',
          body: 'Body',
          relatedEntityType: 'Announcement',
          relatedEntityId: 'announcement-1',
          isRead: false,
        },
      ],
    });

    facade.setActiveScope(DEFAULT_RIGHT_PANEL_SCOPE);

    expect(facade.viewModel().notifications.map((notification) => notification.id)).toEqual([
      'notification-1',
    ]);
    expect(facade.viewModel().unreadCount).toBe(1);
    httpMock.verify();
  });

  it('does not update unread count when mark-read fails', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/notifications').flush({
      items: [
        {
          id: 'notification-1',
          title: 'Announcement',
          relatedEntityType: 'Announcement',
          relatedEntityId: 'announcement-1',
          isRead: false,
        },
      ],
    });

    expect(facade.viewModel().unreadCount).toBe(1);

    facade.markNotificationRead('notification-1');
    httpMock
      .expectOne('/api/notifications/notification-1/read')
      .flush({ error: 'Failed' }, { status: 500, statusText: 'Server Error' });

    expect(facade.viewModel().unreadCount).toBe(1);
    httpMock.verify();
  });

  it('ReferenceOnlyNotificationCreatedDoesNotRequireEmbeddedNotificationAndRefetchesExactlyOnce', async () => {
    const { events } = configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(5)] });
    await flushNotificationPromises();

    vi.useFakeTimers();
    events.next(referenceOnlyNotificationEvent(6));
    events.next(referenceOnlyNotificationEvent(6, '70000000-0000-4000-8000-000000000002'));
    await vi.advanceTimersByTimeAsync(74);
    httpMock.expectNone('/api/notifications');
    await vi.advanceTimersByTimeAsync(1);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(6)] });
    await flushNotificationPromises();

    httpMock.expectNone('/api/notifications');
    expect(facade.viewModel().notifications).toHaveLength(1);
    expect(facade.viewModel().notifications[0].stateVersion).toBe(6);
    httpMock.verify();
  });

  it('DuplicateNotificationEventAndHttpResponseCreateOneVisibleItem', async () => {
    const { events } = configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const initial = httpMock.expectOne('/api/notifications');

    vi.useFakeTimers();
    events.next(referenceOnlyNotificationEvent(6));
    initial.flush({ items: [notificationDto(5)] });
    await flushNotificationPromises();

    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(6)] });
    await flushNotificationPromises();

    expect(facade.viewModel().notifications).toHaveLength(1);
    expect(facade.viewModel().notifications[0]).toEqual(expect.objectContaining({
      id: 'notification-1',
      stateVersion: 6,
    }));
    httpMock.expectNone('/api/notifications');
    httpMock.verify();
  });

  it('AuthorizationInvalidationDoesNotRestoreProtectedStateFromAnInFlightList', async () => {
    const { clearers } = configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const initial = httpMock.expectOne('/api/notifications');

    expect(clearers).toHaveLength(1);
    clearers[0]!();
    expect(initial.cancelled).toBe(true);
    await flushNotificationPromises();

    expect(facade.viewModel().notifications).toEqual([]);
    httpMock.expectNone('/api/notifications');
    httpMock.verify();
  });

  it('StaleNotificationStateVersionIsIgnored', async () => {
    const { events } = configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(6)] });
    await flushNotificationPromises();

    vi.useFakeTimers();
    events.next(referenceOnlyNotificationEvent(6));
    await vi.advanceTimersByTimeAsync(75);

    httpMock.expectNone('/api/notifications');
    expect(facade.viewModel().notifications).toHaveLength(1);
    httpMock.verify();
  });

  it('NotificationVersionGapTriggersCoalescedRefetch', async () => {
    const { events } = configureLiveRightPanel();
    TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(1)] });
    await flushNotificationPromises();

    vi.useFakeTimers();
    events.next(referenceOnlyNotificationEvent(3));
    events.next(referenceOnlyNotificationEvent(4, '70000000-0000-4000-8000-000000000003'));
    await vi.advanceTimersByTimeAsync(75);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(4)] });
    await flushNotificationPromises();

    httpMock.expectNone('/api/notifications');
    httpMock.verify();
  });

  it('RapidReferenceOnlyEventsProduceOneRefetch', async () => {
    const { events } = configureLiveRightPanel();
    TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(1)] });
    await flushNotificationPromises();

    vi.useFakeTimers();
    events.next(referenceOnlyNotificationEvent(2));
    events.next(referenceOnlyNotificationEvent(2, '70000000-0000-4000-8000-000000000004'));
    events.next(referenceOnlyNotificationEvent(2, '70000000-0000-4000-8000-000000000005'));
    await vi.advanceTimersByTimeAsync(75);

    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(2)] });
    await flushNotificationPromises();
    httpMock.expectNone('/api/notifications');
    httpMock.verify();
  });

  it('EventDuringInFlightRequestProducesExactlyOneFollowUpRefetch', async () => {
    const { events } = configureLiveRightPanel();
    TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const initial = httpMock.expectOne('/api/notifications');

    vi.useFakeTimers();
    events.next(referenceOnlyNotificationEvent(6));
    events.next(referenceOnlyNotificationEvent(7, '70000000-0000-4000-8000-000000000006'));
    initial.flush({ items: [notificationDto(5)] });
    await flushNotificationPromises();

    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(7)] });
    await flushNotificationPromises();
    httpMock.expectNone('/api/notifications');
    httpMock.verify();
  });

  it('NoPendingNotificationTimerAfterProtectedStateClear', async () => {
    const { clearers, events } = configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(1)] });
    await flushNotificationPromises();

    vi.useFakeTimers();
    events.next(referenceOnlyNotificationEvent(2));
    clearers[0]!();
    await vi.advanceTimersByTimeAsync(75);

    expect(facade.viewModel().notifications).toEqual([]);
    httpMock.expectNone('/api/notifications');
    httpMock.verify();
  });

  it('TaskNotificationRequiresAuthorizedOpenEndpoint', async () => {
    configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(5)] });

    facade.displayNotificationTarget('notification-1');
    httpMock.expectNone('/api/notifications/notification-1/read');
    httpMock.expectOne('/api/notifications/notification-1/open').flush({
      outcome: 'Opened',
      route: '/projects/project-1/tasks/task-1',
      stateVersion: 6,
      context: { workspaceId: 'workspace-1' },
    });
    await flushNotificationPromises();

    expect(navigate).toHaveBeenCalledWith('/projects/project-1/tasks/task-1');
    expect(facade.viewModel().notifications[0].read).toBe(true);
    httpMock.verify();
  });

  it('UnavailableTaskOpenDoesNotFallbackToPersistedRoute', () => {
    configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    httpMock.expectOne('/api/notifications').flush({ items: [notificationDto(5)] });

    facade.displayNotificationTarget('notification-1');
    expect(facade.viewModel().notifications[0].read).toBe(false);
    httpMock.expectNone('/api/notifications/notification-1/read');
    httpMock.expectOne('/api/notifications/notification-1/open').flush({
      outcome: 'Unavailable',
      route: null,
      stateVersion: 5,
    });

    expect(navigate).not.toHaveBeenCalled();
    expect(facade.viewModel().notifications[0].read).toBe(false);
    expect(facade.viewModel().unavailableMessage).toContain('no longer available');
    httpMock.verify();
  });

  it('DigestNotificationRequiresAuthorizedOpenEndpoint', async () => {
    configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const context = TestBed.inject(NotificationOpenContextService);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    httpMock.expectOne('/api/notifications').flush({ items: [digestNotificationDto(5)] });

    facade.displayNotificationTarget('notification-1');
    httpMock.expectNone('/api/notifications/notification-1/read');
    httpMock.expectOne('/api/notifications/notification-1/open').flush({
      outcome: 'Opened',
      route: '/tasks',
      stateVersion: 6,
      context: { workspaceId: 'workspace-1' },
    });
    await flushNotificationPromises();

    expect(context.takeDigestWorkspace()).toBe('workspace-1');
    expect(navigate).toHaveBeenCalledWith('/tasks');
    httpMock.verify();
  });

  it('UnsupportedNotificationDoesNotNavigate', () => {
    configureLiveRightPanel();
    const facade = TestBed.inject(RightPanelFacade);
    const httpMock = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    httpMock.expectOne('/api/notifications').flush({
      items: [legacyNotificationDto('UnknownRestrictedTarget', 'unsupported-1', '/announcements/unsupported-1')],
    });

    facade.displayNotificationTarget('notification-1');

    httpMock.expectNone('/api/notifications/notification-1/open');
    httpMock.expectNone('/api/notifications/notification-1/read');
    expect(navigate).not.toHaveBeenCalled();
    expect(facade.viewModel().unavailableMessage).toContain('no longer available');
    httpMock.verify();
  });
});

function configureLiveRightPanel(): {
  readonly events: Subject<DurableRealtimeEvent>;
  readonly clearers: Array<() => void>;
} {
  const events = new Subject<DurableRealtimeEvent>();
  const clearers: Array<() => void> = [];
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: RealtimeFacade,
        useValue: {
          durableEvents$: events.asObservable(),
          connectionState: () => 'Connected',
          registerCatchUp: () => undefined,
          registerProtectedStateClearer: (_owner: string, clear: () => void) => {
            clearers.push(clear);
            return () => undefined;
          },
        },
      },
      {
        provide: WorkspaceSelectionFacade,
        useValue: {
          selectWorkspace: vi.fn().mockResolvedValue(true),
          selection: () => ({ status: 'selected', workspaceId: 'workspace-1', source: 'explicit' }),
          transitionRevision: () => 0,
        },
      },
    ],
  });
  return { events, clearers };
}

function notificationDto(stateVersion: number): Record<string, unknown> {
  return {
    id: 'notification-1',
    title: 'Task notification',
    body: 'safe API list body',
    relatedEntityType: 'TaskItem',
    relatedEntityId: 'task-1',
    targetRoute: '/projects/stale-project/tasks/stale-task',
    isRead: false,
    stateVersion,
  };
}

function legacyNotificationDto(
  relatedEntityType: string,
  relatedEntityId: string,
  targetRoute: string,
): Record<string, unknown> {
  return {
    id: 'notification-1',
    title: 'Legacy notification',
    body: 'safe API list body',
    relatedEntityType,
    relatedEntityId,
    targetRoute,
    isRead: false,
    stateVersion: 5,
  };
}

function digestNotificationDto(stateVersion: number): Record<string, unknown> {
  return {
    ...notificationDto(stateVersion),
    relatedEntityType: 'TaskDeadlineDigest',
    targetRoute: null,
  };
}

function referenceOnlyNotificationEvent(stateVersion: number, eventId = '70000000-0000-4000-8000-000000000001'): DurableRealtimeEvent {
  return {
    eventId,
    eventType: 'Notifications.NotificationCreated.v1',
    payloadSchemaVersion: 1,
    occurredAt: '2026-08-06T00:00:00.000Z',
    tenantId: '11111111-1111-1111-1111-111111111111',
    aggregateType: 'Notification',
    aggregateId: 'notification-1',
    aggregateVersion: stateVersion,
    actor: { actorType: 'System', actorId: null },
    correlationId: null,
    causationId: null,
    payload: {
      notificationId: 'notification-1',
      stateVersion,
      requiresRefetch: true,
    },
  };
}

async function flushNotificationPromises(): Promise<void> {
  // Resolves the in-flight refresh promise/finalizer without relying on wall
  // clock scheduling. Debounced paths advance Vitest's fake clock explicitly.
  await Promise.resolve();
  await Promise.resolve();
}
