using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace LabelEditor.Domain.Common;

/// <summary>
/// An immutable list with structural (element-wise) equality, so records that contain
/// collections compare by value. Supports collection expressions: <c>ValueList&lt;int&gt; x = [1, 2];</c>.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
public readonly struct ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly ImmutableArray<T> _items;

    public ValueList(ImmutableArray<T> items) => _items = items.IsDefault ? [] : items;

    public static ValueList<T> Empty => new([]);

    private ImmutableArray<T> Items => _items.IsDefault ? [] : _items;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public ImmutableArray<T> AsImmutableArray() => Items;

    public ValueList<T> Add(T item) => new(Items.Add(item));

    public ValueList<T> AddRange(IEnumerable<T> items) => new(Items.AddRange(items));

    public ValueList<T> Insert(int index, T item) => new(Items.Insert(index, item));

    public ValueList<T> RemoveAt(int index) => new(Items.RemoveAt(index));

    public ValueList<T> SetItem(int index, T item) => new(Items.SetItem(index, item));

    public ValueList<T> RemoveAll(Predicate<T> match) => new(Items.RemoveAll(match));

    public int IndexOf(T item) => Items.IndexOf(item);

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public bool Equals(ValueList<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);

    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);

    public override string ToString() => $"[{string.Join(", ", Items)}]";
}

/// <summary>Factory helpers for <see cref="ValueList{T}"/>.</summary>
public static class ValueList
{
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) => new([.. items]);

    public static ValueList<T> ToValueList<T>(this IEnumerable<T> items) => new([.. items]);
}
