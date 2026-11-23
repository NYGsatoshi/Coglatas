using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using Xunit;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent private-position retention controls; no runtime authority or UI acceptance.</summary>
public sealed class SecurityPrivateLookupCandidateTests
{
    private static readonly DocumentId Document = new(Id(11));
    private static readonly EntityId Root = new(Id(12));
    private static readonly EntityId First = new(Id(13));
    private static readonly EntityId Second = new(Id(14));
    private static readonly PortId Port = new(Id(15));
    private const string LifetimeInvariant = "Invalidated private lookup work must preserve current owner retention.";

    [Fact]
    public void ColdWarmAndUncachedIndexesHaveEqualBytesButCurrentRows()
    {
        var input = Input(); var cache = new ProjectSemanticIndexLookupCache();
        var plain = Build(input); var cold = Build(input, cache); var warm = Build(input, cache);
        AssertIndex(plain, cold); AssertIndex(plain, warm);
        Assert.Equal(1, cache.RetainedCount); Assert.Equal(1, cache.LookupReuseCount);
        Assert.True(cache.RetainedCanonicalBytes > 0); Assert.NotSame(cold, warm);
        Assert.Same(input, warm.Input); Assert.Equal(CompilerSemanticIndexState.Complete, warm.State);
        Assert.True(warm.IndexCoverageComplete); Assert.NotEmpty(warm.References);
        for (var at = 0; at < cold.Symbols.Count; at++) Assert.NotSame(cold.Symbols[at], warm.Symbols[at]);
        foreach (var row in warm.Symbols) Assert.Same(row, warm.Inspect(row.Symbol.Location, input.InputDigest));
    }

    [Fact]
    public void ReusedPositionsResolveRenamedDeclarationsFromCurrentRows()
    {
        var cache = new ProjectSemanticIndexLookupCache(); var old = Build(Input(), cache);
        var input = Input(name: "independent current task"); var current = Build(input, cache);
        Assert.Equal(1, cache.LookupReuseCount); Assert.NotEqual(old.Input.InputDigest, input.InputDigest);
        var row = current.Symbols.Single(item => item.Symbol.EntityId == First && item.Symbol.PortId is null);
        Assert.Equal("independent current task", row.DisplayName);
        Assert.Same(row, current.FindDefinition(Requirement(current).Origin, input.InputDigest).Target);
        AssertIndex(Build(input), current); Assert.NotEqual(old.Data, current.Data);
    }

    [Fact]
    public void ReusedPositionsReadCurrentPortTypeAndCompletionKinds()
    {
        var cache = new ProjectSemanticIndexLookupCache(); Build(Input(), cache);
        var input = Input(expectedKind: "coglatas.project"); var current = Build(input, cache);
        var origin = current.Symbols.Single(item => item.Symbol.PortId == Port).Symbol.Location;
        var page = current.CompleteEntityReference(origin, input.InputDigest);
        Assert.Equal(1, cache.LookupReuseCount); Assert.Equal(CompilerIndexQueryState.Ready, page.State);
        Assert.Equal("coglatas.project", page.ExpectedKind); Assert.NotEmpty(page.Candidates);
        Assert.All(page.Candidates, row => { Assert.Equal("coglatas.project", row.Symbol.Kind); Assert.Contains(current.Symbols, item => ReferenceEquals(item, row)); });
        AssertIndex(Build(input), current);
    }

    [Fact]
    public void ReusedPositionsReadCurrentDocumentMetadata()
    {
        var cache = new ProjectSemanticIndexLookupCache(); var previous = Build(Input(), cache);
        var input = Input(documentName: "independent changed document"); var current = Build(input, cache);
        Assert.Equal(1, cache.LookupReuseCount);
        Assert.Equal("independent changed document", Assert.Single(current.Symbols, row => row.IsDocument).DisplayName);
        Assert.NotEqual(previous.Data, current.Data); AssertIndex(Build(input), current);
    }

