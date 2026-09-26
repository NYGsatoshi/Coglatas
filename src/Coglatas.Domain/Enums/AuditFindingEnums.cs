namespace Coglatas.Domain.Enums;

public enum AuditFindingSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum AuditFindingTriageStatus
{
    Open = 0,
    Reviewing = 1,
    Resolved = 2,
    AcceptedRisk = 3,
    FalsePositive = 4
}

/// <summary>
/// Operational progress for resolving a Finding. This is deliberately separate
/// from both detector triage and the structured Review Decision aggregate.
/// </summary>
public enum AuditFindingWorkflowStatus
{
    Open = 0,
    InReview = 1,
    WaitingFix = 2,
    ReadyForReReview = 3,
    Done = 4
}
