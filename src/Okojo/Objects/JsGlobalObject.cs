using System.Runtime.CompilerServices;

namespace Okojo.Objects;

internal enum GlobalStoreResult : byte
{
    Success = 0,
    Unresolvable = 1,
    ReadOnly = 2,
    FunctionNotDefinable = 3
}

public sealed partial class JsGlobalObject : JsObject
{
    public JsGlobalObject(JsRealm realm) : base(realm)
    {
        Prototype = realm.ObjectPrototype;
    }

    internal bool TryGetPropertyAtom(JsRealm realm, int atom, out JsValue value)
    {
        ref var data = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref data))
        {
            value = globalValueEntries[data.Slot].Value;
            return true;
        }

        ref var descriptor = ref GetNamedDescriptorRef(atom);
        if (!Unsafe.IsNullRef(ref descriptor))
        {
            if (descriptor.IsAccessor)
            {
                var getter = descriptor.Getter;
                value = getter is null
                    ? JsValue.Undefined
                    : getter.Call(realm, JsValue.FromObject(this), ReadOnlySpan<JsValue>.Empty);
            }
            else
            {
                value = descriptor.Value;
            }

            return true;
        }

        if (TryGetOwnPropertySlotInfoAtom(atom, out var ownInfo))
        {
            value = GetNamedByCachedSlotInfo(realm, ownInfo);
            return true;
        }

        if (Prototype is not null &&
            Prototype.TryGetPropertyAtomWithReceiver(realm, this, atom, out value, out _))
            return true;

        value = JsValue.Undefined;
        return false;
    }

    internal bool TryGetPropertyAtomForGlobalCache(JsRealm realm, int atom, out JsValue value, out int globalSlot,
        out int globalVersion)
    {
        globalSlot = -1;
        globalVersion = 0;

        ref var data = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref data))
        {
            value = globalValueEntries[data.Slot].Value;
            globalSlot = data.Slot;
            globalVersion = globalValueEntries[data.Slot].Version;
            return true;
        }

        ref var descriptor = ref GetNamedDescriptorRef(atom);
        if (!Unsafe.IsNullRef(ref descriptor))
        {
            if (descriptor.IsAccessor)
            {
                var getter = descriptor.Getter;
                value = getter is null
                    ? JsValue.Undefined
                    : getter.Call(realm, JsValue.FromObject(this), ReadOnlySpan<JsValue>.Empty);
            }
            else
            {
                value = descriptor.Value;
            }

            return true;
        }

        if (TryGetOwnPropertySlotInfoAtom(atom, out var ownInfo))
        {
            value = GetNamedByCachedSlotInfo(realm, ownInfo);
            return true;
        }

        if (Prototype is not null &&
            Prototype.TryGetPropertyAtomWithReceiver(realm, this, atom, out value, out _))
            return true;

        value = JsValue.Undefined;
        return false;
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        ref var data = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref data))
        {
            value = globalValueEntries[data.Slot].Value;
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        ref var descriptor = ref GetNamedDescriptorRef(atom);
        if (!Unsafe.IsNullRef(ref descriptor))
        {
            if (descriptor.IsAccessor)
            {
                var getter = descriptor.Getter;
                value = getter is null
                    ? JsValue.Undefined
                    : getter.Call(realm, receiverValue, ReadOnlySpan<JsValue>.Empty);
            }
            else
            {
                value = descriptor.Value;
            }

            slotInfo = SlotInfo.Invalid;
            return true;
        }

        if (TryGetOwnPropertySlotInfoAtom(atom, out var ownInfo))
        {
            value = GetNamedByCachedSlotInfo(realm, ownInfo);
            slotInfo = ownInfo;
            return true;
        }

        if (Prototype is not null &&
            Prototype.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out _))
        {
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        value = JsValue.Undefined;
        slotInfo = SlotInfo.Invalid;
        return false;
    }

    internal void SetPropertyAtom(JsRealm realm, int atom, JsValue value)
    {
        SetPropertyAtom(realm, atom, value, out _);
    }

    internal void DefineGlobalBindingAtom(int atom, JsValue value)
    {
        ref var existing = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref existing))
        {
            if ((existing.Flags & JsShapePropertyFlags.Writable) == 0)
                return;
            existing = new(existing.Slot,
                DescriptorUtilities.BuildDataFlags(
                    true,
                    (existing.Flags & JsShapePropertyFlags.Enumerable) != 0,
                    (existing.Flags & JsShapePropertyFlags.Configurable) != 0));
            SetGlobalValue(existing, value);
            return;
        }

        ref var existingDescriptor = ref GetNamedDescriptorRef(atom);
        if (!Unsafe.IsNullRef(ref existingDescriptor))
        {
            if (!existingDescriptor.Writable)
                return;
            existingDescriptor = PropertyDescriptor.Data(value,
                true,
                existingDescriptor.Enumerable,
                existingDescriptor.Configurable);
            return;
        }

        if (!IsExtensibleFlag)
            return;

        var slotInfo = GetOrAddNamedDataSlot(atom, DescriptorUtilities.BuildDataFlags(true, false, true), out _);
        SetGlobalValue(slotInfo, value);
    }

    internal void DefineOwnGlobalDataPropertyAtom(int atom, JsValue value, bool writable, bool enumerable,
        bool configurable)
    {
        namedDescriptors?.Remove(atom);
        var slotInfo = GetOrAddNamedDataSlot(atom,
            DescriptorUtilities.BuildDataFlags(writable, enumerable, configurable), out _);
        SetGlobalValue(slotInfo, value);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (!ReferenceEquals(this, receiver))
            return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);

        // Descriptor-backed named globals (no shape transition).
        ref var current = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref current))
        {
            if ((current.Flags & JsShapePropertyFlags.Writable) == 0)
            {
                slotInfo = SlotInfo.Invalid;
                return false;
            }

            SetGlobalValue(current, value);
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        ref var descriptorCurrent = ref GetNamedDescriptorRef(atom);
        if (!Unsafe.IsNullRef(ref descriptorCurrent))
        {
            if (!descriptorCurrent.Writable)
            {
                slotInfo = SlotInfo.Invalid;
                return false;
            }

            descriptorCurrent = PropertyDescriptor.Data(
                value,
                descriptorCurrent.Writable,
                descriptorCurrent.Enumerable,
                descriptorCurrent.Configurable);
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        if (TryGetOwnPropertySlotInfoAtom(atom, out var ownInfo))
        {
            slotInfo = ownInfo;
            return SetNamedByCachedSlotInfo(realm, ownInfo, value);
        }

        if (!IsExtensibleFlag)
        {
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        var created = GetOrAddNamedDataSlot(atom, DescriptorUtilities.BuildDataFlags(true, true, true), out _);
        SetGlobalValue(created, value);
        slotInfo = SlotInfo.Invalid;
        return true;
    }

    internal GlobalStoreResult StoreGlobalAtom(JsRealm realm, int atom, JsValue value, bool strict,
        bool isInitializationStore, bool useFunctionDeclarationSemantics, bool useConfigurableInitializationSemantics)
    {
        if (strict && !isInitializationStore && !TryGetPropertyAtom(realm, atom, out _))
            return GlobalStoreResult.Unresolvable;

        if (isInitializationStore && useFunctionDeclarationSemantics)
        {
            if (!CanDeclareGlobalFunctionAtom(atom))
                return GlobalStoreResult.FunctionNotDefinable;

            if (useConfigurableInitializationSemantics)
            {
                if (TryGetOwnGlobalDescriptorAtom(atom, out var existingDescriptor))
                {
                    var updatedDescriptor = PropertyDescriptor.Data(
                        value,
                        existingDescriptor.Configurable || existingDescriptor.Writable,
                        existingDescriptor.Configurable || existingDescriptor.Enumerable,
                        existingDescriptor.Configurable);
                    if (TrySetOwnGlobalDescriptorAtom(atom, updatedDescriptor))
                        return GlobalStoreResult.Success;

                    var exactFlags = DescriptorUtilities.BuildDataFlags(
                        existingDescriptor.Configurable || existingDescriptor.Writable,
                        existingDescriptor.Configurable || existingDescriptor.Enumerable,
                        existingDescriptor.Configurable);
                    _ = DefineOwnDataPropertyExact(realm, atom, value, exactFlags);
                }
                else
                {
                    if (!IsExtensibleFlag)
                        return GlobalStoreResult.ReadOnly;

                    var slotInfo = GetOrAddNamedDataSlot(atom,
                        DescriptorUtilities.BuildDataFlags(true, true, true), out _);
                    SetGlobalValue(slotInfo, value);
                }

                return GlobalStoreResult.Success;
            }

            if (TryGetOwnGlobalDescriptorAtom(atom, out var scriptExistingDescriptor))
            {
                var updatedDescriptor = scriptExistingDescriptor.Configurable
                    ? PropertyDescriptor.Data(value, true, true)
                    : PropertyDescriptor.Data(
                        value,
                        scriptExistingDescriptor.Writable,
                        scriptExistingDescriptor.Enumerable,
                        scriptExistingDescriptor.Configurable);
                if (!TrySetOwnGlobalDescriptorAtom(atom, updatedDescriptor))
                {
                    var exactFlags = scriptExistingDescriptor.Configurable
                        ? DescriptorUtilities.BuildDataFlags(true, true, false)
                        : DescriptorUtilities.BuildDataFlags(
                            scriptExistingDescriptor.Writable,
                            scriptExistingDescriptor.Enumerable,
                            scriptExistingDescriptor.Configurable);
                    _ = DefineOwnDataPropertyExact(realm, atom, value, exactFlags);
                }

                return GlobalStoreResult.Success;
            }

            if (!IsExtensibleFlag)
                return GlobalStoreResult.ReadOnly;

            var functionSlot = GetOrAddNamedDataSlot(atom,
                DescriptorUtilities.BuildDataFlags(true, true, false), out _);
            SetGlobalValue(functionSlot, value);
            return GlobalStoreResult.Success;
        }

        // Global var declaration initialization for absent binding.
        if (isInitializationStore &&
            value.IsUndefined &&
            !TryGetOwnGlobalAtom(atom, out _))
        {
            if (!IsExtensibleFlag)
                return GlobalStoreResult.ReadOnly;

            var initSlot = GetOrAddNamedDataSlot(atom,
                DescriptorUtilities.BuildDataFlags(
                    true,
                    true,
                    useConfigurableInitializationSemantics), out _);
            SetGlobalValue(initSlot, JsValue.Undefined);
            return GlobalStoreResult.Success;
        }

        if (isInitializationStore &&
            value.IsUndefined &&
            TryGetOwnGlobalDescriptorAtom(atom, out _))
            return GlobalStoreResult.Success;

        return TrySetPropertyAtom(realm, atom, value, out _)
            ? GlobalStoreResult.Success
            : GlobalStoreResult.ReadOnly;
    }

    internal override bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        ref var dataDescriptor = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref dataDescriptor))
        {
            if ((dataDescriptor.Flags & JsShapePropertyFlags.Configurable) == 0)
                return false;
            InvalidateGlobalValue(dataDescriptor);
            namedData.Remove(atom);
            return true;
        }

        ref var descriptor = ref GetNamedDescriptorRef(atom);
        if (!Unsafe.IsNullRef(ref descriptor))
        {
            if (!descriptor.Configurable)
                return false;
            namedDescriptors!.Remove(atom);
            return true;
        }

        if (base.DeletePropertyAtom(realm, atom))
        {
            namedData.Remove(atom);
            namedDescriptors?.Remove(atom);
            return true;
        }

        return false;
    }

    internal override void CollectForInEnumerableStringAtomKeys(JsRealm realm, HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        if (namedData.Count != 0 || (namedDescriptors is not null && namedDescriptors.Count != 0))
        {
            var indexNames = new List<(uint Index, string Name)>(4);
            var stringNames = new List<string>(8);

            foreach (var entry in namedData)
            {
                if ((entry.Value.Flags & JsShapePropertyFlags.Enumerable) == 0)
                    continue;

                var atom = entry.Key;
                if (atom < 0)
                    continue;

                var key = realm.Atoms.AtomToString(atom);
                if (!visited.Add(key))
                    continue;

                if (TryGetArrayIndexFromCanonicalString(key, out var index))
                    indexNames.Add((index, key));
                else
                    stringNames.Add(key);
            }

            if (namedDescriptors is not null)
                foreach (var entry in namedDescriptors)
                {
                    if (!entry.Value.Enumerable)
                        continue;

                    var atom = entry.Key;
                    if (atom < 0)
                        continue;

                    var key = realm.Atoms.AtomToString(atom);
                    if (!visited.Add(key))
                        continue;

                    if (TryGetArrayIndexFromCanonicalString(key, out var index))
                        indexNames.Add((index, key));
                    else
                        stringNames.Add(key);
                }

            indexNames.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            for (var i = 0; i < indexNames.Count; i++)
                enumerableKeysOut.Add(indexNames[i].Name);
            enumerableKeysOut.AddRange(stringNames);
        }

        base.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        if (namedData.Count != 0)
            foreach (var entry in namedData)
            {
                if (enumerableOnly && (entry.Value.Flags & JsShapePropertyFlags.Enumerable) == 0)
                    continue;
                atomsOut.Add(entry.Key);
            }

        if (namedDescriptors is not null)
            foreach (var entry in namedDescriptors)
            {
                if (enumerableOnly && !entry.Value.Enumerable)
                    continue;
                atomsOut.Add(entry.Key);
            }

        base.CollectOwnNamedPropertyAtoms(realm, atomsOut, enumerableOnly);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true)
    {
        ref var data = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref data))
        {
            if (!needDescriptor)
            {
                descriptor = default;
                return true;
            }

            descriptor = PropertyDescriptor.Data(
                globalValueEntries[data.Slot].Value,
                (data.Flags & JsShapePropertyFlags.Writable) != 0,
                (data.Flags & JsShapePropertyFlags.Enumerable) != 0,
                (data.Flags & JsShapePropertyFlags.Configurable) != 0);
            return true;
        }

        ref var namedDescriptor = ref GetNamedDescriptorRef(atom);
        if (!Unsafe.IsNullRef(ref namedDescriptor))
        {
            descriptor = needDescriptor ? namedDescriptor : default;
            return true;
        }

        return base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor);
    }
}
