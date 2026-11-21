using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SymbolBindingCacheTests
{
    [Fact]
    public void ExactUnchangedBindingsAreReusedWhileEditedOwnersAndNewContextsAreBoundAgain()
    {
        var basis = ReferenceCompletionTests.CompletionInput(); var cache = new ProjectSymbolBindingCache();
        var first = AssertEquivalent(basis, cache).Model!;
        var oldIndex = new CompilerSemanticInspection(first);
        var oldOrigin = Symbol(first, basis.Source.Context, 10, 10000).Location;
        var oldCursor = oldIndex.CompleteEntityReference(oldOrigin, basis.InputDigest, 1).NextCursor;
        Assert.NotNull(oldCursor);
        var changed = JsonNode.Parse(basis.Source.Declarations.CanonicalText)!.AsArray();
        changed[0]!["payload"]!["ports"]![0]!["type"] = Obj(new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true });
        var edit = Input(source: ProjectSource.Create(basis.Source.Context, basis.Source.Documents, Json(changed)));
        var edited = AssertEquivalent(edit, cache).Model!;
        Assert.NotSame(Symbol(first, basis.Source.Context, 10), Symbol(edited, basis.Source.Context, 10));
        Assert.NotSame(Symbol(first, basis.Source.Context, 10, 10000), Symbol(edited, basis.Source.Context, 10, 10000));
        Assert.Same(Symbol(first, basis.Source.Context, 11), Symbol(edited, basis.Source.Context, 11));
        var index = new CompilerSemanticInspection(edited);
        Assert.Equal(ReferenceCompletionState.Unsupported, index.CompleteEntityReference(oldOrigin, edit.InputDigest).State);
        var restored = AssertEquivalent(basis, cache).Model!;
        Assert.Same(Symbol(first, basis.Source.Context, 10), Symbol(restored, basis.Source.Context, 10));
        var restoredIndex = new CompilerSemanticInspection(restored);
        Assert.Equal(ReferenceCompletionState.InvalidCursor, restoredIndex.CompleteEntityReference(oldOrigin, basis.InputDigest, cursor: oldCursor).State);
        var next = Input(source: ProjectSource.Create(Source(90).Context, basis.Source.Documents, basis.Source.Declarations));
        var rebound = AssertEquivalent(next, cache).Model!;
        Assert.NotSame(Symbol(first, basis.Source.Context, 11), Symbol(rebound, next.Source.Context, 11));
        Assert.All(rebound.Symbols, symbol => Assert.Equal(next.Source.Context, symbol.Context));
        Assert.Equal(basis.Source.Data, first.Input.Source.Data); Assert.Equal(oldIndex.Data, new CompilerSemanticInspection(first).Data);
    }

    [Fact]
    public void PinnedDependencyBindingsSurviveRootRevisionsButOnlyUnchangedDeclarationPartitionsSurviveAnEdit()
    {
        var externalBasis = ReferenceCompletionTests.CompletionInput();
        var external = ProjectSource.Create(Source(9).Context, externalBasis.Source.Documents, externalBasis.Source.Declarations);
        var cache = new ProjectSymbolBindingCache(); var fixture = External(external);
        var first = AssertEquivalent(fixture.Input, cache, [fixture.Snapshot]).Model!;
        Assert.True(first.BindingComplete); Assert.All(first.References, reference => Assert.Equal(ReferenceResolutionState.Resolved, reference.State));
        var original = Symbol(first, external.Context, 12);
        var next = External(external, Source(90).Context);
        var second = AssertEquivalent(next.Input, cache, [next.Snapshot]).Model!;
        Assert.Same(original, Symbol(second, external.Context, 12));
        Assert.NotSame(Symbol(first, fixture.Input.Source.Context, 10), Symbol(second, next.Input.Source.Context, 10));
        var declarations = JsonNode.Parse(external.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["ports"] = new JsonArray();
        declarations[1]!["kind"] = "example.opaque-task";
        var changedSource = ProjectSource.Create(external.Context, external.Documents, Json(declarations));
        var changed = External(changedSource);
        var actual = AssertEquivalent(changed.Input, cache, [changed.Snapshot]).Model!;
        Assert.Same(original, Symbol(actual, external.Context, 12));
        Assert.NotSame(Symbol(first, external.Context, 10), Symbol(actual, external.Context, 10));
        Assert.DoesNotContain(actual.Symbols, symbol => symbol.Context.Equals(external.Context) && symbol.PortId == PortId.Parse(Id(10000)));
        Assert.Contains(actual.References, reference => reference.State == ReferenceResolutionState.MissingWithinCompleteScope);
        Assert.Contains(actual.References, reference => reference.State == ReferenceResolutionState.Unsupported);
        Assert.Equal(ValueDecodeStatus.Unsupported, Symbol(actual, external.Context, 11).Status);
        var restored = AssertEquivalent(fixture.Input, cache, [fixture.Snapshot]).Model!;
        Assert.Same(Symbol(first, external.Context, 10), Symbol(restored, external.Context, 10));
        Assert.Same(original, Symbol(first, external.Context, 12));
        Assert.Equal(external.Data, first.BoundSnapshots.Single().Source.Data);
    }

    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial")]
    [InlineData("wrong-digest")] [InlineData("conflict")]
    public void RetainedDependencySymbolsCannotSupplyAReplacedOrUnadmittedSnapshot(string change)
    {
        var external = Source(9); var fixture = External(external); var cache = new ProjectSymbolBindingCache();
        var first = AssertEquivalent(fixture.Input, cache, [fixture.Snapshot]).Model!;
        Assert.Contains(first.Symbols, symbol => symbol.Context.Equals(external.Context));
        CompilerMaterializedSnapshot Replace(RelationScopeSnapshot snapshot) => new(fixture.Snapshot.SnapshotId, fixture.Snapshot.Version, snapshot);
        var changed = change switch
        {
            "missing" => Array.Empty<CompilerMaterializedSnapshot>(),
            "denied" => [Replace(new(external, false, true, true))],
            "partial" => [Replace(new(external, true, false, false))],
            "wrong-digest" => [Replace(new(ProjectSource.Create(external.Context,
                [SourceDocument.Create(external.Documents[0].DocumentId, "changed", external.Documents[0].Root.Data)]), true, true, true))],
            _ => new[] { fixture.Snapshot, Replace(new(external, false, true, true)) }
        };
        var actual = AssertEquivalent(fixture.Input, cache, changed).Model!;
        if (change == "partial") Assert.False(Assert.Single(actual.BoundSnapshots).DeclarationsComplete);
        else Assert.False(actual.BindingComplete);
        if (change is "missing" or "denied" or "wrong-digest")
            Assert.DoesNotContain(actual.Symbols, symbol => symbol.Context.Equals(external.Context));
        if (change is "missing" or "denied" or "wrong-digest" or "conflict")
            Assert.DoesNotContain(actual.References, reference => reference.State == ReferenceResolutionState.Resolved);
        var restored = AssertEquivalent(fixture.Input, cache, [fixture.Snapshot]).Model!;
        Assert.Same(Symbol(first, external.Context, 6), Symbol(restored, external.Context, 6));
    }

    [Theory]
    [InlineData("rules")] [InlineData("units")] [InlineData("calendars")] [InlineData("observations")]
    [InlineData("descriptors")] [InlineData("source-coverage")] [InlineData("budget")] [InlineData("package")]
    public void EveryGlobalDependencyStillRunsItsOriginalAdmissionAndValidation(string dimension)
    {
        var basis = ReferenceCompletionTests.CompletionInput(); var cache = new ProjectSymbolBindingCache();
        AssertEquivalent(basis, cache);
        var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject(); var source = basis.Source;
        if (dimension == "rules") settings["rules"]![0]!["configuration"] = Obj(new { future = true });
        if (dimension is "units" or "calendars") settings[dimension] = new JsonArray(Artifact("example." + dimension));
        if (dimension == "observations") settings["observations"] = new JsonArray(Obj(new { snapshotId = Id(60), version = "1", context = Source(9).Context.Data.Value,
            digest = Source(9).Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        if (dimension == "descriptors") settings["descriptors"] = new JsonArray();
        if (dimension == "source-coverage") settings["sourceCoverage"] = "coglatas.partial";
        if (dimension == "budget") settings["executionProfile"]!["maximumOperations"] = 1;
        if (dimension == "package") source = ProjectSource.Create(source.Context, source.Documents, source.Declarations, source.Relations,
            packageLock: Json(new JsonArray(Artifact("example.unadopted-package"))));
        var changed = Input(settings, source); Assert.NotEqual(basis.InputDigest, changed.InputDigest);
        AssertEquivalent(changed, cache); AssertEquivalent(basis, cache);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompleteDeclaredScopeAndRequestedExecutionPlansKeepExactPublishedArtifacts(bool plan)
    {
        var fixture = CompilerBindingClosureTests.Graph(withRelation: true, twoBVersions: true);
        var input = plan ? CompilerExecutionPlanTests.Request(fixture.Input) : CompilerScopedPublicationTests.Upgrade(fixture.Input);
        var cache = new ProjectSymbolBindingCache();
        var first = AssertEquivalent(input, cache, fixture.Snapshots);
        var second = AssertEquivalent(input, cache, fixture.Snapshots);
        Assert.NotNull(first.Publication); Assert.NotNull(second.Publication);
        Assert.True(second.Publication.IsDeclaredBy(second.Result));
        Assert.Equal(4, second.Publication.Scope.Regions.Count);
        foreach (var symbol in first.Model!.Symbols)
            Assert.Same(symbol, Assert.Single(second.Model!.Symbols, item => item.Key == symbol.Key));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(256)]
    public void BoundsClearCodecAndDocumentIdentityPreserveFullBinding(int capacity)
    {
        var source = Source(); var input = Input(source: source); var cache = new ProjectSymbolBindingCache(capacity, 4096);
        var first = AssertEquivalent(input, cache).Model!;
        Assert.InRange(cache.RetainedCount, 1, capacity); Assert.InRange(cache.RetainedCanonicalBytes, 1, 4096);
        var initial = Assert.Single(first.Symbols);
        var document = SourceDocument.Create(DocumentId.Parse(Id(91)), "other", source.Documents[0].Root.Data);
        var moved = Input(source: ProjectSource.Create(source.Context, [document]));
        var current = Assert.Single(AssertEquivalent(moved, cache).Model!.Symbols);
        Assert.NotSame(initial, current); Assert.Equal(document.DocumentId, current.Location.DocumentId);
        var limited = ProjectSourceCodec.Decode(source.Data.ToCanonicalBytes(), new SourceCodecLimits(MaximumBytes: 1_000_000)).RequireSource();
        Assert.NotSame(initial, Assert.Single(AssertEquivalent(Input(source: limited), cache).Model!.Symbols));
        cache.Clear(); Assert.Equal(0, cache.RetainedCount); Assert.Equal(0, cache.RetainedCanonicalBytes);
        Assert.NotSame(initial, Assert.Single(AssertEquivalent(input, cache).Model!.Symbols));
        var tiny = new ProjectSymbolBindingCache(capacity, 1); AssertEquivalent(input, tiny);
        Assert.Equal(0, tiny.RetainedCount); Assert.Equal(0, tiny.RetainedCanonicalBytes);
    }

    [Fact]
    public void WarmSymbolPartitionsDoNotTransferFullIndexCursorsAfterAnIdenticalRecompile()
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true);
        var cache = new ProjectSymbolBindingCache();
        var first = AssertEquivalent(fixture.Input, cache, fixture.Snapshots);
        var second = AssertEquivalent(fixture.Input, cache, fixture.Snapshots);
        var original = CompilerSemanticIndex.Build(first.Dependencies!.Binding, fixture.Input.InputDigest);
        var current = CompilerSemanticIndex.Build(second.Dependencies!.Binding, fixture.Input.InputDigest);
        Assert.Equal(CompilerSemanticIndexState.Complete, current.State); Assert.Equal(3, current.Regions.Count);
        Assert.Equal(original.Data, current.Data);
        foreach (var symbol in first.Model!.Symbols)
            Assert.Same(symbol, Assert.Single(second.Model!.Symbols, item => item.Key == symbol.Key));
        var origin = Assert.Single(original.Symbols, item => item.Symbol.Context.Equals(fixture.Input.Source.Context) &&
            item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8))).Symbol.Location;
        var completion = original.CompleteEntityReference(origin, fixture.Input.InputDigest, 1);
        Assert.NotNull(completion.NextCursor);
        var refusedCompletion = current.CompleteEntityReference(origin, fixture.Input.InputDigest, 1, completion.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, refusedCompletion.State);
        Assert.Empty(refusedCompletion.Candidates); Assert.Null(refusedCompletion.InputDigest); Assert.Null(refusedCompletion.Origin);
        Assert.False(refusedCompletion.IndexCoverageComplete); Assert.Equal(0, refusedCompletion.ScannedSymbols); Assert.Null(refusedCompletion.NextCursor);
        var references = original.Symbols.Select(item => original.FindReferences(item.Symbol.Location, fixture.Input.InputDigest, 1))
            .First(page => page.NextCursor is not null);
        var refusedReferences = current.FindReferences(references.Target!, fixture.Input.InputDigest, 1, references.NextCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, refusedReferences.State);
        Assert.Empty(refusedReferences.References); Assert.Null(refusedReferences.InputDigest); Assert.Null(refusedReferences.Target);
        Assert.False(refusedReferences.IndexCoverageComplete); Assert.Equal(0, refusedReferences.ScannedReferences); Assert.Null(refusedReferences.NextCursor);
        Assert.Equal(original.Data, current.Data); Assert.Equal(first.Result.Data, second.Result.Data);
    }

    [Theory]
    [InlineData("tenant")] [InlineData("project")] [InlineData("branch")]
    public void ChangingRootOwnershipClearsEvenAnIdenticalRestoredInput(string dimension)
    {
        var source = Source(); var cache = new ProjectSymbolBindingCache(); var input = Input(source: source);
        var first = Assert.Single(AssertEquivalent(input, cache).Model!.Symbols);
        var branch = source.Context.Branch;
        var context = SourceRevisionContext.Committed(new(new(dimension == "tenant" ? TenantId.Parse(Id(90)) : branch.TenantId,
            dimension == "project" ? ProjectId.Parse(Id(90)) : branch.ProjectId,
            dimension == "branch" ? BranchId.Parse(Id(90)) : branch.BranchId), RevisionId.Parse(Id(5))));
        var foreign = Input(source: ProjectSource.Create(context, source.Documents));
        AssertEquivalent(foreign, cache);
        Assert.NotSame(first, Assert.Single(AssertEquivalent(input, cache).Model!.Symbols));
    }

    [Fact]
    public async Task ConcurrentLocalHostGenerationsAndCancellationPreserveExactCompilationReceipts()
    {
        var inputs = Enumerable.Range(0, 12).Select(index => Input(source: Source(100 + index))).ToArray();
        var basis = inputs[0]; CompilerArtifactIdentity At(string name) => new(SourceJson.Parse(basis.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(basis.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            basis.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var cache = new ProjectSymbolBindingCache(); var host = new LocalNetCompilerHost(profile, symbolBindingCache: cache);
        var requests = inputs.Select((input, index) => new CompilerHostRequest(new(input), index)).ToArray();
        var results = await Task.WhenAll(requests.Select((request, index) => { if (index % 3 == 0) cache.Clear(); return host.CompileAsync(request); }));
        for (var index = 0; index < inputs.Length; index++)
        {
            Assert.Equal(CompilerHostStatus.Completed, results[index].Status); Assert.Equal(index, results[index].ClientGeneration);
            Assert.Equal(PureCoreCompiler.Compile(inputs[index], inputs[index].Compiler).Result.Data, results[index].Compilation!.Result.Data);
            Assert.All(results[index].Compilation!.Model!.Symbols, symbol => Assert.Equal(inputs[index].Source.Context, symbol.Context));
        }
        var count = cache.RetainedCount; using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(CompilerHostStatus.Cancelled, (await host.CompileAsync(requests[0], cancelled.Token)).Status);
        Assert.Equal(count, cache.RetainedCount);
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(basis, cancellationToken: cancelled.Token, symbolBindingCache: cache));
        Assert.Equal(count, cache.RetainedCount);
    }

    [Fact]
    public async Task ConcurrentColdReadersReuseThePublishedBindingForEachDeclaration()
    {
        var source = ReferenceCompletionTests.CompletionInput(64).Source;
        var cache = new ProjectSymbolBindingCache();
        var scope = cache.BeginBinding(source.Context.Branch, CancellationToken.None);
        const int readerCount = 8;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = readerCount;
        var readers = Enumerable.Range(0, readerCount).Select(_ => Task.Run(async () =>
        {
            if (Interlocked.Decrement(ref remaining) == 0) start.SetResult();
            await start.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return cache.Read(source, scope, CancellationToken.None);
        })).ToArray();

        var results = await Task.WhenAll(readers);
        var retained = cache.Read(source, scope, CancellationToken.None);
        Assert.Equal(retained.Count, cache.RetainedCount);
        foreach (var result in results)
        {
            Assert.Equal(retained.Count, result.Count);
            for (var index = 0; index < retained.Count; index++)
                Assert.Same(retained[index], result[index]);
        }
    }

    [Fact]
    public void TenantBoundResourceStatusAndPrivateCollectionStorageCannotPoisonRetainedBindings()
    {
        var source = Source(); var resource = Obj(new { entityId = Id(70), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1,
            payload = new { tenantId = Id(1), resourceId = Id(71), snapshotVersion = "1", snapshotDigest = new { algorithm = "sha-256", value = new string('a', 64) }, resourceKind = "coglatas.equipment" } });
        var local = Input(source: ProjectSource.Create(source.Context, source.Documents, Json(new JsonArray(resource))));
        var cache = new ProjectSymbolBindingCache(); var model = AssertEquivalent(local, cache).Model!;
        var symbol = Symbol(model, source.Context, 70); Assert.Equal(ValueDecodeStatus.Decoded, symbol.Status);
        Assert.False(model.Symbols is System.Collections.ICollection);
        Assert.Throws<NotSupportedException>(() => ((IList<CompilerSymbol>)model.Symbols).Clear());
        var bytes = symbol.Location.Data.ToCanonicalBytes(); bytes[0] = (byte)'!';
        var copy = symbol with { Kind = "example.injected" }; Assert.NotEqual(symbol.Kind, copy.Kind);
        var retained = AssertEquivalent(local, cache).Model!; Assert.Same(symbol, Symbol(retained, source.Context, 70));
        var foreignContext = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(90)), source.ProjectId, source.Context.Branch.BranchId), RevisionId.Parse(Id(5))));
        var foreign = Input(source: ProjectSource.Create(foreignContext, source.Documents, local.Source.Declarations));
        var rejected = AssertEquivalent(foreign, cache).Model!;
        Assert.Equal(ValueDecodeStatus.Invalid, Symbol(rejected, foreignContext, 70).Status);
        Assert.Null(rejected.Declarations.Single(item => item.Data.Value.GetProperty("entityId").GetString() == Id(70)).Declaration);
        Assert.NotSame(symbol, Symbol(AssertEquivalent(local, cache).Model!, source.Context, 70));
    }

    [Fact]
    public void AProjectRootReferenceResolvesToTheNewRevisionWithUnchangedDeclarationData()
    {
        var original = ReferenceCompletionTests.CompletionInput(1);
        var root = JsonNode.Parse(original.Source.Documents[0].Root.Data.CanonicalText)!;
        root["payload"]!["roots"] = new JsonArray(Id(10));
        var document = SourceDocument.Create(original.Source.Documents[0].DocumentId, "root", Json(root));
        var basis = Input(source: ProjectSource.Create(original.Source.Context, [document], original.Source.Declarations));
        var cache = new ProjectSymbolBindingCache(); var first = AssertEquivalent(basis, cache).Model!;
        Assert.Equal(ReferenceResolutionState.Resolved, Assert.Single(first.References).State);
        var next = Input(source: ProjectSource.Create(Source(90).Context, [document], original.Source.Declarations));
        var second = AssertEquivalent(next, cache).Model!;
        Assert.True(second.BindingComplete);
        var reference = Assert.Single(second.References); Assert.Equal(ReferenceResolutionState.Resolved, reference.State);
        Assert.Equal(next.Source.Context, reference.Origin.Context);
        Assert.Equal(Symbol(second, next.Source.Context, 10).Key, reference.SymbolKey);
        Assert.NotSame(Symbol(first, basis.Source.Context, 10), Symbol(second, next.Source.Context, 10));
        Assert.Equal(basis.Source.Data, first.Input.Source.Data);
    }

    [Fact]
    public async Task OverlappingBindersWithDifferentOwnersKeepTheirCompleteOriginalModels()
    {
        var first = ReferenceCompletionTests.CompletionInput(8); var branch = first.Source.Context.Branch;
        var context = SourceRevisionContext.Committed(new(new(branch.TenantId, branch.ProjectId, BranchId.Parse(Id(90))), RevisionId.Parse(Id(5))));
        var second = Input(source: ProjectSource.Create(context, first.Source.Documents, first.Source.Declarations));
        var cache = new ProjectSymbolBindingCache(2); using var overlap = new Barrier(2); var entered = 0;
        IEnumerable<CompilerMaterializedSnapshot?> Supply()
        {
            Interlocked.Increment(ref entered);
            if (!overlap.SignalAndWait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Both actual Binder suppliers must overlap.");
            yield break;
        }
        Task<PartialSemanticModel> Bind(CompilerInputManifest input) => Task.Factory.StartNew(
            () => ProjectSemanticBinder.Bind(input, Supply(), symbolBindingCache: cache),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var models = await Task.WhenAll(Bind(first), Bind(second)); Assert.Equal(2, entered);
        for (var index = 0; index < models.Length; index++)
        {
            var input = index == 0 ? first : second; var expected = ProjectSemanticBinder.Bind(input);
            Assert.True(models[index].BindingComplete);
            Assert.Equal(new CompilerSemanticInspection(expected).Data, new CompilerSemanticInspection(models[index]).Data);
            Assert.All(models[index].Symbols, symbol => Assert.Equal(input.Source.Context, symbol.Context));
            Assert.Equal(input.Source.Data, models[index].Input.Source.Data);
        }
        Assert.InRange(cache.RetainedCount, 1, 2);
    }

    [Fact]
    public void ColdAndWarmSymbolPartitionsPreserveInteriorBindingBudgetExhaustionBoundaries()
    {
        var original = ReferenceCompletionTests.CompletionInput();
        var declarations = JsonNode.Parse(original.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        var ports = new JsonArray();
        for (var index = 0; index < 6; index++) { var copy = port.DeepClone(); copy["portId"] = Id(10000 + index); ports.Add(copy); }
        declarations[0]!["payload"]!["ports"] = ports;
        var basis = Input(source: ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(declarations)));
        var complete = ProjectSemanticBinder.Bind(basis); Assert.True(complete.BindingComplete);
        var endpoint = complete.LogicalOperationsConsumed; Assert.True(endpoint > 12);
        var warm = new ProjectSymbolBindingCache(); AssertEquivalent(basis, warm);
        var beforeSymbols = false; var insideBinding = false; var completed = false;
        foreach (var limit in new[] { 1 }.Concat(Enumerable.Range(endpoint - 12, 16)))
        {
            var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = limit;
            var input = Input(settings, basis.Source);
            var cold = AssertEquivalent(input, new ProjectSymbolBindingCache()).Model!;
            var current = AssertEquivalent(input, warm).Model!;
            Assert.Equal(cold.LogicalOperationsConsumed, current.LogicalOperationsConsumed);
            if (current.Findings.Any(finding => finding.Diagnostic.Code == "coglatas.binding.budget-exhausted"))
            {
                if (current.Symbols.Count == 0) beforeSymbols = true;
                else { insideBinding = true; Assert.Empty(current.Declarations); }
            }
            else { completed = true; Assert.True(current.BindingComplete); }
        }
        Assert.True(beforeSymbols); Assert.True(insideBinding); Assert.True(completed);
        AssertEquivalent(basis, warm);
    }

    internal static (CompilerInputManifest Input, CompilerMaterializedSnapshot Snapshot) External(ProjectSource external, SourceRevisionContext? context = null)
    {
        var basis = ReferenceCompletionTests.CompletionInput(1); context ??= basis.Source.Context;
        var declarations = JsonNode.Parse(basis.Source.Declarations.CanonicalText)!.AsArray();
        var target = external.Declarations.Value.GetArrayLength() == 0 ? 6 : 10;
        var kind = target == 6 ? "coglatas.project" : "coglatas.task";
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        port["type"] = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = kind });
        port["defaultValue"] = Obj(new { type = port["type"]!.DeepClone(), state = "coglatas.known", value = new {
            tenantId = external.TenantId.ToString(), projectId = external.ProjectId.ToString(), entityId = Id(target), context = external.Context.Data.Value,
            origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), context, entityId: EntityId.Parse(Id(10))).Data.Value,
            manifest = new { version = "1", digest = external.Digest.Data.Value } } });
        if (target == 10)
        {
            declarations.Add(Obj(new { entityId = Id(70), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
                payload = new { assertions = new[] { new { key = "example.dependency", kind = "coglatas.assert-reference-resolved",
                    target = CompilerAnalysisFixtures.Reference(10, external.Context), portId = Id(10000) } } } }));
            declarations.Add(Obj(new { entityId = Id(71), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1,
                payload = new { target = CompilerAnalysisFixtures.Reference(11, external.Context), condition = PlanDeclarationTests.Literal(true), evidenceKind = "coglatas.model-proof" } }));
        }
        var settings = Settings(); settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(60), version = "1", context = external.Context.Data.Value,
            digest = external.Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        var input = Input(settings, ProjectSource.Create(context, basis.Source.Documents, Json(declarations)));
        return (input, new(ExternalSnapshotId.Parse(Id(60)), "1", new(external, true, true, true)));
    }

    private static CompilerSymbol Symbol(PartialSemanticModel model, SourceRevisionContext context, int entity, int? port = null) =>
        Assert.Single(model.Symbols, symbol => symbol.Context.Equals(context) && symbol.EntityId == EntityId.Parse(Id(entity)) &&
            symbol.PortId == (port is null ? null : PortId.Parse(Id(port.Value))));

    internal static CoreCompilation AssertEquivalent(CompilerInputManifest input, ProjectSymbolBindingCache cache, CompilerMaterializedSnapshot[]? snapshots = null,
        ProjectReferenceBindingCache? referenceBindingCache = null)
    {
        var full = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
        var actual = PureCoreCompiler.Compile(input, input.Compiler, snapshots, symbolBindingCache: cache, referenceBindingCache: referenceBindingCache);
        Assert.Equal(full.Result.Data, actual.Result.Data); Assert.Equal(full.Result.ResultDigest, actual.Result.ResultDigest);
        Assert.Equal(full.LogicalOperationsConsumed, actual.LogicalOperationsConsumed);
        Assert.Equal(full.Dependencies?.Data, actual.Dependencies?.Data);
        Assert.Equal(full.Publication?.Scope.Data, actual.Publication?.Scope.Data);
        Assert.Equal(full.Publication?.SemanticModel, actual.Publication?.SemanticModel);
        Assert.Equal(full.Publication?.SemanticModelDigest, actual.Publication?.SemanticModelDigest);
        Assert.Equal(full.Publication?.Ir, actual.Publication?.Ir); Assert.Equal(full.Publication?.IrDigest, actual.Publication?.IrDigest);
        Assert.Equal(full.Publication?.SourceMap, actual.Publication?.SourceMap);
        Assert.Equal(full.Publication?.SourceMapDigest, actual.Publication?.SourceMapDigest);
        Assert.Equal(full.Publication?.ExecutionPlan?.Data, actual.Publication?.ExecutionPlan?.Data);
        Assert.Equal(full.Publication?.PlanNotProducedReason, actual.Publication?.PlanNotProducedReason);
        Assert.Equal(full.Publication?.LogicalOperationsConsumed, actual.Publication?.LogicalOperationsConsumed);
        Assert.Equal(full.Validation?.Ir?.Data, actual.Validation?.Ir?.Data);
        Assert.Equal(full.Validation?.Ir?.SemanticModel, actual.Validation?.Ir?.SemanticModel);
        Assert.Equal(full.Validation?.Ir?.SourceMap, actual.Validation?.Ir?.SourceMap);
        if (full.Model is { } expected)
        {
            var model = actual.Model!; Assert.Equal(expected.BindingComplete, model.BindingComplete);
            Assert.Equal(expected.LogicalOperationsConsumed, model.LogicalOperationsConsumed);
            Assert.Equal(expected.Symbols.Select(symbol => symbol.Key + symbol.Kind + symbol.Status + symbol.Location.Data.CanonicalText),
                model.Symbols.Select(symbol => symbol.Key + symbol.Kind + symbol.Status + symbol.Location.Data.CanonicalText));
            Assert.Equal(expected.Declarations.Select(item => item.Status + item.DocumentId.ToString() + item.Data.CanonicalText),
                model.Declarations.Select(item => item.Status + item.DocumentId.ToString() + item.Data.CanonicalText));
            Assert.Equal(expected.References.Select(item => item.Origin.Data.CanonicalText + item.State + item.SymbolKey),
                model.References.Select(item => item.Origin.Data.CanonicalText + item.State + item.SymbolKey));
            Assert.Equal(expected.Findings.Select(item => item.State + item.Diagnostic.Data.CanonicalText),
                model.Findings.Select(item => item.State + item.Diagnostic.Data.CanonicalText));
            Assert.Equal(new CompilerSemanticInspection(expected).Data, new CompilerSemanticInspection(model).Data);
            AssertFullIndexEquivalent(full, actual);
        }
        else Assert.Null(actual.Model);
        return actual;
    }

    internal static CompilerSemanticIndex AssertFullIndexEquivalent(CoreCompilation full, CoreCompilation actual, ProjectSemanticIndexLookupCache? lookupCache = null)
    {
        var input = full.Model!.Input;
        var expected = CompilerSemanticIndex.Build(full.Dependencies?.Binding ?? CompilerBindingClosure.Build(full.Model, input.InputDigest), input.InputDigest);
        var current = CompilerSemanticIndex.Build(actual.Dependencies?.Binding ?? CompilerBindingClosure.Build(actual.Model!, input.InputDigest), input.InputDigest, lookupCache: lookupCache);
        var originalResult = actual.Result.Data; var originalSource = input.Source.Data;
        Assert.Equal(expected.Data, current.Data); Assert.Equal(expected.Digest, current.Digest);
        Assert.Equal(expected.LogicalOperationsConsumed, current.LogicalOperationsConsumed);
        foreach (var reference in expected.References)
        {
            var definition = expected.FindDefinition(reference.Origin, input.InputDigest);
            var found = current.FindDefinition(reference.Origin, input.InputDigest);
            Assert.Equal(definition.State, found.State); Assert.Equal(definition.Resolution, found.Resolution);
            Assert.Equal(definition.Target?.Symbol.Key, found.Target?.Symbol.Key);
            Assert.Equal(definition.Target?.Symbol.Location.Data, found.Target?.Symbol.Location.Data);
        }
        foreach (var symbol in expected.Symbols)
        {
            var location = symbol.Symbol.Location;
            Assert.Equal(expected.Inspect(location, input.InputDigest)?.Symbol.Key, current.Inspect(location, input.InputDigest)?.Symbol.Key);
            CompilerIndexCursor? expectedCursor = null, currentCursor = null;
            var pages = 0;
            do
            {
                Assert.InRange(++pages, 1, expected.References.Count + 1);
                var page = expected.FindReferences(location, input.InputDigest, 1, expectedCursor);
                var found = current.FindReferences(location, input.InputDigest, 1, currentCursor);
                Assert.Equal(page.State, found.State); Assert.Equal(page.InputDigest, found.InputDigest);
                Assert.Equal(page.Target?.Data, found.Target?.Data); Assert.Equal(page.IndexCoverageComplete, found.IndexCoverageComplete);
                Assert.Equal(page.ScannedReferences, found.ScannedReferences); Assert.Equal(page.ScanComplete, found.ScanComplete);
                Assert.Equal(page.References.Select(row => (row.Origin.Data, row.State, row.SymbolKey)),
                    found.References.Select(row => (row.Origin.Data, row.State, row.SymbolKey)));
                Assert.Equal(page.NextCursor is null, found.NextCursor is null);
                expectedCursor = page.NextCursor; currentCursor = found.NextCursor;
            } while (expectedCursor is not null);
            expectedCursor = null; currentCursor = null; pages = 0;
            do
            {
                Assert.InRange(++pages, 1, expected.Symbols.Count + 1);
                var page = expected.CompleteEntityReference(location, input.InputDigest, 1, expectedCursor);
                var found = current.CompleteEntityReference(location, input.InputDigest, 1, currentCursor);
                Assert.Equal(page.State, found.State); Assert.Equal(page.InputDigest, found.InputDigest);
                Assert.Equal(page.Origin?.Data, found.Origin?.Data); Assert.Equal(page.ExpectedKind, found.ExpectedKind);
                Assert.Equal(page.IndexCoverageComplete, found.IndexCoverageComplete);
                Assert.Equal(page.ScannedSymbols, found.ScannedSymbols); Assert.Equal(page.ScanComplete, found.ScanComplete);
                Assert.Equal(page.Candidates.Select(row => row.Symbol.Key), found.Candidates.Select(row => row.Symbol.Key));
                Assert.Equal(page.NextCursor is null, found.NextCursor is null);
                expectedCursor = page.NextCursor; currentCursor = found.NextCursor;
            } while (expectedCursor is not null);
        }
        Assert.Equal(originalResult, actual.Result.Data); Assert.Equal(originalSource, input.Source.Data);
        Assert.Equal(expected.Data, current.Data); Assert.Equal(expected.Digest, current.Digest);
        return current;
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("owner")]
    public void SymbolLifetimeInvalidationDuringSupplyCannotRestoreTheEarlierOwner(string invalidation)
    {
        var fixture = External(Source(9)); var cache = new ProjectSymbolBindingCache();
        (int Count, int Bytes, PartialSemanticModel? Foreign, CompilerInputManifest? Input) state = default;
        IEnumerable<CompilerMaterializedSnapshot> Supply()
        {
            state = InvalidateSymbolLifetime(cache, fixture.Input, invalidation);
            yield return fixture.Snapshot;
        }
        var actual = ProjectSemanticBinder.Bind(fixture.Input, Supply(), symbolBindingCache: cache);
        AssertSymbolLifetime(cache, state);
        var expected = ProjectSemanticBinder.Bind(fixture.Input, [fixture.Snapshot]);
        Assert.Equal(CompilerBindingClosure.Build(expected, fixture.Input.InputDigest).Data,
            CompilerBindingClosure.Build(actual, fixture.Input.InputDigest).Data);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("owner")]
    public void SymbolLifetimeRegionalContinuationCannotAcquireANewerEpoch(string invalidation)
    {
        var fixture = CompilerBindingClosureTests.Graph(withRelation: true); var cache = new ProjectSymbolBindingCache();
        var root = ProjectSemanticBinder.Bind(fixture.Input, fixture.Snapshots, symbolBindingCache: cache);
        Assert.True(cache.RetainedCount > 0);
        var state = InvalidateSymbolLifetime(cache, fixture.Input, invalidation);
        var actual = CompilerBindingClosure.Build(root, fixture.Input.InputDigest, symbolBindingCache: cache);
        AssertSymbolLifetime(cache, state);
        Assert.Equal(CompilerBindingClosure.Build(root, fixture.Input.InputDigest).Data, actual.Data);
        var fresh = ProjectSemanticBinder.Bind(fixture.Input, fixture.Snapshots, symbolBindingCache: cache);
        Assert.Equal(actual.Data, CompilerBindingClosure.Build(fresh, fixture.Input.InputDigest, symbolBindingCache: cache).Data);
        Assert.True(cache.RetainedCount > 0);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("owner")]
    public void SymbolLifetimeFullCompilationPreservesTheInvalidatedRootEpoch(string invalidation)
    {
        var fixture = CompilerBindingClosureTests.Graph(withRelation: true); var cache = new ProjectSymbolBindingCache();
        (int Count, int Bytes, PartialSemanticModel? Foreign, CompilerInputManifest? Input) state = default;
        var observed = false;
        IEnumerable<UnitDefinition> Units()
        {
            Assert.True(cache.RetainedCount > 0); observed = true;
            state = InvalidateSymbolLifetime(cache, fixture.Input, invalidation);
            yield break;
        }
        var actual = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, Units(), symbolBindingCache: cache);
        Assert.True(observed); AssertSymbolLifetime(cache, state);
        var expected = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        Assert.Equal(expected.Result.Data, actual.Result.Data); Assert.Equal(expected.Dependencies!.Data, actual.Dependencies!.Data);
        Assert.Equal(expected.LogicalOperationsConsumed, actual.LogicalOperationsConsumed); AssertFullIndexEquivalent(expected, actual);
    }

    [Fact]
    public void SymbolLifetimeDifferentSessionCanIndependentlyBindAnEarlierImmutableRoot()
    {
        var fixture = CompilerBindingClosureTests.Graph(withRelation: true);
        var first = new ProjectSymbolBindingCache(); var second = new ProjectSymbolBindingCache();
        var root = ProjectSemanticBinder.Bind(fixture.Input, fixture.Snapshots, symbolBindingCache: first); first.Clear();
        var actual = CompilerBindingClosure.Build(root, fixture.Input.InputDigest, symbolBindingCache: second);
        Assert.Equal(0, first.RetainedCount); Assert.True(second.RetainedCount > 0);
        Assert.Equal(CompilerBindingClosure.Build(root, fixture.Input.InputDigest).Data, actual.Data);
    }

    private static (int Count, int Bytes, PartialSemanticModel? Foreign, CompilerInputManifest? Input) InvalidateSymbolLifetime(
        ProjectSymbolBindingCache cache, CompilerInputManifest input, string invalidation)
    {
        if (invalidation == "clear") { cache.Clear(); return (0, 0, null, null); }
        var owner = input.Source.Context.Branch;
        var context = SourceRevisionContext.Committed(new(new(owner.TenantId, owner.ProjectId, BranchId.Parse(Id(99))), RevisionId.Parse(Id(5))));
        var foreignInput = Input(source: ProjectSource.Create(context, Source().Documents));
        var foreign = ProjectSemanticBinder.Bind(foreignInput, symbolBindingCache: cache);
        Assert.True(cache.RetainedCount > 0);
        return (cache.RetainedCount, cache.RetainedCanonicalBytes, foreign, foreignInput);
    }

    private static void AssertSymbolLifetime(ProjectSymbolBindingCache cache,
        (int Count, int Bytes, PartialSemanticModel? Foreign, CompilerInputManifest? Input) state)
    {
        Assert.Equal(state.Count, cache.RetainedCount); Assert.Equal(state.Bytes, cache.RetainedCanonicalBytes);
        if (state.Foreign is not { } foreign) return;
        var current = ProjectSemanticBinder.Bind(state.Input!, symbolBindingCache: cache);
        Assert.All(foreign.Symbols, symbol => Assert.Contains(current.Symbols, row => ReferenceEquals(symbol, row)));
    }
}
