using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SimulationDebuggerTests
{
    private static SimulationRunManifest Manifest(CompilerExecutionPlan? plan = null, SimulationRunLimits? limits = null, int run = 970) => new(plan ?? ProjectSimulationTests.Plan(), Guid.Parse(Id(run)), 100, limits);
    private static ProjectLocation Location(CompilerExecutionPlan plan, int id) => ProjectLocation.Parse(SourceJson.Parse(plan.Data.Value.GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("sourceEntityId").GetString() == Id(id)).GetProperty("location").GetRawText()));
    private static SimulationBreakpoint Breakpoint(SimulationRunManifest manifest, ProjectLocation location, SimulationBreakpointPoint point = SimulationBreakpointPoint.TaskStart, int id = 980) => new(Guid.Parse(Id(id)), manifest.Plan.Digest, location, point);
    private static SimulationDebugSnapshot Accepted(SimulationControlResponse response) { Assert.True(response.Accepted, response.Reason); return Assert.IsType<SimulationDebugSnapshot>(response.Snapshot); }
    private static long Clock(SimulationDebugSnapshot snapshot) => long.Parse(snapshot.Data.Value.GetProperty("virtualTicks").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    private static string InspectionState(SimulationDebugSnapshot snapshot) => snapshot.Inspection!.Value.GetProperty("state").GetString()!;

    [Fact]
    public void EachStepCommitsOneActualEventAndRetainsImmutablePrefixesUntilTheSameTerminalRun()
    {
        var manifest = Manifest(); var expected = ProjectSimulation.Execute(manifest, manifest.Plan.Input.InputDigest);
        using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest); var original = debugger.Snapshot;
        Assert.Equal(SimulationControlState.Ready, original.State); Assert.Null(original.Result); Assert.Empty(original.Events);
        for (var i = 1; i <= 6; i++) {
            var before = debugger.Snapshot; var after = Accepted(debugger.Step(before.Stamp));
            Assert.Equal(i, after.Events.Count); Assert.Equal(i, after.Stamp.Revision); Assert.Equal(i - 1, before.Events.Count);
            Assert.Equal(expected.Events.Take(i).Select(x => x.CanonicalText), after.Events.Select(x => x.CanonicalText));
            Assert.Equal(i == 6 ? SimulationControlState.Terminated : SimulationControlState.Paused, after.State);
            Assert.Equal(ContentDigest.Compute("coglatas.simulation-trace-prefix/1", SourceJson.Parse("[" + string.Join(',', after.Events.Select(x => x.CanonicalText)) + "]").ToCanonicalBytes()), after.TracePrefixDigest);
            Assert.Equal(ContentDigest.Compute(SimulationDebugSnapshot.Format, after.Data.ToCanonicalBytes()), after.Digest);
        }
        Assert.Equal(expected.Data, debugger.Snapshot.Result!.Data); Assert.Null(original.Result); Assert.Empty(original.Events);
        var retained = debugger.Snapshot; var list = Assert.IsAssignableFrom<IList<SourceJson>>(retained.Events);
        Assert.Throws<NotSupportedException>(() => list.Clear()); Assert.Throws<NotSupportedException>(() => list[0] = retained.Events[1]);
        var refused = debugger.Step(retained.Stamp); Assert.False(refused.Accepted); Assert.Equal("coglatas.debug.terminated", refused.Reason); Assert.Null(refused.Snapshot);
    }
    [Fact]
    public void BreaksBeforeTheExactPointAndResumePassesItOnceWithoutInventingACancelledRun()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest);
        var first = ProjectLocation.Parse(SourceJson.Parse(debugger.Snapshot.Pending!.Value.GetProperty("location").GetRawText()));
        var bp = Breakpoint(manifest, first); Accepted(debugger.SetBreakpoints(debugger.Snapshot.Stamp, [bp]));
        var stopped = Accepted(debugger.Continue(debugger.Snapshot.Stamp));
        Assert.Equal(SimulationControlState.Paused, stopped.State); Assert.Empty(stopped.Events); Assert.Null(stopped.Result); Assert.Equal(100, Clock(stopped));
        Assert.Equal(bp.Id.ToString("D"), stopped.Data.Value.GetProperty("hitBreakpointId").GetString());
        var inspected = Accepted(debugger.Inspect(stopped.Stamp, first)); Assert.Equal(SimulationControlState.Paused, inspected.State); Assert.Equal("Ready", InspectionState(inspected));
        var done = Accepted(debugger.Continue(inspected.Stamp)); Assert.Equal(SimulationControlState.Terminated, done.State);
        Assert.Equal(ProjectSimulation.Execute(manifest, manifest.Plan.Input.InputDigest).Data, done.Result!.Data); Assert.Empty(stopped.Events);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StepOverAnAtomicTaskRecordsRelevantParallelStartsAndEndsBeforeItsBoundary(bool supplementarySpan)
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest);
        var task = Location(manifest.Plan, 7);
        if (supplementarySpan) task = ProjectLocation.Create(task.DocumentId, task.Context, task.PropertyPath, entityId: task.EntityId, textSpan: new(1234, 0));
        var stepped = Accepted(debugger.StepOver(debugger.Snapshot.Stamp, task));
        Assert.Equal(SimulationControlState.Paused, stepped.State); Assert.Null(stepped.Result); Assert.Equal(4, stepped.Events.Count); Assert.Equal(105, Clock(stepped));
        Assert.Contains(stepped.Events, e => e.Value.GetProperty("location").GetProperty("entityId").GetString() == Id(8) && e.Value.GetProperty("kind").GetString() == "coglatas.task-end");
        var inspected = Accepted(debugger.Inspect(stepped.Stamp, Location(manifest.Plan, 9))); Assert.Equal("WaitingLag", InspectionState(inspected));
        Assert.Equal(1, inspected.Inspection!.Value.GetProperty("waitingReasonCount").GetInt32());
        Assert.Equal("112", inspected.Inspection.Value.GetProperty("waitingReasons")[0].GetProperty("readyAtTicks").GetString());
        Assert.Equal(ProjectSimulation.Execute(manifest, manifest.Plan.Input.InputDigest).Data, Accepted(debugger.Continue(inspected.Stamp)).Result!.Data);
    }
    [Fact]
    public void TaskEndBreakpointRetainsTheLastCommittedClockAndNamesTheUpcomingEndTime()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest);
        var task = Location(manifest.Plan, 7); Accepted(debugger.SetBreakpoints(debugger.Snapshot.Stamp, [Breakpoint(manifest, task, SimulationBreakpointPoint.TaskEnd)]));
        var stopped = Accepted(debugger.Continue(debugger.Snapshot.Stamp)); Assert.Equal(SimulationControlState.Paused, stopped.State); Assert.Equal(3, stopped.Events.Count);
        Assert.Equal(102, Clock(stopped)); Assert.Equal("105", stopped.Pending!.Value.GetProperty("ticks").GetString()); Assert.Equal("coglatas.task-end", stopped.Pending.Value.GetProperty("kind").GetString());
        var inspected = Accepted(debugger.Inspect(stopped.Stamp, task)); Assert.Equal("Running", InspectionState(inspected)); Assert.Equal("3", inspected.Inspection!.Value.GetProperty("remainingTicks").GetString());
        Assert.Equal(ProjectSimulation.Execute(manifest, manifest.Plan.Input.InputDigest).Data, Accepted(debugger.Continue(inspected.Stamp)).Result!.Data);
    }
    [Fact]
    public void AParallelBreakpointInterruptsTaskStepOverWithoutTurningTheDagIntoACallStack()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest);
        Accepted(debugger.SetBreakpoints(debugger.Snapshot.Stamp, [Breakpoint(manifest, Location(manifest.Plan, 8))]));
        var stopped = Accepted(debugger.StepOver(debugger.Snapshot.Stamp, Location(manifest.Plan, 7)));
        Assert.Single(stopped.Events); Assert.Equal(SimulationControlState.Paused, stopped.State); Assert.Equal("coglatas.debug.breakpoint", stopped.Reason);
        var inspected = Accepted(debugger.Inspect(stopped.Stamp, Location(manifest.Plan, 7))); Assert.Equal("Running", InspectionState(inspected));
        Assert.Equal("5", inspected.Inspection!.Value.GetProperty("remainingTicks").GetString());
        var before = debugger.Snapshot; Assert.Equal("coglatas.debug.composite-not-supported", debugger.StepOut(before.Stamp).Reason); Assert.Same(before, debugger.Snapshot);
        Assert.Equal(CompilerExecutionState.Completed, Accepted(debugger.Continue(before.Stamp)).Result!.Execution);
    }
    [Fact]
    public void InspectsAllWaitingCausesOrAnExplicitBoundedSubsetWithoutEvaluatingOutputs()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest, new(MaximumWaitingReasons: 1));
        var inspected = Accepted(debugger.Inspect(debugger.Snapshot.Stamp, Location(manifest.Plan, 9))); Assert.Equal("WaitingPrerequisites", InspectionState(inspected));
        var data = inspected.Inspection!.Value; Assert.Equal(2, data.GetProperty("waitingReasonCount").GetInt32()); Assert.Equal(1, data.GetProperty("waitingReasons").GetArrayLength());
        Assert.Equal("Partial", data.GetProperty("waitingCoverage").GetString()); Assert.Equal("coglatas.fixed-input-defaults", data.GetProperty("values").GetString());
        Assert.Empty(inspected.Events); Assert.Null(inspected.Result); Assert.Equal(100, Clock(inspected));
    }
    [Theory]
    [InlineData("wrong-plan")] [InlineData("wrong-context")] [InlineData("property-path")] [InlineData("future-point")]
    public void UnresolvedBreakpointsRetainTheirReasonAndCannotMoveToAnotherSemanticPoint(string mode)
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest); var location = Location(manifest.Plan, 7);
        if (mode == "wrong-context") location = ProjectLocation.Create(location.DocumentId, Source(44).Context, entityId: location.EntityId);
        if (mode == "property-path") location = ProjectLocation.Create(location.DocumentId, location.Context, "/payload/duration", entityId: location.EntityId);
        var request = new SimulationBreakpoint(Guid.Parse(Id(981)), mode == "wrong-plan" ? ContentDigest.Compute("other-plan", [1]) : manifest.Plan.Digest, location,
            mode == "future-point" ? (SimulationBreakpointPoint)33 : SimulationBreakpointPoint.TaskStart);
        var admitted = Accepted(debugger.SetBreakpoints(debugger.Snapshot.Stamp, [request])); var resolution = Assert.Single(admitted.Breakpoints);
        Assert.False(resolution.Resolved); Assert.Null(resolution.BoundLocation); Assert.NotEqual("coglatas.debug.resolved", resolution.Reason);
        Assert.Equal(ProjectSimulation.Execute(manifest, manifest.Plan.Input.InputDigest).Data, Accepted(debugger.Continue(admitted.Stamp)).Result!.Data);
    }
    [Fact]
    public void SemanticIdentityRemainsAuthoritativeWhenSupplementaryBufferCoordinatesDiffer()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest); var actual = Location(manifest.Plan, 7);
        var supplementary = ProjectLocation.Create(actual.DocumentId, actual.Context, entityId: actual.EntityId, textSpan: new(1234, 0));
        var admitted = Accepted(debugger.SetBreakpoints(debugger.Snapshot.Stamp, [Breakpoint(manifest, supplementary)]));
        Assert.True(admitted.Breakpoints[0].Resolved); Assert.Equal(actual, admitted.Breakpoints[0].BoundLocation);
        Assert.Empty(Accepted(debugger.Continue(admitted.Stamp)).Events);
    }
    [Fact]
    public void StaleWrongRunAndReenteredCommandsCannotPublishNewerStateUnderAnOlderStamp()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest); var initial = debugger.Snapshot;
        var wrong = debugger.Step(new(ContentDigest.Compute("wrong-run", [1]), 0)); Assert.False(wrong.Accepted); Assert.Null(wrong.Snapshot); Assert.Same(initial, debugger.Snapshot);
        IEnumerable<SimulationBreakpoint> Reenter() { Accepted(debugger.Step(initial.Stamp)); yield return Breakpoint(manifest, Location(manifest.Plan, 7)); }
        var refused = debugger.SetBreakpoints(initial.Stamp, Reenter()); Assert.False(refused.Accepted); Assert.Equal("coglatas.debug.stale-control", refused.Reason); Assert.Null(refused.Snapshot);
        Assert.Single(debugger.Snapshot.Events); Assert.Empty(debugger.Snapshot.Breakpoints); Assert.False(debugger.Continue(initial.Stamp).Accepted);
    }
    [Fact]
    public async Task ConcurrentCommandsAtOneRevisionAdmitExactlyOneTransition()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest); var stamp = debugger.Snapshot.Stamp;
        var responses = await Task.WhenAll(Task.Run(() => debugger.Step(stamp)), Task.Run(() => debugger.Step(stamp)));
        Assert.Single(responses, response => response.Accepted); var stale = Assert.Single(responses, response => !response.Accepted);
        Assert.Equal("coglatas.debug.stale-control", stale.Reason); Assert.Null(stale.Snapshot); Assert.Single(debugger.Snapshot.Events); Assert.Equal(1, debugger.Snapshot.Stamp.Revision);
    }
    [Theory]
    [InlineData("event")] [InlineData("time")] [InlineData("trace")] [InlineData("operation")]
    public void SteppedBoundsRetainExactlyTheSameTerminalPrefixAndResultAsAnUninterruptedRun(string kind)
    {
        var limits = kind switch { "event" => new SimulationRunLimits(MaximumEvents: 2), "time" => new(MaximumElapsedTicks: 2), "trace" => new(MaximumTraceBytes: 2), _ => new(MaximumOperations: 1) };
        var manifest = Manifest(limits: limits); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest);
        while (debugger.Snapshot.Result is null) Accepted(debugger.Step(debugger.Snapshot.Stamp));
        Assert.Equal(ProjectSimulation.Execute(manifest, manifest.Plan.Input.InputDigest).Data, debugger.Snapshot.Result!.Data);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, debugger.Snapshot.Result.Execution); Assert.NotEqual(CompilerVerdict.Pass, debugger.Snapshot.Result.Verdict);
    }
    [Fact]
    public void CancellationAfterPauseTerminatesOnlyThisRunAndPreservesItsCommittedPrefix()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest);
        var before = Accepted(debugger.Step(debugger.Snapshot.Stamp)); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var after = Accepted(debugger.Continue(before.Stamp, cancellation.Token)); Assert.Equal(SimulationControlState.Terminated, after.State);
        Assert.Equal(CompilerExecutionState.Cancelled, after.Result!.Execution); Assert.Single(after.Events); Assert.Equal(before.Events[0], after.Events[0]);
        Assert.Null(before.Result); Assert.Equal(SimulationControlState.Paused, before.State);
        Assert.Equal(CompilerExecutionState.Completed, ProjectSimulation.Execute(Manifest(run: 971), manifest.Plan.Input.InputDigest).Execution);
    }
    [Fact]
    public void ControlAndInspectionLimitsAreExplicitAndDoNotAdvanceOrFalsifyTheRun()
    {
        var manifest = Manifest(); using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest, new(MaximumCommands: 1, MaximumBreakpoints: 1, MaximumInspectionBytes: 256));
        var original = debugger.Snapshot; var one = Breakpoint(manifest, Location(manifest.Plan, 7)); var two = Breakpoint(manifest, Location(manifest.Plan, 8), id: 982);
        Assert.False(debugger.SetBreakpoints(original.Stamp, [one, two]).Accepted); Assert.False(debugger.SetBreakpoints(original.Stamp, [one, one]).Accepted);
        Assert.Equal("coglatas.debug.inspection-bound", debugger.Inspect(original.Stamp, one.Location).Reason); Assert.Same(original, debugger.Snapshot);
        var step = Accepted(debugger.Step(original.Stamp)); Assert.Equal("coglatas.debug.command-bound", debugger.Continue(step.Stamp).Reason); Assert.Same(step, debugger.Snapshot); Assert.Null(step.Result);
        debugger.Dispose(); Assert.Throws<ObjectDisposedException>(() => debugger.Step(step.Stamp));
    }
    [Fact]
    public void IncompleteRuntimePreparationCannotDescribePartiallyIndexedTasksAsReady()
    {
        var plan = ProjectSimulationTests.Plan(); var maximum = 1 + plan.Data.ToCanonicalBytes().Length / 64 + 3;
        var manifest = Manifest(plan, new(MaximumOperations: maximum)); using var debugger = new ProjectSimulationDebugger(manifest, plan.Input.InputDigest);
        var before = debugger.Snapshot; Assert.Equal(CompilerExecutionState.ResourceExhausted, before.Result!.Execution); Assert.Empty(before.Events);
        var inspection = debugger.Inspect(before.Stamp, Location(plan, 7)); Assert.False(inspection.Accepted);
        Assert.Equal("coglatas.debug.preparation-incomplete", inspection.Reason); Assert.Null(inspection.Snapshot); Assert.Same(before, debugger.Snapshot);
    }
    [Fact]
    public void AggregateBreakpointBytesAreCheckedBeforeResolutionOrControlPublication()
    {
        var manifest = Manifest(); var location = Location(manifest.Plan, 7); var one = Breakpoint(manifest, location); var two = Breakpoint(manifest, location, id: 982);
        var bytes = one.Data.ToCanonicalBytes().Length + two.Data.ToCanonicalBytes().Length;
        using var bounded = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest, new(MaximumBreakpointBytes: bytes - 1));
        var before = bounded.Snapshot; Assert.Equal("coglatas.debug.breakpoint-byte-bound", bounded.SetBreakpoints(before.Stamp, [one, two]).Reason); Assert.Same(before, bounded.Snapshot);
        using var exact = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest, new(MaximumBreakpointBytes: bytes));
        Assert.Equal(2, Accepted(exact.SetBreakpoints(exact.Snapshot.Stamp, [two, one])).Breakpoints.Count);
        Assert.Empty(Accepted(exact.Continue(exact.Snapshot.Stamp)).Events);
        Assert.Equal(6, Accepted(exact.Continue(exact.Snapshot.Stamp)).Events.Count);
    }
    [Fact]
    public void InspectionRetainsActualTypedDefaultsAndTheirOriginWithoutReevaluatingOrMutatingThem()
    {
        var fixture = WebSimulationContractTests.Cases().Select(row => (string)row[0]).First(text => text.Contains("tests-simulation-root-base", StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(fixture); var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var plan = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(item, input)).Publication!.ExecutionPlan!;
        var task = plan.Data.Value.GetProperty("tasks").EnumerateArray().First(x => x.GetProperty("ports").GetArrayLength() > 0);
        var location = ProjectLocation.Parse(SourceJson.Parse(task.GetProperty("location").GetRawText())); var manifest = Manifest(plan);
        using var debugger = new ProjectSimulationDebugger(manifest, input.InputDigest);
        var inspected = Accepted(debugger.Inspect(debugger.Snapshot.Stamp, location)); var original = inspected.Inspection!;
        Assert.Equal(SourceJson.Parse(task.GetProperty("ports").GetRawText()), SourceJson.Parse(original.Value.GetProperty("ports").GetRawText()));
        Assert.Equal(location.Data, SourceJson.Parse(original.Value.GetProperty("location").GetRawText())); Assert.Equal("coglatas.fixed-input-defaults", original.Value.GetProperty("values").GetString());
        Accepted(debugger.Step(inspected.Stamp)); Assert.Same(original, inspected.Inspection);
        Assert.Equal(input.Source.Data, plan.Input.Source.Data); Assert.Null(inspected.Result);
    }
    [Fact]
    public void RepeatedStableTaskNodesInDifferentCapturedVersionsKeepDistinctBreakpointSlots()
    {
        var graph = CompilerBindingClosureTests.Graph(withRelation: true, twoBVersions: true); var input = CompilerExecutionPlanTests.Request(graph.Input);
        var plan = PureCoreCompiler.Compile(input, input.Compiler, graph.Snapshots).Publication!.ExecutionPlan!;
        var rows = plan.Data.Value.GetProperty("tasks").EnumerateArray().Where(x => x.GetProperty("sourceEntityId").GetString() == Id(77)).ToArray(); Assert.Equal(2, rows.Length);
        var target = ProjectLocation.Parse(SourceJson.Parse(rows[1].GetProperty("location").GetRawText())); var manifest = Manifest(plan);
        using var debugger = new ProjectSimulationDebugger(manifest, input.InputDigest); Accepted(debugger.SetBreakpoints(debugger.Snapshot.Stamp, [Breakpoint(manifest, target)]));
        var stopped = Accepted(debugger.Continue(debugger.Snapshot.Stamp)); Assert.Equal(target.Data, SourceJson.Parse(stopped.Pending!.Value.GetProperty("location").GetRawText()));
        Assert.Equal(SimulationControlState.Paused, stopped.State); Assert.Equal(ProjectSimulation.Execute(manifest, input.InputDigest).Data, Accepted(debugger.Continue(stopped.Stamp)).Result!.Data);
    }
    [Fact]
    public void PausedAdjustmentCreatesNewScenarioAndDraftPlansAndRunsWithoutHotReloadingOldState()
    {
        var plan = ProjectSimulationTests.Plan(); var manifest = Manifest(plan); using var debugger = new ProjectSimulationDebugger(manifest, plan.Input.InputDigest);
        var first = Location(plan, 7); Accepted(debugger.SetBreakpoints(debugger.Snapshot.Stamp, [Breakpoint(manifest, first)])); var paused = Accepted(debugger.Continue(debugger.Snapshot.Stamp));
        var original = paused.Data; var model = ProjectSemanticBinder.Bind(plan.Input);
        var duration = JsonNode.Parse(plan.Data.Value.GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("sourceEntityId").GetString() == Id(7)).GetProperty("duration").GetRawText())!; duration["value"] = "20";
        var scenario = ProjectScenarios.Prepare(model, plan.Input.InputDigest, ScenarioId.Parse(Id(990)), ScenarioRevisionId.Parse(Id(991)),
            [new(first, ProjectScenarioOverrideKind.TaskDuration, Json(duration))]).Plan!; Assert.NotNull(scenario);
        var scenarioPlan = PureCoreCompiler.Compile(scenario.Input, scenario.Input.Compiler).Publication!.ExecutionPlan!; Assert.NotNull(scenarioPlan);
        var basis = plan.Input.Source.Context.CommittedRevision!;
        var draft = ProjectScenarios.ToDraft(scenario, model, plan.Input.InputDigest, new(ProposalId.Parse(Id(992)), CandidateRevisionId.Parse(Id(993)), basis, basis)).Plan!; Assert.NotNull(draft);
        var draftPlan = PureCoreCompiler.Compile(draft.After, draft.After.Compiler).Publication!.ExecutionPlan!; Assert.NotNull(draftPlan);
        var changed = ProjectSimulation.Execute(Manifest(scenarioPlan, run: 994), scenarioPlan.Input.InputDigest);
        var converted = ProjectSimulation.Execute(Manifest(draftPlan, run: 995), draftPlan.Input.InputDigest);
        Assert.Equal(124, changed.VirtualTicks); Assert.Equal(changed.VirtualTicks, converted.VirtualTicks);
        Assert.NotEqual(plan.Input.InputDigest, changed.Manifest.Plan.Input.InputDigest); Assert.NotEqual(changed.Manifest.Digest, converted.Manifest.Digest);
        using var another = new ProjectSimulationDebugger(changed.Manifest, scenarioPlan.Input.InputDigest); Assert.False(another.Step(paused.Stamp).Accepted);
        Assert.Equal(original, paused.Data); Assert.Empty(paused.Events); Assert.Null(paused.Result);
        var old = Accepted(debugger.Continue(paused.Stamp)).Result!; Assert.Equal(115, old.VirtualTicks); Assert.Equal(plan.Data, old.Manifest.Plan.Data);
    }
}
