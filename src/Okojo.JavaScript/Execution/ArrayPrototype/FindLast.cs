using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.findLast / findLastIndex: reverse scans run dense only
// while the whole window is currently valid, otherwise straight generic.
public partial class Intrinsics
{
    private static JsValue RunFindLast(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        JsValue callbackThis
    )
    {
        long k = length - 1;
        if (
            TryOpenDenseRange(obj, length, out var dense, out var store)
            && DenseRangeFullyValid(dense, store, length)
        )
        {
            for (; k >= 0; k--)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                var element = store[(int)k];
                if (element.IsTheHole)
                    GetArrayLikeIndex(realm, obj, k, out element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return element;
            }
            if (k < 0)
                return JsValue.Undefined;
        }

        for (; k >= 0; k--)
        {
            GetArrayLikeIndex(realm, obj, k, out var element);
            if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return element;
        }

        return JsValue.Undefined;
    }

    private static long RunFindLastIndex(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        JsValue callbackThis
    )
    {
        long k = length - 1;
        if (
            TryOpenDenseRange(obj, length, out var dense, out var store)
            && DenseRangeFullyValid(dense, store, length)
        )
        {
            for (; k >= 0; k--)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                var element = store[(int)k];
                if (element.IsTheHole)
                    GetArrayLikeIndex(realm, obj, k, out element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return k;
            }
            if (k < 0)
                return -1;
        }

        for (; k >= 0; k--)
        {
            GetArrayLikeIndex(realm, obj, k, out var element);
            if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return k;
        }

        return -1;
    }
}
