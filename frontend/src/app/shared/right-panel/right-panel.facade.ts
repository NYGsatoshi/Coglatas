import { HttpClient } from '@angular/common/http';
import { computed, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';

import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import {
  ProtectedStateClearReason,
  RealtimeFacade,
} from '../../core/realtime/realtime.facade';
import { NotificationOpenContextService } from '../../core/notifications/notification-open-context.service';
import { WorkspaceSelectionFacade } from '../../core/workspace/workspace-selection.facade';

import {
  NotificationTargetType,
  RightPanelMember,
  RightPanelMockState,
  RightPanelMode,
  RightPanelNotification,
  RightPanelPermission,
  RightPanelScope,
  RightPanelTab,
  RightPanelViewModel,
} from './right-panel.types';

const RIGHT_PANEL_STORAGE_KEY = 'coglatas.rightPanel.mode';
const SUPPORTED_TARGETS = new Set<NotificationTargetType>([
  'announcement',
  'channelConversation',
  'dmConversation',
  'project',
  'task',
  'taskDeadlineDigest',
  'artifact',
  'message',
]);

export const COGLATAS_RIGHT_PANEL_MOCK = new InjectionToken<RightPanelMockState>('COGLATAS_RIGHT_PANEL_MOCK');

const EMPTY_RIGHT_PANEL_SCOPE: RightPanelScope = {
  workspaceId: '',
  projectId: '',
  conversationId: '',
};

interface PagedResponseDto<T> {
  readonly items?: readonly T[];
}

interface NotificationDto {
  readonly id?: unknown;
  readonly notificationType?: unknown;
  readonly title?: unknown;
  readonly body?: unknown;
  readonly relatedEntityType?: unknown;
  readonly relatedEntityId?: unknown;
  readonly isRead?: unknown;
  readonly targetRoute?: unknown;
  readonly stateVersion?: unknown;
}

interface NotificationOpenDto {
  readonly outcome?: unknown;
  readonly route?: unknown;
  readonly stateVersion?: unknown;
  readonly context?: unknown;
}

export function isSupportedNotificationTarget(target: NotificationTargetType): boolean {
  return SUPPORTED_TARGETS.has(target);
}

/**
 * Task, digest, Artifact, and Message routes are protected resources. Their
 * persisted list route is never navigation authority; the current-authorized
 * server open contract is the only way to obtain a route for them.
 */
export function requiresAuthorizedServerOpen(target: NotificationTargetType): boolean {
  return target === 'task' ||
    target === 'taskDeadlineDigest' ||
    target === 'artifact' ||
    target === 'message';
}

export function mapNotificationRoute(
  target: Pick<RightPanelNotification['target'], 'type' | 'id' | 'route'>,
  scope: RightPanelScope = EMPTY_RIGHT_PANEL_SCOPE,
): string | undefined {
  if (!isSupportedNotificationTarget(target.type) || requiresAuthorizedServerOpen(target.type)) {
    return undefined;
  }

  if (target.route && isKnownSafeRoute(target.route)) {
    return target.route;
  }

  if (!target.id) {
    return undefined;
  }

  switch (target.type) {
    case 'announcement':
      return `/announcements/${target.id}`;
    case 'project':
      return '/projects';
    case 'channelConversation':
      return scope.workspaceId ? `/workspaces/${scope.workspaceId}/channels/${target.id}` : undefined;
    case 'dmConversation':
      return `/dm/${target.id}`;
    default:
      return undefined;
  }
}

export function clampRightPanelText(value: string, maxLength: number): string {
  return value.length > maxLength ? `${value.slice(0, Math.max(0, maxLength - 1))}…` : value;
}

@Injectable({ providedIn: 'root' })
export class RightPanelFacade {
  private readonly http = inject(HttpClient);
  private readonly realtime = inject(RealtimeFacade);
  private readonly router = inject(Router, { optional: true });
  private readonly workspaceSelection = inject(WorkspaceSelectionFacade);
  private readonly notificationOpenContext = inject(NotificationOpenContextService);
  private readonly mockState = inject(COGLATAS_RIGHT_PANEL_MOCK, { optional: true });
  private readonly modeState = signal<RightPanelMode>(
    this.mockState?.mode ?? this.readStoredMode(),
  );
  private readonly selectedTabState = signal<RightPanelTab>(
    this.mockState?.selectedTab ?? 'notifications',
  );
  private readonly permissionState = signal<RightPanelPermission>(
    this.mockState?.permission ?? 'granted',
  );
  private readonly scopeState = signal<RightPanelScope>(
    this.mockState?.activeScope ?? EMPTY_RIGHT_PANEL_SCOPE,
  );
  private readonly notificationState = signal<readonly RightPanelNotification[]>(
    this.normalizeNotifications(this.mockState?.notifications ?? []),
  );
  private readonly memberState = signal<readonly RightPanelMember[]>(this.mockState?.members ?? []);
  private readonly selectedNotificationIdState = signal<string | null>(null);
  private readonly notificationOpenInProgressState = signal(false);
  private readonly unavailableMessageState = signal<string | null>(null);
  private notificationStateVersion = 0;
  private notificationRefreshGeneration = 0;
  private notificationRefreshInFlight: Promise<void> | null = null;
  private notificationRefreshRequest: Subscription | null = null;
  private notificationRefreshQueued = false;
  private notificationRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  private notificationOpenRequest: Subscription | null = null;
  private notificationNavigationGeneration = 0;
  private notificationNavigationContextGeneration: number | null = null;
  private readonly notificationMutationRequests = new Set<Subscription>();
  private readonly realtimeEvents: Subscription;

  readonly mode = this.modeState.asReadonly();
  readonly selectedTab = this.selectedTabState.asReadonly();
  readonly activeScope = this.scopeState.asReadonly();

  readonly viewModel = computed<RightPanelViewModel>(() => {
    const scope = this.scopeState();
    const notifications = this.notificationState().filter((notification) =>
      this.inNotificationScope(notification.scope, scope),
    );
    const members = this.memberState().filter((member) => this.inScope(member.scope, scope));

    return {
      mode: this.modeState(),
      selectedTab: this.selectedTabState(),
      permission: this.permissionState(),
      scope,
      notifications,
      unreadCount: notifications.filter((notification) => !notification.read).length,
      members,
      selectedNotificationId: this.selectedNotificationIdState(),
      notificationOpenInProgress: this.notificationOpenInProgressState(),
      unavailableMessage: this.unavailableMessageState(),
      realtimeDegraded: this.realtime.connectionState() !== 'Connected',
    };
  });

  constructor() {
    this.realtimeEvents = this.realtime.durableEvents$.subscribe((event) => this.applyRealtimeEvent(event));
    this.realtime.registerCatchUp('right-panel-notifications', () => this.refreshNotifications());
    this.realtime.registerProtectedStateClearer?.(
      'right-panel-notifications',
      (reason) => this.clearProtectedState(reason),
    );
    if (!this.mockState) {
      this.loadNotifications();
    }
  }

  setMode(mode: RightPanelMode): void {
    this.modeState.set(mode);
    if (mode === 'expanded' || mode === 'collapsed') {
      this.writeStoredMode(mode);
    }
  }

  togglePanel(): void {
    this.setMode(this.modeState() === 'collapsed' ? 'expanded' : 'collapsed');
  }

  openDrawer(): void {
    this.setMode('drawer');
  }

  closePanel(): void {
    this.setMode('collapsed');
  }

  setSelectedTab(tab: RightPanelTab): void {
    this.selectedTabState.set(tab);
  }

  setPermission(permission: RightPanelPermission): void {
    this.permissionState.set(permission);
  }

  setActiveScope(scope: RightPanelScope): void {
    this.scopeState.set(scope);
  }

  /** HTTP remains authoritative when the realtime transport is degraded. */
  refreshNotificationsNow(): void {
    this.unavailableMessageState.set(null);
    void this.refreshNotifications();
  }

  displayNotificationTarget(notificationId: string): void {
    const navigationGeneration = ++this.notificationNavigationGeneration;
    this.notificationOpenRequest?.unsubscribe();
    this.notificationOpenRequest = null;
    this.notificationOpenInProgressState.set(false);
    const notification = this.notificationState().find((item) => item.id === notificationId);
    if (!notification || !isSupportedNotificationTarget(notification.target.type)) {
      this.showUnavailable();
      return;
    }

    this.selectedNotificationIdState.set(notificationId);
    this.unavailableMessageState.set(null);

    if (!requiresAuthorizedServerOpen(notification.target.type)) {
      this.displayLegacyNotificationTarget(notification);
      return;
    }

    if (this.mockState) {
      // Story/test data intentionally has no server authority. Do not invent
      // a route or optimistic read state for a protected notification.
      return;
    }

    this.notificationOpenInProgressState.set(true);
    const generation = this.notificationRefreshGeneration;
    const request = this.http
      .post<NotificationOpenDto>(`/api/notifications/${notificationId}/open`, {}, { withCredentials: true })
      .subscribe({
        next: (response) => {
          if (generation === this.notificationRefreshGeneration) {
            void this.applyNotificationOpenResult(
              notificationId,
              response,
              generation,
              navigationGeneration,
            );
          }
        },
        error: () => {
          if (generation === this.notificationRefreshGeneration) {this.showUnavailable();}
        },
        complete: () => {
          if (generation === this.notificationRefreshGeneration) {this.notificationOpenInProgressState.set(false);}
        },
      });
    this.notificationOpenRequest = request;
    request.add(() => {
      if (this.notificationOpenRequest === request) {this.notificationOpenRequest = null;}
    });
  }

  markNotificationRead(notificationId: string): void {
    if (this.mockState) {
      this.confirmNotificationRead(notificationId);
      return;
    }

    const generation = this.notificationRefreshGeneration;
    const request = this.http
      .patch(`/api/notifications/${notificationId}/read`, {}, { withCredentials: true })
      .subscribe({
        next: () => {
          if (generation === this.notificationRefreshGeneration) {this.confirmNotificationRead(notificationId);}
        },
        error: () => {
          // Keep unread state unchanged unless the backend confirms persistence.
        },
      });
    this.trackNotificationMutation(request);
  }

  clearPanelState(): void {
    this.removeStoredMode();
    this.modeState.set('collapsed');
    this.selectedTabState.set('notifications');
    this.permissionState.set(this.mockState?.permission ?? 'granted');
    this.selectedNotificationIdState.set(null);
    this.notificationOpenInProgressState.set(false);
    this.unavailableMessageState.set(null);
    this.clearProtectedNotificationState();
    this.scopeState.set(this.mockState?.activeScope ?? EMPTY_RIGHT_PANEL_SCOPE);
    this.memberState.set(this.mockState?.members ?? []);
  }

  private loadNotifications(): void {
    void this.refreshNotifications();
  }

  private toNotification(item: NotificationDto): RightPanelNotification {
    const targetType = notificationTargetType(item.relatedEntityType, item.notificationType);
    const persistedRoute = stringValue(item.targetRoute);
    const target = {
      type: targetType,
      id: stringValue(item.relatedEntityId),
      label: requiresAuthorizedServerOpen(targetType)
        ? (targetType === 'taskDeadlineDigest' ? 'Task deadline digest' : targetType)
        : (persistedRoute ?? targetType),
      route: requiresAuthorizedServerOpen(targetType) ? undefined : persistedRoute,
    };

    return {
      id: stringValue(item.id) ?? '',
      scope: EMPTY_RIGHT_PANEL_SCOPE,
      title: stringValue(item.title) ?? 'Notification',
      body: stringValue(item.body) ?? '',
      target,
      read: item.isRead === true,
      stateVersion: numericValue(item.stateVersion),
    };
  }

  private normalizeNotifications(
    notifications: readonly RightPanelNotification[],
  ): readonly RightPanelNotification[] {
    return notifications.map((notification) => {
      const target = {
        ...notification.target,
        route: mapNotificationRoute(notification.target, notification.scope),
      };

      return {
        ...notification,
        target,
        title: clampRightPanelText(notification.title, 80),
        body: clampRightPanelText(notification.body, 160),
      };
    });
  }

  private confirmNotificationRead(notificationId: string): void {
    this.notificationState.update((notifications) =>
      notifications.map((notification) =>
        notification.id === notificationId ? { ...notification, read: true } : notification,
      ),
    );
  }

  private applyRealtimeEvent(event: DurableRealtimeEvent): void {
    if (event.eventType === 'Notifications.NotificationCreated.v1') {
      this.applyNotificationCreated(event);
      return;
    }

    if (event.eventType === 'Notifications.NotificationReadStateChanged.v1') {
      this.applyNotificationReadState(event);
    }
  }

  private applyNotificationCreated(event: DurableRealtimeEvent): void {
    const payload = event.payload;
    const notification = payload['notification'];
    const stateVersion = numericValue(payload['stateVersion']) ?? numericValue(recordValue(notification)['version']) ?? event.aggregateVersion ?? 0;
    const requiresRefetch = payload['requiresRefetch'] === true;
    if (stateVersion <= this.notificationStateVersion) {
      return;
    }

    // Protected target signals may be reference-only. Never derive display
    // data, a route, or recipient relationship state from such a signal.
    if (requiresRefetch || !notification) {
      this.queueNotificationRefresh();
      return;
    }

    const mapped = this.toNotification(recordValue(notification));
    if (!mapped.id) {
      this.queueNotificationRefresh();
      return;
    }

    const uncertainOrdering = this.notificationStateVersion > 0 && stateVersion > this.notificationStateVersion + 1;
    this.notificationState.update((items) => [
      { ...mapped, stateVersion },
      ...items.filter((item) => item.id !== mapped.id),
    ]);
    this.notificationStateVersion = stateVersion;
    if (uncertainOrdering) {
      this.queueNotificationRefresh();
    }
  }

  private applyNotificationReadState(event: DurableRealtimeEvent): void {
    const payload = event.payload;
    const stateVersion = numericValue(payload['stateVersion']) ?? event.aggregateVersion ?? 0;
    if (stateVersion <= this.notificationStateVersion) {
      return;
    }

    const notificationId = stringValue(payload['notificationId']);
    const change = stringValue(payload['change']);
    const uncertainOrdering = this.notificationStateVersion > 0 && stateVersion > this.notificationStateVersion + 1;
    this.notificationState.update((items) => {
      if (change === 'deleted' && notificationId) {
        return items.filter((item) => item.id !== notificationId);
      }
      if (change === 'allRead') {
        return items.map((item) => ({ ...item, read: true, stateVersion }));
      }
      if (change === 'read' && notificationId) {
        return items.map((item) => item.id === notificationId ? { ...item, read: true, stateVersion } : item);
      }
      return items;
    });
    this.notificationStateVersion = stateVersion;
    if (uncertainOrdering || !change) {
      this.queueNotificationRefresh();
    }
  }

  private clearProtectedNotificationState(): void {
    // An authorization change may race an existing HTTP list request. Ignore
    // that request's response so a pre-revocation protected projection cannot
    // be restored after protected state has been cleared.
    this.notificationRefreshGeneration++;
    this.notificationNavigationGeneration++;
    this.clearNotificationNavigationContext();
    this.notificationRefreshRequest?.unsubscribe();
    this.notificationRefreshRequest = null;
    this.notificationOpenRequest?.unsubscribe();
    this.notificationOpenRequest = null;
    for (const request of [...this.notificationMutationRequests]) {request.unsubscribe();}
    this.notificationMutationRequests.clear();
    if (this.notificationRefreshTimer !== null) {
      clearTimeout(this.notificationRefreshTimer);
      this.notificationRefreshTimer = null;
    }
    this.notificationRefreshQueued = false;
    this.notificationStateVersion = 0;
    this.selectedNotificationIdState.set(null);
    this.notificationOpenInProgressState.set(false);
    this.unavailableMessageState.set(null);
    this.notificationState.set([]);
    if (!this.mockState) {
      this.memberState.set([]);
    }
  }

  private clearProtectedState(reason: ProtectedStateClearReason): void {
    if (reason === 'workspace') {
      // Notifications are Tenant/user scoped. A Workspace switch must cancel
      // only the Workspace-local open/context projection, not erase the
      // global list or its current-authorized refresh state.
      this.notificationOpenRequest?.unsubscribe();
      this.notificationOpenRequest = null;
      this.selectedNotificationIdState.set(null);
      this.notificationOpenInProgressState.set(false);
      this.unavailableMessageState.set(null);
      this.memberState.set([]);
      this.clearNotificationNavigationContext();
      return;
    }

    this.clearProtectedNotificationState();
  }

  private clearNotificationNavigationContext(navigationGeneration?: number): boolean {
    if (
      navigationGeneration !== undefined &&
      this.notificationNavigationContextGeneration !== navigationGeneration
    ) {
      return false;
    }

    this.notificationNavigationContextGeneration = null;
    this.notificationOpenContext.clear();
    this.scopeState.set(EMPTY_RIGHT_PANEL_SCOPE);
    return true;
  }

  private refreshNotifications(): Promise<void> {
    if (this.mockState) {
      return Promise.resolve();
    }
    if (this.notificationRefreshInFlight) {
      this.notificationRefreshQueued = true;
      return this.notificationRefreshInFlight;
    }
    if (this.notificationRefreshTimer !== null) {
      clearTimeout(this.notificationRefreshTimer);
      this.notificationRefreshTimer = null;
    }

    const refreshGeneration = this.notificationRefreshGeneration;
    const refreshPromise = new Promise<void>((resolve) => {
      const request = this.http
        .get<PagedResponseDto<NotificationDto>>('/api/notifications', { withCredentials: true })
        .subscribe({
          next: (response) => {
            if (refreshGeneration !== this.notificationRefreshGeneration) {
              resolve();
              return;
            }
            this.notificationState.set(this.normalizeNotifications((response.items ?? []).map((item) => this.toNotification(item))));
            this.notificationStateVersion = Math.max(...this.notificationState().map((item) => item.stateVersion ?? 0), 0);
            resolve();
          },
          error: (error: { status?: number }) => {
            if (refreshGeneration !== this.notificationRefreshGeneration) {
              resolve();
              return;
            }
            if (error.status === 401 || error.status === 403) {
              this.permissionState.set('denied');
              this.notificationState.set([]);
            }
            resolve();
          },
        });
      this.notificationRefreshRequest = request;
      request.add(() => {
        if (this.notificationRefreshRequest === request) {this.notificationRefreshRequest = null;}
        resolve();
      });
    });
    this.notificationRefreshInFlight = refreshPromise;
    void refreshPromise.finally(() => {
      if (this.notificationRefreshInFlight !== refreshPromise) {return;}
      this.notificationRefreshInFlight = null;
      if (this.notificationRefreshQueued) {
        this.notificationRefreshQueued = false;
        // An event that arrived while this request was active has already
        // been coalesced by the in-flight request. Start exactly one
        // follow-up now rather than adding a second wall-clock debounce.
        void this.refreshNotifications();
      }
    });
    return refreshPromise;
  }

  private queueNotificationRefresh(): void {
    if (this.mockState) {return;}
    if (this.notificationRefreshInFlight) {
      this.notificationRefreshQueued = true;
      return;
    }
    if (this.notificationRefreshTimer !== null) {return;}
    this.notificationRefreshTimer = setTimeout(() => {
      this.notificationRefreshTimer = null;
      void this.refreshNotifications();
    }, 75);
  }

  private async applyNotificationOpenResult(
    notificationId: string,
    response: NotificationOpenDto,
    openGeneration: number,
    navigationGeneration: number,
  ): Promise<void> {
    const notification = this.notificationState().find((item) => item.id === notificationId);
    const outcome = stringValue(response.outcome);
    const route = stringValue(response.route);
    const stateVersion = numericValue(response.stateVersion) ?? 0;
    if (!notification || !requiresAuthorizedServerOpen(notification.target.type) || outcome !== 'Opened' || !route) {
      this.showUnavailable();
      return;
    }

    const contextWorkspaceId = workspaceIdFromOpenContext(response.context);
    let routeIsAuthorized = false;
    let targetWorkspaceId: string | undefined;
    let messageConversationId: string | undefined;

    switch (notification.target.type) {
      case 'task':
        targetWorkspaceId = contextWorkspaceId;
        routeIsAuthorized = !!targetWorkspaceId && isTaskDetailRoute(route);
        break;
      case 'taskDeadlineDigest':
        targetWorkspaceId = contextWorkspaceId;
        routeIsAuthorized = route === '/tasks' && !!targetWorkspaceId;
        break;
      case 'artifact':
        targetWorkspaceId = contextWorkspaceId;
        routeIsAuthorized = !!notification.target.id &&
          !!targetWorkspaceId &&
          isArtifactDetailRoute(route, notification.target.id);
        break;
      case 'message':
        targetWorkspaceId = contextWorkspaceId;
        messageConversationId = notification.target.id
          ? conversationIdFromMessageRoute(route, notification.target.id)
          : undefined;
        routeIsAuthorized = !!targetWorkspaceId && !!messageConversationId;
        break;
    }

    if (!routeIsAuthorized) {
      this.showUnavailable();
      return;
    }

    // Cross-workspace state changes happen only after the backend has both
    // authorized the current target and returned an exact canonical route.
    // Neutralize the old scoped route before activating the target Workspace;
    // a rejected guard leaves the application on that neutral route.
    const isNavigationCurrent = (): boolean =>
      navigationGeneration === this.notificationNavigationGeneration &&
      openGeneration === this.notificationRefreshGeneration;
    if (
      !targetWorkspaceId ||
      !await this.workspaceSelection.selectWorkspace(targetWorkspaceId, isNavigationCurrent)
    ) {
      if (isNavigationCurrent()) {
        this.showUnavailable();
      }
      return;
    }

    if (
      navigationGeneration !== this.notificationNavigationGeneration ||
      openGeneration !== this.notificationRefreshGeneration ||
      this.workspaceSelection.selection().workspaceId !== targetWorkspaceId
    ) {
      return;
    }
    const workspaceTransitionRevision = this.workspaceSelection.transitionRevision();
    this.notificationNavigationContextGeneration = navigationGeneration;

    if (notification.target.type === 'artifact' || notification.target.type === 'message') {
      this.scopeState.set({
        workspaceId: targetWorkspaceId,
        projectId: '',
        conversationId: notification.target.type === 'message' ? (messageConversationId ?? '') : '',
      });
    }

    if (notification.target.type === 'taskDeadlineDigest' && targetWorkspaceId) {
      this.notificationOpenContext.setDigestWorkspace(targetWorkspaceId);
    }
    if (!this.router) {
      this.showUnavailable();
      return;
    }
    let navigated = false;
    try {
      navigated = await this.router.navigateByUrl(route);
    } catch {
      navigated = false;
    }
    if (navigationGeneration !== this.notificationNavigationGeneration) {
      const ownedStagedContext = this.clearNotificationNavigationContext(navigationGeneration);
      if (navigated && ownedStagedContext) {
        try {
          await this.router.navigateByUrl('/workspaces');
        } catch {
          // The stale protected route cannot regain authority when neutral
          // route repair itself is unavailable.
        }
      }
      return;
    }
    const workspaceChangedDuringNavigation =
      this.workspaceSelection.selection().workspaceId !== targetWorkspaceId ||
      this.workspaceSelection.transitionRevision() !== workspaceTransitionRevision;
    if (!navigated || openGeneration !== this.notificationRefreshGeneration || workspaceChangedDuringNavigation) {
      this.clearNotificationNavigationContext(navigationGeneration);
      if (navigated && workspaceChangedDuringNavigation) {
        try {
          await this.router.navigateByUrl('/workspaces');
        } catch {
          // The current Workspace selection still fails closed even when the
          // neutral-route repair is itself unavailable.
        }
      }
      this.showUnavailable();
      this.queueNotificationRefresh();
      return;
    }

    // An open result may update read state only after the server authorized
    // the target and the final client-side navigation succeeded. Older
    // versions never overwrite a newer local projection.
    if (stateVersion > this.notificationStateVersion) {
      this.notificationState.update((items) =>
        items.map((item) => item.id === notificationId ? { ...item, read: true, stateVersion } : item),
      );
      this.notificationStateVersion = stateVersion;
    } else if (stateVersion < this.notificationStateVersion) {
      this.queueNotificationRefresh();
    }
  }

  private displayLegacyNotificationTarget(notification: RightPanelNotification): void {
    const route = mapNotificationRoute(notification.target, notification.scope);
    if (!route || !this.router) {
      this.showUnavailable();
      return;
    }

    // Preserve the legacy contract: navigation uses an already safe Angular
    // route and read state changes only after the existing backend PATCH
    // confirms it. Protected targets never enter this path.
    this.markNotificationRead(notification.id);
    void this.router.navigateByUrl(route).catch(() => this.showUnavailable());
  }

  private showUnavailable(): void {
    this.notificationOpenInProgressState.set(false);
    this.unavailableMessageState.set('This notification target is no longer available.');
  }

  private trackNotificationMutation(request: Subscription): void {
    this.notificationMutationRequests.add(request);
    request.add(() => this.notificationMutationRequests.delete(request));
  }

  private inNotificationScope(recordScope: RightPanelScope, activeScope: RightPanelScope): boolean {
    if (!recordScope.workspaceId && !recordScope.projectId && !recordScope.conversationId) {
      return true;
    }

    return this.inScope(recordScope, activeScope);
  }

  private inScope(recordScope: RightPanelScope, activeScope: RightPanelScope): boolean {
    return (
      recordScope.workspaceId === activeScope.workspaceId &&
      recordScope.projectId === activeScope.projectId &&
      recordScope.conversationId === activeScope.conversationId
    );
  }

  private readStoredMode(): RightPanelMode {
    try {
      const stored = globalThis.sessionStorage?.getItem(RIGHT_PANEL_STORAGE_KEY);
      return stored === 'expanded' ? 'expanded' : 'collapsed';
    } catch {
      return 'collapsed';
    }
  }

  private writeStoredMode(mode: 'collapsed' | 'expanded'): void {
    try {
      globalThis.sessionStorage?.setItem(RIGHT_PANEL_STORAGE_KEY, mode);
    } catch {
      // Storage can be unavailable in hardened browsers or tests.
    }
  }

  private removeStoredMode(): void {
    try {
      globalThis.sessionStorage?.removeItem(RIGHT_PANEL_STORAGE_KEY);
    } catch {
      // Storage can be unavailable in hardened browsers or tests.
    }
  }
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function numericValue(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : undefined;
}

function recordValue(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function notificationTargetType(entityType: unknown, notificationType?: unknown): NotificationTargetType {
  const normalizedEntityType = String(entityType ?? '').trim().toLowerCase();
  if (normalizedEntityType === 'artifact') {
    return 'artifact';
  }
  if (normalizedEntityType === 'message') {
    return 'message';
  }

  const normalized = `${String(entityType ?? '')} ${String(notificationType ?? '')}`.toLowerCase();
  if (normalized.includes('announcement')) {
    return 'announcement';
  }
  if (
    normalized.includes('directmessage') ||
    normalized.includes('dmconversation') ||
    normalized.split(/\s+/).includes('dm')
  ) {
    return 'dmConversation';
  }
  if (normalized.includes('conversation') || normalized.includes('channel')) {
    return 'channelConversation';
  }
  if (normalized.includes('project')) {
    return 'project';
  }
  if (normalized.includes('deadline') || normalized.includes('digest')) {
    return 'taskDeadlineDigest';
  }
  if (normalized.includes('task')) {
    return 'task';
  }
  return 'unsupported';
}

function isTaskDetailRoute(route: string): boolean {
  return /^\/projects\/[^/]+\/tasks\/[^/]+$/.test(route);
}

function isArtifactDetailRoute(route: string, artifactId: string): boolean {
  return route === `/artifacts/${artifactId}`;
}

function conversationIdFromMessageRoute(route: string, messageId: string): string | undefined {
  const match = /^\/conversations\/([^/?#]+)\?messageId=([^&#]+)$/.exec(route);
  if (!match) {
    return undefined;
  }

  try {
    const conversationId = decodeURIComponent(match[1]);
    const routeMessageId = decodeURIComponent(match[2]);
    return conversationId && routeMessageId === messageId ? conversationId : undefined;
  } catch {
    return undefined;
  }
}

function isKnownSafeRoute(route: string): boolean {
  return (
    /^\/announcements\/[^/]+$/.test(route) ||
    route === '/projects' ||
    /^\/workspaces\/[^/]+\/channels\/[^/]+$/.test(route) ||
    /^\/dm\/[^/]+$/.test(route)
  );
}

function workspaceIdFromOpenContext(value: unknown): string | undefined {
  return stringValue(recordValue(value)['workspaceId']);
}
