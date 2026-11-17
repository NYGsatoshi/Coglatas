using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebSemanticIndexContractTests
{
    [Theory]
    [InlineData("tests-index-base")]
    [InlineData("tests-index-hypothesis")]
    [InlineData("tests-index-draft")]
    [InlineData("tests-index-partial-base")]
    [InlineData("tests-index-partial-hypothesis")]
    [InlineData("tests-index-partial-draft")]
    public void OriginalCapturedIndexAndEveryQueryPageReplayActualCoreWithoutChangingLegacyFacts(string key)
    {
        var path = Path.Combine(Path.GetDirectoryName(SyntheticOperatorRecordingTests.FixturePath())!, "semantic-index-v1.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("key").GetString() == key);
        new WebPreviewContractTests().BrowserFixturesReproduceExactCoreInputsResultsAndSourceLocations(row.GetRawText());
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(row, input));
        var original = SourceJson.Parse(row.GetProperty("semanticIndex").GetRawText());
        Assert.Equal(original, SyntheticSemanticIndexProjection.Export(compilation));
        Assert.Equal(SourceJson.Parse(row.GetProperty("result").GetRawText()), compilation.Result.Data);
        var frame = original.Value.GetProperty("index"); var data = SourceJson.Parse(frame.GetProperty("text").GetString()!);
        Assert.Equal(SourceJson.Parse(frame.GetProperty("digest").GetRawText()), ContentDigest.Compute(CompilerSemanticIndex.Format, data.ToCanonicalBytes()).Data);
        if (key.Contains("partial", StringComparison.Ordinal))
        {
            Assert.Equal("Incomplete", data.Value.GetProperty("state").GetString());
            Assert.False(data.Value.GetProperty("indexCoverageComplete").GetBoolean());
            Assert.Empty(original.Value.GetProperty("referenceSearches").EnumerateArray());
        }
        else
        {
            Assert.Equal("Complete", data.Value.GetProperty("state").GetString());
            Assert.Equal(3, data.Value.GetProperty("regions").GetArrayLength());
            Assert.Contains(data.Value.GetProperty("references").EnumerateArray(), reference =>
                SourceJson.Parse(reference.GetProperty("origin").GetProperty("context").GetRawText()).CanonicalText != input.Source.Context.Data.CanonicalText);
            Assert.Contains(data.Value.GetProperty("symbols").EnumerateArray(), symbol => symbol.GetProperty("displayName").GetString()?.Contains("日本語", StringComparison.Ordinal) == true);
            Assert.Contains(original.Value.GetProperty("referenceSearches").EnumerateArray(), group =>
                group.GetProperty("pages").GetArrayLength() == 2 && group.GetProperty("pages")[0].GetProperty("referenceIndices").GetArrayLength() == 32 &&
                group.GetProperty("pages")[1].GetProperty("referenceIndices").GetArrayLength() > 0);
        }
    }
}
