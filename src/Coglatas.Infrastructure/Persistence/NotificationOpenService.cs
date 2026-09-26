using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Notifications;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Performs notification opening in one business transaction: own the
/// notification, resolve its current target, then (and only then) mutate read
/// state and stage its recipient-only durable event.
/// </summary>
public sealed class NotificationOpenService(
    AppDbContext dbContext,
    ICurrentTenant currentTenant,
    IClock clock,
    ITransactionalOutbox outbox,
    INotificationTargetResolver targets,
    INotificationService notifications) : INotificationOpenService
{
    public async Task<NotificationTargetResolution> OpenAsync(
        Guid tenantId,
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.TenantId != tenantId ||
            userId == Guid.Empty || notificationId == Guid.Empty)
        {
            return new NotificationTargetResolution(false, false, null, 0);
        }

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var notification = await dbContext.Notifications.SingleOrDefaultAsync(item =>
            item.Id == notificationId &&
            item.TenantId == tenantId &&
            item.UserId == userId &&
            item.DeletedAt == null,
            cancellationToken);
        if (notification is null)
        {
            return new NotificationTargetResolution(false, false, null, 0);
        }

        var resolution = await targets.ResolveAsync(tenantId, userId, notificationId, cancellationToken);
        if (!resolution.IsAvailable)
        {
            return resolution;
        }

        if (!notification.IsRead)
        {
            var now = clock.UtcNow;
            // Reuse the authoritative, bounded current-visibility semantics
            // used by the HTTP unread endpoint. A raw unread-row count would
            // include a now-hidden Task/digest notification and make this
            // recipient-only event disagree with the current projection.
            var unreadBeforeRead = await notifications.GetUnreadCountAsync(userId, cancellationToken);
            var state = await GetOrCreateUserStateAsync(tenantId, userId, now, cancellationToken);
            var stateVersion = checked(state.Version + 1);
            state.Version = stateVersion;
            state.UpdatedAt = now;
            notification.IsRead = true;
            notification.ReadAt = now;
            notification.StateVersion = stateVersion;

            var enqueue = await outbox.EnqueueAsync(
                new DurableEventEnvelope(
                    Guid.NewGuid(),
                    "Notifications.NotificationReadStateChanged.v1",
                    RealtimeEventCatalog.PayloadSchemaVersion1,
                    now,
                    tenantId,
                    "Notification",
                    notification.Id,
                    stateVersion,
                    RealtimeActor.System(),
                    null,
                    null,
                    JsonSerializer.SerializeToElement(new
                    {
                        notificationId = notification.Id,
                        change = "read",
                        unreadCount = Math.Max(0, unreadBeforeRead - 1),
                        stateVersion,
                        updatedAt = now
                    })),
                [new RealtimeRoutingTarget(RealtimeSubscriptionType.User, userId)],
                cancellationToken);
            if (!enqueue.IsSuccess)
            {
                throw new InvalidOperationException("Notification read-state event could not be staged.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return resolution with { StateVersion = stateVersion };
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return resolution;
    }

    private async Task<NotificationUserState> GetOrCreateUserStateAsync(
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.NotificationUserStates.SingleOrDefaultAsync(item =>
            item.TenantId == tenantId && item.UserId == userId,
            cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new NotificationUserState
        {
            TenantId = tenantId,
            UserId = userId,
            Version = 0,
            UpdatedAt = now
        };
        await dbContext.NotificationUserStates.AddAsync(state, cancellationToken);
        return state;
    }

    private Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(CancellationToken cancellationToken) =>
        await dbContext.Database.BeginTransactionAsync(cancellationToken);
}
