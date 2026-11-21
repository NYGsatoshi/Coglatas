namespace Coglatas.Domain.ProjectIde;

public enum ReferenceSearchState { Ready, StaleInput, OutOfScope, Unavailable, InvalidCursor }

/// <summary>Local continuation for one immutable inspection and exact resolved symbol. It grants no current access.</summary>
public sealed class ReferenceSearchCursor
{
    internal CompilerSemanticInspection Owner { get; }
    internal string SymbolKey { get; }
    internal int Offset { get; }
    internal ReferenceSearchCursor(CompilerSemanticInspection owner, string symbolKey, int offset)
    { Owner = owner; SymbolKey = symbolKey; Offset = offset; }
}

public sealed class ReferenceSearchPage
{
    public ReferenceSearchState State { get; }
    public ContentDigest? InputDigest { get; }
    public ProjectLocation? Target { get; }
    public IReadOnlyList<CompilerBoundReference> References { get; }
    public bool IndexCoverageComplete { get; }
    public bool ScanComplete => State == ReferenceSearchState.Ready && NextCursor is null;
    public int ScannedReferences { get; }
    public ReferenceSearchCursor? NextCursor { get; }
    internal ReferenceSearchPage(ReferenceSearchState state, ContentDigest? inputDigest, ProjectLocation? target,
        IEnumerable<CompilerBoundReference> references, bool indexCoverageComplete, int scannedReferences, ReferenceSearchCursor? nextCursor)
    {
        State = state; InputDigest = inputDigest; Target = target;
        References = new ImmutableListView<CompilerBoundReference>(references);
        IndexCoverageComplete = indexCoverageComplete; ScannedReferences = scannedReferences; NextCursor = nextCursor;
    }
}

public sealed partial class CompilerSemanticInspection
{
    public const int MaximumReferenceSearchPageSize = 32;
    public const int MaximumReferenceSearchScan = 256;

    /// <summary>
    /// Page the actual resolved reference projection in its original canonical order.
    /// Index coverage and remaining scan work are separate. No Source map/authorization is inferred.
    /// The existing unpaged query and semantic-inspection bytes remain unchanged.
    /// </summary>
    public ReferenceSearchPage FindReferencesPage(ProjectLocation target, ContentDigest inputDigest,
        int pageSize = MaximumReferenceSearchPageSize, ReferenceSearchCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(inputDigest);
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > MaximumReferenceSearchPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        static ReferenceSearchPage Refused(ReferenceSearchState state) => new(state, null, null,
            Array.Empty<CompilerBoundReference>(), false, 0, null);
        if (!Input.InputDigest.Equals(inputDigest)) return Refused(ReferenceSearchState.StaleInput);
        if (!Input.Source.Context.Equals(target.Context)) return Refused(ReferenceSearchState.OutOfScope);
        var symbol = Inspect(target);
        if (symbol is null || !_uniqueKeys.Contains(symbol.Symbol.Key) || symbol.Symbol.Status != ValueDecodeStatus.Decoded)
            return Refused(ReferenceSearchState.Unavailable);
        if (cursor is not null && (!ReferenceEquals(cursor.Owner, this) ||
            !StringComparer.Ordinal.Equals(cursor.SymbolKey, symbol.Symbol.Key) || cursor.Offset < 0 || cursor.Offset > References.Count))
            return Refused(ReferenceSearchState.InvalidCursor);
        var references = new List<CompilerBoundReference>(); var offset = cursor?.Offset ?? 0; var scanned = 0;
        while (offset < References.Count && scanned < MaximumReferenceSearchScan && references.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = References[offset++]; scanned++;
            if (reference.State == ReferenceResolutionState.Resolved && StringComparer.Ordinal.Equals(reference.SymbolKey, symbol.Symbol.Key))
                references.Add(reference);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(ReferenceSearchState.Ready, Input.InputDigest, symbol.Symbol.Location, references,
            _completionCoverageComplete, scanned, offset < References.Count ? new(this, symbol.Symbol.Key, offset) : null);
    }
}
