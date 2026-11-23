using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerExecutionPlanTests
{
    internal static CompilerInputManifest Request(CompilerInputManifest input, bool engine = true)
    {
        input = CompilerScopedPublicationTests.Upgrade(input);
        var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
        settings["targets"]!.AsArray().Add(CompilerExecutionPlan.Target);
        if (engine) settings["scheduler"] = Obj(new { kind = "coglatas.plan-lowering", artifact = CompilerExecutionPlan.Engine.Data.Value });
        return new(input.Source, Json(settings));
    }
    private static JsonObject Duration(string? ticks) => ticks is null
        ? Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.unknown", reason = "coglatas.missing-input", basis = new { coverage = "coglatas.partial" } })
        : Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = ticks });
    private static CompilerInputManifest Dag(string mode = "coglatas.finish-to-start", string? lag = "0", string? duration = "1", bool reverse = false)
    {
        var source = Source();
        var declarations = new[] { 7, 8, 9 }.Select(id => Obj(new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
            payload = new { duration = Duration(duration), ports = Array.Empty<object>() } })).ToArray();
        var relations = new[] { 7, 8 }.Select(id => Obj(new { relationId = Id(20 + id), documentId = Id(4), kind = RelationKinds.DependsOn, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 },
            source = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(9, source.Context) },
            target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(id, source.Context) },
            payload = new { mode, lag = Duration(lag) } })).ToArray();
        return Request(Input(source: ProjectSource.Create(source.Context, source.Documents, Json(new JsonArray((reverse ? declarations.Reverse() : declarations).Select(x => (JsonNode)x).ToArray())),
            Json(new JsonArray((reverse ? relations.Reverse() : relations).Select(x => (JsonNode)x).ToArray())))));
    }
    private static CoreCompilation Compile(CompilerInputManifest input, params CompilerMaterializedSnapshot[] snapshots) => PureCoreCompiler.Compile(input, input.Compiler, snapshots);
    [Fact]
    public void ProducesAnExactImmutableDefinitionWithStableDependencyDirectionAndNoRunState()
    {
        var input = Dag(); var compiled = Compile(input); var plan = compiled.Publication!.ExecutionPlan!;
        Assert.Equal(CompilerVerdict.Pass, compiled.Result.Verdict); Assert.Equal(CompilerCoverage.Complete, compiled.Result.Coverage);
        Assert.Equal(CompilerExecutionPlan.Format, plan.Data.Value.GetProperty("format").GetString()); Assert.Equal(3, plan.Data.Value.GetProperty("tasks").GetArrayLength());
        Assert.Equal(2, plan.Data.Value.GetProperty("dependencies").GetArrayLength());
        Assert.Equal("coglatas.not-instantiated", plan.Data.Value.GetProperty("execution").GetString());
        Assert.Equal(plan.Digest, ContentDigest.Compute(CompilerExecutionPlan.Format, plan.Data.ToCanonicalBytes()));
        var order = plan.Data.Value.GetProperty("definitionOrder").EnumerateArray().Select(x => x.GetProperty("irNodeId").GetString()).ToArray();
        var tasks = plan.Data.Value.GetProperty("tasks").EnumerateArray().ToDictionary(x => x.GetProperty("sourceEntityId").GetString()!, x => x.GetProperty("irNodeId").GetString());
        Assert.Equal(new[] { tasks[Id(7)], tasks[Id(8)], tasks[Id(9)] }, order);
        var edge = plan.Data.Value.GetProperty("dependencies")[0]; Assert.Equal(tasks[Id(9)], edge.GetProperty("dependent").GetProperty("irNodeId").GetString());
        Assert.Equal(tasks[Id(7)], edge.GetProperty("prerequisite").GetProperty("irNodeId").GetString());
        Assert.True(compiled.Publication.IsDeclaredBy(compiled.Result)); Assert.Contains(compiled.Result.Stages, s => s.StageId == "coglatas.plan-definition" && s.Execution == CompilerExecutionState.Completed);
        Assert.Equal(plan.Data, SourceJson.Parse(compiled.Publication.Ir.Value.GetProperty("executionPlan").GetProperty("definition").GetRawText()));
        Assert.Equal(compiled.Result.Data, Compile(input).Result.Data); Assert.True(compiled.LogicalOperationsConsumed <= input.ExecutionProfile.MaximumOperations);
    }
    [Fact]
    public void ReorderedSourceSetsKeepTheSameStableDefinitionOrder()
    {
        var a = Compile(Dag()).Publication!.ExecutionPlan!; var b = Compile(Dag(reverse: true)).Publication!.ExecutionPlan!;
        Assert.Equal(a.Data.Value.GetProperty("definitionOrder").GetRawText(), b.Data.Value.GetProperty("definitionOrder").GetRawText());
    }
    [Fact]
    public void DeclaredTransitiveVersionsRetainDifferentContextSlotsAndTheSameStableNodeIds()
    {
        var f = CompilerBindingClosureTests.Graph(withRelation: true, twoBVersions: true); var input = Request(f.Input); var compiled = Compile(input, f.Snapshots);
        var plan = compiled.Publication!.ExecutionPlan!; Assert.NotNull(plan);
        var versions = plan.Data.Value.GetProperty("tasks").EnumerateArray().Where(t => t.GetProperty("sourceEntityId").GetString() == Id(77)).ToArray();
        Assert.Equal(2, versions.Length); Assert.Equal(versions[0].GetProperty("irNodeId").GetString(), versions[1].GetProperty("irNodeId").GetString());
        Assert.NotEqual(versions[0].GetProperty("context").GetRawText(), versions[1].GetProperty("context").GetRawText());
        var cached = PureCoreCompiler.Compile(input, input.Compiler, f.Snapshots.Reverse(), declarationCache: new PlanDeclarationReadCache());
        Assert.Equal(compiled.Result.Data, cached.Result.Data); Assert.Equal(plan.Data, cached.Publication!.ExecutionPlan!.Data);
    }
    [Theory]
    [InlineData("coglatas.start-to-start", "0", "1")] [InlineData("coglatas.finish-to-finish", "0", "1")] [InlineData("coglatas.start-to-finish", "0", "1")]
    [InlineData("coglatas.finish-to-start", "-1", "1")] [InlineData("coglatas.finish-to-start", null, "1")] [InlineData("coglatas.finish-to-start", "0", null)]
    public void UnsupportedOrUnknownPlanSemanticsRemainNotProduced(string mode, string? lag, string? duration)
    {
        var compiled = Compile(Dag(mode, lag, duration)); Assert.Null(compiled.Publication?.ExecutionPlan);
        if (compiled.Publication is null) {
            Assert.Empty(compiled.Result.Artifacts); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
            Assert.Contains(compiled.Result.Diagnostics, d => d.Code is "coglatas.relation.mode-not-lowered" or "coglatas.relation.lag-unknown" or "coglatas.relation.unknown-lag");
            Assert.DoesNotContain(compiled.Result.Stages, s => s.StageId == "coglatas.plan-definition" && s.Execution == CompilerExecutionState.Completed); return;
        }
        Assert.NotNull(compiled.Publication.PlanNotProducedReason); Assert.Equal(CompilerCoverage.Partial, compiled.Result.Coverage); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
        Assert.False(compiled.Result.EvidenceComplete); Assert.True(compiled.Publication.IsDeclaredBy(compiled.Result));
        Assert.Contains(compiled.Result.Stages, s => s.StageId == "coglatas.plan-definition" && s.Execution == CompilerExecutionState.NotRun);
    }
    [Theory]
    [InlineData("none")] [InlineData("version")] [InlineData("digest")]
    public void PlanEngineCompatibilityIsActuallyLockedIntoTheInput(string mode)
    {
        var original = Request(Input(), engine: mode != "none"); var settings = JsonNode.Parse(original.Settings.CanonicalText)!.AsObject();
        if (mode == "version") settings["scheduler"]!["artifact"]!["version"] = "2";
        if (mode == "digest") settings["scheduler"]!["artifact"]!["digest"]!["value"] = new string('b', 64);
        var input = new CompilerInputManifest(original.Source, Json(settings)); var compiled = Compile(input);
        Assert.Null(compiled.Publication!.ExecutionPlan); Assert.Equal("coglatas.plan.engine-unsupported", compiled.Publication.PlanNotProducedReason); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
        if (mode != "none") Assert.NotEqual(original.InputDigest, input.InputDigest);
    }
    [Theory]
    [InlineData(false, CompilerVerdict.Fail)] [InlineData(null, CompilerVerdict.Inconclusive)]
    public void RequiredFalseOrUnknownCannotBecomeAProducedPlan(bool? truth, CompilerVerdict verdict)
    {
        var compiled = Compile(Request(CompilerAnalysisFixtures.Constraint(truth))); Assert.Null(compiled.Publication!.ExecutionPlan);
        Assert.Equal(verdict, compiled.Result.Verdict); Assert.Equal("coglatas.plan.required-validation-incomplete", compiled.Publication.PlanNotProducedReason);
    }
    [Fact]
    public void OmittingTheRequiredConstraintRuleCannotAuthorizeAPlan()
    {
        var compiled = Compile(Request(CompilerAnalysisFixtures.Constraint(true, allRules: false)));
        Assert.Contains(compiled.Result.Diagnostics, d => d.Code == "coglatas.analysis.required-family-missing"); Assert.Equal("coglatas.plan.required-validation-incomplete", compiled.Publication!.PlanNotProducedReason); Assert.Null(compiled.Publication.ExecutionPlan); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
    }
    [Fact]
    public void NoPlanTargetPreservesTheExistingVersionTwoPublication()
    {
        var compiled = Compile(CompilerScopedPublicationTests.Upgrade(Input())); Assert.Null(compiled.Publication!.ExecutionPlan); Assert.Null(compiled.Publication.PlanNotProducedReason);
        Assert.DoesNotContain(compiled.Result.Stages, s => s.StageId == "coglatas.plan-definition"); Assert.Equal(CompilerVerdict.Pass, compiled.Result.Verdict);
    }
    [Fact]
    public void LegacyTargetsCannotAdvertisePlanSuccess()
    {
        var input = Input(); var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(); settings["targets"]!.AsArray().Add(CompilerExecutionPlan.Target);
        var compiled = Compile(new(input.Source, Json(settings))); Assert.Null(compiled.Publication); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
        Assert.Contains(compiled.Result.Diagnostics, d => d.Code == "coglatas.plan.requires-version-two");
    }
    private static CompilerHostProfile Profile(CompilerInputManifest input, bool engine) {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(x => x.GetString()), engine ? CompilerExecutionPlan.Engine : null);
    }
    private sealed class Transport(ICoreCompilerHost local) : IServerCompilerTransport {
        public Task<CompilerHostResponse> SubmitAsync(CompilerHostRequest request, CancellationToken token = default) => local.CompileAsync(request, token);
    }
    [Fact]
    public async Task FixedLocalServerAndWindowsPureConsumersShareTheActualDefinitionWithoutStartingARun()
    {
        var input = Dag(); Assert.False(Profile(input, false).Supports(input)); var profile = Profile(input, true);
        var local = new LocalNetCompilerHost(profile); var server = new ServerCompilerHost(profile, new Transport(local)); var request = new CompilerHostRequest(new(input), 1);
        var a = await local.CompileAsync(request); var b = await server.CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, a.Status); Assert.Equal(CompilerHostStatus.Completed, b.Status);
        Assert.Equal(a.Compilation!.Result.Data, b.Compilation!.Result.Data);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(a));
        Assert.Null(view.ScopedExecutionPlan); Assert.True(view.LoadSyntheticScopedSources());
        Assert.Same(a.Compilation.Publication!.ExecutionPlan, view.ScopedExecutionPlan); Assert.True(view.HasCompleteLocalPass);
        view.BeginSynthetic(new(new(input), 2)); Assert.Null(view.ScopedExecutionPlan);
    }
    [Fact]
    public async Task APlanEnabledHostAlsoAcceptsAnOrdinaryNoPlanInputButStillRejectsUnboundPlanRequests()
    {
        var input = CompilerScopedPublicationTests.Upgrade(Input()); var profile = Profile(input, true);
        Assert.True(profile.Supports(input)); Assert.False(profile.Supports(Request(input, engine: false)));
        var ordinary = await new ServerCompilerHost(profile, new Transport(new LocalNetCompilerHost(profile))).CompileAsync(new(new(input), 1));
        Assert.Equal(CompilerHostStatus.Completed, ordinary.Status); Assert.Equal(CompilerVerdict.Pass, ordinary.Compilation!.Result.Verdict);
        Assert.Null(ordinary.Compilation.Publication!.ExecutionPlan);
        var requested = Request(input); var settings = JsonNode.Parse(requested.Settings.CanonicalText)!.AsObject(); settings["scheduler"]!["artifact"]!["digest"]!["value"] = new string('b',64);
        Assert.False(profile.Supports(new(input.Source, Json(settings))));
    }
    private sealed class ReplyTransport(Func<CompilerHostRequest, CompilerHostResponse> reply) : IServerCompilerTransport {
        public Task<CompilerHostResponse> SubmitAsync(CompilerHostRequest request, CancellationToken token = default) => Task.FromResult(reply(request));
    }
    [Theory]
    [InlineData("missing")] [InlineData("output")] [InlineData("version")]
    public async Task ActualPlanProducingStageIsRequiredByPublicationAndServerVerification(string mode)
    {
        var input = Dag(); var original = Compile(input); var stages = original.Result.Stages.Where(s => s.StageId != "coglatas.plan-definition").ToList();
        if (mode != "missing") stages.Add(new("coglatas.plan-definition", mode == "version" ? 2 : 1, CompilerExecutionState.Completed,
            CompilerCoverage.Complete, input.InputDigest, [mode == "output" ? input.Source.Digest : original.Publication!.ExecutionPlan!.Digest]));
        var result = new CompilerResult(input, original.Result.Execution, original.Result.Coverage, original.Result.RuleOutcomes, original.Result.Diagnostics, stages, original.Result.EvidenceComplete, original.Result.Artifacts);
        Assert.False(original.Publication!.IsDeclaredBy(result));
        var constructor = typeof(CoreCompilation).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var forged = (CoreCompilation)constructor.Invoke(new object?[] { input, original.Model, original.Validation, result, original.LogicalOperationsConsumed, original.Dependencies, original.Publication });
        var server = new ServerCompilerHost(Profile(input, true), new ReplyTransport(request => new(CompilerHostStatus.Completed, "example.forged-stage", input.InputDigest,
            request.ClientGeneration, Guid.NewGuid(), CompilerScopedPublicationTests.Receipt(request, forged))));
        var response = await server.CompileAsync(new(new(input), 2)); Assert.Equal(CompilerHostStatus.InvalidInput, response.Status); Assert.Null(response.Compilation);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DirectAndNestedUnknownInputDefaultsRemainNotExecutable(bool nested)
    {
        var input = Dag(); var declarations = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        var boolean = Obj(new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true });
        var unknown = Obj(new { type = boolean, state = "coglatas.unknown", reason = "coglatas.missing-input", basis = new { coverage = "coglatas.partial" } });
        var type = nested ? Obj(new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = true, elementType = boolean }) : boolean;
        var value = nested ? Obj(new { type, state = "coglatas.known", value = new[] { unknown } }) : unknown;
        declarations[0]!["payload"]!["ports"] = new JsonArray(Obj(new { portId = Id(90), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1, defaultValue = value }));
        var source = ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations), input.Source.Relations);
        var compiled = Compile(new(source, input.Settings)); Assert.Null(compiled.Publication!.ExecutionPlan);
        Assert.Equal("coglatas.plan.input-unsupported", compiled.Publication.PlanNotProducedReason); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
    }
    [Theory]
    [InlineData("milestone")] [InlineData("workflow")]
    public void WholeDeclaredUnsupportedKindsCannotBeSilentlyDropped(string kind)
    {
        var input = Dag(); var declarations = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        object payload = kind == "milestone" ? new { target = new { type = new { kind = "coglatas.instant", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "2026-11-14T00:00:00.0000000Z" } }
            : new { states = new[] { Id(100) }, initialState = Id(100), maximumTransitions = 1, transitions = Array.Empty<object>() };
        declarations.Add(Obj(new { entityId = Id(99), documentId = Id(4), kind = "coglatas." + kind, schemaVersion = 1, payload }));
        var source = ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations), input.Source.Relations); var compiled = Compile(new(source, input.Settings));
        Assert.Null(compiled.Publication?.ExecutionPlan); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
        if (compiled.Publication is { } pub) Assert.Equal("coglatas.plan.declaration-unsupported", pub.PlanNotProducedReason);
    }
    [Fact]
    public void MilestoneMembershipCannotBeSilentlyFlattenedIntoAnElapsedTaskDefinition()
    {
        var input = Dag(); var declarations = JsonNode.Parse(input.Source.Declarations.CanonicalText)!.AsArray();
        declarations.Add(Obj(new { entityId = Id(99), documentId = Id(4), kind = "coglatas.milestone", schemaVersion = 1,
            payload = new { target = new { type = new { kind = "coglatas.instant", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "2026-11-14T00:00:00.0000000Z" } } }));
        var relations = JsonNode.Parse(input.Source.Relations.CanonicalText)!.AsArray();
        relations.Add(Obj(new { relationId = Id(88), documentId = Id(4), kind = RelationKinds.Contains, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.Contains, version = 1 },
            source = new { kind = "coglatas.entity", expectedKind = "coglatas.milestone", reference = CompilerAnalysisFixtures.Reference(99, input.Source.Context) },
            target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(8, input.Source.Context) }, payload = new { } }));
        var source = ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations), Json(relations));
        var compiled = Compile(new(source, input.Settings)); Assert.NotNull(compiled.Publication);
        Assert.Null(compiled.Publication.ExecutionPlan); Assert.Equal("coglatas.plan.declaration-unsupported", compiled.Publication.PlanNotProducedReason);
        Assert.DoesNotContain(compiled.Result.Diagnostics, d => d.Code == "coglatas.relation.endpoint-kind"); Assert.NotEqual(CompilerVerdict.Pass, compiled.Result.Verdict);
    }
    [Fact]
    public void ExhaustionAfterValidatedScopeDiscardsPlanAndNormalPublication()
    {
        var input = Dag(); var full = Compile(input); var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = full.LogicalOperationsConsumed - full.Publication!.ExecutionPlan!.LogicalOperationsConsumed / 2;
        var limited = Compile(new(input.Source, Json(settings)));
        Assert.NotNull(limited.Dependencies!.ScopedIr); Assert.Null(limited.Publication); Assert.Empty(limited.Result.Artifacts);
        Assert.Equal(CompilerDependencyAnalysisState.ResourceLimit, limited.Dependencies.State); Assert.Equal(CompilerExecutionState.ResourceExhausted, limited.Result.Execution);
        Assert.NotEqual(CompilerVerdict.Pass, limited.Result.Verdict);
    }
    [Fact]
    public void CancellationAndInsufficientRootWorkCannotPublishAPlan()
    {
        var input = Dag(); using var token = new CancellationTokenSource(); token.Cancel();
        var cancelled = PureCoreCompiler.Compile(input, input.Compiler, cancellationToken: token.Token); Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Result.Execution); Assert.Null(cancelled.Publication);
        var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = 100;
        var limited = Compile(new(input.Source, Json(settings))); Assert.Null(limited.Publication?.ExecutionPlan); Assert.NotEqual(CompilerVerdict.Pass, limited.Result.Verdict);
    }
}
