using System.Buffers;
using System.Runtime.CompilerServices;

namespace Okojo.RegExp.Experimental;

internal sealed class ScratchPooledList<T> : IDisposable
{
    private const int DefaultCapacity = 8;
    private T[] buffer = Array.Empty<T>();

    public int Count { get; private set; }

    public T this[int index]
    {
        get => buffer[index];
        set => buffer[index] = value;
    }

    public void Dispose()
    {
        if (buffer.Length != 0)
            ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        buffer = Array.Empty<T>();
        Count = 0;
    }

    public void Add(T item)
    {
        if ((uint)Count >= (uint)buffer.Length)
            Grow(Count + 1);

        buffer[Count++] = item;
    }

    public T[] ToArray()
    {
        if (Count == 0)
            return Array.Empty<T>();

        var result = new T[Count];
        Array.Copy(buffer, result, Count);
        return result;
    }

    private void Grow(int minCapacity)
    {
        var newCapacity = buffer.Length == 0 ? DefaultCapacity : buffer.Length * 2;
        if (newCapacity < minCapacity)
            newCapacity = minCapacity;

        var newBuffer = ArrayPool<T>.Shared.Rent(newCapacity);
        if (Count != 0)
            Array.Copy(buffer, newBuffer, Count);

        if (buffer.Length != 0)
            ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        buffer = newBuffer;
    }
}
