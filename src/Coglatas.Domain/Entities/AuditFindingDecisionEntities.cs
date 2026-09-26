using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

/// <summary>
/// One immutable structured review-decision revision for a canonical Audit Finding.
/// Current review state is the newest row; previous rows are retained append-only.
/// </summary>
public sealed class AuditFindingDecision : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactFindingId { get; set; }
    public AuditFindingReviewDecision Decision { get; set; }
    public AuditFindingReviewDecision? PreviousDecision { get; set; }
    public string? Rationale { get; set; }
    public Guid ReviewerUserId { get; set; }
    public string ReviewerDisplayName { get; set; } = string.Empty;

    public ArtifactFinding? Finding { get; set; }
}
