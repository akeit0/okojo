using System.Globalization;

namespace Okojo.Runtime;

internal sealed class JsSegmenterObject : JsObject
{
    internal JsSegmenterObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string granularity,
        CultureInfo cultureInfo) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        Granularity = granularity;
        CultureInfo = cultureInfo;
    }

    internal string Locale { get; }
    internal string Granularity { get; }
    internal CultureInfo CultureInfo { get; }
}

internal sealed class JsSegmentsObject : JsObject
{
    private readonly string input;
    private readonly JsObject iteratorPrototype;
    private readonly JsSegmenterObject segmenter;
    private readonly SegmentData[] segments;

    internal JsSegmentsObject(
        JsRealm realm,
        JsObject prototype,
        JsObject iteratorPrototype,
        JsSegmenterObject segmenter,
        string input) : base(realm)
    {
        Prototype = prototype;
        this.segmenter = segmenter;
        this.input = input;
        this.iteratorPrototype = iteratorPrototype;
        segments = ComputeSegments(input, segmenter.Granularity).ToArray();
    }

    internal int SegmentCount => segments.Length;

    internal JsValue Containing(double indexValue)
    {
        if (double.IsNaN(indexValue) || double.IsInfinity(indexValue))
            return JsValue.Undefined;

        var index = (int)indexValue;
        if (index < 0 || index >= input.Length)
            return JsValue.Undefined;

        foreach (var segment in segments)
            if (index >= segment.Index && index < segment.Index + segment.Segment.Length)
                return JsValue.FromObject(CreateSegmentDataObject(segment));

        return JsValue.Undefined;
    }

    internal JsSegmentIteratorObject CreateIterator()
    {
        return new(Realm, iteratorPrototype, this);
    }

    internal SegmentData GetSegmentAt(int index)
    {
        return segments[index];
    }

    internal JsPlainObject CreateSegmentDataObject(in SegmentData data)
    {
        var obj = new JsPlainObject(Realm)
        {
            Prototype = Realm.ObjectPrototype
        };

        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("segment"), JsValue.FromString(data.Segment),
            JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("index"), JsValue.FromInt32(data.Index),
            JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("input"), JsValue.FromString(input),
            JsShapePropertyFlags.Open);

        if (string.Equals(segmenter.Granularity, "word", StringComparison.Ordinal))
            obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("isWordLike"),
                data.IsWordLike ? JsValue.True : JsValue.False,
                JsShapePropertyFlags.Open);

        return obj;
    }

    private static List<SegmentData> ComputeSegments(string input, string granularity)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        return granularity switch
        {
            "word" => ComputeWordSegments(input),
            "sentence" => ComputeSentenceSegments(input),
            _ => ComputeGraphemeSegments(input)
        };
    }

    private static List<SegmentData> ComputeGraphemeSegments(string input)
    {
        List<SegmentData> result = [];
        var enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext())
            result.Add(new(
                enumerator.GetTextElement(),
                enumerator.ElementIndex,
                false));

        return result;
    }

    private static List<SegmentData> ComputeWordSegments(string input)
    {
        List<SegmentData> result = [];
        List<GraphemeInfo> graphemes = [];
        var enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext()) graphemes.Add(new(enumerator.GetTextElement(), enumerator.ElementIndex));

        var i = 0;
        while (i < graphemes.Count)
        {
            var grapheme = graphemes[i].Text;
            var startIndex = graphemes[i].Index;
            var firstChar = grapheme[0];

            if (char.IsWhiteSpace(firstChar))
            {
                var endIndex = startIndex + grapheme.Length;
                i++;
                while (i < graphemes.Count && graphemes[i].Text.Length > 0 && char.IsWhiteSpace(graphemes[i].Text[0]))
                {
                    endIndex = graphemes[i].Index + graphemes[i].Text.Length;
                    i++;
                }

                result.Add(new(input.Substring(startIndex, endIndex - startIndex), startIndex, false));
                continue;
            }

            if (char.IsLetterOrDigit(firstChar) || char.GetUnicodeCategory(firstChar) == UnicodeCategory.ModifierLetter)
            {
                var endIndex = startIndex + grapheme.Length;
                i++;
                while (i < graphemes.Count)
                {
                    var nextGrapheme = graphemes[i].Text;
                    if (nextGrapheme.Length == 0)
                        break;

                    var nextChar = nextGrapheme[0];
                    if (char.IsLetterOrDigit(nextChar) ||
                        nextChar == '\'' || nextChar == '-' ||
                        char.GetUnicodeCategory(nextChar) == UnicodeCategory.ModifierLetter ||
                        char.GetUnicodeCategory(nextChar) == UnicodeCategory.NonSpacingMark)
                    {
                        endIndex = graphemes[i].Index + nextGrapheme.Length;
                        i++;
                        continue;
                    }

                    if ((nextChar == '.' || nextChar == ',') && i + 1 < graphemes.Count)
                    {
                        var afterNext = graphemes[i + 1].Text;
                        if (afterNext.Length > 0 && char.IsDigit(afterNext[0]))
                        {
                            endIndex = graphemes[i + 1].Index + afterNext.Length;
                            i += 2;
                            continue;
                        }
                    }

                    break;
                }

                result.Add(new(input.Substring(startIndex, endIndex - startIndex), startIndex, true));
                continue;
            }

            result.Add(new(grapheme, startIndex, false));
            i++;
        }

        return result;
    }

    private static List<SegmentData> ComputeSentenceSegments(string input)
    {
        List<SegmentData> result = [];
        var index = 0;
        while (index < input.Length)
        {
            var startIndex = index;
            while (index < input.Length)
            {
                var c = input[index++];
                if (c == '.' || c == '!' || c == '?')
                {
                    while (index < input.Length && char.IsWhiteSpace(input[index]))
                        index++;
                    break;
                }
            }

            result.Add(new(input.Substring(startIndex, index - startIndex), startIndex, false));
        }

        return result;
    }

    private readonly record struct GraphemeInfo(string Text, int Index);

    internal readonly record struct SegmentData(string Segment, int Index, bool IsWordLike);
}

internal sealed class JsSegmentIteratorObject : JsObject
{
    private readonly JsSegmentsObject segments;
    private int index;

    internal JsSegmentIteratorObject(JsRealm realm, JsObject prototype, JsSegmentsObject segments) : base(realm)
    {
        Prototype = prototype;
        this.segments = segments;
    }

    internal JsValue Next()
    {
        if (index >= segments.SegmentCount)
            return JsValue.FromObject(Realm.CreateIteratorResultObject(JsValue.Undefined, true));

        var value = segments.CreateSegmentDataObject(segments.GetSegmentAt(index++));
        return JsValue.FromObject(Realm.CreateIteratorResultObject(JsValue.FromObject(value), false));
    }
}
