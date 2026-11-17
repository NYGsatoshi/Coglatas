using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class RegionalPredicateObservationCompilerHostTests
{
    internal static CompilerInputManifest Upgrade(CompilerInputManifest input)
    {
        var settings = JsonNode.Parse(input.Settings.CanonicalText)!;
        settings["semantics"]!["version"] = "2"; settings["ir"]!["version"] = "2";
        return new(input.Source, SourceJson.Parse(settings.ToJsonString()));
    }
    internal static CompilerInputManifest InputWithPredicate() => Upgrade(CompilerAnalysisFixtures.Constraint(false));
    internal static CompilerHostProfile Profile(CompilerInputManifest input, bool noCapabilities = false) =>
        PredicateCompilerHostTests.Profile(input, noCapabilities);
    internal static CompilerHostRequest ChangedCore(CompilerHostRequest core, string dimension) =>
        PredicateCompilerHostTests.ChangedCore(core, dimension);
    internal static (CoreCompilation Control, CompilerHostRequest Core, SourceJson Expected, string Key) ByKey(string key)
    {
        var old = PredicateObservationCompilerHostTests.ByKey(key);
        var input = Upgrade(old.Control.Input);
        var core = new CompilerHostRequest(new(input), old.Core.ClientGeneration, old.Core.Snapshots, old.Core.Units, old.Core.Calendars);
        var control = PureCoreCompiler.Compile(input, input.Compiler, core.Snapshots, core.Units, core.Calendars);
        return (control, core, control.Result.Data, key);
    }
    private static ProjectSource Constraints(ProjectSource source, bool? truth, int count = 1)
    {
        var template = CompilerAnalysisFixtures.Constraint(truth).Source;
        var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
        var relations = JsonNode.Parse(source.Relations.CanonicalText)!.AsArray();
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var entity = 85 + ordinal;
            var constraint = JsonNode.Parse(template.Declarations.CanonicalText)!.AsArray()[0]!.DeepClone();
            constraint["entityId"] = Id(entity); declarations.Add(constraint);
            var predicate = constraint["payload"]!["predicate"]!;
            relations.Add(Obj(new { relationId = Id(200 + ordinal), documentId = Id(4), kind = RelationKinds.Constrains, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = CompilerAnalysisFixtures.Reference(entity, source.Context) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = CompilerAnalysisFixtures.Reference(6, source.Context) },
                payload = new { predicateReference = CompilerAnalysisFixtures.Reference(entity, source.Context),
                    version = source.Context.CommittedRevision!.RevisionId.ToString(),
                    predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(predicate).ToCanonicalBytes()).Data.Value } }));
        }
        return ProjectSource.Create(source.Context, source.Documents, Json(declarations), Json(relations));
    }

    private static CompilerBindingClosureTests.Fixture TwoRegions(bool? truth, int rootCount = 1, int externalCount = 1)
    {
        var fixture = CompilerBindingClosureTests.Graph(transitive: false);
        var external = Assert.Single(fixture.Snapshots);
        var changed = Constraints(external.Snapshot.Source, truth, externalCount);
        var local = JsonNode.Parse(Constraints(fixture.Input.Source, truth, rootCount).Declarations.CanonicalText)!.AsArray();
        local[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(changed.Digest.Data.CanonicalText);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["authorizedExternalSnapshots"]![0]!["digest"] = JsonNode.Parse(changed.Digest.Data.CanonicalText);
        settings["rules"] = CompilerAnalysisFixtures.AllRulesSettings()["rules"]!.DeepClone();
        if (rootCount > 1 || externalCount > 1) settings["executionProfile"]!["maximumOperations"] = 10000000;
        var rootWithConstraint = Constraints(fixture.Input.Source, truth, rootCount);
        var input = Upgrade(new(ProjectSource.Create(rootWithConstraint.Context, rootWithConstraint.Documents,
            Json(local), rootWithConstraint.Relations), Json(settings)));
        return fixture with { Input = input, Snapshots = [new(external.SnapshotId, external.Version, new(changed, true, true, true))] };
    }

    public static IEnumerable<object[]> KeyModes()
    {
        foreach (var key in new[] { "fail", "pass", "unknown", "test-assertion-navigation" })
        foreach (var mode in Enum.GetValues<PredicateEvaluationCaptureMode>()) yield return [key, mode];
    }
    internal static RegionalPredicateObservationCompilerHostRequest Full(CompilerHostRequest core, int bytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        new(core, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: bytes));

    [Theory]
    [MemberData(nameof(KeyModes))]
    public async Task SeparateDeclaredHostPreservesActualLegacyResultAndBindsRequestManifestAndPolicy(string key, PredicateEvaluationCaptureMode mode)
    {
        var f = ByKey(key); var request = new RegionalPredicateObservationCompilerHostRequest(f.Core, new(mode));
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Control.Input));
        var manifest = session.Prepare(request, Guid.NewGuid());
        var response = await new LocalRegionalPredicateObservationCompilerHost(Profile(f.Control.Input)).CompileAsync(request, manifest);
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.Equal(4, response.AttemptId.Version);
        Assert.True(response.IsFor(request)); Assert.True(response.IsFor(manifest));
        Assert.False(response.IsFor(new RegionalPredicateObservationCompilerHostRequest(f.Core, request.ObservationPolicy)));
        Assert.False(response.IsFor(session.Prepare(request, Guid.NewGuid())));
        var execution = Assert.IsType<RecordedRegionalPredicateCompilation>(response.Execution);
        Assert.True(execution.Observation.IsOwnedBy(execution)); Assert.Same(request.ObservationPolicy, execution.Observation.Policy);
        Assert.Equal(f.Expected, execution.Compilation.Result.Data); Assert.Equal(f.Control.LogicalOperationsConsumed, execution.Compilation.LogicalOperationsConsumed);
        Assert.Equal(response.Digest, ContentDigest.Compute(RegionalPredicateObservationCompilerHostResponse.Format, response.Data.ToCanonicalBytes()));
        Assert.Equal(execution.Observation.Digest.Data, SourceJson.Parse(response.Data.Value.GetProperty("observationDigest").GetRawText()));
        Assert.DoesNotContain(response.AttemptId.ToString(), execution.Compilation.Result.Data.CanonicalText);
        Assert.Null(execution.Compilation.PredicatePremises);
        Assert.Equal(mode == PredicateEvaluationCaptureMode.Manifest ? CompilerRegionalPredicateCaptureState.ManifestOnly :
            key == "test-assertion-navigation" ? CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations :
            CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, execution.Observation.State);
        Assert.False(JsonSerializer.SerializeToElement(request).TryGetProperty("Core", out _));
        Assert.False(JsonSerializer.SerializeToElement(response).TryGetProperty("Execution", out _));
        Assert.DoesNotContain("evaluatedChildren", JsonSerializer.Serialize(response));
        Assert.DoesNotContain(f.Control.Input.Source.Data.CanonicalText, JsonSerializer.Serialize(response));
        foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", response.Data.Value.GetProperty(field).GetString());
        Assert.Equal("ActualRegionalEvaluationsOnly", response.Data.Value.GetProperty("captureScope").GetString());
        Assert.Equal("NotRecorded", response.Data.Value.GetProperty("initialRootEvaluation").GetString());
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
        var changed = new RegionalPredicateObservationCompilerHostRequest(core, dimension switch {
            "mode" => new(),
            "bytes" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1),
            "nodes" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1),
            _ => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1) });
        Assert.NotEqual(baseline.PolicyDigest, changed.PolicyDigest); Assert.NotEqual(baseline.ExecutionInputDigest, changed.ExecutionInputDigest);
        Assert.Equal(core.ExecutionInputDigest, baseline.Core.ExecutionInputDigest); Assert.Equal(core.ExecutionInputDigest, changed.Core.ExecutionInputDigest);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
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
        using var declarations = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        var manifest = declarations.Prepare(request, Guid.NewGuid());
        var unsupported = await new LocalRegionalPredicateObservationCompilerHost(Profile(input, noCapabilities: true)).CompileAsync(request, manifest);
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
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        var invocation = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(CompilerHostStatus.Completed, invocation.Response.Status);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, invocation.Response.Execution!.Compilation.Result.Execution);
        Assert.NotEqual(CompilerVerdict.Pass, invocation.Response.Execution.Compilation.Result.Verdict);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, invocation.Response.Execution.Observation.State);
        Assert.Equal(0, invocation.Response.Execution.Observation.RetainedBodyBytes);
        Assert.NotNull(invocation.Receipt.ObservationMetadata);
    }

    [Fact]
    public async Task DirectHostCannotUseAnEqualValueDifferentRequestWithAnEarlierManifest()
    {
        var input = InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7); var request = Full(core); var equal = Full(core);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        var manifest = session.Prepare(request, Guid.NewGuid()); var host = new LocalRegionalPredicateObservationCompilerHost(Profile(input));
        Assert.Equal(request.ExecutionInputDigest, equal.ExecutionInputDigest); Assert.False(manifest.IsFor(equal));
        Assert.Throws<ArgumentException>(() => { _ = host.CompileAsync(equal, manifest); });
        Assert.Equal(0, session.ReservedExecutions);
        var valid = await session.ExecuteAsync(manifest, request); Assert.True(valid.IsFor(request, manifest));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public async Task ActualExternalAndRootRegionalCallsBelongToTheNewDeclaredRunAndCannotPromoteTheEarlierManifest(bool? truth)
    {
        var f = TwoRegions(truth); var core = new CompilerHostRequest(new(f.Input), 7, f.Snapshots);
        var control = PureCoreCompiler.Compile(f.Input, f.Input.Compiler, f.Snapshots);
        var request = new RegionalPredicateObservationCompilerHostRequest(core, new());
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        var original = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        var originalData = original.Receipt.Data; var originalMetadata = original.Receipt.ObservationMetadata;
        var full = Full(core); var declaration = session.Prepare(full, Guid.NewGuid(), original.Receipt);
        var actual = await session.ExecuteAsync(declaration, full);
        Assert.True(actual.IsFor(full, declaration)); Assert.False(original.IsFor(full, declaration));
        Assert.Equal("OriginalRegionalObservation", actual.Receipt.Data.Value.GetProperty("recordingState").GetString());
        Assert.Same(originalData, original.Receipt.Data); Assert.Same(originalMetadata, original.Receipt.ObservationMetadata);
        Assert.Equal(CompilerRegionalPredicateCaptureState.ManifestOnly, original.Response.Execution!.Observation.State);
        var execution = actual.Response.Execution!;
        Assert.Equal(control.Result.Data, execution.Compilation.Result.Data);
        Assert.Equal(control.LogicalOperationsConsumed, execution.Compilation.LogicalOperationsConsumed);
        Assert.Equal(2, execution.Observation.CompletedRegionCalls); Assert.Equal(2, execution.Observation.StartedEvaluations);
        Assert.Equal(2, execution.Observation.CompletedEvaluations);
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, execution.Observation.State);
        Assert.Equal(2, execution.Compilation.Dependencies!.Regions.Count);
        Assert.Equal(execution.Observation.Digest, actual.Receipt.ObservationRecordDigest);
        Assert.Equal(execution.Compilation.Result.Execution.ToString(), actual.Response.Data.Value.GetProperty("compilerExecution").GetString());
        Assert.Equal(execution.Compilation.Result.Coverage.ToString(), actual.Response.Data.Value.GetProperty("compilerCoverage").GetString());
        Assert.Equal(execution.Compilation.Result.Verdict.ToString(), actual.Response.Data.Value.GetProperty("compilerVerdict").GetString());
        Assert.Equal(execution.Compilation.Result.EvidenceComplete, actual.Response.Data.Value.GetProperty("evidenceComplete").GetBoolean());
        foreach (var region in execution.Compilation.Dependencies.Regions)
        {
            if (truth == true) { Assert.Empty(region.Diagnostics); continue; }
            var diagnostic = Assert.Single(region.Diagnostics);
            var view = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
                region, diagnostic, CompilerRegionalPredicateSnapshotMode.AvailableOperators);
            Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, view.State);
            Assert.Equal(1, view.SelectedEvaluations); Assert.Single(view.Bodies); Assert.Single(view.EvaluationFrames);
            var foreign = original.Response.Execution.Compilation.Dependencies!.Regions.Single(r => r.Digest.Equals(region.Digest));
            Assert.Equal(CompilerRegionalPredicateSnapshotState.RefusedInvocation,
                ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
                    foreign, diagnostic, CompilerRegionalPredicateSnapshotMode.AvailableOperators).State);
        }
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("nodes")]
    [InlineData("evaluations")]
    public async Task OriginalAggregateCaptureGapSurvivesTheHostReceiptWithoutChangingActualRegionalTruth(string dimension)
    {
        var f = TwoRegions(false); var core = new CompilerHostRequest(new(f.Input), 7, f.Snapshots);
        var control = PureCoreCompiler.Compile(f.Input, f.Input.Compiler, f.Snapshots);
        var policy = dimension switch {
            "bytes" => new CompilerRegionalPredicateObservationPolicy(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1),
            "nodes" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1),
            _ => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1) };
        var request = new RegionalPredicateObservationCompilerHostRequest(core, policy);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(f.Input));
        var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(CompilerHostStatus.Completed, actual.Response.Status);
        var execution = actual.Response.Execution!;
        Assert.Equal(control.Result.Data, execution.Compilation.Result.Data);
        Assert.Equal(control.LogicalOperationsConsumed, execution.Compilation.LogicalOperationsConsumed);
        Assert.Equal(CompilerExecutionState.Completed, execution.Compilation.Result.Execution);
        Assert.Equal(dimension switch { "bytes" => CompilerRegionalPredicateCaptureState.BodyBudgetExceeded,
            "nodes" => CompilerRegionalPredicateCaptureState.NodeLimitReached, _ => CompilerRegionalPredicateCaptureState.EvaluationLimitReached }, execution.Observation.State);
        Assert.Equal(execution.Observation.Data, actual.Receipt.ObservationMetadata);
        Assert.Equal(execution.Observation.State.ToString(), actual.Receipt.Data.Value.GetProperty("captureState").GetString());
        Assert.Equal(0, execution.Observation.RetainedBodyBytes);
        foreach (var region in execution.Compilation.Dependencies!.Regions)
        {
            var view = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
                region, Assert.Single(region.Diagnostics), CompilerRegionalPredicateSnapshotMode.AvailableOperators);
            Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames);
            Assert.Equal("NotRecorded", view.Data.Value.GetProperty("selection").GetString());
        }
    }

    [Fact]
    public async Task InitialRootAndRegionalPoliciesCannotCollideOrBorrowOriginalOperatorsEvenForTheSameCoreInput()
    {
        var input = PredicateCompilerHostTests.InputWithPredicate(); var core = new CompilerHostRequest(new(input), 7);
        var old = new PredicateObservationCompilerHostRequest(core, new(PredicateEvaluationCaptureMode.ObservedOperators));
        var request = Full(core);
        Assert.Equal(old.Core.ExecutionInputDigest, request.Core.ExecutionInputDigest);
        Assert.NotEqual(old.PolicyDigest, request.PolicyDigest); Assert.NotEqual(old.ExecutionInputDigest, request.ExecutionInputDigest);
        using var oldSession = new LocalPredicateObservationCompilerRecordingSession(Profile(input));
        var oldActual = await oldSession.ExecuteAsync(oldSession.Prepare(old, Guid.NewGuid()), old);
        using var session = new LocalRegionalPredicateObservationCompilerRecordingSession(Profile(input));
        var actual = await session.ExecuteAsync(session.Prepare(request, Guid.NewGuid()), request);
        Assert.Equal(oldActual.Response.Execution!.Compilation.Result.Data, actual.Response.Execution!.Compilation.Result.Data);
        Assert.Equal(1, oldActual.Response.Execution.Observation.CompletedEvaluations);
        Assert.Equal(0, actual.Response.Execution.Observation.CompletedRegionCalls);
        Assert.Equal(0, actual.Response.Execution.Observation.CompletedEvaluations);
        Assert.Equal(CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations, actual.Response.Execution.Observation.State);
        Assert.Equal("NotRecorded", actual.Receipt.ObservationMetadata!.Value.GetProperty("initialRootEvaluation").GetString());
        Assert.NotEqual(PredicateObservationCompilerHostResponse.Format, RegionalPredicateObservationCompilerHostResponse.Format);
    }
}
