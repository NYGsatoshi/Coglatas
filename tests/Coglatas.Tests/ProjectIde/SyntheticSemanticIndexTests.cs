using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticSemanticIndexTests
{
    private static CompilerIndexedSymbol RootPort(SyntheticWorkbenchViewModel vm) => Assert.Single(vm.SemanticIndex!.Symbols,
        item => item.Symbol.Context.Equals(vm.Source!.Context) && item.Symbol.EntityId == EntityId.Parse(Id(7)) && item.Symbol.PortId == PortId.Parse(Id(8)));
    private static async Task<SyntheticWorkbenchViewModel> Ready(bool admit = true)
    {
        var fixture = CompilerBindingClosureTests.Graph(backToRoot: true, withRelation: true); var input = fixture.Input;
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var request = new CompilerHostRequest(new(input), 1, fixture.Snapshots); var vm = new SyntheticWorkbenchViewModel();
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
            if (action != "dispose") Assert.False(vm.NavigateIndexedReference(page, page.References[0]));
        }
        finally { vm.Dispose(); }
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
        Assert.Equal(result, vm.Result.Data);
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
