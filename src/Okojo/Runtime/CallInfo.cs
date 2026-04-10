using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public readonly ref struct CallInfo
{
    private readonly int framePointer;

    internal CallInfo(JsRealm realm, int framePointer)
        : this(realm, framePointer, framePointer + FrameLayout.HeaderSize)
    {
    }

    internal CallInfo(JsRealm realm, int framePointer, int argOffset)
    {
        Realm = realm;
        this.framePointer = framePointer;
        ArgumentOffset = argOffset;
    }

    public JsRealm Realm { get; }

    public JsFunction Function => Realm.GetCallFrameAt(framePointer).Function;
    public JsContext? Context => Realm.GetCallFrameAt(framePointer).Context;
    public CallFrameKind FrameKind => Realm.GetCallFrameAt(framePointer).FrameKind;
    public CallFrameFlag Flags => Realm.GetCallFrameAt(framePointer).Flags;
    public int CallerFp => Realm.GetCallFrameAt(framePointer).CallerFp;
    public int CallerPc => Realm.GetCallFrameAt(framePointer).CallerPc;
    public int ArgumentCount => Realm.GetCallFrameAt(framePointer).ArgCount;
    public int ArgumentOffset { get; }

    public JsValue ThisValue => Realm.Stack[framePointer + FrameLayout.OffsetThisValue];
    public JsValue NewTarget => Realm.GetFrameNewTarget(framePointer);
    public bool IsConstruct => (Flags & CallFrameFlag.IsConstructorCall) != 0;
    public bool IsDerivedConstruct => (Flags & CallFrameFlag.IsDerivedConstructorCall) != 0;

    public ReadOnlySpan<JsValue> Arguments =>
        Realm.Stack.AsSpan(ArgumentOffset, ArgumentCount);

    public T GetThis<T>()
    {
        if (ThisValue.TryGetObject(out var obj))
        {
            if (obj is JsHostObject host && host.Data is T hostData)
                return hostData;
            if (obj is T direct)
                return direct;

            ThrowHostBindingTypeError($"Host function called on incompatible receiver. Expected {typeof(T)}.");
        }

        return ConvertArgument<T>(ThisValue, true);
    }

    public T GetArgument<T>(int index)
    {
        return ConvertArgument<T>(GetArgument(index), false);
    }

    public T GetArgumentOrDefault<T>(int index, T defaultValue)
    {
        if ((uint)index >= (uint)ArgumentCount)
            return defaultValue;
        return GetArgument<T>(index);
    }

    public JsValue GetArgument(int index)
    {
        return (uint)index < (uint)ArgumentCount ? Realm.Stack[ArgumentOffset + index] : JsValue.Undefined;
    }

    public double GetArgumentDouble(int index)
    {
        var value = GetArgument(index);
        if (JsValue.TryGetNumberValue(value, out var number))
            return number;
        return ConvertArgument<double>(value, false);
    }

    public double GetArgumentDoubleOrDefault(int index, double defaultValue)
    {
        if ((uint)index >= (uint)ArgumentCount)
            return defaultValue;
        return GetArgumentDouble(index);
    }

    public float GetArgumentSingle(int index)
    {
        var value = GetArgument(index);
        if (JsValue.TryGetNumberValue(value, out var number))
            return (float)number;
        return ConvertArgument<float>(value, false);
    }

    public float GetArgumentSingleOrDefault(int index, float defaultValue)
    {
        if ((uint)index >= (uint)ArgumentCount)
            return defaultValue;
        return GetArgumentSingle(index);
    }

    public string GetArgumentString(int index)
    {
        var value = GetArgument(index);
        if (value.IsString)
            return value.AsString();
        return ConvertArgument<string>(value, false);
    }

    public string GetArgumentStringOrDefault(int index, string defaultValue)
    {
        if ((uint)index >= (uint)ArgumentCount)
            return defaultValue;
        return GetArgumentString(index);
    }

    public JsValue GetArgumentOrDefault(int index, JsValue defaultValue)
    {
        return (uint)index < (uint)ArgumentCount ? Realm.Stack[ArgumentOffset + index] : defaultValue;
    }

    public bool TryGetArgumentConversionScore<T>(int index, out int score)
    {
        return HostValueConverter.TryGetConversionScore<T>(Realm, GetArgument(index), out score);
    }

    public bool TryGetConversionScore<T>(in JsValue value, out int score)
    {
        return HostValueConverter.TryGetConversionScore<T>(Realm, value, out score);
    }

    private T ConvertArgument<T>(in JsValue value, bool isReceiver)
    {
        try
        {
            if (typeof(T) == typeof(JsValue)) return Unsafe.As<JsValue, T>(ref Unsafe.AsRef(in value));
            if (typeof(T) == typeof(bool))
            {
                var boolValue = value.ToBoolean();
                return Unsafe.As<bool, T>(ref boolValue);
            }

            if (value.IsNumber)
            {
                var numberValue = value.NumberValue;
                if (typeof(T).IsPrimitive)
                {
                    if (typeof(T) == typeof(double)) return Unsafe.As<double, T>(ref numberValue);

                    if (typeof(T) == typeof(float))
                    {
                        var floatValue = (float)numberValue;
                        return Unsafe.As<float, T>(ref floatValue);
                    }

                    if (typeof(T) == typeof(int))
                    {
                        var intValue = (int)numberValue;
                        return Unsafe.As<int, T>(ref intValue);
                    }

                    if (typeof(T) == typeof(long))
                    {
                        var longValue = (long)numberValue;
                        return Unsafe.As<long, T>(ref longValue);
                    }

                    if (typeof(T) == typeof(short))
                    {
                        var shortValue = (short)numberValue;
                        return Unsafe.As<short, T>(ref shortValue);
                    }

                    if (typeof(T) == typeof(byte))
                    {
                        var byteValue = (byte)numberValue;
                        return Unsafe.As<byte, T>(ref byteValue);
                    }

                    if (typeof(T) == typeof(uint))
                    {
                        var uintValue = (uint)numberValue;
                        return Unsafe.As<uint, T>(ref uintValue);
                    }

                    if (typeof(T) == typeof(ulong))
                    {
                        var ulongValue = (ulong)numberValue;
                        return Unsafe.As<ulong, T>(ref ulongValue);
                    }

                    if (typeof(T) == typeof(ushort))
                    {
                        var ushortValue = (ushort)numberValue;
                        return Unsafe.As<ushort, T>(ref ushortValue);
                    }

                    if (typeof(T) == typeof(sbyte))
                    {
                        var sbyteValue = (sbyte)numberValue;
                        return Unsafe.As<sbyte, T>(ref sbyteValue);
                    }
                }
            }

            if (value.IsString && typeof(T) == typeof(string))
            {
                var stringValue = value.AsString();
                return Unsafe.As<string, T>(ref stringValue);
            }

            return HostValueConverter.ConvertFromJsValue<T>(Realm, value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or OverflowException
                                       or FormatException)
        {
            ThrowHostBindingTypeError(isReceiver
                ? $"Host function called on incompatible receiver. Expected {typeof(T)}."
                : $"Host function argument type mismatch. Expected {typeof(T)}.");
            return default!;
        }
    }

    private static void ThrowHostBindingTypeError(string message)
    {
        throw new JsRuntimeException(JsErrorKind.TypeError, message);
    }
}
