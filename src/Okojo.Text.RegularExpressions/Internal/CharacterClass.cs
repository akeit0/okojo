using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

using Okojo.Text.Unicode;
namespace Okojo.Text.RegularExpressions.Internal;

internal readonly record struct CodePointRange(int Start, int End);

/// <summary>
/// Immutable set of class members for a /v character class: a set of single
/// code points plus a set of multi-code-point strings (from \q disjunctions or
/// Unicode properties of strings).
/// </summary>
internal sealed class UnicodeSet
{
    private static readonly UnicodeSet s_empty = new(CharSet.Empty, []);

    internal UnicodeSet(CharSet codePoints, string[] strings)
    {
        CodePoints = codePoints;
        Strings = strings;
    }

    internal static UnicodeSet Empty => s_empty;

    /// <summary>Single-code-point members.</summary>
    internal CharSet CodePoints { get; }

    /// <summary>Members of at least two code points, deduplicated and sorted.</summary>
    internal string[] Strings { get; }

    internal static UnicodeSet FromCodePoints(CharSet set) => new(set, []);

    internal static UnicodeSet FromSingle(int codePoint) =>
        new(CharSet.SingleCodePoint(codePoint), []);

    internal static UnicodeSet FromStrings(IEnumerable<string> strings)
    {
        CharSetBuilder builder = new();
        HashSet<string> multi = new(StringComparer.Ordinal);
        foreach (string value in strings)
        {
            int codePointCount = Utf16.CountCodePoints(value);
            if (codePointCount <= 1)
                builder.Add(Utf16.ReadCodePoint(value, 0));
            else
                multi.Add(value);
        }
        CharSet singles = builder.Build();
        string[] sorted = multi.ToArray();
        Array.Sort(sorted, StringComparer.Ordinal);
        return new UnicodeSet(singles, sorted);
    }

    internal static UnicodeSet Union(UnicodeSet left, UnicodeSet right)
    {
        if (left.Strings.Length == 0 && right.Strings.Length == 0)
            return FromCodePoints(CharSet.Union(left.CodePoints, right.CodePoints));
        HashSet<string> strings = new(StringComparer.Ordinal);
        strings.UnionWith(left.Strings);
        strings.UnionWith(right.Strings);
        string[] merged = strings.ToArray();
        Array.Sort(merged, StringComparer.Ordinal);
        return new UnicodeSet(CharSet.Union(left.CodePoints, right.CodePoints), merged);
    }

    internal static UnicodeSet Intersect(UnicodeSet left, UnicodeSet right)
    {
        if (left.Strings.Length == 0)
            return FromCodePoints(CharSet.Intersect(left.CodePoints, right.CodePoints));
        if (right.Strings.Length == 0)
            return FromCodePoints(CharSet.Intersect(left.CodePoints, right.CodePoints));
        HashSet<string> rightStrings = new(right.Strings, StringComparer.Ordinal);
        HashSet<string> strings = new(StringComparer.Ordinal);
        foreach (string value in left.Strings)
        {
            if (rightStrings.Contains(value))
                strings.Add(value);
        }
        string[] merged = strings.ToArray();
        Array.Sort(merged, StringComparer.Ordinal);
        return new UnicodeSet(CharSet.Intersect(left.CodePoints, right.CodePoints), merged);
    }

    internal static UnicodeSet Subtract(UnicodeSet left, UnicodeSet right)
    {
        CharSet codePoints = CharSet.Subtract(left.CodePoints, right.CodePoints);
        if (left.Strings.Length == 0)
            return FromCodePoints(codePoints);
        if (right.Strings.Length == 0)
            return new UnicodeSet(codePoints, left.Strings);
        HashSet<string> rightStrings = new(right.Strings, StringComparer.Ordinal);
        HashSet<string> strings = new(StringComparer.Ordinal);
        foreach (string value in left.Strings)
        {
            if (!rightStrings.Contains(value))
                strings.Add(value);
        }
        string[] merged = strings.ToArray();
        Array.Sort(merged, StringComparer.Ordinal);
        return new UnicodeSet(codePoints, merged);
    }

    internal string DebugDisplay()
    {
        StringBuilder builder = new(CodePoints.DebugDisplay());
        if (Strings.Length != 0)
            builder.Append(" strings=").Append(Strings.Length);
        return builder.ToString();
    }
}

