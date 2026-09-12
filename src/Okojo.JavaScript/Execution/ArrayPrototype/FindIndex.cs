using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.findIndex: dense fast phase plus generic resume.
public partial class Intrinsics
{
    private static long RunFindIndex(
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
                if (element.IsTheHole)
                    GetArrayLikeIndex(realm, obj, k, out element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return k;
            }
        }

        for (; k < length; k++)
        {
            GetArrayLikeIndex(realm, obj, k, out var element);
            if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return k;
        }

        return -1;
    }
}
