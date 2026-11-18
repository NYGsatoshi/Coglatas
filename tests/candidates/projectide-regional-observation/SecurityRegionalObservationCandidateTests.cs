using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent public-API regional capture controls; local ownership is not runtime authorization.</summary>
public sealed class SecurityRegionalObservationCandidateTests
{
    private const int LargeBodyBudget = ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes;
    private static readonly TenantId Tenant = new(Id(1));
    private static readonly BranchRef RootBranch = new(Tenant, new(Id(2)), new(Id(3)));
    private static readonly BranchRef OtherBranch = new(Tenant, new(Id(4)), new(Id(5)));
    private static readonly DocumentId Document = new(Id(6));

    [Fact]
    public void ActualFalseRegionalCallsKeepOriginalCompilerTruthAndBindingIdentity()
    {
        var fixture = Create();
        var execution = Capture(fixture);
        AssertParity(fixture, execution);
        Assert.Equal(CompilerExecutionState.Completed, execution.Compilation.Result.Execution);
        Assert.Equal(CompilerVerdict.Fail, execution.Compilation.Result.Verdict);
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, execution.Observation.State);
        Assert.True(execution.IsFor(execution.Compilation));
        Assert.True(execution.Observation.IsOwnedBy(execution));
        Assert.Equal(2, execution.Observation.CompletedRegionCalls);
        Assert.Equal(2, execution.Observation.StartedEvaluations);
        Assert.Equal(2, execution.Observation.CompletedEvaluations);
        var regions = execution.Compilation.Dependencies!.Regions;
        Assert.Equal(2, regions.Count);
        var recorded = execution.Observation.Data.Value.GetProperty("regions").EnumerateArray().ToArray();
        Assert.Equal(regions.Count, recorded.Length);
        foreach (var region in regions)
        {
            var item = Assert.Single(recorded, value =>
                value.GetProperty("bindingDigest").GetRawText() == region.Binding.Digest.Data.CanonicalText);
            Assert.True(item.GetProperty("retainedRegion").GetBoolean());
            Assert.Equal(region.Binding.Source.Digest.Data.CanonicalText, item.GetProperty("sourceDigest").GetRawText());
            Assert.Equal(region.Digest.Data.CanonicalText, item.GetProperty("publishedRegionDigest").GetRawText());
            Assert.Equal(region.Binding.Source.Context.Data.CanonicalText, item.GetProperty("context").GetRawText());
            AssertAvailable(Read(execution, region, Assert.Single(region.Diagnostics)));
        }
    }

    [Fact]
    public void TrueRegionalConditionsKeepActualEvaluationsWithoutInventingDiagnostics()
    {
        var fixture = Create(truth: true);
        var execution = Capture(fixture);
        AssertParity(fixture, execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, execution.Observation.State);
        Assert.Equal(2, execution.Observation.CompletedEvaluations);
        Assert.Equal(2, execution.Observation.Data.Value.GetProperty("evaluations").GetArrayLength());
        Assert.All(execution.Compilation.Dependencies!.Regions, region => Assert.Empty(region.Diagnostics));
        Assert.DoesNotContain(execution.Compilation.Result.Diagnostics,
            diagnostic => diagnostic.Code == "coglatas.analysis.dependency-finding");
    }

    [Fact]
    public void UnknownRegionalConstraintsRetainTheirActualDiagnosticGraphsAndCompilerOutcome()
    {
        var fixture = Create(truth: null);
        var execution = Capture(fixture);
        AssertParity(fixture, execution);
        Assert.NotEqual(CompilerVerdict.Pass, execution.Compilation.Result.Verdict);
        Assert.Equal(2, execution.Observation.CompletedEvaluations);
        foreach (var region in execution.Compilation.Dependencies!.Regions)
        {
            var snapshot = Read(execution, region, Assert.Single(region.Diagnostics));
            AssertAvailable(snapshot);
            var frame = Assert.Single(snapshot.EvaluationFrames).Value;
            Assert.Equal("Unknown", frame.Value.GetProperty("truth").GetString());
            Assert.All(snapshot.Bodies.Values,
                value => Assert.Equal("Unknown", value.Value.GetProperty("truth").GetString()));
        }
    }

    [Fact]
    public void GenuineEqualOlderExecutionsRecordsAndCompilationsCannotOwnTheCurrentRead()
    {
        var fixture = Create();
        var current = Capture(fixture);
        var older = Capture(fixture);
        Assert.Equal(current.Observation.Data, older.Observation.Data);
        Assert.Equal(current.Compilation.Result.Data, older.Compilation.Result.Data);
        Assert.NotSame(current, older);
        var region = current.Compilation.Dependencies!.Regions[0];
        var diagnostic = Assert.Single(region.Diagnostics);
        AssertRefused(ProjectCompilerRegionalPredicateObservationSnapshot.Read(
            older, current.Compilation, current.Observation, region, diagnostic));
        AssertRefused(ProjectCompilerRegionalPredicateObservationSnapshot.Read(
            current, older.Compilation, current.Observation, region, diagnostic));
        AssertRefused(ProjectCompilerRegionalPredicateObservationSnapshot.Read(
            current, current.Compilation, older.Observation, region, diagnostic));
        AssertAvailable(Read(current, region, diagnostic));
    }

    [Fact]
    public void GenuineEqualOlderRegionsAndDiagnosticsCannotBorrowCurrentObjectOwnership()
    {
        var fixture = Create();
        var current = Capture(fixture);
        var older = Capture(fixture);
        var region = current.Compilation.Dependencies!.Regions.Single(value => value.Binding.IsRoot);
        var prior = older.Compilation.Dependencies!.Regions.Single(value => value.Binding.IsRoot);
        var diagnostic = Assert.Single(region.Diagnostics);
        var priorDiagnostic = Assert.Single(prior.Diagnostics);
        Assert.Equal(region.Digest, prior.Digest);
        Assert.Equal(diagnostic.Data, priorDiagnostic.Data);
        Assert.NotSame(region, prior);
        Assert.NotSame(diagnostic, priorDiagnostic);
        AssertRefused(Read(current, prior, priorDiagnostic));
        AssertRefused(Read(current, region, priorDiagnostic));
        var other = current.Compilation.Dependencies.Regions.Single(value => !value.Binding.IsRoot);
        AssertRefused(Read(current, other, diagnostic));
        AssertAvailable(Read(current, region, diagnostic));
    }

    [Fact]
    public void RootDependencySummariesCannotReplaceTheOriginalRegionalDiagnostic()
    {
        var execution = Capture(Create());
        var dependency = execution.Compilation.Dependencies!.Regions.Single(region => !region.Binding.IsRoot);
        var original = Assert.Single(dependency.Diagnostics);
        var summary = Assert.Single(execution.Compilation.Result.Diagnostics,
            diagnostic => diagnostic.Code == "coglatas.analysis.dependency-finding");
        Assert.NotSame(original, summary);
        AssertRefused(Read(execution, dependency, summary));
        AssertAvailable(Read(execution, dependency, original));
    }

    [Fact]
    public void DefaultCaptureAndDefaultSnapshotRemainExplicitlyBodyFree()
    {
        var fixture = Create();
        var manifestExecution = Capture(fixture, new());
        AssertParity(fixture, manifestExecution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.ManifestOnly, manifestExecution.Observation.State);
        Assert.Equal(0, manifestExecution.Observation.RetainedBodyBytes);
        var region = manifestExecution.Compilation.Dependencies!.Regions[0];
        var diagnostic = Assert.Single(region.Diagnostics);
        var read = ProjectCompilerRegionalPredicateObservationSnapshot.Read(manifestExecution,
            manifestExecution.Compilation, manifestExecution.Observation, region, diagnostic);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.ManifestOnly, read.State);
        Assert.Equal("NotRecorded", read.Data.Value.GetProperty("selection").GetString());
        Assert.Empty(read.Bodies);
        Assert.Empty(read.EvaluationFrames);
        Assert.Equal(CompilerRegionalPredicateSnapshotState.NoRecordedOperators,
            Read(manifestExecution, region, diagnostic).State);
    }

    [Fact]
    public void DefaultReadOfObservedExecutionKeepsOwnedMetadataWithoutSerializingTheGraph()
    {
        var execution = Capture(Create(rootPredicate: Compound()));
        var region = execution.Compilation.Dependencies!.Regions.Single(value => value.Binding.IsRoot);
        var read = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation,
            execution.Observation, region, Assert.Single(region.Diagnostics));
        Assert.Equal(CompilerRegionalPredicateSnapshotState.ManifestOnly, read.State);
        Assert.Same(execution.Observation.Data, read.OriginalRecordMetadata);
        Assert.Equal(execution.Observation.Digest, read.OriginalRecordDigest);
        Assert.Empty(read.Bodies);
        Assert.Empty(read.EvaluationFrames);
        var serializedExecution = JsonSerializer.SerializeToElement(execution);
        var serializedRead = JsonSerializer.SerializeToElement(read);
        Assert.False(serializedExecution.TryGetProperty("Compilation", out _));
        Assert.False(serializedRead.TryGetProperty("OriginalRecordMetadata", out _));
        foreach (var text in new[] { serializedExecution.GetRawText(), serializedRead.GetRawText() })
        {
            Assert.DoesNotContain("evaluatedChildren", text, StringComparison.Ordinal);
            Assert.DoesNotContain("coglatas.and", text, StringComparison.Ordinal);
            Assert.DoesNotContain("independent-regional-private-document", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OriginalOperatorAndEvaluationHashSubjectsKeepSeparateByteAccounting()
    {
        var execution = Capture(Create(rootPredicate: Compound()));
        var region = execution.Compilation.Dependencies!.Regions.Single(value => value.Binding.IsRoot);
        var view = Read(execution, region, Assert.Single(region.Diagnostics));
        AssertAvailable(view);
        Assert.Single(view.EvaluationFrames);
        Assert.True(view.RetainedEvaluationFrameBytes > 0);
        Assert.Equal(view.Bodies.Values.Sum(value => value.ToCanonicalBytes().Length), view.RetainedBodyBytes);
        Assert.Equal(view.EvaluationFrames.Values.Sum(value => value.ToCanonicalBytes().Length), view.RetainedEvaluationFrameBytes);
        Assert.Equal(view.Digest, ContentDigest.Compute(ProjectCompilerRegionalPredicateObservationSnapshot.Format, view.Data.ToCanonicalBytes()));
        Assert.Equal(execution.Observation.Digest, ContentDigest.Compute(
            ProjectCompilerRegionalPredicateObservation.Format, execution.Observation.Data.ToCanonicalBytes()));
        var root = Assert.Single(view.Bodies.Values, value => value.Value.GetProperty("ordinal").GetInt32() == 0).Value;
        Assert.Equal(1, root.GetProperty("skippedOperands").GetInt32());
        Assert.Equal(new[] { 1, 2 }, root.GetProperty("evaluatedChildren").EnumerateArray().Select(value => value.GetInt32()));
    }

    [Fact]
    public async Task ConcurrentReuseOfOnePolicyCannotShareCollectorOrRegionalOwnership()
    {
        var fixture = Create();
        var policy = new CompilerRegionalPredicateObservationPolicy(PredicateEvaluationCaptureMode.ObservedOperators);
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Capture(fixture, policy))));
        foreach (var execution in runs)
        {
            AssertParity(fixture, execution);
            Assert.Same(policy, execution.Observation.Policy);
            Assert.True(execution.Observation.IsOwnedBy(execution));
            Assert.Equal(2, execution.Observation.CompletedEvaluations);
            Assert.Equal(runs[0].Observation.Data, execution.Observation.Data);
        }
        var first = runs[0].Compilation.Dependencies!.Regions[0];
        AssertRefused(Read(runs[1], first, Assert.Single(first.Diagnostics)));
        AssertAvailable(Read(runs[0], first, Assert.Single(first.Diagnostics)));
    }

    [Fact]
    public void ExactEvaluationCapAndFirstOverflowSpanBothActualRegions()
    {
        foreach (var total in new[] { 128, 129 })
        {
            var fixture = Create(rootCount: 63, dependencyCount: total - 63, operations: 10_000_000);
            var execution = Capture(fixture, new(PredicateEvaluationCaptureMode.ObservedOperators,
                maximumBodyBytes: LargeBodyBudget));
            AssertParity(fixture, execution);
            Assert.Equal(CompilerExecutionState.Completed, execution.Compilation.Result.Execution);
            Assert.Equal(2, execution.Observation.CompletedRegionCalls);
            Assert.Equal(total, execution.Observation.StartedEvaluations);
            Assert.Equal(total, execution.Observation.CompletedEvaluations);
            Assert.Equal(total == 128 ? CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations
                : CompilerRegionalPredicateCaptureState.EvaluationLimitReached, execution.Observation.State);
            Assert.Equal(total == 128 ? 128 : 0,
                execution.Observation.Data.Value.GetProperty("evaluations").GetArrayLength());
            foreach (var region in execution.Compilation.Dependencies!.Regions)
            {
                var view = Read(execution, region, region.Diagnostics[0]);
                if (total == 128) AssertAvailable(view);
                else AssertNoGraph(view, CompilerRegionalPredicateSnapshotState.NoRecordedOperators);
            }
        }
    }

    [Fact]
    public void ExactNodeCapAndFirstOverflowWithdrawAllRegionsWithoutChangingTruth()
    {
        foreach (var overflow in new[] { false, true })
        {
            var root = Unary("coglatas.not", Balanced("coglatas.or", [.. Enumerable.Repeat(false, 255), true]));
            var external = overflow
                ? Unary("coglatas.not", Unary("coglatas.not",
                    Balanced("coglatas.and", [.. Enumerable.Repeat(true, 255), false])))
                : root;
            var fixture = Create(rootPredicate: root, dependencyPredicate: external, operations: 10_000_000);
            var execution = Capture(fixture, new(PredicateEvaluationCaptureMode.ObservedOperators,
                maximumBodyBytes: LargeBodyBudget));
            AssertParity(fixture, execution);
            Assert.Equal(CompilerExecutionState.Completed, execution.Compilation.Result.Execution);
            Assert.Equal(2, execution.Observation.CompletedEvaluations);
            Assert.Equal(overflow ? CompilerRegionalPredicateCaptureState.NodeLimitReached
                : CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, execution.Observation.State);
            foreach (var region in execution.Compilation.Dependencies!.Regions)
            {
                var view = Read(execution, region, Assert.Single(region.Diagnostics));
                if (overflow) AssertNoGraph(view, CompilerRegionalPredicateSnapshotState.NoRecordedOperators);
                else
                {
                    AssertAvailable(view);
                    Assert.Equal(512, view.Bodies.Count);
                    Assert.Equal(1024, execution.Observation.ObservedNodes);
                }
            }
            if (overflow) Assert.Equal(0, execution.Observation.RetainedBodyBytes);
        }
    }

    [Fact]
    public void PredeclaredExactBodyBudgetAndOneByteShortCaptureHaveDifferentSufficiency()
    {
        var fixture = Create(rootPredicate: Compound());
        var reference = Capture(fixture);
        var originalBytes = reference.Observation.RetainedBodyBytes;
        Assert.True(originalBytes > 1);
        foreach (var budget in new[] { originalBytes, originalBytes - 1 })
        {
            var execution = Capture(fixture, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: budget));
            AssertParity(fixture, execution);
            Assert.Equal(budget == originalBytes ? CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations
                : CompilerRegionalPredicateCaptureState.BodyBudgetExceeded, execution.Observation.State);
            Assert.Equal(budget == originalBytes ? originalBytes : 0, execution.Observation.RetainedBodyBytes);
            if (budget != originalBytes)
                Assert.All(execution.Compilation.Dependencies!.Regions, region =>
                    AssertNoGraph(Read(execution, region, region.Diagnostics[0]), CompilerRegionalPredicateSnapshotState.NoRecordedOperators));
        }
    }

    [Fact]
    public void TheFirstCaptureGapSurvivesLaterWholeCompilerResourceExhaustion()
    {
        var original = Create();
        var complete = PureCoreCompiler.Compile(original.Input, original.Input.Compiler, original.Snapshots);
        var fixture = Reprofile(original, complete.LogicalOperationsConsumed - 1);
        var execution = Capture(fixture, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumNodes: 1));
        AssertParity(fixture, execution);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, execution.Compilation.Result.Execution);
        Assert.Equal(2, execution.Observation.CompletedEvaluations);
        Assert.Equal(CompilerRegionalPredicateCaptureState.NodeLimitReached, execution.Observation.State);
        Assert.Equal(0, execution.Observation.RetainedBodyBytes);
        Assert.Empty(execution.Observation.Data.Value.GetProperty("evaluations").EnumerateArray());
    }

    [Fact]
    public void WholeCompilerExhaustionWithdrawsAnOtherwiseSufficientCapture()
    {
        var original = Create();
        var complete = PureCoreCompiler.Compile(original.Input, original.Input.Compiler, original.Snapshots);
        var fixture = Reprofile(original, complete.LogicalOperationsConsumed - 1);
        var execution = Capture(fixture);
        AssertParity(fixture, execution);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, execution.Compilation.Result.Execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, execution.Observation.State);
        Assert.Equal(0, execution.Observation.RetainedBodyBytes);
        Assert.Empty(execution.Observation.Data.Value.GetProperty("evaluations").EnumerateArray());
    }

    [Fact]
    public void MeasuredWorkSeparatesCompletedOneMillionProfileFromActualExhaustion()
    {
        var predicate = Unary("coglatas.not", Balanced("coglatas.or", [.. Enumerable.Repeat(false, 255), true]));
        var fixture = Create(rootPredicate: predicate, dependencyPredicate: predicate);
        var completed = Capture(fixture, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: LargeBodyBudget));
        AssertParity(fixture, completed);
        Assert.Equal(1_000_000, fixture.Input.Settings.Value.GetProperty("executionProfile").GetProperty("maximumOperations").GetInt32());
        Assert.Equal(CompilerExecutionState.Completed, completed.Compilation.Result.Execution);
        Assert.InRange(completed.Compilation.LogicalOperationsConsumed, 2L, 1_000_000L);
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, completed.Observation.State);
        Assert.Equal(2, completed.Observation.CompletedEvaluations);
        Assert.Equal(1024, completed.Observation.ObservedNodes);
        Assert.All(completed.Compilation.Dependencies!.Regions, region =>
            AssertAvailable(Read(completed, region, Assert.Single(region.Diagnostics))));
        // This independent fixture actually fits 1M; require a measured smaller profile to prove refusal.
        // Do not transfer another Product fixture's historical cost to a different Source workload.
        var limited = Reprofile(fixture, completed.Compilation.LogicalOperationsConsumed - 1);
        var execution = Capture(limited, new(PredicateEvaluationCaptureMode.ObservedOperators, maximumBodyBytes: LargeBodyBudget));
        AssertParity(limited, execution);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, execution.Compilation.Result.Execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, execution.Observation.State);
        Assert.Equal(0, execution.Observation.RetainedBodyBytes);
        Assert.Empty(execution.Observation.Data.Value.GetProperty("evaluations").EnumerateArray());
        Assert.Equal(1024, completed.Observation.ObservedNodes);
        Assert.Equal(CompilerRegionalPredicateCaptureState.RecordedRegionalEvaluations, completed.Observation.State);
    }

    [Fact]
    public void IndependentViewerBudgetNeverRewritesTheOriginalCapturedRecord()
    {
        var execution = Capture(Create(rootPredicate: Compound()));
        var region = execution.Compilation.Dependencies!.Regions.Single(value => value.Binding.IsRoot);
        var diagnostic = Assert.Single(region.Diagnostics);
        var original = execution.Observation.Data.ToCanonicalBytes();
        var digest = execution.Observation.Digest;
        var accepted = Read(execution, region, diagnostic);
        var exact = Read(execution, region, diagnostic, accepted.RetainedBodyBytes);
        AssertAvailable(exact);
        var refused = Read(execution, region, diagnostic, accepted.RetainedBodyBytes - 1);
        AssertNoGraph(refused, CompilerRegionalPredicateSnapshotState.BodyBudgetExceeded);
        Assert.Same(execution.Observation.Data, refused.OriginalRecordMetadata);
        Assert.Equal(original, execution.Observation.Data.ToCanonicalBytes());
        Assert.Equal(digest, execution.Observation.Digest);
        Assert.Equal(accepted.Data, Read(execution, region, diagnostic).Data);
    }

    [Fact]
    public void CancelledSnapshotReadCannotReturnPartialFramesOrDamageTheCompletedRecord()
    {
        var execution = Capture(Create(rootPredicate: Compound()));
        var region = execution.Compilation.Dependencies!.Regions.Single(value => value.Binding.IsRoot);
        var diagnostic = Assert.Single(region.Diagnostics);
        var original = execution.Observation.Data.ToCanonicalBytes();
        var accepted = Read(execution, region, diagnostic);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectCompilerRegionalPredicateObservationSnapshot.Read(
            execution, execution.Compilation, execution.Observation, region, diagnostic,
            CompilerRegionalPredicateSnapshotMode.AvailableOperators, LargeBodyBudget, cancellation.Token));
        Assert.Equal(original, execution.Observation.Data.ToCanonicalBytes());
        Assert.Equal(accepted.Data, Read(execution, region, diagnostic).Data);
        Assert.Equal(CompilerExecutionState.Completed, execution.Compilation.Result.Execution);
    }

    [Fact]
    public void CancellationInsideActualMaterializationEnumerationWithdrawsEveryRegionalGraph()
    {
        var fixture = Create();
        using var supplier = new CancellingSupply(fixture.Snapshots[0]);
        var execution = PureCoreCompiler.CompileWithRegionalPredicateObservation(fixture.Input, fixture.Input.Compiler,
            new(PredicateEvaluationCaptureMode.ObservedOperators), supplier, cancellationToken: supplier.Token);
        Assert.True(supplier.Entered);
        Assert.True(supplier.Finished);
        Assert.True(supplier.Token.IsCancellationRequested);
        Assert.Equal(CompilerExecutionState.Cancelled, execution.Compilation.Result.Execution);
        Assert.Equal(CompilerRegionalPredicateCaptureState.CaptureInterrupted, execution.Observation.State);
        Assert.Equal(0, execution.Observation.CompletedRegionCalls);
        Assert.Equal(0, execution.Observation.RetainedBodyBytes);
        Assert.Empty(execution.Observation.Data.Value.GetProperty("evaluations").EnumerateArray());
    }

    [Fact]
    public void MissingDeniedOrChangedSuppliesCannotBorrowPreviouslyObservedPrivateRegions()
    {
        var fixture = Create();
        var admitted = Capture(fixture);
        var originalRegion = admitted.Compilation.Dependencies!.Regions.Single(region => !region.Binding.IsRoot);
        var originalDiagnostic = Assert.Single(originalRegion.Diagnostics);
        AssertAvailable(Read(admitted, originalRegion, originalDiagnostic));
        var supply = fixture.Snapshots[0];
        var denied = new CompilerMaterializedSnapshot(supply.SnapshotId, supply.Version, supply.Snapshot with { IsAuthorized = false });
        foreach (var snapshots in new[]
        {
            Array.Empty<CompilerMaterializedSnapshot>(),
            new[] { denied },
            new[] { supply, denied },
            new[] { new CompilerMaterializedSnapshot(supply.SnapshotId, "changed-version", supply.Snapshot) }
        })
        {
            var rejected = Capture(fixture with { Snapshots = snapshots });
            AssertRefused(Read(rejected, originalRegion, originalDiagnostic));
            Assert.DoesNotContain(rejected.Observation.Data.Value.GetProperty("evaluations").EnumerateArray(),
                value => value.GetProperty("context").GetRawText() == originalRegion.Binding.Source.Context.Data.CanonicalText);
            Assert.Equal(admitted.Compilation.Result.Data, Capture(fixture).Compilation.Result.Data);
        }
    }

    [Fact]
    public void RequirementEvidenceAndUnknownConditionsCannotInventDiagnosticOperatorLinks()
    {
        foreach (bool? truth in new bool?[] { true, false, null })
        {
            var fixture = Create(truth: truth, includeDependency: false, requirement: true);
            var execution = Capture(fixture);
            AssertParity(fixture, execution);
            Assert.Equal(1, execution.Observation.CompletedEvaluations);
            Assert.False(execution.Compilation.Result.EvidenceComplete);
            var region = Assert.Single(execution.Compilation.Dependencies!.Regions);
            var unavailable = Assert.Single(region.Diagnostics,
                diagnostic => diagnostic.Code == "coglatas.analysis.requirement-evidence-unavailable");
            var unlinked = Read(execution, region, unavailable);
            AssertNoGraph(unlinked, CompilerRegionalPredicateSnapshotState.NoObservedRegionalDiagnostic);
            Assert.Equal(0, unlinked.SelectedEvaluations);
            if (truth == false)
            {
                var condition = Assert.Single(region.Diagnostics,
                    diagnostic => diagnostic.Code == "coglatas.analysis.requirement-condition-false");
                AssertAvailable(Read(execution, region, condition));
            }
            else
                Assert.DoesNotContain(region.Diagnostics,
                    diagnostic => diagnostic.Code.Contains("requirement-condition", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ImmutableMapsAndDetachedBodyCopiesCannotPoisonTheRetainedObservation()
    {
        var execution = Capture(Create(rootPredicate: Compound()));
        var region = execution.Compilation.Dependencies!.Regions.Single(value => value.Binding.IsRoot);
        var diagnostic = Assert.Single(region.Diagnostics);
        var view = Read(execution, region, diagnostic);
        var original = execution.Observation.Data.ToCanonicalBytes();
        var originalView = view.Data.ToCanonicalBytes();
        foreach (var map in new[] { view.Bodies, view.EvaluationFrames })
        {
            var entry = map.First();
            var originalBody = entry.Value.ToCanonicalBytes();
            var writable = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(map);
            Assert.Throws<NotSupportedException>(() => writable[entry.Key] = SourceJson.Parse("{}"));
            Assert.Throws<NotSupportedException>(() => writable.Add("foreign", SourceJson.Parse("{}")));
            Assert.Throws<NotSupportedException>(() => writable.Clear());
            Assert.False(map is ICollection);
            Assert.False(map.Keys is ICollection);
            Assert.False(map.Values is ICollection);
            var detachedBytes = entry.Value.ToCanonicalBytes();
            detachedBytes[0] = (byte)'!';
            var detachedJson = JsonNode.Parse(entry.Value.CanonicalText)!.AsObject();
            detachedJson["independentPoison"] = true;
            Assert.Equal(originalBody, entry.Value.ToCanonicalBytes());
            Assert.False(entry.Value.Value.TryGetProperty("independentPoison", out _));
        }
        Assert.Equal(original, execution.Observation.Data.ToCanonicalBytes());
        Assert.Equal(originalView, Read(execution, region, diagnostic).Data.ToCanonicalBytes());
    }

    [Fact]
    public void InvalidCaptureAndViewerPoliciesCannotReturnAnOwnedBodyView()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy((PredicateEvaluationCaptureMode)777));
        foreach (var limit in new[] { 0, 129 })
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy(maximumEvaluations: limit));
        foreach (var limit in new[] { 0, 1025 })
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy(maximumNodes: limit));
        foreach (var limit in new[] { 0, LargeBodyBudget + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerRegionalPredicateObservationPolicy(maximumBodyBytes: limit));
        var execution = Capture(Create());
        var region = execution.Compilation.Dependencies!.Regions[0];
        var diagnostic = Assert.Single(region.Diagnostics);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectCompilerRegionalPredicateObservationSnapshot.Read(
            execution, execution.Compilation, execution.Observation, region, diagnostic, (CompilerRegionalPredicateSnapshotMode)777));
        foreach (var budget in new[] { 0, LargeBodyBudget + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(execution, region, diagnostic, budget));
        AssertAvailable(Read(execution, region, diagnostic));
    }

    [Fact]
    public void InitialRootOnlyFailureIsNeverRelabeledAsARegionalEvaluation()
    {
        var fixture = Create(includeDependency: false, semanticsVersion: "1");
        var ordinary = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler);
        var initial = PureCoreCompiler.CompileWithPredicateObservation(fixture.Input, fixture.Input.Compiler,
            new(PredicateEvaluationCaptureMode.ObservedOperators));
        var regional = Capture(fixture);
        Assert.Equal(CompilerVerdict.Fail, ordinary.Result.Verdict);
        Assert.Equal(ordinary.Result.Data, initial.Compilation.Result.Data);
        Assert.Equal(ordinary.Result.Data, regional.Compilation.Result.Data);
        Assert.Equal(1, initial.Observation.CompletedEvaluations);
        Assert.Equal(0, regional.Observation.CompletedRegionCalls);
        Assert.Equal(0, regional.Observation.CompletedEvaluations);
        Assert.Equal(CompilerRegionalPredicateCaptureState.NoActualRegionalEvaluations, regional.Observation.State);
        Assert.Equal("NotRecorded", regional.Observation.Data.Value.GetProperty("initialRootEvaluation").GetString());
        Assert.Equal("NotRecorded", initial.Observation.Data.Value.GetProperty("regionalEvaluation").GetString());
        Assert.Equal(0, regional.Observation.RetainedBodyBytes);
    }

    private static Guid Id(int value) => Guid.Parse("f1966b71-0000-4000-8000-" + value.ToString("x12", CultureInfo.InvariantCulture));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static object Ref(SourceRevisionContext context, int entity) => new
    {
        tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(),
        entityId = new EntityId(Id(entity)).ToString(), context = context.Data.Value
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
    private static object Unary(string kind, object operand) => new { kind, operands = new[] { operand } };
    private static object Compound() => new { kind = "coglatas.and", operands = new[] { Literal(true), Literal(false), Literal(null) } };
    private static object Balanced(string kind, bool[] leaves)
    {
        if (leaves.Length == 1) return Literal(leaves[0]);
        var middle = leaves.Length / 2;
        return new { kind, operands = new[] { Balanced(kind, leaves[..middle]), Balanced(kind, leaves[middle..]) } };
    }
    private static object Duration() => new
    {
        type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
        state = "coglatas.known", value = "3"
    };
    private static ProjectSource Source(SourceRevisionContext context, object predicate, int count, bool requirement,
        SourceRevisionContext? dependencyContext = null)
    {
        var declarations = new List<object>
        {
            new { entityId = Id(10).ToString(), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1,
                payload = new { duration = Duration(), ports = Array.Empty<object>() } }
        };
        var relations = new List<object>();
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var identity = 20 + ordinal;
            if (requirement)
                declarations.Add(new { entityId = Id(identity).ToString(), documentId = Document.ToString(),
                    kind = "coglatas.requirement", schemaVersion = 1,
                    payload = new { target = Ref(context, 7), condition = predicate, evidenceKind = "coglatas.model-proof" } });
            else
            {
                declarations.Add(new { entityId = Id(identity).ToString(), documentId = Document.ToString(),
                    kind = "coglatas.constraint", schemaVersion = 1,
                    payload = new { predicate, classification = "coglatas.required" } });
                relations.Add(new { relationId = Id(1000 + ordinal).ToString(), documentId = Document.ToString(),
                    kind = RelationKinds.Constrains, schemaVersion = 1,
                    descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
                    source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = Ref(context, identity) },
                    target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = Ref(context, 7) },
                    payload = new { predicateReference = Ref(context, identity),
                        version = context.CommittedRevision!.RevisionId.ToString(),
                        predicateDigest = ContentDigest.Compute("coglatas.predicate/1", PurePredicate.Parse(Json(predicate)).Data.ToCanonicalBytes()).Data.Value } });
            }
        }
        if (dependencyContext is not null)
            relations.Add(new { relationId = Id(9000).ToString(), documentId = Document.ToString(),
                kind = RelationKinds.DependsOn, schemaVersion = 1,
                descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 },
                source = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = Ref(context, 10) },
                target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = Ref(dependencyContext, 10) },
                payload = new { mode = "coglatas.finish-to-start", lag = Duration() } });
        return ProjectSource.Create(context,
            [SourceDocument.Create(Document, "independent-regional-private-document", Json(new
                { entityId = Id(7).ToString(), kind = "coglatas.project", schemaVersion = 1, payload = new { } }))],
            Json(declarations), Json(relations));
    }
    private static Fixture Create(bool? truth = false, int rootCount = 1, int dependencyCount = 1,
        int operations = 1_000_000, object? rootPredicate = null, object? dependencyPredicate = null,
        bool includeDependency = true, bool requirement = false, string semanticsVersion = "2")
    {
        var rootContext = SourceRevisionContext.Committed(new(RootBranch, new(Id(100))));
        var dependencyContext = SourceRevisionContext.Committed(new(OtherBranch, new(Id(200))));
        var dependency = Source(dependencyContext, dependencyPredicate ?? Literal(truth), dependencyCount, requirement);
        var root = Source(rootContext, rootPredicate ?? Literal(truth), rootCount, requirement,
            includeDependency ? dependencyContext : null);
        const string version = "independent-regional-1";
        object Artifact(string name, string revision = "1") => new { artifactId = name, version = revision,
            digest = ContentDigest.Compute("coglatas.security-regional-fixture/1", Encoding.UTF8.GetBytes(name + "/" + revision)).Data.Value };
        object None() => new { kind = "coglatas.none" };
        var settings = Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.security-regional.compiler"),
            ruleset = Artifact("coglatas.security-regional.ruleset"), semantics = Artifact("coglatas.semantic-model", semanticsVersion),
            ir = Artifact("coglatas.project-ir", semanticsVersion), evaluator = Artifact("coglatas.security-regional.evaluator"),
            scheduler = None(), solver = None(), stochastic = None(), importer = None(), scenario = None(),
            authorizedExternalSnapshots = includeDependency ? new object[] { new { snapshotId = Id(300).ToString(), version,
                digest = dependency.Digest.Data.Value, context = dependency.Context.Data.Value,
                coverage = "coglatas.complete", asOf = (string?)null } } : [],
            resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(), observations = Array.Empty<object>(),
            evidence = Array.Empty<object>(), units = Array.Empty<object>(), calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            descriptors = RelationKindDescriptor.BuiltIns.Select(value => new
                { artifactId = value.DescriptorId, version = "1", digest = value.Digest.Data.Value }).ToArray(),
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations,
                CoreCompilerRules.Constraints, CoreCompilerRules.Requirements, CoreCompilerRules.Resources }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = RelationKindDescriptor.BuiltIns.SelectMany(value => value.RequiredCapabilities)
                .Prepend("coglatas.core.structural").Distinct(StringComparer.Ordinal).ToArray(),
            targets = new[] { "coglatas.semantic-model" }, analysisScope = new[] { "coglatas.source-structure" },
            sourceCoverage = "coglatas.complete", clock = new { kind = "coglatas.not-applicable" },
            executionProfile = new { purpose = "coglatas.normal", maximumOperations = operations,
                maximumDeclarations = 10_000, maximumRelations = 20_000, maximumDigits = 256, maximumScale = 256,
                rounding = "coglatas.ties-to-even" }
        });
        return new(new(root, settings), includeDependency
            ? [new(new(Id(300)), version, new(dependency, true, true, true))] : []);
    }
    private sealed record Fixture(CompilerInputManifest Input, CompilerMaterializedSnapshot[] Snapshots);
    private static Fixture Reprofile(Fixture fixture, long operations)
    {
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = operations;
        return fixture with { Input = new(fixture.Input.Source, Json(settings)) };
    }
    private static RecordedRegionalPredicateCompilation Capture(Fixture fixture, CompilerRegionalPredicateObservationPolicy? policy = null)
        => PureCoreCompiler.CompileWithRegionalPredicateObservation(fixture.Input, fixture.Input.Compiler,
            policy ?? new(PredicateEvaluationCaptureMode.ObservedOperators), fixture.Snapshots);
    private static void AssertParity(Fixture fixture, RecordedRegionalPredicateCompilation execution)
    {
        var ordinary = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        Assert.Equal(ordinary.Result.Data, execution.Compilation.Result.Data);
        Assert.Equal(ordinary.LogicalOperationsConsumed, execution.Compilation.LogicalOperationsConsumed);
        Assert.Same(fixture.Input, execution.Compilation.Input);
    }
    private static ProjectCompilerRegionalPredicateObservationSnapshot Read(RecordedRegionalPredicateCompilation execution,
        CompilerDependencyRegion region, CompilerDiagnostic diagnostic, int budget = LargeBodyBudget)
        => ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, execution.Compilation, execution.Observation,
            region, diagnostic, CompilerRegionalPredicateSnapshotMode.AvailableOperators, budget);
    private static void AssertAvailable(ProjectCompilerRegionalPredicateObservationSnapshot snapshot)
    {
        Assert.Equal(CompilerRegionalPredicateSnapshotState.AvailableOperators, snapshot.State);
        Assert.True(snapshot.SelectedEvaluations > 0);
        Assert.NotEmpty(snapshot.Bodies);
        Assert.NotEmpty(snapshot.EvaluationFrames);
        foreach (var pair in snapshot.Bodies)
            Assert.Equal(pair.Key, ContentDigest.Compute(PredicateEvaluationObservation.NodeDomain, pair.Value.ToCanonicalBytes()).Value);
        foreach (var pair in snapshot.EvaluationFrames)
            Assert.Equal(pair.Key, ContentDigest.Compute(PredicateEvaluationObservation.EvaluationDomain, pair.Value.ToCanonicalBytes()).Value);
        Assert.Equal("NotEvaluated", snapshot.Data.Value.GetProperty("authority").GetString());
        Assert.Equal("NotEvaluated", snapshot.Data.Value.GetProperty("producerTrust").GetString());
        Assert.Equal("NotAllocated", snapshot.Data.Value.GetProperty("compilerRunIdentity").GetString());
    }
    private static void AssertNoGraph(ProjectCompilerRegionalPredicateObservationSnapshot snapshot, CompilerRegionalPredicateSnapshotState state)
    {
        Assert.Equal(state, snapshot.State);
        Assert.Empty(snapshot.Bodies);
        Assert.Empty(snapshot.EvaluationFrames);
        Assert.Equal(0, snapshot.RetainedBodyBytes);
        Assert.Equal(0, snapshot.RetainedEvaluationFrameBytes);
    }
    private static void AssertRefused(ProjectCompilerRegionalPredicateObservationSnapshot snapshot)
    {
        AssertNoGraph(snapshot, CompilerRegionalPredicateSnapshotState.RefusedInvocation);
        Assert.Null(snapshot.OriginalRecordMetadata);
        Assert.Null(snapshot.OriginalRecordDigest);
        Assert.Null(snapshot.OriginalCaptureState);
        Assert.Equal(0, snapshot.SelectedEvaluations);
        Assert.Equal("NotEvaluated", snapshot.Data.Value.GetProperty("selection").GetString());
        foreach (var field in new[] { "originalRecord", "region", "diagnostic", "evaluations" })
            Assert.Equal(JsonValueKind.Null, snapshot.Data.Value.GetProperty(field).ValueKind);
    }
    private sealed class CancellingSupply(CompilerMaterializedSnapshot snapshot) : IEnumerable<CompilerMaterializedSnapshot?>, IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        public bool Entered { get; private set; }
        public bool Finished { get; private set; }
        public CancellationToken Token => _source.Token;
        public IEnumerator<CompilerMaterializedSnapshot?> GetEnumerator()
        {
            try
            {
                Entered = true;
                _source.Cancel();
                yield return snapshot;
            }
            finally { Finished = true; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => _source.Dispose();
    }
}
