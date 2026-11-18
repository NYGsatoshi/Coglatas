using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent real-host and pure ViewModel controls, without runtime authorization or Native UI credit.</summary>
public sealed class SecuritySemanticIndexViewModelCandidateTests
{
    private static readonly TenantId Tenant = new(Id(1));
    private static readonly BranchRef Branch = new(Tenant, new(Id(2)), new(Id(3)));
    private static readonly BranchRef DependencyBranch = new(Tenant, new(Id(4)), new(Id(5)));
    private static readonly DocumentId Document = new(Id(6));
    private const string SnapshotVersion = "independent-view-index-1";

    [Fact]
    public async Task IndexRequiresAnActualAcceptedHostResultAndExplicitScopedSourceAdmission()
    {
        using var fixture = await OwnedFixture.Create(admit: false);
        var view = fixture.View; var original = fixture.Response.Compilation!.Result.Data;
        Assert.Null(view.SemanticIndex);
        Assert.False(view.LoadSyntheticSemanticIndex());
        Assert.Equal("Unavailable", view.SemanticIndexReadState);
        Assert.True(view.LoadSyntheticScopedSources());
        Assert.True(view.LoadSyntheticSemanticIndex());
        Assert.Equal("Ready", view.SemanticIndexReadState);
        Assert.Equal(CompilerSemanticIndexState.Complete, Index(view).State);
        Assert.True(Index(view).IndexCoverageComplete);
        Assert.Equal(3, Index(view).Regions.Count);
        Assert.Same(fixture.Response.Compilation!.Result, view.Result);
        Assert.Equal(original, view.Result!.Data);
    }

    [Fact]
    public async Task DependencyOwnedReferencesAndDefinitionsNavigateTheirExactOriginalRegionalSource()
    {
        using var fixture = await OwnedFixture.Create();
        var view = fixture.View; var index = Index(view); var result = view.Result;
        foreach (var source in fixture.Model.Dependencies.Prepend(fixture.Model.Input.Source))
        {
            var reference = Assert.Single(index.References, row => row.Origin.Context.Equals(source.Context) && row.Origin.RelationId == new RelationId(Id(source.Context.Equals(fixture.Model.Input.Source.Context) ? 1000 : 1002)) && row.Origin.PropertyPath == "/source");
            var target = Assert.Single(index.Symbols, row => row.Symbol.Key == reference.SymbolKey);
            Assert.True(view.SelectIndexedSymbol(target));
            var page = Assert.IsType<CompilerIndexReferencePage>(view.ReadIndexedReferences());
            Assert.Contains(page.References, row => ReferenceEquals(row, reference));
            Assert.True(view.NavigateIndexedReference(page, reference));
            Assert.Same(reference.Origin, view.ScopedSourceTarget);
            Assert.Equal(source.Digest, view.ScopedSource!.Digest);
            var definition = Assert.IsType<CompilerIndexDefinition>(view.ReadIndexedDefinition(reference));
            Assert.Equal(CompilerIndexQueryState.Ready, definition.State);
            Assert.Same(target, definition.Target);
            Assert.True(view.NavigateIndexedSymbol(target));
            Assert.Same(target.Symbol.Location, view.ScopedSourceTarget);
            Assert.Equal(target.Symbol.Context, view.ScopedSource!.Context);
        }
        Assert.Same(result, view.Result);
    }

    [Fact]
    public async Task EqualSymbolCopiesAndEarlierIndexMembersCannotBorrowCurrentSelection()
    {
        using var fixture = await OwnedFixture.Create();
        var view = fixture.View; var index = Index(view); var target = Symbol(index, fixture.Model.Dependencies[0].Context, 20);
        Assert.False(view.SelectIndexedSymbol(target with { })); Assert.Null(view.SelectedIndexedSymbol);
        Assert.True(view.SelectIndexedSymbol(target)); Assert.Same(target, view.SelectedIndexedSymbol);
        var oldBytes = index.Data.ToCanonicalBytes();
        Assert.True(view.LoadSyntheticSemanticIndex());
        Assert.Equal(index.Digest, Index(view).Digest); Assert.NotSame(index, Index(view));
        Assert.False(view.SelectIndexedSymbol(target)); Assert.Null(view.SelectedIndexedSymbol);
        Assert.Equal(oldBytes, index.Data.ToCanonicalBytes());
        Assert.True(view.SelectIndexedSymbol(Symbol(Index(view), fixture.Model.Dependencies[0].Context, 20)));
    }

