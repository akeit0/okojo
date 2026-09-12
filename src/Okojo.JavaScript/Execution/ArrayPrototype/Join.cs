using System.Runtime.CompilerServices;
using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Array.prototype.join: dense fast phase plus generic resume. Holes resolve
// through the prototype chain exactly like the generic lookup before being
// treated as empty. String assembly uses the interpolated string handler
// directly: the whole build is scoped to this call, so we skip the
// StringBuilder heap allocation and its CopyTo on ToString.
public partial class Intrinsics
{
    private static string RunJoin(JsRealm realm, JsObject obj, long length, string separator)
    {
        if (length == 0)
            return string.Empty;

        var handler = new DefaultInterpolatedStringHandler(0, 0);

        long k = 0;
        if (TryOpenDenseRange(obj, length, out var dense, out var store))
        {
            for (; k < length; k++)
            {
                if (!DenseWindowValid(dense, store, k))
                    break;
                if (k > 0)
                    handler.AppendLiteral(separator);
                var element = store[(int)k];
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    continue;
                if (!element.IsUndefined && !element.IsNull)
                    JsValueStringAppender.Append(realm, ref handler, element);
            }
        }

        for (; k < length; k++)
        {
            if (k > 0)
                handler.AppendLiteral(separator);
            if (
                !TryGetArrayLikeIndex(realm, obj, k, out var element)
                || element.IsUndefined
                || element.IsNull
            )
                continue;
            JsValueStringAppender.Append(realm, ref handler, element);
        }

        return handler.ToStringAndClear();
    }
}
