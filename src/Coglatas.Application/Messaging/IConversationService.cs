using Coglatas.Application.Common;

namespace Coglatas.Application.Messaging;

public interface IConversationService
{
    Task<Result<ConversationInboxResponse>> ListAsync(ConversationListQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ConversationRecipientResponse>>> ListRecipientsAsync(string? query, CancellationToken cancellationToken = default);
    Task<Result<ConversationDetailResponse>> CreateDirectAsync(CreateDirectConversationRequest request, CancellationToken cancellationToken = default);
    Task<Result<ConversationDetailResponse>> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default);
    Task<Result<ConversationDetailResponse>> GetAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<Result<ConversationDetailResponse>> UpdateAsync(Guid conversationId, UpdateConversationRequest request, CancellationToken cancellationToken = default);
    Task<Result<ConversationDetailResponse>> LockAsync(Guid conversationId, ConversationLockRequest request, CancellationToken cancellationToken = default);
    Task<Result<ConversationDetailResponse>> UnlockAsync(Guid conversationId, ConversationLockRequest request, CancellationToken cancellationToken = default);
    Task<Result<ConversationDetailResponse>> ArchiveAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<Result> LeaveAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ConversationMemberResponse>>> ListMembersAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<Result<ConversationMemberResponse>> AddMemberAsync(Guid conversationId, AddConversationMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveMemberAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<MessageResponse>>> ListMessagesAsync(Guid conversationId, MessageListQuery query, CancellationToken cancellationToken = default);
    Task<Result<MessageResponse>> SendMessageAsync(Guid conversationId, SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<Result<MessageThreadResponse>> GetMessageThreadAsync(Guid messageId, Guid? anchorReplyMessageId = null, CancellationToken cancellationToken = default);
    Task<Result<ThreadMessageCreatedResponse>> SendThreadMessageAsync(Guid messageId, SendThreadMessageRequest request, CancellationToken cancellationToken = default);
    Task<Result<MessageResponse>> UpdateMessageAsync(Guid messageId, UpdateMessageRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task<Result> ReportMessageAsync(Guid messageId, MessageReportRequest request, CancellationToken cancellationToken = default);
    Task<Result> ReportConversationAsync(Guid conversationId, ConversationReportRequest request, CancellationToken cancellationToken = default);
    Task<Result<ParticipantStateResponse>> GetParticipantStateAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<Result<ParticipantStateResponse>> UpdateParticipantStateAsync(Guid conversationId, UpdateParticipantStateRequest request, CancellationToken cancellationToken = default);
    Task<Result> MarkReadAsync(Guid conversationId, MarkConversationReadRequest request, CancellationToken cancellationToken = default);
}
