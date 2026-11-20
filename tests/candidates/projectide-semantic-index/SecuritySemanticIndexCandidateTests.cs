using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent fixed-input Core checks, not runtime authorization or client admission.</summary>
public sealed class SecuritySemanticIndexCandidateTests
{
    private static readonly TenantId Tenant = new(Id(1));
    private static readonly BranchRef Branch = new(Tenant, new(Id(2)), new(Id(3)));
    private static readonly BranchRef DependencyBranch = new(Tenant, new(Id(4)), new(Id(5)));
    private static readonly DocumentId Document = new(Id(6));
    private const string SnapshotVersion = "independent-index-1";

    [Fact]
    public void FullIndexRetainsActualRegionalSourcesAndOriginalCompilerIdentity()
    {
        var fixture = Regional();
        var read = Compile(fixture);
        var originalResult = read.Compilation.Result.Data;
        var index = read.Index;
        Assert.Equal(3, index.Regions.Count);
        Assert.Equal(14, index.Symbols.Count);
        Assert.Equal(8, index.References.Count);
        Assert.Same(fixture.Input, index.Input);
        Assert.Equal(read.Binding.Digest, index.BindingClosureDigest);
        Assert.True(index.IncludesDeclaredSnapshots);
        Assert.True(index.IndexCoverageComplete);
        Assert.All(index.Regions, region => Assert.Same(fixture.Input, region.RootInput));
        foreach (var source in fixture.Dependencies.Prepend(fixture.Input.Source))
        {
            var region = Assert.Single(index.Regions, item => item.Source.Context.Equals(source.Context));
            Assert.Equal(source.Data, region.Source.Data);
            Assert.Equal(source.Digest, region.Source.Digest);
        }
        Assert.Equal(index.Digest, ContentDigest.Compute(CompilerSemanticIndex.Format, index.Data.ToCanonicalBytes()));
        Assert.Equal(originalResult, read.Compilation.Result.Data);
        Assert.InRange(index.LogicalOperationsConsumed, 1, fixture.Input.ExecutionProfile.MaximumOperations);
    }

