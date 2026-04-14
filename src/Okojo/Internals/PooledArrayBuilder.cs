using System.Buffers;
using System.Runtime.CompilerServices;

namespace Okojo.Internals;

internal ref struct PooledArrayBuilder<T>(Span<T> initialBuffer)
{
    private Span<T> buffer = initialBuffer;
    private T[]? rented = null;

    public int Length { get; private set; } = 0;

    public void Add(T item)
    {
        var span = buffer;
        if ((uint)Length < (uint)span.Length)
        {
            span[Length++] = item;
            return;
        }

        SlowAdd(item);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SlowAdd(T item)
    {
        EnsureCapacity(1);
        buffer[Length++] = item;
    }

    public Span<T> AsSpan()
    {
        return buffer[..Length];
    }

    public void Clear()
    {
        Length = 0;
    }

    public override string ToString()
    {
        return buffer[..Length].ToString();
    }

    public T[] ToArray()
    {
        var result = new T[Length];
        buffer[..Length].CopyTo(result);
        return result;
    }

    public void Dispose()
    {
        if (rented is not null)
        {
            ArrayPool<T>.Shared.Return(rented);
            rented = null;
        }
    }

    private void EnsureCapacity(int additional)
    {
        if (Length + additional <= buffer.Length)
            return;

        Grow(additional);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additional)
    {
        var newSize = Math.Max(buffer.Length * 2, Length + additional);
        var newBuffer = ArrayPool<T>.Shared.Rent(newSize);
        buffer[..Length].CopyTo(newBuffer);
        if (rented is not null)
            ArrayPool<T>.Shared.Return(rented);
        rented = newBuffer;
        buffer = newBuffer;
    }
}
