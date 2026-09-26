namespace Coglatas.Application.Audit;

public sealed record AuditFindingsQuery(
    Guid ArtifactVersionId,
    string? Status = null,
    string? Severity = null,
    bool OpenOnly = false,
    string? WorkflowStatus = null,
    bool MyReviews = false,
    bool Overdue = false,
    bool Unassigned = false);

public sealed record AuditFindingHistoryResponse(
    string? FromStatus,
    string ToStatus,
    string? Reason,
    DateTimeOffset ChangedAt);

public sealed record AuditFindingWorkflowHistoryResponse(
    string FromWorkflowStatus,
    string ToWorkflowStatus,
    Guid? FromOwnerUserId,
    string? FromOwnerDisplayName,
    Guid? ToOwnerUserId,
    string? ToOwnerDisplayName,
    DateOnly? FromDueDate,
    DateOnly? ToDueDate,
    DateTimeOffset ChangedAt);

public sealed record AuditFindingOwnerResponse(
    Guid UserId,
    string DisplayName);

public sealed record AuditFindingResponse(
    Guid FindingId,
    Guid ClaimId,
    int ClaimOrdinal,
    string ClaimText,
    string Severity,
    int ConfidencePercent,
    string DetectorKey,
    string PolicyVersion,
    string Status,
    string WorkflowStatus,
    Guid? OwnerUserId,
    string? OwnerDisplayName,
    DateOnly? DueDate,
    bool IsOverdue,
    string? ResolutionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    Guid? RelatedEvidenceId,
    Guid? RelatedEventId,
    IReadOnlyList<AuditFindingHistoryResponse> History,
    IReadOnlyList<AuditFindingWorkflowHistoryResponse> WorkflowHistory);

public sealed record AuditFindingsResponse(
    Guid ArtifactId,
    Guid ArtifactVersionId,
    int ArtifactVersionNumber,
    string ArtifactTitle,
    bool CanReview,
    IReadOnlyList<AuditFindingOwnerResponse> EligibleOwners,
    IReadOnlyList<AuditFindingResponse> Findings);

public sealed record UpdateAuditFindingTriageRequest(
    string Status,
    string? Reason = null,
    Guid? OwnerUserId = null,
    bool AssignOwner = false);

public sealed record UpdateAuditFindingWorkflowRequest(
    string WorkflowStatus,
    Guid? OwnerUserId = null,
    bool AssignOwner = false,
    DateOnly? DueDate = null,
    bool SetDueDate = false);

public sealed record MentionAuditFindingReviewerRequest(
    Guid UserId,
    Guid RequestId);
