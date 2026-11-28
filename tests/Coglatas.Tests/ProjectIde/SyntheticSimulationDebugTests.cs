using Coglatas.ProjectIde.Workbench.Synthetic;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticSimulationDebugTests
{
    private static ProjectLocation Pending(SimulationDebugSnapshot snapshot) => ProjectLocation.Parse(SourceJson.Parse(snapshot.Pending!.Value.GetProperty("location").GetRawText()));
    [Fact]
    public async Task ExplicitAdmittedControlsPreserveCompilerResultsAndNavigateOnlyTheCurrentRunPrefix()
    {
        var f = await SyntheticSimulationTests.Ready(); using var view = f.View; var compiler = view.Result;
        Assert.False(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1000)))); Assert.True(view.LoadSyntheticScopedSources()); var plan = view.ScopedExecutionPlan!;
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1000)), 100)); var ready = view.SimulationDebug!;
        Assert.Equal(SimulationControlState.Ready, ready.State); Assert.Null(view.SimulationRun);
        var first = Pending(ready); var breakpoint = new SimulationBreakpoint(Guid.Parse(Id(1001)), plan.Digest, first, SimulationBreakpointPoint.TaskStart);
        Assert.True(view.SetSyntheticSimulationBreakpoints(ready, [breakpoint])); Assert.False(view.StepSyntheticSimulation(ready));
        Assert.True(view.ContinueSyntheticSimulation(view.SimulationDebug!)); var stopped = view.SimulationDebug!;
        Assert.Equal(SimulationControlState.Paused, stopped.State); Assert.Empty(stopped.Events); Assert.Null(view.SimulationRun);
        Assert.True(view.InspectSyntheticSimulation(stopped, first)); Assert.NotNull(view.SimulationDebug!.Inspection);
        Assert.True(view.StepOverSyntheticSimulation(view.SimulationDebug, first)); var stepped = view.SimulationDebug!; Assert.Equal(4, stepped.Events.Count);
        Assert.False(view.NavigateSimulationDebugEvent(stopped, stepped.Events[0])); Assert.False(view.NavigateSimulationDebugEvent(stepped, SourceJson.Parse(stepped.Events[0].CanonicalText)));
        Assert.True(view.NavigateSimulationDebugEvent(stepped, stepped.Events[0])); Assert.NotNull(view.ScopedSourceTarget);
        Assert.True(view.ContinueSyntheticSimulation(stepped)); var final = view.SimulationDebug!;
        Assert.Equal(SimulationControlState.Terminated, final.State); Assert.Same(final.Result, view.SimulationRun); Assert.Same(compiler, view.Result); Assert.Same(plan, view.ScopedExecutionPlan);
        Assert.Equal(ProjectSimulation.Execute(final.Manifest, plan.Input.InputDigest).Data, final.Result!.Data);
        Assert.True(view.LoadSyntheticScopedSources()); Assert.Null(view.SimulationDebug); Assert.Null(view.SimulationRun); Assert.Equal(4, stepped.Events.Count);
    }
    [Fact]
    public async Task CancelledAndBoundedDebugRunsRemainSeparateTerminalEvidence()
    {
        var f = await SyntheticSimulationTests.Ready(); using var view = f.View; Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1002)), runLimits: new(MaximumEvents: 1)));
        Assert.True(view.ContinueSyntheticSimulation(view.SimulationDebug!)); var bounded = view.SimulationDebug!;
        Assert.Equal(CompilerExecutionState.ResourceExhausted, bounded.Result!.Execution); Assert.Single(bounded.Events);
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1003)))); Assert.False(view.StepSyntheticSimulation(bounded));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.True(view.ContinueSyntheticSimulation(view.SimulationDebug!, cancellation.Token)); Assert.Equal(CompilerExecutionState.Cancelled, view.SimulationRun!.Execution);
        Assert.NotEqual(CompilerVerdict.Pass, view.SimulationRun.Verdict); Assert.Single(bounded.Events);
    }
    [Theory]
    [InlineData("reset", false)] [InlineData("reset", true)] [InlineData("dispose", false)] [InlineData("dispose", true)]
    [InlineData("begin", false)] [InlineData("begin", true)] [InlineData("reload", false)] [InlineData("reload", true)]
    public async Task StartNotificationsCannotPublishAnInterruptedDebugSession(string mode, bool afterPublication)
    {
        var f = await SyntheticSimulationTests.Ready(); using var view = f.View; Assert.True(view.LoadSyntheticScopedSources()); var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.SimulationDebug is not null) != afterPublication) return; entered = true;
            if (mode == "reset") subject.Reset(); if (mode == "dispose") subject.Dispose();
            if (mode == "begin") subject.BeginSynthetic(new(f.Request.Input, 2)); if (mode == "reload") subject.LoadSyntheticScopedSources();
        };
        Assert.False(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1004)))); Assert.True(entered); Assert.Null(view.SimulationDebug); Assert.Null(view.SimulationRun);
    }
    [Theory]
    [InlineData("reset", false)] [InlineData("reset", true)] [InlineData("dispose", false)] [InlineData("dispose", true)]
    [InlineData("begin", false)] [InlineData("begin", true)] [InlineData("reload", false)] [InlineData("reload", true)]
    public async Task CommandEnumerationAndPublicationCannotRetainAnOldRun(string mode, bool duringEnumeration)
    {
        var f = await SyntheticSimulationTests.Ready(); using var view = f.View; Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1005)))); var basis = view.SimulationDebug!; var entered = false;
        void Interrupt(SyntheticWorkbenchViewModel subject) {
            if (entered) return; entered = true;
            if (mode == "reset") subject.Reset(); if (mode == "dispose") subject.Dispose();
            if (mode == "begin") subject.BeginSynthetic(new(f.Request.Input, 2)); if (mode == "reload") subject.LoadSyntheticScopedSources();
        }
        IEnumerable<SimulationBreakpoint> Reenter(SyntheticWorkbenchViewModel subject) { Interrupt(subject); yield return new(Guid.Parse(Id(1006)), basis.Manifest.Plan.Digest, Pending(basis), SimulationBreakpointPoint.TaskStart); }
        if (duringEnumeration) Assert.False(view.SetSyntheticSimulationBreakpoints(basis, Reenter(view)));
        else { view.PropertyChanged += (sender, _) => Interrupt(Assert.IsType<SyntheticWorkbenchViewModel>(sender)); Assert.False(view.StepSyntheticSimulation(basis)); }
        Assert.True(entered); Assert.Null(view.SimulationDebug); Assert.Null(view.SimulationRun); Assert.Empty(basis.Events);
    }
    [Fact]
    public async Task ReentrantStepNotificationCannotApplyTwoCommandsToTheSameUserAction()
    {
        var f = await SyntheticSimulationTests.Ready(); using var view = f.View; Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1007)))); var reentered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender); if (subject.SimulationDebug is not { } current) return; reentered = true; Assert.False(subject.StepSyntheticSimulation(current)); };
        Assert.True(view.StepSyntheticSimulation(view.SimulationDebug!)); Assert.True(reentered); Assert.Single(view.SimulationDebug!.Events);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancelledReadyPublicationWithdrawsTheOwnedDebugSession(bool throwOnPublication, bool throwOnWithdrawal)
    {
        var fixture = await SyntheticSimulationTests.Ready();
        using var view = fixture.View;
        Assert.True(view.LoadSyntheticScopedSources());
        var plan = view.ScopedExecutionPlan;
        var compiler = view.Result;
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var token = cancellation.Token;
        var publicationFailure = new InvalidOperationException("publication failed");
        var withdrawalFailure = new InvalidOperationException("withdrawal failed");
        SimulationDebugSnapshot? published = null;
        var withdrawalNotifications = 0;
        System.ComponentModel.PropertyChangedEventHandler observer = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (published is null && subject.SimulationDebug is { State: SimulationControlState.Ready } snapshot)
            {
                published = snapshot;
                cancel();
                if (throwOnPublication) throw publicationFailure;
            }
            else if (published is not null && subject.SimulationDebug is null && throwOnWithdrawal)
            {
                throw withdrawalFailure;
            }
        };
        System.ComponentModel.PropertyChangedEventHandler laterObserver = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (published is not null && subject.SimulationDebug is null) withdrawalNotifications++;
        };
        view.PropertyChanged += observer;
        view.PropertyChanged += laterObserver;
        Exception? error;
        try { error = CaptureDebugStartError(view, Guid.Parse(Id(1010)), token); }
        finally
        {
            view.PropertyChanged -= observer;
            view.PropertyChanged -= laterObserver;
        }

        Assert.NotNull(published);
        Assert.Null(view.SimulationDebug);
        Assert.Null(view.SimulationRun);
        Assert.Empty(view.SimulationDebugControlReason);
        Assert.False(view.StepSyntheticSimulation(published));
        Assert.Empty(published.Events);
        Assert.Same(plan, view.ScopedExecutionPlan);
        Assert.Same(compiler, view.Result);
        Assert.Equal(1, withdrawalNotifications);
        if (throwOnPublication)
        {
            Assert.Same(publicationFailure, error);
        }
        else
        {
            var cancelled = Assert.IsType<OperationCanceledException>(error);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(throwOnWithdrawal ? withdrawalFailure : null, cancelled.InnerException);
        }
    }

    [Fact]
    public async Task CancelledReadyPublicationKeepsAReentrantReplacementRun()
    {
        var fixture = await SyntheticSimulationTests.Ready();
        using var view = fixture.View;
        Assert.True(view.LoadSyntheticScopedSources());
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var token = cancellation.Token;
        ProjectSimulationRun? replacement = null;
        var replacing = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (replacing || subject.SimulationDebug is not { State: SimulationControlState.Ready }) return;
            replacing = true;
            cancel();
            Assert.True(subject.RunSyntheticSimulation(Guid.Parse(Id(1011)), 100));
            replacement = subject.SimulationRun;
        };
        view.PropertyChanged += observer;
        Exception? error;
        try { error = CaptureDebugStartError(view, Guid.Parse(Id(1012)), token); }
        finally { view.PropertyChanged -= observer; }

        var cancelled = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(token, cancelled.CancellationToken);
        Assert.NotNull(replacement);
        Assert.Same(replacement, view.SimulationRun);
        Assert.Equal(CompilerExecutionState.Completed, replacement.Execution);
        Assert.Null(view.SimulationDebug);
    }

    [Fact]
    public async Task CancellationDuringDebugPreparationRemainsExplicitTerminalEvidence()
    {
        var fixture = await SyntheticSimulationTests.Ready();
        using var view = fixture.View;
        Assert.True(view.LoadSyntheticScopedSources());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1013)), cancellationToken: cancellation.Token));
        var snapshot = Assert.IsType<SimulationDebugSnapshot>(view.SimulationDebug);
        Assert.Equal(SimulationControlState.Terminated, snapshot.State);
        Assert.Same(snapshot.Result, view.SimulationRun);
        Assert.Equal(CompilerExecutionState.Cancelled, view.SimulationRun!.Execution);
        Assert.False(view.StepSyntheticSimulation(snapshot));
    }

    [Theory]
    [InlineData("step", false, false)]
    [InlineData("continue", false, false)]
    [InlineData("step-over", false, false)]
    [InlineData("step", false, true)]
    [InlineData("step", true, true)]
    public async Task CancelledCommandPublicationWithdrawsItsDebugSession(string command, bool failPublication, bool failWithdrawal)
    {
        var fixture = await SyntheticSimulationTests.Ready();
        using var view = fixture.View;
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1014))));
        var basis = Assert.IsType<SimulationDebugSnapshot>(view.SimulationDebug);
        var compiler = view.Result;
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var token = cancellation.Token;
        var publicationFailure = new ObjectDisposedException("publication observer");
        var withdrawalFailure = new InvalidOperationException("withdrawal observer");
        SimulationDebugSnapshot? published = null;
        var withdrawals = 0;
        System.ComponentModel.PropertyChangedEventHandler observer = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (published is null && subject.SimulationDebug is { } snapshot && !ReferenceEquals(snapshot, basis))
            {
                published = snapshot;
                cancel();
                if (failPublication) throw publicationFailure;
            }
            else if (published is not null && subject.SimulationDebug is null && failWithdrawal)
            {
                throw withdrawalFailure;
            }
        };
        System.ComponentModel.PropertyChangedEventHandler laterObserver = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (published is not null && subject.SimulationDebug is null) withdrawals++;
        };
        view.PropertyChanged += observer;
        view.PropertyChanged += laterObserver;
        Exception? error;
        try { error = CaptureDebugCommandError(view, command, basis, token); }
        finally
        {
            view.PropertyChanged -= observer;
            view.PropertyChanged -= laterObserver;
        }

        Assert.NotNull(published);
        Assert.NotEmpty(published.Events);
        Assert.Empty(basis.Events);
        Assert.Null(view.SimulationDebug);
        Assert.Null(view.SimulationRun);
        Assert.Empty(view.SimulationDebugControlReason);
        Assert.False(view.StepSyntheticSimulation(published));
        Assert.Same(compiler, view.Result);
        Assert.Equal(1, withdrawals);
        if (failPublication) Assert.Same(publicationFailure, error);
        else
        {
            var cancelled = Assert.IsType<OperationCanceledException>(error);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(failWithdrawal ? withdrawalFailure : null, cancelled.InnerException);
        }
    }

    [Fact]
    public async Task CancelledCommandPublicationKeepsAReentrantReplacementRun()
    {
        var fixture = await SyntheticSimulationTests.Ready();
        using var view = fixture.View;
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1015))));
        var basis = Assert.IsType<SimulationDebugSnapshot>(view.SimulationDebug);
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        var token = cancellation.Token;
        ProjectSimulationRun? replacement = null;
        var replacing = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (replacing || subject.SimulationDebug is not { } snapshot || ReferenceEquals(snapshot, basis)) return;
            replacing = true;
            cancel();
            Assert.True(subject.RunSyntheticSimulation(Guid.Parse(Id(1016)), 100));
            replacement = subject.SimulationRun;
        };
        view.PropertyChanged += observer;
        Exception? error;
        try { error = CaptureDebugCommandError(view, "step", basis, token); }
        finally { view.PropertyChanged -= observer; }

        var cancelled = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(token, cancelled.CancellationToken);
        Assert.NotNull(replacement);
        Assert.Same(replacement, view.SimulationRun);
        Assert.Equal(CompilerExecutionState.Completed, replacement.Execution);
        Assert.Null(view.SimulationDebug);
    }

    private static Exception? CaptureDebugCommandError(SyntheticWorkbenchViewModel view, string command, SimulationDebugSnapshot basis,
        CancellationToken token)
    {
        try
        {
            _ = command switch
            {
                "step" => view.StepSyntheticSimulation(basis, token),
                "continue" => view.ContinueSyntheticSimulation(basis, token),
                "step-over" => view.StepOverSyntheticSimulation(basis, Pending(basis), token),
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static Exception? CaptureDebugStartError(SyntheticWorkbenchViewModel view, Guid runId, CancellationToken token)
    {
        try
        {
            _ = view.BeginSyntheticSimulationDebug(runId, cancellationToken: token);
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }
}
