using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
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

    [Fact]
    public async Task CurrentProfileOpensOnlyItsOwnedRecordingWithoutChangingCompilerOrRun()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var result = view.Result;
        var plan = view.ScopedExecutionPlan;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        var notifications = 0;
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => notifications++;
        view.PropertyChanged += Changed;
        try
        {
            Assert.Same(fixture.Run, view.ReadSimulationTraceProfileRun(retained.Profile));
            Assert.Same(retained.Profile, view.SimulationTraceProfile);
            Assert.Same(fixture.Run, view.SimulationRun);
            Assert.Same(result, view.Result);
            Assert.Same(plan, view.ScopedExecutionPlan);
            Assert.Equal(0, notifications);
            retained.AssertUnchanged();
        }
        finally { view.PropertyChanged -= Changed; }
    }

    [Fact]
    public async Task EqualContentDetachedProfileCannotOpenTheCurrentRecording()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        var detached = ProjectSimulationTraceProfile.Read(fixture.Run, fixture.Run.Digest);
        Assert.Equal(retained.Profile.Data, detached.Data);
        Assert.Equal(retained.Profile.Digest, detached.Digest);
        Assert.NotSame(retained.Profile, detached);
        Assert.Same(fixture.Run, detached.Run);
        Assert.Null(view.ReadSimulationTraceProfileRun(detached));
        Assert.Same(retained.Profile, view.SimulationTraceProfile);
        Assert.Same(fixture.Run, view.ReadSimulationTraceProfileRun(retained.Profile));
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task SupersededEqualContentProfileCannotOpenTheNewerQueryRecording()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        var newer = ReadCurrentProfile(view);
        Assert.Equal(retained.Profile.Data, newer.Data);
        Assert.Equal(retained.Profile.Digest, newer.Digest);
        Assert.NotSame(retained.Profile, newer);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.Same(fixture.Run, view.ReadSimulationTraceProfileRun(newer));
        Assert.Same(newer, view.SimulationTraceProfile);
        Assert.Same(fixture.Run, view.SimulationRun);
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task RefusedProfileCannotExposeOrOpenTheEarlierRecording()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        var refused = ReadRefusedProfile(view, fixture.Run);
        Assert.NotSame(retained.Profile, refused);
        Assert.Null(view.ReadSimulationTraceProfileRun(refused));
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.Same(fixture.Run, view.SimulationRun);
        Assert.Same(fixture.Compilation.Result, view.Result);
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task ResetWithdrawsProfileNavigationUntilFreshGenerationAndRunAdmission()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        view.Reset();
        Assert.Null(view.Result);
        Assert.Null(view.SimulationRun);
        Assert.Null(view.SimulationTraceProfile);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.False(view.ProfileSyntheticSimulation(fixture.Run.Digest));
        Assert.Throws<ArgumentException>(() => view.BeginSynthetic(fixture.Request));

        var request = new CompilerHostRequest(fixture.Request.Input, 2);
        var response = await CompileTraceRequest(request);
        view.BeginSynthetic(request);
        Assert.False(view.TryAccept(fixture.Response));
        Assert.True(view.TryAccept(response));
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.False(view.ProfileSyntheticSimulation(fixture.Run.Digest));
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(TraceId(902)), 100));
        var current = ReadCurrentProfile(view);
        Assert.NotSame(fixture.Run, current.Run);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.Same(view.SimulationRun, view.ReadSimulationTraceProfileRun(current));
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task DisposalWithdrawsProfileAndRejectsFurtherRecordingReads()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        view.Dispose();
        Assert.Null(view.Result);
        Assert.Null(view.SimulationRun);
        Assert.Null(view.SimulationTraceProfile);
        Assert.Throws<ObjectDisposedException>(() => view.ProfileSyntheticSimulation(fixture.Run.Digest));
        Assert.Throws<ObjectDisposedException>(() => view.ReadSimulationTraceProfileRun(retained.Profile));
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task NewInputRejectsTheEarlierResponseAndWithdrawsPriorProfileNavigation()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        var request = new CompilerHostRequest(new CompilerInputTransfer(TraceInput(102)), 2);
        Assert.NotEqual(fixture.Request.Input.ExpectedInputDigest, request.Input.ExpectedInputDigest);
        var response = await CompileTraceRequest(request);
        var compilation = Assert.IsType<CoreCompilation>(response.Compilation);
        view.BeginSynthetic(request);
        Assert.False(view.TryAccept(fixture.Response));
        Assert.Null(view.SimulationRun);
        Assert.Null(view.SimulationTraceProfile);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.True(view.TryAccept(response));
        Assert.Same(compilation.Result, view.Result);
        Assert.False(view.ProfileSyntheticSimulation(fixture.Run.Digest));
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(TraceId(903)), 100));
        var current = ReadCurrentProfile(view);
        var run = Assert.IsType<ProjectSimulationRun>(current.Run);
        Assert.NotEqual(fixture.Run.Manifest.Plan.Input.InputDigest, run.Manifest.Plan.Input.InputDigest);
        Assert.Same(compilation.Publication!.ExecutionPlan, run.Manifest.Plan);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.Same(run, view.ReadSimulationTraceProfileRun(current));
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task ScopedReadmissionRequiresANewRunAndExplicitProfile()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var result = view.Result;
        var plan = view.ScopedExecutionPlan;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.Null(view.SimulationRun);
        Assert.Null(view.SimulationTraceProfile);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.False(view.ProfileSyntheticSimulation(fixture.Run.Digest));
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(TraceId(904)), 100));
        Assert.Null(view.SimulationTraceProfile);
        var current = ReadCurrentProfile(view);
        Assert.NotSame(fixture.Run, current.Run);
        Assert.Same(result, view.Result);
        Assert.Same(plan, view.ScopedExecutionPlan);
        Assert.Same(view.SimulationRun, view.ReadSimulationTraceProfileRun(current));
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task NewRunWithTheSamePlanCannotReuseThePreviousProfile()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(TraceId(905)), 100));
        var newerRun = Assert.IsType<ProjectSimulationRun>(view.SimulationRun);
        Assert.NotSame(fixture.Run, newerRun);
        Assert.NotEqual(fixture.Run.Digest, newerRun.Digest);
        Assert.Same(fixture.Run.Manifest.Plan, newerRun.Manifest.Plan);
        Assert.Null(view.SimulationTraceProfile);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        var current = ReadCurrentProfile(view);
        Assert.Same(newerRun, view.ReadSimulationTraceProfileRun(current));
        Assert.Same(fixture.Compilation.Result, view.Result);
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task DebugReplacementRequiresItsOwnTerminalRecordingAndExplicitProfile()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(TraceId(906)), 100));
        var prepared = Assert.IsType<SimulationDebugSnapshot>(view.SimulationDebug);
        Assert.Null(prepared.Result);
        Assert.Null(view.SimulationRun);
        Assert.Null(view.SimulationTraceProfile);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.False(view.ProfileSyntheticSimulation(fixture.Run.Digest));
        Assert.True(view.ContinueSyntheticSimulation(prepared));
        var terminal = Assert.IsType<ProjectSimulationRun>(view.SimulationRun);
        Assert.Equal(CompilerExecutionState.Completed, terminal.Execution);
        Assert.Equal(2, terminal.Events.Count);
        Assert.Null(prepared.Result);
        Assert.Empty(prepared.Events);
        Assert.Same(terminal, view.SimulationDebug!.Result);
        Assert.Null(view.SimulationTraceProfile);
        var current = ReadCurrentProfile(view);
        Assert.NotSame(fixture.Run, terminal);
        Assert.Same(terminal, view.ReadSimulationTraceProfileRun(current));
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.Same(fixture.Compilation.Result, view.Result);
        retained.AssertUnchanged();
    }

    [Fact]
    public Task InitialNotificationKeepsTheNewerAdmittedProfile()
        => VerifyProfileReentry(afterPublication: false, admitted: true, cancel: false);

    [Fact]
    public Task PublishedNotificationKeepsTheNewerAdmittedProfile()
        => VerifyProfileReentry(afterPublication: true, admitted: true, cancel: false);

    [Fact]
    public Task InitialNotificationKeepsTheNewerRefusedProfile()
        => VerifyProfileReentry(afterPublication: false, admitted: false, cancel: false);

    [Fact]
    public Task PublishedNotificationKeepsTheNewerRefusedProfile()
        => VerifyProfileReentry(afterPublication: true, admitted: false, cancel: false);

    [Fact]
    public Task InitialCancellationKeepsTheNewerAdmittedProfile()
        => VerifyProfileReentry(afterPublication: false, admitted: true, cancel: true);

    [Fact]
    public Task PublishedCancellationKeepsTheNewerAdmittedProfile()
        => VerifyProfileReentry(afterPublication: true, admitted: true, cancel: true);

    [Fact]
    public Task InitialCancellationKeepsTheNewerRefusedProfile()
        => VerifyProfileReentry(afterPublication: false, admitted: false, cancel: true);

    [Fact]
    public Task PublishedCancellationKeepsTheNewerRefusedProfile()
        => VerifyProfileReentry(afterPublication: true, admitted: false, cancel: true);

    [Fact]
    public async Task PreCancelledProfileQueryWithdrawsPriorSummaryAndPreservesRecording()
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.ProfileSyntheticSimulation(fixture.Run.Digest, token: cancellation.Token));
        Assert.Null(view.SimulationTraceProfile);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        Assert.Same(fixture.Run, view.SimulationRun);
        Assert.Same(fixture.Compilation.Result, view.Result);
        Assert.Same(fixture.Run.Manifest.Plan, view.ScopedExecutionPlan);
        retained.AssertUnchanged();
    }

    [Fact]
    public async Task CancellationWithoutReplacementWithdrawsOnlyTheInterruptedProfile()
    {
        await VerifyProfileReentry(afterPublication: false, admitted: null, cancel: true);
        await VerifyProfileReentry(afterPublication: true, admitted: null, cancel: true);
    }

    private static async Task VerifyProfileReentry(bool afterPublication, bool? admitted, bool cancel)
    {
        using var fixture = await TraceFixture.Create();
        var view = fixture.View;
        var retained = new RetainedTrace(ReadCurrentProfile(view), fixture.Compilation.Result);
        ProjectSimulationTraceProfile? newer = null;
        using var cancellation = new CancellationTokenSource();
        using var notification = new ProfileNotification(view, afterPublication, () =>
        {
            if (admitted is true) newer = ReadCurrentProfile(view);
            else if (admitted is false) newer = ReadRefusedProfile(view, fixture.Run);
            if (cancel) cancellation.Cancel();
        });
        if (cancel)
            Assert.Throws<OperationCanceledException>(() => view.ProfileSyntheticSimulation(fixture.Run.Digest, token: cancellation.Token));
        else
            Assert.False(view.ProfileSyntheticSimulation(fixture.Run.Digest));
        Assert.True(notification.Fired);
        Assert.Equal(cancel, cancellation.IsCancellationRequested);
        Assert.Null(view.ReadSimulationTraceProfileRun(retained.Profile));
        if (admitted is null) Assert.Null(view.SimulationTraceProfile);
        else
        {
            Assert.NotNull(newer);
            Assert.NotSame(retained.Profile, newer);
            Assert.Same(newer, view.SimulationTraceProfile);
            if (admitted.Value)
            {
                Assert.Equal(retained.Profile.Data, newer.Data);
                Assert.Same(fixture.Run, view.ReadSimulationTraceProfileRun(newer));
            }
            else
            {
                AssertNoSummary(newer, "coglatas.profile.wrong-recording");
                Assert.Null(view.ReadSimulationTraceProfileRun(newer));
            }
        }
        Assert.Same(fixture.Run, view.SimulationRun);
        Assert.Same(fixture.Compilation.Result, view.Result);
        Assert.Same(fixture.Run.Manifest.Plan, view.ScopedExecutionPlan);
        retained.AssertUnchanged();
    }

    private static ProjectSimulationTraceProfile ReadCurrentProfile(SyntheticWorkbenchViewModel view)
    {
        var run = Assert.IsType<ProjectSimulationRun>(view.SimulationRun);
        Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        var profile = Assert.IsType<ProjectSimulationTraceProfile>(view.SimulationTraceProfile);
        Assert.Equal(SimulationTraceProfileState.Complete, profile.State);
        Assert.Same(run, profile.Run);
        Assert.Equal(11L, profile.RecordedElapsedTicks);
        Assert.Equal(11L, profile.RecordedBusyTicks);
        Assert.Equal(new BigInteger(11), profile.RecordedTaskTicks);
        Assert.Equal(0, profile.OpenTasks);
        return profile;
    }

    private static ProjectSimulationTraceProfile ReadRefusedProfile(SyntheticWorkbenchViewModel view, ProjectSimulationRun run)
    {
        Assert.False(view.ProfileSyntheticSimulation(run.Manifest.Digest));
        var profile = Assert.IsType<ProjectSimulationTraceProfile>(view.SimulationTraceProfile);
        AssertNoSummary(profile, "coglatas.profile.wrong-recording");
        return profile;
    }

    private sealed class RetainedTrace
    {
        public ProjectSimulationTraceProfile Profile { get; }
        private readonly ProjectSimulationRun _run;
        private readonly ContentDigest _profileDigest;
        private readonly ContentDigest _runDigest;
        private readonly (SourceJson Value, byte[] Bytes)[] _originals;
        public RetainedTrace(ProjectSimulationTraceProfile profile, CompilerResult result)
        {
            Profile = profile; _run = Assert.IsType<ProjectSimulationRun>(profile.Run);
            _profileDigest = profile.Digest; _runDigest = _run.Digest;
            _originals = new[] { profile.Data, _run.Data, _run.Manifest.Data, _run.Manifest.Plan.Data,
                _run.Manifest.Plan.Input.Data, _run.Manifest.Plan.Input.Source.Data, result.Data }
                .Select(value => (Value: value, Bytes: value.ToCanonicalBytes())).ToArray();
        }
        public void AssertUnchanged()
        {
            Assert.Same(_run, Profile.Run);
            Assert.Equal(_profileDigest, Profile.Digest);
            Assert.Equal(_runDigest, _run.Digest);
            foreach (var original in _originals) Assert.Equal(original.Bytes, original.Value.ToCanonicalBytes());
        }
    }

    private sealed class ProfileNotification : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly bool _afterPublication;
        private readonly Action _action;
        public bool Fired { get; private set; }
        public ProfileNotification(SyntheticWorkbenchViewModel view, bool afterPublication, Action action)
        {
            _view = view; _afterPublication = afterPublication; _action = action;
            view.PropertyChanged += Changed;
        }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || (_view.SimulationTraceProfile is not null) != _afterPublication) return;
            Assert.Same(_view, sender);
            Fired = true;
            _action();
        }
        public void Dispose() => _view.PropertyChanged -= Changed;
    }

    private sealed class TraceFixture(SyntheticWorkbenchViewModel view, CompilerHostRequest request,
        CompilerHostResponse response, CoreCompilation compilation, ProjectSimulationRun run) : IDisposable
    {
        public SyntheticWorkbenchViewModel View { get; } = view;
        public CompilerHostRequest Request { get; } = request;
        public CompilerHostResponse Response { get; } = response;
        public CoreCompilation Compilation { get; } = compilation;
        public ProjectSimulationRun Run { get; } = run;
        public static async Task<TraceFixture> Create()
        {
            var request = new CompilerHostRequest(new CompilerInputTransfer(TraceInput(101)), 1);
            var response = await CompileTraceRequest(request);
            var compilation = Assert.IsType<CoreCompilation>(response.Compilation);
            var view = new SyntheticWorkbenchViewModel();
            view.BeginSynthetic(request);
            Assert.True(view.TryAccept(response));
            Assert.Same(compilation.Result, view.Result);
            Assert.True(view.LoadSyntheticScopedSources());
            Assert.Same(compilation.Publication!.ExecutionPlan, view.ScopedExecutionPlan);
            Assert.True(view.RunSyntheticSimulation(Guid.Parse(TraceId(901)), 100));
            var run = Assert.IsType<ProjectSimulationRun>(view.SimulationRun);
            Assert.Equal(CompilerExecutionState.Completed, run.Execution);
            Assert.Equal(CompilerFreshness.Current, run.Freshness);
            Assert.Equal(2, run.Events.Count);
            Assert.Same(view.ScopedExecutionPlan, run.Manifest.Plan);
            Assert.Equal(request.Input.ExpectedInputDigest, run.Manifest.Plan.Input.InputDigest);
            return new(view, request, response, compilation, run);
        }
        public void Dispose() => View.Dispose();
    }

    private static async Task<CompilerHostResponse> CompileTraceRequest(CompilerHostRequest request)
    {
        var input = request.Input.Materialize();
        CompilerArtifactIdentity Contract(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Contract("schema"), Contract("ruleset"),
            Contract("semantics"), Contract("ir"), Contract("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()),
            CompilerExecutionPlan.Engine);
        var response = await new LocalNetCompilerHost(profile).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, response.Status);
        Assert.NotNull(response.Execution);
        var compilation = Assert.IsType<CoreCompilation>(response.Compilation);
        Assert.Equal(CompilerExecutionState.Completed, compilation.Result.Execution);
        Assert.NotNull(compilation.Publication);
        Assert.True(compilation.Publication!.IsDeclaredBy(compilation.Result));
        Assert.NotNull(compilation.Publication.ExecutionPlan);
        return response;
    }

    private static SourceJson TraceJson(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static string TraceId(int value) => new Guid(value, 0, 0x4000, 0x90, 0, 0, 0, 0, 0, 0, 7).ToString();

    private static CompilerInputManifest TraceInput(int revision)
    {
        // Independent public-API fixture: one real Task, without Product test setup or authored run/profile objects.
        var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(TraceId(1)),
            ProjectId.Parse(TraceId(2)), BranchId.Parse(TraceId(3))), RevisionId.Parse(TraceId(revision))));
        var document = SourceDocument.Create(DocumentId.Parse(TraceId(20)), "security-trace-profile-lifecycle",
            TraceJson(new { entityId = TraceId(40), kind = "coglatas.project", schemaVersion = 1, payload = new { } }));
        var declarations = TraceJson(new[] { new { entityId = TraceId(30), documentId = TraceId(20),
            kind = "coglatas.task", schemaVersion = 1, payload = new {
                duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                    state = "coglatas.known", value = "11" }, ports = Array.Empty<object>() } } });
        var source = ProjectSource.Create(context, [document], declarations, TraceJson(Array.Empty<object>()));
        object Artifact(string id, string version = "1") => new { artifactId = id, version,
            digest = new { algorithm = "sha-256", value = new string('d', 64) } };
        var settings = TraceJson(new {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.compiler"),
            ruleset = Artifact("coglatas.ruleset"), semantics = Artifact("coglatas.semantic-model", "2"),
            ir = Artifact("coglatas.project-ir", "2"), evaluator = Artifact("coglatas.evaluator"),
            units = Array.Empty<object>(),
            scheduler = new { kind = "coglatas.plan-lowering", artifact = CompilerExecutionPlan.Engine.Data.Value },
            solver = new { kind = "coglatas.none" }, stochastic = new { kind = "coglatas.none" },
            importer = new { kind = "coglatas.none" }, scenario = new { kind = "coglatas.none" },
            authorizedExternalSnapshots = Array.Empty<object>(), resourceSnapshots = Array.Empty<object>(),
            allocationSnapshots = Array.Empty<object>(), observations = Array.Empty<object>(), evidence = Array.Empty<object>(),
            calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            descriptors = RelationKindDescriptor.BuiltIns.Select(item => new { artifactId = item.DescriptorId, version = "1", digest = item.Digest.Data.Value }).ToArray(),
            rules = new[] { "coglatas.core.schema", "coglatas.core.declarations", "coglatas.core.relations" }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities)
                .Prepend("coglatas.core.structural").Distinct(StringComparer.Ordinal).ToArray(),
            targets = new[] { "coglatas.semantic-model", CompilerExecutionPlan.Target },
            analysisScope = new[] { "coglatas.source-structure" }, sourceCoverage = "coglatas.complete",
            clock = new { kind = "coglatas.not-applicable" },
            executionProfile = new { purpose = "coglatas.normal", maximumOperations = 200000, maximumDeclarations = 10000,
                maximumRelations = 20000, maximumDigits = 256, maximumScale = 256, rounding = "coglatas.ties-to-even" }
        });
        return new(source, settings);
    }
}
