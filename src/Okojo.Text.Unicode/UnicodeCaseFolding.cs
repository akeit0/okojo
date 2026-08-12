using System.Runtime.CompilerServices;
using System.Text;

namespace Okojo.Text.Unicode;

/// <summary>
///     ECMAScript Unicode case-folding: simple case-fold equivalence classes,
///     canonicalization, and invariant upper/lower mapping.
/// </summary>
public static partial class UnicodeCaseFolding
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CanonicalizeUnicode(int codePoint)
    {
        int index = Find(codePoint);
        return index >= 0 ? s_canonicals[index] : codePoint;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EqualsUnicode(int left, int right) =>
        left == right || CanonicalizeUnicode(left) == CanonicalizeUnicode(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EqualsLegacy(int left, int right) =>
        left == right || CanonicalizeLegacy(left) == CanonicalizeLegacy(right);

    public static int CanonicalizeLegacy(int codePoint)
    {
        int upper = ToUpperInvariant(codePoint);
        return codePoint >= 0x80 && upper < 0x80 ? codePoint : upper;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetEquivalent(int offset, int index) => s_values[offset + index];

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
