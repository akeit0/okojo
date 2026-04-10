using System.Buffers;
using System.Globalization;
using System.Text;
using Okojo.Parsing;

namespace Okojo.Runtime;

internal static class JsStringCaseOperations
{
    private static readonly Dictionary<int, string> UpperSpecialCasing = new()
    {
        [0x00DF] = "\u0053\u0053",
        [0x0130] = "\u0130",
        [0xFB00] = "\u0046\u0046",
        [0xFB01] = "\u0046\u0049",
        [0xFB02] = "\u0046\u004C",
        [0xFB03] = "\u0046\u0046\u0049",
        [0xFB04] = "\u0046\u0046\u004C",
        [0xFB05] = "\u0053\u0054",
        [0xFB06] = "\u0053\u0054",
        [0x0587] = "\u0535\u0552",
        [0xFB13] = "\u0544\u0546",
        [0xFB14] = "\u0544\u0535",
        [0xFB15] = "\u0544\u053B",
        [0xFB16] = "\u054E\u0546",
        [0xFB17] = "\u0544\u053D",
        [0x0149] = "\u02BC\u004E",
        [0x0390] = "\u0399\u0308\u0301",
        [0x03B0] = "\u03A5\u0308\u0301",
        [0x01F0] = "\u004A\u030C",
        [0x1E96] = "\u0048\u0331",
        [0x1E97] = "\u0054\u0308",
        [0x1E98] = "\u0057\u030A",
        [0x1E99] = "\u0059\u030A",
        [0x1E9A] = "\u0041\u02BE",
        [0x1F50] = "\u03A5\u0313",
        [0x1F52] = "\u03A5\u0313\u0300",
        [0x1F54] = "\u03A5\u0313\u0301",
        [0x1F56] = "\u03A5\u0313\u0342",
        [0x1FB6] = "\u0391\u0342",
        [0x1FC6] = "\u0397\u0342",
        [0x1FD2] = "\u0399\u0308\u0300",
        [0x1FD3] = "\u0399\u0308\u0301",
        [0x1FD6] = "\u0399\u0342",
        [0x1FD7] = "\u0399\u0308\u0342",
        [0x1FE2] = "\u03A5\u0308\u0300",
        [0x1FE3] = "\u03A5\u0308\u0301",
        [0x1FE4] = "\u03A1\u0313",
        [0x1FE6] = "\u03A5\u0342",
        [0x1FE7] = "\u03A5\u0308\u0342",
        [0x1FF6] = "\u03A9\u0342",
        [0x1F80] = "\u1F08\u0399",
        [0x1F81] = "\u1F09\u0399",
        [0x1F82] = "\u1F0A\u0399",
        [0x1F83] = "\u1F0B\u0399",
        [0x1F84] = "\u1F0C\u0399",
        [0x1F85] = "\u1F0D\u0399",
        [0x1F86] = "\u1F0E\u0399",
        [0x1F87] = "\u1F0F\u0399",
        [0x1F88] = "\u1F08\u0399",
        [0x1F89] = "\u1F09\u0399",
        [0x1F8A] = "\u1F0A\u0399",
        [0x1F8B] = "\u1F0B\u0399",
        [0x1F8C] = "\u1F0C\u0399",
        [0x1F8D] = "\u1F0D\u0399",
        [0x1F8E] = "\u1F0E\u0399",
        [0x1F8F] = "\u1F0F\u0399",
        [0x1F90] = "\u1F28\u0399",
        [0x1F91] = "\u1F29\u0399",
        [0x1F92] = "\u1F2A\u0399",
        [0x1F93] = "\u1F2B\u0399",
        [0x1F94] = "\u1F2C\u0399",
        [0x1F95] = "\u1F2D\u0399",
        [0x1F96] = "\u1F2E\u0399",
        [0x1F97] = "\u1F2F\u0399",
        [0x1F98] = "\u1F28\u0399",
        [0x1F99] = "\u1F29\u0399",
        [0x1F9A] = "\u1F2A\u0399",
        [0x1F9B] = "\u1F2B\u0399",
        [0x1F9C] = "\u1F2C\u0399",
        [0x1F9D] = "\u1F2D\u0399",
        [0x1F9E] = "\u1F2E\u0399",
        [0x1F9F] = "\u1F2F\u0399",
        [0x1FA0] = "\u1F68\u0399",
        [0x1FA1] = "\u1F69\u0399",
        [0x1FA2] = "\u1F6A\u0399",
        [0x1FA3] = "\u1F6B\u0399",
        [0x1FA4] = "\u1F6C\u0399",
        [0x1FA5] = "\u1F6D\u0399",
        [0x1FA6] = "\u1F6E\u0399",
        [0x1FA7] = "\u1F6F\u0399",
        [0x1FA8] = "\u1F68\u0399",
        [0x1FA9] = "\u1F69\u0399",
        [0x1FAA] = "\u1F6A\u0399",
        [0x1FAB] = "\u1F6B\u0399",
        [0x1FAC] = "\u1F6C\u0399",
        [0x1FAD] = "\u1F6D\u0399",
        [0x1FAE] = "\u1F6E\u0399",
        [0x1FAF] = "\u1F6F\u0399",
        [0x1FB3] = "\u0391\u0399",
        [0x1FBC] = "\u0391\u0399",
        [0x1FC3] = "\u0397\u0399",
        [0x1FCC] = "\u0397\u0399",
        [0x1FF3] = "\u03A9\u0399",
        [0x1FFC] = "\u03A9\u0399",
        [0x1FB2] = "\u1FBA\u0399",
        [0x1FB4] = "\u0386\u0399",
        [0x1FC2] = "\u1FCA\u0399",
        [0x1FC4] = "\u0389\u0399",
        [0x1FF2] = "\u1FFA\u0399",
        [0x1FF4] = "\u038F\u0399",
        [0x1FB7] = "\u0391\u0342\u0399",
        [0x1FC7] = "\u0397\u0342\u0399",
        [0x1FF7] = "\u03A9\u0342\u0399"
    };

