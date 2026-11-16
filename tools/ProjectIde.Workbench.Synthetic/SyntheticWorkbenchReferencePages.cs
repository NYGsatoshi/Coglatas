using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private sealed record ReferencePageOwner(ReferenceSearchPage Page, CompilerSemanticInspection Inspection,
        CompilerInspectedSymbol Symbol, CompilerResult Result, ProjectSource Root, long RootGeneration, long QueryGeneration);
    private ReferencePageOwner? _referencePageOwner;
    private long _referencePageGeneration;
    private long? _referencePageNavigationSelection;
    private CompilerInspectedSymbol? CurrentReferencePageSymbol => Selected ?? ReferenceSymbol ?? DiagnosticSymbol;
    public ReferenceSearchPage? ReferencePage
    {
        get
        {
            var owner = _referencePageOwner;
            return !_disposed && State == "Ready" && owner is not null && owner.QueryGeneration == _referencePageGeneration &&
                owner.RootGeneration == _rootSourceGeneration && ReferenceEquals(owner.Inspection, Inspection) &&
                ReferenceEquals(owner.Result, Result) && ReferenceEquals(owner.Root, RootSource) &&
                ReferenceEquals(owner.Symbol, CurrentReferencePageSymbol) ? owner.Page : null;
        }
    }

    /// <summary>Bounded Core pages from the explicitly admitted current root; current runtime authority remains separate.</summary>
    public ReferenceSearchPage? ReadReferenceSearchPage(int pageSize = CompilerSemanticInspection.MaximumReferenceSearchPageSize,
        ReferenceSearchCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearReferenceSearchPage();
        var query = _referencePageGeneration; var lifecycle = _lifecycle; var selection = _selectionGeneration;
        var rootGeneration = _rootSourceGeneration; var root = RootSource; var inspection = Inspection;
        var symbol = CurrentReferencePageSymbol; var compilation = _compilation; var request = _desired;
        bool Current() => !_disposed && State == "Ready" && query == _referencePageGeneration && lifecycle == _lifecycle &&
            selection == _selectionGeneration && rootGeneration == _rootSourceGeneration && ReferenceEquals(root, RootSource) &&
            ReferenceEquals(inspection, Inspection) && ReferenceEquals(symbol, CurrentReferencePageSymbol) &&
            ReferenceEquals(compilation, _compilation) && ReferenceEquals(request, _desired);
        Changed();
        if (pageSize is < 1 or > CompilerSemanticInspection.MaximumReferenceSearchPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current() || root is null || inspection is null || symbol is null || compilation is null || request is null ||
            !ReferenceEquals(Result, compilation.Result) || !root.Data.Equals(compilation.Input.Source.Data) ||
            Resolve(symbol.Symbol.Location, requireSourceMap: true) != "ready") return null;
        var page = inspection.FindReferencesPage(symbol.Symbol.Location, request.Input.ExpectedInputDigest,
            pageSize, cursor, cancellationToken);
        foreach (var reference in page.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Resolve(reference.Origin, requireSourceMap: true) != "ready")
            {
                if (Current()) { NavigationState = "unavailable"; Changed(); }
                return null;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current()) return null;
        var owner = new ReferencePageOwner(page, inspection, symbol, compilation.Result, root, rootGeneration, query);
        _referencePageOwner = owner; Changed();
        if (cancellationToken.IsCancellationRequested)
        {
            if (Current() && ReferenceEquals(_referencePageOwner, owner)) { ClearReferenceSearchPage(); Changed(); }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return Current() && ReferenceEquals(ReferencePage, page) ? page : null;
    }

    /// <summary>Open only an actual original reference in the owned current page while retaining its symbol anchor.</summary>
    public bool NavigateReferencePageOrigin(ReferenceSearchPage page, CompilerBoundReference reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(reference);
        var generation = ++_selectionGeneration;
        SourceTarget = null; ClearScopedSelection(); _referencePageNavigationSelection = null;
        NavigationState = "unavailable";
        if (ReferenceEquals(ReferencePage, page) && page.State == ReferenceSearchState.Ready &&
            page.References.Any(item => ReferenceEquals(item, reference)))
        {
            NavigationState = Resolve(reference.Origin, requireSourceMap: true);
            if (NavigationState == "ready") { SourceTarget = reference.Origin; _referencePageNavigationSelection = generation; }
        }
        Changed();
        if (!ReferenceEquals(ReferencePage, page) && _referencePageNavigationSelection == generation && _selectionGeneration == generation)
        { SourceTarget = null; NavigationState = "unavailable"; _referencePageNavigationSelection = null; Changed(); }
        return !_disposed && generation == _selectionGeneration && ReferenceEquals(ReferencePage, page) &&
            ReferenceEquals(SourceTarget, reference.Origin);
    }

    private void ClearReferenceSearchPage()
    {
        _referencePageGeneration++; _referencePageOwner = null;
        if (_referencePageNavigationSelection is { } selected && selected == _selectionGeneration)
        { SourceTarget = null; NavigationState = ""; }
        _referencePageNavigationSelection = null;
    }
}
