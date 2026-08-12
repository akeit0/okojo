using System.Buffers;
using System.Runtime.CompilerServices;

namespace Okojo.Text.RegularExpressions.Internal;

/// <summary>Stack-first, ArrayPool-backed value stack.</summary>
internal ref struct ValueStack<T>
    where T : struct
{
    private Span<T> _span;
    private T[]? _rented;
    private int _count;

    internal ValueStack(Span<T> initial)
    {
        _span = initial;
        _rented = null;
        _count = 0;
    }

    internal readonly int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Push(T value)
    {
        if ((uint)_count >= (uint)_span.Length)
            Grow();
        _span[_count++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T Pop()
    {
        if (_count == 0)
            throw new InvalidOperationException("The stack is empty.");
        return _span[--_count];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly T Peek()
    {
        if (_count == 0)
            throw new InvalidOperationException("The stack is empty.");
        return _span[_count - 1];
    }

    internal void Truncate(int count)
    {
        if ((uint)count > (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
    }

    internal void Clear() => _count = 0;

    internal void Dispose()
    {
        T[]? rented = _rented;
        _rented = null;
        _span = default;
        _count = 0;
        if (rented is not null)
            ArrayPool<T>.Shared.Return(rented, clearArray: false);
    }

    private void Grow()
    {
        int size = _span.Length == 0 ? 16 : checked(_span.Length * 2);
        T[] next = ArrayPool<T>.Shared.Rent(size);
        _span[.._count].CopyTo(next);
        T[]? previous = _rented;
        _span = next;
        _rented = next;
        if (previous is not null)
            ArrayPool<T>.Shared.Return(previous, clearArray: false);
    }
}
