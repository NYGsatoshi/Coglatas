using System.Globalization;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebSimulationComparisonContractTests
{
    private static JsonDocument Bundle()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory); const string name = "simulation-comparison-v1.json";
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", name))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("The actual Core paired-run comparison asset is required.");
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", name)));
    }
    public static IEnumerable<object[]> Cases()
    {
        using var bundle = Bundle(); var cases = bundle.RootElement.GetProperty("cases");
        foreach (var before in cases.EnumerateArray()) {
            if (!before.TryGetProperty("simulationComparison", out var comparison)) continue;
            var after = cases.EnumerateArray().Single(row => row.GetProperty("key").GetString() == comparison.GetProperty("afterCaseKey").GetString());
            yield return [before.GetRawText(), after.GetRawText()];
        }
    }
    private static ProjectSimulationRun ActualRun(JsonElement item)
    {
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(item, input));
        var plan = Assert.IsType<CompilerExecutionPlan>(compilation.Publication?.ExecutionPlan);
        var original = SourceJson.Parse(item.GetProperty("simulationRun").GetProperty("text").GetString()!);
        var manifest = original.Value.GetProperty("manifest"); var limits = manifest.GetProperty("limits");
        var request = new SimulationRunManifest(plan, Guid.Parse(manifest.GetProperty("runId").GetString()!),
            long.Parse(manifest.GetProperty("startTicks").GetString()!, CultureInfo.InvariantCulture),
            new(limits.GetProperty("maximumEvents").GetInt32(), limits.GetProperty("maximumOperations").GetInt32(),
                long.Parse(limits.GetProperty("maximumElapsedTicks").GetString()!, CultureInfo.InvariantCulture), limits.GetProperty("maximumTraceBytes").GetInt32()));
        var run = ProjectSimulation.Execute(request, input.InputDigest); Assert.Equal(original, run.Data);
        Assert.Equal(run.Digest.Data, SourceJson.Parse(item.GetProperty("simulationRun").GetProperty("digest").GetRawText())); return run;
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void PairedRecordingBytesReproduceTheActualCoreComparison(string beforeFixture, string afterFixture)
    {
        using var a = JsonDocument.Parse(beforeFixture); using var b = JsonDocument.Parse(afterFixture);
        var before = ActualRun(a.RootElement); var after = ActualRun(b.RootElement);
        var comparison = ProjectSimulationRunComparison.Compare(before, before.Digest, after, after.Digest);
        var expected = a.RootElement.GetProperty("simulationComparison");
        Assert.Equal(comparison.Data.CanonicalText, expected.GetProperty("text").GetString());
        Assert.Equal(comparison.Digest.Data, SourceJson.Parse(expected.GetProperty("digest").GetRawText()));
        Assert.Equal(b.RootElement.GetProperty("key").GetString(), expected.GetProperty("afterCaseKey").GetString());
        Assert.Equal(a.RootElement.GetProperty("key").GetString()!.Contains("-bounded-", StringComparison.Ordinal)
            ? SimulationRunComparisonState.Incomplete : SimulationRunComparisonState.Complete, comparison.State);
        Assert.NotEqual(before.Manifest.RunId, after.Manifest.RunId); Assert.NotEqual(before.Manifest.Plan.Input.InputDigest, after.Manifest.Plan.Input.InputDigest);
        Assert.Equal("NotRun", comparison.Data.Value.GetProperty("verdict").GetString());
    }
    [Fact]
    public void MissingPlansAndFinalDraftRowsDoNotInventAnotherPair()
    {
        using var bundle = Bundle(); var rows = bundle.RootElement.GetProperty("cases"); Assert.Equal(12, rows.GetArrayLength()); var pairs = 0;
        foreach (var row in rows.EnumerateArray()) {
            var key = row.GetProperty("key").GetString()!; var hasPair = row.TryGetProperty("simulationComparison", out _);
            if (key.StartsWith("tests-simulation-unknown-", StringComparison.Ordinal) || key.EndsWith("-draft", StringComparison.Ordinal)) Assert.False(hasPair);
            else { Assert.True(hasPair); pairs++; }
        }
        Assert.Equal(6, pairs); Assert.Equal(6, Cases().Count());
    }
    [Fact]
    public void BothSidesKeepOneSourcePerContextAndOneCapturedBodyPerSnapshotVersionAcrossTheBundle()
    {
        using var bundle = Bundle(); var contexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var versions = new Dictionary<(string Id, string Version), (string Context, string Digest)>();
        void Retain(string context, string digest) { if (contexts.TryGetValue(context, out var old)) Assert.Equal(old, digest); else contexts.Add(context, digest); }
        foreach (var row in bundle.RootElement.GetProperty("cases").EnumerateArray()) {
            var rootContext = SourceRevisionContext.Parse(SourceJson.Parse(row.GetProperty("context").GetRawText())).Data.CanonicalText;
            Retain(rootContext, row.GetProperty("sourceDigest").GetProperty("value").GetString()!);
            if (!row.TryGetProperty("materializedSnapshots", out var snapshots)) continue;
            foreach (var entry in snapshots.GetProperty("entries").EnumerateArray()) {
                var context = SourceRevisionContext.Parse(SourceJson.Parse(entry.GetProperty("context").GetRawText())).Data.CanonicalText;
                var digest = entry.GetProperty("source").GetProperty("digest").GetProperty("value").GetString()!; Retain(context, digest);
                var key = (ExternalSnapshotId.Parse(entry.GetProperty("snapshotId").GetString()!).ToString(), entry.GetProperty("version").GetString()!);
                if (versions.TryGetValue(key, out var old)) Assert.Equal(old, (context, digest)); else versions.Add(key, (context, digest));
            }
        }
    }
}
