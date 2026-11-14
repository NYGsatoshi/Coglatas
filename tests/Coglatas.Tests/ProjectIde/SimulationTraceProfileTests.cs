using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SimulationTraceProfileTests
{
    private static ProjectSimulationRun Run(CompilerExecutionPlan? plan = null, long start = 100, SimulationRunLimits? limits = null, CancellationToken token = default)
    {
        plan ??= ProjectSimulationTests.Plan();
        return ProjectSimulation.Execute(new(plan, Guid.Parse(Id(22000)), start, limits), plan.Input.InputDigest, token);
    }
    private static ProjectSimulationTraceProfile Profile(ProjectSimulationRun run) => ProjectSimulationTraceProfile.Read(run, run.Digest);
    private static string Refused(ProjectSimulationTraceProfile profile)
    {
        Assert.Equal(SimulationTraceProfileState.Refused, profile.State); Assert.Null(profile.Run);
        Assert.Null(profile.RecordedElapsedTicks); Assert.Null(profile.RecordedBusyTicks); Assert.Null(profile.UnattributedIdleTicks);
        Assert.Null(profile.RecordedTaskTicks); Assert.Null(profile.PeakActiveTasks); Assert.Null(profile.OpenTasks);
        foreach (var name in new[] { "basis", "asOf", "recording", "period", "metrics" }) Assert.Equal(JsonValueKind.Null, profile.Data.Value.GetProperty(name).ValueKind);
        return profile.Reason;
    }
    [Fact]
    public void ActualParallelRootsAndJoinLagSeparateUnionTaskTimeAndUnattributedIdle()
    {
        var run = Run(); var p = Profile(run);
        Assert.Equal(SimulationTraceProfileState.Complete, p.State); Assert.Same(run, p.Run);
        Assert.Equal(15, p.RecordedElapsedTicks); Assert.Equal(8, p.RecordedBusyTicks); Assert.Equal(7, p.UnattributedIdleTicks);
        Assert.Equal(new BigInteger(10), p.RecordedTaskTicks); Assert.Equal(2, p.PeakActiveTasks); Assert.Equal(0, p.OpenTasks);
        Assert.Equal("SimulatedModel", p.Data.Value.GetProperty("inputKind").GetString()); Assert.Equal("Unattributed", p.Data.Value.GetProperty("attribution").GetString());
        Assert.Equal("NotRun", p.Data.Value.GetProperty("verdict").GetString()); Assert.Equal("NotEvaluated", p.Data.Value.GetProperty("authority").GetString());
        Assert.Equal("coglatas.unlimited", p.Data.Value.GetProperty("capacityModel").GetString()); Assert.Equal("coglatas.simulation", p.Data.Value.GetProperty("evidenceKind").GetString());
        Assert.Equal("FixedRecording", p.Data.Value.GetProperty("asOf").GetProperty("kind").GetString());
        Assert.Equal(run.Digest.Data, SourceJson.Parse(p.Data.Value.GetProperty("basis").GetProperty("runDigest").GetRawText()));
        Assert.Equal(run.Manifest.Plan.Input.Source.Digest.Data, SourceJson.Parse(p.Data.Value.GetProperty("basis").GetProperty("sourceDigest").GetRawText()));
        Assert.Equal("100", p.Data.Value.GetProperty("period").GetProperty("start").GetString()); Assert.Equal("115", p.Data.Value.GetProperty("period").GetProperty("through").GetString());
        Assert.Equal(ContentDigest.Compute(ProjectSimulationTraceProfile.Format, p.Data.ToCanonicalBytes()), p.Digest);
    }
    [Fact]
    public void OverlappingTasksCountOnceInBusyUnionAndSeparatelyInTaskTime()
    {
        var p = Profile(Run(ProjectSimulationTests.Plan(first: 10, second: 5, last: 0, firstLag: 0, secondLag: 0)));
        Assert.Equal(10, p.RecordedElapsedTicks); Assert.Equal(10, p.RecordedBusyTicks); Assert.Equal(new BigInteger(15), p.RecordedTaskTicks);
        Assert.Equal(0, p.UnattributedIdleTicks); Assert.Equal(2, p.PeakActiveTasks);
        var definitions = p.Data.Value.GetProperty("definitions").EnumerateArray().ToDictionary(row => row.GetProperty("metric").GetString()!, row => row.GetProperty("unit").GetString());
        Assert.Equal("virtual-tick", definitions["recordedBusyTicks"]); Assert.Equal("task-virtual-tick", definitions["recordedTaskTicks"]);
    }
    [Theory]
    [InlineData(1, 0, 0, 1, 1)] [InlineData(2, 0, 0, 2, 2)] [InlineData(3, 2, 4, 2, 1)]
    [InlineData(4, 5, 7, 2, 0)] [InlineData(5, 12, 7, 2, 1)]
    public void ActualBoundedPrefixesEndOpenIntervalsAtTheLastCommittedClock(int cap, long elapsed, long taskTicks, int peak, int open)
    {
        var run = Run(limits: new(MaximumEvents: cap)); var p = Profile(run);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, run.Execution); Assert.Equal(SimulationTraceProfileState.Incomplete, p.State);
        Assert.Equal(elapsed, p.RecordedElapsedTicks); Assert.Equal(new BigInteger(taskTicks), p.RecordedTaskTicks);
        Assert.Equal(peak, p.PeakActiveTasks); Assert.Equal(open, p.OpenTasks); Assert.Equal(cap, run.Events.Count);
        Assert.Equal(elapsed, p.RecordedBusyTicks + p.UnattributedIdleTicks);
    }
    [Fact]
    public void EmptyCancelledRecordingIsExplicitIncompleteModelPrefix()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var run = Run(token: cancellation.Token); var p = Profile(run);
        Assert.Equal(CompilerExecutionState.Cancelled, run.Execution); Assert.Empty(run.Events); Assert.Equal(SimulationTraceProfileState.Incomplete, p.State);
        Assert.Equal(0, p.RecordedElapsedTicks); Assert.Equal(BigInteger.Zero, p.RecordedTaskTicks); Assert.Equal(0, p.PeakActiveTasks);
        Assert.Equal("None", p.Data.Value.GetProperty("recording").GetProperty("coverage").GetString());
    }
    [Fact]
    public void ZeroDurationTasksRetainDeclaredEndBeforeStartTransitionOrdering()
    {
        var p = Profile(Run(ProjectSimulationTests.Plan(first: 0, second: 0, last: 0, firstLag: 0, secondLag: 0)));
        Assert.Equal(SimulationTraceProfileState.Complete, p.State); Assert.Equal(0, p.RecordedElapsedTicks); Assert.Equal(0, p.RecordedBusyTicks);
        Assert.Equal(BigInteger.Zero, p.RecordedTaskTicks); Assert.Equal(1, p.PeakActiveTasks); Assert.Equal(0, p.OpenTasks);
    }
    [Fact]
    public void AggregateTaskTimeMayExceedInt64WithoutLosingExactVirtualTicks()
    {
        var p = Profile(Run(ProjectSimulationTests.Plan(first: long.MaxValue, second: long.MaxValue, last: 0, firstLag: 0, secondLag: 0), start: 0));
        Assert.Equal(SimulationTraceProfileState.Complete, p.State); Assert.Equal(long.MaxValue, p.RecordedBusyTicks); Assert.Equal(long.MaxValue, p.RecordedElapsedTicks);
        Assert.Equal(new BigInteger(long.MaxValue) * 2, p.RecordedTaskTicks); Assert.Equal("18446744073709551614", p.Data.Value.GetProperty("metrics").GetProperty("recordedTaskTicks").GetString());
    }
    [Fact]
    public void AStartOffsetDoesNotBecomeWorkOrIdleTime()
    {
        var plan = ProjectSimulationTests.Plan(); var a = Profile(Run(plan)); var b = Profile(Run(plan, start: 500));
        Assert.Equal(a.RecordedElapsedTicks, b.RecordedElapsedTicks); Assert.Equal(a.RecordedBusyTicks, b.RecordedBusyTicks);
        Assert.Equal(a.RecordedTaskTicks, b.RecordedTaskTicks); Assert.Equal(a.UnattributedIdleTicks, b.UnattributedIdleTicks);
    }
    [Fact]
    public void WrongRecordingDigestReturnsNoRunOrMetricSummary()
    {
        var run = Run(); Assert.Equal("coglatas.profile.wrong-recording", Refused(ProjectSimulationTraceProfile.Read(run, run.Manifest.Digest)));
    }
    [Fact]
    public void StaleUnexecutedInputCannotBecomeAZeroWorkProfile()
    {
        var plan = ProjectSimulationTests.Plan(); var stale = ProjectSimulation.Execute(new(plan, Guid.Parse(Id(22001))), plan.Input.Source.Digest);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness); Assert.Equal("coglatas.profile.recording-unavailable", Refused(Profile(stale)));
    }
    [Fact]
    public void UnsupportedEngineCannotBecomeAZeroWorkProfile()
    {
        var plan = ProjectSimulationTests.Plan(); var engine = JsonNode.Parse(ProjectSimulation.Engine.Data.CanonicalText)!; engine["version"] = "2";
        var run = ProjectSimulation.Execute(new(plan, Guid.Parse(Id(22002)), engine: new(Json(engine))), plan.Input.InputDigest);
        Assert.Equal("coglatas.profile.engine-unsupported", Refused(Profile(run)));
    }
    [Fact]
    public void QueryBudgetIsBoundInTheResultAndNeverSilentlyTruncatesAnExistingRecording()
    {
        var run = Run(); Assert.Equal(6, run.Events.Count);
        Assert.Equal("coglatas.profile.event-bound", Refused(ProjectSimulationTraceProfile.Read(run, run.Digest, 5)));
        var admitted = ProjectSimulationTraceProfile.Read(run, run.Digest, 6); var defaultBudget = Profile(run);
        Assert.Equal(SimulationTraceProfileState.Complete, admitted.State); Assert.Equal(6, admitted.QueryMaximumEvents);
        Assert.Equal(6, admitted.Data.Value.GetProperty("queryBudget").GetProperty("maximumEvents").GetInt32());
        Assert.Equal(admitted.RecordedTaskTicks, defaultBudget.RecordedTaskTicks); Assert.NotEqual(admitted.Digest, defaultBudget.Digest);
    }
    [Theory]
    [InlineData(0)] [InlineData(8193)]
    public void UnsupportedQueryBudgetsAreNotClampedOrTreatedAsComplete(int cap)
    {
        var run = Run(); Assert.Throws<ArgumentOutOfRangeException>(() => ProjectSimulationTraceProfile.Read(run, run.Digest, cap));
    }
    [Fact]
    public void CancellationStopsTheProfileQueryWithoutChangingTheOriginalRun()
    {
        var run = Run(); var original = run.Data; using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSimulationTraceProfile.Read(run, run.Digest, cancellationToken: cancellation.Token));
        Assert.Same(original, run.Data); Assert.Equal(6, run.Events.Count);
    }
    [Theory]
    [InlineData("en-US")] [InlineData("fr-FR")] [InlineData("ar-SA")]
    public void RecordedProfileBytesAndExactMetricStringsAreCultureIndependent(string culture)
    {
        var run = Run(); var expected = Profile(run); var original = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); var actual = Profile(run); Assert.Equal(expected.Data, actual.Data); Assert.Equal(expected.Digest, actual.Digest); }
        finally { CultureInfo.CurrentCulture = original; }
    }
    [Fact]
    public void RepeatedReadKeepsOriginalRecordsAndOwnedCanonicalBytesImmutable()
    {
        var run = Run(); var data = run.Data; var first = Profile(run); var bytes = first.Data.ToCanonicalBytes(); bytes[0] = 0;
        var second = Profile(run); Assert.Equal(first.Data, second.Data); Assert.Equal(first.Digest, second.Digest); Assert.Same(data, run.Data); Assert.Equal(6, run.Events.Count);
    }
}
