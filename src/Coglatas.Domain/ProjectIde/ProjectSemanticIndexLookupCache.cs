using System.Globalization;
using System.Text;

namespace Coglatas.Domain.ProjectIde;

/// <summary>
/// Caller-owned reuse of private positional lookup tables for an exact index roster.
/// Public symbols, references, input stamps and cursors always belong to the new index.
/// No Source, model, permission or semantic result is supplied by this session.
/// </summary>
public sealed class ProjectSemanticIndexLookupCache
{
    private readonly object _gate = new();
    private readonly int _maximumEntries;
    private readonly int _maximumCanonicalBytes;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recent = new();
    private BranchRef? _ownerScope;
    private volatile object _generation = new();
    private int _canonicalBytes;
    private long _lookupReuseCount;

    public ProjectSemanticIndexLookupCache(int maximumEntries = 32, int maximumCanonicalBytes = 4_194_304)
    {
        if (maximumEntries is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumCanonicalBytes is < 1 or > 4_194_304) throw new ArgumentOutOfRangeException(nameof(maximumCanonicalBytes));
        _maximumEntries = maximumEntries; _maximumCanonicalBytes = maximumCanonicalBytes;
    }

    public int RetainedCount { get { lock (_gate) return _entries.Count; } }
    /// <summary>Exact framed roster-key bytes, not managed heap usage.</summary>
    public int RetainedCanonicalBytes { get { lock (_gate) return _canonicalBytes; } }
    /// <summary>Completed builds that actually reused lookup construction in this owner lifetime.</summary>
    public long LookupReuseCount { get { lock (_gate) return _lookupReuseCount; } }

    public void Clear()
    {
        lock (_gate) { ClearEntries(); _ownerScope = null; }
    }

    internal SemanticIndexLookupScope BeginBinding(BranchRef ownerScope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_ownerScope != ownerScope) { ClearEntries(); _ownerScope = ownerScope; }
            return new(ownerScope, _generation);
        }
    }

    internal SemanticIndexLookupLease Prepare(SemanticIndexLookupScope scope, IEnumerable<string> dependencies,
        Func<SemanticIndexLookupTables> create, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = FrameKey(scope.Generation, dependencies, token, out var bytes);
        if (key is not null)
        {
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (_ownerScope != scope.Owner || !ReferenceEquals(_generation, scope.Generation))
                    key = null;
                else if (_entries.TryGetValue(key, out var retained))
                    return new(scope.Owner, scope.Generation, key, bytes, retained.Value.Tables, true);
            }
        }

        token.ThrowIfCancellationRequested();
        return new(scope.Owner, scope.Generation, key, key is null ? 0 : bytes, create(), false);
    }

    private string? FrameKey(object generation, IEnumerable<string> dependencies, CancellationToken token, out int bytes)
    {
        bytes = 0;
        if (!ReferenceEquals(_generation, generation)) return null;

        var text = new StringBuilder();
        using var parts = dependencies.GetEnumerator();
        while (ReferenceEquals(_generation, generation) && parts.MoveNext())
        {
            token.ThrowIfCancellationRequested();
            // An invalidation can occur while the iterator produces this part.
            if (!ReferenceEquals(_generation, generation)) return null;
            var part = parts.Current;
            var length = part.Length.ToString(CultureInfo.InvariantCulture);
            var partBytes = Encoding.UTF8.GetByteCount(part);
            if (partBytes > _maximumCanonicalBytes - bytes - length.Length - 1) return null;

            text.Append(length).Append(':').Append(part);
            bytes += partBytes + length.Length + 1;
        }

        return ReferenceEquals(_generation, generation) ? text.ToString() : null;
    }

    // A refused/cancelled build never commits its prepared table. Explicit Clear or
    // a different owner also prevents late publication by an earlier builder.
    internal void Commit(SemanticIndexLookupLease lease, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (lease.Key is null || lease.Owner != _ownerScope || !ReferenceEquals(lease.Generation, _generation)) return;
            if (_entries.TryGetValue(lease.Key, out var retained))
            {
                _recent.Remove(retained); _recent.AddLast(retained);
            }
            else
            {
                while (_entries.Count >= _maximumEntries || _canonicalBytes > _maximumCanonicalBytes - lease.Bytes)
                {
                    var oldest = _recent.First!; _recent.RemoveFirst();
                    _entries.Remove(oldest.Value.Key); _canonicalBytes -= oldest.Value.Bytes;
                }
                _entries.Add(lease.Key, _recent.AddLast(new Entry(lease.Key, lease.Tables, lease.Bytes))); _canonicalBytes += lease.Bytes;
            }
            if (lease.Reused && _lookupReuseCount < long.MaxValue) _lookupReuseCount++;
        }
    }

    private void ClearEntries()
    {
        _entries.Clear(); _recent.Clear(); _canonicalBytes = 0; _lookupReuseCount = 0; _generation = new();
    }
    private sealed record Entry(string Key, SemanticIndexLookupTables Tables, int Bytes);
}

internal sealed record SemanticIndexLookupScope(BranchRef Owner, object Generation);

internal sealed record SemanticIndexLookupLease(BranchRef Owner, object Generation, string? Key, int Bytes,
    SemanticIndexLookupTables Tables, bool Reused);

internal sealed class SemanticIndexLookupTables
{
    private const int AmbiguousPosition = -1;
    private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _locations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _origins = new(StringComparer.Ordinal);
    internal int KeyCount { get; }
    internal int LocationCount { get; }

    internal SemanticIndexLookupTables(IReadOnlyList<CompilerIndexedSymbol> symbols, IReadOnlyList<CompilerBoundReference> references, CancellationToken token)
    {
        for (var index = 0; index < symbols.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var symbol = symbols[index].Symbol;
            KeyCount += RegisterPosition(_keys, symbol.Key, index);
            LocationCount += RegisterPosition(_locations, symbol.Location.Data.CanonicalText, index);
        }
        for (var index = 0; index < references.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            RegisterPosition(_origins, references[index].Origin.Data.CanonicalText, index);
        }
        token.ThrowIfCancellationRequested();
    }

    // Return the change in unique positions. Later occurrences keep an ambiguous key ambiguous.
    private static int RegisterPosition(Dictionary<string, int> positions, string key, int index)
    {
        if (!positions.TryGetValue(key, out var previous))
        {
            positions.Add(key, index);
            return 1;
        }
        if (previous == AmbiguousPosition) return 0;
        positions[key] = AmbiguousPosition;
        return -1;
    }

    internal bool HasKey(string key) => TryKey(key, out _);
    internal bool TryKey(string key, out int index) => TryUniquePosition(_keys, key, out index);
    internal bool TryLocation(string location, out int index) => TryUniquePosition(_locations, location, out index);
    internal bool TryOrigin(string origin, out int index) => TryUniquePosition(_origins, origin, out index);

    private static bool TryUniquePosition(Dictionary<string, int> positions, string key, out int index) =>
        positions.TryGetValue(key, out index) && index != AmbiguousPosition;
}
