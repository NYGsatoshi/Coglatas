using Coglatas.Application.Artifacts;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Audit;

public sealed partial class ArtifactEvidenceManifestServiceTests
{
    [Theory]
    [InlineData("null-claims", "Claims must contain between 1 and 200 items.")]
    [InlineData("empty-claims", "Claims must contain between 1 and 200 items.")]
    [InlineData("too-many-claims", "Claims must contain between 1 and 200 items.")]
    [InlineData("zero-claim-ordinal", "Claim ordinals must be positive and unique.")]
    [InlineData("negative-claim-ordinal", "Claim ordinals must be positive and unique.")]
    [InlineData("duplicate-claim-ordinal", "Claim ordinals must be positive and unique.")]
    [InlineData("null-claim-text", "Claim text is required and must be at most 4000 characters.")]
    [InlineData("blank-claim-text", "Claim text is required and must be at most 4000 characters.")]
    [InlineData("long-claim-text", "Claim text is required and must be at most 4000 characters.")]
    [InlineData("support-status", "Claim support/review status is invalid.")]
    [InlineData("review-status", "Claim support/review status is invalid.")]
    [InlineData("null-evidence", "Each claim may contain at most 20 evidence items.")]
    [InlineData("too-many-evidence", "Each claim may contain at most 20 evidence items.")]
    [InlineData("supported-without-evidence", "A supported claim requires at least one evidence item.")]
    [InlineData("zero-evidence-ordinal", "Evidence ordinals must be positive and unique within a claim.")]
    [InlineData("negative-evidence-ordinal", "Evidence ordinals must be positive and unique within a claim.")]
    [InlineData("duplicate-evidence-ordinal", "Evidence ordinals must be positive and unique within a claim.")]
    [InlineData("source-kind", "Evidence source kind is invalid.")]
    [InlineData("classification", "Evidence provenance classification/status is invalid.")]
    [InlineData("verification", "Evidence provenance classification/status is invalid.")]
    [InlineData("null-reference", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("blank-reference", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-reference", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("null-passage", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("blank-passage", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-passage", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-title", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-publisher", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-type", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-hash", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-version", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("long-location", "Evidence snapshot fields exceed their bounded contract.")]
    [InlineData("published-after-retrieved", "Evidence published time cannot be after its retrieved time.")]
    [InlineData("conflicting-provenance", "Repeated references to the same evidence source must use identical source-level provenance metadata.")]
    [InlineData("finding-severity", "Finding severity or confidence is invalid.")]
    [InlineData("negative-confidence", "Finding severity or confidence is invalid.")]
    [InlineData("excessive-confidence", "Finding severity or confidence is invalid.")]
    [InlineData("blank-detector", "Finding detector key and policy version are required and bounded.")]
    [InlineData("long-detector", "Finding detector key and policy version are required and bounded.")]
    [InlineData("blank-policy", "Finding detector key and policy version are required and bounded.")]
    [InlineData("long-policy", "Finding detector key and policy version are required and bounded.")]
    public async Task InvalidManifestRetainsBothVersionGraphsWithoutPartialClaimsOrEffects(
        string invalidCase,
        string expectedMessage)
    {
        await using var fixture = await Fixture.CreateAsync(canReview: true, canUpdateArtifact: true);
        var before = SelectedGraphSnapshot(fixture);

        var result = await fixture.Service.AttachAsync(fixture.Version.Id, InvalidManifest(invalidCase));

        Assert.False(result.IsSuccess);
        Assert.Equal("ValidationFailed", result.ErrorDetail?.Code);
        Assert.Equal(expectedMessage, result.ErrorDetail?.Message);
        Assert.Equal(before, SelectedGraphSnapshot(fixture));
        Assert.Empty(await fixture.Evidence.ListClaimsAsync(fixture.Version.Id));
        Assert.Empty(await fixture.Evidence.ListClaimsAsync(fixture.SecondVersion.Id));
        Assert.Empty(await fixture.ManifestContext.Set<ArtifactClaim>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.ManifestContext.Set<ArtifactEvidence>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.ManifestContext.Set<ArtifactFinding>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.ManifestContext.Set<AuditFindingHistory>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
        Assert.DoesNotContain(fixture.ManifestContext.ChangeTracker.Entries(),
            entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        Assert.Equal(1, await fixture.ManifestContext.Set<Artifact>().IgnoreQueryFilters().AsNoTracking().CountAsync());
        Assert.Equal(2, await fixture.ManifestContext.Set<ArtifactVersion>().IgnoreQueryFilters().AsNoTracking().CountAsync());
        Assert.Equal(2, await fixture.ManifestContext.Set<Attachment>().IgnoreQueryFilters().AsNoTracking().CountAsync());
        Assert.Equal(2, await fixture.ManifestContext.Set<FileObject>().IgnoreQueryFilters().AsNoTracking().CountAsync());
        Assert.Empty(fixture.AuditLogger.Entries);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task ExactSnapshotAndFindingBoundsRetainTwentyEvidenceItemsAndInitialHistory(int confidence)
    {
        await using var fixture = await Fixture.CreateAsync(canReview: true, canUpdateArtifact: true);
        var actorId = await fixture.AssignDistinctManifestCreatorAsync();
        Assert.NotEqual(actorId, fixture.Artifact.CreatedByUserId);
        Assert.NotEqual(actorId, fixture.Version.CreatedByUserId);
        Assert.NotEqual(actorId, fixture.SecondVersion.CreatedByUserId);
        var before = SelectedGraphSnapshot(fixture);
        var versionId = fixture.Version.Id;
        var projectId = fixture.Artifact.ProjectId;
        var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var evidence = new ArtifactEvidenceManifestItem(
            1, " websnapshot ", " " + new string('r', 2048) + " ",
            " " + new string('t', 512) + " ", " " + new string('p', 4000) + " ",
            " " + new string('l', 512) + " ", null,
            " " + new string('u', 512) + " ", " " + new string('y', 128) + " ",
            " primary ", timestamp, timestamp, " " + new string('h', 256) + " ",
            " " + new string('v', 256) + " ", " verified ");
        var request = new AttachArtifactEvidenceManifestRequest([
            new ArtifactClaimManifestItem(1, " " + new string('c', 4000) + " ", true,
                " supported ", " reviewed ",
                Enumerable.Range(1, 20).Select(ordinal => evidence with { Ordinal = ordinal }).ToArray(),
                new ArtifactFindingManifestItem(" low ", confidence,
                    " " + new string('d', 128) + " ", " " + new string('q', 128) + " "))]);

        var result = await fixture.Service.AttachAsync(versionId, request);

        Assert.True(result.IsSuccess, result.Error ?? result.ErrorDetail?.Message);
        Assert.Equal(versionId, result.Value!.ArtifactVersionId);
        Assert.Equal(1, result.Value.ClaimCount);
        Assert.Equal(before, SelectedGraphSnapshot(fixture));
        var claim = Assert.Single(await fixture.Evidence.ListClaimsAsync(versionId));
        Assert.Equal(versionId, claim.ArtifactVersionId);
        Assert.Equal(fixture.TenantId, claim.TenantId);
        Assert.Equal(new string('c', 4000), claim.Text);
        Assert.True(claim.CitationPresent);
        Assert.Equal(ArtifactClaimSupportStatus.Supported, claim.SupportStatus);
        Assert.Equal(ArtifactClaimReviewStatus.Reviewed, claim.ReviewStatus);
        Assert.Equal(Enumerable.Range(1, 20), claim.Evidence.Select(item => item.Ordinal).Order());
        Assert.All(claim.Evidence, item =>
        {
            Assert.Equal(fixture.TenantId, item.TenantId);
            Assert.Equal(claim.Id, item.ArtifactClaimId);
            Assert.Equal(ArtifactEvidenceSourceKind.WebSnapshot, item.SourceKind);
            Assert.Equal(new string('r', 2048), item.SourceReference);
            Assert.Equal(new string('t', 512), item.SourceTitleSnapshot);
            Assert.Equal(new string('p', 4000), item.PassageSnapshot);
            Assert.Equal(new string('l', 512), item.LocationSnapshot);
            Assert.Equal(new string('u', 512), item.SourcePublisherSnapshot);
            Assert.Equal(new string('y', 128), item.SourceTypeSnapshot);
            Assert.Equal(ArtifactEvidenceSourceClassification.Primary, item.SourceClassification);
            Assert.Equal(timestamp, item.PublishedAtSnapshot);
            Assert.Equal(timestamp, item.RetrievedAtSnapshot);
            Assert.Equal(new string('h', 256), item.ContentHashSnapshot);
            Assert.Equal(new string('v', 256), item.SourceVersionSnapshot);
            Assert.Equal(ArtifactEvidenceVerificationStatus.Verified, item.VerificationStatus);
            Assert.Null(item.SourceEventAuditId);
        });
        var finding = Assert.Single(await fixture.ManifestContext.Set<ArtifactFinding>()
            .AsNoTracking().Include(item => item.History).ToListAsync());
        Assert.Equal(claim.Id, finding.ArtifactClaimId);
        Assert.Equal(fixture.TenantId, finding.TenantId);
        Assert.Equal(AuditFindingSeverity.Low, finding.Severity);
        Assert.Equal(confidence, finding.ConfidencePercent);
        Assert.Equal(new string('d', 128), finding.DetectorKey);
        Assert.Equal(new string('q', 128), finding.PolicyVersion);
        Assert.Equal(AuditFindingTriageStatus.Open, finding.Status);
        var history = Assert.Single(finding.History);
        Assert.Equal(fixture.TenantId, history.TenantId);
        Assert.Equal(finding.Id, history.ArtifactFindingId);
        Assert.Equal(actorId, history.ChangedByUserId);
        Assert.Null(history.FromStatus);
        Assert.Equal(AuditFindingTriageStatus.Open, history.ToStatus);
        Assert.Null(history.OwnerUserId);
        Assert.Null(history.Reason);
        Assert.Empty(await fixture.Evidence.ListClaimsAsync(fixture.SecondVersion.Id));
        var audit = Assert.Single(fixture.AuditLogger.Entries);
        Assert.Equal("ArtifactClaimsEvidenceAttached", audit.Action);
        Assert.Equal("ArtifactVersion", audit.EntityType);
        Assert.Equal(versionId, audit.EntityId);
        Assert.Equal(fixture.TenantId, audit.TenantId);
        Assert.Equal(actorId, audit.ActorUserId);
        Assert.Equal(projectId, audit.ProjectId);
        Assert.Equal(1, audit.Metadata!["claimCount"]);
        Assert.Equal(1, audit.Metadata["findingCount"]);
        Assert.Equal("artifact-claims-evidence-v1", audit.Metadata["schema"]);
        Assert.Equal("audit-findings-v1", audit.Metadata["findingSchema"]);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    private static AttachArtifactEvidenceManifestRequest InvalidManifest(string invalidCase)
    {
        var request = WebSnapshotRequest();
        var finding = new ArtifactFindingManifestItem(nameof(AuditFindingSeverity.Low), 50, "manifest-detector", "policy-v1");
        var first = request.Claims[0] with { Finding = finding };
        var claim = first with { Ordinal = 2, Finding = null };
        var evidence = claim.Evidence[0];
        switch (invalidCase)
        {
            case "null-claims": return request with { Claims = null! };
            case "empty-claims": return request with { Claims = [] };
            case "too-many-claims": return request with
                { Claims = Enumerable.Range(1, 201).Select(ordinal => first with { Ordinal = ordinal }).ToArray() };
            case "zero-claim-ordinal": claim = claim with { Ordinal = 0 }; break;
            case "negative-claim-ordinal": claim = claim with { Ordinal = -1 }; break;
            case "duplicate-claim-ordinal": claim = claim with { Ordinal = 1 }; break;
            case "null-claim-text": claim = claim with { Text = null! }; break;
            case "blank-claim-text": claim = claim with { Text = " \t\r\n " }; break;
            case "long-claim-text": claim = claim with { Text = new string('c', 4001) }; break;
            case "support-status": claim = claim with { SupportStatus = "unknown-status" }; break;
            case "review-status": claim = claim with { ReviewStatus = "unknown-status" }; break;
            case "null-evidence": claim = claim with { Evidence = null! }; break;
            case "too-many-evidence": claim = claim with
                { Evidence = Enumerable.Range(1, 21).Select(ordinal => evidence with { Ordinal = ordinal }).ToArray() }; break;
            case "supported-without-evidence": claim = claim with { Evidence = [] }; break;
            case "zero-evidence-ordinal": evidence = evidence with { Ordinal = 0 }; break;
            case "negative-evidence-ordinal": evidence = evidence with { Ordinal = -1 }; break;
            case "duplicate-evidence-ordinal": claim = claim with { Evidence = [evidence, evidence] }; break;
            case "source-kind": evidence = evidence with { SourceKind = "unknown-source" }; break;
            case "classification": evidence = evidence with { SourceClassification = "unknown-classification" }; break;
            case "verification": evidence = evidence with { VerificationStatus = "unknown-verification" }; break;
            case "null-reference": evidence = evidence with { SourceReference = null! }; break;
            case "blank-reference": evidence = evidence with { SourceReference = " \t " }; break;
            case "long-reference": evidence = evidence with { SourceReference = new string('r', 2049) }; break;
            case "null-passage": evidence = evidence with { PassageSnapshot = null! }; break;
            case "blank-passage": evidence = evidence with { PassageSnapshot = " \r\n " }; break;
            case "long-passage": evidence = evidence with { PassageSnapshot = new string('p', 4001) }; break;
            case "long-title": evidence = evidence with { SourceTitleSnapshot = new string('t', 513) }; break;
            case "long-publisher": evidence = evidence with { SourcePublisherSnapshot = new string('u', 513) }; break;
            case "long-type": evidence = evidence with { SourceTypeSnapshot = new string('y', 129) }; break;
            case "long-hash": evidence = evidence with { ContentHashSnapshot = new string('h', 257) }; break;
            case "long-version": evidence = evidence with { SourceVersionSnapshot = new string('v', 257) }; break;
            case "long-location": evidence = evidence with { LocationSnapshot = new string('l', 513) }; break;
            case "published-after-retrieved": evidence = evidence with
                { PublishedAtSnapshot = DateTimeOffset.UnixEpoch.AddSeconds(1), RetrievedAtSnapshot = DateTimeOffset.UnixEpoch }; break;
            case "conflicting-provenance": evidence = evidence with { SourceTitleSnapshot = "Conflicting title" }; break;
            case "finding-severity": claim = claim with { Finding = finding with { Severity = "unknown-severity" } }; break;
            case "negative-confidence": claim = claim with { Finding = finding with { ConfidencePercent = -1 } }; break;
            case "excessive-confidence": claim = claim with { Finding = finding with { ConfidencePercent = 101 } }; break;
            case "blank-detector": claim = claim with { Finding = finding with { DetectorKey = " \t " } }; break;
            case "long-detector": claim = claim with { Finding = finding with { DetectorKey = new string('d', 129) } }; break;
            case "blank-policy": claim = claim with { Finding = finding with { PolicyVersion = " \r\n " } }; break;
            case "long-policy": claim = claim with { Finding = finding with { PolicyVersion = new string('q', 129) } }; break;
            default: throw new ArgumentOutOfRangeException(nameof(invalidCase));
        }

        if (invalidCase is not ("null-evidence" or "too-many-evidence" or "supported-without-evidence" or "duplicate-evidence-ordinal"))
        {
            claim = claim with { Evidence = [evidence] };
        }
        return request with { Claims = [first, claim] };
    }

    private static object?[] SelectedGraphSnapshot(Fixture fixture) =>
    [
        fixture.Artifact.Id, fixture.Artifact.TenantId, fixture.Artifact.ProjectId,
        fixture.Artifact.TaskItemId, fixture.Artifact.Name, fixture.Artifact.Description,
        fixture.Artifact.ArtifactType, fixture.Artifact.Status, fixture.Artifact.CurrentVersionId,
        fixture.Artifact.CreatedByUserId, fixture.Artifact.DeletedAt,
        fixture.Artifact.CreatedAt, fixture.Artifact.UpdatedAt, fixture.Artifact.DeletedByUserId, fixture.Artifact.DeleteReason,
        fixture.Version.Id, fixture.Version.TenantId, fixture.Version.ArtifactId,
        fixture.Version.VersionNumber, fixture.Version.AttachmentId, fixture.Version.FileObjectId,
        fixture.Version.Notes, fixture.Version.CreatedByUserId, fixture.Version.DeletedAt,
        fixture.Version.CreatedAt, fixture.Version.UpdatedAt, fixture.Version.DeletedByUserId, fixture.Version.DeleteReason,
        fixture.SecondVersion.Id, fixture.SecondVersion.TenantId, fixture.SecondVersion.ArtifactId,
        fixture.SecondVersion.VersionNumber, fixture.SecondVersion.AttachmentId, fixture.SecondVersion.FileObjectId,
        fixture.SecondVersion.Notes, fixture.SecondVersion.CreatedByUserId, fixture.SecondVersion.DeletedAt,
        fixture.SecondVersion.CreatedAt, fixture.SecondVersion.UpdatedAt,
        fixture.SecondVersion.DeletedByUserId, fixture.SecondVersion.DeleteReason
    ];

    private sealed partial class Fixture
    {
        public AppDbContext ManifestContext => Context;

        public async Task<Guid> AssignDistinctManifestCreatorAsync()
        {
            var callerId = CurrentUser.UserId ?? throw new InvalidOperationException("The fixture requires a current caller.");
            var creatorId = Guid.NewGuid();
            Artifact.CreatedByUserId = creatorId;
            Version.CreatedByUserId = creatorId;
            SecondVersion.CreatedByUserId = creatorId;
            await Context.SaveChangesAsync();
            return callerId;
        }
    }
}
