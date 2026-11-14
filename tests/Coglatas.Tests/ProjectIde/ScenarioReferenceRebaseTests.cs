using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
namespace Coglatas.Tests.ProjectIde;

public sealed class ScenarioReferenceRebaseTests
{
    private static CompilerArtifactIdentity Evaluator => new(Json(Artifact("example.fixed-reference-scenario-evaluator")));
    internal static (CompilerInputManifest Input, CompilerMaterializedSnapshot Snapshot, ProjectScenarioOverride Change) Fixture(int revision = 5, bool hasDefault = true, int defaultTarget = 6, string wrapper = "reference", bool opaqueBefore = false)
    {
        var basis = Source(revision);
        var context = SourceRevisionContext.Committed(new(new(basis.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24))));
        var external = ProjectSource.Create(context, basis.Documents, Json(new JsonArray(Obj(new { entityId = Id(16), documentId = Id(4), kind = "coglatas.project", schemaVersion = 1, payload = new { } }))));
        var type = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.project" });
        JsonObject Value(int target) => Obj(new { type, state = "coglatas.known", value = new
        { tenantId = external.TenantId.ToString(), projectId = external.ProjectId.ToString(), entityId = Id(target), context = external.Context.Data.Value,
            origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), basis.Context, entityId: EntityId.Parse(Id(7))).Data.Value,
            manifest = new { version = "1", digest = external.Digest.Data.Value } } });
        var port = Obj(new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1 });
        var initial = opaqueBefore ? Obj(new { type, state = "coglatas.unknown", reason = "example.missing", basis = new { opaqueReference = Value(6) } }) : Value(defaultTarget);
        if (opaqueBefore) initial["basis"]!["opaqueReference"]!["value"]!["origin"]!["context"] = JsonNode.Parse(Source(5).Context.Data.CanonicalText);
        var wrappedInitial = Wrap(initial, wrapper); var wrappedChange = Wrap(Value(16), wrapper);
        port["type"] = wrappedInitial["type"]!.DeepClone();
        if (hasDefault) port["defaultValue"] = wrappedInitial;
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" }, ports = new JsonArray(port) } });
        var test = Obj(new { entityId = Id(9), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1, payload = new
        { assertions = new[] { new { key = "example.pinned-target", kind = "coglatas.assert-reference-resolved", target = CompilerAnalysisFixtures.Reference(7, basis.Context), portId = Id(8) } } } });
        var settings = Settings(); settings["evaluator"] = JsonNode.Parse(Evaluator.Data.CanonicalText);
        settings["authorizedExternalSnapshots"]!.AsArray().Add(Obj(new { snapshotId = Id(30), version = "1", digest = external.Digest.Data.Value,
            context = external.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        var input = Input(settings, ProjectSource.Create(basis.Context, basis.Documents, Json(new JsonArray(task, test))));
        var snapshot = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(30)), "1", new(external, true, true, true));
        var change = new ProjectScenarioOverride(ProjectLocation.Create(DocumentId.Parse(Id(4)), basis.Context, entityId: EntityId.Parse(Id(7))),
            ProjectScenarioOverrideKind.TaskInputDefault, Json(wrappedChange), PortId.Parse(Id(8)));
        return (input, snapshot, change);
    }
    private static JsonObject Wrap(JsonObject value, string kind)
    {
        if (kind == "reference") return value;
        var type = Obj(new { kind = "coglatas." + kind, schemaVersion = 1, nullable = kind == "nullable", acceptsUnknown = true });
        object body;
        if (kind == "structure")
        {
            type["fields"] = new JsonArray(Obj(new { name = "field/ref~", required = true, type = value["type"]!.DeepClone() }));
            body = new JsonObject { ["field/ref~"] = value.DeepClone() };
        }
        else if (kind == "map")
        {
            var keyType = Obj(new { kind = "coglatas.text", schemaVersion = 1, nullable = false, acceptsUnknown = false });
            type["keyType"] = keyType.DeepClone(); type["valueType"] = value["type"]!.DeepClone(); type["identity"] = "coglatas.canonical-value";
            body = new JsonArray(Obj(new { key = new { type = keyType, state = "coglatas.known", value = "example.key" }, value = value.DeepClone() }));
        }
        else
        {
            type["elementType"] = value["type"]!.DeepClone();
            if (kind == "set") type["identity"] = "coglatas.canonical-value";
            body = kind switch { "nullable" => value.DeepClone(), "optional" => Obj(new { present = true, value = value.DeepClone() }), _ => new JsonArray(value.DeepClone()) };
        }
        return Obj(new { type, state = "coglatas.known", value = body });
    }
    private static IEnumerable<TypedProjectReference> References(TypedProjectValue value)
    {
        if (value.KnownValue is TypedProjectReference reference) yield return reference;
        IEnumerable<TypedProjectValue> children = value.KnownValue switch
        {
            TypedProjectValue child => [child], OptionalProjectValue { Present: true, Value: { } child } => [child],
            ProjectStructureValue structure => structure.Fields.Values,
            ProjectCollectionValue collection => collection.Items.Concat(collection.Entries.Select(x => x.Value)), _ => []
        };
        foreach (var child in children) foreach (var nested in References(child)) yield return nested;
    }
    private static TypedProjectValue Default(CompilerInputManifest input) => Assert.IsType<TaskPlanDeclaration>(
        PlanDeclarationReader.Read(input.Source).Single(item => item.Declaration is TaskPlanDeclaration).Declaration).Ports[0].DefaultValue!;
    private static ProjectTestRun Run(CompilerInputManifest input, CompilerMaterializedSnapshot snapshot) =>
        ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input, [snapshot]), Evaluator).Execute(input.InputDigest);
    private static TypedProjectReference Reference(CompilerInputManifest input) => Assert.IsType<TypedProjectReference>(
        Assert.IsType<TaskPlanDeclaration>(PlanDeclarationReader.Read(input.Source).Single(item => item.Declaration is TaskPlanDeclaration).Declaration).Ports[0].DefaultValue!.KnownValue);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RebaseAdvancesOnlyTheDeclaringOriginAndPreservesThePinnedTarget(bool hasDefault)
    {
        var f = Fixture(hasDefault: hasDefault); var basis = ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]); Assert.True(basis.BindingComplete);
        var original = ProjectScenarios.Prepare(basis, f.Input.InputDigest, ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change]);
        Assert.Equal(ProjectScenarioState.Ready, original.State); var plan = original.Plan!;
        Assert.Equal(CompilerVerdict.Pass, Run(plan.Input, f.Snapshot).Verdict);
        var pinned = Reference(plan.Input); Assert.Equal(plan.Input.Source.Context, pinned.Origin.Context);
        var later = Fixture(55, hasDefault); var model = ProjectSemanticBinder.Bind(later.Input, [later.Snapshot]); Assert.True(model.BindingComplete);
        var result = ProjectScenarios.Rebase(plan, model, later.Input.InputDigest, ScenarioRevisionId.Parse(Id(802)));
        Assert.Equal(ProjectScenarioState.Ready, result.State); var after = result.Plan!.Input;
        var reference = Reference(after);
        Assert.Equal(after.Source.Context, reference.Origin.Context); Assert.Equal(pinned.Context, reference.Context);
        Assert.Equal(pinned.ManifestDigest, reference.ManifestDigest); Assert.Equal(pinned.ManifestVersion, reference.ManifestVersion); Assert.Equal(pinned.EntityId, reference.EntityId);
        Assert.True(ProjectSemanticBinder.Bind(after, [later.Snapshot]).BindingComplete);
        Assert.Equal(CompilerVerdict.Pass, Run(after, later.Snapshot).Verdict);
        Assert.NotEqual(CompilerVerdict.Pass, ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(after), Evaluator).Execute(after.InputDigest).Verdict);
        Assert.Equal(CompilerVerdict.NotRun, ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(after, [later.Snapshot]), Evaluator).Execute(plan.Input.InputDigest).Verdict);
        var revision = later.Input.Source.Context.CommittedRevision!;
        var conversion = ProjectScenarios.ToDraft(result.Plan, model, later.Input.InputDigest,
            new(ProposalId.Parse(Id(810)), CandidateRevisionId.Parse(Id(811)), revision, revision));
        Assert.Equal(ProjectScenarioState.Ready, conversion.State);
        Assert.Equal(conversion.Plan!.After.Source.Context, Reference(conversion.Plan.After).Origin.Context);
        Assert.Equal(pinned.Context, Reference(conversion.Plan.After).Context);
        Assert.Equal(CompilerVerdict.Pass, Run(conversion.Plan.After, later.Snapshot).Verdict);
    }
    [Theory]
    [InlineData("nullable")] [InlineData("optional")] [InlineData("structure")]
    [InlineData("list")] [InlineData("sequence")] [InlineData("set")] [InlineData("map")]
    public void NestedTypedOriginsAdvanceWithoutChangingPinnedTargetsOrCollectionShape(string wrapper)
    {
        var f = Fixture(wrapper: wrapper); var model = ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]); Assert.True(model.BindingComplete);
        var original = ProjectScenarios.Prepare(model, f.Input.InputDigest, ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change]).Plan!;
        Assert.NotNull(original); var pin = Assert.Single(References(Default(original.Input)));
        var later = Fixture(55, wrapper: wrapper); var next = ProjectSemanticBinder.Bind(later.Input, [later.Snapshot]); Assert.True(next.BindingComplete);
        var result = ProjectScenarios.Rebase(original, next, later.Input.InputDigest, ScenarioRevisionId.Parse(Id(802)));
        Assert.Equal(ProjectScenarioState.Ready, result.State); var input = result.Plan!.Input;
        var reference = Assert.Single(References(Default(input))); Assert.Equal(input.Source.Context, reference.Origin.Context);
        Assert.Equal(pin.Context, reference.Context); Assert.Equal(pin.ManifestDigest, reference.ManifestDigest); Assert.Equal(pin.EntityId, reference.EntityId);
        Assert.Equal(Default(f.Input).Type.Data, Default(input).Type.Data); Assert.True(ProjectSemanticBinder.Bind(input, [later.Snapshot]).BindingComplete);
        var revision = later.Input.Source.Context.CommittedRevision!;
        var draft = ProjectScenarios.ToDraft(result.Plan, next, later.Input.InputDigest, new(ProposalId.Parse(Id(810)), CandidateRevisionId.Parse(Id(811)), revision, revision)).Plan!;
        Assert.NotNull(draft); Assert.Equal(draft.After.Source.Context, Assert.Single(References(Default(draft.After))).Origin.Context);
        Assert.Equal(pin.Context, Assert.Single(References(Default(draft.After))).Context); Assert.True(ProjectSemanticBinder.Bind(draft.After, [later.Snapshot]).BindingComplete);
        Assert.Equal(original.Input.Source.Data, original.Materialize(f.Input)!.Source.Data);
    }

    [Fact]
    public void ActualPinnedTargetChangesRemainConflictsAndDeniedSnapshotsNeverBecomePass()
    {
        var f = Fixture(); var model = ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]);
        var original = ProjectScenarios.Prepare(model, f.Input.InputDigest, ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change]).Plan!;
        var changed = Fixture(55, defaultTarget: 16);
        var conflict = ProjectScenarios.Rebase(original, ProjectSemanticBinder.Bind(changed.Input, [changed.Snapshot]), changed.Input.InputDigest, ScenarioRevisionId.Parse(Id(802)));
        Assert.Equal(ProjectScenarioState.Conflict, conflict.State); Assert.Null(conflict.Plan);
        var later = Fixture(55); var next = ProjectSemanticBinder.Bind(later.Input, [later.Snapshot]);
        var rebased = ProjectScenarios.Rebase(original, next, later.Input.InputDigest, ScenarioRevisionId.Parse(Id(802))).Plan!;
        var denied = new CompilerMaterializedSnapshot(later.Snapshot.SnapshotId, later.Snapshot.Version, new(later.Snapshot.Snapshot.Source, false, true, true));
        Assert.NotEqual(CompilerVerdict.Pass, Run(rebased.Input, denied).Verdict); Assert.Equal(CompilerVerdict.Pass, Run(rebased.Input, later.Snapshot).Verdict);
    }

    [Fact]
    public void ReferenceShapedUnknownBasisIsNotRewrittenDuringComparisonOrApplication()
    {
        var f = Fixture(opaqueBefore: true); var originalData = Default(f.Input).Data;
        var original = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]), f.Input.InputDigest,
            ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change]).Plan!;
        var later = Fixture(55, opaqueBefore: true); Assert.Equal(originalData, Default(later.Input).Data);
        var result = ProjectScenarios.Rebase(original, ProjectSemanticBinder.Bind(later.Input, [later.Snapshot]), later.Input.InputDigest, ScenarioRevisionId.Parse(Id(802)));
        Assert.Equal(ProjectScenarioState.Ready, result.State); Assert.Equal(CompilerVerdict.Pass, Run(result.Plan!.Input, later.Snapshot).Verdict);
        Assert.Equal(originalData, Default(original.Basis).Data); Assert.Equal(originalData, Default(result.Plan.Basis).Data);
    }

    [Fact]
    public void SetOrderingIsCanonicalWhileAnUnrelatedForeignOriginStaysUnchanged()
    {
        (CompilerInputManifest Input, CompilerMaterializedSnapshot Snapshot, ProjectScenarioOverride Change) Mixed(int revision)
        {
            var f = Fixture(revision, defaultTarget: 16, wrapper: "set");
            var declarations = JsonNode.Parse(f.Input.Source.Declarations.CanonicalText)!.AsArray();
            var value = declarations[0]!["payload"]!["ports"]![0]!["defaultValue"]!.AsObject();
            var local = value["value"]![0]!.DeepClone(); var foreign = local.DeepClone();
            foreign["value"]!["origin"]!["context"] = JsonNode.Parse(Source(1).Context.Data.CanonicalText);
            JsonArray Ordered(params JsonNode[] nodes) => new(nodes.OrderBy(node => Json((JsonObject)node).CanonicalText, StringComparer.Ordinal).ToArray());
            value["value"] = Ordered(local, foreign);
            var input = new CompilerInputManifest(ProjectSource.Create(f.Input.Source.Context, f.Input.Source.Documents, Json(declarations)), f.Input.Settings);
            var replacement = JsonNode.Parse(Default(input).Data.CanonicalText)!.AsObject();
            var entries = replacement["value"]!.AsArray().Select(x => x!.DeepClone()).ToArray();
            entries.Single(x => SourceJson.Parse(x["value"]!["origin"]!["context"]!.ToJsonString()).Equals(input.Source.Context.Data))
                ["value"]!["origin"]!["textSpan"] = Obj(new { start = 0, length = 1 });
            replacement["value"] = Ordered(entries);
            return (input, f.Snapshot, f.Change with { Value = Json(replacement) });
        }
        var f = Mixed(5); var original = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]), f.Input.InputDigest,
            ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change]).Plan!;
        Assert.NotNull(original); var later = Mixed(55);
        var result = ProjectScenarios.Rebase(original, ProjectSemanticBinder.Bind(later.Input, [later.Snapshot]), later.Input.InputDigest, ScenarioRevisionId.Parse(Id(802)));
        Assert.Equal(ProjectScenarioState.Ready, result.State); var value = Default(result.Plan!.Input);
        var collection = Assert.IsType<ProjectCollectionValue>(value.KnownValue);
        Assert.Equal(collection.Items.Select(x => x.Data.CanonicalText).OrderBy(x => x, StringComparer.Ordinal), collection.Items.Select(x => x.Data.CanonicalText));
        var references = References(value).ToArray(); Assert.Equal(2, references.Length);
        Assert.Single(references, x => x.Origin.Context.Equals(result.Plan.Input.Source.Context)); Assert.Single(references, x => x.Origin.Context.Equals(Source(1).Context));
        Assert.All(references, x => Assert.Equal(f.Snapshot.Snapshot.Source.Context, x.Context));
        Assert.NotEqual(CompilerVerdict.Pass, Run(result.Plan.Input, later.Snapshot).Verdict);
        Assert.Equal(ValueDecodeStatus.Decoded, TypedProjectValueCodec.Decode(value.Data).Status);
        var projected = ProjectScenarioAppliedValues.Read(result.Plan, result.Plan.Input).Projection!;
        Assert.NotNull(projected); Assert.Contains(projected.Values[0].OriginEdits, edit => edit.Value.GetProperty("kind").GetString() == "SetOrder");
    }

    [Fact]
    public void AbsentOptionalBeforeValuesStayAbsentWhileTheNewReferenceOriginAdvances()
    {
        (CompilerInputManifest Input, CompilerMaterializedSnapshot Snapshot, ProjectScenarioOverride Change) Absent(int revision)
        {
            var f = Fixture(revision, wrapper: "optional"); var declarations = JsonNode.Parse(f.Input.Source.Declarations.CanonicalText)!.AsArray();
            declarations[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"] = Obj(new { present = false });
            return (new(ProjectSource.Create(f.Input.Source.Context, f.Input.Source.Documents, Json(declarations)), f.Input.Settings), f.Snapshot, f.Change);
        }
        var f = Absent(5); var original = ProjectScenarios.Prepare(ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]), f.Input.InputDigest,
            ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [f.Change]).Plan!;
        var later = Absent(55); var before = Default(later.Input).Data;
        var result = ProjectScenarios.Rebase(original, ProjectSemanticBinder.Bind(later.Input, [later.Snapshot]), later.Input.InputDigest, ScenarioRevisionId.Parse(Id(802)));
        Assert.Equal(ProjectScenarioState.Ready, result.State); Assert.Equal(before, Default(result.Plan!.Basis).Data);
        Assert.False(Assert.IsType<OptionalProjectValue>(Default(result.Plan.Basis).KnownValue).Present);
        var reference = Assert.Single(References(Default(result.Plan.Input)));
        Assert.Equal(result.Plan.Input.Source.Context, reference.Origin.Context); Assert.Equal(f.Snapshot.Snapshot.Source.Context, reference.Context);
        Assert.True(ProjectSemanticBinder.Bind(result.Plan.Input, [later.Snapshot]).BindingComplete);
    }

}
