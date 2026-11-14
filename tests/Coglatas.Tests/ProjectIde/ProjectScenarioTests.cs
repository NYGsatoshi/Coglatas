using Coglatas.Domain.ProjectIde;
using System.Text.Json.Nodes;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectScenarioTests
{
    private static ScenarioId Scenario => ScenarioId.Parse(Id(600));
    private static ScenarioRevisionId Revision(int id = 601) => ScenarioRevisionId.Parse(Id(id));
    private static CompilerArtifactIdentity Evaluator => new(Json(Artifact("example.fixed-scenario-evaluator")));
    private static object BooleanType => new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true };
    private static SourceJson Boolean(bool value) => Json(Obj(new { type = BooleanType, state = "coglatas.known", value }));
    private static SourceJson Duration(string value) => Json(Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value }));
    private static SourceJson Date(string value) => Json(Obj(new { type = new { kind = "coglatas.local-date", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = new { date = value, calendar = new { calendarId = "example.fixed-scenario-calendar", version = "1", timeZoneId = "UTC", rulesDigest = new { algorithm = "sha-256", value = new string('a', 64) } } } }));
    private static CompilerInputManifest Fixture(int revision = 5, bool actual = false, string duration = "1", Action<JsonObject>? mutate = null)
    {
        var basis = Source(revision);
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        { duration = Duration(duration).Value, ports = new[] { new { portId = Id(8), direction = "coglatas.input", type = BooleanType,
            minimumConnections = 0, maximumConnections = 1, defaultValue = Boolean(actual).Value } } } });
        var test = Obj(new { entityId = Id(9), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1, payload = new
        { assertions = new[] { new { key = "example.ready", kind = "coglatas.assert-boolean", target = CompilerAnalysisFixtures.Reference(7, basis.Context), portId = Id(8), expected = Boolean(true).Value } } } });
        var milestone = Obj(new { entityId = Id(10), documentId = Id(4), kind = "coglatas.milestone", schemaVersion = 1, payload = new { target = Date("2026-11-14").Value } });
        var settings = Settings(); settings["evaluator"] = JsonNode.Parse(Evaluator.Data.CanonicalText);
        var source = ProjectSource.Create(basis.Context, basis.Documents, Json(new JsonArray(task, test, milestone)));
        if (mutate is not null) { var root = JsonNode.Parse(source.Data.CanonicalText)!.AsObject(); mutate(root); source = ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource(); }
        return Input(settings, source);
    }
    private static ProjectLocation Target(CompilerInputManifest input, int entity = 7) => ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context, entityId: EntityId.Parse(Id(entity)));
    private static ProjectScenarioOverride Override(CompilerInputManifest input) => new(Target(input), ProjectScenarioOverrideKind.TaskInputDefault, Boolean(true), PortId.Parse(Id(8)));
    private static ProjectScenarioPlan Prepare(CompilerInputManifest input, params ProjectScenarioOverride[] overrides)
    {
        var result = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(input), input.InputDigest, Scenario, Revision(), overrides.Length == 0 ? [Override(input)] : overrides);
        Assert.Equal(ProjectScenarioState.Ready, result.State); return Assert.IsType<ProjectScenarioPlan>(result.Plan);
    }
    private static ProjectTestRun Run(CompilerInputManifest input, ContentDigest? expected = null) =>
        ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), Evaluator).Execute(expected ?? input.InputDigest);

    [Fact]
    public void FrozenBaselineScenarioAndNewDraftHaveIndependentActualTestInputs()
    {
        var basis = Fixture(); var original = basis.Source.Data.CanonicalText;
        Assert.Equal(CompilerVerdict.Fail, Run(basis).Verdict);
        var plan = Prepare(basis);
        Assert.Equal(original, basis.Source.Data.CanonicalText); Assert.Same(basis, plan.Basis);
        Assert.Equal(Scenario, plan.Input.Source.Context.Scenario!.ScenarioId);
        Assert.Equal(Revision(), plan.Input.Source.Context.Scenario!.ScenarioRevisionId);
        Assert.Equal(basis.Source.Context, plan.Input.Source.Context.Scenario!.BaseContext);
        Assert.Equal(plan.OverlayDigest, plan.Input.Source.Context.Scenario!.OverlayDigest);
        Assert.Equal(ContentDigest.Compute("coglatas.local-scenario-overlay/1", plan.Overlay.ToCanonicalBytes()), plan.OverlayDigest);
        Assert.Equal(CompilerVerdict.Pass, Run(plan.Input).Verdict);
        Assert.Equal(CompilerVerdict.NotRun, Run(plan.Input, basis.InputDigest).Verdict);
        Assert.Equal(CompilerVerdict.Fail, Run(basis).Verdict);
        Assert.NotEqual(basis.InputId, plan.Input.InputId);
        Assert.Equal(plan.Input.Data, CompilerInputManifest.ReadEnvelope(plan.Input.Source, plan.Input.ToEnvelope()).Data);
        var proposal = new ProposalContext(ProposalId.Parse(Id(610)), CandidateRevisionId.Parse(Id(611)), basis.Source.Context.CommittedRevision!, basis.Source.Context.CommittedRevision!);
        var preview = ProjectScenarios.ToDraft(plan, ProjectSemanticBinder.Bind(basis), basis.InputDigest, proposal);
        Assert.Equal(ProjectScenarioState.Ready, preview.State); var draft = Assert.IsType<ProjectScenarioDraftPlan>(preview.Plan);
        Assert.Same(draft.After, draft.ApplyToDraft(basis)); Assert.Null(draft.ApplyToDraft(draft.After));
        Assert.Null(draft.After.Source.Context.Scenario); Assert.Equal(proposal.CandidateRevisionId, draft.After.Source.Context.Proposal!.CandidateRevisionId);
        Assert.Equal(basis.Settings, draft.After.Settings); Assert.Equal(CompilerVerdict.Pass, Run(draft.After).Verdict);
        Assert.Equal(CompilerVerdict.NotRun, Run(draft.After, plan.Input.InputDigest).Verdict);
    }

    [Fact]
    public void CanonicalAssumptionsAreOrderIndependentAndCannotBeMutatedThroughReturnedRows()
    {
        var basis = Fixture(); var edits = new[] { Override(basis), new ProjectScenarioOverride(Target(basis), ProjectScenarioOverrideKind.TaskDuration, Duration("2")) };
        var first = Prepare(basis, edits); var second = Prepare(basis, edits.Reverse().ToArray());
        Assert.Equal(first.Input.Data, second.Input.Data); Assert.Equal(first.Input.Source.Data, second.Input.Source.Data);
        Assert.Equal(first.OverlayDigest, second.OverlayDigest); Assert.Equal(2, first.Changes.Count);
        Assert.False(first.Changes is ProjectScenarioChange[]);
        var node = JsonNode.Parse(first.Overlay.CanonicalText)!.AsObject(); node["overrides"]!.AsArray().Clear();
        Assert.Equal(2, first.Overlay.Value.GetProperty("overrides").GetArrayLength());
        Assert.Equal(first.Overlay.Value.GetProperty("overrides").GetRawText(), first.Input.Settings.Value.GetProperty("scenario").GetProperty("assumptions").GetRawText());
    }

    [Fact]
    public void TypedTaskDurationAndMilestoneDateUseExistingDecodersAndPreserveOpaqueSource()
    {
        var basis = Fixture(mutate: root => root["example.opaque"] = Obj(new { context = "must remain opaque", value = 7 }));
        var plan = Prepare(basis, new(Target(basis), ProjectScenarioOverrideKind.TaskDuration, Duration("200")),
            new(Target(basis, 10), ProjectScenarioOverrideKind.MilestoneTarget, Date("2026-12-01")));
        var declarations = PlanDeclarationReader.Read(plan.Input.Source);
        Assert.Equal(200, Assert.IsType<ElapsedDuration>(Assert.IsType<TaskPlanDeclaration>(declarations.Single(x => x.Declaration?.EntityId == EntityId.Parse(Id(7))).Declaration).Duration.KnownValue).Ticks);
        Assert.Equal(Date("2026-12-01"), Assert.IsType<MilestonePlanDeclaration>(declarations.Single(x => x.Declaration?.EntityId == EntityId.Parse(Id(10))).Declaration).Target.Data);
        Assert.Equal(basis.Source.Data.Value.GetProperty("example.opaque").GetRawText(), plan.Input.Source.Data.Value.GetProperty("example.opaque").GetRawText());
        Assert.Equal(basis.Source.PackageLock, plan.Input.Source.PackageLock);
        var compile = PureCoreCompiler.Compile(plan.Input, plan.Input.Compiler);
        Assert.NotNull(compile.Result); Assert.Equal(plan.Input.InputId, compile.Result.Input.InputId);
    }

    [Theory]
    [InlineData("negative", ProjectScenarioState.InvalidOverride)]
    [InlineData("type", ProjectScenarioState.InvalidOverride)]
    [InlineData("unsupported", ProjectScenarioState.Unsupported)]
    [InlineData("port", ProjectScenarioState.Unavailable)]
    [InlineData("document", ProjectScenarioState.Unavailable)]
    [InlineData("scope", ProjectScenarioState.OutOfScope)]
    [InlineData("kind", ProjectScenarioState.Unsupported)]
    public void InvalidOverridesHaveNoPartialPlan(string mode, ProjectScenarioState expected)
    {
        var basis = Fixture(); var change = mode switch
        {
            "negative" => new ProjectScenarioOverride(Target(basis), ProjectScenarioOverrideKind.TaskDuration, Duration("-1")),
            "type" => Override(basis) with { Value = Duration("2") },
            "unsupported" => Override(basis) with { Value = Json(Obj(new { type = new { kind = "example.future", schemaVersion = 2, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = true })) },
            "port" => Override(basis) with { PortId = PortId.Parse(Id(999)) },
            "document" => Override(basis) with { Target = ProjectLocation.Create(DocumentId.Parse(Id(999)), basis.Source.Context, entityId: EntityId.Parse(Id(7))) },
            "scope" => Override(basis) with { Target = Target(Fixture(55)) },
            _ => Override(basis) with { Kind = (ProjectScenarioOverrideKind)999 }
        };
        var result = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(basis), basis.InputDigest, Scenario, Revision(), [new(Target(basis, 10), ProjectScenarioOverrideKind.MilestoneTarget, Date("2026-12-01")), change]);
        Assert.Equal(expected, result.State); Assert.Null(result.Plan); Assert.Equal(CompilerVerdict.Fail, Run(basis).Verdict);
    }

    [Fact]
    public void DuplicateTargetsLimitsEmptyAndStaleInputsNeverMaterialize()
    {
        var basis = Fixture(); var model = ProjectSemanticBinder.Bind(basis); var change = Override(basis);
        ProjectScenarioPreview Call(IEnumerable<ProjectScenarioOverride> edits, ContentDigest? expected = null) => ProjectScenarios.Prepare(model, expected ?? basis.InputDigest, Scenario, Revision(), edits);
        Assert.Equal(ProjectScenarioState.Conflict, Call([change, change]).State);
        Assert.Equal(ProjectScenarioState.ResourceLimit, Call(Enumerable.Repeat(change, ProjectScenarios.MaximumOverrides + 1)).State);
        Assert.Equal(ProjectScenarioState.NoChange, Call([]).State);
        Assert.Equal(ProjectScenarioState.NoChange, Call([change with { Value = Boolean(false) }]).State);
        Assert.Equal(ProjectScenarioState.StaleInput, Call([change], Fixture(55).InputDigest).State);
        var plan = Prepare(basis); Assert.Null(plan.Materialize(Fixture(55))); Assert.Null(plan.Materialize(plan.Input));
        Assert.Equal(ProjectScenarioState.InvalidContext, ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(plan.Input), plan.Input.InputDigest, Scenario, Revision(602), [change]).State);
    }

    [Fact]
    public void RevisionsAreExplicitAndDoNotReuseEarlierRunIdentities()
    {
        var basis = Fixture(); var plan = Prepare(basis); var model = ProjectSemanticBinder.Bind(basis);
        Assert.Equal(ProjectScenarioState.ContextConflict, ProjectScenarios.Revise(plan, model, basis.InputDigest, Revision(), [Override(basis)]).State);
        var revised = ProjectScenarios.Revise(plan, model, basis.InputDigest, Revision(602), [Override(basis)]);
        Assert.Equal(ProjectScenarioState.Ready, revised.State); Assert.NotEqual(plan.Input.InputId, revised.Plan!.Input.InputId);
        Assert.Equal(plan.OverlayDigest, revised.Plan.OverlayDigest);
        Assert.Equal(CompilerVerdict.NotRun, Run(revised.Plan.Input, plan.Input.InputDigest).Verdict);
        Assert.Equal(ProjectScenarioState.StaleInput, ProjectScenarios.Revise(plan, ProjectSemanticBinder.Bind(Fixture(55)), Fixture(55).InputDigest, Revision(603), [Override(basis)]).State);
    }

    [Fact]
    public void ExplicitRebasePreservesUnrelatedBaseChangesAndConflictsOnOverriddenValues()
    {
        var basis = Fixture(); var plan = Prepare(basis); var later = Fixture(55, duration: "3");
        var rebased = ProjectScenarios.Rebase(plan, ProjectSemanticBinder.Bind(later), later.InputDigest, Revision(602));
        Assert.Equal(ProjectScenarioState.Ready, rebased.State); Assert.Equal(later.Source.Context, rebased.Plan!.Input.Source.Context.Scenario!.BaseContext);
        Assert.Equal(CompilerVerdict.Pass, Run(rebased.Plan.Input).Verdict); Assert.NotEqual(plan.OverlayDigest, rebased.Plan.OverlayDigest);
        var conflicting = Fixture(56, actual: true);
        Assert.Equal(ProjectScenarioState.Conflict, ProjectScenarios.Rebase(plan, ProjectSemanticBinder.Bind(conflicting), conflicting.InputDigest, Revision(603)).State);
        var proposal = new ProposalContext(ProposalId.Parse(Id(610)), CandidateRevisionId.Parse(Id(611)), later.Source.Context.CommittedRevision!, later.Source.Context.CommittedRevision!);
        Assert.Equal(ProjectScenarioState.StaleInput, ProjectScenarios.ToDraft(plan, ProjectSemanticBinder.Bind(later), later.InputDigest, proposal).State);
    }

    [Fact]
    public void CandidateBaseRequiresASeparateCandidateVersionForDraftConversion()
    {
        var basis = Fixture(); var committed = basis.Source.Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.Parse(Id(610)), CandidateRevisionId.Parse(Id(611)), committed, committed);
        var candidate = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(basis), proposal); var plan = Prepare(candidate); var model = ProjectSemanticBinder.Bind(candidate);
        Assert.Equal(ProjectScenarioState.InvalidContext, ProjectScenarios.ToDraft(plan, model, candidate.InputDigest, proposal).State);
        var next = new ProposalContext(proposal.ProposalId, CandidateRevisionId.Parse(Id(612)), committed, committed);
        var converted = ProjectScenarios.ToDraft(plan, model, candidate.InputDigest, next);
        Assert.Equal(ProjectScenarioState.Ready, converted.State); Assert.Equal(CompilerVerdict.Pass, Run(converted.Plan!.After).Verdict);
    }

    [Fact]
    public void PinnedScenarioVersionAndPartialDiscoveryAreRefused()
    {
        var basis = Fixture(); var foreign = SourceRevisionContext.Hypothetical(new(Scenario, Revision(), basis.Source.Context, ContentDigest.Compute("example", [1])));
        var root = JsonNode.Parse(basis.Source.Data.CanonicalText)!.AsObject();
        root["declarations"]![1]!["payload"]!["assertions"]![0]!["target"]!["context"] = JsonNode.Parse(foreign.Data.CanonicalText);
        var pinned = new CompilerInputManifest(ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource(), basis.Settings);
        Assert.Equal(ProjectScenarioState.ContextConflict, ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(pinned), pinned.InputDigest, Scenario, Revision(), [Override(pinned)]).State);
        var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumDeclarations"] = 1;
        var partial = new CompilerInputManifest(basis.Source, Json(settings));
        Assert.Equal(ProjectScenarioState.ResourceLimit, ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(partial), partial.InputDigest, Scenario, Revision(), [Override(partial)]).State);
    }

    [Fact]
    public void CancellationAndUnknownValuesCannotBorrowTheBaselineVerdict()
    {
        var basis = Fixture(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(basis), basis.InputDigest, Scenario, Revision(), [Override(basis)], cancelled.Token));
        var unknown = Json(Obj(new { type = BooleanType, state = "coglatas.unknown", reason = "example.hypothetical-missing", basis = new { } }));
        var plan = Prepare(basis, Override(basis) with { Value = unknown });
        Assert.NotEqual(CompilerVerdict.Pass, Run(plan.Input).Verdict); Assert.Equal(CompilerVerdict.Fail, Run(basis).Verdict);
    }
    [Fact]
    public void RebaseRefusesASecondSourceBodyForTheSameImmutableBaseVersion()
    {
        var basis = Fixture(); var plan = Prepare(basis); var substituted = Fixture(duration: "3");
        Assert.Equal(basis.Source.Context, substituted.Source.Context); Assert.NotEqual(basis.Source.Digest, substituted.Source.Digest);
        var result = ProjectScenarios.Rebase(plan, ProjectSemanticBinder.Bind(substituted), substituted.InputDigest, Revision(602));
        Assert.Equal(ProjectScenarioState.Conflict, result.State); Assert.Null(result.Plan);
    }

    [Fact]
    public void AggregateOverrideBytesAreBoundedBeforeCloningOrPublishing()
    {
        var basis = Fixture(); var value = Json(Obj(new { type = BooleanType, state = "coglatas.unknown", reason = "example.missing", basis = new { note = new string('x', 300_000) } }));
        var result = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(basis), basis.InputDigest, Scenario, Revision(), [Override(basis) with { Value = value }]);
        Assert.Equal(ProjectScenarioState.ResourceLimit, result.State); Assert.Null(result.Plan);
    }

    [Fact]
    public void SeveralIndividuallySmallOverridesCannotExceedTheCombinedByteBound()
    {
        var basis = Fixture(); var value = Json(Obj(new { type = BooleanType, state = "coglatas.unknown", reason = "example.missing", basis = new { note = new string('x', 100_000) } }));
        var edit = Override(basis) with { Value = value };
        var result = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(basis), basis.InputDigest, Scenario, Revision(), [edit, edit, edit]);
        Assert.Equal(ProjectScenarioState.ResourceLimit, result.State); Assert.Null(result.Plan);
    }

    [Fact]
    public void AppliedProjectionCoversAllInitialOverrideKindsInCanonicalAssumptionOrder()
    {
        var basis = Fixture();
        var edits = new[] { Override(basis), new ProjectScenarioOverride(Target(basis), ProjectScenarioOverrideKind.TaskDuration, Duration("2")),
            new ProjectScenarioOverride(Target(basis, 10), ProjectScenarioOverrideKind.MilestoneTarget, Date("2026-11-15")) };
        var plan = Prepare(basis, edits); var projection = ProjectScenarioAppliedValues.Read(plan, plan.Input).Projection!;
        Assert.NotNull(projection); Assert.Equal(3, projection.Values.Count);
        Assert.Equal(plan.Changes.Select(item => item.Override.Kind), projection.Values.Select(item => item.Kind));
        Assert.Equal(plan.Changes.Select(item => item.Override.Value), projection.Values.Select(item => item.Value));
        Assert.All(projection.Values, item => Assert.Equal(plan.Input.Source.Context, item.Target.Context));
        var reordered = Prepare(basis, edits.Reverse().ToArray());
        Assert.Equal(projection.Data, ProjectScenarioAppliedValues.Read(reordered, reordered.Input).Projection!.Data);
        var revision = basis.Source.Context.CommittedRevision!;
        var draft = ProjectScenarios.ToDraft(plan, ProjectSemanticBinder.Bind(basis), basis.InputDigest,
            new(ProposalId.Parse(Id(620)), CandidateRevisionId.Parse(Id(621)), revision, revision)).Plan!;
        var after = ProjectScenarioAppliedValues.Read(draft, draft.After).Projection!;
        Assert.Equal(projection.Values.Select(item => item.Value), after.Values.Select(item => item.Value));
        Assert.All(after.Values, item => Assert.Equal(draft.After.Source.Context, item.Target.Context));
        Assert.NotEqual(projection.Digest, after.Digest);
    }

}
