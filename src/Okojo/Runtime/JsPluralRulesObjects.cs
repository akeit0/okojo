using System.Globalization;

namespace Okojo.Runtime;

internal sealed class JsPluralRulesObject : JsObject
{
    internal JsPluralRulesObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string pluralRuleType,
        string notation,
        int minimumIntegerDigits,
        int? minimumFractionDigits,
        int? maximumFractionDigits,
        int? minimumSignificantDigits,
        int? maximumSignificantDigits,
        string roundingMode,
        string roundingPriority,
        int roundingIncrement,
        string trailingZeroDisplay,
        CultureInfo cultureInfo) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        PluralRuleType = pluralRuleType;
        Notation = notation;
        MinimumIntegerDigits = minimumIntegerDigits;
        MinimumFractionDigits = minimumFractionDigits;
        MaximumFractionDigits = maximumFractionDigits;
        MinimumSignificantDigits = minimumSignificantDigits;
        MaximumSignificantDigits = maximumSignificantDigits;
        RoundingMode = roundingMode;
        RoundingPriority = roundingPriority;
        RoundingIncrement = roundingIncrement;
        TrailingZeroDisplay = trailingZeroDisplay;
        CultureInfo = cultureInfo;
    }

    internal string Locale { get; }
    internal string PluralRuleType { get; }
    internal string Notation { get; }
    internal int MinimumIntegerDigits { get; }
    internal int? MinimumFractionDigits { get; }
    internal int? MaximumFractionDigits { get; }
    internal int? MinimumSignificantDigits { get; }
    internal int? MaximumSignificantDigits { get; }
    internal string RoundingMode { get; }
    internal string RoundingPriority { get; }
    internal int RoundingIncrement { get; }
    internal string TrailingZeroDisplay { get; }
    internal CultureInfo CultureInfo { get; }

    internal string Select(double n)
    {
        return string.Equals(PluralRuleType, "ordinal", StringComparison.Ordinal)
            ? SelectOrdinal(n)
            : SelectCardinal(n);
    }

    internal string[] GetPluralCategories()
    {
        var lang = GetLanguageCode();
        if (string.Equals(PluralRuleType, "ordinal", StringComparison.Ordinal))
            return lang switch
            {
                "en" => ["one", "two", "few", "other"],
                _ => ["other"]
            };

        return lang switch
        {
            "ar" => ["zero", "one", "two", "few", "many", "other"],
            "gv" => ["one", "two", "few", "many", "other"],
            "ru" or "uk" or "pl" => ["one", "few", "many", "other"],
            "sl" => ["one", "two", "few", "other"],
            "fr" or "pt" => ["one", "many", "other"],
            "zh" or "ja" or "ko" or "vi" => ["other"],
            _ => ["one", "other"]
        };
    }

    private string SelectCardinal(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
            return "other";

        var absN = Math.Abs(n);
        var i = (long)Math.Floor(absN);
        var v = GetVisibleFractionDigitCount(n);

        var lang = GetLanguageCode();
        return lang switch
        {
            "en" or "de" or "nl" or "sv" or "da" or "no" or "nb" or "nn" =>
                i == 1 && v == 0 ? "one" : "other",
            "fr" => SelectFrenchCardinal(absN, i, v),
            "pt" or "fa" => SelectPortugueseOrPersianCardinal(absN, i, v),
            "es" or "it" => i == 1 && v == 0 ? "one" : "other",
            "gv" => SelectManxCardinal(i, v),
            "sl" => SelectSlovenianCardinal(i, v),
            "ru" or "uk" => SelectSlavicCardinal(i, v),
            "pl" => SelectPolishCardinal(i, v),
            "ar" => SelectArabicCardinal(i),
            "zh" or "ja" or "ko" or "vi" => "other",
            _ => i == 1 && v == 0 ? "one" : "other"
        };
    }

    private string SelectOrdinal(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
            return "other";

        var i = (long)Math.Floor(Math.Abs(n));
        var lang = GetLanguageCode();
        return lang switch
        {
            "en" => SelectEnglishOrdinal(i),
            _ => "other"
        };
    }

    private string GetLanguageCode()
    {
        var dashIndex = Locale.IndexOf('-');
        return dashIndex > 0 ? Locale[..dashIndex].ToLowerInvariant() : Locale.ToLowerInvariant();
    }

    private static string SelectEnglishOrdinal(long n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod100 >= 11 && mod100 <= 13)
            return "other";

        return mod10 switch
        {
            1 => "one",
            2 => "two",
            3 => "few",
            _ => "other"
        };
    }

    private string SelectFrenchCardinal(double absN, long i, int v)
    {
        if (string.Equals(Notation, "compact", StringComparison.Ordinal) && absN >= 1_000_000d)
            return "many";
        if (i == 0 || i == 1)
            return "one";
        if (v == 0 && i != 0 && i % 1_000_000 == 0)
            return "many";
        return "other";
    }

    private string SelectPortugueseOrPersianCardinal(double absN, long i, int v)
    {
        if (string.Equals(Notation, "compact", StringComparison.Ordinal) && absN >= 1_000_000d)
            return "many";
        return i == 0 || i == 1 ? "one" : "other";
    }

    private static string SelectSlavicCardinal(long i, int v)
    {
        if (v != 0)
            return "other";

        var mod10 = i % 10;
        var mod100 = i % 100;
        if (mod10 == 1 && mod100 != 11)
            return "one";
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
            return "few";
        return "other";
    }

    private static string SelectPolishCardinal(long i, int v)
    {
        if (v != 0)
            return "other";
        if (i == 1)
            return "one";

        var mod10 = i % 10;
        var mod100 = i % 100;
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
            return "few";
        return "other";
    }

    private static string SelectArabicCardinal(long i)
    {
        if (i == 0)
            return "zero";
        if (i == 1)
            return "one";
        if (i == 2)
            return "two";

        var mod100 = i % 100;
        if (mod100 >= 3 && mod100 <= 10)
            return "few";
        if (mod100 >= 11 && mod100 <= 99)
            return "many";
        return "other";
    }

    private static string SelectManxCardinal(long i, int v)
    {
        if (v != 0)
            return "many";

        var mod10 = i % 10;
        var mod20 = i % 20;
        if (mod10 == 1)
            return "one";
        if (mod10 == 2)
            return "two";
        if (mod20 == 0)
            return "few";
        return "other";
    }

    private static string SelectSlovenianCardinal(long i, int v)
    {
        var mod100 = i % 100;
        if (v == 0 && mod100 == 1)
            return "one";
        if (v == 0 && mod100 == 2)
            return "two";
        if ((v == 0 && mod100 >= 3 && mod100 <= 4) || v != 0)
            return "few";
        return "other";
    }

    private static int GetVisibleFractionDigitCount(double n)
    {
        var str = n.ToString(CultureInfo.InvariantCulture);
        var dotIndex = str.IndexOf('.');
        return dotIndex < 0 ? 0 : str.Length - dotIndex - 1;
    }
}
