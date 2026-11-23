using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.PredicateCompilerHostTests;

namespace Coglatas.Tests.ProjectIde;

public sealed class PredicateObservationCompilerHostTests
{
    internal static (CoreCompilation Control, CompilerHostRequest Core, SourceJson Expected, string Key) ByKey(string key)
    {
        foreach (var row in RootSourceAdmissionTests.Cases())
        {
            var text = (string)row[0];
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.GetProperty("key").GetString() == key) return Fixture(text);
        }
        throw new InvalidOperationException("The unchanged original fixture is missing.");
    }
    public static IEnumerable<object[]> KeyModes()
    {
        foreach (var key in new[] { "fail", "pass", "unknown", "test-assertion-navigation" })
        foreach (var mode in Enum.GetValues<PredicateEvaluationCaptureMode>()) yield return [key, mode];
    }
    internal static PredicateObservationCompilerHostRequest Full(CompilerHostRequest core, int bytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        new(core, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: bytes));

    [Theory]
    [MemberData(nameof(KeyModes))]
    public async Task SeparateDeclaredHostPreservesActualLegacyResultAndBindsRequestManifestAndPolicy(string key, PredicateEvaluationCaptureMode mode)
    {
        var f = ByKey(key); var request = new PredicateObservationCompilerHostRequest(f.Core, new(mode));
        using var session = new LocalPredicateObservationCompilerRecordingSession(Profile(f.Control.Input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var response = await new LocalPredicateObservationCompilerHost(Profile(f.Control.Input)).CompileAsync(request, manifest);
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.Equal(4, response.AttemptId.Version);
        Assert.True(response.IsFor(request)); Assert.True(response.IsFor(manifest));
        Assert.False(response.IsFor(new PredicateObservationCompilerHostRequest(f.Core, request.ObservationPolicy)));
        Assert.False(response.IsFor(session.Prepare(request, Guid.NewGuid())));
        var execution = Assert.IsType<RecordedPredicateCompilation>(response.Execution);
        Assert.True(execution.Observation.IsOwnedBy(execution)); Assert.Same(request.ObservationPolicy, execution.Observation.Policy);
        Assert.Equal(f.Expected, execution.Compilation.Result.Data); Assert.Equal(f.Control.LogicalOperationsConsumed, execution.Compilation.LogicalOperationsConsumed);
        Assert.Equal(response.Digest, ContentDigest.Compute(PredicateObservationCompilerHostResponse.Format, response.Data.ToCanonicalBytes()));
        Assert.Equal(execution.Observation.Digest.Data, SourceJson.Parse(response.Data.Value.GetProperty("observationDigest").GetRawText()));
        Assert.DoesNotContain(response.AttemptId.ToString(), execution.Compilation.Result.Data.CanonicalText);
        Assert.Null(execution.Compilation.PredicatePremises);
        Assert.Equal(mode == PredicateEvaluationCaptureMode.Manifest ? CompilerPredicateObservationCaptureState.ManifestOnly :
            key == "test-assertion-navigation" ? CompilerPredicateObservationCaptureState.NoActualEvaluations :
            CompilerPredicateObservationCaptureState.RecordedInitialRootEvaluations, execution.Observation.State);
        Assert.False(JsonSerializer.SerializeToElement(request).TryGetProperty("Core", out _));
        Assert.False(JsonSerializer.SerializeToElement(response).TryGetProperty("Execution", out _));
        Assert.DoesNotContain("evaluatedChildren", JsonSerializer.Serialize(response));
        Assert.DoesNotContain(f.Control.Input.Source.Data.CanonicalText, JsonSerializer.Serialize(response));
        foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", response.Data.Value.GetProperty(field).GetString());
        Assert.Equal("NotRecorded", response.Data.Value.GetProperty("regionalEvaluation").GetString());
        Assert.Equal("NotAdopted", response.Data.Value.GetProperty("retentionPolicy").GetString());
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("bytes")]
    [InlineData("nodes")]
    [InlineData("evaluations")]
    public async Task EveryNewPolicyDimensionChangesExecutionBindingWithoutChangingTheOldCoreRequestOrTruth(string dimension)
    {
        var input = InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7); var baseline = Full(core);
        var changed = new PredicateObservationCompilerHostRequest(core, dimension switch {
            "mode" => new(),
            "bytes" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1),
            "nodes" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1),
            _ => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1) });
        Assert.NotEqual(baseline.PolicyDigest, changed.PolicyDigest); Assert.NotEqual(baseline.ExecutionInputDigest, changed.ExecutionInputDigest);
        Assert.Equal(core.ExecutionInputDigest, baseline.Core.ExecutionInputDigest); Assert.Equal(core.ExecutionInputDigest, changed.Core.ExecutionInputDigest);
        using var session = new LocalPredicateObservationCompilerRecordingSession(Profile(input));
        var a = await session.ExecuteAsync(session.Prepare(baseline, Guid.NewGuid()), baseline);
        var b = await session.ExecuteAsync(session.Prepare(changed, Guid.NewGuid(), a.Receipt), changed);
        Assert.Equal(a.Response.Execution!.Compilation.Result.Data, b.Response.Execution!.Compilation.Result.Data);
        Assert.NotEqual(a.Response.AttemptId, b.Response.AttemptId); Assert.False(a.Response.IsFor(changed));
        Assert.Equal(a.Receipt.Manifest.BaseExecutionInputDigest, b.Receipt.Manifest.BaseExecutionInputDigest);
    }

    [Fact]
    public async Task UnsupportedProfileAndPreCancellationHaveNoInventedCompilerEnvelopeOrObservation()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        using var declarations = new LocalPredicateObservationCompilerRecordingSession(Profile(input));
        var manifest = declarations.Prepare(request, Guid.NewGuid());
        var unsupported = await new LocalPredicateObservationCompilerHost(Profile(input, noCapabilities: true)).CompileAsync(request, manifest);
        Assert.Equal(CompilerHostStatus.Unsupported, unsupported.Status); Assert.Null(unsupported.Execution);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var cancelled = await declarations.ExecuteAsync(manifest, request, source.Token);
        Assert.Equal(CompilerHostStatus.Cancelled, cancelled.Response.Status); Assert.Null(cancelled.Response.Execution);
        Assert.Null(cancelled.Receipt.ObservationMetadata); Assert.Null(cancelled.Receipt.ObservationRecordDigest);
        Assert.Equal("HostOutcomeWithoutCompilerResult", cancelled.Receipt.Data.Value.GetProperty("recordingState").GetString());
        Assert.Equal(JsonValueKind.Null, cancelled.Response.Data.Value.GetProperty("resultDigest").ValueKind);
        Assert.Equal(JsonValueKind.Null, cancelled.Response.Data.Value.GetProperty("observationDigest").ValueKind);
    }

    [Fact]
    public async Task HostCompletedMeansActualEnvelopeNotCompilerPassOrCompleteCapture()
    {
        var first = InputWithPredicate(); var complete = PureCoreCompiler.Compile(first, first.Compiler);
        var settings = JsonNode.Parse(first.Settings.CanonicalText)!;
        settings["executionProfile"]!["maximumOperations"] = complete.LogicalOperationsConsumed - 1;
        var input = new CompilerInputManifest(first.Source, SourceJson.Parse(settings.ToJsonString())); var request = Full(new(new(input), 7));
        using var session = new LocalPredicateObservationCompilerRecordingSession(Profile(input));
        var invocation = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(CompilerHostStatus.Completed, invocation.Response.Status);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, invocation.Response.Execution!.Compilation.Result.Execution);
        Assert.NotEqual(CompilerVerdict.Pass, invocation.Response.Execution.Compilation.Result.Verdict);
        Assert.Equal(CompilerPredicateObservationCaptureState.CaptureInterrupted, invocation.Response.Execution.Observation.State);
        Assert.Equal(0, invocation.Response.Execution.Observation.RetainedBodyBytes);
        Assert.NotNull(invocation.Receipt.ObservationMetadata);
    }

    [Fact]
    public async Task DirectHostCannotUseAnEqualValueDifferentRequestWithAnEarlierManifest()
    {
        var input = InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7); var request = Full(core); var equal = Full(core);
        using var session = new LocalPredicateObservationCompilerRecordingSession(Profile(input));
        var manifest = session.Prepare(request, Guid.NewGuid()); var host = new LocalPredicateObservationCompilerHost(Profile(input));
        Assert.Equal(request.ExecutionInputDigest, equal.ExecutionInputDigest); Assert.False(manifest.IsFor(equal));
        Assert.Throws<ArgumentException>(() => { _ = host.CompileAsync(equal, manifest); });
        Assert.Equal(0, session.ReservedExecutions);
        var valid = await session.ExecuteAsync(manifest, request); Assert.True(valid.IsFor(request, manifest));
    }
}
