using System.Globalization;
using System.Runtime.CompilerServices;

namespace Okojo.Runtime.Interop;

internal static class HostValueConverter
{
    internal static JsValue ConvertToJsValue(JsRealm realm, object? value)
    {
        if (value is null)
            return JsValue.Null;
        if (value is JsValue jsValue)
            return jsValue;
        if (value is string s)
            return JsValue.FromString(s);
        if (value is bool b)
            return b ? JsValue.True : JsValue.False;
        if (value is int i32)
            return JsValue.FromInt32(i32);
        if (value is byte u8)
            return JsValue.FromInt32(u8);
        if (value is sbyte i8)
            return JsValue.FromInt32(i8);
        if (value is short i16)
            return JsValue.FromInt32(i16);
        if (value is ushort u16)
            return JsValue.FromInt32(u16);
        if (value is uint u32 && u32 <= int.MaxValue)
            return JsValue.FromInt32((int)u32);
        if (value is long i64 && i64 is >= int.MinValue and <= int.MaxValue)
            return JsValue.FromInt32((int)i64);
        if (value is ulong u64 && u64 <= int.MaxValue)
            return JsValue.FromInt32((int)u64);
        if (value is float f32)
            return new(f32);
        if (value is double f64)
            return new(f64);
        if (value is decimal dec)
            return new((double)dec);
        if (JsRealm.TryConvertTaskObjectToJsValue(realm, value, out var taskValue))
            return taskValue;
        if (value is JsObject okojoObject)
            return JsValue.FromObject(okojoObject);
        return realm.WrapHostObject(value);
    }

    internal static object? ConvertFromJsValue(JsRealm realm, JsValue value, Type targetType)
    {
        if (TryConvertFromJsValue(realm, value, targetType, out var converted, out _))
            return converted;

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            if (value.IsNullOrUndefined)
                return null;
            targetType = nullableType;
        }

        if (targetType == typeof(JsValue))
            return value;
        if (targetType == typeof(string))
        {
            if (value.IsNull)
                return null;
            if (value.IsString)
                return value.AsString();
            return value.ToString();
        }

        if (targetType == typeof(bool))
        {
            if (value.IsBool)
                return value.IsTrue;
            throw new InvalidOperationException($"Cannot convert {value} to Boolean.");
        }

        if (targetType == typeof(int))
            return checked((int)ConvertToDouble(value));
        if (targetType == typeof(uint))
            return checked((uint)ConvertToDouble(value));
        if (targetType == typeof(long))
            return checked((long)ConvertToDouble(value));
        if (targetType == typeof(ulong))
            return checked((ulong)ConvertToDouble(value));
        if (targetType == typeof(short))
            return checked((short)ConvertToDouble(value));
        if (targetType == typeof(ushort))
            return checked((ushort)ConvertToDouble(value));
        if (targetType == typeof(byte))
            return checked((byte)ConvertToDouble(value));
        if (targetType == typeof(sbyte))
            return checked((sbyte)ConvertToDouble(value));
        if (targetType == typeof(float))
            return (float)ConvertToDouble(value);
        if (targetType == typeof(double))
            return ConvertToDouble(value);
        if (targetType == typeof(decimal))
            return Convert.ToDecimal(ConvertToDouble(value), CultureInfo.InvariantCulture);
        if (targetType.IsEnum)
        {
            if (value.IsString)
                return Enum.Parse(targetType, value.AsString(), false);
            return Enum.ToObject(targetType, checked((int)ConvertToDouble(value)));
        }

        if (targetType == typeof(object))
            return ConvertToBoxedHostValue(value);
        if (targetType == typeof(JsObject))
        {
            if (value.TryGetObject(out var obj))
                return obj;
            throw new InvalidOperationException($"Cannot convert {value} to OkojoObject.");
        }

        if (value.TryGetObject(out var hostObj))
        {
            if (hostObj is JsHostObject host && targetType.IsInstanceOfType(host.Data))
                return host.Data;
            if (targetType.IsInstanceOfType(hostObj))
                return hostObj;
        }

