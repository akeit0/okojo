namespace Okojo.Objects;

internal sealed class JsModuleNamespaceObject : JsObject
{
    private bool isLocked;

    public JsModuleNamespaceObject(JsRealm realm)
        : base(realm)
    {
        // Module namespace exotic objects have null [[Prototype]].
        Prototype = null;
        DefineDataPropertyAtom(realm, IdSymbolToStringTag, JsValue.FromString("Module"), JsShapePropertyFlags.None);
    }

    internal void LockForRuntimeMutation()
    {
        isLocked = true;
        PreventExtensions();
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (isLocked)
        {
            if (atom >= 0)
                _ = TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out _, true);
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    internal override bool SetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        if (isLocked)
        {
            _ = TryGetOwnElementDescriptor(index, out _);
            return false;
        }

        return base.SetElementWithReceiver(realm, receiver, index, value);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor, bool needDescriptor = true)
    {
        if (atom >= 0 && base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out _, false))
        {
            if (!needDescriptor)
            {
                descriptor = default;
                return true;
            }

            if (!TryGetPropertyAtom(realm, atom, out var value, out _))
            {
                descriptor = default;
                return false;
            }

            descriptor = PropertyDescriptor.Data(value, true, true);
            return true;
        }

        return base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor);
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        var stringAtoms = new List<int>();
        var symbolAtoms = new List<int>();

        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
        {
            if (enumerableOnly && (entry.Value.Flags & JsShapePropertyFlags.Enumerable) == 0)
                continue;

            if (entry.Key < 0)
                symbolAtoms.Add(entry.Key);
            else
                stringAtoms.Add(entry.Key);
        }

        stringAtoms.Sort((left, right) =>
            string.CompareOrdinal(realm.Atoms.AtomToString(left), realm.Atoms.AtomToString(right)));

        atomsOut.AddRange(stringAtoms);
        atomsOut.AddRange(symbolAtoms);
    }

    public override bool DeleteElement(uint index)
    {
        if (isLocked)
            return !HasOwnElement(index);
        return base.DeleteElement(index);
    }


    internal override bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        if (isLocked)
            return !HasOwnPropertyAtom(realm, atom);
        return base.DeletePropertyAtom(realm, atom);
    }

    internal override void FreezeDataProperties()
    {
        if (!isLocked)
        {
            base.FreezeDataProperties();
            return;
        }

        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
        {
            var atom = entry.Key;
            if (atom < 0 || atom == IdSymbolToStringTag)
                continue;

            if ((entry.Value.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) == 0)
                continue;

            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Cannot redefine property: {Realm.Atoms.AtomToString(atom)}",
                "DEFINE_PROPERTY_REDEFINE",
                errorRealm: Realm);
        }

        base.FreezeDataProperties();
    }

    internal override bool AreAllOwnPropertiesFrozen()
    {
        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
        {
            var atom = entry.Key;
            var flags = entry.Value.Flags;
            if ((flags & JsShapePropertyFlags.Configurable) != 0)
                return false;

            if (atom >= 0 &&
                atom != IdSymbolToStringTag &&
                (flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0)
                return false;
        }

        return true;
    }

    public override string ToString()
    {
        return FormatOwnEnumerablePropertiesForDisplay();
    }

    internal override string FormatForDisplay(int? indentSize, int depth, HashSet<JsObject> visited)
    {
        if (indentSize is null || indentSize <= 0)
            return FormatOwnEnumerablePropertiesForDisplay();
        return FormatOwnEnumerablePropertiesForDisplay(indentSize, depth, visited);
    }
}
