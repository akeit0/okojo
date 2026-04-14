namespace Okojo.RegExp;

internal static partial class ScratchUnicodeStringPropertyTables
{
    private static readonly StringPropertyMatcher EmptyMatcher = new([]);
    private static readonly StringPropertyMatcher BasicEmojiMatcher = new(BasicEmojiStrings);
    private static readonly StringPropertyMatcher EmojiKeycapSequenceMatcher = new(EmojiKeycapSequenceStrings);
    private static readonly StringPropertyMatcher RgiEmojiMatcher = new(RgiEmojiStrings);
    private static readonly StringPropertyMatcher RgiEmojiFlagSequenceMatcher = new(RgiEmojiFlagSequenceStrings);
    private static readonly StringPropertyMatcher RgiEmojiModifierSequenceMatcher = new(RgiEmojiModifierSequenceStrings);
    private static readonly StringPropertyMatcher RgiEmojiTagSequenceMatcher = new(RgiEmojiTagSequenceStrings);
    private static readonly StringPropertyMatcher RgiEmojiZwjSequenceMatcher = new(RgiEmojiZwjSequenceStrings);

    public static bool TryMatchAt(string property, string input, int pos, out int endIndex)
    {
        return GetMatcher(property).TryMatchAt(input, pos, out endIndex);
    }

    public static bool TryMatchBackward(string property, string input, int pos, out int startIndex)
    {
        return GetMatcher(property).TryMatchBackward(input, pos, out startIndex);
    }

    public static void AddMatchEndsAt(string property, string input, int pos, ScratchPooledIntSet results)
    {
        GetMatcher(property).AddMatchEndsAt(input, pos, results);
    }

    public static void AddMatchStartsBackward(string property, string input, int pos, ScratchPooledIntSet results)
    {
        GetMatcher(property).AddMatchStartsBackward(input, pos, results);
    }

    public static bool TryGetLengthRange(string property, out int minLength, out int maxLength)
    {
        var matcher = GetMatcher(property);
        if (!matcher.HasCandidates)
        {
            minLength = default;
            maxLength = default;
            return false;
        }

        minLength = matcher.MinLength;
        maxLength = matcher.MaxLength;
        return true;
    }

    private static StringPropertyMatcher GetMatcher(string property)
    {
        return property switch
        {
            "Basic_Emoji" => BasicEmojiMatcher,
            "Emoji_Keycap_Sequence" => EmojiKeycapSequenceMatcher,
            "RGI_Emoji" => RgiEmojiMatcher,
            "RGI_Emoji_Flag_Sequence" => RgiEmojiFlagSequenceMatcher,
            "RGI_Emoji_Modifier_Sequence" => RgiEmojiModifierSequenceMatcher,
            "RGI_Emoji_Tag_Sequence" => RgiEmojiTagSequenceMatcher,
            "RGI_Emoji_ZWJ_Sequence" => RgiEmojiZwjSequenceMatcher,
            _ => EmptyMatcher
        };
    }

    private static bool TryReadCodePointForward(string input, int pos, out int codePoint)
    {
        if ((uint)pos >= (uint)input.Length)
        {
            codePoint = default;
            return false;
        }

        var current = input[pos];
        if (char.IsHighSurrogate(current) &&
            pos + 1 < input.Length &&
            char.IsLowSurrogate(input[pos + 1]))
        {
            codePoint = char.ConvertToUtf32(current, input[pos + 1]);
            return true;
        }

        codePoint = current;
        return true;
    }

    private static bool TryReadCodePointBackward(string input, int pos, out int codePoint)
    {
        if (pos <= 0 || pos > input.Length)
        {
            codePoint = default;
            return false;
        }

        var current = input[pos - 1];
        if (char.IsLowSurrogate(current) &&
            pos - 2 >= 0 &&
            char.IsHighSurrogate(input[pos - 2]))
        {
            codePoint = char.ConvertToUtf32(input[pos - 2], current);
            return true;
        }

        codePoint = current;
        return true;
    }

    private static int ReadFirstCodePoint(string value)
    {
        return TryReadCodePointForward(value, 0, out var codePoint) ? codePoint : -1;
    }

    private static int ReadLastCodePoint(string value)
    {
        return TryReadCodePointBackward(value, value.Length, out var codePoint) ? codePoint : -1;
    }

    private static bool MatchesAt(string input, int pos, string candidate)
    {
        return pos + candidate.Length <= input.Length &&
               input.AsSpan(pos, candidate.Length).SequenceEqual(candidate.AsSpan());
    }

