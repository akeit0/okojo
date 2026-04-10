using System.Globalization;
using Okojo.Runtime.Intl;

namespace Okojo.Runtime;

internal sealed class JsRelativeTimeFormatObject : JsObject
{
    private static readonly Dictionary<string, string[]> EnglishLongUnits = new(StringComparer.Ordinal)
    {
        ["second"] = ["second", "seconds"],
        ["minute"] = ["minute", "minutes"],
        ["hour"] = ["hour", "hours"],
        ["day"] = ["day", "days"],
        ["week"] = ["week", "weeks"],
        ["month"] = ["month", "months"],
        ["quarter"] = ["quarter", "quarters"],
        ["year"] = ["year", "years"]
    };

    private static readonly Dictionary<string, string[]> EnglishShortUnits = new(StringComparer.Ordinal)
    {
        ["second"] = ["sec."],
        ["minute"] = ["min."],
        ["hour"] = ["hr."],
        ["day"] = ["day", "days"],
        ["week"] = ["wk."],
        ["month"] = ["mo."],
        ["quarter"] = ["qtr.", "qtrs."],
        ["year"] = ["yr."]
    };

    private static readonly Dictionary<string, string[]> EnglishNarrowUnits = new(StringComparer.Ordinal)
    {
        ["second"] = ["sec."],
        ["minute"] = ["min."],
        ["hour"] = ["hr."],
        ["day"] = ["day", "days"],
        ["week"] = ["wk."],
        ["month"] = ["mo."],
        ["quarter"] = ["qtr.", "qtrs."],
        ["year"] = ["yr."]
    };

    private static readonly Dictionary<string, Dictionary<string, string>> PolishLongUnits = new(StringComparer.Ordinal)
    {
        ["second"] = new(StringComparer.Ordinal)
            { ["many"] = "sekund", ["few"] = "sekundy", ["one"] = "sekundę", ["other"] = "sekundy" },
        ["minute"] = new(StringComparer.Ordinal)
            { ["many"] = "minut", ["few"] = "minuty", ["one"] = "minutę", ["other"] = "minuty" },
        ["hour"] = new(StringComparer.Ordinal)
            { ["many"] = "godzin", ["few"] = "godziny", ["one"] = "godzinę", ["other"] = "godziny" },
        ["day"] = new(StringComparer.Ordinal)
            { ["many"] = "dni", ["few"] = "dni", ["one"] = "dzień", ["other"] = "dnia" },
        ["week"] = new(StringComparer.Ordinal)
            { ["many"] = "tygodni", ["few"] = "tygodnie", ["one"] = "tydzień", ["other"] = "tygodnia" },
        ["month"] = new(StringComparer.Ordinal)
            { ["many"] = "miesięcy", ["few"] = "miesiące", ["one"] = "miesiąc", ["other"] = "miesiąca" },
        ["quarter"] = new(StringComparer.Ordinal)
            { ["many"] = "kwartałów", ["few"] = "kwartały", ["one"] = "kwartał", ["other"] = "kwartału" },
        ["year"] = new(StringComparer.Ordinal)
            { ["many"] = "lat", ["few"] = "lata", ["one"] = "rok", ["other"] = "roku" }
    };

    private static readonly Dictionary<string, Dictionary<string, string>> PolishShortUnits =
        new(StringComparer.Ordinal)
        {
            ["second"] = new(StringComparer.Ordinal)
                { ["many"] = "sek.", ["few"] = "sek.", ["one"] = "sek.", ["other"] = "sek." },
            ["minute"] = new(StringComparer.Ordinal)
                { ["many"] = "min", ["few"] = "min", ["one"] = "min", ["other"] = "min" },
            ["hour"] = new(StringComparer.Ordinal)
                { ["many"] = "godz.", ["few"] = "godz.", ["one"] = "godz.", ["other"] = "godz." },
            ["day"] = new(StringComparer.Ordinal)
                { ["many"] = "dni", ["few"] = "dni", ["one"] = "dzień", ["other"] = "dnia" },
            ["week"] = new(StringComparer.Ordinal)
                { ["many"] = "tyg.", ["few"] = "tyg.", ["one"] = "tydz.", ["other"] = "tyg." },
            ["month"] = new(StringComparer.Ordinal)
                { ["many"] = "mies.", ["few"] = "mies.", ["one"] = "mies.", ["other"] = "mies." },
            ["quarter"] = new(StringComparer.Ordinal)
                { ["many"] = "kw.", ["few"] = "kw.", ["one"] = "kw.", ["other"] = "kw." },
            ["year"] = new(StringComparer.Ordinal)
                { ["many"] = "lat", ["few"] = "lata", ["one"] = "rok", ["other"] = "roku" }
        };

