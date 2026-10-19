import {
  ConversationDetailDto,
  ConversationDto,
  MessageDto,
  MessageThreadDto,
  MessageThreadSummaryDto
} from './messaging.api';
import {
  MessagingCapability,
  MessagingTextKey,
  MessagingConversationListItem,
  MessagingMentionCandidate,
  MessagingMessageViewModel,
  MessagingPageStatus,
  MessagingPageViewModel,
  MessagingRouteKind,
  MessagingThreadSummaryViewModel,
  MessagingThreadViewModel
} from './messaging.types';

export interface MessagingMapperContext {
  readonly currentUserId: string;
  readonly currentTenantId: string;
  readonly fallbackRouteKind: MessagingRouteKind;
  readonly existingConversations?: readonly MessagingConversationListItem[];
}

export function mapConversationPage(
  conversation: ConversationDetailDto,
  messages: readonly MessageDto[],
  context: MessagingMapperContext
): MessagingPageViewModel {
  const conversationId = stringValue(conversation.id) ?? '';
  const workspaceId = stringValue(conversation.workspaceId);
  const routeKind = routeKindFromApi(conversation.type) ?? context.fallbackRouteKind;
  const isGroup = isGroupConversation(conversation.type);
  const viewer = (conversation.members ?? []).find(
    (member) => stringValue(member.userId) === context.currentUserId
  );
  const viewerWasRemoved = viewer?.removedAt !== null && viewer?.removedAt !== undefined;
  const viewerLeft = viewer?.leftAt !== null && viewer?.leftAt !== undefined;
  const viewerIsParticipant = viewer !== undefined && !viewerWasRemoved && !viewerLeft;
  const canRead = viewerIsParticipant && viewer?.canRead === true;
  // Group is a persisted compatibility type, but the current backend rejects
  // its send/create commands. Do not present a composer that cannot succeed.
  const canPost = !isGroup && canRead && viewer?.canPost === true && conversation.isLocked !== true && conversation.isArchived !== true;
  const capabilities: MessagingCapability[] = [];

  if (canRead) {
    capabilities.push('readBody', 'viewOwnReadMarker');
  }

  if (canPost) {
    capabilities.push('postMessage');
  }

  if (!isGroup && canRead && viewer?.canCreateThread === true && conversation.isLocked !== true && conversation.isArchived !== true) {
    capabilities.push('createThread');
  }

  const status: MessagingPageStatus = viewerWasRemoved || viewerLeft
    ? 'removedParticipant'
    : !viewerIsParticipant || !canRead
      ? 'permissionDenied'
      : messages.length === 0
        ? 'empty'
        : 'ready';

  return {
    routeKind,
    status,
    title: titleFor(conversation, routeKind, context.currentUserId),
    titleKey: titleKeyFor(conversation, routeKind, context.currentUserId),
    conversation: {
      id: conversationId,
      kind: routeKind,
      isGroup,
      tenantId: context.currentTenantId,
      workspaceId,
      title: titleFor(conversation, routeKind, context.currentUserId),
    titleKey: titleKeyFor(conversation, routeKind, context.currentUserId),
      subtitle: 'Live API data',
      subtitleKey: 'messaging.liveData',
      viewerIsParticipant,
      viewerWasRemoved: viewerWasRemoved || viewerLeft,
      capabilities,
      composerDisabledReason: canPost
        ? undefined
        : isGroup ? 'Sending is unavailable for group conversations in this pilot.' : 'Posting is not available for this conversation.',
      composerDisabledReasonKey: canPost ? undefined : isGroup ? 'messaging.group.readOnly' : 'messaging.postingUnavailable',
      mentionCandidates: mentionCandidatesFor(conversation, context.currentUserId),
      attachment: {
        mode: 'disabled',
        label: 'Attachments are disabled for MVP0 messaging.',
        labelKey: 'messaging.attachmentsDisabled'
      }
    },
    conversations: context.existingConversations ?? [],
    messages: messages.map((message) => mapMessage(message, context.currentUserId)),
    draft: '',
    sending: false,
    sendState: { status: 'idle' },
    hasNewMessagesWhileReading: false,
    readCursorBehavior: 'latestVisibleMessage',
    pagingWindow: {
      visibleMessageIds: messages.map((message) => stringValue(message.id) ?? ''),
      preloadBefore: 0,
      preloadAfter: 0
    }
  };
}

