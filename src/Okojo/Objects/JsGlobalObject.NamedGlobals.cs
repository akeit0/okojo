using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Bytecode;

namespace Okojo.Objects;

public sealed partial class JsGlobalObject
{
    private readonly Dictionary<int, GlobalLexicalBinding> lexicalBindings = new();
    private readonly Dictionary<int, SlotInfo> namedData = new();
    private GlobalValueEntry[] globalValueEntries = Array.Empty<GlobalValueEntry>();
    private int globalValueEntryCount;
    private Dictionary<int, PropertyDescriptor>? namedDescriptors;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref SlotInfo GetNamedDataSlotRef(int atom)
    {
        return ref CollectionsMarshal.GetValueRefOrNullRef(namedData, atom);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref GlobalLexicalBinding GetLexicalBindingRef(int atom)
    {
        return ref CollectionsMarshal.GetValueRefOrNullRef(lexicalBindings, atom);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref GlobalValueEntry GetGlobalValueEntryRef(int slot)
    {
        if ((uint)slot >= (uint)globalValueEntryCount)
            return ref Unsafe.NullRef<GlobalValueEntry>();

        return ref globalValueEntries[slot];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextGlobalValueVersion(int currentVersion)
    {
        if (currentVersion == int.MaxValue)
            return 1;

        var nextVersion = currentVersion + 1;
        return nextVersion == 0 ? 1 : nextVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SlotInfo AllocateNamedDataSlot(JsShapePropertyFlags flags)
    {
        var slot = globalValueEntryCount++;
        if ((uint)slot >= (uint)globalValueEntries.Length)
        {
            var newLength = globalValueEntries.Length == 0 ? 16 : globalValueEntries.Length * 2;
            Array.Resize(ref globalValueEntries, newLength);
        }

        globalValueEntries[slot].Version = 1;
        return new(slot, flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SlotInfo GetOrAddNamedDataSlot(int atom, JsShapePropertyFlags flags, out bool exists)
    {
        ref var slotInfo = ref CollectionsMarshal.GetValueRefOrAddDefault(namedData, atom, out exists);
        if (!exists)
            slotInfo = AllocateNamedDataSlot(flags);
        else if (slotInfo.Flags != flags) slotInfo = new(slotInfo.Slot, flags);

        return slotInfo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int SetGlobalValue(SlotInfo slotInfo, in JsValue value)
    {
        ref var entry = ref globalValueEntries[slotInfo.Slot];
        entry.Value = value;
        entry.Version = NextGlobalValueVersion(entry.Version);
        return entry.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int SetGlobalValue(int slot, in JsValue value)
    {
        ref var entry = ref globalValueEntries[slot];
        entry.Value = value;
        entry.Version = NextGlobalValueVersion(entry.Version);
        return entry.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int InvalidateGlobalValue(SlotInfo slotInfo)
    {
        ref var entry = ref globalValueEntries[slotInfo.Slot];
        entry.Value = JsValue.Undefined;
        entry.Version = NextGlobalValueVersion(entry.Version);
        return entry.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int BumpGlobalValueVersion(SlotInfo slotInfo)
    {
        ref var entry = ref globalValueEntries[slotInfo.Slot];
        entry.Version = NextGlobalValueVersion(entry.Version);
        return entry.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref PropertyDescriptor GetNamedDescriptorRef(int atom)
    {
        if (namedDescriptors is null)
            return ref Unsafe.NullRef<PropertyDescriptor>();

        return ref CollectionsMarshal.GetValueRefOrNullRef(namedDescriptors, atom);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetCachedGlobalValue(int slot, int expectedVersion, out JsValue value)
    {
        ref var entry = ref globalValueEntries[slot];
        if (entry.Version == expectedVersion)
        {
            value = entry.Value;
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TrySetCachedGlobalValue(int slot, int expectedVersion, in JsValue value)
    {
        ref var entry = ref globalValueEntries[slot];
        if (entry.Version != expectedVersion) return false;

        entry.Value = value;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetOwnDataGlobalSlot(int atom, out int slot, out int version)
    {
        ref var slotRef = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref slotRef))
        {
            slot = slotRef.Slot;
            version = globalValueEntries[slot].Version;
            return true;
        }

        slot = -1;
        version = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetOwnWritableDataGlobalSlot(int atom, out int slot, out int version)
    {
        ref var slotRef = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref slotRef) && (slotRef.Flags & JsShapePropertyFlags.Writable) != 0)
        {
            slot = slotRef.Slot;
            version = globalValueEntries[slot].Version;
            return true;
        }

        slot = -1;
        version = 0;
        return false;
    }

    internal bool HasLexicalBindingAtom(int atom)
    {
        return !Unsafe.IsNullRef(ref GetLexicalBindingRef(atom));
    }

    internal bool TryGetLexicalBinding(int atom, out JsContext? context, out int slot, out bool isConst)
    {
        ref var binding = ref GetLexicalBindingRef(atom);
        if (!Unsafe.IsNullRef(ref binding))
        {
            context = binding.Context;
            slot = binding.Slot;
            isConst = binding.IsConst;
            return true;
        }

        context = null;
        slot = -1;
        isConst = false;
        return false;
    }

    internal void RegisterLexicalBindings(JsScript script, JsContext context)
    {
        var atoms = script.TopLevelLexicalAtoms;
        var slots = script.TopLevelLexicalSlots;
        var constFlags = script.TopLevelLexicalConstFlags;
        if (atoms is null || slots is null)
            return;

        var count = Math.Min(atoms.Length, slots.Length);
        for (var i = 0; i < count; i++)
        {
            var atom = atoms[i];
            var isConst = constFlags is not null && i < constFlags.Length && constFlags[i];
            lexicalBindings[atom] = new(context, slots[i], isConst);

            if (namedData.TryGetValue(atom, out var shadowedDataSlot))
                BumpGlobalValueVersion(shadowedDataSlot);
        }
    }

    internal bool TryGetOwnGlobalAtom(int atom, out JsValue value)
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
            value = descriptor.IsAccessor ? JsValue.Undefined : descriptor.Value;
            return true;
        }

        if (TryGetOwnPropertySlotInfoAtom(atom, out var info))
        {
            value = GetNamedSlotUnchecked(info.Slot);
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    internal bool TryGetOwnGlobalDescriptorAtom(int atom, out PropertyDescriptor descriptor)
    {
        ref var data = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref data))
        {
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
            descriptor = namedDescriptor;
            return true;
        }

        if (TryGetOwnPropertySlotInfoAtom(atom, out var info))
        {
            descriptor = PropertyDescriptor.Data(
                GetNamedSlotUnchecked(info.Slot),
                (info.Flags & JsShapePropertyFlags.Writable) != 0,
                (info.Flags & JsShapePropertyFlags.Enumerable) != 0,
                (info.Flags & JsShapePropertyFlags.Configurable) != 0);
            return true;
        }

        descriptor = default;
        return false;
    }

    internal bool TryGetNamedGlobalDescriptorAtom(int atom, out PropertyDescriptor descriptor)
    {
        ref var data = ref GetNamedDataSlotRef(atom);
        if (!Unsafe.IsNullRef(ref data))
        {
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
            descriptor = namedDescriptor;
            return true;
        }

        descriptor = default;
        return false;
    }

    internal bool HasRestrictedGlobalPropertyAtom(int atom)
    {
        if (TryGetOwnGlobalDescriptorAtom(atom, out var descriptor))
            return !descriptor.Configurable;
        return false;
    }

    internal bool CanDeclareGlobalFunctionAtom(int atom)
    {
        if (!TryGetOwnGlobalDescriptorAtom(atom, out var descriptor))
            return IsExtensibleFlag;

        if (descriptor.Configurable)
            return true;

        if (!descriptor.IsAccessor && descriptor.Writable && descriptor.Enumerable)
            return true;

        return false;
    }

    internal bool CanDeclareGlobalVarAtom(int atom)
    {
        return TryGetOwnGlobalDescriptorAtom(atom, out _) || IsExtensibleFlag;
    }

    internal bool TrySetOwnGlobalDescriptorAtom(int atom, in PropertyDescriptor descriptor)
    {
        var hadData = !Unsafe.IsNullRef(ref GetNamedDataSlotRef(atom));
        var hadDescriptor = !Unsafe.IsNullRef(ref GetNamedDescriptorRef(atom));
        if (!hadData && !hadDescriptor)
            return false;

        if (descriptor.IsAccessor)
        {
            if (namedData.TryGetValue(atom, out var removedSlot))
                InvalidateGlobalValue(removedSlot);
            namedData.Remove(atom);
            (namedDescriptors ??= new())[atom] = descriptor;
            return true;
        }

        namedDescriptors?.Remove(atom);
        var dataSlot = GetOrAddNamedDataSlot(atom,
            DescriptorUtilities.BuildDataFlags(descriptor.Writable, descriptor.Enumerable, descriptor.Configurable),
            out _);
        SetGlobalValue(dataSlot, descriptor.Value);
        return true;
    }

    internal IEnumerable<KeyValuePair<int, PropertyDescriptor>> EnumerateNamedGlobalDescriptors()
    {
        foreach (var entry in namedData)
            yield return new(entry.Key,
                PropertyDescriptor.Data(
                    globalValueEntries[entry.Value.Slot].Value,
                    (entry.Value.Flags & JsShapePropertyFlags.Writable) != 0,
                    (entry.Value.Flags & JsShapePropertyFlags.Enumerable) != 0,
                    (entry.Value.Flags & JsShapePropertyFlags.Configurable) != 0));

        if (namedDescriptors is null)
            yield break;

        foreach (var entry in namedDescriptors)
            yield return entry;
    }

    private readonly struct GlobalLexicalBinding(JsContext context, int slot, bool isConst)
    {
        public readonly JsContext Context = context;
        public readonly int Slot = slot;
        public readonly bool IsConst = isConst;
    }

    private struct GlobalValueEntry
    {
        public JsValue Value;
        public int Version;
    }
}
