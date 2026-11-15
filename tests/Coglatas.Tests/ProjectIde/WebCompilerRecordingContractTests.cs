using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebCompilerRecordingContractTests
{
    public static IEnumerable<object[]> Cases() => LocalCompilerRecordingTests.Cases();
    private static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "compiler-recordings-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("The original observed Compiler recording fixture is required.");
        return Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "compiler-recordings-v1.json");
    }
    private static SourceJson Data(JsonElement value) => SourceJson.Parse(value.GetRawText());
    private static SourceJson TextFrame(JsonElement frame, string domain)
    {
        var text = frame.GetProperty("text").GetString()!; var data = SourceJson.Parse(text);
        Assert.Equal(text, data.CanonicalText);
        Assert.Equal(ContentDigest.Compute(domain, Encoding.UTF8.GetBytes(text)).Data, Data(frame.GetProperty("digest")));
        return data;
    }
    private static void EqualExcept(JsonElement original, JsonElement replay, params string[] dynamicFields)
    {
        Assert.Equal(original.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal), replay.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        foreach (var property in original.EnumerateObject()) if (!dynamicFields.Contains(property.Name, StringComparer.Ordinal)) Assert.Equal(Data(property.Value), Data(replay.GetProperty(property.Name)));
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalObservedBytesAndFreshActualInvocationsRetainFullInputsAndSeparateOutcomes(string key)
    {
        using var bundle = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var item = bundle.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("key").GetString() == key);
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, Data(item.GetProperty("manifest")));
        CompilerArtifactIdentity Artifact(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        var request = new CompilerHostRequest(new(input), 7, WebPreviewContractTests.Snapshots(item, input));
        using var session = new LocalCompilerRecordingSession(profile);
        LocalCompilerRecordedInvocation? first = null; JsonElement originalManifest = default; JsonElement originalReceipt = default;
        var observedRuns = new HashSet<Guid>(); var observedAttempts = new HashSet<Guid>();
        foreach (var label in new[] { "original", "diagnostic", "budget", "cancelled" }) {
            var observed = item.GetProperty("compilerRecordings").GetProperty(label);
            var declaration = TextFrame(observed.GetProperty("manifest"), "coglatas.local-compiler-recording-manifest/1");
            var receipt = TextFrame(observed.GetProperty("receipt"), "coglatas.local-compiler-recording-receipt/1");
            var m = declaration.Value; var r = receipt.Value; var h = observed.GetProperty("host");
            var runId = Guid.Parse(m.GetProperty("runId").GetString()!); var attemptId = Guid.Parse(r.GetProperty("attemptId").GetString()!);
            Assert.Equal(4, runId.Version); Assert.Equal(4, attemptId.Version); Assert.True(observedRuns.Add(runId)); Assert.True(observedAttempts.Add(attemptId));
            Assert.Equal(runId.ToString(), r.GetProperty("runId").GetString()); Assert.Equal(attemptId.ToString(), h.GetProperty("attemptId").GetString());
            Assert.Equal(Data(observed.GetProperty("manifest").GetProperty("digest")), Data(r.GetProperty("manifestDigest")));
            Assert.Equal(request.MaterializationDigest.Data, Data(m.GetProperty("materializationsDigest")));
            Assert.Equal(request.ExecutionInputDigest.Data, Data(m.GetProperty("executionInputDigest")));
            var mode = label is "diagnostic" or "budget" ? CompilerOutputSnapshotMode.AvailableOutputs : CompilerOutputSnapshotMode.Manifest;
            var manifest = session.Prepare(request, Guid.NewGuid(), mode, label == "budget" ? 1 : ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, first?.Receipt);
            using var cancellation = new CancellationTokenSource(); if (label == "cancelled") cancellation.Cancel();
            var replay = await LocalCompilerRecordedInvocation.ExecuteAsync(session, manifest, request, cancellation.Token);
            EqualExcept(m, replay.Receipt.Manifest.Data.Value, "sessionId", "runId", "origin", "knownLineage");
            EqualExcept(r, replay.Receipt.Data.Value, "runId", "manifestDigest", "attemptId");
            Assert.NotEqual(runId, replay.Receipt.Manifest.RunId); Assert.NotEqual(attemptId, replay.Response.AttemptId);
            Assert.Equal(replay.Response.Status.ToString(), h.GetProperty("status").GetString()); Assert.Equal(replay.Response.ReasonCode, h.GetProperty("reasonCode").GetString());
            Assert.Equal(request.ClientGeneration, h.GetProperty("clientGeneration").GetInt64()); Assert.Equal(input.InputDigest.Data, Data(h.GetProperty("requestedInputDigest")));
            if (first is null) { first = replay; originalManifest = observed.GetProperty("manifest"); originalReceipt = observed.GetProperty("receipt"); Assert.Equal(JsonValueKind.Null, m.GetProperty("origin").ValueKind); Assert.Empty(m.GetProperty("knownLineage").EnumerateArray()); }
            else {
                var origin = m.GetProperty("origin"); var firstManifest = SourceJson.Parse(originalManifest.GetProperty("text").GetString()!).Value;
                var firstReceipt = SourceJson.Parse(originalReceipt.GetProperty("text").GetString()!).Value;
                Assert.Equal(firstManifest.GetProperty("runId").GetString(), origin.GetProperty("runId").GetString());
                Assert.Equal(Data(originalManifest.GetProperty("digest")), Data(origin.GetProperty("manifestDigest")));
                Assert.Equal(Data(originalReceipt.GetProperty("digest")), Data(origin.GetProperty("receiptDigest")));
                Assert.Equal(firstReceipt.GetProperty("attemptId").GetString(), origin.GetProperty("attemptId").GetString());
                Assert.Equal(firstManifest.GetProperty("sessionId").GetString(), m.GetProperty("sessionId").GetString());
                Assert.Equal(new[] { firstManifest.GetProperty("runId").GetString() }, m.GetProperty("knownLineage").EnumerateArray().Select(value => value.GetString()));
            }
            if (label == "cancelled") {
                Assert.Null(replay.Response.Compilation); Assert.Null(replay.Receipt.Snapshot); Assert.Equal(JsonValueKind.Null, observed.GetProperty("snapshot").ValueKind);
                foreach (var field in new[] { "execution", "executionDigest", "result", "resultDigest" }) Assert.Equal(JsonValueKind.Null, h.GetProperty(field).ValueKind);
            } else {
                Assert.Equal(replay.Response.Compilation!.Result.Data, Data(h.GetProperty("result"))); Assert.Equal(Data(item.GetProperty("result")), Data(h.GetProperty("result")));
                Assert.Equal(replay.Response.Compilation.Result.ResultDigest.Data, Data(h.GetProperty("resultDigest")));
                Assert.Equal(replay.Response.Execution!.Data, Data(h.GetProperty("execution"))); Assert.Equal(replay.Response.Execution.Digest.Data, Data(h.GetProperty("executionDigest")));
                var snapshot = replay.Receipt.Snapshot!; var captured = observed.GetProperty("snapshot");
                Assert.Equal(snapshot.Data, TextFrame(captured, "coglatas.local-compiler-output-snapshot/1"));
                Assert.Equal(snapshot.RetainedBodyBytes, captured.GetProperty("retainedBodyBytes").GetInt32());
                Assert.Equal(label == "diagnostic" ? 3 : 0, captured.GetProperty("bodies").GetArrayLength());
                foreach (var body in captured.GetProperty("bodies").EnumerateArray()) {
                    var role = body.GetProperty("role").GetString()!; Assert.Equal(snapshot.Bodies[role].CanonicalText, body.GetProperty("text").GetString());
                    Assert.Equal(replay.Response.Compilation.Result.Artifacts.Single(artifact => artifact.Role == role).Digest.Data, Data(body.GetProperty("digest")));
                }
            }
        }
        Assert.Equal(4, session.ReservedExecutions);
    }
    [Fact]
    public void BundlePreservesAllPreviousCasesAndDistinctOriginalObservedIdentities()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(FixturePath())); var cases = data.RootElement.GetProperty("cases"); Assert.Equal(12, cases.GetArrayLength());
        var runs = new HashSet<string>(StringComparer.Ordinal); var attempts = new HashSet<string>(StringComparer.Ordinal); var sessions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var previous in WebCompilerOutputSnapshotContractTests.Cases()) {
            using var old = JsonDocument.Parse((string)previous[0]); var before = old.RootElement;
            var current = cases.EnumerateArray().Single(row => row.GetProperty("key").GetString() == before.GetProperty("key").GetString());
            foreach (var property in before.EnumerateObject()) {
                Assert.Equal(Data(property.Value), Data(current.GetProperty(property.Name)));
            }
            foreach (var label in new[] { "original", "diagnostic", "budget", "cancelled" }) {
                var frame = current.GetProperty("compilerRecordings").GetProperty(label); using var m = JsonDocument.Parse(frame.GetProperty("manifest").GetProperty("text").GetString()!); using var r = JsonDocument.Parse(frame.GetProperty("receipt").GetProperty("text").GetString()!);
                Assert.True(runs.Add(m.RootElement.GetProperty("runId").GetString()!)); Assert.True(attempts.Add(r.RootElement.GetProperty("attemptId").GetString()!));
                if (label == "original") Assert.True(sessions.Add(m.RootElement.GetProperty("sessionId").GetString()!));
            }
        }
        Assert.Equal(48, runs.Count); Assert.Equal(48, attempts.Count); Assert.Equal(12, sessions.Count);
    }
}
