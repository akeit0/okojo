using System.Buffers;

namespace Okojo.JavaScript.Internals;

/// <summary>
///     Pooled growable builder for MANAGED element types (types holding
///     object references, e.g. <c>JsValue</c>, <c>string</c>). Unlike
///     <see cref="PooledArrayBuilder{T}"/> (unmanaged, stackalloc-seeded),
///     every rented buffer is returned with <c>clearArray: true</c> and
///     grown buffers are cleared before swap so stale references never leak
///     through the pool.
/// </summary>
internal ref struct PooledManagedArrayBuilder<T>
{
    private T[] buffer;
    private T[]? rented;
    private int length;

    public PooledManagedArrayBuilder(int minCapacity)
    {
        rented = System.Buffers.ArrayPool<T>.Shared.Rent(minCapacity);
        buffer = rented;
        length = 0;
    }

    public readonly int Length => length;

    public void Add(T item)
    {
        if ((uint)length < (uint)buffer.Length)
        {
            buffer[length++] = item;
            return;
        }

        Grow();
        buffer[length++] = item;
    }

    public readonly ReadOnlySpan<T> AsSpan() => buffer.AsSpan(0, length);

    /// <summary>Copies the accumulated elements into an exact-size array.</summary>
    public T[] ToArray()
    {
        var result = new T[length];
        Array.Copy(buffer, result, length);
        return result;
    }

    /// <summary>
    ///     Resets length and clears emitted references so the pooled buffer
    ///     can be reused within the same operation without retaining objects.
    /// </summary>
    public void Clear()
    {
        Array.Clear(buffer, 0, length);
        length = 0;
    }

    public void Dispose()
    {
        if (rented is null)
            return;

        // Clear only the used prefix - references never live beyond Length,
        // so clearing whole pooled buffers would waste time on large rentals.
        Array.Clear(buffer, 0, length);
        System.Buffers.ArrayPool<T>.Shared.Return(rented, clearArray: false);
        rented = null;
        buffer = [];
    }

    private void Grow()
    {
        var newSize = Math.Max(buffer.Length * 2, length + 1);
        var newBuffer = System.Buffers.ArrayPool<T>.Shared.Rent(newSize);
        Array.Copy(buffer, newBuffer, length);
        Array.Clear(buffer, 0, length);
        System.Buffers.ArrayPool<T>.Shared.Return(buffer, clearArray: false);
        rented = newBuffer;
        buffer = newBuffer;
    }
}
