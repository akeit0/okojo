using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Okojo;

[StructLayout(LayoutKind.Explicit)]
public readonly struct JsValue : IEquatable<JsValue>
{
    internal const ulong BoxHdr = 0x7FF8000000000000UL;
    internal const ulong BoxMask = 0xFFFF000000000000UL;
    internal const ulong Top32Mask = 0xFFFFFFFF00000000UL;
    internal const int TagShift = 44;
    internal const ulong TagMask = 0xFUL;
    internal const ulong JsInt32Top32Bits = (BoxHdr | ((ulong)Tag.JsTagInt << TagShift)) & Top32Mask;
    internal const ulong JsNullBits = BoxHdr | ((ulong)Tag.JsTagNull << TagShift);
    internal const ulong JsUndefinedBits = BoxHdr | ((ulong)Tag.JsTagUndefined << TagShift);
    internal const ulong JsTheHoleBits = BoxHdr | ((ulong)Tag.JsTagUninitialized << TagShift);
    internal const ulong JsStringBits = BoxHdr | ((ulong)Tag.JsTagString << TagShift);
    internal const ulong JsSymbolBits = BoxHdr | ((ulong)Tag.JsTagSymbol << TagShift);
    internal const ulong JsObjectBits = BoxHdr | ((ulong)Tag.JsTagObject << TagShift);
    internal const ulong JsBigIntBits = BoxHdr | ((ulong)Tag.JsTagBigInt << TagShift);
    internal const ulong JsContextBits = BoxHdr | ((ulong)Tag.JsTagContext << TagShift);
    internal const ulong JsBoolTrueBits = BoxHdr | ((ulong)Tag.JsTagBool << TagShift) | 1UL;
    internal const ulong JsBoolFalseBits = BoxHdr | ((ulong)Tag.JsTagBool << TagShift);

    public static JsValue Undefined => new(JsUndefinedBits);
    public static JsValue TheHole => new(JsTheHoleBits);
    public static JsValue Null => new(JsNullBits);
    public static JsValue True => new(true);
    public static JsValue False => new(false);
    public const ulong JsNan = 0x7ff0000000000001;
    public static JsValue NaN => new(JsNan);

    [FieldOffset(0)] public readonly ulong U;

    // [FieldOffset(0)] public readonly double D;
    [FieldOffset(8)] public readonly object? Obj;

    public Tag Tag
    {
        get
        {
            if ((U & BoxMask) != BoxHdr) return Tag.JsTagFloat64;
            return (Tag)((U >> TagShift) & TagMask);
        }
    }

    public bool IsFloat64 => (U & BoxMask) != BoxHdr;
    public bool IsInt32 => (U & Top32Mask) == JsInt32Top32Bits;
    public bool IsNumber => (U & BoxMask) != BoxHdr || (U & Top32Mask) == JsInt32Top32Bits;

    public bool IsNumeric =>
        (U & BoxMask) != BoxHdr || (U & Top32Mask) is JsInt32Top32Bits or (JsBigIntBits & Top32Mask);

    public bool IsSymbol => U == JsSymbolBits;
    public bool IsString => U == JsStringBits;
    public bool IsObject => U == JsObjectBits;
    public bool IsBigInt => U == JsBigIntBits;
    public bool IsTrue => U == (BoxHdr | ((ulong)Tag.JsTagBool << TagShift) | 1UL);
    public bool IsFalse => U == (BoxHdr | ((ulong)Tag.JsTagBool << TagShift));
    public bool IsBool => (U & ~1ul) == (BoxHdr | ((ulong)Tag.JsTagBool << TagShift));
    public bool IsNull => U == JsNullBits;
    public bool IsUndefined => U == JsUndefinedBits;
    public bool IsNullOrUndefined => U is JsUndefinedBits or JsNullBits;

    public bool IsTheHole => U == JsTheHoleBits;
    public bool IsNaN => U == JsNan;

    internal JsValue(ulong u)
    {
        U = u;
        Obj = null;
    }

    internal JsValue(Tag tag, uint payload = 0, object? obj = null)
    {
        U = BoxHdr | ((ulong)tag << TagShift) | payload;
        Obj = obj;
    }

    internal JsValue(ulong u, object? obj)
    {
        U = u;
        Obj = obj;
    }

    public static JsValue FromInt32(int value)
    {
        return new(Tag.JsTagInt, (uint)value);
    }

    public static JsValue FromString(string value)
    {
        return new(Tag.JsTagString, 0, value);
    }

    public static JsValue FromString(JsString value)
    {
        return new(Tag.JsTagString, 0, value.StringLikeObject);
    }

    public static JsValue FromSymbol(Symbol value)
    {
        return new(Tag.JsTagSymbol, 0, value);
    }

    public static JsValue FromObject(JsObject value)
    {
        return new(Tag.JsTagObject, 0, value);
    }

    public static JsValue FromObject(JsContext value)
    {
        return new(Tag.JsTagContext, 0, value);
    }

    public static JsValue FromBigInt(JsBigInt value)
    {
        return new(Tag.JsTagBigInt, 0, value);
    }

    public JsString AsJsString()
    {
        return new(Obj!);
    }

    public string AsString()
    {
        return AsJsString().Flatten();
    }

    public bool TryGetString([NotNullWhen(true)] out string? str)
    {
        if (IsString)
        {
            str = AsJsString().Flatten();
            return true;
        }

        str = null;
        return false;
    }

    public Symbol AsSymbol()
    {
        return (Symbol)Obj!;
    }

    public JsObject AsObject()
    {
        return (JsObject)Obj!;
    }

    public JsBigInt AsBigInt()
    {
        return (JsBigInt)Obj!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal JsValue NegateBigInt()
    {
        var bigint = AsBigInt().Value;
        if (bigint.IsZero) return this;
        return FromBigInt(new(-bigint));
    }

    public bool TryGetObject([NotNullWhen(true)] out JsObject? obj)
    {
        if (IsObject)
        {
            obj = Unsafe.As<JsObject>(Obj!);
            return true;
        }

        obj = null;
        return false;
    }

    public JsValue(double d)
    {
        if (double.IsNaN(d))
            U = JsNan;
        else
            U = Unsafe.As<double, ulong>(ref d);

        Obj = null;
    }

    public JsValue(bool v) : this(Tag.JsTagBool, v ? 1u : 0u)
    {
    }

    internal double FastFloat64Value => Unsafe.As<ulong, double>(ref Unsafe.AsRef(in U));

    public double Float64Value =>
        IsFloat64 ? Unsafe.As<ulong, double>(ref Unsafe.AsRef(in U)) : IsInt32 ? Int32Value : double.NaN;

    public int Int32Value => (int)(U & 0xFFFFFFFFUL);

    internal double FastNumberValue
    {
        get
        {
            if ((U & BoxMask) != BoxHdr) return Unsafe.As<ulong, double>(ref Unsafe.AsRef(in U));

            var intValue = (int)(U & 0xFFFFFFFFUL);
            // if(intValue==0)return +0d;
            return intValue;
        }
    }

    internal static bool TryGetNumberValue(in JsValue value, out double number)
    {
        if ((value.U & BoxMask) != BoxHdr)
        {
            number = Unsafe.As<ulong, double>(ref Unsafe.AsRef(in value.U));
            return true;
        }

        if ((value.U & Top32Mask) == JsInt32Top32Bits)
        {
            number = (int)(value.U & 0xFFFFFFFFUL);
            return true;
        }

        Unsafe.SkipInit(out number);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetNumberValueFromUlong(ulong u, out double number)
    {
        if ((u & BoxMask) != BoxHdr)
        {
            number = Unsafe.As<ulong, double>(ref u);
            return true;
        }

        if ((u & Top32Mask) == JsInt32Top32Bits)
        {
            number = (int)(u & 0xFFFFFFFFUL);
            return true;
        }

        Unsafe.SkipInit(out number);
        return false;
    }

    internal static double FastNumberValueFromULong(ulong u)
    {
        if ((u & BoxMask) != BoxHdr) return Unsafe.As<ulong, double>(ref u);

        var intValue = (int)(u & 0xFFFFFFFFUL);
        // if(intValue==0)return +0d;
        return intValue;
    }

    public double NumberValue => (U & BoxMask) != BoxHdr ? Unsafe.As<ulong, double>(ref Unsafe.AsRef(in U)) :
        IsInt32 ? Int32Value : double.NaN;

    public static implicit operator JsValue(int value)
    {
        return new(Tag.JsTagInt, (uint)value);
    }

    public static implicit operator JsValue(double value)
    {
        return new(value);
    }

    public static implicit operator JsValue(bool value)
    {
        return new(value);
    }

    public static implicit operator JsValue(string value)
    {
        return new(BoxHdr | ((ulong)Tag.JsTagString << TagShift), value);
    }

    public static implicit operator JsValue(JsString value)
    {
        return FromString(value);
    }

    public static implicit operator JsValue(Symbol value)
    {
        return FromSymbol(value);
    }

    public static implicit operator JsValue(JsObject value)
    {
        return new(BoxHdr | ((ulong)Tag.JsTagObject << TagShift), value);
    }

    public bool Equals(JsValue other)
    {
        return U == other.U && ReferenceEquals(Obj, other.Obj);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(U, Obj);
    }

    public static bool SameValue(in JsValue a, in JsValue b)
    {
        if (a.IsNumber && b.IsNumber)
        {
            var da = a.FastNumberValue;
            var db = b.FastNumberValue;
            if (double.IsNaN(da) && double.IsNaN(db))
                return true;
            if (da == 0d && db == 0d)
                return double.IsNegativeInfinity(1d / da) == double.IsNegativeInfinity(1d / db);
            return da == db;
        }

        if (a.Tag != b.Tag)
            return false;

        if (a.IsString && b.IsString)
            return JsString.EqualsOrdinal(a.AsJsString(), b.AsJsString());
        if (a.IsSymbol && b.IsSymbol)
            return ReferenceEquals(a.AsSymbol(), b.AsSymbol());
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
        return a.U == b.U;
    }

    public override string ToString()
    {
        if (IsUndefined) return "undefined";
        if (IsNull) return "null";
        if (IsNaN) return "NaN";
        if (IsFloat64) return JsNumberFormatting.ToJsString(Float64Value);
        if (IsTrue) return "true";
        if (IsFalse) return "false";
        if (IsInt32) return Int32Value.ToString();
        if (IsSymbol) return AsSymbol().ToString();
        if (IsString) return AsString();
        if (IsBigInt) return AsBigInt().ToString();
        if (IsObject) return AsObject().ToString() ?? $"[object {AsObject().GetType().Name}]";
        return Tag.ToString();
    }

    public string ToString(JsRealm realm)
    {
        if (IsUndefined) return "undefined";
        if (IsNull) return "null";
        if (IsNaN) return "NaN";
        if (IsFloat64) return JsNumberFormatting.ToJsString(Float64Value);
        if (IsTrue) return "true";
        if (IsFalse) return "false";
        if (IsInt32) return Int32Value.ToString();
        if (IsSymbol) return AsSymbol().ToString();
        if (IsString) return AsString();
        if (IsBigInt) return AsBigInt().ToString();
        if (IsObject) return realm.ToPrimitiveSlowPath(this, true).ToString(realm);
        return Tag.ToString();
    }

    public bool ToBoolean()
    {
        if (IsBool)
            return IsTrue;
        if (IsNullOrUndefined)
            return false;
        if (IsNumber)
        {
            var number = NumberValue;
            return number != 0d && !double.IsNaN(number);
        }

        if (IsString)
            return AsString().Length != 0;
        if (IsBigInt)
            return !AsBigInt().Value.IsZero;
        return true;
    }

    public static string NumberToJsString(double number)
    {
        return JsNumberFormatting.ToJsString(number);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double StringToNumber(string s)
    {
        var span = s.AsSpan().Trim();
        if (span.Length == 0) return 0d;
        if (TryParseHexLiteral(span, out var hex))
            return hex;
        if (double.TryParse(span, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var n))
            return n;
        return double.NaN;

        static bool TryParseHexLiteral(ReadOnlySpan<char> text, out double number)
        {
            number = 0;
            if (text.Length < 3)
                return false;
            if (text[0] == '+' || text[0] == '-')
                return false;
            if (text[0] != '0' || (text[1] != 'x' && text[1] != 'X'))
                return false;

            double acc = 0;
            for (var i = 2; i < text.Length; i++)
            {
                var digit = text[i] switch
                {
                    >= '0' and <= '9' => text[i] - '0',
                    >= 'a' and <= 'f' => text[i] - 'a' + 10,
                    >= 'A' and <= 'F' => text[i] - 'A' + 10,
                    _ => -1
                };
                if (digit < 0)
                    return false;
                acc = acc * 16d + digit;
            }

            number = acc;
            return true;
        }
    }
}
