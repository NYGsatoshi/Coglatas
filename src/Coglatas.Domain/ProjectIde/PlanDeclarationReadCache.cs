using System.Text;

namespace Coglatas.Domain.ProjectIde;

/// <summary>
/// Caller-owned bounded reuse of immutable declaration decoding. Binding,
/// materializations, diagnostics and compilation are recomputed for every input.
/// This is not an authorization or semantic-result cache.
/// </summary>
public sealed class PlanDeclarationReadCache
{
    private readonly object _gate = new();
    private readonly int _maximumEntries;
    private readonly int _maximumCanonicalBytes;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private BranchRef? _scope;
    private int _canonicalBytes;

    public PlanDeclarationReadCache(int maximumEntries = 256, int maximumCanonicalBytes = 4_194_304)
    {
        if (maximumEntries is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumCanonicalBytes is < 1 or > 4_194_304) throw new ArgumentOutOfRangeException(nameof(maximumCanonicalBytes));
        _maximumEntries = maximumEntries; _maximumCanonicalBytes = maximumCanonicalBytes;
    }

    public int RetainedCount { get { lock (_gate) return _entries.Count; } }
    /// <summary>Canonical input bytes only; this is not a managed heap measurement.</summary>
    public int RetainedCanonicalBytes { get { lock (_gate) return _canonicalBytes; } }

    public void Clear()
    {
        lock (_gate) { ClearEntries(); _scope = null; }
    }

    public IReadOnlyList<DeclarationDecodeResult> Read(ProjectSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_scope != source.Context.Branch) { ClearEntries(); _scope = source.Context.Branch; }
            var results = new List<DeclarationDecodeResult>();
            foreach (var document in source.Documents)
                results.Add(ReadOne(document.Root.Data, document.DocumentId));
            foreach (var declaration in source.Declarations.Value.EnumerateArray())
                results.Add(ReadOne(SourceJson.FromElement(declaration, source.Data.Limits), SourceFields.Identity<DocumentId>(declaration, "documentId")));
            cancellationToken.ThrowIfCancellationRequested();
            return new ImmutableListView<DeclarationDecodeResult>(results);

            DeclarationDecodeResult ReadOne(SourceJson data, DocumentId document)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = new Key(document, data.Limits, data);
                if (_entries.TryGetValue(key, out var existing))
                {
                    _recent.Remove(existing); _recent.AddLast(existing);
                    return PlanDeclarationReader.CheckTenant(existing.Value.Result, source.TenantId);
                }
                var result = PlanDeclarationReader.Read(data, document);
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = Encoding.UTF8.GetByteCount(data.CanonicalText);
                if (bytes <= _maximumCanonicalBytes)
                {
                    while (_entries.Count >= _maximumEntries || _canonicalBytes > _maximumCanonicalBytes - bytes)
                    {
                        var oldest = _recent.First!; _recent.RemoveFirst();
                        _entries.Remove(oldest.Value.Key); _canonicalBytes -= oldest.Value.Bytes;
                    }
                    _entries.Add(key, _recent.AddLast(new Entry(key, result, bytes))); _canonicalBytes += bytes;
                }
                return PlanDeclarationReader.CheckTenant(result, source.TenantId);
            }
        }
    }

    private void ClearEntries() { _entries.Clear(); _recent.Clear(); _canonicalBytes = 0; }
    private sealed record Key(DocumentId Document, SourceCodecLimits Limits, SourceJson Data);
    private sealed record Entry(Key Key, DeclarationDecodeResult Result, int Bytes);
}
