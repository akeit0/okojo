using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.lastIndexOf: backward scan stays generic (a reverse dense
// window cannot resume without rechecking skipped slots).
public partial class Intrinsics
{
    private static long RunLastIndexOf(
        JsRealm realm,
        JsObject obj,
        long start,
        JsValue searchElement
    )
    {
        for (var k = start; k >= 0; k--)
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
