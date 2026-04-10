using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Okojo.Runtime.Intl;

namespace Okojo.Runtime;

internal sealed partial class JsDurationFormatObject : JsObject
{
    private static readonly string[] DurationUnits =
    [
        "years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds", "microseconds",
        "nanoseconds"
    ];

    internal JsDurationFormatObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string style,
        string numberingSystem,
        CultureInfo cultureInfo,
        string yearsStyle,
        string monthsStyle,
        string weeksStyle,
        string daysStyle,
        string hoursStyle,
        string minutesStyle,
        string secondsStyle,
        string millisecondsStyle,
        string microsecondsStyle,
        string nanosecondsStyle,
        string yearsDisplay,
        string monthsDisplay,
        string weeksDisplay,
        string daysDisplay,
        string hoursDisplay,
        string minutesDisplay,
        string secondsDisplay,
        string millisecondsDisplay,
        string microsecondsDisplay,
        string nanosecondsDisplay,
        int? fractionalDigits) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        Style = style;
        NumberingSystem = numberingSystem;
        CultureInfo = cultureInfo;
        YearsStyle = yearsStyle;
        MonthsStyle = monthsStyle;
        WeeksStyle = weeksStyle;
        DaysStyle = daysStyle;
        HoursStyle = hoursStyle;
        MinutesStyle = minutesStyle;
        SecondsStyle = secondsStyle;
        MillisecondsStyle = millisecondsStyle;
        MicrosecondsStyle = microsecondsStyle;
        NanosecondsStyle = nanosecondsStyle;
        YearsDisplay = yearsDisplay;
        MonthsDisplay = monthsDisplay;
        WeeksDisplay = weeksDisplay;
        DaysDisplay = daysDisplay;
        HoursDisplay = hoursDisplay;
        MinutesDisplay = minutesDisplay;
        SecondsDisplay = secondsDisplay;
        MillisecondsDisplay = millisecondsDisplay;
        MicrosecondsDisplay = microsecondsDisplay;
        NanosecondsDisplay = nanosecondsDisplay;
        FractionalDigits = fractionalDigits;
    }

    internal string Locale { get; }
    internal string Style { get; }
    internal string NumberingSystem { get; }
    internal CultureInfo CultureInfo { get; }
    internal string YearsStyle { get; }
    internal string MonthsStyle { get; }
    internal string WeeksStyle { get; }
    internal string DaysStyle { get; }
    internal string HoursStyle { get; }
    internal string MinutesStyle { get; }
    internal string SecondsStyle { get; }
    internal string MillisecondsStyle { get; }
    internal string MicrosecondsStyle { get; }
    internal string NanosecondsStyle { get; }
    internal string YearsDisplay { get; }
    internal string MonthsDisplay { get; }
    internal string WeeksDisplay { get; }
    internal string DaysDisplay { get; }
    internal string HoursDisplay { get; }
    internal string MinutesDisplay { get; }
    internal string SecondsDisplay { get; }
    internal string MillisecondsDisplay { get; }
    internal string MicrosecondsDisplay { get; }
    internal string NanosecondsDisplay { get; }
    internal int? FractionalDigits { get; }

    [GeneratedRegex(
        @"^([+-])?P(?:(\d+)Y)?(?:(\d+)M)?(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)(?:[.,](\d{1,9}))?H)?(?:(\d+)(?:[.,](\d{1,9}))?M)?(?:(\d+)(?:[.,](\d{1,9}))?S)?)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 100)]
    private static partial Regex DurationPatternRegex();

    internal string Format(DurationRecord duration)
    {
        var parts = Partition(duration);
        return string.Concat(parts.Select(static part => part.Value));
    }

    internal JsArray FormatToParts(DurationRecord duration)
    {
        var result = Realm.CreateArrayObject();
        uint index = 0;
        foreach (var part in Partition(duration))
            result.SetElement(index++, JsValue.FromObject(CreatePartObject(part.Type, part.Value, part.Unit)));
        return result;
    }

    internal static bool TryParseDurationString(string input, out DurationRecord record)
    {
        record = default;
        var match = DurationPatternRegex().Match(input);
        if (!match.Success)
            return false;

        var hasAnyComponent = false;
        for (var i = 2; i <= 11; i++)
            if (match.Groups[i].Success)
            {
                hasAnyComponent = true;
                break;
            }

        if (!hasAnyComponent)
            return false;

        if (match.Groups[7].Success && (match.Groups[8].Success || match.Groups[10].Success))
            return false;
        if (match.Groups[9].Success && match.Groups[10].Success)
            return false;

        var sign = string.Equals(match.Groups[1].Value, "-", StringComparison.Ordinal) ? -1d : 1d;
        var years = ParseDurationComponent(match.Groups[2].Value);
        var months = ParseDurationComponent(match.Groups[3].Value);
        var weeks = ParseDurationComponent(match.Groups[4].Value);
        var days = ParseDurationComponent(match.Groups[5].Value);
        var hours = ParseDurationComponent(match.Groups[6].Value);

        double minutes = 0;
        double seconds = 0;
        double milliseconds = 0;
        double microseconds = 0;
        double nanoseconds = 0;

        if (match.Groups[7].Success)
        {
            var frac = ParseFraction(match.Groups[7].Value);
            var totalMinutes = frac * 60d;
            minutes = Math.Truncate(totalMinutes);
            var remainingSeconds = (totalMinutes - minutes) * 60d;
            seconds = Math.Truncate(remainingSeconds);
            var remainingMs = (remainingSeconds - seconds) * 1000d;
            milliseconds = Math.Truncate(remainingMs);
            var remainingUs = (remainingMs - milliseconds) * 1000d;
            microseconds = Math.Truncate(remainingUs);
            var remainingNs = (remainingUs - microseconds) * 1000d;
            nanoseconds = Math.Round(remainingNs);
        }
        else
        {
            minutes = ParseDurationComponent(match.Groups[8].Value);
            if (match.Groups[9].Success)
            {
                var frac = ParseFraction(match.Groups[9].Value);
                var totalSeconds = frac * 60d;
                seconds = Math.Truncate(totalSeconds);
                var remainingMs = (totalSeconds - seconds) * 1000d;
                milliseconds = Math.Truncate(remainingMs);
                var remainingUs = (remainingMs - milliseconds) * 1000d;
                microseconds = Math.Truncate(remainingUs);
                var remainingNs = (remainingUs - microseconds) * 1000d;
                nanoseconds = Math.Round(remainingNs);
            }
            else if (match.Groups[10].Success)
            {
                seconds = double.Parse(match.Groups[10].Value, CultureInfo.InvariantCulture);
                if (match.Groups[11].Success)
                {
                    var fraction = match.Groups[11].Value.PadRight(9, '0');
                    milliseconds = double.Parse(fraction.AsSpan(0, 3), CultureInfo.InvariantCulture);
                    microseconds = double.Parse(fraction.AsSpan(3, 3), CultureInfo.InvariantCulture);
                    nanoseconds = double.Parse(fraction.AsSpan(6, 3), CultureInfo.InvariantCulture);
                }
            }
        }

        record = new(
            NoNegativeZero(sign * years),
            NoNegativeZero(sign * months),
            NoNegativeZero(sign * weeks),
            NoNegativeZero(sign * days),
            NoNegativeZero(sign * hours),
            NoNegativeZero(sign * minutes),
            NoNegativeZero(sign * seconds),
            NoNegativeZero(sign * milliseconds),
            NoNegativeZero(sign * microseconds),
            NoNegativeZero(sign * nanoseconds));
        return true;
    }

    internal static double NoNegativeZero(double value)
    {
        return value == 0d ? 0d : value;
    }

    private List<DurationPart> Partition(DurationRecord duration)
    {
        var result = new List<List<DurationPart>>();
        var needSeparator = false;
        var displayNegativeSign = true;
        var hasNegative = duration.HasNegativeComponent;

        foreach (var unit in DurationUnits)
        {
            var numericValue = duration.GetUnitValue(unit);
            var style = GetUnitStyle(unit);
            var display = GetUnitDisplay(unit);
            var singularUnit = unit[..^1];

            var formatValue = new JsValue(numericValue);
            var done = false;
            if (unit is "seconds" or "milliseconds" or "microseconds")
            {
                var nextStyle = GetUnitStyle(DurationUnits[Array.IndexOf(DurationUnits, unit) + 1]);
                if (string.Equals(nextStyle, "numeric", StringComparison.Ordinal))
                {
                    var exponent = unit switch
                    {
                        "seconds" => 9,
                        "milliseconds" => 6,
                        _ => 3
                    };
                    formatValue = JsValue.FromString(DurationToFractionalString(duration, exponent));
                    done = true;
                }
            }

            var numericLike = style is "numeric" or "2-digit";
            var displayRequired = unit == "minutes" &&
                                  (needSeparator ||
                                   (string.Equals(Style, "digital", StringComparison.Ordinal) &&
                                    (duration.IsPresent("minutes") || duration.IsPresent("hours")))) &&
                                  (string.Equals(SecondsDisplay, "always", StringComparison.Ordinal) ||
                                   duration.Seconds != 0 || duration.Milliseconds != 0 || duration.Microseconds != 0 ||
                                   duration.Nanoseconds != 0);
            var shouldDisplay = ShouldDisplayValue(formatValue, display) || displayRequired;

            if (shouldDisplay)
            {
                var hideSign = !displayNegativeSign;
                if (displayNegativeSign)
                {
                    displayNegativeSign = false;
                    if (IsZeroValue(formatValue) && hasNegative)
                        formatValue = formatValue.IsString
                            ? JsValue.FromString(EnsureNegativeString(formatValue.AsString()))
                            : new(-0d);
                }

                var numberFormat = CreateNumberFormat(
                    singularUnit,
                    style,
                    hideSign,
                    GetMinimumIntegerDigits(unit, style, needSeparator),
                    done ? FractionalDigits ?? 0 : null,
                    done ? FractionalDigits ?? 9 : null,
                    done ? "trunc" : null);

                List<DurationPart> list;
                if (!needSeparator)
                {
                    list = [];
                }
                else
                {
                    list = result[^1];
                    list.Add(new("literal", ":", null));
                }

                foreach (var numberPart in BuildNumberParts(numberFormat, formatValue, singularUnit))
                    list.Add(numberPart);

                if (!needSeparator)
                {
                    if (style is "numeric" or "2-digit")
                        needSeparator = true;
                    result.Add(list);
                }
            }

            if (done)
                break;
        }

        var listStyle = string.Equals(Style, "digital", StringComparison.Ordinal) ? "short" : Style;
        var listFormat = new JsListFormatObject(Realm, Realm.ObjectPrototype, Locale, "unit", listStyle);
        var strings = result.Select(static parts => string.Concat(parts.Select(static part => part.Value))).ToList();
        var listParts = listFormat.FormatToParts(strings);
        var flattened = new List<DurationPart>();
        var elementIndex = 0;
        for (uint i = 0; i < listParts.Length; i++)
        {
            if (!listParts.TryGetElement(i, out var partValue) || !partValue.TryGetObject(out var partObject))
                continue;

            partObject.TryGetProperty("type", out var typeValue);
            partObject.TryGetProperty("value", out var valueValue);
            var type = typeValue.IsString ? typeValue.AsString() : string.Empty;
            var value = valueValue.IsString ? valueValue.AsString() : string.Empty;
            if (string.Equals(type, "element", StringComparison.Ordinal))
                flattened.AddRange(result[elementIndex++]);
            else
                flattened.Add(new(type, value, null));
        }

        return flattened;
    }

    private JsNumberFormatObject CreateNumberFormat(
        string unit,
        string style,
        bool hideSign,
        int minimumIntegerDigits,
        int? minimumFractionDigits,
        int? maximumFractionDigits,
        string? roundingMode)
    {
        var numericLike = style is "numeric" or "2-digit";
        return new(
            Realm,
            Realm.ObjectPrototype,
            Locale,
            NumberingSystem,
            numericLike ? "decimal" : "unit",
            null,
            "symbol",
            "standard",
            numericLike ? null : unit,
            numericLike ? "short" : style,
            "standard",
            "short",
            minimumIntegerDigits,
            minimumFractionDigits ?? 0,
            maximumFractionDigits ?? 3,
            null,
            null,
            false,
            false,
            numericLike ? "false" : "auto",
            hideSign ? "never" : "auto",
            roundingMode ?? "halfExpand",
            "auto",
            1,
            "auto",
            CultureInfo);
    }

    private int GetMinimumIntegerDigits(string unit, string style, bool needSeparator)
    {
        if (string.Equals(style, "2-digit", StringComparison.Ordinal))
            return 2;

        if (string.Equals(Style, "digital", StringComparison.Ordinal))
        {
            if (unit is "minutes" or "seconds")
                return 2;
            return 1;
        }

        if (needSeparator && unit is "minutes" or "seconds")
            return 2;

        return 1;
    }

    private List<DurationPart> BuildNumberParts(JsNumberFormatObject numberFormat, in JsValue value, string unit)
    {
        if (value.IsString &&
            string.Equals(numberFormat.Style, "decimal", StringComparison.Ordinal))
        {
            var raw = value.AsString();
            if (CanPreserveExactDecimal(raw))
                return BuildExactDecimalParts(numberFormat, raw, unit);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedExact))
                return BuildNumberParts(numberFormat, new(parsedExact), unit);
        }

        var number = value.IsString ? double.Parse(value.AsString(), CultureInfo.InvariantCulture) : value.NumberValue;
        var partsArray = numberFormat.FormatToParts(number);
        var parts = new List<DurationPart>();
        for (uint i = 0; i < partsArray.Length; i++)
        {
            if (!partsArray.TryGetElement(i, out var partValue) || !partValue.TryGetObject(out var partObject))
                continue;

            partObject.TryGetProperty("type", out var typeValue);
            partObject.TryGetProperty("value", out var valueValue);
            var type = typeValue.IsString ? typeValue.AsString() : string.Empty;
            var text = valueValue.IsString ? valueValue.AsString() : string.Empty;
            parts.Add(new(type, text, unit));
        }

        return parts;
    }

    private static bool CanPreserveExactDecimal(string raw)
    {
        var start = raw.StartsWith("-", StringComparison.Ordinal) || raw.StartsWith("+", StringComparison.Ordinal)
            ? 1
            : 0;
        var unsigned = raw[start..];
        var dotIndex = unsigned.IndexOf('.');
        var integerPart = dotIndex >= 0 ? unsigned[..dotIndex] : unsigned;
        var fractionPart = dotIndex >= 0 ? unsigned[(dotIndex + 1)..] : string.Empty;
        integerPart = integerPart.TrimStart('0');
        fractionPart = fractionPart.TrimEnd('0');
        var significantDigits = (integerPart.Length == 0 ? 1 : integerPart.Length) + fractionPart.Length;
        return significantDigits <= 15;
    }

    private List<DurationPart> BuildExactDecimalParts(JsNumberFormatObject numberFormat, string raw, string unit)
    {
        var parts = new List<DurationPart>();
        var negative = raw.StartsWith("-", StringComparison.Ordinal);
        var startIndex = raw.StartsWith("+", StringComparison.Ordinal) || negative ? 1 : 0;
        var unsigned = raw[startIndex..];
        var dotIndex = unsigned.IndexOf('.');
        var integerPart = dotIndex >= 0 ? unsigned[..dotIndex] : unsigned;
        var fractionPart = dotIndex >= 0 ? unsigned[(dotIndex + 1)..] : string.Empty;
        if (integerPart.Length == 0)
            integerPart = "0";

        var maxFractionDigits = numberFormat.MaximumFractionDigits;
        var minFractionDigits = numberFormat.MinimumFractionDigits;
        if (fractionPart.Length > maxFractionDigits)
            fractionPart = fractionPart[..maxFractionDigits];
        while (fractionPart.Length > minFractionDigits && fractionPart.EndsWith('0'))
            fractionPart = fractionPart[..^1];
        if (fractionPart.Length < minFractionDigits)
            fractionPart = fractionPart.PadRight(minFractionDigits, '0');
        integerPart = integerPart.PadLeft(numberFormat.MinimumIntegerDigits, '0');

        var decimalSeparator = OkojoIntlNumberingSystemData.GetDecimalSeparator(numberFormat.NumberingSystem,
            numberFormat.CultureInfo.NumberFormat.NumberDecimalSeparator);
        var renderedInteger =
            OkojoIntlNumberingSystemData.TransliterateDigits(integerPart, numberFormat.NumberingSystem);
        var renderedFraction =
            OkojoIntlNumberingSystemData.TransliterateDigits(fractionPart, numberFormat.NumberingSystem);

        if (negative && !string.Equals(numberFormat.SignDisplay, "never", StringComparison.Ordinal))
            parts.Add(new("minusSign", "-", unit));

        parts.Add(new("integer", renderedInteger, unit));
        if (renderedFraction.Length != 0)
        {
            parts.Add(new("decimal", decimalSeparator, unit));
            parts.Add(new("fraction", renderedFraction, unit));
        }

        return parts;
    }


    private JsPlainObject CreatePartObject(string type, string value, string? unit)
    {
        var obj = new JsPlainObject(Realm)
        {
            Prototype = Realm.ObjectPrototype
        };
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("type"), JsValue.FromString(type),
            JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("value"), JsValue.FromString(value),
            JsShapePropertyFlags.Open);
        if (unit is not null)
            obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("unit"), JsValue.FromString(unit),
                JsShapePropertyFlags.Open);
        return obj;
    }

    private string GetUnitStyle(string unit)
    {
        return unit switch
        {
            "years" => YearsStyle,
            "months" => MonthsStyle,
            "weeks" => WeeksStyle,
            "days" => DaysStyle,
            "hours" => HoursStyle,
            "minutes" => MinutesStyle,
            "seconds" => SecondsStyle,
            "milliseconds" => MillisecondsStyle,
            "microseconds" => MicrosecondsStyle,
            _ => NanosecondsStyle
        };
    }

    private string GetUnitDisplay(string unit)
    {
        return unit switch
        {
            "years" => YearsDisplay,
            "months" => MonthsDisplay,
            "weeks" => WeeksDisplay,
            "days" => DaysDisplay,
            "hours" => HoursDisplay,
            "minutes" => MinutesDisplay,
            "seconds" => SecondsDisplay,
            "milliseconds" => MillisecondsDisplay,
            "microseconds" => MicrosecondsDisplay,
            _ => NanosecondsDisplay
        };
    }

    private static bool ShouldDisplayValue(in JsValue value, string display)
    {
        if (string.Equals(display, "always", StringComparison.Ordinal))
            return true;
        return !IsZeroValue(value);
    }

    private static bool IsZeroValue(in JsValue value)
    {
        if (value.IsString)
            return IsZeroString(value.AsString());
        return value.NumberValue == 0d;
    }

    private static bool IsZeroString(string value)
    {
        foreach (var c in value)
        {
            if (c is '-' or '+' or '.')
                continue;
            if (c != '0')
                return false;
        }

        return true;
    }

    private static string EnsureNegativeString(string value)
    {
        return value.StartsWith("-", StringComparison.Ordinal) ? value : "-" + value;
    }

    private static string DurationToFractionalString(DurationRecord duration, int exponent)
    {
        if (exponent == 9 && duration.Milliseconds == 0 && duration.Microseconds == 0 && duration.Nanoseconds == 0)
            return IntegerString(duration.Seconds);
        if (exponent == 6 && duration.Microseconds == 0 && duration.Nanoseconds == 0)
            return IntegerString(duration.Milliseconds);
        if (exponent == 3 && duration.Nanoseconds == 0)
            return IntegerString(duration.Microseconds);

        var ns = ToBigInteger(duration.Nanoseconds);
        switch (exponent)
        {
            case 9:
                ns += ToBigInteger(duration.Seconds) * 1_000_000_000;
                goto case 6;
            case 6:
                ns += ToBigInteger(duration.Milliseconds) * 1_000_000;
                goto case 3;
            case 3:
                ns += ToBigInteger(duration.Microseconds) * 1_000;
                break;
        }

        var divisor = BigInteger.Pow(10, exponent);
        var quotient = ns / divisor;
        var remainder = BigInteger.Abs(ns % divisor);
        var fraction = remainder.ToString(CultureInfo.InvariantCulture).PadLeft(exponent, '0');
        if (ns.Sign < 0 && quotient.IsZero)
            return "-0." + fraction;
        return quotient.ToString(CultureInfo.InvariantCulture) + "." + fraction;
    }

    private static string IntegerString(double value)
    {
        return ToBigInteger(value).ToString(CultureInfo.InvariantCulture);
    }

    private static BigInteger ToBigInteger(double value)
    {
        return new(value);
    }

    private static double ParseFraction(string fractionDigits)
    {
        return double.Parse("0." + fractionDigits, CultureInfo.InvariantCulture);
    }

    private static double ParseDurationComponent(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0d;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return result;
        return double.PositiveInfinity;
    }

    private readonly record struct DurationPart(string Type, string Value, string? Unit);

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct DurationRecord(
        double Years,
        double Months,
        double Weeks,
        double Days,
        double Hours,
        double Minutes,
        double Seconds,
        double Milliseconds,
        double Microseconds,
        double Nanoseconds,
        ulong PresentMask = 0)
    {
        internal bool HasNegativeComponent =>
            Years < 0 || Months < 0 || Weeks < 0 || Days < 0 || Hours < 0 || Minutes < 0 || Seconds < 0 ||
            Milliseconds < 0 || Microseconds < 0 || Nanoseconds < 0;

        internal bool IsPresent(string unit)
        {
            return (PresentMask & GetUnitMask(unit)) != 0;
        }

        internal double GetUnitValue(string unit)
        {
            return unit switch
            {
                "years" => Years,
                "months" => Months,
                "weeks" => Weeks,
                "days" => Days,
                "hours" => Hours,
                "minutes" => Minutes,
                "seconds" => Seconds,
                "milliseconds" => Milliseconds,
                "microseconds" => Microseconds,
                _ => Nanoseconds
            };
        }

        private static ulong GetUnitMask(string unit)
        {
            return unit switch
            {
                "years" => 1UL << 0,
                "months" => 1UL << 1,
                "weeks" => 1UL << 2,
                "days" => 1UL << 3,
                "hours" => 1UL << 4,
                "minutes" => 1UL << 5,
                "seconds" => 1UL << 6,
                "milliseconds" => 1UL << 7,
                "microseconds" => 1UL << 8,
                _ => 1UL << 9
            };
        }
    }
}
