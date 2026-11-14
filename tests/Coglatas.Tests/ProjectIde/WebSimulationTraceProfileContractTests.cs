using System.Globalization;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebSimulationTraceProfileContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "simulation-profile-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("The actual simulation trace profile fixture is required.");
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "simulation-profile-v1.json")));
        var cases = data.RootElement.GetProperty("cases"); Assert.Equal(12, cases.GetArrayLength());
        foreach (var item in cases.EnumerateArray()) yield return [item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalProfileBytesReplayActualSourceCompilerRunAndExactMetricDefinitions(string fixture)
    {
        using var parsed = JsonDocument.Parse(fixture); var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(item, input));
        Assert.Equal(compilation.Result.Data, SourceJson.Parse(item.GetProperty("result").GetRawText()));
        if (compilation.Publication?.ExecutionPlan is not { } plan) {
            Assert.False(item.TryGetProperty("simulationRun", out _)); Assert.False(item.TryGetProperty("simulationTraceProfile", out _));
            Assert.Equal("coglatas.plan.required-validation-incomplete", compilation.Publication!.PlanNotProducedReason); return;
        }
        var frame = item.GetProperty("simulationRun"); var originalRun = SourceJson.Parse(frame.GetProperty("text").GetString()!);
        var manifest = originalRun.Value.GetProperty("manifest"); var limits = manifest.GetProperty("limits");
        var request = new SimulationRunManifest(plan, Guid.Parse(manifest.GetProperty("runId").GetString()!), long.Parse(manifest.GetProperty("startTicks").GetString()!, CultureInfo.InvariantCulture),
            new(limits.GetProperty("maximumEvents").GetInt32(), limits.GetProperty("maximumOperations").GetInt32(),
                long.Parse(limits.GetProperty("maximumElapsedTicks").GetString()!, CultureInfo.InvariantCulture), limits.GetProperty("maximumTraceBytes").GetInt32()));
        var run = ProjectSimulation.Execute(request, input.InputDigest);
        Assert.Equal(request.Data, SourceJson.Parse(manifest.GetRawText())); Assert.Equal(run.Data, originalRun);
        Assert.Equal(run.Digest.Data, SourceJson.Parse(frame.GetProperty("digest").GetRawText()));
        var profile = ProjectSimulationTraceProfile.Read(run, run.Digest); var projection = item.GetProperty("simulationTraceProfile");
        Assert.Equal(profile.Data, SourceJson.Parse(projection.GetProperty("text").GetString()!)); Assert.Equal(profile.Digest.Data, SourceJson.Parse(projection.GetProperty("digest").GetRawText()));
        var bounded = item.GetProperty("key").GetString()!.Contains("-bounded-", StringComparison.Ordinal);
        var external = item.GetProperty("key").GetString()!.Contains("-external-", StringComparison.Ordinal);
        Assert.Equal(bounded ? SimulationTraceProfileState.Incomplete : SimulationTraceProfileState.Complete, profile.State);
        Assert.Equal(bounded ? 0 : 3, profile.RecordedElapsedTicks); Assert.Equal(bounded ? 0 : 3, profile.RecordedBusyTicks);
        Assert.Equal(bounded ? "0" : external ? "4" : "3", profile.Data.Value.GetProperty("metrics").GetProperty("recordedTaskTicks").GetString());
        Assert.Equal(bounded ? 2 : 0, profile.OpenTasks); Assert.Equal(bounded || external ? 2 : 1, profile.PeakActiveTasks);
        Assert.Equal("SimulatedModel", profile.Data.Value.GetProperty("inputKind").GetString()); Assert.Equal(CompilerVerdict.NotRun, run.Verdict);
        Assert.Equal("NotEvaluated", profile.Data.Value.GetProperty("authority").GetString()); Assert.Equal(run.Data, originalRun);
    }
    [Fact]
    public void EntireProfileBundleRetainsOneSourceBodyPerContextAndSnapshotVersion()
    {
        var contexts = new Dictionary<string, string>(StringComparer.Ordinal); var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        static void Retain(Dictionary<string, string> index, string key, string value) {
            if (index.TryGetValue(key, out var existing)) Assert.Equal(existing, value); else index.Add(key, value);
        }
        var profiles = 0;
        foreach (var row in Cases()) {
            using var parsed = JsonDocument.Parse((string)row[0]); var item = parsed.RootElement;
            Retain(contexts, SourceJson.Parse(item.GetProperty("context").GetRawText()).CanonicalText, item.GetProperty("sourceDigest").GetProperty("value").GetString()!);
            if (item.TryGetProperty("simulationTraceProfile", out _)) profiles++;
            if (!item.TryGetProperty("materializedSnapshots", out var materialized)) continue;
            foreach (var entry in materialized.GetProperty("entries").EnumerateArray()) {
                var digest = entry.GetProperty("source").GetProperty("digest").GetProperty("value").GetString()!;
                Retain(contexts, SourceJson.Parse(entry.GetProperty("context").GetRawText()).CanonicalText, digest);
                Retain(versions, entry.GetProperty("snapshotId").GetString() + "|" + entry.GetProperty("version").GetString(), digest);
            }
        }
        Assert.Equal(9, profiles);
    }
}