    private static readonly Dictionary<string, Dictionary<string, string>> PolishNarrowUnits =
        new(StringComparer.Ordinal)
        {
            ["second"] = new(StringComparer.Ordinal) { ["many"] = "s", ["few"] = "s", ["one"] = "s", ["other"] = "s" },
            ["minute"] = new(StringComparer.Ordinal)
                { ["many"] = "min", ["few"] = "min", ["one"] = "min", ["other"] = "min" },
            ["hour"] =
                new(StringComparer.Ordinal) { ["many"] = "g.", ["few"] = "g.", ["one"] = "g.", ["other"] = "g." },
            ["day"] = new(StringComparer.Ordinal)
                { ["many"] = "dni", ["few"] = "dni", ["one"] = "dzień", ["other"] = "dnia" },
            ["week"] = new(StringComparer.Ordinal)
                { ["many"] = "tyg.", ["few"] = "tyg.", ["one"] = "tydz.", ["other"] = "tyg." },
            ["month"] = new(StringComparer.Ordinal)
                { ["many"] = "mies.", ["few"] = "mies.", ["one"] = "mies.", ["other"] = "mies." },
            ["quarter"] = new(StringComparer.Ordinal)
                { ["many"] = "kw.", ["few"] = "kw.", ["one"] = "kw.", ["other"] = "kw." },
            ["year"] = new(StringComparer.Ordinal)
                { ["many"] = "lat", ["few"] = "lata", ["one"] = "rok", ["other"] = "roku" }
        };

    internal JsRelativeTimeFormatObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string numberingSystem,
        string style,
        string numeric,
        CultureInfo cultureInfo) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        NumberingSystem = numberingSystem;
        Style = style;
        Numeric = numeric;
        CultureInfo = cultureInfo;
    }

    internal string Locale { get; }
    internal string NumberingSystem { get; }
    internal string Style { get; }
    internal string Numeric { get; }
    internal CultureInfo CultureInfo { get; }

    internal string Format(double value, string unit)
    {
        if (string.Equals(Numeric, "auto", StringComparison.Ordinal))
        {
            var phrase = GetSpecialPhrase(value, unit);
            if (phrase is not null)
                return phrase;
        }

        var isPast = value < 0 || double.IsNegativeInfinity(1.0 / value);
        var absValue = Math.Abs(value);
        var pluralForm = GetPluralForm(absValue);
        var unitName = GetUnitName(unit, pluralForm);
        var formattedNumber = FormatNumber(absValue);

        if (IsPolishLocale())
            return isPast ? $"{formattedNumber} {unitName} temu" : $"za {formattedNumber} {unitName}";

        return isPast ? $"{formattedNumber} {unitName} ago" : $"in {formattedNumber} {unitName}";
    }

    internal JsArray FormatToParts(double value, string unit)
    {
        var result = Realm.CreateArrayObject();
        uint index = 0;

        if (string.Equals(Numeric, "auto", StringComparison.Ordinal))
        {
            var phrase = GetSpecialPhrase(value, unit);
            if (phrase is not null)
            {
                result.SetElement(index++, JsValue.FromObject(CreateLiteralPart(phrase)));
                return result;
            }
        }

        var isPast = value < 0 || double.IsNegativeInfinity(1.0 / value);
        var absValue = Math.Abs(value);
        var pluralForm = GetPluralForm(absValue);
        var unitName = GetUnitName(unit, pluralForm);

        if (IsPolishLocale())
        {
            if (!isPast)
                result.SetElement(index++, JsValue.FromObject(CreateLiteralPart("za ")));
            AppendNumberParts(result, ref index, absValue, unit);
            result.SetElement(index++,
                JsValue.FromObject(CreateLiteralPart(isPast ? $" {unitName} temu" : $" {unitName}")));
            return result;
        }

        if (!isPast)
            result.SetElement(index++, JsValue.FromObject(CreateLiteralPart("in ")));
        AppendNumberParts(result, ref index, absValue, unit);
        result.SetElement(index++, JsValue.FromObject(CreateLiteralPart(isPast ? $" {unitName} ago" : $" {unitName}")));
        return result;
    }

    private bool IsPolishLocale()
    {
        return Locale.StartsWith("pl", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetSpecialPhrase(double value, string unit)
    {
        if (!Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return null;

        var isPast = value < 0 || double.IsNegativeInfinity(1.0 / value);
        var absoluteRounded = (long)Math.Round(Math.Abs(value));
        if (absoluteRounded == 0)
            return unit switch
            {
                "second" => "now",
                "minute" => "this minute",
                "hour" => "this hour",
                "day" => "today",
                "week" => "this week",
                "month" => "this month",
                "quarter" => "this quarter",
                "year" => "this year",
                _ => null
            };

        if (absoluteRounded != 1)
            return null;

        return isPast
            ? unit switch
            {
                "day" => "yesterday",
                "week" => "last week",
                "month" => "last month",
                "quarter" => "last quarter",
                "year" => "last year",
                _ => null
            }
            : unit switch
            {
                "day" => "tomorrow",
                "week" => "next week",
                "month" => "next month",
                "quarter" => "next quarter",
                "year" => "next year",
                _ => null
            };
    }

    private string GetPluralForm(double value)
    {
        var i = (long)Math.Abs(Math.Truncate(value));
        var v = GetFractionDigitCount(value);
        if (IsPolishLocale())
        {
            if (v != 0)
                return "other";
            if (i == 1)
                return "one";
            var mod10 = i % 10;
            var mod100 = i % 100;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
                return "few";
            if (mod10 == 0 || mod10 == 1 || (mod10 >= 5 && mod10 <= 9) || (mod100 >= 12 && mod100 <= 14))
                return "many";
            return "other";
        }

        return i == 1 && v == 0 ? "one" : "other";
    }

    private static int GetFractionDigitCount(double value)
    {
        var absValue = Math.Abs(value);
        var intPart = Math.Truncate(absValue);
        var fracPart = absValue - intPart;
        if (fracPart == 0)
            return 0;
        var fraction = fracPart.ToString("0.###############", CultureInfo.InvariantCulture);
        return fraction.StartsWith("0.", StringComparison.Ordinal) ? fraction.Length - 2 : 0;
    }

    private string GetUnitName(string unit, string pluralForm)
    {
        if (IsPolishLocale())
        {
            var unitSource = Style switch
            {
                "short" => PolishShortUnits,
                "narrow" => PolishNarrowUnits,
                _ => PolishLongUnits
            };
            var unitMap = unitSource[unit];
            return unitMap.TryGetValue(pluralForm, out var value) ? value : unitMap["many"];
        }

        var unitSourceEnglish = Style switch
        {
            "short" => EnglishShortUnits,
            "narrow" => EnglishNarrowUnits,
            _ => EnglishLongUnits
        };
        var forms = unitSourceEnglish[unit];
        if (forms.Length == 1)
            return forms[0];
        return string.Equals(pluralForm, "one", StringComparison.Ordinal) ? forms[0] : forms[1];
    }

    private string FormatNumber(double value)
    {
        var raw = JsNumberFormatting.ToJsString(value);
        var integerPart = raw;
        string? fractionPart = null;
        var dotIndex = raw.IndexOf('.');
        if (dotIndex >= 0)
        {
            integerPart = raw[..dotIndex];
            fractionPart = raw[(dotIndex + 1)..];
        }

        var groupSeparator = CultureInfo.NumberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator))
            groupSeparator = IsPolishLocale() ? "\u00A0" : ",";
        groupSeparator = OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem, groupSeparator);
        var decimalSeparator = OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
            CultureInfo.NumberFormat.NumberDecimalSeparator);
        var useGrouping = ShouldGroupDigits(integerPart.Length);
        var groups = useGrouping ? SplitIntegerGroups(integerPart) : [integerPart];
        for (var i = 0; i < groups.Count; i++)
            groups[i] = OkojoIntlNumberingSystemData.TransliterateDigits(groups[i], NumberingSystem);

        var result = string.Join(groupSeparator, groups);
        if (!string.IsNullOrEmpty(fractionPart))
            result += decimalSeparator +
                      OkojoIntlNumberingSystemData.TransliterateDigits(fractionPart, NumberingSystem);

        return result;
    }

    private void AppendNumberParts(JsArray result, ref uint index, double value, string unit)
    {
        foreach (var part in FormatNumberParts(value, unit))
            result.SetElement(index++, JsValue.FromObject(part));
    }

    private JsPlainObject CreateLiteralPart(string value)
    {
        var obj = new JsPlainObject(Realm)
        {
            Prototype = Realm.ObjectPrototype
        };
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("type"), JsValue.FromString("literal"),
            JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("value"), JsValue.FromString(value),
            JsShapePropertyFlags.Open);
        return obj;
    }

    private IEnumerable<JsPlainObject> FormatNumberParts(double value, string unit)
    {
        var raw = JsNumberFormatting.ToJsString(value);
        var integerPart = raw;
        string? fractionPart = null;
        var dotIndex = raw.IndexOf('.');
        if (dotIndex >= 0)
        {
            integerPart = raw[..dotIndex];
            fractionPart = raw[(dotIndex + 1)..];
        }

        var groupSeparator = CultureInfo.NumberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator))
            groupSeparator = IsPolishLocale() ? "\u00A0" : ",";
        groupSeparator = OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem, groupSeparator);
        var decimalSeparator = OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
            CultureInfo.NumberFormat.NumberDecimalSeparator);
        var useGrouping = ShouldGroupDigits(integerPart.Length);
        var groups = useGrouping ? SplitIntegerGroups(integerPart) : [integerPart];

        for (var i = 0; i < groups.Count; i++)
        {
            yield return CreateNumberPart("integer",
                OkojoIntlNumberingSystemData.TransliterateDigits(groups[i], NumberingSystem), unit);
            if (i + 1 < groups.Count)
                yield return CreateNumberPart("group", groupSeparator, unit);
        }

        if (!string.IsNullOrEmpty(fractionPart))
        {
            yield return CreateNumberPart("decimal", decimalSeparator, unit);
            yield return CreateNumberPart("fraction",
                OkojoIntlNumberingSystemData.TransliterateDigits(fractionPart, NumberingSystem), unit);
        }
    }

    private bool ShouldGroupDigits(int integerDigits)
    {
        if (IsPolishLocale())
            return integerDigits >= 5;
        return integerDigits >= 4;
    }

    private static List<string> SplitIntegerGroups(string digits)
    {
        List<string> result = [];
        var firstGroupLength = digits.Length % 3;
        if (firstGroupLength == 0)
            firstGroupLength = 3;
        result.Add(digits[..firstGroupLength]);
        for (var i = firstGroupLength; i < digits.Length; i += 3)
            result.Add(digits.Substring(i, 3));
        return result;
    }

    private JsPlainObject CreateNumberPart(string type, string value, string unit)
    {
        var obj = new JsPlainObject(Realm)
        {
            Prototype = Realm.ObjectPrototype
        };
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("type"), JsValue.FromString(type),
            JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("value"), JsValue.FromString(value),
            JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("unit"), JsValue.FromString(unit),
            JsShapePropertyFlags.Open);
        return obj;
    }
}
