using System.Globalization;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;

namespace Coglatas.Application.Notifications;

public sealed class TaskNotificationPreferenceService(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    ITaskWorkspaceTimeZoneResolver timeZones,
    ITaskNotificationPreferenceRepository preferences) : ITaskNotificationPreferenceService
{
    public const string AuthenticationRequiredCode = "TASK_NOTIFICATION_PREFERENCE_AUTHENTICATION_REQUIRED";
    public const string NotFoundCode = "TASK_NOTIFICATION_PREFERENCE_NOT_FOUND";
    public const string InvalidLocalTimeCode = "TASK_NOTIFICATION_PREFERENCE_INVALID_LOCAL_TIME";
    public const string VersionConflictCode = "TASK_NOTIFICATION_PREFERENCE_VERSION_CONFLICT";

    public async Task<TaskNotificationPreferenceResult> GetAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUser(out var userId))
        {
            return TaskNotificationPreferenceResult.Failure(
                AuthenticationRequiredCode,
                "Authentication is required.");
        }

        var context = await preferences.GetAccessibleAsync(workspaceId, userId, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        return TaskNotificationPreferenceResult.Success(await ToResponseAsync(workspaceId, context, cancellationToken));
    }

    public async Task<TaskNotificationPreferenceResult> UpdateAsync(
        Guid workspaceId,
        UpdateTaskNotificationPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUser(out var userId))
        {
            return TaskNotificationPreferenceResult.Failure(
                AuthenticationRequiredCode,
                "Authentication is required.");
        }

        var context = await preferences.GetAccessibleAsync(workspaceId, userId, cancellationToken);
        if (context is null)
        {
            return NotFound();
        }

        if (!TryParseLocalTime(request.DeadlineDigestLocalTime, out var localTime))
        {
            return TaskNotificationPreferenceResult.Failure(
                InvalidLocalTimeCode,
                "deadlineDigestLocalTime must be an HH:mm value from 00:00 through 23:45 at 15-minute intervals.");
        }

        if (!request.ExpectedVersion.HasValue || request.ExpectedVersion.Value <= 0 ||
            request.ExpectedVersion.Value != context.Version)
        {
            return VersionConflict(context.Version);
        }

        var updated = await preferences.TryUpdateAsync(
            workspaceId,
            userId,
            request.ExpectedVersion.Value,
            localTime,
            clock.UtcNow,
            cancellationToken);
        if (!updated)
        {
            var current = await preferences.GetAccessibleAsync(workspaceId, userId, cancellationToken);
            return current is null ? NotFound() : VersionConflict(current.Version);
        }

        var persisted = await preferences.GetAccessibleAsync(workspaceId, userId, cancellationToken);
        return persisted is null
            ? NotFound()
            : TaskNotificationPreferenceResult.Success(await ToResponseAsync(workspaceId, persisted, cancellationToken));
    }

    private async Task<TaskNotificationPreferenceResponse> ToResponseAsync(
        Guid workspaceId,
        TaskNotificationPreferenceContext context,
        CancellationToken cancellationToken)
    {
        var workspaceTimeZone = await timeZones.ResolveAsync(currentTenant.TenantId, workspaceId, cancellationToken);
        return new TaskNotificationPreferenceResponse(
            Format(context.DeadlineDigestLocalTime),
            Format(context.DeadlineDigestLocalTime ?? context.DefaultDeadlineDigestLocalTime)!,
            workspaceTimeZone.Id,
            context.Version);
    }

    private bool TryGetCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue && currentTenant.IsAvailable;
    }

    private static bool TryParseLocalTime(string? value, out TimeOnly? localTime)
    {
        localTime = null;
        if (value is null)
        {
            return true;
        }

        if (!TimeOnly.TryParseExact(
                value,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed) ||
            parsed.Minute % 15 != 0)
        {
            return false;
        }

        localTime = parsed;
        return true;
    }

    private static string? Format(TimeOnly? value) => value?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static TaskNotificationPreferenceResult NotFound() =>
        TaskNotificationPreferenceResult.Failure(NotFoundCode, "Task notification preferences are unavailable for this workspace.");

    private static TaskNotificationPreferenceResult VersionConflict(long currentVersion) =>
        TaskNotificationPreferenceResult.Failure(
            VersionConflictCode,
            "Task notification preferences have changed. Refetch and retry.",
            currentVersion);
}
