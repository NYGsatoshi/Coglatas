using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Immutable detector/policy finding bound to one canonical ArtifactClaim.
/// Triage and operational workflow fields are mutable; the parent Claim/Evidence
/// projection remains immutable.
/// </summary>
public sealed class ArtifactFinding : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactClaimId { get; set; }
    public AuditFindingSeverity Severity { get; set; }
    public int ConfidencePercent { get; set; }
    public string DetectorKey { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public AuditFindingTriageStatus Status { get; set; } = AuditFindingTriageStatus.Open;
    public AuditFindingWorkflowStatus WorkflowStatus { get; set; } = AuditFindingWorkflowStatus.Open;
    public Guid? OwnerUserId { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? ResolutionReason { get; set; }

    public ArtifactClaim? ArtifactClaim { get; set; }
    public ICollection<AuditFindingHistory> History { get; } = new List<AuditFindingHistory>();
    public ICollection<AuditFindingWorkflowHistory> WorkflowHistory { get; } = new List<AuditFindingWorkflowHistory>();
}

/// <summary>
/// Append-only audit-facing triage transition history for one finding.
/// </summary>
public sealed class AuditFindingHistory : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactFindingId { get; set; }
    public AuditFindingTriageStatus? FromStatus { get; set; }
    public AuditFindingTriageStatus ToStatus { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? Reason { get; set; }
    public Guid ChangedByUserId { get; set; }

    public ArtifactFinding? Finding { get; set; }
}

/// <summary>
/// Append-only operational workflow history. Owner, due date, and workflow
/// progress are versioned together so the review queue can reconstruct exactly
/// how responsibility changed without conflating it with triage or Decision.
/// </summary>
public sealed class AuditFindingWorkflowHistory : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactFindingId { get; set; }
    public AuditFindingWorkflowStatus FromWorkflowStatus { get; set; }
    public AuditFindingWorkflowStatus ToWorkflowStatus { get; set; }
    public Guid? FromOwnerUserId { get; set; }
    public Guid? ToOwnerUserId { get; set; }
    public DateOnly? FromDueDate { get; set; }
    public DateOnly? ToDueDate { get; set; }
    public Guid ChangedByUserId { get; set; }

    public ArtifactFinding? Finding { get; set; }
}
