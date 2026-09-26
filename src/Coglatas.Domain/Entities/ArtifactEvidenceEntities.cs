using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Immutable verification claim owned by one ArtifactVersion.
/// </summary>
public sealed class ArtifactClaim : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactVersionId { get; set; }
    public Guid LogicalClaimId { get; set; } = Guid.NewGuid();
    public int Ordinal { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool CitationPresent { get; set; }
    public ArtifactClaimSupportStatus SupportStatus { get; set; } = ArtifactClaimSupportStatus.Unverified;
    public ArtifactClaimReviewStatus ReviewStatus { get; set; } = ArtifactClaimReviewStatus.Unreviewed;

    public ArtifactVersion? ArtifactVersion { get; set; }
    public ArtifactFinding? Finding { get; set; }
    public ICollection<ArtifactEvidence> Evidence { get; } = new List<ArtifactEvidence>();
}

/// <summary>Immutable structured report attached once to an ArtifactVersion.</summary>
public sealed class ArtifactReportDocument : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactVersionId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public ArtifactVersion? ArtifactVersion { get; set; }
    public ICollection<ArtifactReportSection> Sections { get; } = new List<ArtifactReportSection>();
}

public sealed class ArtifactReportSection : AuditableEntity, ITenantEntity
{
    public Guid LogicalSectionId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ArtifactReportDocumentId { get; set; }
    public int Ordinal { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public ArtifactReportDocument? Document { get; set; }
    public ICollection<ArtifactReportCitation> Citations { get; } = new List<ArtifactReportCitation>();
}

public sealed class ArtifactReportCitation : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactReportSectionId { get; set; }
    public int Ordinal { get; set; }
    public int AnchorStartUtf16 { get; set; }
    public int AnchorLengthUtf16 { get; set; }
    public Guid ArtifactClaimId { get; set; }
    public ArtifactReportSection? Section { get; set; }
    public ArtifactClaim? Claim { get; set; }
}

/// <summary>
/// Immutable, bounded source passage and provenance snapshot attached to a claim.
/// The SourceReference is opaque and never authorizes retrieval by itself.
/// </summary>
public sealed class ArtifactEvidence : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactClaimId { get; set; }
    public int Ordinal { get; set; }
    public ArtifactEvidenceSourceKind SourceKind { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public string? SourceTitleSnapshot { get; set; }
    public string? SourcePublisherSnapshot { get; set; }
    public string? SourceTypeSnapshot { get; set; }
    public ArtifactEvidenceSourceClassification SourceClassification { get; set; } = ArtifactEvidenceSourceClassification.Unknown;
    public DateTimeOffset? PublishedAtSnapshot { get; set; }
    public DateTimeOffset? RetrievedAtSnapshot { get; set; }
    public string? ContentHashSnapshot { get; set; }
    public string? SourceVersionSnapshot { get; set; }
    public ArtifactEvidenceVerificationStatus VerificationStatus { get; set; } = ArtifactEvidenceVerificationStatus.Unverified;
    public string PassageSnapshot { get; set; } = string.Empty;
    public string? LocationSnapshot { get; set; }
    public Guid? SourceEventAuditId { get; set; }

    public ArtifactClaim? ArtifactClaim { get; set; }
}
