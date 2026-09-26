using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Messaging;
using Coglatas.Application.Notifications;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Messaging;

public sealed class MentionAttentionResolverTests
{
    [Fact]
    public async Task ListsOnlyUnreadMentionMessagesInCandidateConversations()
    {
        var userId = Guid.NewGuid();
        var candidateConversationId = Guid.NewGuid();
        var otherConversationId = Guid.NewGuid();
        var mentionMessageId = Guid.NewGuid();
        var otherMessageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var notifications = new FakeNotificationService([
            new NotificationListItemResponse(
                Guid.NewGuid(), userId, NotificationType.Mention, "Mention", null,
                "Message", mentionMessageId, false, now, null, null),
            new NotificationListItemResponse(
                Guid.NewGuid(), userId, NotificationType.Mention, "Other mention", null,
                "Message", otherMessageId, false, now, null, null),
            new NotificationListItemResponse(
                Guid.NewGuid(), userId, NotificationType.DirectMessage, "Message", null,
                "Message", mentionMessageId, false, now, null, null)
        ]);
        var messaging = new FakeMessagingRepository(new Dictionary<Guid, Message>
        {
            [mentionMessageId] = MessageFor(mentionMessageId, candidateConversationId, now),
            [otherMessageId] = MessageFor(otherMessageId, otherConversationId, now)
        });

        var result = await MentionAttentionResolver.ListUnreadMentionConversationIdsAsync(
            notifications,
            messaging,
            userId,
            new HashSet<Guid> { candidateConversationId });

        Assert.Single(result);
        Assert.Contains(candidateConversationId, result);
    }

    [Fact]
    public async Task ReadMentionDoesNotProduceConversationAttention()
    {
        var userId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var notifications = new FakeNotificationService([
            new NotificationListItemResponse(
                Guid.NewGuid(), userId, NotificationType.Mention, "Mention", null,
                "Message", messageId, true, now, now, null)
        ]);
        var messaging = new FakeMessagingRepository(new Dictionary<Guid, Message>
        {
            [messageId] = MessageFor(messageId, conversationId, now)
        });

        var result = await MentionAttentionResolver.ListUnreadMentionConversationIdsAsync(
            notifications,
            messaging,
            userId,
            new HashSet<Guid> { conversationId });

        Assert.Empty(result);
    }

    private static Message MessageFor(Guid messageId, Guid conversationId, DateTimeOffset createdAt) => new()
    {
        Id = messageId,
        WorkspaceId = Guid.NewGuid(),
        ConversationId = conversationId,
        AuthorUserId = Guid.NewGuid(),
        Body = "Message",
        CreatedAt = createdAt
    };

    private sealed class FakeNotificationService(IReadOnlyList<NotificationListItemResponse> items) : INotificationService
    {
        public Task<PagedResponse<NotificationListItemResponse>> ListAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var userItems = items.Where(item => item.UserId == userId).ToArray();
            var pageItems = userItems.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            return Task.FromResult(new PagedResponse<NotificationListItemResponse>(
                pageItems,
                page,
                pageSize,
                userItems.Length));
        }

        public Task NotifyAsync(
            Guid recipientUserId,
            string title,
            string? body,
            string sourceType,
            Guid sourceId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMessagingRepository(IReadOnlyDictionary<Guid, Message> messages) : IMessagingRepository
    {
        public Task<Message?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(messages.GetValueOrDefault(messageId));

        public Task<PagedResponse<Conversation>> ListForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConversationInboxRepositoryResult> ListInboxForUserAsync(Guid userId, ConversationInboxView view, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<Guid>? QueryReadableConversationIds(Guid userId) => null;
        public Task<IReadOnlySet<Guid>> FilterReadableConversationIdsAsync(Guid userId, IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> SearchDirectRecipientsAsync(Guid userId, string? query, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Conversation?> FindDirectAsync(Guid workspaceId, Guid? projectId, Guid userAId, Guid userBId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Conversation?> FindDirectForUsersAsync(Guid userAId, Guid userBId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Workspace?> FindSharedActiveWorkspaceAsync(Guid userAId, Guid userBId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConversationMember?> GetMemberAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConversationMember>> ListMembersAsync(Guid conversationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponse<Message>> ListMessagesAsync(Guid conversationId, int limit, DateTimeOffset? before, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponse<Message>> ListThreadRepliesAsync(Guid conversationId, Guid threadRootMessageId, int limit, DateTimeOffset? before, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, MessageThreadSummaryResponse>> GetThreadSummariesAsync(Guid conversationId, IReadOnlyCollection<Guid> threadRootMessageIds, int participantLimit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MessageThreadSummaryResponse> GetThreadSummaryAsync(Guid conversationId, Guid threadRootMessageId, int participantLimit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountUnreadMessagesAsync(Guid conversationId, Guid userId, DateTimeOffset? lastReadAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Message?> FindMessageByClientRequestIdAsync(Guid conversationId, Guid authorUserId, Guid clientRequestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReadState?> GetReadStateAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddMemberAsync(ConversationMember member, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddMessageAsync(Message message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddReadStateAsync(ReadState readState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAttachmentAsync(Attachment attachment, MessageAttachment link, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
