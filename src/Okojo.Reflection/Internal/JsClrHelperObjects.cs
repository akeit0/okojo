using Okojo.Runtime;
using Okojo.Runtime.Interop;
using static Okojo.Runtime.AtomTable;

namespace Okojo.Objects;

internal readonly record struct OkojoClrUsingImport(string? NamespacePath, JsValue Value, bool IsNamespace);

internal sealed class JsClrTypedNullObject : JsObject, IClrTypedNullReference
{
    internal JsClrTypedNullObject(JsRealm realm, Type targetType)
        : base(realm)
    {
        TargetType = targetType;
        Prototype = realm.Intrinsics.ObjectPrototype;
        PreventExtensions();
    }

    private string DisplayTag => $"CLR Null {ClrDisplay.FormatTypeName(TargetType)}";

    public Type TargetType { get; }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (atom == IdSymbolToStringTag)
        {
            value = JsValue.FromString(DisplayTag);
            return true;
        }

        if (Prototype is not null && Prototype != this)
            return Prototype.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out _);

        value = JsValue.Undefined;
        return false;
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor, bool needDescriptor = true)
    {
        if (atom == IdSymbolToStringTag)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Const(JsValue.FromString(DisplayTag), false, false, true)
                : default;
            return true;
        }

        descriptor = default;
        return false;
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        if (!enumerableOnly)
            atomsOut.Add(IdSymbolToStringTag);
    }

    public override string ToString()
    {
        return $"[{DisplayTag}]";
    }
}

internal sealed class JsClrUsingResolverObject : JsObject
{
    private readonly List<OkojoClrUsingImport> imports = [];

    internal JsClrUsingResolverObject(JsRealm realm)
        : base(realm)
    {
        Prototype = realm.Intrinsics.ObjectPrototype;
        var addFunction = new JsHostFunction(realm, static (in info) =>
        {
            var resolver = (JsClrUsingResolverObject)((JsHostFunction)info.Function).UserData!;
            resolver.AddImports(info.Arguments);
            return JsValue.FromObject(resolver);
        }, "Add", 0)
        {
            UserData = this
        };
        _ = DefineOwnDataPropertyExact(realm, realm.Atoms.InternNoCheck("Add"), JsValue.FromObject(addFunction),
            JsShapePropertyFlags.None);
        _ = DefineOwnDataPropertyExact(realm, IdSymbolToStringTag, JsValue.FromString(DisplayTag),
            JsShapePropertyFlags.None);
        PreventExtensions();
    }

    private string DisplayTag => "CLR Using";

    internal void AddImports(ReadOnlySpan<JsValue> values)
    {
        for (var i = 0; i < values.Length; i++)
            AddImport(values[i]);
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        if (base.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo))
            return true;

        slotInfo = SlotInfo.Invalid;
        if (atom >= 0 && TryResolveImportMember(realm.Atoms.AtomToString(atom), out value))
            return true;

        value = JsValue.Undefined;
        return false;
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor, bool needDescriptor = true)
    {
        if (base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor))
            return true;

        if (atom >= 0 && TryResolveImportMember(realm.Atoms.AtomToString(atom), out var value))
        {
            descriptor = needDescriptor ? PropertyDescriptor.Const(value) : default;
            return true;
        }

        descriptor = default;
        return false;
    }

    public override string ToString()
    {
        return $"[{DisplayTag}]";
    }

    private void AddImport(in JsValue value)
    {
        if (JsRealm.TryExtractClrNamespacePath(value, out var namespacePath))
        {
            imports.Add(new(namespacePath, value, true));
            return;
        }

        if (JsRealm.TryExtractClrType(value, out _))
        {
            imports.Add(new(null, value, false));
            return;
        }

        throw new JsRuntimeException(JsErrorKind.TypeError, "$using accepts only CLR namespaces or CLR types.",
            "CLR_USING");
    }

    private bool TryResolveImportMember(string name, out JsValue value)
    {
        for (var i = 0; i < imports.Count; i++)
        {
            var import = imports[i];
            if (import.IsNamespace)
            {
                var combinedPath = string.IsNullOrEmpty(import.NamespacePath) ? name : $"{import.NamespacePath}.{name}";
                if (Realm.TryResolveClrPathExactly(combinedPath, out value))
                    return true;
                continue;
            }

            if (import.Value.TryGetObject(out var obj) && obj is JsObject okojoObj &&
                okojoObj.TryGetProperty(name, out value))
                return true;
        }

        value = JsValue.Undefined;
        return false;
    }
}

internal sealed class JsClrPlaceHolderObject : JsObject, IClrByRefPlaceholder
{
    private object? currentValue;

    internal JsClrPlaceHolderObject(JsRealm realm, Type targetType)
        : base(realm)
    {
        TargetType = targetType;
        Prototype = realm.Intrinsics.ObjectPrototype;
        PreventExtensions();
    }

    private string DisplayTag => $"CLR Place {ClrDisplay.FormatTypeName(TargetType)}";

    public Type TargetType { get; }
    public bool HasValue { get; private set; }

    public bool TryPrepareByRefValue(JsRealm realm, Type parameterType, bool allowUnset, out object? value,
        out int score)
    {
        score = 0;
        if (!parameterType.IsAssignableFrom(TargetType) && parameterType != TargetType)
        {
            value = null;
            return false;
        }

        score = parameterType == TargetType ? 0 : 2;
        if (HasValue)
        {
            value = currentValue;
            return true;
        }

        if (!allowUnset)
        {
            value = null;
            return false;
        }

        value = parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
        return true;
    }

    public void SetBoxedValue(object? value)
    {
        currentValue = value;
        HasValue = true;
    }

    internal void InitializeFromJsValue(JsRealm realm, in JsValue value)
    {
        currentValue = HostValueConverter.ConvertFromJsValue(realm, value, TargetType);
        HasValue = true;
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (atom == IdValue)
        {
            value = HasValue ? HostValueConverter.ConvertToJsValue(realm, currentValue) : JsValue.Undefined;
            return true;
        }

        if (atom == IdSymbolToStringTag)
        {
            value = JsValue.FromString(DisplayTag);
            return true;
        }

        if (Prototype is not null && Prototype != this)
            return Prototype.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out _);

        value = JsValue.Undefined;
        return false;
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (atom == IdValue && ReferenceEquals(receiver, this))
        {
            InitializeFromJsValue(realm, value);
            return true;
        }

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor, bool needDescriptor = true)
    {
        if (atom == IdValue)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Data(
                    HasValue ? HostValueConverter.ConvertToJsValue(realm, currentValue) : JsValue.Undefined, true,
                    false, true)
                : default;
            return true;
        }

        if (atom == IdSymbolToStringTag)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Const(JsValue.FromString(DisplayTag), false, false, true)
                : default;
            return true;
        }

        descriptor = default;
        return false;
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        if (!enumerableOnly)
        {
            atomsOut.Add(IdValue);
            atomsOut.Add(IdSymbolToStringTag);
        }
    }

    public override string ToString()
    {
        return $"[{DisplayTag}]";
    }
}
