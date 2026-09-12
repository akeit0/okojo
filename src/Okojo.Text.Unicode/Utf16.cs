using System.Runtime.CompilerServices;
using System.Text;

namespace Okojo.Text.Unicode;

/// <summary>
///     UTF-16 / Unicode code-point utilities over <see cref="ReadOnlySpan{T}"/> of <see cref="char"/>.
/// </summary>
public static class Utf16
{
    /// <summary>Returns true if the value is a UTF-16 high surrogate (U+D800..U+DBFF).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsHighSurrogate(int value) => (uint)(value - 0xD800) <= 0x3FF;

    /// <summary>Returns true if the value is a UTF-16 low surrogate (U+DC00..U+DFFF).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLowSurrogate(int value) => (uint)(value - 0xDC00) <= 0x3FF;

    /// <summary>Combines a high/low surrogate pair into a single code point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CombineSurrogates(int high, int low) =>
        ((high - 0xD800) << 10) + (low - 0xDC00) + 0x10000;

    /// <summary>Returns the UTF-16 code-unit width (1 or 2) of a code point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CodeUnitLength(int codePoint) => codePoint >= 0x10000 ? 2 : 1;

    /// <summary>
    ///     Reads the code point at <paramref name="position"/>. In unicode mode a valid surrogate
    ///     pair is combined into one code point; otherwise reads a single UTF-16 code unit.
    /// </summary>
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

    /// <summary>
    ///     Reads the code point ending just before <paramref name="position"/>. In unicode mode a
    ///     valid surrogate pair is combined into one code point.
    /// </summary>
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

    /// <summary>Reads the code point at <paramref name="position"/>, returning 0 when out of range.</summary>
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

    /// <summary>
    ///     ECMAScript <c>AdvanceStringIndex</c>: advances by two code units across a surrogate pair
    ///     in unicode mode, otherwise by one.
    /// </summary>
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

    /// <summary>Counts the code points in a UTF-16 string (lone surrogates count as one each).</summary>
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

    /// <summary>Returns true if the code point is an ECMAScript line terminator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLineTerminator(int codePoint) =>
        codePoint is '\n' or '\r' or 0x2028 or 0x2029;

    /// <summary>Returns true if the code point is an ASCII word character (<c>a-z A-Z 0-9 _</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiWord(int codePoint) =>
        (uint)(codePoint - 'a') <= 'z' - 'a'
        || (uint)(codePoint - 'A') <= 'Z' - 'A'
        || (uint)(codePoint - '0') <= 9
        || codePoint == '_';

    /// <summary>
    ///     Returns true if the code point is a word character, optionally using Unicode case-fold
    ///     equivalence when both <paramref name="unicode"/> and <paramref name="ignoreCase"/> are set.
    /// </summary>
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

    /// <summary>Returns true if <paramref name="position"/> is not inside a surrogate pair.</summary>
    public static bool IsUnicodeBoundary(ReadOnlySpan<char> input, int position) =>
        position <= 0
        || position >= input.Length
        || !char.IsLowSurrogate(input[position])
        || !char.IsHighSurrogate(input[position - 1]);

    /// <summary>Appends a code point to a <see cref="StringBuilder"/>, encoding astral values as a surrogate pair.</summary>
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
