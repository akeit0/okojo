namespace Okojo.Objects;

public abstract class JsFunction : JsObject
{
    protected FunctionFlags Flags;
    private JsObject? prototypePropertyObject;

    private protected JsFunction(JsRealm realm, string name,
        int length, StaticNamedPropertyLayout shape, bool isConstructor = true) : base(shape)
    {
        Prototype = realm.FunctionPrototype;
        Name = name ?? string.Empty;
        Length = length;
        Flags = FunctionFlags.HasLengthProperty | FunctionFlags.HasNameProperty;
        if (isConstructor)
            Flags |= FunctionFlags.IsConstructor;
    }

    protected JsFunction(JsRealm realm, string name = "",
        bool assignFunctionPrototype = true,
        int length = 0,
        bool hasPrototypeProperty = false,
        bool prototypeHasConstructor = true,
        bool isConstructor = true)
        : base(realm)
    {
        if (assignFunctionPrototype)
            Prototype = realm.FunctionPrototype;
        Name = name ?? string.Empty;
        Length = length;
        Flags = FunctionFlags.HasLengthProperty | FunctionFlags.HasNameProperty;
        if (isConstructor)
            Flags |= FunctionFlags.IsConstructor;

        if (hasPrototypeProperty)
        {
            Flags |= FunctionFlags.HasPrototypeProperty | FunctionFlags.PrototypeValuePending;
            if (prototypeHasConstructor)
                Flags |= FunctionFlags.PrototypeHasConstructor;
        }
    }

    public string Name { get; }
    public int Length { get; }
    public bool IsConstructor => (Flags & FunctionFlags.IsConstructor) != 0;

    public JsValue Call(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args)
    {
        return realm.InvokeFunction(this, thisValue, args);
    }

    internal virtual JsValue InvokeNonBytecodeCall(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        int callerPc)
    {
        throw new InvalidOperationException("Function does not implement non-bytecode [[Call]].");
    }

