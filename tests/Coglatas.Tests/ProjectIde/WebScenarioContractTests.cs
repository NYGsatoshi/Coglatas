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
        var cases = data.RootElement.GetProperty("cases"); Assert.Equal(3, cases.GetArrayLength());
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
        var plan = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), SyntheticProjectTestConfiguration.Evaluator);
        var run = plan.Execute(input.InputDigest);
        Assert.Equal(plan.Data.CanonicalText, projection.GetProperty("plan").GetProperty("text").GetString());
        Assert.Equal(plan.Digest.Data, Json(projection.GetProperty("plan").GetProperty("digest")));
        Assert.Equal(run.Data.CanonicalText, projection.GetProperty("result").GetProperty("text").GetString());
        Assert.Equal(run.Digest.Data, Json(projection.GetProperty("result").GetProperty("digest")));
        Assert.Equal(item.GetProperty("key").GetString() == "tests-scenario-base" ? CompilerVerdict.Fail : CompilerVerdict.Pass, run.Verdict);
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
        var basis = Input(rows[0]); var scenario = Input(rows[1]); var draft = Input(rows[2]);
        var metadata = rows[0].GetProperty("preparedScenario"); var overlay = SourceJson.Parse(metadata.GetProperty("overlay").GetProperty("text").GetString()!);
        var changes = overlay.Value.GetProperty("overrides").EnumerateArray().Select(x => new ProjectScenarioOverride(
            ProjectLocation.Parse(Json(x.GetProperty("target"))), Enum.Parse<ProjectScenarioOverrideKind>(x.GetProperty("kind").GetString()!),
            Json(x.GetProperty("value")), x.GetProperty("portId").ValueKind == JsonValueKind.Null ? null : PortId.Parse(x.GetProperty("portId").GetString()!))).ToArray();
        var identity = scenario.Source.Context.Scenario!; var model = ProjectSemanticBinder.Bind(basis);
        var plan = ProjectScenarios.Prepare(model, basis.InputDigest, identity.ScenarioId, identity.ScenarioRevisionId, changes).Plan!;
        Assert.NotNull(plan); Assert.Equal(overlay, plan.Overlay); Assert.Equal(identity.OverlayDigest, plan.OverlayDigest);
        Assert.Equal(scenario.Data, plan.Input.Data); Assert.Equal(scenario.Source.Data, plan.Materialize(basis)!.Source.Data);
        var conversion = ProjectScenarios.ToDraft(plan, model, basis.InputDigest, draft.Source.Context.Proposal!).Plan!;
        Assert.NotNull(conversion); Assert.Equal(draft.Data, conversion.After.Data); Assert.Equal(draft.Source.Data, conversion.ApplyToDraft(basis)!.Source.Data);
        Assert.Equal(conversion.Data, Json(metadata.GetProperty("draftPlan")));
        var first = ProjectSemanticDiff.Compare(model, ProjectSemanticBinder.Bind(scenario), basis.InputDigest, scenario.InputDigest);
        var second = ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(scenario), ProjectSemanticBinder.Bind(draft), scenario.InputDigest, draft.InputDigest);
        Assert.Equal(first.Data, Json(rows[0].GetProperty("preparedDiff").GetProperty("comparison")));
        Assert.Equal(second.Data, Json(rows[1].GetProperty("preparedDiff").GetProperty("comparison")));
        Assert.False(first.NoChanges); Assert.True(second.NoChanges);
    }
}
