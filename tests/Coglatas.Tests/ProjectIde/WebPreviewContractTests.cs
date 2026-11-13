using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebPreviewContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "preview-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Combined Product fixture is required; this test cannot pass without it.");
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "preview-v1.json")));
        Assert.Equal("synthetic-fixture", data.RootElement.GetProperty("mode").GetString());
        var contexts = data.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("context").GetRawText()).ToArray();
        Assert.Equal(contexts.Length, contexts.Distinct(StringComparer.Ordinal).Count());
        foreach (var item in data.RootElement.GetProperty("cases").EnumerateArray()) yield return [item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void BrowserFixturesReproduceExactCoreInputsResultsAndSourceLocations(string fixture)
    {
        using var parsed = JsonDocument.Parse(fixture); var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        Assert.Equal(source.Data, SourceJson.Parse(item.GetProperty("source").GetRawText()));
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        Assert.Equal(input.InputId.ToString(), item.GetProperty("inputId").GetString());
        Assert.Equal(source.Digest.Data, SourceJson.Parse(item.GetProperty("sourceDigest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler);
        var result = item.GetProperty("key").GetString() switch
        {
            "not-run" => new CompilerResult(input, CompilerExecutionState.NotRun, CompilerCoverage.None, [], [], [], false),
            "cancelled" => new CompilerResult(input, CompilerExecutionState.Cancelled, CompilerCoverage.None, [], [], [], false),
            "timeout" => new CompilerResult(input, CompilerExecutionState.TimedOut, CompilerCoverage.None, [], [], [], false),
            _ => compilation.Result
        };
        Assert.Equal(result.Data, SourceJson.Parse(item.GetProperty("result").GetRawText()));
        Assert.Equal(result.ResultDigest.Data, SourceJson.Parse(item.GetProperty("resultDigest").GetRawText()));
        Assert.Equal(result.ResultId.ToString(), item.GetProperty("resultId").GetString());
        if (result.Execution == CompilerExecutionState.Completed && compilation.Model is { } model)
        {
            var inspection = new CompilerSemanticInspection(model);
            Assert.Equal(inspection.Data, SourceJson.Parse(item.GetProperty("semanticInspection").GetRawText()));
            var pages = item.GetProperty("referenceCompletions").EnumerateArray().ToArray();
            Assert.Equal(inspection.Symbols.Count(symbol => symbol.Symbol.PortId is not null), pages.Length);
            foreach (var page in pages)
            {
                var origin = inspection.Symbols.Single(symbol => symbol.Symbol.Key == page.GetProperty("originKey").GetString());
                var expected = inspection.CompleteEntityReference(origin.Symbol.Location, input.InputDigest, 32);
                Assert.Equal(expected.State.ToString(), page.GetProperty("state").GetString());
                Assert.Equal(expected.ExpectedKind, page.GetProperty("expectedKind").GetString());
                Assert.Equal(expected.CoverageComplete, page.GetProperty("coverageComplete").GetBoolean());
                Assert.Equal(expected.NextCursor is not null, page.GetProperty("truncated").GetBoolean());
                Assert.Equal(expected.InputDigest.Data, SourceJson.Parse(page.GetProperty("inputDigest").GetRawText()));
                Assert.Equal(expected.Candidates.Select(candidate => candidate.Symbol.Symbol.Key), page.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("symbolKey").GetString()));
                Assert.Equal(expected.Candidates.Select(candidate => candidate.DisplayName), page.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("displayName").GetString()));
            }
        }
        else Assert.Equal(JsonValueKind.Null, item.GetProperty("semanticInspection").ValueKind);
        if (item.TryGetProperty("preparedRename", out var prepared) && prepared.ValueKind != JsonValueKind.Null)
        {
            var plan = prepared.GetProperty("plan");
            var preview = ProjectDraftRename.Preview(compilation.Model!, ProjectLocation.Parse(SourceJson.Parse(plan.GetProperty("target").GetRawText())),
                input.InputDigest, plan.GetProperty("newName").GetString()!, SourceRevisionContext.Parse(SourceJson.Parse(plan.GetProperty("afterContext").GetRawText())));
            Assert.Equal(DraftRenameState.Ready, preview.State);
            Assert.Equal(preview.Plan!.Data, SourceJson.Parse(plan.GetRawText()));
            var afterFixture = Cases().Select(row => JsonDocument.Parse((string)row[0])).ToArray();
            try
            {
                var after = afterFixture.Single(value => value.RootElement.GetProperty("key").GetString() == prepared.GetProperty("afterCaseKey").GetString()).RootElement;
                Assert.Equal(preview.Plan.After.Source.Data, SourceJson.Parse(after.GetProperty("source").GetRawText()));
                Assert.Equal(preview.Plan.After.InputDigest.Data, SourceJson.Parse(after.GetProperty("inputDigest").GetRawText()));
            }
            finally { foreach (var value in afterFixture) value.Dispose(); }
        }
        foreach (var record in item.GetProperty("declarations").EnumerateArray())
        {
            var location = ProjectLocation.Parse(SourceJson.Parse(record.GetProperty("location").GetRawText()));
            Assert.Equal(source.Context, location.Context); Assert.Contains(location.DocumentId, source.Manifest.DocumentIds);
            var declaration = PlanDeclarationReader.Read(source).Single(d => d.Data.Value.GetProperty("entityId").GetString() == record.GetProperty("identity").GetString());
            Assert.Equal(declaration.Status.ToString(), record.GetProperty("status").GetString());
            Assert.Equal(declaration.Data, SourceJson.Parse(record.GetProperty("data").GetRawText()));
        }
        if (result.Artifacts.Any(artifact => artifact.Role == "coglatas.source-map") && compilation.Validation?.Ir is { } ir)
        {
            Assert.Equal(ir.SourceMap, SourceJson.Parse(item.GetProperty("sourceMap").GetRawText()));
            Assert.Equal(ir.SourceMapDigest.Data, SourceJson.Parse(item.GetProperty("sourceMapDigest").GetRawText()));
        }
        else Assert.Equal(JsonValueKind.Null, item.GetProperty("sourceMap").ValueKind);
    }
}
