using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Okojo.Objects;

public class JsObject
{
    private const byte DeleteChurnPromotionThreshold = 2;
    private const byte RedefineChurnPromotionThreshold = 2;
    internal readonly JsObjectKind ObjectKind;
    private byte deleteChurn;
    protected internal Dictionary<uint, PropertyDescriptor>? IndexedProperties;
    protected bool IsExtensibleFlag = true;
    internal NamedPropertyLayout NamedPropertyLayout;
    private byte redefineChurn;
    internal JsValue[] SlotsArray = Array.Empty<JsValue>();

    protected JsObject(JsRealm realm, bool useDictionaryMode = false)
    {
        NamedPropertyLayout = useDictionaryMode
            ? new DynamicNamedPropertyLayout(realm)
            : realm.EmptyShape;
    }

    protected JsObject(StaticNamedPropertyLayout shape)
    {
        NamedPropertyLayout = shape;
        if (shape.StorageSlotCount != 0)
            SlotsArray = new JsValue[shape.StorageSlotCount];
    }

    public JsObject? Prototype { get; internal set; }
    internal virtual bool IsExtensible => IsExtensibleFlag;

    internal JsRealm Realm => NamedPropertyLayout.Owner;
    internal StaticNamedPropertyLayout Shape => StaticNamedPropertyLayout;
    internal JsValue[] Slots => SlotsArray;
    internal bool UsesDynamicNamedProperties => NamedPropertyLayout.IsDynamic;

    private StaticNamedPropertyLayout StaticNamedPropertyLayout =>
        (StaticNamedPropertyLayout)NamedPropertyLayout;

    private DynamicNamedPropertyLayout DynamicNamedPropertyLayout =>
        (DynamicNamedPropertyLayout)NamedPropertyLayout;

    public JsValue this[uint index]
    {
        get
        {
            if (TryGetElement(index, out var value))
                return value;
            return JsValue.Undefined;
        }
        set => _ = TrySetElement(index, value);
    }

    public JsValue this[string key]
    {
        get
        {
            if (TryGetProperty(key, out var value))
                return value;
            return JsValue.Undefined;
        }
        set
        {
            if (TryGetArrayIndexFromCanonicalString(key, out var elementIndex))
            {
                _ = TrySetElement(elementIndex, value);
                return;
            }

            var atom = NamedPropertyLayout.Owner.Atoms.InternNoCheck(key);
            _ = TrySetPropertyAtom(NamedPropertyLayout.Owner, atom, value, out _);
        }
    }

    internal void SetExtensibleFlag(bool value)
    {
        IsExtensibleFlag = value;
    }

    internal virtual JsObject? GetPrototypeOf(JsRealm realm)
    {
        return Prototype;
    }

    public string ToDisplayString(int? indentSize = null)
    {
        if (indentSize is null || indentSize <= 0)
            return ToString() ?? string.Empty;

        var visited = new HashSet<JsObject> { this };
        return FormatForDisplay(indentSize, 0, visited);
    }

    public IReadOnlyList<string> GetEnumerableOwnPropertyNames()
    {
        var atoms = new List<int>();
        CollectOwnNamedPropertyAtoms(Realm, atoms, true);
        if (atoms.Count == 0)
            return Array.Empty<string>();

        var names = new string[atoms.Count];
        for (var i = 0; i < atoms.Count; i++)
            names[i] = Realm.Atoms.AtomToString(atoms[i]);
        return names;
    }

    internal virtual bool TrySetPrototype(JsObject? proto)
    {
        if (ReferenceEquals(Prototype, proto))
            return true;
        if (ReferenceEquals(this, Realm.ObjectPrototype))
            return false;
        if (!IsExtensible)
            return false;

        var cursor = proto;
        while (cursor is not null)
        {
            if (ReferenceEquals(cursor, this))
                return false;
            cursor = cursor.Prototype;
        }

        Prototype = proto;
        return true;
    }

    public bool TryGetPropertyByAtom(int atom, out JsValue value)
    {
        return TryGetPropertyAtomWithReceiverValue(Realm, this, atom, out value, out _);
    }

    internal bool TryGetPropertyAtom(JsRealm realm, int atom, out JsValue value, out SlotInfo slotInfo)
    {
        return TryGetPropertyAtomWithReceiverValue(realm, this, atom, out value, out slotInfo);
    }

