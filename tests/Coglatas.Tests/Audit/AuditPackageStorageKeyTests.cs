using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Tests.Audit;

public sealed class AuditPackageStorageKeyTests
{
    [Fact]
    public void CreateAndParseRoundTripsTenantJobAndArtifactVersion()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var artifactVersionId = Guid.NewGuid();

        var key = AuditPackageStorageKey.Create(tenantId, jobId, artifactVersionId);

        var parsed = AuditPackageStorageKey.TryParse(
            key,
            out var parsedTenantId,
            out var parsedJobId,
            out var parsedArtifactVersionId);

        Assert.True(parsed);
        Assert.Equal(tenantId, parsedTenantId);
        Assert.Equal(jobId, parsedJobId);
        Assert.Equal(artifactVersionId, parsedArtifactVersionId);
        Assert.StartsWith("audit-packages/", key, StringComparison.Ordinal);
        Assert.EndsWith(".zip", key, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("audit-packages/not-a-guid/job/version.zip")]
    [InlineData("tenant/job/version.zip")]
    [InlineData("audit-packages/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/not-a-version.zip")]
    public void TryParseRejectsMalformedOrNonPackageKeys(string key)
    {
        Assert.False(AuditPackageStorageKey.TryParse(key, out _, out _, out _));
    }

    [Fact]
    public void FileNameIsDeterministicAndNeverUsesVersionZero()
    {
        var artifactId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

        Assert.Equal(
            "audit-aaaaaaaabbbb4ccc8dddeeeeeeeeeeee-v1.zip",
            AuditPackageStorageKey.FileName(artifactId, 0));
        Assert.Equal(
            "audit-aaaaaaaabbbb4ccc8dddeeeeeeeeeeee-v7.zip",
            AuditPackageStorageKey.FileName(artifactId, 7));
    }
}
