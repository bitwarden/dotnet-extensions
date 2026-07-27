using System.Collections;
using System.Runtime.CompilerServices;

namespace Bitwarden.Server.Sdk.Features.Analyzers;

internal sealed class EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[] _values;

    public EquatableArray(T[] values) => _values = values;

    public T this[int index] => _values[index];

    public int Count => _values.Length;

    public bool Equals(EquatableArray<T> other)
    {
        return ((ReadOnlySpan<T>)_values).SequenceEqual(other._values);
    }

    public override bool Equals(object? obj)
        => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 0;
        foreach (var values in _values)
        {
            hash = Combine(hash, values is null ? 0 : values.GetHashCode());
        }
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Combine(int h1, int h2)
    {
        uint rol5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
        return ((int)rol5 + h1) ^ h2;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
