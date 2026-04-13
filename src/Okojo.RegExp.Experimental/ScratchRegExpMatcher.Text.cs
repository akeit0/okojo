using System.Text;

namespace Okojo.RegExp.Experimental;

internal static partial class ScratchRegExpMatcher
{
    private static bool TryReadCodePoint(string input, int pos, bool unicode, out int nextPos, out int codePoint)
    {
        if ((uint)pos >= (uint)input.Length)
        {
            nextPos = default;
            codePoint = default;
            return false;
        }

        if (unicode && char.IsHighSurrogate(input[pos]) && pos + 1 < input.Length &&
            char.IsLowSurrogate(input[pos + 1]))
        {
            codePoint = char.ConvertToUtf32(input[pos], input[pos + 1]);
            nextPos = pos + 2;
            return true;
        }

        codePoint = input[pos];
        nextPos = pos + 1;
        return true;
    }

    private static bool CodePointEquals(int left, int right, bool ignoreCase)
    {
        if (!ignoreCase)
            return left == right;

        return CanonicalizeCodePoint(left) == CanonicalizeCodePoint(right);
    }

    private static bool CodePointInRange(int codePoint, int start, int end, bool ignoreCase)
    {
        if (!ignoreCase)
            return codePoint >= start && codePoint <= end;

        var canonicalCodePoint = CanonicalizeCodePoint(codePoint);
        var canonicalStart = CanonicalizeCodePoint(start);
        var canonicalEnd = CanonicalizeCodePoint(end);
        if (canonicalStart > canonicalEnd)
            (canonicalStart, canonicalEnd) = (canonicalEnd, canonicalStart);
        return canonicalCodePoint >= canonicalStart && canonicalCodePoint <= canonicalEnd;
    }

    private static int CanonicalizeCodePoint(int codePoint)
    {
        if ((uint)codePoint <= char.MaxValue)
            codePoint = char.ToLowerInvariant((char)codePoint);
        else if (Rune.TryCreate(codePoint, out var rune)) codePoint = Rune.ToLowerInvariant(rune).Value;

        return codePoint switch
        {
            0x017F => 's',
            0x212A => 'k',
            0xFB06 => 0xFB05,
            0x1FD3 => 0x0390,
            0x1FE3 => 0x03B0,
            _ => codePoint
        };
    }

    private static bool IsLineTerminator(char ch)
    {
        return ch is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private static bool IsLineTerminator(int codePoint)
    {
        return codePoint is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private static bool IsWordBoundary(string input, int pos, bool unicode, bool ignoreCase)
    {
        var before = pos > 0 && TryReadCodePointBackward(input, pos, unicode, out _, out var beforeCp) &&
                     IsWord(beforeCp, unicode, ignoreCase);
        var after = pos < input.Length && TryReadCodePoint(input, pos, unicode, out _, out var afterCp) &&
                    IsWord(afterCp, unicode, ignoreCase);
        return before != after;
    }

    private static bool TryReadCodePointBackward(string input, int pos, bool unicode, out int previousPos,
        out int codePoint)
    {
        if (pos <= 0)
        {
            previousPos = default;
            codePoint = default;
            return false;
        }

        var last = input[pos - 1];
        if (unicode && char.IsLowSurrogate(last) && pos >= 2 && char.IsHighSurrogate(input[pos - 2]))
        {
            previousPos = pos - 2;
            codePoint = char.ConvertToUtf32(input[pos - 2], last);
            return true;
        }

        previousPos = pos - 1;
        codePoint = last;
        return true;
    }

    private static bool IsWord(int codePoint, bool unicode, bool ignoreCase)
    {
        if (codePoint is >= '0' and <= '9' ||
            codePoint is >= 'A' and <= 'Z' ||
            codePoint is >= 'a' and <= 'z' ||
            codePoint == '_')
            return true;

        if (!unicode || !ignoreCase)
            return false;

        var canonical = CanonicalizeCodePoint(codePoint);
        return canonical is >= '0' and <= '9' ||
               canonical is >= 'A' and <= 'Z' ||
               canonical is >= 'a' and <= 'z' ||
               canonical == '_';
    }
}
