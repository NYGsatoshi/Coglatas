namespace Coglatas.Web.Testing;

/// <summary>
/// Stable, synthetic-only identities used by the focused PR07-D real-backend
/// smoke. The corresponding endpoints are mapped only with the existing
/// explicit Test-environment browser-smoke opt-in.
/// </summary>
public static class BrowserSmokeNotificationFixture
{
    public const string ProjectSlug = "browser-smoke-pr07-notifications";
    public const string ProjectTitle = "PR07 Browser Smoke Notifications Project";
    public const string TaskTitle = "PR07 authorized notification task";
    public const string RecipientEmail = "browser-smoke-recipient@example.test";
    public const string NotificationTitle = "PR07D authorized delivery smoke notification";
    public const int MaximumDispatchDelaySeconds = 15;
}

public sealed record BrowserSmokeTaskNotificationRequest(int? DispatchDelaySeconds);

public sealed record BrowserSmokeTaskNotificationResponse(
    Guid NotificationId,
    Guid EventId,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid TaskId,
    long StateVersion,
    int DispatchDelaySeconds);

public sealed record BrowserSmokeOutboxEventSnapshot(
    Guid EventId,
    string Status,
    int AttemptCount,
    string? OutcomeCode);
