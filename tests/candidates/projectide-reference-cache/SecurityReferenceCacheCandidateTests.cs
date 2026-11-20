using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Xunit;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent fixed-input reference-cache controls; no runtime authority or UI acceptance.</summary>
public sealed class SecurityReferenceCacheCandidateTests
{
    private static readonly DocumentId Document = new(Id(101));
    private static readonly EntityId Root = new(Id(102));
    private static readonly EntityId TaskEntity = new(Id(103));
    private static readonly PortId FirstPort = new(Id(104));
    private const string SnapshotVersion = "independent-reference-snapshot-v1";
    private const string LifetimeInvariant = "Invalidated reference binding must preserve the current session retention.";

    [Fact]
    public void ColdWarmAndUncachedResolutionPublishEquivalentCurrentModels()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var uncached = Bind(fixture);
        var cold = Bind(fixture, cache);
        var warm = Bind(fixture, cache);
        Assert.True(cold.BindingComplete);
        Assert.NotEmpty(cold.References);
        Assert.All(cold.References, row => Assert.Equal(ReferenceResolutionState.Resolved, row.State));
        Assert.Equal(ModelBytes(uncached), ModelBytes(cold));
        Assert.Equal(ModelBytes(uncached), ModelBytes(warm));
        Assert.NotSame(cold, warm);
        Assert.Same(fixture.Input, warm.Input);
        Assert.True(cache.RetainedCount > 0);
        Assert.True(cache.RetainedCanonicalBytes > 0);
        foreach (var row in cold.References) Assert.Same(row, At(warm, row));
    }

    [Fact]
    public void PublicReferenceCollectionsAndDetachedCopiesCannotPoisonWarmResolution()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var model = Bind(fixture, cache);
        var original = ModelBytes(model);
        Assert.False(model.References is IList);
        Assert.False(model.References is ICollection);
        Assert.False(model.References is CompilerBoundReference[]);
        var writable = Assert.IsAssignableFrom<IList<CompilerBoundReference>>(model.References);
        Assert.True(writable.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => writable.Clear());
        Assert.Throws<NotSupportedException>(() => writable[0] = model.References[0] with { SymbolKey = "substituted" });
        var copied = model.References.ToArray();
        copied[0] = copied[0] with { State = ReferenceResolutionState.OutOfScope, SymbolKey = null };
        var locationBytes = model.References[0].Origin.ToCanonicalBytes();
        Array.Clear(locationBytes);
        var contextBytes = model.References[0].Origin.Context.Data.ToCanonicalBytes();
        Array.Clear(contextBytes);
        Assert.Equal(original, ModelBytes(model));
        Assert.Equal(original, ModelBytes(Bind(fixture, cache)));
    }

    [Fact]
    public void MissingAddedAndRemovedLocalTargetsRetainTheirDistinctOutcomes()
    {
        var cache = new ProjectReferenceBindingCache();
        var missing = Fixture(Source(Context(), includeTask: false));
        var present = Fixture(Source(Context()));
        var absentRow = Assert.Single(Bind(missing, cache).References);
        Assert.Equal(ReferenceResolutionState.MissingWithinCompleteScope, absentRow.State);
        Assert.Null(absentRow.SymbolKey);
        var presentRow = Assert.Single(Bind(present, cache).References);
        Assert.Equal(ReferenceResolutionState.Resolved, presentRow.State);
        Assert.NotNull(presentRow.SymbolKey);
        Assert.NotSame(absentRow, presentRow);
        Assert.Same(absentRow, Assert.Single(Bind(missing, cache).References));
        Assert.Same(presentRow, Assert.Single(Bind(present, cache).References));
    }

    [Fact]
    public void AnUnrelatedDeclarationEditReusesOnlyUnchangedLookupDependencies()
    {
        var cache = new ProjectReferenceBindingCache();
        var before = Fixture(Source(Context()));
        var after = Fixture(Source(Context(), unrelatedName: "independent-new-declaration"));
        var first = Bind(before, cache);
        var edited = Bind(after, cache);
        Assert.NotEqual(before.Input.InputDigest, after.Input.InputDigest);
        Assert.NotEqual(first.Symbols.Count, edited.Symbols.Count);
        Assert.Same(Assert.Single(first.References), Assert.Single(edited.References));
        Assert.Equal(ModelBytes(Bind(after)), ModelBytes(edited));
        Assert.Equal(ModelBytes(Bind(before)), ModelBytes(first));
    }

    [Fact]
    public void ChangedTargetKindsCannotReuseResolvedReferenceState()
    {
        var cache = new ProjectReferenceBindingCache();
        var valid = Fixture(Source(Context()));
        var unsupported = Fixture(Source(Context(), taskKind: "independent.future-task"));
        var first = Assert.Single(Bind(valid, cache).References);
        var rejected = Bind(unsupported, cache);
        var current = Assert.Single(rejected.References);
        Assert.NotSame(first, current);
        Assert.Equal(ReferenceResolutionState.Unsupported, current.State);
        Assert.False(rejected.BindingComplete);
        Assert.Equal(ModelBytes(Bind(unsupported)), ModelBytes(rejected));
        Assert.Same(first, Assert.Single(Bind(valid, cache).References));
    }

    [Fact]
    public void WarmCacheCannotSupplyMissingDeniedOrConflictingOriginalSnapshots()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var first = Bind(fixture, cache);
        var typed = TypedReference(first);
        var middle = fixture.Snapshots[0];
        var denied = new CompilerMaterializedSnapshot(middle.SnapshotId, middle.Version, middle.Snapshot with { IsAuthorized = false });
        var variants = new[]
        {
            new[] { fixture.Snapshots[1] },
            new[] { denied, fixture.Snapshots[1] },
            new[] { middle, denied, fixture.Snapshots[1] }
        };
        foreach (var supplies in variants)
        {
            var current = Bind(fixture with { Snapshots = supplies }, cache);
            Assert.False(current.BindingComplete);
            Assert.NotEqual(ReferenceResolutionState.Resolved, TypedReference(current).State);
            Assert.NotSame(typed, TypedReference(current));
            Assert.Equal(ModelBytes(Bind(fixture with { Snapshots = supplies })), ModelBytes(current));
        }
        Assert.Same(typed, TypedReference(Bind(fixture, cache)));
        Assert.Equal(ReferenceResolutionState.Resolved, typed.State);
    }

    [Fact]
    public void ChangedSnapshotVersionOrSourceBytesCannotBorrowOriginalResolution()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var first = TypedReference(Bind(fixture, cache));
        var original = fixture.Snapshots[0];
        var changed = Source(original.Snapshot.Source.Context, unrelatedName: "different-supplied-body");
        foreach (var replacement in new[]
        {
            new CompilerMaterializedSnapshot(original.SnapshotId, "wrong-version", original.Snapshot),
            new CompilerMaterializedSnapshot(original.SnapshotId, original.Version, new RelationScopeSnapshot(changed, true, true, true))
        })
        {
            var candidate = fixture with { Snapshots = [replacement, fixture.Snapshots[1]] };
            var result = Bind(candidate, cache);
            Assert.False(result.BindingComplete);
            Assert.NotEqual(ReferenceResolutionState.Resolved, TypedReference(result).State);
            Assert.NotSame(first, TypedReference(result));
            Assert.Equal(ModelBytes(Bind(candidate)), ModelBytes(result));
        }
        Assert.Same(first, TypedReference(Bind(fixture, cache)));
    }

    [Fact]
    public void PartialCoverageAndExhaustedLogicalBudgetsRemainIndependentOfHits()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        Bind(fixture, cache);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["sourceCoverage"] = "coglatas.partial";
        var partial = fixture with { Input = new CompilerInputManifest(fixture.Input.Source, Json(settings)) };
        var partialModel = Bind(partial, cache);
        Assert.False(partialModel.BindingComplete);
        Assert.Equal(ModelBytes(Bind(partial)), ModelBytes(partialModel));
        settings["sourceCoverage"] = "coglatas.complete";
        settings["executionProfile"]!["maximumOperations"] = 1;
        var limited = fixture with { Input = new CompilerInputManifest(fixture.Input.Source, Json(settings)) };
        var model = Bind(limited, cache);
        Assert.False(model.BindingComplete);
        Assert.Empty(model.References);
        Assert.Contains(model.Findings, item => item.Diagnostic.Code == "coglatas.binding.budget-exhausted");
        Assert.Equal(ModelBytes(Bind(limited)), ModelBytes(model));
        Assert.True(Bind(fixture, cache).BindingComplete);
    }

    [Fact]
    public void ClearingDuringActualSnapshotEnumerationPreventsLateRetention()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var entered = 0;
        IEnumerable<CompilerMaterializedSnapshot> Supply()
        {
            entered++;
            cache.Clear();
            foreach (var snapshot in fixture.Snapshots) yield return snapshot;
        }
        var result = ProjectSemanticBinder.Bind(fixture.Input, Supply(), referenceBindingCache: cache);
        Assert.Equal(1, entered);
        AssertRetention(cache, 0, 0);
        Assert.True(result.BindingComplete);
        Assert.Equal(ModelBytes(Bind(fixture)), ModelBytes(result));
        Assert.True(Bind(fixture, cache).BindingComplete);
        Assert.True(cache.RetainedCount > 0);
    }

    [Fact]
    public void NewOwnerDuringActualSnapshotEnumerationKeepsItsExactRetainedObjects()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var foreign = Fixture(Source(Context(owner: 41)));
        PartialSemanticModel? foreignModel = null;
        var bytes = 0;
        IEnumerable<CompilerMaterializedSnapshot> Supply()
        {
            foreignModel = Bind(foreign, cache);
            Assert.Single(foreignModel.References);
            bytes = cache.RetainedCanonicalBytes;
            foreach (var snapshot in fixture.Snapshots) yield return snapshot;
        }
        var result = ProjectSemanticBinder.Bind(fixture.Input, Supply(), referenceBindingCache: cache);
        AssertRetention(cache, 1, bytes);
        Assert.NotNull(foreignModel);
        Assert.Same(Assert.Single(foreignModel.References), Assert.Single(Bind(foreign, cache).References));
        Assert.Equal(ModelBytes(Bind(fixture)), ModelBytes(result));
    }

    [Fact]
    public void ClearingBeforeRegionalContinuationPreventsOldRootRepopulation()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var root = Bind(fixture, cache);
        Assert.True(cache.RetainedCount > 0);
        cache.Clear();
        var closure = CompilerBindingClosure.Build(root, fixture.Input.InputDigest, referenceBindingCache: cache);
        AssertRetention(cache, 0, 0);
        Assert.Equal(CompilerBindingClosureState.Complete, closure.State);
        Assert.Equal(3, closure.Regions.Count);
        Assert.Equal(CompilerBindingClosure.Build(root, fixture.Input.InputDigest).Data, closure.Data);
        var fresh = Bind(fixture, cache);
        Assert.Equal(closure.Data, CompilerBindingClosure.Build(fresh, fixture.Input.InputDigest, referenceBindingCache: cache).Data);
        Assert.True(cache.RetainedCount > 0);
    }

    [Fact]
    public void NewOwnerBeforeRegionalContinuationCannotBeReplacedByAnOldRoot()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var root = Bind(fixture, cache);
        var foreign = Fixture(Source(Context(owner: 42)));
        var current = Bind(foreign, cache);
        var bytes = cache.RetainedCanonicalBytes;
        var closure = CompilerBindingClosure.Build(root, fixture.Input.InputDigest, referenceBindingCache: cache);
        AssertRetention(cache, 1, bytes);
        Assert.Same(Assert.Single(current.References), Assert.Single(Bind(foreign, cache).References));
        Assert.Equal(CompilerBindingClosure.Build(root, fixture.Input.InputDigest).Data, closure.Data);
    }

    [Fact]
    public void ClearingAfterRootBindingWithinFullCompilePreventsRegionalRetention()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var entered = 0;
        IEnumerable<UnitDefinition> Units()
        {
            entered++;
            Assert.True(cache.RetainedCount > 0);
            cache.Clear();
            yield break;
        }
        var result = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, Units(), referenceBindingCache: cache);
        Assert.Equal(1, entered);
        AssertRetention(cache, 0, 0);
        AssertCompilationEquivalent(Compile(fixture), result);
    }

    [Fact]
    public void NewOwnerAfterRootBindingWithinFullCompileSurvivesOlderContinuation()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var foreign = Fixture(Source(Context(owner: 43)));
        PartialSemanticModel? current = null;
        var bytes = 0;
        var entered = 0;
        IEnumerable<UnitDefinition> Units()
        {
            entered++;
            Assert.True(cache.RetainedCount > 0);
            current = Bind(foreign, cache);
            bytes = cache.RetainedCanonicalBytes;
            yield break;
        }
        var result = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, Units(), referenceBindingCache: cache);
        AssertRetention(cache, 1, bytes);
        Assert.Equal(1, entered);
        Assert.NotNull(current);
        Assert.Same(Assert.Single(current.References), Assert.Single(Bind(foreign, cache).References));
        AssertCompilationEquivalent(Compile(fixture), result);
    }

    [Fact]
    public void ASeparateSessionMayRetainFreshRegionsFromTheEarlierImmutableRoot()
    {
        var fixture = Graph();
        var earlier = new ProjectReferenceBindingCache();
        var root = Bind(fixture, earlier);
        earlier.Clear();
        var independent = new ProjectReferenceBindingCache();
        var closure = CompilerBindingClosure.Build(root, fixture.Input.InputDigest, referenceBindingCache: independent);
        Assert.Equal(0, earlier.RetainedCount);
        Assert.True(independent.RetainedCount > 0);
        Assert.Equal(3, closure.Regions.Count);
        Assert.Equal(CompilerBindingClosureState.Complete, closure.State);
        Assert.Equal(CompilerBindingClosure.Build(root, fixture.Input.InputDigest).Data, closure.Data);
    }

    [Fact]
    public void EntryEvictionAndOversizedKeysPreservePublishedAndUncachedResults()
    {
        var fixture = Graph();
        var small = new ProjectReferenceBindingCache(maximumEntries: 1);
        var first = Bind(fixture, small);
        var original = ModelBytes(first);
        var warm = Bind(fixture, small);
        Assert.Equal(1, small.RetainedCount);
        Assert.Equal(original, ModelBytes(warm));
        var tiny = new ProjectReferenceBindingCache(maximumEntries: 1, maximumCanonicalBytes: 1);
        Assert.Equal(original, ModelBytes(Bind(fixture, tiny)));
        Assert.Equal(0, tiny.RetainedCount);
        Assert.Equal(0, tiny.RetainedCanonicalBytes);
        small.Clear();
        Assert.Equal(0, small.RetainedCount);
        Assert.Equal(0, small.RetainedCanonicalBytes);
        Assert.Equal(original, ModelBytes(first));
        Assert.Equal(original, ModelBytes(Bind(fixture, small)));
    }

    [Fact]
    public void CallerByteArraysAndSnapshotArrayCopiesCannotRewriteRetainedReferences()
    {
        var fixture = Graph();
        var bytes = fixture.Input.Source.Data.ToCanonicalBytes();
        var decoded = ProjectSourceCodec.Decode(bytes).RequireSource();
        var independent = fixture with { Input = new CompilerInputManifest(decoded, fixture.Input.Settings) };
        var supplies = independent.Snapshots.ToArray();
        var cache = new ProjectReferenceBindingCache();
        var first = ProjectSemanticBinder.Bind(independent.Input, supplies, referenceBindingCache: cache);
        var original = ModelBytes(first);
        Array.Clear(bytes);
        supplies[0] = new CompilerMaterializedSnapshot(supplies[0].SnapshotId, supplies[0].Version,
            supplies[0].Snapshot with { IsAuthorized = false });
        var sourceBytes = first.Input.Source.Data.ToCanonicalBytes();
        Array.Clear(sourceBytes);
        Assert.Equal(original, ModelBytes(first));
        Assert.Equal(original, ModelBytes(Bind(independent, cache)));
        Assert.All(first.BoundSnapshots, item => Assert.True(item.IsAuthorized));
    }

    [Fact]
    public void AReferenceFreeTenantProjectOrBranchSwitchStillFlushesRetention()
    {
        var fixture = Fixture(Source(Context()));
        foreach (var context in new[] { Context(owner: 2), Context(tenant: 2), Context(branch: 2) })
        {
            var cache = new ProjectReferenceBindingCache();
            var first = Assert.Single(Bind(fixture, cache).References);
            var empty = Fixture(Source(context, roots: false, includeTask: false));
            var result = Bind(empty, cache);
            Assert.True(result.BindingComplete);
            Assert.Empty(result.References);
            Assert.Equal(0, cache.RetainedCount);
            Assert.Equal(0, cache.RetainedCanonicalBytes);
            var restored = Assert.Single(Bind(fixture, cache).References);
            Assert.NotSame(first, restored);
            Assert.Equal(first, restored);
        }
    }

    [Fact]
    public void PreCancelledBindingCannotSwitchTheCurrentOwnerOrRetainedObjects()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var first = Bind(fixture, cache);
        var count = cache.RetainedCount;
        var bytes = cache.RetainedCanonicalBytes;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(
            Fixture(Source(Context(owner: 77))).Input, cancellationToken: cancelled.Token, referenceBindingCache: cache));
        Assert.Equal(count, cache.RetainedCount);
        Assert.Equal(bytes, cache.RetainedCanonicalBytes);
        foreach (var row in first.References) Assert.Same(row, At(Bind(fixture, cache), row));
    }

    [Fact]
    public void CancellationInsideActualSupplyCannotPublishReferenceRetention()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var entered = false;
        using var cancelled = new CancellationTokenSource();
        IEnumerable<CompilerMaterializedSnapshot> Supply()
        {
            entered = true;
            cancelled.Cancel();
            foreach (var snapshot in fixture.Snapshots) yield return snapshot;
        }
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(
            fixture.Input, Supply(), cancelled.Token, referenceBindingCache: cache));
        Assert.True(entered);
        Assert.Equal(0, cache.RetainedCount);
        Assert.Equal(0, cache.RetainedCanonicalBytes);
        Assert.True(Bind(fixture, cache).BindingComplete);
    }

    [Fact]
    public async Task ConcurrentActualOwnersAndClearsPreserveEachOriginalModel()
    {
        var cache = new ProjectReferenceBindingCache(maximumEntries: 8);
        var work = Enumerable.Range(51, 4).Select(owner => System.Threading.Tasks.Task.Run(() =>
        {
            var fixture = Fixture(Source(Context(owner: owner)));
            var expected = ModelBytes(Bind(fixture));
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (attempt % 3 == 0) cache.Clear();
                var result = Bind(fixture, cache);
                Assert.Equal(expected, ModelBytes(result));
                Assert.All(result.References, item => Assert.Equal(fixture.Input.Source.Context, item.Origin.Context));
            }
        })).ToArray();
        await System.Threading.Tasks.Task.WhenAll(work);
        Assert.InRange(cache.RetainedCount, 0, 8);
        cache.Clear();
        Assert.Equal(0, cache.RetainedCanonicalBytes);
    }

    [Fact]
    public void FullCompilationAndFreshIndexesKeepCanonicalBytesChargesAndCursorOwnership()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var expected = Compile(fixture);
        var first = Compile(fixture, cache);
        var second = Compile(fixture, cache);
        AssertCompilationEquivalent(expected, first);
        AssertCompilationEquivalent(expected, second);
        var reachableOnly = CompilerBindingClosure.Build(first.Model!, fixture.Input.InputDigest);
        Assert.False(reachableOnly.IncludesDeclaredSnapshots);
        var partialIndex = CompilerSemanticIndex.Build(reachableOnly, fixture.Input.InputDigest);
        Assert.Equal(CompilerSemanticIndexState.Incomplete, partialIndex.State);
        Assert.False(partialIndex.IndexCoverageComplete);
        var firstClosure = first.Dependencies!.Binding;
        var secondClosure = second.Dependencies!.Binding;
        Assert.True(firstClosure.IncludesDeclaredSnapshots);
        Assert.True(secondClosure.IncludesDeclaredSnapshots);
        var oldIndex = CompilerSemanticIndex.Build(firstClosure, fixture.Input.InputDigest);
        var index = CompilerSemanticIndex.Build(secondClosure, fixture.Input.InputDigest);
        Assert.Equal(CompilerSemanticIndexState.Complete, index.State);
        Assert.Equal(oldIndex.Data, index.Data);
        Assert.NotSame(oldIndex, index);
        var origin = Assert.Single(first.Model!.Symbols, row => row.PortId == FirstPort && row.Context.Equals(fixture.Input.Source.Context)).Location;
        var page = oldIndex.CompleteEntityReference(origin, fixture.Input.InputDigest, 1);
        Assert.NotNull(page.NextCursor);
        var refused = index.CompleteEntityReference(origin, fixture.Input.InputDigest, 1, page.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, refused.State);
        Assert.Empty(refused.Candidates);
        Assert.Null(refused.NextCursor);
        Assert.Null(refused.Origin);
        Assert.Null(refused.InputDigest);
        var local = Fixture(Source(Context(owner: 91)));
        var localResult = Compile(local, cache);
        Assert.Equal(CompilerExecutionState.Completed, localResult.Result.Execution);
        Assert.Equal(CompilerVerdict.Pass, localResult.Result.Verdict);
        Assert.NotNull(localResult.Validation?.Ir);
    }

    [Fact]
    public void AReusedDocumentReferenceRepublishesTheExactTargetAfterAnotherOriginDisappears()
    {
        var target = Source(Context(owner: 2));
        var snapshot = Snapshot(target, 2);
        var original = Fixture(Source(Context(), target, documentReference: true, portCount: 2), snapshot);
        var cache = new ProjectReferenceBindingCache();
        var before = Bind(original, cache);
        var surviving = Assert.Single(before.References, row => row.Origin.PropertyPath.Contains(new PortId(Id(105)).ToString(), StringComparison.Ordinal));
        var declarations = JsonNode.Parse(original.Input.Source.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["ports"]!.AsArray().RemoveAt(0);
        var source = ProjectSource.Create(original.Input.Source.Context, original.Input.Source.Documents, Json(declarations));
        var after = Fixture(source, snapshot);
        var current = Bind(after, cache);
        Assert.Same(surviving, At(current, surviving));
        Assert.Contains(current.Symbols, row => row.Key == surviving.SymbolKey && row.Kind == SourceDocument.SchemaId);
        Assert.Equal(ModelBytes(Bind(after)), ModelBytes(current));
        Assert.Equal(3, before.References.Count);
        Assert.Equal(2, current.References.Count);
    }

    [Fact]
    public void ChangedTypedOriginAndManifestNeverBorrowPreviouslyResolvedReferences()
    {
        var fixture = Graph();
        var cache = new ProjectReferenceBindingCache();
        var first = TypedReference(Bind(fixture, cache));
        foreach (var variant in new[] { "origin", "version", "digest" })
        {
            var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
            var value = declarations[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!;
            if (variant == "origin") value["origin"]!["entityId"] = Id(999).ToString();
            if (variant == "version") value["manifest"]!["version"] = "substituted-version";
            if (variant == "digest") value["manifest"]!["digest"] = JsonNode.Parse(Digest("substituted").Data.CanonicalText);
            var source = ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(declarations));
            var changed = fixture with { Input = new CompilerInputManifest(source, fixture.Input.Settings) };
            var result = Bind(changed, cache);
            Assert.False(result.BindingComplete);
            Assert.NotEqual(ReferenceResolutionState.Resolved, TypedReference(result).State);
            Assert.NotSame(first, TypedReference(result));
            Assert.Equal(ModelBytes(Bind(changed)), ModelBytes(result));
        }
        Assert.Same(first, TypedReference(Bind(fixture, cache)));
    }

    private sealed record FixedGraph(CompilerInputManifest Input, CompilerMaterializedSnapshot[] Snapshots);

    private static FixedGraph Graph()
    {
        var leaf = Source(Context(owner: 3));
        var middle = Source(Context(owner: 2), leaf);
        return Fixture(Source(Context(), middle), Snapshot(middle, 2), Snapshot(leaf, 3));
    }

    private static FixedGraph Fixture(ProjectSource source, params CompilerMaterializedSnapshot[] snapshots) =>
        new(Input(source, snapshots), snapshots);

    private static PartialSemanticModel Bind(FixedGraph fixture, ProjectReferenceBindingCache? cache = null) =>
        ProjectSemanticBinder.Bind(fixture.Input, fixture.Snapshots, referenceBindingCache: cache);

    private static CoreCompilation Compile(FixedGraph fixture, ProjectReferenceBindingCache? cache = null) =>
        PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, referenceBindingCache: cache);

    private static CompilerBoundReference TypedReference(PartialSemanticModel model) =>
        Assert.Single(model.References, row => row.Origin.PropertyPath.Contains("/defaultValue", StringComparison.Ordinal));

    private static CompilerBoundReference At(PartialSemanticModel model, CompilerBoundReference reference) =>
        Assert.Single(model.References, row => row.Origin.Data.Equals(reference.Origin.Data));

    private static void AssertRetention(ProjectReferenceBindingCache cache, int count, int bytes) =>
        Assert.True(cache.RetainedCount == count && cache.RetainedCanonicalBytes == bytes, LifetimeInvariant);

    private static void AssertCompilationEquivalent(CoreCompilation expected, CoreCompilation actual)
    {
        foreach (var compilation in new[] { expected, actual })
        {
            Assert.Equal(CompilerExecutionState.Completed, compilation.Result.Execution);
            Assert.NotNull(compilation.Model);
            Assert.True(compilation.Model.BindingComplete);
            Assert.NotNull(compilation.Dependencies);
            Assert.Equal(CompilerBindingClosureState.Complete, compilation.Dependencies.Binding.State);
            Assert.Equal(3, compilation.Dependencies.Binding.Regions.Count);
            Assert.Equal(3, compilation.Dependencies.Regions.Count);
        }
        Assert.Equal(expected.Result.Data, actual.Result.Data);
        Assert.Equal(expected.Result.ResultDigest, actual.Result.ResultDigest);
        Assert.Equal(expected.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
        Assert.Equal(expected.Dependencies?.Data, actual.Dependencies?.Data);
        Assert.Equal(expected.Validation?.Ir?.Data, actual.Validation?.Ir?.Data);
        Assert.Equal(ModelBytes(expected.Model!), ModelBytes(actual.Model!));
    }

    private static string ModelBytes(PartialSemanticModel model) => Json(new
    {
        input = model.Input.Data.Value, model.BindingComplete, model.LogicalOperationsConsumed,
        declarations = model.Declarations.Select(row => new { row.Status, data = row.Data.Value }).ToArray(),
        symbols = model.Symbols.Select(row => new { row.Key, row.Kind, row.Status, context = row.Context.Data.Value, location = row.Location.Data.Value }).ToArray(),
        references = model.References.Select(row => new { origin = row.Origin.Data.Value, row.State, row.SymbolKey }).ToArray(),
        findings = model.Findings.Select(row => new { row.State, diagnostic = row.Diagnostic.Data.Value }).ToArray(),
        snapshots = model.BoundSnapshots.Select(row => new { source = row.Source.Data.Value, row.IsAuthorized, row.DeclarationsComplete, row.RelationsComplete }).ToArray()
    }).CanonicalText;

    private static ProjectSource Source(SourceRevisionContext context, ProjectSource? target = null, bool roots = true,
        bool includeTask = true, string taskKind = "coglatas.task", string? unrelatedName = null, bool documentReference = false, int portCount = 1)
    {
        var document = SourceDocument.Create(Document, "independent-reference-cache", Json(new
        {
            entityId = Root.ToString(), kind = "coglatas.project", schemaVersion = 1,
            payload = new { roots = roots ? new[] { TaskEntity.ToString() } : Array.Empty<string>() }
        }));
        var declarations = new List<object>();
        var ports = new List<object>();
        if (target is not null)
        {
            var type = new { kind = documentReference ? "coglatas.document-reference" : "coglatas.entity-reference",
                schemaVersion = 1, nullable = false, acceptsUnknown = false,
                expectedKind = documentReference ? SourceDocument.SchemaId : "coglatas.task" };
            for (var index = 0; index < portCount; index++)
            {
                var reference = new Dictionary<string, object>
                {
                    ["tenantId"] = target.TenantId.ToString(), ["projectId"] = target.ProjectId.ToString(),
                    ["context"] = target.Context.Data.Value,
                    ["origin"] = ProjectLocation.Create(Document, context, entityId: TaskEntity).Data.Value,
                    ["manifest"] = new { version = SnapshotVersion, digest = target.Digest.Data.Value },
                    [documentReference ? "documentId" : "entityId"] = documentReference ? Document.ToString() : TaskEntity.ToString()
                };
                ports.Add(new { portId = new PortId(Id(104 + index)).ToString(), direction = "coglatas.input", type,
                    minimumConnections = 0, maximumConnections = 1, defaultValue = new { type, state = "coglatas.known", value = reference } });
            }
        }
        if (includeTask) declarations.Add(new { entityId = TaskEntity.ToString(), documentId = Document.ToString(),
            kind = taskKind, schemaVersion = 1, payload = new { duration = Duration(), ports } });
        if (unrelatedName is not null) declarations.Add(new { entityId = Id(106).ToString(), documentId = Document.ToString(),
            kind = "coglatas.task", schemaVersion = 1, displayName = unrelatedName,
            payload = new { duration = Duration(), ports = Array.Empty<object>() } });
        return ProjectSource.Create(context, [document], Json(declarations));
    }

    private static object Duration() => new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1,
        nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "11" };

    private static CompilerInputManifest Input(ProjectSource source, CompilerMaterializedSnapshot[] snapshots)
    {
        object Artifact(string id) => new { artifactId = id, version = "1", digest = Digest(id).Data.Value };
        var none = new { kind = "coglatas.none" };
        return new CompilerInputManifest(source, Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.independent-reference-compiler"),
            ruleset = Artifact("coglatas.independent-reference-rules"), semantics = Artifact("coglatas.semantic-model"),
            ir = Artifact("coglatas.project-ir"), evaluator = Artifact("coglatas.independent-reference-evaluator"),
            authorizedExternalSnapshots = snapshots.Select(item => new { snapshotId = item.SnapshotId.ToString(), version = item.Version,
                digest = item.Snapshot.Source.Digest.Data.Value, context = item.Snapshot.Source.Context.Data.Value,
                coverage = "coglatas.complete", asOf = (string?)null }).ToArray(),
            resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(), observations = Array.Empty<object>(),
            evidence = Array.Empty<object>(), descriptors = Array.Empty<object>(), units = Array.Empty<object>(), calendars = Array.Empty<object>(),
            timeZones = Array.Empty<object>(), solver = none, stochastic = none, importer = none, scheduler = none, scenario = none,
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = new[] { "coglatas.core.structural" }, targets = new[] { "coglatas.semantic-model", "coglatas.project-ir" },
            analysisScope = new[] { "coglatas.source-structure" }, sourceCoverage = "coglatas.complete",
            executionProfile = new { purpose = "coglatas.normal", maximumOperations = 1_000_000, maximumDeclarations = 64,
                maximumRelations = 64, maximumDigits = 128, maximumScale = 32, rounding = "coglatas.ties-to-even" },
            clock = new { kind = "coglatas.not-applicable" }
        }));
    }

    private static CompilerMaterializedSnapshot Snapshot(ProjectSource source, int id) =>
        new(new ExternalSnapshotId(Id(700 + id)), SnapshotVersion, new RelationScopeSnapshot(source, true, true, true));

    private static SourceRevisionContext Context(int owner = 1, int revision = 1, int tenant = 1, int branch = 1) =>
        SourceRevisionContext.Committed(new RevisionRef(new BranchRef(new TenantId(Id(1000 + tenant)),
            new ProjectId(Id(2000 + owner)), new BranchId(Id(3000 + branch))), new RevisionId(Id(4000 + revision))));

    private static Guid Id(int value) => Guid.Parse($"19380007-0000-4000-8000-{value:x12}");
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static ContentDigest Digest(string text) => ContentDigest.Compute("coglatas.independent-reference-cache/1", Encoding.UTF8.GetBytes(text));
}
