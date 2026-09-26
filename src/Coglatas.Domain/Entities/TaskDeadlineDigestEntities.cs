using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Durable generation identity and claim state for one recipient's Workspace-local daily digest.
/// Delivery remains the responsibility of the transactional outbox.
/// </summary>
public sealed class TaskDeadlineDigestJob : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly LocalDate { get; set; }
    public int PolicyVersion { get; set; }
    public TaskDeadlineDigestJobStatus Status { get; set; } = TaskDeadlineDigestJobStatus.Pending;
    public int AttemptCount { get; set; }
    public int AutomaticAttemptCount { get; set; }
    public int AttemptSequence { get; set; }
    public DateTimeOffset ScheduledForUtc { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? ClaimOwner { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? ClaimExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastErrorCode { get; set; }
    public Guid? NotificationId { get; set; }

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
    public Notification? Notification { get; set; }
    public ICollection<TaskDeadlineDigestAttempt> Attempts { get; } = new List<TaskDeadlineDigestAttempt>();
}

/// <summary>
/// Append-only history for automatic attempts and operator-audited restarts.
/// </summary>
public sealed class TaskDeadlineDigestAttempt : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid JobId { get; set; }
    public int AttemptNumber { get; set; }
    public TaskDeadlineDigestAttemptTrigger Trigger { get; set; } = TaskDeadlineDigestAttemptTrigger.Automatic;
    public TaskDeadlineDigestAttemptStatus Status { get; set; } = TaskDeadlineDigestAttemptStatus.Pending;
    public Guid? RestartedFromAttemptId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public string? ClaimOwner { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? ClaimExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastErrorCode { get; set; }

    public TaskDeadlineDigestJob? Job { get; set; }
    public TaskDeadlineDigestAttempt? RestartedFromAttempt { get; set; }
    public User? RequestedByUser { get; set; }
    public ICollection<TaskDeadlineDigestAttempt> RestartAttempts { get; } = new List<TaskDeadlineDigestAttempt>();
}
