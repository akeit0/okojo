namespace Okojo.Objects;

public delegate JsValue JsHostFunctionBody(
    scoped in CallInfo info);

public sealed class JsHostFunction : JsFunction, ILazyHostMethodProvider
{
    internal readonly JsHostFunctionBody BodyField;

    internal JsHostFunction(
        JsRealm realm,
        JsHostFunctionBody body,
        string name,
        int length, bool assignFunctionPrototype,
        bool isConstructor)
        : base(realm, name, assignFunctionPrototype, length,
            isConstructor: isConstructor)
    {
        BodyField = body;
    }

    public JsHostFunction(
        JsRealm realm,
        JsHostFunctionBody body,
        string name,
        int length,
        bool isConstructor = false)
        : base(realm, name, length: length, isConstructor: isConstructor)
    {
        BodyField = body;
    }

    public JsHostFunction(
        JsRealm realm, string name,
        int length,
        JsHostFunctionBody body,
        bool isConstructor = true
    )
        : base(realm, name, length: length, isConstructor: isConstructor)
    {
        BodyField = body;
    }

    private JsHostFunction(
        JsRealm realm,
        JsHostFunctionBody body,
        string name,
        int length, StaticNamedPropertyLayout shape,
        bool isConstructor = true)
        : base(realm, name, length, shape, isConstructor)
    {
        BodyField = body;
    }

    public JsHostFunctionBody Body => BodyField;
    public object? UserData { get; set; }

    public bool TryGetOrCreateLazyHostMethod(JsRealm realm, int atom, out JsValue method)
    {
        if (UserData is IClrTypeFunctionData typeData &&
            typeData.LayoutInfo.TryGetOrCreateMethodValue(realm, atom, out method))
            return true;

        method = JsValue.TheHole;
        return false;
    }

    public static JsHostFunction CreateEmptyShapedFunction(JsRealm realm, JsHostFunctionBody body, string name,
        int length, bool isConstructor = true)
    {
        return new(realm, body, name, length, realm.EmptyShape, isConstructor);
    }

    internal static JsHostFunction CreateShapedFunction(JsRealm realm, JsHostFunctionBody body, string name,
        int length, StaticNamedPropertyLayout shape, bool isConstructor = true)
    {
        return new(realm, body, name, length, shape, isConstructor);
    }

    public JsValue Invoke(scoped in CallInfo info)
    {
        return BodyField(in info);
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        if (atom == IdSymbolToStringTag && UserData is IClrTypeFunctionData typeData)
        {
            slotInfo = SlotInfo.Invalid;
            value = JsValue.FromString(typeData.DisplayTag);
            return true;
        }

        if (!base.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo))
            return false;

        if (value.IsTheHole)
            value = MaterializeLazyMethodValueIfNeeded(realm, atom, slotInfo, value);
        return true;
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true)
    {
        if (atom == IdSymbolToStringTag && UserData is IClrTypeFunctionData typeData)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Const(JsValue.FromString(typeData.DisplayTag), false, false,
                    true)
                : default;
            return true;
        }

        if (!base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor))
            return false;

        if (!needDescriptor || !descriptor.HasValue || !descriptor.Value.IsTheHole)
            return true;
        if (!NamedPropertyLayout.TryGetSlotInfo(atom, out var slotInfo))
            return true;

        _ = MaterializeLazyMethodValueIfNeeded(realm, atom, slotInfo, descriptor.Value);
        descriptor = BuildNamedDescriptorBySlotInfo(slotInfo);
        return true;
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        base.CollectOwnNamedPropertyAtoms(realm, atomsOut, enumerableOnly);
        if (!enumerableOnly && UserData is IClrTypeFunctionData)
            atomsOut.Add(IdSymbolToStringTag);
    }

    internal override JsValue InvokeNonBytecodeCall(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        int callerPc)
    {
        return realm.InvokeHostFunctionWithExitFrame(this, thisValue, args, callerPc, JsValue.Undefined);
    }

    internal override JsValue InvokeNonBytecodeConstruct(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        JsValue newTarget, int callerPc, CallFrameFlag flags)
    {
        return realm.InvokeHostFunctionWithExitFrame(this, thisValue, args, callerPc, newTarget, flags);
    }

    public override string ToString()
    {
        if (UserData is IClrTypeFunctionData typeData)
            return $"[{typeData.DisplayTag}]";
        return base.ToString();
    }

    private JsValue MaterializeLazyMethodValueIfNeeded(JsRealm realm, int atom, in SlotInfo slotInfo, in JsValue value)
    {
        if (!value.IsTheHole)
            return value;
        if (UserData is not IClrTypeFunctionData typeData)
            return value;

        var flags = slotInfo.Flags;
        if ((flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != JsShapePropertyFlags.None)
            return value;

        if (typeData.LayoutInfo.TryGetOrCreateMethodValue(realm, atom, out var methodValue))
        {
            Slots[slotInfo.Slot] = methodValue;
            return methodValue;
        }

        return value;
    }
}
