using Coglatas.Application.Audit;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Artifacts;

public sealed class ArtifactEvidenceManifestService(
    IArtifactRepository artifacts,
    IArtifactEvidenceRepository evidenceRepository,
    IArtifactAuthorizationService artifactAuthorization,
    IFileRepository files,
    IFileAuthorizationService fileAuthorization,
    IAuditAuthorizationService auditAuthorization,
    ICurrentUser currentUser,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork) : IArtifactEvidenceManifestService
{
    private const int MaxClaims = 200;
    private const int MaxEvidencePerClaim = 20;
    private const int MaxClaimText = 4000;
    private const int MaxSourceReference = 2048;
    private const int MaxSourceTitle = 512;
    private const int MaxSourcePublisher = 512;
    private const int MaxSourceType = 128;
    private const int MaxContentHash = 256;
    private const int MaxSourceVersion = 256;
    private const int MaxPassage = 4000;
    private const int MaxLocation = 512;
    private const int MaxDetectorKey = 128;
    private const int MaxPolicyVersion = 128;

    public async Task<Result<ArtifactEvidenceManifestResponse>> AttachAsync(
        Guid artifactVersionId,
        AttachArtifactEvidenceManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Failure("AuthenticationRequired", "Authentication is required.");
        }

        var auditReview = await auditAuthorization.AuthorizeAsync(
            CapabilityKeys.AuditReview,
            "audit.claims-evidence.attach",
            cancellationToken);
        if (!auditReview.IsSuccess)
        {
            return AuthorizationFailure(auditReview);
        }

        var version = await artifacts.GetVersionAsync(artifactVersionId, cancellationToken);
        if (version?.Artifact is null || version.DeletedAt.HasValue || version.Artifact.DeletedAt.HasValue ||
            !await artifactAuthorization.CanUpdateArtifact(userId, version.ArtifactId, cancellationToken))
        {
            return Failure("ArtifactVersionNotFound", "The artifact version is not available.");
        }

        if (request.Claims is null || request.Claims.Count is < 1 or > MaxClaims)
        {
            return Failure("ValidationFailed", $"Claims must contain between 1 and {MaxClaims} items.");
        }

        if (await evidenceRepository.HasClaimsAsync(artifactVersionId, cancellationToken))
        {
            return Failure("EvidenceManifestAlreadyAttached", "Claims and evidence are immutable once attached to an artifact version.");
        }

        var claimOrdinals = new HashSet<int>();
        // MaxClaims is the repository-owned execution/allocation ceiling. The
        // request count is validated above, but must not control allocation size.
        var claims = new List<ArtifactClaim>(MaxClaims);
        var provenanceBySource = new Dictionary<(ArtifactEvidenceSourceKind Kind, string Reference), SourceProvenanceContract>();
        var findingCount = 0;
        // The request count is only an end marker for already-validated input.
        // MaxClaims remains the execution bound even if a custom collection
        // implementation is supplied to the application boundary.
        var requestClaimCount = request.Claims.Count;
        for (var claimIndex = 0; claimIndex < MaxClaims; claimIndex++)
        {
            if (claimIndex == requestClaimCount)
            {
                break;
            }

            var item = request.Claims[claimIndex];
            if (item.Ordinal <= 0 || !claimOrdinals.Add(item.Ordinal))
            {
                return Failure("ValidationFailed", "Claim ordinals must be positive and unique.");
            }

            var claimText = NormalizeRequired(item.Text);
            if (claimText is null || claimText.Length > MaxClaimText)
            {
                return Failure("ValidationFailed", $"Claim text is required and must be at most {MaxClaimText} characters.");
            }

            if (!TryParseEnum(item.SupportStatus, out ArtifactClaimSupportStatus supportStatus) ||
                !TryParseEnum(item.ReviewStatus, out ArtifactClaimReviewStatus reviewStatus))
            {
                return Failure("ValidationFailed", "Claim support/review status is invalid.");
            }

            if (item.Evidence is null || item.Evidence.Count > MaxEvidencePerClaim)
            {
                return Failure("ValidationFailed", $"Each claim may contain at most {MaxEvidencePerClaim} evidence items.");
            }

            if (supportStatus == ArtifactClaimSupportStatus.Supported && item.Evidence.Count == 0)
            {
                return Failure("ValidationFailed", "A supported claim requires at least one evidence item.");
            }

            var claim = new ArtifactClaim
            {
                TenantId = version.TenantId,
                ArtifactVersionId = version.Id,
                LogicalClaimId = Guid.NewGuid(),
                Ordinal = item.Ordinal,
                Text = claimText,
                CitationPresent = item.CitationPresent,
                SupportStatus = supportStatus,
                ReviewStatus = reviewStatus,
                ArtifactVersion = version
            };

            var evidenceOrdinals = new HashSet<int>();
            foreach (var evidenceItem in item.Evidence)
            {
                if (evidenceItem.Ordinal <= 0 || !evidenceOrdinals.Add(evidenceItem.Ordinal))
                {
                    return Failure("ValidationFailed", "Evidence ordinals must be positive and unique within a claim.");
                }

                if (!TryParseEnum(evidenceItem.SourceKind, out ArtifactEvidenceSourceKind sourceKind))
                {
                    return Failure("ValidationFailed", "Evidence source kind is invalid.");
                }

                if (!TryParseOptionalEnum(
                        evidenceItem.SourceClassification,
                        ArtifactEvidenceSourceClassification.Unknown,
                        out ArtifactEvidenceSourceClassification sourceClassification) ||
                    !TryParseOptionalEnum(
                        evidenceItem.VerificationStatus,
                        ArtifactEvidenceVerificationStatus.Unverified,
                        out ArtifactEvidenceVerificationStatus verificationStatus))
                {
                    return Failure("ValidationFailed", "Evidence provenance classification/status is invalid.");
                }

                var sourceReference = NormalizeRequired(evidenceItem.SourceReference);
                var passage = NormalizeRequired(evidenceItem.PassageSnapshot);
                var sourceTitle = NormalizeOptional(evidenceItem.SourceTitleSnapshot);
                var sourcePublisher = NormalizeOptional(evidenceItem.SourcePublisherSnapshot);
                var sourceType = NormalizeOptional(evidenceItem.SourceTypeSnapshot);
                var contentHash = NormalizeOptional(evidenceItem.ContentHashSnapshot);
                var sourceVersion = NormalizeOptional(evidenceItem.SourceVersionSnapshot);
                var location = NormalizeOptional(evidenceItem.LocationSnapshot);
                if (sourceReference is null || sourceReference.Length > MaxSourceReference ||
                    passage is null || passage.Length > MaxPassage ||
                    sourceTitle?.Length > MaxSourceTitle ||
                    sourcePublisher?.Length > MaxSourcePublisher ||
                    sourceType?.Length > MaxSourceType ||
                    contentHash?.Length > MaxContentHash ||
                    sourceVersion?.Length > MaxSourceVersion ||
                    location?.Length > MaxLocation)
                {
                    return Failure("ValidationFailed", "Evidence snapshot fields exceed their bounded contract.");
                }

                if (evidenceItem.PublishedAtSnapshot.HasValue && evidenceItem.RetrievedAtSnapshot.HasValue &&
                    evidenceItem.PublishedAtSnapshot.Value > evidenceItem.RetrievedAtSnapshot.Value)
                {
                    return Failure("ValidationFailed", "Evidence published time cannot be after its retrieved time.");
                }

                var provenance = new SourceProvenanceContract(
                    sourceTitle,
                    sourcePublisher,
                    sourceType,
                    sourceClassification,
                    evidenceItem.PublishedAtSnapshot,
                    evidenceItem.RetrievedAtSnapshot,
                    contentHash,
                    sourceVersion,
                    verificationStatus);
                var sourceKey = (sourceKind, sourceReference);
                if (provenanceBySource.TryGetValue(sourceKey, out var existingProvenance) &&
                    existingProvenance != provenance)
                {
                    return Failure(
                        "ValidationFailed",
                        "Repeated references to the same evidence source must use identical source-level provenance metadata.");
                }
                provenanceBySource[sourceKey] = provenance;

                if (!await CanAttachSourceAsync(userId, sourceKind, sourceReference, cancellationToken))
                {
                    return Failure("SourceNotAuthorized", "The evidence source is not available in the current authorization scope.");
                }

                claim.Evidence.Add(new ArtifactEvidence
                {
                    TenantId = version.TenantId,
                    ArtifactClaimId = claim.Id,
                    Ordinal = evidenceItem.Ordinal,
                    SourceKind = sourceKind,
                    SourceReference = sourceReference,
                    SourceTitleSnapshot = sourceTitle,
                    SourcePublisherSnapshot = sourcePublisher,
                    SourceTypeSnapshot = sourceType,
                    SourceClassification = sourceClassification,
                    PublishedAtSnapshot = evidenceItem.PublishedAtSnapshot,
                    RetrievedAtSnapshot = evidenceItem.RetrievedAtSnapshot,
                    ContentHashSnapshot = contentHash,
                    SourceVersionSnapshot = sourceVersion,
                    VerificationStatus = verificationStatus,
                    PassageSnapshot = passage,
                    LocationSnapshot = location,
                    SourceEventAuditId = evidenceItem.SourceEventAuditId,
                    ArtifactClaim = claim
                });
            }

            if (item.Finding is not null)
            {
                if (!TryParseEnum(item.Finding.Severity, out AuditFindingSeverity severity) ||
                    item.Finding.ConfidencePercent is < 0 or > 100)
                {
                    return Failure("ValidationFailed", "Finding severity or confidence is invalid.");
                }

                var detectorKey = NormalizeRequired(item.Finding.DetectorKey);
                var policyVersion = NormalizeRequired(item.Finding.PolicyVersion);
                if (detectorKey is null || detectorKey.Length > MaxDetectorKey ||
                    policyVersion is null || policyVersion.Length > MaxPolicyVersion)
                {
                    return Failure("ValidationFailed", "Finding detector key and policy version are required and bounded.");
                }

                var finding = new ArtifactFinding
                {
                    TenantId = version.TenantId,
                    ArtifactClaimId = claim.Id,
                    Severity = severity,
                    ConfidencePercent = item.Finding.ConfidencePercent,
                    DetectorKey = detectorKey,
                    PolicyVersion = policyVersion,
                    Status = AuditFindingTriageStatus.Open,
                    ArtifactClaim = claim
                };
                finding.History.Add(new AuditFindingHistory
                {
                    TenantId = version.TenantId,
                    ArtifactFindingId = finding.Id,
                    FromStatus = null,
                    ToStatus = AuditFindingTriageStatus.Open,
                    OwnerUserId = null,
                    Reason = null,
                    ChangedByUserId = userId,
                    Finding = finding
                });
                claim.Finding = finding;
                findingCount += 1;
            }

            claims.Add(claim);
        }

        await evidenceRepository.AddClaimsAsync(claims, cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            "ArtifactClaimsEvidenceAttached",
            "ArtifactVersion",
            version.Id,
            "Artifact claim evidence manifest attached.",
            ProjectId: version.Artifact.ProjectId,
            TenantId: version.TenantId,
            Metadata: new Dictionary<string, object?>
            {
                ["claimCount"] = claims.Count,
                ["findingCount"] = findingCount,
                ["schema"] = "artifact-claims-evidence-v1",
                ["findingSchema"] = "audit-findings-v1"
            }), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ArtifactEvidenceManifestResponse>.Success(
            new ArtifactEvidenceManifestResponse(version.Id, claims.Count));
    }

    private async Task<bool> CanAttachSourceAsync(
        Guid userId,
        ArtifactEvidenceSourceKind sourceKind,
        string sourceReference,
        CancellationToken cancellationToken)
    {
        if (sourceKind == ArtifactEvidenceSourceKind.WebSnapshot)
        {
            return true;
        }

        if (!Guid.TryParse(sourceReference, out var sourceId) || sourceId == Guid.Empty)
        {
            return false;
        }

        if (sourceKind == ArtifactEvidenceSourceKind.ArtifactVersion)
        {
            return await artifactAuthorization.CanDownloadArtifactVersion(userId, sourceId, cancellationToken);
        }

        var attachment = await files.GetAttachmentAsync(sourceId, cancellationToken);
        return attachment is not null &&
            !attachment.DeletedAt.HasValue &&
            await fileAuthorization.CanViewAttachment(userId, attachment, cancellationToken);
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }

    private static bool TryParseEnum<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed);

    private static bool TryParseOptionalEnum<TEnum>(string? value, TEnum defaultValue, out TEnum parsed)
        where TEnum : struct, Enum
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            parsed = defaultValue;
            return true;
        }

        return TryParseEnum(normalized, out parsed);
    }

    private static string? NormalizeRequired(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static Result<ArtifactEvidenceManifestResponse> AuthorizationFailure(Result denied) =>
        denied.ErrorDetail is not null
            ? Result<ArtifactEvidenceManifestResponse>.Failure(denied.ErrorDetail)
            : Result<ArtifactEvidenceManifestResponse>.Failure(denied.Error ?? "Audit review is not permitted.");

    private static Result<ArtifactEvidenceManifestResponse> Failure(string code, string message) =>
        Result<ArtifactEvidenceManifestResponse>.Failure(new ApplicationErrorDetail(code, message));

    private sealed record SourceProvenanceContract(
        string? SourceTitle,
        string? Publisher,
        string? SourceType,
        ArtifactEvidenceSourceClassification Classification,
        DateTimeOffset? PublishedAt,
        DateTimeOffset? RetrievedAt,
        string? ContentHash,
        string? SourceVersion,
        ArtifactEvidenceVerificationStatus VerificationStatus);
}
