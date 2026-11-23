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

    private sealed class CancelRecordedRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _replace;
        private readonly ContentDigest _runDigest;
        private int _notifications;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public ProjectSimulationTraceProfile? Replacement { get; private set; }
        public CancelRecordedRead(SyntheticWorkbenchViewModel vm, bool replace)
        { _vm = vm; _replace = replace; _runDigest = vm.SimulationRun!.Digest; vm.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.ProfileSyntheticSimulation(_runDigest, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.ProfileSyntheticSimulation(_runDigest);
                Replacement = _vm.SimulationTraceProfile;
            }
            _source.Cancel();
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationAtRecordedSimulationReadPublicationWithdrawsOnlyItsOwnedView(bool replace)
    {
        var f = await Ready();
        using var view = f.View;
        var result = view.Result!; var resultData = result.Data; var run = view.SimulationRun!; var runData = run.Data;
        Assert.NotEmpty(run.Events);
        Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        var previous = view.SimulationTraceProfile!; var previousData = previous.Data;
        using var cancellation = new CancelRecordedRead(view, replace); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, view.Result); Assert.Equal(resultData, result.Data); Assert.Same(run, view.SimulationRun); Assert.Equal(runData, run.Data);
        Assert.Equal(previousData, previous.Data);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, view.SimulationTraceProfile);
        }
        else Assert.Null(view.SimulationTraceProfile);
        Assert.IsType<OperationCanceledException>(error);
    }

    [Fact]
    public async Task CurrentSummaryReadsOnlyItsOwnedOriginalRecordingWithoutStartingExecution()
    {
        var f = await Ready();
        using var view = f.View;
        var run = view.SimulationRun!; var result = view.Result!; var source = view.Source!;
        var runData = run.Data; var resultData = result.Data; var sourceData = source.Data;
        Assert.NotEmpty(run.Events); Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        var basis = view.SimulationTraceProfile!; var summaryData = basis.Data;
        Assert.Same(run, view.ReadSimulationTraceProfileRun(basis));
        Assert.Same(run, view.SimulationRun); Assert.Same(result, view.Result); Assert.Same(source, view.Source);
        Assert.Equal(runData, run.Data); Assert.Equal(resultData, result.Data); Assert.Equal(sourceData, source.Data); Assert.Equal(summaryData, basis.Data);
        var detached = ProjectSimulationTraceProfile.Read(run, run.Digest);
        Assert.Equal(summaryData, detached.Data); Assert.NotSame(basis, detached);
        Assert.Null(view.ReadSimulationTraceProfileRun(detached)); Assert.Same(basis, view.SimulationTraceProfile);
    }
    [Theory]
    [InlineData("profile")] [InlineData("run")] [InlineData("reload")] [InlineData("reset")]
    public async Task AnOlderSummaryCannotOpenARecordingAfterOwnershipChanges(string mode)
    {
        var f = await Ready();
        using var view = f.View;
        var run = view.SimulationRun!; Assert.True(view.ProfileSyntheticSimulation(run.Digest)); var basis = view.SimulationTraceProfile!;
        var original = basis.Data;
        if (mode == "profile") Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        else Change(view, f.Request, mode);
        Assert.Null(view.ReadSimulationTraceProfileRun(basis)); Assert.Equal(original, basis.Data);
        if (mode == "profile") Assert.Same(run, view.ReadSimulationTraceProfileRun(view.SimulationTraceProfile!));
    }
    [Fact]
    public async Task ARefusedSummaryDoesNotProvideARecordingRead()
    {
        var f = await Ready();
        using var view = f.View;
        var run = view.SimulationRun!; Assert.False(view.ProfileSyntheticSimulation(run.Manifest.Digest));
        var refused = view.SimulationTraceProfile!; Assert.Equal(SimulationTraceProfileState.Refused, refused.State);
        Assert.Null(view.ReadSimulationTraceProfileRun(refused)); Assert.Same(run, view.SimulationRun);
    }

    private sealed class CancelInitialRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _admitted;
        private readonly ProjectSimulationRun _run;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public ProjectSimulationTraceProfile? Replacement { get; private set; }
        public CancelInitialRead(SyntheticWorkbenchViewModel view, bool admitted)
        {
            _view = view; _admitted = admitted; _run = view.SimulationRun!;
            view.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _view.ProfileSyntheticSimulation(_run.Digest, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || _view.SimulationTraceProfile is not null) return;
            Fired = true;
            ReplacementSucceeded = _view.ProfileSyntheticSimulation(_admitted ? _run.Digest : _run.Manifest.Digest);
            Replacement = _view.SimulationTraceProfile;
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringInitialNotificationReportsCancellationAndPreservesTheNewerSummary(bool admitted)
    {
        var f = await Ready();
        using var view = f.View;
        var run = view.SimulationRun!;
        var result = view.Result!;
        var runData = run.Data;
        var resultData = result.Data;
        Assert.True(view.ProfileSyntheticSimulation(run.Digest));
        var previous = view.SimulationTraceProfile!;
        var previousData = previous.Data;
        using var cancellation = new CancelInitialRead(view, admitted);
        var error = cancellation.Read();
        Assert.True(cancellation.Fired);
        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.Equal(admitted, cancellation.ReplacementSucceeded);
        var latest = cancellation.Replacement;
        Assert.NotNull(latest);
        Assert.NotSame(previous, latest);
        Assert.Same(latest, view.SimulationTraceProfile);
        Assert.Equal(admitted ? SimulationTraceProfileState.Complete : SimulationTraceProfileState.Refused, latest.State);
        if (admitted) Assert.Same(run, latest.Run);
        else Assert.Null(latest.Run);
        Assert.Same(run, view.SimulationRun);
        Assert.Same(result, view.Result);
        Assert.Equal(runData, run.Data);
        Assert.Equal(resultData, result.Data);
        Assert.Equal(previousData, previous.Data);
        Assert.IsType<OperationCanceledException>(error);
    }

}
