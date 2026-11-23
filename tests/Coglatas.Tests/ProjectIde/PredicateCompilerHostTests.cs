using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class PredicateCompilerHostTests
{
    public static IEnumerable<object[]> Cases() => RootSourceAdmissionTests.Cases();
    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => Enum.GetValues<CompilerPredicateCaptureMode>().Select(mode => new[] { row[0], mode }));
    internal static (CoreCompilation Control, CompilerHostRequest Core, SourceJson Expected, string Key) Fixture(string text)
    {
        var (control, core, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        using var json = JsonDocument.Parse(text);
        return (control, core, SourceJson.Parse(json.RootElement.GetProperty("result").GetRawText()), json.RootElement.GetProperty("key").GetString()!);
    }
    internal static CompilerHostProfile Profile(CompilerInputManifest input, bool noCapabilities = false)
    {
        CompilerArtifactIdentity At(string key) => new(SourceJson.Parse(input.Settings.Value.GetProperty(key).GetRawText()));
        return new(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            noCapabilities ? [] : input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(x => x.GetString()), input.PlanLowerer);
    }
    internal static CompilerInputManifest InputWithPredicate() => CompilerAnalysisFixtures.Constraint(false);
    internal static PredicateCompilerHostRequest Request(CompilerInputManifest input, CompilerPredicateCapturePolicy? policy = null, long generation = 7) =>
        new(new(new(input), generation), policy ?? new());

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ActualPolicyBoundHostPreservesEveryOriginalLegacyResultAndOwnsOnlyItsInvocation(string text, CompilerPredicateCaptureMode mode)
    {
        var f = Fixture(text); var policy = new CompilerPredicateCapturePolicy(mode); var request = new PredicateCompilerHostRequest(f.Core, policy);
        var response = await new LocalPredicateCompilerHost(Profile(f.Control.Input)).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.True(response.IsFor(request)); Assert.Equal(4, response.AttemptId.Version);
        Assert.False(response.IsFor(new(f.Core, policy))); Assert.Equal(request.ExecutionInputDigest, response.RequestedExecutionInputDigest);
        Assert.Equal(f.Expected, response.Compilation!.Result.Data); Assert.Equal(f.Control.LogicalOperationsConsumed, response.Compilation.LogicalOperationsConsumed);
        Assert.Null(f.Control.PredicatePremises); var record = Assert.IsType<ProjectPredicatePremiseRecord>(response.Compilation.PredicatePremises);
        Assert.True(record.IsOwnedBy(response.Compilation)); Assert.Same(policy, record.Policy);
        Assert.Equal(response.Digest, ContentDigest.Compute(PredicateCompilerHostResponse.Format, response.Data.ToCanonicalBytes()));
        Assert.Equal(record.Digest.Data, SourceJson.Parse(response.Data.Value.GetProperty("predicateRecordDigest").GetRawText()));
        Assert.DoesNotContain(response.AttemptId.ToString(), response.Compilation.Result.Data.CanonicalText, StringComparison.Ordinal);
        if (mode == CompilerPredicateCaptureMode.Manifest)
        { Assert.Equal(PredicatePremiseRecordState.ManifestOnly, record.State); Assert.Empty(record.Bodies); }
        else if (f.Key is "fail" or "pass")
        { Assert.Equal(PredicatePremiseRecordState.CapturedOriginalArrays, record.State); Assert.Single(record.Bodies); Assert.Equal(f.Key == "fail" ? 134 : 133, record.RetainedBodyBytes); }
        else { Assert.Equal(PredicatePremiseRecordState.NoOriginalArraysObserved, record.State); Assert.Empty(record.Bodies); }
        foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", response.Data.Value.GetProperty(field).GetString());
        Assert.Equal("NotAdopted", response.Data.Value.GetProperty("retentionPolicy").GetString());
    }

    [Theory]
    [InlineData("mode")] [InlineData("bytes")] [InlineData("count")]
    public async Task EveryPolicyDimensionChangesExecutionBindingWithoutChangingTheOldRequestOrCompilerTruth(string dimension)
    {
        var input = InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7);
        var baseline = new PredicateCompilerHostRequest(core, new(CompilerPredicateCaptureMode.OriginalRootArrays));
        var changedPolicy = dimension switch {
            "mode" => new CompilerPredicateCapturePolicy(),
            "bytes" => new CompilerPredicateCapturePolicy(CompilerPredicateCaptureMode.OriginalRootArrays, 1),
            _ => new CompilerPredicateCapturePolicy(CompilerPredicateCaptureMode.OriginalRootArrays, maximumDistinctBodies: 1) };
        var changed = new PredicateCompilerHostRequest(core, changedPolicy);
        Assert.Equal(core.ExecutionInputDigest, baseline.Core.ExecutionInputDigest); Assert.Equal(core.ExecutionInputDigest, changed.Core.ExecutionInputDigest);
        Assert.NotEqual(baseline.PolicyDigest, changed.PolicyDigest); Assert.NotEqual(baseline.ExecutionInputDigest, changed.ExecutionInputDigest);
        var host = new LocalPredicateCompilerHost(Profile(input)); var a = await host.CompileAsync(baseline); var b = await host.CompileAsync(changed);
        Assert.Equal(a.Compilation!.Result.Data, b.Compilation!.Result.Data); Assert.NotEqual(a.AttemptId, b.AttemptId);
        Assert.False(a.IsFor(changed)); Assert.False(b.IsFor(baseline));
        Assert.NotEqual(a.Compilation.PredicatePremises!.Data, b.Compilation.PredicatePremises!.Data);
    }

    internal static CalendarSnapshot Calendar()
    {
        const string start = "2026-01-01T00:00:00.0000000Z", end = "2027-01-01T00:00:00.0000000Z";
        var data = SourceJson.Parse(JsonSerializer.Serialize(new { schemaVersion = 1, timeZoneId = "Etc/UTC", version = "fixture-1",
            validFrom = start, validTo = end, segments = new[] { new { start, end, offsetTicks = "0" } } }));
        var zoneReference = new TimeZoneReference(SourceJson.Parse(JsonSerializer.Serialize(new {
            timeZoneId = "Etc/UTC", version = "fixture-1", rulesDigest = TimeZoneSnapshot.ComputeDigest(data).Data.Value })));
        var zone = TimeZoneSnapshot.Read(data, zoneReference).Value!;
        var calendarData = SourceJson.Parse(JsonSerializer.Serialize(new { schemaVersion = 1, calendarId = "coglatas.host-fixture", version = "fixture-1",
            timeZone = zoneReference.Data.Value, validFrom = start, validTo = end, workingIntervals = Array.Empty<object>(),
            nonWorkingIntervals = Array.Empty<object>(), exceptions = Array.Empty<object>() }));
        var reference = new CalendarReference(SourceJson.Parse(JsonSerializer.Serialize(new {
            calendarId = "coglatas.host-fixture", version = "fixture-1", timeZoneId = "Etc/UTC", rulesDigest = CalendarSnapshot.ComputeDigest(calendarData).Data.Value })));
        return CalendarSnapshot.Read(calendarData, reference, zone).Value!;
    }
    internal static CompilerHostRequest ChangedCore(CompilerHostRequest core, string dimension)
    {
        var input = core.Input.Materialize();
        if (dimension == "generation") return new(core.Input, core.ClientGeneration + 1, core.Snapshots, core.Units, core.Calendars);
        if (dimension == "input") return new(new(CompilerAnalysisFixtures.Constraint(true)), core.ClientGeneration, core.Snapshots);
        if (dimension == "unit")
        {
            var data = SourceJson.Parse("{\"unitId\":\"unit.kg\",\"version\":1,\"dimension\":{\"dimension.mass\":1}}");
            return new(core.Input, core.ClientGeneration, core.Snapshots, [new(data, ContentDigest.Compute("coglatas.unit-definition/1", data.ToCanonicalBytes()))], core.Calendars);
        }
        if (dimension == "calendar") return new(core.Input, core.ClientGeneration, core.Snapshots, core.Units, [Calendar()]);
        if (dimension == "snapshots") return new(new(input), core.ClientGeneration);
        var first = core.Snapshots[0];
        var changed = dimension switch { "authorized" => first.Snapshot with { IsAuthorized = false },
            "declarations" => first.Snapshot with { DeclarationsComplete = false }, _ => first.Snapshot with { RelationsComplete = false } };
        return new(core.Input, core.ClientGeneration, core.Snapshots.Select((s, i) => i == 0 ? new(s.SnapshotId, s.Version, changed) : s), core.Units, core.Calendars);
    }

    [Theory]
    [InlineData("unit")] [InlineData("calendar")] [InlineData("snapshots")]
    [InlineData("authorized")] [InlineData("declarations")] [InlineData("relations")]
    public async Task ActualMaterializationsStayBoundEvenWhenTheDeclaredManifestIsIdentical(string dimension)
    {
        var fixture = CompilerBindingClosureTests.Graph(withRelation: true);
        var core = new CompilerHostRequest(new(fixture.Input), 7, fixture.Snapshots);
        var request = new PredicateCompilerHostRequest(core, new()); var other = new PredicateCompilerHostRequest(ChangedCore(core, dimension), new());
        Assert.Equal(request.Core.Input.ExpectedInputDigest, other.Core.Input.ExpectedInputDigest);
        Assert.NotEqual(request.Core.MaterializationDigest, other.Core.MaterializationDigest); Assert.NotEqual(request.ExecutionInputDigest, other.ExecutionInputDigest);
        var host = new LocalPredicateCompilerHost(Profile(fixture.Input)); var a = await host.CompileAsync(request); var b = await host.CompileAsync(other);
        Assert.Equal(CompilerHostStatus.Completed, a.Status); Assert.Equal(CompilerHostStatus.Completed, b.Status);
        Assert.True(a.IsFor(request)); Assert.True(b.IsFor(other)); Assert.False(a.IsFor(other)); Assert.False(b.IsFor(request));
        Assert.Equal(other.ExecutionInputDigest.Data, SourceJson.Parse(b.Data.Value.GetProperty("executionInputDigest").GetRawText()));
    }

    [Fact]
    public async Task InvalidUnsupportedAndPreCancelledInvocationsNeverPublishCompilerOrPredicateBodies()
    {
        var input = InputWithPredicate(); var request = Request(input, new(CompilerPredicateCaptureMode.OriginalRootArrays));
        var invalid = new PredicateCompilerHostRequest(new(new CompilerInputTransfer(Encoding.UTF8.GetBytes("invalid"), input.ToEnvelope().ToCanonicalBytes(), input.InputDigest), 7), new());
        var host = new LocalPredicateCompilerHost(Profile(input));
        var bad = await host.CompileAsync(invalid); Assert.Equal(CompilerHostStatus.InvalidInput, bad.Status);
        var unsupported = await new LocalPredicateCompilerHost(Profile(input, true)).CompileAsync(request); Assert.Equal(CompilerHostStatus.Unsupported, unsupported.Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = await host.CompileAsync(request, cancellation.Token); Assert.Equal(CompilerHostStatus.Cancelled, cancelled.Status);
        foreach (var response in new[] { bad, unsupported, cancelled })
        {
            Assert.Null(response.Compilation); Assert.Equal(4, response.AttemptId.Version);
            Assert.Equal(JsonValueKind.Null, response.Data.Value.GetProperty("resultDigest").ValueKind);
            Assert.Equal(JsonValueKind.Null, response.Data.Value.GetProperty("predicateRecordDigest").ValueKind);
        }
    }

    [Fact]
    public async Task ResourceExhaustionPreservesCompilerAxesAndReturnsAnExplicitInterruptedRecordingGap()
    {
        var original = InputWithPredicate(); var settings = System.Text.Json.Nodes.JsonNode.Parse(original.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = 1;
        var input = new CompilerInputManifest(original.Source, Json(settings)); var control = PureCoreCompiler.Compile(input, input.Compiler);
        var response = await new LocalPredicateCompilerHost(Profile(input)).CompileAsync(Request(input, new(CompilerPredicateCaptureMode.OriginalRootArrays)));
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.Equal(CompilerExecutionState.ResourceExhausted, response.Compilation!.Result.Execution);
        Assert.Equal(control.Result.Data, response.Compilation.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, response.Compilation.LogicalOperationsConsumed);
        Assert.Equal(PredicatePremiseRecordState.CaptureInterrupted, response.Compilation.PredicatePremises!.State); Assert.Empty(response.Compilation.PredicatePremises.Bodies);
        Assert.NotEqual(CompilerVerdict.Pass, response.Compilation.Result.Verdict); Assert.False(response.Compilation.Result.EvidenceComplete);
    }

    [Fact]
    public async Task ExistingLocalHostAndOutputRecordingContractsStaySeparateFromTheNewExplicitRequest()
    {
        var input = InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7); var profile = Profile(input);
        var old = await new LocalNetCompilerHost(profile).CompileAsync(core);
        using var session = new LocalCompilerRecordingSession(profile); var output = await session.ExecuteAsync(session.Prepare(core, Guid.NewGuid()), core);
        var captured = await new LocalPredicateCompilerHost(profile).CompileAsync(new(core, new(CompilerPredicateCaptureMode.OriginalRootArrays)));
        Assert.Null(old.Compilation!.PredicatePremises); Assert.Null(output.Response.Compilation!.PredicatePremises);
        Assert.Equal(old.Compilation.Result.Data, captured.Compilation!.Result.Data);
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, output.Receipt.Snapshot!.State); Assert.Empty(output.Receipt.Snapshot.Bodies);
        Assert.Single(captured.Compilation.PredicatePremises!.Bodies);
        Assert.Throws<ArgumentNullException>(() => new PredicateCompilerHostRequest(null!, new()));
        Assert.Throws<ArgumentNullException>(() => new PredicateCompilerHostRequest(core, null!));
    }
}
