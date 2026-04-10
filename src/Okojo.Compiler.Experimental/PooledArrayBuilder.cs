using System.Buffers;
using System.Runtime.CompilerServices;

namespace Okojo.Compiler.Experimental;

internal sealed class PooledArrayBuilder<T> : IDisposable
{
    private T[] buffer;
    private bool disposed;

    public PooledArrayBuilder(int initialCapacity = 8)
    {
        buffer = ArrayPool<T>.Shared.Rent(Math.Max(4, initialCapacity));
    }

    public int Count { get; private set; }

    public void Add(T value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if ((uint)Count >= (uint)buffer.Length)
            Grow();
        buffer[Count++] = value;
    }

    public ReadOnlySpan<T> AsSpan()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return buffer.AsSpan(0, Count);
    }

    private void Grow()
    {
        var next = ArrayPool<T>.Shared.Rent(buffer.Length * 2);
        Array.Copy(buffer, next, Count);
        ArrayPool<T>.Shared.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        buffer = next;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        ArrayPool<T>.Shared.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        buffer = Array.Empty<T>();
        Count = 0;
    }
}
