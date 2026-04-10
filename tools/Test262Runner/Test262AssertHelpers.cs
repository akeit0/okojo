using System.Globalization;
using Okojo;
using Okojo.Objects;

internal static class Test262AssertHelpers
{
    internal static bool IsTruthy(JsValue value)
    {
        if (value.IsBool)
            return value.IsTrue;
        if (value.IsNumber)
        {
            var n = value.NumberValue;
            return !double.IsNaN(n) && n != 0d;
        }

        if (value.IsString)
            return value.AsString().Length > 0;
        if (value.IsNull || value.IsUndefined)
            return false;
        return value.IsObject;
    }

    internal static bool SameValue(JsValue a, JsValue b)
    {
        if (a.IsNumber && b.IsNumber)
        {
            var da = a.NumberValue;
            var db = b.NumberValue;
            if (double.IsNaN(da) && double.IsNaN(db))
                return true;
            if (da == 0d && db == 0d)
                return double.IsNegativeInfinity(1d / da) == double.IsNegativeInfinity(1d / db);
            return da == db;
        }

        if (a.IsString && b.IsString)
            return a.AsString() == b.AsString();
        if (a.IsBigInt && b.IsBigInt)
            return a.AsBigInt().Equals(b.AsBigInt());
        if (a.IsBool && b.IsBool)
            return a.IsTrue == b.IsTrue;
        if (a.IsNull && b.IsNull)
            return true;
        if (a.IsUndefined && b.IsUndefined)
            return true;
        if (a.IsObject && b.IsObject)
            return ReferenceEquals(a.AsObject(), b.AsObject());
        if (a.IsSymbol && b.IsSymbol)
            return ReferenceEquals(a.AsSymbol(), b.AsSymbol());
        return false;
    }

    internal static bool CompareArrayLikeValues(JsValue actual, JsValue expected)
    {
        if (!actual.TryGetObject(out var actualObj) || !expected.TryGetObject(out var expectedObj))
            return false;

        var actualLength = GetLengthValue(actualObj);
        var expectedLength = GetLengthValue(expectedObj);
        if (!StrictNotEqual(actualLength, expectedLength))
        {
            var loopCount = ToArrayLikeLoopCount(actualLength);
            for (var i = 0; i < loopCount; i++)
            {
                if (!TryReadArrayLikeElement(actualObj, i, out var actualValue))
                    actualValue = JsValue.Undefined;
                if (!TryReadArrayLikeElement(expectedObj, i, out var expectedValue))
                    expectedValue = JsValue.Undefined;
                if (!SameValue(actualValue, expectedValue))
                    return false;
            }

            return true;
        }

        return false;
    }

    private static bool TryReadArrayLikeElement(JsObject obj, int index, out JsValue value)
    {
        if (obj.TryGetElement((uint)index, out value))
            return true;
        return obj.TryGetProperty(index.ToString(CultureInfo.InvariantCulture), out value);
    }

    private static JsValue GetLengthValue(JsObject obj)
    {
        return obj.TryGetProperty("length", out var lengthValue) ? lengthValue : JsValue.Undefined;
    }

    private static bool StrictNotEqual(JsValue a, JsValue b)
    {
        return !SameValue(a, b);
    }

    private static int ToArrayLikeLoopCount(JsValue lengthValue)
    {
        if (lengthValue.IsUndefined)
            return 0;

        var n = lengthValue.NumberValue;
        if (double.IsNaN(n) || n <= 0)
            return 0;
        if (double.IsInfinity(n) || n > int.MaxValue)
            return int.MaxValue;
        return (int)n;
    }
}
