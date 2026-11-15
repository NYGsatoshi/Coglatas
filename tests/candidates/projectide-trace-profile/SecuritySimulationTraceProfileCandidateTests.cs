using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>
/// Independent Security acceptance candidates for #1962. Fixed local SimulatedModel only;
/// no actor authority, operational observations, Trust/Eligibility or Release permission.
/// </summary>
public sealed class SecuritySimulationTraceProfileCandidateTests
{
    private static ProjectSimulationRun Record(
        CompilerExecutionPlan? plan = null, long start = 100,
        SimulationRunLimits? limits = null, CancellationToken token = default)
    {
        plan ??= ProjectSimulationTests.Plan();
        return ProjectSimulation.Execute(
            new(plan, Guid.Parse(Id(23100)), start, limits), plan.Input.InputDigest, token);
    }

    private static ProjectSimulationTraceProfile Read(ProjectSimulationRun run)
        => ProjectSimulationTraceProfile.Read(run, run.Digest);

    private static void AssertNoSummary(ProjectSimulationTraceProfile result, string reason)
    {
        Assert.Equal(SimulationTraceProfileState.Refused, result.State);
        Assert.Equal(reason, result.Reason);
        Assert.Null(result.Run);
        Assert.Null(result.RecordedElapsedTicks);
        Assert.Null(result.RecordedBusyTicks);
        Assert.Null(result.RecordedTaskTicks);
        Assert.Null(result.UnattributedIdleTicks);
        Assert.Null(result.PeakActiveTasks);
        Assert.Null(result.OpenTasks);
        foreach (var field in new[] { "basis", "asOf", "recording", "period", "metrics" })
            Assert.Equal(JsonValueKind.Null, result.Data.Value.GetProperty(field).ValueKind);
    }

    [Fact]
    public void ActualFixedRecordingSeparatesElapsedUnionTaskTimeAndUnattributedIdle()
    {
        var original = Record();
        var profile = Read(original);
        Assert.Equal(SimulationTraceProfileState.Complete, profile.State);
        Assert.Same(original, profile.Run);
        Assert.Equal(15L, profile.RecordedElapsedTicks);
        Assert.Equal(8L, profile.RecordedBusyTicks);
        Assert.Equal(BigInteger.Parse("10", CultureInfo.InvariantCulture), profile.RecordedTaskTicks);
        Assert.Equal(7L, profile.UnattributedIdleTicks);
        Assert.Equal(2, profile.PeakActiveTasks);
        Assert.Equal(0, profile.OpenTasks);
        Assert.Equal(15, profile.RecordedBusyTicks + profile.UnattributedIdleTicks);
        Assert.Equal(ContentDigest.Compute(ProjectSimulationTraceProfile.Format, profile.Data.ToCanonicalBytes()), profile.Digest);
    }

    [Fact]
    public void OverlapCountsOnceInBusyUnionButTwiceInTaskIntegral()
    {
        var plan = ProjectSimulationTests.Plan(first: 10, second: 5, last: 0, firstLag: 0, secondLag: 0);
        var profile = Read(Record(plan, start: 0));
        Assert.Equal(SimulationTraceProfileState.Complete, profile.State);
        Assert.Equal(10L, profile.RecordedElapsedTicks);
        Assert.Equal(10L, profile.RecordedBusyTicks);
        Assert.Equal(new BigInteger(15), profile.RecordedTaskTicks);
        Assert.Equal(0L, profile.UnattributedIdleTicks);
        Assert.Equal(2, profile.PeakActiveTasks);
        Assert.Equal("task-virtual-tick", profile.Data.Value.GetProperty("definitions").EnumerateArray()
            .Single(row => row.GetProperty("metric").GetString() == "recordedTaskTicks")
            .GetProperty("unit").GetString());
    }

