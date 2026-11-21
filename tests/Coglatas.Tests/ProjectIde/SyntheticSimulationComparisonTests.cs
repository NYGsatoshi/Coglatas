using System.ComponentModel;
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
    [Fact]
    public async Task ScenarioScheduleImpactUsesTheCapturedBaselineUntilDraftConversion()
    {
        var f = await ReadyScenarioSchedule();
        using var view = f.View;
        var baseline = Assert.IsType<ProjectSimulationRun>(f.Baseline);
        var hypothesis = Assert.IsType<ProjectSimulationRun>(view.SimulationRun);
        Assert.Same(baseline, view.ScenarioBaselineSimulation);
        Assert.Null(view.ScenarioScheduleImpact);
        Assert.True(view.ReadScenarioScheduleImpact());
        var impact = Assert.IsType<ProjectScenarioElapsedScheduleImpact>(view.ScenarioScheduleImpact);
        Assert.Equal(ScenarioElapsedScheduleImpactState.Complete, impact.State);
        Assert.Equal(3, impact.ModeledFinishDeltaTicks);
        Assert.Equal(baseline.Digest, impact.Baseline!.RunDigest);
        Assert.Equal(hypothesis.Digest, impact.Hypothesis!.RunDigest);
        Assert.Equal(6, Assert.Single(impact.Baseline.Tasks, row => row.Task.EntityId == EntityId.Parse(Id(7))).TotalFloatTicks);

        var basis = f.Scenario.Basis;
        Assert.Equal(CompilerVerdict.Fail, view.ScenarioBaselineTests!.Verdict);
        Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
        Assert.Equal(basis.InputDigest, view.ScenarioBaselineTests.Input.InputDigest);
        Assert.Equal(f.Scenario.Input.InputDigest, view.TestRun.Input.InputDigest);
        Assert.Equal(2, f.Scenario.Changes.Count);
        var sourceDiff = view.CompareSynthetic(ProjectSemanticBinder.Bind(basis), basis.InputDigest);
        Assert.NotNull(sourceDiff);
        Assert.False(sourceDiff.NoChanges);
        Assert.Same(impact, view.ScenarioScheduleImpact);

        var originalRevision = basis.Source.Context.CommittedRevision!;
        var conversion = view.PreviewScenarioDraft(ProjectSemanticBinder.Bind(basis), basis.InputDigest,
            new(ProposalId.Parse(Id(1230)), CandidateRevisionId.Parse(Id(1231)), originalRevision, originalRevision))!.Plan!;
        Assert.Same(impact, view.ScenarioScheduleImpact);
        var draft = Assert.IsType<CompilerHostRequest>(view.ApplyScenarioDraft(conversion, basis, 3));
        Assert.Null(view.ScenarioBaselineSimulation);
        Assert.Null(view.ScenarioScheduleImpact);
        Assert.True(view.TryAccept(await Host(conversion.After).CompileAsync(draft)));
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.RunSyntheticTests());
        Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
        Assert.Equal(conversion.After.InputDigest, view.TestRun.Input.InputDigest);
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1232)), 100));
        Assert.False(view.ReadScenarioScheduleImpact());
        Assert.Equal(115, baseline.VirtualTicks);
        Assert.Equal(118, hypothesis.VirtualTicks);
    }

    [Fact]
    public async Task ScenarioScheduleImpactDoesNotSubstituteALaterRunForAMissingBaseline()
    {
        var f = await ReadyScenarioSchedule(captureBaseline: false);
        using var view = f.View;
        Assert.Null(view.ScenarioBaselineSimulation);
        Assert.NotNull(view.SimulationRun);
        Assert.False(view.ReadScenarioScheduleImpact());
        Assert.Null(view.ScenarioScheduleImpact);
    }

    [Fact]
    public async Task ScenarioScheduleImpactWithdrawsProjectionWhenTheRunChangesAndShowsIncomplete()
    {
        var f = await ReadyScenarioSchedule(limits: new(MaximumEvents: 2));
        using var view = f.View;
        Assert.True(view.ReadScenarioScheduleImpact());
        Assert.Equal(ScenarioElapsedScheduleImpactState.Incomplete, view.ScenarioScheduleImpact!.State);
        Assert.Null(view.ScenarioScheduleImpact.ModeledFinishDeltaTicks);

        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1233)), 100));
        Assert.Null(view.ScenarioScheduleImpact);
        Assert.Same(f.Baseline, view.ScenarioBaselineSimulation);
        Assert.True(view.ReadScenarioScheduleImpact());
        Assert.Equal(3, view.ScenarioScheduleImpact!.ModeledFinishDeltaTicks);

        Assert.True(view.LoadSyntheticScopedSources());
        Assert.Null(view.ScenarioScheduleImpact);
        Assert.False(view.ReadScenarioScheduleImpact());
        view.Reset();
        Assert.Null(view.ScenarioBaselineSimulation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScenarioScheduleImpactKeepsOnlyTheLatestReentrantRead(bool afterPublication)
    {
        var f = await ReadyScenarioSchedule();
        using var view = f.View;
        ProjectScenarioElapsedScheduleImpact? newer = null;
        var entered = false;
        view.PropertyChanged += (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || (subject.ScenarioScheduleImpact is not null) != afterPublication) return;
            entered = true;
            Assert.True(subject.ReadScenarioScheduleImpact());
            newer = subject.ScenarioScheduleImpact;
        };
        Assert.False(view.ReadScenarioScheduleImpact());
        Assert.True(entered);
        Assert.NotNull(newer);
        Assert.Same(newer, view.ScenarioScheduleImpact);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScenarioScheduleImpactCancellationWithdrawsOnlyTheCancelledPublication(bool replace)
    {
        var f = await ReadyScenarioSchedule();
        using var view = f.View;
        using var source = new CancellationTokenSource();
        var token = source.Token;
        Action cancel = source.Cancel;
        ProjectScenarioElapsedScheduleImpact? newer = null;
        var entered = false;
        var hypothesis = view.SimulationRun;
        view.PropertyChanged += (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || subject.ScenarioScheduleImpact is null) return;
            entered = true;
            if (replace)
            {
                Assert.True(subject.ReadScenarioScheduleImpact());
                newer = subject.ScenarioScheduleImpact;
            }
            cancel();
        };
        Assert.Throws<OperationCanceledException>(() => view.ReadScenarioScheduleImpact(token));
        Assert.True(entered);
        if (replace) Assert.NotNull(newer);
        Assert.Same(newer, view.ScenarioScheduleImpact);
        Assert.Same(hypothesis, view.SimulationRun);
        Assert.Same(f.Baseline, view.ScenarioBaselineSimulation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScenarioScheduleImpactObserverFailurePreservesCancellationOwnership(bool replace)
    {
        var f = await ReadyScenarioSchedule();
        using var view = f.View;
        using var source = new CancellationTokenSource();
        var token = source.Token;
        Action cancel = source.Cancel;
        var error = new InvalidOperationException("Schedule observer failed.");
        var entered = false;
        ProjectScenarioElapsedScheduleImpact? newer = null;
        view.PropertyChanged += (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || subject.ScenarioScheduleImpact is null) return;
            entered = true;
            if (replace)
            {
                Assert.True(subject.ReadScenarioScheduleImpact());
                newer = subject.ScenarioScheduleImpact;
            }
            cancel();
            throw error;
        };
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => view.ReadScenarioScheduleImpact(token)));
        Assert.True(entered);
        if (replace) Assert.NotNull(newer);
        Assert.Same(newer, view.ScenarioScheduleImpact);
        Assert.Same(f.Baseline, view.ScenarioBaselineSimulation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScenarioScheduleImpactNotifiesLaterObserversWhenWithdrawalThrows(bool throwOnPublication)
    {
        var f = await ReadyScenarioSchedule();
        using var view = f.View;
        using var source = new CancellationTokenSource();
        var token = source.Token;
        Action cancel = source.Cancel;
        var publicationError = new InvalidOperationException("Schedule publication observer failed.");
        var withdrawalError = new InvalidOperationException("Schedule withdrawal observer failed.");
        var published = false;
        var laterWithdrawals = 0;
        PropertyChangedEventHandler firstObserver = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (published)
            {
                if (subject.ScenarioScheduleImpact is null) throw withdrawalError;
                return;
            }
            if (subject.ScenarioScheduleImpact is null) return;
            published = true;
            cancel();
            if (throwOnPublication) throw publicationError;
        };
        PropertyChangedEventHandler laterObserver = (sender, _) =>
        {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (published && subject.ScenarioScheduleImpact is null) laterWithdrawals++;
        };

        view.PropertyChanged += firstObserver;
        view.PropertyChanged += laterObserver;
        Exception? observed;
        try { observed = Record.Exception(() => { view.ReadScenarioScheduleImpact(token); }); }
        finally
        {
            view.PropertyChanged -= firstObserver;
            view.PropertyChanged -= laterObserver;
        }
        if (throwOnPublication)
            Assert.Same(publicationError, observed);
        else
        {
            var cancelled = Assert.IsType<OperationCanceledException>(observed);
            Assert.Equal(token, cancelled.CancellationToken);
            Assert.Same(withdrawalError, cancelled.InnerException);
        }
        Assert.True(published);
        Assert.Null(view.ScenarioScheduleImpact);
        Assert.Equal(1, laterWithdrawals);
    }

    private static async Task<(SyntheticWorkbenchViewModel View, ProjectScenarioPlan Scenario, ProjectSimulationRun? Baseline)>
        ReadyScenarioSchedule(bool captureBaseline = true, SimulationRunLimits? limits = null)
    {
        var input = ScenarioScheduleInput();
        var view = new SyntheticWorkbenchViewModel();
        var initial = new CompilerHostRequest(new(input), 1);
        view.BeginSynthetic(initial);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(initial)));
        Assert.True(view.RunSyntheticTests());
        Assert.True(view.LoadSyntheticScopedSources());
        if (captureBaseline) Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1220)), 100));
        var baseline = view.SimulationRun;
        var plan = Assert.IsType<CompilerExecutionPlan>(view.ScopedExecutionPlan);
        var task = plan.Data.Value.GetProperty("tasks").EnumerateArray().Single(row => row.GetProperty("sourceEntityId").GetString() == Id(7));
        var target = ProjectLocation.Parse(SourceJson.Parse(task.GetProperty("location").GetRawText()));
        var duration = JsonNode.Parse(task.GetProperty("duration").GetRawText())!;
        duration["value"] = "14";
        var port = task.GetProperty("ports").EnumerateArray().Single();
        var enabled = JsonNode.Parse(port.GetProperty("defaultValue").GetRawText())!;
        enabled["value"] = true;
        var scenario = Assert.IsType<ProjectScenarioPlan>(view.PreviewScenario(ScenarioId.Parse(Id(1221)), ScenarioRevisionId.Parse(Id(1222)),
            [new(target, ProjectScenarioOverrideKind.TaskDuration, Json(duration)),
                new(target, ProjectScenarioOverrideKind.TaskInputDefault, Json(enabled), PortId.Parse(Id(8)))])?.Plan);
        var request = Assert.IsType<CompilerHostRequest>(view.LoadScenario(scenario, 2));
        Assert.True(view.TryAccept(await Host(scenario.Input).CompileAsync(request)));
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.RunSyntheticTests());
        Assert.True(view.RunSyntheticSimulation(Guid.Parse(Id(1223)), 100, limits));
        return (view, scenario, baseline);
    }

    private static CompilerInputManifest ScenarioScheduleInput()
    {
        var timing = ProjectSimulationTests.Plan().Input;
        var tested = ProjectTestRunnerTests.Fixture(mutate: (port, _, test) =>
        {
            port["defaultValue"]!["value"] = false;
            test["entityId"] = Id(10);
        });
        var declarations = JsonNode.Parse(timing.Source.Declarations.CanonicalText)!.AsArray();
        var testedDeclarations = JsonNode.Parse(tested.Source.Declarations.CanonicalText)!.AsArray();
        var scheduledTask = declarations.Single(node => node!["entityId"]!.GetValue<string>() == Id(7))!;
        var testedTask = testedDeclarations.Single(node => node!["entityId"]!.GetValue<string>() == Id(7))!;
        scheduledTask["payload"]!["ports"] = testedTask["payload"]!["ports"]!.DeepClone();
        declarations.Add(testedDeclarations.Single(node => node!["entityId"]!.GetValue<string>() == Id(10))!.DeepClone());
        var settings = JsonNode.Parse(timing.Settings.CanonicalText)!.AsObject();
        settings["evaluator"] = JsonNode.Parse(SyntheticProjectTestConfiguration.Evaluator.Data.CanonicalText);
        return new(ProjectSource.Create(timing.Source.Context, timing.Source.Documents, Json(declarations), timing.Source.Relations), Json(settings));
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

    private sealed class CancelRecordedRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _replace;
        private readonly ProjectSimulationRun _before;
        private int _notifications;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public ProjectSimulationRunComparison? Replacement { get; private set; }
        public CancelRecordedRead(SyntheticWorkbenchViewModel vm, bool replace, ProjectSimulationRun before)
        { _vm = vm; _replace = replace; _before = before; vm.PropertyChanged += Changed; }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _vm.CompareSyntheticSimulationRuns(_before, _before.Digest, token: Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace)
            {
                ReplacementSucceeded = _vm.CompareSyntheticSimulationRuns(_before, _before.Digest);
                Replacement = _vm.SimulationComparison;
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
        Assert.NotEmpty(run.Events); var beforeData = f.Before.Data; Assert.NotEmpty(f.Before.Events);
        Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest));
        var previous = view.SimulationComparison!; var previousData = previous.Data;
        using var cancellation = new CancelRecordedRead(view, replace, f.Before); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, view.Result); Assert.Equal(resultData, result.Data); Assert.Same(run, view.SimulationRun); Assert.Equal(runData, run.Data);
        Assert.Equal(previousData, previous.Data); Assert.Equal(beforeData, f.Before.Data);
        if (replace)
        {
            Assert.True(cancellation.ReplacementSucceeded); Assert.NotNull(cancellation.Replacement);
            Assert.Same(cancellation.Replacement, view.SimulationComparison);
        }
        else Assert.Null(view.SimulationComparison);
        Assert.IsType<OperationCanceledException>(error);
    }

    private sealed class CancelInitialRead : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly CancellationTokenSource _source = new();
        private readonly bool _admitted;
        private readonly ProjectSimulationRun _before;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public bool ReplacementSucceeded { get; private set; }
        public ProjectSimulationRunComparison? Replacement { get; private set; }
        public CancelInitialRead(SyntheticWorkbenchViewModel view, bool admitted, ProjectSimulationRun before)
        {
            _view = view; _admitted = admitted; _before = before;
            view.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => _view.CompareSyntheticSimulationRuns(_before, _before.Digest, Token);
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || _view.SimulationComparison is not null) return;
            Fired = true;
            ReplacementSucceeded = _view.CompareSyntheticSimulationRuns(_before, _admitted ? _before.Digest : _before.Manifest.Digest);
            Replacement = _view.SimulationComparison;
            _source.Cancel();
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringInitialNotificationReportsCancellationAndPreservesTheNewerComparison(bool admitted)
    {
        var f = await Ready();
        using var view = f.View;
        var run = view.SimulationRun!;
        var result = view.Result!;
        var runData = run.Data;
        var resultData = result.Data;
        var beforeData = f.Before.Data;
        Assert.True(view.CompareSyntheticSimulationRuns(f.Before, f.Before.Digest));
        var previous = view.SimulationComparison!;
        var previousData = previous.Data;
        using var cancellation = new CancelInitialRead(view, admitted, f.Before);
        var error = cancellation.Read();
        Assert.True(cancellation.Fired);
        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.Equal(admitted, cancellation.ReplacementSucceeded);
        var latest = cancellation.Replacement;
        Assert.NotNull(latest);
        Assert.NotSame(previous, latest);
        Assert.Same(latest, view.SimulationComparison);
        Assert.Equal(admitted ? SimulationRunComparisonState.Complete : SimulationRunComparisonState.Refused, latest.State);
        if (admitted)
        {
            Assert.Same(f.Before, latest.Before);
            Assert.Same(run, latest.After);
        }
        else
        {
            Assert.Null(latest.Before);
            Assert.Null(latest.After);
        }
        Assert.Same(run, view.SimulationRun);
        Assert.Same(result, view.Result);
        Assert.Equal(runData, run.Data);
        Assert.Equal(resultData, result.Data);
        Assert.Equal(beforeData, f.Before.Data);
        Assert.Equal(previousData, previous.Data);
        Assert.IsType<OperationCanceledException>(error);
    }

}
