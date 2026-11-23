using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebPredicateRecordingContractTests
{
    public static IEnumerable<object[]> Keys() => new[] { "fail", "pass", "unknown", "test-assertion-navigation" }.Select(key => new object[] { key });
    private static JsonDocument Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend/public/assets/projectide/predicate-recordings-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Combined Product predicate fixture is required.");
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend/public/assets/projectide/predicate-recordings-v1.json")));
    }
    private static SourceJson Data(JsonElement frame) => SourceJson.Parse(frame.GetProperty("text").GetString()!);
    private static void Hash(JsonElement frame, string domain)
    {
        var original = frame.GetProperty("text").GetString()!;
        Assert.Equal(SourceJson.Parse(frame.GetProperty("digest").GetRawText()), ContentDigest.Compute(domain, Encoding.UTF8.GetBytes(original)).Data);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void ActualPredeclaredRunsAndSelectedReadFramesReplayOriginalCoreWithoutRestampingRunIdentity(string key)
    {
        using var fixture = Fixture();
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("key").GetString() == key);
        var projection = row.GetProperty("predicateRecordings");
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        var control = PureCoreCompiler.Compile(input, input.Compiler);
        Hash(projection.GetProperty("source"), ProjectSource.Format); Hash(projection.GetProperty("input"), CompilerInputManifest.Format);
        Assert.Equal(source.Data.CanonicalText, projection.GetProperty("source").GetProperty("text").GetString());
        Assert.Equal(input.Data.CanonicalText, projection.GetProperty("input").GetProperty("text").GetString());
        Assert.Equal(SourceJson.Parse(row.GetProperty("result").GetRawText()), control.Result.Data);
        var manifests = new Dictionary<string, JsonElement>(); var responses = new Dictionary<string, JsonElement>();
        var runs = new HashSet<Guid>(); var attempts = new HashSet<Guid>(); string? session = null;
        foreach (var label in new[] { "original", "diagnostic", "budget", "cancelled", "retry" })
        {
            var frame = projection.GetProperty(label);
            foreach (var (member, domain) in new[] {
                ("request", PredicateCompilerHostRequest.Format), ("policy", "coglatas.local-predicate-capture-policy/1"),
                ("baseExecution", "coglatas.compiler-host-input/1"), ("materializations", "coglatas.host-materializations/1"),
                ("manifest", LocalPredicateCompilerRecordingManifest.Format), ("response", PredicateCompilerHostResponse.Format),
                ("receipt", LocalPredicateCompilerRecordingReceipt.Format) }) Hash(frame.GetProperty(member), domain);
            var manifest = Data(frame.GetProperty("manifest")).Value; var receipt = Data(frame.GetProperty("receipt")).Value;
            var response = Data(frame.GetProperty("response")).Value;
            manifests.Add(label, manifest); responses.Add(label, response);
            var run = Guid.Parse(manifest.GetProperty("runId").GetString()!); var attempt = Guid.Parse(response.GetProperty("attemptId").GetString()!);
            Assert.Equal(4, run.Version); Assert.Equal(4, attempt.Version); Assert.True(runs.Add(run)); Assert.True(attempts.Add(attempt));
            session ??= manifest.GetProperty("sessionId").GetString(); Assert.Equal(session, manifest.GetProperty("sessionId").GetString());
            Assert.Equal(manifest.GetProperty("runId").GetString(), receipt.GetProperty("runId").GetString());
            Assert.Equal(response.GetProperty("attemptId").GetString(), receipt.GetProperty("attemptId").GetString());
            Assert.Equal(SourceJson.Parse(frame.GetProperty("manifest").GetProperty("digest").GetRawText()), SourceJson.Parse(receipt.GetProperty("manifestDigest").GetRawText()));
            Assert.Equal(SourceJson.Parse(frame.GetProperty("response").GetProperty("digest").GetRawText()), SourceJson.Parse(receipt.GetProperty("hostResponseDigest").GetRawText()));
            var policyData = Data(frame.GetProperty("policy")).Value;
            var policy = new CompilerPredicateCapturePolicy(Enum.Parse<CompilerPredicateCaptureMode>(policyData.GetProperty("mode").GetString()!),
                policyData.GetProperty("maximumBodyBytes").GetInt32(), policyData.GetProperty("maximumDistinctBodies").GetInt32());
            var request = new PredicateCompilerHostRequest(new(new(input), manifest.GetProperty("clientGeneration").GetInt64()), policy);
            Assert.Equal(request.Data.CanonicalText, frame.GetProperty("request").GetProperty("text").GetString());
            Assert.Equal(request.PolicyData.CanonicalText, frame.GetProperty("policy").GetProperty("text").GetString());
            Assert.Equal(request.Core.ExecutionInputData.CanonicalText, frame.GetProperty("baseExecution").GetProperty("text").GetString());
            Assert.Equal(request.Core.Materializations.CanonicalText, frame.GetProperty("materializations").GetProperty("text").GetString());
            if (label == "cancelled")
            {
                Assert.Equal("Cancelled", response.GetProperty("hostStatus").GetString());
                Assert.Equal(JsonValueKind.Null, frame.GetProperty("result").ValueKind); Assert.Equal(JsonValueKind.Null, frame.GetProperty("predicate").ValueKind);
                Assert.Empty(frame.GetProperty("bodies").EnumerateArray()); Assert.Empty(frame.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(JsonValueKind.Null, response.GetProperty("resultDigest").ValueKind); Assert.Equal(JsonValueKind.Null, receipt.GetProperty("predicateRecordDigest").ValueKind);
                continue;
            }
            var actual = PureCoreCompiler.CompileWithPredicateCapture(input, input.Compiler, policy); var record = actual.PredicatePremises!;
            Assert.True(record.IsOwnedBy(actual)); Assert.Equal(control.Result.Data, actual.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
            Assert.Equal(actual.Result.Data.CanonicalText, frame.GetProperty("result").GetProperty("text").GetString());
            Assert.Equal(record.Data.CanonicalText, frame.GetProperty("predicate").GetProperty("text").GetString());
            Hash(frame.GetProperty("result"), "coglatas.compiler-result/1"); Hash(frame.GetProperty("predicate"), ProjectPredicatePremiseRecord.Format);
            var bodyFrames = frame.GetProperty("bodies").EnumerateArray().ToArray(); Assert.Equal(record.Bodies.Count, bodyFrames.Length);
            foreach (var body in bodyFrames)
            {
                Hash(body, ProjectPredicatePremiseRecord.BodyDomain); var digest = body.GetProperty("digest").GetProperty("value").GetString()!;
                Assert.Equal(record.Bodies[digest].CanonicalText, body.GetProperty("text").GetString());
                Assert.Equal(record.Bodies[digest].ToCanonicalBytes().Length, body.GetProperty("bytes").GetInt32());
            }
            var admission = ProjectRootSourceAdmission.Read(actual, actual.Result, actual.Result.ResultDigest)!;
            var diagnostics = actual.Result.Diagnostics.Where(d => d.Location.Context.Equals(input.Source.Context)).ToArray();
            var diagnosticFrames = frame.GetProperty("diagnostics").EnumerateArray().ToArray(); Assert.Equal(diagnostics.Length, diagnosticFrames.Length);
            for (var index = 0; index < diagnostics.Length; index++)
            {
                var diagnostic = diagnostics[index]; var captured = diagnosticFrames[index];
                Hash(captured.GetProperty("diagnostic"), "coglatas.compiler-diagnostic/1");
                Assert.Equal(diagnostic.Data.CanonicalText, captured.GetProperty("diagnostic").GetProperty("text").GetString());
                foreach (var (choice, mode, limit) in new[] {
                    ("manifest", DiagnosticPremiseSnapshotMode.Manifest, 1048576), ("available", DiagnosticPremiseSnapshotMode.AvailableBodies, 1048576),
                    ("budget", DiagnosticPremiseSnapshotMode.AvailableBodies, 1) })
                {
                    var snapshot = ProjectRecordedPredicatePremiseSnapshot.Read(admission, record, diagnostic, actual.Result.ResultDigest, mode, limit);
                    Hash(captured.GetProperty(choice), ProjectRecordedPredicatePremiseSnapshot.Format);
                    Assert.Equal(snapshot.Data.CanonicalText, captured.GetProperty(choice).GetProperty("text").GetString());
                    foreach (var body in snapshot.Bodies) Assert.Same(record.Bodies[body.Key], body.Value);
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
        }
        Assert.NotEqual(manifests["diagnostic"].GetProperty("runId").GetString(), manifests["retry"].GetProperty("runId").GetString());
        Assert.Equal(projection.GetProperty("diagnostic").GetProperty("predicate").GetProperty("text").GetString(), projection.GetProperty("retry").GetProperty("predicate").GetProperty("text").GetString());
    }
}
