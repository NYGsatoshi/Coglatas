using Coglatas.Domain.Common;

namespace Coglatas.Domain.Entities;

public enum FeedbackViewerGrantStatus
{
    Pending,
    Active,
    Declined,
    Revoked,
    Expired
}

public sealed class FeedbackInvitationPolicy : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public long VersionNo { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool ApprovedForAutomaticApproval { get; set; }
    public int DecisionWindowSeconds { get; set; }
    public int MaximumActiveSeconds { get; set; }
}

public sealed class FeedbackViewerGrant : AuditableEntity, ITenantEntity
{
    private FeedbackViewerGrant()
    {
    }

    public Guid TenantId { get; set; }
    public Guid PresentationId { get; private set; }
    public Guid InviterUserId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public Guid InvitationPolicyId { get; private init; }
    public long InvitationPolicyVersion { get; private init; }
    public FeedbackViewerGrantStatus Status { get; private set; }
    public long VersionNo { get; private set; } = 1;
    public int ActiveDurationSeconds { get; private init; }
    public DateTimeOffset DecisionDeadlineUtc { get; private init; }
    public DateTimeOffset? RecipientAcceptedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public string? TerminationReason { get; private set; }

    public static FeedbackViewerGrant Issue(Guid presentationId, Guid inviterId, Guid recipientId,
        FeedbackInvitationPolicy policy, int? activeDurationSeconds, DateTimeOffset utcNow)
    {
        var seconds = activeDurationSeconds ?? policy.MaximumActiveSeconds;
        if (presentationId == Guid.Empty || inviterId == Guid.Empty || recipientId == Guid.Empty ||
            inviterId == recipientId || !policy.Enabled || policy.DecisionWindowSeconds <= 0 ||
            seconds <= 0 || seconds > policy.MaximumActiveSeconds)
        {
            throw new InvalidOperationException("The requested invitation is outside its policy.");
        }

        return new FeedbackViewerGrant
        {
            TenantId = policy.TenantId,
            PresentationId = presentationId,
            InviterUserId = inviterId,
            RecipientUserId = recipientId,
            InvitationPolicyId = policy.Id,
            InvitationPolicyVersion = policy.VersionNo,
            DecisionDeadlineUtc = utcNow.AddSeconds(policy.DecisionWindowSeconds),
            ActiveDurationSeconds = seconds
        };
    }

    public bool IsActive(DateTimeOffset utcNow) =>
        Status == FeedbackViewerGrantStatus.Active && ExpiresAtUtc > utcNow;

    public bool ExpireIfElapsed(DateTimeOffset utcNow)
    {
        if (!(Status == FeedbackViewerGrantStatus.Pending && DecisionDeadlineUtc <= utcNow) &&
            !(Status == FeedbackViewerGrantStatus.Active && ExpiresAtUtc <= utcNow))
        {
            return false;
        }

        Status = FeedbackViewerGrantStatus.Expired;
        VersionNo++;
        return true;
    }

    public void Accept(FeedbackInvitationPolicy policy, long expectedVersionNo, DateTimeOffset utcNow)
    {
        RequirePending(policy, expectedVersionNo, utcNow);
        if (RecipientAcceptedAtUtc.HasValue)
        {
            throw new InvalidOperationException("The invitation was already accepted.");
        }

        RecipientAcceptedAtUtc = utcNow;
        UpdateActivation(policy, utcNow);
    }

    public void Approve(FeedbackInvitationPolicy policy, Guid approverId, long expectedVersionNo, DateTimeOffset utcNow)
    {
        RequirePending(policy, expectedVersionNo, utcNow);
        if (ApprovedAtUtc.HasValue || policy.ApprovedForAutomaticApproval || approverId == Guid.Empty)
        {
            throw new InvalidOperationException("The invitation cannot be approved.");
        }

        ApprovedByUserId = approverId;
        ApprovedAtUtc = utcNow;
        UpdateActivation(policy, utcNow);
    }

