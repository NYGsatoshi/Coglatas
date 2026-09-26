using System.Text.Json;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Communication;

public sealed record CommunicationPollingQuery(
    int Page = 1,
    int PageSize = 50,
    Guid? WorkspaceId = null);

public sealed record CommunicationUpdatesPollingQuery(
    DateTimeOffset? Since = null,
    string? Cursor = null,
    int Page = 1,
    int PageSize = 50,
    Guid? WorkspaceId = null);

public sealed record ConversationUnreadPollingItem(
    Guid ConversationId,
    ConversationType ConversationType,
    int UnreadCount,
    bool HasUnread,
    DateTimeOffset? LastVisibleMessageAt,
    Guid? LatestMessageId,
    DateTimeOffset UpdatedAt,
    bool IsMuted,
    bool IsArchived);

public sealed record ConversationUnreadPollingResponse(
    IReadOnlyList<ConversationUnreadPollingItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTimeOffset PolledAt);

public sealed record NotificationPollingItem(
    Guid NotificationId,
    NotificationType NotificationType,
    string TargetType,
    Guid? TargetId,
    string Title,
    string Summary,
    DateTimeOffset CreatedAt,
    bool IsRead,
    DateTimeOffset? ReadAt,
    Guid? ConversationId,
    Guid? ProjectId);

public sealed record NotificationPollingResponse(
    IReadOnlyList<NotificationPollingItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTimeOffset PolledAt);

public sealed record CommunicationUpdatePollingItem(
    string UpdateType,
    Guid? ConversationId,
    Guid? NotificationId,
    int? UnreadCount,
    DateTimeOffset UpdatedAt);

public sealed record CommunicationUpdatesPollingResponse(
    IReadOnlyList<CommunicationUpdatePollingItem> Items,
    DateTimeOffset CursorFrom,
    DateTimeOffset CursorTo,
    string NextCursor,
    int Page,
    int PageSize,
    bool FullRefresh,
    DateTimeOffset PolledAt);

internal sealed record CommunicationPollingCursor(
    Guid ActorUserId,
    Guid? TenantId,
    Guid? WorkspaceId,
    DateTimeOffset Since)
{
    public string Encode()
    {
        var json = JsonSerializer.Serialize(this);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public static bool TryDecode(string cursor, out CommunicationPollingCursor? value)
    {
        value = null;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            value = JsonSerializer.Deserialize<CommunicationPollingCursor>(json);
            return value is not null;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
