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
        // Each invalidation assigns a new epoch, so an older scope can take the uncached path immediately.
        if (!ReferenceEquals(_generation, scope.Generation))
            return new(scope.Owner, scope.Generation, null, 0, create(), false);

        var text = new StringBuilder();
        var bytes = 0;
        foreach (var part in dependencies)
        {
            token.ThrowIfCancellationRequested();
            var length = part.Length.ToString(CultureInfo.InvariantCulture);
            var partBytes = Encoding.UTF8.GetByteCount(part);
            if (partBytes > _maximumCanonicalBytes - bytes - length.Length - 1)
                return new(scope.Owner, scope.Generation, null, 0, create(), false);

            text.Append(length).Append(':').Append(part);
            bytes += partBytes + length.Length + 1;
        }

        string? key = text.ToString();
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_ownerScope != scope.Owner || !ReferenceEquals(_generation, scope.Generation))
                key = null;
            else if (_entries.TryGetValue(key, out var retained))
                return new(scope.Owner, scope.Generation, key, bytes, retained.Value.Tables, true);
        }

        token.ThrowIfCancellationRequested();
        return new(scope.Owner, scope.Generation, key, key is null ? 0 : bytes, create(), false);
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
    private readonly Dictionary<string, int> _keys;
    private readonly Dictionary<string, int> _locations;
    private readonly Dictionary<string, int> _origins;
    internal int KeyCount => _keys.Count;
    internal int LocationCount => _locations.Count;

    internal SemanticIndexLookupTables(IReadOnlyList<CompilerIndexedSymbol> symbols, IReadOnlyList<CompilerBoundReference> references)
    {
        _keys = symbols.Select((item, index) => (item.Symbol.Key, Index: index)).GroupBy(item => item.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().Index, StringComparer.Ordinal);
        _locations = symbols.Select((item, index) => (Location: item.Symbol.Location.Data.CanonicalText, Index: index)).GroupBy(item => item.Location, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().Index, StringComparer.Ordinal);
        _origins = references.Select((item, index) => (Origin: item.Origin.Data.CanonicalText, Index: index)).GroupBy(item => item.Origin, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count() == 1 ? group.Single().Index : -1, StringComparer.Ordinal);
    }

    internal bool HasKey(string key) => _keys.ContainsKey(key);
    internal bool TryKey(string key, out int index) => _keys.TryGetValue(key, out index);
    internal bool TryLocation(string location, out int index) => _locations.TryGetValue(location, out index);
    internal bool TryOrigin(string origin, out int index) => _origins.TryGetValue(origin, out index) && index >= 0;
}
