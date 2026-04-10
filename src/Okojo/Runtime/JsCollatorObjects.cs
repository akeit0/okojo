using System.Globalization;
using System.Text;

namespace Okojo.Runtime;

internal sealed class JsCollatorObject : JsObject
{
    private JsHostFunction? boundCompare;

    internal JsCollatorObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string usage,
        string sensitivity,
        bool ignorePunctuation,
        string collation,
        bool numeric,
        string caseFirst,
        CompareInfo compareInfo,
        CompareOptions compareOptions) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        Usage = usage;
        Sensitivity = sensitivity;
        IgnorePunctuation = ignorePunctuation;
        Collation = collation;
        Numeric = numeric;
        CaseFirst = caseFirst;
        CompareInfo = compareInfo;
        CompareOptions = compareOptions;
    }

    internal string Locale { get; }
    internal string Usage { get; }
    internal string Sensitivity { get; }
    internal bool IgnorePunctuation { get; }
    internal string Collation { get; }
    internal bool Numeric { get; }
    internal string CaseFirst { get; }
    internal CompareInfo CompareInfo { get; }
    internal CompareOptions CompareOptions { get; }

    internal JsHostFunction GetOrCreateBoundCompare(JsRealm realm)
    {
        if (boundCompare is not null)
            return boundCompare;

        boundCompare = new(realm, static (in info) =>
        {
            var collator = (JsCollatorObject)((JsHostFunction)info.Function).UserData!;
            var x = info.Arguments.Length > 0 ? info.Realm.ToJsStringSlowPath(info.Arguments[0]) : "undefined";
            var y = info.Arguments.Length > 1 ? info.Realm.ToJsStringSlowPath(info.Arguments[1]) : "undefined";
            return JsValue.FromInt32(collator.Compare(x, y));
        }, string.Empty, 2)
        {
            UserData = this
        };
        return boundCompare;
    }

    internal int Compare(string x, string y)
    {
        x = x.Normalize(NormalizationForm.FormC);
        y = y.Normalize(NormalizationForm.FormC);
        ApplyLocaleSensitiveTransforms(ref x, ref y);
        string? normalizedSearchX = null;
        string? normalizedSearchY = null;

        if (string.Equals(Usage, "search", StringComparison.Ordinal))
        {
            normalizedSearchX = NormalizeSearchValue(x);
            normalizedSearchY = NormalizeSearchValue(y);
            if (string.Equals(normalizedSearchX, normalizedSearchY, StringComparison.Ordinal))
            {
                if (!string.Equals(CaseFirst, "false", StringComparison.Ordinal) &&
                    string.Equals(x, y, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x, y, StringComparison.Ordinal))
                {
                    var caseFirstResult = CompareCaseFirst(x, y);
                    return caseFirstResult < 0 ? -1 : caseFirstResult > 0 ? 1 : 0;
                }

                return 0;
            }
        }

        var result = Numeric
            ? NaturalStringCompare(x, y)
            : CompareInfo.Compare(x, y, CompareOptions);

        if (string.Equals(Usage, "search", StringComparison.Ordinal) &&
            result == 0 &&
            normalizedSearchX is not null &&
            normalizedSearchY is not null &&
            !string.Equals(normalizedSearchX, normalizedSearchY, StringComparison.Ordinal))
            result = string.CompareOrdinal(x, y);

        if (result == 0 && !IgnorePunctuation && !string.Equals(x, y, StringComparison.Ordinal))
        {
            var xStripped = StripPunctuation(x);
            var yStripped = StripPunctuation(y);
            if (string.Equals(xStripped, yStripped, StringComparison.Ordinal))
                result = string.CompareOrdinal(x, y);
        }

        if (result == 0 &&
            !string.Equals(CaseFirst, "false", StringComparison.Ordinal) &&
            string.Equals(x, y, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(x, y, StringComparison.Ordinal))
            result = CompareCaseFirst(x, y);

        return result < 0 ? -1 : result > 0 ? 1 : 0;
    }

    private void ApplyLocaleSensitiveTransforms(ref string x, ref string y)
    {
        if (!Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(Collation, "phonebk", StringComparison.Ordinal) ||
            string.Equals(Usage, "search", StringComparison.Ordinal))
        {
            x = FoldGermanPhonebook(x);
            y = FoldGermanPhonebook(y);
        }
    }

    private int NaturalStringCompare(string x, string y)
    {
        var xi = 0;
        var yi = 0;
        while (xi < x.Length && yi < y.Length)
        {
            var xIsDigit = char.IsDigit(x[xi]);
            var yIsDigit = char.IsDigit(y[yi]);
            if (xIsDigit && yIsDigit)
            {
                var xStart = xi;
                var yStart = yi;
                while (xi < x.Length && char.IsDigit(x[xi]))
                    xi++;
                while (yi < y.Length && char.IsDigit(y[yi]))
                    yi++;

                var numericCompare = CompareDigitRuns(x.AsSpan(xStart, xi - xStart), y.AsSpan(yStart, yi - yStart));
                if (numericCompare != 0)
                    return numericCompare;
                continue;
            }

            var charCompare = CompareInfo.Compare(x, xi, 1, y, yi, 1, CompareOptions);
            if (charCompare == 0 &&
                !string.Equals(CaseFirst, "false", StringComparison.Ordinal) &&
                char.ToUpperInvariant(x[xi]) == char.ToUpperInvariant(y[yi]) &&
                x[xi] != y[yi])
                charCompare = CompareCaseFirst(x[xi].ToString(), y[yi].ToString());

            if (charCompare != 0)
                return charCompare < 0 ? -1 : 1;

            xi++;
            yi++;
        }

        return (x.Length - xi).CompareTo(y.Length - yi) switch
        {
            < 0 => -1,
            > 0 => 1,
            _ => 0
        };
    }

    private int CompareCaseFirst(string x, string y)
    {
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var xc = x[i];
            var yc = y[i];
            if (char.ToUpperInvariant(xc) != char.ToUpperInvariant(yc) || xc == yc)
                continue;

            var xUpper = char.IsLetter(xc) && char.IsUpper(xc);
            var yUpper = char.IsLetter(yc) && char.IsUpper(yc);
            if (xUpper == yUpper)
                continue;

            if (string.Equals(CaseFirst, "upper", StringComparison.Ordinal))
                return xUpper ? -1 : 1;
            if (string.Equals(CaseFirst, "lower", StringComparison.Ordinal))
                return xUpper ? 1 : -1;
        }

        return string.CompareOrdinal(x, y);
    }

    private static int CompareDigitRuns(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        x = TrimLeadingZeros(x);
        y = TrimLeadingZeros(y);
        if (x.Length != y.Length)
            return x.Length < y.Length ? -1 : 1;

        for (var i = 0; i < x.Length; i++)
        {
            if (x[i] == y[i])
                continue;
            return x[i] < y[i] ? -1 : 1;
        }

        return 0;
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> value)
    {
        var index = 0;
        while (index < value.Length - 1 && value[index] == '0')
            index++;
        return value[index..];
    }

    private static string StripPunctuation(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            if (!char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsWhiteSpace(c))
                builder.Append(c);

        return builder.ToString();
    }

    private static string FoldGermanPhonebook(string value)
    {
        return value
            .Replace("Ä", "Ae", StringComparison.Ordinal)
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("Ö", "Oe", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("Ü", "Ue", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);
    }

    private string NormalizeSearchValue(string value)
    {
        var candidate = value;
        if (string.Equals(Sensitivity, "base", StringComparison.Ordinal) ||
            string.Equals(Sensitivity, "case", StringComparison.Ordinal))
            candidate = RemoveDiacritics(candidate);

        if (string.Equals(Sensitivity, "base", StringComparison.Ordinal) ||
            string.Equals(Sensitivity, "accent", StringComparison.Ordinal))
            candidate = candidate.ToUpperInvariant();

        return candidate;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
