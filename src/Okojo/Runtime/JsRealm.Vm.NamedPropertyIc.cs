using System.Runtime.CompilerServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUseNamedPropertyIc(
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        bool receiverIsObject,
        JsObject obj,
        int atom,
        out OkojoNamedPropertyIcEntry ic)
    {
        if (namedPropertyIcEntries is null || !receiverIsObject || obj.UsesDynamicNamedProperties)
        {
            ic = default;
            return false;
        }

#if DEBUG
        if ((uint)icSlot >= (uint)namedPropertyIcEntries.Length)
            throw new InvalidOperationException("Named property feedback slot is out of range.");
#endif

        ic = namedPropertyIcEntries[icSlot];
        return ReferenceEquals(obj.Shape, ic.Shape)
#if DEBUG
               && ic.NameAtom == atom
#endif
            ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanCacheNamedPropertyResult(bool receiverIsObject, JsObject obj, in SlotInfo slotInfo)
    {
        return receiverIsObject && slotInfo.IsValid && !obj.UsesDynamicNamedProperties;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateNamedPropertyIc(
        OkojoNamedPropertyIcEntry[]? namedPropertyIcEntries,
        int icSlot,
        JsObject obj,
        int atom,
        in SlotInfo slotInfo)
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
}
