using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectLocationTests
{
    private static string Id(int number) => "00000000-0000-4000-8000-" + number.ToString("x12", System.Globalization.CultureInfo.InvariantCulture);
    private static BranchRef Branch(int branch = 3) => new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(2)), BranchId.Parse(Id(branch)));
    private static SourceRevisionContext Committed(int revision = 4) => SourceRevisionContext.Committed(new(Branch(), RevisionId.Parse(Id(revision))));

    [Fact]
    public void EntityPropertyAndRelationLocationsRoundTripWithExactRevision()
    {
        var entity = ProjectLocation.Create(DocumentId.Parse(Id(5)), Committed(), "/payload/a~1b~0c",
            entityId: EntityId.Parse(Id(6)), textSpan: new(2, 4));
        var relation = ProjectLocation.Create(DocumentId.Parse(Id(5)), Committed(), "/target",
            relationId: RelationId.Parse(Id(7)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "location-v1.canonical.json")),
            entity.ToCanonicalBytes());
        foreach (var location in new[] { entity, relation })
        {
            var decoded = ProjectLocation.Parse(SourceJson.Parse(location.ToCanonicalBytes()));
            Assert.Equal(location, decoded);
            Assert.Equal(location.ToCanonicalBytes(), decoded.ToCanonicalBytes());
            Assert.Equal(RevisionId.Parse(Id(4)), decoded.Context.CommittedRevision!.RevisionId);
        }
        Assert.Equal("/payload/a~1b~0c", entity.PropertyPath);
        Assert.Equal(2, entity.TextSpan!.Start);
        Assert.NotEqual(entity, ProjectLocation.Create(entity.DocumentId, Committed(8), entity.PropertyPath, entity.EntityId, textSpan: entity.TextSpan));
        Assert.Throws<FormatException>(() => ProjectLocation.Create(entity.DocumentId, Committed(), "row 3"));
        Assert.Throws<FormatException>(() => ProjectLocation.Create(entity.DocumentId, Committed(), "/bad~2"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceTextSpan(int.MaxValue, 1));
    }

    [Fact]
    public void CandidateAndScenarioBindingsNeverBecomeCommittedIdentity()
    {
        var proposal = new ProposalContext(ProposalId.Parse(Id(9)), CandidateRevisionId.Parse(Id(10)),
            new(Branch(), RevisionId.Parse(Id(4))), new(Branch(), RevisionId.Parse(Id(8))));
        var candidate = SourceRevisionContext.Candidate(proposal);
        Assert.Null(candidate.CommittedRevision);
        Assert.Equal(CandidateRevisionId.Parse(Id(10)), candidate.Proposal!.CandidateRevisionId);
        var scenario = SourceRevisionContext.Hypothetical(new(ScenarioId.Parse(Id(11)), ScenarioRevisionId.Parse(Id(12)), candidate,
            ContentDigest.Compute("coglatas.scenario-overlay/1", SourceJson.Parse("{}").ToCanonicalBytes())));
        var location = ProjectLocation.Create(DocumentId.Parse(Id(5)), scenario);
        Assert.Equal(candidate, location.Context.Scenario!.BaseContext);
        Assert.Null(location.Context.CommittedRevision);
        Assert.Equal(location, ProjectLocation.Parse(SourceJson.Parse(location.ToCanonicalBytes())));
        Assert.Throws<ArgumentException>(() => new ScenarioRef(ScenarioId.Parse(Id(11)), ScenarioRevisionId.Parse(Id(12)), scenario, scenario.Scenario!.OverlayDigest));
    }

    [Fact]
    public void ScopeMismatchAndConflictingDiscriminantsAreRejected()
    {
        var other = new BranchRef(TenantId.Parse(Id(13)), ProjectId.Parse(Id(2)), BranchId.Parse(Id(3)));
        Assert.Throws<ArgumentException>(() => new ProposalContext(ProposalId.Parse(Id(9)), CandidateRevisionId.Parse(Id(10)),
            new(Branch(), RevisionId.Parse(Id(4))), new(other, RevisionId.Parse(Id(8)))));
        var mixed = JsonNode.Parse(Committed().Data.CanonicalText)!.AsObject();
        mixed["candidateRevisionId"] = Id(10);
        Assert.Throws<FormatException>(() => SourceRevisionContext.Parse(SourceJson.Parse(mixed.ToJsonString())));
        var branchChanged = JsonNode.Parse(Committed().Data.CanonicalText)!.AsObject();
        branchChanged["branch"]!["branchId"] = Id(14);
        Assert.NotEqual(Committed(), SourceRevisionContext.Parse(SourceJson.Parse(branchChanged.ToJsonString())));
    }

    [Fact]
    public void ContextAndLocationUnknownDataAndNullPresenceArePreserved()
    {
        var node = JsonNode.Parse(ProjectLocation.Create(DocumentId.Parse(Id(5)), Committed()).Data.CanonicalText)!.AsObject();
        node["future"] = new JsonObject { ["value"] = null };
        node["entityId"] = null;
        node["context"]!["futureContext"] = new JsonObject { ["token"] = "future.kind" };
        var location = ProjectLocation.Parse(SourceJson.Parse(node.ToJsonString()));
        var roundTrip = JsonNode.Parse(location.ToCanonicalBytes())!;
        Assert.Equal("future.kind", roundTrip["context"]!["futureContext"]!["token"]!.GetValue<string>());
        Assert.True(roundTrip.AsObject().ContainsKey("entityId"));
        Assert.Null(roundTrip["entityId"]);
        Assert.Equal(location.Data.CanonicalText, ProjectLocation.Parse(SourceJson.Parse(location.ToCanonicalBytes())).Data.CanonicalText);
    }
}