export function mapConversationListItem(
  conversation: ConversationDto
): MessagingConversationListItem {
  const id = stringValue(conversation.id) ?? '';
  const kind = routeKindFromApi(conversation.type) ?? 'channel';
  const workspaceId = stringValue(conversation.workspaceId);

  return {
    id,
    kind,
    workspaceId,
    isGroup: isGroupConversation(conversation.type),
    title: titleFor(conversation, kind),
    titleKey: titleKeyFor(conversation, kind),
    route: kind === 'dm' ? `/dm/${id}` : `/workspaces/${workspaceId ?? ''}/channels/${id}`,
    lastActivityLabel: formatDate(conversation.updatedAt) || formatDate(conversation.createdAt),
    lastActivityAt: stringValue(conversation.updatedAt) ?? stringValue(conversation.createdAt),
    safePreviewLabel: stringValue(conversation.lastMessage?.body) ?? '',
    viewerIsParticipant: true,
    unreadCount: numberValue(conversation.unreadCount),
    hasMention: booleanValue(conversation.hasMention),
    isLater: booleanValue(conversation.isLater)
  };
}

export function mapMessage(message: MessageDto, currentUserId: string): MessagingMessageViewModel {
  return {
    id: stringValue(message.id) ?? `message-${Date.now()}`,
    authorUserId: stringValue(message.authorUserId),
    authorLabel: stringValue(message.authorDisplayName) ?? 'Unknown user',
    authorLabelKey: stringValue(message.authorDisplayName) ? undefined : 'messaging.unknownUser',
    authorRoleLabel: 'member',
    authorRoleLabelKey: 'messaging.member',
    isOwnMessage: stringValue(message.authorUserId) === currentUserId,
    body: message.isDeleted === true ? '' : (stringValue(message.body) ?? ''),
    isDeleted: message.isDeleted === true,
    createdAt: stringValue(message.createdAt),
    editedAt: stringValue(message.editedAt),
    version: numberValue(message.version),
    clientRequestId: stringValue(message.clientRequestId),
    sentAtLabel: formatDate(message.createdAt),
    deliveryState: 'confirmed',
    retryAllowed: false,
    threadRootMessageId: stringValue(message.threadRootMessageId),
    thread: mapThreadSummary(message.thread)
  };
}

export function mapThreadSummary(
  summary: MessageThreadSummaryDto | null | undefined
): MessagingThreadSummaryViewModel | undefined {
  const threadRootMessageId = strictIdentity(summary?.threadRootMessageId);
  const replyCount = nonNegativeInteger(summary?.replyCount);
  if (!threadRootMessageId || replyCount === undefined) {
    return undefined;
  }
  return {
    threadRootMessageId,
    replyCount,
    latestReplyAt: stringValue(summary?.latestReplyAt),
    participantDisplayNames: (summary?.participantDisplayNames ?? [])
      .filter((value): value is string => typeof value === 'string' && value.length > 0)
      .slice(0, 3)
  };
}

