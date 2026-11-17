using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private sealed record SemanticIndexOwner(CompilerSemanticIndex Index, CoreCompilation Compilation, long ScopedGeneration);
    private sealed record SemanticQueryOwner(CompilerSemanticIndex Index, CompilerIndexedSymbol Symbol, long Generation);
    private SemanticIndexOwner? _semanticIndexOwner;
    private SemanticQueryOwner? _semanticQueryOwner;
    private long _semanticIndexGeneration;
    private long _semanticQueryGeneration;
    private long? _semanticNavigationSelection;
    private CompilerIndexedSymbol? _indexedSymbol;
    private CompilerIndexCompletionPage? _indexedCompletion;
    private CompilerIndexReferencePage? _indexedReferences;
    private CompilerIndexDefinition? _indexedDefinition;
    public string SemanticIndexReadState { get; private set; } = "Idle";
    public CompilerSemanticIndex? SemanticIndex => CurrentScopedSources && _semanticIndexOwner is { } owner &&
        owner.ScopedGeneration == _scopedGeneration && ReferenceEquals(owner.Compilation, _compilation) ? owner.Index : null;
    public CompilerIndexedSymbol? SelectedIndexedSymbol => SemanticIndex is null ? null : _indexedSymbol;
    private bool CurrentSemanticQuery => _semanticQueryOwner is { } owner && owner.Generation == _semanticQueryGeneration &&
        ReferenceEquals(owner.Index, SemanticIndex) && ReferenceEquals(owner.Symbol, SelectedIndexedSymbol);
    public CompilerIndexCompletionPage? IndexedCompletion => CurrentSemanticQuery ? _indexedCompletion : null;
    public CompilerIndexReferencePage? IndexedReferences => CurrentSemanticQuery ? _indexedReferences : null;
    public CompilerIndexDefinition? IndexedDefinition => CurrentSemanticQuery ? _indexedDefinition : null;

    /// <summary>Explicit synthetic whole-scope read after original regional Source admission; no runtime authority is inferred.</summary>
    public bool LoadSyntheticSemanticIndex(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ClearSemanticIndex();
        var generation = _semanticIndexGeneration; var scoped = _scopedGeneration; var compilation = _compilation; var desired = _desired;
        bool Current() => !_disposed && generation == _semanticIndexGeneration && scoped == _scopedGeneration && CurrentScopedSources &&
            compilation is not null && ReferenceEquals(compilation, _compilation) && ReferenceEquals(desired, _desired);
        SemanticIndexReadState = "Loading"; Changed();
        if (token.IsCancellationRequested)
        { if (generation == _semanticIndexGeneration && scoped == _scopedGeneration) { ClearSemanticIndex(); Changed(); } token.ThrowIfCancellationRequested(); }
        if (!Current() || compilation?.Dependencies is not { } dependencies || desired is null)
        { if (generation == _semanticIndexGeneration) { SemanticIndexReadState = "Unavailable"; Changed(); } return false; }
        CompilerSemanticIndex index;
        try { index = CompilerSemanticIndex.Build(dependencies.Binding, desired.Input.ExpectedInputDigest, cancellationToken: token); }
        catch (OperationCanceledException)
        { if (Current()) { ClearSemanticIndex(); Changed(); } throw; }
        if (!Current()) return false;
        if (index.State != CompilerSemanticIndexState.Complete || !index.IndexCoverageComplete)
        { SemanticIndexReadState = "Unavailable"; Changed(); return false; }
        var owner = new SemanticIndexOwner(index, compilation, scoped); _semanticIndexOwner = owner; SemanticIndexReadState = "Ready"; Changed();
        if (token.IsCancellationRequested)
        { if (ReferenceEquals(_semanticIndexOwner, owner)) { ClearSemanticIndex(); Changed(); } token.ThrowIfCancellationRequested(); }
        return Current() && ReferenceEquals(SemanticIndex, index);
    }
    public bool SelectIndexedSymbol(CompilerIndexedSymbol symbol)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(symbol);
        var index = SemanticIndex; var selection = ClearSelection();
        if (index is null || !index.Symbols.Any(item => ReferenceEquals(item, symbol))) { NavigationState = "unavailable"; Changed(); return false; }
        _indexedSymbol = symbol; Changed();
        return selection == _selectionGeneration && ReferenceEquals(index, SemanticIndex) && ReferenceEquals(SelectedIndexedSymbol, symbol);
    }
    private T? ReadSemanticQuery<T>(Func<CompilerSemanticIndex, CompilerIndexedSymbol, T> read, Action<T> publish, CancellationToken token) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ClearSemanticQueries();
        var generation = _semanticQueryGeneration; var index = SemanticIndex; var symbol = SelectedIndexedSymbol;
        bool Current() => !_disposed && generation == _semanticQueryGeneration && index is not null && symbol is not null &&
            ReferenceEquals(index, SemanticIndex) && ReferenceEquals(symbol, SelectedIndexedSymbol);
        Changed(); token.ThrowIfCancellationRequested(); if (!Current()) return null;
        var value = read(index!, symbol!); token.ThrowIfCancellationRequested(); if (!Current()) return null;
        var owner = new SemanticQueryOwner(index!, symbol!, generation); _semanticQueryOwner = owner; publish(value); Changed();
        if (token.IsCancellationRequested)
        { if (ReferenceEquals(_semanticQueryOwner, owner)) { ClearSemanticQueries(); Changed(); } token.ThrowIfCancellationRequested(); }
        return Current() && ReferenceEquals(_semanticQueryOwner, owner) ? value : null;
    }
    public CompilerIndexCompletionPage? ReadIndexedCompletion(int pageSize = CompilerSemanticIndex.MaximumPageSize,
        CompilerIndexCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > CompilerSemanticIndex.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        return ReadSemanticQuery((index, symbol) => index.CompleteEntityReference(symbol.Symbol.Location, index.Input.InputDigest, pageSize, cursor, cancellationToken),
            value => _indexedCompletion = value, cancellationToken);
    }
    public CompilerIndexReferencePage? ReadIndexedReferences(int pageSize = CompilerSemanticIndex.MaximumPageSize,
        CompilerIndexCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > CompilerSemanticIndex.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        return ReadSemanticQuery((index, symbol) => index.FindReferences(symbol.Symbol.Location, index.Input.InputDigest, pageSize, cursor, cancellationToken),
            value => _indexedReferences = value, cancellationToken);
    }
    public CompilerIndexDefinition? ReadIndexedDefinition(CompilerBoundReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return ReadSemanticQuery<CompilerIndexDefinition>((index, _) => index.References.Any(item => ReferenceEquals(item, reference))
                ? index.FindDefinition(reference.Origin, index.Input.InputDigest, cancellationToken) : new(CompilerIndexQueryState.Unavailable, null, null),
            value => _indexedDefinition = value, cancellationToken);
    }
    /// <summary>Navigate only a current original index symbol, query candidate or definition. Original regional maps remain required.</summary>
    public bool NavigateIndexedSymbol(CompilerIndexedSymbol symbol)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(symbol);
        var index = SemanticIndex; var selection = ++_selectionGeneration; ClearScopedSelection(); _semanticNavigationSelection = null; NavigationState = "unavailable";
        if (index is not null && index.Symbols.Any(item => ReferenceEquals(item, symbol)) &&
            (ReferenceEquals(symbol, SelectedIndexedSymbol) || IndexedCompletion?.Candidates.Any(item => ReferenceEquals(item, symbol)) == true || ReferenceEquals(IndexedDefinition?.Target, symbol)))
            ResolveIndexedLocation(symbol.Symbol.Location, symbol.IsDocument);
        if (NavigationState == "ready") _semanticNavigationSelection = selection;
        Changed();
        if ((!ReferenceEquals(index, SemanticIndex) || SelectedIndexedSymbol is null) && _semanticNavigationSelection == selection && selection == _selectionGeneration)
        { ClearScopedSelection(); _semanticNavigationSelection = null; NavigationState = "unavailable"; Changed(); }
        return selection == _selectionGeneration && ReferenceEquals(index, SemanticIndex) && _semanticNavigationSelection == selection && ScopedSourceTarget is not null;
    }
    public bool NavigateIndexedReference(CompilerIndexReferencePage page, CompilerBoundReference reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(reference);
        var selection = ++_selectionGeneration; ClearScopedSelection(); _semanticNavigationSelection = null; NavigationState = "unavailable";
        if (ReferenceEquals(IndexedReferences, page) && page.State == CompilerIndexQueryState.Ready && page.References.Any(item => ReferenceEquals(item, reference)))
            ResolveIndexedLocation(reference.Origin, false);
        if (NavigationState == "ready") _semanticNavigationSelection = selection;
        Changed();
        if (!ReferenceEquals(IndexedReferences, page) && _semanticNavigationSelection == selection && selection == _selectionGeneration)
        { ClearScopedSelection(); _semanticNavigationSelection = null; NavigationState = "unavailable"; Changed(); }
        return selection == _selectionGeneration && ReferenceEquals(IndexedReferences, page) && _semanticNavigationSelection == selection && ScopedSourceTarget is not null;
    }
    private void ResolveIndexedLocation(ProjectLocation location, bool document)
    {
        if (!CurrentScopedSources) return;
        if (!document) { ResolveScopedSourceTarget(location); return; }
        var region = _scopedSources!.Regions.SingleOrDefault(item => item.Binding.Source.Context.Equals(location.Context));
        var original = region?.Binding.Source.Documents.SingleOrDefault(item => item.DocumentId == location.DocumentId);
        if (original is null) return;
        ResolveScopedSourceTarget(ProjectLocation.Create(original.DocumentId, location.Context, entityId: original.Root.EntityId));
        if (NavigationState == "ready") ScopedSourceTarget = location;
    }
    private void ClearSemanticQueries()
    {
        _semanticQueryGeneration++; _semanticQueryOwner = null; _indexedCompletion = null; _indexedReferences = null; _indexedDefinition = null;
        if (_semanticNavigationSelection == _selectionGeneration) { ClearScopedSelection(); NavigationState = ""; }
        _semanticNavigationSelection = null;
    }
    private void ClearSemanticIndexSelection() { ClearSemanticQueries(); _indexedSymbol = null; }
    private void ClearSemanticIndex()
    { _semanticIndexGeneration++; ClearSemanticIndexSelection(); _semanticIndexOwner = null; SemanticIndexReadState = "Idle"; }
}
