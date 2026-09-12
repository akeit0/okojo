namespace Okojo.Text.RegularExpressions;

/// <summary>Flags defined by ECMAScript RegExp.</summary>
[Flags]
public enum RegExpFlags
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>The <c>d</c> (has indices) flag.</summary>
    HasIndices = 1 << 0,

    /// <summary>The <c>g</c> (global) flag.</summary>
    Global = 1 << 1,

    /// <summary>The <c>i</c> (ignore case) flag.</summary>
    IgnoreCase = 1 << 2,

    /// <summary>The <c>m</c> (multiline) flag.</summary>
    Multiline = 1 << 3,

    /// <summary>The <c>s</c> (dot all) flag.</summary>
    DotAll = 1 << 4,

    /// <summary>The <c>u</c> (unicode) flag.</summary>
    Unicode = 1 << 5,

    /// <summary>The <c>v</c> (unicode sets) flag.</summary>
    UnicodeSets = 1 << 6,

    /// <summary>The <c>y</c> (sticky) flag.</summary>
    Sticky = 1 << 7,
}

internal static class RegExpFlagParser
{
    internal static RegExpFlags Parse(string? text)
    {
        RegExpFlags result = RegExpFlags.None;
        if (string.IsNullOrEmpty(text))
            return result;

        foreach (char ch in text)
        {
            RegExpFlags flag = ch switch
            {
                'd' => RegExpFlags.HasIndices,
                'g' => RegExpFlags.Global,
                'i' => RegExpFlags.IgnoreCase,
                'm' => RegExpFlags.Multiline,
                's' => RegExpFlags.DotAll,
                'u' => RegExpFlags.Unicode,
                'v' => RegExpFlags.UnicodeSets,
                'y' => RegExpFlags.Sticky,
                _ => throw new RegExpParseException(
                    $"Unknown ECMAScript regular-expression flag '{ch}'.",
                    -1,
                    RegExpParseError.InvalidFlag
                ),
            };

            if ((result & flag) != 0)
            {
                throw new RegExpParseException(
                    $"Duplicate ECMAScript regular-expression flag '{ch}'.",
                    -1,
                    RegExpParseError.InvalidFlag
                );
            }
            result |= flag;
        }

        if (
            (result & (RegExpFlags.Unicode | RegExpFlags.UnicodeSets))
            == (RegExpFlags.Unicode | RegExpFlags.UnicodeSets)
        )
        {
            throw new RegExpParseException(
                "The ECMAScript 'u' and 'v' flags are mutually exclusive.",
                -1,
                RegExpParseError.IncompatibleFlags
            );
        }
        return result;
    }

    internal static string Format(RegExpFlags flags)
    {
        Span<char> buffer = stackalloc char[8];
        int length = 0;
        if ((flags & RegExpFlags.HasIndices) != 0)
            buffer[length++] = 'd';
        if ((flags & RegExpFlags.Global) != 0)
            buffer[length++] = 'g';
        if ((flags & RegExpFlags.IgnoreCase) != 0)
            buffer[length++] = 'i';
        if ((flags & RegExpFlags.Multiline) != 0)
            buffer[length++] = 'm';
        if ((flags & RegExpFlags.DotAll) != 0)
            buffer[length++] = 's';
        if ((flags & RegExpFlags.Unicode) != 0)
            buffer[length++] = 'u';
        if ((flags & RegExpFlags.UnicodeSets) != 0)
            buffer[length++] = 'v';
        if ((flags & RegExpFlags.Sticky) != 0)
            buffer[length++] = 'y';
        return new string(buffer[..length]);
    }
}
