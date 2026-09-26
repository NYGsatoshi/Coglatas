using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.Notifications;

public sealed class NotificationApplicationService(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    INotificationService notifications,
    INotificationOpenService notificationOpen,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork) : INotificationApplicationService
{
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<NotificationListItemResponse>>> ListAsync(NotificationListQuery query, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result<PagedResponse<NotificationListItemResponse>>.Failure("Authentication is required.");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        return Result<PagedResponse<NotificationListItemResponse>>.Success(await notifications.ListAsync(userId, page, pageSize, cancellationToken));
    }

    public async Task<Result<NotificationUnreadCountResponse>> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result<NotificationUnreadCountResponse>.Failure("Authentication is required.");
        }

        return Result<NotificationUnreadCountResponse>.Success(new NotificationUnreadCountResponse(await notifications.GetUnreadCountAsync(userId, cancellationToken)));
    }

    public async Task<Result> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result.Failure("Authentication is required.");
        }

        if (!await notifications.MarkAsReadAsync(userId, notificationId, cancellationToken))
        {
            return Result.Failure("Notification not found.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<NotificationOpenResponse>> OpenAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result<NotificationOpenResponse>.Failure("Authentication is required.");
        }

        // The infrastructure implementation owns the target-resolution,
        // read-state and Outbox transaction. Do not mark a notification read
        // on the client or before its current target is authorized.
        var resolution = await notificationOpen.OpenAsync(
            currentTenant.IsAvailable ? currentTenant.TenantId : Guid.Empty,
            userId,
            notificationId,
            cancellationToken);
        if (!resolution.IsOwned)
        {
            // Controller maps this to the same safe not-found response used
            // for a missing notification; another recipient is never exposed.
            return Result<NotificationOpenResponse>.Failure("Notification not found.");
        }

        if (!resolution.IsAvailable)
        {
            // Ownership alone is not authority to open a protected target.
            // Current authorization can disappear after the session was
            // established (membership revoke, role/scope change, target
            // deletion). Collapse that stale authorization state to the same
            // metadata-safe not-found response as a missing/foreign row. A
            // 2xx "Unavailable" response would turn authorization loss into
            // an observable success class and violates the SEC-05 fail-closed
            // contract even when no read mutation occurs.
            return Result<NotificationOpenResponse>.Failure("Notification not found.");
        }

        return Result<NotificationOpenResponse>.Success(new NotificationOpenResponse(
            "Opened",
            resolution.Route,
            resolution.StateVersion,
            resolution.WorkspaceId.HasValue
                ? new NotificationOpenContextResponse(resolution.WorkspaceId.Value)
                : null));
    }

    public async Task<Result> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result.Failure("Authentication is required.");
        }

        await notifications.MarkAllAsReadAsync(userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Result.Failure("Authentication is required.");
        }

        if (!await notifications.DeleteAsync(userId, notificationId, clock.UtcNow, cancellationToken))
        {
            return Result.Failure("Notification not found.");
        }

        await auditLogger.LogUserActionAsync(userId, "NotificationDeleted", "Notification", notificationId, "Notification deleted.", cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }
}