    [Fact]
    public void ChangingVirtualStartDoesNotInventPersonWorkOrIdleAttribution()
    {
        var plan = ProjectSimulationTests.Plan();
        var first = Read(Record(plan, start: 100));
        var second = Read(Record(plan, start: 500));
        Assert.Equal(first.RecordedElapsedTicks, second.RecordedElapsedTicks);
        Assert.Equal(first.RecordedBusyTicks, second.RecordedBusyTicks);
        Assert.Equal(first.RecordedTaskTicks, second.RecordedTaskTicks);
        Assert.Equal(first.UnattributedIdleTicks, second.UnattributedIdleTicks);
        Assert.Equal("100", first.Data.Value.GetProperty("period").GetProperty("start").GetString());
        Assert.Equal("500", second.Data.Value.GetProperty("period").GetProperty("start").GetString());
        Assert.NotEqual(first.Digest, second.Digest);
    }

    [Fact]
    public void ZeroDurationTransitionsKeepExplicitOrderWithoutFakeUtilization()
    {
        var plan = ProjectSimulationTests.Plan(first: 0, second: 0, last: 0, firstLag: 0, secondLag: 0);
        var p = Read(Record(plan, start: 0));
        Assert.Equal(SimulationTraceProfileState.Complete, p.State);
        Assert.Equal(0L, p.RecordedElapsedTicks);
        Assert.Equal(0L, p.RecordedBusyTicks);
        Assert.Equal(BigInteger.Zero, p.RecordedTaskTicks);
        Assert.Equal(1, p.PeakActiveTasks);
        Assert.Equal(0, p.OpenTasks);
    }

    [Fact]
    public void HugeParallelTaskIntegralIsExactBeyondInt64()
    {
        var plan = ProjectSimulationTests.Plan(first: long.MaxValue, second: long.MaxValue,
            last: 0, firstLag: 0, secondLag: 0);
        var p = Read(Record(plan, start: 0));
        Assert.Equal(SimulationTraceProfileState.Complete, p.State);
        Assert.Equal(long.MaxValue, p.RecordedElapsedTicks);
        Assert.Equal(long.MaxValue, p.RecordedBusyTicks);
        Assert.Equal(new BigInteger(long.MaxValue) * 2, p.RecordedTaskTicks);
        Assert.Equal("18446744073709551614",
            p.Data.Value.GetProperty("metrics").GetProperty("recordedTaskTicks").GetString());
    }

