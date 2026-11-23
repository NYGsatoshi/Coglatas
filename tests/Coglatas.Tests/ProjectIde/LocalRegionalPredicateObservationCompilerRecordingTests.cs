using System.Reflection;
using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.RegionalPredicateObservationCompilerHostTests;

namespace Coglatas.Tests.ProjectIde;

public sealed class LocalRegionalPredicateObservationCompilerRecordingTests
{
    public static IEnumerable<object[]> Keys() => new[] { "fail", "pass", "unknown", "test-assertion-navigation" }.Select(key => new object[] { key });

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task PredeclaredOriginalAndDiagnosticRunsKeepDistinctActualOriginsAndPreserveAllLegacyBytes(string key)
    {
        var f = ByKey(key);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Control.Input));
        var originalRequest = new RegionalPredicateObservationCompilerHostRequest(f.Core, new());
        var originalManifest = session.Prepare(originalRequest, Guid.NewGuid());
        Assert.Equal("BeforeExecution", originalManifest.Data.Value.GetProperty("capture").GetProperty("declared").GetString());
        var original = await session.ExecuteAsync(originalManifest, originalRequest);
        Assert.True(original.IsFor(originalRequest, originalManifest)); Assert.Equal(CompilerHostStatus.Completed, original.Response.Status);
        Assert.Equal(CompilerRegionalPredicateCaptureState.ManifestOnly, original.Response.Execution!.Observation.State);
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
            Assert.Equal(invocation.Receipt.Digest, ContentDigest.Compute(LocalRegionalPredicateObservationCompilerRecordingReceipt.Format, invocation.Receipt.Data.ToCanonicalBytes()));
            Assert.Equal(invocation.Receipt.Manifest.Digest, ContentDigest.Compute(LocalRegionalPredicateObservationCompilerRecordingManifest.Format, invocation.Receipt.Manifest.Data.ToCanonicalBytes()));
            Assert.Equal("NotRecorded", invocation.Receipt.Data.Value.GetProperty("earlierIntermediateStates").GetString());
            Assert.Equal("ActualRegionalEvaluationsOnly", invocation.Receipt.Data.Value.GetProperty("captureScope").GetString());
            Assert.Equal("NotRecorded", invocation.Receipt.Data.Value.GetProperty("initialRootEvaluation").GetString());
            foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", invocation.Receipt.Data.Value.GetProperty(field).GetString());
            Assert.Equal("NotAdopted", invocation.Receipt.Data.Value.GetProperty("retentionPolicy").GetString());
            Assert.DoesNotContain("evaluatedChildren", JsonSerializer.Serialize(invocation));
            Assert.DoesNotContain(f.Control.Input.Source.Data.CanonicalText, JsonSerializer.Serialize(invocation));
        }
        if (key is "fail" or "unknown")
        {
            var execution = diagnostic.Response.Execution;
            var view = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
                Assert.Single(execution.Compilation.Dependencies!.Regions),
                Assert.Single(Assert.Single(execution.Compilation.Dependencies.Regions).Diagnostics), CompilerRegionalPredicateSnapshotMode.AvailableOperators);
            Assert.Single(view.Bodies); Assert.Single(view.EvaluationFrames);
        }
        else if (key == "pass") Assert.Empty(diagnostic.Response.Execution.Compilation.Result.Diagnostics);
        else Assert.Equal(CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations, diagnostic.Response.Execution.Observation.State);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task CancelledOriginalHostOutcomeHasNoInventedCompilerResultAndOnlyANewRunCanRetry(string key)
    {
        var f = ByKey(key); var request = Full(f.Core);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Control.Input));
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
    [InlineData("nodes")]
    [InlineData("evaluations")]
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
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        var original = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var manifest = session.Prepare(request, Guid.NewGuid()); var before = session.ReservedExecutions;
        var changed = dimension switch {
            "mode" => new RegionalPredicateObservationCompilerHostRequest(core, new()),
            "bytes" => Full(core, 1),
            "nodes" => new(core, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1)),
            "evaluations" => new(core, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1)),
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
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        var oldManifest = session.Prepare(request, Guid.NewGuid()); var old = await session.ExecuteAsync(oldManifest, request);
        var intended = session.Prepare(request, Guid.NewGuid()); var current = await session.ExecuteAsync(intended, request);
        Assert.True(old.Response.IsFor(request)); Assert.True(current.Response.IsFor(request));
        Assert.Equal(old.Response.Execution!.Compilation.Result.ResultDigest, current.Response.Execution!.Compilation.Result.ResultDigest);
        Assert.Equal(old.Response.Execution.Observation.Digest, current.Response.Execution.Observation.Digest);
        Assert.True(old.IsFor(request, oldManifest)); Assert.False(old.IsFor(request, intended)); Assert.True(current.IsFor(request, intended));
        Assert.False(old.Receipt.IsFor(current.Response)); Assert.False(old.Response.IsFor(intended));
        var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(
            typeof(LocalRegionalPredicateObservationCompilerRecordingReceipt), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { intended, request, old.Response }, null));
        Assert.IsType<ArgumentException>(error.InnerException);
        var pairError = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(
            typeof(RegionalPredicateObservationCompilerRecordedInvocation), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { old.Response, current.Receipt }, null));
        Assert.IsType<ArgumentException>(pairError.InnerException);
    }

    [Fact]
    public async Task ForeignSessionOriginAndDuplicateRunAreRefusedWithoutReservingAnotherExecution()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        using var owner = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        using var foreign = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
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
        var input = InputWithPredicate(); var request = new RegionalPredicateObservationCompilerHostRequest(new(new(input), 7), new());
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        LocalRegionalPredicateObservationCompilerRecordingReceipt? origin = null;
        for (var index = 0; index < LocalRegionalPredicateObservationCompilerRecordingSession.MaximumExecutions; index++)
        {
            var manifest = session.Prepare(request, Guid.NewGuid(), origin);
            Assert.Equal(index, manifest.Data.Value.GetProperty("knownLineage").GetArrayLength());
            var actual = await session.ExecuteAsync(manifest, request);
            Assert.True(actual.IsFor(request, manifest)); origin = actual.Receipt;
        }
        Assert.Equal(LocalRegionalPredicateObservationCompilerRecordingSession.MaximumExecutions, session.ReservedExecutions);
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), origin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request));
        Assert.Equal(LocalRegionalPredicateObservationCompilerRecordingSession.MaximumExecutions, session.ReservedExecutions);
    }

    [Fact]
    public async Task ConcurrentDuplicateReservationProducesOnlyOneActualReceipt()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
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
        var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        var invalid = Full(new(new CompilerInputTransfer(Encoding.UTF8.GetBytes("invalid"), input.ToEnvelope().ToCanonicalBytes(), input.InputDigest), 7));
        Assert.Throws<FormatException>(() => session.Prepare(invalid, Guid.NewGuid())); Assert.Equal(0, session.ReservedExecutions);
        var manifest = session.Prepare(request, Guid.NewGuid()); var actual = await session.ExecuteAsync(manifest, request);
        var execution = actual.Response.Execution!;
        var region = Assert.Single(execution.Compilation.Dependencies!.Regions);
        var diagnostic = Assert.Single(region.Diagnostics);
        var original = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
            region, diagnostic, CompilerRegionalPredicateSnapshotMode.AvailableOperators);
        Assert.Single(original.Bodies);
        session.Dispose();
        Assert.Equal(0, session.ReservedExecutions);
        Assert.Throws<ObjectDisposedException>(() => session.Prepare(request, Guid.NewGuid()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ExecuteAsync(manifest, request));
        Assert.Single(original.Bodies); Assert.True(actual.IsFor(request, manifest));
        Assert.Equal("NotEvaluated", actual.Receipt.Data.Value.GetProperty("authority").GetString());
        Assert.Equal("NotAdopted", actual.Receipt.Data.Value.GetProperty("retentionPolicy").GetString());
    }

    [Fact]
    public async Task ExistingNormalAndArrayHostProtocolsRemainSeparateFromTheNewObservationResponse()
    {
        var input = InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7);
        var ordinary = await new LocalNetCompilerHost(Profile(input)).CompileAsync(core);
        var arrays = await new LocalPredicateCompilerHost(Profile(input)).CompileAsync(new(core, new(CompilerPredicateCaptureMode.OriginalRootArrays)));
        var request = Full(core);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(ordinary.Compilation!.Result.Data, actual.Response.Execution!.Compilation.Result.Data);
        Assert.Equal(arrays.Compilation!.Result.Data, actual.Response.Execution.Compilation.Result.Data);
        Assert.Null(actual.Response.Execution.Compilation.PredicatePremises);
        Assert.Null(ordinary.Compilation.PredicatePremises); Assert.NotNull(arrays.Compilation.PredicatePremises);
        Assert.DoesNotContain("observationDigest", ordinary.Execution!.Data.CanonicalText);
        Assert.DoesNotContain("observationDigest", arrays.Data.CanonicalText);
    }
}