    [Fact]
    public void DefinitionsUseExactRootAndDependencyOwnedRelationOrigins()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        foreach (var source in fixture.Dependencies.Prepend(fixture.Input.Source))
        foreach (var relation in TypedRelationReader.Read(source))
        {
            var typed = Assert.IsType<TypedRelation>(relation.Relation);
            foreach (var endpoint in new[] { ("/source", typed.Source), ("/target", typed.Target) })
            {
                var origin = ProjectLocation.Create(Document, source.Context, endpoint.Item1, relationId: typed.RelationId);
                var definition = index.FindDefinition(origin, fixture.Input.InputDigest);
                Assert.Equal(CompilerIndexQueryState.Ready, definition.State);
                Assert.Equal(ReferenceResolutionState.Resolved, definition.Resolution);
                var target = Assert.IsType<CompilerIndexedSymbol>(definition.Target);
                Assert.Equal(endpoint.Item2.Reference.Context, target.Symbol.Context);
                Assert.Equal(endpoint.Item2.Reference.EntityId, target.Symbol.EntityId);
                Assert.Contains(index.Symbols, item => ReferenceEquals(item, target));
            }
        }
    }

    [Fact]
    public void EqualEntityIdsInTwoPinnedRevisionsRemainDifferentDefinitions()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        var targets = fixture.Dependencies.Select(source => Symbol(index, source.Context, 20)).ToArray();
        Assert.Equal(targets[0].Symbol.EntityId, targets[1].Symbol.EntityId);
        Assert.NotEqual(targets[0].Symbol.Key, targets[1].Symbol.Key);
        Assert.NotEqual(targets[0].Symbol.Context, targets[1].Symbol.Context);
        foreach (var target in targets)
        {
            var page = index.FindReferences(target.Symbol.Location, fixture.Input.InputDigest);
            Assert.Equal(2, page.References.Count);
            Assert.All(page.References, reference => Assert.Equal(target.Symbol.Key, reference.SymbolKey));
            Assert.Contains(page.References, reference => reference.Origin.Context.Equals(target.Symbol.Context));
            Assert.Contains(page.References, reference => reference.Origin.Context.Equals(fixture.Input.Source.Context));
        }
    }

    [Fact]
    public void DeclaredUnusedSnapshotsEnterOnlyTheExplicitFullClosure()
    {
        var fixture = Regional(linkDependencies: false);
        var read = Compile(fixture);
        Assert.Equal(3, read.Index.Regions.Count);
        Assert.Contains(read.Index.Symbols, item => item.DisplayName == "private-dependency-200-task-20");
        var reachable = CompilerBindingClosure.Build(Assert.IsType<PartialSemanticModel>(read.Compilation.Model), fixture.Input.InputDigest);
        var reachableIndex = CompilerSemanticIndex.Build(reachable, fixture.Input.InputDigest);
        Assert.Single(reachableIndex.Regions);
        Assert.False(reachableIndex.IncludesDeclaredSnapshots);
        Assert.False(reachableIndex.IndexCoverageComplete);
        Assert.Equal(CompilerSemanticIndexState.Incomplete, reachableIndex.State);
        Assert.DoesNotContain(reachableIndex.Symbols, item => item.Symbol.Context.Equals(fixture.Dependencies[0].Context));
        var page = reachableIndex.CompleteEntityReference(Port(reachableIndex, fixture.Input.Source.Context, 10).Symbol.Location, fixture.Input.InputDigest);
        Assert.True(page.ScanComplete);
        Assert.False(page.IndexCoverageComplete);
    }

    [Fact]
    public void MissingDeniedAndConflictingMaterializationsCannotSupplyPrivateIndexRows()
    {
        var fixture = Regional();
        Assert.Equal(3, Compile(fixture).Index.Regions.Count);
        var first = fixture.Supply[0];
        var denied = new CompilerMaterializedSnapshot(first.SnapshotId, first.Version, first.Snapshot with { IsAuthorized = false });
        foreach (var supplied in new[]
        {
            fixture.Supply.Skip(1).ToArray(),
            new[] { denied, fixture.Supply[1] },
            new[] { first, denied, fixture.Supply[1] },
            new[] { first, new CompilerMaterializedSnapshot(first.SnapshotId, "conflicting-version", first.Snapshot), fixture.Supply[1] }
        })
        {
            var index = Compile(fixture with { Supply = supplied }, complete: false).Index;
            AssertHidden(index, fixture.Dependencies[0]);
            Assert.False(index.IndexCoverageComplete);
        }
        Assert.Equal(3, Compile(fixture).Index.Regions.Count);
    }

    [Fact]
    public void WrongVersionAndSameContextBodySubstitutionCannotBorrowAnInputPin()
    {
        var fixture = Regional();
        var first = fixture.Supply[0];
        var source = first.Snapshot.Source;
        var changed = ProjectSource.Create(source.Context, source.Documents,
            Json(new[] { TaskDeclaration(20, "substituted-private-name"), TaskDeclaration(21, "replacement") }), source.Relations);
        foreach (var substitute in new[]
        {
            new CompilerMaterializedSnapshot(first.SnapshotId, "wrong-version", first.Snapshot),
            new CompilerMaterializedSnapshot(first.SnapshotId, first.Version, new(changed, true, true, true))
        })
        {
            var index = Compile(fixture with { Supply = [substitute, fixture.Supply[1]] }, complete: false).Index;
            AssertHidden(index, source);
            Assert.DoesNotContain("substituted-private-name", index.Data.CanonicalText);
        }
        Assert.Equal(3, Compile(fixture).Index.Regions.Count);
    }

    [Fact]
    public void PartialCapturedCoverageCannotBecomeACompleteIndexOrPage()
    {
        var fixture = Regional();
        var first = fixture.Supply[0];
        var partial = new CompilerMaterializedSnapshot(first.SnapshotId, first.Version,
            first.Snapshot with { DeclarationsComplete = false, RelationsComplete = false });
        var index = Compile(fixture with { Supply = [partial, fixture.Supply[1]] }, complete: false).Index;
        Assert.Equal(CompilerSemanticIndexState.Incomplete, index.State);
        Assert.False(index.IndexCoverageComplete);
        var port = Port(index, fixture.Input.Source.Context, 10);
        var page = index.CompleteEntityReference(port.Symbol.Location, fixture.Input.InputDigest);
        Assert.Equal(CompilerIndexQueryState.Ready, page.State);
        Assert.False(page.IndexCoverageComplete);
        Assert.True(page.ScanComplete);
    }

    [Fact]
    public void StaleBuildContainsNoRegionsSymbolsOrReferences()
    {
        var read = Compile(Regional());
        var index = CompilerSemanticIndex.Build(read.Binding, Digest("other-input"));
        Assert.Equal(CompilerSemanticIndexState.StaleInput, index.State);
        Assert.Empty(index.Regions);
        Assert.Empty(index.Symbols);
        Assert.Empty(index.References);
        Assert.False(index.IndexCoverageComplete);
    }

    [Fact]
    public void StaleQueryInputRefusesCandidatesTargetsCountsAndContinuation()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        var stale = Digest("stale-query");
        var target = Symbol(index, fixture.Dependencies[0].Context, 20);
        Assert.Null(index.Inspect(target.Symbol.Location, stale));
        AssertRefused(index.FindReferences(target.Symbol.Location, stale), CompilerIndexQueryState.StaleInput);
        AssertRefused(index.CompleteEntityReference(Port(index, fixture.Input.Source.Context, 10).Symbol.Location, stale), CompilerIndexQueryState.StaleInput);
        var definition = index.FindDefinition(index.References[0].Origin, stale);
        Assert.Equal(CompilerIndexQueryState.StaleInput, definition.State);
        Assert.Null(definition.Target);
        Assert.Null(definition.Resolution);
    }

    [Fact]
    public void ForeignTenantProjectBranchAndRevisionLocationsStayOutOfScope()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        var actual = Symbol(index, fixture.Input.Source.Context, 10).Symbol.Location;
        foreach (var context in new[]
        {
            Context(100, new(new(Id(99)), Branch.ProjectId, Branch.BranchId)),
            Context(100, new(Tenant, new(Id(99)), Branch.BranchId)),
            Context(100, new(Tenant, Branch.ProjectId, new(Id(99)))),
            Context(999, Branch)
        })
        {
            var location = ProjectLocation.Create(actual.DocumentId, context, actual.PropertyPath, actual.EntityId);
            Assert.Null(index.Inspect(location, fixture.Input.InputDigest));
            AssertRefused(index.FindReferences(location, fixture.Input.InputDigest), CompilerIndexQueryState.OutOfScope);
            AssertRefused(index.CompleteEntityReference(location, fixture.Input.InputDigest), CompilerIndexQueryState.OutOfScope);
            Assert.Equal(CompilerIndexQueryState.OutOfScope, index.FindDefinition(location, fixture.Input.InputDigest).State);
        }
    }

    [Fact]
    public void ExactLocationMembershipRejectsAlteredDocumentEntityAndPropertyPath()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        var actual = Symbol(index, fixture.Input.Source.Context, 10);
        Assert.Same(actual, index.Inspect(ProjectLocation.Parse(actual.Symbol.Location.Data), fixture.Input.InputDigest));
        foreach (var location in new[]
        {
            ProjectLocation.Create(new(Id(999)), fixture.Input.Source.Context, entityId: actual.Symbol.EntityId),
            ProjectLocation.Create(Document, fixture.Input.Source.Context, entityId: new(Id(999))),
            ProjectLocation.Create(Document, fixture.Input.Source.Context, "/copied-path", actual.Symbol.EntityId)
        })
        {
            Assert.Null(index.Inspect(location, fixture.Input.InputDigest));
            AssertRefused(index.FindReferences(location, fixture.Input.InputDigest), CompilerIndexQueryState.Unavailable);
        }
        var origin = index.References[0].Origin;
        var altered = ProjectLocation.Create(origin.DocumentId, origin.Context, origin.PropertyPath + "/copy", relationId: origin.RelationId);
        Assert.Equal(CompilerIndexQueryState.Unavailable, index.FindDefinition(altered, fixture.Input.InputDigest).State);
    }

    [Fact]
    public void CompletionRequiresAnActualSupportedEntityReferencePort()
    {
        var fixture = Local(taskCount: 4, booleanSecondPort: true);
        var index = Compile(fixture).Index;
        AssertRefused(index.CompleteEntityReference(Symbol(index, fixture.Input.Source.Context, 10).Symbol.Location, fixture.Input.InputDigest), CompilerIndexQueryState.Unavailable);
        AssertRefused(index.CompleteEntityReference(Port(index, fixture.Input.Source.Context, 11).Symbol.Location, fixture.Input.InputDigest), CompilerIndexQueryState.Unsupported);
        var document = Assert.Single(index.Symbols, item => item.IsDocument);
        AssertRefused(index.CompleteEntityReference(document.Symbol.Location, fixture.Input.InputDigest), CompilerIndexQueryState.Unavailable);
        Assert.Equal(4, index.CompleteEntityReference(Port(index, fixture.Input.Source.Context, 10).Symbol.Location, fixture.Input.InputDigest).Candidates.Count);
    }

    [Fact]
    public void CompletionPagesPreserveThirtyTwoResultBoundsAndExactOrderedMembership()
    {
        var fixture = Local(taskCount: 70);
        var index = Compile(fixture).Index;
        var origin = Port(index, fixture.Input.Source.Context, 10).Symbol.Location;
        var seen = new List<CompilerIndexedSymbol>();
        var sizes = new List<int>();
        CompilerIndexCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = index.CompleteEntityReference(origin, fixture.Input.InputDigest, 32, cursor);
            Assert.Equal(CompilerIndexQueryState.Ready, page.State);
            Assert.InRange(page.Candidates.Count, 1, 32);
            Assert.InRange(page.ScannedSymbols, page.Candidates.Count, 256);
            Assert.True(page.IndexCoverageComplete);
            seen.AddRange(page.Candidates);
            sizes.Add(page.Candidates.Count);
            cursor = page.NextCursor;
            Assert.Equal(cursor is null, page.ScanComplete);
            Assert.InRange(++pages, 1, 10);
        } while (cursor is not null);
        Assert.Equal(new[] { 32, 32, 6 }, sizes);
        Assert.Equal(3, pages);
        var expected = index.Symbols.Where(item => !item.IsDocument && item.Symbol.PortId is null && item.Symbol.Kind == "coglatas.task").ToArray();
        Assert.Equal(expected, seen);
        Assert.Equal(70, seen.Select(item => item.Symbol.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CompletionCursorsCannotCrossIndexInstancesOrOriginPorts()
    {
        var fixture = Regional();
        var read = Compile(fixture);
        var index = read.Index;
        var origin = Port(index, fixture.Input.Source.Context, 10).Symbol.Location;
        var cursor = index.CompleteEntityReference(origin, fixture.Input.InputDigest, 1).NextCursor;
        Assert.NotNull(cursor);
        var equalIndex = CompilerSemanticIndex.Build(read.Binding, fixture.Input.InputDigest);
        Assert.Equal(index.Digest, equalIndex.Digest);
        AssertRefused(equalIndex.CompleteEntityReference(origin, fixture.Input.InputDigest, cursor: cursor), CompilerIndexQueryState.InvalidCursor);
        AssertRefused(index.CompleteEntityReference(Port(index, fixture.Input.Source.Context, 11).Symbol.Location, fixture.Input.InputDigest, cursor: cursor), CompilerIndexQueryState.InvalidCursor);
        Assert.Equal(CompilerIndexQueryState.Ready, index.CompleteEntityReference(origin, fixture.Input.InputDigest, cursor: cursor).State);
    }

    [Fact]
    public void ContinuationsCannotCrossCompletionAndReferenceQueryKinds()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        var origin = Port(index, fixture.Input.Source.Context, 10).Symbol.Location;
        var target = Symbol(index, fixture.Dependencies[0].Context, 20).Symbol.Location;
        var completion = index.CompleteEntityReference(origin, fixture.Input.InputDigest, 1).NextCursor;
        var references = index.FindReferences(target, fixture.Input.InputDigest, 1).NextCursor;
        Assert.NotNull(completion);
        Assert.NotNull(references);
        AssertRefused(index.FindReferences(target, fixture.Input.InputDigest, cursor: completion), CompilerIndexQueryState.InvalidCursor);
        AssertRefused(index.CompleteEntityReference(origin, fixture.Input.InputDigest, cursor: references), CompilerIndexQueryState.InvalidCursor);
    }

    [Fact]
    public void ReferencePagesPreserveOriginalRowsAndThirtyTwoResultLimits()
    {
        var fixture = FanIn(70);
        var index = Compile(fixture).Index;
        var target = Symbol(index, fixture.Input.Source.Context, 900).Symbol.Location;
        var seen = new List<CompilerBoundReference>();
        CompilerIndexCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = index.FindReferences(target, fixture.Input.InputDigest, 32, cursor);
            Assert.Equal(CompilerIndexQueryState.Ready, page.State);
            Assert.InRange(page.References.Count, 0, 32);
            Assert.InRange(page.ScannedReferences, page.References.Count, 256);
            seen.AddRange(page.References);
            cursor = page.NextCursor;
            Assert.InRange(++pages, 1, 10);
        } while (cursor is not null);
        var expected = index.References.Where(item => item.SymbolKey == Symbol(index, fixture.Input.Source.Context, 900).Symbol.Key).ToArray();
        Assert.Equal(70, expected.Length);
        Assert.Equal(expected, seen);
        Assert.Equal(70, seen.Select(item => item.Origin.Data.CanonicalText).Distinct(StringComparer.Ordinal).Count());
        Assert.All(seen, row => Assert.Contains(index.References, original => ReferenceEquals(original, row)));
    }

    [Fact]
    public void EmptyTwoHundredFiftySixScanPageStillReachesItsLateOriginalReference()
    {
        var fixture = FanIn(130, lateTarget: true);
        var read = Compile(fixture);
        var index = read.Index;
        Assert.Equal(260, index.References.Count);
        var target = Symbol(index, fixture.Input.Source.Context, 901);
        Assert.True(Array.FindIndex(index.References.ToArray(), row => row.SymbolKey == target.Symbol.Key) >= 256);
        var first = index.FindReferences(target.Symbol.Location, fixture.Input.InputDigest);
        Assert.Equal(CompilerIndexQueryState.Ready, first.State);
        Assert.Empty(first.References);
        Assert.Equal(256, first.ScannedReferences);
        Assert.False(first.ScanComplete);
        Assert.True(first.IndexCoverageComplete);
        Assert.NotNull(first.NextCursor);
        var last = index.FindReferences(target.Symbol.Location, fixture.Input.InputDigest, cursor: first.NextCursor);
        Assert.Equal(CompilerIndexQueryState.Ready, last.State);
        Assert.Equal(target.Symbol.Key, Assert.Single(last.References).SymbolKey);
        Assert.Equal(4, last.ScannedReferences);
        Assert.True(last.ScanComplete);
        Assert.Null(last.NextCursor);
        Assert.Equal(3_000_000, fixture.Input.ExecutionProfile.MaximumOperations);
        Assert.InRange(index.LogicalOperationsConsumed, 1, 1_000_000);
        Assert.InRange(index.Data.ToCanonicalBytes().Length, 1, 4 * 1024 * 1024);
        Assert.NotNull(read.Compilation.Publication);
    }

    [Fact]
    public void ReferenceCursorsCannotCrossEqualIndexesOrTargetMembers()
    {
        var fixture = Regional();
        var read = Compile(fixture);
        var index = read.Index;
        var target = Symbol(index, fixture.Dependencies[0].Context, 20).Symbol.Location;
        var cursor = index.FindReferences(target, fixture.Input.InputDigest, 1).NextCursor;
        Assert.NotNull(cursor);
        var other = CompilerSemanticIndex.Build(read.Binding, fixture.Input.InputDigest);
        Assert.Equal(index.Digest, other.Digest);
        AssertRefused(other.FindReferences(target, fixture.Input.InputDigest, cursor: cursor), CompilerIndexQueryState.InvalidCursor);
        AssertRefused(index.FindReferences(Symbol(index, fixture.Dependencies[1].Context, 20).Symbol.Location, fixture.Input.InputDigest, cursor: cursor), CompilerIndexQueryState.InvalidCursor);
        Assert.Equal(CompilerIndexQueryState.Ready, index.FindReferences(target, fixture.Input.InputDigest, cursor: cursor).State);
    }

    [Fact]
    public void ResourceExhaustionAndInvalidPageSizesNeverRelaxExistingBounds()
    {
        var fixture = Regional();
        var read = Compile(fixture);
        var index = CompilerSemanticIndex.Build(read.Binding, fixture.Input.InputDigest, maximumOperations: 1);
        Assert.Equal(CompilerSemanticIndexState.ResourceLimit, index.State);
        Assert.Empty(index.Regions);
        Assert.Empty(index.Symbols);
        Assert.Empty(index.References);
        var target = Symbol(read.Index, fixture.Input.Source.Context, 10).Symbol.Location;
        AssertRefused(index.FindReferences(target, fixture.Input.InputDigest), CompilerIndexQueryState.ResourceLimit);
        foreach (var invalid in new[] { 0, 33 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => read.Index.FindReferences(target, fixture.Input.InputDigest, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => read.Index.CompleteEntityReference(target, fixture.Input.InputDigest, invalid));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => CompilerSemanticIndex.Build(read.Binding, fixture.Input.InputDigest, 1_000_001));
        Assert.Equal(32, CompilerSemanticIndex.MaximumPageSize);
        Assert.Equal(256, CompilerSemanticIndex.MaximumScan);
    }

    [Fact]
    public void CancellationBeforeBuildAndQueriesPreservesTheOriginalIndexAndCompiler()
    {
        var fixture = Regional();
        var read = Compile(fixture);
        var original = read.Index.Data;
        var result = read.Compilation.Result.Data;
        var target = Symbol(read.Index, fixture.Dependencies[0].Context, 20).Symbol.Location;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompilerSemanticIndex.Build(read.Binding, fixture.Input.InputDigest, cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => read.Index.FindDefinition(read.Index.References[0].Origin, fixture.Input.InputDigest, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => read.Index.FindReferences(target, fixture.Input.InputDigest, cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => read.Index.CompleteEntityReference(Port(read.Index, fixture.Input.Source.Context, 10).Symbol.Location, fixture.Input.InputDigest, cancellationToken: cancellation.Token));
        Assert.Equal(original, read.Index.Data);
        Assert.Equal(result, read.Compilation.Result.Data);
        Assert.Equal(CompilerIndexQueryState.Ready, read.Index.FindReferences(target, fixture.Input.InputDigest).State);
    }

    [Fact]
    public void PublishedRostersAndLegacySynchronizationRootsCannotRewriteMembership()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        var original = index.Data;
        RejectMutation(index.Symbols);
        RejectMutation(index.References);
        RejectMutation(index.Regions);
        var page = index.CompleteEntityReference(Port(index, fixture.Input.Source.Context, 10).Symbol.Location, fixture.Input.InputDigest);
        RejectMutation(page.Candidates);
        var references = index.FindReferences(Symbol(index, fixture.Dependencies[0].Context, 20).Symbol.Location, fixture.Input.InputDigest);
        RejectMutation(references.References);
        Assert.Equal(original, index.Data);
        Assert.Equal(index.Digest, ContentDigest.Compute(CompilerSemanticIndex.Format, index.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void DetachedJsonAndRecordCopiesCannotChangeRetainedIndexOrMemberIdentity()
    {
        var fixture = Regional();
        var index = Compile(fixture).Index;
        var original = index.Data;
        var actual = Symbol(index, fixture.Dependencies[0].Context, 20);
        var copied = actual with { DisplayName = "detached-replacement" };
        Assert.NotEqual(actual.DisplayName, copied.DisplayName);
        Assert.Same(actual, index.Inspect(copied.Symbol.Location, fixture.Input.InputDigest));
        var detached = JsonNode.Parse(index.Data.CanonicalText)!.AsObject();
        detached["symbols"]!.AsArray().Clear();
        var bytes = index.Data.ToCanonicalBytes();
        Array.Fill(bytes, (byte)0);
        Assert.Equal(14, index.Symbols.Count);
        Assert.Equal(original, index.Data);
        Assert.NotEqual(index.Digest, ContentDigest.Compute(CompilerSemanticIndex.Format, Json(detached).ToCanonicalBytes()));
    }

    private static Guid Id(int value) => Guid.Parse("f1930e60-0000-4000-8000-" + value.ToString("x12", CultureInfo.InvariantCulture));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static ContentDigest Digest(string value) => ContentDigest.Compute("coglatas.security-semantic-index-fixture/1", Encoding.UTF8.GetBytes(value));
    private static SourceRevisionContext Context(int revision, BranchRef? branch = null) => SourceRevisionContext.Committed(new(branch ?? Branch, new(Id(revision))));
    private static object Duration() => new
    {
        type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
        state = "coglatas.known", value = "1"
    };
    private static object TaskDeclaration(int identity, string name, bool port = false, bool boolean = false) => new
    {
        entityId = new EntityId(Id(identity)).ToString(), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1,
        displayName = name,
        payload = new { duration = Duration(), ports = port ? new[] { InputPort(identity, boolean) } : Array.Empty<object>() }
    };
    private static object InputPort(int identity, bool boolean) => new
    {
        portId = new PortId(Id(20_000 + identity)).ToString(), direction = "coglatas.input",
        type = boolean ? (object)new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true }
            : new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.task" },
        minimumConnections = 0, maximumConnections = 1
    };
    private static object Endpoint(SourceRevisionContext context, int entity) => new
    {
        kind = "coglatas.entity", expectedKind = "coglatas.task",
        reference = new { tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(),
            entityId = new EntityId(Id(entity)).ToString(), context = context.Data.Value }
    };
    private static object Edge(int identity, SourceRevisionContext fromContext, int from, SourceRevisionContext toContext, int to) => new
    {
        relationId = new RelationId(Id(identity)).ToString(), documentId = Document.ToString(), kind = RelationKinds.DependsOn, schemaVersion = 1,
        descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 }, source = Endpoint(fromContext, from), target = Endpoint(toContext, to),
        payload = new { mode = "coglatas.finish-to-start", lag = Duration() }
    };
    private static ProjectSource Source(SourceRevisionContext context, object[] declarations, object[]? relations = null) =>
        ProjectSource.Create(context, [SourceDocument.Create(Document, "independent-semantic-index", Json(new
        {
            entityId = new EntityId(Id(7)).ToString(), kind = "coglatas.project", schemaVersion = 1, payload = new { }
        }))], Json(declarations), Json(relations ?? []));
    private static object Pin(ProjectSource source, int identity) => new
    {
        snapshotId = new ExternalSnapshotId(Id(identity)).ToString(), version = SnapshotVersion, digest = source.Digest.Data.Value,
        context = source.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null
    };
    private static CompilerInputManifest Input(ProjectSource source, object[]? pins = null, int maximumOperations = 1_000_000)
    {
        object Artifact(string identity, string version = "1") => new { artifactId = identity, version, digest = Digest(identity + "/" + version).Data.Value };
        object None() => new { kind = "coglatas.none" };
        return new(source, Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.security-semantic-index.compiler"),
            ruleset = Artifact("coglatas.security-semantic-index.ruleset"), semantics = Artifact("coglatas.semantic-model", "2"),
            ir = Artifact("coglatas.project-ir", "2"), evaluator = Artifact("coglatas.security-semantic-index.evaluator"),
            scheduler = None(), solver = None(), stochastic = None(), importer = None(), scenario = None(),
            authorizedExternalSnapshots = pins ?? [], resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(),
            observations = Array.Empty<object>(), evidence = Array.Empty<object>(), units = Array.Empty<object>(), calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            descriptors = RelationKindDescriptor.BuiltIns.Select(item => new { artifactId = item.DescriptorId, version = "1", digest = item.Digest.Data.Value }).ToArray(),
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities).Prepend("coglatas.core.structural").Distinct(StringComparer.Ordinal).ToArray(),
            targets = new[] { "coglatas.semantic-model", "coglatas.project-ir" }, analysisScope = new[] { "coglatas.source-structure" },
            sourceCoverage = "coglatas.complete", clock = new { kind = "coglatas.not-applicable" },
            executionProfile = new { purpose = "coglatas.normal", maximumOperations, maximumDeclarations = 2048, maximumRelations = 1024,
                maximumDigits = 64, maximumScale = 32, rounding = "coglatas.ties-to-even" }
        }));
    }
    private sealed record Fixture(CompilerInputManifest Input, ProjectSource[] Dependencies, CompilerMaterializedSnapshot[] Supply);
    private sealed record Read(CoreCompilation Compilation, CompilerBindingClosure Binding, CompilerSemanticIndex Index);
    private static Fixture Regional(bool linkDependencies = true)
    {
        var context = Context(100);
        var dependencies = new[] { 200, 201 }.Select(revision =>
        {
            var dependency = Context(revision, DependencyBranch);
            return Source(dependency, [TaskDeclaration(20, $"private-dependency-{revision}-task-20"), TaskDeclaration(21, $"private-dependency-{revision}-task-21")],
                [Edge(1002, dependency, 20, dependency, 21)]);
        }).ToArray();
        var source = Source(context, [TaskDeclaration(10, "root-task-10", port: true), TaskDeclaration(11, "root-task-11", port: true)],
            linkDependencies ? [Edge(1000, context, 10, dependencies[0].Context, 20), Edge(1001, context, 11, dependencies[1].Context, 20)] : []);
        return new(Input(source, dependencies.Select((item, index) => Pin(item, 300 + index)).ToArray()), dependencies,
            dependencies.Select((item, index) => new CompilerMaterializedSnapshot(new(Id(300 + index)), SnapshotVersion, new(item, true, true, true))).ToArray());
    }
    private static Fixture Local(int taskCount, bool booleanSecondPort = false)
    {
        var source = Source(Context(100), Enumerable.Range(10, taskCount)
            .Select(identity => TaskDeclaration(identity, "local-task-" + identity.ToString(CultureInfo.InvariantCulture), identity is 10 or 11, booleanSecondPort && identity == 11)).ToArray());
        return new(Input(source), [], []);
    }
    private static Fixture FanIn(int count, bool lateTarget = false)
    {
        var context = Context(100);
        var tasks = Enumerable.Range(100, count).Select(identity => TaskDeclaration(identity, "fan-in-task-" + identity.ToString(CultureInfo.InvariantCulture)))
            .Append(TaskDeclaration(900, "ordinary-target")).Append(TaskDeclaration(901, "late-target")).ToArray();
        var edges = Enumerable.Range(0, count).Select(index => Edge(10_000 + index, context, 100 + index, context,
            lateTarget && index == count - 1 ? 901 : 900)).ToArray();
        return new(Input(Source(context, tasks, edges), maximumOperations: 3_000_000), [], []);
    }
    private static Read Compile(Fixture fixture, bool complete = true)
    {
        var compiled = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Supply);
        var model = Assert.IsType<PartialSemanticModel>(compiled.Model);
        var binding = compiled.Dependencies?.Binding ?? CompilerBindingClosure.Build(model, fixture.Input.InputDigest);
        var index = CompilerSemanticIndex.Build(binding, fixture.Input.InputDigest, maximumOperations: 1_000_000);
        if (complete)
        {
            Assert.Equal(CompilerExecutionState.Completed, compiled.Result.Execution);
            Assert.Equal(CompilerBindingClosureState.Complete, binding.State);
            Assert.Equal(CompilerSemanticIndexState.Complete, index.State);
            Assert.True(index.IndexCoverageComplete);
            Assert.True(index.IncludesDeclaredSnapshots);
        }
        return new(compiled, binding, index);
    }
    private static CompilerIndexedSymbol Symbol(CompilerSemanticIndex index, SourceRevisionContext context, int entity) =>
        Assert.Single(index.Symbols, item => !item.IsDocument && item.Symbol.PortId is null && item.Symbol.EntityId == new EntityId(Id(entity)) && item.Symbol.Context.Equals(context));
    private static CompilerIndexedSymbol Port(CompilerSemanticIndex index, SourceRevisionContext context, int entity) =>
        Assert.Single(index.Symbols, item => item.Symbol.PortId == new PortId(Id(20_000 + entity)) && item.Symbol.Context.Equals(context));
    private static void AssertHidden(CompilerSemanticIndex index, ProjectSource source)
    {
        Assert.DoesNotContain(index.Regions, region => region.Source.Context.Equals(source.Context));
        Assert.DoesNotContain(index.Symbols, symbol => symbol.Symbol.Context.Equals(source.Context));
        Assert.DoesNotContain(index.References, reference => reference.Origin.Context.Equals(source.Context));
        Assert.DoesNotContain("private-dependency-200", index.Data.CanonicalText);
        var target = ProjectLocation.Create(Document, source.Context, entityId: new(Id(20)));
        Assert.Null(index.Inspect(target, index.Input.InputDigest));
        AssertRefused(index.FindReferences(target, index.Input.InputDigest), CompilerIndexQueryState.OutOfScope);
    }
    private static void AssertRefused(CompilerIndexReferencePage page, CompilerIndexQueryState state)
    {
        Assert.Equal(state, page.State); Assert.Null(page.InputDigest); Assert.Null(page.Target);
        Assert.Empty(page.References); Assert.False(page.IndexCoverageComplete); Assert.False(page.ScanComplete);
        Assert.Equal(0, page.ScannedReferences); Assert.Null(page.NextCursor);
    }
    private static void AssertRefused(CompilerIndexCompletionPage page, CompilerIndexQueryState state)
    {
        Assert.Equal(state, page.State); Assert.Null(page.InputDigest); Assert.Null(page.Origin); Assert.Null(page.ExpectedKind);
        Assert.Empty(page.Candidates); Assert.False(page.IndexCoverageComplete); Assert.False(page.ScanComplete);
        Assert.Equal(0, page.ScannedSymbols); Assert.Null(page.NextCursor);
    }
    private static void RejectMutation<T>(IReadOnlyList<T> values)
    {
        Assert.NotEmpty(values);
        var original = values.ToArray();
        if (values is IList<T> generic) Assert.Throws<NotSupportedException>(() => generic[0] = default!);
        if (values is IList legacy) Assert.Throws<NotSupportedException>(() => legacy[0] = null);
        if (values is ICollection collection && collection.SyncRoot is Array sync) Array.Clear(sync);
        Assert.Equal(original, values);
    }
}
