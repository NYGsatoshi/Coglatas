using Coglatas.Application.Common;

namespace Coglatas.Application.Auth;

public interface IUserSessionService
{
    Task<SessionValidationResult> ValidateSessionAsync(
        Guid userId,
        Guid sessionId,
        Guid? tenantId,
        bool requireActiveTenantMembership,
        CancellationToken cancellationToken = default);

    Task<Result> RevokeSessionAsync(
        Guid sessionId,
        Guid? actorUserId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<Result<int>> RevokeUserSessionsAsync(
        Guid userId,
        Guid? actorUserId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default);
}

public sealed record SessionValidationResult(bool IsValid, string? FailureReason = null)
{
    public static SessionValidationResult Success() => new(true);

    public static SessionValidationResult Failure(string reason) => new(false, reason);
}
