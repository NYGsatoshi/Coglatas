import type { MESSAGING_ENGLISH_MESSAGES } from '../../core/i18n/messages/messaging.messages';

export type MessagingTextKey = keyof typeof MESSAGING_ENGLISH_MESSAGES;

export type MessagingRouteKind = 'channel' | 'dm';

export type MessagingPageStatus =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'permissionDenied'
  | 'removedParticipant'
  | 'manualRefreshError'
  | 'sessionExpired';

export type MessagingCapability =
  | 'readBody'
  | 'postMessage'
  | 'createThread'
  | 'manageGroupMembers'
  | 'leaveGroup'
  | 'retryMessage'
  | 'viewOwnReadMarker'
  | 'viewOthersPreciseReadTimestamps';

export type MessageDeliveryState = 'confirmed' | 'sending' | 'failed';

export type MessagingMessageActionMode = 'idle' | 'editing' | 'confirmDelete' | 'confirmReport';

export type MessagingMessageActionPending = 'edit' | 'delete' | 'report' | 'saveForLater';

export interface MessagingMessageActionFeedback {
  readonly id: number;
  readonly message: string;
  readonly messageKey?: MessagingTextKey;
  readonly focusTimeline: boolean;
}

export interface MessagingMessageActionState {
  readonly messageId: string | null;
  readonly mode: MessagingMessageActionMode;
  readonly draft: string;
  readonly pending: MessagingMessageActionPending | null;
  readonly error?: string;
  readonly errorKey?: MessagingTextKey;
  readonly feedback?: MessagingMessageActionFeedback;
}

export type MessageFailureCode = 'network' | 'permissionDenied' | 'sessionExpired' | 'validation';

export type MessageSendState =
  | { readonly status: 'idle' }
  | { readonly status: 'sending'; readonly clientRequestId?: string }
  | { readonly status: 'sent'; readonly messageId: string }
  | {
      readonly status: 'failed';
      readonly message: string;
      readonly messageKey?: MessagingTextKey;
      readonly requestId?: string;
    };

export interface MessagingDraftScope {
  readonly tenantId: string;
  readonly userId: string;
  readonly workspaceId?: string;
  readonly conversationId: string;
}

export interface MessagingPagingWindow {
  readonly beforeMessageId?: string;
  readonly afterMessageId?: string;
  readonly visibleMessageIds: readonly string[];
  readonly preloadBefore: number;
  readonly preloadAfter: number;
}

export interface MessagingConversationListItem {
  readonly id: string;
  readonly kind: MessagingRouteKind;
  readonly workspaceId?: string;
  readonly isGroup?: boolean;
  readonly title: string;
  readonly titleKey?: MessagingTextKey;
  readonly route: string;
  readonly lastActivityLabel: string;
  readonly lastActivityAt?: string;
  readonly safePreviewLabel: string;
  readonly viewerIsParticipant: boolean;
  readonly unreadCount?: number;
  readonly hasMention?: boolean;
  readonly isLater?: boolean;
}

export type MessagingInboxView = 'All' | 'Unread' | 'Mentions' | 'Later';

export interface MessagingInboxCounts {
  readonly all: number;
  readonly unread: number;
  readonly mentions: number;
  readonly later: number;
}

export type MessagingInboxStatus = 'loading' | 'ready' | 'unavailable' | 'error';

export interface MessagingInboxViewModel {
  readonly view: MessagingInboxView;
  readonly requestedView?: MessagingInboxView;
  readonly counts: MessagingInboxCounts;
  readonly status: MessagingInboxStatus;
  readonly laterPendingConversationId?: string;
  readonly error?: string;
  readonly errorKey?: MessagingTextKey;
}

export interface MessagingMentionCandidate {
  readonly userId: string;
  readonly displayName: string;
}

export interface MessagingParticipant {
  readonly userId: string;
  readonly displayName: string;
  readonly role?: 'admin' | 'member' | 'readOnly';
  readonly isCurrentUser: boolean;
}

export interface MessagingGroupMember extends MessagingParticipant {
  readonly membership: 'active' | 'left' | 'removed';
}

export interface MessagingGroupActionState {
  readonly pending: 'add' | 'remove' | 'leave' | null;
  readonly errorKey?: MessagingTextKey;
  readonly withdrawnConversationId?: string;
  readonly focusRequest?: number;
}