/// <summary>Immutable sorted, non-overlapping set of Unicode code points/code units.</summary>
internal sealed class CharSet
{
    private static readonly CharSet s_empty = new([]);
    private static readonly CharSet s_allUnicode = new([new CodePointRange(0, 0x10FFFF)]);
    private static readonly Lazy<CharSet> s_unicodeSetsFoldDomain = new(
        CreateUnicodeSetsFoldDomain,
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    private readonly CodePointRange[] _ranges;

    // One bit per code point for 0..255, checked before the range binary search.
    private readonly ulong _ascii0;
    private readonly ulong _ascii1;
    private readonly ulong _ascii2;
    private readonly ulong _ascii3;

    internal CharSet(CodePointRange[] ranges)
    {
        _ranges = ranges;
        ulong ascii0 = 0,
            ascii1 = 0,
            ascii2 = 0,
            ascii3 = 0;
        foreach (CodePointRange range in ranges)
        {
            if (range.Start > 255)
                break;
            int end = Math.Min(range.End, 255);
            for (int codePoint = Math.Max(0, range.Start); codePoint <= end; codePoint++)
            {
                int bit = codePoint & 63;
                ulong mask = 1UL << bit;
                switch (codePoint >> 6)
                {
                    case 0:
                        ascii0 |= mask;
                        break;
                    case 1:
                        ascii1 |= mask;
                        break;
                    case 2:
                        ascii2 |= mask;
                        break;
                    default:
                        ascii3 |= mask;
                        break;
                }
            }
        }
        _ascii0 = ascii0;
        _ascii1 = ascii1;
        _ascii2 = ascii2;
        _ascii3 = ascii3;
    }

    internal static CharSet Empty => s_empty;
    internal static CharSet AllUnicode => s_allUnicode;
    internal ReadOnlySpan<CodePointRange> Ranges => _ranges;
    internal bool IsSingle => Ranges.Length == 1 && Ranges[0].Start == Ranges[0].End;
    internal int Single =>
        IsSingle ? Ranges[0].Start : throw new InvalidOperationException("Set is not a singleton.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(int codePoint)
    {
        if ((uint)codePoint < 256)
        {
            ulong mask = 1UL << (codePoint & 63);
            return (codePoint >> 6) switch
            {
                0 => (_ascii0 & mask) != 0,
                1 => (_ascii1 & mask) != 0,
                2 => (_ascii2 & mask) != 0,
                _ => (_ascii3 & mask) != 0,
            };
        }
        int low = 0;
        int high = Ranges.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            CodePointRange range = Ranges[middle];
            if (codePoint < range.Start)
                high = middle - 1;
            else if (codePoint > range.End)
                low = middle + 1;
            else
                return true;
        }
        return false;
    }

    internal bool ContainsCaseInsensitive(int codePoint, bool unicode)
    {
        if (Contains(codePoint))
            return true;
        if (unicode)
        {
            if (!UnicodeCaseFolding.TryGetEquivalents(codePoint, out int offset, out int count))
                return false;
            for (int i = 0; i < count; i++)
            {
                if (Contains(UnicodeCaseFolding.GetEquivalent(offset, i)))
                    return true;
            }
            return false;
        }

        int canonical = UnicodeCaseFolding.CanonicalizeLegacy(codePoint);
        if (canonical != codePoint && Contains(canonical))
            return true;
        // Legacy Canonicalize is not necessarily symmetric under one conversion; compare
        // the handful of simple invariant variants without allocating.
        int upper = UnicodeCaseFolding.ToUpperInvariant(codePoint);
        int lower = UnicodeCaseFolding.ToLowerInvariant(codePoint);
        return (
                upper != codePoint
                && UnicodeCaseFolding.EqualsLegacy(codePoint, upper)
                && Contains(upper)
            )
            || (
                lower != codePoint
                && UnicodeCaseFolding.EqualsLegacy(codePoint, lower)
                && Contains(lower)
            );
    }

    internal static CharSet SingleCodePoint(int value) => new([new CodePointRange(value, value)]);

    internal static CharSet Union(CharSet left, CharSet right)
    {
        if (left.Ranges.Length == 0)
            return right;
        if (right.Ranges.Length == 0)
            return left;
        CharSetBuilder builder = new(left.Ranges.Length + right.Ranges.Length);
        builder.AddRanges(left.Ranges);
        builder.AddRanges(right.Ranges);
        return builder.Build();
    }

    internal static CharSet Intersect(CharSet left, CharSet right)
    {
        if (left.Ranges.Length == 0 || right.Ranges.Length == 0)
            return Empty;
        List<CodePointRange> result = [];
        int i = 0;
        int j = 0;
        while (i < left.Ranges.Length && j < right.Ranges.Length)
        {
            CodePointRange a = left.Ranges[i];
            CodePointRange b = right.Ranges[j];
            int start = Math.Max(a.Start, b.Start);
            int end = Math.Min(a.End, b.End);
            if (start <= end)
                result.Add(new CodePointRange(start, end));
            if (a.End < b.End)
                i++;
            else
                j++;
        }
        return result.Count == 0 ? Empty : new CharSet(result.ToArray());
    }

    internal static CharSet Subtract(CharSet left, CharSet right)
    {
        if (left.Ranges.Length == 0 || right.Ranges.Length == 0)
            return left;
        List<CodePointRange> result = [];
        int j = 0;
        foreach (CodePointRange source in left.Ranges)
        {
            int current = source.Start;
            while (j < right.Ranges.Length && right.Ranges[j].End < current)
                j++;
            int k = j;
            while (k < right.Ranges.Length && right.Ranges[k].Start <= source.End)
            {
                CodePointRange remove = right.Ranges[k];
                if (remove.Start > current)
                    result.Add(new CodePointRange(current, remove.Start - 1));
                if (remove.End == int.MaxValue)
                {
                    current = int.MaxValue;
                    break;
                }
                current = Math.Max(current, remove.End + 1);
                if (current > source.End)
                    break;
                k++;
            }
            if (current <= source.End)
                result.Add(new CodePointRange(current, source.End));
        }
        return result.Count == 0 ? Empty : new CharSet(result.ToArray());
    }

    internal static CharSet Complement(CharSet set, int maximum) =>
        Subtract(new CharSet([new CodePointRange(0, maximum)]), set);

    /// <summary>
    /// ECMAScript /v+i operands are transformed to simple-case-fold canonical values before
    /// intersection/subtraction. This returns that canonical value set.
    /// </summary>
    internal CharSet UnicodeCaseClosure()
    {
        if (Ranges.Length == 0)
            return this;
        CharSetBuilder builder = new(Ranges.Length + 64);
        builder.AddRanges(Ranges);
        ReadOnlySpan<int> keys = UnicodeCaseFolding.MappedCodePoints;
        for (int i = 0; i < keys.Length; i++)
        {
            int key = keys[i];
            if (!Contains(key))
                continue;
            if (!UnicodeCaseFolding.TryGetEquivalents(key, out int offset, out int count))
                continue;
            for (int equivalent = 0; equivalent < count; equivalent++)
            {
                builder.Add(UnicodeCaseFolding.GetEquivalent(offset, equivalent));
            }
        }
        return builder.Build();
    }

    internal CharSet FoldForUnicodeSets()
    {
        if (Ranges.Length == 0)
            return this;
        CharSetBuilder mappedKeys = new();
        CharSetBuilder canonicalValues = new();
        ReadOnlySpan<int> keys = UnicodeCaseFolding.MappedCodePoints;
        for (int i = 0; i < keys.Length; i++)
        {
            int key = keys[i];
            int canonical = UnicodeCaseFolding.CanonicalizeUnicode(key);
            if (canonical == key || !Contains(key))
                continue;
            mappedKeys.Add(key);
            canonicalValues.Add(canonical);
        }
        CharSet withoutMapped = Subtract(this, mappedKeys.Build());
        return Union(withoutMapped, canonicalValues.Build());
    }

    internal static CharSet ComplementUnicodeSets(CharSet foldedSet, bool ignoreCase) =>
        Subtract(ignoreCase ? s_unicodeSetsFoldDomain.Value : AllUnicode, foldedSet);

    private static CharSet CreateUnicodeSetsFoldDomain()
    {
        CharSetBuilder nonCanonical = new();
        ReadOnlySpan<int> keys = UnicodeCaseFolding.MappedCodePoints;
        for (int i = 0; i < keys.Length; i++)
        {
            int key = keys[i];
            if (UnicodeCaseFolding.CanonicalizeUnicode(key) != key)
                nonCanonical.Add(key);
        }
        return Subtract(AllUnicode, nonCanonical.Build());
    }

    internal string DebugDisplay()
    {
        if (Ranges.Length == 0)
            return "[]";
        StringBuilder builder = new("[");
        int shown = Math.Min(Ranges.Length, 8);
        for (int i = 0; i < shown; i++)
        {
            if (i != 0)
                builder.Append(' ');
            CodePointRange range = Ranges[i];
            builder.Append("U+").Append(range.Start.ToString("X", CultureInfo.InvariantCulture));
            if (range.End != range.Start)
                builder.Append("-U+").Append(range.End.ToString("X", CultureInfo.InvariantCulture));
        }
        if (shown != Ranges.Length)
            builder.Append(" …");
        return builder.Append(']').ToString();
    }
}

internal sealed class CharSetBuilder
{
    private readonly List<CodePointRange> _ranges;

    internal CharSetBuilder(int capacity = 8) => _ranges = new List<CodePointRange>(capacity);

    internal void Add(int value) => Add(value, value);

    internal void Add(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, end);
        _ranges.Add(new CodePointRange(start, end));
    }

    internal void AddRanges(ReadOnlySpan<CodePointRange> ranges)
    {
        foreach (CodePointRange range in ranges)
            _ranges.Add(range);
    }

    internal void AddSet(CharSet set) => AddRanges(set.Ranges);

    internal CharSet Build()
    {
        if (_ranges.Count == 0)
            return CharSet.Empty;
        _ranges.Sort(
            static (a, b) =>
                a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End)
        );
        List<CodePointRange> merged = new(_ranges.Count);
        int start = _ranges[0].Start;
        int end = _ranges[0].End;
        for (int i = 1; i < _ranges.Count; i++)
        {
            CodePointRange next = _ranges[i];
            if (next.Start <= end || (end != int.MaxValue && next.Start == end + 1))
            {
                end = Math.Max(end, next.End);
            }
            else
            {
                merged.Add(new CodePointRange(start, end));
                start = next.Start;
                end = next.End;
            }
        }
        merged.Add(new CodePointRange(start, end));
        return new CharSet(merged.ToArray());
    }
}
