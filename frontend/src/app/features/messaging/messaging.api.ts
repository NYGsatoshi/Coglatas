import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface PagedResponseDto<T> {
  readonly items?: readonly T[];
}

export type ConversationInboxViewDto = 'All' | 'Unread' | 'Mentions' | 'Later';

export interface ConversationInboxCountsDto {
  readonly all?: unknown;
  readonly unread?: unknown;
  readonly mentions?: unknown;
  readonly later?: unknown;
}

export interface ConversationInboxResponseDto extends PagedResponseDto<ConversationDto> {
  readonly page?: unknown;
  readonly pageSize?: unknown;
  readonly totalCount?: unknown;
  readonly view?: unknown;
  readonly counts?: ConversationInboxCountsDto | null;
}

export type ConversationTypeDto =
  | 'DirectMessage'
  | 'ProjectChannel'
  | 'WorkspaceChannel'
  | 'Thread'
  | 'CommitteeChannel'
  | 'AnnouncementThread'
  | 'ExternalSharedChannel'
  | 'LegalHoldConversation'
  | 'Group'
  | 'ProjectLinked'
  | 'System'
  | number;

export interface ConversationDto {
  readonly id?: unknown;
  readonly workspaceId?: unknown;
  readonly projectId?: unknown;
  readonly type?: ConversationTypeDto | unknown;
  readonly title?: unknown;
  readonly parentConversationId?: unknown;
  readonly rootConversationId?: unknown;
  readonly lastMessage?: MessageDto | null;
  readonly unreadCount?: unknown;
  readonly hasMention?: unknown;
  readonly isMuted?: unknown;
  readonly isArchived?: unknown;
  readonly isLater?: unknown;
  readonly isLocked?: unknown;
  readonly updatedAt?: unknown;
  readonly createdAt?: unknown;
}

export interface ConversationDetailDto extends ConversationDto {
  readonly members?: readonly ConversationMemberDto[];
}

export interface ConversationMemberDto {
  readonly userId?: unknown;
  readonly displayName?: unknown;
  readonly email?: unknown;
  readonly role?: unknown;
  readonly canRead?: unknown;
  readonly canPost?: unknown;
  readonly canManageMembers?: unknown;
  readonly canCreateThread?: unknown;
  readonly leftAt?: unknown;
  readonly removedAt?: unknown;
}

export interface AttachmentDto {
  readonly id?: unknown;
  readonly fileName?: unknown;
  readonly contentType?: unknown;
  readonly fileSize?: unknown;
}

export interface MessageDto {
  readonly id?: unknown;
  readonly workspaceId?: unknown;
  readonly conversationId?: unknown;
  readonly authorUserId?: unknown;
  readonly authorDisplayName?: unknown;
  readonly body?: unknown;
  readonly attachments?: readonly AttachmentDto[];
  readonly createdAt?: unknown;
  readonly updatedAt?: unknown;
  readonly editedAt?: unknown;
  readonly isDeleted?: unknown;
  readonly clientRequestId?: unknown;
  readonly version?: unknown;
  readonly threadRootMessageId?: unknown;
  readonly thread?: MessageThreadSummaryDto | null;
}

export interface MessageThreadSummaryDto {
  readonly threadRootMessageId?: unknown;
  readonly replyCount?: unknown;
  readonly latestReplyAt?: unknown;
  readonly participantDisplayNames?: readonly unknown[];
}

export interface MessageThreadDto {
  readonly rootMessage?: MessageDto;
  readonly replies?: readonly MessageDto[];
  readonly summary?: MessageThreadSummaryDto;
  readonly hasMore?: unknown;
  readonly maximumReplies?: unknown;
}

export interface ThreadMessageCreatedDto {
  readonly message?: MessageDto;
  readonly summary?: MessageThreadSummaryDto;
}

export interface ConversationRecipientDto {
  readonly userId?: unknown;
  readonly displayName?: unknown;
}

export interface GroupWorkspaceMemberDto extends ConversationRecipientDto {
  readonly status?: unknown;
}

export interface ParticipantStateDto {
  readonly participantId?: unknown;
  readonly userId?: unknown;
  readonly conversationId?: unknown;
  readonly lastOpenedAt?: unknown;
  readonly lastReadMessageId?: unknown;
  readonly lastReadAt?: unknown;
  readonly unreadCursorMessageId?: unknown;
  readonly unreadCount?: unknown;
  readonly isMuted?: unknown;
  readonly isArchived?: unknown;
  readonly isLater?: unknown;
  readonly createdAt?: unknown;
  readonly updatedAt?: unknown;
}

export interface MessageNotificationPreferenceDto {
  readonly messageNotificationsEnabled?: unknown;
}

export interface MessageSearchResponseDto {
  readonly items?: unknown;
}

