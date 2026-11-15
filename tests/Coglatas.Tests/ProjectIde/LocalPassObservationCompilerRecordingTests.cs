using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.PredicateCompilerHostTests;
using static Coglatas.Tests.ProjectIde.PredicateObservationCompilerHostTests;

namespace Coglatas.Tests.ProjectIde;

public sealed class LocalPassObservationCompilerRecordingTests
{
    public static IEnumerable<object[]> Keys() => new[] { "fail", "pass", "unknown", "test-assertion-navigation" }.Select(key => new object[] { key });

    private static PassObservationCompilerHostRequest Full(CompilerHostRequest core, int bytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        new(core, new(CompilerPassObservationMode.AvailableOriginalJson, maximumBodyBytes: bytes));
    private static ProjectCompilerPassObservationSnapshot Available(RecordedPassCompilation execution) =>
        ProjectCompilerPassObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
            execution.Compilation.Input.InputDigest, execution.Compilation.Result.ResultDigest, CompilerPassObservationSnapshotMode.AvailableOriginalJson);

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task PredeclaredOriginalAndDiagnosticRunsKeepDistinctActualOriginsAndPreserveAllLegacyBytes(string key)
    {
        var f = ByKey(key);
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(f.Control.Input));
        var originalRequest = new PassObservationCompilerHostRequest(f.Core, new());
        var originalManifest = session.Prepare(originalRequest, Guid.NewGuid());
        Assert.Equal("BeforeExecution", originalManifest.Data.Value.GetProperty("capture").GetProperty("declared").GetString());
        var original = await session.ExecuteAsync(originalManifest, originalRequest);
        Assert.True(original.IsFor(originalRequest, originalManifest)); Assert.Equal(CompilerHostStatus.Completed, original.Response.Status);
        Assert.Equal(CompilerPassObservationState.ManifestOnly, original.Response.Execution!.Observation.State);
        var originalMetadata = original.Receipt.ObservationMetadata;
        var fullRequest = Full(f.Core);
        var intended = session.Prepare(fullRequest, Guid.NewGuid(), original.Receipt);
        var diagnostic = await session.ExecuteAsync(intended, fullRequest);
        Assert.True(diagnostic.IsFor(fullRequest, intended));
        Assert.NotEqual(originalManifest.ExecutionInputDigest, intended.ExecutionInputDigest);
        Assert.Equal(originalManifest.BaseExecutionInputDigest, intended.BaseExecutionInputDigest);
        Assert.Equal(f.Expected, original.Response.Execution.Compilation.Result.Data);
        Assert.Equal(f.Expected, diagnostic.Response.Execution!.Compilation.Result.Data);
        Assert.Equal(f.Control.LogicalOperationsConsumed, diagnostic.Response.Execution.Compilation.LogicalOperationsConsumed);
        Assert.Same(originalMetadata, original.Receipt.ObservationMetadata);
        Assert.Equal(4, intended.RunId.Version); Assert.Equal(4, diagnostic.Response.AttemptId.Version);
        Assert.NotEqual(originalManifest.RunId, intended.RunId); Assert.NotEqual(original.Response.AttemptId, diagnostic.Response.AttemptId);
        var origin = intended.Data.Value.GetProperty("origin");
        Assert.Equal(originalManifest.RunId.ToString(), origin.GetProperty("runId").GetString());
        Assert.Equal(original.Response.AttemptId.ToString(), origin.GetProperty("attemptId").GetString());
        Assert.Equal(originalManifest.Digest.Data, SourceJson.Parse(origin.GetProperty("manifestDigest").GetRawText()));
        Assert.Equal(original.Receipt.Digest.Data, SourceJson.Parse(origin.GetProperty("receiptDigest").GetRawText()));
        Assert.Equal(new[] { originalManifest.RunId.ToString() }, intended.Data.Value.GetProperty("knownLineage").EnumerateArray().Select(x => x.GetString()));
        Assert.Same(diagnostic.Response.Execution.Observation.Data, diagnostic.Receipt.ObservationMetadata);
        foreach (var invocation in new[] { original, diagnostic })
        {
            Assert.True(invocation.Receipt.IsFor(invocation.Response));
            Assert.Equal(invocation.Receipt.Digest, ContentDigest.Compute(LocalPassObservationCompilerRecordingReceipt.Format, invocation.Receipt.Data.ToCanonicalBytes()));
            Assert.Equal(invocation.Receipt.Manifest.Digest, ContentDigest.Compute(LocalPassObservationCompilerRecordingManifest.Format, invocation.Receipt.Manifest.Data.ToCanonicalBytes()));
            Assert.Equal("NotRecorded", invocation.Receipt.Data.Value.GetProperty("earlierIntermediateStates").GetString());
            Assert.Equal("NotRecorded", invocation.Receipt.Data.Value.GetProperty("regionalEvaluation").GetString());
            foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", invocation.Receipt.Data.Value.GetProperty(field).GetString());
            Assert.Equal("NotAdopted", invocation.Receipt.Data.Value.GetProperty("retentionPolicy").GetString());
            Assert.DoesNotContain("evaluatedChildren", JsonSerializer.Serialize(invocation));
            Assert.DoesNotContain(f.Control.Input.Source.Data.CanonicalText, JsonSerializer.Serialize(invocation));
        }
        var captured = diagnostic.Response.Execution!;
        Assert.Equal(CompilerPassObservationState.AvailableOriginalJson, captured.Observation.State);
        Assert.Equal(10, captured.Observation.VisitedEvents);
        var view = Available(captured);
        Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, view.State);
        Assert.NotEmpty(view.Bodies);
        Assert.Equal(f.Control.Input.Source.Data, view.Bodies[ProjectSource.Format + ":" + f.Control.Input.Source.Digest.Value]);

    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task CancelledOriginalHostOutcomeHasNoInventedCompilerResultAndOnlyANewRunCanRetry(string key)
    {
        var f = ByKey(key); var request = Full(f.Core);
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(f.Control.Input));
        using var source = new CancellationTokenSource();
        source.Cancel();
        var manifest = session.Prepare(request, Guid.NewGuid());
        var cancelled = await session.ExecuteAsync(manifest, request, source.Token);
        Assert.Equal(CompilerHostStatus.Cancelled, cancelled.Response.Status); Assert.Null(cancelled.Response.Execution);
        Assert.Null(cancelled.Receipt.ObservationMetadata); Assert.Null(cancelled.Receipt.ObservationRecordDigest);
        Assert.Equal(JsonValueKind.Null, cancelled.Receipt.Data.Value.GetProperty("resultDigest").ValueKind);
        var nextManifest = session.Prepare(request, Guid.NewGuid(), cancelled.Receipt);
        var next = await session.ExecuteAsync(nextManifest, request);
        Assert.Equal(f.Expected, next.Response.Execution!.Compilation.Result.Data);
        Assert.NotEqual(cancelled.Response.AttemptId, next.Response.AttemptId);
        Assert.Equal(cancelled.Response.AttemptId.ToString(), nextManifest.Data.Value.GetProperty("origin").GetProperty("attemptId").GetString());
        Assert.False(cancelled.IsFor(request, nextManifest)); Assert.Null(cancelled.Receipt.ObservationMetadata);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("bytes")]
    [InlineData("events")]
    [InlineData("generation")]
    [InlineData("input")]
    [InlineData("unit")]
    [InlineData("calendar")]
    [InlineData("snapshots")]
    [InlineData("authorized")]
    [InlineData("declarations")]
    [InlineData("relations")]
    public async Task ExactRequestPolicyGenerationAndMaterializationChangesCannotUseTheEarlierManifest(string dimension)
    {
        var f = CompilerBindingClosureTests.Graph(withRelation: true);
        var core = new CompilerHostRequest(new(f.Input), 7, f.Snapshots); var request = Full(core);
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(f.Input));
        var original = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var manifest = session.Prepare(request, Guid.NewGuid()); var before = session.ReservedExecutions;
        var changed = dimension switch {
            "mode" => new PassObservationCompilerHostRequest(core, new()),
            "bytes" => Full(core, 1),
            "events" => new(core, new(CompilerPassObservationMode.AvailableOriginalJson, maximumEvents: 1)),
            _ => Full(ChangedCore(core, dimension)) };
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(manifest, changed));
        Assert.Equal(before, session.ReservedExecutions);
        if (!changed.Core.ExecutionInputDigest.Equals(request.Core.ExecutionInputDigest))
            Assert.Throws<ArgumentException>(() => session.Prepare(changed, Guid.NewGuid(), original.Receipt));
        else
            Assert.Equal(original.Response.AttemptId.ToString(), session.Prepare(changed, Guid.NewGuid(), original.Receipt)
                .Data.Value.GetProperty("origin").GetProperty("attemptId").GetString());
        var equal = Full(new(core.Input, 7, core.Snapshots.Reverse()));
        Assert.Equal(request.ExecutionInputDigest, equal.ExecutionInputDigest);
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(manifest, equal));
        Assert.Equal(before, session.ReservedExecutions);
        var valid = await session.ExecuteAsync(manifest, request); Assert.True(valid.IsFor(request, manifest));
    }

    [Fact]
    public async Task WholeGenuineOlderPairCannotSatisfyTheCurrentIntentEvenWithTheSameRequestAndAllContentDigests()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(input));
        var oldManifest = session.Prepare(request, Guid.NewGuid()); var old = await session.ExecuteAsync(oldManifest, request);
        var intended = session.Prepare(request, Guid.NewGuid()); var current = await session.ExecuteAsync(intended, request);
        Assert.True(old.Response.IsFor(request)); Assert.True(current.Response.IsFor(request));
        Assert.Equal(old.Response.Execution!.Compilation.Result.ResultDigest, current.Response.Execution!.Compilation.Result.ResultDigest);
        Assert.Equal(old.Response.Execution.Observation.Digest, current.Response.Execution.Observation.Digest);
        Assert.True(old.IsFor(request, oldManifest)); Assert.False(old.IsFor(request, intended)); Assert.True(current.IsFor(request, intended));
        Assert.False(old.Receipt.IsFor(current.Response)); Assert.False(old.Response.IsFor(intended));
        var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(
            typeof(LocalPassObservationCompilerRecordingReceipt), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { intended, request, old.Response }, null));
        Assert.IsType<ArgumentException>(error.InnerException);
        var pairError = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(
            typeof(PassObservationCompilerRecordedInvocation), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { old.Response, current.Receipt }, null));
        Assert.IsType<ArgumentException>(pairError.InnerException);
    }

    [Fact]
    public async Task ForeignSessionOriginAndDuplicateRunAreRefusedWithoutReservingAnotherExecution()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        using var owner = new LocalPassObservationCompilerRecordingSession(Profile(input));
        using var foreign = new LocalPassObservationCompilerRecordingSession(Profile(input));
        var manifest = owner.Prepare(request, Guid.NewGuid()); var actual = await owner.ExecuteAsync(manifest, request);
        await Assert.ThrowsAsync<ArgumentException>(() => foreign.ExecuteAsync(manifest, request));
        Assert.Throws<ArgumentException>(() => foreign.Prepare(request, Guid.NewGuid(), actual.Receipt));
        Assert.Equal(0, foreign.ReservedExecutions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(manifest, request));
        Assert.Equal(1, owner.ReservedExecutions);
        Assert.Throws<ArgumentException>(() => owner.Prepare(request, manifest.RunId, actual.Receipt));
        Assert.Throws<ArgumentException>(() => owner.Prepare(request, Guid.Empty));
    }

    [Fact]
    public async Task ActualBoundedLineageCannotCycleOrGrowPastTheSessionCapacity()
    {
        var input = InputWithPredicate(); var request = new PassObservationCompilerHostRequest(new(new(input), 7), new());
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(input));
        LocalPassObservationCompilerRecordingReceipt? origin = null;
        for (var index = 0; index < LocalPassObservationCompilerRecordingSession.MaximumExecutions; index++)
        {
            var manifest = session.Prepare(request, Guid.NewGuid(), origin);
            Assert.Equal(index, manifest.Data.Value.GetProperty("knownLineage").GetArrayLength());
            var actual = await session.ExecuteAsync(manifest, request);
            Assert.True(actual.IsFor(request, manifest)); origin = actual.Receipt;
        }
        Assert.Equal(LocalPassObservationCompilerRecordingSession.MaximumExecutions, session.ReservedExecutions);
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), origin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request));
        Assert.Equal(LocalPassObservationCompilerRecordingSession.MaximumExecutions, session.ReservedExecutions);
    }

    [Fact]
    public async Task ConcurrentDuplicateReservationProducesOnlyOneActualReceipt()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var first = session.ExecuteAsync(manifest, request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(manifest, request));
        var actual = await first;
        Assert.True(actual.IsFor(request, manifest)); Assert.Equal(1, session.ReservedExecutions);
    }

    [Fact]
    public async Task InvalidTransferFailsBeforeReservationAndDisposalDoesNotClaimPhysicalErasureOfReturnedFrames()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        var session = new LocalPassObservationCompilerRecordingSession(Profile(input));
        var invalid = Full(new(new CompilerInputTransfer(Encoding.UTF8.GetBytes("invalid"), input.ToEnvelope().ToCanonicalBytes(), input.InputDigest), 7));
        Assert.Throws<FormatException>(() => session.Prepare(invalid, Guid.NewGuid())); Assert.Equal(0, session.ReservedExecutions);
        var manifest = session.Prepare(request, Guid.NewGuid()); var actual = await session.ExecuteAsync(manifest, request);
        var execution = actual.Response.Execution!;
        var original = Available(execution);
        Assert.NotEmpty(original.Bodies);
        session.Dispose();
        Assert.Equal(0, session.ReservedExecutions);
        Assert.Throws<ObjectDisposedException>(() => session.Prepare(request, Guid.NewGuid()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ExecuteAsync(manifest, request));
        Assert.NotEmpty(original.Bodies); Assert.True(actual.IsFor(request, manifest));
        Assert.Equal("NotEvaluated", actual.Receipt.Data.Value.GetProperty("authority").GetString());
        Assert.Equal("NotAdopted", actual.Receipt.Data.Value.GetProperty("retentionPolicy").GetString());
    }

    [Theory]
    [InlineData(CompilerPassObservationMode.Manifest, 1048576, 16, CompilerPassObservationState.ManifestOnly)]
    [InlineData(CompilerPassObservationMode.AvailableOriginalJson, 1, 16, CompilerPassObservationState.BodyBudgetExceeded)]
    [InlineData(CompilerPassObservationMode.AvailableOriginalJson, 1048576, 1, CompilerPassObservationState.EventLimitExceeded)]
    public async Task NewDiagnosticCaptureCannotFillTheOriginalHostRunsPredeclaredGap(CompilerPassObservationMode mode, int bytes, int events, CompilerPassObservationState expected)
    {
        var input = InputWithPredicate();
        var core = new CompilerHostRequest(new(input), 7);
        var originalRequest = new PassObservationCompilerHostRequest(core, new(mode, bytes, events));
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(input));
        var originalManifest = session.Prepare(originalRequest, Guid.NewGuid());
        var original = await session.ExecuteAsync(originalManifest, originalRequest);
        var originalExecution = original.Response.Execution!;
        var originalMetadata = originalExecution.Observation.Data;
        Assert.Equal(expected, originalExecution.Observation.State);
        var before = Available(originalExecution);
        Assert.Equal(CompilerPassObservationSnapshotState.NoRecordedOriginalBodies, before.State);
        Assert.Empty(before.Bodies);
        var diagnosticRequest = Full(core);
        var diagnosticManifest = session.Prepare(diagnosticRequest, Guid.NewGuid(), original.Receipt);
        var diagnostic = await session.ExecuteAsync(diagnosticManifest, diagnosticRequest);
        Assert.NotEqual(originalManifest.RunId, diagnosticManifest.RunId);
        Assert.NotEqual(original.Response.AttemptId, diagnostic.Response.AttemptId);
        Assert.NotEqual(originalManifest.ExecutionInputDigest, diagnosticManifest.ExecutionInputDigest);
        Assert.Equal(originalManifest.BaseExecutionInputDigest, diagnosticManifest.BaseExecutionInputDigest);
        Assert.Equal(originalExecution.Compilation.Result.Data, diagnostic.Response.Execution!.Compilation.Result.Data);
        Assert.NotEmpty(Available(diagnostic.Response.Execution).Bodies);
        var after = ProjectCompilerPassObservationSnapshot.Read(originalExecution, originalExecution.Compilation, originalExecution.Observation,
            input.InputDigest, originalExecution.Compilation.Result.ResultDigest, CompilerPassObservationSnapshotMode.AvailableOriginalJson,
            ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
        Assert.Equal(CompilerPassObservationSnapshotState.NoRecordedOriginalBodies, after.State);
        Assert.Empty(after.Bodies);
        Assert.Equal(expected, after.OriginalCaptureState);
        Assert.Same(originalMetadata, originalExecution.Observation.Data);
        Assert.Same(originalMetadata, original.Receipt.ObservationMetadata);
    }

    [Fact]
    public async Task CompletedHostCanRetainActualPassBodiesForAResourceExhaustedCompilerWithoutClaimingSuccessfulEvidence()
    {
        var first = InputWithPredicate();
        var complete = PureCoreCompiler.Compile(first, first.Compiler);
        var settings = JsonNode.Parse(first.Settings.CanonicalText)!;
        settings["executionProfile"]!["maximumOperations"] = complete.LogicalOperationsConsumed - 1;
        var input = new CompilerInputManifest(first.Source, SourceJson.Parse(settings.ToJsonString()));
        var request = Full(new(new(input), 7));
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(input));
        var invocation = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(CompilerHostStatus.Completed, invocation.Response.Status);
        var execution = invocation.Response.Execution!;
        Assert.Equal(CompilerExecutionState.ResourceExhausted, execution.Compilation.Result.Execution);
        Assert.NotEqual(CompilerVerdict.Pass, execution.Compilation.Result.Verdict);
        Assert.False(execution.Compilation.Result.EvidenceComplete);
        Assert.Equal(CompilerPassObservationState.AvailableOriginalJson, execution.Observation.State);
        Assert.Equal(10, execution.Observation.VisitedEvents);
        Assert.NotEmpty(Available(execution).Bodies);
        Assert.Same(execution.Observation.Data, invocation.Receipt.ObservationMetadata);
        Assert.Equal("NotEvaluated", invocation.Receipt.Data.Value.GetProperty("eligibility").GetString());
    }

    [Fact]
    public async Task UnsupportedHostProfileHasNoInventedCompilationOrObservation()
    {
        var input = InputWithPredicate();
        var request = Full(new(new(input), 7));
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(input, noCapabilities: true));
        var invocation = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(CompilerHostStatus.Unsupported, invocation.Response.Status);
        Assert.Null(invocation.Response.Execution);
        Assert.Null(invocation.Receipt.ObservationMetadata);
        Assert.Null(invocation.Receipt.ObservationRecordDigest);
        Assert.Equal("HostOutcomeWithoutCompilerResult", invocation.Receipt.Data.Value.GetProperty("recordingState").GetString());
        Assert.Equal(JsonValueKind.Null, invocation.Response.Data.Value.GetProperty("resultDigest").ValueKind);
    }

    [Fact]
    public async Task ExistingNormalAndArrayHostProtocolsRemainSeparateFromTheNewObservationResponse()
    {
        var input = InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7);
        var ordinary = await new LocalNetCompilerHost(Profile(input)).CompileAsync(core);
        var arrays = await new LocalPredicateCompilerHost(Profile(input)).CompileAsync(new(core, new(CompilerPredicateCaptureMode.OriginalRootArrays)));
        var request = Full(core);
        using var session = new LocalPassObservationCompilerRecordingSession(Profile(input));
        var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(ordinary.Compilation!.Result.Data, actual.Response.Execution!.Compilation.Result.Data);
        Assert.Equal(arrays.Compilation!.Result.Data, actual.Response.Execution.Compilation.Result.Data);
        Assert.Null(actual.Response.Execution.Compilation.PredicatePremises);
        Assert.Null(ordinary.Compilation.PredicatePremises); Assert.NotNull(arrays.Compilation.PredicatePremises);
        Assert.DoesNotContain("observationDigest", ordinary.Execution!.Data.CanonicalText);
        Assert.DoesNotContain("observationDigest", arrays.Data.CanonicalText);
    }
}
