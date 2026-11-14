using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticProjectTestEditingTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
    }
    private static async Task<(SyntheticWorkbenchViewModel View, ProjectLocation Target, SourceJson Definition, SourceRevisionContext Next)> Ready()
    {
        using var data = JsonDocument.Parse(WebPreviewContractTests.Cases().Select(row => (string)row[0])
            .Single(text => JsonSerializer.Deserialize<JsonElement>(text).GetProperty("key").GetString() == "tests-fail"));
        var root = data.RootElement; var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(root.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(root.GetProperty("manifest").GetRawText()));
        var basis = source.Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.Parse(Id(420)), CandidateRevisionId.Parse(Id(421)), basis, basis);
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(input), proposal);
        var request = new CompilerHostRequest(new(draft), 1); var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(draft).CompileAsync(request)));
        var target = ProjectLocation.Create(source.Documents[0].DocumentId, draft.Source.Context, entityId: EntityId.Parse(Id(10)));
        var export = view.ExportTestDefinition(target)!; Assert.Equal(ProjectTestEditState.Ready, export.State);
        var definition = JsonNode.Parse(export.Definition!.CanonicalText)!.AsObject(); definition["payload"]!["assertions"]![0]!["expected"]!["value"] = false;
        return (view, target, Json(definition), SourceRevisionContext.Candidate(new(proposal.ProposalId, CandidateRevisionId.Parse(Id(422)), basis, basis)));
    }
    [Fact]
    public async Task ApplyingOnlyTheCurrentPreviewClearsOldCompilationAndTestsAndRequestsAFreshCompile()
    {
        var f = await Ready(); using var view = f.View; Assert.True(view.RunSyntheticTests()); var oldRun = view.TestRun;
        Assert.Equal(CompilerVerdict.Fail, oldRun!.Verdict);
        var preview = view.PreviewTestDefinition(f.Target, f.Definition, f.Next)!; Assert.Equal(ProjectTestEditState.Ready, preview.State);
        Assert.Same(oldRun, view.TestRun); Assert.Null(view.ApplyPreviewedTestDefinition(preview.Plan!, 1));
        var request = view.ApplyPreviewedTestDefinition(preview.Plan!, 2)!; Assert.NotNull(request);
        Assert.Equal("Waiting", view.State); Assert.Null(view.TestRun); Assert.Null(view.Result); Assert.Null(view.TestEditPreview); Assert.Null(view.SourceTarget);
        Assert.Null(view.ApplyPreviewedTestDefinition(preview.Plan!, 3));
        Assert.True(view.TryAccept(await Host(request.Input.Materialize()).CompileAsync(request)));
        Assert.True(view.RunSyntheticTests()); Assert.Equal(CompilerVerdict.Pass, view.TestRun!.Verdict);
    }
    [Fact]
    public async Task ANewSelectionInvalidatesThePreparedDefinitionPlan()
    {
        var f = await Ready(); using var view = f.View; var preview = view.PreviewTestDefinition(f.Target, f.Definition, f.Next)!;
        Assert.True(view.Select(f.Target)); Assert.Null(view.TestEditPreview); Assert.Null(view.ApplyPreviewedTestDefinition(preview.Plan!, 2));
    }
    [Theory]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    [InlineData("dispose", false)]
    [InlineData("dispose", true)]
    [InlineData("begin", false)]
    [InlineData("begin", true)]
    [InlineData("select", false)]
    [InlineData("select", true)]
    public async Task NotificationReentryCannotRetainOrReturnAnInterruptedDefinitionPreview(string mode, bool existing)
    {
        var f = await Ready(); var view = f.View;
        try
        {
            if (existing) Assert.NotNull(view.PreviewTestDefinition(f.Target, f.Definition, f.Next));
            var entered = false; var input = view.Result!.Input;
            view.PropertyChanged += (sender, _) =>
            {
                if (entered) return; entered = true; var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
                if (mode == "reset") current.Reset();
                if (mode == "dispose") current.Dispose();
                if (mode == "begin") current.BeginSynthetic(new(new(input), 2));
                if (mode == "select") current.Select(f.Target);
            };
            Assert.Null(view.PreviewTestDefinition(f.Target, f.Definition, f.Next)); Assert.True(entered); Assert.Null(view.TestEditPreview);
        }
        finally { view.Dispose(); }
    }
}
