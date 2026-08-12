using System.Runtime.CompilerServices;

namespace Okojo.Text.RegularExpressions.Internal;

internal static partial class UnicodePropertyDatabase
{
    private static readonly CharSet?[] s_setCache = new CharSet[s_entries.Length / 2];
    private static readonly int s_idStart = ResolvePropertyId("ID_Start");
    private static readonly int s_idContinue = ResolvePropertyId("ID_Continue");
    private static readonly int s_whiteSpace = ResolvePropertyId("White_Space");

    private static readonly CharSet s_digit = CreateRange('0', '9');
    private static readonly CharSet s_word = CreateWord();
    private static readonly CharSet s_space = CreateSpace();

    internal static CharSet Digit => s_digit;
    internal static CharSet Word => s_word;
    internal static CharSet WhiteSpace => s_space;

    internal static bool TryResolve(ReadOnlySpan<char> expression, out int propertyId)
    {
        if (!TryNormalize(expression, out string normalized))
        {
            propertyId = -1;
            return false;
        }
        if (UnicodePropertyDatabaseAdditional.TryResolve(normalized, out int additionalId))
        {
            propertyId = ~additionalId;
            return true;
        }
        if (s_aliases.TryGetValue(normalized, out propertyId))
            return true;
        propertyId = -1;
        return false;
    }

    internal static bool TryResolveStrings(ReadOnlySpan<char> expression, out string[] strings)
    {
        if (TryNormalize(expression, out string normalized))
            return TryResolveStringsNormalized(normalized, out strings);
        strings = [];
        return false;
    }

    private static bool TryNormalize(ReadOnlySpan<char> expression, out string normalized)
    {
        if (expression.IsEmpty)
        {
            normalized = string.Empty;
            return false;
        }

        Span<char> scratch =
            expression.Length <= 256
                ? stackalloc char[expression.Length]
                : new char[expression.Length];
        int written = 0;
        foreach (char value in expression)
        {
            if (value is >= 'a' and <= 'z')
                scratch[written++] = (char)(value - ('a' - 'A'));
            else if (value is >= 'A' and <= 'Z' or >= '0' and <= '9')
                scratch[written++] = value;
            else if (value == '=')
                scratch[written++] = value;
            else if (value == '_') { }
            else
            {
                normalized = string.Empty;
                return false;
            }
        }
        normalized = new string(scratch[..written]);
        return true;
    }

    internal static CharSet Resolve(string expression, bool negate, int maximum)
    {
        if (!TryResolve(expression, out int id))
        {
            throw new KeyNotFoundException($"Unknown ECMAScript Unicode property '{expression}'.");
        }
        CharSet set = GetSet(id);
        if (maximum < 0x10FFFF)
            set = CharSet.Intersect(set, CreateRange(0, maximum));
        return negate ? CharSet.Complement(set, maximum) : set;
    }

    internal static CharSet GetSet(int propertyId)
    {
        if (propertyId < 0)
        {
            int additionalId = ~propertyId;
            return UnicodePropertyDatabaseAdditional.GetSet(additionalId);
        }
        if ((uint)propertyId >= (uint)s_setCache.Length)
            throw new ArgumentOutOfRangeException(nameof(propertyId));
        CharSet? cached = Volatile.Read(ref s_setCache[propertyId]);
        if (cached is not null)
            return cached;

        int offset = s_entries[propertyId * 2];
        int count = s_entries[propertyId * 2 + 1];
        CodePointRange[] ranges = new CodePointRange[count];
        for (int i = 0; i < count; i++)
        {
            ranges[i] = new CodePointRange(s_ranges[offset + i * 2], s_ranges[offset + i * 2 + 1]);
        }
        CharSet created = ranges.Length == 0 ? CharSet.Empty : new CharSet(ranges);
        return Interlocked.CompareExchange(ref s_setCache[propertyId], created, null) ?? created;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Contains(int propertyId, int codePoint)
    {
        if ((uint)codePoint > 0x10FFFFu)
            return false;
        if (propertyId < 0)
            return UnicodePropertyDatabaseAdditional.GetSet(~propertyId).Contains(codePoint);
        if ((uint)propertyId >= (uint)(s_entries.Length / 2))
            return false;
        int offset = s_entries[propertyId * 2];
        int count = s_entries[propertyId * 2 + 1];
        int low = 0;
        int high = count - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            int rangeOffset = offset + middle * 2;
            int start = s_ranges[rangeOffset];
            int end = s_ranges[rangeOffset + 1];
            if (codePoint < start)
                high = middle - 1;
            else if (codePoint > end)
                low = middle + 1;
            else
                return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsIdentifierStart(int codePoint) =>
        codePoint is '_' or '$' || Contains(s_idStart, codePoint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsIdentifierContinue(int codePoint) =>
        codePoint is '_' or '$' or 0x200C or 0x200D || Contains(s_idContinue, codePoint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsEcmaWhitespace(int codePoint) =>
        codePoint == 0xFEFF || Contains(s_whiteSpace, codePoint);

    private static int ResolvePropertyId(string name) =>
        TryResolve(name, out int id)
            ? id
            : throw new InvalidOperationException($"Generated property '{name}' is missing.");

    private static CharSet CreateRange(int start, int end) => new([new CodePointRange(start, end)]);

    private static CharSet CreateWord() =>
        new([
            new CodePointRange('0', '9'),
            new CodePointRange('A', 'Z'),
            new CodePointRange('_', '_'),
            new CodePointRange('a', 'z'),
        ]);

    private static CharSet CreateSpace()
    {
        CharSetBuilder builder = new();
        builder.AddRanges(GetSet(s_whiteSpace).Ranges);
        builder.Add(0xFEFF);
        return CharSet.Subtract(builder.Build(), CharSet.SingleCodePoint(0x0085));
    }
}
