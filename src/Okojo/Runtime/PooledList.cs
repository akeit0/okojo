using System.Buffers;
using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

internal ref struct PooledList<T>(int capacity)
{
    private T[]? array = ArrayPool<T>.Shared.Rent(capacity > 0 ? capacity : 4);

    public int Count { get; private set; } = 0;

    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref array![index];
        }
    }

    public readonly ref T ItemRef(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return ref array![index];
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        EnsureCapacity(Count + 1);
        array![Count++] = item;
    }

    public void RemoveLast()
    {
        if (Count == 0)
            throw new InvalidOperationException("The list is empty.");
        Count -= 1;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            array![Count] = default!;
    }

    public void AddRange(ReadOnlySpan<T> values)
    {
        if (values.IsEmpty)
            return;

        var start = Count;
        EnsureCapacity(start + values.Length);
        values.CopyTo(array.AsSpan(start));
        Count = start + values.Length;
    }

    public readonly ReadOnlySpan<T> AsSpan()
    {
        return array is null ? [] : array.AsSpan(0, Count);
    }

    public void Clear()
    {
        if (array is not null && RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Count != 0)
            Array.Clear(array, 0, Count);
        Count = 0;
    }

    public void Truncate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Count)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (array is not null && RuntimeHelpers.IsReferenceOrContainsReferences<T>() && count < Count)
            Array.Clear(array, count, Count - count);
        Count = count;
    }

    public void Dispose()
    {
        if (array is null)
            return;

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Count != 0)
            Array.Clear(array, 0, Count);

        ArrayPool<T>.Shared.Return(array);
        array = null;
        Count = 0;
    }

    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int min)
    {
        if (array is null)
        {
            array = ArrayPool<T>.Shared.Rent(min > 0 ? min : 4);
            return;
        }

        if (min <= array.Length)
            return;

        var newCapacity = Math.Max(min, array.Length * 2);
        var newArray = ArrayPool<T>.Shared.Rent(newCapacity);
        Array.Copy(array, newArray, Count);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Count != 0)
            Array.Clear(array, 0, Count);
        ArrayPool<T>.Shared.Return(array);
        array = newArray;
    }

    internal struct Enumerator
    {
        private readonly T[]? array;
        private readonly int count;
        private int index;

        internal Enumerator(in PooledList<T> list)
        {
            array = list.array;
            count = list.Count;
            index = -1;
        }

        public readonly T Current => array![index];

        public bool MoveNext()
        {
            var next = index + 1;
            if (next >= count)
                return false;
            index = next;
            return true;
        }
    }
}
