using System.Buffers;

namespace Okojo.RegExp;

internal sealed class ScratchMatchStateArena : IDisposable
{
    private readonly int captureSlots;
    private readonly int stride;
    private int nextOffset;

    public ScratchMatchStateArena(int captureCount)
    {
        captureSlots = captureCount + 1;
        stride = captureSlots * 3;
        Buffer = ArrayPool<int>.Shared.Rent(Math.Max(stride * 8, stride));
        Array.Clear(Buffer, 0, stride);
        nextOffset = stride;
        Root = new(this, 0, captureSlots);
    }

    public ScratchMatchState Root { get; }

    internal int[] Buffer { get; private set; }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(Buffer);
        Buffer = Array.Empty<int>();
        nextOffset = 0;
    }

    internal ScratchMatchState Clone(in ScratchMatchState source)
    {
        var offset = Allocate();
        Array.Copy(Buffer, source.BaseOffset, Buffer, offset, stride);
        return new(this, offset, captureSlots);
    }

    internal ScratchMatchState AllocateBlank()
    {
        var offset = Allocate();
        Array.Clear(Buffer, offset, stride);
        return new(this, offset, captureSlots);
    }

    internal ScratchMatchStateLease RentClone(in ScratchMatchState source, out ScratchMatchState state)
    {
        var mark = nextOffset;
        state = Clone(source);
        return new(this, mark);
    }

    internal ScratchMatchStateLease RentBlank(out ScratchMatchState state)
    {
        var mark = nextOffset;
        state = AllocateBlank();
        return new(this, mark);
    }

    public void Reset()
    {
        Array.Clear(Buffer, 0, stride);
        nextOffset = stride;
    }

    internal void Rewind(int mark)
    {
        if (Buffer.Length == 0)
            return;

        if (mark < stride)
            mark = stride;
        if (mark <= nextOffset)
            nextOffset = mark;
    }

    private int Allocate()
    {
        var offset = nextOffset;
        var required = offset + stride;
        if (required > Buffer.Length)
            Grow(required);
        nextOffset = required;
        return offset;
    }

    private void Grow(int minLength)
    {
        var newLength = Buffer.Length * 2;
        if (newLength < minLength)
            newLength = minLength;

        var newBuffer = ArrayPool<int>.Shared.Rent(newLength);
        Array.Copy(Buffer, newBuffer, nextOffset);
        ArrayPool<int>.Shared.Return(Buffer);
        Buffer = newBuffer;
    }
}

internal readonly struct ScratchMatchStateLease(ScratchMatchStateArena? arena, int mark) : IDisposable
{
    public void Dispose()
    {
        arena?.Rewind(mark);
    }
}

internal readonly struct ScratchMatchStateIntView(ScratchMatchStateArena? arena, int offset, int length)
{
    public int Length { get; } = length;

    public int this[int index]
    {
        get => arena is null ? 0 : arena.Buffer[offset + index];
        set
        {
            if (arena is not null)
                arena.Buffer[offset + index] = value;
        }
    }

    public Span<int> AsSpan()
    {
        return arena is null ? Span<int>.Empty : arena.Buffer.AsSpan(offset, Length);
    }
}

internal readonly struct ScratchMatchStateBoolView(ScratchMatchStateArena? arena, int offset, int length)
{
    public int Length { get; } = length;

    public bool this[int index]
    {
        get => arena is not null && arena.Buffer[offset + index] != 0;
        set
        {
            if (arena is not null)
                arena.Buffer[offset + index] = value ? 1 : 0;
        }
    }

    public Span<int> AsSpan()
    {
        return arena is null ? Span<int>.Empty : arena.Buffer.AsSpan(offset, Length);
    }
}

internal struct ScratchMatchState
{
    public static ScratchMatchState Empty { get; } = new(null, 0, 1);

    private readonly ScratchMatchStateArena? arena;
    private readonly int captureSlots;

    internal ScratchMatchState(ScratchMatchStateArena? arena, int baseOffset, int captureSlots)
    {
        this.arena = arena;
        this.captureSlots = captureSlots;
        BaseOffset = baseOffset;
        Starts = new(arena, baseOffset, captureSlots);
        Ends = new(arena, baseOffset + captureSlots, captureSlots);
        Matched = new(arena, baseOffset + captureSlots * 2, captureSlots);
    }

    internal int BaseOffset { get; }
    public ScratchMatchStateIntView Starts;
    public ScratchMatchStateIntView Ends;
    public ScratchMatchStateBoolView Matched;

    public ScratchMatchState Clone()
    {
        if (arena is null)
            return this;

        return arena.Clone(this);
    }

    public ScratchMatchState CreateSibling()
    {
        if (arena is null)
            return this;

        return arena.AllocateBlank();
    }

    public ScratchMatchStateLease RentClone(out ScratchMatchState clone)
    {
        if (arena is null)
        {
            clone = this;
            return default;
        }

        return arena.RentClone(this, out clone);
    }

    public ScratchMatchStateLease RentSibling(out ScratchMatchState sibling)
    {
        if (arena is null)
        {
            sibling = this;
            return default;
        }

        return arena.RentBlank(out sibling);
    }

    public void CopyFrom(ScratchMatchState other)
    {
        if (arena is null || (ReferenceEquals(arena, other.arena) && BaseOffset == other.BaseOffset))
            return;

        Array.Copy(other.arena!.Buffer, other.BaseOffset, arena.Buffer, BaseOffset, captureSlots * 3);
    }

    public void ClearCapture(int index)
    {
        if (arena is null)
            return;

        Starts[index] = 0;
        Ends[index] = 0;
        Matched[index] = false;
    }
}
