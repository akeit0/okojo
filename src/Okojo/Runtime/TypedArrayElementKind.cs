namespace Okojo.Runtime;

internal enum TypedArrayElementKind : byte
{
    Int8,
    Uint8,
    Uint8Clamped,
    Int16,
    Uint16,
    Int32,
    Uint32,
    Float16,
    Float32,
    Float64,
    BigInt64,
    BigUint64
}

internal static class TypedArrayElementKindInfo
{
    internal static int GetBytesPerElement(this TypedArrayElementKind kind)
    {
        return kind switch
        {
            TypedArrayElementKind.Int8 => 1,
            TypedArrayElementKind.Uint8 => 1,
            TypedArrayElementKind.Uint8Clamped => 1,
            TypedArrayElementKind.Int16 => 2,
            TypedArrayElementKind.Uint16 => 2,
            TypedArrayElementKind.Int32 => 4,
            TypedArrayElementKind.Uint32 => 4,
            TypedArrayElementKind.Float16 => 2,
            TypedArrayElementKind.Float32 => 4,
            TypedArrayElementKind.Float64 => 8,
            TypedArrayElementKind.BigInt64 => 8,
            TypedArrayElementKind.BigUint64 => 8,
            _ => 1
        };
    }

    internal static string GetConstructorName(this TypedArrayElementKind kind)
    {
        return kind switch
        {
            TypedArrayElementKind.Int8 => "Int8Array",
            TypedArrayElementKind.Uint8 => "Uint8Array",
            TypedArrayElementKind.Uint8Clamped => "Uint8ClampedArray",
            TypedArrayElementKind.Int16 => "Int16Array",
            TypedArrayElementKind.Uint16 => "Uint16Array",
            TypedArrayElementKind.Int32 => "Int32Array",
            TypedArrayElementKind.Uint32 => "Uint32Array",
            TypedArrayElementKind.Float16 => "Float16Array",
            TypedArrayElementKind.Float32 => "Float32Array",
            TypedArrayElementKind.Float64 => "Float64Array",
            TypedArrayElementKind.BigInt64 => "BigInt64Array",
            TypedArrayElementKind.BigUint64 => "BigUint64Array",
            _ => "Uint8Array"
        };
    }

    internal static bool IsBigIntFamily(this TypedArrayElementKind kind)
    {
        return kind is TypedArrayElementKind.BigInt64 or TypedArrayElementKind.BigUint64;
    }

    internal static JsValue NormalizeValue(JsRealm realm, TypedArrayElementKind kind, in JsValue value)
    {
        return kind switch
        {
            TypedArrayElementKind.Int8 => JsValue.FromInt32(
                unchecked((sbyte)ToFixedWidthInteger(realm, value, 8, true))),
            TypedArrayElementKind.Uint8 => JsValue.FromInt32(
                unchecked((byte)ToFixedWidthInteger(realm, value, 8, false))),
            TypedArrayElementKind.Uint8Clamped => JsValue.FromInt32(ToUint8Clamp(realm, value)),
            TypedArrayElementKind.Int16 => JsValue.FromInt32(
                unchecked((short)ToFixedWidthInteger(realm, value, 16, true))),
            TypedArrayElementKind.Uint16 => JsValue.FromInt32(
                unchecked((ushort)ToFixedWidthInteger(realm, value, 16, false))),
            TypedArrayElementKind.Int32 => JsValue.FromInt32(
                unchecked((int)ToFixedWidthInteger(realm, value, 32, true))),
            TypedArrayElementKind.Uint32 => new(
                (double)unchecked((uint)ToFixedWidthInteger(realm, value, 32, false))),
            TypedArrayElementKind.Float16 => new((double)(Half)realm.ToNumberSlowPath(value)),
            TypedArrayElementKind.Float32 => new((float)realm.ToNumberSlowPath(value)),
            TypedArrayElementKind.Float64 => new(realm.ToNumberSlowPath(value)),
            TypedArrayElementKind.BigInt64 => JsValue.FromBigInt(
                new(Intrinsics.BigIntAsIntN(64, Intrinsics.ToBigIntValue(realm, value).Value))),
            TypedArrayElementKind.BigUint64 => JsValue.FromBigInt(
                new(Intrinsics.BigIntAsUintN(64, Intrinsics.ToBigIntValue(realm, value).Value))),
            _ => value
        };
    }

    private static long ToFixedWidthInteger(JsRealm realm, in JsValue value, int bits, bool signed)
    {
        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number) || number == 0d || double.IsInfinity(number))
            return 0;

        var integer = Math.Truncate(number);
        if (bits == 32)
        {
            var uint32 = unchecked((ulong)(long)integer);
            return signed ? unchecked((int)uint32) : unchecked((uint)uint32);
        }

        var moduloBase = 1L << bits;
        var modulo = (long)integer % moduloBase;
        if (modulo < 0)
            modulo += moduloBase;
        if (!signed)
            return modulo;
        var signedThreshold = 1L << (bits - 1);
        return modulo >= signedThreshold ? modulo - moduloBase : modulo;
    }

    private static int ToUint8Clamp(JsRealm realm, in JsValue value)
    {
        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number) || number <= 0d)
            return 0;
        if (number >= 255d)
            return 255;

        var floor = Math.Floor(number);
        var fraction = number - floor;
        if (fraction < 0.5d)
            return (int)floor;
        if (fraction > 0.5d)
            return (int)floor + 1;
        return ((int)floor & 1) == 0 ? (int)floor : (int)floor + 1;
    }
}
