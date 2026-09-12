using System.Globalization;

namespace Okojo.Globalization;

/// <summary>
///     Portable ECMA-402 plural-rules selector.
/// </summary>
public sealed class PluralRules
{
    private readonly string languageCode;

    /// <summary>Creates a plural-rules selector for a locale.</summary>
    public PluralRules(string locale, PluralRulesOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(locale);
        options ??= new();
        Locale = locale;
        PluralRuleType = options.PluralRuleType;
        Notation = options.Notation;
        languageCode = GetLanguageCodeCore(locale);
    }

    /// <summary>Creates a plural-rules selector from explicit option strings.</summary>
    public PluralRules(string locale, string pluralRuleType, string notation)
        : this(
            locale,
            new PluralRulesOptions { PluralRuleType = pluralRuleType, Notation = notation }
        ) { }

    /// <summary>The locale tag.</summary>
    public string Locale { get; }

    /// <summary><c>"cardinal"</c> or <c>"ordinal"</c>.</summary>
    public string PluralRuleType { get; }

    /// <summary>The number notation (<c>"standard"</c> etc.).</summary>
    public string Notation { get; }

    /// <summary>Returns the plural category for a numeric value.</summary>
    public string Select(double n)
    {
        return string.Equals(PluralRuleType, "ordinal", StringComparison.Ordinal)
            ? SelectOrdinal(n)
            : SelectCardinal(n);
    }

    /// <summary>Returns the plural categories the locale/type can produce.</summary>
    public string[] GetPluralCategories()
    {
        var lang = languageCode;
        if (string.Equals(PluralRuleType, "ordinal", StringComparison.Ordinal))
            return lang switch
            {
                "en" => ["one", "two", "few", "other"],
                _ => ["other"],
            };

        return lang switch
        {
            "ar" => ["zero", "one", "two", "few", "many", "other"],
            "gv" => ["one", "two", "few", "many", "other"],
            "ru" or "uk" or "pl" => ["one", "few", "many", "other"],
            "sl" => ["one", "two", "few", "other"],
            "fr" or "pt" => ["one", "many", "other"],
            "zh" or "ja" or "ko" or "vi" => ["other"],
            _ => ["one", "other"],
        };
    }

    private string SelectCardinal(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
            return "other";

        var absN = Math.Abs(n);
        var i = (long)Math.Floor(absN);
        var v = GetVisibleFractionDigitCount(n);

        var lang = languageCode;
        return lang switch
        {
            "en" or "de" or "nl" or "sv" or "da" or "no" or "nb" or "nn" => i == 1 && v == 0
                ? "one"
                : "other",
            "fr" => SelectFrenchCardinal(absN, i, v),
            "pt" or "fa" => SelectPortugueseOrPersianCardinal(absN, i, v),
            "es" or "it" => i == 1 && v == 0 ? "one" : "other",
            "gv" => SelectManxCardinal(i, v),
            "sl" => SelectSlovenianCardinal(i, v),
            "ru" or "uk" => SelectSlavicCardinal(i, v),
            "pl" => SelectPolishCardinal(i, v),
            "ar" => SelectArabicCardinal(i),
            "zh" or "ja" or "ko" or "vi" => "other",
            _ => i == 1 && v == 0 ? "one" : "other",
        };
    }

    private string SelectOrdinal(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
            return "other";

        var i = (long)Math.Floor(Math.Abs(n));
        var lang = languageCode;
        return lang switch
        {
            "en" => SelectEnglishOrdinal(i),
            _ => "other",
        };
    }

    private static string GetLanguageCodeCore(string locale)
    {
        var dashIndex = locale.IndexOf('-');
        return dashIndex > 0 ? locale[..dashIndex].ToLowerInvariant() : locale.ToLowerInvariant();
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
            _ => "other",
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
        Span<char> buffer = stackalloc char[32];
        if (!n.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            return 0;
        var span = buffer[..written];
        var dotIndex = span.IndexOf('.');
        return dotIndex < 0 ? 0 : written - dotIndex - 1;
    }
}
