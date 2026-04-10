namespace Okojo.Runtime;

internal readonly struct DescriptorRequestedBooleans
{
    internal readonly bool Writable;
    internal readonly bool Enumerable;
    internal readonly bool Configurable;

    internal DescriptorRequestedBooleans(bool writable, bool enumerable, bool configurable)
    {
        Writable = writable;
        Enumerable = enumerable;
        Configurable = configurable;
    }
}

internal static class DescriptorUtilities
{
    internal static bool IsRequestedTrue(bool hasAttribute, in JsValue value)
    {
        return hasAttribute && ToBooleanForDescriptor(value);
    }

    internal static DescriptorRequestedBooleans ReadRequestedBooleans(
        bool hasWritable, in JsValue writableValue,
        bool hasEnumerable, in JsValue enumerableValue,
        bool hasConfigurable, in JsValue configurableValue)
    {
        return new(
            IsRequestedTrue(hasWritable, writableValue),
            IsRequestedTrue(hasEnumerable, enumerableValue),
            IsRequestedTrue(hasConfigurable, configurableValue));
    }

    internal static JsShapePropertyFlags BuildDataFlags(bool writable, bool enumerable, bool configurable)
    {
        var flags = JsShapePropertyFlags.None;
        if (writable)
            flags |= JsShapePropertyFlags.Writable;
        if (enumerable)
            flags |= JsShapePropertyFlags.Enumerable;
        if (configurable)
            flags |= JsShapePropertyFlags.Configurable;
        return flags;
    }

    internal static JsShapePropertyFlags BuildAccessorFlags(bool enumerable, bool configurable, bool hasGetter,
        bool hasSetter)
    {
        var flags = JsShapePropertyFlags.None;
        if (enumerable)
            flags |= JsShapePropertyFlags.Enumerable;
        if (configurable)
            flags |= JsShapePropertyFlags.Configurable;
        if (hasGetter)
            flags |= JsShapePropertyFlags.HasGetter;
        if (hasSetter)
            flags |= JsShapePropertyFlags.HasSetter;
        return flags;
    }

    internal static bool ToBooleanForDescriptor(in JsValue value)
    {
        if (value.IsBool)
            return value.IsTrue;
        if (value.IsInt32)
            return value.Int32Value != 0;
        if (value.IsNumber)
        {
            var n = value.NumberValue;
            return n != 0 && !double.IsNaN(n);
        }

        if (value.IsString)
            return value.AsString().Length != 0;
        if (value.IsNull || value.IsUndefined)
            return false;
        return true;
    }
}
