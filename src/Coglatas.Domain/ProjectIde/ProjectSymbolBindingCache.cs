using System.Text;

namespace Coglatas.Domain.ProjectIde;

/// <summary>
/// Caller-owned bounded reuse of exact declaration decoding and context-bound symbols.
/// Admission, reference resolution, diagnostics and semantic validation remain fresh.
/// Retained entries grant no access to a materialized dependency.
/// </summary>
public sealed class ProjectSymbolBindingCache
{
    private readonly object _gate = new();
    private readonly int _maximumEntries;
    private readonly int _maximumCanonicalBytes;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private BranchRef? _ownerScope;
    private readonly object _identity = new();
    private object _generation = new();
    private int _canonicalBytes;

    public ProjectSymbolBindingCache(int maximumEntries = 256, int maximumCanonicalBytes = 4_194_304)
    {
        if (maximumEntries is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumCanonicalBytes is < 1 or > 4_194_304) throw new ArgumentOutOfRangeException(nameof(maximumCanonicalBytes));
        _maximumEntries = maximumEntries; _maximumCanonicalBytes = maximumCanonicalBytes;
    }

    public int RetainedCount { get { lock (_gate) return _entries.Count; } }
    /// <summary>Declaration and context key bytes only, not a managed heap measurement.</summary>
    public int RetainedCanonicalBytes { get { lock (_gate) return _canonicalBytes; } }

    public void Clear()
    {
        lock (_gate) { ClearEntries(); _ownerScope = null; }
    }

    internal SymbolBindingScope BeginBinding(BranchRef ownerScope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_ownerScope != ownerScope) { ClearEntries(); _ownerScope = ownerScope; }
            return new(_identity, ownerScope, _generation);
        }
    }

    internal SymbolBindingScope ContinueBinding(BranchRef ownerScope, SymbolBindingScope? earlier, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return earlier is not null && ReferenceEquals(earlier.CacheIdentity, _identity) && earlier.Owner == ownerScope
            ? earlier : BeginBinding(ownerScope, token);
    }

    internal IReadOnlyList<BoundProjectDeclaration> Read(ProjectSource source, SymbolBindingScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retain = ReferenceEquals(scope.CacheIdentity, _identity) && _ownerScope == scope.Owner && ReferenceEquals(scope.Generation, _generation);
            var result = new List<BoundProjectDeclaration>();
            foreach (var document in source.Documents) result.Add(ReadOne(document.Root.Data, document.DocumentId));
            foreach (var declaration in source.Declarations.Value.EnumerateArray())
                result.Add(ReadOne(SourceJson.FromElement(declaration, source.Data.Limits), SourceFields.Identity<DocumentId>(declaration, "documentId")));
            cancellationToken.ThrowIfCancellationRequested();
            return new ImmutableListView<BoundProjectDeclaration>(result);

            BoundProjectDeclaration ReadOne(SourceJson data, DocumentId document)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = new Key(source.Context, document, data.Limits, data);
                if (retain && _entries.TryGetValue(key, out var retained))
                {
                    _recent.Remove(retained); _recent.AddLast(retained);
                    return retained.Value.Binding;
                }
                var decoded = PlanDeclarationReader.CheckTenant(PlanDeclarationReader.Read(data, document), source.TenantId);
                var binding = new BoundProjectDeclaration(decoded,
                    new ImmutableListView<CompilerSymbol>(ProjectSemanticBinder.BindSymbols(decoded, source.Context)));
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = Encoding.UTF8.GetByteCount(data.CanonicalText) + Encoding.UTF8.GetByteCount(source.Context.Data.CanonicalText);
                if (retain && bytes <= _maximumCanonicalBytes)
                {
                    while (_entries.Count >= _maximumEntries || _canonicalBytes > _maximumCanonicalBytes - bytes)
                    {
                        var oldest = _recent.First!; _recent.RemoveFirst();
                        _entries.Remove(oldest.Value.Key); _canonicalBytes -= oldest.Value.Bytes;
                    }
                    _entries.Add(key, _recent.AddLast(new Entry(key, binding, bytes))); _canonicalBytes += bytes;
                }
                return binding;
            }
        }
    }

    private void ClearEntries() { _entries.Clear(); _recent.Clear(); _canonicalBytes = 0; _generation = new(); }
    private sealed record Key(SourceRevisionContext Context, DocumentId Document, SourceCodecLimits Limits, SourceJson Data);
    private sealed record Entry(Key Key, BoundProjectDeclaration Binding, int Bytes);
}

internal sealed record SymbolBindingScope(object CacheIdentity, BranchRef Owner, object Generation);

internal sealed record BoundProjectDeclaration(DeclarationDecodeResult Declaration, IReadOnlyList<CompilerSymbol> Symbols);
