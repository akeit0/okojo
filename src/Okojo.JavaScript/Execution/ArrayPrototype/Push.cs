using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.push / pop: dense mutators. Push stores at previously
// absent indices, so it requires an element-free prototype chain (no
// no-elements protector yet); pop falls back when the tail is a hole so the
// chain is consulted for the returned value.
public partial class Intrinsics
{
    internal static bool TryPushDense(JsObject obj, ReadOnlySpan<JsValue> items, out JsValue result)
    {
        result = default;
        if (
            items.Length == 0
            || !DenseArrayFastPath.TryGet(obj, out var target)
            || !target.IsExtensible
            || !target.LengthIsWritable
            || !ReceiverChainFreeOfElements(obj)
        )
            return false;

        var start = target.Length;
        var length = start + items.Length;
        if (start > (uint)target.Dense!.Length || length > Array.MaxLength)
            return false;

        DenseArrayFastPath.EnsureCapacity(target, length);
        var store = target.Dense!;
        for (var i = 0; i < items.Length; i++)
            store[start + i] = items[i];
        target.SetLength((uint)length);
        result = FromLength(length);
        return true;
    }

    internal static bool TryPopDense(JsObject obj, out JsValue result)
    {
        result = default;
        if (!DenseArrayFastPath.TryGet(obj, out var target) || !target.LengthIsWritable)
            return false;

        var length = target.Length;
        if (length == 0)
        {
            result = JsValue.Undefined;
            return true;
        }

        var store = target.Dense!;
        if (length > (uint)store.Length)
            return false;

        var tail = store[length - 1];
        if (tail.IsTheHole)
            return false;

        store[length - 1] = JsValue.TheHole;
        target.SetLength(length - 1);
        result = tail;
        return true;
    }
}