    [Fact]
    public async Task EqualReferenceCopiesAndForeignDefinitionsCannotBorrowCurrentQueryMembership()
    {
        using var fixture = await OwnedFixture.Create();
        var view = fixture.View; var index = Index(view); var target = Symbol(index, fixture.Model.Dependencies[0].Context, 20);
        Assert.True(view.SelectIndexedSymbol(target));
        var page = Assert.IsType<CompilerIndexReferencePage>(view.ReadIndexedReferences());
        var reference = page.References[0];
        Assert.False(view.NavigateIndexedReference(page, reference with { }));
        Assert.Null(view.ScopedSourceTarget);
        var refused = Assert.IsType<CompilerIndexDefinition>(view.ReadIndexedDefinition(reference with { }));
        Assert.Equal(CompilerIndexQueryState.Unavailable, refused.State); Assert.Null(refused.Target); Assert.Null(refused.Resolution);
        var accepted = Assert.IsType<CompilerIndexDefinition>(view.ReadIndexedDefinition(reference));
        Assert.Equal(CompilerIndexQueryState.Ready, accepted.State); Assert.Same(target, accepted.Target);
    }

    [Fact]
    public async Task ReferenceGraphContainsOnlyTheCurrentOriginalPageAndTarget()
    {
        using var fixture = await OwnedFixture.Create();
        var view = fixture.View; var index = Index(view); var target = Symbol(index, fixture.Model.Dependencies[0].Context, 20);
        Assert.True(view.SelectIndexedSymbol(target));
        var page = Assert.IsType<CompilerIndexReferencePage>(view.ReadIndexedReferences());
        var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        Assert.Same(target, graph.Target); Assert.Same(page, graph.Page);
        Assert.Equal(page.References, graph.Edges); Assert.NotSame(page.References, graph.Edges);
        Assert.All(graph.Edges, edge => Assert.Contains(index.References, row => ReferenceEquals(row, edge)));
        Assert.True(view.NavigateIndexedGraphOrigin(graph, graph.Edges[0]));
        Assert.Same(graph.Edges[0].Origin, view.ScopedSourceTarget);
        Assert.True(view.NavigateIndexedGraphTarget(graph)); Assert.Same(target.Symbol.Location, view.ScopedSourceTarget);
    }

    [Fact]
    public async Task OlderGraphsAndCopiedEdgesCannotOverwriteANewerOwnedNavigationTarget()
    {
        using var fixture = await OwnedFixture.Create();
        var view = fixture.View; var index = Index(view); var first = Symbol(index, fixture.Model.Dependencies[0].Context, 20);
        Assert.True(view.SelectIndexedSymbol(first)); Assert.NotNull(view.ReadIndexedReferences());
        var older = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        var target = Symbol(index, fixture.Model.Dependencies[1].Context, 20);
        Assert.True(view.SelectIndexedSymbol(target)); Assert.NotNull(view.ReadIndexedReferences());
        var current = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        Assert.True(view.NavigateIndexedGraphTarget(current)); var currentLocation = view.ScopedSourceTarget;
        Assert.False(view.NavigateIndexedGraphOrigin(older, older.Edges[0]));
        Assert.False(view.NavigateIndexedGraphTarget(older));
        Assert.False(view.NavigateIndexedGraphOrigin(current, current.Edges[0] with { }));
        Assert.Same(current, view.IndexedReferenceGraph); Assert.Same(currentLocation, view.ScopedSourceTarget);
    }

