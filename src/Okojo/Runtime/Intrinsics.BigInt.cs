using System.Numerics;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateBigIntConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "BigInt is not a constructor");

            var value = args.Length == 0 ? JsValue.Undefined : args[0];
            var primitive = realm.ToPrimitiveSlowPath(value, false);
            if (primitive.IsNumber)
                return JsValue.FromBigInt(NumberToBigInt(primitive.NumberValue));
            return JsValue.FromBigInt(ToBigIntPrimitive(realm, primitive));
        }, "BigInt", 1, true);
    }

    private void InstallBigIntConstructorBuiltins()
    {
        const int atomAsIntN = IdAsIntN;
        const int atomAsUintN = IdAsUintN;

        var asIntNFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var bits = realm.ToIndexForBigInt(BigIntArgValue(args, 0));
            var bigint = ToBigIntValue(realm, BigIntArgValue(args, 1)).Value;
            return JsValue.FromBigInt(new(BigIntAsIntN(bits, bigint)));
        }, "asIntN", 2);

        var asUintNFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var bits = realm.ToIndexForBigInt(BigIntArgValue(args, 0));
            var bigint = ToBigIntValue(realm, BigIntArgValue(args, 1)).Value;
            return JsValue.FromBigInt(new(BigIntAsUintN(bits, bigint)));
        }, "asUintN", 2);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(atomAsIntN, JsValue.FromObject(asIntNFn)),
            PropertyDefinition.Mutable(atomAsUintN, JsValue.FromObject(asUintNFn))
        ];
        BigIntConstructor.InitializePrototypeProperty(BigIntPrototype);
        BigIntConstructor.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private void InstallBigIntPrototypeBuiltins()
    {
        const int atomToLocaleString = IdToLocaleString;

        var toStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var bigint = ThisBigIntValue(realm, thisValue).Value;
            var radix = 10;
            if (args.Length != 0 && !args[0].IsUndefined)
            {
                var radixNum = realm.ToIntegerOrInfinity(args[0]);
                if (double.IsInfinity(radixNum) || radixNum < 2d || radixNum > 36d)
                    throw new JsRuntimeException(JsErrorKind.RangeError,
                        "toString() radix argument must be between 2 and 36");
                radix = (int)radixNum;
            }

            return BigIntToString(bigint, radix);
        }, "toString", 0);

        var toLocaleStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var bigint = ThisBigIntValue(realm, thisValue).Value;
            if (!realm.GlobalObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("Intl"), out var intlValue,
                    out _) ||
                !intlValue.TryGetObject(out var intlObject) ||
                !intlObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("NumberFormat"), out var ctorValue,
                    out _) ||
                !ctorValue.TryGetObject(out var ctorObject) ||
                ctorObject is not JsFunction ctorFn)
                return BigIntToString(bigint, 10);

            var locales = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            var options = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            var numberFormatValue =
                realm.ConstructWithExplicitNewTarget(ctorFn, [locales, options], JsValue.FromObject(ctorFn), -1);
            if (numberFormatValue.TryGetObject(out var numberFormatObject) &&
                numberFormatObject is JsNumberFormatObject numberFormat)
            {
                var exactValue = JsValue.FromBigInt(new(bigint));
                if (numberFormat.TryFormatExactValue(exactValue, out var exact))
                    return JsValue.FromString(exact);
                return JsValue.FromString(numberFormat.Format((double)bigint));
            }

            return BigIntToString(bigint, 10);
        }, "toLocaleString", 0);

        var valueOfFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return JsValue.FromBigInt(ThisBigIntValue(realm, thisValue));
            },
            "valueOf", 0);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(BigIntConstructor)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn)),
            PropertyDefinition.Mutable(atomToLocaleString, JsValue.FromObject(toLocaleStringFn)),
            PropertyDefinition.Mutable(IdValueOf, JsValue.FromObject(valueOfFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("BigInt"), configurable: true)
        ];
        BigIntPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }

    internal static JsBigInt ThisBigIntValue(JsRealm realm, in JsValue value)
    {
        if (value.IsBigInt)
            return value.AsBigInt();
        if (value.TryGetObject(out var obj) && obj is JsBigIntObject boxed)
            return boxed.Value;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "BigInt.prototype method requires that 'this' be a BigInt");
    }

    internal static JsBigInt ToBigIntValue(JsRealm realm, in JsValue value)
    {
        return ToBigIntPrimitive(realm, realm.ToPrimitiveSlowPath(value, false));
    }

    internal static JsBigInt ToBigIntPrimitive(JsRealm realm, in JsValue value)
    {
        if (value.IsBigInt)
            return value.AsBigInt();
        if (value.IsBool)
            return new(value.IsTrue ? BigInteger.One : BigInteger.Zero);
        if (value.IsString)
            return ParseBigIntString(value.AsString());
        if (value.IsObject)
            return ToBigIntPrimitive(realm, realm.ToPrimitiveSlowPath(value, false));
        throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert value to a BigInt");
    }

    internal static JsBigInt NumberToBigInt(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Truncate(number))
            throw new JsRuntimeException(JsErrorKind.RangeError, "The number is not an integer");
        return new(new(number));
    }

    internal static bool TryParseBigIntString(string text, out JsBigInt value)
    {
        try
        {
            value = ParseBigIntString(text);
            return true;
        }
        catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.SyntaxError)
        {
            value = default!;
            return false;
        }
    }

    internal static JsBigInt ParseBigIntString(string text)
    {
        var span = text.AsSpan().Trim();
        if (span.Length == 0)
            return new(BigInteger.Zero);

        var negative = false;
        var sawExplicitSign = false;
        if (span[0] == '+' || span[0] == '-')
        {
            sawExplicitSign = true;
            negative = span[0] == '-';
            span = span[1..];
            if (span.Length == 0)
                throw new JsRuntimeException(JsErrorKind.SyntaxError, "Cannot convert string to BigInt");
        }

        var radix = 10;
        if (span.Length >= 2 && span[0] == '0')
            switch (span[1])
            {
                case 'x':
                case 'X':
                    if (sawExplicitSign)
                        throw new JsRuntimeException(JsErrorKind.SyntaxError, "Cannot convert string to BigInt");
                    radix = 16;
                    span = span[2..];
                    break;
                case 'o':
                case 'O':
                    if (sawExplicitSign)
                        throw new JsRuntimeException(JsErrorKind.SyntaxError, "Cannot convert string to BigInt");
                    radix = 8;
                    span = span[2..];
                    break;
                case 'b':
                case 'B':
                    if (sawExplicitSign)
                        throw new JsRuntimeException(JsErrorKind.SyntaxError, "Cannot convert string to BigInt");
                    radix = 2;
                    span = span[2..];
                    break;
            }

        if (span.Length == 0)
            throw new JsRuntimeException(JsErrorKind.SyntaxError, "Cannot convert string to BigInt");

        var result = BigInteger.Zero;
        for (var i = 0; i < span.Length; i++)
        {
            var digit = span[i] switch
            {
                >= '0' and <= '9' => span[i] - '0',
                >= 'a' and <= 'z' => span[i] - 'a' + 10,
                >= 'A' and <= 'Z' => span[i] - 'A' + 10,
                _ => -1
            };
            if (digit < 0 || digit >= radix)
                throw new JsRuntimeException(JsErrorKind.SyntaxError, "Cannot convert string to BigInt");
            result = result * radix + digit;
        }

        return new(negative ? -result : result);
    }


    internal static string BigIntToString(BigInteger value, int radix)
    {
        if (radix == 10)
            return value.ToString();

        if (value.IsZero)
            return "0";

        var negative = value.Sign < 0;
        var remaining = BigInteger.Abs(value);
        Span<char> scratch = stackalloc char[128];
        char[]? rented = null;
        var index = scratch.Length;

        while (remaining > BigInteger.Zero)
        {
            remaining = BigInteger.DivRem(remaining, radix, out var rem);
            var digit = (int)rem;
            if (index == 0)
            {
                rented ??= new char[scratch.Length * 2];
                scratch.CopyTo(rented.AsSpan(rented.Length - scratch.Length));
                index = rented.Length - scratch.Length;
                scratch = rented;
            }

            scratch[--index] = (char)(digit < 10 ? '0' + digit : 'a' + digit - 10);
        }

        if (negative)
            scratch[--index] = '-';
        return new(scratch[index..]);
    }

    internal static BigInteger BigIntAsUintN(ulong bits, BigInteger bigint)
    {
        if (bits == 0)
            return BigInteger.Zero;
        if (bits >= int.MaxValue)
            return bigint;
        var modulo = BigInteger.One << (int)bits;
        var result = bigint % modulo;
        if (result.Sign < 0)
            result += modulo;
        return result;
    }

    internal static BigInteger BigIntAsIntN(ulong bits, BigInteger bigint)
    {
        if (bits == 0)
            return BigInteger.Zero;
        if (bits >= int.MaxValue)
            return bigint;
        var modulo = BigInteger.One << (int)bits;
        var result = bigint % modulo;
        if (result.Sign < 0)
            result += modulo;
        var signedThreshold = BigInteger.One << ((int)bits - 1);
        if (result >= signedThreshold)
            result -= modulo;
        return result;
    }

    private static JsValue BigIntArgValue(ReadOnlySpan<JsValue> args, int index)
    {
        return index < args.Length ? args[index] : JsValue.Undefined;
    }
}
