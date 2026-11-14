using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectSimulationTests
{
    private static readonly Guid RunId = Guid.Parse(Id(950));
    private static JsonObject Duration(long ticks) => Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    private static CompilerExecutionPlan Plan(long first = 5, long second = 2, long last = 3, long firstLag = 1, long secondLag = 10, bool reverse = false)
    {
        var source = Source();
        var declarations = new[] { (7, first), (8, second), (9, last) }.Select(row => Obj(new { entityId = Id(row.Item1), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
            payload = new { duration = Duration(row.Item2), ports = Array.Empty<object>() } })).ToArray();
        var relations = new[] { (7, firstLag), (8, secondLag) }.Select(row => Obj(new { relationId = Id(30 + row.Item1), documentId = Id(4), kind = RelationKinds.DependsOn, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 },
            source = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(9, source.Context) },
            target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(row.Item1, source.Context) },
            payload = new { mode = "coglatas.finish-to-start", lag = Duration(row.Item2) } })).ToArray();
        var input = CompilerExecutionPlanTests.Request(Input(source: ProjectSource.Create(source.Context, source.Documents,
            Json(new JsonArray((reverse ? declarations.Reverse() : declarations).Select(x => (JsonNode)x).ToArray())),
            Json(new JsonArray((reverse ? relations.Reverse() : relations).Select(x => (JsonNode)x).ToArray())))));
        return PureCoreCompiler.Compile(input, input.Compiler).Publication!.ExecutionPlan!;
    }
    private static ProjectSimulationRun Run(CompilerExecutionPlan plan, SimulationRunLimits? limits = null, long start = 100) =>
        ProjectSimulation.Execute(new(plan, RunId, start, limits), plan.Input.InputDigest);
    private static string Entity(SourceJson item) => item.Value.GetProperty("location").GetProperty("entityId").GetString()!;
    private static string Kind(SourceJson item) => item.Value.GetProperty("kind").GetString()!;
    private static long Time(SourceJson item) => long.Parse(item.Value.GetProperty("ticks").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ParallelRootsAndAllJoinLagsUseAVirtualClockRatherThanTheDefinitionOrderAsACallStack()
    {
        var plan = Plan(); var original = plan.Data; var result = Run(plan);
        Assert.Equal(CompilerExecutionState.Completed, result.Execution); Assert.Equal(CompilerCoverage.Complete, result.Coverage);
        Assert.Equal(CompilerVerdict.NotRun, result.Verdict); Assert.Equal(CompilerFreshness.Current, result.Freshness);
        Assert.Equal(3, result.StartedTasks); Assert.Equal(3, result.CompletedTasks); Assert.Equal(115, result.VirtualTicks);
        Assert.Equal(new long[] { 100, 100, 102, 105, 112, 115 }, result.Events.Select(Time));
        Assert.Equal(new[] { Id(7), Id(8), Id(8), Id(7), Id(9), Id(9) }, result.Events.Select(Entity));
        Assert.Equal(new[] { "coglatas.task-start", "coglatas.task-start", "coglatas.task-end", "coglatas.task-end", "coglatas.task-start", "coglatas.task-end" }, result.Events.Select(Kind));
        Assert.Equal(original, plan.Data); Assert.Equal("coglatas.not-instantiated", plan.Data.Value.GetProperty("execution").GetString());
        Assert.Equal(ContentDigest.Compute(ProjectSimulationRun.Format, result.Data.ToCanonicalBytes()), result.Digest);
        Assert.Equal(ContentDigest.Compute(SimulationRunManifest.Format, result.Manifest.Data.ToCanonicalBytes()), result.Manifest.Digest);
        Assert.Equal("coglatas.simulation", result.Data.Value.GetProperty("evidenceKind").GetString());
        Assert.Equal("coglatas.no-outcome-assertions", result.Data.Value.GetProperty("verdictReason").GetString());
    }
    [Fact]
    public void SameManifestReplaysExactlyAndANewRunHasANewIdentityWithoutOverwritingOldEvidence()
    {
        var plan = Plan(); var manifest = new SimulationRunManifest(plan, RunId); var first = ProjectSimulation.Execute(manifest, plan.Input.InputDigest);
        var replay = ProjectSimulation.Execute(manifest, plan.Input.InputDigest); Assert.Equal(first.Data, replay.Data); Assert.Equal(first.Digest, replay.Digest);
        var next = ProjectSimulation.Execute(new(plan, Guid.Parse(Id(951)), originRunDigest: first.Digest), plan.Input.InputDigest);
        Assert.NotEqual(first.Manifest.Digest, next.Manifest.Digest); Assert.NotEqual(first.Digest, next.Digest);
        Assert.Equal(first.Events.Select(x => x.CanonicalText), next.Events.Select(x => x.CanonicalText));
        Assert.Equal(first.Digest.Data.Value.GetRawText(), next.Manifest.Data.Value.GetProperty("originRunDigest").GetRawText());
        var bytes = first.Data.ToCanonicalBytes(); bytes[0] = 0; Assert.Equal(first.Data, replay.Data);
        Assert.False(first.Events is SourceJson[]);
        var publicList = Assert.IsAssignableFrom<IList<SourceJson>>(first.Events); Assert.True(publicList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => publicList[0] = first.Events[1]);
        Assert.Throws<NotSupportedException>(() => publicList.Clear()); Assert.Equal(first.Data, replay.Data);
    }
    [Fact]
    public void StableSimultaneousOrderingSurvivesSourceSetReorderingAndZeroDuration()
    {
        var a = Run(Plan(0, 0, 0, 0, 0)); var b = Run(Plan(0, 0, 0, 0, 0, reverse: true));
        Assert.Equal(a.Events.Select(x => x.CanonicalText), b.Events.Select(x => x.CanonicalText));
        Assert.All(a.Events, item => Assert.Equal(100, Time(item)));
        Assert.Equal(new[] { Id(7), Id(7), Id(8), Id(8), Id(9), Id(9) }, a.Events.Select(Entity));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, a.Events.Select(x => x.Value.GetProperty("sequence").GetInt32()));
        Assert.Equal(ProjectSimulation.Policy, a.Manifest.Data.Value.GetProperty("policy").GetString());
    }
    [Theory]
    [InlineData(1, 1, 0)] [InlineData(2, 2, 0)] [InlineData(3, 2, 1)] [InlineData(5, 3, 2)]
    public void EventExhaustionRetainsExactlyTheCommittedTransitionPrefix(int cap, int started, int completed)
    {
        var result = Run(Plan(), new(MaximumEvents: cap));
        Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Execution); Assert.Equal(CompilerCoverage.Partial, result.Coverage);
        Assert.Equal("coglatas.simulation.event-bound", result.Reason); Assert.Equal(cap, result.Events.Count);
        Assert.Equal(started, result.StartedTasks); Assert.Equal(completed, result.CompletedTasks); Assert.NotEqual(CompilerVerdict.Pass, result.Verdict);
    }
    [Theory]
    [InlineData(0, 2, 100)] [InlineData(2, 3, 102)] [InlineData(14, 5, 112)] [InlineData(15, 6, 115)]
    public void InclusiveElapsedTimeBoundDoesNotReadOrAdvanceAWallClock(long limit, int count, long clock)
    {
        var result = Run(Plan(), new(MaximumElapsedTicks: limit)); Assert.Equal(count, result.Events.Count); Assert.Equal(clock, result.VirtualTicks);
        Assert.Equal(count == 6 ? CompilerExecutionState.Completed : CompilerExecutionState.ResourceExhausted, result.Execution);
        if (count < 6) Assert.Equal("coglatas.simulation.time-bound", result.Reason);
    }
    [Fact]
    public void TraceByteBoundIsExactAndRefusesBeforeAnUnrecordedTransition()
    {
        var plan = Plan(); var full = Run(plan); var firstSize = full.Events[0].ToCanonicalBytes().Length + 2;
        var none = Run(plan, new(MaximumTraceBytes: firstSize - 1)); Assert.Empty(none.Events); Assert.Equal(0, none.StartedTasks); Assert.Equal(CompilerCoverage.None, none.Coverage);
        var one = Run(plan, new(MaximumTraceBytes: firstSize)); Assert.Single(one.Events); Assert.Equal(1, one.StartedTasks); Assert.Equal(0, one.CompletedTasks);
        Assert.Equal("coglatas.simulation.trace-bound", one.Reason); Assert.Equal(CompilerExecutionState.ResourceExhausted, one.Execution);
        var exactAll = 2 + full.Events.Sum(x => x.ToCanonicalBytes().Length) + full.Events.Count - 1;
        Assert.Equal(CompilerExecutionState.Completed, Run(plan, new(MaximumTraceBytes: exactAll)).Execution);
    }
    [Fact]
    public void OperationBoundIncludesPreparationAndEveryRecordedTransition()
    {
        var plan = Plan(); var full = Run(plan); var none = Run(plan, new(MaximumOperations: 1));
        Assert.Equal("coglatas.simulation.operation-bound", none.Reason); Assert.Empty(none.Events); Assert.Equal(0, none.LogicalOperationsConsumed);
        Assert.Equal(CompilerCoverage.None, none.Coverage);
        var prefix = Run(plan, new(MaximumOperations: full.LogicalOperationsConsumed - 1));
        Assert.Equal(CompilerExecutionState.ResourceExhausted, prefix.Execution); Assert.Equal(5, prefix.Events.Count);
        Assert.True(prefix.LogicalOperationsConsumed <= prefix.Manifest.Limits.MaximumOperations);
        Assert.Equal(CompilerExecutionState.Completed, Run(plan, new(MaximumOperations: full.LogicalOperationsConsumed)).Execution);
    }
    [Fact]
    public void StaleInputUnsupportedEngineAndCancellationCannotProduceExecutionOrPass()
    {
        var plan = Plan(); var wrong = ContentDigest.Compute("different-input", [1]);
        var stale = ProjectSimulation.Execute(new(plan, RunId), wrong); Assert.Equal(CompilerFreshness.Stale, stale.Freshness); Assert.Equal(CompilerExecutionState.NotRun, stale.Execution); Assert.Empty(stale.Events);
        var engine = JsonNode.Parse(ProjectSimulation.Engine.Data.CanonicalText)!; engine["version"] = "2";
        var unsupported = ProjectSimulation.Execute(new(plan, RunId, engine: new(Json(engine))), plan.Input.InputDigest);
        Assert.Equal("coglatas.simulation.engine-unsupported", unsupported.Reason); Assert.Equal(CompilerExecutionState.NotRun, unsupported.Execution); Assert.Empty(unsupported.Events);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = ProjectSimulation.Execute(new(plan, RunId), plan.Input.InputDigest, cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Execution); Assert.Equal(CompilerCoverage.None, cancelled.Coverage); Assert.Empty(cancelled.Events);
    }
    [Theory]
    [InlineData(long.MaxValue, 0, 0)] [InlineData(1, long.MaxValue, 0)] [InlineData(1, 0, long.MaxValue)]
    public void StartFinishAndLagOverflowNeverWrapTheClockOrFabricateACompletedRun(long start, long duration, long lag)
    {
        var result = Run(Plan(duration == 0 ? 1 : duration, 0, 0, lag, 0), start: start);
        Assert.Equal(CompilerExecutionState.InternalError, result.Execution); Assert.Equal("coglatas.simulation.tick-overflow", result.Reason);
        Assert.All(result.Events, item => Assert.True(Time(item) >= start)); Assert.NotEqual(CompilerVerdict.Pass, result.Verdict);
    }
    [Fact]
    public void ExactCapturedRevisionSlotsDoNotCollapseWhenStableTaskNodeIdsMatch()
    {
        var graph = CompilerBindingClosureTests.Graph(withRelation: true, twoBVersions: true);
        var input = CompilerExecutionPlanTests.Request(graph.Input); var plan = PureCoreCompiler.Compile(input, input.Compiler, graph.Snapshots).Publication!.ExecutionPlan!;
        var result = Run(plan); Assert.Equal(CompilerExecutionState.Completed, result.Execution);
        var repeated = result.Events.Where(x => Entity(x) == Id(77) && Kind(x) == "coglatas.task-start").ToArray();
        Assert.Equal(2, repeated.Length); Assert.Equal(repeated[0].Value.GetProperty("irNodeId").GetString(), repeated[1].Value.GetProperty("irNodeId").GetString());
        Assert.NotEqual(repeated[0].Value.GetProperty("context").GetRawText(), repeated[1].Value.GetProperty("context").GetRawText());
        Assert.Equal(plan.Data.Value.GetProperty("tasks").GetArrayLength(), result.CompletedTasks);
    }
    [Fact]
    public void EmptyValidatedPlanDoesNotInventTaskExecutionOrPass()
    {
        var input = CompilerExecutionPlanTests.Request(Input());
        var plan = PureCoreCompiler.Compile(input, input.Compiler).Publication!.ExecutionPlan!; Assert.NotNull(plan);
        var result = Run(plan); Assert.Equal(CompilerExecutionState.NotRun, result.Execution); Assert.Equal(CompilerCoverage.None, result.Coverage);
        Assert.Equal("coglatas.simulation.empty-plan", result.Reason); Assert.Empty(result.Events); Assert.Equal(0, result.CompletedTasks);
    }
    [Fact]
    public void EveryDeclaredRunSettingAndSourceBasisParticipatesInIdentity()
    {
        var plan = Plan(); var baseline = new SimulationRunManifest(plan, RunId);
        var manifests = new[] {
            new SimulationRunManifest(plan, RunId, startTicks: 1),
            new SimulationRunManifest(plan, RunId, limits: new(MaximumEvents: 1)),
            new SimulationRunManifest(plan, RunId, limits: new(MaximumOperations: 100)),
            new SimulationRunManifest(plan, RunId, limits: new(MaximumElapsedTicks: 1)),
            new SimulationRunManifest(plan, RunId, limits: new(MaximumTraceBytes: 2)),
            new SimulationRunManifest(Plan(last: 4), RunId)
        };
        Assert.All(manifests, item => Assert.NotEqual(baseline.Digest, item.Digest));
        Assert.Equal(manifests.Length, manifests.Select(item => item.Digest).Distinct().Count());
        Assert.Equal("coglatas.none", baseline.Data.Value.GetProperty("stochastic").GetString());
        Assert.Equal("coglatas.none", baseline.Data.Value.GetProperty("sideEffects").GetString());
    }
    [Theory]
    [InlineData(0, 1, 0, 2)] [InlineData(8193, 1, 0, 2)] [InlineData(1, 0, 0, 2)] [InlineData(1, 1000001, 0, 2)] [InlineData(1, 1, -1, 2)] [InlineData(1, 1, 0, 1)]
    public void InvalidRunLimitsAreRejectedWithoutChangingExistingCompilerProfiles(int events, int operations, long elapsed, int bytes)
    {
        var plan = Plan(); Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationRunManifest(plan, RunId, limits: new(events, operations, elapsed, bytes)));
        Assert.Throws<ArgumentException>(() => new SimulationRunManifest(plan, Guid.Empty));
        Assert.Throws<ArgumentException>(() => new SimulationRunManifest(plan, RunId, -1));
    }
}