    internal bool TryGetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom,
        out JsValue value,
        out SlotInfo slotInfo)
    {
        return TryGetPropertyAtomWithReceiverValue(realm, receiver, atom, out value, out slotInfo);
    }

    internal virtual bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value,
        out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (NamedPropertyLayout.TryGetSlotInfo(atom, out var foundInfo))
        {
            var exposeStaticSlot = !NamedPropertyLayout.IsDynamic;
            if (exposeStaticSlot)
                slotInfo = foundInfo;

            var flags = foundInfo.Flags;
            ref var valRef = ref SlotsArray[foundInfo.Slot];
            if (valRef.IsTheHole)
                ILazyHostMethodProvider.GetOrCreateLazyHostMethod(atom, this, out valRef);
            if ((flags & JsShapePropertyFlags.HasGetter) != 0)
            {
                value = InvokeAccessorGetter(realm, receiverValue, ref valRef);
                return true;
            }

            if ((flags & JsShapePropertyFlags.HasSetter) == 0)
            {
                value = valRef;
                return true;
            }

            value = JsValue.Undefined;
            if (exposeStaticSlot)
                slotInfo = SlotInfo.Invalid;
            return true;
        }

        if (Prototype != null && Prototype != this)
            return Prototype.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out _);

        value = JsValue.Undefined;
        return false;
    }

    public bool TryGetElement(uint index, out JsValue value)
    {
        return TryGetElementWithReceiver(NamedPropertyLayout.Owner, this, index, out value);
    }

    internal virtual bool TryGetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, out JsValue value)
    {
        if (TryGetOwnElementDescriptor(index, out var descriptor))
        {
            if (descriptor.IsAccessor)
            {
                var getter = descriptor.Getter;
                if (getter is null)
                {
                    value = JsValue.Undefined;
                    return true;
                }

                value = InvokeAccessorFunction(realm, receiver, getter, ReadOnlySpan<JsValue>.Empty);
                return true;
            }

            value = descriptor.Value;
            return true;
        }

        if (Prototype is not null)
            return Prototype.TryGetElementWithReceiver(realm, receiver, index, out value);
        value = JsValue.Undefined;
        return false;
    }

    internal bool HasOwnElement(uint index)
    {
        return TryGetOwnElementDescriptor(index, out _);
    }

    internal virtual bool TrySetOwnElement(uint index, JsValue value, out bool hadOwnElement)
    {
        if (!TryGetOwnElementDescriptor(index, out var descriptor))
        {
            hadOwnElement = false;
            return false;
        }

        hadOwnElement = true;
        if (descriptor.IsAccessor)
        {
            if (descriptor.Setter is null)
                return false;

            var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
            _ = InvokeAccessorFunction(NamedPropertyLayout.Owner, this, descriptor.Setter, arg);
            return true;
        }

        if (!descriptor.Writable)
            return false;

        DefineElementDescriptor(index, new(value, null, descriptor.Flags));
        return true;
    }

    internal void SetPropertyAtom(JsRealm realm, int atom, JsValue value, out SlotInfo slotInfo)
    {
        _ = TrySetPropertyAtom(realm, atom, value, out slotInfo);
    }

    internal bool TrySetPropertyAtom(JsRealm realm, int atom, JsValue value, out SlotInfo slotInfo)
    {
        return SetPropertyAtomWithReceiver(realm, this, atom, value, out slotInfo);
    }

    internal virtual bool SetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        return SetElementCore(realm, receiver, index, value);
    }

    internal virtual bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        return SetPropertyAtomCore(realm, receiver, atom, value, out slotInfo);
    }

    private bool SetPropertyAtomCore(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (NamedPropertyLayout.TryGetSlotInfo(atom, out var foundInfo, out var hint))
        {
            if (!UsesDynamicNamedProperties)
                slotInfo = foundInfo;
            var flags = foundInfo.Flags;
            if ((flags & JsShapePropertyFlags.HasSetter) != 0)
                return TryInvokeAccessorSetter(realm, receiver, value, foundInfo);

            if ((flags & JsShapePropertyFlags.HasGetter) != 0 || (flags & JsShapePropertyFlags.Writable) == 0)
            {
                // getter-only accessor in non-strict mode: ignore assignment
                slotInfo = SlotInfo.Invalid;
                return false;
            }

            if (!ReferenceEquals(this, receiver))
            {
                if (receiver.TryDefineOwnDataPropertyForSet(realm, atom, value, out slotInfo))
                    return true;
                slotInfo = SlotInfo.Invalid;
                return false;
            }

            if (!UsesDynamicNamedProperties)
                slotInfo = foundInfo;
            SlotsArray[foundInfo.Slot] = value;
            OnOwnNamedDataPropertyAssigned(atom, value);
            return true;
        }

        if (Prototype is not null && Prototype != this &&
            TrySetInheritedDescriptor(realm, receiver, atom, value, out var inheritedHandled))
        {
            slotInfo = SlotInfo.Invalid;
            return inheritedHandled;
        }

        if (!ReferenceEquals(this, receiver))
        {
            if (receiver.TryDefineOwnDataPropertyForSet(realm, atom, value, out slotInfo))
                return true;
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        if (!receiver.IsExtensible)
        {
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        if (UsesDynamicNamedProperties)
        {
            AddDynamicDataProperty(atom, value, JsShapePropertyFlags.Open, hint);
            slotInfo = SlotInfo.Invalid;
            OnOwnNamedDataPropertyAssigned(atom, value);
            return true;
        }

        Transit(atom, value, out slotInfo);
        OnOwnNamedDataPropertyAssigned(atom, value);
        return true;
    }

    private void Transit(int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        var shape = StaticNamedPropertyLayout.GetOrAddTransition(atom, JsShapePropertyFlags.Open, out slotInfo);
        NamedPropertyLayout = shape;
        var required = shape.StorageSlotCount;
        if (required > SlotsArray.Length)
            Array.Resize(ref SlotsArray, required);
        SlotsArray[slotInfo.Slot] = value;
    }

    public void SetElement(uint index, JsValue value)
    {
        _ = TrySetElement(index, value);
    }

    internal bool TrySetElement(uint index, JsValue value)
    {
        return SetElementWithReceiver(NamedPropertyLayout.Owner, this, index, value);
    }

    private bool SetElementCore(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out var existing))
        {
            if (existing.IsAccessor)
            {
                if (existing.Setter is not null)
                {
                    var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                    _ = InvokeAccessorFunction(NamedPropertyLayout.Owner, this, existing.Setter, arg);
                }

                return existing.Setter is not null;
            }

            if (!existing.Writable)
                return false;

            if (!ReferenceEquals(this, receiver))
            {
                if (receiver.TryDefineOwnDataPropertyForSet(realm, index, value, out _))
                    return true;
                return false;
            }

            IndexedProperties[index] = new(value, null, existing.Flags);
            return true;
        }

        if (Prototype is not null &&
            TrySetInheritedElementDescriptor(realm, receiver, index, value, out var inheritedHandled))
            return inheritedHandled;

        if (TryGetOwnElementDescriptor(index, out var ownElementDescriptor))
        {
            if (ownElementDescriptor.IsAccessor)
            {
                if (ownElementDescriptor.Setter is not null)
                {
                    var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                    _ = InvokeAccessorFunction(NamedPropertyLayout.Owner, receiver, ownElementDescriptor.Setter, arg);
                    return true;
                }

                return false;
            }

            if (!ownElementDescriptor.Writable)
                return false;

            if (!ReferenceEquals(this, receiver))
            {
                if (receiver.TryDefineOwnDataPropertyForSet(realm, index, value, out _))
                    return true;
                return false;
            }
        }

        if (!ReferenceEquals(this, receiver))
        {
            if (receiver.TryDefineOwnDataPropertyForSet(realm, index, value, out _))
                return true;
            return false;
        }

        if (!receiver.IsExtensible)
            return false;

        (IndexedProperties ??= new())[index] = PropertyDescriptor.OpenData(value);
        return true;
    }

    public virtual bool DeleteElement(uint index)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out var descriptor))
        {
            if (!descriptor.Configurable)
                return false;
            IndexedProperties.Remove(index);
        }

        return true;
    }

    internal virtual bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        if (UsesDynamicNamedProperties)
        {
            if (!NamedPropertyLayout.TryGetSlotInfo(atom, out var dynamicInfo))
                return true;
            if ((dynamicInfo.Flags & JsShapePropertyFlags.Configurable) == 0)
                return false;

            RebuildDynamicLayoutExcluding(atom);
            return true;
        }

        if (!StaticNamedPropertyLayout.TryGetSlotInfo(atom, out var info))
            return true;
        if ((info.Flags & JsShapePropertyFlags.Configurable) == 0)
            return false;

        var shape = StaticNamedPropertyLayout;
        var currentEntries = shape.UnsafeEntries;
        var nextShape = shape.RebuildExcluding(atom);
        var nextEntries = nextShape.UnsafeEntries;
        var nextSlots = nextShape.StorageSlotCount == 0
            ? Array.Empty<JsValue>()
            : new JsValue[nextShape.StorageSlotCount];
        var nextIndex = 0;
        for (var i = 0; i < currentEntries.Length; i++)
        {
            ref readonly var oldEntry = ref currentEntries[i];
            if (oldEntry.Atom == atom)
                continue;

            CopyExistingSlots(nextSlots, nextEntries[nextIndex].SlotInfo, oldEntry.SlotInfo);
            nextIndex++;
        }

        NamedPropertyLayout = nextShape;
        SlotsArray = nextSlots;
        PromoteAfterDeleteChurn(realm);
        return true;
    }

    internal virtual void CollectForInEnumerableStringAtomKeys(
        JsRealm realm,
        HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        if (IndexedProperties is not null && IndexedProperties.Count != 0)
        {
            var indices = new uint[IndexedProperties.Count];
            var cursor = 0;
            foreach (var index in IndexedProperties.Keys)
                indices[cursor++] = index;
            Array.Sort(indices);

            for (var i = 0; i < indices.Length; i++)
            {
                if (!IndexedProperties.TryGetValue(indices[i], out var descriptor))
                    continue;
                if (!descriptor.Enumerable)
                    continue;
                var key = indices[i].ToString(CultureInfo.InvariantCulture);
                if (visited.Add(key))
                    enumerableKeysOut.Add(key);
            }
        }

        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
        {
            var atom = entry.Key;
            if (atom < 0)
                continue; // skip symbol keys
            var key = realm.Atoms.AtomToString(atom);
            if (!visited.Add(key))
                continue;
            if ((entry.Value.Flags & JsShapePropertyFlags.Enumerable) != 0)
                enumerableKeysOut.Add(key);
        }
    }

    internal bool TryGetOwnNamedSlotIndex(int atom, out int slot)
    {
        if (NamedPropertyLayout.TryGetSlotInfo(atom, out var info))
        {
            slot = info.Slot;
            return true;
        }

        slot = -1;
        return false;
    }

    internal bool TryGetOwnNamedDataSlotIndex(int atom, out int slot)
    {
        if (NamedPropertyLayout.TryGetSlotInfo(atom, out var info) &&
            (info.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) == 0)
        {
            slot = info.Slot;
            return true;
        }

        slot = -1;
        return false;
    }

    internal JsValue GetNamedSlotUnchecked(int slot)
    {
        return SlotsArray[slot];
    }

    internal void SetNamedSlotUnchecked(int slot, JsValue value)
    {
        SlotsArray[slot] = value;
    }

    internal void InitializeLiteralNamedSlot(int slot, JsValue value)
    {
        EnsureNamedSlotCapacity(slot + 1);
        SlotsArray[slot] = value;
    }

    internal void InitializeStorageFromCachedShape(StaticNamedPropertyLayout shape)
    {
        NamedPropertyLayout = shape;
        var count = shape.StorageSlotCount;
        SlotsArray = count == 0 ? Array.Empty<JsValue>() : new JsValue[count];
        if (count != 0)
            SlotsArray.AsSpan().Fill(JsValue.Undefined);
    }

    internal bool TryGetOwnPropertySlotInfoAtom(int atom, out SlotInfo info)
    {
        if (UsesDynamicNamedProperties)
        {
            info = SlotInfo.Invalid;
            return false;
        }

        return StaticNamedPropertyLayout.TryGetSlotInfo(atom, out info);
    }

    internal virtual bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out descriptor))
            return true;

        descriptor = default;
        return false;
    }

    internal virtual void DefineElementDescriptor(uint index, in PropertyDescriptor descriptor)
    {
        (IndexedProperties ??= new())[index] = descriptor;
    }

    internal virtual void PreventExtensions()
    {
        IsExtensibleFlag = false;
    }

    internal virtual void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        if (IndexedProperties is null || IndexedProperties.Count == 0)
            return;

        foreach (var kvp in IndexedProperties)
        {
            if (enumerableOnly && !kvp.Value.Enumerable)
                continue;
            indicesOut.Add(kvp.Key);
        }
    }

    internal bool HasOwnPropertyAtom(JsRealm realm, int atom)
    {
        return TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out _, false);
    }

    internal virtual bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor, bool needDescriptor = true)
    {
        return TryGetOwnNamedPropertyDescriptorFromLayout(atom, out descriptor, needDescriptor);
    }

    internal virtual void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        _ = realm;
        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
        {
            if (enumerableOnly && (entry.Value.Flags & JsShapePropertyFlags.Enumerable) == 0) continue;

            atomsOut.Add(entry.Key);
        }
    }

    internal virtual void DefineNewPropertiesNoCollision(JsRealm realm, ReadOnlySpan<PropertyDefinition> definitions)
    {
        _ = RequireCompatibleRealm(realm);

        if (definitions.Length == 0)
            return;

        var prevShape = StaticNamedPropertyLayout;
        var nextShape = prevShape.AppendNoCollision(definitions);
        var prevEntries = prevShape.UnsafeEntries;
        var nextEntries = nextShape.UnsafeEntries;
        var nextSlots = nextShape.StorageSlotCount == 0
            ? Array.Empty<JsValue>()
            : new JsValue[nextShape.StorageSlotCount];

        for (var i = 0; i < prevEntries.Length; i++)
            CopyExistingSlots(nextSlots, nextEntries[i].SlotInfo, prevEntries[i].SlotInfo);

        var nextIndex = prevEntries.Length;
        for (var i = 0; i < definitions.Length; i++)
        {
            ref readonly var def = ref definitions[i];
            ref readonly var slotInfo = ref nextEntries[nextIndex++].SlotInfo;
            if (def.HasTwoValues)
            {
                nextSlots[slotInfo.Slot] = def.Getter is null ? JsValue.Undefined : def.Getter;
                nextSlots[slotInfo.AccessorSetterSlot] =
                    def.Setter is null ? JsValue.Undefined : def.Setter;
                continue;
            }

            if (def.HasGetter)
            {
                nextSlots[slotInfo.Slot] = def.Getter is null ? JsValue.Undefined : def.Getter;
                continue;
            }

            if (def.HasSetter)
            {
                nextSlots[slotInfo.Slot] = def.Setter is null ? JsValue.Undefined : def.Setter;
                continue;
            }

            nextSlots[slotInfo.Slot] = def.Value;
        }

        NamedPropertyLayout = nextShape;
        SlotsArray = nextSlots;
    }

    protected virtual void OnOwnNamedDataPropertyAssigned(int atom, in JsValue value)
    {
        _ = atom;
        _ = value;
    }

    internal void InitializeDynamicOpenDataPropertiesNoCollision(
        JsRealm realm,
        ReadOnlySpan<int> atoms,
        ReadOnlySpan<JsValue> values)
    {
        var ownerRealm = RequireCompatibleRealm(realm);
        if (!UsesDynamicNamedProperties)
            throw new InvalidOperationException(
                "Dynamic open-data initialization requires dynamic named-property layout.");
        if (atoms.Length != values.Length)
            throw new ArgumentException("Atom and value counts must match.");
        if (atoms.Length == 0)
            return;
        if (SlotsArray.Length != 0)
            throw new InvalidOperationException(
                "Dynamic open-data initialization requires no existing named properties.");

        NamedPropertyLayout = DynamicNamedPropertyLayout.CreateOpenDataNoCollision(ownerRealm, atoms);
        SlotsArray = new JsValue[values.Length];
        values.CopyTo(SlotsArray);
    }

    internal bool HasAnyOwnElements()
    {
        return IndexedProperties is not null && IndexedProperties.Count != 0;
    }

    internal bool HasOwnPropertyKey(JsRealm realm, in JsValue key)
    {
        if (this is JsTypedArrayObject typedArray &&
            Intrinsics.TryHasTypedArrayIntegerIndexedElement(realm, typedArray, key, out var typedArrayHasProperty,
                out var typedArrayHandled))
            return typedArrayHandled && typedArrayHasProperty;

        if (key.IsString)
        {
            var text = key.AsString();
            if (TryGetArrayIndexFromCanonicalString(text, out var index))
                return HasOwnElement(index);
            var atom = realm.Atoms.InternNoCheck(text);
            return HasOwnPropertyAtom(realm, atom);
        }

        if (key.IsNumber)
        {
            var n = key.NumberValue;
            if (n >= 0 && n < uint.MaxValue && n == Math.Truncate(n))
            {
                var index = (uint)n;
                return HasOwnElement(index);
            }

            var atom = realm.Atoms.InternNoCheck(n.ToString(CultureInfo.InvariantCulture));
            return HasOwnPropertyAtom(realm, atom);
        }

        if (key.IsSymbol)
        {
            var atom = key.AsSymbol().Atom;
            return HasOwnPropertyAtom(realm, atom);
        }

        return false;
    }

    internal JsValue GetNamedByCachedSlotInfo(JsRealm realm, SlotInfo slotInfo)
    {
        return GetNamedValueBySlotInfo(realm, JsValue.FromObject(this), slotInfo);
    }

    internal bool SetNamedByCachedSlotInfo(JsRealm realm, SlotInfo slotInfo, in JsValue value)
    {
        return SetNamedValueBySlotInfo(realm, this, slotInfo, value);
    }

    private protected JsValue GetNamedValueBySlotInfo(JsRealm realm, in JsValue receiverValue, in SlotInfo slotInfo)
    {
        var flags = slotInfo.Flags;
        if ((flags & JsShapePropertyFlags.HasGetter) != 0)
            return InvokeAccessorGetter(realm, receiverValue, slotInfo);
        if ((flags & JsShapePropertyFlags.HasSetter) == 0)
            return SlotsArray[slotInfo.Slot];
        return JsValue.Undefined;
    }

    private protected bool SetNamedValueBySlotInfo(JsRealm realm, JsObject receiver, in SlotInfo slotInfo,
        in JsValue value)
    {
        var flags = slotInfo.Flags;
        if ((flags & JsShapePropertyFlags.HasSetter) != 0)
            return TryInvokeAccessorSetter(realm, receiver, value, slotInfo);
        if ((flags & JsShapePropertyFlags.HasGetter) != 0)
            return false;
        if ((flags & JsShapePropertyFlags.Writable) == 0)
            return false;

        SlotsArray[slotInfo.Slot] = value;
        return true;
    }

    private protected PropertyDescriptor BuildNamedDescriptorBySlotInfo(in SlotInfo slotInfo)
    {
        if ((slotInfo.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0)
        {
            var getterValue = (slotInfo.Flags & JsShapePropertyFlags.HasGetter) != 0
                ? SlotsArray[slotInfo.Slot]
                : JsValue.Undefined;
            JsFunction? setter = null;
            if ((slotInfo.Flags & JsShapePropertyFlags.HasSetter) != 0)
            {
                var setterSlot = (slotInfo.Flags & JsShapePropertyFlags.BothAccessor) ==
                                 JsShapePropertyFlags.BothAccessor
                    ? slotInfo.AccessorSetterSlot
                    : slotInfo.Slot;
                var setterValue = SlotsArray[setterSlot];
                if (!setterValue.IsUndefined && setterValue.TryGetObject(out var setterObj) &&
                    setterObj is JsFunction setterFn)
                    setter = setterFn;
            }

            return new(getterValue, setter, slotInfo.Flags);
        }

        return new(SlotsArray[slotInfo.Slot], null, slotInfo.Flags);
    }

    public bool TryGetOwnPropertyFlags(string name, out JsShapePropertyFlags flags)
    {
        var realm = NamedPropertyLayout.Owner;
        var atom = realm.Atoms.InternNoCheck(name);
        return TryGetOwnPropertyFlagsAtom(realm, atom, out flags);
    }

    internal virtual bool TryGetOwnPropertyFlagsAtom(JsRealm realm, int atom, out JsShapePropertyFlags flags)
    {
        if (TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor))
        {
            flags = descriptor.Flags;
            return true;
        }

        flags = JsShapePropertyFlags.None;
        return false;
    }

    public void DefineDataProperty(string name, JsValue value, JsShapePropertyFlags flags)
    {
        var realm = NamedPropertyLayout.Owner;
        var atom = realm.Atoms.InternNoCheck(name);
        DefineDataPropertyAtom(realm, atom, value, flags);
    }

    internal virtual void DefineDataPropertyAtom(JsRealm realm, int atom, JsValue value, JsShapePropertyFlags flags)
    {
        if (NamedPropertyLayout.TryGetSlotInfo(atom, out var existing))
        {
            // Preserve existing descriptor flags; only overwrite value on existing property.
            SlotsArray[existing.Slot] = value;
            return;
        }

        if (!IsExtensibleFlag)
            return;

        if (UsesDynamicNamedProperties)
        {
            DefineDataPropertyAtomDynamicAddNoInlining(atom, value, flags);
            return;
        }

        DefineDataPropertyAtomStaticAddNoInlining(atom, value, flags);
    }

    internal virtual bool DefineOwnDataPropertyExact(JsRealm realm, int atom, JsValue value,
        JsShapePropertyFlags flags)
    {
        var ownerRealm = RequireCompatibleRealm(realm);

        if (UsesDynamicNamedProperties)
            return DefineOwnDataPropertyExactDynamicSlow(atom, value, flags);

        if (ShouldPromoteForOwnDataRedefine(ownerRealm, atom, flags))
        {
            PromoteFastNamedPropertiesToDynamic(ownerRealm);
            return DefineOwnDataPropertyExact(realm, atom, value, flags);
        }

        return DefineOwnDataPropertyExactStaticSlow(atom, value, flags);
    }

    public void DefineAccessorProperty(string name, JsFunction? getter, JsFunction? setter,
        JsShapePropertyFlags flags = JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable)
    {
        var realm = NamedPropertyLayout.Owner;
        var atom = realm.Atoms.InternNoCheck(name);
        DefineAccessorPropertyAtom(realm, atom, getter, setter, flags);
    }

    internal virtual void DefineAccessorPropertyAtom(JsRealm realm, int atom, JsFunction? getter,
        JsFunction? setter,
        JsShapePropertyFlags flags)
    {
        if (getter is null && setter is null)
            throw new InvalidOperationException("Accessor property requires getter and/or setter.");
        if (getter is null != ((flags & JsShapePropertyFlags.HasGetter) == 0))
            flags = getter is null ? flags & ~JsShapePropertyFlags.HasGetter : flags | JsShapePropertyFlags.HasGetter;
        if (setter is null != ((flags & JsShapePropertyFlags.HasSetter) == 0))
            flags = setter is null ? flags & ~JsShapePropertyFlags.HasSetter : flags | JsShapePropertyFlags.HasSetter;

        if (UsesDynamicNamedProperties)
            DefineAccessorPropertyAtomDynamicSlow(atom, getter, setter, flags);
        else
            DefineAccessorPropertyAtomStaticSlow(atom, getter, setter, flags);
    }

    internal virtual bool DefineOwnAccessorPropertyExact(JsRealm realm, int atom, JsFunction? getter,
        JsFunction? setter,
        JsShapePropertyFlags flags)
    {
        var ownerRealm = RequireCompatibleRealm(realm);

        var hasGetter = (flags & JsShapePropertyFlags.HasGetter) != 0;
        var hasSetter = (flags & JsShapePropertyFlags.HasSetter) != 0;
        if (!hasGetter && !hasSetter)
            return false;

        if (UsesDynamicNamedProperties)
            return DefineOwnAccessorPropertyExactDynamicSlow(atom, getter, setter, flags, hasGetter, hasSetter);

        if (ShouldPromoteForOwnAccessorRedefine(ownerRealm, atom, flags))
        {
            PromoteFastNamedPropertiesToDynamic(ownerRealm);
            return DefineOwnAccessorPropertyExact(realm, atom, getter, setter, flags);
        }

        return DefineOwnAccessorPropertyExactStaticSlow(atom, getter, setter, flags, hasGetter, hasSetter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsRealm RequireCompatibleRealm(JsRealm realm)
    {
        var ownerRealm = NamedPropertyLayout.Owner ?? realm;
        if (!ReferenceEquals(ownerRealm.Agent, realm.Agent))
            throw new InvalidOperationException(
                "OkojoObject cannot be used across different OkojoVirtualMachine instances.");
        return ownerRealm;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool DefineOwnDataPropertyExactDynamicSlow(int atom, JsValue value, JsShapePropertyFlags flags)
    {
        if (DynamicNamedPropertyLayout.TryGetSlotInfo(atom, out var dynamicExisting))
        {
            var existingAccessor =
                (dynamicExisting.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            if (existingAccessor)
            {
                RebuildDynamicLayoutReplacingProperty(atom, flags, value, JsValue.Undefined);
                return true;
            }

            SlotsArray[dynamicExisting.Slot] = value;
            RewriteDynamicNamedPropertyFlags(atom, static (_, newFlags) => newFlags, flags);
            return true;
        }

        if (!IsExtensibleFlag)
            return false;

        AddDynamicDataProperty(atom, value, flags);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool DefineOwnDataPropertyExactStaticSlow(int atom, JsValue value, JsShapePropertyFlags flags)
    {
        if (StaticNamedPropertyLayout.TryGetSlotInfo(atom, out var existing))
        {
            var existingAccessor =
                (existing.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            if (existingAccessor)
            {
                RebuildShapeReplacingProperty(atom, flags, value, JsValue.Undefined);
                return true;
            }

            SlotsArray[existing.Slot] = value;
            RewriteOwnNamedPropertyFlags(atom, static (_, newFlags) => newFlags, flags);
            return true;
        }

        if (!IsExtensibleFlag)
            return false;

        var nextShape = StaticNamedPropertyLayout.GetOrAddTransition(atom, flags, out var slotInfo);
        NamedPropertyLayout = nextShape;
        if (nextShape.StorageSlotCount > SlotsArray.Length)
            Array.Resize(ref SlotsArray, nextShape.StorageSlotCount);
        SlotsArray[slotInfo.Slot] = value;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DefineAccessorPropertyAtomDynamicSlow(int atom, JsFunction? getter, JsFunction? setter,
        JsShapePropertyFlags flags)
    {
        if (DynamicNamedPropertyLayout.TryGetSlotInfo(atom, out var dynamicExisting))
        {
            var existingAccessor =
                (dynamicExisting.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            if (!existingAccessor)
            {
                RebuildDynamicLayoutReplacingProperty(
                    atom,
                    flags,
                    getter is null ? JsValue.Undefined : getter,
                    setter is null ? JsValue.Undefined : setter);
                return;
            }

            var existingEnumerable = (dynamicExisting.Flags & JsShapePropertyFlags.Enumerable) != 0;
            var existingConfigurable = (dynamicExisting.Flags & JsShapePropertyFlags.Configurable) != 0;
            var incomingEnumerable = (flags & JsShapePropertyFlags.Enumerable) != 0;
            var incomingConfigurable = (flags & JsShapePropertyFlags.Configurable) != 0;
            if (existingEnumerable != incomingEnumerable || existingConfigurable != incomingConfigurable)
                throw new NotSupportedException(
                    "Accessor descriptor redefinition with different flags is not supported yet.");

            var mergedGetter = getter;
            var mergedSetter = setter;
            if (mergedGetter is null && (dynamicExisting.Flags & JsShapePropertyFlags.HasGetter) != 0)
            {
                var getterValue = SlotsArray[dynamicExisting.Slot];
                if (!getterValue.IsUndefined &&
                    getterValue.TryGetObject(out var getterObj) &&
                    getterObj is JsFunction getterFn)
                    mergedGetter = getterFn;
            }

            if (mergedSetter is null && (dynamicExisting.Flags & JsShapePropertyFlags.HasSetter) != 0)
            {
                var setterSlot = (dynamicExisting.Flags & JsShapePropertyFlags.BothAccessor) ==
                                 JsShapePropertyFlags.BothAccessor
                    ? dynamicExisting.AccessorSetterSlot
                    : dynamicExisting.Slot;
                var setterValue = SlotsArray[setterSlot];
                if (!setterValue.IsUndefined &&
                    setterValue.TryGetObject(out var setterObj) &&
                    setterObj is JsFunction setterFn)
                    mergedSetter = setterFn;
            }

            var includeGetter = mergedGetter is not null;
            var includeSetter = mergedSetter is not null;
            var mergedFlags = DescriptorUtilities.BuildAccessorFlags(
                existingEnumerable, existingConfigurable, includeGetter, includeSetter);
            if (mergedFlags != dynamicExisting.Flags)
            {
                RebuildDynamicLayoutReplacingProperty(
                    atom,
                    mergedFlags,
                    mergedGetter is null ? JsValue.Undefined : mergedGetter,
                    mergedSetter is null ? JsValue.Undefined : mergedSetter);
                return;
            }

            WriteAccessorSlots(dynamicExisting, mergedGetter, mergedSetter);
            return;
        }

        if (!IsExtensibleFlag)
            return;

        AddDynamicAccessorProperty(atom, getter, setter, flags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DefineAccessorPropertyAtomStaticSlow(int atom, JsFunction? getter, JsFunction? setter,
        JsShapePropertyFlags flags)
    {
        var shape = StaticNamedPropertyLayout;
        if (shape.TryGetSlotInfo(atom, out var existing))
        {
            var existingAccessor =
                (existing.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            if (!existingAccessor)
            {
                RebuildShapeReplacingProperty(
                    atom,
                    flags,
                    getter is null ? JsValue.Undefined : getter,
                    setter is null ? JsValue.Undefined : setter);
                return;
            }

            var existingEnumerable = (existing.Flags & JsShapePropertyFlags.Enumerable) != 0;
            var existingConfigurable = (existing.Flags & JsShapePropertyFlags.Configurable) != 0;
            var incomingEnumerable = (flags & JsShapePropertyFlags.Enumerable) != 0;
            var incomingConfigurable = (flags & JsShapePropertyFlags.Configurable) != 0;
            if (existingEnumerable != incomingEnumerable || existingConfigurable != incomingConfigurable)
                throw new NotSupportedException(
                    "Accessor descriptor redefinition with different flags is not supported yet.");

            var existingHasGetter = (existing.Flags & JsShapePropertyFlags.HasGetter) != 0;
            var existingHasSetter = (existing.Flags & JsShapePropertyFlags.HasSetter) != 0;
            var incomingHasGetter = (flags & JsShapePropertyFlags.HasGetter) != 0;
            var incomingHasSetter = (flags & JsShapePropertyFlags.HasSetter) != 0;
            var mergedHasGetter = existingHasGetter || incomingHasGetter;
            var mergedHasSetter = existingHasSetter || incomingHasSetter;

            var mergedGetter = getter;
            var mergedSetter = setter;
            if (mergedGetter is null && existingHasGetter)
            {
                var getterValue = SlotsArray[existing.Slot];
                if (!getterValue.IsUndefined &&
                    getterValue.TryGetObject(out var getterObj) &&
                    getterObj is JsFunction getterFn)
                    mergedGetter = getterFn;
            }

            if (mergedSetter is null && existingHasSetter)
            {
                var existingSetterSlot = (existing.Flags & JsShapePropertyFlags.BothAccessor) ==
                                         JsShapePropertyFlags.BothAccessor
                    ? existing.AccessorSetterSlot
                    : existing.Slot;
                var setterValue = SlotsArray[existingSetterSlot];
                if (!setterValue.IsUndefined &&
                    setterValue.TryGetObject(out var setterObj) &&
                    setterObj is JsFunction setterFn)
                    mergedSetter = setterFn;
            }

            var mergedFlags = DescriptorUtilities.BuildAccessorFlags(
                existingEnumerable, existingConfigurable, mergedHasGetter, mergedHasSetter);
            if (mergedFlags != existing.Flags)
            {
                RebuildShapeReplacingProperty(
                    atom,
                    mergedFlags,
                    mergedGetter is null ? JsValue.Undefined : mergedGetter,
                    mergedSetter is null ? JsValue.Undefined : mergedSetter);
                return;
            }

            WriteAccessorSlots(existing, mergedGetter, mergedSetter);
            return;
        }

        if (!IsExtensibleFlag)
            return;

        shape = shape.GetOrAddTransition(atom, flags, out var slotInfo);
        NamedPropertyLayout = shape;
        if (shape.StorageSlotCount > SlotsArray.Length)
            Array.Resize(ref SlotsArray, shape.StorageSlotCount);
        WriteAccessorSlots(slotInfo, getter, setter);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool DefineOwnAccessorPropertyExactDynamicSlow(int atom, JsFunction? getter, JsFunction? setter,
        JsShapePropertyFlags flags, bool hasGetter, bool hasSetter)
    {
        if (DynamicNamedPropertyLayout.TryGetSlotInfo(atom, out var dynamicExisting))
        {
            var existingAccessor =
                (dynamicExisting.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            var getterValue = getter is null ? JsValue.Undefined : getter;
            var setterValue = setter is null ? JsValue.Undefined : setter;
            if (!existingAccessor)
            {
                RebuildDynamicLayoutReplacingProperty(atom, flags, getterValue, setterValue);
                return true;
            }

            var existingHasGetter = (dynamicExisting.Flags & JsShapePropertyFlags.HasGetter) != 0;
            var existingHasSetter = (dynamicExisting.Flags & JsShapePropertyFlags.HasSetter) != 0;
            if (existingHasGetter != hasGetter || existingHasSetter != hasSetter)
            {
                RebuildDynamicLayoutReplacingProperty(atom, flags, getterValue, setterValue);
                return true;
            }

            if (hasGetter && hasSetter)
            {
                SlotsArray[dynamicExisting.Slot] = getterValue;
                SlotsArray[dynamicExisting.AccessorSetterSlot] = setterValue;
            }
            else if (hasGetter)
            {
                SlotsArray[dynamicExisting.Slot] = getterValue;
            }
            else
            {
                SlotsArray[dynamicExisting.Slot] = setterValue;
            }

            RewriteDynamicNamedPropertyFlags(atom, static (_, newFlags) => newFlags, flags);
            return true;
        }

        if (!IsExtensibleFlag)
            return false;

        AddDynamicAccessorProperty(atom, getter, setter, flags);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool DefineOwnAccessorPropertyExactStaticSlow(int atom, JsFunction? getter, JsFunction? setter,
        JsShapePropertyFlags flags, bool hasGetter, bool hasSetter)
    {
        if (StaticNamedPropertyLayout.TryGetSlotInfo(atom, out var existing))
        {
            var existingAccessor =
                (existing.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            var getterValue = getter is null ? JsValue.Undefined : getter;
            var setterValue = setter is null ? JsValue.Undefined : setter;
            if (!existingAccessor)
            {
                RebuildShapeReplacingProperty(atom, flags, getterValue, setterValue);
                return true;
            }

            var existingHasGetter = (existing.Flags & JsShapePropertyFlags.HasGetter) != 0;
            var existingHasSetter = (existing.Flags & JsShapePropertyFlags.HasSetter) != 0;
            if (existingHasGetter != hasGetter || existingHasSetter != hasSetter)
            {
                RebuildShapeReplacingProperty(atom, flags, getterValue, setterValue);
                return true;
            }

            if (hasGetter && hasSetter)
            {
                SlotsArray[existing.Slot] = getterValue;
                SlotsArray[existing.AccessorSetterSlot] = setterValue;
            }
            else if (hasGetter)
            {
                SlotsArray[existing.Slot] = getterValue;
            }
            else
            {
                SlotsArray[existing.Slot] = setterValue;
            }

            RewriteOwnNamedPropertyFlags(atom, static (_, newFlags) => newFlags, flags);
            return true;
        }

        if (!IsExtensibleFlag)
            return false;

        var nextShape = StaticNamedPropertyLayout.GetOrAddTransition(atom, flags, out var slotInfo);
        NamedPropertyLayout = nextShape;
        if (nextShape.StorageSlotCount > SlotsArray.Length)
            Array.Resize(ref SlotsArray, nextShape.StorageSlotCount);
        WriteAccessorSlots(slotInfo, getter, setter);
        return true;
    }

    internal void DefineClassAccessorPropertyAtom(JsRealm realm, int atom, JsFunction? getter, JsFunction? setter)
    {
        var mergedGetter = getter;
        var mergedSetter = setter;
        var shape = StaticNamedPropertyLayout;
        if (shape.TryGetSlotInfo(atom, out var existing))
        {
            if ((existing.Flags & JsShapePropertyFlags.HasGetter) != 0 && mergedGetter is null)
            {
                var existingGetter = SlotsArray[existing.Slot];
                if (existingGetter.TryGetObject(out var existingGetterObj) && existingGetterObj is JsFunction fn)
                    mergedGetter = fn;
            }

            if ((existing.Flags & JsShapePropertyFlags.HasSetter) != 0 && mergedSetter is null)
            {
                var existingSetterSlot = (existing.Flags & JsShapePropertyFlags.BothAccessor) ==
                                         JsShapePropertyFlags.BothAccessor
                    ? existing.AccessorSetterSlot
                    : existing.Slot;
                var existingSetter = SlotsArray[existingSetterSlot];
                if (existingSetter.TryGetObject(out var existingSetterObj) && existingSetterObj is JsFunction fn)
                    mergedSetter = fn;
            }
        }

        var flags = DescriptorUtilities.BuildAccessorFlags(
            false,
            true,
            mergedGetter is not null,
            mergedSetter is not null);
        _ = DefineOwnAccessorPropertyExact(realm, atom, mergedGetter, mergedSetter, flags);
    }

    internal virtual void SealDataProperties()
    {
        if (IndexedProperties is not null)
        {
            var keys = IndexedProperties.Keys.ToArray();
            for (var i = 0; i < keys.Length; i++)
            {
                var index = keys[i];
                var descriptor = IndexedProperties[index];
                IndexedProperties[index] = new(
                    descriptor.Value,
                    descriptor.SetterFunction,
                    descriptor.Flags & ~JsShapePropertyFlags.Configurable);
            }
        }

        if (UsesDynamicNamedProperties)
        {
            foreach (var entry in EnumerateDynamicSlotInfos().ToArray())
                RewriteDynamicNamedPropertyFlags(
                    entry.Key,
                    static (existingFlags, _) => existingFlags & ~JsShapePropertyFlags.Configurable,
                    JsShapePropertyFlags.None);

            return;
        }

        RewriteOwnNamedPropertyFlags(static flags => flags & ~JsShapePropertyFlags.Configurable);
    }

    internal virtual void FreezeDataProperties()
    {
        if (IndexedProperties is not null)
        {
            var keys = IndexedProperties.Keys.ToArray();
            for (var i = 0; i < keys.Length; i++)
            {
                var index = keys[i];
                var descriptor = IndexedProperties[index];
                var flags = descriptor.Flags & ~JsShapePropertyFlags.Configurable;
                if ((descriptor.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) == 0)
                    flags &= ~JsShapePropertyFlags.Writable;
                IndexedProperties[index] = new(
                    descriptor.Value,
                    descriptor.SetterFunction,
                    flags);
            }
        }

        if (UsesDynamicNamedProperties)
        {
            foreach (var entry in EnumerateDynamicSlotInfos().ToArray())
            {
                var flags = entry.Value.Flags & ~JsShapePropertyFlags.Configurable;
                if ((entry.Value.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) == 0)
                    flags &= ~JsShapePropertyFlags.Writable;
                RewriteDynamicNamedPropertyFlags(entry.Key, static (_, newFlags) => newFlags, flags);
            }

            return;
        }

        RewriteOwnNamedPropertyFlags(static flags =>
        {
            flags &= ~JsShapePropertyFlags.Configurable;
            if ((flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) == 0)
                flags &= ~JsShapePropertyFlags.Writable;
            return flags;
        });
    }

    internal virtual bool AreAllOwnPropertiesSealed()
    {
        if (IndexedProperties is not null)
            foreach (var descriptor in IndexedProperties.Values)
                if ((descriptor.Flags & JsShapePropertyFlags.Configurable) != 0)
                    return false;

        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
            if ((entry.Value.Flags & JsShapePropertyFlags.Configurable) != 0)
                return false;

        return true;
    }

    internal virtual bool AreAllOwnPropertiesFrozen()
    {
        if (IndexedProperties is not null)
            foreach (var descriptor in IndexedProperties.Values)
            {
                if ((descriptor.Flags & JsShapePropertyFlags.Configurable) != 0)
                    return false;
                if (!descriptor.IsAccessor && (descriptor.Flags & JsShapePropertyFlags.Writable) != 0)
                    return false;
            }

        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
        {
            var flags = entry.Value.Flags;
            if ((flags & JsShapePropertyFlags.Configurable) != 0)
                return false;
            var isAccessor = (flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            if (!isAccessor && (flags & JsShapePropertyFlags.Writable) != 0)
                return false;
        }

        return true;
    }

    protected void EnsureDynamicNamedProperties()
    {
        if (UsesDynamicNamedProperties)
            return;

        if (StaticNamedPropertyLayout.PropertyCount == 0)
        {
            NamedPropertyLayout = new DynamicNamedPropertyLayout(NamedPropertyLayout.Owner);
            return;
        }

        PromoteFastNamedPropertiesToDynamic(NamedPropertyLayout.Owner);
    }

    protected void PromoteFastNamedPropertiesToDynamic(JsRealm realm)
    {
        if (UsesDynamicNamedProperties)
            return;

        var promoted = new DynamicNamedPropertyLayout(realm);
        foreach (var entry in StaticNamedPropertyLayout.EnumerateSlotInfos())
            promoted.SetSlotInfo(entry.Key, entry.Value);

        NamedPropertyLayout = promoted;
        deleteChurn = 0;
        redefineChurn = 0;
    }

    private void PromoteAfterDeleteChurn(JsRealm realm)
    {
        if (deleteChurn < byte.MaxValue)
            deleteChurn++;
        if (deleteChurn >= DeleteChurnPromotionThreshold)
            PromoteFastNamedPropertiesToDynamic(realm);
    }

    private bool ShouldPromoteForOwnDataRedefine(JsRealm realm, int atom, JsShapePropertyFlags flags)
    {
        if (!TryGetOwnNamedPropertyDescriptorAtomFromShape(atom, out var existing))
            return false;

        var churny = existing.IsAccessor || existing.Flags != flags;
        if (!churny)
            return false;

        if (redefineChurn < byte.MaxValue)
            redefineChurn++;
        return redefineChurn >= RedefineChurnPromotionThreshold;
    }

    private bool ShouldPromoteForOwnAccessorRedefine(JsRealm realm, int atom, JsShapePropertyFlags flags)
    {
        if (!TryGetOwnNamedPropertyDescriptorAtomFromShape(atom, out var existing))
            return false;

        var churny = !existing.IsAccessor || existing.Flags != flags;
        if (!churny)
            return false;

        if (redefineChurn < byte.MaxValue)
            redefineChurn++;
        return redefineChurn >= RedefineChurnPromotionThreshold;
    }

    private bool TryGetOwnNamedPropertyDescriptorAtomFromShape(int atom, out PropertyDescriptor descriptor)
    {
        if (StaticNamedPropertyLayout.TryGetSlotInfo(atom, out var info))
        {
            descriptor = CreatePropertyDescriptorFromSlotInfo(atom, info);
            return true;
        }

        descriptor = default;
        return false;
    }

    private bool TrySetInheritedDescriptor(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out bool handled)
    {
        var atomText = atom >= 0 ? realm.Atoms.AtomToString(atom) : null;
        double numericIndex = default;
        var hasCanonicalNumericIndex = atomText is not null &&
                                       Intrinsics.TryGetCanonicalNumericIndexString(realm, atomText,
                                           out numericIndex);
        for (var cursor = Prototype; cursor is not null; cursor = cursor.Prototype)
        {
            if (cursor is IProxyObject)
            {
                handled = cursor.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out _);
                return true;
            }

            if (cursor is JsTypedArrayObject typedArray && hasCanonicalNumericIndex)
            {
                if (ReferenceEquals(cursor, receiver))
                {
                    handled = Intrinsics.SetCanonicalNumericIndexOnTypedArrayForSet(typedArray, numericIndex, value);
                    return true;
                }

                if (!Intrinsics.IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex))
                {
                    handled = true;
                    return true;
                }

                break;
            }

            if (!cursor.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor))
                continue;

            if (descriptor.IsAccessor)
            {
                if (descriptor.Setter is null)
                {
                    handled = false;
                    return true;
                }

                var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                _ = InvokeAccessorFunction(realm, receiver, descriptor.Setter, arg);
                handled = true;
                return true;
            }

            if (!descriptor.Writable)
            {
                handled = false;
                return true;
            }

            break;
        }

        handled = false;
        return false;
    }

    protected bool TrySetInheritedElementDescriptor(JsRealm realm, JsObject receiver, uint index, JsValue value,
        out bool handled)
    {
        for (var cursor = Prototype; cursor is not null; cursor = cursor.Prototype)
        {
            if (cursor is IProxyObject)
            {
                handled = cursor.SetElementWithReceiver(realm, receiver, index, value);
                return true;
            }

            if (cursor is JsTypedArrayObject typedArray)
            {
                if (ReferenceEquals(cursor, receiver))
                {
                    handled = Intrinsics.SetCanonicalNumericIndexOnTypedArrayForSet(typedArray, index, value);
                    return true;
                }

                if (!Intrinsics.IsValidTypedArrayCanonicalNumericIndex(typedArray, index))
                {
                    handled = true;
                    return true;
                }

                break;
            }

            if (!cursor.TryGetOwnElementDescriptor(index, out var descriptor))
                continue;

            if (descriptor.IsAccessor)
            {
                if (descriptor.Setter is null)
                {
                    handled = false;
                    return true;
                }

                var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                _ = InvokeAccessorFunction(realm, receiver, descriptor.Setter, arg);
                handled = true;
                return true;
            }

            if (!descriptor.Writable)
            {
                handled = false;
                return true;
            }

            break;
        }

        handled = false;
        return false;
    }

    private bool TryGetOwnNamedPropertySlotInfoAny(int atom, out SlotInfo info)
    {
        return NamedPropertyLayout.TryGetSlotInfo(atom, out info);
    }

    private bool TryGetOwnNamedPropertyDescriptorFromLayout(int atom, out PropertyDescriptor descriptor,
        bool needDescriptor)
    {
        if (!TryGetOwnNamedPropertySlotInfoAny(atom, out var info))
        {
            descriptor = default;
            return false;
        }

        if (!needDescriptor)
        {
            descriptor = default;
            return true;
        }

        descriptor = CreatePropertyDescriptorFromSlotInfo(atom, info);
        return true;
    }

    private PropertyDescriptor CreatePropertyDescriptorFromSlotInfo(int atom, in SlotInfo info)
    {
        if ((info.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0)
        {
            var getterValue = (info.Flags & JsShapePropertyFlags.HasGetter) != 0
                ? GetSlotValueMaterialized(atom, info.Slot)
                : JsValue.Undefined;
            JsFunction? setter = null;
            if ((info.Flags & JsShapePropertyFlags.HasSetter) != 0)
            {
                var setterSlot = (info.Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor
                    ? info.AccessorSetterSlot
                    : info.Slot;
                var setterValue = GetSlotValueMaterialized(atom, setterSlot);
                if (!setterValue.IsUndefined &&
                    setterValue.TryGetObject(out var setterObj) &&
                    setterObj is JsFunction setterFn)
                    setter = setterFn;
            }

            return new(getterValue, setter, info.Flags);
        }

        return new(GetSlotValueMaterialized(atom, info.Slot), null, info.Flags);
    }

    private JsValue GetSlotValueMaterialized(int atom, int slot)
    {
        ref var slotRef = ref SlotsArray[slot];
        if (slotRef.IsTheHole)
            ILazyHostMethodProvider.GetOrCreateLazyHostMethod(atom, this, out slotRef);
        return slotRef;
    }

    private JsValue GetSlotValueMaterialized(int slot)
    {
        ref var slotRef = ref SlotsArray[slot];
        if (slotRef.IsTheHole)
            return JsValue.Undefined;
        return slotRef;
    }

    private IEnumerable<KeyValuePair<int, SlotInfo>> EnumerateDynamicSlotInfos()
    {
        return NamedPropertyLayout.EnumerateSlotInfos();
    }

    private void AddDynamicDataProperty(int atom, JsValue value, JsShapePropertyFlags flags, int hint = -1)
    {
        var dynamicLayout = Unsafe.As<DynamicNamedPropertyLayout>(NamedPropertyLayout);
        var slot = dynamicLayout.StorageSlotCount;
        if (SlotsArray.Length <= slot + 1) ResizeNamedSlotCapacity(slot + 1);

        SlotsArray[slot] = value;
        dynamicLayout.AddNewSlotInfoUnchecked(atom, new(slot, flags), hint);
    }

    private void AddDynamicAccessorProperty(int atom, JsFunction? getter, JsFunction? setter,
        JsShapePropertyFlags flags)
    {
        var width = (flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1;
        var slot = DynamicNamedPropertyLayout.StorageSlotCount;
        EnsureNamedSlotCapacity(slot + width);
        var slotInfo = new SlotInfo(slot, flags);
        WriteAccessorSlots(slotInfo, getter, setter);
        DynamicNamedPropertyLayout.AddNewSlotInfoUnchecked(atom, slotInfo);
    }

    private void EnsureNamedSlotCapacity(int minimumLength)
    {
        if (SlotsArray.Length >= minimumLength)
            return;

        var newLength = SlotsArray.Length == 0 ? 4 : SlotsArray.Length * 2;
        if (newLength < minimumLength)
            newLength = minimumLength;
        Array.Resize(ref SlotsArray, newLength);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResizeNamedSlotCapacity(int minimumLength)
    {
        if (SlotsArray.Length >= minimumLength)
            return;

        var newLength = SlotsArray.Length == 0 ? 4 : SlotsArray.Length * 2;
        if (newLength < minimumLength)
            newLength = minimumLength;
        Array.Resize(ref SlotsArray, newLength);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool SetPropertyAtomFallbackNoInlining(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (Prototype is not null && TrySetInheritedDescriptor(realm, receiver, atom, value, out var inheritedHandled))
        {
            slotInfo = SlotInfo.Invalid;
            return inheritedHandled;
        }

        if (!receiver.IsExtensible)
        {
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        if (UsesDynamicNamedProperties)
        {
            AddDynamicDataProperty(atom, value, JsShapePropertyFlags.Open);
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        var shape = StaticNamedPropertyLayout.GetOrAddTransition(atom, JsShapePropertyFlags.Open, out slotInfo);
        NamedPropertyLayout = shape;
        var required = shape.StorageSlotCount;
        if (required > SlotsArray.Length)
            Array.Resize(ref SlotsArray, required);
        SlotsArray[slotInfo.Slot] = value;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DefineDataPropertyAtomDynamicAddNoInlining(int atom, JsValue value, JsShapePropertyFlags flags)
    {
        AddDynamicDataProperty(atom, value, flags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DefineDataPropertyAtomStaticAddNoInlining(int atom, JsValue value, JsShapePropertyFlags flags)
    {
        var shape = StaticNamedPropertyLayout.GetOrAddTransition(atom, flags, out var slotInfo);
        NamedPropertyLayout = shape;
        if (shape.StorageSlotCount > SlotsArray.Length)
            Array.Resize(ref SlotsArray, shape.StorageSlotCount);
        SlotsArray[slotInfo.Slot] = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RewriteDynamicNamedPropertyFlags(int atom,
        Func<JsShapePropertyFlags, JsShapePropertyFlags, JsShapePropertyFlags> mapFlags,
        JsShapePropertyFlags newFlags)
    {
        NamedPropertyLayout = DynamicNamedPropertyLayout.RewriteFlags(atom, mapFlags, newFlags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RebuildDynamicLayoutExcluding(int excludedAtom)
    {
        var shape = DynamicNamedPropertyLayout;
        var currentEntries = shape.CopyLiveEntries();
        var nextShape = shape.RebuildExcluding(excludedAtom);
        var nextEntries = nextShape.UnsafeEntries;
        var nextSlots = nextShape.StorageSlotCount == 0
            ? Array.Empty<JsValue>()
            : new JsValue[nextShape.StorageSlotCount];
        var nextIndex = 0;
        for (var i = 0; i < currentEntries.Length; i++)
        {
            ref readonly var oldEntry = ref currentEntries[i];
            if (oldEntry.Atom == excludedAtom)
                continue;

            CopyExistingSlots(nextSlots, nextEntries[nextIndex].SlotInfo, oldEntry.SlotInfo);
            nextIndex++;
        }

        NamedPropertyLayout = nextShape;
        SlotsArray = nextSlots;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RebuildDynamicLayoutReplacingProperty(int targetAtom, JsShapePropertyFlags targetFlags,
        in JsValue targetPrimary,
        in JsValue targetSecondary)
    {
        var shape = DynamicNamedPropertyLayout;
        var currentEntries = shape.CopyLiveEntries();
        var nextShape = shape.RebuildReplacingProperty(targetAtom, targetFlags);
        var nextEntries = nextShape.UnsafeEntries;
        var nextSlots = nextShape.StorageSlotCount == 0
            ? Array.Empty<JsValue>()
            : new JsValue[nextShape.StorageSlotCount];
        for (var i = 0; i < currentEntries.Length; i++)
        {
            ref readonly var oldEntry = ref currentEntries[i];
            ref readonly var newEntry = ref nextEntries[i];
            if (oldEntry.Atom == targetAtom)
            {
                WriteSlotsForFlags(nextSlots, newEntry.SlotInfo, targetFlags, targetPrimary, targetSecondary);
                continue;
            }

            CopyExistingSlots(nextSlots, newEntry.SlotInfo, oldEntry.SlotInfo);
        }

        NamedPropertyLayout = nextShape;
        SlotsArray = nextSlots;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RewriteOwnNamedPropertyFlags(Func<JsShapePropertyFlags, JsShapePropertyFlags> mapFlags)
    {
        var shape = StaticNamedPropertyLayout;
        NamedPropertyLayout = shape.RewriteFlags(mapFlags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RewriteOwnNamedPropertyFlags(int atom,
        Func<JsShapePropertyFlags, JsShapePropertyFlags, JsShapePropertyFlags> mapFlags,
        JsShapePropertyFlags newFlags)
    {
        var shape = StaticNamedPropertyLayout;
        NamedPropertyLayout = shape.RewriteFlags(atom, mapFlags, newFlags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RebuildShapeReplacingProperty(int targetAtom, JsShapePropertyFlags targetFlags,
        in JsValue targetPrimary,
        in JsValue targetSecondary)
    {
        var shape = StaticNamedPropertyLayout;
        var currentEntries = shape.UnsafeEntries;
        var nextShape = shape.RebuildReplacingProperty(targetAtom, targetFlags);
        var nextEntries = nextShape.UnsafeEntries;
        var nextSlots = nextShape.StorageSlotCount == 0
            ? Array.Empty<JsValue>()
            : new JsValue[nextShape.StorageSlotCount];
        for (var i = 0; i < currentEntries.Length; i++)
        {
            ref readonly var oldEntry = ref currentEntries[i];
            ref readonly var newEntry = ref nextEntries[i];
            if (oldEntry.Atom == targetAtom)
            {
                WriteSlotsForFlags(nextSlots, newEntry.SlotInfo, targetFlags, targetPrimary, targetSecondary);
                continue;
            }

            CopyExistingSlots(nextSlots, newEntry.SlotInfo, oldEntry.SlotInfo);
        }

        NamedPropertyLayout = nextShape;
        SlotsArray = nextSlots;
    }

    private void CopyExistingSlots(JsValue[] destination, in SlotInfo destinationInfo, in SlotInfo sourceInfo)
    {
        if ((sourceInfo.Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor)
        {
            destination[destinationInfo.Slot] = SlotsArray[sourceInfo.Slot];
            destination[destinationInfo.AccessorSetterSlot] = SlotsArray[sourceInfo.AccessorSetterSlot];
            return;
        }

        destination[destinationInfo.Slot] = SlotsArray[sourceInfo.Slot];
    }

    private static void WriteSlotsForFlags(JsValue[] destination, in SlotInfo slotInfo, JsShapePropertyFlags flags,
        in JsValue primary, in JsValue secondary)
    {
        var hasGetter = (flags & JsShapePropertyFlags.HasGetter) != 0;
        var hasSetter = (flags & JsShapePropertyFlags.HasSetter) != 0;
        if (hasGetter && hasSetter)
        {
            destination[slotInfo.Slot] = primary;
            destination[slotInfo.AccessorSetterSlot] = secondary;
            return;
        }

        if (hasGetter)
        {
            destination[slotInfo.Slot] = primary;
            return;
        }

        if (hasSetter)
        {
            destination[slotInfo.Slot] = secondary;
            return;
        }

        destination[slotInfo.Slot] = primary;
    }

    private void WriteAccessorSlots(in SlotInfo slotInfo, JsFunction? getter, JsFunction? setter)
    {
        if ((slotInfo.Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor)
        {
            SlotsArray[slotInfo.Slot] = getter is null ? JsValue.Undefined : getter;
            SlotsArray[slotInfo.AccessorSetterSlot] = setter is null ? JsValue.Undefined : setter;
            return;
        }

        SlotsArray[slotInfo.Slot] = (slotInfo.Flags & JsShapePropertyFlags.HasGetter) != 0
            ? getter is null ? JsValue.Undefined : getter
            : setter is null
                ? JsValue.Undefined
                : setter;
    }

    private JsValue InvokeAccessorGetter(JsRealm realm, JsObject receiver, in SlotInfo slotInfo)
    {
        var fnVal = GetSlotValueMaterialized(slotInfo.Slot);
        if (!fnVal.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
            return JsValue.Undefined;
        return InvokeAccessorFunction(realm, receiver, fn, ReadOnlySpan<JsValue>.Empty);
    }

    private JsValue InvokeAccessorGetter(JsRealm realm, in JsValue receiverValue, in SlotInfo slotInfo)
    {
        var fnVal = GetSlotValueMaterialized(slotInfo.Slot);
        if (!fnVal.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
            return JsValue.Undefined;
        return InvokeAccessorFunction(realm, receiverValue, fn, ReadOnlySpan<JsValue>.Empty);
    }

    private JsValue InvokeAccessorGetter(JsRealm realm, in JsValue receiverValue, ref JsValue fnVal)
    {
        if (!fnVal.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
            return JsValue.Undefined;
        return InvokeAccessorFunction(realm, receiverValue, fn, ReadOnlySpan<JsValue>.Empty);
    }

    private void InvokeAccessorSetter(JsRealm realm, JsObject receiver, in JsValue value, SlotInfo slotInfo)
    {
        var setterSlot = (slotInfo.Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor
            ? slotInfo.AccessorSetterSlot
            : slotInfo.Slot;
        var fnVal = SlotsArray[setterSlot];
        var fn = (JsFunction)fnVal.Obj!;
        var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
        _ = InvokeAccessorFunction(realm, receiver, fn, arg);
    }

    private bool TryInvokeAccessorSetter(JsRealm realm, JsObject receiver, in JsValue value, SlotInfo slotInfo)
    {
        var setterSlot = (slotInfo.Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor
            ? slotInfo.AccessorSetterSlot
            : slotInfo.Slot;
        var fnVal = SlotsArray[setterSlot];
        if (fnVal.IsUndefined)
            return false;
        if (!fnVal.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Setter must be callable", "SETTER_NOT_CALLABLE");

        var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
        _ = InvokeAccessorFunction(realm, receiver, fn, arg);
        return true;
    }

    protected static JsValue InvokeAccessorFunction(JsRealm realm, JsObject receiver, JsFunction fn,
        ReadOnlySpan<JsValue> args)
    {
        return realm.InvokeFunction(fn, JsValue.FromObject(receiver), args);
    }

    protected static JsValue InvokeAccessorFunction(JsRealm realm, in JsValue receiverValue, JsFunction fn,
        ReadOnlySpan<JsValue> args)
    {
        return realm.InvokeFunction(fn, receiverValue, args);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        var realm = NamedPropertyLayout.Owner;
        if (TryGetArrayIndexFromCanonicalString(name, out var index))
            return TryGetElement(index, out value);
        var atom = realm.Atoms.InternNoCheck(name);
        return TryGetPropertyAtom(realm, atom, out value, out _);
    }

    public void SetProperty(string name, JsValue value)
    {
        var realm = NamedPropertyLayout.Owner;
        if (TryGetArrayIndexFromCanonicalString(name, out var index))
        {
            SetElement(index, value);
            return;
        }

        var atom = realm.Atoms.InternNoCheck(name);
        SetPropertyAtom(realm, atom, value, out _);
    }

    protected string FormatOwnEnumerablePropertiesForDisplay()
    {
        return FormatOwnEnumerablePropertiesForDisplay(null, 0, null);
    }

    protected string FormatOwnEnumerablePropertiesForDisplay(int? indentSize, int depth, HashSet<JsObject>? visited)
    {
        var realm = NamedPropertyLayout.Owner;
        var multiline = indentSize is > 0;
        var sb = new StringBuilder();
        if (multiline)
            sb.Append('{');
        else
            sb.Append("{ ");
        var first = true;

        if (IndexedProperties is not null && IndexedProperties.Count != 0)
        {
            var indices = new uint[IndexedProperties.Count];
            var cursor = 0;
            foreach (var index in IndexedProperties.Keys)
                indices[cursor++] = index;
            Array.Sort(indices);

            for (var i = 0; i < indices.Length; i++)
            {
                var index = indices[i];
                if (!IndexedProperties.TryGetValue(index, out var descriptor))
                    continue;
                if (!descriptor.Enumerable)
                    continue;
                if (!first)
                    sb.Append(multiline ? "," : ", ");
                first = false;
                if (multiline)
                {
                    sb.AppendLine();
                    sb.Append(new string(' ', (depth + 1) * indentSize!.Value));
                }

                var key = index.ToString(CultureInfo.InvariantCulture);
                sb.Append(FormatDisplayPropertyKey(key));
                sb.Append(": ");
                sb.Append(FormatDisplayValue(descriptor.Value, indentSize, depth + 1, visited));
            }
        }

        foreach (var entry in NamedPropertyLayout.EnumerateSlotInfos())
        {
            var atom = entry.Key;
            var info = entry.Value;
            if (atom < 0)
                continue;
            if ((info.Flags & JsShapePropertyFlags.Enumerable) == 0)
                continue;

            if (!first)
                sb.Append(multiline ? "," : ", ");
            first = false;
            if (multiline)
            {
                sb.AppendLine();
                sb.Append(new string(' ', (depth + 1) * indentSize!.Value));
            }

            sb.Append(FormatDisplayPropertyKey(realm.Atoms.AtomToString(atom)));
            sb.Append(": ");
            sb.Append(FormatDisplayValue(info, indentSize, depth + 1, visited));
        }

        if (multiline)
        {
            if (!first)
            {
                sb.AppendLine();
                sb.Append(new string(' ', depth * indentSize!.Value));
            }

            sb.Append('}');
        }
        else
        {
            sb.Append(" }");
        }

        return sb.ToString();
    }

    private string FormatDisplayValue(in SlotInfo info, int? indentSize, int depth, HashSet<JsObject>? visited)
    {
        var hasGetter = (info.Flags & JsShapePropertyFlags.HasGetter) != 0;
        var hasSetter = (info.Flags & JsShapePropertyFlags.HasSetter) != 0;
        if (hasGetter && hasSetter)
            return "[Getter/Setter]";
        if (hasGetter)
            return "[Getter]";
        if (hasSetter)
            return "[Setter]";
        return FormatDisplayValue(SlotsArray[info.Slot], indentSize, depth, visited);
    }

    private string FormatDisplayValue(in JsValue value, int? indentSize, int depth, HashSet<JsObject>? visited)
    {
        if (value.IsString)
            return $"'{value.AsString()}'";
        if (value.TryGetObject(out var obj))
        {
            if (ReferenceEquals(obj, this) || (visited is not null && visited.Contains(obj)))
                return "[Circular]";

            if (indentSize is > 0 && visited is not null)
            {
                visited.Add(obj);
                var rendered = obj.FormatForDisplay(indentSize, depth, visited);
                visited.Remove(obj);
                return rendered;
            }
        }

        return value.ToString() ?? string.Empty;
    }

    internal virtual string FormatForDisplay(int? indentSize, int depth, HashSet<JsObject> visited)
    {
        if (indentSize is null || indentSize <= 0)
            return FormatOwnEnumerablePropertiesForDisplay();
        return FormatOwnEnumerablePropertiesForDisplay(indentSize, depth, visited);
    }

    public override string ToString()
    {
        return FormatOwnEnumerablePropertiesForDisplay();
    }

    internal static string FormatDisplayPropertyKey(string key)
    {
        return IsDisplayUnquotedKey(key) ? key : $"'{EscapeSingleQuotedString(key)}'";
    }

    internal static bool IsDisplayUnquotedKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        var first = key[0];
        if (!(char.IsLetter(first) || first is '_' or '$'))
            return false;

        for (var i = 1; i < key.Length; i++)
        {
            var c = key[i];
            if (!(char.IsLetterOrDigit(c) || c is '_' or '$'))
                return false;
        }

        return true;
    }

    internal static string EscapeSingleQuotedString(string text)
    {
        if (text.IndexOfAny(['\\', '\'', '\r', '\n', '\t']) < 0)
            return text;

        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\'':
                    sb.Append("\\'");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }

        return sb.ToString();
    }
}
