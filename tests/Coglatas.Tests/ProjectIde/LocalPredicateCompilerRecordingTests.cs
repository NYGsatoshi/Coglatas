using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.PredicateCompilerHostTests;

namespace Coglatas.Tests.ProjectIde;

public sealed class LocalPredicateCompilerRecordingTests
{
    public static IEnumerable<object[]> Cases() => PredicateCompilerHostTests.Cases();
    private static PredicateCompilerHostRequest Full(CompilerHostRequest core, int bytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        new(core, new(CompilerPredicateCaptureMode.OriginalRootArrays, bytes));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PredeclaredManifestAndNewDiagnosticRunBindGenuineAttemptsWithoutRecoveringTheOriginalMissingBodies(string text)
    {
        var f = Fixture(text);
        using var session = new LocalPredicateCompilerRecordingSession(Profile(f.Control.Input));
        var originalRequest = new PredicateCompilerHostRequest(f.Core, new());
        var declaration = session.Prepare(originalRequest, Guid.NewGuid());
        Assert.Equal("BeforeExecution", declaration.Data.Value.GetProperty("capture").GetProperty("declared").GetString());
        var original = await session.ExecuteAsync(declaration, originalRequest);
        Assert.True(original.Receipt.IsFor(original.Response)); Assert.Same(declaration, original.Receipt.Manifest); Assert.Empty(original.Receipt.Bodies);
        Assert.Equal(PredicatePremiseRecordState.ManifestOnly.ToString(), original.Receipt.PredicateMetadata!.Value.GetProperty("state").GetString());
        var originalMetadata = original.Receipt.PredicateMetadata; var fullRequest = Full(f.Core);
        var rerunManifest = session.Prepare(fullRequest, Guid.NewGuid(), original.Receipt);
        Assert.NotEqual(declaration.ExecutionInputDigest, rerunManifest.ExecutionInputDigest);
        Assert.Equal(declaration.BaseExecutionInputDigest, rerunManifest.BaseExecutionInputDigest);
        var rerun = await session.ExecuteAsync(rerunManifest, fullRequest);
        Assert.Equal(f.Expected, original.Response.Compilation!.Result.Data); Assert.Equal(f.Expected, rerun.Response.Compilation!.Result.Data);
        Assert.NotEqual(original.Receipt.AttemptId, rerun.Receipt.AttemptId); Assert.NotEqual(declaration.RunId, rerunManifest.RunId);
        Assert.Equal(4, rerun.Receipt.AttemptId.Version); Assert.True(rerun.Receipt.IsFor(rerun.Response)); Assert.False(original.Receipt.IsFor(rerun.Response));
        Assert.Same(rerun.Response.Compilation.PredicatePremises!.Data, rerun.Receipt.PredicateMetadata);
        Assert.Equal(f.Key is "fail" or "pass" ? 1 : 0, rerun.Receipt.Bodies.Count);
        Assert.Empty(original.Receipt.Bodies); Assert.Same(originalMetadata, original.Receipt.PredicateMetadata);
        var origin = rerunManifest.Data.Value.GetProperty("origin");
        Assert.Equal(declaration.RunId.ToString(), origin.GetProperty("runId").GetString());
        Assert.Equal(original.Receipt.AttemptId.ToString(), origin.GetProperty("attemptId").GetString());
        Assert.Equal(original.Receipt.Digest.Data, SourceJson.Parse(origin.GetProperty("receiptDigest").GetRawText()));
        Assert.Equal(original.Receipt.Manifest.Digest.Data, SourceJson.Parse(origin.GetProperty("manifestDigest").GetRawText()));
        Assert.NotEqual(rerunManifest.RunId, rerun.Receipt.AttemptId);
        var limitedRequest = Full(f.Core, 1); var limited = await session.ExecuteAsync(session.Prepare(limitedRequest, Guid.NewGuid(), rerun.Receipt), limitedRequest);
        Assert.Equal(f.Expected, limited.Response.Compilation!.Result.Data); Assert.Empty(limited.Receipt.Bodies);
        Assert.Equal(f.Key is "fail" or "pass" ? "BodyBudgetExceeded" : "NoOriginalArraysObserved", limited.Receipt.PredicateMetadata!.Value.GetProperty("state").GetString());
        foreach (var invocation in new[] { original, rerun, limited })
        {
            var receipt = invocation.Receipt;
            Assert.Equal(receipt.Digest, ContentDigest.Compute(LocalPredicateCompilerRecordingReceipt.Format, receipt.Data.ToCanonicalBytes()));
            Assert.Equal("NotRecorded", receipt.Data.Value.GetProperty("earlierIntermediateStates").GetString());
            Assert.Equal("NotRecorded", receipt.Data.Value.GetProperty("relationshipStructure").GetString());
            Assert.Equal("NotEvaluated", receipt.Data.Value.GetProperty("authority").GetString());
            Assert.Equal("NotAdopted", receipt.Data.Value.GetProperty("retentionPolicy").GetString());
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GenuineCancelledHostOutcomeCanBeLinkedOnlyAsANewRunWithNoInventedOriginalResult(string text)
    {
        var f = Fixture(text);
        using var session = new LocalPredicateCompilerRecordingSession(Profile(f.Control.Input)); var request = Full(f.Core);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request, cancellation.Token);
        Assert.Equal(CompilerHostStatus.Cancelled, cancelled.Response.Status); Assert.Null(cancelled.Response.Compilation);
        Assert.Null(cancelled.Receipt.PredicateMetadata); Assert.Null(cancelled.Receipt.PredicateRecordDigest); Assert.Empty(cancelled.Receipt.Bodies);
        Assert.Equal("HostOutcomeWithoutCompilerResult", cancelled.Receipt.Data.Value.GetProperty("recordingState").GetString());
        Assert.Equal(JsonValueKind.Null, cancelled.Receipt.Data.Value.GetProperty("resultDigest").ValueKind);
        var next = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid(), cancelled.Receipt), request);
        Assert.Equal(f.Expected, next.Response.Compilation!.Result.Data); Assert.NotEqual(cancelled.Receipt.AttemptId, next.Receipt.AttemptId);
        Assert.Equal(cancelled.Receipt.AttemptId.ToString(), next.Receipt.Manifest.Data.Value.GetProperty("origin").GetProperty("attemptId").GetString());
        Assert.Null(cancelled.Receipt.PredicateMetadata);
    }

