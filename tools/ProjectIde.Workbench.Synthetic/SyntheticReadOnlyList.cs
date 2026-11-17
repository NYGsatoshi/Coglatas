namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>Owns a detached read-only roster without exposing synchronization storage.</summary>
internal sealed class SyntheticReadOnlyList<T> : IReadOnlyList<T>, IList<T>
{
    private readonly List<T> _values;
    internal SyntheticReadOnlyList(IEnumerable<T> values) => _values = new List<T>(values);
    public int Count => _values.Count;
    public T this[int index] => _values[index];
    T IList<T>.this[int index] { get => _values[index]; set => throw new NotSupportedException(); }
    bool ICollection<T>.IsReadOnly => true;
    int IList<T>.IndexOf(T item) => _values.IndexOf(item);
    bool ICollection<T>.Contains(T item) => _values.Contains(item);
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
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
