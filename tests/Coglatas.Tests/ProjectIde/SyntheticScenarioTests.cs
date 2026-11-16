using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticScenarioTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }
    private static async Task<(SyntheticWorkbenchViewModel View, CompilerInputManifest Input, ProjectScenarioOverride Change)> Ready()
    {
        using var data = JsonDocument.Parse(WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(text => JsonSerializer.Deserialize<JsonElement>(text).GetProperty("key").GetString() == "tests-fail"));
        var root = data.RootElement; var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(root.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(root.GetProperty("manifest").GetRawText()));
        var task = PlanDeclarationReader.Read(source).Select(x => x.Declaration).OfType<TaskPlanDeclaration>().Single();
        var port = task.Ports.Single(x => x.Type.Kind == "coglatas.boolean");
        var value = JsonNode.Parse(port.DefaultValue!.Data.CanonicalText)!.AsObject(); value["value"] = true;
        var change = new ProjectScenarioOverride(ProjectLocation.Create(task.DocumentId, source.Context, entityId: task.EntityId),
            ProjectScenarioOverrideKind.TaskInputDefault, Json(value), port.PortId);
        var request = new CompilerHostRequest(new(input), 1); var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(request))); return (view, input, change);
    }
    private static ProjectScenarioPreview Preview(SyntheticWorkbenchViewModel view, ProjectScenarioOverride change, int revision = 701) =>
        view.PreviewScenario(ScenarioId.Parse(Id(700)), ScenarioRevisionId.Parse(Id(revision)), [change])!;

    [Fact]
    public async Task ScenarioSeparatesBaselineAssumptionsSourceDiffAndFreshTestResultsBeforeNewDraft()
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.RunSyntheticTests());
        var beforeResult = view.Result; var beforeRun = view.TestRun; Assert.Equal(CompilerVerdict.Fail, beforeRun!.Verdict);
        var preview = Preview(view, f.Change); Assert.Equal(ProjectScenarioState.Ready, preview.State); var plan = preview.Plan!;
        Assert.Null(view.LoadScenario(plan, 1)); var request = view.LoadScenario(plan, 2)!;
        Assert.Equal("Waiting", view.State); Assert.Null(view.TestRun); Assert.Null(view.Result); Assert.Null(view.ScenarioPreview); Assert.Null(view.ActiveScenario);
        Assert.True(view.TryAccept(await Host(plan.Input).CompileAsync(request))); Assert.Same(plan, view.ActiveScenario);
        Assert.Same(beforeResult, view.ScenarioBaselineResult); Assert.Same(beforeRun, view.ScenarioBaselineTests);
        Assert.True(view.RunSyntheticTests()); Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
        Assert.NotEqual(beforeRun.Input.InputId, view.TestRun.Input.InputId); Assert.Single(plan.Changes);
        var difference = view.CompareSynthetic(ProjectSemanticBinder.Bind(f.Input), f.Input.InputDigest)!;
        Assert.False(difference.NoChanges); Assert.NotEmpty(difference.Changes); Assert.Equal(f.Input.InputDigest, difference.BeforeInputDigest);
        var basis = f.Input.Source.Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.Parse(Id(710)), CandidateRevisionId.Parse(Id(711)), basis, basis);
        var conversion = view.PreviewScenarioDraft(ProjectSemanticBinder.Bind(f.Input), f.Input.InputDigest, proposal)!;
        Assert.Equal(ProjectScenarioState.Ready, conversion.State); Assert.Null(view.ApplyScenarioDraft(conversion.Plan!, plan.Input, 3));
        Assert.Null(view.ApplyScenarioDraft(conversion.Plan!, f.Input, 2));
        var draft = view.ApplyScenarioDraft(conversion.Plan!, f.Input, 3)!;
        Assert.Equal("Waiting", view.State); Assert.Null(view.ActiveScenario); Assert.Null(view.ScenarioBaselineTests); Assert.Null(view.TestRun);
        Assert.True(view.TryAccept(await Host(draft.Input.Materialize()).CompileAsync(draft))); Assert.True(view.RunSyntheticTests());
        Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict); Assert.Null(view.Source!.Context.Scenario);
        Assert.Equal(f.Input.Source.Data, plan.Basis.Source.Data);
    }

    [Theory]
    [InlineData("reset", false)] [InlineData("reset", true)]
    [InlineData("dispose", false)] [InlineData("dispose", true)]
    [InlineData("begin", false)] [InlineData("begin", true)]
    [InlineData("select", false)] [InlineData("select", true)]
    [InlineData("navigate", false)] [InlineData("navigate", true)]
    public async Task PreviewPublicationCannotSurviveAnInterruptedLifecycleOrSelection(string mode, bool afterPublication)
    {
        var f = await Ready(); using var view = f.View; var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || afterPublication && subject.ScenarioPreview is null) return; entered = true;
            if (mode == "reset") subject.Reset();
            if (mode == "dispose") subject.Dispose();
            if (mode == "begin") subject.BeginSynthetic(new(new(f.Input), 2));
            if (mode == "select") subject.Select(Assert.IsType<ProjectLocation>(f.Change.Target));
            if (mode == "navigate") subject.Navigate();
        };
        Assert.Null(Preview(view, f.Change)); Assert.True(entered); Assert.Null(view.ScenarioPreview); Assert.Null(view.ActiveScenario);
    }

    [Fact]
    public async Task ANewerReentrantPreviewIsRetainedWhileTheInterruptedCallerReturnsNothing()
    {
        var f = await Ready(); using var view = f.View; ProjectScenarioPreview? newest = null; var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender); if (entered || subject.ScenarioPreview is null) return; entered = true; newest = Preview(subject, f.Change, 702); };
        Assert.Null(Preview(view, f.Change)); Assert.NotNull(newest); Assert.Same(newest, view.ScenarioPreview);
        Assert.Equal(ScenarioRevisionId.Parse(Id(702)), newest.Plan!.Input.Source.Context.Scenario!.ScenarioRevisionId);
    }

    [Theory]
    [InlineData("reset")] [InlineData("begin")] [InlineData("dispose")]
    public async Task InterruptedLoadCannotRestoreTheCapturedScenarioSession(string mode)
    {
        var f = await Ready(); using var view = f.View; var plan = Preview(view, f.Change).Plan!; var entered = false;
        view.PropertyChanged += (sender, _) => {
            var subject = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (entered || subject.State != "Waiting") return; entered = true;
            if (mode == "reset") subject.Reset();
            if (mode == "dispose") subject.Dispose();
            if (mode == "begin") subject.BeginSynthetic(new(new(f.Input), 3));
        };
        Assert.Null(view.LoadScenario(plan, 2)); Assert.True(entered); Assert.Null(view.ActiveScenario); Assert.Null(view.ScenarioBaselineResult);
    }

    [Fact]
    public async Task NewSelectionAndMissingOrStaleBasePreventDraftApplication()
    {
        var f = await Ready(); using var view = f.View; var plan = Preview(view, f.Change).Plan!;
        Assert.True(view.Select(Assert.IsType<ProjectLocation>(f.Change.Target))); Assert.Null(view.LoadScenario(plan, 2));
        plan = Preview(view, f.Change).Plan!; var request = view.LoadScenario(plan, 2)!; Assert.True(view.TryAccept(await Host(plan.Input).CompileAsync(request)));
        var basis = f.Input.Source.Context.CommittedRevision!; var proposal = new ProposalContext(ProposalId.Parse(Id(710)), CandidateRevisionId.Parse(Id(711)), basis, basis);
        var stale = view.PreviewScenarioDraft(ProjectSemanticBinder.Bind(plan.Input), plan.Input.InputDigest, proposal)!;
        Assert.Equal(ProjectScenarioState.StaleInput, stale.State); Assert.Null(stale.Plan);
        var current = view.PreviewScenarioDraft(ProjectSemanticBinder.Bind(f.Input), f.Input.InputDigest, proposal)!;
        view.Reset(); Assert.Null(view.ApplyScenarioDraft(current.Plan!, f.Input, 3)); Assert.Null(view.ActiveScenario);
    }
    [Fact]
    public async Task PinnedReferenceScenarioAndDraftUseActualHostSnapshotsAndClearOldResultsOnDenial()
    {
        var f = ScenarioReferenceRebaseTests.Fixture();
        var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject(); settings["evaluator"] = JsonNode.Parse(SyntheticProjectTestConfiguration.Evaluator.Data.CanonicalText);
        var input = new CompilerInputManifest(f.Input.Source, Json(settings));
        using var view = new SyntheticWorkbenchViewModel();

        var initial = new CompilerHostRequest(new(input), 1, [f.Snapshot]); view.BeginSynthetic(initial);
        Assert.True(view.TryAccept(await Host(input).CompileAsync(initial))); Assert.True(view.RunSyntheticTests()); Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
        var baseline = view.TestRun; var preview = Preview(view, f.Change); var plan = preview.Plan!;
        var request = view.LoadScenario(plan, 2)!; Assert.Null(view.TestRun); Assert.Same(f.Snapshot, request.Snapshots[0]);
        Assert.True(view.TryAccept(await Host(plan.Input).CompileAsync(request))); Assert.True(view.RunSyntheticTests()); Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
        Assert.NotEqual(baseline.Input.InputId, view.TestRun.Input.InputId);
        var revision = input.Source.Context.CommittedRevision!;
        var conversion = view.PreviewScenarioDraft(ProjectSemanticBinder.Bind(input, [f.Snapshot]), input.InputDigest,
            new(ProposalId.Parse(Id(710)), CandidateRevisionId.Parse(Id(711)), revision, revision))!;
        var draft = view.ApplyScenarioDraft(conversion.Plan!, input, 3)!; Assert.Null(view.TestRun);
        Assert.True(view.TryAccept(await Host(conversion.Plan!.After).CompileAsync(draft))); Assert.True(view.RunSyntheticTests()); Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
        var denied = new CompilerMaterializedSnapshot(f.Snapshot.SnapshotId, f.Snapshot.Version, new(f.Snapshot.Snapshot.Source, false, true, true));
        var revoked = new CompilerHostRequest(new(conversion.Plan.After), 4, [denied]); view.BeginSynthetic(revoked); Assert.Null(view.TestRun); Assert.Null(view.ActiveScenario);
        Assert.True(view.TryAccept(await Host(conversion.Plan.After).CompileAsync(revoked))); Assert.True(view.RunSyntheticTests()); Assert.NotEqual(CompilerVerdict.Pass, view.TestRun!.Verdict);
    }


    private sealed class CancelScenarioPreview : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _vm;
        private readonly CompilerInputManifest _input;
        private readonly ProjectScenarioOverride _change;
        private readonly PartialSemanticModel _basis;
        private readonly bool _draft;
        private readonly bool _replace;
        private readonly CancellationTokenSource _source = new();
        private int _notifications;
        public CancellationToken Token => _source.Token;
        public bool Fired { get; private set; }
        public object? Replacement { get; private set; }
        public CancelScenarioPreview(SyntheticWorkbenchViewModel vm, CompilerInputManifest input,
            ProjectScenarioOverride change, bool draft, bool replace)
        {
            _vm = vm; _input = input; _change = change; _basis = ProjectSemanticBinder.Bind(input);
            _draft = draft; _replace = replace; vm.PropertyChanged += Changed;
        }
        public Exception? Read() => Record.Exception(ReadCore);
        private void ReadCore() => ReadPreview(704, Token);
        private object? ReadPreview(int revision, CancellationToken token = default)
        {
            if (!_draft) return _vm.PreviewScenario(ScenarioId.Parse(Id(700)), ScenarioRevisionId.Parse(Id(revision)), [_change], token);
            var committed = _input.Source.Context.CommittedRevision!;
            var proposal = new ProposalContext(ProposalId.Parse(Id(710)), CandidateRevisionId.Parse(Id(revision)), committed, committed);
            return _vm.PreviewScenarioDraft(_basis, _input.InputDigest, proposal, token);
        }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Fired || ++_notifications != 2) return;
            Fired = true;
            if (_replace) Replacement = ReadPreview(705);
            _source.Cancel();
        }
        public void Dispose() { _vm.PropertyChanged -= Changed; _source.Dispose(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task CancellationAtScenarioPreviewPublicationWithdrawsOnlyItsOwnedPreview(bool draft, bool replace)
    {
        var f = await Ready();
        using var view = f.View;
        Assert.True(view.RunSyntheticTests()); var originalInputData = f.Input.Data;
        if (draft)
        {
            var scenario = Preview(view, f.Change).Plan!;
            var request = view.LoadScenario(scenario, 2)!;
            Assert.True(view.TryAccept(await Host(scenario.Input).CompileAsync(request)));
            Assert.True(view.RunSyntheticTests());
        }
        var result = view.Result!; var resultData = result.Data; var source = view.Source!; var sourceData = source.Data;
        var tests = view.TestRun!; var testsData = tests.Data; var active = view.ActiveScenario;
        var baselineResult = view.ScenarioBaselineResult; var baselineTests = view.ScenarioBaselineTests;
        var committed = f.Input.Source.Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.Parse(Id(710)), CandidateRevisionId.Parse(Id(711)), committed, committed);
        object previous; SourceJson previousData;
        if (draft)
        {
            var prior = view.PreviewScenarioDraft(ProjectSemanticBinder.Bind(f.Input), f.Input.InputDigest, proposal)!;
            Assert.Equal(ProjectScenarioState.Ready, prior.State); Assert.NotNull(prior.Plan);
            previous = prior; previousData = prior.Plan.Data;
        }
        else
        {
            var prior = Preview(view, f.Change); Assert.Equal(ProjectScenarioState.Ready, prior.State); Assert.NotEmpty(prior.Plan!.Changes);
            previous = prior; previousData = prior.Plan.Input.Data;
        }
        using var cancellation = new CancelScenarioPreview(view, f.Input, f.Change, draft, replace); var token = cancellation.Token;
        var error = cancellation.Read();
        Assert.True(cancellation.Fired); Assert.True(token.IsCancellationRequested);
        Assert.Same(result, view.Result); Assert.Equal(resultData, result.Data); Assert.Same(source, view.Source); Assert.Equal(sourceData, source.Data);
        Assert.Same(tests, view.TestRun); Assert.Equal(testsData, tests.Data); Assert.Equal(originalInputData, f.Input.Data);
        Assert.Same(active, view.ActiveScenario); Assert.Same(baselineResult, view.ScenarioBaselineResult); Assert.Same(baselineTests, view.ScenarioBaselineTests);
        Assert.Equal(previousData, draft ? Assert.IsType<ProjectScenarioDraftPreview>(previous).Plan!.Data : Assert.IsType<ProjectScenarioPreview>(previous).Plan!.Input.Data);
        object? current = draft ? view.ScenarioDraftPreview : view.ScenarioPreview;
        if (replace)
        {
            Assert.NotNull(cancellation.Replacement); Assert.Same(cancellation.Replacement, current);
            if (draft) Assert.Equal(ProjectScenarioState.Ready, Assert.IsType<ProjectScenarioDraftPreview>(current).State);
            else Assert.Equal(ProjectScenarioState.Ready, Assert.IsType<ProjectScenarioPreview>(current).State);
        }
        else Assert.Null(current);
        Assert.IsType<OperationCanceledException>(error);
    }
}