export function mapMessageThread(
  value: MessageThreadDto,
  currentUserId: string,
  triggerElementId?: string
): MessagingThreadViewModel | null {
  if (!value.rootMessage || !value.summary || !Array.isArray(value.replies)) {
    return null;
  }
  const rootMessageId = strictIdentity(value.rootMessage.id);
  const rootConversationId = strictIdentity(value.rootMessage.conversationId);
  const maximumReplies = positiveInteger(value.maximumReplies);
  if (!rootMessageId || !rootConversationId || maximumReplies === undefined) {
    return null;
  }
  const rootMessage = mapMessage(value.rootMessage, currentUserId);
  const summary = mapThreadSummary(value.summary);
  if (
    !summary ||
    rootMessage.threadRootMessageId ||
    summary.threadRootMessageId !== rootMessageId ||
    typeof value.hasMore !== 'boolean'
  ) {
    return null;
  }
  const replyIds = value.replies.map((reply) => strictIdentity(reply.id));
  if (
    replyIds.some((replyId) => !replyId) ||
    new Set(replyIds).size !== replyIds.length ||
    value.replies.some((reply) =>
      strictIdentity(reply.threadRootMessageId) !== rootMessageId ||
      strictIdentity(reply.conversationId) !== rootConversationId
    )
  ) {
    return null;
  }
  if (
    value.replies.length > maximumReplies ||
    summary.replyCount < value.replies.length ||
    (value.hasMore ? summary.replyCount <= value.replies.length : summary.replyCount !== value.replies.length)
  ) {
    return null;
  }
  const replies = value.replies.map((reply) => mapMessage(reply, currentUserId));
  return {
    status: 'ready',
    rootMessageId,
    rootMessage,
    replies,
    summary,
    hasMore: value.hasMore,
    maximumReplies,
    draft: '',
    sending: false,
    triggerElementId
  };
}

export function routeKindFromApi(value: unknown): MessagingRouteKind | null {
  if (value === 0 || isGroupConversation(value)) {
    return 'dm';
  }

  const normalized = String(value ?? '').toLowerCase();
  if (!normalized) {
    return null;
  }

  return normalized === 'directmessage' ? 'dm' : 'channel';
}

export function isGroupConversation(value: unknown): boolean {
  return value === 7 || value === 'Group';
}

function titleFor(conversation: ConversationDto, routeKind: MessagingRouteKind, currentUserId?: string): string {
  const title = stringValue(conversation.title);
  if (title) {
    return title;
  }

  if (isGroupConversation(conversation.type)) {
    return 'Group DM';
  }

  const members = (conversation as { members?: readonly { userId?: unknown; displayName?: unknown }[] }).members ?? [];
  if (routeKind === 'dm' && members.length > 0) {
    const recipient = members.find((member) => stringValue(member.userId) !== currentUserId);
    const displayName = stringValue(recipient?.displayName);
    if (displayName) {
      return displayName;
    }
  }

  return routeKind === 'dm' ? 'Direct message' : 'Conversation';
}

function titleKeyFor(conversation: ConversationDto, routeKind: MessagingRouteKind, currentUserId?: string): MessagingTextKey | undefined {
  if (stringValue(conversation.title)) {
    return undefined;
  }
  if (isGroupConversation(conversation.type)) {
    return 'messaging.group.title';
  }
  const members = (conversation as ConversationDetailDto).members ?? [];
  if (routeKind === 'dm' && stringValue(members.find((member) => stringValue(member.userId) !== currentUserId)?.displayName)) {
    return undefined;
  }
  return routeKind === 'dm' ? 'messaging.directMessage' : 'messaging.conversation';
}

function mentionCandidatesFor(
  conversation: ConversationDetailDto,
  currentUserId: string
): readonly MessagingMentionCandidate[] {
  return (conversation.members ?? []).flatMap((member) => {
    const userId = stringValue(member.userId);
    const displayName = stringValue(member.displayName);
    const active = member.leftAt === null || member.leftAt === undefined;
    const notRemoved = member.removedAt === null || member.removedAt === undefined;
    if (!userId || !displayName || userId === currentUserId || member.canRead !== true || !active || !notRemoved) {
      return [];
    }
    return [{ userId, displayName }];
  });
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function numberValue(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

function nonNegativeInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
    ? value
    : undefined;
}

function positiveInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0
    ? value
    : undefined;
}

function strictIdentity(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

function booleanValue(value: unknown): boolean | undefined {
  return typeof value === 'boolean' ? value : undefined;
}

function formatDate(value: unknown): string {
  const raw = stringValue(value);
  return raw ? new Date(raw).toLocaleString() : '';
}
