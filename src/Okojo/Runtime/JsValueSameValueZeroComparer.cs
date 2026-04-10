using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

internal sealed class JsValueSameValueZeroComparer : IEqualityComparer<JsValue>
{
    internal static readonly JsValueSameValueZeroComparer Instance = new();

    public bool Equals(JsValue x, JsValue y)
    {
        if (x.IsNumber && y.IsNumber)
        {
            var dx = x.NumberValue;
            var dy = y.NumberValue;
            if (double.IsNaN(dx) && double.IsNaN(dy))
                return true;
            if (dx == 0d && dy == 0d)
                return true;
            return dx.Equals(dy);
        }

        if (x.Tag != y.Tag)
            return false;

        if (x.IsUndefined || x.IsNull)
            return true;
        if (x.IsBool)
            return x.IsTrue == y.IsTrue;
        if (x.IsString)
            return x.AsJsString().Equals(y.AsJsString());
        if (x.IsBigInt)
            return x.AsBigInt().Equals(y.AsBigInt());
        if (x.IsSymbol)
            return ReferenceEquals(x.AsSymbol(), y.AsSymbol());
        if (x.IsObject)
            return ReferenceEquals(x.Obj, y.Obj);

        return false;
    }

    public int GetHashCode(JsValue value)
    {
        if (value.IsNumber)
        {
            var d = value.NumberValue;
            if (double.IsNaN(d))
                return 0x7FF8_0000;
            if (d == 0d)
                return 0;
            return BitConverter.DoubleToInt64Bits(d).GetHashCode();
        }

        if (value.IsUndefined)
            return 1;
        if (value.IsNull)
            return 2;
        if (value.IsBool)
            return value.IsTrue ? 3 : 4;
        if (value.IsString)
            return value.AsJsString().GetHashCode();
        if (value.IsBigInt)
            return value.AsBigInt().Value.GetHashCode();
        if (value.IsSymbol)
            return RuntimeHelpers.GetHashCode(value.AsSymbol());
        if (value.IsObject)
            return RuntimeHelpers.GetHashCode(value.Obj!);

        return 0;
    }
}
