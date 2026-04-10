using System.Numerics;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallMathBuiltins()
    {
        // Math is installed as a plain object with host-function properties.
    }

    private JsPlainObject CreateMathObject()
    {
        var math = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };

        var absFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Abs(MathArg(realm, args, 0)));
            }, "abs", 1);
        var acosFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Acos(MathArg(realm, args, 0)));
            }, "acos", 1);
        var acoshFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Acosh(MathArg(realm, args, 0)));
            }, "acosh", 1);
        var asinFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Asin(MathArg(realm, args, 0)));
            }, "asin", 1);
        var asinhFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Asinh(MathArg(realm, args, 0)));
            }, "asinh", 1);
        var atanFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Atan(MathArg(realm, args, 0)));
            }, "atan", 1);
        var atan2Fn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Atan2(MathArg(realm, args, 0), MathArg(realm, args, 1)));
            },
            "atan2", 2);
        var atanhFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Atanh(MathArg(realm, args, 0)));
            }, "atanh", 1);
        var cbrtFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Cbrt(MathArg(realm, args, 0)));
            }, "cbrt", 1);
        var ceilFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Ceiling(MathArg(realm, args, 0)));
            }, "ceil", 1);
        var clz32Fn = new JsHostFunction(Realm, (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return JsValue.FromInt32(Clz32(realm, args));
            },
            "clz32", 1);
        var cosFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Cos(MathArg(realm, args, 0)));
            }, "cos", 1);
        var coshFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Cosh(MathArg(realm, args, 0)));
            }, "cosh", 1);
        var expFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Exp(MathArg(realm, args, 0)));
            }, "exp", 1);
        var expm1Fn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var x = MathArg(realm, args, 0);
                if (x == 0) return new(x);
                return new(Math.Exp(x) - 1d);
            }, "expm1", 1);
        var f16RoundFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new((double)(Half)MathArg(realm, args, 0));
            }, "f16round", 1);
        var floorFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Floor(MathArg(realm, args, 0)));
            }, "floor", 1);
        var froundFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new((float)MathArg(realm, args, 0));
            }, "fround", 1);
        var hypotFn = new JsHostFunction(Realm, (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Hypot(realm, args));
            }, "hypot",
            2);
        var imulFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return JsValue.FromInt32(
                    unchecked((int)(ToUint32(realm, ArgValue(args, 0)) * ToUint32(realm, ArgValue(args, 1)))));
            }, "imul",
            2);
        var logFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Log(MathArg(realm, args, 0)));
            }, "log", 1);
        var log1PFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var x = MathArg(realm, args, 0);
                if (x == 0) return new(x);
                return new(Math.Log(1d + x));
            }, "log1p", 1);
        var log2Fn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Log2(MathArg(realm, args, 0)));
            }, "log2", 1);
        var log10Fn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Log10(MathArg(realm, args, 0)));
            }, "log10", 1);
        var maxFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            return new(Max(realm, args));
        }, "max", 2);
        var minFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            return new(Min(realm, args));
        }, "min", 2);
        var powFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var baseValue = MathArg(realm, args, 0);
                var exponentValue = MathArg(realm, args, 1);
                if (exponentValue == 0)
                    return new(1d);
                if (baseValue is 1 or -1 && double.IsInfinity(exponentValue)) return new(double.NaN);
                return new(Math.Pow(baseValue, exponentValue));
            },
            "pow", 2);
        var randomFn = new JsHostFunction(Realm, (in info) => { return new(Random.Shared.NextDouble()); },
            "random", 0);
        var roundFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(RoundJs(MathArg(realm, args, 0)));
            }, "round", 1);
        var signFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(SignJs(MathArg(realm, args, 0)));
            }, "sign", 1);
        var sinFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Sin(MathArg(realm, args, 0)));
            }, "sin", 1);
        var sinhFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Sinh(MathArg(realm, args, 0)));
            }, "sinh", 1);
        var sqrtFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Sqrt(MathArg(realm, args, 0)));
            }, "sqrt", 1);
        var sumPreciseFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(SumPrecise(realm, ArgValue(args, 0)));
            }, "sumPrecise", 1);
        var tanFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Tan(MathArg(realm, args, 0)));
            }, "tan", 1);
        var tanhFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Tanh(MathArg(realm, args, 0)));
            }, "tanh", 1);
        var truncFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                return new(Math.Truncate(MathArg(realm, args, 0)));
            }, "trunc", 1);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Math"), configurable: true),
            PropertyDefinition.Mutable(IdAbs, JsValue.FromObject(absFn)),
            PropertyDefinition.Mutable(IdAcos, JsValue.FromObject(acosFn)),
            PropertyDefinition.Mutable(IdAcosh, JsValue.FromObject(acoshFn)),
            PropertyDefinition.Mutable(IdAsin, JsValue.FromObject(asinFn)),
            PropertyDefinition.Mutable(IdAsinh, JsValue.FromObject(asinhFn)),
            PropertyDefinition.Mutable(IdAtan, JsValue.FromObject(atanFn)),
            PropertyDefinition.Mutable(IdAtan2, JsValue.FromObject(atan2Fn)),
            PropertyDefinition.Mutable(IdAtanh, JsValue.FromObject(atanhFn)),
            PropertyDefinition.Mutable(IdCbrt, JsValue.FromObject(cbrtFn)),
            PropertyDefinition.Mutable(IdCeil, JsValue.FromObject(ceilFn)),
            PropertyDefinition.Mutable(IdClz32, JsValue.FromObject(clz32Fn)),
            PropertyDefinition.Mutable(IdCos, JsValue.FromObject(cosFn)),
            PropertyDefinition.Mutable(IdCosh, JsValue.FromObject(coshFn)),
            PropertyDefinition.Mutable(IdExp, JsValue.FromObject(expFn)),
            PropertyDefinition.Mutable(IdExpm1, JsValue.FromObject(expm1Fn)),
            PropertyDefinition.Mutable(IdF16Round, JsValue.FromObject(f16RoundFn)),
            PropertyDefinition.Mutable(IdFloor, JsValue.FromObject(floorFn)),
            PropertyDefinition.Mutable(IdFround, JsValue.FromObject(froundFn)),
            PropertyDefinition.Mutable(IdHypot, JsValue.FromObject(hypotFn)),
            PropertyDefinition.Mutable(IdImul, JsValue.FromObject(imulFn)),
            PropertyDefinition.Mutable(IdLog, JsValue.FromObject(logFn)),
            PropertyDefinition.Mutable(IdLog1P, JsValue.FromObject(log1PFn)),
            PropertyDefinition.Mutable(IdLog2, JsValue.FromObject(log2Fn)),
            PropertyDefinition.Mutable(IdLog10, JsValue.FromObject(log10Fn)),
            PropertyDefinition.Mutable(IdMax, JsValue.FromObject(maxFn)),
            PropertyDefinition.Mutable(IdMin, JsValue.FromObject(minFn)),
            PropertyDefinition.Mutable(IdPow, JsValue.FromObject(powFn)),
            PropertyDefinition.Mutable(IdRandom, JsValue.FromObject(randomFn)),
            PropertyDefinition.Mutable(IdRound, JsValue.FromObject(roundFn)),
            PropertyDefinition.Mutable(IdSign, JsValue.FromObject(signFn)),
            PropertyDefinition.Mutable(IdSin, JsValue.FromObject(sinFn)),
            PropertyDefinition.Mutable(IdSinh, JsValue.FromObject(sinhFn)),
            PropertyDefinition.Mutable(IdSqrt, JsValue.FromObject(sqrtFn)),
            PropertyDefinition.Mutable(IdSumPrecise, JsValue.FromObject(sumPreciseFn)),
            PropertyDefinition.Mutable(IdTan, JsValue.FromObject(tanFn)),
            PropertyDefinition.Mutable(IdTanh, JsValue.FromObject(tanhFn)),
            PropertyDefinition.Mutable(IdTrunc, JsValue.FromObject(truncFn)),
            PropertyDefinition.Const(IdE, new(Math.E)),
            PropertyDefinition.Const(IdLn2, new(Math.Log(2d))),
            PropertyDefinition.Const(IdLn10, new(Math.Log(10d))),
            PropertyDefinition.Const(IdLog2E, new(Math.Log2(Math.E))),
            PropertyDefinition.Const(IdLog10E, new(Math.Log10(Math.E))),
            PropertyDefinition.Const(IdPi, new(Math.PI)),
            PropertyDefinition.Const(IdSqrt12, new(Math.Sqrt(0.5d))),
            PropertyDefinition.Const(IdSqrt2, new(Math.Sqrt(2d)))
        ];

        math.DefineNewPropertiesNoCollision(Realm, defs);
        return math;
    }

    private static JsValue ArgValue(ReadOnlySpan<JsValue> args, int index)
    {
        return index < args.Length ? args[index] : JsValue.Undefined;
    }

    private static double MathArg(JsRealm realm, ReadOnlySpan<JsValue> args, int index)
    {
        return realm.ToNumberSlowPath(ArgValue(args, index));
    }

    private static int Clz32(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        var value = ToUint32(realm, ArgValue(args, 0));
        return value == 0 ? 32 : BitOperations.LeadingZeroCount(value);
    }

    private static double Hypot(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0)
            return 0d;

        var coerced = (stackalloc double[args.Length]);
        for (var i = 0; i < args.Length; i++)
            coerced[i] = Math.Abs(realm.ToNumberSlowPath(args[i]));

        var max = 0d;
        var sum = 0d;
        var sawNaN = false;
        for (var i = 0; i < coerced.Length; i++)
        {
            var n = coerced[i];
            if (double.IsPositiveInfinity(n))
                return double.PositiveInfinity;
            if (double.IsNaN(n))
            {
                sawNaN = true;
                continue;
            }

            if (n > max)
            {
                var r = max / n;
                sum = sum * r * r + 1d;
                max = n;
            }
            else if (n != 0d)
            {
                var r = n / max;
                sum += r * r;
            }
        }

        if (sawNaN)
            return double.NaN;
        if (max == 0d)
            return 0d;
        return max * Math.Sqrt(sum);
    }

    private static double Max(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0)
            return double.NegativeInfinity;

        var max = double.NegativeInfinity;
        for (var i = 0; i < args.Length; i++)
        {
            var v = realm.ToNumberSlowPath(args[i]);
            if (double.IsNaN(v))
                max = v;
            else if (v > max || (v == 0d && max == 0d && !double.IsNegative(v)))
                max = v;
        }

        return max;
    }

    private static double Min(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0)
            return double.PositiveInfinity;

        var min = double.PositiveInfinity;
        for (var i = 0; i < args.Length; i++)
        {
            var v = realm.ToNumberSlowPath(args[i]);
            if (double.IsNaN(v))
                min = v;
            else if (v < min || (v == 0d && min == 0d && double.IsNegative(v)))
                min = v;
        }

        return min;
    }

    private static double RoundJs(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number) || number == 0d)
            return number;

        var integer = Math.Truncate(number);
        if (number == integer)
            return number;

        if (number < 0d)
        {
            if (number >= -0.5d)
                return -0d;

            var fraction = integer - number;
            return fraction > 0.5d ? integer - 1d : integer;
        }

        var positiveFraction = number - integer;
        return positiveFraction >= 0.5d ? integer + 1d : integer;
    }

    private static double SignJs(double number)
    {
        if (double.IsNaN(number))
            return double.NaN;
        if (number == 0d)
            return number;
        return number < 0d ? -1d : 1d;
    }

    private static uint ToUint32(JsRealm realm, in JsValue value)
    {
        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number) || number == 0d || double.IsInfinity(number))
            return 0;

        var intPart = Math.Truncate(number);
        var uint32 = intPart % 4294967296d;
        if (uint32 < 0)
            uint32 += 4294967296d;
        return (uint)uint32;
    }

    private static double SumPrecise(JsRealm realm, in JsValue iterable)
    {
        var values = new PooledList<double>(8);
        var sawValue = false;
        var sawNegativeZeroOnly = true;
        var sawNaN = false;
        var sawPositiveInfinity = false;
        var sawNegativeInfinity = false;

        try
        {
            SumPreciseIterateValues(realm, iterable, ref values, ref sawValue, ref sawNegativeZeroOnly, ref sawNaN,
                ref sawPositiveInfinity, ref sawNegativeInfinity);

            if (sawNaN || (sawPositiveInfinity && sawNegativeInfinity))
                return double.NaN;
            if (sawPositiveInfinity)
                return double.PositiveInfinity;
            if (sawNegativeInfinity)
                return double.NegativeInfinity;
            if (!sawValue || sawNegativeZeroOnly)
                return BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));

            return Internals.SumPrecise.Sum(values.AsSpan());
        }
        finally
        {
            values.Dispose();
        }
    }

    private static void SumPreciseIterateValues(
        JsRealm realm,
        in JsValue iterable,
        ref PooledList<double> values,
        ref bool sawValue,
        ref bool sawNegativeZeroOnly,
        ref bool sawNaN,
        ref bool sawPositiveInfinity,
        ref bool sawNegativeInfinity)
    {
        if (iterable.TryGetObject(out var objectValue))
            if (TryGetIteratorObjectForMathSum(realm, objectValue, out var iterator))
                try
                {
                    while (true)
                    {
                        var step = StepIteratorForMathSum(realm, iterator, out var done);
                        if (done)
                            return;
                        SumPreciseConsumeValue(step, ref values, ref sawValue, ref sawNegativeZeroOnly, ref sawNaN,
                            ref sawPositiveInfinity, ref sawNegativeInfinity);
                    }
                }
                catch
                {
                    CloseIteratorForMathSum(realm, iterator);
                    throw;
                }

        throw new JsRuntimeException(JsErrorKind.TypeError, "Math.sumPrecise value is not iterable");
    }

    private static void SumPreciseConsumeValue(
        in JsValue value,
        ref PooledList<double> values,
        ref bool sawValue,
        ref bool sawNegativeZeroOnly,
        ref bool sawNaN,
        ref bool sawPositiveInfinity,
        ref bool sawNegativeInfinity)
    {
        if (!value.IsNumber)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Math.sumPrecise iterable values must be numbers");

        var n = value.NumberValue;
        sawValue = true;
        if (double.IsNaN(n))
        {
            sawNaN = true;
            return;
        }

        if (double.IsPositiveInfinity(n))
        {
            sawPositiveInfinity = true;
            return;
        }

        if (double.IsNegativeInfinity(n))
        {
            sawNegativeInfinity = true;
            return;
        }

        if (n != 0d || !double.IsNegative(n))
            sawNegativeZeroOnly = false;

        if (n != 0d)
            values.Add(n);
    }

    private static bool TryGetIteratorObjectForMathSum(JsRealm realm, JsObject iterable, out JsObject iterator)
    {
        iterator = default!;
        if (!iterable.TryGetPropertyAtom(realm, IdSymbolIterator, out var iteratorMethod, out _))
            return false;
        if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            return false;

        var iteratorValue = realm.InvokeFunction(iteratorFn, JsValue.FromObject(iterable), []);
        if (!iteratorValue.TryGetObject(out iterator!))
            return false;
        return true;
    }

    private static JsValue StepIteratorForMathSum(JsRealm realm, JsObject iterator, out bool done)
    {
        if (!iterator.TryGetPropertyAtom(realm, IdNext, out var nextMethod, out _))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Math.sumPrecise iterator.next is not a function");
        if (!nextMethod.TryGetObject(out var nextMethodObj) || nextMethodObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Math.sumPrecise iterator.next is not a function");

        var stepResult = realm.InvokeFunction(nextFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
        if (!stepResult.TryGetObject(out var resultObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Math.sumPrecise iterator result must be object");

        _ = resultObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
        done = JsRealm.ToBoolean(doneValue);
        if (done)
            return JsValue.Undefined;

        return resultObj.TryGetPropertyAtom(realm, IdValue, out var value, out _)
            ? value
            : JsValue.Undefined;
    }

    private static void CloseIteratorForMathSum(JsRealm realm, JsObject iterator)
    {
        if (!iterator.TryGetPropertyAtom(realm, IdReturn, out var returnMethod, out _))
            return;
        if (!returnMethod.TryGetObject(out var returnMethodObj) || returnMethodObj is not JsFunction returnFn)
            return;

        _ = realm.InvokeFunction(returnFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
    }
}
