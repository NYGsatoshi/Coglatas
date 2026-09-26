using Coglatas.Application.Audit;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Audit;

public sealed class AuditSourceIdentityTests
{
    [Fact]
    public void CreateReturnsStableOpaqueIdentifierForSameAuthorizedSource()
    {
        var first = AuditSourceIdentity.Create(
            ArtifactEvidenceSourceKind.WebSnapshot,
            " https://example.invalid/source ");
        var second = AuditSourceIdentity.Create(
            ArtifactEvidenceSourceKind.WebSnapshot,
            "https://example.invalid/source");

        Assert.Equal(first, second);
        Assert.Matches("^src_[0-9a-f]{24}$", first);
        Assert.DoesNotContain("example", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCanonicalizesGuidReferencesForRepositoryBackedSources()
    {
        const string lower = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
        const string upper = "AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE";

        var lowerId = AuditSourceIdentity.Create(ArtifactEvidenceSourceKind.FileAttachment, lower);
        var upperId = AuditSourceIdentity.Create(ArtifactEvidenceSourceKind.FileAttachment, upper);

        Assert.Equal(lowerId, upperId);
    }

    [Fact]
    public void CreateSeparatesDifferentSourceKindsEvenWhenReferenceMatches()
    {
        const string reference = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

        var attachment = AuditSourceIdentity.Create(ArtifactEvidenceSourceKind.FileAttachment, reference);
        var artifactVersion = AuditSourceIdentity.Create(ArtifactEvidenceSourceKind.ArtifactVersion, reference);

        Assert.NotEqual(attachment, artifactVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingReference(string reference)
    {
        Assert.Throws<ArgumentException>(() =>
            AuditSourceIdentity.Create(ArtifactEvidenceSourceKind.WebSnapshot, reference));
    }
}