export interface MessagingAttachmentPlaceholder {
  readonly mode: 'disabled';
  readonly label: string;
  readonly labelKey?: MessagingTextKey;
}

export interface MessagingMessageReadState {
  readonly ownReadLabel?: string;
  readonly otherReadSummaryLabel?: string;
  readonly otherReadPreciseTimestampLabel?: string;
}

export interface MessagingMessageViewModel {
  readonly id: string;
  readonly clientRequestId?: string;
  readonly authorUserId?: string;
  readonly authorLabel: string;
  readonly authorLabelKey?: MessagingTextKey;
  readonly authorRoleLabel: string;
  readonly authorRoleLabelKey?: MessagingTextKey;
  readonly isOwnMessage: boolean;
  readonly body: string;
  readonly isDeleted: boolean;
  readonly createdAt?: string;
  readonly editedAt?: string;
  readonly version?: number;
  readonly sentAtLabel: string;
  readonly deliveryState: MessageDeliveryState;
  readonly failureCode?: MessageFailureCode;
  readonly safeFailureReason?: string;
  readonly safeFailureReasonKey?: MessagingTextKey;
  readonly retryAllowed: boolean;
  readonly readState?: MessagingMessageReadState;
  readonly mentionedUserIds?: readonly string[];
  readonly threadRootMessageId?: string;
  readonly thread?: MessagingThreadSummaryViewModel;
}

export interface MessagingThreadSummaryViewModel {
  readonly threadRootMessageId: string;
  readonly replyCount: number;
  readonly latestReplyAt?: string;
  readonly participantDisplayNames: readonly string[];
}

export type MessagingThreadStatus = 'closed' | 'loading' | 'ready' | 'permissionDenied' | 'error';

export interface MessagingThreadIdentity extends MessagingDraftScope {
  readonly kind: MessagingRouteKind;
  readonly rootMessageId: string;
}

export interface MessagingThreadTabViewModel {
  readonly identity: MessagingThreadIdentity;
  readonly triggerElementId: string;
  readonly anchorReplyMessageId?: string;
}

export interface MessagingThreadViewModel {
  readonly status: MessagingThreadStatus;
  readonly rootMessageId: string | null;
  readonly rootMessage?: MessagingMessageViewModel;
  readonly replies: readonly MessagingMessageViewModel[];
  readonly summary?: MessagingThreadSummaryViewModel;
  readonly hasMore: boolean;
  readonly maximumReplies: number;
  readonly anchorReplyMessageId?: string;
  readonly draft: string;
  readonly sending: boolean;
  readonly pendingClientRequestId?: string;
  readonly triggerElementId?: string;
  readonly error?: string;
  readonly errorKey?: MessagingTextKey;
}

export interface MessagingConversationViewModel {
  readonly id: string;
  readonly kind: MessagingRouteKind;
  readonly isGroup?: boolean;
  readonly groupMembers?: readonly MessagingGroupMember[];
  readonly tenantId: string;
  readonly workspaceId?: string;
  readonly title: string;
  readonly titleKey?: MessagingTextKey;
  readonly subtitle: string;
  readonly subtitleKey?: MessagingTextKey;
  readonly viewerIsParticipant: boolean;
  readonly viewerWasRemoved: boolean;
  readonly capabilities: readonly MessagingCapability[];
  readonly composerDisabledReason?: string;
  readonly composerDisabledReasonKey?: MessagingTextKey;
  readonly mentionCandidates?: readonly MessagingMentionCandidate[];
  readonly participants?: readonly MessagingParticipant[];
  readonly attachment: MessagingAttachmentPlaceholder;
}

export interface MessagingPageViewModel {
  readonly routeKind: MessagingRouteKind;
  readonly status: MessagingPageStatus;
  readonly title: string;
  readonly titleKey?: MessagingTextKey;
  readonly conversation: MessagingConversationViewModel;
  readonly conversations: readonly MessagingConversationListItem[];
  readonly messages: readonly MessagingMessageViewModel[];
  readonly draft: string;
  readonly sending: boolean;
  readonly sendState: MessageSendState;
  readonly hasNewMessagesWhileReading: boolean;
  readonly inlineError?: string;
  readonly inlineErrorKey?: MessagingTextKey;
  readonly readCursorBehavior: 'latestVisibleMessage' | 'conversationOpenFallback';
  readonly pagingWindow: MessagingPagingWindow;
  readonly mockSendFailure?: MessageFailureCode;
  readonly realtimeDegraded?: boolean;
}
