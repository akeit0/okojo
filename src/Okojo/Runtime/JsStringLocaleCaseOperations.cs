using System.Buffers;
using System.Globalization;
using System.Text;
using Okojo.Parsing;

namespace Okojo.Runtime;

internal static class JsStringLocaleCaseOperations
{
    public static JsString ToLocaleUpperCase(JsRealm realm, JsString value, ReadOnlySpan<JsValue> arguments)
    {
        var culture = Intrinsics.ResolveRequestedLocaleCulture(realm, arguments);
        var langName = culture.TwoLetterISOLanguageName;
        if (string.Equals(langName, "tr", StringComparison.Ordinal) ||
            string.Equals(langName, "az", StringComparison.Ordinal))
            return ToUpperCaseTurkic(value);

        if (string.Equals("lt", culture.Name, StringComparison.OrdinalIgnoreCase))
            value = LithuanianStringProcessor(value);
        return JsStringCaseOperations.ToUpperCaseUnicodeDefault(value);
    }

    public static JsString ToLocaleLowerCase(JsRealm realm, JsString value, ReadOnlySpan<JsValue> arguments)
    {
        var culture = Intrinsics.ResolveRequestedLocaleCulture(realm, arguments);
        return ToLowerCaseWithSpecialCasing(value, culture);
    }

