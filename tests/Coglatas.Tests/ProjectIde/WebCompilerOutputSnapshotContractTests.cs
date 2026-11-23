using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebCompilerOutputSnapshotContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "compiler-outputs-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("The actual Compiler output snapshot fixture is required.");
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "compiler-outputs-v1.json")));
        var cases = data.RootElement.GetProperty("cases"); Assert.Equal(12, cases.GetArrayLength());
        foreach (var item in cases.EnumerateArray()) yield return [item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalOutputSnapshotBytesReplayTheExactCompilerAndPreserveOriginalResultAxes(string fixture)
    {
        using var parsed = JsonDocument.Parse(fixture); var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(item, input));
        Assert.Equal(compilation.Result.Data, SourceJson.Parse(item.GetProperty("result").GetRawText()));
        Assert.Equal(compilation.Result.ResultDigest.Data, SourceJson.Parse(item.GetProperty("resultDigest").GetRawText()));
        Assert.NotNull(compilation.Publication); var original = compilation.Result.Data;
        var projection = item.GetProperty("compilerOutputs"); Assert.Equal(2, projection.EnumerateObject().Count());
        foreach (var mode in new[] { CompilerOutputSnapshotMode.Manifest, CompilerOutputSnapshotMode.AvailableOutputs }) {
            var budgetGap = mode == CompilerOutputSnapshotMode.AvailableOutputs && item.GetProperty("key").GetString() == "tests-publication-root-draft";
            var snapshot = ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest, mode,
                budgetGap ? 1 : ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, desiredInput: input);
            var frame = projection.GetProperty(mode == CompilerOutputSnapshotMode.Manifest ? "manifest" : "available");
            Assert.Equal(4, frame.EnumerateObject().Count()); Assert.Equal(snapshot.Data.CanonicalText, frame.GetProperty("text").GetString());
            Assert.Equal(snapshot.Digest.Data, SourceJson.Parse(frame.GetProperty("digest").GetRawText()));
            Assert.Equal(snapshot.RetainedBodyBytes, frame.GetProperty("retainedBodyBytes").GetInt32());
            Assert.Equal(mode == CompilerOutputSnapshotMode.Manifest ? CompilerOutputSnapshotState.ManifestOnly : budgetGap ? CompilerOutputSnapshotState.BodyBudgetExceeded : CompilerOutputSnapshotState.CapturedAvailableOutputs, snapshot.State);
            Assert.Equal(CompilerFreshness.Current, snapshot.Freshness); Assert.Empty(snapshot.ChangedDimensions);
            var bodies = frame.GetProperty("bodies"); Assert.Equal(snapshot.Bodies.Count, bodies.GetArrayLength());
            Assert.Equal(mode == CompilerOutputSnapshotMode.Manifest || budgetGap ? 0 : 3, bodies.GetArrayLength());
            Assert.Equal(snapshot.Bodies.Keys.OrderBy(role => role, StringComparer.Ordinal), bodies.EnumerateArray().Select(body => body.GetProperty("role").GetString()!));
            foreach (var body in bodies.EnumerateArray()) {
                Assert.Equal(3, body.EnumerateObject().Count()); var role = body.GetProperty("role").GetString()!;
                Assert.Equal(snapshot.Bodies[role].CanonicalText, body.GetProperty("text").GetString());
                Assert.Equal(compilation.Result.Artifacts.Single(reference => reference.Role == role).Digest.Data, SourceJson.Parse(body.GetProperty("digest").GetRawText()));
            }
            foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", snapshot.Data.Value.GetProperty(field).GetString());
            Assert.Equal("NotAdopted", snapshot.Data.Value.GetProperty("retentionPolicy").GetString());
            Assert.Equal("NotRecorded", snapshot.Data.Value.GetProperty("intermediateStates").GetString());
            Assert.Equal("NotAllocated", snapshot.Data.Value.GetProperty("runIdentity").GetString()); Assert.Same(original, compilation.Result.Data);
        }
    }
    [Fact]
    public void EntireOutputBundleRetainsOneSourcePerContextAndAnExplicitAtomicBudgetGap()
    {
        var contexts = new Dictionary<string, string>(StringComparer.Ordinal); var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        static void Retain(Dictionary<string, string> index, string key, string value) {
            if (index.TryGetValue(key, out var existing)) Assert.Equal(existing, value); else index.Add(key, value);
        }
        var manifests = 0; var captured = 0; var gaps = 0;
        foreach (var row in Cases()) {
            using var parsed = JsonDocument.Parse((string)row[0]); var item = parsed.RootElement;
            Retain(contexts, SourceJson.Parse(item.GetProperty("context").GetRawText()).CanonicalText, item.GetProperty("sourceDigest").GetProperty("value").GetString()!);
            var projection = item.GetProperty("compilerOutputs");
            using var manifest = JsonDocument.Parse(projection.GetProperty("manifest").GetProperty("text").GetString()!);
            using var available = JsonDocument.Parse(projection.GetProperty("available").GetProperty("text").GetString()!);
            if (manifest.RootElement.GetProperty("state").GetString() == "ManifestOnly") manifests++;
            if (available.RootElement.GetProperty("state").GetString() == "CapturedAvailableOutputs") captured++;
            if (available.RootElement.GetProperty("state").GetString() == "BodyBudgetExceeded") gaps++;
            if (!item.TryGetProperty("materializedSnapshots", out var materialized)) continue;
            foreach (var entry in materialized.GetProperty("entries").EnumerateArray()) {
                var digest = entry.GetProperty("source").GetProperty("digest").GetProperty("value").GetString()!;
                Retain(contexts, SourceJson.Parse(entry.GetProperty("context").GetRawText()).CanonicalText, digest);
                Retain(versions, entry.GetProperty("snapshotId").GetString() + "|" + entry.GetProperty("version").GetString(), digest);
            }
        }
        Assert.Equal(12, manifests); Assert.Equal(11, captured); Assert.Equal(1, gaps);
    }
}
