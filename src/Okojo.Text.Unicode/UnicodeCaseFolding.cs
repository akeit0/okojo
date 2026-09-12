using System.Runtime.CompilerServices;
using System.Text;

namespace Okojo.Text.Unicode;

/// <summary>
///     ECMAScript Unicode case-folding: simple case-fold equivalence classes,
///     canonicalization, and invariant upper/lower mapping.
/// </summary>
public static partial class UnicodeCaseFolding
{
    /// <summary>Maps a code point to its simple case-fold canonical value (uppercase representative).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CanonicalizeUnicode(int codePoint)
    {
        int index = Find(codePoint);
        return index >= 0 ? s_canonicals[index] : codePoint;
    }

    /// <summary>Compares two code points under Unicode simple case folding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EqualsUnicode(int left, int right) =>
        left == right || CanonicalizeUnicode(left) == CanonicalizeUnicode(right);

    /// <summary>Compares two code points under legacy (non-unicode) case folding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EqualsLegacy(int left, int right) =>
        left == right || CanonicalizeLegacy(left) == CanonicalizeLegacy(right);

    /// <summary>
    ///     Legacy canonicalization: upper-cases the code point, but keeps non-ASCII characters that
    ///     would map to an ASCII character unchanged (ECMAScript legacy <c>Canonicalize</c>).
    /// </summary>
    public static int CanonicalizeLegacy(int codePoint)
    {
        int upper = ToUpperInvariant(codePoint);
        return codePoint >= 0x80 && upper < 0x80 ? codePoint : upper;
    }

    /// <summary>
    ///     Reports the simple case-fold equivalence class for a code point as a slice into
    ///     <see cref="GetEquivalent"/>'s backing table.
    /// </summary>
    public static bool TryGetEquivalents(int codePoint, out int offset, out int count)
    {
        int index = Find(codePoint);
        if (index < 0)
        {
            offset = 0;
            count = 0;
            return false;
        }
        offset = s_offsets[index];
        count = s_counts[index];
        return true;
    }

    /// <summary>Returns the <paramref name="index"/>-th equivalent within an equivalence class.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetEquivalent(int offset, int index) => s_values[offset + index];

    /// <summary>Invariant upper-case mapping of a single code point.</summary>
    public static int ToUpperInvariant(int codePoint)
    {
        if ((uint)codePoint <= 0xFFFFu)
        {
            char value = (char)codePoint;
            return char.IsSurrogate(value) ? codePoint : char.ToUpperInvariant(value);
        }
        return (uint)codePoint <= 0x10FFFFu
            ? Rune.ToUpperInvariant(new Rune(codePoint)).Value
            : codePoint;
    }

    /// <summary>Invariant lower-case mapping of a single code point.</summary>
    public static int ToLowerInvariant(int codePoint)
    {
        if ((uint)codePoint <= 0xFFFFu)
        {
            char value = (char)codePoint;
            return char.IsSurrogate(value) ? codePoint : char.ToLowerInvariant(value);
        }
        return (uint)codePoint <= 0x10FFFFu
            ? Rune.ToLowerInvariant(new Rune(codePoint)).Value
            : codePoint;
    }

    /// <summary>All code points that participate in simple case folding.</summary>
    public static ReadOnlySpan<int> MappedCodePoints => s_keys;

    private static int Find(int codePoint)
    {
        int low = 0;
        int high = s_keys.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            int current = s_keys[middle];
            if (codePoint < current)
                high = middle - 1;
            else if (codePoint > current)
                low = middle + 1;
            else
                return middle;
        }
        return -1;
    }
}
