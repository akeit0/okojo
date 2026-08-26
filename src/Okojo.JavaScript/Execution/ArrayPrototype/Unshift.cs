using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.shift / unshift: raw relocations over hole-free dense
// windows; unshift additionally extends length, so the prototype chain must
// be element-free (no no-elements protector yet).
public partial class Intrinsics
{
    internal static bool TryShiftDense(JsObject obj, out JsValue result)
    {
        result = default;
        if (
            !DenseArrayFastPath.TryGet(obj, out var target)
            || !target.LengthIsWritable
            || target.Length > int.MaxValue
        )
            return false;

        var length = target.Length;
        var store = target.Dense!;
        if (length == 0)
        {
            result = JsValue.Undefined;
            return true;
        }

        if (DenseArrayFastPath.RangeHasHole(store.AsSpan(0, (int)length)))
            return false;

        result = store[0];
        Array.Copy(store, 1, store, 0, length - 1);
        store[length - 1] = JsValue.TheHole;
        target.SetLength(length - 1);
        return true;
    }

    internal static bool TryUnshiftDense(
        JsObject obj,
        ReadOnlySpan<JsValue> items,
        out JsValue result
    )
    {
        result = default;
        if (
            items.Length == 0
            || !DenseArrayFastPath.TryGet(obj, out var target)
            || !target.IsExtensible
            || !target.LengthIsWritable
            || target.Length > int.MaxValue
            || !ReceiverChainFreeOfElements(obj)
        )
            return false;

        var length = target.Length;
        var newLength = length + items.Length;
        if (newLength > MaxSafeIntegerLength || newLength > uint.MaxValue)
            return false;
        if (DenseArrayFastPath.RangeHasHole(target.Dense!.AsSpan(0, (int)length)))
            return false;

        DenseArrayFastPath.EnsureCapacity(target, newLength);
        var store = target.Dense!;
        Array.Copy(store, 0, store, items.Length, length);
        for (var i = 0; i < items.Length; i++)
            store[i] = items[i];
        target.SetLength((uint)newLength);
        result = FromLength(newLength);
        return true;
    }
}
