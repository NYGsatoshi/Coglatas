namespace Coglatas.Application.Artifacts;

public sealed record AttachArtifactEvidenceManifestRequest(
    IReadOnlyList<ArtifactClaimManifestItem> Claims);

public sealed record ArtifactClaimManifestItem(
    int Ordinal,
    string Text,
    bool CitationPresent,
    string SupportStatus,
    string ReviewStatus,
    IReadOnlyList<ArtifactEvidenceManifestItem> Evidence,
    ArtifactFindingManifestItem? Finding = null);

public sealed record ArtifactEvidenceManifestItem(
    int Ordinal,
    string SourceKind,
    string SourceReference,
    string? SourceTitleSnapshot,
    string PassageSnapshot,
    string? LocationSnapshot,
    Guid? SourceEventAuditId,
    string? SourcePublisherSnapshot = null,
    string? SourceTypeSnapshot = null,
    string? SourceClassification = null,
    DateTimeOffset? PublishedAtSnapshot = null,
    DateTimeOffset? RetrievedAtSnapshot = null,
    string? ContentHashSnapshot = null,
    string? SourceVersionSnapshot = null,
    string? VerificationStatus = null);

public sealed record ArtifactFindingManifestItem(
    string Severity,
    int ConfidencePercent,
    string DetectorKey,
    string PolicyVersion);

public sealed record ArtifactEvidenceManifestResponse(
    Guid ArtifactVersionId,
    int ClaimCount);
