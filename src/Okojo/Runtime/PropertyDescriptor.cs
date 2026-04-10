using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public readonly struct PropertyDescriptor
{
    internal readonly JsValue Value1;
    internal readonly JsFunction? SetterFunction;
    internal readonly JsShapePropertyFlags Flags;

    public bool HasValue => (Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.None;
    public bool HasGetter => (Flags & JsShapePropertyFlags.HasGetter) != 0;
    public bool HasSetter => (Flags & JsShapePropertyFlags.HasSetter) != 0;

    public bool HasTwoValues => (Flags & JsShapePropertyFlags.BothAccessor) ==
                                JsShapePropertyFlags.BothAccessor;

    public JsValue Value => (Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.None
        ? Value1
        : JsValue.Undefined;

    public JsFunction? Getter => (Flags & JsShapePropertyFlags.HasGetter) != 0
        ? Unsafe.As<JsFunction>(Value1.Obj)
        : null;

    public JsFunction? Setter => SetterFunction;

    public bool IsAccessor => (Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
    public bool Writable => (Flags & JsShapePropertyFlags.Writable) != 0;
    public bool Enumerable => (Flags & JsShapePropertyFlags.Enumerable) != 0;
    public bool Configurable => (Flags & JsShapePropertyFlags.Configurable) != 0;

    internal PropertyDescriptor(JsValue value1, JsFunction? setterFunction, JsShapePropertyFlags flags)
    {
        Value1 = value1;
        SetterFunction = setterFunction;
        Flags = flags;
    }

    public static PropertyDescriptor OpenData(JsValue value)
    {
        var flags = DescriptorUtilities.BuildDataFlags(true, true, true);
        return new(value, null, flags);
    }

    public static PropertyDescriptor Mutable(JsValue value, bool enumerable = false)
    {
        var flags = DescriptorUtilities.BuildDataFlags(true, enumerable, true);
        return new(value, null, flags);
    }

    public static PropertyDescriptor Const(JsValue value, bool writable = false, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildDataFlags(writable, enumerable, configurable);

        return new(value, null, flags);
    }

    public static PropertyDescriptor Data(JsValue value, bool writable = false, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildDataFlags(writable, enumerable, configurable);

        return new(value, null, flags);
    }

    public static PropertyDescriptor GetterData(JsFunction getter, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildAccessorFlags(
            enumerable, configurable, true, false);

        return new(JsValue.FromObject(getter), null, flags);
    }

    public static PropertyDescriptor SetterData(JsFunction value, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildAccessorFlags(
            enumerable, configurable, false, true);

        return new(JsValue.Undefined, value, flags);
    }

    public static PropertyDescriptor GetterSetterData(JsFunction getter, JsFunction setter,
        bool enumerable = false, bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildAccessorFlags(
            enumerable, configurable, true, true);

        return new(JsValue.FromObject(getter), setter, flags);
    }
}
