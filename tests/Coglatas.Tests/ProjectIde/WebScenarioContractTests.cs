using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebScenarioContractTests
{
    private static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "scenario-v1.json"))) directory = directory.Parent;
        return directory is null ? throw new InvalidOperationException("Scenario Product fixture is required.") :
            Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "scenario-v1.json");
    }
    public static IEnumerable<object[]> Cases()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var cases = data.RootElement.GetProperty("cases"); Assert.Equal(15, cases.GetArrayLength());
        foreach (var item in cases.EnumerateArray()) yield return [item.GetRawText()];
    }
    private static SourceJson Json(JsonElement value) => SourceJson.Parse(value.GetRawText());
    private static CompilerInputManifest Input(JsonElement value)
    {
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(value.GetProperty("sourceText").GetString()!)).RequireSource();
        return CompilerInputManifest.ReadEnvelope(source, Json(value.GetProperty("manifest")));
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryScenarioBrowserCaseReplaysExactCompilerTestAndSourceBytes(string fixture)
    {
        new WebPreviewContractTests().BrowserFixturesReproduceExactCoreInputsResultsAndSourceLocations(fixture);
        using var document = JsonDocument.Parse(fixture); var item = document.RootElement; var input = Input(item);
        var projection = item.GetProperty("localProjectTests");
        Assert.Equal(input.Data.CanonicalText, projection.GetProperty("inputText").GetString());
        var plan = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input, WebPreviewContractTests.Snapshots(item, input)), SyntheticProjectTestConfiguration.Evaluator);
        var run = plan.Execute(input.InputDigest);
        Assert.Equal(plan.Data.CanonicalText, projection.GetProperty("plan").GetProperty("text").GetString());
        Assert.Equal(plan.Digest.Data, Json(projection.GetProperty("plan").GetProperty("digest")));
        Assert.Equal(run.Data.CanonicalText, projection.GetProperty("result").GetProperty("text").GetString());
        Assert.Equal(run.Digest.Data, Json(projection.GetProperty("result").GetProperty("digest")));
        var key = item.GetProperty("key").GetString()!;
        if (key.StartsWith("tests-missing-reference", StringComparison.Ordinal) || key.StartsWith("tests-denied-reference", StringComparison.Ordinal)) Assert.NotEqual(CompilerVerdict.Pass, run.Verdict);
        else Assert.Equal(key.EndsWith("-base", StringComparison.Ordinal) ? CompilerVerdict.Fail : CompilerVerdict.Pass, run.Verdict);
        Assert.Equal(CompilerVerdict.NotRun, plan.Execute(ContentDigest.Compute("example.foreign-input", [])).Verdict);
        var tests = PlanDeclarationReader.Read(input.Source).Where(x => x.Declaration is ProjectTestPlanDeclaration).ToArray();
        Assert.Equal(tests.Select(x => x.Data.CanonicalText), projection.GetProperty("declarations").EnumerateArray().Select(x => x.GetProperty("text").GetString()));
        Assert.Equal(tests.SelectMany(x => ((ProjectTestPlanDeclaration)x.Declaration!).Assertions).Select(x => x.Data.CanonicalText),
            projection.GetProperty("assertions").EnumerateArray().Select(x => x.GetProperty("text").GetString()));
    }
    [Fact]
    public void BrowserOverlayAndConversionReconstructTheExactScenarioDraftAndBothSourceComparisons()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath())); var rows = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        foreach (var group in rows.Chunk(3))
        {
        var basis = Input(group[0]); var scenario = Input(group[1]); var draft = Input(group[2]);
        var metadata = group[0].GetProperty("preparedScenario"); var overlay = SourceJson.Parse(metadata.GetProperty("overlay").GetProperty("text").GetString()!);
        var changes = overlay.Value.GetProperty("overrides").EnumerateArray().Select(x => new ProjectScenarioOverride(
            ProjectLocation.Parse(Json(x.GetProperty("target"))), Enum.Parse<ProjectScenarioOverrideKind>(x.GetProperty("kind").GetString()!),
            Json(x.GetProperty("value")), x.GetProperty("portId").ValueKind == JsonValueKind.Null ? null : PortId.Parse(x.GetProperty("portId").GetString()!))).ToArray();
        var snapshots = WebPreviewContractTests.Snapshots(group[0], basis);
        var identity = scenario.Source.Context.Scenario!; var model = ProjectSemanticBinder.Bind(basis, snapshots);
        var plan = ProjectScenarios.Prepare(model, basis.InputDigest, identity.ScenarioId, identity.ScenarioRevisionId, changes).Plan!;
        Assert.NotNull(plan); Assert.Equal(overlay, plan.Overlay); Assert.Equal(identity.OverlayDigest, plan.OverlayDigest);
        Assert.Equal(scenario.Data, plan.Input.Data); Assert.Equal(scenario.Source.Data, plan.Materialize(basis)!.Source.Data);
        var conversion = ProjectScenarios.ToDraft(plan, model, basis.InputDigest, draft.Source.Context.Proposal!).Plan!;
        Assert.NotNull(conversion); Assert.Equal(draft.Data, conversion.After.Data); Assert.Equal(draft.Source.Data, conversion.ApplyToDraft(basis)!.Source.Data);
        Assert.Equal(conversion.Data, Json(metadata.GetProperty("draftPlan")));
        foreach (var (key, projection) in new[] { ("scenario", ProjectScenarioAppliedValues.Read(plan, scenario).Projection!),
            ("draft", ProjectScenarioAppliedValues.Read(conversion, draft).Projection!) })
        {
            var value = metadata.GetProperty("appliedValues").GetProperty(key);
            Assert.Equal(projection.Data.CanonicalText, value.GetProperty("text").GetString());
            Assert.Equal(projection.Digest.Data, Json(value.GetProperty("digest")));
        }
        var first = ProjectSemanticDiff.Compare(model, ProjectSemanticBinder.Bind(scenario, snapshots), basis.InputDigest, scenario.InputDigest);
        var second = ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(scenario, snapshots), ProjectSemanticBinder.Bind(draft, snapshots), scenario.InputDigest, draft.InputDigest);
        Assert.Equal(first.Data, Json(group[0].GetProperty("preparedDiff").GetProperty("comparison")));
        Assert.Equal(second.Data, Json(group[1].GetProperty("preparedDiff").GetProperty("comparison")));
        Assert.False(first.NoChanges);
        if (model.BindingComplete) { Assert.Equal(SemanticDiffState.Complete, first.State); Assert.True(second.NoChanges); }
        else { Assert.Equal(SemanticDiffState.Incomplete, first.State); Assert.Equal(SemanticDiffState.Incomplete, second.State); Assert.False(second.NoChanges); }
        }
    }
}