export interface MessageFollowUpListItemDto {
  readonly messageId?: unknown;
  readonly conversationId?: unknown;
  readonly workspaceId?: unknown;
  readonly conversationType?: unknown;
  readonly conversationTitle?: unknown;
  readonly threadRootMessageId?: unknown;
  readonly authorDisplayName?: unknown;
  readonly body?: unknown;
  readonly messageCreatedAt?: unknown;
  readonly savedAt?: unknown;
}

export interface MessageFollowUpListResponseDto extends PagedResponseDto<MessageFollowUpListItemDto> {
  readonly page?: unknown;
  readonly pageSize?: unknown;
  readonly totalCount?: unknown;
}

export interface MessageFollowUpStateDto {
  readonly messageId?: unknown;
  readonly isSaved?: unknown;
  readonly savedAt?: unknown;
}

export type MessageReadFilterDto = 'All' | 'Read' | 'Unread';
export type MessageAttachmentFilterDto = 'All' | 'With' | 'Without';

export interface MessageSearchRequestDto {
  readonly query?: string;
  readonly authorUserId?: string;
  readonly fromDate?: string;
  readonly toDateExclusive?: string;
  readonly messageRead?: MessageReadFilterDto;
  readonly messageAttachment?: MessageAttachmentFilterDto;
}

export interface MessageAuthorOptionDto {
  readonly userId?: unknown;
  readonly displayName?: unknown;
}

export interface MessageAuthorOptionsResponseDto {
  readonly items?: unknown;
}

@Injectable({ providedIn: 'root' })
export class MessagingApi {
  private readonly http = inject(HttpClient);

  listConversations(view: ConversationInboxViewDto = 'All', page = 1): Observable<ConversationInboxResponseDto> {
    const params: Record<string, string | number> = {};
    if (view !== 'All') { params['view'] = view; }
    if (page > 1) { params['page'] = page; }
    return this.http.get<ConversationInboxResponseDto>('/api/conversations', {
      params: Object.keys(params).length > 0 ? params : undefined,
      withCredentials: true
    });
  }

  searchRecipients(query: string): Observable<readonly ConversationRecipientDto[]> {
    return this.http.get<readonly ConversationRecipientDto[]>('/api/conversations/recipients', {
      params: { query },
      withCredentials: true
    });
  }

  searchMessages(request: string | MessageSearchRequestDto): Observable<MessageSearchResponseDto> {
    const filters: MessageSearchRequestDto = typeof request === 'string' ? { query: request } : request;
    let params = new HttpParams()
      .set('type', 'Message')
      .set('page', '1')
      .set('pageSize', '50');
    if (filters.query) {
      params = params.set('q', filters.query);
    }
    if (filters.authorUserId) {
      params = params.set('authorUserId', filters.authorUserId);
    }
    if (filters.fromDate) {
      params = params.set('fromDate', filters.fromDate);
    }
    if (filters.toDateExclusive) {
      params = params.set('toDateExclusive', filters.toDateExclusive);
    }
    if (filters.messageRead && filters.messageRead !== 'All') {
      params = params.set('messageRead', filters.messageRead);
    }
    if (filters.messageAttachment && filters.messageAttachment !== 'All') {
      params = params.set('messageAttachment', filters.messageAttachment);
    }

    return this.http.get<MessageSearchResponseDto>('/api/search', {
      params,
      withCredentials: true
    });
  }

  searchMessageAuthors(query: string): Observable<MessageAuthorOptionsResponseDto> {
    return this.http.get<MessageAuthorOptionsResponseDto>('/api/search/message-authors', {
      params: { q: query, limit: '20' },
      withCredentials: true
    });
  }

  resolveMessageAuthor(userId: string): Observable<MessageAuthorOptionsResponseDto> {
    return this.http.get<MessageAuthorOptionsResponseDto>('/api/search/message-authors', {
      params: { selectedUserId: userId, limit: '1' },
      withCredentials: true
    });
  }

  createDirectConversation(recipientUserId: string): Observable<ConversationDetailDto> {
    return this.http.post<ConversationDetailDto>(
      '/api/conversations/direct',
      { recipientUserId },
      { withCredentials: true }
    );
  }

  listGroupWorkspaceMembers(workspaceId: string): Observable<readonly GroupWorkspaceMemberDto[]> {
    return this.http.get<readonly GroupWorkspaceMemberDto[]>(`/api/workspaces/${workspaceId}/members`, {
      withCredentials: true
    });
  }

  createGroupConversation(workspaceId: string, memberUserIds: readonly string[], title: string): Observable<ConversationDetailDto> {
    return this.http.post<ConversationDetailDto>('/api/conversations', {
      type: 7, workspaceId, memberUserIds, title: title.trim() || null
    }, { withCredentials: true });
  }

  addGroupMember(conversationId: string, userId: string): Observable<ConversationMemberDto> {
    return this.http.post<ConversationMemberDto>(`/api/conversations/${conversationId}/members`, { userId }, { withCredentials: true });
  }

