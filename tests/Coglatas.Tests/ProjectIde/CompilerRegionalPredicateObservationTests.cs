using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerRegionalPredicateObservationTests
{
    private static CompilerInputManifest Upgrade(CompilerInputManifest input)
    {
        var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
        settings["semantics"]!["version"] = "2"; settings["ir"]!["version"] = "2";
        return new(input.Source, Json(settings));
    }
    private static RecordedRegionalPredicateCompilation Capture(CompilerInputManifest input,
        CompilerRegionalPredicateObservationPolicy? policy = null, CompilerMaterializedSnapshot[]? snapshots = null,
        CancellationToken token = default) => PureCoreCompiler.CompileWithRegionalPredicateObservation(input, input.Compiler,
            policy ?? new(PredicateEvaluationCaptureMode.ObservedOperators), snapshots, cancellationToken: token);
    private static ProjectCompilerRegionalPredicateObservationSnapshot View(RecordedRegionalPredicateCompilation run,
        CompilerDependencyRegion region, CompilerDiagnostic diagnostic, int? budget = null) =>
        ProjectCompilerRegionalPredicateObservationSnapshot.Read(run, run.Compilation, run.Observation, region, diagnostic,
            CompilerRegionalPredicateSnapshotMode.AvailableOperators, budget ?? ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes);

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

    [Theory]
    [InlineData(false)] [InlineData(true)] [InlineData(null)]
    public void ActualRegionalTruthPreservesCompilerFactsAndTheOldRootOnlyGap(bool? truth)
    {
        var input = Upgrade(CompilerAnalysisFixtures.Constraint(truth));
        var full = PureCoreCompiler.Compile(input, input.Compiler);
        var old = PureCoreCompiler.CompileWithPredicateObservation(input, input.Compiler, new(PredicateEvaluationCaptureMode.ObservedOperators));
        var run = Capture(input);
        Assert.Equal(full.Result.Data, run.Compilation.Result.Data); Assert.Equal(full.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.Equal(old.Compilation.Result.Data, full.Result.Data);
        Assert.Equal(0, old.Observation.StartedEvaluations); Assert.Equal("NotRecorded", old.Observation.Data.Value.GetProperty("regionalEvaluation").GetString());
        Assert.True(run.IsFor(run.Compilation)); Assert.True(run.Observation.IsOwnedBy(run));
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, run.Observation.State);
        Assert.Equal(1, run.Observation.CompletedRegionCalls); Assert.Equal(1, run.Observation.StartedEvaluations);
        Assert.Equal(1, run.Observation.CompletedEvaluations);
        Assert.Equal("ActualRegionalEvaluationsOnly", run.Observation.Data.Value.GetProperty("scope").GetString());
        Assert.Equal(run.Observation.Digest, ContentDigest.Compute(ProjectCompilerRegionalPredicateObservation.Format, run.Observation.Data.ToCanonicalBytes()));
        var region = Assert.Single(run.Compilation.Dependencies!.Regions);
        var regionMetadata = Assert.Single(run.Observation.Data.Value.GetProperty("regions").EnumerateArray());
        Assert.Equal(region.Data.Value.GetProperty("execution").GetString(), regionMetadata.GetProperty("regionalResult").GetProperty("execution").GetString());
        Assert.Equal(1, run.Observation.Data.Value.GetProperty("completedRegionCalls").GetInt32());
        if (truth == true) { Assert.Empty(run.Compilation.Result.Diagnostics); return; }
        var diagnostic = Assert.Single(region.Diagnostics);
        var view = View(run, region, diagnostic);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, view.State); Assert.Equal(1, view.SelectedEvaluations);
        Assert.NotEmpty(view.Bodies); Assert.Single(view.EvaluationFrames);
        foreach (var pair in view.Bodies) Assert.Equal(pair.Key, ContentDigest.Compute(PredicateEvaluationObservation.NodeDomain, pair.Value.ToCanonicalBytes()).Value);
        foreach (var pair in view.EvaluationFrames) Assert.Equal(pair.Key, ContentDigest.Compute(PredicateEvaluationObservation.EvaluationDomain, pair.Value.ToCanonicalBytes()).Value);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)] [InlineData(null)]
    public void EqualPredicatesInTwoActualRegionsHaveDistinctOwnedInvocationOrdinals(bool? truth)
    {
        var fixture = TwoRegions(truth);
        var full = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        var run = Capture(fixture.Input, snapshots: fixture.Snapshots);
        Assert.Equal(full.Result.Data, run.Compilation.Result.Data); Assert.Equal(full.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.Equal(2, run.Observation.CompletedRegionCalls); Assert.Equal(2, run.Observation.StartedEvaluations); Assert.Equal(2, run.Observation.CompletedEvaluations);
        var evaluations = run.Observation.Data.Value.GetProperty("evaluations").EnumerateArray().ToArray();
        Assert.Equal(new[] { 0, 1 }, evaluations.Select(e => e.GetProperty("ordinal").GetInt32()));
        Assert.NotEqual(evaluations[0].GetProperty("bindingRegionDigest").GetRawText(), evaluations[1].GetProperty("bindingRegionDigest").GetRawText());
        var regions = run.Compilation.Dependencies!.Regions;
        foreach (var region in regions)
        {
            if (truth == true) { Assert.Empty(region.Diagnostics); continue; }
            var diagnostic = Assert.Single(region.Diagnostics); var view = View(run, region, diagnostic);
            Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, view.State);
            var foreign = Assert.Single(regions, r => !ReferenceEquals(r, region));
            Assert.Equal(CompilerRegionalPredicateSnapshotState.RefusedInvocation, View(run, foreign, diagnostic).State);
        }
        if (truth == true) return;
        var dependency = Assert.Single(regions, r => !r.Binding.IsRoot);
        var summary = Assert.Single(run.Compilation.Result.Diagnostics, d => d.Code == "coglatas.analysis.dependency-finding");
        Assert.Equal(CompilerRegionalPredicateSnapshotState.RefusedInvocation, View(run, dependency, summary).State);
    }

    [Theory]
    [InlineData("manifest")] [InlineData("evaluations")] [InlineData("nodes")] [InlineData("bodies")]
    public void AggregateCaptureLimitsWithdrawEveryRegionalGraphWithoutChangingTruth(string mode)
    {
        var fixture = TwoRegions(false);
        var policy = mode switch {
            "manifest" => new CompilerRegionalPredicateObservationPolicy(),
            "evaluations" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1),
            "nodes" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1),
            _ => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1) };
        var run = Capture(fixture.Input, policy, fixture.Snapshots);
        var expected = mode switch {
            "manifest" => CompilerRegionalPredicateCaptureState.ManifestOnly,
            "evaluations" => CompilerRegionalPredicateCaptureState.EvaluationLimitReached,
            "nodes" => CompilerRegionalPredicateCaptureState.NodeLimitReached,
            _ => CompilerRegionalPredicateCaptureState.BodyBudgetExceeded };
        Assert.Equal(expected, run.Observation.State); Assert.Equal(2, run.Observation.StartedEvaluations);
        Assert.Equal(0, run.Observation.RetainedBodyBytes);
        Assert.Equal(PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots).Result.Data, run.Compilation.Result.Data);
        foreach (var region in run.Compilation.Dependencies!.Regions)
        {
            var view = View(run, region, Assert.Single(region.Diagnostics));
            Assert.Equal(CompilerRegionalPredicateSnapshotState.NoRecordedOperators, view.State);
            Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames);
            Assert.Equal("NotRecorded", view.Data.Value.GetProperty("selection").GetString());
        }
    }

    [Fact]
    public void EqualOlderRecordsRegionsAndDiagnosticsCannotOwnTheCurrentRead()
    {
        var input = Upgrade(CompilerAnalysisFixtures.Constraint(false)); var current = Capture(input); var older = Capture(input);
        Assert.Equal(current.Observation.Data, older.Observation.Data);
        var region = Assert.Single(current.Compilation.Dependencies!.Regions); var diagnostic = Assert.Single(region.Diagnostics);
        var foreign = Assert.Single(older.Compilation.Dependencies!.Regions); var foreignDiagnostic = Assert.Single(foreign.Diagnostics);
        Assert.Equal(diagnostic.Data, foreignDiagnostic.Data);
        var refusals = new[] {
            ProjectCompilerRegionalPredicateObservationSnapshot.Read(older, current.Compilation, current.Observation, region, diagnostic),
            ProjectCompilerRegionalPredicateObservationSnapshot.Read(current, older.Compilation, current.Observation, region, diagnostic),
            View(current, foreign, foreignDiagnostic), View(current, region, foreignDiagnostic) };
        foreach (var refusal in refusals)
        {
            Assert.Equal(CompilerRegionalPredicateSnapshotState.RefusedInvocation, refusal.State);
            Assert.Null(refusal.OriginalRecordMetadata); Assert.Null(refusal.OriginalRecordDigest); Assert.Null(refusal.OriginalCaptureState);
            Assert.Equal(0, refusal.SelectedEvaluations); Assert.Empty(refusal.Bodies); Assert.Empty(refusal.EvaluationFrames);
            Assert.Equal("NotEvaluated", refusal.Data.Value.GetProperty("selection").GetString());
        }
    }

    [Fact]
    public void ExplicitViewerBudgetAndManifestReadsPreserveOriginalCapture()
    {
        var run = Capture(Upgrade(CompilerAnalysisFixtures.Constraint(false)));
        var region = Assert.Single(run.Compilation.Dependencies!.Regions); var diagnostic = Assert.Single(region.Diagnostics);
        var original = run.Observation.Data; var digest = run.Observation.Digest; var available = View(run, region, diagnostic);
        var manifest = ProjectCompilerRegionalPredicateObservationSnapshot.Read(run, run.Compilation, run.Observation, region, diagnostic);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.ManifestOnly, manifest.State); Assert.Empty(manifest.Bodies); Assert.Empty(manifest.EvaluationFrames);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, View(run, region, diagnostic, available.RetainedBodyBytes).State);
        var refused = View(run, region, diagnostic, available.RetainedBodyBytes - 1);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.BodyBudgetExceeded, refused.State); Assert.Empty(refused.Bodies); Assert.Empty(refused.EvaluationFrames);
        Assert.Same(original, run.Observation.Data); Assert.Same(digest, run.Observation.Digest);
    }

    [Fact]
    public void PrecancelledCompileAndReadHaveNoAcceptedPartialBodies()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var input = Upgrade(CompilerAnalysisFixtures.Constraint(false)); var interrupted = Capture(input, token: cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, interrupted.Compilation.Result.Execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, interrupted.Observation.State);
        Assert.Equal(0, interrupted.Observation.RetainedBodyBytes);
        var completed = Capture(input); var region = Assert.Single(completed.Compilation.Dependencies!.Regions); var diagnostic = Assert.Single(region.Diagnostics);
        var token = cancellation.Token;
        Assert.Throws<OperationCanceledException>(() => ProjectCompilerRegionalPredicateObservationSnapshot.Read(completed,
            completed.Compilation, completed.Observation, region, diagnostic, cancellationToken: token));
    }

    public static IEnumerable<object[]> BooleanCases()
    {
        foreach (var kind in new[] { "coglatas.and", "coglatas.or" })
            foreach (bool? left in new bool?[] { false, true, null })
                foreach (bool? right in new bool?[] { false, true, null }) yield return [kind, left!, right!];
    }
    private static CompilerInputManifest PredicateInput(object ast, int? maximumOperations = null)
    {
        var template = CompilerAnalysisFixtures.Constraint(false);
        var declarations = JsonNode.Parse(template.Source.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["predicate"] = System.Text.Json.JsonSerializer.SerializeToNode(ast);
        var relations = JsonNode.Parse(template.Source.Relations.CanonicalText)!.AsArray();
        relations[0]!["payload"]!["predicateDigest"] = JsonNode.Parse(ContentDigest.Compute("coglatas.predicate/1",
            PurePredicate.Parse(SourceJson.Parse(System.Text.Json.JsonSerializer.Serialize(ast))).Data.ToCanonicalBytes()).Data.CanonicalText);
        var settings = JsonNode.Parse(template.Settings.CanonicalText)!.AsObject();
        if (maximumOperations is { } operations) settings["executionProfile"]!["maximumOperations"] = operations;
        return Upgrade(new(ProjectSource.Create(template.Source.Context, template.Source.Documents, Json(declarations), Json(relations)), Json(settings)));
    }
    private static object Literal(bool? value) => PlanDeclarationTests.Literal(value);
    private static object Op(string kind, params object[] operands) => new { kind, operands };
    private static CompilerInputManifest Compound() => PredicateInput(Op("coglatas.and",
        Op("coglatas.or", Literal(false), Literal(true)), Op("coglatas.not", Literal(true)), Literal(null)));

    [Theory]
    [MemberData(nameof(BooleanCases))]
    public void ActualRegionalTraversalKeepsThreeValuedTruthAndOriginalShortCircuitOrder(string kind, bool? left, bool? right)
    {
        var ast = Op(kind, Literal(left), Literal(right)); var input = PredicateInput(ast);
        var full = PureCoreCompiler.Compile(input, input.Compiler); var run = Capture(input);
        Assert.Equal(full.Result.Data, run.Compilation.Result.Data); Assert.Equal(full.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        var control = PurePredicate.Parse(SourceJson.Parse(System.Text.Json.JsonSerializer.Serialize(ast))).Evaluate();
        var shortCircuit = kind == "coglatas.and" && left == false || kind == "coglatas.or" && left == true;
        Assert.Equal(shortCircuit ? 2 : 3, run.Observation.ObservedNodes);
        var region = Assert.Single(run.Compilation.Dependencies!.Regions);
        if (control.Truth == PredicateTruth.True) { Assert.Empty(region.Diagnostics); return; }
        var view = View(run, region, Assert.Single(region.Diagnostics));
        Assert.Equal(shortCircuit ? 2 : 3, view.Bodies.Count);
        var root = Assert.Single(view.Bodies.Values, b => b.Value.GetProperty("ordinal").GetInt32() == 0).Value;
        Assert.Equal(shortCircuit ? 1 : 0, root.GetProperty("skippedOperands").GetInt32());
        Assert.Equal(shortCircuit ? new[] { 1 } : new[] { 1, 2 }, root.GetProperty("evaluatedChildren").EnumerateArray().Select(n => n.GetInt32()));
    }

    [Fact]
    public void RegionalCompoundFramesPreserveActualOrderedDuplicatePremises()
    {
        var run = Capture(Compound()); var region = Assert.Single(run.Compilation.Dependencies!.Regions);
        var view = View(run, region, Assert.Single(region.Diagnostics));
        Assert.Equal(6, view.Bodies.Count); Assert.Single(view.EvaluationFrames);
        var root = Assert.Single(view.Bodies.Values, b => b.Value.GetProperty("ordinal").GetInt32() == 0).Value;
        Assert.Equal(new[] { 1, 4 }, root.GetProperty("evaluatedChildren").EnumerateArray().Select(n => n.GetInt32()));
        Assert.Equal(1, root.GetProperty("skippedOperands").GetInt32());
        var frame = Assert.Single(view.EvaluationFrames).Value.Value;
        var premises = frame.GetProperty("premises").EnumerateArray().Select(p => p.GetRawText()).ToArray();
        Assert.Equal(3, premises.Length); Assert.Equal(2, premises.Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(premises[0], premises[1]); Assert.Equal(premises[1], premises[2]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)] [InlineData(null)]
    public void RequirementConditionsNeverAttachOperatorsToTheirMissingEvidenceDiagnostic(bool? truth)
    {
        var source = Source();
        var input = Upgrade(new(ProjectSource.Create(source.Context, source.Documents,
            Json(new JsonArray(CompilerAnalysisFixtures.RequirementDeclaration(source.Context, truth)))), Json(CompilerAnalysisFixtures.AllRulesSettings())));
        var full = PureCoreCompiler.Compile(input, input.Compiler); var run = Capture(input);
        Assert.Equal(full.Result.Data, run.Compilation.Result.Data); Assert.Equal(1, run.Observation.CompletedEvaluations);
        var region = Assert.Single(run.Compilation.Dependencies!.Regions);
        var missing = Assert.Single(region.Diagnostics, d => d.Code == "coglatas.analysis.requirement-evidence-unavailable");
        var view = View(run, region, missing);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.NoObservedRegionalDiagnostic, view.State);
        Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames);
        Assert.False(run.Compilation.Result.EvidenceComplete);
        if (truth != false)
        {
            Assert.Single(region.Diagnostics); Assert.DoesNotContain(region.Diagnostics, d => d.Code.Contains("requirement-condition", StringComparison.Ordinal)); return;
        }
        var condition = Assert.Single(region.Diagnostics, d => d.Code == "coglatas.analysis.requirement-condition-false");
        Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, View(run, region, condition).State);
    }

    [Fact]
    public void ARealInitialRootFailureWithoutRegionalCallsRemainsExplicitlyUnrecorded()
    {
        var input = CompilerAnalysisFixtures.Constraint(false); var full = PureCoreCompiler.Compile(input, input.Compiler);
        var run = Capture(input); var old = PureCoreCompiler.CompileWithPredicateObservation(input, input.Compiler, new(PredicateEvaluationCaptureMode.ObservedOperators));
        Assert.Equal(CompilerVerdict.Fail, full.Result.Verdict); Assert.Equal(full.Result.Data, run.Compilation.Result.Data);
        Assert.Equal(1, old.Observation.CompletedEvaluations); Assert.Equal(0, run.Observation.CompletedRegionCalls);
        Assert.Equal(0, run.Observation.StartedEvaluations); Assert.Equal(CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations, run.Observation.State);
        Assert.Equal("NotRecorded", run.Observation.Data.Value.GetProperty("initialRootEvaluation").GetString());
        Assert.Equal(0, run.Observation.RetainedBodyBytes);
    }

    [Fact]
    public void VersionOneDependencyReevaluationRemainsSeparateFromActualInitialRootObservation()
    {
        var fixture = TwoRegions(false); var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["semantics"]!["version"] = "1"; settings["ir"]!["version"] = "1";
        var input = new CompilerInputManifest(fixture.Input.Source, Json(settings));
        var run = Capture(input, snapshots: fixture.Snapshots);
        var old = PureCoreCompiler.CompileWithPredicateObservation(input, input.Compiler, new(PredicateEvaluationCaptureMode.ObservedOperators), fixture.Snapshots);
        Assert.Equal(old.Compilation.Result.Data, run.Compilation.Result.Data);
        Assert.Equal(old.Compilation.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.Equal(0, old.Observation.CompletedEvaluations); Assert.Equal(CompilerPredicateObservationCaptureState.NoActualEvaluations, old.Observation.State);
        Assert.Contains(run.Compilation.Result.Diagnostics, d => d.Code == "coglatas.semantic.external-closure-not-lowered");
        Assert.Equal(2, run.Observation.CompletedEvaluations);
        Assert.Equal(2, run.Observation.CompletedRegionCalls);
        foreach (var region in run.Compilation.Dependencies!.Regions)
            Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, View(run, region, Assert.Single(region.Diagnostics)).State);
    }

    private sealed class CancellingMaterializations(CompilerMaterializedSnapshot snapshot) : IEnumerable<CompilerMaterializedSnapshot?>, IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        internal bool Entered { get; private set; }
        internal bool EnumerationDisposed { get; private set; }
        internal CancellationToken Token => _cancellation.Token;
        internal bool IsCancellationRequested => _cancellation.IsCancellationRequested;
        public IEnumerator<CompilerMaterializedSnapshot?> GetEnumerator()
        {
            try { Entered = true; _cancellation.Cancel(); yield return snapshot; }
            finally { EnumerationDisposed = true; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => _cancellation.Dispose();
    }

    [Fact]
    public void ActualCancelledMaterializationEnumerationCannotLeaveRegionalFrames()
    {
        var fixture = TwoRegions(false);

        using var supply = new CancellingMaterializations(fixture.Snapshots[0]);
        var source = fixture.Input.Source.Data;
        var run = PureCoreCompiler.CompileWithRegionalPredicateObservation(fixture.Input, fixture.Input.Compiler,
            new(PredicateEvaluationCaptureMode.ObservedOperators), supply, cancellationToken: supply.Token);
        Assert.True(supply.Entered); Assert.True(supply.EnumerationDisposed); Assert.True(supply.IsCancellationRequested);
        Assert.Equal(CompilerExecutionState.Cancelled, run.Compilation.Result.Execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, run.Observation.State);
        Assert.Equal(0, run.Observation.CompletedRegionCalls); Assert.Equal(0, run.Observation.RetainedBodyBytes); Assert.Same(source, fixture.Input.Source.Data);
    }

    [Fact]
    public async Task ConcurrentPolicyReuseKeepsActualRegionOwnershipAndOriginalFactsIsolated()
    {
        var input = Compound(); var source = input.Source.Data; var policy = new CompilerRegionalPredicateObservationPolicy(PredicateEvaluationCaptureMode.ObservedOperators);
        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Capture(input, policy))));
        foreach (var run in runs)
        {
            Assert.Equal(runs[0].Compilation.Result.Data, run.Compilation.Result.Data); Assert.Equal(runs[0].Observation.Data, run.Observation.Data);
            Assert.True(run.Observation.IsOwnedBy(run)); Assert.Equal(1, run.Observation.CompletedEvaluations);
            var region = Assert.Single(run.Compilation.Dependencies!.Regions);
            Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, View(run, region, Assert.Single(region.Diagnostics)).State);
        }
        var actual = Assert.Single(runs[0].Compilation.Dependencies!.Regions);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.RefusedInvocation, View(runs[1], actual, Assert.Single(actual.Diagnostics)).State);
        Assert.Same(source, input.Source.Data);
    }

    [Fact]
    public void ReturnedOperatorAndEvaluationMapsCannotMutateTheOriginalRegionalFrames()
    {
        var run = Capture(Compound()); var region = Assert.Single(run.Compilation.Dependencies!.Regions); var diagnostic = Assert.Single(region.Diagnostics);
        var view = View(run, region, diagnostic); var before = run.Observation.Data; var digest = run.Observation.Digest;
        foreach (var map in new[] { view.Bodies, view.EvaluationFrames })
        {
            Assert.False(map is System.Collections.ICollection); Assert.False(map.Keys is System.Collections.ICollection); Assert.False(map.Values is System.Collections.ICollection);
            var item = map.First(); var writable = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(map);
            Assert.Throws<NotSupportedException>(() => writable[item.Key] = SourceJson.Parse("{}"));
            Assert.Throws<NotSupportedException>(() => writable.Clear());
            var copy = item.Value.ToCanonicalBytes(); copy[0] = (byte)'!';
            var detached = JsonNode.Parse(item.Value.CanonicalText)!.AsObject(); detached["changed"] = true;
            Assert.NotEqual(copy, item.Value.ToCanonicalBytes()); Assert.False(item.Value.Value.TryGetProperty("changed", out _));
        }
        Assert.Same(before, run.Observation.Data); Assert.Same(digest, run.Observation.Digest);
        var again = View(run, region, diagnostic);
        Assert.Equal(view.Data, again.Data); Assert.Equal(view.Bodies.Select(p => (p.Key, p.Value)), again.Bodies.Select(p => (p.Key, p.Value)));
    }

    [Fact]
    public void MetadataSerializationContainsNoOriginalSourceOrOperatorBodiesOrCompilation()
    {
        var input = Compound(); var run = Capture(input); var region = Assert.Single(run.Compilation.Dependencies!.Regions);
        var manifest = ProjectCompilerRegionalPredicateObservationSnapshot.Read(run, run.Compilation, run.Observation, region, Assert.Single(region.Diagnostics));
        Assert.False(System.Text.Json.JsonSerializer.SerializeToElement(run).TryGetProperty("Compilation", out _));
        Assert.Same(run.Observation.Data, manifest.OriginalRecordMetadata); Assert.Empty(manifest.Bodies); Assert.Empty(manifest.EvaluationFrames);
        foreach (var text in new[] { System.Text.Json.JsonSerializer.Serialize(run), System.Text.Json.JsonSerializer.Serialize(manifest) })
        {
            Assert.DoesNotContain("evaluatedChildren", text); Assert.DoesNotContain("coglatas.and", text); Assert.DoesNotContain(input.Source.Data.CanonicalText, text);
        }
    }

    [Fact]
    public void InvalidCaptureAndViewerPoliciesNeverReturnAnOwnedBodyView()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy((PredicateEvaluationCaptureMode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy(maximumEvaluations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy(maximumEvaluations: 129));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy(maximumNodes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy(maximumBodyBytes: 0));
        var run = Capture(Compound()); var region = Assert.Single(run.Compilation.Dependencies!.Regions); var diagnostic = Assert.Single(region.Diagnostics);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectCompilerRegionalPredicateObservationSnapshot.Read(run, run.Compilation,
            run.Observation, region, diagnostic, (CompilerRegionalPredicateSnapshotMode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => View(run, region, diagnostic, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => View(run, region, diagnostic, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1));
    }

    [Theory]
    [InlineData("none")] [InlineData("evaluations")] [InlineData("nodes")] [InlineData("bodies")]
    public void ActualPostEvaluationResourceExhaustionKeepsEarlierCaptureGapsIndependent(string limit)
    {
        var fixture = TwoRegions(false); var complete = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = complete.LogicalOperationsConsumed - 1;
        var input = new CompilerInputManifest(fixture.Input.Source, Json(settings));
        var policy = limit switch {
            "evaluations" => new CompilerRegionalPredicateObservationPolicy(PredicateEvaluationCaptureMode.ObservedOperators, maximumEvaluations: 1),
            "nodes" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1),
            "bodies" => new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: 1),
            _ => new(PredicateEvaluationCaptureMode.ObservedOperators) };
        var run = Capture(input, policy, fixture.Snapshots); var full = PureCoreCompiler.Compile(input, input.Compiler, fixture.Snapshots);
        Assert.Equal(full.Result.Data, run.Compilation.Result.Data); Assert.Equal(CompilerExecutionState.ResourceExhausted, full.Result.Execution);
        Assert.Equal(2, run.Observation.StartedEvaluations); Assert.Equal(2, run.Observation.CompletedEvaluations);
        Assert.Equal(0, run.Observation.RetainedBodyBytes);
        Assert.Equal(limit switch {
            "evaluations" => CompilerRegionalPredicateCaptureState.EvaluationLimitReached,
            "nodes" => CompilerRegionalPredicateCaptureState.NodeLimitReached,
            "bodies" => CompilerRegionalPredicateCaptureState.BodyBudgetExceeded,
            _ => CompilerRegionalPredicateCaptureState.CaptureInterrupted }, run.Observation.State);
    }

    [Theory]
    [InlineData(128)] [InlineData(129)]
    public void ExactAggregateEvaluationCeilingSpansBothActualRegions(int count)
    {
        var fixture = TwoRegions(false, count - 64, 64); var run = Capture(fixture.Input, snapshots: fixture.Snapshots);
        Assert.Equal(PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots).Result.Data, run.Compilation.Result.Data);
        Assert.Equal(CompilerExecutionState.Completed, run.Compilation.Result.Execution);
        Assert.Equal(2, run.Observation.CompletedRegionCalls); Assert.Equal(count, run.Observation.StartedEvaluations); Assert.Equal(count, run.Observation.CompletedEvaluations);
        Assert.Equal(count == 128 ? CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations : CompilerRegionalPredicateCaptureState.EvaluationLimitReached, run.Observation.State);
        Assert.Equal(count == 128 ? 128 : 0, run.Observation.Data.Value.GetProperty("evaluations").GetArrayLength());
        if (count == 129) Assert.Equal(0, run.Observation.RetainedBodyBytes);
        foreach (var region in run.Compilation.Dependencies!.Regions)
        {
            var view = View(run, region, region.Diagnostics[0]);
            Assert.Equal(count == 128 ? CompilerRegionalPredicateSnapshotState.AvailableOperators : CompilerRegionalPredicateSnapshotState.NoRecordedOperators, view.State);
        }
    }

    private static object Balanced(string kind, bool[] leaves)
    {
        if (leaves.Length == 1) return Literal(leaves[0]);
        var middle = leaves.Length / 2;
        return Op(kind, Balanced(kind, leaves[..middle]), Balanced(kind, leaves[middle..]));
    }
    private static CompilerBindingClosureTests.Fixture TwoPredicates(object rootAst, object externalAst)
    {
        var fixture = TwoRegions(false);
        ProjectSource Rewrite(ProjectSource source, object ast)
        {
            var declarations = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            declarations.Single(d => d!["entityId"]!.GetValue<string>() == Id(85))!["payload"]!["predicate"] = System.Text.Json.JsonSerializer.SerializeToNode(ast);
            var relations = JsonNode.Parse(source.Relations.CanonicalText)!.AsArray();
            relations.Single(r => r!["relationId"]!.GetValue<string>() == Id(200))!["payload"]!["predicateDigest"] =
                JsonNode.Parse(ContentDigest.Compute("coglatas.predicate/1", PurePredicate.Parse(SourceJson.Parse(System.Text.Json.JsonSerializer.Serialize(ast))).Data.ToCanonicalBytes()).Data.CanonicalText);
            return ProjectSource.Create(source.Context, source.Documents, Json(declarations), Json(relations));
        }
        var original = Assert.Single(fixture.Snapshots); var external = Rewrite(original.Snapshot.Source, externalAst);
        var root = Rewrite(fixture.Input.Source, rootAst); var declarations = JsonNode.Parse(root.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(external.Digest.Data.CanonicalText);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["authorizedExternalSnapshots"]![0]!["digest"] = JsonNode.Parse(external.Digest.Data.CanonicalText);
        settings["executionProfile"]!["maximumOperations"] = 10000000;
        return fixture with { Input = new(ProjectSource.Create(root.Context, root.Documents, Json(declarations), root.Relations), Json(settings)),
            Snapshots = [new(original.SnapshotId, original.Version, new(external, true, true, true))] };
    }

    [Theory]
    [InlineData(1024)] [InlineData(1025)]
    public void ActualVisitedNodesAtAndBeyondTheHardCaptureCeilingPreserveCompilerTruth(int totalNodes)
    {
        var root = Op("coglatas.not", Balanced("coglatas.or", Enumerable.Repeat(false, 255).Append(true).ToArray()));
        var external = totalNodes == 1024 ? root : Op("coglatas.not", Op("coglatas.not",
            Balanced("coglatas.and", Enumerable.Repeat(true, 255).Append(false).ToArray())));
        var fixture = TwoPredicates(root, external);
        var run = Capture(fixture.Input, new(PredicateEvaluationCaptureMode.ObservedOperators,
            maximumBodyBytes: ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes), fixture.Snapshots);
        var full = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        Assert.Equal(full.Result.Data, run.Compilation.Result.Data); Assert.Equal(full.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.Equal(CompilerExecutionState.Completed, full.Result.Execution); Assert.Equal(CompilerVerdict.Fail, full.Result.Verdict);
        Assert.Equal(2, run.Observation.CompletedEvaluations);
        Assert.Equal(totalNodes == 1024 ? CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations : CompilerRegionalPredicateCaptureState.NodeLimitReached, run.Observation.State);
        foreach (var region in run.Compilation.Dependencies!.Regions)
        {
            var view = View(run, region, Assert.Single(region.Diagnostics), ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
            if (totalNodes == 1024) { Assert.Equal(1024, run.Observation.ObservedNodes); Assert.Equal(512, view.Bodies.Count); }
            else { Assert.Empty(view.Bodies); Assert.Empty(view.EvaluationFrames); Assert.Equal(0, run.Observation.RetainedBodyBytes); }
        }
    }

    [Theory]
    [InlineData(1023)] [InlineData(1024)]
    public void InvalidFlatArityRemainsRefusedBeforeAnyCompilerObservation(int leaves)
    {
        var ast = Op("coglatas.and", Enumerable.Repeat(Literal(true), leaves - 1).Append(Literal(false)).ToArray());
        var data = SourceJson.Parse(System.Text.Json.JsonSerializer.Serialize(ast));
        Assert.Throws<FormatException>(() => PurePredicate.Parse(data));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExactOriginalOperatorByteBudgetIsMeasuredBeforeTheRun(bool below)
    {
        var input = Compound(); var measured = Capture(input); var bytes = measured.Observation.RetainedBodyBytes;
        Assert.True(bytes > 1);
        var run = Capture(input, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: bytes - (below ? 1 : 0)));
        Assert.Equal(measured.Compilation.Result.Data, run.Compilation.Result.Data);
        Assert.Equal(below ? CompilerRegionalPredicateCaptureState.BodyBudgetExceeded : CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, run.Observation.State);
        var region = Assert.Single(run.Compilation.Dependencies!.Regions); var view = View(run, region, Assert.Single(region.Diagnostics));
        Assert.Equal(below ? CompilerRegionalPredicateSnapshotState.NoRecordedOperators : CompilerRegionalPredicateSnapshotState.AvailableOperators, view.State);
        Assert.Equal(below ? 0 : bytes, run.Observation.RetainedBodyBytes);
    }

    [Fact]
    public void SameBranchSnapshotVersionsRetainSeparateActualRegionAndDiagnosticOwnership()
    {
        var fixture = CompilerBindingClosureTests.Graph(twoBVersions: true);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["rules"] = CompilerAnalysisFixtures.AllRulesSettings()["rules"]!.DeepClone(); settings["executionProfile"]!["maximumOperations"] = 1000000;
        var snapshots = fixture.Snapshots.Select(snapshot => {
            if (snapshot.Snapshot.Source.Context.Branch != fixture.B.Branch) return snapshot;
            var source = Constraints(snapshot.Snapshot.Source, false);
            var pin = settings["authorizedExternalSnapshots"]!.AsArray().Single(p => p!["snapshotId"]!.GetValue<string>() == snapshot.SnapshotId.ToString())!;
            pin["digest"] = JsonNode.Parse(source.Digest.Data.CanonicalText);
            return new CompilerMaterializedSnapshot(snapshot.SnapshotId, snapshot.Version, new(source, true, true, true));
        }).ToArray();
        var input = Upgrade(new(fixture.Input.Source, Json(settings))); var full = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
        var run = Capture(input, snapshots: snapshots); Assert.Equal(full.Result.Data, run.Compilation.Result.Data);
        Assert.Equal(4, run.Observation.CompletedRegionCalls); Assert.Equal(2, run.Observation.CompletedEvaluations);
        var versions = run.Compilation.Dependencies!.Regions.Where(r => r.Binding.Source.Context.Branch == fixture.B.Branch).ToArray();
        Assert.Equal(2, versions.Length); Assert.NotEqual(versions[0].Binding.Source.Context, versions[1].Binding.Source.Context);
        foreach (var region in versions)
        {
            var diagnostic = Assert.Single(region.Diagnostics); Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, View(run, region, diagnostic).State);
            var other = Assert.Single(versions, r => !ReferenceEquals(r, region));
            Assert.Equal(CompilerRegionalPredicateSnapshotState.RefusedInvocation, View(run, other, diagnostic).State);
        }
    }

    private static (CompilerInputManifest Input, CompilerMaterializedSnapshot[] Snapshots) DeclaredRoster(int operations)
    {
        var original = Source(); var settings = CompilerAnalysisFixtures.AllRulesSettings(); var pins = new JsonArray(); var snapshots = new List<CompilerMaterializedSnapshot>();
        for (var ordinal = 0; ordinal < 32; ordinal++)
        {
            var context = SourceRevisionContext.Committed(new(new(original.TenantId, ProjectId.Parse(Id(1000 + ordinal)), BranchId.Parse(Id(2000 + ordinal))),
                RevisionId.Parse(Id(3000 + ordinal))));
            var source = Constraints(ProjectSource.Create(context, original.Documents), false);
            var id = ExternalSnapshotId.Parse(Id(4000 + ordinal));
            snapshots.Add(new(id, "1", new(source, true, true, true)));
            pins.Add(Obj(new { snapshotId = id.ToString(), version = "1", context = context.Data.Value, digest = source.Digest.Data.Value,
                coverage = "coglatas.complete", asOf = (string?)null }));
        }
        settings["authorizedExternalSnapshots"] = pins; settings["executionProfile"]!["maximumOperations"] = operations;
        return (Upgrade(new(original, Json(settings))), snapshots.ToArray());
    }

    [Fact]
    public void MaximumActualDeclaredSnapshotRosterRemainsBoundedAndSeparatelyOwned()
    {
        var (input, supplied) = DeclaredRoster(10000000); var run = Capture(input, snapshots: supplied);
        var full = PureCoreCompiler.Compile(input, input.Compiler, supplied);
        Assert.Equal(full.Result.Data, run.Compilation.Result.Data); Assert.Equal(full.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.Equal(CompilerExecutionState.Completed, full.Result.Execution);
        Assert.Equal(33, run.Observation.CompletedRegionCalls); Assert.Equal(32, run.Observation.CompletedEvaluations);
        Assert.Equal(33, run.Observation.Data.Value.GetProperty("regions").GetArrayLength()); Assert.Equal(32, run.Observation.Data.Value.GetProperty("evaluations").GetArrayLength());
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, run.Observation.State);
        Assert.Equal(33, run.Compilation.Dependencies!.Regions.Count);
        foreach (var region in run.Compilation.Dependencies.Regions.Where(r => !r.Binding.IsRoot))
            Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, View(run, region, Assert.Single(region.Diagnostics)).State);
    }

    [Theory]
    [InlineData(128)] [InlineData(129)]
    public void OriginalSmallerCompilerBudgetStillRefusesLargeEvaluationFixturesBeforeAnyObservedCall(int count)
    {
        var fixture = TwoRegions(false, count - 64, 64); var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = 1000000;
        var input = new CompilerInputManifest(fixture.Input.Source, Json(settings)); var run = Capture(input, snapshots: fixture.Snapshots);
        Assert.Equal(PureCoreCompiler.Compile(input, input.Compiler, fixture.Snapshots).Result.Data, run.Compilation.Result.Data);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, run.Compilation.Result.Execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, run.Observation.State);
        Assert.Equal(0, run.Observation.CompletedRegionCalls); Assert.Equal(0, run.Observation.CompletedEvaluations); Assert.Equal(0, run.Observation.RetainedBodyBytes);
    }

    [Fact]
    public void OriginalSmallerCompilerBudgetStillRefusesTheMaximumSnapshotRosterBeforeEvaluation()
    {
        var (input, snapshots) = DeclaredRoster(1000000); var run = Capture(input, snapshots: snapshots);
        Assert.Equal(PureCoreCompiler.Compile(input, input.Compiler, snapshots).Result.Data, run.Compilation.Result.Data);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, run.Compilation.Result.Execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, run.Observation.State);
        Assert.Equal(0, run.Observation.CompletedRegionCalls); Assert.Equal(0, run.Observation.CompletedEvaluations); Assert.Equal(0, run.Observation.RetainedBodyBytes);
    }
}
