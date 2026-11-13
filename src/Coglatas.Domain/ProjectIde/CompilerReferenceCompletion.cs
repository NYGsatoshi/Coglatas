namespace Coglatas.Domain.ProjectIde;

public enum ReferenceCompletionState { Ready, StaleInput, OutOfScope, Unavailable, Unsupported, InvalidCursor }

/// <summary>In-process continuation issued by one immutable inspection for one exact port.</summary>
public sealed class ReferenceCompletionCursor
{
    internal CompilerSemanticInspection Owner { get; }
    internal string OriginKey { get; }
    internal int Offset { get; }
    internal ReferenceCompletionCursor(CompilerSemanticInspection owner, string originKey, int offset)
    { Owner = owner; OriginKey = originKey; Offset = offset; }
}

public sealed record ReferenceCompletionCandidate(CompilerInspectedSymbol Symbol, string? DisplayName);
public sealed record ReferenceCompletionPage(
    ReferenceCompletionState State, ContentDigest InputDigest, ProjectLocation Origin, string? ExpectedKind,
    IReadOnlyList<ReferenceCompletionCandidate> Candidates, bool CoverageComplete, int ScannedSymbols,
    ReferenceCompletionCursor? NextCursor);

public sealed partial class CompilerSemanticInspection
{
    private readonly HashSet<string> _uniqueKeys;
    private readonly Dictionary<string, CompilerInspectedSymbol> _locations;
    private readonly Dictionary<EntityId, string?> _displayNames;
    private readonly bool _completionCoverageComplete;
    public const int MaximumCompletionPageSize = 32;
    public const int MaximumCompletionScan = 256;

    /// <summary>
    /// Bounded local entity-reference suggestions derived from the canonical port type.
    /// This reads an already admitted snapshot; it grants no authorization and performs no edit.
    /// Coverage describes the binding, while NextCursor describes remaining local symbols.
    /// </summary>
    public ReferenceCompletionPage CompleteEntityReference(ProjectLocation origin, ContentDigest inputDigest,
        int pageSize = 16, ReferenceCompletionCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(inputDigest);
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > MaximumCompletionPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        ReferenceCompletionPage Refused(ReferenceCompletionState state) => new(state, Input.InputDigest, origin, null,
            Array.Empty<ReferenceCompletionCandidate>(), false, 0, null);
        if (!Input.InputDigest.Equals(inputDigest)) return Refused(ReferenceCompletionState.StaleInput);
        if (!Input.Source.Context.Equals(origin.Context)) return Refused(ReferenceCompletionState.OutOfScope);
        var inspected = Inspect(origin);
        if (inspected is null || !_uniqueKeys.Contains(inspected.Symbol.Key) ||
            inspected.Symbol.Status != ValueDecodeStatus.Decoded || inspected.Symbol.PortId is null || inspected.DeclaredType is null)
            return Refused(ReferenceCompletionState.Unavailable);
        var type = new TypeReference(inspected.DeclaredType);
        if (!type.IsSupported || type.Kind != "coglatas.entity-reference" || type.ExpectedKind is null)
            return Refused(ReferenceCompletionState.Unsupported);
        if (cursor is not null && (!ReferenceEquals(cursor.Owner, this) || cursor.OriginKey != inspected.Symbol.Key))
            return Refused(ReferenceCompletionState.InvalidCursor);

        var candidates = new List<ReferenceCompletionCandidate>();
        var offset = cursor?.Offset ?? 0;
        var scanned = 0;
        while (offset < Symbols.Count && scanned < MaximumCompletionScan && candidates.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Symbols[offset++]; scanned++;
            if (candidate.Symbol.PortId is not null || candidate.Symbol.Status != ValueDecodeStatus.Decoded ||
                candidate.Symbol.Kind != type.ExpectedKind || !_uniqueKeys.Contains(candidate.Symbol.Key)) continue;
            candidates.Add(new(candidate, _displayNames.GetValueOrDefault(candidate.Symbol.EntityId)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(ReferenceCompletionState.Ready, Input.InputDigest, origin, type.ExpectedKind, candidates.AsReadOnly(),
            _completionCoverageComplete, scanned, offset < Symbols.Count ? new(this, inspected.Symbol.Key, offset) : null);
    }
}
