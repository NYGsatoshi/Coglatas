using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class LocalCompilerRecordingTests
{
    public static IEnumerable<object[]> Cases() => WebCompilerOutputSnapshotContractTests.Cases().Select(row => {
        using var json = JsonDocument.Parse((string)row[0]);
        return new object[] { json.RootElement.GetProperty("key").GetString()! };
    });
    private static (CompilerInputManifest Input, CompilerHostRequest Request, SourceJson Result) Case(string key)
    {
        var text = WebCompilerOutputSnapshotContractTests.Cases().Select(row => (string)row[0]).Single(text => {
            using var parsed = JsonDocument.Parse(text); return parsed.RootElement.GetProperty("key").GetString() == key;
        });
        using var data = JsonDocument.Parse(text); var row = data.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        return (input, new(new(input), 7, WebPreviewContractTests.Snapshots(row, input)), SourceJson.Parse(row.GetProperty("result").GetRawText()));
    }
    private static CompilerHostProfile Profile(CompilerInputManifest input, bool noCapabilities = false)
    {
        CompilerArtifactIdentity At(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        return new(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            noCapabilities ? [] : input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
    }

    private static CalendarSnapshot MaterializedCalendar()
    {
        const string start = "2026-01-01T00:00:00.0000000Z";
        const string end = "2027-01-01T00:00:00.0000000Z";
        var zoneData = SourceJson.Parse(JsonSerializer.Serialize(new { schemaVersion = 1, timeZoneId = "Etc/UTC", version = "fixture-1",
            validFrom = start, validTo = end, segments = new[] { new { start, end, offsetTicks = "0" } } }));
        var zoneReference = new TimeZoneReference(SourceJson.Parse(JsonSerializer.Serialize(new {
            timeZoneId = "Etc/UTC", version = "fixture-1", rulesDigest = TimeZoneSnapshot.ComputeDigest(zoneData).Data.Value })));
        var zone = TimeZoneSnapshot.Read(zoneData, zoneReference).Value!;
        var data = SourceJson.Parse(JsonSerializer.Serialize(new { schemaVersion = 1, calendarId = "coglatas.host-fixture", version = "fixture-1",
            timeZone = zoneReference.Data.Value, validFrom = start, validTo = end, workingIntervals = Array.Empty<object>(),
            nonWorkingIntervals = Array.Empty<object>(), exceptions = Array.Empty<object>() }));
        var reference = new CalendarReference(SourceJson.Parse(JsonSerializer.Serialize(new {
            calendarId = "coglatas.host-fixture", version = "fixture-1", timeZoneId = "Etc/UTC", rulesDigest = CalendarSnapshot.ComputeDigest(data).Data.Value })));
        return CalendarSnapshot.Read(data, reference, zone).Value!;
    }

    private static (CompilerInputManifest Input, CompilerHostRequest Request) Probe()
    {
        var (input, request, _) = Case((string)Cases().First()[0]);
        return (input, request);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DeclaredCaptureAndDistinctDiagnosticAttemptsPreserveActualCompilerTruth(string key)
    {
        var (input, request, expected) = Case(key);
        var session = new LocalCompilerRecordingSession(Profile(input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        Assert.Equal("BeforeExecution", manifest.Data.Value.GetProperty("capture").GetProperty("declared").GetString());
        Assert.Equal(request.ExecutionInputDigest, manifest.ExecutionInputDigest);
        var original = await session.ExecuteAsync(manifest, request);
        Assert.Equal(CompilerHostStatus.Completed, original.Response.Status); Assert.Equal(expected, original.Response.Compilation!.Result.Data);
        Assert.Same(manifest, original.Receipt.Manifest); Assert.Equal(original.Response.AttemptId, original.Receipt.AttemptId);
        Assert.Equal(4, original.Receipt.AttemptId.Version); Assert.NotEqual(manifest.RunId, original.Receipt.AttemptId);
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, original.Receipt.Snapshot!.State); Assert.Empty(original.Receipt.Snapshot.Bodies);
        var rerunManifest = session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs, diagnosticOrigin: original.Receipt);
        var rerun = await session.ExecuteAsync(rerunManifest, request);
        Assert.Equal(expected, rerun.Response.Compilation!.Result.Data); Assert.NotEqual(original.Response.AttemptId, rerun.Response.AttemptId);
        Assert.NotEqual(manifest.RunId, rerunManifest.RunId); Assert.NotEqual(original.Receipt.Digest, rerun.Receipt.Digest);
        Assert.Equal(3, rerun.Receipt.Snapshot!.Bodies.Count);
        var origin = rerunManifest.Data.Value.GetProperty("origin");
        Assert.Equal(original.Response.AttemptId.ToString(), origin.GetProperty("attemptId").GetString());
        Assert.Equal(original.Receipt.Digest.Data, SourceJson.Parse(origin.GetProperty("receiptDigest").GetRawText()));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, manifest.RunId, diagnosticOrigin: rerun.Receipt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(manifest, request));
        using var foreign = new LocalCompilerRecordingSession(Profile(input));
        Assert.Throws<ArgumentException>(() => foreign.Prepare(request, Guid.NewGuid(), diagnosticOrigin: original.Receipt));
        await Assert.ThrowsAsync<ArgumentException>(() => foreign.ExecuteAsync(manifest, request));
        foreach (var receipt in new[] { original.Receipt, rerun.Receipt }) {
            Assert.Equal("NotEvaluated", receipt.Data.Value.GetProperty("authority").GetString());
            Assert.Equal("NotAdopted", receipt.Data.Value.GetProperty("retentionPolicy").GetString());
            Assert.Equal("NotRecorded", receipt.Data.Value.GetProperty("earlierIntermediateStates").GetString());
        }
        session.Dispose(); await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ExecuteAsync(rerunManifest, request));
        Assert.Equal(3, rerun.Receipt.Snapshot.Bodies.Count); // Caller-owned bodies are not claimed to be physically erased.
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task BudgetGapAndHostOnlyCancellationNeverFabricateOrRewriteCompilerResults(string key)
    {
        var (input, request, expected) = Case(key);
        using var session = new LocalCompilerRecordingSession(Profile(input));
        var limited = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs, 1), request);
        Assert.Equal(expected, limited.Response.Compilation!.Result.Data);
        Assert.Equal(CompilerOutputSnapshotState.BodyBudgetExceeded, limited.Receipt.Snapshot!.State); Assert.Empty(limited.Receipt.Snapshot.Bodies);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs), request, cancellation.Token);
        Assert.Equal(CompilerHostStatus.Cancelled, cancelled.Response.Status); Assert.Null(cancelled.Response.Compilation); Assert.Null(cancelled.Receipt.Snapshot);
        Assert.Equal(JsonValueKind.Null, cancelled.Receipt.Data.Value.GetProperty("resultDigest").ValueKind);
        Assert.Equal("HostOutcomeWithoutCompilerResult", cancelled.Receipt.Data.Value.GetProperty("recordingState").GetString());
        var diagnostic = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid(), diagnosticOrigin: cancelled.Receipt), request);
        Assert.Equal(expected, diagnostic.Response.Compilation!.Result.Data); Assert.NotEqual(cancelled.Receipt.AttemptId, diagnostic.Receipt.AttemptId);
        Assert.Equal(cancelled.Receipt.AttemptId.ToString(), diagnostic.Receipt.Manifest.Data.Value.GetProperty("origin").GetProperty("attemptId").GetString());
    }

    [Fact]
    public async Task RecordingEvidenceKeepsExactDiagnosticLineageWithoutRetainingPrivateBodies()
    {
        var (input, request) = Probe();
        using var session = new LocalCompilerRecordingSession(Profile(input));
        var original = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid()), request);
        var diagnostic = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs,
                diagnosticOrigin: original.Receipt), request);
        var capture = Assert.IsType<ProjectCompilerOutputSnapshot>(diagnostic.Receipt.Snapshot);
        Assert.NotEmpty(capture.Bodies);

        var originalEvidence = CompilerRecordingEvidence.FromInvocation(original);
        Assert.Null(originalEvidence.OriginRunId);
        Assert.Null(originalEvidence.OriginReceiptDigest);
        var evidence = CompilerRecordingEvidence.FromInvocation(diagnostic);
        Assert.Equal(SecuritySourceIdentitySnapshot.Capture(input.Source.Context, input.InputDigest), evidence.Source);
        Assert.Equal(input.Source.Digest.Value, evidence.SourceDigest);
        Assert.Equal(diagnostic.Receipt.Manifest.RunId, evidence.RunId);
        Assert.Equal(diagnostic.Receipt.AttemptId, evidence.AttemptId);
        Assert.NotEqual(originalEvidence.AttemptId, evidence.AttemptId);
        Assert.Equal(original.Receipt.Manifest.RunId, evidence.OriginRunId);
        Assert.Equal(original.Receipt.Digest.Value, evidence.OriginReceiptDigest);
        Assert.Equal(capture.Digest.Value, evidence.CaptureDigest);
        Assert.Equal(capture.State, evidence.OriginalCaptureState);
        Assert.Equal(originalEvidence.Result?.ResultDigest, evidence.Result?.ResultDigest);

        var payload = JsonSerializer.SerializeToElement(evidence);
        Assert.Equal(new[] { "AttemptId", "CaptureDigest", "CaptureMode", "CompilerInputId", "ExecutionInputDigest", "HostStatus",
                "ManifestDigest", "MaterializationsDigest", "OriginReceiptDigest", "OriginRunId", "OriginalCaptureState", "ReceiptDigest",
                "Result", "RunId", "Source", "SourceDigest" },
            payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var serialized = payload.GetRawText();
        var sessionId = diagnostic.Receipt.Manifest.Data.Value.GetProperty("sessionId").GetString();
        Assert.DoesNotContain(Assert.IsType<string>(sessionId), serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(input.Source.Documents[0].LogicalName, serialized, StringComparison.Ordinal);
        foreach (var body in capture.Bodies.Values)
            Assert.DoesNotContain(body.CanonicalText, serialized, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ActualInputGenerationAndMaterializationSubstitutionAreRefusedBeforeReservation(string key)
    {
        var (input, request, _) = Case(key);
        using var session = new LocalCompilerRecordingSession(Profile(input));
        var original = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var declared = session.Prepare(request, Guid.NewGuid()); var before = session.ReservedExecutions;
        var generation = new CompilerHostRequest(new(input), 8, request.Snapshots);
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(declared, generation));
        var invalid = new CompilerHostRequest(new CompilerInputTransfer(Encoding.UTF8.GetBytes("invalid"), input.ToEnvelope().ToCanonicalBytes(), input.InputDigest), 7, request.Snapshots);
        await Assert.ThrowsAsync<FormatException>(() => session.ExecuteAsync(declared, invalid));
        var unitData = SourceJson.Parse("{\"unitId\":\"unit.kg\",\"version\":1,\"dimension\":{\"dimension.mass\":1}}");
        var unit = new UnitDefinition(unitData, ContentDigest.Compute("coglatas.unit-definition/1", unitData.ToCanonicalBytes()));
        var withUnit = new CompilerHostRequest(new(input), 7, request.Snapshots, [unit]);
        Assert.NotEqual(request.ExecutionInputDigest, withUnit.ExecutionInputDigest);
        Assert.Throws<ArgumentException>(() => session.Prepare(withUnit, Guid.NewGuid(), diagnosticOrigin: original.Receipt));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(declared, withUnit));
        var withCalendar = new CompilerHostRequest(new(input), 7, request.Snapshots, calendars: [MaterializedCalendar()]);
        Assert.NotEqual(request.ExecutionInputDigest, withCalendar.ExecutionInputDigest);
        Assert.Throws<ArgumentException>(() => session.Prepare(withCalendar, Guid.NewGuid(), diagnosticOrigin: original.Receipt));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(declared, withCalendar));
        if (request.Snapshots.Count > 0) {
            var without = new CompilerHostRequest(new(input), 7);
            Assert.Throws<ArgumentException>(() => session.Prepare(without, Guid.NewGuid(), diagnosticOrigin: original.Receipt));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(declared, without));
            var repeated = new CompilerHostRequest(new(input), 7, request.Snapshots.Concat(request.Snapshots.Take(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(declared, repeated));
        }
        Assert.Equal(before, session.ReservedExecutions);
        var reordered = new CompilerHostRequest(new(input), 7, request.Snapshots.Reverse());
        Assert.Equal(request.ExecutionInputDigest, reordered.ExecutionInputDigest);
        var valid = await session.ExecuteAsync(declared, reordered); Assert.Equal(original.Response.Compilation!.Result.Data, valid.Response.Compilation!.Result.Data);
    }

    [Theory]
    [InlineData("authorized")]
    [InlineData("declarations")]
    [InlineData("relations")]
    public async Task SnapshotScopeClaimChangesCannotReuseAnOriginalExecutionDeclaration(string dimension)
    {
        var probe = Cases().Select(row => Case((string)row[0])).First(value => value.Request.Snapshots.Count > 0);
        using var session = new LocalCompilerRecordingSession(Profile(probe.Input));
        var manifest = session.Prepare(probe.Request, Guid.NewGuid()); var original = await session.ExecuteAsync(manifest, probe.Request);
        var first = probe.Request.Snapshots[0];
        var changed = dimension switch { "authorized" => first.Snapshot with { IsAuthorized = false },
            "declarations" => first.Snapshot with { DeclarationsComplete = false }, _ => first.Snapshot with { RelationsComplete = false } };
        var snapshots = probe.Request.Snapshots.Select((value, index) => index == 0 ? new CompilerMaterializedSnapshot(value.SnapshotId, value.Version, changed) : value);
        var request = new CompilerHostRequest(new(probe.Input), 7, snapshots);
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), diagnosticOrigin: original.Receipt));
        await Assert.ThrowsAsync<ArgumentException>(() => session.ExecuteAsync(session.Prepare(probe.Request, Guid.NewGuid()), request));
        Assert.Equal(1, session.ReservedExecutions);
    }

    [Fact]
    public async Task ConcurrentCallsReserveOneIdentityAndHonorTheLocalCapacity()
    {
        var (input, request) = Probe();
        using var session = new LocalCompilerRecordingSession(Profile(input));
        var declaration = session.Prepare(request, Guid.NewGuid());
        static async Task<bool> Attempt(LocalCompilerRecordingSession ownedSession, CompilerHostRequest inputRequest, LocalCompilerRecordingManifest manifest)
        {
            try { await ownedSession.ExecuteAsync(manifest, inputRequest); return true; }
            catch (InvalidOperationException) { return false; }
        }
        static async Task<bool[]> Concurrent(LocalCompilerRecordingSession ownedSession, CompilerHostRequest inputRequest, LocalCompilerRecordingManifest? shared)
        {
            using var barrier = new Barrier(16);
            var workers = new Task<bool>[16];
            for (var worker = 0; worker < workers.Length; worker++) {
                workers[worker] = Task.Factory.StartNew(static async state => {
                    var invocation = ((LocalCompilerRecordingSession Session, CompilerHostRequest Request, LocalCompilerRecordingManifest? Shared, Barrier Gate))state!;
                    var manifest = invocation.Shared ?? invocation.Session.Prepare(invocation.Request, Guid.NewGuid());
                    if (!invocation.Gate.SignalAndWait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Concurrent test gate timed out.");
                    return await Attempt(invocation.Session, invocation.Request, manifest);
                }, (ownedSession, inputRequest, shared, barrier), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            }
            return await Task.WhenAll(workers);
        }
        Assert.Single(await Concurrent(session, request, declaration), value => value); Assert.Equal(1, session.ReservedExecutions);
        for (var n = 1; n < LocalCompilerRecordingSession.MaximumExecutions - 1; n++)
            Assert.True(await Attempt(session, request, session.Prepare(request, Guid.NewGuid())));
        Assert.Single(await Concurrent(session, request, null), value => value);
        Assert.Equal(LocalCompilerRecordingSession.MaximumExecutions, session.ReservedExecutions);
    }

    [Fact]
    public async Task DiagnosticLineageAndDeclarationLimitsRemainBounded()
    {
        var (input, request) = Probe();
        using var session = new LocalCompilerRecordingSession(Profile(input));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.Empty));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), (CompilerOutputSnapshotMode)999));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), maximumBodyBytes: 0));
        var originalId = Guid.NewGuid(); var receipt = (await session.ExecuteAsync(session.Prepare(request, originalId), request)).Receipt;
        for (var n = 0; n < LocalCompilerRecordingSession.MaximumDiagnosticAncestors; n++)
            receipt = (await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid(), diagnosticOrigin: receipt), request)).Receipt;
        Assert.Equal(31, receipt.Manifest.Data.Value.GetProperty("knownLineage").GetArrayLength());
        Assert.Throws<ArgumentException>(() => session.Prepare(request, Guid.NewGuid(), diagnosticOrigin: receipt));
        Assert.Throws<ArgumentException>(() => session.Prepare(request, originalId, diagnosticOrigin: receipt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request));
    }

    [Fact]
    public async Task UnsupportedHostOutcomeHasNoFabricatedCompilerResult()
    {
        var (input, request) = Probe(); Assert.NotEmpty(input.Settings.Value.GetProperty("featureSet").EnumerateArray());
        using var session = new LocalCompilerRecordingSession(Profile(input, noCapabilities: true));
        var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(CompilerHostStatus.Unsupported, actual.Response.Status); Assert.Null(actual.Response.Compilation); Assert.Null(actual.Receipt.Snapshot);
        Assert.Equal("HostOutcomeWithoutCompilerResult", actual.Receipt.Data.Value.GetProperty("recordingState").GetString());
    }
}