    [Fact]
    public void ReusedOriginPositionsReadNewTargetsAndReferenceInstances()
    {
        var cache = new ProjectSemanticIndexLookupCache(); var before = Build(Input(), cache);
        var input = Input(target: Second); var current = Build(input, cache);
        Assert.Equal(1, cache.LookupReuseCount);
        var oldReference = Requirement(before); var newReference = Requirement(current);
        Assert.Equal(oldReference.Origin.Data, newReference.Origin.Data); Assert.NotSame(oldReference, newReference);
        var definition = current.FindDefinition(oldReference.Origin, input.InputDigest);
        Assert.Equal(CompilerIndexQueryState.Ready, definition.State); Assert.Equal(Second, definition.Target!.Symbol.EntityId);
        Assert.Contains(current.Symbols, row => ReferenceEquals(row, definition.Target)); AssertIndex(Build(input), current);
    }

    [Fact]
    public void UnresolvedCurrentTargetsCannotBorrowPreviousResolution()
    {
        var cache = new ProjectSemanticIndexLookupCache(); var before = Build(Input(), cache);
        var input = Input(target: new EntityId(Id(999))); var current = Build(input, cache, expectedBindingComplete: false);
        Assert.Equal(1, cache.LookupReuseCount);
        var result = current.FindDefinition(Requirement(before).Origin, input.InputDigest);
        Assert.Equal(CompilerIndexQueryState.Unresolved, result.State); Assert.Null(result.Target);
        Assert.Equal(ReferenceResolutionState.MissingWithinCompleteScope, result.Resolution);
        Assert.Equal(CompilerSemanticIndexState.Incomplete, current.State); Assert.False(current.IndexCoverageComplete);
        AssertIndex(Build(input, expectedBindingComplete: false), current);
        var restored = Build(Input(), cache); Assert.Equal(2, cache.LookupReuseCount);
        Assert.Equal(First, restored.FindDefinition(Requirement(restored).Origin, restored.Input.InputDigest).Target!.Symbol.EntityId);
    }

    [Fact]
    public void WarmIndexesRejectReferenceCursorsFromEarlierInstances()
    {
        var input = Input(); var cache = new ProjectSemanticIndexLookupCache();
        var old = Build(input, cache); var current = Build(input, cache);
        var target = old.Symbols.Single(row => row.Symbol.EntityId == First && row.Symbol.PortId is null).Symbol.Location;
        var first = old.FindReferences(target, input.InputDigest, 1); Assert.NotNull(first.NextCursor);
        var refused = current.FindReferences(target, input.InputDigest, 1, first.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, refused.State); Assert.Empty(refused.References);
        Assert.Null(refused.Target); Assert.Null(refused.InputDigest); Assert.Null(refused.NextCursor); Assert.Equal(0, refused.ScannedReferences);
        Assert.Equal(CompilerIndexQueryState.Ready, old.FindReferences(target, input.InputDigest, 1, first.NextCursor).State);
    }

    [Fact]
    public void WarmIndexesRejectCompletionCursorsFromEarlierInstances()
    {
        var input = Input(); var cache = new ProjectSemanticIndexLookupCache();
        var old = Build(input, cache); var current = Build(input, cache);
        var origin = old.Symbols.Single(row => row.Symbol.PortId == Port).Symbol.Location;
        var first = old.CompleteEntityReference(origin, input.InputDigest, 1); Assert.NotNull(first.NextCursor);
        var refused = current.CompleteEntityReference(origin, input.InputDigest, 1, first.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, refused.State); Assert.Empty(refused.Candidates);
        Assert.Null(refused.Origin); Assert.Null(refused.InputDigest); Assert.Null(refused.NextCursor); Assert.Equal(0, refused.ScannedSymbols);
        Assert.Equal(CompilerIndexQueryState.Ready, old.CompleteEntityReference(origin, input.InputDigest, 1, first.NextCursor).State);
    }

