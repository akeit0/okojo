using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Okojo.Parsing;

internal ref struct PooledCharBuilder(Span<char> initialBuffer)
{
    private Span<char> buffer = initialBuffer;
    private char[]? rented = null;

    public int Length { get; private set; } = 0;

    public void Append(char c)
    {
        EnsureCapacity(1);
        buffer[Length++] = c;
    }

    public void Append(ReadOnlySpan<char> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(buffer[Length..]);
        Length += value.Length;
    }

    public void Append(JsString value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(buffer.Slice(Length, value.Length));
        Length += value.Length;
    }

    public void Append(JsString value, int start, int length)
    {
        EnsureCapacity(length);
        value.CopyTo(start, length, buffer.Slice(Length, length));
        Length += length;
    }

    public void AppendRune(int codePoint)
    {
        var rune = new Rune(codePoint);
        EnsureCapacity(rune.Utf16SequenceLength);
        Length += rune.EncodeToUtf16(buffer[Length..]);
    }

    public void AppendRune(Rune rune)
    {
        EnsureCapacity(rune.Utf16SequenceLength);
        Length += rune.EncodeToUtf16(buffer[Length..]);
    }

    public ReadOnlySpan<char> AsSpan()
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

    public void Dispose()
    {
        if (rented is not null)
        {
            ArrayPool<char>.Shared.Return(rented);
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
        var newBuffer = ArrayPool<char>.Shared.Rent(newSize);
        buffer[..Length].CopyTo(newBuffer);
        if (rented is not null)
            ArrayPool<char>.Shared.Return(rented);
        rented = newBuffer;
        buffer = newBuffer;
    }
}
