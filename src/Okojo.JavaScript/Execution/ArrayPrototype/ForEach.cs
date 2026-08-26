using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.forEach: dense fast phase walks the live backing store and
// stops at the first shape change; the generic phase resumes from that index
// so hole, prototype-chain, and observer semantics stay exact (V8-style
// TryFast*/Slow split, one file per builtin).
public partial class Intrinsics
{
    private static void RunForEach(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        JsValue callbackThis
    )
    {
        long k = 0;
        if (TryOpenDenseRange(obj, length, out var dense, out var store))
        {
            for (; k < length; k++)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                var element = store[(int)k];
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    continue;
                InvokeArrayCallback(realm, callback, callbackThis, element, k, obj);
            }
        }

        for (; k < length; k++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            InvokeArrayCallback(realm, callback, callbackThis, element, k, obj);
        }
    }
}
