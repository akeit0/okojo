namespace Okojo.Values;

public readonly partial struct JsString
{
    public static bool EqualsOrdinal(JsString left, JsString right)
    {
        var leftObject = left.StringLikeObject;
        var rightObject = right.StringLikeObject;

        if (ReferenceEquals(leftObject, rightObject))
            return true;

        var leftLength = GetLength(leftObject);
        if (leftLength != GetLength(rightObject))
            return false;

        if (TryGetFlatSpan(leftObject, out var leftSpan) &&
            TryGetFlatSpan(rightObject, out var rightSpan))
            return leftSpan.SequenceEqual(rightSpan);

        return CompareRanges(leftObject, 0, rightObject, 0, leftLength) == 0;
    }

    public static int CompareOrdinal(JsString left, JsString right)
    {
        if (ReferenceEquals(left.StringLikeObject, right.StringLikeObject))
            return 0;

        if (TryGetFlatSpan(left.StringLikeObject, out var leftSpan) &&
            TryGetFlatSpan(right.StringLikeObject, out var rightSpan))
            return leftSpan.SequenceCompareTo(rightSpan);

        var commonLength = Math.Min(left.Length, right.Length);
        var cmp = CompareRanges(left.StringLikeObject, 0, right.StringLikeObject, 0, commonLength);
        return cmp != 0 ? cmp : left.Length.CompareTo(right.Length);
    }

    public static bool StartsWith(JsString value, JsString prefix)
    {
        if (TryGetFlatSpan(value.StringLikeObject, out var valueSpan) &&
            TryGetFlatSpan(prefix.StringLikeObject, out var prefixSpan))
            return valueSpan.StartsWith(prefixSpan, StringComparison.Ordinal);

        return prefix.Length <= value.Length &&
               CompareRanges(value.StringLikeObject, 0, prefix.StringLikeObject, 0, prefix.Length) == 0;
    }

    public static bool EndsWith(JsString value, JsString suffix)
    {
        if (TryGetFlatSpan(value.StringLikeObject, out var valueSpan) &&
            TryGetFlatSpan(suffix.StringLikeObject, out var suffixSpan))
            return valueSpan.EndsWith(suffixSpan, StringComparison.Ordinal);

        return suffix.Length <= value.Length &&
               CompareRanges(value.StringLikeObject, value.Length - suffix.Length, suffix.StringLikeObject, 0,
                   suffix.Length) == 0;
    }

    public static int IndexOf(JsString value, JsString needle, int startIndex = 0)
    {
        var valueLength = value.Length;
        var needleLength = needle.Length;

        if ((uint)startIndex > (uint)valueLength)
            throw new ArgumentOutOfRangeException(nameof(startIndex));

        if (needleLength == 0)
            return startIndex;

        if (needleLength > valueLength - startIndex)
            return -1;

        if (TryGetFlatSpan(value.StringLikeObject, out var valueSpan) &&
            TryGetFlatSpan(needle.StringLikeObject, out var needleSpan))
            return valueSpan.Slice(startIndex).IndexOf(needleSpan, StringComparison.Ordinal) is int index && index >= 0
                ? startIndex + index
                : -1;

        var flatNeedle = needle.Flatten();
        var first = flatNeedle[0];
        var position = IndexOfChar(value.StringLikeObject, first, startIndex);
        while (position >= 0)
        {
            if (CompareRangeToFlatString(value.StringLikeObject, position, flatNeedle) == 0)
                return position;

            position = IndexOfChar(value.StringLikeObject, first, position + 1);
        }

        return -1;
    }

    public static int LastIndexOf(JsString value, JsString needle, int startIndex)
    {
        var valueLength = value.Length;
        var needleLength = needle.Length;

        if (needleLength == 0)
            return Math.Min(startIndex, valueLength);

        if (needleLength > valueLength)
            return -1;

        var searchStart = Math.Min(startIndex, valueLength - needleLength);
        if (searchStart < 0)
            return -1;

        if (TryGetFlatSpan(value.StringLikeObject, out var valueSpan) &&
            TryGetFlatSpan(needle.StringLikeObject, out var needleSpan))
            return valueSpan[..(searchStart + needleLength)].LastIndexOf(needleSpan, StringComparison.Ordinal);

        var flatNeedle = needle.Flatten();
        for (var position = searchStart; position >= 0; position--)
            if (CompareRangeToFlatString(value.StringLikeObject, position, flatNeedle) == 0)
                return position;

        return -1;
    }

    private static int CompareRanges(object left, int leftStart, object right, int rightStart, int length)
    {
        var leftChunks = new ChunkEnumerator(left, leftStart, length);
        var rightChunks = new ChunkEnumerator(right, rightStart, length);

        ReadOnlySpan<char> leftSpan = default;
        ReadOnlySpan<char> rightSpan = default;
        var leftOffset = 0;
        var rightOffset = 0;

        while (true)
        {
            if (leftOffset >= leftSpan.Length)
            {
                if (!leftChunks.MoveNext())
                    return 0;

                leftSpan = leftChunks.Current;
                leftOffset = 0;
            }

            if (rightOffset >= rightSpan.Length)
            {
                rightChunks.MoveNext();
                rightSpan = rightChunks.Current;
                rightOffset = 0;
            }

            var count = Math.Min(leftSpan.Length - leftOffset, rightSpan.Length - rightOffset);
            var cmp = leftSpan.Slice(leftOffset, count).SequenceCompareTo(rightSpan.Slice(rightOffset, count));
            if (cmp != 0)
                return cmp;

            leftOffset += count;
            rightOffset += count;
        }
    }

    private static int CompareRangeToFlatString(object value, int start, string needle)
    {
        var chunks = new ChunkEnumerator(value, start, needle.Length);
        var offset = 0;

        while (chunks.MoveNext())
        {
            var chunk = chunks.Current;
            var cmp = chunk.SequenceCompareTo(needle.AsSpan(offset, chunk.Length));
            if (cmp != 0)
                return cmp;
            offset += chunk.Length;
        }

        return 0;
    }

    private static int IndexOfChar(object value, char needle, int startIndex)
    {
        var position = startIndex;
        var chunks = new ChunkEnumerator(value, startIndex, GetLength(value) - startIndex);
        while (chunks.MoveNext())
        {
            var chunk = chunks.Current;
            var localIndex = chunk.IndexOf(needle);
            if (localIndex >= 0)
                return position + localIndex;
            position += chunk.Length;
        }

        return -1;
    }
}
