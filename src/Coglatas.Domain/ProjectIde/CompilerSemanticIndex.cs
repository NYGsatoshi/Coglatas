namespace Coglatas.Domain.ProjectIde;

public enum CompilerSemanticIndexState { Complete, Incomplete, StaleInput, ResourceLimit }
public enum CompilerIndexQueryState { Ready, StaleInput, OutOfScope, Unavailable, Unsupported, Unresolved, InvalidCursor, ResourceLimit }

public sealed record CompilerIndexedSymbol(CompilerSymbol Symbol, SourceJson? DeclaredType, string? DisplayName, bool IsDocument);

/// <summary>In-process continuation owned by one immutable index, query kind and exact anchor. No authority is granted.</summary>
public sealed class CompilerIndexCursor
{
    internal CompilerSemanticIndex Owner { get; }
    internal string Query { get; }
    internal string Anchor { get; }
    internal int Offset { get; }
    internal CompilerIndexCursor(CompilerSemanticIndex owner, string query, string anchor, int offset)
    { Owner = owner; Query = query; Anchor = anchor; Offset = offset; }
}

public sealed record CompilerIndexDefinition(CompilerIndexQueryState State, CompilerIndexedSymbol? Target, ReferenceResolutionState? Resolution);
public sealed record CompilerIndexCompletionPage(CompilerIndexQueryState State, ContentDigest? InputDigest, ProjectLocation? Origin,
    string? ExpectedKind, IReadOnlyList<CompilerIndexedSymbol> Candidates, bool IndexCoverageComplete, int ScannedSymbols, CompilerIndexCursor? NextCursor)
{
    public bool ScanComplete => State == CompilerIndexQueryState.Ready && NextCursor is null;
}
public sealed record CompilerIndexReferencePage(CompilerIndexQueryState State, ContentDigest? InputDigest, ProjectLocation? Target,
    IReadOnlyList<CompilerBoundReference> References, bool IndexCoverageComplete, int ScannedReferences, CompilerIndexCursor? NextCursor)
{
    public bool ScanComplete => State == CompilerIndexQueryState.Ready && NextCursor is null;
}

