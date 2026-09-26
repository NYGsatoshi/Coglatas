namespace Coglatas.Application.Audit;

public sealed record AuditFindingDecisionOptionResponse(
    string Decision,
    string Label,
    bool RationaleRequired);

public sealed record AuditFindingDecisionHistoryResponse(
    Guid DecisionId,
    string Decision,
    string? PreviousDecision,
    string? Rationale,
    Guid ReviewerUserId,
    string ReviewerDisplayName,
    DateTimeOffset Timestamp);

public sealed record AuditFindingDecisionResponse(
    Guid FindingId,
    bool ReviewCompleted,
    bool CanReview,
    AuditFindingDecisionHistoryResponse? CurrentDecision,
    IReadOnlyList<AuditFindingDecisionHistoryResponse> History,
    IReadOnlyList<AuditFindingDecisionOptionResponse> Options);

public sealed record SaveAuditFindingDecisionRequest(
    string Decision,
    string? Rationale = null);
