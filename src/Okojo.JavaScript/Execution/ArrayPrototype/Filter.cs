using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.filter: dense fast phase plus generic resume (one file per builtin).
public partial class Intrinsics
{
    private static JsArray RunFilter(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        JsValue callbackThis
    )
    {
        var result = realm.CreateArrayObject();

        long k = 0;
        uint to = 0;
        if (TryOpenDenseRange(obj, length, out var dense, out var store))
        {
            for (; k < length; k++)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                var element = store[(int)k];
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    continue;
                if (!ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    continue;
                DefineFreshArrayLikeIndex(result, to++, element);
            }
        }

        for (; k < length; k++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            if (!ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                continue;
            DefineFreshArrayLikeIndex(result, to++, element);
        }

        result.SetLength(to);
        return result;
    }
}