/// <summary>Fresh full-reference semantic index over actual admitted binding regions. It grants no current query/open/apply authority.</summary>
public sealed class CompilerSemanticIndex
{
    public const string Format = "coglatas.semantic-index/1";
    public const int MaximumPageSize = 32;
    public const int MaximumScan = 256;
    public CompilerInputManifest Input { get; }
    public ContentDigest BindingClosureDigest { get; }
    public CompilerSemanticIndexState State { get; }
    public bool IncludesDeclaredSnapshots { get; }
    public bool IndexCoverageComplete { get; }
    public IReadOnlyList<CompilerBindingRegion> Regions { get; }
    public IReadOnlyList<CompilerIndexedSymbol> Symbols { get; }
    public IReadOnlyList<CompilerBoundReference> References { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    /// <summary>Separate derived-index work; original Compiler logical operations and receipts remain unchanged.</summary>
    public int LogicalOperationsConsumed { get; }
    private readonly SemanticIndexLookupTables _lookups;
    private readonly HashSet<SourceRevisionContext> _contexts;

    private CompilerSemanticIndex(CompilerBindingClosure binding, CompilerSemanticIndexState state,
        IEnumerable<CompilerBindingRegion> regions, IEnumerable<CompilerIndexedSymbol> symbols, IEnumerable<CompilerBoundReference> references,
        int maximumOperations, ref int remaining, bool charge, CancellationToken token, ProjectSemanticIndexLookupCache? lookupCache = null)
    {
        Input = binding.Input; BindingClosureDigest = binding.Digest; State = state; IncludesDeclaredSnapshots = binding.IncludesDeclaredSnapshots;
        Regions = new ImmutableListView<CompilerBindingRegion>(regions);
        Symbols = new ImmutableListView<CompilerIndexedSymbol>(symbols.OrderBy(item => item.Symbol.Key, StringComparer.Ordinal));
        References = new ImmutableListView<CompilerBoundReference>(references.OrderBy(item => item.Origin.Data.CanonicalText, StringComparer.Ordinal)
            .ThenBy(item => item.State).ThenBy(item => item.SymbolKey, StringComparer.Ordinal));
        _contexts = Regions.Select(region => region.Source.Context).ToHashSet();
        var preparedLookups = lookupCache?.Prepare(Input.Source.Context.Branch, LookupDependencies(Symbols, References),
            () => new SemanticIndexLookupTables(Symbols, References), token);
        _lookups = preparedLookups?.Tables ?? new SemanticIndexLookupTables(Symbols, References);
        IndexCoverageComplete = state == CompilerSemanticIndexState.Complete && IncludesDeclaredSnapshots &&
            _lookups.KeyCount == Symbols.Count && _lookups.LocationCount == Symbols.Count && Symbols.All(item => item.Symbol.Status == ValueDecodeStatus.Decoded);
        Data = SourceJson.FromObject(new {
            format = Format, compilerInputId = Input.InputId.ToString(), rootInputDigest = Input.InputDigest.Data.Value,
            bindingClosureDigest = BindingClosureDigest.Data.Value, state = State.ToString(), includesDeclaredSnapshots = IncludesDeclaredSnapshots,
            indexCoverageComplete = IndexCoverageComplete,
            regions = Regions.Select(region => new { context = region.Source.Context.Data.Value, sourceDigest = region.Source.Digest.Data.Value,
                bindingRegionDigest = region.Digest.Data.Value, bindingComplete = region.BindingComplete }).ToArray(),
            symbols = Symbols.Select(item => new { key = item.Symbol.Key, kind = item.Symbol.Kind, status = item.Symbol.Status.ToString(),
                location = item.Symbol.Location.Data.Value, declaredType = item.DeclaredType?.Value, displayName = item.DisplayName, isDocument = item.IsDocument }).ToArray(),
            references = References.Select(item => new { origin = item.Origin.Data.Value, state = item.State.ToString(), symbolKey = item.SymbolKey }).ToArray()
        }, Input.Source.Data.Limits);
        if (charge && !ProjectSemanticBinder.WithinBudget(Data.Value, ref remaining, token)) throw new FormatException("Semantic index exhausted its derived-work budget.");
        LogicalOperationsConsumed = maximumOperations - Math.Max(0, remaining);
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes()); token.ThrowIfCancellationRequested();
        if (preparedLookups is not null) lookupCache!.Commit(preparedLookups, token);
    }

