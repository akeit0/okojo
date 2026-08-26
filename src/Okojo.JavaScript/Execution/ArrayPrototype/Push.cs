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
        const long maxSafeInteger = 9007199254740991L;
        if (start > maxSafeInteger - items.Length)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Invalid array length",
                "ARRAY_LENGTH_INVALID"
            );
        if (start + items.Length > uint.MaxValue)
            throw new JsRuntimeException(
                JsErrorKind.RangeError,
                "Invalid array length",
                "ARRAY_LENGTH_INVALID"
            );

        DenseArrayFastPath.EnsureCapacity(target, start + items.Length);
        var store = target.Dense!;
        for (var i = 0; i < items.Length; i++)
            store[start + i] = items[i];
        target.SetLength((uint)(start + items.Length));
        result = FromLength(start + items.Length);
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

        var tail = target.Dense![length - 1];
        if (tail.IsTheHole)
            return false;

        target.Dense![length - 1] = JsValue.TheHole;
        target.SetLength(length - 1);
        result = tail;
        return true;
    }
}
