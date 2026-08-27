using System.Runtime.CompilerServices;

namespace Okojo.JavaScript.Execution;

/// <summary>
/// V8-style FastJSArray gate plus direct dense-store operations shared by
/// Array.prototype builtins. Mirrors Torque's <c>TryFast*</c> macros: a cheap
/// qualification check followed by raw backing-store work, with the generic
/// algorithm acting as the labeled Slow fallback.
/// </summary>
internal static class DenseArrayFastPath
{
    /// <summary>
    /// Qualifies when the receiver is a plain dense array: no sparse element
    /// dictionary (and therefore no element accessors or exotic descriptors),
    /// with a live backing store present.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGet(JsObject obj, out JsArray array)
    {
        if (obj is JsArray arr && arr.IndexedProperties is null && arr.Dense is not null)
        {
            array = arr;
            return true;
        }

        array = null!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool HasStorageForLength(JsArray array, long length) =>
        length >= 0 && array.Dense is { } store && length <= store.Length;

    /// <summary>
    /// Presence-preserving relocations (shift/unshift/splice moves) must not
    /// start resolving holes through the prototype chain, so callers bail to
    /// the generic algorithm when a hole sits inside a moved range. This scan
    /// substitutes for V8's no-elements protector.
    /// </summary>
    internal static bool RangeHasHole(ReadOnlySpan<JsValue> range)
    {
        // Span iteration lets the JIT elide bounds checks per element.
        foreach (var value in range)
            if (value.IsTheHole)
                return true;
        return false;
    }

    /// <summary>
    /// Grows the array's dense store so that at least <paramref name="needed"/>
    /// slots exist, hole-filling slots beyond the previous store length.
    /// Callers must have verified extensibility where writes extend length.
    /// </summary>
    internal static void EnsureCapacity(JsArray array, long needed)
    {
        var store = array.Dense;
        if (store is not null && needed <= store.Length)
            return;

        var capacity = store is null || store.Length == 0 ? 4 : store.Length;
        while (capacity < needed)
            capacity <<= 1;

        var grown = new JsValue[capacity];
        if (store is not null)
            Array.Copy(store, grown, store.Length);
        grown.AsSpan(store?.Length ?? 0).Fill(JsValue.TheHole);
        array.Dense = grown;
    }
}
