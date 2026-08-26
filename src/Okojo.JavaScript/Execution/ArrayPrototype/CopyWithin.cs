using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.fill / copyWithin / reverse: raw range operations over
// dense windows. copyWithin and fill consult the prototype chain for absent
// targets, so they require an element-free chain (no no-elements protector
// yet); reverse only needs a fully valid window because every slot exists.
public partial class Intrinsics
{
    internal static bool TryFillDense(JsObject obj, long start, long end, JsValue value)
    {
        if (!TryOpenDenseRange(obj, end, out var target, out var store))
            return false;
        if (!ReceiverChainFreeOfElements(obj))
            return false;
        store.AsSpan((int)start, (int)(end - start)).Fill(value);
        return true;
    }

    internal static bool TryCopyWithinDense(JsObject obj, long length, long to, long from, long end)
    {
        if (length == 0 || length > int.MaxValue || !DenseArrayFastPath.TryGet(obj, out var target))
            return false;

        var store = target.Dense!;
        var count = Math.Min(end - from, length - to);
        if (count <= 0)
            return true;

        var highest = Math.Max(to + count, Math.Max(end, from));
        if (
            highest > length
            || DenseArrayFastPath.RangeHasHole(store.AsSpan(0, (int)highest))
            || !ReceiverChainFreeOfElements(obj)
        )
            return false;

        Array.Copy(store, (int)from, store, (int)to, (int)count);
        return true;
    }

    internal static bool TryReverseDense(JsObject obj, long length)
    {
        if (
            length == 0
            || length > int.MaxValue
            || !TryOpenDenseRange(obj, length, out var target, out var store)
            || !DenseRangeFullyValid(target, store, length)
        )
            return false;

        store.AsSpan(0, (int)length).Reverse();
        return true;
    }
}
