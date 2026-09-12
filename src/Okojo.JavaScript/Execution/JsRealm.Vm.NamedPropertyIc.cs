using System.Runtime.CompilerServices;
using Okojo.JavaScript.Bytecode;

namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUseNamedPropertyIc(
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        bool receiverIsObject,
        JsObject obj,
        int atom,
        out SlotInfo slotInfo
    )
    {
        if (namedPropertyIcEntries is null || !receiverIsObject || obj.UsesDynamicNamedProperties)
        {
            slotInfo = SlotInfo.Invalid;
            return false;
        }

#if DEBUG
        if ((uint)icSlot >= (uint)namedPropertyIcEntries.Length)
            throw new InvalidOperationException("Named property feedback slot is out of range.");
#endif

        ref readonly var ic = ref namedPropertyIcEntries[icSlot];
        if (!ReferenceEquals(obj.Shape, ic.Shape))
        {
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        slotInfo = ic.SlotInfo;
        return true
#if DEBUG
            && ic.NameAtom == atom
#endif
        ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUsePrototypeNamedPropertyIc(
        OkojoPrototypeNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        bool receiverIsObject,
        JsObject obj,
        int atom,
        out JsObject? holder,
        out SlotInfo slotInfo
    )
    {
        holder = null;
        if (namedPropertyIcEntries is null || !receiverIsObject || obj.UsesDynamicNamedProperties)
        {
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        ref readonly var ic = ref namedPropertyIcEntries[icSlot];
        var cachedHolder = ic.Holder;
        if (
            cachedHolder is null
            || !ReferenceEquals(obj.Shape, ic.ReceiverShape)
            || cachedHolder.UsesDynamicNamedProperties
            || !ReferenceEquals(obj.Prototype, cachedHolder)
            || !ReferenceEquals(cachedHolder.Shape, ic.HolderShape)
        )
        {
            slotInfo = SlotInfo.Invalid;
            return false;
        }

        holder = cachedHolder;
        slotInfo = ic.SlotInfo;
        return true
#if DEBUG
            && ic.NameAtom == atom
#endif
        ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanCacheNamedPropertyResult(
        bool receiverIsObject,
        JsObject obj,
        in SlotInfo slotInfo
    )
    {
        return receiverIsObject && slotInfo.IsValid && !obj.UsesDynamicNamedProperties;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryGetNamedPropertyFromPrototypeIc(
        OkojoPrototypeNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        bool receiverIsObject,
        JsObject obj,
        int atom,
        out JsValue value
    )
    {
        if (
            CanUsePrototypeNamedPropertyIc(
                namedPropertyIcEntries,
                icSlot,
                receiverIsObject,
                obj,
                atom,
                out var holder,
                out var slotInfo
            )
        )
        {
            value = holder!.GetNamedByCachedSlotInfo(this, slotInfo);
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdateNamedPropertyIcAfterGet(
        JsScript script,
        ref OkojoPrototypeNamedPropertyIcEntry[]? prototypeNamedPropertyIcEntries,
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        bool receiverIsObject,
        JsObject obj,
        int atom,
        in SlotInfo slotInfo
    )
    {
        if (CanCacheNamedPropertyResult(receiverIsObject, obj, slotInfo))
        {
            UpdateNamedPropertyIc(namedPropertyIcEntries, icSlot, obj, atom, slotInfo);
            return;
        }

        if (
            receiverIsObject
            && TryFindPrototypeProperty(
                this,
                obj,
                atom,
                out var prototypeHolder,
                out var prototypeSlotInfo
            )
        )
        {
            prototypeNamedPropertyIcEntries ??= script.GetOrCreatePrototypeNamedPropertyIcEntries();
            UpdatePrototypeNamedPropertyIc(
                prototypeNamedPropertyIcEntries,
                icSlot,
                obj,
                atom,
                prototypeSlotInfo,
                prototypeHolder
            );
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateNamedPropertyIc(
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        JsObject obj,
        int atom,
        in SlotInfo slotInfo
    )
    {
        if (namedPropertyIcEntries is null || !slotInfo.IsValid || obj.UsesDynamicNamedProperties)
            return;

#if DEBUG
        if ((uint)icSlot >= (uint)namedPropertyIcEntries.Length)
            throw new InvalidOperationException("Named property feedback slot is out of range.");
#endif

        ref var ic = ref namedPropertyIcEntries[icSlot];
        ic.Shape = obj.Shape;
        ic.SlotInfo = slotInfo;
        ic.NameAtom = atom;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdatePrototypeNamedPropertyIc(
        OkojoPrototypeNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        JsObject receiver,
        int atom,
        in SlotInfo slotInfo,
        JsObject holder
    )
    {
        if (
            namedPropertyIcEntries is null
            || !slotInfo.IsValid
            || receiver.UsesDynamicNamedProperties
            || !IsPrototypeIcObject(receiver)
            || !IsPrototypeIcObject(holder)
            || holder.UsesDynamicNamedProperties
            || receiver.Prototype is null
        )
            return;

#if DEBUG
        if ((uint)icSlot >= (uint)namedPropertyIcEntries.Length)
            throw new InvalidOperationException("Named property feedback slot is out of range.");
#endif

        ref var ic = ref namedPropertyIcEntries[icSlot];
        ic.ReceiverShape = receiver.Shape;
        ic.SlotInfo = slotInfo;
        ic.NameAtom = atom;
        ic.Holder = holder;
        ic.HolderShape = holder.Shape;
    }

    private static bool TryFindPrototypeProperty(
        JsRealm realm,
        JsObject receiver,
        int atom,
        out JsObject holder,
        out SlotInfo slotInfo
    )
    {
        holder = null!;
        slotInfo = SlotInfo.Invalid;
        if (!IsPrototypeIcObject(receiver) || receiver.UsesDynamicNamedProperties)
            return false;

        if (receiver.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out _, false))
            return false;

        var cursor = receiver.Prototype;
        if (cursor is null || !IsPrototypeIcObject(cursor) || cursor.UsesDynamicNamedProperties)
            return false;
        if (!cursor.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out _, false))
            return false;
        if (
            !cursor.TryGetOwnPropertySlotInfoAtom(atom, out var candidate)
            || (candidate.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter))
                != 0
            || cursor.Slots[candidate.Slot].IsTheHole
        )
            return false;

        holder = cursor;
        slotInfo = candidate;
        return true;
    }

    private static bool IsPrototypeIcObject(JsObject obj)
    {
        return obj is JsPlainObject or JsArray;
    }
}
