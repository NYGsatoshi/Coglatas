using Coglatas.Domain.Enums;

namespace Coglatas.Application.Notifications;

public sealed record TaskDeadlineDigestScheduleCandidate(
    Guid WorkspaceId,
    Guid UserId,
    string? WorkspaceTimeZoneId,
    TimeOnly EffectiveLocalTime);

public sealed record TaskDeadlineDigestScheduleWrite(
    Guid JobId,
    Guid WorkspaceId,
    Guid UserId,
    DateOnly LocalDate,
    int PolicyVersion,
    DateTimeOffset ScheduledForUtc);

public sealed record TaskDeadlineDigestClaim(
    Guid JobId,
    Guid TenantId,
    Guid WorkspaceId,
    Guid UserId,
    DateOnly LocalDate,
    int PolicyVersion,
    Guid ClaimToken,
    TaskDeadlineDigestAttemptTrigger Trigger);

public sealed record TaskDeadlineDigestCurrentContext(
    Guid TenantId,
    Guid WorkspaceId,
    Guid UserId,
    string? WorkspaceTimeZoneId,
    string? TenantTimeZoneId,
    TimeOnly EffectiveLocalTime);

/// <summary>
/// A bounded candidate page returned while generating a digest. The structural
/// values are retained only long enough for the repository-owned commit fence
/// to prove that the same current Task is still eligible before staging a
/// visible notification.
/// </summary>
public sealed record TaskDeadlineDigestCandidate(
    Guid TaskId,
    DateTimeOffset DeadlineAt,
    Guid ProjectId = default,
    Guid? WorkflowStageId = null);

/// <summary>
/// Result of the repository-owned current-state fence. Application code never
/// receives provider error details or SQLSTATE values.
/// </summary>
public enum TaskDeadlineDigestGenerationFenceOutcome
{
    Current = 0,
    ClaimLost = 1,
    CurrentStateChanged = 2
}

public sealed record TaskDeadlineDigestTransition(bool Changed, bool Terminal);

public sealed record TaskDeadlineDigestStoreDiagnostics(
    long DueCount,
    long ClaimedCount,
    long SucceededCount,
    long FailedCount,
    DateTimeOffset? OldestDueAt,
    DateTimeOffset? OldestClaimedAt);

public enum TaskDeadlineDigestRestartOutcome
{
    Restarted = 0,
    NotFound = 1,
    NotFailed = 2,
    ActiveAttemptExists = 3
}

public interface ITaskDeadlineDigestTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface ITaskDeadlineDigestRepository
{
    Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<string?> GetTenantTimeZoneIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskDeadlineDigestScheduleCandidate>> ListScheduleCandidatesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<int> UpsertSchedulesAsync(
        IReadOnlyCollection<TaskDeadlineDigestScheduleWrite> schedules,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskDeadlineDigestClaim>> ClaimDueAsync(
        string claimOwner,
        DateTimeOffset now,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken = default);

    Task<TaskDeadlineDigestClaim?> GetClaimedAsync(
        Guid jobId,
        Guid claimToken,
        bool forUpdate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires the generation claim fence at the beginning of a generation
    /// transaction. The implementation locks the claimed Job first and its
    /// claimed Attempt second, then verifies the supplied token, status,
    /// tenant, recipient, Workspace, and trigger identity. A null result
    /// means a stale worker must stage nothing.
    /// </summary>
    Task<TaskDeadlineDigestClaim?> AcquireGenerationClaimFenceAsync(
        TaskDeadlineDigestClaim claim,
        CancellationToken cancellationToken = default);

    Task<TaskDeadlineDigestCurrentContext?> GetCurrentContextAsync(
        Guid jobId,
        Guid claimToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskDeadlineDigestCandidate>> ListCurrentCandidatesAsync(
        Guid jobId,
        Guid claimToken,
        DateTimeOffset deadlineBeforeUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the current authorization/lifecycle state that authorized the
    /// supplied bounded candidate page and verifies that it has not changed
    /// since evaluation. The same generation transaction must already hold
    /// its Job/Attempt ownership fence through
    /// <see cref="AcquireGenerationClaimFenceAsync"/>; this method never
    /// acquires that initial claim fence. A non-current result must be retried
    /// in a new generation transaction before any visible digest state is
    /// staged.
    /// </summary>
    Task<TaskDeadlineDigestGenerationFenceOutcome> AcquireGenerationFenceAsync(
        TaskDeadlineDigestClaim claim,
        TaskDeadlineDigestCurrentContext? evaluatedContext,
        IReadOnlyCollection<TaskDeadlineDigestCandidate> evaluatedCandidates,
        CancellationToken cancellationToken = default);

    Task<ITaskDeadlineDigestTransaction> BeginGenerationTransactionAsync(
        CancellationToken cancellationToken = default);

    Task<bool> MarkSucceededAsync(
        Guid jobId,
        Guid claimToken,
        Guid? notificationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task<bool> DeferAsync(
        Guid jobId,
        Guid claimToken,
        DateTimeOffset scheduledForUtc,
        DateTimeOffset deferredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a still-owned claim because the Tenant rollout flag was
    /// disabled. This is intentionally distinct from ordinary defer: it
    /// restores the claimed attempt budget and preserves an operator restart
    /// attempt as the same pending audited row.
    /// </summary>
    Task<bool> ReleaseFeatureDisabledClaimAsync(
        Guid jobId,
        Guid claimToken,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken = default);

    Task<TaskDeadlineDigestTransition> MarkFailureAsync(
        Guid jobId,
        Guid claimToken,
        string errorCode,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);

    Task<TaskDeadlineDigestRestartOutcome> RestartFailedAsync(
        Guid jobId,
        Guid actorUserId,
        string reason,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default);

    Task<TaskDeadlineDigestStoreDiagnostics> GetDiagnosticsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards tracked state after a rolled-back generation attempt so an
    /// internal retry always begins from current persisted state.
    /// </summary>
    void ResetGenerationState();
}
