namespace Coglatas.Application.Notifications;

/// <summary>
/// Database contract for the immutable, recipient-specific notification
/// identity introduced by TASK-V1-PR07-A.
/// </summary>
public static class NotificationLogicalKeyContract
{
    public const int MaximumLength = 512;
    public const string UniqueIndexName = "IX_notifications_TenantId_UserId_LogicalKey";
}
