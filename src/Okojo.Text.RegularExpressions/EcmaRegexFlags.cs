namespace Okojo.Text.RegularExpressions;

/// <summary>Flags defined by ECMAScript RegExp.</summary>
[Flags]
public enum EcmaRegexFlagSet
{
    None = 0,
    HasIndices = 1 << 0, // d
    Global = 1 << 1, // g
    IgnoreCase = 1 << 2, // i
    Multiline = 1 << 3, // m
    DotAll = 1 << 4, // s
    Unicode = 1 << 5, // u
    UnicodeSets = 1 << 6, // v
    Sticky = 1 << 7, // y
}

internal static class EcmaRegexFlagParser
{
    internal static EcmaRegexFlagSet Parse(string? text)
    {
        EcmaRegexFlagSet result = EcmaRegexFlagSet.None;
        if (string.IsNullOrEmpty(text))
            return result;

        foreach (char ch in text)
        {
            EcmaRegexFlagSet flag = ch switch
            {
                'd' => EcmaRegexFlagSet.HasIndices,
                'g' => EcmaRegexFlagSet.Global,
                'i' => EcmaRegexFlagSet.IgnoreCase,
                'm' => EcmaRegexFlagSet.Multiline,
                's' => EcmaRegexFlagSet.DotAll,
                'u' => EcmaRegexFlagSet.Unicode,
                'v' => EcmaRegexFlagSet.UnicodeSets,
                'y' => EcmaRegexFlagSet.Sticky,
                _ => throw new EcmaRegexParseException(
                    $"Unknown ECMAScript regular-expression flag '{ch}'.",
                    -1,
                    EcmaRegexError.InvalidFlag
                ),
            };

            if ((result & flag) != 0)
            {
                throw new EcmaRegexParseException(
                    $"Duplicate ECMAScript regular-expression flag '{ch}'.",
                    -1,
                    EcmaRegexError.InvalidFlag
                );
            }
            result |= flag;
        }

        if (
            (result & (EcmaRegexFlagSet.Unicode | EcmaRegexFlagSet.UnicodeSets))
            == (EcmaRegexFlagSet.Unicode | EcmaRegexFlagSet.UnicodeSets)
        )
        {
            throw new EcmaRegexParseException(
                "The ECMAScript 'u' and 'v' flags are mutually exclusive.",
                -1,
                EcmaRegexError.IncompatibleFlags
            );
        }
        return result;
    }

    internal static string Format(EcmaRegexFlagSet flags)
    {
        Span<char> buffer = stackalloc char[8];
        int length = 0;
        if ((flags & EcmaRegexFlagSet.HasIndices) != 0)
            buffer[length++] = 'd';
        if ((flags & EcmaRegexFlagSet.Global) != 0)
            buffer[length++] = 'g';
        if ((flags & EcmaRegexFlagSet.IgnoreCase) != 0)
            buffer[length++] = 'i';
        if ((flags & EcmaRegexFlagSet.Multiline) != 0)
            buffer[length++] = 'm';
        if ((flags & EcmaRegexFlagSet.DotAll) != 0)
            buffer[length++] = 's';
        if ((flags & EcmaRegexFlagSet.Unicode) != 0)
            buffer[length++] = 'u';
        if ((flags & EcmaRegexFlagSet.UnicodeSets) != 0)
            buffer[length++] = 'v';
        if ((flags & EcmaRegexFlagSet.Sticky) != 0)
            buffer[length++] = 'y';
        return new string(buffer[..length]);
    }
}
