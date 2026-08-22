using System.Globalization;
using System.Numerics;
using Okojo.Globalization;

namespace Okojo.JavaScript.Execution;

internal sealed class JsNumberFormatObject : JsObject
{
    private readonly NumberFormat core;

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
        CultureInfo cultureInfo
    )
        : base(realm)
    {
        Prototype = prototype;
        core = new(
            locale,
            numberingSystem,
            style,
            currency,
            currencyDisplay,
            currencySign,
            unit,
            unitDisplay,
            notation,
            compactDisplay,
            minimumIntegerDigits,
            minimumFractionDigits,
            maximumFractionDigits,
            minimumSignificantDigits,
            maximumSignificantDigits,
            minimumSignificantDigitsExplicit,
            maximumSignificantDigitsExplicit,
            useGrouping,
            signDisplay,
            roundingMode,
            roundingPriority,
            roundingIncrement,
            trailingZeroDisplay,
            cultureInfo
        );
    }

    internal string Locale => core.Locale;
    internal string NumberingSystem => core.NumberingSystem;
    internal string Style => core.Style;
    internal string? Currency => core.Currency;
    internal string CurrencyDisplay => core.CurrencyDisplay;
    internal string CurrencySign => core.CurrencySign;
    internal string? Unit => core.Unit;
    internal string UnitDisplay => core.UnitDisplay;
    internal string Notation => core.Notation;
    internal string CompactDisplay => core.CompactDisplay;
    internal int MinimumIntegerDigits => core.MinimumIntegerDigits;
    internal int MinimumFractionDigits => core.MinimumFractionDigits;
    internal int MaximumFractionDigits => core.MaximumFractionDigits;
    internal int? MinimumSignificantDigits => core.MinimumSignificantDigits;
    internal int? MaximumSignificantDigits => core.MaximumSignificantDigits;
    internal bool MinimumSignificantDigitsExplicit => core.MinimumSignificantDigitsExplicit;
    internal bool MaximumSignificantDigitsExplicit => core.MaximumSignificantDigitsExplicit;
    internal string UseGrouping => core.UseGrouping;
    internal string SignDisplay => core.SignDisplay;
    internal string RoundingMode => core.RoundingMode;
    internal string RoundingPriority => core.RoundingPriority;
    internal int RoundingIncrement => core.RoundingIncrement;
    internal string TrailingZeroDisplay => core.TrailingZeroDisplay;
    internal CultureInfo CultureInfo => core.CultureInfo;

    internal bool SupportsExactIntegralFormatting => core.SupportsExactIntegralFormatting;

    internal string Format(double value)
    {
        return core.Format(value);
    }

    internal bool TryFormatExactValue(in JsValue value, out string formatted)
    {
        if (!TryToExactRawString(value, out var raw))
        {
            formatted = string.Empty;
            return false;
        }

        return core.TryFormatExactString(raw, out formatted);
    }

    internal string FormatExactIntegralString(string raw)
    {
        return core.FormatExactIntegralString(raw);
    }

    internal JsArray FormatToParts(double value)
    {
        List<IntlPart> parts;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            parts = core.FormatParts(value);
        }
        else if (
            TryToExactRawString(new(value), out var raw)
            && core.TryFormatExactParts(raw, out var exactParts)
        )
        {
            parts = exactParts;
        }
        else
        {
            parts = core.FormatParts(value);
        }

        var result = Realm.CreateArrayObject();
        uint index = 0;
        foreach (var part in parts)
            result.SetElement(index++, JsValue.FromObject(CreatePart(part.Type, part.Value)));
        return result;
    }

    private JsPlainObject CreatePart(string type, string value)
    {
        var obj = new JsPlainObject(Realm) { Prototype = Realm.ObjectPrototype };
        obj.DefineDataPropertyAtom(
            Realm,
            Realm.Atoms.InternNoCheck("type"),
            JsValue.FromString(type),
            JsShapePropertyFlags.Open
        );
        obj.DefineDataPropertyAtom(
            Realm,
            Realm.Atoms.InternNoCheck("value"),
            JsValue.FromString(value),
            JsShapePropertyFlags.Open
        );
        return obj;
    }

    private static bool TryToExactRawString(in JsValue value, out string raw)
    {
        if (value.IsNumber)
        {
            var number = value.NumberValue;
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                raw = string.Empty;
                return false;
            }

            var negativeZero = number == 0d && double.IsNegativeInfinity(1d / number);
            raw = negativeZero ? "-0" : number.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        if (value.IsBigInt)
        {
            raw = value.AsBigInt().Value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (!value.IsString)
        {
            raw = string.Empty;
            return false;
        }

        raw = value.AsString();
        return true;
    }
}
