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
        view.PropertyChanged += (_, _) =>
        {
            if (entered || afterPublication && view.ScenarioPreview is null) return; entered = true;
            if (mode == "reset") view.Reset();
            if (mode == "dispose") view.Dispose();
            if (mode == "begin") view.BeginSynthetic(new(new(f.Input), 2));
            if (mode == "select") view.Select(f.Change.Target);
            if (mode == "navigate") view.Navigate();
        };
        Assert.Null(Preview(view, f.Change)); Assert.True(entered); Assert.Null(view.ScenarioPreview); Assert.Null(view.ActiveScenario);
    }

    [Fact]
    public async Task ANewerReentrantPreviewIsRetainedWhileTheInterruptedCallerReturnsNothing()
    {
        var f = await Ready(); using var view = f.View; ProjectScenarioPreview? newest = null; var entered = false;
        view.PropertyChanged += (_, _) => { if (entered || view.ScenarioPreview is null) return; entered = true; newest = Preview(view, f.Change, 702); };
        Assert.Null(Preview(view, f.Change)); Assert.NotNull(newest); Assert.Same(newest, view.ScenarioPreview);
        Assert.Equal(ScenarioRevisionId.Parse(Id(702)), newest.Plan!.Input.Source.Context.Scenario!.ScenarioRevisionId);
    }

    [Theory]
    [InlineData("reset")] [InlineData("begin")] [InlineData("dispose")]
    public async Task InterruptedLoadCannotRestoreTheCapturedScenarioSession(string mode)
    {
        var f = await Ready(); using var view = f.View; var plan = Preview(view, f.Change).Plan!; var entered = false;
        view.PropertyChanged += (_, _) =>
        {
            if (entered || view.State != "Waiting") return; entered = true;
            if (mode == "reset") view.Reset();
            if (mode == "dispose") view.Dispose();
            if (mode == "begin") view.BeginSynthetic(new(new(f.Input), 3));
        };
        Assert.Null(view.LoadScenario(plan, 2)); Assert.True(entered); Assert.Null(view.ActiveScenario); Assert.Null(view.ScenarioBaselineResult);
    }

    [Fact]
    public async Task NewSelectionAndMissingOrStaleBasePreventDraftApplication()
    {
        var f = await Ready(); using var view = f.View; var plan = Preview(view, f.Change).Plan!;
        Assert.True(view.Select(f.Change.Target)); Assert.Null(view.LoadScenario(plan, 2));
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
        var input = new CompilerInputManifest(f.Input.Source, Json(settings)); using var view = new SyntheticWorkbenchViewModel();
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

}
