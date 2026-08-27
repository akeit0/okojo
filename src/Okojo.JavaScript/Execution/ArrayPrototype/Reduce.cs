using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.reduce: dense fast phase plus generic resume.
public partial class Intrinsics
{
    private static JsValue RunReduce(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        bool hasAccumulator,
        JsValue accumulator
    )
    {
        long k = 0;
        if (TryOpenDenseRange(obj, length, out var dense, out var store))
        {
            if (!hasAccumulator)
            {
                while (k < length)
                {
                    if (DenseWindowValid(dense, store, k) && !store[(int)k].IsTheHole)
                    {
                        accumulator = store[(int)k];
                        break;
                    }

                    if (TryGetArrayLikeIndex(realm, obj, k, out accumulator))
                        break;

                    k++;
                }
                if (k >= length)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "Reduce of empty array with no initial value"
                    );
                k++;
            }

            for (; k < length; k++)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                var element = store[(int)k];
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    continue;
                Span<JsValue> callbackArgs =
                [
                    accumulator,
                    element,
                    FromLength(k),
                    JsValue.FromObject(obj),
                ];
                accumulator = realm.InvokeFunction(callback, JsValue.Undefined, callbackArgs);
            }
        }
        else
        {
            if (!hasAccumulator)
            {
                while (k < length && !TryGetArrayLikeIndex(realm, obj, k, out accumulator))
                    k++;
                if (k >= length)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "Reduce of empty array with no initial value"
                    );
                k++;
            }
        }

        for (; k < length; k++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            Span<JsValue> callbackArgs =
            [
                accumulator,
                element,
                FromLength(k),
                JsValue.FromObject(obj),
            ];
            accumulator = realm.InvokeFunction(callback, JsValue.Undefined, callbackArgs);
        }

        return accumulator;
    }

    private static JsValue RunReduceRight(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        bool hasAccumulator,
        JsValue accumulator
    )
    {
        long k = length - 1;
        if (!hasAccumulator)
        {
            while (k >= 0 && !TryGetArrayLikeIndex(realm, obj, k, out accumulator))
                k--;
            if (k < 0)
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "Reduce of empty array with no initial value"
                );
            k--;
        }

        for (; k >= 0; k--)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            Span<JsValue> callbackArgs =
            [
                accumulator,
                element,
                FromLength(k),
                JsValue.FromObject(obj),
            ];
            accumulator = realm.InvokeFunction(callback, JsValue.Undefined, callbackArgs);
        }

        return accumulator;
    }
}
