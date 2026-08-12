using System.Runtime.CompilerServices;
using System.Text;

namespace Okojo.Text.Unicode;

/// <summary>
///     UTF-16 / Unicode code-point utilities over <see cref="ReadOnlySpan{T}"/> of <see cref="char"/>.
/// </summary>
public static class Utf16
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsHighSurrogate(int value) => (uint)(value - 0xD800) <= 0x3FF;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLowSurrogate(int value) => (uint)(value - 0xDC00) <= 0x3FF;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CombineSurrogates(int high, int low) =>
        ((high - 0xD800) << 10) + (low - 0xDC00) + 0x10000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CodeUnitLength(int codePoint) => codePoint >= 0x10000 ? 2 : 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadForward(
        ReadOnlySpan<char> input,
        int position,
        bool unicode,
        out int codePoint,
        out int width
    )
    {
        if ((uint)position >= (uint)input.Length)
        {
            codePoint = 0;
            width = 0;
            return false;
        }

        int first = input[position];
        if (unicode && IsHighSurrogate(first) && position + 1 < input.Length)
        {
            int second = input[position + 1];
            if (IsLowSurrogate(second))
            {
                codePoint = CombineSurrogates(first, second);
                width = 2;
                return true;
            }
        }

        codePoint = first;
        width = 1;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadBackward(
        ReadOnlySpan<char> input,
        int position,
        bool unicode,
        out int codePoint,
        out int width
    )
    {
        if (position <= 0 || position > input.Length)
        {
            codePoint = 0;
            width = 0;
            return false;
        }

        int last = input[position - 1];
        if (unicode && IsLowSurrogate(last) && position >= 2)
        {
            int first = input[position - 2];
            if (IsHighSurrogate(first))
            {
                codePoint = CombineSurrogates(first, last);
                width = 2;
                return true;
            }
        }

        codePoint = last;
        width = 1;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadForward(
        ReadOnlySpan<char> input,
        int position,
        bool unicode,
        out int width
    )
    {
        _ = TryReadForward(input, position, unicode, out int codePoint, out width);
        return codePoint;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AdvanceStringIndex(ReadOnlySpan<char> input, int index, bool unicode)
    {
        if ((uint)index >= (uint)input.Length)
            return input.Length + 1;
        if (
            unicode
            && IsHighSurrogate(input[index])
            && index + 1 < input.Length
            && IsLowSurrogate(input[index + 1])
        )
        {
            return index + 2;
        }
        return index + 1;
    }

    public static int CountCodePoints(ReadOnlySpan<char> value)
    {
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLowSurrogate(value[i]))
                continue;
            count++;
        }
        return count;
    }

    /// <summary>Reads the code point at the start of a string (surrogate-aware).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadCodePoint(ReadOnlySpan<char> value, int position)
    {
        int first = value[position];
        if (
            IsHighSurrogate(first)
            && position + 1 < value.Length
            && IsLowSurrogate(value[position + 1])
        )
        {
            return CombineSurrogates(first, value[position + 1]);
        }
        return first;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLineTerminator(int codePoint) =>
        codePoint is '\n' or '\r' or 0x2028 or 0x2029;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiWord(int codePoint) =>
        (uint)(codePoint - 'a') <= 'z' - 'a'
        || (uint)(codePoint - 'A') <= 'Z' - 'A'
        || (uint)(codePoint - '0') <= 9
        || codePoint == '_';

    public static bool IsWord(int codePoint, bool unicode, bool ignoreCase)
    {
        if (IsAsciiWord(codePoint))
            return true;
        if (!unicode || !ignoreCase)
            return false;
        if (!UnicodeCaseFolding.TryGetEquivalents(codePoint, out int offset, out int count))
            return false;
        for (int i = 0; i < count; i++)
        {
            if (IsAsciiWord(UnicodeCaseFolding.GetEquivalent(offset, i)))
                return true;
        }
        return false;
    }

    public static bool IsUnicodeBoundary(ReadOnlySpan<char> input, int position) =>
        position <= 0
        || position >= input.Length
        || !char.IsLowSurrogate(input[position])
        || !char.IsHighSurrogate(input[position - 1]);

    public static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        if ((uint)codePoint <= 0xFFFFu)
        {
            builder.Append((char)codePoint);
            return;
        }
        int value = codePoint - 0x10000;
        builder.Append((char)(0xD800 + (value >> 10)));
        builder.Append((char)(0xDC00 + (value & 0x3FF)));
    }
}