    private static JsString ToLowerCaseWithSpecialCasing(JsString value, CultureInfo culture)
    {
        var langName = culture.TwoLetterISOLanguageName;
        var isTurkishOrAzeri = string.Equals(langName, "tr", StringComparison.Ordinal) ||
                               string.Equals(langName, "az", StringComparison.Ordinal);
        var isLithuanian = string.Equals(langName, "lt", StringComparison.Ordinal);

        if (!isTurkishOrAzeri && !isLithuanian && !NeedsSpecialCasing(value))
            return value.Flatten().ToLower(culture);

        char[]? pooledChars = null;
        var chars = value.Flatten(out pooledChars);
        try
        {
            using var builder = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length + 8, 256)]);
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];

                if (c == '\u03A3')
                {
                    builder.Append(IsFinalSigmaContext(chars, i) ? '\u03C2' : '\u03C3');
                    continue;
                }

                if (isTurkishOrAzeri)
                {
                    if (c == '\u0130')
                    {
                        builder.Append('i');
                        continue;
                    }

                    if (c == 'I')
                    {
                        if (FollowedByDotAbove(chars, i))
                        {
                            builder.Append('i');
                            i++;
                            while (i < chars.Length && chars[i] != '\u0307')
                            {
                                builder.Append(char.ToLower(chars[i], culture));
                                i++;
                            }

                            continue;
                        }

                        builder.Append('\u0131');
                        continue;
                    }
                }
                else if (isLithuanian)
                {
                    if (c == '\u00CC')
                    {
                        builder.Append('i');
                        builder.Append('\u0307');
                        builder.Append('\u0300');
                        continue;
                    }

                    if (c == '\u00CD')
                    {
                        builder.Append('i');
                        builder.Append('\u0307');
                        builder.Append('\u0301');
                        continue;
                    }

                    if (c == '\u0128')
                    {
                        builder.Append('i');
                        builder.Append('\u0307');
                        builder.Append('\u0303');
                        continue;
                    }

                    if ((c == 'I' || c == 'J' || c == '\u012E') && FollowedByCombiningClass230(chars, i))
                    {
                        builder.Append(char.ToLower(c, culture));
                        builder.Append('\u0307');
                        continue;
                    }
                }
                else if (c == '\u0130')
                {
                    builder.Append('i');
                    builder.Append('\u0307');
                    continue;
                }

                if (char.IsHighSurrogate(c) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
                {
                    builder.Append(c);
                    builder.Append(chars[i + 1]);
                    i++;
                    continue;
                }

                builder.Append(char.ToLower(c, culture));
            }

            return builder.ToString();
        }
        finally
        {
            if (pooledChars is not null)
                ArrayPool<char>.Shared.Return(pooledChars);
        }
    }

    private static JsString LithuanianStringProcessor(JsString input)
    {
        char[]? pooledChars = null;
        var chars = input.Flatten(out pooledChars);
        try
        {
            List<int> replaceableIndices = [];
            for (var i = 0; i < chars.Length; i++)
                if (chars[i] == '\u0307')
                    replaceableIndices.Add(i);

            var write = 0;
            for (var read = 0; read < replaceableIndices.Count; read++)
            {
                var idx = replaceableIndices[read];
                if (idx > 0 && (chars[idx - 1] == 'I' || chars[idx - 1] == 'J'))
                    continue;

                replaceableIndices[write++] = idx;
            }

            if (write != replaceableIndices.Count)
                replaceableIndices.RemoveRange(write, replaceableIndices.Count - write);
            if (replaceableIndices.Count == 0)
                return input;

            using var builder = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length, 256)]);
            var replaceableCursor = 0;
            for (var i = 0; i < chars.Length; i++)
            {
                if (replaceableCursor < replaceableIndices.Count && replaceableIndices[replaceableCursor] == i)
                {
                    replaceableCursor++;
                    continue;
                }

                builder.Append(chars[i]);
            }

            return builder.ToString();
        }
        finally
        {
            if (pooledChars is not null)
                ArrayPool<char>.Shared.Return(pooledChars);
        }
    }

    private static JsString ToUpperCaseTurkic(JsString value)
    {
        if (value.Length == 0)
            return value;

        char[]? pooledChars = null;
        var chars = value.Flatten(out pooledChars);
        try
        {
            var builder = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length + 8, 256)]);
            try
            {
                var changed = false;
                for (var i = 0; i < chars.Length; i++)
                {
                    var ch = chars[i];
                    if (ch == 'i')
                    {
                        builder.Append('\u0130');
                        changed = true;
                        continue;
                    }

                    if (ch == '\u0131')
                    {
                        builder.Append('I');
                        changed = true;
                        continue;
                    }

                    var consumed =
                        JsStringCaseOperations.AppendUpperCaseUnicodeDefault(ref builder, chars, i,
                            out var unitChanged);
                    if (consumed == 2)
                        i++;
                    if (unitChanged)
                        changed = true;
                }

                return changed ? builder.ToString() : value;
            }
            finally
            {
                builder.Dispose();
            }
        }
        finally
        {
            if (pooledChars is not null)
                ArrayPool<char>.Shared.Return(pooledChars);
        }
    }

    private static bool NeedsSpecialCasing(JsString value)
    {
        char[]? pooledChars = null;
        var chars = value.Flatten(out pooledChars);
        try
        {
            foreach (var c in chars)
                if (c == '\u03A3' || c == '\u0130')
                    return true;

            return false;
        }
        finally
        {
            if (pooledChars is not null)
                ArrayPool<char>.Shared.Return(pooledChars);
        }
    }

    private static bool FollowedByDotAbove(ReadOnlySpan<char> value, int index)
    {
        for (var j = index + 1; j < value.Length; j++)
        {
            if (char.IsHighSurrogate(value[j]) && j + 1 < value.Length && char.IsLowSurrogate(value[j + 1]))
            {
                var codePoint = char.ConvertToUtf32(value[j], value[j + 1]);
                var combiningClass = GetCombiningClass(codePoint);
                if (combiningClass == 0 || combiningClass >= 230)
                    return false;
                j++;
                continue;
            }

            var ch = value[j];
            if (ch == '\u0307')
                return true;

            var charCombiningClass = GetCombiningClass(ch);
            if (charCombiningClass == 0 || charCombiningClass >= 230)
                return false;
        }

        return false;
    }

    private static bool FollowedByCombiningClass230(ReadOnlySpan<char> value, int index)
    {
        for (var j = index + 1; j < value.Length; j++)
        {
            int codePoint;
            if (char.IsHighSurrogate(value[j]) && j + 1 < value.Length && char.IsLowSurrogate(value[j + 1]))
            {
                codePoint = char.ConvertToUtf32(value[j], value[j + 1]);
                j++;
            }
            else
            {
                codePoint = value[j];
            }

            var combiningClass = GetCombiningClass(codePoint);
            if (combiningClass == 0)
                return false;
            if (combiningClass == 230)
                return true;
        }

        return false;
    }

    private static int GetCombiningClass(int codePoint)
    {
        if (codePoint < 0x0300)
            return 0;

        return codePoint switch
        {
            >= 0x0300 and <= 0x0314 => 230,
            0x033D or 0x033E or 0x033F => 230,
            0x0340 or 0x0341 or 0x0342 or 0x0343 or 0x0344 or 0x0346 => 230,
            0x034A or 0x034B or 0x034C => 230,
            0x0350 or 0x0351 or 0x0352 => 230,
            0x0357 => 230,
            0x035B => 230,
            >= 0x0363 and <= 0x036F => 230,
            0x035C or 0x035F => 233,
            0x0334 or 0x0335 or 0x0336 or 0x0337 or 0x0338 => 1,
            >= 0x0316 and <= 0x0319 => 220,
            >= 0x031C and <= 0x0320 => 220,
            >= 0x0323 and <= 0x0326 => 220,
            >= 0x0329 and <= 0x0333 => 220,
            0x0339 or 0x033A or 0x033B or 0x033C => 220,
            0x0345 => 240,
            0x0347 or 0x0348 or 0x0349 => 220,
            0x034D or 0x034E => 220,
            0x0353 or 0x0354 or 0x0355 or 0x0356 => 220,
            0x0359 or 0x035A => 220,
            0x031A => 232,
            0x0315 => 232,
            0x0358 => 232,
            >= 0x0590 and <= 0x05CF => GetHebrewCombiningClass(codePoint),
            0x101FD => 220,
            >= 0x1D165 and <= 0x1D169 => 216,
            >= 0x1D16D and <= 0x1D172 => 216,
            >= 0x1D17B and <= 0x1D182 => 220,
            >= 0x1D185 and <= 0x1D189 => 230,
            >= 0x1D18A and <= 0x1D18B => 220,
            _ => codePoint <= 0xFFFF &&
                 CharUnicodeInfo.GetUnicodeCategory((char)codePoint) == UnicodeCategory.NonSpacingMark
                ? 230
                : 0
        };
    }

    private static int GetHebrewCombiningClass(int codePoint)
    {
        return codePoint switch
        {
            >= 0x0591 and <= 0x05AF => 220,
            >= 0x05B0 and <= 0x05BD => 220,
            0x05BF => 230,
            0x05C1 => 230,
            0x05C2 => 220,
            0x05C4 => 230,
            0x05C5 => 220,
            0x05C7 => 220,
            _ => 0
        };
    }

    private static bool IsFinalSigmaContext(ReadOnlySpan<char> value, int index)
    {
        var foundCasedBefore = false;
        for (var i = index - 1; i >= 0; i--)
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

            foundCasedBefore = true;
            break;
        }

        if (!foundCasedBefore)
            return false;

        for (var i = index + 1; i < value.Length; i++)
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
