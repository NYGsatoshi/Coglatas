using Coglatas.Application.Common;

namespace Coglatas.Application.Notifications;

public sealed record TaskNotificationPreferenceResponse(
    string? DeadlineDigestLocalTime,
    string EffectiveDeadlineDigestLocalTime,
    string WorkspaceTimeZoneId,
    long Version);

public sealed record UpdateTaskNotificationPreferenceRequest(
    string? DeadlineDigestLocalTime,
    long? ExpectedVersion);

/// <summary>
/// A focused result keeps safe retry metadata available without widening the
/// repository-wide Result envelope for one private preference API.
/// </summary>
public sealed record TaskNotificationPreferenceResult(
    TaskNotificationPreferenceResponse? Value,
    ApplicationErrorDetail? ErrorDetail = null,
    long? CurrentVersion = null)
{
    public bool IsSuccess => Value is not null;

    public static TaskNotificationPreferenceResult Success(TaskNotificationPreferenceResponse value) => new(value);

    public static TaskNotificationPreferenceResult Failure(
        string code,
        string message,
        long? currentVersion = null) =>
        new(null, new ApplicationErrorDetail(code, message), currentVersion);
}
