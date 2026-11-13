using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Owns its storage and exposes no mutable collection or synchronization-storage view.</summary>
internal sealed class ImmutableListView<T> : IReadOnlyList<T>, IList<T>
{
    private readonly T[] _values;
    internal static ImmutableListView<T> Empty { get; } = new(Array.Empty<T>());
    internal ImmutableListView(IEnumerable<T> values) => _values = values.ToArray();
    public int Count => _values.Length;
    public T this[int index] => _values[index];
    T IList<T>.this[int index] { get => _values[index]; set => throw new NotSupportedException(); }
    bool ICollection<T>.IsReadOnly => true;
    int IList<T>.IndexOf(T item) => Array.IndexOf(_values, item);
    bool ICollection<T>.Contains(T item) => Array.IndexOf(_values, item) >= 0;
    void ICollection<T>.CopyTo(T[] array, int arrayIndex) => _values.CopyTo(array, arrayIndex);
    void ICollection<T>.Add(T item) => throw new NotSupportedException();
    void ICollection<T>.Clear() => throw new NotSupportedException();
    bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
    void IList<T>.Insert(int index, T item) => throw new NotSupportedException();
    void IList<T>.RemoveAt(int index) => throw new NotSupportedException();
    public IEnumerator<T> GetEnumerator()
    {
        foreach (var value in _values) yield return value;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Keys, values and enumeration never expose the owned dictionary or its synchronization root.</summary>
internal sealed class ImmutableMapView<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, IDictionary<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _values;
    internal ImmutableMapView(IEnumerable<KeyValuePair<TKey, TValue>> values, IEqualityComparer<TKey>? comparer = null) =>
        _values = values.ToDictionary(pair => pair.Key, pair => pair.Value, comparer);
    public int Count => _values.Count;
    public TValue this[TKey key] => _values[key];
    TValue IDictionary<TKey, TValue>.this[TKey key] { get => _values[key]; set => throw new NotSupportedException(); }
    ICollection<TKey> IDictionary<TKey, TValue>.Keys => new ImmutableListView<TKey>(_values.Keys);
    ICollection<TValue> IDictionary<TKey, TValue>.Values => new ImmutableListView<TValue>(_values.Values);
    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;
    void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => throw new NotSupportedException();
    bool IDictionary<TKey, TValue>.Remove(TKey key) => throw new NotSupportedException();
    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();
    void ICollection<KeyValuePair<TKey, TValue>>.Clear() => throw new NotSupportedException();
    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
        _values.TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)_values).CopyTo(array, arrayIndex);
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
