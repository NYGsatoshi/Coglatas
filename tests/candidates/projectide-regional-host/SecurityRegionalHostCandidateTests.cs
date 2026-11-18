using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent local host/session ownership controls, without runtime or remote authority claims.</summary>
public sealed class SecurityRegionalHostCandidateTests
{
    [Fact]
    public async Task PrepareBindsExactCoreMaterializationsPolicyGenerationAndRunBeforeExecution()
    {
        var fixture = Create();
        var request = Request(fixture, new(), generation: 17);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var runId = Guid.NewGuid();
        var manifest = session.Prepare(request, runId);
        Assert.Equal(0, session.ReservedExecutions);
        Assert.True(manifest.IsFor(request));
        Assert.Equal(runId, manifest.RunId);
        Assert.Equal(17, manifest.ClientGeneration);
        Assert.Equal(request.ExecutionInputDigest, manifest.ExecutionInputDigest);
        Assert.Equal(request.Core.ExecutionInputDigest, manifest.BaseExecutionInputDigest);
        Assert.Equal(request.Core.MaterializationDigest, manifest.MaterializationsDigest);
        Assert.Equal(request.PolicyDigest, manifest.PolicyDigest);
        Assert.Equal("BeforeExecution", manifest.Data.Value.GetProperty("capture").GetProperty("declared").GetString());
        Assert.Equal(JsonValueKind.Null, manifest.Data.Value.GetProperty("origin").ValueKind);
        Assert.Empty(manifest.Data.Value.GetProperty("knownLineage").EnumerateArray());
        Assert.Equal(manifest.Digest, ContentDigest.Compute(
            LocalRegionalPredicateObservationCompilerRecordingManifest.Format, manifest.Data.ToCanonicalBytes()));
        Assert.Equal(request.ExecutionInputDigest, ContentDigest.Compute(
            RegionalPredicateObservationCompilerHostRequest.Format, request.Data.ToCanonicalBytes()));
        Assert.Equal("Manifest", request.PolicyData.Value.GetProperty("mode").GetString());
        var original = await session.ExecuteAsync(manifest, request);
        var originalReceipt = original.Receipt.Data.ToCanonicalBytes();
        Assert.Equal(CompilerRegionalPredicateCaptureState.ManifestOnly, original.Response.Execution!.Observation.State);
        var full = new RegionalPredicateObservationCompilerHostRequest(request.Core,
            new(PredicateEvaluationCaptureMode.ObservedOperators));
        var nextManifest = session.Prepare(full, Guid.NewGuid(), original.Receipt);
        var next = await session.ExecuteAsync(nextManifest, full);
        AssertOwned(full, nextManifest, next);
        AssertOrigin(original, nextManifest);
        AssertCompleted(fixture, next);
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, next.Response.Execution!.Observation.State);
        Assert.Equal(originalReceipt, original.Receipt.Data.ToCanonicalBytes());
        Assert.Equal(CompilerRegionalPredicateCaptureState.ManifestOnly, original.Response.Execution.Observation.State);
        Assert.Equal(0, original.Response.Execution.Observation.RetainedBodyBytes);
        Assert.False(original.IsFor(full, nextManifest));
    }

    [Fact]
    public async Task ActualTrueFalseAndUnknownRegionalRunsPreserveEveryOriginalCompilerAxis()
    {
        foreach (bool? truth in new bool?[] { false, true, null })
        {
            var fixture = Create(truth);
            var request = Request(fixture);
            using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
            var manifest = session.Prepare(request, Guid.NewGuid());
            var invocation = await session.ExecuteAsync(manifest, request);
            AssertOwned(request, manifest, invocation);
            var execution = AssertCompleted(fixture, invocation);
            Assert.Equal(2, execution.Observation.CompletedRegionCalls);
            Assert.Equal(2, execution.Observation.CompletedEvaluations);
            Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, execution.Observation.State);
            Assert.Same(request.ObservationPolicy, execution.Observation.Policy);
            Assert.Equal(execution.Compilation.Result.Execution.ToString(),
                invocation.Response.Data.Value.GetProperty("compilerExecution").GetString());
            Assert.Equal(execution.Compilation.Result.Coverage.ToString(),
                invocation.Response.Data.Value.GetProperty("compilerCoverage").GetString());
            Assert.Equal(execution.Compilation.Result.Verdict.ToString(),
                invocation.Response.Data.Value.GetProperty("compilerVerdict").GetString());
            Assert.Equal(execution.Compilation.Result.EvidenceComplete,
                invocation.Response.Data.Value.GetProperty("evidenceComplete").GetBoolean());
            if (truth == true) Assert.All(execution.Compilation.Dependencies!.Regions, region => Assert.Empty(region.Diagnostics));
            else Assert.All(execution.Compilation.Dependencies!.Regions,
                region => Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, View(execution, region).State));
        }
    }

    [Fact]
    public async Task AnEqualContentNewRequestCannotExecuteTheEarlierDeclaration()
    {
        var fixture = Create();
        var request = Request(fixture);
        var equal = new RegionalPredicateObservationCompilerHostRequest(request.Core, request.ObservationPolicy);
        Assert.Equal(request.ExecutionInputDigest, equal.ExecutionInputDigest);
        Assert.Equal(request.PolicyDigest, equal.PolicyDigest);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        Assert.False(manifest.IsFor(equal));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(manifest, equal));
        Assert.Equal(0, session.ReservedExecutions);
        var host = new LocalRegionalPredicateObservationCompilerHost(Profile(fixture.Input));
        Assert.Throws<ArgumentException>(() => { _ = host.CompileAsync(equal, manifest); });
        Assert.Equal(0, session.ReservedExecutions);
        AssertOwned(request, manifest, await session.ExecuteAsync(manifest, request));
    }

    [Fact]
    public async Task AllFourPolicyDimensionsChangeRegionalExecutionButPreserveTheOldCoreInput()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var initial = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var originalReceipt = initial.Receipt.Data.ToCanonicalBytes();
        var policies = new[]
        {
            new CompilerRegionalPredicateObservationPolicy(),
            new(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1),
            new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1),
            new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1)
        };
        foreach (var policy in policies)
        {
            var changed = new RegionalPredicateObservationCompilerHostRequest(request.Core, policy);
            Assert.NotEqual(request.PolicyDigest, changed.PolicyDigest);
            Assert.NotEqual(request.ExecutionInputDigest, changed.ExecutionInputDigest);
            Assert.Equal(request.Core.ExecutionInputDigest, changed.Core.ExecutionInputDigest);
            var priorManifest = session.Prepare(request, Guid.NewGuid());
            var reserved = session.ReservedExecutions;
            await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(priorManifest, changed));
            Assert.Equal(reserved, session.ReservedExecutions);
            var intended = session.Prepare(changed, Guid.NewGuid(), initial.Receipt);
            var actual = await session.ExecuteAsync(intended, changed);
            AssertOwned(changed, intended, actual);
            AssertCompleted(fixture, actual);
            Assert.NotEqual(initial.Response.AttemptId, actual.Response.AttemptId);
            Assert.Equal(originalReceipt, initial.Receipt.Data.ToCanonicalBytes());
        }
    }

    [Fact]
    public async Task ClientGenerationIsSeparatelyBoundEvenWhenEveryExecutionDigestMatches()
    {
        var fixture = Create();
        var original = Request(fixture, generation: 17);
        var changed = Request(fixture, generation: 18);
        Assert.Equal(original.Core.ExecutionInputDigest, changed.Core.ExecutionInputDigest);
        Assert.Equal(original.ExecutionInputDigest, changed.ExecutionInputDigest);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var originalManifest = session.Prepare(original, Guid.NewGuid());
        var earlier = await session.ExecuteAsync(originalManifest, original);
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(session.Prepare(original, Guid.NewGuid()), changed));
        Assert.Equal(1, session.ReservedExecutions);
        Assert.False(earlier.Response.IsFor(changed));
        var nextManifest = session.Prepare(changed, Guid.NewGuid(), earlier.Receipt);
        var next = await session.ExecuteAsync(nextManifest, changed);
        Assert.Equal(18, next.Response.ClientGeneration);
        Assert.Equal(18, nextManifest.ClientGeneration);
        AssertOwned(changed, nextManifest, next);
        Assert.Equal(earlier.Response.Execution!.Compilation.Result.Data, next.Response.Execution!.Compilation.Result.Data);
        AssertOrigin(earlier, nextManifest);
    }

    [Fact]
    public async Task ChangedSnapshotAdmissionVersionAndCompletenessCannotBorrowAnEarlierOrigin()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var original = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var supply = fixture.Snapshots[0];
        var alternatives = new[]
        {
            Array.Empty<CompilerMaterializedSnapshot>(),
            new[] { new CompilerMaterializedSnapshot(supply.SnapshotId, "different-version", supply.Snapshot) },
            new[] { new CompilerMaterializedSnapshot(supply.SnapshotId, supply.Version, supply.Snapshot with { IsAuthorized = false }) },
            new[] { new CompilerMaterializedSnapshot(supply.SnapshotId, supply.Version, supply.Snapshot with { DeclarationsComplete = false }) },
            new[] { new CompilerMaterializedSnapshot(supply.SnapshotId, supply.Version, supply.Snapshot with { RelationsComplete = false }) }
        };
        foreach (var snapshots in alternatives)
        {
            var changed = new RegionalPredicateObservationCompilerHostRequest(
                new(request.Core.Input, request.ClientGeneration, snapshots), request.ObservationPolicy);
            Assert.NotEqual(request.Core.MaterializationDigest, changed.Core.MaterializationDigest);
            Assert.NotEqual(request.Core.ExecutionInputDigest, changed.Core.ExecutionInputDigest);
            Assert.Throws<ArgumentException>(() => session.Prepare(changed, Guid.NewGuid(), original.Receipt));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), changed));
            Assert.Equal(1, session.ReservedExecutions);
        }
        Assert.True(original.Receipt.IsFor(original.Response));
    }

    [Fact]
    public async Task ActualUnitAndCalendarMaterializationsAreBoundBeforeReservation()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var original = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var unitData = Json(new { unitId = "unit.security-mass", version = 1, dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } });
        var unit = new UnitDefinition(unitData, ContentDigest.Compute("coglatas.unit-definition/1", unitData.ToCanonicalBytes()));
        var alternatives = new[]
        {
            new CompilerHostRequest(request.Core.Input, request.ClientGeneration, request.Core.Snapshots, [unit]),
            new CompilerHostRequest(request.Core.Input, request.ClientGeneration, request.Core.Snapshots, calendars: [Calendar()])
        };
        foreach (var core in alternatives)
        {
            var changed = new RegionalPredicateObservationCompilerHostRequest(core, request.ObservationPolicy);
            Assert.NotEqual(request.Core.MaterializationDigest, core.MaterializationDigest);
            Assert.NotEqual(request.ExecutionInputDigest, changed.ExecutionInputDigest);
            Assert.Throws<ArgumentException>(() => session.Prepare(changed, Guid.NewGuid(), original.Receipt));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), changed));
            Assert.Equal(1, session.ReservedExecutions);
        }
    }

    [Fact]
    public async Task ChangedSourceInputCannotBorrowAnOwnedReceiptFromThePreviousInput()
    {
        var before = Create();
        var after = Create(true);
        var originalRequest = Request(before);
        var changed = Request(after);
        Assert.NotEqual(originalRequest.Core.Input.ExpectedInputDigest, changed.Core.Input.ExpectedInputDigest);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(before.Input));
        var original = await session.ExecuteAsync(session.Prepare(originalRequest, Guid.NewGuid()), originalRequest);
        Assert.Throws<ArgumentException>(() => session.Prepare(changed, Guid.NewGuid(), original.Receipt));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(session.Prepare(originalRequest, Guid.NewGuid()), changed));
        Assert.Equal(1, session.ReservedExecutions);
        var own = session.Prepare(changed, Guid.NewGuid());
        AssertOwned(changed, own, await session.ExecuteAsync(own, changed));
    }

    [Fact]
    public async Task PreparingManyDeclarationsDoesNotReserveExecutionOrRewriteAnEarlierManifest()
    {
        var fixture = Create(includeDependency: false);
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var first = session.Prepare(request, Guid.NewGuid());
        var original = first.Data.ToCanonicalBytes();
        for (var index = 0; index < 64; index++)
        {
            var later = session.Prepare(request, Guid.NewGuid());
            Assert.True(later.IsFor(request));
            Assert.NotEqual(first.RunId, later.RunId);
        }
        Assert.Equal(0, session.ReservedExecutions);
        Assert.Equal(original, first.Data.ToCanonicalBytes());
        AssertOwned(request, first, await session.ExecuteAsync(first, request));
        Assert.Equal(1, session.ReservedExecutions);
    }

    [Fact]
    public async Task DirectHostAttemptsDoNotReserveTheSessionOrInventAPairedReceipt()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var host = new LocalRegionalPredicateObservationCompilerHost(Profile(fixture.Input));
        var first = await host.CompileAsync(request, manifest);
        var second = await host.CompileAsync(request, manifest);
        Assert.Equal(CompilerHostStatus.Completed, first.Status);
        Assert.Equal(CompilerHostStatus.Completed, second.Status);
        Assert.True(first.IsFor(request));
        Assert.True(first.IsFor(manifest));
        Assert.NotEqual(first.AttemptId, second.AttemptId);
        Assert.Equal(first.Execution!.Compilation.Result.Data, second.Execution!.Compilation.Result.Data);
        Assert.Equal(0, session.ReservedExecutions);
        var recorded = await session.ExecuteAsync(manifest, request);
        AssertOwned(request, manifest, recorded);
        Assert.Equal(1, session.ReservedExecutions);
        Assert.NotEqual(first.AttemptId, recorded.Response.AttemptId);
        Assert.False(recorded.Receipt.IsFor(first));
        Assert.False(recorded.Receipt.IsFor(second));
        Assert.Equal("NotAllocated", recorded.Response.Execution!.Observation.Data.Value.GetProperty("compilerRunIdentity").GetString());
    }

    [Fact]
    public async Task WholeGenuineEqualOlderInvocationCannotSatisfyTheNewIntendedManifest()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var oldManifest = session.Prepare(request, Guid.NewGuid());
        var earlier = await session.ExecuteAsync(oldManifest, request);
        var currentManifest = session.Prepare(request, Guid.NewGuid());
        var current = await session.ExecuteAsync(currentManifest, request);
        Assert.Equal(earlier.Response.Execution!.Compilation.Result.ResultDigest,
            current.Response.Execution!.Compilation.Result.ResultDigest);
        Assert.Equal(earlier.Response.Execution.Observation.Digest, current.Response.Execution.Observation.Digest);
        Assert.True(earlier.Response.IsFor(request));
        Assert.True(current.Response.IsFor(request));
        Assert.False(earlier.IsFor(request, currentManifest));
        Assert.False(current.IsFor(request, oldManifest));
        Assert.False(earlier.Receipt.IsFor(current.Response));
        Assert.False(current.Receipt.IsFor(earlier.Response));
        Assert.False(earlier.Response.IsFor(currentManifest));
        AssertOwned(request, currentManifest, current);
    }

    [Fact]
    public async Task ARecreatedEqualManifestCannotSubstituteItsNewOwnershipTokenOrReserveTheRunTwice()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var runId = Guid.NewGuid();
        var actual = session.Prepare(request, runId);
        var equal = session.Prepare(request, runId);
        Assert.NotSame(actual, equal);
        Assert.Equal(actual.Data, equal.Data);
        Assert.Equal(actual.Digest, equal.Digest);
        Assert.True(actual.IsFor(request));
        Assert.True(equal.IsFor(request));
        var invocation = await session.ExecuteAsync(actual, request);
        AssertOwned(request, actual, invocation);
        Assert.False(invocation.Response.IsFor(equal));
        Assert.False(invocation.IsFor(request, equal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(equal, request));
        Assert.Equal(1, session.ReservedExecutions);
    }

    [Fact]
    public async Task ForeignSessionsCannotExecuteDeclarationsOrBorrowGenuineOrigins()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var owner = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        using var foreign = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var manifest = owner.Prepare(request, Guid.NewGuid());
        var actual = await owner.ExecuteAsync(manifest, request);
        await Assert.ThrowsAsync<ArgumentException>(() => foreign.ExecuteAsync(manifest, request));
        Assert.Throws<ArgumentException>(() => foreign.Prepare(request, Guid.NewGuid(), actual.Receipt));
        Assert.Equal(0, foreign.ReservedExecutions);
        Assert.Equal(1, owner.ReservedExecutions);
        var theirManifest = foreign.Prepare(request, Guid.NewGuid());
        var theirActual = await foreign.ExecuteAsync(theirManifest, request);
        Assert.False(actual.IsFor(request, theirManifest));
        Assert.False(theirActual.Receipt.IsFor(actual.Response));
        Assert.Equal(actual.Response.Execution!.Observation.Data, theirActual.Response.Execution!.Observation.Data);
    }

    [Fact]
    public async Task ConcurrentDuplicateExecutionsReserveExactlyOneActualAttempt()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RegionalPredicateObservationCompilerRecordedInvocation?> Attempt()
        {
            await start.Task;
            try { return await session.ExecuteAsync(manifest, request); }
            catch (InvalidOperationException) { return null; }
        }
        var attempts = Enumerable.Range(0, 4).Select(_ => Attempt()).ToArray();
        start.SetResult(true);
        var outcomes = await Task.WhenAll(attempts);
        var actual = Assert.IsType<RegionalPredicateObservationCompilerRecordedInvocation>(
            Assert.Single(outcomes, value => value is not null));
        Assert.Equal(3, outcomes.Count(value => value is null));
        Assert.Equal(1, session.ReservedExecutions);
        AssertOwned(request, manifest, actual);
        AssertCompleted(fixture, actual);
    }

    [Fact]
    public async Task ThirtyTwoActualRunsRetainThirtyOneAncestorsAndRefuseFurtherCapacity()
    {
        var fixture = Create(includeDependency: false);
        var request = Request(fixture, new());
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        LocalRegionalPredicateObservationCompilerRecordingReceipt? previous = null;
        var ancestors = new List<string>();
        for (var index = 0; index < 32; index++)
        {
            var manifest = session.Prepare(request, Guid.NewGuid(), previous);
            Assert.Equal(ancestors, manifest.Data.Value.GetProperty("knownLineage").EnumerateArray().Select(value => value.GetString()));
            var actual = await session.ExecuteAsync(manifest, request);
            AssertOwned(request, manifest, actual);
            Assert.Equal(CompilerHostStatus.Completed, actual.Response.Status);
            Assert.Equal(CompilerRegionalPredicateCaptureState.ManifestOnly, actual.Response.Execution!.Observation.State);
            Assert.Equal(index + 1, session.ReservedExecutions);
            ancestors.Add(manifest.RunId.ToString());
            previous = actual.Receipt;
        }
        Assert.NotNull(previous);
        Assert.Equal(31, previous.Manifest.Data.Value.GetProperty("knownLineage").GetArrayLength());
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), previous));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request));
        Assert.Equal(32, session.ReservedExecutions);
    }

    [Fact]
    public async Task DiagnosticLineageCannotCycleOrReuseAnAncestorRunIdentity()
    {
        var fixture = Create(includeDependency: false);
        var request = Request(fixture, new());
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var root = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var childManifest = session.Prepare(request, Guid.NewGuid(), root.Receipt);
        var child = await session.ExecuteAsync(childManifest, request);
        AssertOrigin(root, childManifest);
        Assert.Throws<ArgumentException>(() => session.Prepare(request, root.Receipt.Manifest.RunId, child.Receipt));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, childManifest.RunId, child.Receipt));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(root.Receipt.Manifest, request));
        Assert.Equal(2, session.ReservedExecutions);
    }

    [Fact]
    public async Task CancelledOutcomeHasNoCompilerGraphAndOnlyANewRunCanRetryIt()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var originalManifest = session.Prepare(request, Guid.NewGuid());
        var cancelled = await session.ExecuteAsync(originalManifest, request, cancellation.Token);
        AssertNoEnvelope(cancelled, CompilerHostStatus.Cancelled);
        var originalBytes = cancelled.Receipt.Data.ToCanonicalBytes();
        Assert.Equal(1, session.ReservedExecutions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(originalManifest, request));
        var nextManifest = session.Prepare(request, Guid.NewGuid(), cancelled.Receipt);
        var current = await session.ExecuteAsync(nextManifest, request);
        AssertOwned(request, nextManifest, current);
        AssertCompleted(fixture, current);
        AssertOrigin(cancelled, nextManifest);
        Assert.NotEqual(cancelled.Response.AttemptId, current.Response.AttemptId);
        Assert.NotEqual(originalManifest.RunId, nextManifest.RunId);
        Assert.False(cancelled.IsFor(request, nextManifest));
        Assert.Equal(originalBytes, cancelled.Receipt.Data.ToCanonicalBytes());
        AssertNoEnvelope(cancelled, CompilerHostStatus.Cancelled);
    }

    [Fact]
    public async Task DisposalWithdrawsSessionUseWithoutClaimingErasureOfCallerOwnedFrames()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var actual = await session.ExecuteAsync(manifest, request);
        var execution = AssertCompleted(fixture, actual);
        var retained = View(execution, execution.Compilation.Dependencies!.Regions[0]);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, retained.State);
        var data = retained.Data.ToCanonicalBytes();
        var bodies = retained.Bodies.ToDictionary(pair => pair.Key, pair => pair.Value.ToCanonicalBytes(), StringComparer.Ordinal);
        session.Dispose();
        Assert.Equal(0, session.ReservedExecutions);
        Assert.Throws<ObjectDisposedException>(() => session.Prepare(request, Guid.NewGuid()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ExecuteAsync(manifest, request));
        Assert.True(actual.IsFor(request, manifest));
        Assert.Equal(data, retained.Data.ToCanonicalBytes());
        foreach (var pair in bodies) Assert.Equal(pair.Value, retained.Bodies[pair.Key].ToCanonicalBytes());
        Assert.Equal("NotEvaluated", actual.Receipt.Data.Value.GetProperty("authority").GetString());
        Assert.Equal("NotAdopted", actual.Receipt.Data.Value.GetProperty("retentionPolicy").GetString());
    }

    [Fact]
    public async Task HostCompletedKeepsCompilerFailureAndCaptureGapsAsSeparateAxes()
    {
        var fixture = Create();
        foreach (var policy in new[]
        {
            new CompilerRegionalPredicateObservationPolicy(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1),
            new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1),
            new(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1)
        })
        {
            var request = Request(fixture, policy);
            using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
            var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
            var execution = AssertCompleted(fixture, actual);
            Assert.Equal(CompilerExecutionState.Completed, execution.Compilation.Result.Execution);
            Assert.Equal(CompilerVerdict.Fail, execution.Compilation.Result.Verdict);
            var expected = policy.MaximumBodyBytes == 1 ? CompilerRegionalPredicateCaptureState.BodyBudgetExceeded
                : policy.MaximumNodes == 1 ? CompilerRegionalPredicateCaptureState.NodeLimitReached
                : CompilerRegionalPredicateCaptureState.EvaluationLimitReached;
            Assert.Equal(expected, execution.Observation.State);
            Assert.Equal(expected.ToString(), actual.Receipt.Data.Value.GetProperty("captureState").GetString());
            Assert.Equal(0, execution.Observation.RetainedBodyBytes);
            Assert.Same(execution.Observation.Data, actual.Receipt.ObservationMetadata);
        }
    }

    [Fact]
    public async Task ResourceExhaustionIsAnActualCompletedHostEnvelopeWithInterruptedCapture()
    {
        var fixture = Create();
        var complete = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = complete.LogicalOperationsConsumed - 1;
        var limited = fixture with { Input = new(fixture.Input.Source, Json(settings)) };
        var request = Request(limited);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(limited.Input));
        var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var execution = AssertCompleted(limited, actual);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, execution.Compilation.Result.Execution);
        Assert.NotEqual(CompilerVerdict.Pass, execution.Compilation.Result.Verdict);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, execution.Observation.State);
        Assert.Equal(0, execution.Observation.RetainedBodyBytes);
        Assert.NotNull(actual.Receipt.ObservationMetadata);
        Assert.Equal("OriginalRegionalObservation", actual.Receipt.Data.Value.GetProperty("recordingState").GetString());
    }

    [Fact]
    public async Task UnsupportedHostProfileCannotInventACompilerEnvelopeOrObservationMetadata()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input, noCapabilities: true));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var actual = await session.ExecuteAsync(manifest, request);
        AssertOwned(request, manifest, actual);
        AssertNoEnvelope(actual, CompilerHostStatus.Unsupported);
        Assert.Equal("coglatas.regional-predicate-observation-host.compatibility-unavailable", actual.Response.ReasonCode);
        Assert.Equal(1, session.ReservedExecutions);
    }

    [Fact]
    public void MalformedOrWrongDigestTransfersFailBeforeAnyExecutionReservation()
    {
        var fixture = Create();
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var source = fixture.Input.Source.Data.ToCanonicalBytes();
        var manifest = fixture.Input.ToEnvelope().ToCanonicalBytes();
        var foreignDigest = ContentDigest.Compute("coglatas.security-regional-host-invalid/1", Encoding.UTF8.GetBytes("different"));
        foreach (var transfer in new[]
        {
            new CompilerInputTransfer(Encoding.UTF8.GetBytes("not-json"), manifest, fixture.Input.InputDigest),
            new CompilerInputTransfer(source, Encoding.UTF8.GetBytes("{}"), fixture.Input.InputDigest),
            new CompilerInputTransfer(source, manifest, foreignDigest)
        })
        {
            var request = new RegionalPredicateObservationCompilerHostRequest(new(transfer), new());
            Assert.Throws<FormatException>(() => session.Prepare(request, Guid.NewGuid()));
            Assert.Equal(0, session.ReservedExecutions);
        }
    }

    [Fact]
    public async Task MetadataSerializationAndDetachedCopiesCannotExposeOrRewriteOriginalOperatorGraphs()
    {
        var fixture = Create();
        var request = Request(fixture);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var actual = await session.ExecuteAsync(manifest, request);
        var execution = AssertCompleted(fixture, actual);
        Assert.False(JsonSerializer.SerializeToElement(request).TryGetProperty("Core", out _));
        Assert.False(JsonSerializer.SerializeToElement(actual.Response).TryGetProperty("Execution", out _));
        foreach (var text in new[] { JsonSerializer.Serialize(request), JsonSerializer.Serialize(manifest),
            JsonSerializer.Serialize(actual.Response), JsonSerializer.Serialize(actual.Receipt) })
        {
            Assert.DoesNotContain("evaluatedChildren", text, StringComparison.Ordinal);
            Assert.DoesNotContain("independent-regional-host-private-document", text, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Input.Source.Data.CanonicalText, text, StringComparison.Ordinal);
        }
        foreach (var data in new[] { request.Data, manifest.Data, actual.Response.Data, actual.Receipt.Data, actual.Receipt.ObservationMetadata! })
        {
            var original = data.ToCanonicalBytes();
            var bytes = data.ToCanonicalBytes();
            bytes[0] = (byte)'!';
            var detached = JsonNode.Parse(data.CanonicalText)!.AsObject();
            detached["independentPoison"] = true;
            Assert.Equal(original, data.ToCanonicalBytes());
            Assert.False(data.Value.TryGetProperty("independentPoison", out _));
        }
        var current = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var newExecution = AssertCompleted(fixture, current);
        var oldRegion = execution.Compilation.Dependencies!.Regions[0];
        var refusal = ProjectCompilerRegionalPredicateObservationSnapshot.Read(newExecution, newExecution.Compilation,
            newExecution.Observation, oldRegion, Assert.Single(oldRegion.Diagnostics), CompilerRegionalPredicateSnapshotMode.AvailableOperators);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.RefusedInvocation, refusal.State);
        Assert.Null(refusal.OriginalRecordMetadata);
        Assert.Empty(refusal.Bodies);
        Assert.Empty(refusal.EvaluationFrames);
    }

    [Fact]
    public async Task LegacyHostProtocolsAndCallerBufferIsolationRemainSeparateFromRegionalRecording()
    {
        var fixture = Create();
        var sourceBytes = fixture.Input.Source.Data.ToCanonicalBytes();
        var manifestBytes = fixture.Input.ToEnvelope().ToCanonicalBytes();
        var transfer = new CompilerInputTransfer(sourceBytes, manifestBytes, fixture.Input.InputDigest);
        sourceBytes[0] = (byte)'!';
        manifestBytes[0] = (byte)'!';
        var recoveredSource = transfer.RecoverSourceBytes();
        var recoveredManifest = transfer.RecoverManifestBytes();
        recoveredSource[0] = (byte)'?';
        recoveredManifest[0] = (byte)'?';
        var supplies = fixture.Snapshots.ToList();
        var core = new CompilerHostRequest(transfer, 9, supplies);
        supplies.Clear();
        Assert.Single(core.Snapshots);
        Assert.Equal(fixture.Input.InputDigest, transfer.Materialize().InputDigest);
        var normal = await new LocalNetCompilerHost(Profile(fixture.Input)).CompileAsync(core);
        var arrays = await new LocalPredicateCompilerHost(Profile(fixture.Input))
            .CompileAsync(new(core, new(CompilerPredicateCaptureMode.OriginalRootArrays)));
        var regionalRequest = new RegionalPredicateObservationCompilerHostRequest(core, new(PredicateEvaluationCaptureMode.ObservedOperators));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(fixture.Input));
        var regional = await session.ExecuteAsync(session.Prepare(regionalRequest, Guid.NewGuid()), regionalRequest);
        var execution = AssertCompleted(fixture, regional);
        Assert.Equal(normal.Compilation!.Result.Data, execution.Compilation.Result.Data);
        Assert.Equal(arrays.Compilation!.Result.Data, execution.Compilation.Result.Data);
        Assert.Null(normal.Compilation.PredicatePremises);
        Assert.Null(execution.Compilation.PredicatePremises);
        Assert.NotNull(arrays.Compilation.PredicatePremises);
        Assert.DoesNotContain("observationDigest", normal.Execution!.Data.CanonicalText, StringComparison.Ordinal);
        Assert.DoesNotContain("observationDigest", arrays.Data.CanonicalText, StringComparison.Ordinal);
    }

    private static Guid Id(int value) => Guid.Parse("f1966b72-0000-4000-8000-" + value.ToString("x12", CultureInfo.InvariantCulture));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private sealed record Fixture(CompilerInputManifest Input, CompilerMaterializedSnapshot[] Snapshots);
    private static object Reference(SourceRevisionContext context, int entity) => new
    {
        tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(),
        entityId = Id(entity).ToString(), context = context.Data.Value
    };
    private static object Literal(bool? truth) => new
    {
        kind = "coglatas.literal",
        value = truth.HasValue
            ? new Dictionary<string, object?> { ["type"] = BooleanType(), ["state"] = "coglatas.known", ["value"] = truth.Value }
            : new Dictionary<string, object?> { ["type"] = BooleanType(), ["state"] = "coglatas.unknown",
                ["reason"] = "coglatas.missing-input", ["basis"] = new { coverage = "coglatas.partial" } }
    };
    private static object BooleanType() => new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true };
    private static object Duration() => new
    {
        type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
        state = "coglatas.known", value = "5"
    };
    private static ProjectSource Source(SourceRevisionContext context, bool? truth, SourceRevisionContext? dependency = null)
    {
        var predicate = Literal(truth);
        var declarations = Json(new object[]
        {
            new { entityId = Id(10).ToString(), documentId = Id(6).ToString(), kind = "coglatas.task", schemaVersion = 1,
                payload = new { duration = Duration(), ports = Array.Empty<object>() } },
            new { entityId = Id(20).ToString(), documentId = Id(6).ToString(), kind = "coglatas.constraint", schemaVersion = 1,
                payload = new { predicate, classification = "coglatas.required" } }
        });
        var relations = new List<object>
        {
            new { relationId = Id(1000).ToString(), documentId = Id(6).ToString(), kind = RelationKinds.Constrains, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = Reference(context, 20) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = Reference(context, 7) },
                payload = new { predicateReference = Reference(context, 20), version = context.CommittedRevision!.RevisionId.ToString(),
                    predicateDigest = ContentDigest.Compute("coglatas.predicate/1", PurePredicate.Parse(Json(predicate)).Data.ToCanonicalBytes()).Data.Value } }
        };
        if (dependency is not null)
            relations.Add(new { relationId = Id(9000).ToString(), documentId = Id(6).ToString(), kind = RelationKinds.DependsOn, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = Reference(context, 10) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = Reference(dependency, 10) },
                payload = new { mode = "coglatas.finish-to-start", lag = Duration() } });
        return ProjectSource.Create(context, [SourceDocument.Create(new(Id(6)), "independent-regional-host-private-document",
            Json(new { entityId = Id(7).ToString(), kind = "coglatas.project", schemaVersion = 1, payload = new { } }))],
            declarations, Json(relations));
    }
    private static Fixture Create(bool? truth = false, bool includeDependency = true)
    {
        var tenant = new TenantId(Id(1));
        var rootContext = SourceRevisionContext.Committed(new(new(tenant, new(Id(2)), new(Id(3))), new(Id(100))));
        var dependencyContext = SourceRevisionContext.Committed(new(new(tenant, new(Id(4)), new(Id(5))), new(Id(200))));
        var dependency = Source(dependencyContext, truth);
        var source = Source(rootContext, truth, includeDependency ? dependencyContext : null);
        const string version = "independent-regional-host-1";
        object Artifact(string name, string revision = "1") => new { artifactId = name, version = revision,
            digest = ContentDigest.Compute("coglatas.security-regional-host-fixture/1", Encoding.UTF8.GetBytes(name + "/" + revision)).Data.Value };
        object None() => new { kind = "coglatas.none" };
        var settings = Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.security-regional-host.compiler"),
            ruleset = Artifact("coglatas.security-regional-host.ruleset"), semantics = Artifact("coglatas.semantic-model", "2"),
            ir = Artifact("coglatas.project-ir", "2"), evaluator = Artifact("coglatas.security-regional-host.evaluator"),
            scheduler = None(), solver = None(), stochastic = None(), importer = None(), scenario = None(),
            authorizedExternalSnapshots = includeDependency ? new object[] { new { snapshotId = Id(300).ToString(), version,
                digest = dependency.Digest.Data.Value, context = dependency.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null } } : [],
            resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(), observations = Array.Empty<object>(),
            evidence = Array.Empty<object>(), units = Array.Empty<object>(), calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            descriptors = RelationKindDescriptor.BuiltIns.Select(value => new { artifactId = value.DescriptorId, version = "1", digest = value.Digest.Data.Value }).ToArray(),
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations,
                CoreCompilerRules.Constraints, CoreCompilerRules.Requirements, CoreCompilerRules.Resources }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = RelationKindDescriptor.BuiltIns.SelectMany(value => value.RequiredCapabilities)
                .Prepend("coglatas.core.structural").Distinct(StringComparer.Ordinal).ToArray(),
            targets = new[] { "coglatas.semantic-model" }, analysisScope = new[] { "coglatas.source-structure" },
            sourceCoverage = "coglatas.complete", clock = new { kind = "coglatas.not-applicable" },
            executionProfile = new { purpose = "coglatas.normal", maximumOperations = 1_000_000,
                maximumDeclarations = 10_000, maximumRelations = 20_000, maximumDigits = 256, maximumScale = 256,
                rounding = "coglatas.ties-to-even" }
        });
        return new(new(source, settings), includeDependency ? [new(new(Id(300)), version, new(dependency, true, true, true))] : []);
    }
    private static RegionalPredicateObservationCompilerHostRequest Request(Fixture fixture,
        CompilerRegionalPredicateObservationPolicy? policy = null, long generation = 9)
        => new(new(new CompilerInputTransfer(fixture.Input), generation, fixture.Snapshots),
            policy ?? new(PredicateEvaluationCaptureMode.ObservedOperators));
    private static CompilerHostProfile Profile(CompilerInputManifest input, bool noCapabilities = false)
    {
        CompilerArtifactIdentity Contract(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        return new(input.Compiler, Contract("schema"), Contract("ruleset"), Contract("semantics"), Contract("ir"), Contract("evaluator"),
            noCapabilities ? [] : input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
    }
    private static CalendarSnapshot Calendar()
    {
        const string start = "2026-02-01T00:00:00.0000000Z", end = "2026-03-01T00:00:00.0000000Z";
        var zoneData = Json(new { schemaVersion = 1, timeZoneId = "Etc/UTC", version = "security-host-1",
            validFrom = start, validTo = end, segments = new[] { new { start, end, offsetTicks = "0" } } });
        var zoneReference = new TimeZoneReference(Json(new { timeZoneId = "Etc/UTC", version = "security-host-1",
            rulesDigest = TimeZoneSnapshot.ComputeDigest(zoneData).Data.Value }));
        var zone = TimeZoneSnapshot.Read(zoneData, zoneReference).Value!;
        var calendarData = Json(new { schemaVersion = 1, calendarId = "coglatas.security-regional-host", version = "security-host-1",
            timeZone = zoneReference.Data.Value, validFrom = start, validTo = end,
            workingIntervals = Array.Empty<object>(), nonWorkingIntervals = Array.Empty<object>(), exceptions = Array.Empty<object>() });
        var reference = new CalendarReference(Json(new { calendarId = "coglatas.security-regional-host", version = "security-host-1",
            timeZoneId = "Etc/UTC", rulesDigest = CalendarSnapshot.ComputeDigest(calendarData).Data.Value }));
        return CalendarSnapshot.Read(calendarData, reference, zone).Value!;
    }
    private static void AssertOwned(RegionalPredicateObservationCompilerHostRequest request,
        LocalRegionalPredicateObservationCompilerRecordingManifest manifest, RegionalPredicateObservationCompilerRecordedInvocation invocation)
    {
        Assert.True(manifest.IsFor(request));
        Assert.True(invocation.IsFor(request, manifest));
        Assert.True(invocation.Response.IsFor(request));
        Assert.True(invocation.Response.IsFor(manifest));
        Assert.True(invocation.Receipt.IsFor(invocation.Response));
        Assert.Same(manifest, invocation.Receipt.Manifest);
        Assert.Same(manifest, invocation.Response.Manifest);
        Assert.Equal(4, invocation.Response.AttemptId.Version);
        Assert.Equal(invocation.Response.AttemptId, invocation.Receipt.AttemptId);
        Assert.Equal(request.ExecutionInputDigest, invocation.Response.RequestedExecutionInputDigest);
        Assert.Equal(invocation.Response.Digest, invocation.Receipt.HostResponseDigest);
        Assert.Equal(invocation.Response.Digest, ContentDigest.Compute(
            RegionalPredicateObservationCompilerHostResponse.Format, invocation.Response.Data.ToCanonicalBytes()));
        Assert.Equal(invocation.Receipt.Digest, ContentDigest.Compute(
            LocalRegionalPredicateObservationCompilerRecordingReceipt.Format, invocation.Receipt.Data.ToCanonicalBytes()));
        foreach (var field in new[] { "authority", "producerTrust", "eligibility" })
            Assert.Equal("NotEvaluated", invocation.Receipt.Data.Value.GetProperty(field).GetString());
        Assert.Equal("NotAdopted", invocation.Receipt.Data.Value.GetProperty("retentionPolicy").GetString());
        Assert.Equal("NotRecorded", invocation.Receipt.Data.Value.GetProperty("initialRootEvaluation").GetString());
    }
    private static RecordedRegionalPredicateCompilation AssertCompleted(Fixture fixture, RegionalPredicateObservationCompilerRecordedInvocation actual)
    {
        Assert.Equal(CompilerHostStatus.Completed, actual.Response.Status);
        var execution = Assert.IsType<RecordedRegionalPredicateCompilation>(actual.Response.Execution);
        var ordinary = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        Assert.Equal(ordinary.Result.Data, execution.Compilation.Result.Data);
        Assert.Equal(ordinary.LogicalOperationsConsumed, execution.Compilation.LogicalOperationsConsumed);
        Assert.True(execution.Observation.IsOwnedBy(execution));
        Assert.Equal(execution.Observation.Digest, actual.Receipt.ObservationRecordDigest);
        Assert.Same(execution.Observation.Data, actual.Receipt.ObservationMetadata);
        return execution;
    }
    private static void AssertOrigin(RegionalPredicateObservationCompilerRecordedInvocation earlier,
        LocalRegionalPredicateObservationCompilerRecordingManifest later)
    {
        var origin = later.Data.Value.GetProperty("origin");
        Assert.Equal(earlier.Receipt.Manifest.RunId.ToString(), origin.GetProperty("runId").GetString());
        Assert.Equal(earlier.Response.AttemptId.ToString(), origin.GetProperty("attemptId").GetString());
        Assert.Equal(earlier.Receipt.Manifest.Digest.Data.CanonicalText, origin.GetProperty("manifestDigest").GetRawText());
        Assert.Equal(earlier.Receipt.Digest.Data.CanonicalText, origin.GetProperty("receiptDigest").GetRawText());
        Assert.Equal(earlier.Receipt.Manifest.BaseExecutionInputDigest, later.BaseExecutionInputDigest);
    }
    private static void AssertNoEnvelope(RegionalPredicateObservationCompilerRecordedInvocation actual, CompilerHostStatus status)
    {
        Assert.Equal(status, actual.Response.Status);
        Assert.Equal(status, actual.Receipt.HostStatus);
        Assert.Null(actual.Response.Execution);
        Assert.Null(actual.Receipt.ObservationMetadata);
        Assert.Null(actual.Receipt.ObservationRecordDigest);
        foreach (var name in new[] { "resultDigest", "observationDigest", "compilerExecution", "captureState" })
            Assert.Equal(JsonValueKind.Null, actual.Response.Data.Value.GetProperty(name).ValueKind);
        Assert.Equal(JsonValueKind.Null, actual.Receipt.Data.Value.GetProperty("resultDigest").ValueKind);
        Assert.Equal("HostOutcomeWithoutCompilerResult", actual.Receipt.Data.Value.GetProperty("recordingState").GetString());
    }
    private static ProjectCompilerRegionalPredicateObservationSnapshot View(RecordedRegionalPredicateCompilation execution,
        CompilerDependencyRegion region)
        => ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
            region, Assert.Single(region.Diagnostics), CompilerRegionalPredicateSnapshotMode.AvailableOperators);
}
