using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

// Shared dense-window helpers for the Array.prototype per-builtin fast paths
// (V8-style FastJSArray gate + Slow labels, factored out of every builtin).
public partial class Intrinsics
{
    /// <summary>
    /// Opens a dense iteration window when obj is a plain dense array whose
    /// live length fits an int-sized scan.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
    )]
    internal static bool TryOpenDenseRange(
        JsObject obj,
        long length,
        out JsArray array,
        out JsValue[] store
    )
    {
        if (
            length <= int.MaxValue
            && obj is JsArray candidate
            && candidate.IndexedProperties is null
            && candidate.Dense is not null
        )
        {
            array = candidate;
            store = candidate.Dense;
            return true;
        }

        array = null!;
        store = null!;
        return false;
    }

    /// <summary>Reports whether the captured dense window is still live.</summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
    )]
    internal static bool DenseWindowValid(JsArray array, JsValue[] store, long index) =>
        (ulong)index < array.Length && ReferenceEquals(store, array.Dense);

    /// <summary>True when every slot in [0, count) can be read densely right now.</summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
    )]
    internal static bool DenseRangeFullyValid(JsArray dense, JsValue[] store, long count) =>
        (ulong)count <= dense.Length && ReferenceEquals(store, dense.Dense);

    /// <summary>
    /// Write-extending fast paths (push/unshift/splice) store at indices that
    /// were previously absent, where Set would consult the prototype chain for
    /// indexed properties or accessors. Without a no-elements protector we
    /// require an element-free prototype chain before bypassing it.
    /// </summary>
    internal static bool ReceiverChainFreeOfElements(JsObject receiver)
    {
        for (JsObject? p = receiver.Prototype; p is not null; p = p.Prototype)
        {
            if (p.IndexedProperties is { Count: > 0 })
                return false;
            if (p is JsArray { Length: > 0 })
                return false;
        }
        return true;
    }
}
