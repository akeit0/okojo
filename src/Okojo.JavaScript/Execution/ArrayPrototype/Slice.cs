using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.slice: raw span copy into a fresh array. Copied holes mean
// absence in the result, matching the generic define-if-present behavior.
public partial class Intrinsics
{
    internal static bool TrySliceDense(
        JsRealm realm,
        JsObject obj,
        long length,
        long start,
        long end,
        out JsArray result
    )
    {
        result = null!;
        if (start < 0)
            start = 0;
        if (end < start)
            end = start;
        if (end > length)
            end = length;
        if (
            !TryOpenDenseRange(obj, length, out var source, out var sourceStore)
            || end > sourceStore.Length
        )
            return false;

        var count = (uint)Math.Max(0, end - start);
        result = realm.CreateArrayObject();
        if (count > 0)
        {
            DenseArrayFastPath.EnsureCapacity(result, count);
            Array.Copy(sourceStore, (int)start, result.Dense!, 0, (int)count);
        }
        result.SetLength(count);
        return true;
    }
}
