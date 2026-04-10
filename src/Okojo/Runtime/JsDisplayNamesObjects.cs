using System.Globalization;

namespace Okojo.Runtime;

internal sealed class JsDisplayNamesObject : JsObject
{
    private static readonly HashSet<string> SupportedCurrencyCodes = BuildSupportedCurrencyCodes();

    private static readonly string[] SupportedCurrencyCodesOrdered =
        [.. SupportedCurrencyCodes.OrderBy(static value => value, StringComparer.Ordinal)];

    private static readonly Dictionary<string, string> ScriptNames = new(StringComparer.Ordinal)
    {
        ["Arab"] = "Arabic",
        ["Cyrl"] = "Cyrillic",
        ["Deva"] = "Devanagari",
        ["Grek"] = "Greek",
        ["Hans"] = "Simplified Han",
        ["Hant"] = "Traditional Han",
        ["Hebr"] = "Hebrew",
        ["Jpan"] = "Japanese",
        ["Kore"] = "Korean",
        ["Latn"] = "Latin"
    };

    private static readonly Dictionary<string, string> CalendarNames = new(StringComparer.Ordinal)
    {
        ["buddhist"] = "Buddhist Calendar",
        ["chinese"] = "Chinese Calendar",
        ["coptic"] = "Coptic Calendar",
        ["dangi"] = "Dangi Calendar",
        ["ethioaa"] = "Ethiopic Amete Alem Calendar",
        ["ethiopic"] = "Ethiopic Calendar",
        ["gregory"] = "Gregorian Calendar",
        ["gregorian"] = "Gregorian Calendar",
        ["hebrew"] = "Hebrew Calendar",
        ["indian"] = "Indian National Calendar",
        ["islamic"] = "Islamic Calendar",
        ["islamic-civil"] = "Islamic (civil) Calendar",
        ["islamic-rgsa"] = "Islamic (Saudi Arabia, sighting) Calendar",
        ["islamic-tbla"] = "Islamic (tabular, Thursday epoch) Calendar",
        ["islamic-umalqura"] = "Islamic (Umm al-Qura) Calendar",
        ["iso8601"] = "ISO-8601 Calendar",
        ["japanese"] = "Japanese Calendar",
        ["persian"] = "Persian Calendar",
        ["roc"] = "Minguo Calendar"
    };

    private static readonly Dictionary<string, string> DateTimeFieldNames = new(StringComparer.Ordinal)
    {
        ["era"] = "era",
        ["year"] = "year",
        ["quarter"] = "quarter",
        ["month"] = "month",
        ["weekOfYear"] = "week",
        ["weekday"] = "day of the week",
        ["day"] = "day",
        ["dayPeriod"] = "AM/PM",
        ["hour"] = "hour",
        ["minute"] = "minute",
        ["second"] = "second",
        ["timeZoneName"] = "time zone"
    };

    internal JsDisplayNamesObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string displayType,
        string style,
        string fallback,
        string? languageDisplay,
        CultureInfo cultureInfo) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        DisplayType = displayType;
        Style = style;
        Fallback = fallback;
        LanguageDisplay = languageDisplay;
        CultureInfo = cultureInfo;
    }

    internal string Locale { get; }
    internal string DisplayType { get; }
    internal string Style { get; }
    internal string Fallback { get; }
    internal string? LanguageDisplay { get; }
    internal CultureInfo CultureInfo { get; }

    internal string? Of(string code)
    {
        return DisplayType switch
        {
            "language" => GetLanguageDisplayName(code),
            "region" => GetRegionDisplayName(code),
            "script" => ScriptNames.TryGetValue(code, out var scriptName) ? scriptName : GetFallbackValue(code),
            "currency" => SupportedCurrencyCodes.Contains(code) ? code : GetFallbackValue(code),
            "calendar" => CalendarNames.TryGetValue(code, out var calendarName) ? calendarName : GetFallbackValue(code),
            "dateTimeField" => DateTimeFieldNames.TryGetValue(code, out var fieldName)
                ? fieldName
                : GetFallbackValue(code),
            _ => GetFallbackValue(code)
        };
    }

    internal static string[] GetSupportedCurrencyCodes()
    {
        return SupportedCurrencyCodesOrdered;
    }

    private static HashSet<string> BuildSupportedCurrencyCodes()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            if (string.IsNullOrEmpty(culture.Name))
                continue;

            try
            {
                var region = new RegionInfo(culture.Name);
                if (region.ISOCurrencySymbol.Length == 3)
                    result.Add(region.ISOCurrencySymbol.ToUpperInvariant());
            }
            catch (ArgumentException)
            {
            }
        }

        result.Add("EUR");
        result.Add("USD");
        result.Add("JPY");
        return result;
    }

    private string? GetLanguageDisplayName(string code)
    {
        try
        {
            var culture = new CultureInfo(code, false);
            if (Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return culture.EnglishName;
            return culture.DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return GetFallbackValue(code);
        }
    }

    private string? GetRegionDisplayName(string code)
    {
        try
        {
            var region = new RegionInfo(code);
            if (Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return region.EnglishName;
            return region.DisplayName;
        }
        catch (ArgumentException)
        {
            return GetFallbackValue(code);
        }
    }

    private string? GetFallbackValue(string code)
    {
        return string.Equals(Fallback, "code", StringComparison.Ordinal) ? code : null;
    }
}
