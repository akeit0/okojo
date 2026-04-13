namespace Okojo.RegExp.Experimental;

internal static class ScratchUnicodeRangeSearch
{
    public static bool ContainsRange(ReadOnlySpan<int> ranges, int codePoint)
    {
        if (ranges.IsEmpty || codePoint < ranges[0] || codePoint > ranges[^1])
            return false;

        var lo = 0;
        var hi = (ranges.Length >> 1) - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var rangeIndex = mid << 1;
            var start = ranges[rangeIndex];
            var end = ranges[rangeIndex + 1];
            if (codePoint < start)
                hi = mid - 1;
            else if (codePoint > end)
                lo = mid + 1;
            else
                return true;
        }

        return false;
    }
}
