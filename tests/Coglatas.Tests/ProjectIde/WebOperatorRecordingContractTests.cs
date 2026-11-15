using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebOperatorRecordingContractTests
{
    public static IEnumerable<object[]> Keys() => SyntheticOperatorRecordingTests.Keys();
    private static SourceJson Data(JsonElement frame) => SourceJson.Parse(frame.GetProperty("text").GetString()!);
    private static void Hash(JsonElement frame, string domain)
    {
        Assert.Equal(SourceJson.Parse(frame.GetProperty("digest").GetRawText()),
            ContentDigest.Compute(domain, Encoding.UTF8.GetBytes(frame.GetProperty("text").GetString()!)).Data);
    }
    private static void Bodies(JsonElement frames, IReadOnlyDictionary<string, SourceJson> originals, string domain)
    {
        var values = frames.EnumerateArray().ToArray(); Assert.Equal(originals.Count, values.Length);
        Assert.Equal(originals.Keys.Order(StringComparer.Ordinal), values.Select(v => v.GetProperty("digest").GetProperty("value").GetString()));
        foreach (var frame in values)
        {
            Hash(frame, domain); var key = frame.GetProperty("digest").GetProperty("value").GetString()!;
            Assert.Equal(originals[key].CanonicalText, frame.GetProperty("text").GetString());
            Assert.Equal(originals[key].ToCanonicalBytes().Length, frame.GetProperty("bytes").GetInt32());
        }
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void ActualOriginalHostFramesAndAllSelectedModesReplayTheExactCompilerWithoutRestampingRandomIdentities(string key)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(SyntheticOperatorRecordingTests.FixturePath()));
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(v => v.GetProperty("key").GetString() == key);
        var projection = row.GetProperty("operatorRecordings");
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        var control = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.Equal(SourceJson.Parse(row.GetProperty("result").GetRawText()), control.Result.Data);
        Hash(projection.GetProperty("source"), ProjectSource.Format); Hash(projection.GetProperty("input"), CompilerInputManifest.Format);
        Assert.Equal(source.Data.CanonicalText, projection.GetProperty("source").GetProperty("text").GetString());
        Assert.Equal(input.Data.CanonicalText, projection.GetProperty("input").GetProperty("text").GetString());
        var manifests = new Dictionary<string, JsonElement>(); var responses = new Dictionary<string, JsonElement>();
        var runs = new HashSet<Guid>(); var attempts = new HashSet<Guid>(); string? session = null;
        foreach (var label in new[] { "original", "diagnostic", "budget", "cancelled", "retry" })
        {
            var captured = projection.GetProperty(label);
            foreach (var (name, domain) in new[] { ("request", PredicateObservationCompilerHostRequest.Format),
                ("policy", "coglatas.local-predicate-observation-capture-policy/1"), ("baseExecution", "coglatas.compiler-host-input/1"),
                ("materializations", "coglatas.host-materializations/1"), ("manifest", LocalPredicateObservationCompilerRecordingManifest.Format),
                ("response", PredicateObservationCompilerHostResponse.Format), ("receipt", LocalPredicateObservationCompilerRecordingReceipt.Format) })
                Hash(captured.GetProperty(name), domain);
            var manifest = Data(captured.GetProperty("manifest")).Value; var response = Data(captured.GetProperty("response")).Value;
            var receipt = Data(captured.GetProperty("receipt")).Value; var policyData = Data(captured.GetProperty("policy")).Value;
            manifests.Add(label, manifest); responses.Add(label, response);
            var run = Guid.Parse(manifest.GetProperty("runId").GetString()!); var attempt = Guid.Parse(response.GetProperty("attemptId").GetString()!);
            Assert.Equal(4, run.Version); Assert.Equal(4, attempt.Version); Assert.True(runs.Add(run)); Assert.True(attempts.Add(attempt));
            session ??= manifest.GetProperty("sessionId").GetString(); Assert.Equal(session, manifest.GetProperty("sessionId").GetString());
            Assert.Equal(run.ToString(), receipt.GetProperty("runId").GetString()); Assert.Equal(run.ToString(), response.GetProperty("runId").GetString());
            Assert.Equal(attempt.ToString(), receipt.GetProperty("attemptId").GetString());
            Assert.Equal(SourceJson.Parse(captured.GetProperty("manifest").GetProperty("digest").GetRawText()), SourceJson.Parse(receipt.GetProperty("manifestDigest").GetRawText()));
            Assert.Equal(SourceJson.Parse(captured.GetProperty("manifest").GetProperty("digest").GetRawText()), SourceJson.Parse(response.GetProperty("manifestDigest").GetRawText()));
            Assert.Equal(SourceJson.Parse(captured.GetProperty("response").GetProperty("digest").GetRawText()), SourceJson.Parse(receipt.GetProperty("hostResponseDigest").GetRawText()));
            var policy = new CompilerPredicateObservationPolicy(Enum.Parse<PredicateEvaluationCaptureMode>(policyData.GetProperty("mode").GetString()!),
                policyData.GetProperty("maximumEvaluations").GetInt32(), policyData.GetProperty("maximumNodes").GetInt32(), policyData.GetProperty("maximumBodyBytes").GetInt32());
            var request = new PredicateObservationCompilerHostRequest(new(new(input), manifest.GetProperty("clientGeneration").GetInt64()), policy);
            Assert.Equal(request.Data.CanonicalText, captured.GetProperty("request").GetProperty("text").GetString());
            Assert.Equal(request.PolicyData.CanonicalText, captured.GetProperty("policy").GetProperty("text").GetString());
            Assert.Equal(request.Core.ExecutionInputData.CanonicalText, captured.GetProperty("baseExecution").GetProperty("text").GetString());
            Assert.Equal(request.Core.Materializations.CanonicalText, captured.GetProperty("materializations").GetProperty("text").GetString());
            if (label == "cancelled")
            {
                Assert.Equal("Cancelled", response.GetProperty("hostStatus").GetString());
                Assert.Equal(JsonValueKind.Null, captured.GetProperty("result").ValueKind); Assert.Equal(JsonValueKind.Null, captured.GetProperty("observation").ValueKind);
                Assert.Equal(JsonValueKind.Null, response.GetProperty("resultDigest").ValueKind); Assert.Equal(JsonValueKind.Null, receipt.GetProperty("observationRecordDigest").ValueKind);
                Assert.Empty(captured.GetProperty("diagnostics").EnumerateArray()); continue;
            }
            var actual = PureCoreCompiler.CompileWithPredicateObservation(input, input.Compiler, policy);
            Assert.Equal(control.Result.Data, actual.Compilation.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, actual.Compilation.LogicalOperationsConsumed);
            Hash(captured.GetProperty("result"), "coglatas.compiler-result/1"); Hash(captured.GetProperty("observation"), ProjectCompilerPredicateObservation.Format);
            Assert.Equal(actual.Compilation.Result.Data.CanonicalText, captured.GetProperty("result").GetProperty("text").GetString());
            Assert.Equal(actual.Observation.Data.CanonicalText, captured.GetProperty("observation").GetProperty("text").GetString());
            Assert.Equal(actual.Observation.Digest.Data, SourceJson.Parse(response.GetProperty("observationDigest").GetRawText()));
            Assert.Equal(actual.Observation.Digest.Data, SourceJson.Parse(receipt.GetProperty("observationRecordDigest").GetRawText()));
            Assert.Equal("NotEvaluated", receipt.GetProperty("authority").GetString()); Assert.Equal("NotAdopted", receipt.GetProperty("retentionPolicy").GetString());
            var diagnostics = actual.Compilation.Result.Diagnostics.Where(d => d.Location.Context.Equals(source.Context)).ToArray();
            var entries = captured.GetProperty("diagnostics").EnumerateArray().ToArray(); Assert.Equal(diagnostics.Length, entries.Length);
            for (var index = 0; index < diagnostics.Length; index++)
            {
                var diagnostic = diagnostics[index]; var entry = entries[index];
                Hash(entry.GetProperty("diagnostic"), "coglatas.compiler-diagnostic/1");
                Assert.Equal(diagnostic.Data.CanonicalText, entry.GetProperty("diagnostic").GetProperty("text").GetString());
                foreach (var (choice, mode, budget) in new[] { ("manifest", CompilerPredicateObservationSnapshotMode.Manifest, 1048576),
                    ("available", CompilerPredicateObservationSnapshotMode.AvailableOperators, 1048576), ("budget", CompilerPredicateObservationSnapshotMode.AvailableOperators, 1) })
                {
                    var view = entry.GetProperty(choice); var snapshot = ProjectCompilerPredicateObservationSnapshot.Read(actual, actual.Compilation,
                        actual.Observation, diagnostic, mode, budget);
                    Hash(view.GetProperty("frame"), ProjectCompilerPredicateObservationSnapshot.Format);
                    Assert.Equal(snapshot.Data.CanonicalText, view.GetProperty("frame").GetProperty("text").GetString());
                    Bodies(view.GetProperty("operatorBodies"), snapshot.Bodies, "coglatas.observed-predicate-node/1");
                    Bodies(view.GetProperty("evaluationFrames"), snapshot.EvaluationFrames, "coglatas.local-predicate-evaluation/1");
                }
            }
        }
        foreach (var (child, parent) in new[] { ("diagnostic", "original"), ("budget", "diagnostic"), ("retry", "cancelled") })
        {
            var origin = manifests[child].GetProperty("origin");
            Assert.Equal(manifests[parent].GetProperty("runId").GetString(), origin.GetProperty("runId").GetString());
            Assert.Equal(responses[parent].GetProperty("attemptId").GetString(), origin.GetProperty("attemptId").GetString());
            Assert.Equal(SourceJson.Parse(projection.GetProperty(parent).GetProperty("manifest").GetProperty("digest").GetRawText()), SourceJson.Parse(origin.GetProperty("manifestDigest").GetRawText()));
            Assert.Equal(SourceJson.Parse(projection.GetProperty(parent).GetProperty("receipt").GetProperty("digest").GetRawText()), SourceJson.Parse(origin.GetProperty("receiptDigest").GetRawText()));
            Assert.Equal(manifests[parent].GetProperty("knownLineage").EnumerateArray().Select(v => v.GetString()).Append(manifests[parent].GetProperty("runId").GetString()),
                manifests[child].GetProperty("knownLineage").EnumerateArray().Select(v => v.GetString()));
        }
    }

    [Fact]
    public void GlobalOriginalRunAttemptAndSourceContextIdentitiesRemainDistinctAndProducerMetadataIsUntrusted()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(SyntheticOperatorRecordingTests.FixturePath()));
        var runs = new HashSet<string>(); var attempts = new HashSet<string>(); var contexts = new HashSet<string>();
        var rows = fixture.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(11, rows.Length);
        Assert.Equal(SyntheticOperatorRecordingTests.Keys().Select(v => (string)v[0]).Order(StringComparer.Ordinal), rows.Select(r => r.GetProperty("key").GetString()).Order(StringComparer.Ordinal));
        foreach (var row in rows)
        {
            Assert.True(contexts.Add(SourceJson.Parse(row.GetProperty("context").GetRawText()).CanonicalText));
            foreach (var label in new[] { "original", "diagnostic", "budget", "cancelled", "retry" })
            {
                var member = row.GetProperty("operatorRecordings").GetProperty(label);
                Assert.True(runs.Add(Data(member.GetProperty("manifest")).Value.GetProperty("runId").GetString()!));
                Assert.True(attempts.Add(Data(member.GetProperty("response")).Value.GetProperty("attemptId").GetString()!));
            }
        }
        Assert.Equal(55, runs.Count); Assert.Equal(55, attempts.Count); Assert.Empty(runs.Intersect(attempts));
        var producer = fixture.RootElement.GetProperty("operatorRecordingProducer");
        foreach (var name in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", producer.GetProperty(name).GetString());
        Assert.Equal("NotAdopted", producer.GetProperty("retentionPolicy").GetString());
        Assert.Matches("^[a-f0-9]{64}$", producer.GetProperty("domainAssemblySha256").GetString()!);
        Assert.Matches("^[a-f0-9]{64}$", producer.GetProperty("applicationAssemblySha256").GetString()!);
    }

    [Fact]
    public void NestedOriginalExampleKeepsObservedOrderDuplicatesAndSkippedOperandsInsteadOfInventingAProofTree()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(SyntheticOperatorRecordingTests.FixturePath()));
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("key").GetString() == "operator-nested-duplicates");
        var view = row.GetProperty("operatorRecordings").GetProperty("diagnostic").GetProperty("diagnostics")[0].GetProperty("available");
        var bodies = view.GetProperty("operatorBodies").EnumerateArray().Select(Data).ToArray(); Assert.Equal(6, bodies.Length);
        var root = bodies.Single(b => b.Value.GetProperty("ordinal").GetInt32() == 0).Value;
        Assert.Equal(new[] { 1, 4 }, root.GetProperty("evaluatedChildren").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal(1, root.GetProperty("skippedOperands").GetInt32());
        var premises = Data(Assert.Single(view.GetProperty("evaluationFrames").EnumerateArray())).Value.GetProperty("premises").EnumerateArray()
            .Select(v => v.GetProperty("value").GetString()).ToArray();
        Assert.Equal(3, premises.Length); Assert.Equal(2, premises.Distinct().Count());
    }
}