    private static bool MatchesBackward(string input, int pos, string candidate)
    {
        return pos >= candidate.Length &&
               input.AsSpan(pos - candidate.Length, candidate.Length).SequenceEqual(candidate.AsSpan());
    }

    private sealed class StringPropertyMatcher
    {
        private readonly StringPropertyBucket[] forwardBuckets;
        private readonly StringPropertyBucket[] backwardBuckets;
        public int MinLength { get; }
        public int MaxLength { get; }
        public bool HasCandidates => forwardBuckets.Length != 0;

        public StringPropertyMatcher(string[] candidates)
        {
            var bucketMap = new Dictionary<int, List<string>>();
            forwardBuckets = BuildBuckets(candidates, bucketMap, ReadFirstCodePoint);
            bucketMap.Clear();
            backwardBuckets = BuildBuckets(candidates, bucketMap, ReadLastCodePoint);
            if (candidates.Length == 0)
            {
                MinLength = 0;
                MaxLength = 0;
                return;
            }

            var minLength = int.MaxValue;
            var maxLength = 0;
            for (var i = 0; i < candidates.Length; i++)
            {
                var length = candidates[i].Length;
                if (length < minLength)
                    minLength = length;
                if (length > maxLength)
                    maxLength = length;
            }

            MinLength = minLength;
            MaxLength = maxLength;
        }

        public bool TryMatchAt(string input, int pos, out int endIndex)
        {
            if (!TryReadCodePointForward(input, pos, out var codePoint) ||
                !TryGetBucketCandidates(forwardBuckets, codePoint, out var candidates))
            {
                endIndex = default;
                return false;
            }

            foreach (var candidate in candidates)
                if (MatchesAt(input, pos, candidate))
                {
                    endIndex = pos + candidate.Length;
                    return true;
                }

            endIndex = default;
            return false;
        }

        public bool TryMatchBackward(string input, int pos, out int startIndex)
        {
            if (!TryReadCodePointBackward(input, pos, out var codePoint) ||
                !TryGetBucketCandidates(backwardBuckets, codePoint, out var candidates))
            {
                startIndex = default;
                return false;
            }

            foreach (var candidate in candidates)
                if (MatchesBackward(input, pos, candidate))
                {
                    startIndex = pos - candidate.Length;
                    return true;
                }

            startIndex = default;
            return false;
        }

        public void AddMatchEndsAt(string input, int pos, ScratchPooledIntSet results)
        {
            if (!TryReadCodePointForward(input, pos, out var codePoint) ||
                !TryGetBucketCandidates(forwardBuckets, codePoint, out var candidates))
                return;

            foreach (var candidate in candidates)
                if (MatchesAt(input, pos, candidate))
                    results.Add(pos + candidate.Length);
        }

        public void AddMatchStartsBackward(string input, int pos, ScratchPooledIntSet results)
        {
            if (!TryReadCodePointBackward(input, pos, out var codePoint) ||
                !TryGetBucketCandidates(backwardBuckets, codePoint, out var candidates))
                return;

            foreach (var candidate in candidates)
                if (MatchesBackward(input, pos, candidate))
                    results.Add(pos - candidate.Length);
        }

        private static bool TryGetBucketCandidates(StringPropertyBucket[] buckets, int codePoint, out string[] candidates)
        {
            var index = FindBucketIndex(buckets, codePoint);
            if (index >= 0)
            {
                candidates = buckets[index].Candidates;
                return true;
            }

            candidates = [];
            return false;
        }

        private static int FindBucketIndex(StringPropertyBucket[] buckets, int codePoint)
        {
            var low = 0;
            var high = buckets.Length - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var bucketCodePoint = buckets[mid].CodePoint;
                if (bucketCodePoint == codePoint)
                    return mid;
                if (bucketCodePoint < codePoint)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return -1;
        }

        private static StringPropertyBucket[] BuildBuckets(string[] candidates, Dictionary<int, List<string>> bucketMap, Func<string, int> selector)
        {
            foreach (var candidate in candidates)
            {
                var key = selector(candidate);
                if (!bucketMap.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    bucketMap[key] = bucket;
                }

                bucket.Add(candidate);
            }

            if (bucketMap.Count == 0)
                return [];

            var bucketArray = new StringPropertyBucket[bucketMap.Count];
            var index = 0;
            foreach (var (key, bucket) in bucketMap)
                bucketArray[index++] = new(key, [.. bucket]);

            Array.Sort(bucketArray, static (left, right) => left.CodePoint.CompareTo(right.CodePoint));
            return bucketArray;
        }
    }

    private readonly record struct StringPropertyBucket(int CodePoint, string[] Candidates);
}
