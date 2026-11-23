using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class PredeclaredPredicateCaptureTests
{
    public static IEnumerable<object[]> Cases() => RootSourceAdmissionTests.Cases();
    private static CoreCompilation Capture(CompilerInputManifest input, CompilerPredicateCapturePolicy? policy = null,
        CancellationToken cancellationToken = default, PlanDeclarationReadCache? cache = null) =>
        PureCoreCompiler.CompileWithPredicateCapture(input, input.Compiler, policy ?? new(CompilerPredicateCaptureMode.OriginalRootArrays),
            cancellationToken: cancellationToken, declarationCache: cache);
    private static ProjectPredicatePremiseRecord Record(CoreCompilation compilation)
    {
        var record = Assert.IsType<ProjectPredicatePremiseRecord>(compilation.PredicatePremises);
        Assert.True(record.IsOwnedBy(compilation));
        Assert.Equal(compilation.Input.InputDigest, record.InputDigest); Assert.Equal(compilation.Result.ResultDigest, record.ResultDigest);
        Assert.Equal(record.Bodies.Values.Sum(b => b.ToCanonicalBytes().Length), record.RetainedBodyBytes);
        Assert.Equal(record.Digest, ContentDigest.Compute(ProjectPredicatePremiseRecord.Format, record.Data.ToCanonicalBytes()));
        foreach (var item in record.Bodies) Assert.Equal(item.Key, ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, item.Value.ToCanonicalBytes()).Value);
        return record;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalLegacyResultAndOperationsStayUnchangedWhileActualRootArraysAreCaptured(string text)
    {
        var (control, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        using var json = JsonDocument.Parse(text); var key = json.RootElement.GetProperty("key").GetString();
        var actual = Capture(control.Input); var record = Record(actual);
        Assert.Null(control.PredicatePremises); Assert.False(record.IsOwnedBy(control));
        Assert.Equal(control.Result.Data, actual.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
        Assert.Equal(SourceJson.Parse(json.RootElement.GetProperty("result").GetRawText()), actual.Result.Data);
        if (key is "fail" or "pass")
        {
            Assert.Equal(PredicatePremiseRecordState.CapturedOriginalArrays, record.State); var body = Assert.Single(record.Bodies);
            Assert.Equal(key == "fail" ? 134 : 133, record.RetainedBodyBytes);
            var predicate = Assert.Single(actual.Model!.Declarations.Select(d => d.Declaration).OfType<ConstraintPlanDeclaration>()).Predicate;
            var expected = SourceJson.FromObject(predicate.Evaluate().EvaluatedPremises.Select(p => p.Value).ToArray());
            Assert.Equal(expected, body.Value);
            if (key == "fail") Assert.Contains(actual.Result.Diagnostics.SelectMany(d => d.Evidence.Premises), p => p.Value == body.Key);
            else Assert.Contains(actual.Result.RuleOutcomes.SelectMany(r => r.Evidence.Premises), p => p.Value == body.Key);
        }
        else { Assert.Equal(PredicatePremiseRecordState.NoOriginalArraysObserved, record.State); Assert.Empty(record.Bodies); }
        Assert.Equal("BeforeExecution", record.Data.Value.GetProperty("capture").GetProperty("declared").GetString());
        Assert.Equal("RootInitialRuleEvaluation", record.Data.Value.GetProperty("scope").GetString());
        foreach (var field in new[] { "regionalEvaluation", "relationshipStructure", "earlierStates" }) Assert.Equal("NotRecorded", record.Data.Value.GetProperty(field).GetString());
        foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", record.Data.Value.GetProperty(field).GetString());
        Assert.Equal("NotAllocated", record.Data.Value.GetProperty("runIdentity").GetString());
        Assert.Equal("NotAdopted", record.Data.Value.GetProperty("retentionPolicy").GetString());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitDefaultManifestRetainsNoBodiesAndDoesNotRetroactivelyRecordEarlierExecution(string text)
    {
        var (control, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text); var actual = Capture(control.Input, new()); var record = Record(actual);
        Assert.Equal(CompilerPredicateCaptureMode.Manifest, record.Policy.Mode); Assert.Equal(PredicatePremiseRecordState.ManifestOnly, record.State);
        Assert.Empty(record.Bodies); Assert.Equal(0, record.RetainedBodyBytes); Assert.Equal(control.Result.Data, actual.Result.Data);
        Assert.Null(control.PredicatePremises); Assert.False(record.IsOwnedBy(control));
    }

    private static CompilerInputManifest TwoDistinctArrays()
    {
        var first = CompilerAnalysisFixtures.Constraint(false); var other = CompilerAnalysisFixtures.Constraint(true);
        var declarations = JsonNode.Parse(first.Source.Declarations.CanonicalText)!.AsArray();
        var declaration = JsonNode.Parse(other.Source.Declarations.CanonicalText)![0]!.DeepClone(); declaration["entityId"] = Id(8); declarations.Add(declaration);
        var relations = JsonNode.Parse(first.Source.Relations.CanonicalText)!.AsArray();
        var relation = JsonNode.Parse(other.Source.Relations.CanonicalText)![0]!.DeepClone(); relation["relationId"] = Id(21);
        relation["source"]!["reference"]!["entityId"] = Id(8); relation["payload"]!["predicateReference"]!["entityId"] = Id(8); relations.Add(relation);
        return new(ProjectSource.Create(first.Source.Context, first.Source.Documents, Json(declarations), Json(relations)), first.Settings);
    }

    [Fact]
    public void DistinctBodyCountAndLastByteLimitsAtomicallyWithdrawAllBodiesWithoutChangingFailedTruth()
    {
        var input = TwoDistinctArrays(); var control = PureCoreCompiler.Compile(input, input.Compiler); var full = Record(Capture(input));
        Assert.Equal(CompilerVerdict.Fail, control.Result.Verdict); Assert.Equal(2, full.Bodies.Count);
        var exact = Record(Capture(input, new(CompilerPredicateCaptureMode.OriginalRootArrays, full.RetainedBodyBytes, 2)));
        Assert.Equal(full.Data.Value.GetProperty("bodies").GetRawText(), exact.Data.Value.GetProperty("bodies").GetRawText());
        foreach (var policy in new[] { new CompilerPredicateCapturePolicy(CompilerPredicateCaptureMode.OriginalRootArrays, full.RetainedBodyBytes - 1),
            new CompilerPredicateCapturePolicy(CompilerPredicateCaptureMode.OriginalRootArrays, maximumDistinctBodies: 1) })
        {
            var compiled = Capture(input, policy); var limited = Record(compiled);
            Assert.Equal(policy.MaximumDistinctBodies == 1 ? PredicatePremiseRecordState.BodyCountLimitReached : PredicatePremiseRecordState.BodyBudgetExceeded, limited.State);
            Assert.Empty(limited.Bodies); Assert.Equal(0, limited.RetainedBodyBytes); Assert.Equal(control.Result.Data, compiled.Result.Data);
            Assert.Equal(control.LogicalOperationsConsumed, compiled.LogicalOperationsConsumed);
        }
        var repeated = Record(Capture(CompilerAnalysisFixtures.Constraint(false), new(CompilerPredicateCaptureMode.OriginalRootArrays, maximumDistinctBodies: 1)));
        Assert.Equal(PredicatePremiseRecordState.CapturedOriginalArrays, repeated.State); Assert.Single(repeated.Bodies);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void FinalPublicationAttachmentAndDeclarationCacheCannotDropOrShareTheInvocationRecord(bool truth)
    {
        var input = CompilerScopedPublicationTests.Upgrade(CompilerAnalysisFixtures.Constraint(truth)); var control = PureCoreCompiler.Compile(input, input.Compiler);
        var cache = new PlanDeclarationReadCache(); var actual = Capture(input, cache: cache); var next = Capture(input, cache: cache);
        var record = Record(actual); var nextRecord = Record(next);
        Assert.NotNull(actual.Dependencies); Assert.NotNull(actual.Publication); Assert.True(actual.Publication.IsDeclaredBy(actual.Result));
        Assert.Equal(control.Result.Data, actual.Result.Data); Assert.Equal(control.Publication!.Ir, actual.Publication.Ir);
        Assert.Equal(control.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
        Assert.Equal(record.Data, nextRecord.Data); Assert.NotSame(record, nextRecord);
        Assert.False(record.IsOwnedBy(control)); Assert.False(record.IsOwnedBy(next)); Assert.False(nextRecord.IsOwnedBy(actual));
        Assert.NotSame(actual.Result, next.Result); Assert.Equal(actual.Result.ResultDigest, next.Result.ResultDigest);
    }

    [Fact]
    public async Task ReusingAnImmutablePolicyAcrossConcurrentCallsCreatesSeparateOwnedRecords()
    {
        var input = CompilerAnalysisFixtures.Constraint(false); var policy = new CompilerPredicateCapturePolicy(CompilerPredicateCaptureMode.OriginalRootArrays);
        var all = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Capture(input, policy))));
        foreach (var actual in all)
        {
            var record = Record(actual); Assert.Same(policy, record.Policy); Assert.Single(record.Bodies);
            foreach (var other in all.Where(c => !ReferenceEquals(c, actual))) Assert.False(record.IsOwnedBy(other));
        }
    }

    [Fact]
    public void ReturnedCollectionsAndByteCopiesCannotModifyTheOriginalRecord()
    {
        var record = Record(Capture(CompilerAnalysisFixtures.Constraint(false))); var data = record.Data; var digest = record.Digest;
        Assert.False(record.Bodies is IDictionary); var map = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(record.Bodies); Assert.True(map.IsReadOnly);
        var item = Assert.Single(map); Assert.Throws<NotSupportedException>(() => map.Clear()); Assert.Throws<NotSupportedException>(() => map[item.Key] = SourceJson.Parse("[]"));
        Assert.Throws<NotSupportedException>(() => map.Keys.Clear()); Assert.Throws<NotSupportedException>(() => map.Values.Clear());
        var bytes = item.Value.ToCanonicalBytes(); bytes[0] ^= 1;
        Assert.Same(data, record.Data); Assert.Equal(digest, record.Digest); Assert.Equal(item.Key, ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, item.Value.ToCanonicalBytes()).Value);
    }

    [Fact]
    public void CancelledAndResourceLimitedCompilationsExposeAnExplicitGapAndNoBodies()
    {
        var input = CompilerAnalysisFixtures.Constraint(false); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = Capture(input, cancellationToken: cancellation.Token); var record = Record(cancelled);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Result.Execution); Assert.Equal(PredicatePremiseRecordState.CaptureInterrupted, record.State); Assert.Empty(record.Bodies);
        var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = 1;
        var limited = Capture(new(input.Source, Json(settings))); var limitedRecord = Record(limited);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, limited.Result.Execution); Assert.Equal(PredicatePremiseRecordState.CaptureInterrupted, limitedRecord.State); Assert.Empty(limitedRecord.Bodies);
    }

    private sealed class RecordedTransport(CoreCompilation compilation) : IServerCompilerTransport
    {
        public Task<CompilerHostResponse> SubmitAsync(CompilerHostRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompilerHostResponse(CompilerHostStatus.Completed, "example.predicate-record", request.Input.ExpectedInputDigest,
                request.ClientGeneration, Guid.NewGuid(), CompilerScopedPublicationTests.Receipt(request, compilation)));
    }

    [Theory]
    [InlineData(CompilerPredicateCaptureMode.Manifest)] [InlineData(CompilerPredicateCaptureMode.OriginalRootArrays)]
    public async Task ExistingServerProtocolRejectsAnUnrequestedPredicateRecordWithoutReinterpretingOutputCapture(CompilerPredicateCaptureMode mode)
    {
        var input = CompilerAnalysisFixtures.Constraint(false); var captured = Capture(input, new(mode)); var control = PureCoreCompiler.Compile(input, input.Compiler);
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(input.Settings.Value.GetProperty(key).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        var request = new CompilerHostRequest(new(input), 1);
        var normal = await new ServerCompilerHost(profile, new RecordedTransport(control)).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, normal.Status); Assert.Same(control, normal.Compilation);
        var refused = await new ServerCompilerHost(profile, new RecordedTransport(captured)).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.InvalidInput, refused.Status); Assert.Null(refused.Compilation); Assert.Null(refused.Execution);
        Assert.Equal("coglatas.host.result-binding-mismatch", refused.ReasonCode);
        Assert.Equal(control.Result.Data, captured.Result.Data); Assert.True(captured.PredicatePremises!.IsOwnedBy(captured));
    }

    [Fact]
    public void InvalidPolicyAndNullDeclarationsAreRejectedBeforeAnyCapture()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateCapturePolicy((CompilerPredicateCaptureMode)99));
        foreach (var bytes in new[] { -1, 0, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1 }) Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateCapturePolicy(maximumBodyBytes: bytes));
        foreach (var count in new[] { -1, 0, CompilerPredicateCapturePolicy.MaximumBodies + 1 }) Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateCapturePolicy(maximumDistinctBodies: count));
        var input = CompilerAnalysisFixtures.Constraint(false);
        Assert.Throws<ArgumentNullException>(() => PureCoreCompiler.CompileWithPredicateCapture(input, input.Compiler, null!));
        Assert.Throws<ArgumentNullException>(() => PureCoreCompiler.CompileWithPredicateCapture(null!, input.Compiler, new()));
        Assert.Throws<ArgumentNullException>(() => PureCoreCompiler.CompileWithPredicateCapture(input, null!, new()));
    }
}