    /// <summary>Build from the actual closure, preserving explicit reachable-only/incomplete scope. No Compiler result or permission is cached.</summary>
    public static CompilerSemanticIndex Build(CompilerBindingClosure binding, ContentDigest expectedInput, int? maximumOperations = null, CancellationToken cancellationToken = default,
        ProjectSemanticIndexLookupCache? lookupCache = null)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(expectedInput); cancellationToken.ThrowIfCancellationRequested();
        var maximum = maximumOperations ?? binding.Input.ExecutionProfile.MaximumOperations;
        if (maximum < 1 || maximum > binding.Input.ExecutionProfile.MaximumOperations) throw new ArgumentOutOfRangeException(nameof(maximumOperations));
        lookupCache?.BeginBinding(binding.Input.Source.Context.Branch, cancellationToken);
        var remaining = maximum;
        CompilerSemanticIndex Empty(CompilerSemanticIndexState state) => new(binding, state, [], [], [], maximum, ref remaining, false, cancellationToken);
        if (!binding.Input.InputDigest.Equals(expectedInput) || binding.State == CompilerBindingClosureState.StaleInput) return Empty(CompilerSemanticIndexState.StaleInput);
        if (binding.State == CompilerBindingClosureState.ResourceLimit) return Empty(CompilerSemanticIndexState.ResourceLimit);
        var symbols = new List<CompilerIndexedSymbol>(); var references = new List<CompilerBoundReference>();
        try
        {
            foreach (var region in binding.Regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0) return Empty(CompilerSemanticIndexState.ResourceLimit);
                var declarations = new Dictionary<EntityId, List<DeclarationDecodeResult>>();
                foreach (var declaration in region.Declarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (--remaining < 0) return Empty(CompilerSemanticIndexState.ResourceLimit);
                    var id = SourceFields.Identity<EntityId>(declaration.Data.Value, "entityId");
                    if (!declarations.TryGetValue(id, out var rows)) declarations.Add(id, rows = []);
                    rows.Add(declaration);
                }
                foreach (var symbol in region.Model.Symbols.Where(item => item.Context.Equals(region.Source.Context)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    remaining -= symbol.Key.Length + 1;
                    if (remaining < 0) return Empty(CompilerSemanticIndexState.ResourceLimit);
                    SourceJson? type = null; string? name = null;
                    if (declarations.TryGetValue(symbol.EntityId, out var rows) && rows.Count == 1 && rows[0].Declaration is { } declaration)
                    {
                        name = declaration.DisplayName;
                        if (symbol.PortId is { } port && declaration is TaskPlanDeclaration task)
                            type = task.Ports.Single(item => item.PortId == port).Type.Data;
                    }
                    symbols.Add(new(symbol, type, name, false));
                }
                foreach (var document in region.Source.Documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (--remaining < 0) return Empty(CompilerSemanticIndexState.ResourceLimit);
                    var key = SourceJson.FromObject(new { context = region.Source.Context.Data.Value, documentId = document.DocumentId.ToString() }).CanonicalText;
                    var status = ValueFields.HasUnknown(document.Data.Value, "documentId", "logicalName", "schemaId", "schemaVersion", "root", "digest") ||
                        ValueFields.HasUnknown(document.Digest.Data.Value, "algorithm", "value") ? ValueDecodeStatus.Unsupported : ValueDecodeStatus.Decoded;
                    symbols.Add(new(new(key, SourceDocument.SchemaId, document.Root.EntityId, null, region.Source.Context,
                        ProjectLocation.Create(document.DocumentId, region.Source.Context), status), null, document.LogicalName, true));
                }
                foreach (var reference in region.References)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (--remaining < 0) return Empty(CompilerSemanticIndexState.ResourceLimit);
                    references.Add(reference);
                }
            }
            var state = binding.State == CompilerBindingClosureState.ResourceLimit ? CompilerSemanticIndexState.ResourceLimit :
                binding.State == CompilerBindingClosureState.Complete && binding.IncludesDeclaredSnapshots ? CompilerSemanticIndexState.Complete : CompilerSemanticIndexState.Incomplete;
            return new(binding, state, binding.Regions, symbols, references, maximum, ref remaining, true, cancellationToken, lookupCache);
        }
        catch (FormatException) { return Empty(CompilerSemanticIndexState.ResourceLimit); }
    }

    private static IEnumerable<string> LookupDependencies(IReadOnlyList<CompilerIndexedSymbol> symbols, IReadOnlyList<CompilerBoundReference> references)
    {
        yield return "symbols"; yield return symbols.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var symbol in symbols)
        {
            yield return symbol.Symbol.Key; yield return symbol.Symbol.Location.Data.CanonicalText;
        }
        yield return "origins"; yield return references.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var reference in references) yield return reference.Origin.Data.CanonicalText;
    }

    private CompilerIndexQueryState Check(ProjectLocation location, ContentDigest inputDigest)
    {
        if (!Input.InputDigest.Equals(inputDigest) || State == CompilerSemanticIndexState.StaleInput) return CompilerIndexQueryState.StaleInput;
        if (State == CompilerSemanticIndexState.ResourceLimit) return CompilerIndexQueryState.ResourceLimit;
        return _contexts.Contains(location.Context) ? CompilerIndexQueryState.Ready : CompilerIndexQueryState.OutOfScope;
    }
    private CompilerIndexedSymbol? Unique(ProjectLocation location) => _lookups.TryLocation(location.Data.CanonicalText, out var position) &&
        _lookups.HasKey(Symbols[position].Symbol.Key) && Symbols[position].Symbol.Status == ValueDecodeStatus.Decoded ? Symbols[position] : null;
    public CompilerIndexedSymbol? Inspect(ProjectLocation location, ContentDigest inputDigest)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(inputDigest);
        return Check(location, inputDigest) == CompilerIndexQueryState.Ready ? Unique(location) : null;
    }
    public CompilerIndexDefinition FindDefinition(ProjectLocation origin, ContentDigest inputDigest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(inputDigest); cancellationToken.ThrowIfCancellationRequested();
        var state = Check(origin, inputDigest); if (state != CompilerIndexQueryState.Ready) return new(state, null, null);
        if (!_lookups.TryOrigin(origin.Data.CanonicalText, out var position)) return new(CompilerIndexQueryState.Unavailable, null, null);
        var reference = References[position]; cancellationToken.ThrowIfCancellationRequested();
        if (reference.State != ReferenceResolutionState.Resolved) return new(CompilerIndexQueryState.Unresolved, null, reference.State);
        return reference.SymbolKey is { } key && _lookups.TryKey(key, out var targetPosition) && Symbols[targetPosition].Symbol.Status == ValueDecodeStatus.Decoded
            ? new(CompilerIndexQueryState.Ready, Symbols[targetPosition], reference.State) : new(CompilerIndexQueryState.Unavailable, null, reference.State);
    }
    public CompilerIndexReferencePage FindReferences(ProjectLocation target, ContentDigest inputDigest, int pageSize = MaximumPageSize,
        CompilerIndexCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(inputDigest); cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        static CompilerIndexReferencePage Refused(CompilerIndexQueryState state) => new(state, null, null, [], false, 0, null);
        var check = Check(target, inputDigest); if (check != CompilerIndexQueryState.Ready) return Refused(check);
        var symbol = Unique(target); if (symbol is null) return Refused(CompilerIndexQueryState.Unavailable);
        if (cursor is not null && (!ReferenceEquals(cursor.Owner, this) || cursor.Query != "references" || cursor.Anchor != symbol.Symbol.Key || cursor.Offset < 0 || cursor.Offset > References.Count))
            return Refused(CompilerIndexQueryState.InvalidCursor);
        var rows = new List<CompilerBoundReference>(); var offset = cursor?.Offset ?? 0; var scanned = 0;
        while (offset < References.Count && scanned < MaximumScan && rows.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested(); var row = References[offset++]; scanned++;
            if (row.State == ReferenceResolutionState.Resolved && row.SymbolKey == symbol.Symbol.Key) rows.Add(row);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(CompilerIndexQueryState.Ready, Input.InputDigest, symbol.Symbol.Location, new ImmutableListView<CompilerBoundReference>(rows),
            IndexCoverageComplete, scanned, offset < References.Count ? new(this, "references", symbol.Symbol.Key, offset) : null);
    }
    public CompilerIndexCompletionPage CompleteEntityReference(ProjectLocation origin, ContentDigest inputDigest, int pageSize = 16,
        CompilerIndexCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(inputDigest); cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        static CompilerIndexCompletionPage Refused(CompilerIndexQueryState state) => new(state, null, null, null, [], false, 0, null);
        var check = Check(origin, inputDigest); if (check != CompilerIndexQueryState.Ready) return Refused(check);
        var symbol = Unique(origin);
        if (symbol?.Symbol.PortId is null || symbol.DeclaredType is null) return Refused(CompilerIndexQueryState.Unavailable);
        var type = new TypeReference(symbol.DeclaredType);
        if (!type.IsSupported || type.Kind != "coglatas.entity-reference" || type.ExpectedKind is null) return Refused(CompilerIndexQueryState.Unsupported);
        if (cursor is not null && (!ReferenceEquals(cursor.Owner, this) || cursor.Query != "completion" || cursor.Anchor != symbol.Symbol.Key || cursor.Offset < 0 || cursor.Offset > Symbols.Count))
            return Refused(CompilerIndexQueryState.InvalidCursor);
        var rows = new List<CompilerIndexedSymbol>(); var offset = cursor?.Offset ?? 0; var scanned = 0;
        while (offset < Symbols.Count && scanned < MaximumScan && rows.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested(); var row = Symbols[offset++]; scanned++;
            if (!row.IsDocument && row.Symbol.PortId is null && row.Symbol.Kind == type.ExpectedKind &&
                row.Symbol.Status == ValueDecodeStatus.Decoded && _lookups.HasKey(row.Symbol.Key)) rows.Add(row);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(CompilerIndexQueryState.Ready, Input.InputDigest, symbol.Symbol.Location, type.ExpectedKind, new ImmutableListView<CompilerIndexedSymbol>(rows),
            IndexCoverageComplete, scanned, offset < Symbols.Count ? new(this, "completion", symbol.Symbol.Key, offset) : null);
    }
}
