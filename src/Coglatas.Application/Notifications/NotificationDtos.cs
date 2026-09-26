using Coglatas.Domain.Enums;

namespace Coglatas.Application.Notifications;

public sealed record NotificationListQuery(int Page = 1, int PageSize = 20);

public sealed record NotificationListItemResponse(
    Guid Id,
    Guid UserId,
    NotificationType NotificationType,
    string Title,
    string? Body,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? TargetRoute,
    long StateVersion = 0);

public sealed record NotificationUnreadCountResponse(int UnreadCount, long StateVersion = 0);

/// <summary>
/// The result of resolving and opening a notification target. Unavailable is
/// intentionally uniform: it never contains a reason or protected resource
/// data. Context is populated only when canonical navigation requires the
/// currently authorized Workspace identity.
/// </summary>
public sealed record NotificationOpenResponse(
    string Outcome,
    string? Route,
    long StateVersion,
    NotificationOpenContextResponse? Context = null);

public sealed record NotificationOpenContextResponse(Guid WorkspaceId);