using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Reflection;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SemanticIndexLookupCacheTests
{
    [Fact]
    public void WarmPrivateLookupsKeepPublicRowsDefinitionsAndBothCursorKindsOwnedByTheNewIndex()
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true);
        var cache = new ProjectSemanticIndexLookupCache();
        var first = Build(fixture.Input, cache, fixture.Snapshots)!;
        var current = Build(fixture.Input, cache, fixture.Snapshots)!;
        Assert.Equal(1, cache.LookupReuseCount); Assert.Equal(1, cache.RetainedCount);
        Assert.NotSame(first, current); Assert.Equal(first.Data, current.Data);
        for (var i = 0; i < first.Symbols.Count; i++) Assert.NotSame(first.Symbols[i], current.Symbols[i]);
        var port = first.Symbols.First(row => row.Symbol.PortId is not null).Symbol.Location;
        var completion = first.CompleteEntityReference(port, fixture.Input.InputDigest, 1); Assert.NotNull(completion.NextCursor);
        var refusedCompletion = current.CompleteEntityReference(port, fixture.Input.InputDigest, 1, completion.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, refusedCompletion.State); Assert.Empty(refusedCompletion.Candidates);
        Assert.Null(refusedCompletion.InputDigest); Assert.Equal(0, refusedCompletion.ScannedSymbols);
        var references = first.Symbols.Select(row => first.FindReferences(row.Symbol.Location, fixture.Input.InputDigest, 1))
            .First(page => page.NextCursor is not null);
        var refusedReferences = current.FindReferences(references.Target!, fixture.Input.InputDigest, 1, references.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, refusedReferences.State); Assert.Empty(refusedReferences.References);
        Assert.Null(refusedReferences.InputDigest); Assert.Equal(0, refusedReferences.ScannedReferences);
        foreach (var reference in current.References)
        {
            var definition = current.FindDefinition(reference.Origin, fixture.Input.InputDigest);
            if (definition.Target is { } target) Assert.Contains(current.Symbols, row => ReferenceEquals(row, target));
        }
        Assert.Throws<NotSupportedException>(() => ((IList<CompilerIndexedSymbol>)current.Symbols).Clear());
        Assert.Equal(first.Data, Build(fixture.Input, cache, fixture.Snapshots)!.Data);
    }

    [Theory]
    [InlineData("name")] [InlineData("type")] [InlineData("document")]
    public void ReusedPositionsReadNamesTypesAndDocumentMetadataFromTheCurrentRows(string change)
    {
        var basis = ReferenceCompletionTests.CompletionInput(1); var cache = new ProjectSemanticIndexLookupCache();
        var first = Build(basis, cache)!; var declarations = JsonNode.Parse(basis.Source.Declarations.CanonicalText)!.AsArray();
        var documents = basis.Source.Documents;
        if (change == "name") declarations[0]!["displayName"] = "Current display name";
        if (change == "type") declarations[0]!["payload"]!["ports"]![0]!["type"]!["expectedKind"] = "coglatas.project";
        if (change == "document") documents = [SourceDocument.Create(documents[0].DocumentId, "current document", documents[0].Root.Data)];
        var input = Input(source: ProjectSource.Create(basis.Source.Context, documents, Json(declarations)));
        var current = Build(input, cache)!; Assert.Equal(1, cache.LookupReuseCount);
        Assert.NotEqual(first.Input.InputDigest, current.Input.InputDigest); Assert.NotEqual(first.Digest, current.Digest);
        var task = current.Symbols.Single(row => row.Symbol.EntityId == EntityId.Parse(Id(10)) && row.Symbol.PortId is null);
        if (change == "name") Assert.Equal("Current display name", task.DisplayName);
        if (change == "document") Assert.Equal("current document", Assert.Single(current.Symbols, row => row.IsDocument).DisplayName);
        if (change == "type")
        {
            var port = current.Symbols.Single(row => row.Symbol.PortId is not null);
            var page = current.CompleteEntityReference(port.Symbol.Location, input.InputDigest);
            Assert.Equal("coglatas.project", page.ExpectedKind); Assert.NotEmpty(page.Candidates);
            Assert.All(page.Candidates, row => Assert.Equal("coglatas.project", row.Symbol.Kind));
        }
    }

    [Fact]
    public void AnUnchangedOriginPositionUsesItsNewTargetAndResolutionRatherThanAnEarlierReferenceObject()
    {
        var basis = ReferenceCompletionTests.CompletionInput(2); var cache = new ProjectSemanticIndexLookupCache();
        CompilerInputManifest At(int target)
        {
            var declarations = JsonNode.Parse(basis.Source.Declarations.CanonicalText)!.AsArray();
            declarations.Add(Obj(new { entityId = Id(70), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1,
                payload = new { target = CompilerAnalysisFixtures.Reference(target, basis.Source.Context),
                    condition = PlanDeclarationTests.Literal(true), evidenceKind = "coglatas.model-proof" } }));
            return Input(source: ProjectSource.Create(basis.Source.Context, basis.Source.Documents, Json(declarations)));
        }
        var original = Build(At(10), cache)!; var origin = Assert.Single(original.References).Origin;
        var current = Build(At(11), cache)!;
        Assert.Equal(EntityId.Parse(Id(11)), current.FindDefinition(origin, current.Input.InputDigest).Target!.Symbol.EntityId);
        Assert.NotSame(Assert.Single(original.References), Assert.Single(current.References));
        var missing = Build(At(999), cache)!; var refused = missing.FindDefinition(origin, missing.Input.InputDigest);
        Assert.Equal(CompilerIndexQueryState.Unresolved, refused.State);
        Assert.Equal(ReferenceResolutionState.MissingWithinCompleteScope, refused.Resolution); Assert.Null(refused.Target);
        var restored = Build(At(10), cache)!;
        Assert.Equal(EntityId.Parse(Id(10)), restored.FindDefinition(origin, restored.Input.InputDigest).Target!.Symbol.EntityId);
        Assert.Equal(3, cache.LookupReuseCount);
    }

    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial-declarations")]
    [InlineData("partial-relations")] [InlineData("duplicate")]
    public void ChangedDependencyAdmissionKeepsExactUncachedCoverageAndEveryQueryPage(string admission)
    {
        var basis = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true);
        var changed = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true, bAdmission: admission);
        var cache = new ProjectSemanticIndexLookupCache(); Build(basis.Input, cache, basis.Snapshots);
        var snapshots = admission == "duplicate" ? changed.Snapshots.Append(changed.Snapshots[1]).ToArray() : changed.Snapshots;
        var current = Build(changed.Input, cache, snapshots)!;
        Assert.False(current.IndexCoverageComplete); Build(basis.Input, cache, basis.Snapshots);
    }

    [Theory]
    [InlineData("rules")] [InlineData("units")] [InlineData("calendars")] [InlineData("observations")]
    [InlineData("descriptors")] [InlineData("source-coverage")] [InlineData("package")]
    public void GlobalInputChangesStillRebuildTheActualCompilerAndIndexFacts(string dimension)
    {
        var fixture = SymbolBindingCacheTests.External(Source(9)); var source = fixture.Input.Source;
        var cache = new ProjectSemanticIndexLookupCache(); Build(fixture.Input, cache, [fixture.Snapshot]);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        if (dimension == "rules") settings["rules"]![0]!["configuration"] = Obj(new { future = true });
        if (dimension is "units" or "calendars") settings[dimension] = new JsonArray(Artifact("example." + dimension));
        if (dimension == "observations") settings["observations"] = new JsonArray(Obj(new { snapshotId = Id(61), version = "1", context = Source(9).Context.Data.Value,
            digest = Source(9).Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        if (dimension == "descriptors") settings["descriptors"] = new JsonArray();
        if (dimension == "source-coverage") settings["sourceCoverage"] = "coglatas.partial";
        if (dimension == "package") source = ProjectSource.Create(source.Context, source.Documents, source.Declarations, source.Relations,
            packageLock: Json(new JsonArray(Artifact("example.unadopted-package"))));
        Build(Input(settings, source), cache, [fixture.Snapshot]); Build(fixture.Input, cache, [fixture.Snapshot]);
    }

    [Theory]
    [InlineData("committed")] [InlineData("candidate")] [InlineData("scenario")]
    public void ContextChangesCannotMixRowsWhileReturningToAnIdenticalRosterCanReusePrivateTables(string kind)
    {
        var original = Source(); var committed = original.Context.CommittedRevision!;
        var context = kind == "committed" ? Source(99).Context : kind == "candidate"
            ? SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(1500)), CandidateRevisionId.Parse(Id(1501)), committed, committed))
            : SourceRevisionContext.Hypothetical(new(ScenarioId.Parse(Id(1502)), ScenarioRevisionId.Parse(Id(1503)), original.Context, original.Digest));
        var input = Input(source: original); var settings = Settings();
        if (context.Scenario is { } scenario) settings["scenario"] = Obj(new { baseContext = scenario.BaseContext.Data.Value,
            overlayDigest = scenario.OverlayDigest.Data.Value, assumptions = Array.Empty<object>() });
        var other = Input(settings, ProjectSource.Create(context, original.Documents));
        var cache = new ProjectSemanticIndexLookupCache(); var first = Build(input, cache)!; var current = Build(other, cache)!;
        Assert.All(current.Symbols, row => Assert.Equal(context, row.Symbol.Context));
        Assert.Equal(CompilerIndexQueryState.OutOfScope, current.FindReferences(first.Symbols[0].Symbol.Location, other.InputDigest).State);
        Assert.Equal(0, cache.LookupReuseCount); Assert.Equal(2, cache.RetainedCount);
        Build(input, cache); Assert.Equal(1, cache.LookupReuseCount);
    }

    [Theory]
    [InlineData("tenant")] [InlineData("project")] [InlineData("branch")]
    public void ANewOwnerClearsTheSessionEvenWhenTheDerivedIndexIsStaleAndPublishesNoRows(string dimension)
    {
        var source = Source(); var cache = new ProjectSemanticIndexLookupCache(); var input = Input(source: source);
        Build(input, cache); var branch = source.Context.Branch;
        var context = SourceRevisionContext.Committed(new(new(dimension == "tenant" ? TenantId.Parse(Id(90)) : branch.TenantId,
            dimension == "project" ? ProjectId.Parse(Id(90)) : branch.ProjectId,
            dimension == "branch" ? BranchId.Parse(Id(90)) : branch.BranchId), RevisionId.Parse(Id(5))));
        var other = Input(source: ProjectSource.Create(context, source.Documents)); var compilation = PureCoreCompiler.Compile(other, other.Compiler);
        var binding = CompilerBindingClosure.Build(compilation.Model!, other.InputDigest);
        var stale = CompilerSemanticIndex.Build(binding, input.InputDigest, lookupCache: cache);
        Assert.Equal(CompilerSemanticIndexState.StaleInput, stale.State); Assert.Empty(stale.Symbols); Assert.Empty(stale.References);
        Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.LookupReuseCount);
        Build(input, cache); Assert.Equal(0, cache.LookupReuseCount);
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(1, 65536)] [InlineData(2, 65536)]
    public void EntryAndExactKeyByteBoundsDeclineOrEvictWithoutChangingAnyQuery(int entries, int bytes)
    {
        var cache = new ProjectSemanticIndexLookupCache(entries, bytes);
        foreach (var revision in Enumerable.Range(100, 5)) Build(Input(source: Source(revision)), cache);
        Build(Input(source: Source(104)), cache);
        Assert.InRange(cache.RetainedCount, 0, entries); Assert.InRange(cache.RetainedCanonicalBytes, 0, bytes);
        if (bytes == 1) { Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.LookupReuseCount); }
        else Assert.True(cache.LookupReuseCount > 0);
        cache.Clear(); Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.RetainedCanonicalBytes); Assert.Equal(0, cache.LookupReuseCount);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(10001, 1)] [InlineData(1, 0)] [InlineData(1, 4194305)]
    public void InvalidRetentionBoundsAreRefusedBeforeAnySessionExists(int entries, int bytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectSemanticIndexLookupCache(entries, bytes));

    [Fact]
    public void LateDerivedBudgetExhaustionAndCancellationCannotCommitPreparedLookupsOrTransferOldRows()
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true);
        var compilation = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        var binding = compilation.Dependencies!.Binding; var complete = CompilerSemanticIndex.Build(binding, fixture.Input.InputDigest);
        var cache = new ProjectSemanticIndexLookupCache();
        var refused = CompilerSemanticIndex.Build(binding, fixture.Input.InputDigest, complete.LogicalOperationsConsumed - 1, lookupCache: cache);
        Assert.Equal(CompilerSemanticIndexState.ResourceLimit, refused.State); Assert.Empty(refused.Symbols); Assert.Empty(refused.References);
        Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.LookupReuseCount);
        Assert.Equal(complete.Data, CompilerSemanticIndex.Build(binding, fixture.Input.InputDigest, lookupCache: cache).Data);
        CompilerSemanticIndex.Build(binding, fixture.Input.InputDigest, complete.LogicalOperationsConsumed - 1, lookupCache: cache);
        Assert.Equal(1, cache.RetainedCount); Assert.Equal(0, cache.LookupReuseCount);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompilerSemanticIndex.Build(binding, fixture.Input.InputDigest, cancellationToken: cancelled.Token, lookupCache: cache));
        Assert.Equal(1, cache.RetainedCount); Assert.Equal(0, cache.LookupReuseCount);
    }

    [Theory]
    [InlineData("clear")] [InlineData("owner")]
    public void InvalidationBetweenBuildStartAndLookupPreparationCannotRepopulateTheEarlierOwner(string invalidation)
    {
        var input = Input(); var binding = CompilerBindingClosure.Build(ProjectSemanticBinder.Bind(input), input.InputDigest);
        var index = CompilerSemanticIndex.Build(binding, input.InputDigest); var owner = input.Source.Context.Branch;
        var cache = new ProjectSemanticIndexLookupCache(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var begin = typeof(ProjectSemanticIndexLookupCache).GetMethod("BeginBinding", flags)!;
        var prepare = typeof(ProjectSemanticIndexLookupCache).GetMethod("Prepare", flags)!;
        var commit = typeof(ProjectSemanticIndexLookupCache).GetMethod("Commit", flags)!;
        // Exercise the real staging boundary without adding a production timing hook.
        // The void-return fallback also runs the same control against the original implementation.
        var earlierScope = begin.Invoke(cache, [owner, CancellationToken.None]) ?? owner;
        if (invalidation == "clear") cache.Clear();
        else begin.Invoke(cache, [new BranchRef(owner.TenantId, owner.ProjectId, BranchId.Parse(Id(99))), CancellationToken.None]);
        var tablesType = prepare.ReturnType.GetProperty("Tables")!.PropertyType;
        var constructor = tablesType.GetConstructors(flags).Single();
        var factory = Expression.Lambda(prepare.GetParameters()[2].ParameterType, Expression.New(constructor,
            Expression.Constant(index.Symbols, typeof(IReadOnlyList<CompilerIndexedSymbol>)),
            Expression.Constant(index.References, typeof(IReadOnlyList<CompilerBoundReference>)),
            Expression.Constant(CancellationToken.None))).Compile();
        var enumerated = false;
        IEnumerable<string> EarlierRoster()
        {
            enumerated = true;
            yield return "exact-roster-control";
        }

        var prepared = prepare.Invoke(cache, [earlierScope, EarlierRoster(), factory, CancellationToken.None]);
        Assert.False(enumerated);
        commit.Invoke(cache, [prepared, CancellationToken.None]);
        Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.LookupReuseCount);
        var currentScope = begin.Invoke(cache, [owner, CancellationToken.None]) ?? owner;
        var current = prepare.Invoke(cache, [currentScope, new[] { "exact-roster-control" }, factory, CancellationToken.None]);
        commit.Invoke(cache, [current, CancellationToken.None]); Assert.Equal(1, cache.RetainedCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidationDuringKeyFramingSkipsTheRemainingRoster(bool changeOwner)
    {
        var cache = new ProjectSemanticIndexLookupCache();
        var owner = Input().Source.Context.Branch;
        var earlier = cache.BeginBinding(owner, CancellationToken.None);
        var readSuffix = false;
        var disposed = false;

        IEnumerable<string> Dependencies()
        {
            try
            {
                yield return "already-framed";
                if (changeOwner)
                    cache.BeginBinding(new BranchRef(owner.TenantId, owner.ProjectId, BranchId.Parse(Id(99))), CancellationToken.None);
                else
                    cache.Clear();
                yield return "discovered-during-invalidation";
                readSuffix = true;
                yield return "obsolete-roster-suffix";
            }
            finally
            {
                disposed = true;
            }
        }

        var lease = cache.Prepare(earlier, Dependencies(), () =>
        {
            Assert.True(disposed);
            return new([], [], CancellationToken.None);
        }, CancellationToken.None);

        Assert.False(readSuffix);
        Assert.Null(lease.Key);
        Assert.Equal(0, lease.Bytes);
        cache.Commit(lease, CancellationToken.None);
        Assert.Equal(0, cache.RetainedCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationDuringFreshLookupsDoesNotReadTheRemainingRoster(bool cancelInSymbols)
    {
        var input = ReferenceCompletionTests.CompletionInput(4);
        var binding = CompilerBindingClosure.Build(ProjectSemanticBinder.Bind(input), input.InputDigest);
        var index = CompilerSemanticIndex.Build(binding, input.InputDigest);
        var symbol = index.Symbols[0];
        var reference = new CompilerBoundReference(symbol.Symbol.Location, ReferenceResolutionState.Resolved, symbol.Symbol.Key);
        using var cancellation = new CancellationTokenSource();
        var symbols = new CancelOnFirstRead<CompilerIndexedSymbol>(symbol, cancellation);
        var references = new CancelOnFirstRead<CompilerBoundReference>(reference, cancellation);

        Assert.Throws<OperationCanceledException>(() => new SemanticIndexLookupTables(
            cancelInSymbols ? symbols : index.Symbols, cancelInSymbols ? [] : references, cancellation.Token));
        Assert.Equal(cancelInSymbols ? 1 : 0, symbols.ReadCount);
        Assert.Equal(cancelInSymbols ? 0 : 1, references.ReadCount);
    }

    [Fact]
    public void RepeatedKeysLocationsAndOriginsStayAmbiguousWhileUniquePositionsArePreserved()
    {
        var input = ReferenceCompletionTests.CompletionInput(4);
        var binding = CompilerBindingClosure.Build(ProjectSemanticBinder.Bind(input), input.InputDigest);
        var index = CompilerSemanticIndex.Build(binding, input.InputDigest);
        var first = index.Symbols[0];
        var second = index.Symbols[1];
        var third = index.Symbols[2];
        var unique = index.Symbols[3];
        var duplicateLocation = third with { Symbol = third.Symbol with { Location = second.Symbol.Location } };
        var reference = new CompilerBoundReference(first.Symbol.Location, ReferenceResolutionState.Resolved, first.Symbol.Key);
        var uniqueReference = new CompilerBoundReference(unique.Symbol.Location, ReferenceResolutionState.Resolved, unique.Symbol.Key);
        var tables = new SemanticIndexLookupTables([first, first, first, second, duplicateLocation, unique],
            [reference, reference, reference, uniqueReference], CancellationToken.None);

        Assert.Equal(3, tables.KeyCount);
        Assert.Equal(1, tables.LocationCount);
        Assert.False(tables.HasKey(first.Symbol.Key));
        Assert.False(tables.TryKey(first.Symbol.Key, out _));
        Assert.False(tables.TryLocation(first.Symbol.Location.Data.CanonicalText, out _));
        Assert.False(tables.TryLocation(second.Symbol.Location.Data.CanonicalText, out _));
        Assert.False(tables.TryOrigin(reference.Origin.Data.CanonicalText, out _));
        Assert.True(tables.TryKey(third.Symbol.Key, out var thirdKey));
        Assert.Equal(4, thirdKey);
        Assert.True(tables.TryLocation(unique.Symbol.Location.Data.CanonicalText, out var uniqueLocation));
        Assert.Equal(5, uniqueLocation);
        Assert.True(tables.TryOrigin(uniqueReference.Origin.Data.CanonicalText, out var uniqueOrigin));
        Assert.Equal(3, uniqueOrigin);
    }

    private sealed class CancelOnFirstRead<T>(T row, CancellationTokenSource cancellation) : IReadOnlyList<T>
    {
        public int Count => 4;
        public int ReadCount { get; private set; }

        public T this[int index]
        {
            get
            {
                ReadCount++;
                if (index == 0) cancellation.Cancel();
                return row;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (var index = 0; index < Count; index++) yield return this[index];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClearCompletesWhileAnOlderBuilderPreparesLookups(bool pauseDuringDependencies)
    {
        var cache = new ProjectSemanticIndexLookupCache();
        var scope = cache.BeginBinding(Input().Source.Context.Branch, CancellationToken.None);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = TimeSpan.FromSeconds(10);

        void Pause()
        {
            paused.SetResult();
            if (!resume.Task.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Lookup preparation did not resume.");
        }

        IEnumerable<string> Dependencies()
        {
            if (pauseDuringDependencies) Pause();
            yield return "original-roster";
        }

        SemanticIndexLookupTables Create()
        {
            if (!pauseDuringDependencies) Pause();
            return new([], [], CancellationToken.None);
        }

        var preparation = Task.Run(() => cache.Prepare(scope, Dependencies(), Create, CancellationToken.None));
        Task? clear = null;
        try
        {
            await paused.Task.WaitAsync(timeout);
            clear = Task.Run(cache.Clear);
            await clear.WaitAsync(timeout);
        }
        finally
        {
            resume.SetResult();
            await preparation;
            if (clear is not null) await clear;
        }

        cache.Commit(await preparation, CancellationToken.None);
        Assert.Equal(0, cache.RetainedCount);
        Assert.Equal(0, cache.RetainedCanonicalBytes);
        Assert.Equal(0, cache.LookupReuseCount);
    }


    [Fact]
    public async Task OverlappingBuildersAndExplicitClearKeepTheirOwnInputDataAndNeverPublishAPartialTable()
    {
        var inputs = Enumerable.Range(100, 4).Select(revision => Input(source: Source(revision))).ToArray();
        var bindings = inputs.Select(input => CompilerBindingClosure.Build(ProjectSemanticBinder.Bind(input), input.InputDigest)).ToArray();
        var expected = bindings.Select(binding => CompilerSemanticIndex.Build(binding, binding.Input.InputDigest).Data).ToArray();
        var cache = new ProjectSemanticIndexLookupCache();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(ordinal => Task.Run(() =>
        {
            var at = ordinal % inputs.Length; if (ordinal % 3 == 0) cache.Clear();
            var current = CompilerSemanticIndex.Build(bindings[at], inputs[at].InputDigest, lookupCache: cache);
            Assert.Equal(expected[at], current.Data); Assert.All(current.Symbols, row => Assert.Equal(inputs[at].Source.Context, row.Symbol.Context));
        })));
        Assert.InRange(cache.RetainedCount, 0, 4);
    }

    [Fact]
    public async Task ThePureViewModelCanReusePrivateTablesWithoutAcceptingEarlierSymbolsPagesOrGraphObjects()
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true); var input = fixture.Input;
        CompilerArtifactIdentity Artifact(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var cache = new ProjectSemanticIndexLookupCache();
        using var vm = new SyntheticWorkbenchViewModel(cache);
        var request = new CompilerHostRequest(new(input), 1, fixture.Snapshots); vm.BeginSynthetic(request);
        Assert.True(vm.TryAccept(await new LocalNetCompilerHost(profile).CompileAsync(request)));
        Assert.False(vm.LoadSyntheticSemanticIndex()); Assert.Equal(0, cache.RetainedCount);
        Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.LoadSyntheticSemanticIndex()); var old = vm.SemanticIndex!;
        var symbol = old.Symbols.First(row => old.FindReferences(row.Symbol.Location, input.InputDigest).References.Count > 0);
        Assert.True(vm.SelectIndexedSymbol(symbol)); var page = Assert.IsType<CompilerIndexReferencePage>(vm.ReadIndexedReferences());
        var graph = Assert.IsType<SyntheticReferenceGraphPage>(vm.IndexedReferenceGraph);
        Assert.True(vm.LoadSyntheticSemanticIndex()); Assert.Equal(1, cache.LookupReuseCount); Assert.NotSame(old, vm.SemanticIndex);
        Assert.False(vm.SelectIndexedSymbol(symbol)); Assert.Null(vm.SelectedIndexedSymbol);
        Assert.False(vm.NavigateIndexedReference(page, page.References[0])); Assert.False(vm.NavigateIndexedGraphTarget(graph));
        Assert.Null(vm.ScopedSourceTarget);
    }

    [Fact]
    public void OfflinePreparedProjectionBytesAndTheirExistingExportLimitAreUnchanged()
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true);
        var compilation = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        var expected = SyntheticSemanticIndexProjection.Export(compilation); Assert.NotNull(expected);
        var cache = new ProjectSemanticIndexLookupCache();
        Assert.Equal(expected, SyntheticSemanticIndexProjection.Export(compilation, lookupCache: cache));
        var independentlyCompiled = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        Assert.NotSame(compilation.Model, independentlyCompiled.Model);
        Assert.NotSame(compilation.Model!.References[0], independentlyCompiled.Model!.References[0]);
        Assert.Equal(expected, SyntheticSemanticIndexProjection.Export(independentlyCompiled, lookupCache: cache)); Assert.Equal(1, cache.LookupReuseCount);
        var index = CompilerSemanticIndex.Build(independentlyCompiled.Dependencies!.Binding, fixture.Input.InputDigest, lookupCache: cache);
        foreach (var symbol in index.Symbols)
            foreach (var reference in index.FindReferences(symbol.Symbol.Location, index.Input.InputDigest).References)
                Assert.Contains(index.References, original => ReferenceEquals(original, reference));
        Assert.Null(SyntheticSemanticIndexProjection.Export(compilation, 1, cache)); Assert.Equal(1, cache.RetainedCount);
    }

    private static CompilerSemanticIndex? Build(CompilerInputManifest input, ProjectSemanticIndexLookupCache cache,
        CompilerMaterializedSnapshot[]? snapshots = null)
    {
        var full = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
        var actual = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
        Assert.Equal(full.Result.Data, actual.Result.Data); Assert.Equal(full.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
        if (full.Model is null) { Assert.Null(actual.Model); return null; }
        return SymbolBindingCacheTests.AssertFullIndexEquivalent(full, actual, cache);
    }
}