    [Fact]
    public async Task PublishedGraphEdgesRejectMutableStorageAndPreserveRetainedOriginals()
    {
        using var fixture = await OwnedFixture.Create();
        var view = fixture.View; var target = Symbol(Index(view), fixture.Model.Dependencies[0].Context, 20);
        Assert.True(view.SelectIndexedSymbol(target)); Assert.NotNull(view.ReadIndexedReferences());
        var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph); var rows = graph.Edges.ToArray();
        Assert.NotEmpty(rows); Assert.False(graph.Edges is Array); Assert.False(graph.Edges is ICollection);
        if (graph.Edges is IList<CompilerBoundReference> writable) Assert.Throws<NotSupportedException>(() => writable[0] = rows[0] with { });
        var copy = graph.Edges.ToArray(); Array.Clear(copy);
        Assert.Equal(rows, graph.Edges);
        view.Reset(); Assert.Null(view.IndexedReferenceGraph); Assert.Equal(rows, graph.Edges);
        Assert.All(graph.Edges, edge => Assert.Contains(rows, row => ReferenceEquals(row, edge)));
    }

    [Fact]
    public async Task CompletionCandidatesAndContinuationsBelongToTheirCurrentOriginAndIndex()
    {
        using var fixture = await OwnedFixture.Create();
        var view = fixture.View; var index = Index(view); var port = Port(index, fixture.Model.Input.Source.Context, 10);
        Assert.True(view.SelectIndexedSymbol(port));
        var first = Assert.IsType<CompilerIndexCompletionPage>(view.ReadIndexedCompletion(1));
        Assert.Single(first.Candidates); Assert.NotNull(first.NextCursor); Assert.False(first.ScanComplete);
        var candidate = first.Candidates[0];
        Assert.False(view.NavigateIndexedSymbol(candidate with { }));
        Assert.True(view.NavigateIndexedSymbol(candidate)); Assert.Same(candidate.Symbol.Location, view.ScopedSourceTarget);
        var next = Assert.IsType<CompilerIndexCompletionPage>(view.ReadIndexedCompletion(1, first.NextCursor));
        Assert.Equal(CompilerIndexQueryState.Ready, next.State); Assert.DoesNotContain(next.Candidates, row => ReferenceEquals(row, candidate));
        Assert.True(view.SelectIndexedSymbol(Port(index, fixture.Model.Input.Source.Context, 11)));
        var wrong = Assert.IsType<CompilerIndexCompletionPage>(view.ReadIndexedCompletion(cursor: first.NextCursor));
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, wrong.State); Assert.Empty(wrong.Candidates); Assert.Null(wrong.NextCursor);
        Assert.Null(view.ScopedSourceTarget);
    }

    [Fact]
    public async Task ReferencePagesKeepThirtyTwoResultLimitsAndWithdrawPreviousGraphMembership()
    {
        using var fixture = await OwnedFixture.Create(FanIn(70));
        var view = fixture.View; var index = Index(view); var target = Symbol(index, fixture.Model.Input.Source.Context, 900);
        Assert.True(view.SelectIndexedSymbol(target)); var seen = new List<CompilerBoundReference>();
        CompilerIndexCursor? cursor = null; SyntheticReferenceGraphPage? older = null; var sizes = new List<int>();
        do
        {
            var page = Assert.IsType<CompilerIndexReferencePage>(view.ReadIndexedReferences(cursor: cursor));
            Assert.Equal(CompilerIndexQueryState.Ready, page.State); Assert.InRange(page.ScannedReferences, 1, 256);
            var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
            Assert.Same(page, graph.Page); Assert.Equal(page.References, graph.Edges);
            if (older is not null) Assert.False(view.NavigateIndexedGraphOrigin(older, older.Edges[0]));
            sizes.Add(graph.Edges.Count); seen.AddRange(graph.Edges); cursor = page.NextCursor; older = graph;
            Assert.InRange(sizes.Count, 1, 5);
        } while (cursor is not null);
        Assert.Equal(new[] { 32, 32, 6 }, sizes);
        Assert.Equal(index.References.Where(row => row.SymbolKey == target.Symbol.Key), seen);
        Assert.Equal(70, seen.Select(row => row.Origin.Data.CanonicalText).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task EmptyFirstScanGraphRetainsItsContinuationAndReachesTheLateOriginalEdge()
    {
        using var fixture = await OwnedFixture.Create(FanIn(130, lateTarget: true));
        var view = fixture.View; var index = Index(view); var target = Symbol(index, fixture.Model.Input.Source.Context, 901);
        Assert.Equal(260, index.References.Count); Assert.True(view.SelectIndexedSymbol(target));
        var first = Assert.IsType<CompilerIndexReferencePage>(view.ReadIndexedReferences());
        var empty = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        Assert.Equal(CompilerIndexQueryState.Ready, first.State); Assert.Empty(empty.Edges); Assert.Equal(256, first.ScannedReferences);
        Assert.False(first.ScanComplete); Assert.NotNull(first.NextCursor); Assert.True(first.IndexCoverageComplete);
        var last = Assert.IsType<CompilerIndexReferencePage>(view.ReadIndexedReferences(cursor: first.NextCursor));
        var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        var original = Assert.Single(graph.Edges); Assert.Equal(target.Symbol.Key, original.SymbolKey);
        Assert.Equal(4, last.ScannedReferences); Assert.True(last.ScanComplete); Assert.Null(last.NextCursor);
        Assert.False(view.NavigateIndexedGraphTarget(empty)); Assert.True(view.NavigateIndexedGraphOrigin(graph, original));
        Assert.Same(original.Origin, view.ScopedSourceTarget);
    }

    [Fact]
    public async Task ScopedReadmissionWithdrawsIndexQueriesAndOldMemberNavigation()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View; var old = Index(view);
        SelectReferences(fixture); var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        Assert.True(view.NavigateIndexedGraphTarget(graph)); var oldBytes = old.Data.ToCanonicalBytes();
        Assert.True(view.LoadSyntheticScopedSources()); AssertAbsent(view); Assert.Equal("Idle", view.SemanticIndexReadState);
        Assert.False(view.SelectIndexedSymbol(graph.Target)); Assert.True(view.LoadSyntheticSemanticIndex());
        Assert.NotSame(old, Index(view)); Assert.Equal(old.Digest, Index(view).Digest);
        Assert.False(view.NavigateIndexedGraphTarget(graph)); Assert.Equal(oldBytes, old.Data.ToCanonicalBytes());
    }

    [Fact]
    public async Task SameInputNewHostGenerationCannotReadOrNavigateTheEarlierIndex()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View; var old = Index(view);
        SelectReferences(fixture); var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        var request = new CompilerHostRequest(new CompilerInputTransfer(fixture.Model.Input), 2, fixture.Model.Supply);
        var response = await CompileHost(request); view.BeginSynthetic(request); AssertAbsent(view);
        Assert.False(view.TryAccept(fixture.Response)); Assert.True(view.TryAccept(response));
        Assert.False(view.LoadSyntheticSemanticIndex()); Assert.True(view.LoadSyntheticScopedSources()); Assert.True(view.LoadSyntheticSemanticIndex());
        Assert.Equal(old.Digest, Index(view).Digest); Assert.NotSame(old, Index(view));
        Assert.False(view.NavigateIndexedGraphTarget(graph)); Assert.Same(response.Compilation!.Result, view.Result);
    }

    [Fact]
    public async Task ResetRequiresANewerGenerationAndFreshSourceAdmission()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View;
        SelectReferences(fixture); var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        view.Reset(); AssertAbsent(view); Assert.Null(view.Result); Assert.Null(view.Source);
        Assert.False(view.NavigateIndexedGraphTarget(graph));
        Assert.Throws<ArgumentException>(() => view.BeginSynthetic(fixture.Request));
        var request = new CompilerHostRequest(new CompilerInputTransfer(fixture.Model.Input), 2, fixture.Model.Supply);
        view.BeginSynthetic(request); Assert.True(view.TryAccept(await CompileHost(request)));
        Assert.False(view.LoadSyntheticSemanticIndex()); Assert.True(view.LoadSyntheticScopedSources()); Assert.True(view.LoadSyntheticSemanticIndex());
    }

    [Fact]
    public async Task DisposalWithdrawsTheIndexAndRejectsSubsequentSemanticOperations()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View; var old = Index(view); var bytes = old.Data.ToCanonicalBytes();
        SelectReferences(fixture); var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        view.Dispose(); AssertAbsent(view); Assert.Null(view.Result);
        Assert.Throws<ObjectDisposedException>(() => view.LoadSyntheticSemanticIndex());
        Assert.Throws<ObjectDisposedException>(() => view.SelectIndexedSymbol(graph.Target));
        Assert.Throws<ObjectDisposedException>(() => view.ReadIndexedReferences());
        Assert.Throws<ObjectDisposedException>(() => view.NavigateIndexedGraphTarget(graph));
        Assert.Equal(bytes, old.Data.ToCanonicalBytes()); Assert.NotEmpty(graph.Edges);
    }

    [Fact]
    public async Task PreCancelledIndexReadWithdrawsTheOldViewAndPreservesTheCompilerResult()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View; var old = Index(view); var result = view.Result;
        var bytes = old.Data.ToCanonicalBytes(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.LoadSyntheticSemanticIndex(cancelled.Token));
        AssertAbsent(view); Assert.Equal("Idle", view.SemanticIndexReadState); Assert.Same(result, view.Result);
        Assert.Equal(bytes, old.Data.ToCanonicalBytes()); Assert.True(view.LoadSyntheticSemanticIndex());
    }

    [Fact]
    public async Task CancelledOlderIndexAtEitherNotificationPreservesANewerAdmittedIndex()
    {
        foreach (var published in new[] { false, true })
        {
            using var fixture = await OwnedFixture.Create(); var view = fixture.View; var result = view.Result;
            using var cancellation = new CancellationTokenSource(); CompilerSemanticIndex? newer = null;
            using var notification = new Notification(view, () => (view.SemanticIndex is not null) == published, () =>
            {
                Assert.True(view.LoadSyntheticSemanticIndex()); newer = Index(view); cancellation.Cancel();
            });
            Assert.Throws<OperationCanceledException>(() => view.LoadSyntheticSemanticIndex(cancellation.Token));
            Assert.True(notification.Fired); Assert.NotNull(newer); Assert.Same(newer, view.SemanticIndex);
            Assert.Equal("Ready", view.SemanticIndexReadState); Assert.Same(result, view.Result);
        }
    }

    [Fact]
    public async Task ReentrantResetDisposalAndSourceReadmissionPreventOldIndexPublication()
    {
        foreach (var published in new[] { false, true })
        foreach (var boundary in new[] { "reset", "dispose", "readmit" })
        {
            using var fixture = await OwnedFixture.Create(); var view = fixture.View; var original = fixture.Response.Compilation!.Result.Data;
            using var notification = new Notification(view, () => (view.SemanticIndex is not null) == published, () =>
            {
                if (boundary == "reset") view.Reset(); else if (boundary == "dispose") view.Dispose(); else Assert.True(view.LoadSyntheticScopedSources());
            });
            Assert.False(view.LoadSyntheticSemanticIndex()); Assert.True(notification.Fired); AssertAbsent(view);
            Assert.Equal(original, fixture.Response.Compilation!.Result.Data);
            if (boundary == "readmit") { Assert.True(view.LoadSyntheticSemanticIndex()); Assert.Equal("Ready", view.SemanticIndexReadState); }
        }
    }

    [Fact]
    public async Task PreCancelledReferenceQueryWithdrawsOldGraphWithoutMutatingCallerHeldRows()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View; var index = Index(view);
        SelectReferences(fixture); var graph = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph); var rows = graph.Edges.ToArray();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => view.ReadIndexedReferences(cancellationToken: cancelled.Token));
        Assert.Null(view.IndexedReferences); Assert.Null(view.IndexedReferenceGraph); Assert.Same(index, view.SemanticIndex);
        Assert.Equal(rows, graph.Edges); Assert.NotNull(view.ReadIndexedReferences());
    }

    [Fact]
    public async Task CancelledOlderReferenceQueryAtEitherNotificationPreservesANewerReadyPage()
    {
        foreach (var published in new[] { false, true })
        {
            using var fixture = await OwnedFixture.Create(); var view = fixture.View; SelectReferences(fixture);
            using var cancellation = new CancellationTokenSource(); CompilerIndexReferencePage? newer = null; SyntheticReferenceGraphPage? newerGraph = null;
            using var notification = new Notification(view, () => (view.IndexedReferences is not null) == published, () =>
            {
                newer = view.ReadIndexedReferences(1); newerGraph = view.IndexedReferenceGraph; cancellation.Cancel();
            });
            Assert.Throws<OperationCanceledException>(() => view.ReadIndexedReferences(cancellationToken: cancellation.Token));
            Assert.True(notification.Fired); Assert.NotNull(newer); Assert.Equal(CompilerIndexQueryState.Ready, newer!.State);
            Assert.Same(newer, view.IndexedReferences); Assert.Same(newerGraph, view.IndexedReferenceGraph); Assert.Single(newer.References);
        }
    }

    [Fact]
    public async Task CancelledOlderReferenceQueryAtEitherNotificationPreservesANewerRefusedPage()
    {
        foreach (var published in new[] { false, true })
        {
            using var fixture = await OwnedFixture.Create(); var view = fixture.View; SelectReferences(fixture);
            var foreign = ForeignCursor(fixture); using var cancellation = new CancellationTokenSource(); CompilerIndexReferencePage? newer = null;
            using var notification = new Notification(view, () => (view.IndexedReferences is not null) == published, () =>
            {
                newer = view.ReadIndexedReferences(cursor: foreign); cancellation.Cancel();
            });
            Assert.Throws<OperationCanceledException>(() => view.ReadIndexedReferences(cancellationToken: cancellation.Token));
            Assert.True(notification.Fired); Assert.NotNull(newer); Assert.Equal(CompilerIndexQueryState.InvalidCursor, newer!.State);
            Assert.Same(newer, view.IndexedReferences); Assert.Empty(newer.References); Assert.Null(newer.NextCursor);
            Assert.Null(newer.InputDigest); Assert.Null(newer.Target); Assert.Equal(0, newer.ScannedReferences); Assert.Null(view.IndexedReferenceGraph);
        }
    }

    [Fact]
    public async Task UncancelledReentrantReferenceQueryCannotReplaceANewerReadyOrRefusedOwner()
    {
        foreach (var published in new[] { false, true })
        foreach (var refuse in new[] { false, true })
        {
            using var fixture = await OwnedFixture.Create(); var view = fixture.View; SelectReferences(fixture);
            var cursor = refuse ? ForeignCursor(fixture) : null; CompilerIndexReferencePage? newer = null;
            using var notification = new Notification(view, () => (view.IndexedReferences is not null) == published,
                () => newer = view.ReadIndexedReferences(1, cursor));
            Assert.Null(view.ReadIndexedReferences()); Assert.True(notification.Fired); Assert.NotNull(newer);
            Assert.Same(newer, view.IndexedReferences);
            Assert.Equal(refuse ? CompilerIndexQueryState.InvalidCursor : CompilerIndexQueryState.Ready, newer!.State);
        }
    }

    [Fact]
    public async Task ReentrantNavigationCannotOverwriteANewerSelectedGraphTarget()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View; var index = Index(view); SelectReferences(fixture);
        var old = Assert.IsType<SyntheticReferenceGraphPage>(view.IndexedReferenceGraph);
        var newerTarget = Symbol(index, fixture.Model.Dependencies[1].Context, 20); SyntheticReferenceGraphPage? newer = null;
        using var notification = new Notification(view, () => view.ScopedSourceTarget is not null, () =>
        {
            Assert.True(view.SelectIndexedSymbol(newerTarget)); Assert.NotNull(view.ReadIndexedReferences());
            newer = view.IndexedReferenceGraph; Assert.True(view.NavigateIndexedGraphTarget(newer!));
        });
        Assert.False(view.NavigateIndexedGraphOrigin(old, old.Edges[0])); Assert.True(notification.Fired);
        Assert.NotNull(newer); Assert.Same(newer, view.IndexedReferenceGraph); Assert.Same(newerTarget, view.SelectedIndexedSymbol);
        Assert.Same(newerTarget.Symbol.Location, view.ScopedSourceTarget);
    }

    [Fact]
    public async Task MissingDeniedAndWrongVersionSnapshotsCannotPublishAPrivateIndex()
    {
        var original = Regional(); var supply = original.Supply[0];
        foreach (var replacement in new[]
        {
            original.Supply.Skip(1).ToArray(),
            new[] { new CompilerMaterializedSnapshot(supply.SnapshotId, supply.Version, supply.Snapshot with { IsAuthorized = false }), original.Supply[1] },
            new[] { new CompilerMaterializedSnapshot(supply.SnapshotId, "wrong-version", supply.Snapshot), original.Supply[1] }
        })
        {
            using var fixture = await OwnedFixture.Create(original with { Supply = replacement }, admit: false);
            Assert.False(fixture.View.LoadSyntheticScopedSources()); Assert.False(fixture.View.LoadSyntheticSemanticIndex());
            AssertAbsent(fixture.View); Assert.Equal("Unavailable", fixture.View.SemanticIndexReadState);
        }
        using var restored = await OwnedFixture.Create(original); Assert.Equal(3, Index(restored.View).Regions.Count);
    }

    [Fact]
    public async Task DocumentNavigationAndRejectedPageSizesPreserveOriginalIndexAndCompilerBytes()
    {
        using var fixture = await OwnedFixture.Create(); var view = fixture.View; var index = Index(view); var bytes = index.Data.ToCanonicalBytes();
        var resultBytes = view.Result!.Data.ToCanonicalBytes();
        foreach (var document in index.Symbols.Where(row => row.IsDocument))
        {
            Assert.True(view.SelectIndexedSymbol(document)); Assert.True(view.NavigateIndexedSymbol(document));
            Assert.Same(document.Symbol.Location, view.ScopedSourceTarget);
            Assert.Equal(document.Symbol.Context, view.ScopedSource!.Context);
        }
        foreach (var size in new[] { 0, 33 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => view.ReadIndexedReferences(size));
            Assert.Throws<ArgumentOutOfRangeException>(() => view.ReadIndexedCompletion(size));
        }
        Assert.Same(index, view.SemanticIndex); Assert.Equal(bytes, index.Data.ToCanonicalBytes()); Assert.Equal(resultBytes, view.Result!.Data.ToCanonicalBytes());
    }

    private static CompilerSemanticIndex Index(SyntheticWorkbenchViewModel view) => Assert.IsType<CompilerSemanticIndex>(view.SemanticIndex);
    private static void SelectReferences(OwnedFixture fixture)
    {
        Assert.True(fixture.View.SelectIndexedSymbol(Symbol(Index(fixture.View), fixture.Model.Dependencies[0].Context, 20)));
        Assert.NotNull(fixture.View.ReadIndexedReferences());
    }
    private static CompilerIndexCursor ForeignCursor(OwnedFixture fixture)
    {
        var compilation = fixture.Response.Compilation!;
        var index = CompilerSemanticIndex.Build(compilation.Dependencies!.Binding, compilation.Input.InputDigest);
        var target = Symbol(index, fixture.Model.Dependencies[0].Context, 20);
        return Assert.IsType<CompilerIndexCursor>(index.FindReferences(target.Symbol.Location, compilation.Input.InputDigest, 1).NextCursor);
    }
    private static void AssertAbsent(SyntheticWorkbenchViewModel view)
    {
        Assert.Null(view.SemanticIndex); Assert.Null(view.SelectedIndexedSymbol); Assert.Null(view.IndexedReferences);
        Assert.Null(view.IndexedCompletion); Assert.Null(view.IndexedDefinition); Assert.Null(view.IndexedReferenceGraph); Assert.Null(view.ScopedSourceTarget);
    }
    private sealed class Notification : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view; private readonly Func<bool> _when; private readonly Action _action;
        public bool Fired { get; private set; }
        public Notification(SyntheticWorkbenchViewModel view, Func<bool> when, Action action)
        { _view = view; _when = when; _action = action; view.PropertyChanged += Changed; }
        private void Changed(object? sender, PropertyChangedEventArgs args)
        { if (Fired || !_when()) return; Assert.Same(_view, sender); Fired = true; _action(); }
        public void Dispose() => _view.PropertyChanged -= Changed;
    }
    private sealed class OwnedFixture(Fixture model, SyntheticWorkbenchViewModel view, CompilerHostRequest request, CompilerHostResponse response) : IDisposable
    {
        public Fixture Model { get; } = model;
        public SyntheticWorkbenchViewModel View { get; } = view;
        public CompilerHostRequest Request { get; } = request;
        public CompilerHostResponse Response { get; } = response;
        public static async Task<OwnedFixture> Create(Fixture? model = null, bool admit = true)
        {
            model ??= Regional(); var request = new CompilerHostRequest(new CompilerInputTransfer(model.Input), 1, model.Supply);
            var response = await CompileHost(request); var view = new SyntheticWorkbenchViewModel();
            view.BeginSynthetic(request); Assert.True(view.TryAccept(response)); Assert.Same(response.Compilation!.Result, view.Result);
            if (admit) { Assert.True(view.LoadSyntheticScopedSources()); Assert.True(view.LoadSyntheticSemanticIndex()); }
            return new(model, view, request, response);
        }
        public void Dispose() => View.Dispose();
    }
    private static async Task<CompilerHostResponse> CompileHost(CompilerHostRequest request)
    {
        var input = request.Input.Materialize();
        CompilerArtifactIdentity Contract(string name) => new(SourceJson.Parse(input.Settings.Value.GetProperty(name).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Contract("schema"), Contract("ruleset"), Contract("semantics"), Contract("ir"),
            Contract("evaluator"), input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()));
        var response = await new LocalNetCompilerHost(profile).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, response.Status); Assert.NotNull(response.Execution); Assert.NotNull(response.Compilation);
        Assert.Equal(CompilerExecutionState.Completed, response.Compilation!.Result.Execution);
        return response;
    }

    private static Guid Id(int value) => Guid.Parse("f1930b70-0000-4000-8000-" + value.ToString("x12", CultureInfo.InvariantCulture));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static ContentDigest Digest(string value) => ContentDigest.Compute("coglatas.security-viewmodel-index-fixture/1", Encoding.UTF8.GetBytes(value));
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
        ProjectSource.Create(context, [SourceDocument.Create(Document, "independent-viewmodel-index", Json(new
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
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.security-viewmodel-index.compiler"),
            ruleset = Artifact("coglatas.security-viewmodel-index.ruleset"), semantics = Artifact("coglatas.semantic-model", "2"),
            ir = Artifact("coglatas.project-ir", "2"), evaluator = Artifact("coglatas.security-viewmodel-index.evaluator"),
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
    private static Fixture Regional(bool linkDependencies = true)
    {
        var context = Context(100);
        var dependencies = new[] { 200, 201 }.Select(revision =>
        {
            var dependency = Context(revision, DependencyBranch);
            return Source(dependency, [TaskDeclaration(20, $"independent-view-dependency-{revision}-task-20"), TaskDeclaration(21, $"independent-view-dependency-{revision}-task-21")],
                [Edge(1002, dependency, 20, dependency, 21)]);
        }).ToArray();
        var source = Source(context, [TaskDeclaration(10, "root-task-10", port: true), TaskDeclaration(11, "root-task-11", port: true)],
            linkDependencies ? [Edge(1000, context, 10, dependencies[0].Context, 20), Edge(1001, context, 11, dependencies[1].Context, 20)] : []);
        return new(Input(source, dependencies.Select((item, index) => Pin(item, 300 + index)).ToArray()), dependencies,
            dependencies.Select((item, index) => new CompilerMaterializedSnapshot(new(Id(300 + index)), SnapshotVersion, new(item, true, true, true))).ToArray());
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
    private static CompilerIndexedSymbol Symbol(CompilerSemanticIndex index, SourceRevisionContext context, int entity) =>
        Assert.Single(index.Symbols, item => !item.IsDocument && item.Symbol.PortId is null && item.Symbol.EntityId == new EntityId(Id(entity)) && item.Symbol.Context.Equals(context));
    private static CompilerIndexedSymbol Port(CompilerSemanticIndex index, SourceRevisionContext context, int entity) =>
        Assert.Single(index.Symbols, item => item.Symbol.PortId == new PortId(Id(20_000 + entity)) && item.Symbol.Context.Equals(context));
}