    internal virtual JsValue InvokeNonBytecodeConstruct(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        JsValue newTarget, int callerPc, CallFrameFlag flags)
    {
        throw new InvalidOperationException("Function does not implement non-bytecode [[Construct]].");
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, true, out var descriptor))
        {
            slotInfo = SlotInfo.Invalid;
            value = descriptor.Value;
            return true;
        }

        return base.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor, bool needDescriptor = true)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, needDescriptor, out descriptor))
            return true;

        return base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor);
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        AddOrderedFunctionOwnAtomIfVisible(realm, IdLength, atomsOut, enumerableOnly);
        AddOrderedFunctionOwnAtomIfVisible(realm, IdName, atomsOut, enumerableOnly);
        AddOrderedFunctionOwnAtomIfVisible(realm, IdPrototype, atomsOut, enumerableOnly);

        var scratch = new List<int>(8);
        base.CollectOwnNamedPropertyAtoms(realm, scratch, enumerableOnly);
        for (var i = 0; i < scratch.Count; i++)
        {
            var atom = scratch[i];
            if (atom == IdLength || atom == IdName || atom == IdPrototype)
                continue;
            atomsOut.Add(atom);
        }
    }

    internal override void CollectForInEnumerableStringAtomKeys(JsRealm realm, HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        AddOrderedFunctionEnumerableKeyIfVisible(realm, IdLength, visited, enumerableKeysOut);
        AddOrderedFunctionEnumerableKeyIfVisible(realm, IdName, visited, enumerableKeysOut);
        AddOrderedFunctionEnumerableKeyIfVisible(realm, IdPrototype, visited, enumerableKeysOut);
        base.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal override void DefineNewPropertiesNoCollision(JsRealm realm, ReadOnlySpan<PropertyDefinition> definitions)
    {
        for (var i = 0; i < definitions.Length; i++)
        {
            ref readonly var definition = ref definitions[i];
            var atom = definition.Atom;
            if (atom != IdLength && atom != IdName && atom != IdPrototype)
                continue;

            ClearIntrinsicOwnProperty(atom);
            if (atom == IdPrototype)
                ApplyKnownPrototypePropertyDefinition(in definition);
        }

        base.DefineNewPropertiesNoCollision(realm, definitions);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, false, out _))
            return TrySetIntrinsicOwnProperty(realm, atom, receiver, value, out slotInfo);

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    internal override bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, false, out var descriptor))
        {
            if (!descriptor.Configurable)
                return false;

            ClearIntrinsicOwnProperty(atom);
            return true;
        }

        var result = base.DeletePropertyAtom(realm, atom);
        if (result && atom == IdPrototype)
            prototypePropertyObject = null;
        return result;
    }

    internal override void DefineDataPropertyAtom(JsRealm realm, int atom, JsValue value, JsShapePropertyFlags flags)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, false, out _))
        {
            MaterializeIntrinsicDataProperty(realm, atom, value, flags);
            return;
        }

        base.DefineDataPropertyAtom(realm, atom, value, flags);
        if (atom == IdPrototype)
            ApplyPrototypePropertyValue(value);
    }

    internal override bool DefineOwnDataPropertyExact(JsRealm realm, int atom, JsValue value,
        JsShapePropertyFlags flags)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, false, out _))
        {
            MaterializeIntrinsicDataProperty(realm, atom, value, flags);
            return true;
        }

        var result = base.DefineOwnDataPropertyExact(realm, atom, value, flags);
        if (result && atom == IdPrototype)
            ApplyPrototypePropertyValue(value);
        return result;
    }

    internal override void DefineAccessorPropertyAtom(JsRealm realm, int atom, JsFunction? getter,
        JsFunction? setter, JsShapePropertyFlags flags)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, false, out _))
        {
            MaterializeIntrinsicAccessorProperty(realm, atom, getter, setter, flags);
            return;
        }

        base.DefineAccessorPropertyAtom(realm, atom, getter, setter, flags);
        if (atom == IdPrototype)
            prototypePropertyObject = null;
    }

    internal override bool DefineOwnAccessorPropertyExact(JsRealm realm, int atom, JsFunction? getter,
        JsFunction? setter, JsShapePropertyFlags flags)
    {
        var hasGetter = getter is not null;
        var hasSetter = setter is not null;
        if (!hasGetter && !hasSetter)
            return false;

        if (TryGetIntrinsicOwnDescriptor(atom, false, out _))
        {
            MaterializeIntrinsicAccessorProperty(realm, atom, getter, setter, flags);
            return true;
        }

        var result = base.DefineOwnAccessorPropertyExact(realm, atom, getter, setter, flags);
        if (result && atom == IdPrototype)
            prototypePropertyObject = null;
        return result;
    }

    internal override void FreezeDataProperties()
    {
        if ((Flags & FunctionFlags.HasLengthProperty) != 0)
            MaterializeIntrinsicDataProperty(Realm, IdLength, JsValue.FromInt32(Length),
                JsShapePropertyFlags.None);
        if ((Flags & FunctionFlags.HasNameProperty) != 0)
            MaterializeIntrinsicDataProperty(Realm, IdName, JsValue.FromString(Name),
                JsShapePropertyFlags.None);
        if ((Flags & FunctionFlags.HasPrototypeProperty) != 0)
            MaterializeIntrinsicDataProperty(Realm, IdPrototype, EnsurePrototypePropertyValue(),
                JsShapePropertyFlags.None);
        base.FreezeDataProperties();
    }

    internal override void SealDataProperties()
    {
        if ((Flags & FunctionFlags.HasLengthProperty) != 0)
            MaterializeIntrinsicDataProperty(Realm, IdLength, JsValue.FromInt32(Length),
                JsShapePropertyFlags.None);
        if ((Flags & FunctionFlags.HasNameProperty) != 0)
            MaterializeIntrinsicDataProperty(Realm, IdName, JsValue.FromString(Name),
                JsShapePropertyFlags.None);
        base.SealDataProperties();
    }

    internal override bool AreAllOwnPropertiesSealed()
    {
        if ((Flags & (FunctionFlags.HasLengthProperty | FunctionFlags.HasNameProperty)) != 0)
            return false;

        return base.AreAllOwnPropertiesSealed();
    }

    internal override bool AreAllOwnPropertiesFrozen()
    {
        if ((Flags & (FunctionFlags.HasLengthProperty | FunctionFlags.HasNameProperty |
                      FunctionFlags.HasPrototypeProperty)) != 0)
            return false;

        return base.AreAllOwnPropertiesFrozen();
    }

    protected virtual JsObject GetPrototypePropertyObjectPrototype(JsRealm realm)
    {
        return realm.Intrinsics.ObjectPrototype;
    }

    protected bool TryGetMaterializedPrototypePropertyObject(out JsObject prototypeObject)
    {
        if (prototypePropertyObject is not null)
        {
            prototypeObject = prototypePropertyObject;
            return true;
        }

        prototypeObject = null!;
        return false;
    }

    protected override void OnOwnNamedDataPropertyAssigned(int atom, in JsValue value)
    {
        if (atom == IdPrototype)
            ApplyPrototypePropertyValue(value);
    }

    public override string ToString()
    {
        return string.IsNullOrEmpty(Name) ? "[Function (anonymous)]" : $"[Function {Name}]";
    }

    private bool TryGetIntrinsicOwnDescriptor(int atom, bool materializePrototype, out PropertyDescriptor descriptor)
    {
        if (atom == IdLength && (Flags & FunctionFlags.HasLengthProperty) != 0)
        {
            descriptor = PropertyDescriptor.Data(JsValue.FromInt32(Length), configurable: true);
            return true;
        }

        if (atom == IdName && (Flags & FunctionFlags.HasNameProperty) != 0)
        {
            descriptor = PropertyDescriptor.Data(JsValue.FromString(Name), configurable: true);
            return true;
        }

        if (atom == IdPrototype && (Flags & FunctionFlags.HasPrototypeProperty) != 0)
        {
            var flag = JsShapePropertyFlags.None;
            if ((Flags & FunctionFlags.IsPrototypePropertyConst) == 0)
                flag |= JsShapePropertyFlags.Writable;
            descriptor = new(
                materializePrototype ? EnsurePrototypePropertyValue() : JsValue.Undefined,
                null,
                flag);
            return true;
        }

        descriptor = default;
        return false;
    }

    private bool TrySetIntrinsicOwnProperty(JsRealm realm, int atom, JsObject receiver, JsValue value,
        out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (atom != IdPrototype)
            return false;
        if ((Flags & FunctionFlags.IsPrototypePropertyConst) != 0)
            return false;

        if (!ReferenceEquals(this, receiver))
        {
            MaterializeIntrinsicDataProperty(realm, atom, EnsurePrototypePropertyValue(),
                JsShapePropertyFlags.Writable);
            return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
        }

        MaterializeIntrinsicDataProperty(realm, atom, value, JsShapePropertyFlags.Writable);
        return true;
    }

    private void MaterializeIntrinsicDataProperty(JsRealm realm, int atom, JsValue value, JsShapePropertyFlags flags)
    {
        ClearIntrinsicOwnProperty(atom);
        base.DefineDataPropertyAtom(realm, atom, value, flags);
        if (atom == IdPrototype)
            ApplyPrototypePropertyValue(value);
    }

    private void MaterializeIntrinsicAccessorProperty(JsRealm realm, int atom, JsFunction? getter,
        JsFunction? setter, JsShapePropertyFlags flags)
    {
        ClearIntrinsicOwnProperty(atom);
        base.DefineAccessorPropertyAtom(realm, atom, getter, setter, flags);
        if (atom == IdPrototype)
            prototypePropertyObject = null;
    }

    private void ClearIntrinsicOwnProperty(int atom)
    {
        if (atom == IdName)
        {
            Flags &= ~FunctionFlags.HasNameProperty;
            return;
        }

        if (atom == IdLength)
        {
            Flags &= ~FunctionFlags.HasLengthProperty;
            return;
        }


        if (atom == IdPrototype)
        {
            Flags &= ~(FunctionFlags.HasPrototypeProperty | FunctionFlags.PrototypeValuePending);
            prototypePropertyObject = null;
        }
    }

    private void AddOrderedFunctionOwnAtomIfVisible(JsRealm realm, int atom, List<int> atomsOut, bool enumerableOnly)
    {
        if (!TryGetOrderedFunctionOwnDescriptor(realm, atom, enumerableOnly && atom == IdPrototype,
                out var descriptor))
            return;
        if (enumerableOnly && !descriptor.Enumerable)
            return;
        atomsOut.Add(atom);
    }

    private void AddOrderedFunctionEnumerableKeyIfVisible(JsRealm realm, int atom, HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        if (!TryGetOrderedFunctionOwnDescriptor(realm, atom, false, out var descriptor) ||
            !descriptor.Enumerable)
            return;

        var key = realm.Atoms.AtomToString(atom);
        if (visited.Add(key))
            enumerableKeysOut.Add(key);
    }

    private bool TryGetOrderedFunctionOwnDescriptor(JsRealm realm, int atom, bool materializePrototype,
        out PropertyDescriptor descriptor)
    {
        if (TryGetIntrinsicOwnDescriptor(atom, materializePrototype, out descriptor))
            return true;

        return base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor);
    }

    internal void InitializePrototypeProperty(JsObject value)
    {
        if (prototypePropertyObject != null)
            throw new InvalidOperationException("Prototype property already initialized.");
        prototypePropertyObject = value;
        Flags |= FunctionFlags.HasPrototypeProperty | FunctionFlags.IsPrototypePropertyConst;
    }

    private void ApplyKnownPrototypePropertyDefinition(in PropertyDefinition definition)
    {
        if (definition.HasValue)
        {
            ApplyPrototypePropertyValue(definition.Value);
            return;
        }

        prototypePropertyObject = null;
    }

    private void ApplyPrototypePropertyValue(in JsValue value)
    {
        prototypePropertyObject = value.TryGetObject(out var prototypeObject) ? prototypeObject : null;
    }

    private JsValue EnsurePrototypePropertyValue()
    {
        if ((Flags & FunctionFlags.PrototypeValuePending) == 0 && prototypePropertyObject is not null)
            return JsValue.FromObject(prototypePropertyObject);

        if ((Flags & FunctionFlags.PrototypeValuePending) == 0)
            return JsValue.Undefined;

        var realm = Realm;
        var functionPrototypeObject = new JsPlainObject(realm, false)
        {
            Prototype = GetPrototypePropertyObjectPrototype(realm)
        };

        if ((Flags & FunctionFlags.PrototypeHasConstructor) != 0)
        {
            functionPrototypeObject.InitializeStorageFromCachedShape(realm.FunctionPrototypeObjectShape);
            functionPrototypeObject.SetNamedSlotUnchecked(JsRealm.FunctionPrototypeConstructorSlot,
                JsValue.FromObject(this));
        }
        else
        {
            functionPrototypeObject.InitializeStorageFromCachedShape(realm.FunctionPrototypeObjectShapeNoConstructor);
        }

        prototypePropertyObject = functionPrototypeObject;
        Flags &= ~FunctionFlags.PrototypeValuePending;
        return JsValue.FromObject(functionPrototypeObject);
    }

    [Flags]
    protected enum FunctionFlags
    {
        None = 0,
        HasNameProperty = 1 << 0,
        HasLengthProperty = 1 << 1,
        HasPrototypeProperty = 1 << 2,
        IsConstructor = 1 << 3,
        PrototypeHasConstructor = 1 << 4,
        PrototypeValuePending = 1 << 5,
        IsPrototypePropertyConst = 1 << 6
    }
}
