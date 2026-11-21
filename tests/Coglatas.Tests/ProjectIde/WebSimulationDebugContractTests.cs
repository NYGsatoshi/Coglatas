using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebSimulationDebugContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        const string name = "simulation-debug-v1.json";
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", name))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("The actual Core debugger recording asset is required.");
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", name)));
        var cases = data.RootElement.GetProperty("cases"); Assert.Equal(12, cases.GetArrayLength());
        foreach (var item in cases.EnumerateArray()) yield return [item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalRecordedCommandsReplayTheActualCoreSnapshotsAndTerminalRunBytes(string fixture)
    {
        using var parsed = JsonDocument.Parse(fixture); var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(item, input));
        Assert.Equal(compilation.Result.Data, SourceJson.Parse(item.GetProperty("result").GetRawText()));
        if (compilation.Publication?.ExecutionPlan is not { } plan)
        {
            Assert.False(item.TryGetProperty("simulationDebug", out _)); Assert.False(item.TryGetProperty("simulationRun", out _));
            Assert.Equal("coglatas.plan.required-validation-incomplete", compilation.Publication!.PlanNotProducedReason); return;
        }
        var recording = item.GetProperty("simulationDebug"); Assert.Equal("coglatas.local-simulation-debug-recording/1", recording.GetProperty("format").GetString());
        var manifest = SourceJson.Parse(recording.GetProperty("manifest").GetProperty("text").GetString()!); var limits = manifest.Value.GetProperty("limits");
        var request = new SimulationRunManifest(plan, Guid.Parse(manifest.Value.GetProperty("runId").GetString()!), long.Parse(manifest.Value.GetProperty("startTicks").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            new(limits.GetProperty("maximumEvents").GetInt32(), limits.GetProperty("maximumOperations").GetInt32(),
                long.Parse(limits.GetProperty("maximumElapsedTicks").GetString()!, System.Globalization.CultureInfo.InvariantCulture), limits.GetProperty("maximumTraceBytes").GetInt32()));
        Assert.Equal(request.Data, manifest); Assert.Equal(request.Digest.Data, SourceJson.Parse(recording.GetProperty("manifest").GetProperty("digest").GetRawText()));
        using var debugger = new ProjectSimulationDebugger(request, input.InputDigest);
        Assert.Equal(debugger.Controls, SourceJson.Parse(recording.GetProperty("controls").GetProperty("text").GetString()!));
        Assert.Equal(debugger.ControlsDigest.Data, SourceJson.Parse(recording.GetProperty("controls").GetProperty("digest").GetRawText()));
        var index = 0;
        foreach (var frame in recording.GetProperty("frames").EnumerateArray())
        {
            var expected = SourceJson.Parse(frame.GetProperty("snapshot").GetProperty("text").GetString()!);
            var target = frame.GetProperty("target").ValueKind == JsonValueKind.Null ? null : ProjectLocation.Parse(SourceJson.Parse(frame.GetProperty("target").GetRawText()));
            SimulationControlResponse? response = frame.GetProperty("action").GetString() switch
            {
                "coglatas.recorded-preparation" when index == 0 => null,
                "coglatas.recorded-set-breakpoints" => debugger.SetBreakpoints(debugger.Snapshot.Stamp, expected.Value.GetProperty("breakpoints").EnumerateArray().Select(entry => {
                    var value = entry.GetProperty("request"); Assert.Equal(plan.Digest.Data, SourceJson.Parse(value.GetProperty("planDigest").GetRawText()));
                    return new SimulationBreakpoint(Guid.Parse(value.GetProperty("breakpointId").GetString()!), plan.Digest,
                        ProjectLocation.Parse(SourceJson.Parse(value.GetProperty("location").GetRawText())), (SimulationBreakpointPoint)value.GetProperty("point").GetInt32()); })),
                "coglatas.continue" => debugger.Continue(debugger.Snapshot.Stamp),
                "coglatas.event-step" => debugger.Step(debugger.Snapshot.Stamp),
                "coglatas.task-step-over" => debugger.StepOver(debugger.Snapshot.Stamp, target!),
                "coglatas.inspect-fixed-task" => debugger.Inspect(debugger.Snapshot.Stamp, target!),
                _ => throw new InvalidOperationException("Unsupported recorded command")
            };
            if (index != 0) Assert.True(response!.Accepted, response.Reason);
            var snapshot = debugger.Snapshot; Assert.Equal(index++, snapshot.Stamp.Revision);
            Assert.Equal(expected, snapshot.Data); Assert.Equal(snapshot.Digest.Data, SourceJson.Parse(frame.GetProperty("snapshot").GetProperty("digest").GetRawText()));
            Assert.Equal(SourceJson.Parse(JsonSerializer.Serialize(snapshot.Events.Select(value => value.Value).ToArray())), SourceJson.Parse(frame.GetProperty("events").GetProperty("text").GetString()!));
            Assert.Equal(snapshot.TracePrefixDigest.Data, SourceJson.Parse(frame.GetProperty("events").GetProperty("digest").GetRawText()));
            if (snapshot.Inspection is null) Assert.Equal(JsonValueKind.Null, frame.GetProperty("inspection").ValueKind);
            else {
                var inspection = frame.GetProperty("inspection"); Assert.Equal(snapshot.Inspection, SourceJson.Parse(inspection.GetProperty("text").GetString()!));
                Assert.Equal(ContentDigest.Compute("coglatas.simulation-task-inspection/1", snapshot.Inspection.ToCanonicalBytes()).Data, SourceJson.Parse(inspection.GetProperty("digest").GetRawText()));
            }
        }
        Assert.True(index >= 10); Assert.Equal(SimulationControlState.Terminated, debugger.Snapshot.State);
        var full = ProjectSimulation.Execute(request, input.InputDigest); Assert.Equal(full.Data, debugger.Snapshot.Result!.Data);
        Assert.Equal(full.Data, SourceJson.Parse(item.GetProperty("simulationRun").GetProperty("text").GetString()!));
        Assert.Equal(CompilerVerdict.NotRun, full.Verdict);
    }
    [Fact]
    public void ContinueResumesAcrossInspectionAndStopsAtTheNextActualBreakpoint()
    {
        var item = Cases().Select(row => Assert.IsType<JsonObject>(JsonNode.Parse((string)row[0])))
            .Single(row => Assert.IsAssignableFrom<JsonValue>(row["key"]).GetValue<string>() == "tests-simulation-root-base");
        var recording = Assert.IsType<JsonObject>(item["simulationDebug"]);
        var frames = Assert.IsType<JsonArray>(recording["frames"]);
        // Omit the explicit event step and its inspection: Continue must resume the retained start breakpoint itself.
        frames.RemoveAt(6); frames.RemoveAt(5);
        for (var index = 0; index < frames.Count; index++) {
            var frame = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(frames[index])["snapshot"]);
            var snapshot = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.IsAssignableFrom<JsonValue>(frame["text"]).GetValue<string>()));
            snapshot["revision"] = index;
            var canonical = SourceJson.Parse(snapshot.ToJsonString()); frame["text"] = canonical.CanonicalText;
            frame["digest"] = JsonNode.Parse(ContentDigest.Compute("coglatas.simulation-debug-snapshot/1", canonical.ToCanonicalBytes()).Data.CanonicalText);
        }
        OriginalRecordedCommandsReplayTheActualCoreSnapshotsAndTerminalRunBytes(item.ToJsonString());
    }
    [Fact]
    public void BreakpointUpdatesPreserveInspectionAndResumeAcrossContinue()
    {
        var item = Cases().Select(row => Assert.IsType<JsonObject>(JsonNode.Parse((string)row[0])))
            .Single(row => Assert.IsAssignableFrom<JsonValue>(row["key"]).GetValue<string>() == "tests-simulation-root-base");
        var recording = Assert.IsType<JsonObject>(item["simulationDebug"]);
        var frames = Assert.IsType<JsonArray>(recording["frames"]);
        frames.RemoveAt(6); frames.RemoveAt(5);
        var retained = Assert.IsType<JsonObject>(frames[4]!.DeepClone());
        retained["action"] = "coglatas.recorded-set-breakpoints"; retained["target"] = null;
        var retainedSnapshot = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.IsAssignableFrom<JsonValue>(Assert.IsType<JsonObject>(retained["snapshot"])["text"]).GetValue<string>()));
        retainedSnapshot["reason"] = "coglatas.debug.breakpoints-set";
        Assert.IsType<JsonObject>(retained["snapshot"])["text"] = retainedSnapshot.ToJsonString(); frames.Insert(5, retained);
        for (var index = 0; index < frames.Count; index++) {
            var frame = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(frames[index])["snapshot"]);
            var snapshot = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.IsAssignableFrom<JsonValue>(frame["text"]).GetValue<string>()));
            snapshot["revision"] = index;
            var canonical = SourceJson.Parse(snapshot.ToJsonString()); frame["text"] = canonical.CanonicalText;
            frame["digest"] = JsonNode.Parse(ContentDigest.Compute("coglatas.simulation-debug-snapshot/1", canonical.ToCanonicalBytes()).Data.CanonicalText);
        }
        OriginalRecordedCommandsReplayTheActualCoreSnapshotsAndTerminalRunBytes(item.ToJsonString());
            }
    [Fact]
    public void EveryCapturedSourceAndSnapshotVersionKeepsOneBodyAcrossTheWholeDebugBundle()
    {
        var contexts = new Dictionary<string, string>(StringComparer.Ordinal); var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        static void Retain(Dictionary<string, string> values, string key, string digest) { if (values.TryGetValue(key, out var previous)) Assert.Equal(previous, digest); else values.Add(key, digest); }
        foreach (var row in Cases()) {
            using var data = JsonDocument.Parse((string)row[0]); var item = data.RootElement;
            Retain(contexts, SourceJson.Parse(item.GetProperty("context").GetRawText()).CanonicalText, item.GetProperty("sourceDigest").GetProperty("value").GetString()!);
            if (!item.TryGetProperty("materializedSnapshots", out var snapshots)) continue;
            foreach (var entry in snapshots.GetProperty("entries").EnumerateArray()) {
                var digest = entry.GetProperty("source").GetProperty("digest").GetProperty("value").GetString()!;
                Retain(contexts, SourceJson.Parse(entry.GetProperty("context").GetRawText()).CanonicalText, digest);
                Retain(versions, entry.GetProperty("snapshotId").GetString() + "|" + entry.GetProperty("version").GetString(), digest);
            }
        }
    }
}
