using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.map: dense fast phase plus generic resume (one file per builtin).
public partial class Intrinsics
{
    private static JsArray RunMap(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        JsValue callbackThis
    )
    {
        var result = realm.CreateArrayObject();
        var resultLength = RequireArrayStorageLength(length);

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
                FreshArrayOperations.DefineElement(
                    result,
                    (uint)k,
                    InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)
                );
            }
        }

        for (; k < length; k++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            FreshArrayOperations.DefineElement(
                result,
                (uint)k,
                InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)
            );
        }

        result.SetLength(resultLength);
        return result;
    }
}