    [Fact]
    public void BoundedRecordingCountsOnlyItsCommittedPrefixAndOpenTasks()
    {
        var partial = Record(limits: new SimulationRunLimits(MaximumEvents: 2));
        var profile = Read(partial);
        Assert.Equal(2, partial.Events.Count);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, partial.Execution);
        Assert.Equal(SimulationTraceProfileState.Incomplete, profile.State);
        Assert.Equal(CompilerVerdict.NotRun.ToString(),
            profile.Data.Value.GetProperty("verdict").GetString());
        Assert.Equal(0L, profile.RecordedElapsedTicks);
        Assert.Equal(0L, profile.RecordedBusyTicks);
        Assert.Equal(BigInteger.Zero, profile.RecordedTaskTicks);
        Assert.Equal(2, profile.OpenTasks);
        Assert.Equal(2, profile.PeakActiveTasks);
        Assert.Equal("coglatas.profile.recorded-prefix", profile.Reason);
    }

    [Fact]
    public void AShorterQueryBudgetRefusesInsteadOfCallingItsPrefixComplete()
    {
        var full = Record();
        Assert.Equal(6, full.Events.Count);
        AssertNoSummary(ProjectSimulationTraceProfile.Read(full, full.Digest, maximumEvents: 5),
            "coglatas.profile.event-bound");
        var admitted = ProjectSimulationTraceProfile.Read(full, full.Digest, maximumEvents: 6);
        Assert.Equal(SimulationTraceProfileState.Complete, admitted.State);
        Assert.Equal(6, admitted.QueryMaximumEvents);
        Assert.NotEqual(admitted.Digest, Read(full).Digest);
    }

    [Fact]
    public void WrongRecordingOrUnadmittedEngineCannotDiscloseOriginalMetrics()
    {
        var run = Record();
        AssertNoSummary(ProjectSimulationTraceProfile.Read(run, run.Manifest.Digest),
            "coglatas.profile.wrong-recording");

        var plan = ProjectSimulationTests.Plan();
        var other = JsonNode.Parse(ProjectSimulation.Engine.Data.CanonicalText)!;
        other["version"] = "2";
        var foreign = ProjectSimulation.Execute(
            new(plan, Guid.Parse(Id(23101)), engine: new(Json(other))), plan.Input.InputDigest);
        AssertNoSummary(Read(foreign), "coglatas.profile.engine-unsupported");
    }

    [Fact]
    public void StaleRecordingAndCancelledProfileQueriesDoNotProduceOldMetricSummary()
    {
        var plan = ProjectSimulationTests.Plan();
        var stale = ProjectSimulation.Execute(new(plan, Guid.Parse(Id(23102))),
            plan.Input.Source.Digest);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness);
        AssertNoSummary(Read(stale), "coglatas.profile.recording-unavailable");

        var good = Record(plan);
        var bytes = good.Data.ToCanonicalBytes();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ProjectSimulationTraceProfile.Read(good, good.Digest, cancellationToken: cancellation.Token));
        Assert.Equal(bytes, good.Data.ToCanonicalBytes());
    }

    [Fact]
    public void MetadataNeverClaimsOperationalObservationsAuthorityOrOutcomePass()
    {
        var recording = Record();
        var p = Read(recording);
        var json = p.Data.Value;
        Assert.Equal("SimulatedModel", json.GetProperty("inputKind").GetString());
        Assert.Equal("NotRun", json.GetProperty("verdict").GetString());
        Assert.Equal("NotEvaluated", json.GetProperty("authority").GetString());
        Assert.Equal("Unattributed", json.GetProperty("attribution").GetString());
        Assert.Equal("coglatas.unlimited", json.GetProperty("capacityModel").GetString());
        Assert.Equal("coglatas.simulation", json.GetProperty("evidenceKind").GetString());
        Assert.Equal("FixedRecording", json.GetProperty("asOf").GetProperty("kind").GetString());
        Assert.Equal(recording.Digest.Data.Value.GetRawText(),
            json.GetProperty("basis").GetProperty("runDigest").GetRawText());
        Assert.Equal(recording.Manifest.Plan.Input.Source.Context.Data.Value.GetRawText(),
            json.GetProperty("basis").GetProperty("context").GetRawText());
    }

    [Fact]
    public void ReturnedCanonicalByteBuffersAreIndependentAndCultureInvariant()
    {
        var run = Record();
        var baseline = Read(run);
        var initial = baseline.Data.ToCanonicalBytes();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var replacement = Read(run);
            Assert.Equal(baseline.Digest, replacement.Digest);
            Assert.Equal(initial, replacement.Data.ToCanonicalBytes());
            var mutable = replacement.Data.ToCanonicalBytes();
            mutable[0] = (byte)'X';
            Assert.Equal(initial, baseline.Data.ToCanonicalBytes());
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public async Task WindowsPureOwnerCannotReuseAnOldRecordingAfterReadmission()
    {
        var fixture = await SyntheticSimulationTests.Ready();
        using var view = fixture.View;
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(23103)), 100));
        var run = view.SimulationRun!;
        var compiler = view.Result;
        Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        var p = view.SimulationTraceProfile!;
        Assert.Equal(SimulationTraceProfileState.Complete, p.State);
        Assert.Same(run, p.Run);
        Assert.False(view.ProfileSyntheticSimulation(run.Manifest.Digest));
        Assert.Null(view.SimulationTraceProfile!.Run);
        Assert.Null(view.SimulationTraceProfile.RecordedBusyTicks);
        Assert.Same(compiler, view.Result);
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.Null(view.SimulationRun);
        Assert.Null(view.SimulationTraceProfile);
        Assert.False(view.ProfileSyntheticSimulation(run.Digest));
    }
}
