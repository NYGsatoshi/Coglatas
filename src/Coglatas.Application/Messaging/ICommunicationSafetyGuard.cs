namespace Coglatas.Application.Messaging;

public interface ICommunicationSafetyGuard
{
    CommunicationSafetyDecision CheckMessagePost(CommunicationSafetyScope scope, string normalizedBody, DateTimeOffset now);

    // An important-only TaskComment update has notification significance even
    // though it has no body to duplicate-check. It deliberately shares the
    // post window with normal comments while keeping its intent explicit at
    // the call site.
    CommunicationSafetyDecision CheckTaskCommentSignificance(CommunicationSafetyScope scope, DateTimeOffset now) =>
        CheckMessagePost(scope, string.Empty, now);

    CommunicationSafetyDecision CheckThreadCreate(CommunicationSafetyScope scope, DateTimeOffset now);
    CommunicationSafetyDecision CheckReport(CommunicationSafetyScope scope, DateTimeOffset now);
}

public sealed record CommunicationSafetyScope(
    Guid ActorUserId,
    Guid TenantId,
    Guid WorkspaceId,
    Guid ConversationId);

public sealed record CommunicationSafetyDecision(bool IsAllowed, string ReasonCode, int? RetryAfterSeconds = null)
{
    public static CommunicationSafetyDecision Allow() => new(true, "allow");
    public static CommunicationSafetyDecision Deny(string reasonCode, int? retryAfterSeconds = null) => new(false, reasonCode, retryAfterSeconds);
}

