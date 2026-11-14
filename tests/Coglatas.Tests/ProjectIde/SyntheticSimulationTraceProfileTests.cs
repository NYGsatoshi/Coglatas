using System.Numerics;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticSimulationTraceProfileTests
{
    private static async Task<(SyntheticWorkbenchViewModel View, CompilerHostRequest Request)> Ready()
    {
        var f = await SyntheticSimulationTests.Ready(); Assert.True(f.View.LoadSyntheticScopedSources());
        Assert.True(f.View.RunSyntheticSimulation(Guid.Parse(Id(22100)), 100)); return f;
    }
    [Fact]
    public async Task ExplicitReadUsesTheOwnedRecordingWithoutStartingExecutionOrReplacingAnyInputs()
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; var compiler = view.Result; var plan = view.ScopedExecutionPlan;
        Assert.True(view.CompareSyntheticSimulationRuns(run, run.Digest)); var comparison = view.SimulationComparison;
        Assert.True(view.ProfileSyntheticSimulation(run.Digest)); var profile = view.SimulationTraceProfile!;
        Assert.Equal(SimulationTraceProfileState.Complete, profile.State); Assert.Same(run, profile.Run);
        Assert.Equal(15, profile.RecordedElapsedTicks); Assert.Equal(8, profile.RecordedBusyTicks); Assert.Equal(new BigInteger(10), profile.RecordedTaskTicks);
        Assert.Equal(ProjectSimulationTraceProfile.Read(run, run.Digest).Data, profile.Data);
        Assert.Same(run, view.SimulationRun); Assert.Same(compiler, view.Result); Assert.Same(plan, view.ScopedExecutionPlan); Assert.Same(comparison, view.SimulationComparison);
        Assert.Equal(CompilerVerdict.NotRun, run.Verdict);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PartialAndCancelledRecordingsRemainIncompleteAtTheirOwnBoundary(bool cancel)
    {
        var f = await Ready(); using var view = f.View; using var cancellation = new CancellationTokenSource(); if (cancel) cancellation.Cancel();
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(22101)), limits: new(MaximumEvents: 1), cancellationToken: cancellation.Token));
        var run = view.SimulationRun!; Assert.True(view.ProfileSyntheticSimulation(run.Digest)); var profile = view.SimulationTraceProfile!;
        Assert.Equal(SimulationTraceProfileState.Incomplete, profile.State); Assert.Same(run, profile.Run); Assert.Equal(0, profile.RecordedElapsedTicks);
        Assert.Equal(cancel ? 0 : 1, profile.OpenTasks); Assert.Equal(cancel ? CompilerCoverage.None : CompilerCoverage.Partial, run.Coverage);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WrongRecordingOrInsufficientQueryBudgetWithdrawsEarlierMetrics(bool wrongDigest)
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        Assert.False(view.ProfileSyntheticSimulation(wrongDigest ? run.Manifest.Digest : run.Digest, maximumEvents: wrongDigest ? 6 : 5));
        var refused = view.SimulationTraceProfile!; Assert.Equal(SimulationTraceProfileState.Refused, refused.State);
        Assert.Equal(wrongDigest ? "coglatas.profile.wrong-recording" : "coglatas.profile.event-bound", refused.Reason);
        Assert.Null(refused.Run); Assert.Null(refused.RecordedElapsedTicks); Assert.Null(refused.RecordedTaskTicks); Assert.Same(run, view.SimulationRun);
    }
    [Fact]
    public async Task MissingOwnedRunCannotReexecuteOrRecoverAnEarlierRecording()
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        Assert.True(view.LoadSyntheticScopedSources()); var compiler = view.Result;
        Assert.False(view.ProfileSyntheticSimulation(run.Digest)); Assert.Null(view.SimulationTraceProfile); Assert.Null(view.SimulationRun); Assert.Same(compiler, view.Result);
        view.Reset(); Assert.False(view.ProfileSyntheticSimulation(run.Digest)); Assert.Null(view.SimulationTraceProfile);
    }
    [Fact]
    public async Task CancelledQueryWithdrawsItsPriorSummaryAndLeavesTheRecordingUnchanged()
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; var data = run.Data; Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.ProfileSyntheticSimulation(run.Digest, token: cancellation.Token));
        Assert.Null(view.SimulationTraceProfile); Assert.Same(run, view.SimulationRun); Assert.Same(data, run.Data);
    }
    [Theory]
    [InlineData("reset")] [InlineData("dispose")] [InlineData("begin")] [InlineData("reload")] [InlineData("run")] [InlineData("debug")]
    public async Task ReplacingTheOwnedContextWithdrawsThePreviouslyPublishedProfile(string mode)
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        var previous = view.SimulationTraceProfile!; Change(view, f.Request, mode);
        Assert.Null(view.SimulationTraceProfile); Assert.Same(run, previous.Run); Assert.Equal(15, previous.RecordedElapsedTicks);
    }
    [Theory]
    [InlineData("reset", false)] [InlineData("reset", true)] [InlineData("dispose", false)] [InlineData("dispose", true)]
    [InlineData("begin", false)] [InlineData("begin", true)] [InlineData("reload", false)] [InlineData("reload", true)]
    [InlineData("run", false)] [InlineData("run", true)] [InlineData("debug", false)] [InlineData("debug", true)]
    public async Task NotificationReentrancyCannotPublishOrRetainAnInterruptedProfile(string mode, bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.SimulationTraceProfile is not null) != afterPublication) return;
            entered = true; Change(subject, f.Request, mode);
        };
        Assert.False(view.ProfileSyntheticSimulation(run.Digest)); Assert.True(entered); Assert.Null(view.SimulationTraceProfile);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ANewerReentrantQueryOwnsThePublishedProfile(bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; var entered = false; ProjectSimulationTraceProfile? latest = null;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.SimulationTraceProfile is not null) != afterPublication) return;
            entered = true; Assert.True(subject.ProfileSyntheticSimulation(run.Digest, maximumEvents: 6)); latest = subject.SimulationTraceProfile;
        };
        Assert.False(view.ProfileSyntheticSimulation(run.Digest)); Assert.True(entered); Assert.NotNull(latest); Assert.Same(latest, view.SimulationTraceProfile); Assert.Equal(6, latest.QueryMaximumEvents);
    }
    [Fact]
    public async Task PausedDebuggerHasNoFinalRunProfileAndItsTerminalRecordingCanBeReadExplicitly()
    {
        var f = await Ready(); using var view = f.View; var old = view.SimulationRun!; Assert.True(view.ProfileSyntheticSimulation(old.Digest));
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(22104)), 100)); var paused = view.SimulationDebug!;
        Assert.Null(view.SimulationTraceProfile); Assert.False(view.ProfileSyntheticSimulation(old.Digest)); Assert.Null(paused.Result);
        Assert.True(view.ContinueSyntheticSimulation(paused)); var terminal = view.SimulationRun!;
        Assert.True(view.ProfileSyntheticSimulation(terminal.Digest)); Assert.Same(terminal, view.SimulationTraceProfile!.Run);
        Assert.Null(paused.Result); Assert.Empty(paused.Events); Assert.Same(terminal, view.SimulationDebug!.Result);
    }
    [Fact]
    public async Task InvalidQueryLimitWithdrawsThePreviousProfileWithoutChangingTheRun()
    {
        var f = await Ready(); using var view = f.View; var run = view.SimulationRun!; Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ProfileSyntheticSimulation(run.Digest, maximumEvents: 0));
        Assert.Null(view.SimulationTraceProfile); Assert.Same(run, view.SimulationRun);
    }
    private static void Change(SyntheticWorkbenchViewModel view, CompilerHostRequest request, string mode)
    {
        if (mode == "reset") view.Reset(); if (mode == "dispose") view.Dispose();
        if (mode == "begin") view.BeginSynthetic(new(request.Input, 2)); if (mode == "reload") Assert.True(view.LoadSyntheticScopedSources());
        if (mode == "run") Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(22102))));
        if (mode == "debug") Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(22103))));
    }
}
