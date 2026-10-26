using System.Collections;
using System.Collections.Immutable;

namespace Tint.Generator;

// An immutable array with value equality, so incremental pipeline models compare by content.
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    public EquatableArray(IEnumerable<T> items)
    {
        _items = items.ToArray();
    }

    public static EquatableArray<T> Empty => default;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = _items ?? [];
        var right = other._items ?? [];
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var item in _items ?? [])
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public ImmutableArray<T> ToImmutableArray() => _items is null ? [] : [.. _items];
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T>
        => new(items);
}
