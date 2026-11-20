using System.Reflection;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticSemanticIndexTests
{
    private static CompilerIndexedSymbol RootPort(SyntheticWorkbenchViewModel vm) => Assert.Single(vm.SemanticIndex!.Symbols,
        item => item.Symbol.Context.Equals(vm.Source!.Context) && item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8)));
    private static async Task<SyntheticWorkbenchViewModel> Ready(bool admit = true, ProjectSemanticIndexLookupCache? cache = null)
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true); var input = fixture.Input;
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var request = new CompilerHostRequest(new(input), 1, fixture.Snapshots);
        var vm = cache is null ? new SyntheticWorkbenchViewModel() : new SyntheticWorkbenchViewModel(cache);
        try
        {
            vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await new LocalNetCompilerHost(profile).CompileAsync(request)));
            if (admit) { Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.LoadSyntheticSemanticIndex()); Assert.True(vm.SelectIndexedSymbol(RootPort(vm))); }
            return vm;
        }
        catch { vm.Dispose(); throw; }
    }
    [Fact]
    public async Task FullIndexRequiresExplicitOriginalSourceAdmissionAndUsesActualCrossRegionQueriesAndNavigation()
    {
        using var vm = await Ready(false); Assert.False(vm.LoadSyntheticSemanticIndex()); Assert.Null(vm.SemanticIndex);
        Assert.True(vm.LoadSyntheticScopedSources()); Assert.True(vm.LoadSyntheticSemanticIndex()); Assert.True(vm.SelectIndexedSymbol(RootPort(vm)));
        var result = vm.Result!.Data; var index = vm.SemanticIndex!;
        var completion = Assert.IsType<CompilerIndexCompletionPage>(vm.ReadIndexedCompletion()); Assert.Equal(CompilerIndexQueryState.Ready, completion.State);
        var foreign = completion.Candidates.First(item => !item.Symbol.Context.Equals(vm.Source!.Context));
        Assert.True(vm.NavigateIndexedSymbol(foreign)); Assert.Equal(foreign.Symbol.Context, vm.ScopedSource!.Context); Assert.Same(completion, vm.IndexedCompletion);
        var transitive = index.Symbols.Single(item => item.Symbol.EntityId == EntityId.Parse(Id(77)) && item.Symbol.PortId == PortId.Parse(Id(78)));
        Assert.True(vm.SelectIndexedSymbol(transitive)); var references = Assert.IsType<CompilerIndexReferencePage>(vm.ReadIndexedReferences());
        var origin = Assert.Single(references.References); Assert.True(vm.NavigateIndexedReference(references, origin)); Assert.Equal(origin.Origin, vm.ScopedSourceTarget);
        var definition = Assert.IsType<CompilerIndexDefinition>(vm.ReadIndexedDefinition(origin)); Assert.Equal(CompilerIndexQueryState.Ready, definition.State);
        Assert.True(vm.NavigateIndexedSymbol(definition.Target!)); Assert.Equal(transitive.Symbol.Location, vm.ScopedSourceTarget);
        Assert.False(vm.NavigateIndexedReference(references, origin)); Assert.Null(vm.ScopedSourceTarget);
        Assert.Same(index, vm.SemanticIndex); Assert.Equal(result, vm.Result.Data); Assert.NotEqual(CompilerVerdict.Pass, vm.Result.Verdict);
    }
    [Theory]
    [InlineData("sources")] [InlineData("selection")] [InlineData("root")] [InlineData("reset")] [InlineData("dispose")] [InlineData("index")]
    public async Task ReplacingAnOwningBoundaryWithdrawsQueriesAndTheirOwnedSourceTarget(string action)
    {
        var vm = await Ready();
        try
        {
            var symbol = RootPort(vm); var page = Assert.IsType<CompilerIndexReferencePage>(vm.ReadIndexedReferences());
            Assert.True(vm.NavigateIndexedReference(page, page.References[0]));
            switch (action)
            {
                case "sources": Assert.True(vm.LoadSyntheticScopedSources()); break;
                case "selection": vm.Select(symbol.Symbol.Location); break;
                case "root": Assert.True(vm.LoadSyntheticRootSource(vm.Result!.ResultDigest)); break;
                case "reset": vm.Reset(); break;
                case "dispose": vm.Dispose(); break;
                case "index": Assert.True(vm.LoadSyntheticSemanticIndex()); break;
            }
            Assert.Null(vm.IndexedReferences); Assert.Null(vm.SelectedIndexedSymbol); Assert.Null(vm.ScopedSourceTarget);
            Assert.Null(vm.IndexedReferenceGraph);
            if (action != "dispose") Assert.False(vm.NavigateIndexedReference(page, page.References[0]));
        }
        finally { vm.Dispose(); }
    }
    [Fact]
    public async Task ReferenceGraphKeepsTheOriginalPageEdgesAndNavigatesTheirActualRegionalSourceAndDefinition()
    {
        using var vm = await Ready();
        var target = Assert.Single(vm.SemanticIndex!.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(77)) && item.Symbol.PortId == PortId.Parse(Id(78)));
        Assert.True(vm.SelectIndexedSymbol(target)); var page = Assert.IsType<CompilerIndexReferencePage>(vm.ReadIndexedReferences());
        var graph = Assert.IsType<SyntheticReferenceGraphPage>(vm.IndexedReferenceGraph); var edge = Assert.Single(graph.Edges);
        Assert.Same(page, graph.Page); Assert.Same(target, graph.Target); Assert.Same(page.References[0], edge);
        Assert.NotEqual(vm.Source!.Context, edge.Origin.Context);
        Assert.Throws<NotSupportedException>(() => ((IList<CompilerBoundReference>)graph.Edges).Clear());
        var result = vm.Result!.Data; var source = vm.Source.Data;
        Assert.True(vm.NavigateIndexedGraphOrigin(graph, edge)); Assert.Equal(edge.Origin, vm.ScopedSourceTarget);
        Assert.True(vm.NavigateIndexedGraphTarget(graph)); Assert.Equal(target.Symbol.Location, vm.ScopedSourceTarget);
        Assert.False(vm.NavigateIndexedGraphOrigin(graph, edge with { })); Assert.Equal(target.Symbol.Location, vm.ScopedSourceTarget);
        Assert.NotNull(vm.ReadIndexedReferences()); var newer = Assert.IsType<SyntheticReferenceGraphPage>(vm.IndexedReferenceGraph);
        Assert.NotSame(graph, newer); Assert.True(vm.NavigateIndexedGraphTarget(newer));
        Assert.False(vm.NavigateIndexedGraphOrigin(graph, edge)); Assert.False(vm.NavigateIndexedGraphTarget(graph));
        Assert.Same(newer, vm.IndexedReferenceGraph); Assert.Equal(target.Symbol.Location, vm.ScopedSourceTarget);
        Assert.Equal(result, vm.Result.Data); Assert.Equal(source, vm.Source.Data);
    }
    [Fact]
    public async Task EmptyAndRefusedReferencePagesKeepDifferentGraphStates()
    {
        using var vm = await Ready();
        using var other = await Ready();
        var index = vm.SemanticIndex!;
        var unused = index.Symbols.First(symbol => { var page = index.FindReferences(symbol.Symbol.Location, index.Input.InputDigest); return page.State == CompilerIndexQueryState.Ready && page.References.Count == 0; });
        Assert.True(vm.SelectIndexedSymbol(unused)); var empty = Assert.IsType<CompilerIndexReferencePage>(vm.ReadIndexedReferences());
        var graph = Assert.IsType<SyntheticReferenceGraphPage>(vm.IndexedReferenceGraph);
        Assert.Empty(graph.Edges); Assert.Same(empty, graph.Page); Assert.True(empty.ScanComplete);
        Assert.Same(unused, graph.Target); Assert.True(vm.NavigateIndexedGraphTarget(graph));
        var foreignCursor = other.SemanticIndex!.Symbols.Select(symbol => other.SemanticIndex.FindReferences(symbol.Symbol.Location, other.SemanticIndex.Input.InputDigest, 1))
            .First(page => page.NextCursor is not null).NextCursor; Assert.NotNull(foreignCursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, vm.ReadIndexedReferences(cursor: foreignCursor)!.State);
        Assert.Null(vm.IndexedReferenceGraph); Assert.Null(vm.ScopedSourceTarget);
        Assert.False(vm.NavigateIndexedGraphTarget(graph));
    }
    [Fact]
    public async Task CopiedOrForeignSymbolsReferencesAndCursorsCannotBorrowCurrentIndexOwnership()
    {
        using var vm = await Ready(); using var other = await Ready(); var original = RootPort(vm);
        Assert.False(vm.SelectIndexedSymbol(original with { })); Assert.False(vm.SelectIndexedSymbol(RootPort(other)));
        Assert.True(vm.SelectIndexedSymbol(original));
        var cursor = Assert.IsType<CompilerIndexCompletionPage>(other.ReadIndexedCompletion(1)).NextCursor; Assert.NotNull(cursor);
        Assert.Equal(CompilerIndexQueryState.InvalidCursor, vm.ReadIndexedCompletion(cursor: cursor)!.State);
        var reference = vm.SemanticIndex!.References[0];
        Assert.Equal(CompilerIndexQueryState.Unavailable, vm.ReadIndexedDefinition(reference with { })!.State);
        Assert.Null(vm.IndexedDefinition!.Target); Assert.Null(vm.ScopedSourceTarget);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task CancelledOlderInitialOrPublishedIndexReadPreservesTheNewerOwnedIndex(bool initial)
    {
        using var vm = await Ready();
        using var probe = new IndexReplacementProbe(vm, initial);
        var result = vm.Result!.Data;
        Assert.Throws<OperationCanceledException>(() => vm.LoadSyntheticSemanticIndex(probe.Token));
        Assert.False(probe.Armed); Assert.Same(probe.Newer, vm.SemanticIndex);
        Assert.NotNull(vm.IndexedCompletion); Assert.NotNull(vm.ScopedSourceTarget); Assert.Equal(result, vm.Result.Data);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task CancelledOlderQueryPublicationPreservesNewerReadyOrRefusedPages(bool ready)
    {
        using var vm = await Ready();
        using var other = await Ready();
        var foreignCursor = other.ReadIndexedCompletion(1)!.NextCursor; Assert.NotNull(foreignCursor);
        using var probe = new QueryReplacementProbe(vm, ready, foreignCursor);
        var result = vm.Result!.Data;
        Assert.Throws<OperationCanceledException>(() => vm.ReadIndexedReferences(cancellationToken: probe.Token));
        Assert.False(probe.Armed); Assert.Same(probe.Newer, vm.IndexedReferences);
        Assert.Equal(ready ? CompilerIndexQueryState.Ready : CompilerIndexQueryState.InvalidCursor, probe.Newer!.State);
        if (ready) Assert.NotNull(vm.ScopedSourceTarget); else Assert.Null(vm.ScopedSourceTarget);
        if (ready) Assert.Same(probe.Newer, vm.IndexedReferenceGraph!.Page); else Assert.Null(vm.IndexedReferenceGraph);
        Assert.Equal(result, vm.Result.Data);
    }

    [Theory]
    [InlineData("Loading", false)] [InlineData("Loading", true)]
    [InlineData("Ready", false)] [InlineData("Ready", true)]
    public Task ThrowingIndexObserverWithdrawsOnlyItsCancelledView(string boundary, bool replace) =>
        VerifyIndexWithdrawal(boundary, replace, throwOnPublication: true);

    [Theory]
    [InlineData("Loading")]
    [InlineData("Ready")]
    public Task ThrowingIndexWithdrawalObserverPreservesCancellation(string boundary) =>
        VerifyIndexWithdrawal(boundary, replace: false, throwOnPublication: false);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CancelledIndexRefusalWithdrawsOnlyItsOwnedState(bool replace, bool throwOnPublication)
    {
        using var vm = await Ready(admit: false);
        var result = vm.Result;
        var source = vm.Source;
        CompilerSemanticIndex? replacement = null;

        void ReplaceIndex()
        {
            if (!replace) return;
            Assert.True(vm.LoadSyntheticScopedSources());
            Assert.True(vm.LoadSyntheticSemanticIndex());
            replacement = Assert.IsType<CompilerSemanticIndex>(vm.SemanticIndex);
        }

        using var observer = new ThrowingObserver(vm, () => vm.SemanticIndexReadState == "Unavailable",
            () => vm.SemanticIndexReadState == "Idle", ReplaceIndex, throwOnPublication);
        var error = Record.Exception(() => { vm.LoadSyntheticSemanticIndex(observer.Token); });

        Assert.True(observer.Token.IsCancellationRequested);
        Assert.Same(result, vm.Result);
        Assert.Same(source, vm.Source);
        Assert.Equal(replace ? "Ready" : "Idle", vm.SemanticIndexReadState);
        Assert.Same(replacement, vm.SemanticIndex);
        Assert.Equal(replace ? 0 : 1, observer.WithdrawalNotifications);
        if (!replace) AssertObserverError(observer, error, throwOnPublication);
        else
        {
            Assert.NotNull(replacement);
            var cancelled = Assert.IsType<OperationCanceledException>(error);
            Assert.Equal(observer.Token, cancelled.CancellationToken);
            Assert.Null(cancelled.InnerException);
        }
    }

    [Fact]
    public async Task IndexBuildCancellationSurvivesThrowingWithdrawalObserver()
    {
        var cache = new ProjectSemanticIndexLookupCache();
        using var vm = await Ready(cache: cache);
        var result = vm.Result;
        var source = vm.Source;
        var prior = Assert.IsType<CompilerSemanticIndex>(vm.SemanticIndex);
        using var observer = new BuildCancellationObserver(vm);
        observer.CancelWhileWaitingForCache(cache);

        Assert.Same(result, vm.Result);
        Assert.Same(source, vm.Source);
        Assert.NotEmpty(prior.Symbols);
        Assert.Null(vm.SemanticIndex);
        Assert.Equal("Idle", vm.SemanticIndexReadState);
        Assert.Equal(1, observer.WithdrawalNotifications);
        var cancellation = Assert.IsType<OperationCanceledException>(observer.Error);
        Assert.Equal(observer.Token, cancellation.CancellationToken);
        Assert.Same(observer.WithdrawalError, cancellation.InnerException);
    }

    private sealed class BuildCancellationObserver : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly Thread _read;
        private readonly ManualResetEventSlim _loading = new();
        private readonly CancellationTokenSource _cancel = new();
        public CancellationToken Token => _cancel.Token;
        public Exception? Error { get; private set; }
        public int WithdrawalNotifications { get; private set; }
        public InvalidOperationException WithdrawalError { get; } = new("Semantic withdrawal observer failed.");

        public BuildCancellationObserver(SyntheticWorkbenchViewModel view)
        {
            _view = view;
            _view.PropertyChanged += Changed;
            _read = new Thread(Read) { IsBackground = true };
        }

        public void CancelWhileWaitingForCache(ProjectSemanticIndexLookupCache cache)
        {
            // Hold the actual cache gate until the reader reaches Build, after its Loading notification.
            var field = typeof(ProjectSemanticIndexLookupCache).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var gate = Assert.IsType<object>(field.GetValue(cache));
            try
            {
                lock (gate)
                {
                    _read.Start();
                    Assert.True(_loading.Wait(TimeSpan.FromSeconds(10)));
                    Assert.True(SpinWait.SpinUntil(() => (_read.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10)));
                    _cancel.Cancel();
                }
            }
            finally { Assert.True(_read.Join(TimeSpan.FromSeconds(10))); }
        }

        private void Read() => Error = Record.Exception(() => { _view.LoadSyntheticSemanticIndex(Token); });

        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (_view.SemanticIndexReadState == "Loading") _loading.Set();
            if (_view.SemanticIndexReadState != "Idle" || !Token.IsCancellationRequested) return;
            WithdrawalNotifications++;
            throw WithdrawalError;
        }

        public void Dispose()
        {
            _view.PropertyChanged -= Changed;
            _loading.Dispose();
            _cancel.Dispose();
        }
    }

    private static async Task VerifyIndexWithdrawal(string boundary, bool replace, bool throwOnPublication)
    {
        using var vm = await Ready();
        var result = vm.Result;
        var source = vm.Source;
        var original = Assert.IsType<CompilerSemanticIndex>(vm.SemanticIndex);
        CompilerSemanticIndex? replacement = null;
        CompilerIndexCompletionPage? completion = null;

        void PublishOrNavigate()
        {
            if (replace)
            {
                Assert.True(vm.LoadSyntheticSemanticIndex());
                replacement = Assert.IsType<CompilerSemanticIndex>(vm.SemanticIndex);
            }
            if (!replace && boundary == "Loading") return;
            Assert.True(vm.SelectIndexedSymbol(RootPort(vm)));
            completion = Assert.IsType<CompilerIndexCompletionPage>(vm.ReadIndexedCompletion());
            Assert.True(vm.NavigateIndexedSymbol(completion.Candidates[0]));
        }

        using var observer = new ThrowingObserver(vm, () => vm.SemanticIndexReadState == boundary,
            () => vm.SemanticIndexReadState == "Idle", PublishOrNavigate, throwOnPublication);
        var error = Record.Exception(() => { vm.LoadSyntheticSemanticIndex(observer.Token); });

        Assert.True(observer.Token.IsCancellationRequested);
        Assert.Same(result, vm.Result);
        Assert.Same(source, vm.Source);
        Assert.NotEmpty(original.Symbols);
        if (replace)
        {
            Assert.NotNull(replacement);
            Assert.NotSame(original, replacement);
            Assert.Same(replacement, vm.SemanticIndex);
            Assert.Equal("Ready", vm.SemanticIndexReadState);
            Assert.Same(completion, vm.IndexedCompletion);
            Assert.NotNull(vm.ScopedSourceTarget);
        }
        else
        {
            Assert.Equal("Idle", vm.SemanticIndexReadState);
            Assert.Null(vm.SemanticIndex);
            Assert.Null(vm.SelectedIndexedSymbol);
            Assert.Null(vm.IndexedCompletion);
            Assert.Null(vm.ScopedSourceTarget);
        }
        AssertObserverError(observer, error, throwOnPublication);
        Assert.Equal(replace ? 0 : 1, observer.WithdrawalNotifications);
    }

    [Theory]
    [InlineData("withdraw")] [InlineData("ready")] [InlineData("refused")]
    public Task ThrowingQueryObserverWithdrawsOnlyItsCancelledPage(string outcome) =>
        VerifyQueryWithdrawal(outcome, throwOnPublication: true);

    [Fact]
    public Task ThrowingQueryWithdrawalObserverPreservesCancellation() =>
        VerifyQueryWithdrawal("withdraw", throwOnPublication: false);

    private static async Task VerifyQueryWithdrawal(string outcome, bool throwOnPublication)
    {
        using var vm = await Ready();
        using var other = await Ready();
        var foreignCursor = Assert.IsType<CompilerIndexCursor>(other.ReadIndexedCompletion(1)!.NextCursor);
        var index = vm.SemanticIndex;
        var selected = vm.SelectedIndexedSymbol;
        var result = vm.Result;
        var earlier = Assert.IsType<CompilerIndexReferencePage>(vm.ReadIndexedReferences());
        var replace = outcome != "withdraw";
        var ready = outcome != "refused";
        CompilerIndexReferencePage? replacement = null;

        void PublishOrNavigate()
        {
            if (replace)
                replacement = Assert.IsType<CompilerIndexReferencePage>(vm.ReadIndexedReferences(cursor: ready ? null : foreignCursor));
            if (ready)
            {
                var page = Assert.IsType<CompilerIndexReferencePage>(vm.IndexedReferences);
                Assert.True(vm.NavigateIndexedReference(page, page.References[0]));
            }
        }

        using var observer = new ThrowingObserver(vm, () => vm.IndexedReferences is not null,
            () => vm.IndexedReferences is null, PublishOrNavigate, throwOnPublication);
        var error = Record.Exception(() => { vm.ReadIndexedReferences(cancellationToken: observer.Token); });

        Assert.True(observer.Token.IsCancellationRequested);
        Assert.Same(result, vm.Result);
        Assert.Same(index, vm.SemanticIndex);
        Assert.Same(selected, vm.SelectedIndexedSymbol);
        Assert.NotEmpty(earlier.References);
        if (replace)
        {
            Assert.NotNull(replacement);
            Assert.Same(replacement, vm.IndexedReferences);
            Assert.Equal(ready ? CompilerIndexQueryState.Ready : CompilerIndexQueryState.InvalidCursor, replacement.State);
        }
        else Assert.Null(vm.IndexedReferences);
        if (replace && ready)
        {
            Assert.Same(replacement, vm.IndexedReferenceGraph!.Page);
            Assert.NotNull(vm.ScopedSourceTarget);
        }
        else
        {
            Assert.Null(vm.IndexedReferenceGraph);
            Assert.Null(vm.ScopedSourceTarget);
        }
        AssertObserverError(observer, error, throwOnPublication);
        Assert.Equal(replace ? 0 : 1, observer.WithdrawalNotifications);
    }

    private static void AssertObserverError(ThrowingObserver observer, Exception? error, bool throwOnPublication)
    {
        if (throwOnPublication)
        {
            Assert.Same(observer.PublicationError, error);
            return;
        }
        var cancelled = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(observer.Token, cancelled.CancellationToken);
        Assert.Same(observer.WithdrawalError, cancelled.InnerException);
    }

    private sealed class ThrowingObserver : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly Func<bool> _isPublished;
        private readonly Func<bool> _isWithdrawn;
        private readonly Action _published;
        private readonly bool _throwOnPublication;
        private readonly CancellationTokenSource _source = new();
        private bool _fired;

        public CancellationToken Token => _source.Token;
        public InvalidOperationException PublicationError { get; } = new("Semantic publication observer failed.");
        public InvalidOperationException WithdrawalError { get; } = new("Semantic withdrawal observer failed.");
        public int WithdrawalNotifications { get; private set; }

        public ThrowingObserver(SyntheticWorkbenchViewModel view, Func<bool> isPublished, Func<bool> isWithdrawn,
            Action published, bool throwOnPublication)
        {
            _view = view;
            _isPublished = isPublished;
            _isWithdrawn = isWithdrawn;
            _published = published;
            _throwOnPublication = throwOnPublication;
            _view.PropertyChanged += Changed;
        }

        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (_fired)
            {
                if (Token.IsCancellationRequested)
                {
                    WithdrawalNotifications++;
                    if (_isWithdrawn()) throw WithdrawalError;
                }
                return;
            }
            if (!_isPublished()) return;
            _fired = true;
            _published();
            _source.Cancel();
            if (_throwOnPublication) throw PublicationError;
        }

        public void Dispose()
        {
            _view.PropertyChanged -= Changed;
            _source.Dispose();
        }
    }

    private sealed class IndexReplacementProbe : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly bool _initial;
        private readonly CancellationTokenSource _cancelled = new();
        public CancellationToken Token => _cancelled.Token;
        public bool Armed { get; private set; } = true;
        public CompilerSemanticIndex? Newer { get; private set; }
        public IndexReplacementProbe(SyntheticWorkbenchViewModel view, bool initial)
        { _view = view; _initial = initial; _view.PropertyChanged += Changed; }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!Armed || _view.SemanticIndexReadState != (_initial ? "Loading" : "Ready")) return;
            Armed = false; _cancelled.Cancel(); Assert.True(_view.LoadSyntheticSemanticIndex()); Newer = _view.SemanticIndex;
            Assert.True(_view.SelectIndexedSymbol(RootPort(_view)));
            var page = Assert.IsType<CompilerIndexCompletionPage>(_view.ReadIndexedCompletion());
            Assert.True(_view.NavigateIndexedSymbol(page.Candidates[0]));
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _cancelled.Dispose(); }
    }
    private sealed class QueryReplacementProbe : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly bool _ready;
        private readonly CompilerIndexCursor _foreignCursor;
        private readonly CancellationTokenSource _cancelled = new();
        public CancellationToken Token => _cancelled.Token;
        public bool Armed { get; private set; } = true;
        public CompilerIndexReferencePage? Newer { get; private set; }
        public QueryReplacementProbe(SyntheticWorkbenchViewModel view, bool ready, CompilerIndexCursor foreignCursor)
        { _view = view; _ready = ready; _foreignCursor = foreignCursor; _view.PropertyChanged += Changed; }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!Armed || _view.IndexedReferences is null) return;
            Armed = false; _cancelled.Cancel(); Newer = _view.ReadIndexedReferences(cursor: _ready ? null : _foreignCursor);
            if (_ready) Assert.True(_view.NavigateIndexedReference(Newer!, Newer!.References[0]));
        }
        public void Dispose() { _view.PropertyChanged -= Changed; _cancelled.Dispose(); }
    }
}