    public static JsString ToUpperCaseUnicodeDefault(JsString value)
    {
        if (value.Length == 0)
            return value;

        char[]? pooledChars = null;
        var chars = value.Flatten(out pooledChars);
        try
        {
            var asciiOnly = true;
            var anyAsciiLower = false;
            for (var i = 0; i < chars.Length; i++)
            {
                var ch = chars[i];
                if (ch >= 0x80)
                {
                    asciiOnly = false;
                    break;
                }

                if (ch is >= 'a' and <= 'z')
                    anyAsciiLower = true;
            }

            if (asciiOnly)
            {
                if (!anyAsciiLower)
                    return value;

                using var builder = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length, 256)]);
                for (var i = 0; i < chars.Length; i++)
                    builder.Append(char.ToUpperInvariant(chars[i]));
                return builder.ToString();
            }

            using var pooled = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length + 8, 256)]);
            var changed = false;
            for (var i = 0; i < chars.Length; i++)
            {
                var ch = chars[i];
                if (char.IsHighSurrogate(ch) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
                {
                    var rune = new Rune(ch, chars[i + 1]);
                    if (UpperSpecialCasing.TryGetValue(rune.Value, out var specialPair))
                    {
                        pooled.Append(specialPair);
                        changed = true;
                    }
                    else
                    {
                        var upper = Rune.ToUpperInvariant(rune);
                        if (upper != rune)
                            changed = true;
                        pooled.AppendRune(upper);
                    }

                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(ch))
                {
                    pooled.Append(ch);
                    continue;
                }

                if (UpperSpecialCasing.TryGetValue(ch, out var special))
                {
                    pooled.Append(special);
                    changed = true;
                    continue;
                }

                var upperChar = char.ToUpperInvariant(ch);
                if (upperChar != ch)
                    changed = true;
                pooled.Append(upperChar);
            }

            return changed ? pooled.ToString() : value;
        }
        finally
        {
            if (pooledChars is not null)
                ArrayPool<char>.Shared.Return(pooledChars);
        }
    }

    internal static int AppendUpperCaseUnicodeDefault(ref PooledCharBuilder builder, ReadOnlySpan<char> value,
        int index, out bool changed)
    {
        var ch = value[index];
        if (char.IsHighSurrogate(ch) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
        {
            var rune = new Rune(ch, value[index + 1]);
            if (UpperSpecialCasing.TryGetValue(rune.Value, out var specialPair))
            {
                builder.Append(specialPair);
                changed = true;
            }
            else
            {
                var upper = Rune.ToUpperInvariant(rune);
                builder.AppendRune(upper);
                changed = upper != rune;
            }

            return 2;
        }

        if (char.IsLowSurrogate(ch))
        {
            builder.Append(ch);
            changed = false;
            return 1;
        }

        if (UpperSpecialCasing.TryGetValue(ch, out var special))
        {
            builder.Append(special);
            changed = true;
            return 1;
        }

        var upperChar = char.ToUpperInvariant(ch);
        builder.Append(upperChar);
        changed = upperChar != ch;
        return 1;
    }

    public static JsString ToLowerCaseUnicodeDefault(JsString value)
    {
        if (value.Length == 0)
            return value;

        char[]? pooledChars = null;
        var chars = value.Flatten(out pooledChars);
        try
        {
            var asciiOnly = true;
            var anyAsciiUpper = false;
            for (var i = 0; i < chars.Length; i++)
            {
                var ch = chars[i];
                if (ch >= 0x80)
                {
                    asciiOnly = false;
                    break;
                }

                if (ch is >= 'A' and <= 'Z')
                    anyAsciiUpper = true;
            }

            if (asciiOnly)
            {
                if (!anyAsciiUpper)
                    return value;

                using var builder = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length, 256)]);
                for (var i = 0; i < chars.Length; i++)
                    builder.Append(char.ToLowerInvariant(chars[i]));
                return builder.ToString();
            }

            using var pooled = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length + 8, 256)]);
            var changed = false;
            for (var i = 0; i < chars.Length; i++)
            {
                var ch = chars[i];
                if (ch == '\u03A3')
                {
                    var mapped = IsFinalSigmaContext(chars, i) ? '\u03C2' : '\u03C3';
                    pooled.Append(mapped);
                    changed = true;
                    continue;
                }

                if (ch == '\u0130')
                {
                    pooled.Append('i');
                    pooled.Append('\u0307');
                    changed = true;
                    continue;
                }

                if (char.IsHighSurrogate(ch) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
                {
                    var rune = new Rune(ch, chars[i + 1]);
                    var lower = Rune.ToLowerInvariant(rune);
                    if (lower != rune)
                        changed = true;
                    pooled.AppendRune(lower);
                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(ch))
                {
                    pooled.Append(ch);
                    continue;
                }

                var lowerChar = char.ToLowerInvariant(ch);
                if (lowerChar != ch)
                    changed = true;
                pooled.Append(lowerChar);
            }

            return changed ? pooled.ToString() : value;
        }
        finally
        {
            if (pooledChars is not null)
                ArrayPool<char>.Shared.Return(pooledChars);
        }
    }

    private static bool IsFinalSigmaContext(ReadOnlySpan<char> value, int sigmaIndex)
    {
        var hasCasedBefore = false;
        for (var i = sigmaIndex - 1; i >= 0; i--)
        {
            Rune rune;
            if (char.IsLowSurrogate(value[i]) && i > 0 && char.IsHighSurrogate(value[i - 1]))
            {
                rune = new(value[i - 1], value[i]);
                i--;
            }
            else
            {
                rune = new(value[i]);
            }

            if (!IsCased(rune))
                continue;

            hasCasedBefore = true;
            break;
        }

        if (!hasCasedBefore)
            return false;

        for (var i = sigmaIndex + 1; i < value.Length; i++)
        {
            Rune rune;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                rune = new(value[i], value[i + 1]);
                i++;
            }
            else
            {
                rune = new(value[i]);
            }

            if (!IsCased(rune))
                continue;

            return false;
        }

        return true;
    }

    private static bool IsCased(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter;
    }
}
