using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticSimulationComparisonTests
{
    private static async Task<(SyntheticWorkbenchViewModel View, CompilerHostRequest Request, ProjectSimulationRun Before)> Ready()
    {
        var f = await SyntheticSimulationTests.Ready(); Assert.True(f.View.LoadSyntheticScopedSources());
        Assert.True(f.View.RunSyntheticSimulation(Guid.Parse(Id(1100)))); var before = f.View.SimulationRun!;
        Assert.True(f.View.RunSyntheticSimulation(Guid.Parse(Id(1101)), 100)); return (f.View, f.Request, before);
    }
    [Fact]
    public async Task ExplicitComparisonKeepsBothRecordingsAndTheCurrentCompilerResultImmutable()
    {
        var f = await Ready(); using var view = f.View; var after = view.SimulationRun!; var compiler = view.Result;
        var originalBefore = f.Before.Data; var originalAfter = after.Data;
        Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); var comparison = view.SimulationComparison!;
        Assert.Equal(SimulationRunComparisonState.Complete, comparison.State); Assert.Same(f.Before, comparison.Before); Assert.Same(after, comparison.After);
        Assert.True(comparison.SameInput); Assert.True(comparison.SameLimits); Assert.Equal(0L, comparison.ObservedElapsedDelta);
        Assert.Same(compiler, view.Result); Assert.Same(after, view.SimulationRun); Assert.Same(originalBefore, f.Before.Data); Assert.Same(originalAfter, after.Data);
        Assert.Equal(CompilerVerdict.NotRun, after.Verdict);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PartialAndCancelledRunsHaveNoCompleteDelta(bool cancel)
    {
        var f = await Ready(); using var view = f.View; using var cancellation = new CancellationTokenSource(); if (cancel) cancellation.Cancel();
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1102)), limits: new(MaximumEvents: 1), cancellationToken: cancellation.Token));
        var after = view.SimulationRun!; Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest));
        Assert.Equal(SimulationRunComparisonState.Incomplete, view.SimulationComparison!.State); Assert.Null(view.SimulationComparison.ObservedElapsedDelta);
        Assert.Same(after, view.SimulationComparison.After); Assert.Same(f.Before, view.SimulationComparison.Before);
        Assert.Equal(cancel ? CompilerExecutionState.Cancelled : CompilerExecutionState.ResourceExhausted, after.Execution);
    }
    [Fact]
    public async Task WrongExpectedRecordingWithdrawsThePriorSummaryAndRetainsOnlyRefusal()
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest));
        Assert.False(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Manifest.Digest)); var refused = view.SimulationComparison!;
        Assert.Equal(SimulationRunComparisonState.Refused, refused.State); Assert.Null(refused.Before); Assert.Null(refused.After);
        Assert.Null(refused.SameInput); Assert.Null(refused.SameLimits); Assert.Null(refused.ObservedElapsedDelta);
    }
    [Fact]
    public async Task MissingCurrentRunCannotExecuteOrRestoreAnOldRecording()
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.LoadSyntheticScopedSources()); var compiler = view.Result;
        Assert.False(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); Assert.Null(view.SimulationComparison); Assert.Null(view.SimulationRun); Assert.Same(compiler, view.Result);
        view.Reset(); Assert.False(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); Assert.Null(view.SimulationComparison); Assert.Null(view.Result);
    }
    [Fact]
    public async Task ComparisonCancellationWithdrawsThePriorPairWithoutReplacingTheOwnedRun()
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); var after = view.SimulationRun;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest, cancellation.Token));
        Assert.Null(view.SimulationComparison); Assert.Same(after, view.SimulationRun);
    }
    [Theory]
    [InlineData("reset")] [InlineData("dispose")] [InlineData("begin")] [InlineData("reload")] [InlineData("run")] [InlineData("debug")]
    public async Task ReplacingTheOwnedContextWithdrawsBothComparisonSummaries(string mode)
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); var recording = view.SimulationComparison!;
        Change(view, f.Request, mode); Assert.Null(view.SimulationComparison); Assert.Same(f.Before, recording.Before); Assert.NotNull(recording.After);
    }
    [Theory]
    [InlineData("reset", false)] [InlineData("reset", true)] [InlineData("dispose", false)] [InlineData("dispose", true)]
    [InlineData("begin", false)] [InlineData("begin", true)] [InlineData("reload", false)] [InlineData("reload", true)]
    [InlineData("run", false)] [InlineData("run", true)] [InlineData("debug", false)] [InlineData("debug", true)]
    public async Task NotificationReentrancyCannotPublishOrRetainTheInterruptedPair(string mode, bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.SimulationComparison is not null) != afterPublication) return; entered = true; Change(subject, f.Request, mode);
        };
        Assert.False(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); Assert.True(entered); Assert.Null(view.SimulationComparison);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TheLatestReentrantComparisonOwnsTheResult(bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; var entered = false; ProjectSimulationRunComparison? latest = null;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.SimulationComparison is not null) != afterPublication) return; entered = true;
            Assert.True(subject.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); latest = subject.SimulationComparison;
        };
        Assert.False(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest)); Assert.True(entered); Assert.NotNull(latest); Assert.Same(latest, view.SimulationComparison);
    }
    [Fact]
    public async Task PausedEditingCompilesNewScenarioAndDraftRunsAndComparesWithoutChangingTheOriginalTrace()
    {
        var f = await Ready(); using var view = f.View; var originalPlan = view.ScopedExecutionPlan!; var input = originalPlan.Input;
        Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1110)), 100)); Assert.True(view.StepSyntheticSimulation(view.SimulationDebug!));
        var paused = view.SimulationDebug!; var originalTrace = paused.Data; Assert.Single(paused.Events); Assert.Null(paused.Result);
        var task = originalPlan.Data.Value.GetProperty("tasks").EnumerateArray().Single(row => row.GetProperty("sourceEntityId").GetString() == Id(7));
        var location = ProjectLocation.Parse(SourceJson.Parse(task.GetProperty("location").GetRawText()));
        var duration = JsonNode.Parse(task.GetProperty("duration").GetRawText())!; duration["value"] = "20";
        var scenario = view.PreviewScenario(ScenarioId.Parse(Id(1111)), ScenarioRevisionId.Parse(Id(1112)),
            [new(location, ProjectScenarioOverrideKind.TaskDuration, Json(duration))])!.Plan!;
        var request = view.LoadScenario(scenario, 2)!; Assert.NotNull(request); Assert.Null(view.SimulationDebug); Assert.Null(view.SimulationComparison);
        Assert.True(view.TryAccept(await Host(scenario.Input).CompileAsync(request))); Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1113)), 100)); var hypothesis = view.SimulationRun!;
        Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest));
        Assert.False(view.SimulationComparison!.SameInput); Assert.Equal(9L, view.SimulationComparison.ObservedElapsedDelta);
        Assert.False(view.StepSyntheticSimulation(paused)); Assert.Equal(originalTrace, paused.Data); Assert.Single(paused.Events); Assert.Null(paused.Result);
        var model = ProjectSemanticBinder.Bind(input); var basis = input.Source.Context.CommittedRevision!;
        var conversion = view.PreviewScenarioDraft(model, input.InputDigest,
            new(ProposalId.Parse(Id(1114)), CandidateRevisionId.Parse(Id(1115)), basis, basis))!.Plan!;
        var draft = view.ApplyScenarioDraft(conversion, input, 3)!; Assert.NotNull(draft); Assert.Null(view.SimulationComparison); Assert.Null(view.SimulationRun);
        Assert.True(view.TryAccept(await Host(draft.Input.Materialize()).CompileAsync(draft))); Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1116)), 200)); var converted = view.SimulationRun!;
        Assert.True(view.CompareSyntheticSimulationRuns(hypothesis, hypothesis.Digest)); Assert.False(view.SimulationComparison!.SameInput);
        Assert.Equal(0L, view.SimulationComparison.ObservedElapsedDelta); Assert.Equal(24, converted.VirtualTicks - converted.Manifest.StartTicks);
        Assert.Equal(originalTrace, paused.Data); Assert.Equal(15, f.Before.VirtualTicks - f.Before.Manifest.StartTicks);
    }
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()), CompilerExecutionPlan.Engine));
    }
    private static void Change(SyntheticWorkbenchViewModel view, CompilerHostRequest request, string mode)
    {
        if (mode == "reset") view.Reset(); if (mode == "dispose") view.Dispose();
        if (mode == "begin") view.BeginSynthetic(new(request.Input, 2)); if (mode == "reload") Assert.True(view.LoadSyntheticScopedSources());
        if (mode == "run") Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1103))));
        if (mode == "debug") Assert.True(view.BeginSyntheticSimulationDebug(Guid.Parse(Id(1104))));
    }
}