    [Fact]
    public void PublicCollectionsAndDetachedBytesCannotPoisonRetainedPositions()
    {
        var input = Input(); var cache = new ProjectSemanticIndexLookupCache(); var index = Build(input, cache);
        var bytes = index.Data.ToCanonicalBytes(); var original = bytes.ToArray(); Array.Clear(bytes);
        Assert.Throws<NotSupportedException>(() => ((IList<CompilerIndexedSymbol>)index.Symbols).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<CompilerBoundReference>)index.References).Clear());
        var copy = index.Symbols.ToArray(); copy[0] = copy[0] with { DisplayName = "poisoned detached row" };
        Assert.Equal(original, Build(input, cache).Data.ToCanonicalBytes()); Assert.Equal(1, cache.LookupReuseCount);
    }

    [Fact]
    public void StaleAndForeignQueriesExposeNoBorrowedRowsOrInputStamp()
    {
        var input = Input(); var cache = new ProjectSemanticIndexLookupCache(); var index = Build(input, cache);
        var wrong = Input(name: "different digest").InputDigest; var origin = Requirement(index).Origin;
        Assert.Equal(CompilerIndexQueryState.StaleInput, index.FindDefinition(origin, wrong).State);
        Assert.Null(index.FindDefinition(origin, wrong).Target);
        var target = index.Symbols.First().Symbol.Location;
        var references = index.FindReferences(target, wrong); Assert.Null(references.InputDigest); Assert.Empty(references.References);
        var foreign = ProjectLocation.Create(Document, Context(branch: 8), entityId: First);
        Assert.Null(index.Inspect(foreign, input.InputDigest));
        Assert.Equal(CompilerIndexQueryState.OutOfScope, index.FindDefinition(foreign, input.InputDigest).State);
        Assert.Null(index.FindReferences(foreign, input.InputDigest).InputDigest);
    }

    [Fact]
    public void EveryTenantProjectAndBranchSwitchInvalidatesRetentionBeforeStaleBuild()
    {
        foreach (var context in new[] { Context(tenant: 8), Context(project: 8), Context(branch: 8) })
        {
            var cache = new ProjectSemanticIndexLookupCache(); var old = Input(); Build(old, cache); Build(old, cache);
            Assert.Equal(1, cache.LookupReuseCount);
            var other = Input(context: context); var compilation = Compile(other);
            var stale = CompilerSemanticIndex.Build(compilation.Dependencies!.Binding, old.InputDigest, lookupCache: cache);
            Assert.Equal(CompilerSemanticIndexState.StaleInput, stale.State); Assert.Empty(stale.Symbols); Assert.Empty(stale.References);
            Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.RetainedCanonicalBytes); Assert.Equal(0, cache.LookupReuseCount);
            AssertIndex(Build(other), Build(other, cache)); Assert.Equal(0, cache.LookupReuseCount);
        }
    }

    [Fact]
    public void PreCancelledOwnerChangePreservesTheExistingPublicCache()
    {
        var cache = new ProjectSemanticIndexLookupCache(); var input = Input(); Build(input, cache); Build(input, cache);
        var before = Retention(cache); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var other = Compile(Input(context: Context(project: 7)));
        Assert.Throws<OperationCanceledException>(() => CompilerSemanticIndex.Build(other.Dependencies!.Binding, other.Input.InputDigest,
            cancellationToken: cancelled.Token, lookupCache: cache));
        Assert.Equal(before, Retention(cache)); Build(input, cache); Assert.Equal(before.Reused + 1, cache.LookupReuseCount);
    }

    [Fact]
    public void LateDerivedBudgetRefusalCannotCommitColdOrReusedTables()
    {
        var input = Input(); var compilation = Compile(input); var binding = compilation.Dependencies!.Binding;
        var plain = CompilerSemanticIndex.Build(binding, input.InputDigest); Assert.True(plain.LogicalOperationsConsumed > 1);
        var cache = new ProjectSemanticIndexLookupCache();
        var cold = CompilerSemanticIndex.Build(binding, input.InputDigest, plain.LogicalOperationsConsumed - 1, lookupCache: cache);
        Assert.Equal(CompilerSemanticIndexState.ResourceLimit, cold.State); Assert.Empty(cold.Symbols); Assert.Empty(cold.References);
        Assert.Equal((0, 0, 0L), Retention(cache)); Build(input, cache); var retained = Retention(cache);
        var warm = CompilerSemanticIndex.Build(binding, input.InputDigest, plain.LogicalOperationsConsumed - 1, lookupCache: cache);
        Assert.Equal(CompilerSemanticIndexState.ResourceLimit, warm.State); Assert.Empty(warm.Symbols); Assert.Empty(warm.References);
        Assert.Equal(retained, Retention(cache)); AssertIndex(plain, Build(input, cache));
    }

    [Fact]
    public void ClearBeforePrivatePreparationCannotRepopulateAnInvalidatedLifetime()
    {
        var index = Build(Input()); var cache = new ProjectSemanticIndexLookupCache(); var boundary = new PrivateBoundary(cache, index);
        var earlier = boundary.Begin(index.Input.Source.Context.Branch); cache.Clear(); var retained = Retention(cache);
        boundary.Commit(boundary.Prepare(earlier, "independent earlier clear"));
        Assert.True(Retention(cache) == retained, LifetimeInvariant);
    }

    [Fact]
    public void NewOwnerBeforePrivatePreparationCannotReplaceItsRetainedTable()
    {
        var old = Build(Input()); var cache = new ProjectSemanticIndexLookupCache(); var boundary = new PrivateBoundary(cache, old);
        var earlier = boundary.Begin(old.Input.Source.Context.Branch);
        var currentInput = Input(context: Context(branch: 9)); Build(currentInput, cache); Build(currentInput, cache);
        var retained = Retention(cache); Assert.Equal(1, retained.Count); Assert.Equal(1, retained.Reused);
        boundary.Commit(boundary.Prepare(earlier, "independent earlier owner"));
        Assert.True(Retention(cache) == retained, LifetimeInvariant);
        Build(currentInput, cache); Assert.Equal(retained.Reused + 1, cache.LookupReuseCount);
    }

    [Fact]
    public void FreshPrivateLifetimeStillPublishesAndReusesItsActualTables()
    {
        var input = Input(context: Context(branch: 9)); var index = Build(input); var cache = new ProjectSemanticIndexLookupCache();
        var boundary = new PrivateBoundary(cache, index); var scope = boundary.Begin(input.Source.Context.Branch);
        boundary.Commit(boundary.Prepare(scope, "independent fresh current roster"));
        Assert.Equal(1, cache.RetainedCount); Assert.True(cache.RetainedCanonicalBytes > 0); Assert.Equal(0, cache.LookupReuseCount);
        boundary.Commit(boundary.Prepare(scope, "independent fresh current roster")); Assert.Equal(1, cache.LookupReuseCount);
        AssertIndex(index, Build(input, cache)); Assert.Equal(2, cache.RetainedCount);
    }

    [Fact]
    public void ClearAfterPreparationPreventsLatePrivateCommit()
    {
        var index = Build(Input()); var cache = new ProjectSemanticIndexLookupCache(); var boundary = new PrivateBoundary(cache, index);
        var prepared = boundary.Prepare(boundary.Begin(index.Input.Source.Context.Branch), "independent prepared clear");
        cache.Clear(); boundary.Commit(prepared); Assert.Equal((0, 0, 0L), Retention(cache));
        AssertIndex(index, Build(index.Input, cache)); Assert.Equal(1, cache.RetainedCount);
    }

    [Fact]
    public void NewOwnerAfterPreparationPreventsLatePrivateCommit()
    {
        var index = Build(Input()); var cache = new ProjectSemanticIndexLookupCache(); var boundary = new PrivateBoundary(cache, index);
        var prepared = boundary.Prepare(boundary.Begin(index.Input.Source.Context.Branch), "independent prepared owner");
        var currentInput = Input(context: Context(project: 9)); Build(currentInput, cache); var retained = Retention(cache);
        boundary.Commit(prepared); Assert.Equal(retained, Retention(cache)); Build(currentInput, cache); Assert.Equal(1, cache.LookupReuseCount);
    }

    [Fact]
    public void CancelledPrivateCommitCannotPublishPreparedPositions()
    {
        var index = Build(Input()); var cache = new ProjectSemanticIndexLookupCache(); var boundary = new PrivateBoundary(cache, index);
        var prepared = boundary.Prepare(boundary.Begin(index.Input.Source.Context.Branch), "independent cancelled commit");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var error = Assert.Throws<TargetInvocationException>(() => boundary.Commit(prepared, cancelled.Token));
        Assert.IsType<OperationCanceledException>(error.InnerException); Assert.Equal((0, 0, 0L), Retention(cache));
        AssertIndex(index, Build(index.Input, cache));
    }

    [Fact]
    public void BoundedEvictionKeepsCurrentResultsAndReusesOnlyTheRetainedRoster()
    {
        var cache = new ProjectSemanticIndexLookupCache(maximumEntries: 1);
        var first = Input(); var second = Input(context: Context(revision: 2));
        AssertIndex(Build(first), Build(first, cache)); AssertIndex(Build(second), Build(second, cache));
        Assert.Equal(1, cache.RetainedCount); Assert.Equal(0, cache.LookupReuseCount);
        Build(second, cache); Assert.Equal(1, cache.LookupReuseCount);
        AssertIndex(Build(first), Build(first, cache)); Assert.Equal(1, cache.RetainedCount); Assert.Equal(1, cache.LookupReuseCount);
        cache.Clear(); Assert.Equal((0, 0, 0L), Retention(cache));
    }

    [Fact]
    public void OversizedRosterKeysDeclineRetentionWithoutChangingQueries()
    {
        var input = Input(); var cache = new ProjectSemanticIndexLookupCache(maximumCanonicalBytes: 1);
        AssertIndex(Build(input), Build(input, cache)); AssertIndex(Build(input), Build(input, cache));
        Assert.Equal((0, 0, 0L), Retention(cache));
    }

    [Fact]
    public async Task OverlappingPublicBuildersAndClearsKeepOriginalInputOwnership()
    {
        var inputs = Enumerable.Range(1, 4).Select(owner => Input(context: Context(project: owner))).ToArray();
        var expected = inputs.Select(BuildWithoutCache).ToArray(); var cache = new ProjectSemanticIndexLookupCache(2);
        await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 16).Select(ordinal => System.Threading.Tasks.Task.Run(() =>
        {
            if (ordinal % 3 == 0) cache.Clear(); var at = ordinal % inputs.Length;
            var actual = Build(inputs[at], cache); AssertIndex(expected[at], actual);
            Assert.All(actual.Symbols, row => Assert.Equal(inputs[at].Source.Context, row.Symbol.Context));
        })));
        Assert.InRange(cache.RetainedCount, 0, 2); cache.Clear(); Assert.Equal((0, 0, 0L), Retention(cache));
    }

    [Fact]
    public void SeparateCompilationsExportOriginalReferenceIdentitiesAndEqualBytes()
    {
        var input = Input(); var first = Compile(input); var second = Compile(input);
        Assert.NotSame(first.Model, second.Model); Assert.NotSame(first.Model!.References[0], second.Model!.References[0]);
        Assert.Equal(first.Result.Data, second.Result.Data); Assert.Equal(first.LogicalOperationsConsumed, second.LogicalOperationsConsumed);
        var expected = SyntheticSemanticIndexProjection.Export(first); Assert.NotNull(expected);
        var cache = new ProjectSemanticIndexLookupCache(); Assert.Equal(expected, SyntheticSemanticIndexProjection.Export(first, lookupCache: cache));
        Assert.Equal(expected, SyntheticSemanticIndexProjection.Export(second, lookupCache: cache)); Assert.Equal(1, cache.LookupReuseCount);
        var index = CompilerSemanticIndex.Build(second.Dependencies!.Binding, input.InputDigest, lookupCache: cache);
        foreach (var symbol in index.Symbols)
        {
            var page = index.FindReferences(symbol.Symbol.Location, input.InputDigest);
            foreach (var reference in page.References) Assert.Contains(index.References, row => ReferenceEquals(row, reference));
        }
        Assert.Null(SyntheticSemanticIndexProjection.Export(second, 1, cache)); Assert.Equal(1, cache.RetainedCount);
    }

    [Fact]
    public void InvalidRetentionBoundsAreRefusedBeforeAUsableCacheExists()
    {
        foreach (var (entries, bytes) in new[] { (0, 1), (10_001, 1), (1, 0), (1, 4_194_305) })
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectSemanticIndexLookupCache(entries, bytes));
    }

    private static CompilerSemanticIndex BuildWithoutCache(CompilerInputManifest input) => Build(input);
    private static CoreCompilation Compile(CompilerInputManifest input, bool expectedBindingComplete = true)
    {
        var result = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.Equal(CompilerExecutionState.Completed, result.Result.Execution); Assert.NotNull(result.Model);
        Assert.Equal(expectedBindingComplete, result.Model.BindingComplete);
        if (expectedBindingComplete)
        {
            Assert.NotNull(result.Dependencies);
            Assert.Equal(CompilerBindingClosureState.Complete, result.Dependencies.Binding.State);
            Assert.True(result.Dependencies.Binding.IncludesDeclaredSnapshots);
        }
        else Assert.Null(result.Dependencies);
        return result;
    }
    private static CompilerSemanticIndex Build(CompilerInputManifest input, ProjectSemanticIndexLookupCache? cache = null,
        bool expectedBindingComplete = true)
    {
        var compilation = Compile(input, expectedBindingComplete);
        // The deliberately unresolved root has no attached dependency analysis. Its
        // actual model still supports a public, explicitly incomplete binding closure.
        var binding = expectedBindingComplete ? compilation.Dependencies!.Binding
            : CompilerBindingClosure.Build(compilation.Model!, input.InputDigest);
        if (!expectedBindingComplete)
        {
            Assert.Equal(CompilerBindingClosureState.Incomplete, binding.State);
            Assert.False(binding.IncludesDeclaredSnapshots);
        }
        return CompilerSemanticIndex.Build(binding, input.InputDigest, lookupCache: cache);
    }
    private static CompilerBoundReference Requirement(CompilerSemanticIndex index) =>
        Assert.Single(index.References, item => item.Origin.EntityId == new EntityId(Id(31)) && item.Origin.PropertyPath == "/payload/target");
    private static (int Count, int Bytes, long Reused) Retention(ProjectSemanticIndexLookupCache cache) =>
        (cache.RetainedCount, cache.RetainedCanonicalBytes, cache.LookupReuseCount);
    private static void AssertIndex(CompilerSemanticIndex expected, CompilerSemanticIndex actual)
    {
        Assert.Equal(expected.State, actual.State); Assert.Equal(expected.Data, actual.Data); Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
        Assert.Equal(expected.IndexCoverageComplete, actual.IndexCoverageComplete); Assert.Equal(expected.Symbols.Count, actual.Symbols.Count);
        Assert.Equal(expected.References.Count, actual.References.Count);
        foreach (var reference in actual.References)
        {
            var definition = actual.FindDefinition(reference.Origin, actual.Input.InputDigest);
            if (definition.Target is { } target) Assert.Contains(actual.Symbols, row => ReferenceEquals(row, target));
        }
    }

    // Compatibility adapter for the real internal staging boundary in all pinned
    // Product versions. It does not replace fields, patch Product, or import Product tests.
    private sealed class PrivateBoundary(ProjectSemanticIndexLookupCache cache, CompilerSemanticIndex actualIndex)
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo BeginMethod = typeof(ProjectSemanticIndexLookupCache).GetMethod("BeginBinding", Hidden)!;
        private static readonly MethodInfo PrepareMethod = typeof(ProjectSemanticIndexLookupCache).GetMethod("Prepare", Hidden)!;
        private static readonly MethodInfo CommitMethod = typeof(ProjectSemanticIndexLookupCache).GetMethod("Commit", Hidden)!;
        public object Begin(BranchRef owner) => BeginMethod.Invoke(cache, [owner, CancellationToken.None]) ?? owner;
        public object Prepare(object scope, string dependency)
        {
            var tables = PrepareMethod.ReturnType.GetProperty("Tables")!.PropertyType;
            var symbols = Expression.Constant(actualIndex.Symbols, typeof(IReadOnlyList<CompilerIndexedSymbol>));
            var references = Expression.Constant(actualIndex.References, typeof(IReadOnlyList<CompilerBoundReference>));
            var currentConstructor = tables.GetConstructor(Hidden, [symbols.Type, references.Type, typeof(CancellationToken)]);
            var create = currentConstructor is not null
                ? Expression.New(currentConstructor, symbols, references, Expression.Constant(CancellationToken.None))
                : Expression.New(tables.GetConstructor(Hidden, [symbols.Type, references.Type])
                    ?? throw new InvalidOperationException("Unsupported private lookup table constructor."), symbols, references);
            var factory = Expression.Lambda(PrepareMethod.GetParameters()[2].ParameterType, create).Compile();
            return PrepareMethod.Invoke(cache, [scope, new[] { dependency }, factory, CancellationToken.None])!;
        }
        public void Commit(object lease, CancellationToken token = default) => CommitMethod.Invoke(cache, [lease, token]);
    }

    private static CompilerInputManifest Input(SourceRevisionContext? context = null, string name = "independent original task",
        string expectedKind = "coglatas.task", string documentName = "independent private lookup", EntityId? target = null)
    {
        context ??= Context(); var chosen = target ?? First;
        var document = SourceDocument.Create(Document, documentName, Json(new { entityId = Root.ToString(), kind = "coglatas.project", schemaVersion = 1,
            payload = new { roots = new[] { First.ToString(), Second.ToString() } } }));
        var port = new { portId = Port.ToString(), direction = "coglatas.input",
            type = new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind },
            minimumConnections = 0, maximumConnections = 1 };
        object Duration() => new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "13" };
        object RequirementDeclaration(int value) => new { entityId = Id(value).ToString(), documentId = Document.ToString(), kind = "coglatas.requirement", schemaVersion = 1,
            payload = new { target = new { tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(), entityId = chosen.ToString(), context = context.Data.Value },
                condition = new { kind = "coglatas.literal", value = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = true } },
                evidenceKind = "coglatas.model-proof" } };
        var declarations = new object[] {
            new { entityId = First.ToString(), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1, displayName = name, payload = new { duration = Duration(), ports = new[] { port } } },
            new { entityId = Second.ToString(), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1, displayName = "independent second task", payload = new { duration = Duration(), ports = Array.Empty<object>() } },
            RequirementDeclaration(31), RequirementDeclaration(32)
        };
        var source = ProjectSource.Create(context, [document], Json(declarations));
        object Artifact(string id, string version = "1") => new { artifactId = id, version,
            digest = ContentDigest.Compute("coglatas.independent-private-lookup/1", Encoding.UTF8.GetBytes(id + "/" + version)).Data.Value };
        var none = new { kind = "coglatas.none" };
        return new CompilerInputManifest(source, Json(new {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.independent-lookup-compiler"), ruleset = Artifact("coglatas.independent-lookup-rules"),
            semantics = Artifact("coglatas.semantic-model", "2"), ir = Artifact("coglatas.project-ir", "2"), evaluator = Artifact("coglatas.independent-lookup-evaluator"),
            authorizedExternalSnapshots = Array.Empty<object>(), resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(), observations = Array.Empty<object>(),
            evidence = Array.Empty<object>(), descriptors = Array.Empty<object>(), units = Array.Empty<object>(), calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            solver = none, stochastic = none, importer = none, scheduler = none, scenario = none,
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }.Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = new[] { "coglatas.core.structural" }, targets = new[] { "coglatas.semantic-model", "coglatas.project-ir" }, analysisScope = new[] { "coglatas.source-structure" }, sourceCoverage = "coglatas.complete",
            executionProfile = new { purpose = "coglatas.normal", maximumOperations = 1_000_000, maximumDeclarations = 64, maximumRelations = 64, maximumDigits = 128, maximumScale = 32, rounding = "coglatas.ties-to-even" },
            clock = new { kind = "coglatas.not-applicable" }
        }));
    }
    private static SourceRevisionContext Context(int tenant = 1, int project = 1, int branch = 1, int revision = 1) =>
        SourceRevisionContext.Committed(new RevisionRef(new BranchRef(new TenantId(Id(1000 + tenant)), new ProjectId(Id(2000 + project)), new BranchId(Id(3000 + branch))), new RevisionId(Id(4000 + revision))));
    private static Guid Id(int value) => Guid.Parse($"19380009-0000-4000-8000-{value:x12}");
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
}