  removeGroupMember(conversationId: string, userId: string): Observable<unknown> {
    return this.http.delete(`/api/conversations/${conversationId}/members/${userId}`, { withCredentials: true });
  }

  leaveGroup(conversationId: string): Observable<unknown> {
    return this.http.post(`/api/conversations/${conversationId}/leave`, {}, { withCredentials: true });
  }

  getConversation(conversationId: string): Observable<ConversationDetailDto> {
    return this.http.get<ConversationDetailDto>(`/api/conversations/${conversationId}`, {
      withCredentials: true
    });
  }

  listMessages(conversationId: string, before?: string, anchorMessageId?: string): Observable<PagedResponseDto<MessageDto>> {
    return this.http.get<PagedResponseDto<MessageDto>>(`/api/conversations/${conversationId}/messages`, {
      params: {
        ...(before ? { before } : {}),
        ...(anchorMessageId ? { anchorMessageId } : {})
      },
      withCredentials: true
    });
  }

  listMessageFollowUps(page = 1, pageSize = 20): Observable<MessageFollowUpListResponseDto> {
    return this.http.get<MessageFollowUpListResponseDto>('/api/me/message-follow-ups', {
      params: { page: String(page), pageSize: String(pageSize) },
      withCredentials: true
    });
  }

  saveMessageFollowUp(messageId: string): Observable<MessageFollowUpStateDto> {
    return this.http.put<MessageFollowUpStateDto>(
      `/api/me/message-follow-ups/${messageId}`,
      {},
      { withCredentials: true }
    );
  }

  removeMessageFollowUp(messageId: string): Observable<MessageFollowUpStateDto> {
    return this.http.delete<MessageFollowUpStateDto>(`/api/me/message-follow-ups/${messageId}`, {
      withCredentials: true
    });
  }

  sendMessage(
    conversationId: string,
    body: string,
    clientRequestId?: string,
    mentionedUserIds: readonly string[] = []
  ): Observable<MessageDto> {
    return this.http.post<MessageDto>(
      `/api/conversations/${conversationId}/messages`,
      { body, clientRequestId, mentionedUserIds },
      { withCredentials: true }
    );
  }

  getMessageThread(messageId: string, anchorReplyMessageId?: string): Observable<MessageThreadDto> {
    return this.http.get<MessageThreadDto>(`/api/messages/${messageId}/thread`, {
      withCredentials: true,
      params: anchorReplyMessageId ? { anchorReplyMessageId } : {}
    });
  }

  sendThreadMessage(
    messageId: string,
    body: string,
    clientRequestId?: string,
    mentionedUserIds: readonly string[] = []
  ): Observable<ThreadMessageCreatedDto> {
    return this.http.post<ThreadMessageCreatedDto>(
      `/api/messages/${messageId}/thread/messages`,
      { body, clientRequestId, mentionedUserIds },
      { withCredentials: true }
    );
  }

  updateMessage(messageId: string, body: string): Observable<MessageDto> {
    return this.http.patch<MessageDto>(
      `/api/messages/${messageId}`,
      { body },
      { withCredentials: true }
    );
  }

  deleteMessage(messageId: string): Observable<unknown> {
    return this.http.delete(`/api/messages/${messageId}`, {
      withCredentials: true
    });
  }

  reportMessage(messageId: string, reasonCode: string): Observable<unknown> {
    return this.http.post(
      `/api/messages/${messageId}/report`,
      { reasonCode },
      { withCredentials: true }
    );
  }

  getParticipantState(conversationId: string): Observable<ParticipantStateDto> {
    return this.http.get<ParticipantStateDto>(`/api/conversations/${conversationId}/state`, {
      withCredentials: true
    });
  }

  updateParticipantState(conversationId: string, isMuted: boolean): Observable<ParticipantStateDto> {
    return this.http.patch<ParticipantStateDto>(
      `/api/conversations/${conversationId}/state`,
      { isMuted },
      { withCredentials: true }
    );
  }

  updateConversationLater(conversationId: string, isLater: boolean): Observable<ParticipantStateDto> {
    return this.http.patch<ParticipantStateDto>(
      `/api/conversations/${conversationId}/state`,
      { isLater },
      { withCredentials: true }
    );
  }

  getMessageNotificationPreference(): Observable<MessageNotificationPreferenceDto> {
    return this.http.get<MessageNotificationPreferenceDto>('/api/me/message-notification-preferences', {
      withCredentials: true
    });
  }

  updateMessageNotificationPreference(messageNotificationsEnabled: boolean): Observable<MessageNotificationPreferenceDto> {
    return this.http.patch<MessageNotificationPreferenceDto>(
      '/api/me/message-notification-preferences',
      { messageNotificationsEnabled },
      { withCredentials: true }
    );
  }

  markRead(conversationId: string, lastReadMessageId: string | null): Observable<unknown> {
    return this.http.post(
      `/api/conversations/${conversationId}/read`,
      { lastReadMessageId },
      { withCredentials: true }
    );
  }
}
