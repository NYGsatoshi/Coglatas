using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Owns its storage and exposes no mutable collection or synchronization-storage view.</summary>
internal sealed class ImmutableListView<T> : IReadOnlyList<T>
{
    private readonly T[] _values;
    internal static ImmutableListView<T> Empty { get; } = new(Array.Empty<T>());
    internal ImmutableListView(IEnumerable<T> values) => _values = values.ToArray();
    public int Count => _values.Length;
    public T this[int index] => _values[index];
    public IEnumerator<T> GetEnumerator()
    {
        foreach (var value in _values) yield return value;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Keys, values and enumeration never expose the owned dictionary or its synchronization root.</summary>
internal sealed class ImmutableMapView<TKey, TValue> : IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _values;
    internal ImmutableMapView(IEnumerable<KeyValuePair<TKey, TValue>> values, IEqualityComparer<TKey>? comparer = null) =>
        _values = values.ToDictionary(pair => pair.Key, pair => pair.Value, comparer);
    public int Count => _values.Count;
    public TValue this[TKey key] => _values[key];
    public IEnumerable<TKey> Keys { get { foreach (var pair in _values) yield return pair.Key; } }
    public IEnumerable<TValue> Values { get { foreach (var pair in _values) yield return pair.Value; } }
    public bool ContainsKey(TKey key) => _values.ContainsKey(key);
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _values.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var pair in _values) yield return pair;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
