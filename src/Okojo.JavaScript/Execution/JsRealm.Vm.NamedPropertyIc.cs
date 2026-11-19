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

    /// <summary>Monomorphic and prototype IC miss for named loads on an object receiver.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue LoadNamedPropertyIcMiss(
        JsScript script,
        ref OkojoPrototypeNamedPropertyIcEntry[]? prototypeNamedPropertyIcEntries,
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        JsObject obj,
        int atom
    )
    {
        if (
            script.GetPolymorphicNamedPropertyIcEntries(icSlot) is { } polymorphic
            && TryLoadNamedPropertyPolymorphic(polymorphic, obj, out var value)
        )
            return value;

        if (obj.TryGetPropertyAtom(this, atom, out value, out var slotInfo))
            UpdateNamedPropertyIcAfterGet(
                script,
                ref prototypeNamedPropertyIcEntries,
                namedPropertyIcEntries,
                icSlot,
                true,
                obj,
                atom,
                slotInfo
            );
        return value;
    }

    /// <summary>Monomorphic IC miss for named stores.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StoreNamedPropertyIcMiss(
        JsBytecodeFunction currentFunc,
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        bool receiverIsObject,
        JsObject obj,
        int atom,
        JsValue value
    )
    {
        var script = currentFunc.Script;
        bool succeeded;
        if (
            !receiverIsObject
            || script.GetPolymorphicNamedPropertyIcEntries(icSlot) is not { } polymorphic
            || !TryStoreNamedPropertyPolymorphic(polymorphic, obj, value, out succeeded)
        )
        {
            var previousLayout = obj.NamedPropertyLayout;
            succeeded = obj.TrySetPropertyAtom(this, atom, value, out var slotInfo);
            if (CanCacheNamedPropertyResult(receiverIsObject, obj, slotInfo))
            {
                RecordNamedPropertyStore(
                    script,
                    namedPropertyIcEntries,
                    icSlot,
                    obj,
                    previousLayout,
                    atom,
                    slotInfo
                );
                UpdateNamedPropertyIc(namedPropertyIcEntries, icSlot, obj, atom, slotInfo);
            }
        }

        if (!succeeded && currentFunc.IsStrict)
            ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
    }

    private bool TryLoadNamedPropertyPolymorphic(
        OkojoPolymorphicNamedPropertyIcEntry[] entries,
        JsObject obj,
        out JsValue value
    )
    {
        if (!obj.UsesDynamicNamedProperties)
        {
            var shape = obj.Shape;
            for (var i = 0; i < entries.Length; i++)
            {
                ref readonly var e = ref entries[i];
                if (!ReferenceEquals(e.ReceiverShape, shape))
                {
                    if (e.ReceiverShape is null)
                        break;
                    continue;
                }

                if (e.Kind == OkojoPolymorphicNamedPropertyIcEntry.KindOwn)
                {
                    value = obj.GetNamedByCachedSlotInfo(this, e.SlotInfo);
                    return true;
                }

                if (e.Kind == OkojoPolymorphicNamedPropertyIcEntry.KindPrototypeData)
                {
                    var holder = e.Holder!;
                    if (
                        ReferenceEquals(obj.Prototype, holder)
                        && !holder.UsesDynamicNamedProperties
                        && ReferenceEquals(holder.Shape, e.HolderShape)
                    )
                    {
                        value = holder.SlotRef(e.SlotInfo.Slot);
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Stores through a polymorphic own-slot or add-transition way. Returns false on a miss;
    /// otherwise <paramref name="succeeded"/> carries the [[Set]] result.
    /// </summary>
    private bool TryStoreNamedPropertyPolymorphic(
        OkojoPolymorphicNamedPropertyIcEntry[] entries,
        JsObject obj,
        in JsValue value,
        out bool succeeded
    )
    {
        succeeded = false;
        if (obj.UsesDynamicNamedProperties)
            return false;

        var shape = obj.Shape;
        for (var i = 0; i < entries.Length; i++)
        {
            ref readonly var e = ref entries[i];
            if (!ReferenceEquals(e.ReceiverShape, shape))
            {
                if (e.ReceiverShape is null)
                    break;
                continue;
            }

            if (e.Kind == OkojoPolymorphicNamedPropertyIcEntry.KindOwn)
            {
                succeeded = obj.SetNamedByCachedSlotInfo(this, e.SlotInfo, value);
                return true;
            }

            if (
                e.Kind == OkojoPolymorphicNamedPropertyIcEntry.KindTransition
                && obj is JsPlainObject { CanApplyCachedTransition: true }
                && IsTransitionChainStillValid(obj, e)
            )
            {
                obj.AddNamedByCachedTransition(e.TransitionShape!, e.SlotInfo.Slot, value);
                succeeded = true;
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTransitionChainStillValid(
        JsObject obj,
        in OkojoPolymorphicNamedPropertyIcEntry e
    )
    {
        var first = e.Holder;
        if (!ReferenceEquals(obj.Prototype, first))
            return false;
        if (first is null)
            return true;
        if (first.UsesDynamicNamedProperties || !ReferenceEquals(first.Shape, e.HolderShape))
            return false;
        var second = e.Holder2;
        if (!ReferenceEquals(first.Prototype, second))
            return false;
        if (second is null)
            return true;
        return !second.UsesDynamicNamedProperties
            && ReferenceEquals(second.Shape, e.Holder2Shape)
            && second.Prototype is null;
    }

    private static void InsertPolymorphicNamedPropertyIcEntry(
        JsScript script,
        int icSlot,
        in OkojoPolymorphicNamedPropertyIcEntry entry
    )
    {
        var entries = script.GetOrCreatePolymorphicNamedPropertyIcEntries(icSlot);
        var replace = entries.Length - 1;
        for (var i = 0; i < entries.Length; i++)
        {
            ref var e = ref entries[i];
            if (e.ReceiverShape is null || ReferenceEquals(e.ReceiverShape, entry.ReceiverShape))
            {
                replace = i;
                break;
            }
        }

        // Most recent first: shift older ways down over the replaced one.
        for (var i = replace; i > 0; i--)
            entries[i] = entries[i - 1];
        entries[0] = entry;
    }

    /// <summary>Records a slow-path load result once the site has seen another shape.</summary>
    private static void RecordPolymorphicLoad(
        JsScript script,
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        OkojoPrototypeNamedPropertyIcEntry[]? prototypeNamedPropertyIcEntries,
        int icSlot,
        JsObject obj,
        in SlotInfo slotInfo,
        JsObject? holder
    )
    {
        if (namedPropertyIcEntries is null || obj.UsesDynamicNamedProperties)
            return;
        var shape = obj.Shape;
        var primary = namedPropertyIcEntries[icSlot].Shape;
        var prototypePrimary = prototypeNamedPropertyIcEntries?[icSlot].ReceiverShape;
        if (
            (primary is null || ReferenceEquals(primary, shape))
            && (prototypePrimary is null || ReferenceEquals(prototypePrimary, shape))
        )
            return;

        InsertPolymorphicNamedPropertyIcEntry(
            script,
            icSlot,
            new()
            {
                ReceiverShape = shape,
                Holder = holder,
                HolderShape = holder?.Shape,
                SlotInfo = slotInfo,
                Kind = holder is null
                    ? OkojoPolymorphicNamedPropertyIcEntry.KindOwn
                    : OkojoPolymorphicNamedPropertyIcEntry.KindPrototypeData,
            }
        );
    }

    /// <summary>
    /// Records a slow-path store: add transitions always, own stores once the site is
    /// polymorphic. Only ordinary receivers with a short ordinary prototype chain qualify
    /// for transitions so setters, read-only and exotic [[Set]] remain on the slow path.
    /// </summary>
    private static void RecordNamedPropertyStore(
        JsScript script,
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        JsObject obj,
        NamedPropertyLayout previousLayout,
        int atom,
        in SlotInfo slotInfo
    )
    {
        if (
            namedPropertyIcEntries is null
            || !slotInfo.IsValid
            || obj.UsesDynamicNamedProperties
            || previousLayout is not StaticNamedPropertyLayout previousShape
        )
            return;

        var shape = obj.Shape;
        if (ReferenceEquals(previousShape, shape))
        {
            var primary = namedPropertyIcEntries[icSlot].Shape;
            if (primary is not null && !ReferenceEquals(primary, shape))
                InsertPolymorphicNamedPropertyIcEntry(
                    script,
                    icSlot,
                    new()
                    {
                        ReceiverShape = shape,
                        SlotInfo = slotInfo,
                        Kind = OkojoPolymorphicNamedPropertyIcEntry.KindOwn,
                    }
                );
            return;
        }

        if (
            obj is not JsPlainObject { CanApplyCachedTransition: true }
            || slotInfo.Flags != JsShapePropertyFlags.Open
            || shape.PropertyCount != previousShape.PropertyCount + 1
            || !shape.TryGetSlotInfo(atom, out var added)
            || added.Slot != slotInfo.Slot
            || previousShape.TryGetSlotInfo(atom, out _)
        )
            return;

        var first = obj.Prototype;
        JsObject? second = null;
        if (first is not null)
        {
            if (first is not JsPlainObject || first.UsesDynamicNamedProperties)
                return;
            second = first.Prototype;
            if (
                second is not null
                && (
                    second is not JsPlainObject
                    || second.UsesDynamicNamedProperties
                    || second.Prototype is not null
                )
            )
                return;
        }

        InsertPolymorphicNamedPropertyIcEntry(
            script,
            icSlot,
            new()
            {
                ReceiverShape = previousShape,
                Holder = first,
                HolderShape = first?.Shape,
                Holder2 = second,
                Holder2Shape = second?.Shape,
                TransitionShape = shape,
                SlotInfo = slotInfo,
                Kind = OkojoPolymorphicNamedPropertyIcEntry.KindTransition,
            }
        );
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
            RecordPolymorphicLoad(
                script,
                namedPropertyIcEntries,
                prototypeNamedPropertyIcEntries,
                icSlot,
                obj,
                slotInfo,
                null
            );
            UpdateNamedPropertyIc(namedPropertyIcEntries, icSlot, obj, atom, slotInfo);
            return;
        }

        // Array length is an own property with no slot. There is no prototype cache to fill.
        if (atom == IdLength && obj is JsArray)
            return;

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
            if (IsPrototypeIcObject(obj) && IsPrototypeIcObject(prototypeHolder))
                RecordPolymorphicLoad(
                    script,
                    namedPropertyIcEntries,
                    prototypeNamedPropertyIcEntries,
                    icSlot,
                    obj,
                    prototypeSlotInfo,
                    prototypeHolder
                );
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
            || cursor.SlotRef(candidate.Slot).IsTheHole
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