        if (value.IsNull && !targetType.IsValueType)
            return null;
        throw new InvalidOperationException($"Cannot convert {value} to {targetType}.");
    }

    internal static T ConvertFromJsValue<T>(JsRealm realm, JsValue value)
    {
        if (typeof(T) == typeof(JsValue))
            return Unsafe.As<JsValue, T>(ref Unsafe.AsRef(in value));
        if (typeof(T) == typeof(string))
            return ConvertString<T>(value);
        if (typeof(T) == typeof(bool))
            return ConvertBoolean<T>(value);
        if (typeof(T) == typeof(int))
            return ConvertInt32<T>(value);
        if (typeof(T) == typeof(uint))
            return ConvertUInt32<T>(value);
        if (typeof(T) == typeof(long))
            return ConvertInt64<T>(value);
        if (typeof(T) == typeof(ulong))
            return ConvertUInt64<T>(value);
        if (typeof(T) == typeof(short))
            return ConvertInt16<T>(value);
        if (typeof(T) == typeof(ushort))
            return ConvertUInt16<T>(value);
        if (typeof(T) == typeof(byte))
            return ConvertByte<T>(value);
        if (typeof(T) == typeof(sbyte))
            return ConvertSByte<T>(value);
        if (typeof(T) == typeof(float))
            return ConvertSingle<T>(value);
        if (typeof(T) == typeof(double))
            return ConvertDouble<T>(value);
        if (typeof(T) == typeof(decimal))
            return ConvertDecimal<T>(value);
        if (typeof(T) == typeof(object))
        {
            var boxed = ConvertToBoxedHostValue(value);
            return Unsafe.As<object?, T>(ref boxed)!;
        }

        if (typeof(T) == typeof(JsObject))
        {
            if (value.TryGetObject(out var obj))
                return Unsafe.As<JsObject, T>(ref obj);
            throw new InvalidOperationException($"Cannot convert {value} to OkojoObject.");
        }

        if (value.TryGetObject(out var hostObj))
        {
            if (hostObj is JsHostObject host && host.Data is T hostData)
                return hostData;
            if (hostObj is T direct)
                return direct;
        }

        if (value.IsNullOrUndefined && Nullable.GetUnderlyingType(typeof(T)) is not null)
            return default!;
        return (T)ConvertFromJsValue(realm, value, typeof(T))!;
    }

    internal static bool TryGetConversionScore<T>(JsRealm realm, in JsValue value, out int score)
    {
        if (typeof(T) == typeof(JsValue))
        {
            score = 0;
            return true;
        }

        if (typeof(T) == typeof(string))
        {
            score = value.IsString ? 0 : value.IsNull ? 1 : 30;
            return true;
        }

        if (typeof(T) == typeof(bool))
        {
            score = value.IsBool ? 0 : 30;
            return true;
        }

        if (typeof(T) == typeof(int))
            return TryGetNumericScore(value, typeof(int), out score);
        if (typeof(T) == typeof(uint))
            return TryGetNumericScore(value, typeof(uint), out score);
        if (typeof(T) == typeof(long))
            return TryGetNumericScore(value, typeof(long), out score);
        if (typeof(T) == typeof(ulong))
            return TryGetNumericScore(value, typeof(ulong), out score);
        if (typeof(T) == typeof(short))
            return TryGetNumericScore(value, typeof(short), out score);
        if (typeof(T) == typeof(ushort))
            return TryGetNumericScore(value, typeof(ushort), out score);
        if (typeof(T) == typeof(byte))
            return TryGetNumericScore(value, typeof(byte), out score);
        if (typeof(T) == typeof(sbyte))
            return TryGetNumericScore(value, typeof(sbyte), out score);
        if (typeof(T) == typeof(float))
            return TryGetNumericScore(value, typeof(float), out score);
        if (typeof(T) == typeof(double))
            return TryGetNumericScore(value, typeof(double), out score);
        if (typeof(T) == typeof(decimal))
            return TryGetNumericScore(value, typeof(decimal), out score);

        if (typeof(T) == typeof(object))
        {
            score = value.IsNullOrUndefined ? 50 : value.TryGetObject(out _) ? 10 : 20;
            return true;
        }

        if (typeof(T) == typeof(JsObject))
        {
            score = 0;
            return value.TryGetObject(out _);
        }

        if (value.TryGetObject(out var hostObj))
        {
            if (hostObj is JsHostObject host && host.Data is T hostData)
            {
                score = hostData!.GetType() == typeof(T) ? 0 : 2;
                return true;
            }

            if (hostObj is T direct)
            {
                score = direct!.GetType() == typeof(T) ? 0 : 2;
                return true;
            }
        }

        if (value.IsNullOrUndefined && Nullable.GetUnderlyingType(typeof(T)) is not null)
        {
            score = 1;
            return true;
        }

        return TryGetConversionScoreSlow(realm, value, typeof(T), out score);
    }

    internal static bool TryGetConversionScore(JsRealm realm, in JsValue value, Type targetType, out int score)
    {
        score = 0;
        var originalTargetType = targetType;
        if (TryConvertClrHelperValue(value, originalTargetType, out _, out score))
            return true;

        if (JsRealm.TryConvertJsValueToTaskObject(realm, value, originalTargetType, out _, out score))
            return true;

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            if (value.IsNullOrUndefined)
            {
                score = 1;
                return true;
            }

            targetType = nullableType;
            score = 1;
        }

        if (targetType == typeof(JsValue))
        {
            score = 0;
            return true;
        }

        if (targetType == typeof(string))
        {
            if (value.IsNull || value.IsString)
            {
                score += 0;
                return true;
            }

            if (value.IsBool || value.IsNumber)
            {
                score += 5;
                return true;
            }

            return false;
        }

        if (targetType == typeof(bool))
        {
            if (!value.IsBool)
                return false;
            score += 0;
            return true;
        }

        if (TryGetNumericScore(value, targetType, out var numericScore))
        {
            score += numericScore;
            return true;
        }

        if (targetType.IsEnum)
        {
            if (value.IsString)
                try
                {
                    _ = Enum.Parse(targetType, value.AsString(), false);
                    score += 2;
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }

            if (JsValue.TryGetNumberValue(value, out var enumNumber))
                try
                {
                    _ = Enum.ToObject(targetType, checked((int)enumNumber));
                    score += 2;
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }

            return false;
        }

        if (targetType == typeof(object))
        {
            score += 100;
            return true;
        }

        if (targetType == typeof(JsObject))
        {
            score += 0;
            return value.TryGetObject(out _);
        }

        if (value.TryGetObject(out var hostObj))
        {
            if (hostObj is JsHostObject host && targetType.IsInstanceOfType(host.Data))
            {
                score += targetType == host.Data!.GetType() ? 0 : 2;
                return true;
            }

            if (targetType.IsInstanceOfType(hostObj))
            {
                score += targetType == hostObj.GetType() ? 0 : 2;
                return true;
            }
        }

        if (value.IsNull && !targetType.IsValueType)
        {
            score += 1;
            return true;
        }

        return false;
    }

    internal static bool TryConvertFromJsValue(JsRealm realm, JsValue value, Type targetType, out object? result,
        out int score)
    {
        score = 0;
        var originalTargetType = targetType;
        if (TryConvertClrHelperValue(value, originalTargetType, out result, out score))
            return true;

        if (JsRealm.TryConvertJsValueToTaskObject(realm, value, originalTargetType, out result, out score))
            return true;

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            if (value.IsNullOrUndefined)
            {
                result = null;
                score = 1;
                return true;
            }

            targetType = nullableType;
            score = 1;
        }

        if (targetType == typeof(JsValue))
        {
            result = value;
            return true;
        }

        if (targetType == typeof(string))
        {
            if (value.IsNull)
            {
                result = null;
                return true;
            }

            if (value.IsString)
            {
                result = value.AsString();
                return true;
            }

            if (value.IsBool || value.IsNumber)
            {
                result = value.ToString();
                score += 5;
                return true;
            }

            result = null;
            return false;
        }

        if (targetType == typeof(bool))
        {
            if (value.IsBool)
            {
                result = value.IsTrue;
                return true;
            }

            result = null;
            return false;
        }

        if (TryConvertNumeric(value, targetType, out result, out var numericScore))
        {
            score += numericScore;
            return true;
        }

        if (targetType.IsEnum)
        {
            if (value.IsString)
                try
                {
                    result = Enum.Parse(targetType, value.AsString(), false);
                    score += 2;
                    return true;
                }
                catch (ArgumentException)
                {
                    result = null;
                    return false;
                }

            if (JsValue.TryGetNumberValue(value, out var enumNumber))
                try
                {
                    result = Enum.ToObject(targetType, checked((int)enumNumber));
                    score += 2;
                    return true;
                }
                catch (OverflowException)
                {
                    result = null;
                    return false;
                }

            result = null;
            return false;
        }

        if (targetType == typeof(object))
        {
            result = ConvertToBoxedHostValue(value);
            score += 100;
            return true;
        }

        if (targetType == typeof(JsObject))
        {
            if (value.TryGetObject(out var obj))
            {
                result = obj;
                return true;
            }

            result = null;
            return false;
        }

        if (value.TryGetObject(out var hostObj))
        {
            if (hostObj is JsHostObject host && targetType.IsInstanceOfType(host.Data))
            {
                result = host.Data;
                score += targetType == host.Data.GetType() ? 0 : 2;
                return true;
            }

            if (targetType.IsInstanceOfType(hostObj))
            {
                result = hostObj;
                score += targetType == hostObj.GetType() ? 0 : 2;
                return true;
            }
        }

        if (value.IsNull && !targetType.IsValueType)
        {
            result = null;
            score += 1;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryConvertClrHelperValue(JsValue value, Type targetType, out object? result, out int score)
    {
        score = 0;
        if (!value.TryGetObject(out var obj))
        {
            result = null;
            return false;
        }

        if (obj is IClrTypedNullReference typedNull)
        {
            if (!AllowsNull(targetType))
            {
                result = null;
                return false;
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            var comparisonType = nullableType ?? targetType;
            if (comparisonType == typedNull.TargetType)
            {
                result = null;
                return true;
            }

            if (!comparisonType.IsAssignableFrom(typedNull.TargetType))
            {
                result = null;
                return false;
            }

            result = null;
            score = 1;
            return true;
        }

        result = null;
        return false;
    }

    private static object? ConvertToBoxedHostValue(JsValue value)
    {
        if (value.IsUndefined || value.IsNull)
            return null;
        if (value.IsString)
            return value.AsString();
        if (value.IsBool)
            return value.IsTrue;
        if (value.IsInt32)
            return value.Int32Value;
        if (value.IsNumber)
            return value.NumberValue;
        if (value.TryGetObject(out var obj))
            return obj is JsHostObject host ? host.Data : obj;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertString<T>(JsValue value)
    {
        if (value.IsNull)
        {
            string? nullString = null;
            return Unsafe.As<string?, T>(ref nullString)!;
        }

        if (value.IsString)
        {
            var stringValue = value.AsString();
            return Unsafe.As<string, T>(ref stringValue);
        }

        var fallbackString = value.ToString();
        return Unsafe.As<string?, T>(ref fallbackString)!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertBoolean<T>(JsValue value)
    {
        if (value.IsBool)
        {
            var boolValue = value.IsTrue;
            return Unsafe.As<bool, T>(ref boolValue);
        }

        throw new InvalidOperationException($"Cannot convert {value} to Boolean.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertInt32<T>(JsValue value)
    {
        var intValue = checked((int)ConvertToDouble(value));
        return Unsafe.As<int, T>(ref intValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertUInt32<T>(JsValue value)
    {
        var uintValue = checked((uint)ConvertToDouble(value));
        return Unsafe.As<uint, T>(ref uintValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertInt64<T>(JsValue value)
    {
        var longValue = checked((long)ConvertToDouble(value));
        return Unsafe.As<long, T>(ref longValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertUInt64<T>(JsValue value)
    {
        var ulongValue = checked((ulong)ConvertToDouble(value));
        return Unsafe.As<ulong, T>(ref ulongValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertInt16<T>(JsValue value)
    {
        var shortValue = checked((short)ConvertToDouble(value));
        return Unsafe.As<short, T>(ref shortValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertUInt16<T>(JsValue value)
    {
        var ushortValue = checked((ushort)ConvertToDouble(value));
        return Unsafe.As<ushort, T>(ref ushortValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertByte<T>(JsValue value)
    {
        var byteValue = checked((byte)ConvertToDouble(value));
        return Unsafe.As<byte, T>(ref byteValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertSByte<T>(JsValue value)
    {
        var sbyteValue = checked((sbyte)ConvertToDouble(value));
        return Unsafe.As<sbyte, T>(ref sbyteValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertSingle<T>(JsValue value)
    {
        var floatValue = (float)ConvertToDouble(value);
        return Unsafe.As<float, T>(ref floatValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertDouble<T>(JsValue value)
    {
        var doubleValue = ConvertToDouble(value);
        return Unsafe.As<double, T>(ref doubleValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ConvertDecimal<T>(JsValue value)
    {
        var decimalValue = Convert.ToDecimal(ConvertToDouble(value), CultureInfo.InvariantCulture);
        return Unsafe.As<decimal, T>(ref decimalValue);
    }

    private static double ConvertToDouble(JsValue value)
    {
        if (JsValue.TryGetNumberValue(value, out var number))
            return number;
        throw new InvalidOperationException($"Cannot convert {value} to Number.");
    }

    private static bool TryGetNumericScore(in JsValue value, Type targetType, out int score)
    {
        score = 0;
        if (!JsValue.TryGetNumberValue(value, out var number))
            return false;

        try
        {
            if (targetType == typeof(int))
            {
                _ = checked((int)number);
                score = value.IsInt32 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(uint))
            {
                _ = checked((uint)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(long))
            {
                _ = checked((long)number);
                score = value.IsInt32 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(ulong))
            {
                _ = checked((ulong)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(short))
            {
                _ = checked((short)number);
                score = value.IsInt32 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(ushort))
            {
                _ = checked((ushort)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(byte))
            {
                _ = checked((byte)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(sbyte))
            {
                _ = checked((sbyte)number);
                score = value.IsInt32 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(float))
            {
                _ = (float)number;
                score = 1;
                return true;
            }

            if (targetType == typeof(double))
            {
                score = 0;
                return true;
            }

            if (targetType == typeof(decimal))
            {
                _ = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
                score = 1;
                return true;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        return false;
    }

    private static bool TryGetConversionScoreSlow(JsRealm realm, in JsValue value, Type targetType, out int score)
    {
        return TryConvertFromJsValue(realm, value, targetType, out _, out score);
    }

    private static bool TryConvertNumeric(JsValue value, Type targetType, out object? result, out int score)
    {
        score = 0;
        if (!JsValue.TryGetNumberValue(value, out var number))
        {
            result = null;
            return false;
        }

        try
        {
            if (targetType == typeof(int))
            {
                result = checked((int)number);
                score = value.IsInt32 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(uint))
            {
                result = checked((uint)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(long))
            {
                result = checked((long)number);
                score = value.IsInt32 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(ulong))
            {
                result = checked((ulong)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 0 : 1;
                return true;
            }

            if (targetType == typeof(short))
            {
                result = checked((short)number);
                score = value.IsInt32 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(ushort))
            {
                result = checked((ushort)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(byte))
            {
                result = checked((byte)number);
                score = value.IsInt32 && value.Int32Value >= 0 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(sbyte))
            {
                result = checked((sbyte)number);
                score = value.IsInt32 ? 1 : 2;
                return true;
            }

            if (targetType == typeof(float))
            {
                result = (float)number;
                score = 1;
                return true;
            }

            if (targetType == typeof(double))
            {
                result = number;
                score = 0;
                return true;
            }

            if (targetType == typeof(decimal))
            {
                result = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
                score = 1;
                return true;
            }
        }
        catch (OverflowException)
        {
            result = null;
            return false;
        }

        result = null;
        return false;
    }

    private static bool AllowsNull(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }
}
