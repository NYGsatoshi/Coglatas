namespace Coglatas.Application.Notifications;

/// <summary>
/// Tenant-scoped persistence boundary for a current user's private Workspace
/// digest preference. Implementations must fail closed for inactive membership.
/// </summary>
public interface ITaskNotificationPreferenceRepository
{
    Task<TaskNotificationPreferenceContext?> GetAccessibleAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a single conditional update against the expected version.
    /// A false result means either access changed or another writer won; callers
    /// must re-read through <see cref="GetAccessibleAsync"/> before classifying it.
    /// </summary>
    Task<bool> TryUpdateAsync(
        Guid workspaceId,
        Guid userId,
        long expectedVersion,
        TimeOnly? deadlineDigestLocalTime,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}

public sealed record TaskNotificationPreferenceContext(
    TimeOnly? DeadlineDigestLocalTime,
    TimeOnly DefaultDeadlineDigestLocalTime,
    long Version);
