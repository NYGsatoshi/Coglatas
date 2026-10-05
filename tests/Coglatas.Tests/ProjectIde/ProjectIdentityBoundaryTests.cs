using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectIdentityBoundaryTests
{
    private delegate bool TryParseIdentity<T>(string? text, out T value);

    [Fact]
    public void EveryIdentityFactoryConstructorAndTryParseEnforcesTheUuidBoundary()
    {
        AssertIdentityBoundary(TenantId.New, value => new TenantId(value), TenantId.TryParse);
        AssertIdentityBoundary(ProjectId.New, value => new ProjectId(value), ProjectId.TryParse);
        AssertIdentityBoundary(EntityId.New, value => new EntityId(value), EntityId.TryParse);
        AssertIdentityBoundary(RelationId.New, value => new RelationId(value), RelationId.TryParse);
        AssertIdentityBoundary(BranchId.New, value => new BranchId(value), BranchId.TryParse);
        AssertIdentityBoundary(RevisionId.New, value => new RevisionId(value), RevisionId.TryParse);
        AssertIdentityBoundary(ProposalId.New, value => new ProposalId(value), ProposalId.TryParse);
        AssertIdentityBoundary(CandidateRevisionId.New, value => new CandidateRevisionId(value), CandidateRevisionId.TryParse);
        AssertIdentityBoundary(ScenarioId.New, value => new ScenarioId(value), ScenarioId.TryParse);
        AssertIdentityBoundary(ScenarioRevisionId.New, value => new ScenarioRevisionId(value), ScenarioRevisionId.TryParse);
        AssertIdentityBoundary(DocumentId.New, value => new DocumentId(value), DocumentId.TryParse);
    }

    private static void AssertIdentityBoundary<T>(Func<T> create, Func<Guid, T> construct, TryParseIdentity<T> tryParse)
        where T : struct, IProjectIdentity<T>
    {
        var first = create();
        var second = create();
        Assert.NotEqual(first, second);
        Assert.Equal(4, first.Value.Version);
        Assert.Equal(0x80, first.Value.ToByteArray(true)[8] & 0xc0);
        Assert.Equal(first, construct(first.Value));
        Assert.Equal(first, T.Parse(first.Value.ToString("D")));
        Assert.True(tryParse(first.Value.ToString("D").ToUpperInvariant(), out var parsed));
        Assert.Equal(first, parsed);
        Assert.Equal(first.Value.ToString("D"), parsed.ToString());
        Assert.Equal(first, JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(first)));

        foreach (var invalid in new[] { Guid.Empty, Guid.Parse("00000000-0000-1000-8000-000000000001"),
                     Guid.Parse("00000000-0000-4000-0000-000000000001") })
        {
            Assert.Throws<ArgumentException>(() => construct(invalid));
            Assert.False(tryParse(invalid.ToString("D"), out var rejected));
            Assert.Equal(default, rejected);
        }
        foreach (var invalid in new[] { null, "", "display-name", first.Value.ToString("N"), " " + first })
        {
            Assert.False(tryParse(invalid, out var rejected));
            Assert.Equal(default, rejected);
        }
        Assert.Throws<ArgumentException>(() => JsonSerializer.Serialize(default(T)));
    }
}
