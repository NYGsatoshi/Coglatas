export type RealtimeConnectionState = 'Connected' | 'Reconnecting' | 'Degraded' | 'Offline';

export type RealtimeSubscriptionType = 'user' | 'tenant' | 'workspace' | 'conversation' | 'project';

export interface RealtimeSubscriptionRequest {
  readonly subscriptionType: RealtimeSubscriptionType;
  /** Resource IDs are opaque. User and tenant IDs are resolved by the server. */
  readonly resourceId?: string;
}

export interface RealtimeSubscriptionResult {
  readonly allowed: boolean;
  readonly code: string;
}

export interface RealtimeActor {
  readonly actorType: 'User' | 'System';
  readonly actorId: string | null;
}

export interface DurableRealtimeEvent {
  readonly eventId: string;
  readonly eventType: RealtimeEventType;
  readonly payloadSchemaVersion: 1;
  readonly occurredAt: string;
  readonly tenantId: string;
  readonly aggregateType: string;
  readonly aggregateId: string;
  readonly aggregateVersion: number | null;
  readonly actor: RealtimeActor;
  readonly correlationId: string | null;
  readonly causationId: string | null;
  readonly payload: Record<string, unknown>;
}

export type RealtimeEventType =
  | 'Messaging.MessageCreated.v1'
  | 'Messaging.MessageUpdated.v1'
  | 'Messaging.MessageDeleted.v1'
  | 'Messaging.ThreadChanged.v1'
  | 'Messaging.ConversationUnreadChanged.v1'
  | 'Notifications.NotificationCreated.v1'
  | 'Notifications.NotificationReadStateChanged.v1'
  | 'Projects.TaskChanged.v1'
  | 'Projects.TaskAssignmentChanged.v1'
  | 'Projects.TaskWorkflowChanged.v1'
  | 'Projects.TaskCommentChanged.v1'
  | 'Projects.ProjectChanged.v1'
  | 'Announcements.AnnouncementChanged.v1'
  | 'Files.FileChanged.v1'
  | 'Security.AuthorizationStateChanged.v1'
  | 'Feedback.PresentationInvalidated.v1';

export interface RealtimeDiagnostic {
  readonly code:
    | 'ConnectionFailed'
    | 'SubscriptionDenied'
    | 'UnsupportedEvent'
    | 'DuplicateEvent'
    | 'StaleEvent'
    | 'CatchUpFailed';
  readonly eventType?: string;
  readonly eventId?: string;
}

export const REALTIME_EVENT_TYPES: ReadonlySet<string> = new Set([
  'Messaging.MessageCreated.v1',
  'Messaging.MessageUpdated.v1',
  'Messaging.MessageDeleted.v1',
  'Messaging.ThreadChanged.v1',
  'Messaging.ConversationUnreadChanged.v1',
  'Notifications.NotificationCreated.v1',
  'Notifications.NotificationReadStateChanged.v1',
  'Projects.TaskChanged.v1',
  'Projects.TaskAssignmentChanged.v1',
  'Projects.TaskWorkflowChanged.v1',
  'Projects.TaskCommentChanged.v1',
  'Projects.ProjectChanged.v1',
  'Announcements.AnnouncementChanged.v1',
  'Files.FileChanged.v1',
  'Security.AuthorizationStateChanged.v1',
  'Feedback.PresentationInvalidated.v1'
]);
