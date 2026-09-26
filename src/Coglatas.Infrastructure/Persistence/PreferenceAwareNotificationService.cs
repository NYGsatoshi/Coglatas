using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Notifications;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Applies recipient Message preferences before a Message notification row is
/// created. Conversation activity/realtime delivery is not suppressed; only
/// the notification surface obeys Global OFF and per-conversation Mute.
/// </summary>
public sealed class PreferenceAwareNotificationService(
    DbNotificationService inner,
    AppDbContext dbContext,
    IMessageNotificationPreferenceStore preferences,
    ICurrentTenant currentTenant) : INotificationService
{
    public Task<Guid> StageTaskDeadlineDigestByLogicalKeyAsync(
        Guid userId,
        Guid digestJobId,
        string logicalKey,
        CancellationToken cancellationToken = default) =>
        inner.StageTaskDeadlineDigestByLogicalKeyAsync(userId, digestJobId, logicalKey, cancellationToken);

    public Task<Guid> StageTaskByLogicalKeyAsync(
        Guid userId,
        NotificationType type,
        string title,
        Guid taskId,
        string logicalKey,
        CancellationToken cancellationToken = default) =>
        inner.StageTaskByLogicalKeyAsync(userId, type, title, taskId, logicalKey, cancellationToken);

    public Task<Guid> CreateOrGetByLogicalKeyAsync(
        Guid userId,
        NotificationType type,
        string title,
        string? body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        string logicalKey,
        CancellationToken cancellationToken = default) =>
        inner.CreateOrGetByLogicalKeyAsync(
            userId,
            type,
            title,
            body,
            relatedEntityType,
            relatedEntityId,
            logicalKey,
            cancellationToken);

    public async Task<Guid> CreateAsync(
        Guid userId,
        NotificationType type,
        string title,
        string? body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(relatedEntityType, "Message", StringComparison.OrdinalIgnoreCase) &&
            (!relatedEntityId.HasValue ||
             !await ShouldDeliverMessageNotificationAsync(userId, relatedEntityId.Value, cancellationToken)))
        {
            return Guid.Empty;
        }

        return await inner.CreateAsync(
            userId,
            type,
            title,
            body,
            relatedEntityType,
            relatedEntityId,
            cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> CreateManyAsync(
        IReadOnlyCollection<Guid> userIds,
        NotificationType type,
        string title,
        string? body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default) =>
        inner.CreateManyAsync(
            userIds,
            type,
            title,
            body,
            relatedEntityType,
            relatedEntityId,
            actorUserId,
            cancellationToken);

    public Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default) =>
        inner.MarkAsReadAsync(userId, notificationId, cancellationToken);

    public Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        inner.MarkAllAsReadAsync(userId, cancellationToken);

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        inner.GetUnreadCountAsync(userId, cancellationToken);

    public Task<PagedResponse<NotificationListItemResponse>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        inner.ListAsync(userId, page, pageSize, cancellationToken);

    public Task<bool> DeleteAsync(
        Guid userId,
        Guid notificationId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(userId, notificationId, deletedAt, cancellationToken);

    public async Task NotifyAsync(
        Guid recipientUserId,
        string title,
        string? body,
        string sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(sourceType, "Message", StringComparison.OrdinalIgnoreCase) &&
            !await ShouldDeliverMessageNotificationAsync(recipientUserId, sourceId, cancellationToken))
        {
            return;
        }

        await inner.NotifyAsync(recipientUserId, title, body, sourceType, sourceId, cancellationToken);
    }

    private async Task<bool> ShouldDeliverMessageNotificationAsync(
        Guid recipientUserId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || currentTenant.IsPlatformScope || recipientUserId == Guid.Empty)
        {
            return false;
        }

        var tenantId = currentTenant.TenantId;
        var message = dbContext.Messages.Local.FirstOrDefault(item => item.Id == messageId)
            ?? await dbContext.Messages.AsNoTracking().FirstOrDefaultAsync(
                item => item.Id == messageId && item.TenantId == tenantId,
                cancellationToken);
        if (message is null || message.TenantId != tenantId || message.DeletedAt.HasValue)
        {
            return false;
        }

        var member = dbContext.ConversationMembers.Local.FirstOrDefault(item =>
                item.ConversationId == message.ConversationId &&
                item.UserId == recipientUserId)
            ?? await dbContext.ConversationMembers.AsNoTracking().FirstOrDefaultAsync(
                item => item.ConversationId == message.ConversationId &&
                        item.UserId == recipientUserId &&
                        item.TenantId == tenantId,
                cancellationToken);
        if (member is null ||
            member.TenantId != tenantId ||
            member.LeftAt.HasValue ||
            member.RemovedAt.HasValue ||
            !member.CanRead ||
            member.IsMuted)
        {
            return false;
        }

        return await preferences.GetEnabledAsync(tenantId, recipientUserId, cancellationToken) == true;
    }
}
