using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebSemanticIndexContractTests
{
    [Fact]
    public void OriginalCapturedIndexRetainsScopedTransportRefusalAndActualBoundedPages()
    {
        var path = Path.Combine(Path.GetDirectoryName(SyntheticOperatorRecordingTests.FixturePath())!, "semantic-index-large-v1.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        var rows = fixture.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(2, rows.Length);
        foreach (var row in rows)
        {
            new WebPreviewContractTests().BrowserFixturesReproduceExactCoreInputsResultsAndSourceLocations(row.GetRawText());
            var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
            var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
            var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(row, input));
            var original = SourceJson.Parse(row.GetProperty("semanticIndex").GetRawText());
            Assert.Equal(original, SyntheticSemanticIndexProjection.Export(compilation));
            var data = SourceJson.Parse(original.Value.GetProperty("index").GetProperty("text").GetString()!);
            Assert.True(data.Value.GetProperty("indexCoverageComplete").GetBoolean());
            Assert.Equal("Complete", data.Value.GetProperty("state").GetString());
            var symbols = data.Value.GetProperty("symbols").GetArrayLength();
            var references = data.Value.GetProperty("references").GetArrayLength();
            Assert.True(references > 256); Assert.True(original.ToCanonicalBytes().Length < 4194304);
            Assert.Equal(symbols, original.Value.GetProperty("referenceSearches").GetArrayLength());
            if (row.GetProperty("key").GetString() == "tests-index-large-base")
            {
                Assert.True((long)symbols * references > 1000000);
                var refusal = Assert.Throws<FormatException>(() => SyntheticScopedSourceProjection.Export(compilation));
                Assert.Contains("byte", refusal.Message, StringComparison.Ordinal);
                Assert.False(row.TryGetProperty("scopedSources", out _));
            }
            else
            {
                Assert.Equal("tests-index-bounded-base", row.GetProperty("key").GetString());
                Assert.Equal(SourceJson.Parse(row.GetProperty("scopedSources").GetRawText()), SyntheticScopedSourceProjection.Export(compilation));
                Assert.Contains(original.Value.GetProperty("referenceSearches").EnumerateArray(), group =>
                    group.GetProperty("pages").EnumerateArray().Any(page => !page.GetProperty("scanComplete").GetBoolean() &&
                        page.GetProperty("referenceIndices").GetArrayLength() == 0 && page.GetProperty("scannedReferences").GetInt32() == 256));
            }
        }
    }

    [Theory]
    [InlineData("tests-index-base")]
    [InlineData("tests-index-hypothesis")]
    [InlineData("tests-index-draft")]
    public void NarrowProjectionBoundOmitsOnlyIndexAndPreservesOriginalCompilerFacts(string key)
    {
        var path = Path.Combine(Path.GetDirectoryName(SyntheticOperatorRecordingTests.FixturePath())!, "semantic-index-v1.json");
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("key").GetString() == key);
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(row, input));
        var originalResult = compilation.Result.Data;
        var originalInput = compilation.Input;
        var originalBinding = compilation.Dependencies!.Binding;
        var original = SourceJson.Parse(row.GetProperty("semanticIndex").GetRawText());
        Assert.Equal(original, SyntheticSemanticIndexProjection.Export(compilation));
        Assert.Null(SyntheticSemanticIndexProjection.Export(compilation, maximumProjectionBytes: 1024));
        Assert.Same(originalResult, compilation.Result.Data);
        Assert.Same(originalInput, compilation.Input);
        Assert.Same(originalBinding, compilation.Dependencies.Binding);
        Assert.Equal(original, SyntheticSemanticIndexProjection.Export(compilation));
    }

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
