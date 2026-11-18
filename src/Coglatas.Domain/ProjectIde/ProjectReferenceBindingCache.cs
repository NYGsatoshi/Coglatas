using System.Globalization;
using System.Text;

namespace Coglatas.Domain.ProjectIde;

/// <summary>
/// Caller-owned bounded reuse of reference resolution after fresh dependency admission.
/// Exact origin, reference and lookup dependencies determine reuse; diagnostics and
/// complete models are rebuilt for the current input. Retention grants no access.
/// </summary>
public sealed class ProjectReferenceBindingCache
{
    private readonly object _gate = new();
    private readonly int _maximumEntries;
    private readonly int _maximumCanonicalBytes;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recent = new();
    private BranchRef? _ownerScope;
    private int _canonicalBytes;

    public ProjectReferenceBindingCache(int maximumEntries = 256, int maximumCanonicalBytes = 4_194_304)
    {
        if (maximumEntries is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumCanonicalBytes is < 1 or > 4_194_304) throw new ArgumentOutOfRangeException(nameof(maximumCanonicalBytes));
        _maximumEntries = maximumEntries; _maximumCanonicalBytes = maximumCanonicalBytes;
    }

    public int RetainedCount { get { lock (_gate) return _entries.Count; } }
    /// <summary>Exact framed dependency-key bytes, not a managed heap measurement.</summary>
    public int RetainedCanonicalBytes { get { lock (_gate) return _canonicalBytes; } }

    public void Clear()
    {
        lock (_gate) { ClearEntries(); _ownerScope = null; }
    }

    internal void BeginBinding(BranchRef ownerScope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_ownerScope != ownerScope) { ClearEntries(); _ownerScope = ownerScope; }
        }
    }

    internal ReferenceBindingOutcome Resolve(BranchRef ownerScope, IEnumerable<string> dependencies,
        Func<ReferenceBindingOutcome> bind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_ownerScope != ownerScope) { ClearEntries(); _ownerScope = ownerScope; }
            var text = new StringBuilder(); var bytes = 0;
            foreach (var part in dependencies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Length framing preserves exact equality without relying on a hash
                // or constructing another bounded Source/Compiler protocol body.
                var length = part.Length.ToString(CultureInfo.InvariantCulture);
                var partBytes = Encoding.UTF8.GetByteCount(part);
                if (partBytes > _maximumCanonicalBytes - bytes - length.Length - 1)
                {
                    var unretained = bind(); cancellationToken.ThrowIfCancellationRequested(); return unretained;
                }
                text.Append(length).Append(':').Append(part); bytes += partBytes + length.Length + 1;
            }
            var key = text.ToString();
            if (_entries.TryGetValue(key, out var retained))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _recent.Remove(retained); _recent.AddLast(retained); return retained.Value.Binding;
            }
            var binding = bind(); cancellationToken.ThrowIfCancellationRequested();
            while (_entries.Count >= _maximumEntries || _canonicalBytes > _maximumCanonicalBytes - bytes)
            {
                var oldest = _recent.First!; _recent.RemoveFirst();
                _entries.Remove(oldest.Value.Key); _canonicalBytes -= oldest.Value.Bytes;
            }
            _entries.Add(key, _recent.AddLast(new Entry(key, binding, bytes))); _canonicalBytes += bytes;
            return binding;
        }
    }

    private void ClearEntries() { _entries.Clear(); _recent.Clear(); _canonicalBytes = 0; }
    private sealed record Entry(string Key, ReferenceBindingOutcome Binding, int Bytes);
}

internal sealed record ReferenceBindingOutcome(CompilerBoundReference Reference, string? DiagnosticCode = null,
    bool Invalid = false, CompilerSymbol? DocumentSymbol = null);
