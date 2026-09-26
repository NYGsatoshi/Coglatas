using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Messaging;

public static class MentionAttentionResolver
{
    private const int NotificationPageSize = 100;

    public static async Task<IReadOnlySet<Guid>> ListUnreadMentionConversationIdsAsync(
        INotificationService notifications,
        IMessagingRepository messaging,
        Guid userId,
        IReadOnlySet<Guid> candidateConversationIds,
        CancellationToken cancellationToken = default)
    {
        var conversationIds = new HashSet<Guid>();
        if (candidateConversationIds.Count == 0)
        {
            return conversationIds;
        }

        var pageNumber = 1;
        while (conversationIds.Count < candidateConversationIds.Count)
        {
            var page = await notifications.ListAsync(userId, pageNumber, NotificationPageSize, cancellationToken);
            var messageIds = page.Items
                .Where(notification =>
                    !notification.IsRead &&
                    notification.NotificationType == NotificationType.Mention &&
                    string.Equals(notification.RelatedEntityType, "Message", StringComparison.Ordinal) &&
                    notification.RelatedEntityId.HasValue)
                .Select(notification => notification.RelatedEntityId!.Value)
                .Distinct()
                .ToArray();

            foreach (var messageId in messageIds)
            {
                var message = await messaging.GetMessageAsync(messageId, cancellationToken);
                if (message is not null &&
                    !message.DeletedAt.HasValue &&
                    candidateConversationIds.Contains(message.ConversationId))
                {
                    conversationIds.Add(message.ConversationId);
                }
            }

            if (page.Items.Count == 0 || page.Page * page.PageSize >= page.TotalCount)
            {
                break;
            }

            pageNumber++;
        }

        return conversationIds;
    }
}
