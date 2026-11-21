using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticSimulationTests
{
    internal static async Task<(SyntheticWorkbenchViewModel View, CompilerHostRequest Request)> Ready()
    {
        var input = ProjectSimulationTests.Plan().Input;
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(x => x.GetString()), CompilerExecutionPlan.Engine);
        var request = new CompilerHostRequest(new(input), 1); var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await new LocalNetCompilerHost(profile).CompileAsync(request))); return (view, request);
    }
    [Fact]
    public async Task ExplicitAdmittedRunKeepsCompilerAndPlanSeparateAndNavigatesOnlyItsOwnEvents()
    {
        var f = await Ready(); using var view = f.View; var original = view.Result;
        Assert.False(view.RunSyntheticSimulation(Guid.Parse(Id(960)))); Assert.Null(view.SimulationRun);
        Assert.True(view.LoadSyntheticScopedSources()); var plan = view.ScopedExecutionPlan;
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(960)), 100)); var run = view.SimulationRun!;
        Assert.Equal(CompilerExecutionState.Completed, run.Execution); Assert.Same(original, view.Result); Assert.Same(plan, view.ScopedExecutionPlan);
        Assert.Equal(CompilerVerdict.NotRun, run.Verdict); Assert.True(view.NavigateSimulationEvent(run.Events[0]));
        Assert.Equal(run.Events[0].Value.GetProperty("location").GetRawText(), view.ScopedSourceTarget!.Data.CanonicalText);
        Assert.False(view.NavigateSimulationEvent(SourceJson.Parse(run.Events[0].CanonicalText)));
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(961)), limits: new(MaximumEvents: 1)));
        Assert.Equal(CompilerExecutionState.ResourceExhausted, view.SimulationRun!.Execution); Assert.False(view.NavigateSimulationEvent(run.Events[0]));
        view.BeginSynthetic(new(f.Request.Input, 2)); Assert.Null(view.SimulationRun); Assert.Equal(6, run.Events.Count);
    }
    [Fact]
    public async Task CancelledRunRemainsExplicitAndReadmissionWithdrawsIt()
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.LoadSyntheticScopedSources());
        using var token = new CancellationTokenSource(); token.Cancel();
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(962)), cancellationToken: token.Token));
        Assert.Equal(CompilerExecutionState.Cancelled, view.SimulationRun!.Execution); Assert.Empty(view.SimulationRun.Events);
        Assert.True(view.LoadSyntheticScopedSources()); Assert.Null(view.SimulationRun);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicationCancellationWithdrawsTheOwnedSimulationAndItsProfile(bool throwOnPublication, bool throwOnWithdrawal)
    {
        var ready = await Ready();
        using var view = ready.View;
        Assert.True(view.LoadSyntheticScopedSources());
        var plan = view.ScopedExecutionPlan;
        using var cancellation = new CancellationTokenSource();
        var publicationFailure = new InvalidOperationException("publication failed");
        var withdrawalFailure = new InvalidOperationException("withdrawal failed");
        var published = false;
        var withdrawalNotifications = 0;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, _) =>
        {
            if (!published && view.SimulationRun is { Execution: CompilerExecutionState.Completed } run)
            {
                published = true;
                Assert.True(view.ProfileSyntheticSimulation(run.Digest));
                Assert.NotNull(view.SimulationTraceProfile);
                cancellation.Cancel();
                if (throwOnPublication) throw publicationFailure;
            }
            else if (published && view.SimulationRun is null && throwOnWithdrawal)
            {
                throw withdrawalFailure;
            }
        };
        System.ComponentModel.PropertyChangedEventHandler laterObserver = (_, _) =>
        {
            if (published && view.SimulationRun is null) withdrawalNotifications++;
        };
        view.PropertyChanged += observer;
        view.PropertyChanged += laterObserver;
        Exception? failure;
        try { failure = Record.Exception(() => { _ = view.RunSyntheticSimulation(Guid.Parse(Id(964)), cancellationToken: cancellation.Token); }); }
        finally
        {
            view.PropertyChanged -= observer;
            view.PropertyChanged -= laterObserver;
        }

        Assert.True(published);
        Assert.Null(view.SimulationRun);
        Assert.Null(view.SimulationTraceProfile);
        Assert.Same(plan, view.ScopedExecutionPlan);
        Assert.Equal(1, withdrawalNotifications);
        if (throwOnPublication)
        {
            Assert.Same(publicationFailure, failure);
        }
        else
        {
            var cancelled = Assert.IsType<OperationCanceledException>(failure);
            Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            Assert.Same(throwOnWithdrawal ? withdrawalFailure : null, cancelled.InnerException);
        }
    }

    [Fact]
    public async Task PublicationCancellationKeepsAReentrantReplacementSimulation()
    {
        var ready = await Ready();
        using var view = ready.View;
        Assert.True(view.LoadSyntheticScopedSources());
        using var cancellation = new CancellationTokenSource();
        ProjectSimulationRun? replacement = null;
        var replaced = false;
        System.ComponentModel.PropertyChangedEventHandler observer = (_, _) =>
        {
            if (replaced || view.SimulationRun is null) return;
            replaced = true;
            cancellation.Cancel();
            Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(965)), 100));
            replacement = view.SimulationRun;
        };
        view.PropertyChanged += observer;
        OperationCanceledException cancelled;
        try { cancelled = Assert.Throws<OperationCanceledException>(() => view.RunSyntheticSimulation(Guid.Parse(Id(966)), cancellationToken: cancellation.Token)); }
        finally { view.PropertyChanged -= observer; }
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.NotNull(replacement);
        Assert.Same(replacement, view.SimulationRun);
        Assert.Equal(CompilerExecutionState.Completed, replacement.Execution);
    }

    [Theory]
    [InlineData("reset", false)] [InlineData("reset", true)] [InlineData("dispose", false)] [InlineData("dispose", true)]
    [InlineData("begin", false)] [InlineData("begin", true)] [InlineData("reload", false)] [InlineData("reload", true)]
    public async Task NotificationReentrancyCannotPublishOrRetainAnOldRun(string mode, bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.LoadSyntheticScopedSources()); var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.SimulationRun is not null) != afterPublication) return;
            entered = true;
            if (mode == "reset") subject.Reset();
            if (mode == "dispose") subject.Dispose();
            if (mode == "begin") subject.BeginSynthetic(new(f.Request.Input, 2));
            if (mode == "reload") subject.LoadSyntheticScopedSources();
        };
        Assert.False(view.RunSyntheticSimulation(Guid.Parse(Id(963)))); Assert.True(entered); Assert.Null(view.SimulationRun);
    }
}
