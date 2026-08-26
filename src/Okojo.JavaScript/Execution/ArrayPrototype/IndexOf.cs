using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.includes / indexOf (V8 groups these as ArrayIndexOfIncludes).
public partial class Intrinsics
{
    internal static bool RunIncludes(
        JsRealm realm,
        JsObject obj,
        long length,
        long start,
        JsValue searchElement
    )
    {
        long k = start;
        if (TryOpenDenseRange(obj, length, out var dense, out var store))
        {
            for (; k < length; k++)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                var element = store[(int)k];
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    element = JsValue.Undefined;
                if (JsValueSameValueZeroComparer.Instance.Equals(element, searchElement))
                    return true;
            }
        }

        for (; k < length; k++)
        {
            var element = TryGetArrayLikeIndex(realm, obj, k, out var value)
                ? value
                : JsValue.Undefined;
            if (JsValueSameValueZeroComparer.Instance.Equals(element, searchElement))
                return true;
        }

        return false;
    }

    private static long RunIndexOf(
        JsRealm realm,
        JsObject obj,
        long length,
        long start,
        JsValue searchElement
    )
    {
        long k = start;
        if (TryOpenDenseRange(obj, length, out var dense, out var store))
        {
            for (; k < length; k++)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                var element = store[(int)k];
                if (element.IsTheHole)
                    continue;
                if (StrictEquals(element, searchElement))
                    return k;
            }
        }

        for (; k < length; k++)
        {
            if (!HasArrayLikeIndex(realm, obj, k))
                continue;
            GetArrayLikeIndex(realm, obj, k, out var element);
            if (StrictEquals(element, searchElement))
                return k;
        }

        return -1;
    }
}