    [Theory]
    [InlineData("mode")] [InlineData("bytes")] [InlineData("count")] [InlineData("generation")] [InlineData("input")]
    [InlineData("unit")] [InlineData("calendar")] [InlineData("snapshots")]
    [InlineData("authorized")] [InlineData("declarations")] [InlineData("relations")]
    public async Task PolicyGenerationInputAndMaterializationSubstitutionsAreRefusedBeforeRunReservation(string dimension)
    {
        var fixture = CompilerBindingClosureTests.Graph(withRelation: true);
        var core = new CompilerHostRequest(new(fixture.Input), 7, fixture.Snapshots); var request = Full(core);
        using var session = new LocalPredicateCompilerRecordingSession(Profile(fixture.Input));
        var original = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var declaration = session.Prepare(request, Guid.NewGuid()); var before = session.ReservedExecutions;
        var changed = dimension switch {
            "mode" => new PredicateCompilerHostRequest(core, new()),
            "bytes" => Full(core, 1),
            "count" => new PredicateCompilerHostRequest(core, new(CompilerPredicateCaptureMode.OriginalRootArrays, maximumDistinctBodies: 1)),
            _ => Full(ChangedCore(core, dimension)) };
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(declaration, changed));
        Assert.Equal(before, session.ReservedExecutions);
        if (!changed.Core.ExecutionInputDigest.Equals(request.Core.ExecutionInputDigest))
            Assert.Throws<ArgumentException>(() => session.Prepare(changed, Guid.NewGuid(), original.Receipt));
        else
            Assert.Equal(original.Receipt.AttemptId.ToString(), session.Prepare(changed, Guid.NewGuid(), original.Receipt).Data.Value.GetProperty("origin").GetProperty("attemptId").GetString());
        var invalidCore = new CompilerHostRequest(new CompilerInputTransfer(Encoding.UTF8.GetBytes("invalid"),
            fixture.Input.ToEnvelope().ToCanonicalBytes(), fixture.Input.InputDigest), 7, fixture.Snapshots);
        await Assert.ThrowsAsync<FormatException>(() => session.ExecuteAsync(declaration, Full(invalidCore)));
        Assert.Equal(before, session.ReservedExecutions);
        var reordered = Full(new(core.Input, 7, core.Snapshots.Reverse()));
        var valid = await session.ExecuteAsync(declaration, reordered);
        Assert.Equal(original.Response.Compilation!.Result.Data, valid.Response.Compilation!.Result.Data);
    }

    [Fact]
    public async Task ForeignSessionAndOlderSameInputResponseCannotSubstituteForAnOwnedRunOrReceipt()
    {
        var input = InputWithPredicate(); var request = Full(new(new(input), 7));
        using var first = new LocalPredicateCompilerRecordingSession(Profile(input));
        using var foreign = new LocalPredicateCompilerRecordingSession(Profile(input));
        var manifest = first.Prepare(request, Guid.NewGuid()); var original = await first.ExecuteAsync(manifest, request);
        await Assert.ThrowsAsync<ArgumentException>(() => foreign.ExecuteAsync(manifest, request));
        Assert.Throws<ArgumentException>(() => foreign.Prepare(request, Guid.NewGuid(), original.Receipt));
        Assert.Equal(0, foreign.ReservedExecutions); await Assert.ThrowsAsync<InvalidOperationException>(() => first.ExecuteAsync(manifest, request));
        var next = await first.ExecuteAsync(first.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(original.Response.Compilation!.Result.ResultDigest, next.Response.Compilation!.Result.ResultDigest);
        Assert.NotSame(original.Response.Compilation.Result, next.Response.Compilation.Result);
        Assert.False(original.Response.Compilation.PredicatePremises!.IsOwnedBy(next.Response.Compilation));
        Assert.False(original.Receipt.IsFor(next.Response)); Assert.False(next.Receipt.IsFor(original.Response));
        Assert.NotEqual(original.Receipt.AttemptId, next.Receipt.AttemptId);
        Assert.Empty(typeof(PredicateCompilerHostResponse).GetConstructors()); Assert.Empty(typeof(LocalPredicateCompilerRecordingReceipt).GetConstructors());
        Assert.Empty(typeof(PredicateCompilerRecordedInvocation).GetConstructors());
    }

    [Fact]
    public async Task ConcurrentDuplicateRunsAndCapacityExhaustionDoNotSilentlyEvictExistingRecords()
    {
        var input = InputWithPredicate(); var request = Request(input);
        using var session = new LocalPredicateCompilerRecordingSession(Profile(input));
        static async Task<bool[]> Concurrent(LocalPredicateCompilerRecordingSession owner, PredicateCompilerHostRequest req, LocalPredicateCompilerRecordingManifest? shared)
        {
            using var barrier = new Barrier(8); var jobs = new Task<bool>[8];
            for (var i = 0; i < jobs.Length; i++)
                jobs[i] = Task.Factory.StartNew(static async state => {
                    var (owned, actualRequest, declaration, gate) = ((LocalPredicateCompilerRecordingSession, PredicateCompilerHostRequest, LocalPredicateCompilerRecordingManifest?, Barrier))state!;
                    var manifest = declaration ?? owned.Prepare(actualRequest, Guid.NewGuid());
                    if (!gate.SignalAndWait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Concurrent recording test gate timed out.");
                    try { await owned.ExecuteAsync(manifest, actualRequest); return true; } catch (InvalidOperationException) { return false; }
                }, (owner, req, shared, barrier), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            return await Task.WhenAll(jobs);
        }
        Assert.Single(await Concurrent(session, request, session.Prepare(request, Guid.NewGuid())), x => x);
        Assert.Equal(1, session.ReservedExecutions);
        for (var i = 1; i < LocalPredicateCompilerRecordingSession.MaximumExecutions - 1; i++) await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Single(await Concurrent(session, request, null), x => x); Assert.Equal(32, session.ReservedExecutions);
    }

    [Fact]
    public async Task DiagnosticLineageAndDisposalRemainBoundedWithoutClaimingPhysicalDeletion()
    {
        var input = InputWithPredicate(); var request = Request(input); var session = new LocalPredicateCompilerRecordingSession(Profile(input));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.Empty));
        var firstRun = Guid.NewGuid(); var invocation = await session.ExecuteAsync(session.Prepare(request, firstRun), request);
        for (var i = 0; i < LocalPredicateCompilerRecordingSession.MaximumDiagnosticAncestors; i++)
            invocation = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid(), invocation.Receipt), request);
        Assert.Equal(31, invocation.Receipt.Manifest.Data.Value.GetProperty("knownLineage").GetArrayLength());
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), invocation.Receipt));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, firstRun, invocation.Receipt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request));
        session.Dispose(); Assert.Throws<ObjectDisposedException>(() => session.Prepare(request, Guid.NewGuid()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ExecuteAsync(invocation.Receipt.Manifest, request));
        Assert.NotNull(invocation.Receipt.PredicateMetadata); Assert.Empty(invocation.Receipt.Bodies);
    }

    [Fact]
    public async Task CallerOwnedOriginalFramesStayImmutableAndNoUnsupportedHostResultIsInvented()
    {
        var input = InputWithPredicate(); var request = Request(input, new(CompilerPredicateCaptureMode.OriginalRootArrays));
        var session = new LocalPredicateCompilerRecordingSession(Profile(input));
        try
        {
            var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
            Assert.Single(actual.Receipt.Bodies); var map = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(actual.Receipt.Bodies); Assert.True(map.IsReadOnly);
            var first = map.First(); Assert.Throws<NotSupportedException>(() => map.Clear()); Assert.Throws<NotSupportedException>(() => map[first.Key] = SourceJson.Parse("[]"));
            Assert.Throws<NotSupportedException>(() => map.Keys.Clear()); Assert.Throws<NotSupportedException>(() => map.Values.Clear());
            var bytes = first.Value.ToCanonicalBytes(); bytes[0] ^= 1;
            Assert.Equal(first.Key, ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, first.Value.ToCanonicalBytes()).Value);
            Assert.Same(actual.Response.Compilation!.PredicatePremises!.Data, actual.Receipt.PredicateMetadata);
            Assert.DoesNotContain(typeof(LocalPredicateCompilerRecordingReceipt).GetProperties(), p => p.PropertyType == typeof(CoreCompilation) || p.PropertyType == typeof(PredicateCompilerHostResponse) || p.PropertyType == typeof(CompilerHostRequest));
            session.Dispose(); Assert.Single(actual.Receipt.Bodies);
        }
        finally { session.Dispose(); }
        using var unsupported = new LocalPredicateCompilerRecordingSession(Profile(input, true));
        var refused = await unsupported.ExecuteAsync(unsupported.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(CompilerHostStatus.Unsupported, refused.Response.Status); Assert.Null(refused.Response.Compilation);
        Assert.Null(refused.Receipt.PredicateMetadata); Assert.Empty(refused.Receipt.Bodies);
    }
}
