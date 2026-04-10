using System.Globalization;
using System.Numerics;
using System.Text;
using Okojo.Runtime.Intl;

namespace Okojo.Runtime;

internal sealed class JsNumberFormatObject : JsObject
{
    private static readonly HashSet<string> Min2GroupingLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "pl"
    };

    private static readonly
        Dictionary<string, (long divisor, string shortSuffix, string longSuffix, bool shortSpace, bool longSpace)>
        CompactPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["en-thousand"] = (1_000L, "K", "thousand", false, true),
            ["en-million"] = (1_000_000L, "M", "million", false, true),
            ["en-billion"] = (1_000_000_000L, "B", "billion", false, true),
            ["en-trillion"] = (1_000_000_000_000L, "T", "trillion", false, true),
            ["zh-TW-thousand"] = (10_000L, "萬", "萬", false, false),
            ["zh-TW-million"] = (100_000_000L, "億", "億", false, false)
        };

    internal JsNumberFormatObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string numberingSystem,
        string style,
        string? currency,
        string currencyDisplay,
        string currencySign,
        string? unit,
        string unitDisplay,
        string notation,
        string compactDisplay,
        int minimumIntegerDigits,
        int minimumFractionDigits,
        int maximumFractionDigits,
        int? minimumSignificantDigits,
        int? maximumSignificantDigits,
        bool minimumSignificantDigitsExplicit,
        bool maximumSignificantDigitsExplicit,
        string useGrouping,
        string signDisplay,
        string roundingMode,
        string roundingPriority,
        int roundingIncrement,
        string trailingZeroDisplay,
        CultureInfo cultureInfo) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        NumberingSystem = numberingSystem;
        Style = style;
        Currency = currency;
        CurrencyDisplay = currencyDisplay;
        CurrencySign = currencySign;
        Unit = unit;
        UnitDisplay = unitDisplay;
        Notation = notation;
        CompactDisplay = compactDisplay;
        MinimumIntegerDigits = minimumIntegerDigits;
        MinimumFractionDigits = minimumFractionDigits;
        MaximumFractionDigits = maximumFractionDigits;
        MinimumSignificantDigits = minimumSignificantDigits;
        MaximumSignificantDigits = maximumSignificantDigits;
        MinimumSignificantDigitsExplicit = minimumSignificantDigitsExplicit;
        MaximumSignificantDigitsExplicit = maximumSignificantDigitsExplicit;
        UseGrouping = useGrouping;
        SignDisplay = signDisplay;
        RoundingMode = roundingMode;
        RoundingPriority = roundingPriority;
        RoundingIncrement = roundingIncrement;
        TrailingZeroDisplay = trailingZeroDisplay;
        CultureInfo = cultureInfo;
    }

    internal string Locale { get; }
    internal string NumberingSystem { get; }
    internal string Style { get; }
    internal string? Currency { get; }
    internal string CurrencyDisplay { get; }
    internal string CurrencySign { get; }
    internal string? Unit { get; }
    internal string UnitDisplay { get; }
    internal string Notation { get; }
    internal string CompactDisplay { get; }
    internal int MinimumIntegerDigits { get; }
    internal int MinimumFractionDigits { get; }
    internal int MaximumFractionDigits { get; }
    internal int? MinimumSignificantDigits { get; }
    internal int? MaximumSignificantDigits { get; }
    internal bool MinimumSignificantDigitsExplicit { get; }
    internal bool MaximumSignificantDigitsExplicit { get; }
    internal string UseGrouping { get; }
    internal string SignDisplay { get; }
    internal string RoundingMode { get; }
    internal string RoundingPriority { get; }
    internal int RoundingIncrement { get; }
    internal string TrailingZeroDisplay { get; }
    internal CultureInfo CultureInfo { get; }

    internal bool SupportsExactIntegralFormatting =>
        string.Equals(Style, "decimal", StringComparison.Ordinal) &&
        string.Equals(Notation, "standard", StringComparison.Ordinal) &&
        !MinimumSignificantDigits.HasValue &&
        !MaximumSignificantDigits.HasValue;

    internal string Format(double value)
    {
        if (double.IsNaN(value))
            return FormatSpecialValue(CultureInfo.NumberFormat.NaNSymbol, false, true);
        if (double.IsPositiveInfinity(value))
            return FormatSpecialValue(CultureInfo.NumberFormat.PositiveInfinitySymbol, false, false);
        if (double.IsNegativeInfinity(value))
            return FormatSpecialValue(CultureInfo.NumberFormat.PositiveInfinitySymbol, true, false);

        if (!string.Equals(Notation, "standard", StringComparison.Ordinal))
            return FormatWithNotation(value);

        return FormatStandard(value);
    }

    internal bool TryFormatExactValue(in JsValue value, out string formatted)
    {
        if (TryFormatExactParts(value, out var parts))
        {
            formatted = JoinParts(parts);
            return true;
        }

        formatted = string.Empty;
        return false;
    }

    internal string FormatExactIntegralString(string raw)
    {
        var digits = raw;
        var isNegative = false;
        if (digits.StartsWith('+'))
        {
            digits = digits[1..];
        }
        else if (digits.StartsWith('-'))
        {
            isNegative = true;
            digits = digits[1..];
        }

        digits = digits.TrimStart('0');
        if (digits.Length == 0)
            digits = "0";
        digits = digits.PadLeft(MinimumIntegerDigits, '0');

        var groupedInteger = ApplyGrouping(digits);
        groupedInteger = OkojoIntlNumberingSystemData.TransliterateDigits(groupedInteger, NumberingSystem);
        if (!ShouldShowSign(isNegative, digits == "0"))
            return groupedInteger;
        return isNegative ? ApplyNegativeSign(groupedInteger) : ApplyPositiveSign(groupedInteger);
    }

    private string FormatExactDecimalValue(ExactDecimalValue value)
    {
        var isNegative = value.IsNegative;
        var absoluteValue = value.Abs();
        var usedSignificantDigits = MinimumSignificantDigits.HasValue || MaximumSignificantDigits.HasValue;
        var raw = usedSignificantDigits
            ? FormatExactDecimalWithSignificantDigits(absoluteValue)
            : FormatExactDecimalWithFractionDigits(absoluteValue);

        var displaysAsZero = IsFormattedZero(raw);
        var formatted = FormatRawAsciiNumberString(raw, !usedSignificantDigits);
        if (!ShouldShowSign(isNegative, displaysAsZero))
            return formatted;
        return isNegative ? ApplyNegativeSign(formatted) : ApplyPositiveSign(formatted);
    }

    private string FormatStandard(double value)
    {
        return Style switch
        {
            "percent" => FormatPercent(value),
            "currency" => FormatCurrency(value),
            "unit" => FormatUnit(value),
            _ => FormatDecimal(value)
        };
    }

    private string FormatWithNotation(double value)
    {
        return Notation switch
        {
            "scientific" => FormatScientific(value),
            "engineering" => FormatEngineering(value),
            "compact" => FormatCompact(value),
            _ => FormatStandard(value)
        };
    }

    internal JsArray FormatToParts(double value)
    {
        List<(string Type, string Value)> parts;
        if (double.IsNaN(value))
        {
            parts = [];
            AppendSignParts(parts, false, false, true);
            parts.Add(("nan", CultureInfo.NumberFormat.NaNSymbol));
            return CreatePartsArray(parts);
        }

        if (double.IsInfinity(value))
        {
            parts = [];
            AppendSignParts(parts, double.IsNegativeInfinity(value), false, false);
            parts.Add(("infinity", CultureInfo.NumberFormat.PositiveInfinitySymbol));
            return CreatePartsArray(parts);
        }

        if (TryFormatExactParts(new(value), out parts))
            return CreatePartsArray(parts);

        parts = Notation switch
        {
            "compact" => BuildCompactParts(value),
            "scientific" => BuildExponentParts(value, false),
            "engineering" => BuildExponentParts(value, true),
            _ => Style switch
            {
                "percent" => BuildPercentParts(value),
                "currency" => BuildCurrencyParts(value),
                "unit" => BuildUnitParts(value),
                _ => BuildDecimalParts(value)
            }
        };

        return CreatePartsArray(parts);
    }

    private bool TryFormatExactParts(in JsValue value, out List<(string Type, string Value)> parts)
    {
        parts = [];
        if (!TryGetExactFormattedRaw(value, out var raw, out var isNegative, out var usedSignificantDigits))
            return false;

        var isZero = IsFormattedZero(raw);
        switch (Style)
        {
            case "decimal":
                AppendSignParts(parts, isNegative, isZero, false);
                AppendNumberCoreParts(parts, raw, !usedSignificantDigits);
                return true;
            case "percent":
                AppendExactPercentParts(parts, raw, isNegative, isZero, usedSignificantDigits);
                return true;
            case "currency":
                AppendExactCurrencyParts(parts, raw, isNegative, isZero, usedSignificantDigits);
                return true;
            default:
                return false;
        }
    }

    private bool TryGetExactFormattedRaw(in JsValue value, out string raw, out bool isNegative,
        out bool usedSignificantDigits)
    {
        raw = string.Empty;
        isNegative = false;
        usedSignificantDigits = false;
        if (!string.Equals(Notation, "standard", StringComparison.Ordinal) ||
            !string.Equals(RoundingMode, "halfExpand", StringComparison.Ordinal) ||
            RoundingIncrement != 1 ||
            !TryParseExactDecimalValue(value, out var exactValue))
            return false;

        isNegative = exactValue.IsNegative;
        var absoluteValue = exactValue.Abs();
        if (string.Equals(Style, "percent", StringComparison.Ordinal))
            absoluteValue = MultiplyExactByPowerOfTen(absoluteValue, 2);

        usedSignificantDigits = MinimumSignificantDigits.HasValue || MaximumSignificantDigits.HasValue;
        raw = usedSignificantDigits
            ? FormatExactDecimalWithSignificantDigits(absoluteValue)
            : FormatExactDecimalWithFractionDigits(absoluteValue);
        return true;
    }

    private void AppendExactPercentParts(List<(string Type, string Value)> parts, string raw, bool isNegative,
        bool isZero, bool usedSignificantDigits)
    {
        AppendSignParts(parts, isNegative, isZero, false);

        var percentSymbol = CultureInfo.NumberFormat.PercentSymbol;
        if (string.IsNullOrEmpty(percentSymbol))
            percentSymbol = "%";

        switch (CultureInfo.NumberFormat.PercentPositivePattern)
        {
            case 2:
                parts.Add(("percentSign", percentSymbol));
                AppendNumberCoreParts(parts, raw, !usedSignificantDigits);
                break;
            case 3:
                parts.Add(("percentSign", percentSymbol));
                parts.Add(("literal", GetPercentSpacingLiteral()));
                AppendNumberCoreParts(parts, raw, !usedSignificantDigits);
                break;
            case 0:
                AppendNumberCoreParts(parts, raw, !usedSignificantDigits);
                parts.Add(("literal", GetPercentSpacingLiteral()));
                parts.Add(("percentSign", percentSymbol));
                break;
            default:
                AppendNumberCoreParts(parts, raw, !usedSignificantDigits);
                parts.Add(("percentSign", percentSymbol));
                break;
        }
    }

    private void AppendExactCurrencyParts(List<(string Type, string Value)> parts, string raw, bool inputNegative,
        bool isZero, bool usedSignificantDigits)
    {
        var showAccountingParens = inputNegative &&
                                   ShouldShowSign(true, isZero) &&
                                   string.Equals(CurrencySign, "accounting", StringComparison.Ordinal) &&
                                   UsesAccountingParentheses();

        var symbol = GetCurrencySymbol();
        var suffixCurrency = UsesCurrencyAfterNumber();

        if (showAccountingParens)
            parts.Add(("literal", "("));
        else
            AppendSignParts(parts, inputNegative, isZero, false);

        if (!suffixCurrency)
            parts.Add(("currency", symbol));

        AppendNumberCoreParts(parts, raw, !usedSignificantDigits);

        if (suffixCurrency)
        {
            parts.Add(("literal", GetCurrencySpacingLiteral()));
            parts.Add(("currency", symbol));
        }

        if (showAccountingParens)
            parts.Add(("literal", ")"));
    }

    private string FormatPercent(double value)
    {
        var numeric = FormatNumberCore(value * 100d);
        var percentSymbol = CultureInfo.NumberFormat.PercentSymbol;
        if (string.IsNullOrEmpty(percentSymbol))
            percentSymbol = "%";
        return numeric + percentSymbol;
    }

    private string FormatCurrency(double value)
    {
        return JoinParts(BuildCurrencyParts(value));
    }

    private string FormatUnit(double value)
    {
        var numeric = FormatNumberCore(value);
        if (string.IsNullOrEmpty(Unit))
            return numeric;
        if (string.Equals(Unit, "kilometer-per-hour", StringComparison.Ordinal) &&
            string.Equals(UnitDisplay, "long", StringComparison.Ordinal))
        {
            if (Locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return "時速 " + numeric + " " + GetRenderedUnit(value);
            if (Locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase))
                return "每小時 " + numeric + " " + GetRenderedUnit(value);
            if (Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                return "시속 " + numeric + GetRenderedUnit(value);
        }

        var suffix = GetFormattedUnitSuffix(value);
        return numeric + suffix;
    }

    private string FormatDecimal(double value)
    {
        return FormatNumberCore(value);
    }

    private string FormatScientific(double value)
    {
        if (value == 0d)
            return FormatNumberCore(value);

        var isNegative = IsNegative(value);
        var absValue = Math.Abs(value);
        var exponent = (int)Math.Floor(Math.Log10(absValue));
        var mantissa = absValue / Math.Pow(10d, exponent);
        var roundedMantissa = ApplyConfiguredRounding(mantissa, false, out var usedSignificantDigits);
        var mantissaText = FormatRoundedNumberString(roundedMantissa, usedSignificantDigits);
        var result = mantissaText + "E" + exponent.ToString(CultureInfo.InvariantCulture);
        return isNegative ? ApplyNegativeSign(result) : result;
    }

    private string FormatEngineering(double value)
    {
        if (value == 0d)
            return FormatNumberCore(value);

        var isNegative = IsNegative(value);
        var absValue = Math.Abs(value);
        var exponent = (int)Math.Floor(Math.Log10(absValue));
        var engineeringExponent = (int)(Math.Floor(exponent / 3d) * 3d);
        var mantissa = absValue / Math.Pow(10d, engineeringExponent);
        var roundedMantissa = ApplyConfiguredRounding(mantissa, false, out var usedSignificantDigits);
        var mantissaText = FormatRoundedNumberString(roundedMantissa, usedSignificantDigits);
        var result = mantissaText + "E" + engineeringExponent.ToString(CultureInfo.InvariantCulture);
        return isNegative ? ApplyNegativeSign(result) : result;
    }

    private string FormatCompact(double value)
    {
        if (value == 0d)
            return FormatNumberCore(value);

        var isNegative = IsNegative(value);
        var absValue = Math.Abs(value);
        var compactLocale = GetCompactLocaleKey();
        if (compactLocale == "de")
            return FormatCompactDe(value, absValue, isNegative);
        var pattern = ResolveCompactPattern(compactLocale, absValue);
        if (pattern is null) return FormatCompactSmallValue(value, absValue, isNegative);

        var compactValue = absValue / pattern.Value.divisor;
        var maxFractionDigits = compactValue >= 100 ? 0 : compactValue >= 10 ? 0 : 1;
        var rounded = RoundWithMode(compactValue, maxFractionDigits, false);
        var formatted = FormatCompactNumber(rounded, maxFractionDigits);
        var suffix = string.Equals(CompactDisplay, "long", StringComparison.Ordinal)
            ? pattern.Value.longSuffix
            : pattern.Value.shortSuffix;
        var addSpace = string.Equals(CompactDisplay, "long", StringComparison.Ordinal)
            ? pattern.Value.longSpace
            : pattern.Value.shortSpace;
        var separator = addSpace
            ? GetCompactSeparator(compactLocale, string.Equals(CompactDisplay, "long", StringComparison.Ordinal))
            : string.Empty;
        var result = formatted + separator + suffix;
        return isNegative ? ApplyNegativeSign(result) : result;
    }

    private string FormatCompactSmallValue(double value, double absValue, bool isNegative)
    {
        if (absValue == 0d)
            return "0";

        int maxFractionDigits;
        if (absValue >= 100d)
        {
            maxFractionDigits = 0;
        }
        else if (absValue >= 10d)
        {
            maxFractionDigits = 0;
        }
        else if (absValue >= 1d)
        {
            maxFractionDigits = 1;
        }
        else
        {
            var magnitude = (int)Math.Floor(Math.Log10(absValue));
            maxFractionDigits = 1 - magnitude;
        }

        var rounded = RoundWithMode(absValue, maxFractionDigits, false);
        var formatted = FormatCompactNumber(rounded, maxFractionDigits);
        return isNegative ? ApplyNegativeSign(formatted) : formatted;
    }

    private string FormatNumberCore(double value)
    {
        var isNegative = IsNegative(value);
        var absValue = Math.Abs(value);
        var rounded = ApplyConfiguredRounding(absValue, isNegative, out var usedSignificantDigits);

        var result = FormatRoundedNumberString(rounded, usedSignificantDigits);
        if (!ShouldShowSign(isNegative, rounded == 0))
            return result;
        return isNegative ? ApplyNegativeSign(result) : ApplyPositiveSign(result);
    }

    private string FormatExactDecimalWithFractionDigits(ExactDecimalValue value)
    {
        var rounded = RoundExactToFractionDigits(value, MaximumFractionDigits, value.IsNegative);
        return TrimRawFraction(ToPlainAsciiString(rounded));
    }

    private string FormatExactDecimalWithSignificantDigits(ExactDecimalValue value)
    {
        var minSig = MinimumSignificantDigits ?? 1;
        var maxSig = MaximumSignificantDigits ?? Math.Max(minSig, 21);

        if (string.Equals(RoundingPriority, "morePrecision", StringComparison.Ordinal) ||
            string.Equals(RoundingPriority, "lessPrecision", StringComparison.Ordinal))
        {
            var significantResult = FormatExactToSignificantDigits(value, minSig, maxSig);
            var fractionResult = FormatExactDecimalWithFractionDigits(value);
            var comparison = CompareExactPrecisionLoss(value, significantResult, fractionResult);

            if (string.Equals(RoundingPriority, "morePrecision", StringComparison.Ordinal))
            {
                if (comparison < 0)
                    return significantResult;
                if (comparison > 0)
                    return fractionResult;

                if (MaximumSignificantDigitsExplicit && !MinimumSignificantDigitsExplicit)
                    return fractionResult;

                if (MaximumSignificantDigitsExplicit && MinimumSignificantDigitsExplicit)
                {
                    var effectiveFractionMax = MaximumFractionDigits + 1;
                    if (maxSig > effectiveFractionMax)
                        return significantResult;
                    if (effectiveFractionMax > maxSig)
                        return fractionResult;

                    var effectiveFractionMin = MinimumFractionDigits + 1;
                    return minSig >= effectiveFractionMin ? significantResult : fractionResult;
                }

                return significantResult;
            }

            if (comparison > 0)
                return significantResult;
            if (comparison < 0)
                return fractionResult;

            if (MaximumSignificantDigitsExplicit && !MinimumSignificantDigitsExplicit)
                return significantResult;

            if (MaximumSignificantDigitsExplicit && MinimumSignificantDigitsExplicit)
            {
                var effectiveFractionMax = MaximumFractionDigits + 1;
                if (maxSig < effectiveFractionMax)
                    return significantResult;
                if (effectiveFractionMax < maxSig)
                    return fractionResult;

                var effectiveFractionMin = MinimumFractionDigits + 1;
                return minSig <= effectiveFractionMin ? significantResult : fractionResult;
            }

            return fractionResult;
        }

        return FormatExactToSignificantDigits(value, minSig, maxSig);
    }

    private string FormatExactToSignificantDigits(ExactDecimalValue value, int minSig, int maxSig)
    {
        if (value.IsZero)
            return minSig > 1 ? "0." + new string('0', minSig - 1) : "0";

        var rounded = RoundExactToSignificantDigits(value, maxSig, value.IsNegative);
        var raw = ToPlainAsciiString(rounded);
        raw = TrimTrailingFractionZerosBySignificance(raw, minSig);
        raw = EnsureMinimumSignificantDigits(raw, minSig);
        return raw;
    }

    private string FormatRawAsciiNumberString(string raw, bool trimFraction = true)
    {
        var dotIndex = raw.IndexOf('.');
        var integerPart = dotIndex >= 0 ? raw[..dotIndex] : raw;
        var fractionPart = dotIndex >= 0 ? raw[(dotIndex + 1)..] : string.Empty;

        integerPart = integerPart.PadLeft(MinimumIntegerDigits, '0');
        if (trimFraction)
            fractionPart = TrimFraction(fractionPart);

        var groupedInteger = ApplyGrouping(integerPart);
        groupedInteger = OkojoIntlNumberingSystemData.TransliterateDigits(groupedInteger, NumberingSystem);

        var builder = new StringBuilder(groupedInteger);
        if (fractionPart.Length > 0)
        {
            builder.Append(OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
                CultureInfo.NumberFormat.NumberDecimalSeparator));
            builder.Append(OkojoIntlNumberingSystemData.TransliterateDigits(fractionPart, NumberingSystem));
        }

        return builder.ToString();
    }

    private string TrimFraction(string fractionPart)
    {
        if (fractionPart.Length == 0)
            return string.Empty;

        var minLength = MinimumFractionDigits;
        var end = fractionPart.Length;
        while (end > minLength && fractionPart[end - 1] == '0')
            end--;
        return fractionPart[..end];
    }

    private string FormatRoundedNumberString(double rounded, bool usedSignificantDigits)
    {
        var raw = usedSignificantDigits
            ? FormatUsingSignificantDigits(rounded)
            : rounded.ToString("F" + MaximumFractionDigits, CultureInfo.InvariantCulture);
        return FormatRawAsciiNumberString(raw);
    }

    private double ApplyConfiguredRounding(double absValue, bool isNegative, out bool usedSignificantDigits)
    {
        usedSignificantDigits = MinimumSignificantDigits.HasValue || MaximumSignificantDigits.HasValue;
        if (usedSignificantDigits)
            return RoundToSignificantDigits(absValue, isNegative);

        if (RoundingIncrement > 1)
            return RoundToIncrement(absValue, isNegative);
        return RoundWithMode(absValue, MaximumFractionDigits, isNegative);
    }

    private double RoundToIncrement(double absValue, bool isNegative)
    {
        var increment = RoundingIncrement / Math.Pow(10d, MaximumFractionDigits);
        if (increment == 0)
            return absValue;
        var scaled = absValue / increment;
        var rounded = ApplyRoundingModeToIntegral(scaled, isNegative);
        return rounded * increment;
    }

    private double RoundToSignificantDigits(double absValue, bool isNegative)
    {
        if (absValue == 0d)
            return 0d;

        var maxDigits = MaximumSignificantDigits ?? MinimumSignificantDigits ?? 21;
        var exponent = (int)Math.Floor(Math.Log10(absValue));
        var fractionDigits = Math.Max(0, maxDigits - exponent - 1);
        return RoundWithMode(absValue, fractionDigits, isNegative);
    }

    private double RoundWithMode(double absValue, int fractionDigits, bool isNegative)
    {
        var scale = Math.Pow(10d, fractionDigits);
        var scaled = absValue * scale;
        var roundedIntegral = ApplyRoundingModeToIntegral(scaled, isNegative);
        return roundedIntegral / scale;
    }

    private double ApplyRoundingModeToIntegral(double scaled, bool isNegative)
    {
        var floor = Math.Floor(scaled);
        var ceil = Math.Ceiling(scaled);
        var fraction = scaled - floor;
        const double epsilon = 1e-9;
        if (Math.Abs(fraction - 0.5d) < epsilon)
            fraction = 0.5d;

        return RoundingMode switch
        {
            "ceil" => isNegative ? floor : ceil,
            "floor" => isNegative ? ceil : floor,
            "expand" => ceil,
            "trunc" => floor,
            "halfCeil" => fraction > 0.5d ? ceil : fraction < 0.5d ? floor : isNegative ? floor : ceil,
            "halfFloor" => fraction > 0.5d ? ceil : fraction < 0.5d ? floor : isNegative ? ceil : floor,
            "halfTrunc" => fraction > 0.5d ? ceil : fraction < 0.5d ? floor : floor,
            "halfEven" => fraction > 0.5d ? ceil : fraction < 0.5d ? floor : ((long)floor & 1L) == 0L ? floor : ceil,
            _ => fraction >= 0.5d ? ceil : floor
        };
    }

    private string FormatUsingSignificantDigits(double rounded)
    {
        var minDigits = MinimumSignificantDigits ?? 1;
        var maxDigits = MaximumSignificantDigits ?? Math.Max(minDigits, 21);
        var text = rounded.ToString("G" + maxDigits, CultureInfo.InvariantCulture);
        if (!text.Contains('E') && !text.Contains('e') && minDigits > 1 && rounded != 0d)
        {
            var digitCount = CountSignificantDigits(text);
            if (digitCount < minDigits)
            {
                var extraFractionDigits = minDigits - digitCount;
                text = rounded.ToString("F" + extraFractionDigits, CultureInfo.InvariantCulture);
            }
        }

        return text;
    }

    private static int CountSignificantDigits(string text)
    {
        var count = 0;
        var started = false;
        foreach (var c in text)
        {
            if (c is '.' or '-' or '+')
                continue;
            if (c == '0' && !started)
                continue;
            if (char.IsDigit(c))
            {
                started = true;
                count++;
            }
        }

        return count == 0 ? 1 : count;
    }

    private static bool IsFormattedZero(string raw)
    {
        foreach (var c in raw)
        {
            if (c is '.' or '+' or '-')
                continue;
            if (c != '0')
                return false;
        }

        return true;
    }

    private string EnsureMinimumSignificantDigits(string raw, int minimumSignificantDigits)
    {
        var digitCount = CountSignificantDigits(raw);
        if (digitCount >= minimumSignificantDigits)
            return raw;

        var zerosToAdd = minimumSignificantDigits - digitCount;
        if (!raw.Contains('.'))
            return raw + "." + new string('0', zerosToAdd);
        return raw + new string('0', zerosToAdd);
    }

    private string TrimRawFraction(string raw)
    {
        var dotIndex = raw.IndexOf('.');
        if (dotIndex < 0)
            return raw;

        var integerPart = raw[..dotIndex];
        var fractionPart = TrimFraction(raw[(dotIndex + 1)..]);
        return fractionPart.Length == 0 ? integerPart : integerPart + "." + fractionPart;
    }

    private string TrimTrailingFractionZerosBySignificance(string raw, int minimumSignificantDigits)
    {
        if (!raw.Contains('.'))
            return raw;

        var result = raw;
        while (CountSignificantDigits(result) > minimumSignificantDigits &&
               result.Length > 0 &&
               result[^1] == '0')
            result = result[..^1];

        if (result.Length > 0 && result[^1] == '.')
            result = result[..^1];
        return result;
    }

    private int CompareExactPrecisionLoss(ExactDecimalValue original, string significantResult, string fractionResult)
    {
        var sigValue = ParseFormattedExactDecimal(significantResult);
        var fracValue = ParseFormattedExactDecimal(fractionResult);
        var sigLoss = GetExactDifferenceMagnitude(original, sigValue);
        var fracLoss = GetExactDifferenceMagnitude(original, fracValue);
        return sigLoss.CompareTo(fracLoss);
    }

    private static BigInteger GetExactDifferenceMagnitude(ExactDecimalValue left, ExactDecimalValue right)
    {
        var commonScale = Math.Max(left.Scale, right.Scale);
        var leftScaled = left.SignedUnscaled * Pow10(commonScale - left.Scale);
        var rightScaled = right.SignedUnscaled * Pow10(commonScale - right.Scale);
        return BigInteger.Abs(leftScaled - rightScaled);
    }

    private ExactDecimalValue RoundExactToFractionDigits(ExactDecimalValue value, int targetScale, bool isNegative)
    {
        if (value.Scale == targetScale)
            return value;

        if (value.Scale < targetScale)
        {
            var adjusted = value.SignedUnscaled * Pow10(targetScale - value.Scale);
            return new(adjusted, targetScale, value.IsNegativeZero);
        }

        var shift = value.Scale - targetScale;
        var divisor = Pow10(shift);
        var absUnscaled = value.Abs().SignedUnscaled;
        var quotient = BigInteger.DivRem(absUnscaled, divisor, out var remainder);
        if (ShouldRoundUp(quotient, remainder, divisor, isNegative))
            quotient += BigInteger.One;
        var signed = isNegative && quotient != BigInteger.Zero ? -quotient : quotient;
        return new(signed, targetScale, isNegative && quotient == BigInteger.Zero);
    }

    private static ExactDecimalValue MultiplyExactByPowerOfTen(ExactDecimalValue value, int exponent)
    {
        if (exponent == 0)
            return value;

        if (exponent > 0)
        {
            if (value.Scale >= exponent)
                return new(value.SignedUnscaled, value.Scale - exponent, value.IsNegativeZero);
            return new(value.SignedUnscaled * Pow10(exponent - value.Scale), 0, value.IsNegativeZero);
        }

        return new(value.SignedUnscaled, value.Scale + -exponent, value.IsNegativeZero);
    }

    private ExactDecimalValue RoundExactToSignificantDigits(ExactDecimalValue value, int maxSignificantDigits,
        bool isNegative)
    {
        var absUnscaled = value.Abs().SignedUnscaled;
        var digits = absUnscaled.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= maxSignificantDigits)
            return value;

        var shift = digits.Length - maxSignificantDigits;
        var divisor = Pow10(shift);
        var quotient = BigInteger.DivRem(absUnscaled, divisor, out var remainder);
        if (ShouldRoundUp(quotient, remainder, divisor, isNegative))
            quotient += BigInteger.One;
        var rounded = quotient * divisor;
        var signed = isNegative ? -rounded : rounded;
        return new(signed, value.Scale, false);
    }

    private bool ShouldRoundUp(BigInteger quotient, BigInteger remainder, BigInteger divisor, bool isNegative)
    {
        if (remainder.IsZero)
            return false;

        var halfComparison = (remainder * 2).CompareTo(divisor);
        var aboveHalf = halfComparison > 0;
        var tie = halfComparison == 0;

        return RoundingMode switch
        {
            "ceil" => !isNegative,
            "floor" => isNegative,
            "expand" => true,
            "trunc" => false,
            "halfCeil" => aboveHalf || (tie && !isNegative),
            "halfFloor" => aboveHalf || (tie && isNegative),
            "halfTrunc" => aboveHalf,
            "halfEven" => aboveHalf || (tie && !quotient.IsEven),
            _ => aboveHalf || tie
        };
    }

    private static string ToPlainAsciiString(ExactDecimalValue value)
    {
        var absUnscaled = value.Abs().SignedUnscaled;
        var digits = absUnscaled.ToString(CultureInfo.InvariantCulture);
        if (value.Scale == 0)
            return digits;

        if (digits.Length > value.Scale)
            return digits[..(digits.Length - value.Scale)] + "." + digits[(digits.Length - value.Scale)..];

        return "0." + new string('0', value.Scale - digits.Length) + digits;
    }

    private static ExactDecimalValue ParseFormattedExactDecimal(string raw)
    {
        var dotIndex = raw.IndexOf('.');
        if (dotIndex < 0)
            return new(BigInteger.Parse(raw, CultureInfo.InvariantCulture), 0, false);

        var integerPart = raw[..dotIndex];
        var fractionPart = raw[(dotIndex + 1)..];
        var digits = (integerPart + fractionPart).TrimStart('0');
        if (digits.Length == 0)
            return new(BigInteger.Zero, 0, false);
        return new(BigInteger.Parse(digits, CultureInfo.InvariantCulture), fractionPart.Length, false);
    }

    private static bool TryParseExactDecimalValue(in JsValue value, out ExactDecimalValue result)
    {
        if (value.IsNumber)
        {
            var number = value.NumberValue;
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                result = default;
                return false;
            }

            var negativeZero = number == 0d && double.IsNegativeInfinity(1d / number);
            var raw = negativeZero ? "-0" : number.ToString("R", CultureInfo.InvariantCulture);
            return TryParseExactDecimalString(raw, out result);
        }

        if (value.IsBigInt)
        {
            result = new(value.AsBigInt().Value, 0, false);
            return true;
        }

        if (!value.IsString)
        {
            result = default;
            return false;
        }

        return TryParseExactDecimalString(value.AsString(), out result);
    }

    private static bool TryParseExactDecimalString(string raw, out ExactDecimalValue result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = raw.Trim();
        var index = 0;
        var isNegative = false;
        if (text[index] is '+' or '-')
        {
            isNegative = text[index] == '-';
            index++;
        }

        var integerStart = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
            index++;
        var integerPart = text[integerStart..index];

        var fractionPart = string.Empty;
        if (index < text.Length && text[index] == '.')
        {
            index++;
            var fractionStart = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
                index++;
            fractionPart = text[fractionStart..index];
        }

        if (integerPart.Length == 0 && fractionPart.Length == 0)
            return false;

        var exponent = 0;
        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            index++;
            var exponentNegative = false;
            if (index < text.Length && text[index] is '+' or '-')
            {
                exponentNegative = text[index] == '-';
                index++;
            }

            var exponentStart = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
                index++;
            if (exponentStart == index)
                return false;

            if (!int.TryParse(text[exponentStart..index], NumberStyles.None, CultureInfo.InvariantCulture,
                    out exponent))
                return false;
            if (exponentNegative)
                exponent = -exponent;
        }

        if (index != text.Length)
            return false;

        var scale = fractionPart.Length - exponent;
        var digits = integerPart + fractionPart;
        if (scale < 0)
        {
            digits += new string('0', -scale);
            scale = 0;
        }

        digits = digits.TrimStart('0');
        if (digits.Length == 0)
        {
            result = new(BigInteger.Zero, 0, isNegative);
            return true;
        }

        var unscaled = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (isNegative)
            unscaled = -unscaled;
        result = new(unscaled, scale, false);
        return true;
    }

    private static BigInteger Pow10(int exponent)
    {
        var result = BigInteger.One;
        for (var i = 0; i < exponent; i++)
            result *= 10;
        return result;
    }

    private static (long divisor, string shortSuffix, string longSuffix, bool shortSpace, bool longSpace)?
        ResolveCompactPattern(string compactLocale, double absValue)
    {
        if (compactLocale == "zh-TW")
        {
            if (absValue >= 100_000_000d)
                return CompactPatterns["zh-TW-million"];
            if (absValue >= 10_000d)
                return CompactPatterns["zh-TW-thousand"];
            return null;
        }

        if (compactLocale == "ja")
        {
            if (absValue >= 100_000_000d)
                return (100_000_000L, "億", "億", false, false);
            if (absValue >= 10_000d)
                return (10_000L, "万", "万", false, false);
            return null;
        }

        if (compactLocale == "ko")
        {
            if (absValue >= 100_000_000d)
                return (100_000_000L, "억", "억", false, false);
            if (absValue >= 10_000d)
                return (10_000L, "만", "만", false, false);
            if (absValue >= 1_000d)
                return (1_000L, "천", "천", false, false);
            return null;
        }

        if (compactLocale == "de")
        {
            if (absValue >= 1_000_000d)
                return (1_000_000L, "Mio.", "Millionen", true, true);
            return null;
        }

        if (compactLocale == "en-IN")
        {
            if (absValue >= 10_000_000d)
                return (10_000_000L, "Cr", "crore", false, true);
            if (absValue >= 100_000d)
                return (100_000L, "L", "lakh", false, true);
        }

        if (absValue >= 1_000_000_000_000d)
            return CompactPatterns["en-trillion"];
        if (absValue >= 1_000_000_000d)
            return CompactPatterns["en-billion"];
        if (absValue >= 1_000_000d)
            return CompactPatterns["en-million"];
        if (absValue >= 1_000d)
            return CompactPatterns["en-thousand"];
        return null;
    }

    private string FormatCompactNumber(double value, int maximumFractionDigits)
    {
        var raw = value.ToString("F" + maximumFractionDigits, CultureInfo.InvariantCulture);
        if (raw.Contains('.'))
            raw = raw.TrimEnd('0').TrimEnd('.');
        raw = raw.Replace(".", CultureInfo.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);
        return raw;
    }

    private string FormatCompactDe(double value, double absValue, bool isNegative)
    {
        var isLong = string.Equals(CompactDisplay, "long", StringComparison.Ordinal);
        if (absValue >= 1_000_000d)
        {
            var formatted = FormatCompactDeScaled(absValue / 1_000_000d);
            var separator = isLong ? " " : "\u00A0";
            var suffix = isLong ? "Millionen" : "Mio.";
            var result = formatted + separator + suffix;
            return isNegative ? ApplyNegativeSign(result) : result;
        }

        if (isLong && absValue >= 1_000d)
        {
            var formatted = FormatCompactDeScaled(absValue / 1_000d);
            var result = formatted + " Tausend";
            return isNegative ? ApplyNegativeSign(result) : result;
        }

        var small = FormatCompactDeShortSmall(absValue);
        return isNegative ? ApplyNegativeSign(small) : small;
    }

    private string FormatCompactDeScaled(double compactValue)
    {
        var maxFractionDigits = compactValue >= 100 ? 0 : compactValue >= 10 ? 0 : 1;
        var rounded = RoundWithMode(compactValue, maxFractionDigits, false);
        return FormatCompactNumber(rounded, maxFractionDigits);
    }

    private string FormatCompactDeShortSmall(double absValue)
    {
        var maxFractionDigits = GetCompactSmallFractionDigits(absValue);
        var rounded = RoundWithMode(absValue, maxFractionDigits, false);
        var raw = rounded.ToString("F" + maxFractionDigits, CultureInfo.InvariantCulture);
        var dotIndex = raw.IndexOf('.');
        var integerPart = dotIndex >= 0 ? raw[..dotIndex] : raw;
        var fractionPart = dotIndex >= 0 ? raw[(dotIndex + 1)..] : string.Empty;
        if (integerPart.Length >= 5)
            integerPart = ApplyGrouping(integerPart);
        fractionPart = fractionPart.TrimEnd('0');

        if (fractionPart.Length == 0)
            return OkojoIntlNumberingSystemData.TransliterateDigits(integerPart, NumberingSystem);

        return OkojoIntlNumberingSystemData.TransliterateDigits(integerPart, NumberingSystem) +
               OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
                   CultureInfo.NumberFormat.NumberDecimalSeparator) +
               OkojoIntlNumberingSystemData.TransliterateDigits(fractionPart, NumberingSystem);
    }

    private bool IsNegative(double value)
    {
        return value < 0 || double.IsNegativeInfinity(1d / value);
    }

    private string FormatSpecialValue(string text, bool isNegative, bool isNan)
    {
        if (!ShouldShowSpecialSign(isNegative, isNan))
            return text;
        return isNegative ? ApplyNegativeSign(text) : ApplyPositiveSign(text);
    }

    private bool ShouldShowSpecialSign(bool isNegative, bool isNan)
    {
        if (isNegative)
            return SignDisplay switch
            {
                "never" => false,
                "negative" => true,
                _ => true
            };

        if (isNan)
            return string.Equals(SignDisplay, "always", StringComparison.Ordinal);

        return SignDisplay is "always" or "exceptZero";
    }

    private void AppendSignParts(JsArray result, ref uint index, bool isNegative, bool isZero, bool isNan)
    {
        var showSign = ShouldShowSign(isNegative, isZero);
        if (!showSign)
            return;

        var type = isNegative ? "minusSign" : "plusSign";
        var value = isNegative ? string.IsNullOrEmpty(CultureInfo.NumberFormat.NegativeSign)
                ? "-"
                : CultureInfo.NumberFormat.NegativeSign
            : string.IsNullOrEmpty(CultureInfo.NumberFormat.PositiveSign) ? "+" : CultureInfo.NumberFormat.PositiveSign;
        if (isNan && string.Equals(SignDisplay, "exceptZero", StringComparison.Ordinal))
            return;
        result.SetElement(index++, JsValue.FromObject(CreatePart(type, value)));
    }

    private void AppendSignParts(List<(string Type, string Value)> parts, bool isNegative, bool isZero, bool isNan)
    {
        var showSign = ShouldShowSign(isNegative, isZero);
        if (!showSign)
            return;

        var type = isNegative ? "minusSign" : "plusSign";
        var value = isNegative
            ? string.IsNullOrEmpty(CultureInfo.NumberFormat.NegativeSign) ? "-" : CultureInfo.NumberFormat.NegativeSign
            : string.IsNullOrEmpty(CultureInfo.NumberFormat.PositiveSign)
                ? "+"
                : CultureInfo.NumberFormat.PositiveSign;
        if (isNan && string.Equals(SignDisplay, "exceptZero", StringComparison.Ordinal))
            return;
        parts.Add((type, value));
    }

    private void AppendNumberCoreParts(JsArray result, ref uint index, double rounded, bool usedSignificantDigits)
    {
        var raw = usedSignificantDigits
            ? FormatUsingSignificantDigits(rounded)
            : rounded.ToString("F" + MaximumFractionDigits, CultureInfo.InvariantCulture);
        var dotIndex = raw.IndexOf('.');
        var integerPart = dotIndex >= 0 ? raw[..dotIndex] : raw;
        var fractionPart = dotIndex >= 0 ? raw[(dotIndex + 1)..] : string.Empty;

        integerPart = integerPart.PadLeft(MinimumIntegerDigits, '0');
        fractionPart = TrimFraction(fractionPart);

        List<string> groups = [integerPart];
        if (ShouldApplyGrouping(integerPart.Length))
        {
            groups.Clear();
            var firstGroupLength = integerPart.Length % 3;
            if (firstGroupLength == 0)
                firstGroupLength = 3;
            groups.Add(integerPart[..firstGroupLength]);
            for (var i = firstGroupLength; i < integerPart.Length; i += 3)
                groups.Add(integerPart.Substring(i, 3));
        }

        var groupSeparator = OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem,
            string.IsNullOrEmpty(CultureInfo.NumberFormat.NumberGroupSeparator)
                ? ","
                : CultureInfo.NumberFormat.NumberGroupSeparator);
        for (var i = 0; i < groups.Count; i++)
        {
            result.SetElement(index++, JsValue.FromObject(CreatePart("integer",
                OkojoIntlNumberingSystemData.TransliterateDigits(groups[i], NumberingSystem))));
            if (i + 1 < groups.Count)
                result.SetElement(index++, JsValue.FromObject(CreatePart("group", groupSeparator)));
        }

        if (fractionPart.Length > 0)
        {
            result.SetElement(index++, JsValue.FromObject(CreatePart("decimal",
                OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
                    CultureInfo.NumberFormat.NumberDecimalSeparator))));
            result.SetElement(index++, JsValue.FromObject(CreatePart("fraction",
                OkojoIntlNumberingSystemData.TransliterateDigits(fractionPart, NumberingSystem))));
        }
    }

    private void AppendNumberCoreParts(List<(string Type, string Value)> parts, double rounded,
        bool usedSignificantDigits)
    {
        var raw = usedSignificantDigits
            ? FormatUsingSignificantDigits(rounded)
            : rounded.ToString("F" + MaximumFractionDigits, CultureInfo.InvariantCulture);
        AppendNumberCoreParts(parts, raw, true);
    }

    private void AppendNumberCoreParts(List<(string Type, string Value)> parts, string raw, bool trimFraction)
    {
        var dotIndex = raw.IndexOf('.');
        var integerPart = dotIndex >= 0 ? raw[..dotIndex] : raw;
        var fractionPart = dotIndex >= 0 ? raw[(dotIndex + 1)..] : string.Empty;

        integerPart = integerPart.PadLeft(MinimumIntegerDigits, '0');
        if (trimFraction)
            fractionPart = TrimFraction(fractionPart);

        List<string> groups = [integerPart];
        if (ShouldApplyGrouping(integerPart.Length))
        {
            groups.Clear();
            if (Locale.StartsWith("en-IN", StringComparison.OrdinalIgnoreCase))
            {
                var grouped = ApplyIndianGrouping(integerPart);
                groups.AddRange(grouped.Split(OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem,
                    string.IsNullOrEmpty(CultureInfo.NumberFormat.NumberGroupSeparator)
                        ? ","
                        : CultureInfo.NumberFormat.NumberGroupSeparator)));
            }
            else
            {
                var firstGroupLength = integerPart.Length % 3;
                if (firstGroupLength == 0)
                    firstGroupLength = 3;
                groups.Add(integerPart[..firstGroupLength]);
                for (var i = firstGroupLength; i < integerPart.Length; i += 3)
                    groups.Add(integerPart.Substring(i, 3));
            }
        }

        var groupSeparator = OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem,
            string.IsNullOrEmpty(CultureInfo.NumberFormat.NumberGroupSeparator)
                ? ","
                : CultureInfo.NumberFormat.NumberGroupSeparator);
        for (var i = 0; i < groups.Count; i++)
        {
            parts.Add(("integer", OkojoIntlNumberingSystemData.TransliterateDigits(groups[i], NumberingSystem)));
            if (i + 1 < groups.Count)
                parts.Add(("group", groupSeparator));
        }

        if (fractionPart.Length > 0)
        {
            parts.Add(("decimal",
                OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
                    CultureInfo.NumberFormat.NumberDecimalSeparator)));
            parts.Add(("fraction", OkojoIntlNumberingSystemData.TransliterateDigits(fractionPart, NumberingSystem)));
        }
    }

    private void AppendUnitParts(JsArray result, ref uint index)
    {
        if (string.IsNullOrEmpty(Unit))
            return;

        if (TryAppendPrefixedLongUnitParts(result, ref index))
            return;

        var renderedUnit = GetRenderedUnit();
        if (!string.Equals(UnitDisplay, "narrow", StringComparison.Ordinal))
            result.SetElement(index++, JsValue.FromObject(CreatePart("literal", " ")));
        result.SetElement(index++, JsValue.FromObject(CreatePart("unit", renderedUnit)));
    }

    private bool TryAppendPrefixedLongUnitParts(JsArray result, ref uint index)
    {
        if (!string.Equals(Unit, "kilometer-per-hour", StringComparison.Ordinal) ||
            !string.Equals(UnitDisplay, "long", StringComparison.Ordinal))
            return false;

        if (Locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            result.SetElement(index++, JsValue.FromObject(CreatePart("literal", "時速 ")));
            result.SetElement(index++, JsValue.FromObject(CreatePart("unit", "キロメートル")));
            return true;
        }

        if (Locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase))
        {
            result.SetElement(index++, JsValue.FromObject(CreatePart("literal", "每小時 ")));
            result.SetElement(index++, JsValue.FromObject(CreatePart("unit", "公里")));
            return true;
        }

        return false;
    }

    private bool TryAppendPrefixedLongUnitParts(List<(string Type, string Value)> parts, double rounded,
        bool usedSignificantDigits, bool isNegative)
    {
        if (!string.Equals(Unit, "kilometer-per-hour", StringComparison.Ordinal) ||
            !string.Equals(UnitDisplay, "long", StringComparison.Ordinal))
            return false;

        if (Locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(("unit", "時速"));
            parts.Add(("literal", " "));
            AppendSignParts(parts, isNegative, rounded == 0d, false);
            AppendNumberCoreParts(parts, rounded, usedSignificantDigits);
            parts.Add(("literal", " "));
            parts.Add(("unit", "キロメートル"));
            return true;
        }

        if (Locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(("unit", "每小時"));
            parts.Add(("literal", " "));
            AppendSignParts(parts, isNegative, rounded == 0d, false);
            AppendNumberCoreParts(parts, rounded, usedSignificantDigits);
            parts.Add(("literal", " "));
            parts.Add(("unit", "公里"));
            return true;
        }

        if (Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(("unit", "시속"));
            parts.Add(("literal", " "));
            AppendSignParts(parts, isNegative, rounded == 0d, false);
            AppendNumberCoreParts(parts, rounded, usedSignificantDigits);
            parts.Add(("unit", "킬로미터"));
            return true;
        }

        return false;
    }

    private string GetRenderedUnit()
    {
        return GetRenderedUnit(2d);
    }

    private string GetRenderedUnit(double value)
    {
        if (string.Equals(Unit, "percent", StringComparison.Ordinal))
            return "%";

        if (TryGetLocalizedDurationUnit(value, out var localizedDurationUnit))
            return localizedDurationUnit!;

        if (string.Equals(Unit, "kilometer-per-hour", StringComparison.Ordinal))
        {
            if (Locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase))
                return UnitDisplay switch
                {
                    "long" => "公里",
                    _ => "公里/小時"
                };

            if (Locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return UnitDisplay switch
                {
                    "long" => "キロメートル",
                    _ => "km/h"
                };

            if (Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                return UnitDisplay switch
                {
                    "long" => "킬로미터",
                    _ => "km/h"
                };

            if (Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
                return UnitDisplay switch
                {
                    "long" => "Kilometer pro Stunde",
                    _ => "km/h"
                };

            return UnitDisplay switch
            {
                "long" => "kilometers per hour",
                _ => "km/h"
            };
        }

        return Unit!;
    }

    private bool TryGetLocalizedDurationUnit(double value, out string? renderedUnit)
    {
        renderedUnit = null;
        if (Unit is not ("year" or "month" or "week" or "day" or "hour" or "minute" or "second" or "millisecond"
            or "microsecond" or "nanosecond"))
            return false;

        var singular = Math.Abs(value) == 1d;
        var localeKey = Locale.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
        renderedUnit = localeKey switch
        {
            "es" => GetSpanishDurationUnit(Unit!, UnitDisplay, singular),
            _ => GetEnglishDurationUnit(Unit!, UnitDisplay, singular)
        };
        return renderedUnit is not null;
    }

    private static string? GetEnglishDurationUnit(string unit, string unitDisplay, bool singular)
    {
        return (unit, unitDisplay, singular) switch
        {
            ("year", "long", true) => "year",
            ("year", "long", false) => "years",
            ("year", "short", true) => "yr",
            ("year", "short", false) => "yrs",
            ("year", "narrow", _) => "y",
            ("month", "long", true) => "month",
            ("month", "long", false) => "months",
            ("month", "short", true) => "mth",
            ("month", "short", false) => "mths",
            ("month", "narrow", _) => "m",
            ("week", "long", true) => "week",
            ("week", "long", false) => "weeks",
            ("week", "short", true) => "wk",
            ("week", "short", false) => "wks",
            ("week", "narrow", _) => "w",
            ("day", "long" or "short", true) => "day",
            ("day", "long" or "short", false) => "days",
            ("day", "narrow", _) => "d",
            ("hour", "long", true) => "hour",
            ("hour", "long", false) => "hours",
            ("hour", "short", _) => "hr",
            ("hour", "narrow", _) => "h",
            ("minute", "long", true) => "minute",
            ("minute", "long", false) => "minutes",
            ("minute", "short", _) => "min",
            ("minute", "narrow", _) => "m",
            ("second", "long", true) => "second",
            ("second", "long", false) => "seconds",
            ("second", "short", _) => "sec",
            ("second", "narrow", _) => "s",
            ("millisecond", "long", true) => "millisecond",
            ("millisecond", "long", false) => "milliseconds",
            ("millisecond", "short", _) => "ms",
            ("millisecond", "narrow", _) => "ms",
            ("microsecond", "long", true) => "microsecond",
            ("microsecond", "long", false) => "microseconds",
            ("microsecond", "short", _) => "μs",
            ("microsecond", "narrow", _) => "μs",
            ("nanosecond", "long", true) => "nanosecond",
            ("nanosecond", "long", false) => "nanoseconds",
            ("nanosecond", "short", _) => "ns",
            ("nanosecond", "narrow", _) => "ns",
            _ => null
        };
    }

    private static string? GetSpanishDurationUnit(string unit, string unitDisplay, bool singular)
    {
        return (unit, unitDisplay, singular) switch
        {
            ("year", "long", true) => "año",
            ("year", "long", false) => "años",
            ("year", "short", _) => "a",
            ("year", "narrow", _) => "a",
            ("month", "long", true) => "mes",
            ("month", "long", false) => "meses",
            ("month", "short", _) => "m.",
            ("month", "narrow", _) => "m",
            ("week", "long", true) => "semana",
            ("week", "long", false) => "semanas",
            ("week", "short", _) => "sem.",
            ("week", "narrow", _) => "sem",
            ("day", "long", true) => "día",
            ("day", "long", false) => "días",
            ("day", "short", _) => "d",
            ("day", "narrow", _) => "d",
            ("hour", "long", true) => "hora",
            ("hour", "long", false) => "horas",
            ("hour", "short", _) => "h",
            ("hour", "narrow", _) => "h",
            ("minute", "long", true) => "minuto",
            ("minute", "long", false) => "minutos",
            ("minute", "short", _) => "min",
            ("minute", "narrow", _) => "min",
            ("second", "long", true) => "segundo",
            ("second", "long", false) => "segundos",
            ("second", "short", _) => "s",
            ("second", "narrow", _) => "s",
            ("millisecond", "long", true) => "milisegundo",
            ("millisecond", "long", false) => "milisegundos",
            ("millisecond", "short", _) => "ms",
            ("millisecond", "narrow", _) => "ms",
            ("microsecond", "long", true) => "microsegundo",
            ("microsecond", "long", false) => "microsegundos",
            ("microsecond", "short", _) => "μs",
            ("microsecond", "narrow", _) => "μs",
            ("nanosecond", "long", true) => "nanosegundo",
            ("nanosecond", "long", false) => "nanosegundos",
            ("nanosecond", "short", _) => "ns",
            ("nanosecond", "narrow", _) => "ns",
            _ => null
        };
    }

    private string GetCurrencySymbol()
    {
        if (Currency is null)
            return CultureInfo.NumberFormat.CurrencySymbol;

        return CurrencyDisplay switch
        {
            "code" => Currency,
            "name" => Currency,
            "narrowSymbol" => Currency is "USD" ? "$" : Currency,
            _ => Currency switch
            {
                "EUR" => "€",
                "USD" when Locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) => "US$",
                "USD" when Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase) => "US$",
                "USD" => "$",
                _ => Currency
            }
        };
    }

    private string GetFormattedUnitSuffix(double value)
    {
        if (string.IsNullOrEmpty(Unit))
            return string.Empty;
        if (Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return GetRenderedUnit(value);
        if (Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            return " " + GetRenderedUnit(value);
        return string.Equals(UnitDisplay, "narrow", StringComparison.Ordinal)
            ? GetRenderedUnit(value)
            : " " + GetRenderedUnit(value);
    }

    private string ApplyGrouping(string digits)
    {
        if (!ShouldApplyGrouping(digits.Length))
            return digits;

        if (Locale.StartsWith("en-IN", StringComparison.OrdinalIgnoreCase))
            return ApplyIndianGrouping(digits);

        List<string> groups = [];
        var firstGroupLength = digits.Length % 3;
        if (firstGroupLength == 0)
            firstGroupLength = 3;
        groups.Add(digits[..firstGroupLength]);
        for (var i = firstGroupLength; i < digits.Length; i += 3)
            groups.Add(digits.Substring(i, 3));

        var groupSeparator = CultureInfo.NumberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator))
            groupSeparator = ",";
        groupSeparator = OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem, groupSeparator);
        return string.Join(groupSeparator, groups);
    }

    private string ApplyIndianGrouping(string digits)
    {
        if (digits.Length <= 3)
            return digits;

        var groupSeparator = CultureInfo.NumberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator))
            groupSeparator = ",";
        groupSeparator = OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem, groupSeparator);

        var groups = new List<string>();
        var lastThree = digits[^3..];
        var leading = digits[..^3];
        while (leading.Length > 2)
        {
            groups.Insert(0, leading[^2..]);
            leading = leading[..^2];
        }

        if (leading.Length > 0)
            groups.Insert(0, leading);
        groups.Add(lastThree);
        return string.Join(groupSeparator, groups);
    }

    private bool ShouldApplyGrouping(int integerDigits)
    {
        return UseGrouping switch
        {
            "false" => false,
            "min2" => integerDigits >= 5,
            _ when string.Equals(UseGrouping, "auto", StringComparison.Ordinal) => UsesMin2GroupingForAuto()
                ? integerDigits >= 5
                : integerDigits >= 4,
            _ => integerDigits >= 4
        };
    }

    private bool UsesMin2GroupingForAuto()
    {
        var language = Locale;
        var dashIndex = language.IndexOf('-');
        if (dashIndex >= 0)
            language = language[..dashIndex];
        return Min2GroupingLanguages.Contains(language);
    }

    private bool ShouldShowSign(bool isNegative, bool isZero)
    {
        return SignDisplay switch
        {
            "never" => false,
            "always" => true,
            "exceptZero" => !isZero,
            "negative" => isNegative && !isZero,
            _ => isNegative
        };
    }

    private string ApplyNegativeSign(string value)
    {
        var negativeSign = CultureInfo.NumberFormat.NegativeSign;
        if (string.IsNullOrEmpty(negativeSign))
            negativeSign = "-";
        return negativeSign + value;
    }

    private string ApplyPositiveSign(string value)
    {
        var positiveSign = CultureInfo.NumberFormat.PositiveSign;
        if (string.IsNullOrEmpty(positiveSign))
            positiveSign = "+";
        return positiveSign + value;
    }

    private JsPlainObject CreatePart(string type, string value)
    {
        var obj = new JsPlainObject(Realm)
        {
            Prototype = Realm.ObjectPrototype
        };
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("type"), JsValue.FromString(type),
            JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("value"), JsValue.FromString(value),
            JsShapePropertyFlags.Open);
        return obj;
    }

    private JsArray CreatePartsArray(List<(string Type, string Value)> parts)
    {
        var result = Realm.CreateArrayObject();
        uint index = 0;
        foreach (var (type, value) in parts)
            result.SetElement(index++, JsValue.FromObject(CreatePart(type, value)));
        return result;
    }

    private List<(string Type, string Value)> BuildDecimalParts(double value)
    {
        var parts = new List<(string Type, string Value)>();
        var negative = IsNegative(value);
        var rounded = ApplyConfiguredRounding(Math.Abs(value), negative, out var usedSignificantDigits);
        AppendSignParts(parts, negative, rounded == 0d, false);
        AppendNumberCoreParts(parts, rounded, usedSignificantDigits);
        return parts;
    }

    private List<(string Type, string Value)> BuildPercentParts(double value)
    {
        var parts = new List<(string Type, string Value)>();
        var negative = IsNegative(value);
        var rounded = ApplyConfiguredRounding(Math.Abs(value * 100d), negative, out var usedSignificantDigits);
        AppendSignParts(parts, negative, rounded == 0d, false);
        AppendNumberCoreParts(parts, rounded, usedSignificantDigits);
        var percentSymbol = CultureInfo.NumberFormat.PercentSymbol;
        if (string.IsNullOrEmpty(percentSymbol))
            percentSymbol = "%";
        parts.Add(("percentSign", percentSymbol));
        return parts;
    }

    private List<(string Type, string Value)> BuildCurrencyParts(double value)
    {
        var parts = new List<(string Type, string Value)>();
        var inputNegative = IsNegative(value);
        var rounded = ApplyConfiguredRounding(Math.Abs(value), inputNegative, out var usedSignificantDigits);
        var isZero = rounded == 0d;
        var showAccountingParens = inputNegative &&
                                   ShouldShowSign(true, isZero) &&
                                   string.Equals(CurrencySign, "accounting", StringComparison.Ordinal) &&
                                   UsesAccountingParentheses();

        var symbol = GetCurrencySymbol();
        var suffixCurrency = UsesCurrencyAfterNumber();

        if (showAccountingParens)
            parts.Add(("literal", "("));
        else
            AppendSignParts(parts, inputNegative, isZero, false);

        if (!suffixCurrency)
            parts.Add(("currency", symbol));

        AppendNumberCoreParts(parts, rounded, usedSignificantDigits);

        if (suffixCurrency)
        {
            parts.Add(("literal", GetCurrencySpacingLiteral()));
            parts.Add(("currency", symbol));
        }

        if (showAccountingParens)
            parts.Add(("literal", ")"));

        return parts;
    }

    private List<(string Type, string Value)> BuildUnitParts(double value)
    {
        var parts = new List<(string Type, string Value)>();
        var negative = IsNegative(value);
        var rounded = ApplyConfiguredRounding(Math.Abs(value), negative, out var usedSignificantDigits);

        if (TryAppendPrefixedLongUnitParts(parts, rounded, usedSignificantDigits, negative))
            return parts;

        AppendSignParts(parts, negative, rounded == 0d, false);
        AppendNumberCoreParts(parts, rounded, usedSignificantDigits);
        var wantsSpaceBeforeUnit = !string.Equals(UnitDisplay, "narrow", StringComparison.Ordinal) ||
                                   Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        if (wantsSpaceBeforeUnit &&
            !(Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(UnitDisplay, "long", StringComparison.Ordinal)) &&
            !string.Equals(Unit, "percent", StringComparison.Ordinal))
            parts.Add(("literal", " "));
        parts.Add(("unit", GetRenderedUnit(value)));
        return parts;
    }

    private List<(string Type, string Value)> BuildCompactParts(double value)
    {
        var parts = new List<(string Type, string Value)>();
        var negative = IsNegative(value);
        var absValue = Math.Abs(value);
        var compactLocale = GetCompactLocaleKey();
        if (compactLocale == "de")
            return BuildCompactDeParts(value, absValue, negative);
        var pattern = ResolveCompactPattern(compactLocale, absValue);

        if (pattern is null)
        {
            var roundedSmall = absValue == 0d
                ? 0d
                : RoundWithMode(absValue, GetCompactSmallFractionDigits(absValue), false);
            AppendSignParts(parts, negative, roundedSmall == 0d, false);
            AppendCompactNumericParts(parts,
                FormatCompactNumber(roundedSmall, GetCompactSmallFractionDigits(absValue)));
            return parts;
        }

        var compactValue = absValue / pattern.Value.divisor;
        var maxFractionDigits = compactValue >= 100 ? 0 : compactValue >= 10 ? 0 : 1;
        var rounded = RoundWithMode(compactValue, maxFractionDigits, false);
        AppendSignParts(parts, negative, rounded == 0d, false);
        AppendCompactNumericParts(parts, FormatCompactNumber(rounded, maxFractionDigits));
        var isLong = string.Equals(CompactDisplay, "long", StringComparison.Ordinal);
        var suffix = isLong ? pattern.Value.longSuffix : pattern.Value.shortSuffix;
        var addSpace = isLong ? pattern.Value.longSpace : pattern.Value.shortSpace;
        if (addSpace)
            parts.Add(("literal", GetCompactSeparator(compactLocale, isLong)));
        parts.Add(("compact", suffix));
        return parts;
    }

    private List<(string Type, string Value)> BuildCompactDeParts(double value, double absValue, bool negative)
    {
        var parts = new List<(string Type, string Value)>();
        var isLong = string.Equals(CompactDisplay, "long", StringComparison.Ordinal);

        if (absValue >= 1_000_000d)
        {
            AppendSignParts(parts, negative, false, false);
            AppendCompactNumericParts(parts, FormatCompactDeScaled(absValue / 1_000_000d));
            parts.Add(("literal", isLong ? " " : "\u00A0"));
            parts.Add(("compact", isLong ? "Millionen" : "Mio."));
            return parts;
        }

        if (isLong && absValue >= 1_000d)
        {
            AppendSignParts(parts, negative, false, false);
            AppendCompactNumericParts(parts, FormatCompactDeScaled(absValue / 1_000d));
            parts.Add(("literal", " "));
            parts.Add(("compact", "Tausend"));
            return parts;
        }

        var formatted = FormatCompactDeShortSmall(absValue);
        AppendSignParts(parts, negative, formatted == "0", false);
        AppendFormattedDecimalParts(parts, formatted);
        return parts;
    }

    private List<(string Type, string Value)> BuildExponentParts(double value, bool engineering)
    {
        var formatted = engineering ? FormatEngineering(value) : FormatScientific(value);
        var parts = new List<(string Type, string Value)>();

        var negativeSign = string.IsNullOrEmpty(CultureInfo.NumberFormat.NegativeSign)
            ? "-"
            : CultureInfo.NumberFormat.NegativeSign;
        if (formatted.StartsWith(negativeSign, StringComparison.Ordinal))
        {
            parts.Add(("minusSign", negativeSign));
            formatted = formatted[negativeSign.Length..];
        }

        var separatorIndex = formatted.IndexOf('E');
        if (separatorIndex < 0)
        {
            AppendCompactNumericParts(parts, formatted);
            return parts;
        }

        AppendCompactNumericParts(parts, formatted[..separatorIndex]);
        parts.Add(("exponentSeparator", "E"));
        var exponent = formatted[(separatorIndex + 1)..];
        if (exponent.StartsWith("-", StringComparison.Ordinal))
        {
            parts.Add(("exponentMinusSign", "-"));
            exponent = exponent[1..];
        }
        else if (exponent.StartsWith("+", StringComparison.Ordinal))
        {
            parts.Add(("exponentPlusSign", "+"));
            exponent = exponent[1..];
        }

        parts.Add(("exponentInteger", exponent));
        return parts;
    }

    private void AppendCompactNumericParts(List<(string Type, string Value)> parts, string formatted)
    {
        var decimalSeparator =
            OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
                CultureInfo.NumberFormat.NumberDecimalSeparator);
        var dotIndex = formatted.IndexOf(decimalSeparator, StringComparison.Ordinal);
        if (dotIndex < 0)
        {
            parts.Add(("integer", formatted));
            return;
        }

        var integerPart = formatted[..dotIndex];
        var fractionPart = formatted[(dotIndex + decimalSeparator.Length)..];
        parts.Add(("integer", integerPart));
        parts.Add(("decimal", decimalSeparator));
        if (fractionPart.Length > 0)
            parts.Add(("fraction", fractionPart));
    }

    private void AppendFormattedDecimalParts(List<(string Type, string Value)> parts, string formatted)
    {
        var decimalSeparator =
            OkojoIntlNumberingSystemData.GetDecimalSeparator(NumberingSystem,
                CultureInfo.NumberFormat.NumberDecimalSeparator);
        var groupSeparator = OkojoIntlNumberingSystemData.GetGroupSeparator(NumberingSystem,
            string.IsNullOrEmpty(CultureInfo.NumberFormat.NumberGroupSeparator)
                ? ","
                : CultureInfo.NumberFormat.NumberGroupSeparator);

        var decimalIndex = formatted.IndexOf(decimalSeparator, StringComparison.Ordinal);
        var integerPortion = decimalIndex >= 0 ? formatted[..decimalIndex] : formatted;
        var fractionPart = decimalIndex >= 0 ? formatted[(decimalIndex + decimalSeparator.Length)..] : string.Empty;

        var groups = integerPortion.Split(groupSeparator);
        for (var i = 0; i < groups.Length; i++)
        {
            parts.Add(("integer", groups[i]));
            if (i + 1 < groups.Length)
                parts.Add(("group", groupSeparator));
        }

        if (fractionPart.Length > 0)
        {
            parts.Add(("decimal", decimalSeparator));
            parts.Add(("fraction", fractionPart));
        }
    }

    private int GetCompactSmallFractionDigits(double absValue)
    {
        if (absValue == 0d)
            return 0;
        if (absValue >= 100d)
            return 0;
        if (absValue >= 10d)
            return 0;
        if (absValue >= 1d)
            return 1;

        var magnitude = (int)Math.Floor(Math.Log10(absValue));
        return 1 - magnitude;
    }

    private string GetCompactLocaleKey()
    {
        if (Locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase))
            return "zh-TW";
        if (Locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return "ja";
        if (Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return "ko";
        if (Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            return "de";
        if (Locale.StartsWith("en-IN", StringComparison.OrdinalIgnoreCase))
            return "en-IN";
        return "en";
    }

    private static string GetCompactSeparator(string compactLocale, bool isLong)
    {
        if (compactLocale == "de" && !isLong)
            return "\u00A0";
        return " ";
    }

    private bool UsesAccountingParentheses()
    {
        return Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
               Locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
               Locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ||
               Locale.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase);
    }

    private bool UsesCurrencyAfterNumber()
    {
        return Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase) ||
               Locale.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
    }

    private string GetCurrencySpacingLiteral()
    {
        return Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase) ||
               Locale.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? "\u00A0"
            : " ";
    }

    private string GetPercentSpacingLiteral()
    {
        return Locale.StartsWith("de", StringComparison.OrdinalIgnoreCase) ||
               Locale.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? "\u00A0"
            : " ";
    }

    private static string JoinParts(List<(string Type, string Value)> parts)
    {
        var builder = new StringBuilder();
        foreach (var (_, value) in parts)
            builder.Append(value);
        return builder.ToString();
    }

    private readonly struct ExactDecimalValue
    {
        internal ExactDecimalValue(BigInteger signedUnscaled, int scale, bool isNegativeZero)
        {
            SignedUnscaled = signedUnscaled;
            Scale = scale;
            IsNegativeZero = isNegativeZero;
        }

        internal BigInteger SignedUnscaled { get; }
        internal int Scale { get; }
        internal bool IsNegativeZero { get; }
        internal bool IsNegative => SignedUnscaled.Sign < 0 || (SignedUnscaled.IsZero && IsNegativeZero);
        internal bool IsZero => SignedUnscaled.IsZero;

        internal ExactDecimalValue Abs()
        {
            return SignedUnscaled.Sign < 0 || IsNegativeZero
                ? new(BigInteger.Abs(SignedUnscaled), Scale, false)
                : this;
        }
    }
}
