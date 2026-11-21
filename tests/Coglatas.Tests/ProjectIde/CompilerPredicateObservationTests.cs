using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerPredicateObservationTests
{
    private static RecordedPredicateCompilation Capture(CompilerInputManifest input, CompilerPredicateObservationPolicy? policy = null,
        CancellationToken token = default) => PureCoreCompiler.CompileWithPredicateObservation(input, input.Compiler,
            policy ?? new(PredicateEvaluationCaptureMode.ObservedOperators), cancellationToken: token);

    private static ProjectCompilerPredicateObservationSnapshot Available(RecordedPredicateCompilation run, CompilerDiagnostic diagnostic, int? budget = null) =>
        ProjectCompilerPredicateObservationSnapshot.Read(run, run.Compilation, run.Observation, diagnostic,
            CompilerPredicateObservationSnapshotMode.AvailableOperators, budget ?? ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes);

    private static CompilerInputManifest Constraint(object ast)
    {
        var template = CompilerAnalysisFixtures.Constraint(false);
        var declarations = JsonNode.Parse(template.Source.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["predicate"] = JsonSerializer.SerializeToNode(ast);
        var relations = JsonNode.Parse(template.Source.Relations.CanonicalText)!.AsArray();
        relations[0]!["payload"]!["predicateDigest"] = JsonNode.Parse(ContentDigest.Compute("coglatas.predicate/1",
            PurePredicate.Parse(SourceJson.Parse(JsonSerializer.Serialize(ast))).Data.ToCanonicalBytes()).Data.CanonicalText);
        return new(ProjectSource.Create(template.Source.Context, template.Source.Documents, Json(declarations), Json(relations)), template.Settings);
    }
    private static object Op(string kind, params object[] operands) => new { kind, operands };
    private static object Literal(bool? value) => PlanDeclarationTests.Literal(value);
    private static CompilerInputManifest Compound() => Constraint(Op("coglatas.and",
        Op("coglatas.or", Literal(false), Literal(true)), Op("coglatas.not", Literal(true)), Literal(null)));
    private static CompilerInputManifest TwoConstraints()
    {
        var first = CompilerAnalysisFixtures.Constraint(false);
        var declarations = JsonNode.Parse(first.Source.Declarations.CanonicalText)!.AsArray();
        var second = declarations[0]!.DeepClone(); second["entityId"] = Id(8); declarations.Add(second);
        var relations = JsonNode.Parse(first.Source.Relations.CanonicalText)!.AsArray();
        var relation = relations[0]!.DeepClone(); relation["relationId"] = Id(21);
        relation["source"]!["reference"]!["entityId"] = Id(8); relation["payload"]!["predicateReference"]!["entityId"] = Id(8); relations.Add(relation);
        return new(ProjectSource.Create(first.Source.Context, first.Source.Documents, Json(declarations), Json(relations)), first.Settings);
    }

    private static void VerifyRecord(RecordedPredicateCompilation run)
    {
        var record = run.Observation;
        Assert.True(record.IsOwnedBy(run)); Assert.True(run.IsFor(run.Compilation));
        Assert.Equal(run.Compilation.Input.InputDigest, record.InputDigest); Assert.Equal(run.Compilation.Result.ResultDigest, record.ResultDigest);
        Assert.Equal(record.Digest, ContentDigest.Compute(ProjectCompilerPredicateObservation.Format, record.Data.ToCanonicalBytes()));
        Assert.Equal("ActualInitialRootEvaluationOnly", record.Data.Value.GetProperty("scope").GetString());
        Assert.Equal("NotRecorded", record.Data.Value.GetProperty("regionalEvaluation").GetString());
        Assert.Equal("NotAllocated", record.Data.Value.GetProperty("compilerRunIdentity").GetString());
        foreach (var field in new[] { "authority", "producerTrust", "eligibility", "runSelection" })
            Assert.Equal("NotEvaluated", record.Data.Value.GetProperty(field).GetString());
        Assert.Equal("NotAdopted", record.Data.Value.GetProperty("retentionPolicy").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void ActualInitialRootTruthKeepsOriginalResultAndOperationsAcrossPredeclaredModes(bool? truth)
    {
        var input = CompilerAnalysisFixtures.Constraint(truth); var legacy = PureCoreCompiler.Compile(input, input.Compiler);
        foreach (var policy in new[] { new CompilerPredicateObservationPolicy(), new(PredicateEvaluationCaptureMode.ObservedOperators) })
        {
            var run = Capture(input, policy); VerifyRecord(run);
            Assert.Equal(legacy.Result.Data, run.Compilation.Result.Data);
            Assert.Equal(legacy.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
            Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(run.Compilation));
            Assert.Null(run.Compilation.PredicatePremises); Assert.Equal(1, run.Observation.StartedEvaluations); Assert.Equal(1, run.Observation.CompletedEvaluations);
            if (truth == true) { Assert.Empty(run.Compilation.Result.Diagnostics); continue; }
            var diagnostic = Assert.Single(run.Compilation.Result.Diagnostics);
            var view = Available(run, diagnostic);
            if (policy.Mode == PredicateEvaluationCaptureMode.Manifest)
            {
                Assert.Equal(CompilerPredicateObservationSnapshotState.NoRecordedOperators, view.State);
                Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames);
            }
            else
            {
                Assert.Equal(CompilerPredicateObservationSnapshotState.AvailableOperators, view.State);
                Assert.Single(view.Bodies); Assert.Single(view.EvaluationFrames);
                if (truth is null) Assert.Equal(CompilerEvidenceKind.Missing, diagnostic.Evidence.Kind);
            }
        }
    }

    public static IEnumerable<object[]> BinaryCases()
    {
        foreach (var kind in new[] { "coglatas.and", "coglatas.or" })
        foreach (var left in new bool?[] { false, true, null })
        foreach (var right in new bool?[] { false, true, null }) yield return [kind, left!, right!];
    }

    [Theory]
    [MemberData(nameof(BinaryCases))]
    public void ActualCompilerBooleanTraversalRetainsThreeValuedTruthAndOriginalShortCircuitVisits(string kind, bool? left, bool? right)
    {
        var input = Constraint(Op(kind, Literal(left), Literal(right))); var run = Capture(input);
        var predicate = Assert.Single(run.Compilation.Model!.Declarations.Select(d => d.Declaration).OfType<ConstraintPlanDeclaration>()).Predicate;
        var evaluation = predicate.Evaluate();
        var metadata = Assert.Single(run.Observation.Data.Value.GetProperty("evaluations").EnumerateArray()).GetProperty("originalObservation");
        Assert.Equal(evaluation.Truth.ToString(), metadata.GetProperty("truth").GetString());
        var shortCircuit = kind == "coglatas.and" && left == false || kind == "coglatas.or" && left == true;
        Assert.Equal(shortCircuit ? 2 : 3, run.Observation.ObservedNodes);
        if (evaluation.Truth == PredicateTruth.True) { Assert.Empty(run.Compilation.Result.Diagnostics); return; }
        var view = Available(run, Assert.Single(run.Compilation.Result.Diagnostics));
        Assert.Equal(shortCircuit ? 2 : 3, view.Bodies.Count);
        var root = Assert.Single(view.Bodies.Values, b => b.Value.GetProperty("ordinal").GetInt32() == 0).Value;
        Assert.Equal(shortCircuit ? 1 : 0, root.GetProperty("skippedOperands").GetInt32());
        Assert.Equal(shortCircuit ? 1 : 2, root.GetProperty("evaluatedChildren").GetArrayLength());
    }

    public static IEnumerable<object[]> NestedCases()
    {
        foreach (var a in new bool?[] { false, true, null })
        foreach (var b in new bool?[] { false, true, null })
        foreach (var c in new bool?[] { false, true, null }) yield return [a!, b!, c!];
    }

    [Theory]
    [MemberData(nameof(NestedCases))]
    public void NestedActualRootEvaluationsKeepTheLegacyCompilerBytesAcrossCaptureGaps(bool? a, bool? b, bool? c)
    {
        var input = Constraint(Op("coglatas.and", Op("coglatas.or", Literal(a), Literal(b)), Op("coglatas.not", Literal(c))));
        var legacy = PureCoreCompiler.Compile(input, input.Compiler);
        foreach (var policy in new[] { new CompilerPredicateObservationPolicy(), new(PredicateEvaluationCaptureMode.ObservedOperators),
                     new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1), new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1) })
        {
            var run = Capture(input, policy);
            Assert.Equal(legacy.Result.Data, run.Compilation.Result.Data); Assert.Equal(legacy.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
            Assert.Equal(1, run.Observation.StartedEvaluations); Assert.Equal(1, run.Observation.CompletedEvaluations);
            foreach (var diagnostic in run.Compilation.Result.Diagnostics)
                if (run.Observation.State != CompilerPredicateObservationCaptureState.RecordedInitialRootEvaluations) Assert.Empty(Available(run, diagnostic).Bodies);
        }
    }

    [Fact]
    public void ActualOperatorOrderAndDuplicatePremisesCannotBeReconstructedFromTheLegacySortedProofArray()
    {
        var input = Compound(); var run = Capture(input); var diagnostic = Assert.Single(run.Compilation.Result.Diagnostics);
        var view = Available(run, diagnostic); Assert.Equal(6, view.Bodies.Count); Assert.Single(view.EvaluationFrames);
        var root = Assert.Single(view.Bodies.Values, b => b.Value.GetProperty("ordinal").GetInt32() == 0).Value;
        Assert.Equal(new[] { 1, 4 }, root.GetProperty("evaluatedChildren").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(1, root.GetProperty("skippedOperands").GetInt32());
        var frame = Assert.Single(view.EvaluationFrames);
        Assert.Equal(frame.Key, ContentDigest.Compute(PredicateEvaluationObservation.EvaluationDomain, frame.Value.ToCanonicalBytes()).Value);
        Assert.Equal(3, frame.Value.Value.GetProperty("premises").GetArrayLength());
        var old = PureCoreCompiler.CompileWithPredicateCapture(input, input.Compiler, new(CompilerPredicateCaptureMode.OriginalRootArrays));
        Assert.Equal(run.Compilation.Result.Data, old.Result.Data); Assert.Equal(run.Compilation.LogicalOperationsConsumed, old.LogicalOperationsConsumed);
        Assert.Equal(2, Assert.Single(old.PredicatePremises!.Bodies).Value.Value.GetArrayLength());
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("pass")]
    [InlineData("unknown")]
    [InlineData("test-assertion-navigation")]
    public void FourUnchangedActualLegacyFixturesKeepTheirOriginalResultBytesAndDeclaredGaps(string key)
    {
        foreach (var entry in RootSourceAdmissionTests.Cases())
        {
            var text = (string)entry[0];
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.GetProperty("key").GetString() != key)
            {
                continue;
            }
            var (control, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text); var run = Capture(control.Input);
            Assert.Equal(control.Result.Data, run.Compilation.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
            Assert.Equal(SourceJson.Parse(json.RootElement.GetProperty("result").GetRawText()), run.Compilation.Result.Data);
            VerifyRecord(run);
            if (key == "test-assertion-navigation") Assert.Equal(CompilerPredicateObservationCaptureState.NoActualEvaluations, run.Observation.State);
            return;
        }
        throw new InvalidOperationException("The unchanged legacy fixture is missing.");
    }

    [Fact]
    public void DefaultMetadataAndAutomaticSerializationContainNoSourceOrOperatorOrEvaluationFrameBodies()
    {
        var input = Compound(); var run = Capture(input); var diagnostic = Assert.Single(run.Compilation.Result.Diagnostics);
        var view = ProjectCompilerPredicateObservationSnapshot.Read(run, run.Compilation, run.Observation, diagnostic);
        Assert.Equal(CompilerPredicateObservationSnapshotState.ManifestOnly, view.State); Assert.Equal(1, view.SelectedEvaluations);
        Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames);
        Assert.Same(run.Observation.Data, view.OriginalRecordMetadata);
        Assert.False(JsonSerializer.SerializeToElement(run).TryGetProperty("Compilation", out _));
        foreach (var text in new[] { JsonSerializer.Serialize(run), JsonSerializer.Serialize(view) })
        {
            Assert.DoesNotContain("evaluatedChildren", text); Assert.DoesNotContain("coglatas.and", text);
            Assert.DoesNotContain(input.Source.Data.CanonicalText, text);
        }
        Assert.Equal(7, Assert.Single(typeof(CoreCompilation).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)).GetParameters().Length);
        Assert.Empty(typeof(RecordedPredicateCompilation).GetConstructors());
    }

    [Fact]
    public void WholeGenuineEqualDigestOlderCompilationsAndDiagnosticsCannotOwnTheCurrentRead()
    {
        var input = Compound(); var old = Capture(input); var current = Capture(input);
        Assert.Equal(old.Observation.Digest, current.Observation.Digest); Assert.Equal(old.Compilation.Result.ResultDigest, current.Compilation.Result.ResultDigest);
        var oldDiagnostic = Assert.Single(old.Compilation.Result.Diagnostics); var diagnostic = Assert.Single(current.Compilation.Result.Diagnostics);
        Assert.Equal(oldDiagnostic.Data, diagnostic.Data); Assert.False(old.Observation.IsOwnedBy(current));
        foreach (var refused in new[] {
            ProjectCompilerPredicateObservationSnapshot.Read(current, current.Compilation, old.Observation, diagnostic),
            ProjectCompilerPredicateObservationSnapshot.Read(current, old.Compilation, current.Observation, diagnostic),
            ProjectCompilerPredicateObservationSnapshot.Read(current, current.Compilation, current.Observation, oldDiagnostic) })
        {
            Assert.Equal(CompilerPredicateObservationSnapshotState.RefusedInvocation, refused.State);
            Assert.Empty(refused.Bodies); Assert.Empty(refused.EvaluationFrames);
            Assert.Null(refused.OriginalRecordMetadata); Assert.Null(refused.OriginalRecordDigest); Assert.Null(refused.OriginalCaptureState);
            Assert.Equal("NotEvaluated", refused.Data.Value.GetProperty("selection").GetString());
        }
        Assert.Equal(6, Available(current, diagnostic).Bodies.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void ActualScopedRegionalDiagnosticsHaveNoBorrowedInitialRootOperatorGraph(bool? truth)
    {
        var first = CompilerAnalysisFixtures.Constraint(truth); var settings = JsonNode.Parse(first.Settings.CanonicalText)!;
        settings["semantics"]!["version"] = "2"; settings["ir"]!["version"] = "2";
        var run = Capture(new(first.Source, Json(settings))); Assert.NotNull(run.Compilation.Publication);
        Assert.Equal(0, run.Observation.StartedEvaluations); Assert.Equal(0, run.Observation.CompletedEvaluations);
        Assert.Equal(CompilerPredicateObservationCaptureState.NoActualEvaluations, run.Observation.State);
        if (truth == true) { Assert.Empty(run.Compilation.Result.Diagnostics); return; }
        var view = Available(run, Assert.Single(run.Compilation.Result.Diagnostics));
        Assert.Equal(CompilerPredicateObservationSnapshotState.NoObservedPublishedDiagnostic, view.State);
        Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames);
        Assert.Equal("NotRecorded", view.Data.Value.GetProperty("regionalEvaluation").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void ActualRequirementConditionNeverAttachesItsGraphToMissingEvidenceOrInventsEligibility(bool? truth)
    {
        var original = Source();
        var source = ProjectSource.Create(original.Context, original.Documents,
            Json(new JsonArray(CompilerAnalysisFixtures.RequirementDeclaration(original.Context, truth))));
        var run = Capture(new(source, Json(CompilerAnalysisFixtures.AllRulesSettings())));
        Assert.Equal(1, run.Observation.StartedEvaluations); Assert.Equal(1, run.Observation.CompletedEvaluations);
        var missing = Assert.Single(run.Compilation.Result.Diagnostics, d => d.Code == "coglatas.analysis.requirement-evidence-unavailable");
        Assert.Equal(CompilerPredicateObservationSnapshotState.NoObservedPublishedDiagnostic, Available(run, missing).State);
        if (truth == false)
        {
            var condition = Assert.Single(run.Compilation.Result.Diagnostics, d => d.Code == "coglatas.analysis.requirement-condition-false");
            Assert.Single(Available(run, condition).Bodies);
        }
        else Assert.DoesNotContain(run.Compilation.Result.Diagnostics, d => d.Code == "coglatas.analysis.requirement-condition-false");
        Assert.False(run.Compilation.Result.EvidenceComplete);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AggregateNodeBodyAndEvaluationLimitsAtomicallyWithdrawEveryEarlierGraph(int choice)
    {
        var input = TwoConstraints(); var legacy = PureCoreCompiler.Compile(input, input.Compiler);
        var policy = new CompilerPredicateObservationPolicy(PredicateEvaluationCaptureMode.ObservedOperators,
            maximumEvaluations: choice == 2 ? 1 : 128, maximumNodes: choice == 0 ? 1 : 1024, maximumBodyBytes: choice == 1 ? 1 : 1048576);
        var run = Capture(input, policy);
        Assert.Equal(2, run.Observation.StartedEvaluations); Assert.Equal(2, run.Observation.CompletedEvaluations);
        Assert.Equal(choice == 0 ? CompilerPredicateObservationCaptureState.NodeLimitReached :
            choice == 1 ? CompilerPredicateObservationCaptureState.BodyBudgetExceeded : CompilerPredicateObservationCaptureState.EvaluationLimitReached, run.Observation.State);
        Assert.Equal(legacy.Result.Data, run.Compilation.Result.Data); Assert.Equal(0, run.Observation.RetainedBodyBytes);
        foreach (var diagnostic in run.Compilation.Result.Diagnostics)
        {
            var view = Available(run, diagnostic, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
            Assert.Equal(CompilerPredicateObservationSnapshotState.NoRecordedOperators, view.State);
            Assert.Equal(run.Observation.State, view.OriginalCaptureState); Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames);
            Assert.Equal("NotRecorded", view.Data.Value.GetProperty("selection").GetString());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ActualResourceExhaustionAfterTwoEvaluationsPreservesEarlierIndependentCaptureGaps(int choice)
    {
        var original = TwoConstraints(); var complete = PureCoreCompiler.Compile(original, original.Compiler);
        var settings = JsonNode.Parse(original.Settings.CanonicalText)!; settings["executionProfile"]!["maximumOperations"] = complete.LogicalOperationsConsumed - 1;
        var input = new CompilerInputManifest(original.Source, Json(settings));
        var policy = new CompilerPredicateObservationPolicy(choice == 0 ? PredicateEvaluationCaptureMode.Manifest : PredicateEvaluationCaptureMode.ObservedOperators,
            maximumEvaluations: choice == 4 ? 1 : 128, maximumNodes: choice == 2 ? 1 : 1024, maximumBodyBytes: choice == 3 ? 1 : 1048576);
        var run = Capture(input, policy); var legacy = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, run.Compilation.Result.Execution);
        Assert.Equal(legacy.Result.Data, run.Compilation.Result.Data); Assert.Equal(legacy.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.Equal(2, run.Observation.StartedEvaluations); Assert.Equal(2, run.Observation.CompletedEvaluations); Assert.Equal(0, run.Observation.RetainedBodyBytes);
        Assert.Equal(choice == 2 ? CompilerPredicateObservationCaptureState.NodeLimitReached : choice == 3 ? CompilerPredicateObservationCaptureState.BodyBudgetExceeded :
            choice == 4 ? CompilerPredicateObservationCaptureState.EvaluationLimitReached : CompilerPredicateObservationCaptureState.CaptureInterrupted, run.Observation.State);
        foreach (var diagnostic in run.Compilation.Result.Diagnostics) Assert.Empty(Available(run, diagnostic).Bodies);
    }

    [Fact]
    public void ViewerBudgetAndCancellationDoNotModifyOriginalCapturedFramesOrRepairAnEarlierGap()
    {
        var run = Capture(Compound()); var diagnostic = Assert.Single(run.Compilation.Result.Diagnostics); var first = Available(run, diagnostic);
        var gap = Available(run, diagnostic, first.RetainedBodyBytes - 1);
        Assert.Equal(CompilerPredicateObservationSnapshotState.BodyBudgetExceeded, gap.State);
        Assert.Equal(CompilerPredicateObservationCaptureState.RecordedInitialRootEvaluations, gap.OriginalCaptureState);
        Assert.Empty(gap.Bodies); Assert.Empty(gap.EvaluationFrames);
        var exact = Available(run, diagnostic, first.RetainedBodyBytes);
        Assert.Equal(first.Bodies.Count, exact.Bodies.Count);
        foreach (var pair in first.Bodies) Assert.Same(pair.Value, exact.Bodies[pair.Key]);
        foreach (var pair in first.EvaluationFrames) Assert.Same(pair.Value, exact.EvaluationFrames[pair.Key]);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectCompilerPredicateObservationSnapshot.Read(run, run.Compilation, run.Observation,
            diagnostic, CompilerPredicateObservationSnapshotMode.AvailableOperators, cancellationToken: cancelled.Token));
        Assert.Equal(first.Bodies.Count, Available(run, diagnostic).Bodies.Count);
    }

    [Fact]
    public void OriginalFrameMapsAndByteCopiesCannotMutateTheOwnedRecord()
    {
        var run = Capture(Compound()); var diagnostic = Assert.Single(run.Compilation.Result.Diagnostics); var view = Available(run, diagnostic);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, SourceJson>)view.Bodies).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, SourceJson>)view.EvaluationFrames).Clear());
        foreach (var pair in view.Bodies)
        {
            Assert.Equal(pair.Key, ContentDigest.Compute(PredicateEvaluationObservation.NodeDomain, pair.Value.ToCanonicalBytes()).Value);
            var bytes = pair.Value.ToCanonicalBytes(); bytes[0] = 0;
            Assert.Same(pair.Value, Available(run, diagnostic).Bodies[pair.Key]);
        }
        Assert.Equal(view.RetainedEvaluationFrameBytes, view.EvaluationFrames.Values.Sum(f => f.ToCanonicalBytes().Length));
    }

    [Fact]
    public void PreCancelledActualCompilerHasNoInventedCompletedEvaluationOrOperatorFrames()
    {
        var input = Compound(); using var source = new CancellationTokenSource(); source.Cancel();
        foreach (var mode in new[] { PredicateEvaluationCaptureMode.Manifest, PredicateEvaluationCaptureMode.ObservedOperators })
        {
            var run = Capture(input, new(mode), source.Token);
            Assert.Equal(CompilerExecutionState.Cancelled, run.Compilation.Result.Execution);
            Assert.Equal(CompilerPredicateObservationCaptureState.CaptureInterrupted, run.Observation.State);
            Assert.Equal(0, run.Observation.StartedEvaluations); Assert.Equal(0, run.Observation.CompletedEvaluations); Assert.Equal(0, run.Observation.RetainedBodyBytes);
            Assert.Empty(run.Observation.Data.Value.GetProperty("evaluations").EnumerateArray());
        }
    }

    [Fact]
    public void InvalidPredeclaredAndViewerLimitsFailBeforeAnyFramePublication()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateObservationPolicy((PredicateEvaluationCaptureMode)100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateObservationPolicy(maximumEvaluations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateObservationPolicy(maximumEvaluations: 129));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateObservationPolicy(maximumNodes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPredicateObservationPolicy(maximumBodyBytes: 0));
        var run = Capture(Compound()); var diagnostic = Assert.Single(run.Compilation.Result.Diagnostics);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectCompilerPredicateObservationSnapshot.Read(run, run.Compilation, run.Observation,
            diagnostic, (CompilerPredicateObservationSnapshotMode)100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Available(run, diagnostic, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Available(run, diagnostic, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1));
        Assert.Equal(6, Available(run, diagnostic).Bodies.Count);
    }

    [Fact]
    public async Task ConcurrentPolicyReuseHasNoSharedCollectorOrCrossInvocationReadAuthority()
    {
        var input = Compound(); var policy = new CompilerPredicateObservationPolicy(PredicateEvaluationCaptureMode.ObservedOperators);
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Capture(input, policy))));
        foreach (var run in runs)
        {
            Assert.Equal(runs[0].Compilation.Result.Data, run.Compilation.Result.Data); VerifyRecord(run);
            Assert.Equal(6, Available(run, Assert.Single(run.Compilation.Result.Diagnostics)).Bodies.Count);
        }
        var foreign = ProjectCompilerPredicateObservationSnapshot.Read(runs[0], runs[0].Compilation, runs[0].Observation,
            Assert.Single(runs[1].Compilation.Result.Diagnostics), CompilerPredicateObservationSnapshotMode.AvailableOperators);
        Assert.Equal(CompilerPredicateObservationSnapshotState.RefusedInvocation, foreign.State);
    }
}