    public void Terminate(bool decline, string? reason, long expectedVersionNo, DateTimeOffset utcNow)
    {
        if (VersionNo != expectedVersionNo ||
            (Status != FeedbackViewerGrantStatus.Pending || DecisionDeadlineUtc <= utcNow) &&
            (decline || !IsActive(utcNow)))
        {
            throw new InvalidOperationException("The invitation has changed or already ended.");
        }

        Status = decline ? FeedbackViewerGrantStatus.Declined : FeedbackViewerGrantStatus.Revoked;
        TerminationReason = reason;
        VersionNo++;
    }

    private void RequirePending(FeedbackInvitationPolicy policy, long expectedVersionNo, DateTimeOffset utcNow)
    {
        if (VersionNo != expectedVersionNo || Status != FeedbackViewerGrantStatus.Pending ||
            DecisionDeadlineUtc <= utcNow || !policy.Enabled || InvitationPolicyId != policy.Id ||
            InvitationPolicyVersion != policy.VersionNo)
        {
            throw new InvalidOperationException("The invitation is no longer pending under the current policy.");
        }
    }

    private void UpdateActivation(FeedbackInvitationPolicy policy, DateTimeOffset utcNow)
    {
        if (RecipientAcceptedAtUtc.HasValue && (ApprovedAtUtc.HasValue || policy.ApprovedForAutomaticApproval))
        {
            Status = FeedbackViewerGrantStatus.Active;
            ExpiresAtUtc = utcNow.AddSeconds(ActiveDurationSeconds);
        }

        VersionNo++;
    }
}

public sealed class AnswerVisibilityDecision : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FormSubmissionValueId { get; init; }
    public bool IsHidden { get; private set; }
    public long VersionNo { get; private set; } = 1;
    public Guid? ChangedByUserId { get; private set; }
    public string? Reason { get; private set; }

    public void Decide(bool hide, Guid actorId, string reason, long expectedVersionNo)
    {
        if (VersionNo != expectedVersionNo || IsHidden == hide)
        {
            throw new InvalidOperationException("Answer visibility has changed or already matches the decision.");
        }

        IsHidden = hide;
        ChangedByUserId = actorId;
        Reason = reason;
        VersionNo = expectedVersionNo + 1;
    }
}

public enum FeedbackReportStatus
{
    Open,
    Resolved,
    Dismissed
}

public sealed class FeedbackReportCase : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PresentationId { get; init; }
    public Guid FormSubmissionValueId { get; init; }
    public Guid ReporterUserId { get; init; }
    public string Reason { get; init; } = "";
    public FeedbackReportStatus Status { get; private set; }
    public long VersionNo { get; private set; } = 1;
    public Guid? DisposedByUserId { get; private set; }
    public DateTimeOffset? DisposedAtUtc { get; private set; }
    public string? DispositionReason { get; private set; }

    public void Dispose(bool dismiss, Guid actorId, string reason, long expectedVersionNo, DateTimeOffset utcNow)
    {
        if (VersionNo != expectedVersionNo || Status != FeedbackReportStatus.Open)
        {
            throw new InvalidOperationException("The report has changed or is no longer open.");
        }

        Status = dismiss ? FeedbackReportStatus.Dismissed : FeedbackReportStatus.Resolved;
        DisposedByUserId = actorId;
        DisposedAtUtc = utcNow;
        DispositionReason = reason;
        VersionNo = expectedVersionNo + 1;
    }
}

public sealed class FeedbackReadState : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; init; }
    public Guid FormSubmissionId { get; init; }
    public long ObservedRevision { get; set; }
    public bool IsRead { get; set; }
}

public sealed class TeacherPrivateNote : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PresentationId { get; init; }
    public Guid AuthorUserId { get; init; }
    public string Text { get; init; } = "";
}

public sealed class SharedTeacherFeedback : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PresentationId { get; init; }
    public Guid AuthorUserId { get; init; }
    public string Text { get; init; } = "";
}
