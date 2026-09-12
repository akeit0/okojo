using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.splice: raw relocation over a hole-free dense window.
// Holes disqualify the fast path because spec relocation consults the
// prototype chain for missing elements; writes at previously absent indices
// require an element-free prototype chain (no no-elements protector yet).
public partial class Intrinsics
{
    internal static bool TrySpliceDense(
        JsRealm realm,
        JsObject obj,
        long length,
        long actualStart,
        long actualDeleteCount,
        ReadOnlySpan<JsValue> items,
        out JsArray deletedElements
    )
    {
        deletedElements = null!;
        if (
            length > int.MaxValue
            || !DenseArrayFastPath.TryGet(obj, out var target)
            || !target.IsExtensible
            || !target.LengthIsWritable
            || !ReceiverChainFreeOfElements(obj)
        )
            return false;

        var store = target.Dense!;
        if (length > (uint)store.Length)
            return false;
        if (DenseArrayFastPath.RangeHasHole(store.AsSpan(0, (int)length)))
            return false;

        deletedElements = realm.CreateArrayObject();
        if (actualDeleteCount > 0)
        {
            DenseArrayFastPath.EnsureCapacity(deletedElements, actualDeleteCount);
            Array.Copy(store, (int)actualStart, deletedElements.Dense!, 0, (int)actualDeleteCount);
        }
        deletedElements.SetLength((uint)Math.Max(0, actualDeleteCount));

        var itemCount = items.Length;
        var newLength = length - actualDeleteCount + itemCount;

        if (itemCount < actualDeleteCount)
        {
            var shift = actualDeleteCount - itemCount;
            Array.Copy(
                store,
                (int)(actualStart + actualDeleteCount),
                store,
                (int)(actualStart + itemCount),
                (int)(length - actualStart - actualDeleteCount)
            );
            store.AsSpan((int)newLength, (int)shift).Fill(JsValue.TheHole);
        }
        else if (itemCount > actualDeleteCount)
        {
            DenseArrayFastPath.EnsureCapacity(target, newLength);
            store = target.Dense!;
            Array.Copy(
                store,
                (int)(actualStart + actualDeleteCount),
                store,
                (int)(actualStart + itemCount),
                (int)(length - actualStart - actualDeleteCount)
            );
        }

        items.CopyTo(store.AsSpan((int)actualStart));
        target.SetLength((uint)newLength);
        return true;
    }
}
