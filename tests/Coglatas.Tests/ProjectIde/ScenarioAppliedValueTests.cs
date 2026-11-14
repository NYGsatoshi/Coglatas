using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ScenarioAppliedValueTests
{
    private static ProjectScenarioPlan Plan((CompilerInputManifest Input, CompilerMaterializedSnapshot Snapshot, ProjectScenarioOverride Change) f) =>
        ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]), f.Input.InputDigest,
            ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change]).Plan!;
    private static SourceJson AppliedDefault(CompilerInputManifest input) => PlanDeclarationReader.Read(input.Source).Select(x => x.Declaration)
        .OfType<TaskPlanDeclaration>().Single().Ports.Single().DefaultValue!.Data;
    private static void Check(ProjectScenarioAppliedValues projection, ProjectScenarioPlan scenario, CompilerInputManifest after)
    {
        Assert.Same(after, projection.Input); var row = Assert.Single(projection.Values);
        Assert.Equal(AppliedDefault(after), row.Value); Assert.Equal(after.Source.Context, row.Target.Context);
        Assert.Equal(scenario.Changes[0].Override.Target.DocumentId, row.Target.DocumentId);
        Assert.Equal(scenario.Changes[0].Override.Target.EntityId, row.Target.EntityId);
        Assert.Equal(scenario.Changes[0].Override.Kind, row.Kind); Assert.Equal(scenario.Changes[0].Override.PortId, row.PortId);
        var assumption = Json(Obj(new { target = scenario.Changes[0].Override.Target.Data.Value,
            kind = row.Kind.ToString(), portId = row.PortId?.ToString(), value = scenario.Changes[0].Override.Value.Value }));
        Assert.Equal(assumption, row.Assumption);
        Assert.Equal(ContentDigest.Compute("coglatas.local-scenario-assumption/1", assumption.ToCanonicalBytes()), row.AssumptionDigest);
        Assert.Equal(ContentDigest.Compute("coglatas.local-scenario-value/1", row.Value.ToCanonicalBytes()), row.ValueDigest);
        Assert.Equal(ContentDigest.Compute(ProjectScenarioAppliedValues.Format, projection.Data.ToCanonicalBytes()), projection.Digest);
        var data = projection.Data.Value;
        Assert.Equal(ProjectScenarioAppliedValues.Format, data.GetProperty("format").GetString());
        Assert.Equal(scenario.OverlayDigest.Data, SourceJson.Parse(data.GetProperty("overlayDigest").GetRawText()));
        Assert.Equal(scenario.Basis.InputDigest.Data, SourceJson.Parse(data.GetProperty("baseInputDigest").GetRawText()));
        Assert.Equal(scenario.Input.InputDigest.Data, SourceJson.Parse(data.GetProperty("scenarioInputDigest").GetRawText()));
        Assert.Equal(after.InputDigest.Data, SourceJson.Parse(data.GetProperty("afterInputDigest").GetRawText()));
        Assert.Equal(after.Source.Digest.Data, SourceJson.Parse(data.GetProperty("afterSourceDigest").GetRawText()));
        Assert.Equal(after.Source.Context.Data, SourceJson.Parse(data.GetProperty("afterContext").GetRawText()));
        var encoded = Assert.Single(data.GetProperty("values").EnumerateArray());
        Assert.Equal(row.Target.Data, SourceJson.Parse(encoded.GetProperty("target").GetRawText()));
        Assert.Equal(row.AssumptionDigest.Data, SourceJson.Parse(encoded.GetProperty("assumption").GetProperty("digest").GetRawText()));
        Assert.Equal(row.Value.CanonicalText, encoded.GetProperty("value").GetProperty("text").GetString());
        Assert.Equal(row.ValueDigest.Data, SourceJson.Parse(encoded.GetProperty("value").GetProperty("digest").GetRawText()));
    }
    [Theory]
    [InlineData("reference")] [InlineData("nullable")] [InlineData("optional")] [InlineData("structure")]
    [InlineData("list")] [InlineData("sequence")] [InlineData("set")] [InlineData("map")]
    public void CanonicalValuesAreReadFromActualScenarioRebasedAndDraftSources(string wrapper)
    {
        var f = ScenarioReferenceRebaseTests.Fixture(wrapper: wrapper); var originalBytes = f.Input.Source.Data;
        var plan = Plan(f); var projection = ProjectScenarioAppliedValues.Read(plan, plan.Input);
        Assert.Equal(ProjectScenarioState.Ready, projection.State); Check(projection.Projection!, plan, plan.Input);
        Assert.NotEqual(f.Change.Value, projection.Projection!.Values[0].Value);
        Assert.Single(projection.Projection.Values[0].OriginEdits);
        Assert.Equal("OriginContext", projection.Projection.Values[0].OriginEdits[0].Value.GetProperty("kind").GetString());
        Assert.Equal(projection.Projection.Values[0].OriginEdits[0], SourceJson.Parse(projection.Projection.Data.Value.GetProperty("values")[0].GetProperty("originEdits")[0].GetRawText()));
        Assert.Equal(projection.Projection.Data, ProjectScenarioAppliedValues.Read(plan, plan.Input).Projection!.Data);
        var later = ScenarioReferenceRebaseTests.Fixture(55, wrapper: wrapper); var model = ProjectSemanticBinder.Bind(later.Input, [later.Snapshot]);
        var rebased = ProjectScenarios.Rebase(plan, model, later.Input.InputDigest, ScenarioRevisionId.Parse(Id(802))).Plan!;
        var next = ProjectScenarioAppliedValues.Read(rebased, rebased.Input).Projection!; Check(next, rebased, rebased.Input);
        Assert.NotEqual(projection.Projection.Digest, next.Digest);
        var revision = later.Input.Source.Context.CommittedRevision!;
        var draft = ProjectScenarios.ToDraft(rebased, model, later.Input.InputDigest, new(ProposalId.Parse(Id(810)), CandidateRevisionId.Parse(Id(811)), revision, revision)).Plan!;
        var converted = ProjectScenarioAppliedValues.Read(draft, draft.After).Projection!; Check(converted, rebased, draft.After);
        Assert.NotEqual(next.Digest, converted.Digest);
        Assert.Equal(originalBytes, f.Input.Source.Data);
        Assert.True(ProjectSemanticBinder.Bind(draft.After, [later.Snapshot]).BindingComplete);
        var clone = JsonNode.Parse(converted.Data.CanonicalText)!; clone["values"]![0]!["value"]!["text"] = "{}";
        var array = converted.Values.ToArray(); array[0] = array[0] with { Value = Json(Obj(new { changed = true })) };
        Assert.Equal(AppliedDefault(draft.After), converted.Values[0].Value);
        Assert.NotEqual(converted.Data, Json(clone)); Check(converted, rebased, draft.After);
    }
    [Theory]
    [InlineData("base")] [InlineData("new-revision")] [InlineData("settings")] [InlineData("same-context-source")]
    public void WrongStageAndSubstitutedInputOrSourceCannotCreateAProjection(string mode)
    {
        var f = ScenarioReferenceRebaseTests.Fixture(); var plan = Plan(f); var expected = plan.Input;
        var supplied = mode == "base" ? f.Input : ScenarioReferenceRebaseTests.Fixture(55).Input;
        if (mode == "settings") { var settings = JsonNode.Parse(expected.Settings.CanonicalText)!.AsObject(); settings["clock"] = Obj(new { kind = "coglatas.known", instant = "2030-01-01T00:00:00.0000000Z" }); supplied = new(expected.Source, Json(settings)); }
        if (mode == "same-context-source") supplied = new(ProjectSource.Create(expected.Source.Context, expected.Source.Documents, Json(new JsonArray())), expected.Settings);
        var refused = ProjectScenarioAppliedValues.Read(plan, supplied); Assert.Equal(ProjectScenarioState.StaleInput, refused.State); Assert.Null(refused.Projection);
        var revision = f.Input.Source.Context.CommittedRevision!;
        var draft = ProjectScenarios.ToDraft(plan, ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]), f.Input.InputDigest,
            new(ProposalId.Parse(Id(810)), CandidateRevisionId.Parse(Id(811)), revision, revision)).Plan!;
        Assert.Equal(ProjectScenarioState.StaleInput, ProjectScenarioAppliedValues.Read(draft, expected).State);
        Assert.Equal(ProjectScenarioState.StaleInput, ProjectScenarioAppliedValues.Read(plan, draft.After).State);
    }
    [Fact]
    public void ProjectionReadinessDoesNotCreateReferenceResolutionOrExecutionPass()
    {
        var f = ScenarioReferenceRebaseTests.Fixture(); var plan = Plan(f); var input = plan.Input;
        var projection = ProjectScenarioAppliedValues.Read(plan, input); Assert.Equal(ProjectScenarioState.Ready, projection.State);
        var evaluator = new CompilerArtifactIdentity(SourceJson.Parse(input.Settings.Value.GetProperty("evaluator").GetRawText()));
        var denied = new CompilerMaterializedSnapshot(f.Snapshot.SnapshotId, f.Snapshot.Version, new(f.Snapshot.Snapshot.Source, false, true, true));
        foreach (var snapshots in new[] { Array.Empty<CompilerMaterializedSnapshot>(), new[] { denied } })
        {
            var model = ProjectSemanticBinder.Bind(input, snapshots); Assert.False(model.BindingComplete);
            Assert.NotEqual(CompilerVerdict.Pass, ProjectTestPlan.Compile(model, evaluator).Execute(input.InputDigest).Verdict);
        }
        Assert.Equal(CompilerVerdict.Pass, ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input, [f.Snapshot]), evaluator).Execute(input.InputDigest).Verdict);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectScenarioAppliedValues.Read(plan, input, cancellation.Token));
    }
    [Fact]
    public void ExpandedAppliedContextCannotExceedTheOriginalAggregateProcessingBound()
    {
        var f = ScenarioReferenceRebaseTests.Fixture();
        var value = Obj(new { type = f.Change.Value.Value.GetProperty("type"), state = "coglatas.unknown", reason = "example.missing", basis = new { opaque = "" } });
        var length = ProjectScenarios.MaximumOverrideBytes - System.Text.Encoding.UTF8.GetByteCount(Json(value).CanonicalText)
            - System.Text.Encoding.UTF8.GetByteCount(f.Change.Target.Data.CanonicalText);
        value["basis"]!["opaque"] = new string('x', length);
        var preview = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]), f.Input.InputDigest,
            ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change with { Value = Json(value) }]);
        Assert.Equal(ProjectScenarioState.Ready, preview.State);
        var projection = ProjectScenarioAppliedValues.Read(preview.Plan!, preview.Plan!.Input);
        Assert.Equal(ProjectScenarioState.ResourceLimit, projection.State); Assert.Null(projection.Projection);
        Assert.NotNull(preview.Plan.Materialize(f.Input));
    }

}
