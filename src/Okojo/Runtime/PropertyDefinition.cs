using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public readonly struct PropertyDefinition
{
    internal readonly JsValue Value1;
    internal readonly JsFunction? SetterFunction;
    internal readonly JsShapePropertyFlags Flags;

    /// <summary>
    ///     Duplicated implementation of PropertyDescriptor because 8byte alignment of PropertyDescriptor causes 32byte size,
    ///     KeyValuePair of int and PropertyDescriptor causes 40byte size. Inlining PropertyDescriptor into PropertyDefinition
    ///     allows us to keep the size of KeyValuePair of int and PropertyDefinition to 32byte.
    /// </summary>
    public readonly int Atom;

    public PropertyDescriptor Descriptor => new(Value1, SetterFunction, Flags);
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

    public bool IsAccessor => (Flags & JsShapePropertyFlags.BothAccessor) != 0;
    public bool Writable => (Flags & JsShapePropertyFlags.Writable) != 0;
    public bool Enumerable => (Flags & JsShapePropertyFlags.Enumerable) != 0;
    public bool Configurable => (Flags & JsShapePropertyFlags.Configurable) != 0;

    internal PropertyDefinition(int atom, JsValue value1, JsFunction? setterFunction, JsShapePropertyFlags flags)
    {
        Atom = atom;
        Value1 = value1;
        SetterFunction = setterFunction;
        Flags = flags;
    }

    public static PropertyDefinition OpenData(int atom, JsValue value)
    {
        var flags = DescriptorUtilities.BuildDataFlags(true, true, true);
        return new(atom, value, null, flags);
    }

    public static PropertyDefinition Mutable(int atom, JsValue value, bool enumerable = false)
    {
        var flags = DescriptorUtilities.BuildDataFlags(true, enumerable, true);
        return new(atom, value, null, flags);
    }

    public static PropertyDefinition Const(int atom, JsValue value, bool writable = false, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildDataFlags(writable, enumerable, configurable);

        return new(atom, value, null, flags);
    }

    public static PropertyDefinition Data(int atom, JsValue value, bool writable = false, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildDataFlags(writable, enumerable, configurable);

        return new(atom, value, null, flags);
    }

    public static PropertyDefinition GetterData(int atom, JsFunction getter, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildAccessorFlags(
            enumerable, configurable, true, false);

        return new(atom, JsValue.FromObject(getter), null, flags);
    }

    public static PropertyDefinition SetterData(int atom, JsFunction value, bool enumerable = false,
        bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildAccessorFlags(
            enumerable, configurable, false, true);

        return new(atom, JsValue.Undefined, value, flags);
    }

    public static PropertyDefinition GetterSetterData(int atom, JsFunction getter, JsFunction setter,
        bool enumerable = false, bool configurable = false)
    {
        var flags = DescriptorUtilities.BuildAccessorFlags(
            enumerable, configurable, true, true);

        return new(atom, JsValue.FromObject(getter), setter, flags);
    }
}
