using System.Globalization;

namespace Okojo.Runtime.Interop;

public static class CallInfoSpanConverter
{
    public static int GetRemainingArgumentCount(scoped in CallInfo info, int startIndex)
    {
        return info.ArgumentCount > startIndex ? info.ArgumentCount - startIndex : 0;
    }

    public static ReadOnlySpan<JsValue> GetArgumentSpan(scoped in CallInfo info, int startIndex)
    {
        var count = GetRemainingArgumentCount(info, startIndex);
        return info.Arguments.Slice(startIndex, count);
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<bool> destination)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = info.GetArgument(startIndex + i).ToBoolean();
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<string> destination)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = info.GetArgumentString(startIndex + i);
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] = value.IsNumber ? checked((byte)value.NumberValue) : info.GetArgument<byte>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<sbyte> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] =
                value.IsNumber ? checked((sbyte)value.NumberValue) : info.GetArgument<sbyte>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<short> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] =
                value.IsNumber ? checked((short)value.NumberValue) : info.GetArgument<short>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<ushort> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] = value.IsNumber
                ? checked((ushort)value.NumberValue)
                : info.GetArgument<ushort>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<int> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] = value.IsNumber ? checked((int)value.NumberValue) : info.GetArgument<int>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<uint> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] = value.IsNumber ? checked((uint)value.NumberValue) : info.GetArgument<uint>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<long> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] = value.IsNumber ? checked((long)value.NumberValue) : info.GetArgument<long>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<ulong> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var argumentIndex = startIndex + i;
            var value = info.GetArgument(argumentIndex);
            destination[i] =
                value.IsNumber ? checked((ulong)value.NumberValue) : info.GetArgument<ulong>(argumentIndex);
        }
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<float> destination)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = info.GetArgumentSingle(startIndex + i);
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<double> destination)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = info.GetArgumentDouble(startIndex + i);
    }

    public static void FillArgumentSpan(scoped in CallInfo info, int startIndex, Span<decimal> destination)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = Convert.ToDecimal(info.GetArgumentDouble(startIndex + i), CultureInfo.InvariantCulture);
    }

    public static void FillArgumentSpan<T>(scoped in CallInfo info, int startIndex, Span<T> destination)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = HostValueConverter.ConvertFromJsValue<T>(info.Realm, info.GetArgument(startIndex + i));
    }
}
