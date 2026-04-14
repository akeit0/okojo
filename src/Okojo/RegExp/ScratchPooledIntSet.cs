using System.Buffers;

namespace Okojo.RegExp;

internal sealed class ScratchPooledIntSet : IDisposable
{
    private const int DefaultCapacity = 4;
    private const int SmallUnionThreshold = 8;
    private int[] buffer = Array.Empty<int>();
    private bool isSortedDistinct = true;

    public int Count { get; private set; }

    public int this[int index] => buffer[index];

    public void Dispose()
    {
        if (buffer.Length != 0)
            ArrayPool<int>.Shared.Return(buffer);

        buffer = Array.Empty<int>();
        Count = 0;
        isSortedDistinct = true;
    }

    public bool Add(int value)
    {
        if (isSortedDistinct)
            return AddSorted(value);

        if (Contains(value))
            return false;

        if ((uint)Count >= (uint)buffer.Length)
            Grow(Count + 1);

        buffer[Count++] = value;
        return true;
    }

    public void UnionWith(ScratchPooledIntSet other)
    {
        if (other.Count == 0)
            return;

        if (Count == 0)
        {
            EnsureCapacity(other.Count);
            other.CopyTo(buffer);
            Count = other.Count;
            isSortedDistinct = other.isSortedDistinct;
            if (!isSortedDistinct)
                NormalizeSortedDistinct();
            return;
        }

        if (other.Count == 1)
        {
            Add(other[0]);
            return;
        }

        if (Count + other.Count <= SmallUnionThreshold)
        {
            for (var i = 0; i < other.Count; i++)
                Add(other[i]);
            return;
        }

        EnsureCapacity(Count + other.Count);
        other.CopyTo(buffer.AsSpan(Count));
        Count += other.Count;
        isSortedDistinct = false;
        NormalizeSortedDistinct();
    }

    public void UnionWith(ReadOnlySpan<int> values)
    {
        if (values.Length == 0)
            return;

        if (Count == 0)
        {
            EnsureCapacity(values.Length);
            values.CopyTo(buffer);
            Count = values.Length;
            isSortedDistinct = false;
            NormalizeSortedDistinct();
            return;
        }

        if (values.Length == 1)
        {
            Add(values[0]);
            return;
        }

        if (Count + values.Length <= SmallUnionThreshold)
        {
            for (var i = 0; i < values.Length; i++)
                Add(values[i]);
            return;
        }

        EnsureCapacity(Count + values.Length);
        values.CopyTo(buffer.AsSpan(Count));
        Count += values.Length;
        isSortedDistinct = false;
        NormalizeSortedDistinct();
    }

    public void IntersectWith(ScratchPooledIntSet other)
    {
        if (Count == 0)
            return;
        if (other.Count == 0)
        {
            Count = 0;
            isSortedDistinct = true;
            return;
        }

        if (isSortedDistinct && other.isSortedDistinct)
        {
            IntersectSorted(other);
            return;
        }

        var write = 0;
        for (var i = 0; i < Count; i++)
        {
            var value = buffer[i];
            if (other.Contains(value))
                buffer[write++] = value;
        }

        Count = write;
        isSortedDistinct = false;
    }

    public void ExceptWith(ScratchPooledIntSet other)
    {
        if (Count == 0 || other.Count == 0)
            return;

        if (isSortedDistinct && other.isSortedDistinct)
        {
            ExceptSorted(other);
            return;
        }

        var write = 0;
        for (var i = 0; i < Count; i++)
        {
            var value = buffer[i];
            if (!other.Contains(value))
                buffer[write++] = value;
        }

        Count = write;
        isSortedDistinct = false;
    }

    public int MaxOrDefault(int defaultValue = -1)
    {
        var max = defaultValue;
        for (var i = 0; i < Count; i++)
            if (buffer[i] > max)
                max = buffer[i];

        return max;
    }

    public void CopyTo(Span<int> destination)
    {
        buffer.AsSpan(0, Count).CopyTo(destination);
    }

    private bool Contains(int value)
    {
        if (isSortedDistinct)
            return Array.BinarySearch(buffer, 0, Count, value) >= 0;

        for (var i = 0; i < Count; i++)
            if (buffer[i] == value)
                return true;

        return false;
    }

    private bool AddSorted(int value)
    {
        var insertIndex = Array.BinarySearch(buffer, 0, Count, value);
        if (insertIndex >= 0)
            return false;

        insertIndex = ~insertIndex;
        EnsureCapacity(Count + 1);
        if (insertIndex < Count)
            Array.Copy(buffer, insertIndex, buffer, insertIndex + 1, Count - insertIndex);

        buffer[insertIndex] = value;
        Count++;
        return true;
    }

    private void EnsureCapacity(int minCapacity)
    {
        if ((uint)minCapacity > (uint)buffer.Length)
            Grow(minCapacity);
    }

    private void NormalizeSortedDistinct()
    {
        if (Count <= 1)
        {
            isSortedDistinct = true;
            return;
        }

        var values = buffer.AsSpan(0, Count);
        values.Sort();

        var uniqueCount = 1;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] == values[uniqueCount - 1])
                continue;

            values[uniqueCount++] = values[i];
        }

        Count = uniqueCount;
        isSortedDistinct = true;
    }

    private void IntersectSorted(ScratchPooledIntSet other)
    {
        var left = 0;
        var right = 0;
        var write = 0;
        while (left < Count && right < other.Count)
        {
            var leftValue = buffer[left];
            var rightValue = other.buffer[right];
            if (leftValue == rightValue)
            {
                buffer[write++] = leftValue;
                left++;
                right++;
            }
            else if (leftValue < rightValue)
            {
                left++;
            }
            else
            {
                right++;
            }
        }

        Count = write;
        isSortedDistinct = true;
    }

    private void ExceptSorted(ScratchPooledIntSet other)
    {
        var left = 0;
        var right = 0;
        var write = 0;
        while (left < Count)
        {
            if (right >= other.Count)
            {
                buffer[write++] = buffer[left++];
                continue;
            }

            var leftValue = buffer[left];
            var rightValue = other.buffer[right];
            if (leftValue == rightValue)
            {
                left++;
                right++;
            }
            else if (leftValue < rightValue)
            {
                buffer[write++] = leftValue;
                left++;
            }
            else
            {
                right++;
            }
        }

        Count = write;
        isSortedDistinct = true;
    }

    private void Grow(int minCapacity)
    {
        var newCapacity = buffer.Length == 0 ? DefaultCapacity : buffer.Length * 2;
        if (newCapacity < minCapacity)
            newCapacity = minCapacity;

        var newBuffer = ArrayPool<int>.Shared.Rent(newCapacity);
        if (Count != 0)
            Array.Copy(buffer, newBuffer, Count);

        if (buffer.Length != 0)
            ArrayPool<int>.Shared.Return(buffer);

        buffer = newBuffer;
    }
}
