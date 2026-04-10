using System.Globalization;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallNumberPrototypeBuiltins()
    {
        const int atomToLocaleString = IdToLocaleString;
        const int atomToFixed = IdToFixed;
        const int atomToExponential = IdToExponential;
        const int atomToPrecision = IdToPrecision;
        var valueOfFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var thisValue = info.ThisValue;
                return new(GetThisNumberValue(thisValue, "valueOf"));
            }, "valueOf", 0);

        var toStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                var value = GetThisNumberValue(thisValue, "toString");
                var radixNumber = args.Length == 0 || args[0].IsUndefined
                    ? 10
                    : realm.ToIntegerOrInfinity(args[0]);
                if (double.IsInfinity(radixNumber))
                    throw new JsRuntimeException(JsErrorKind.RangeError, "radix must be between 2 and 36");
                var radix = (int)radixNumber;
                if (radix < 2 || radix > 36)
                    throw new JsRuntimeException(JsErrorKind.RangeError, "radix must be between 2 and 36");
                return NumberToString(value, radix);
            }, "toString", 1);
        var toLocaleStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var number = GetThisNumberValue(thisValue, "toLocaleString");
                if (!realm.GlobalObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("Intl"), out var intlValue,
                        out _) ||
                    !intlValue.TryGetObject(out var intlObject) ||
                    !intlObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("NumberFormat"), out var ctorValue,
                        out _) ||
                    !ctorValue.TryGetObject(out var ctorObject) ||
                    ctorObject is not JsFunction ctorFn)
                    return JsNumberFormatting.ToJsString(number);

                var locales = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
                var options = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
                var numberFormatValue =
                    realm.ConstructWithExplicitNewTarget(ctorFn, [locales, options], JsValue.FromObject(ctorFn), -1);
                if (numberFormatValue.TryGetObject(out var numberFormatObject) &&
                    numberFormatObject is JsNumberFormatObject numberFormat)
                {
                    JsValue exactValue = new(number);
                    if (numberFormat.TryFormatExactValue(exactValue, out var exact))
                        return JsValue.FromString(exact);
                    return JsValue.FromString(numberFormat.Format(number));
                }

                return JsNumberFormatting.ToJsString(number);
            }, "toLocaleString", 0);
        var toFixedFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                var value = GetThisNumberValue(thisValue, "toFixed");
                var digits = GetDigitsArgument(realm, args, "toFixed", true, 0, 100);
                if (double.IsNaN(value))
                    return "NaN";
                if (double.IsPositiveInfinity(value))
                    return "Infinity";
                if (double.IsNegativeInfinity(value))
                    return "-Infinity";
                if (Math.Abs(value) >= 1e21)
                    return JsNumberFormatting.ToJsString(value);
                return value.ToString($"F{digits}", CultureInfo.InvariantCulture);
            }, "toFixed", 1);
        var toExponentialFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                var value = GetThisNumberValue(thisValue, "toExponential");
                var fractionDigitsUndefined = args.Length == 0 || args[0].IsUndefined;
                var digitsNumber = fractionDigitsUndefined ? 0d : realm.ToIntegerOrInfinity(args[0]);

                if (double.IsNaN(value))
                    return "NaN";
                if (double.IsPositiveInfinity(value))
                    return "Infinity";
                if (double.IsNegativeInfinity(value))
                    return "-Infinity";

                if (double.IsInfinity(digitsNumber) || digitsNumber < 0 || digitsNumber > 100)
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        "toExponential() argument must be between 0 and 100");

                var digits = (int)digitsNumber;
                if (value == 0d)
                {
                    if (fractionDigitsUndefined || digits == 0)
                        return "0e+0";
                    return "0." + new string('0', digits) + "e+0";
                }

                if (fractionDigitsUndefined)
                    return FormatShortestExponential(value);

                return JsNumberPrecisionFormatting.FormatExponential(value, digits);
            }, "toExponential", 1);
        var toPrecisionFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                var value = GetThisNumberValue(thisValue, "toPrecision");
                if (args.Length == 0 || args[0].IsUndefined)
                    return JsNumberFormatting.ToJsString(value);
                var precisionNumber = realm.ToIntegerOrInfinity(args[0]);
                if (double.IsNaN(value))
                    return "NaN";
                if (double.IsPositiveInfinity(value))
                    return "Infinity";
                if (double.IsNegativeInfinity(value))
                    return "-Infinity";
                if (double.IsInfinity(precisionNumber) || precisionNumber < 1 || precisionNumber > 100)
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        "toPrecision() argument must be between 1 and 100");

                var precision = (int)precisionNumber;
                if (value == 0d)
                {
                    if (precision == 1)
                        return "0";
                    return "0." + new string('0', precision - 1);
                }

                return JsNumberPrecisionFormatting.FormatPrecision(value, precision);
            }, "toPrecision", 1);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(NumberConstructor)),
            PropertyDefinition.Mutable(IdValueOf, JsValue.FromObject(valueOfFn)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn)),
            PropertyDefinition.Mutable(atomToLocaleString, JsValue.FromObject(toLocaleStringFn)),
            PropertyDefinition.Mutable(atomToFixed, JsValue.FromObject(toFixedFn)),
            PropertyDefinition.Mutable(atomToExponential, JsValue.FromObject(toExponentialFn)),
            PropertyDefinition.Mutable(atomToPrecision, JsValue.FromObject(toPrecisionFn))
        ];
        NumberPrototype.DefineNewPropertiesNoCollision(Realm, defs);

        const int atomNaN = IdNaN;
        const int atomPosInf = IdPositiveInfinity;
        const int atomNegInf = IdNegativeInfinity;
        const int atomMaxValue = IdMaxValue;
        const int atomMinValue = IdMinValue;
        const int atomMaxSafeInteger = IdMaxSafeInteger;
        const int atomMinSafeInteger = IdMinSafeInteger;
        const int atomEpsilon = IdEpsilon;
        const int atomIsFinite = IdIsFinite;
        const int atomIsInteger = IdIsInteger;
        const int atomIsNaN = IdIsNaN;
        const int atomIsSafeInteger = IdIsSafeInteger;
        var numberIsFiniteFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var args = info.Arguments;
                if (args.Length == 0 || !args[0].IsNumber)
                    return JsValue.False;
                var number = args[0].NumberValue;
                return !double.IsNaN(number) && !double.IsInfinity(number) ? JsValue.True : JsValue.False;
            }, "isFinite", 1);
        var numberIsNaNFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var args = info.Arguments;
                return args.Length != 0 && args[0].IsNumber && double.IsNaN(args[0].NumberValue)
                    ? JsValue.True
                    : JsValue.False;
            }, "isNaN", 1);
        var numberIsIntegerFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var args = info.Arguments;
                if (args.Length == 0 || !args[0].IsNumber)
                    return JsValue.False;
                var number = args[0].NumberValue;
                return !double.IsNaN(number) && !double.IsInfinity(number) && number == Math.Truncate(number)
                    ? JsValue.True
                    : JsValue.False;
            }, "isInteger", 1);
        var numberIsSafeIntegerFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var args = info.Arguments;
                if (args.Length == 0 || !args[0].IsNumber)
                    return JsValue.False;
                var number = args[0].NumberValue;
                return !double.IsNaN(number) &&
                       !double.IsInfinity(number) &&
                       number == Math.Truncate(number) &&
                       Math.Abs(number) <= 9007199254740991d
                    ? JsValue.True
                    : JsValue.False;
            }, "isSafeInteger", 1);
        Span<PropertyDefinition> ctorDefs =
        [
            PropertyDefinition.Const(atomNaN, new(double.NaN)),
            PropertyDefinition.Const(atomPosInf, new(double.PositiveInfinity)),
            PropertyDefinition.Const(atomNegInf, new(double.NegativeInfinity)),
            PropertyDefinition.Const(atomMaxValue, new(double.MaxValue)),
            PropertyDefinition.Const(atomMinValue, new(double.Epsilon)),
            PropertyDefinition.Const(atomEpsilon, new(2.220446049250313e-16d)),
            PropertyDefinition.Const(atomMaxSafeInteger, new(9007199254740991d)),
            PropertyDefinition.Const(atomMinSafeInteger, new(-9007199254740991d)),
            PropertyDefinition.Mutable(atomIsFinite, JsValue.FromObject(numberIsFiniteFn)),
            PropertyDefinition.Mutable(atomIsInteger, JsValue.FromObject(numberIsIntegerFn)),
            PropertyDefinition.Mutable(atomIsNaN, JsValue.FromObject(numberIsNaNFn)),
            PropertyDefinition.Mutable(atomIsSafeInteger, JsValue.FromObject(numberIsSafeIntegerFn))
        ];
        NumberConstructor.InitializePrototypeProperty(NumberPrototype);
        NumberConstructor.DefineNewPropertiesNoCollision(Realm, ctorDefs);
    }

    private static double GetThisNumberValue(JsValue thisValue, string methodName)
    {
        if (thisValue.IsNumber)
            return thisValue.NumberValue;
        if (thisValue.TryGetObject(out var obj) && obj is JsNumberObject boxed)
            return boxed.Value;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            $"Number.prototype.{methodName} requires that 'this' be a Number");
    }

    private static int GetDigitsArgument(JsRealm realm, ReadOnlySpan<JsValue> args, string methodName,
        bool allowUndefined, int min, int max)
    {
        if (args.Length == 0 || args[0].IsUndefined)
        {
            if (allowUndefined)
                return 0;
            throw new JsRuntimeException(JsErrorKind.RangeError,
                $"Number.prototype.{methodName} argument is out of range");
        }

        var number = realm.ToIntegerOrInfinity(args[0]);
        if (double.IsInfinity(number) || number < min || number > max)
            throw new JsRuntimeException(JsErrorKind.RangeError,
                methodName switch
                {
                    "toExponential" => "toExponential() argument must be between 0 and 100",
                    "toFixed" => "toFixed() digits argument must be between 0 and 100",
                    "toPrecision" => "toPrecision() argument must be between 1 and 100",
                    _ => $"Number.prototype.{methodName} argument is out of range"
                });

        return (int)number;
    }

    private static string NormalizeExponentialString(string text)
    {
        var lower = text.ToLowerInvariant();
        var eIndex = lower.LastIndexOf('e');
        if (eIndex < 0 || eIndex + 2 >= lower.Length)
            return lower;

        var sign = lower[eIndex + 1];
        if (sign is not ('+' or '-'))
            return lower;

        var digitsStart = eIndex + 2;
        var firstNonZero = digitsStart;
        while (firstNonZero < lower.Length && lower[firstNonZero] == '0')
            firstNonZero++;

        if (firstNonZero == digitsStart)
            return lower;

        if (firstNonZero == lower.Length)
            return lower[..digitsStart] + "0";

        return string.Concat(lower.AsSpan(0, digitsStart), lower.AsSpan(firstNonZero));
    }

    private static string FormatShortestExponential(double value)
    {
        var formatted = NormalizeExponentialString(value.ToString("E15", CultureInfo.InvariantCulture));
        var eIndex = formatted.IndexOf('e');
        if (eIndex < 0)
            return formatted;

        var mantissa = formatted[..eIndex];
        var exponent = formatted[eIndex..];
        if (mantissa.Contains('.'))
        {
            mantissa = mantissa.TrimEnd('0');
            if (mantissa.EndsWith('.'))
                mantissa = mantissa[..^1];
        }

        return mantissa + exponent;
    }

    private static string NumberToString(double value, int radix)
    {
        if (double.IsNaN(value))
            return "NaN";
        if (value == 0d)
            return "0";
        if (double.IsPositiveInfinity(value))
            return "Infinity";
        if (double.IsNegativeInfinity(value))
            return "-Infinity";
        if (radix == 10)
            return JsNumberFormatting.ToJsString(value);
        if (value < 0d)
            return "-" + NumberToString(-value, radix);

        var integer = (long)value;
        var fraction = value - integer;
        var result = ToBase(integer, radix);
        if (fraction != 0d)
            result += "." + ToFractionBase(fraction, radix);
        return result;
    }

    private static string ToBase(long value, int radix)
    {
        if (value == 0)
            return "0";

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> buffer = stackalloc char[65];
        var cursor = buffer.Length;
        var current = value;
        while (current > 0)
        {
            var digit = current % radix;
            current /= radix;
            buffer[--cursor] = digits[(int)digit];
        }

        return new(buffer[cursor..]);
    }

    private static string ToFractionBase(double fraction, int radix)
    {
        if (fraction == 0d)
            return "0";

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> buffer = stackalloc char[64];
        var length = 0;
        var current = fraction;
        while (current > 0d && length < 50)
        {
            var scaled = current * radix;
            var digit = (int)scaled;
            current = scaled - digit;
            buffer[length++] = digits[digit];
        }

        return new(buffer[..length]);
    }
}
