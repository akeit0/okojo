using System.Buffers;

namespace Okojo.RegExp;

internal sealed class ScratchPooledIntSet : IDisposable
{
    private const int DefaultCapacity = 4;
    private int[] buffer = Array.Empty<int>();

    public int Count { get; private set; }

    public int this[int index] => buffer[index];

    public void Dispose()
    {
        if (buffer.Length != 0)
            ArrayPool<int>.Shared.Return(buffer);

        buffer = Array.Empty<int>();
        Count = 0;
    }

    public bool Add(int value)
    {
        if (Contains(value))
            return false;

        if ((uint)Count >= (uint)buffer.Length)
            Grow(Count + 1);

        buffer[Count++] = value;
        return true;
    }

    public void UnionWith(ScratchPooledIntSet other)
    {
        for (var i = 0; i < other.Count; i++)
            Add(other[i]);
    }

    public void IntersectWith(ScratchPooledIntSet other)
    {
        var write = 0;
        for (var i = 0; i < Count; i++)
        {
            var value = buffer[i];
            if (other.Contains(value))
                buffer[write++] = value;
        }

        Count = write;
    }

    public void ExceptWith(ScratchPooledIntSet other)
    {
        var write = 0;
        for (var i = 0; i < Count; i++)
        {
            var value = buffer[i];
            if (!other.Contains(value))
                buffer[write++] = value;
        }

        Count = write;
    }

    public int MaxOrDefault(int defaultValue = -1)
    {
        var max = defaultValue;
        for (var i = 0; i < Count; i++)
            if (buffer[i] > max)
                max = buffer[i];

        return max;
    }

    private bool Contains(int value)
    {
        for (var i = 0; i < Count; i++)
            if (buffer[i] == value)
                return true;

        return false;
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
