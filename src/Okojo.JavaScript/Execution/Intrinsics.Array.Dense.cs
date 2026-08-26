using System.Runtime.CompilerServices;
using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

/// <summary>
/// Dense-storage implementations of the Array.prototype builtins, split per
/// function with separate fast and slow phases mirroring V8's Torque macros:
/// the fast phase walks the live backing store directly and stops at the
/// first shape change (resize, shrink, sparse conversion), and the generic
/// phase resumes from that index through the full property machine so hole,
/// prototype-chain, and observer semantics stay exact.
/// </summary>
public partial class Intrinsics
{
    /// <summary>
    /// Opens a dense iteration window over <paramref name="obj"/> when it is a
    /// plain dense array whose live length fits an int-sized scan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryOpenDenseRange(
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
            && (store = candidate.Dense!) is not null
        )
        {
            array = candidate;
            return true;
        }

        array = null!;
        store = null!;
        return false;
    }

    /// <summary>Reports whether the captured dense window is still live.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DenseWindowValid(JsArray array, JsValue[] store, long index) =>
        (ulong)index < array.Length && ReferenceEquals(store, array.Dense);

    // ------------------------------------------------------------------
    // forEach / every / some / map / filter
    // ------------------------------------------------------------------

    private static void RunForEach(
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
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    continue;
                InvokeArrayCallback(realm, callback, callbackThis, element, k, obj);
            }
        }

        for (; k < length; k++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            InvokeArrayCallback(realm, callback, callbackThis, element, k, obj);
        }
    }

    private static bool RunEvery(
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
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    continue;
                if (!ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return false;
            }
        }

        for (; k < length; k++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            if (!ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return false;
        }

        return true;
    }

    private static bool RunSome(
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
                if (element.IsTheHole && !TryGetArrayLikeIndex(realm, obj, k, out element))
                    continue;
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return true;
            }
        }

        for (; k < length; k++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                continue;
            if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return true;
        }

        return false;
    }

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

    // ------------------------------------------------------------------
    // find family: forward scans share a dense loop, reverse scans stay
    // generic because a backward window cannot resume without re-checking
    // every slot it already skipped.
    // ------------------------------------------------------------------

    private static JsValue RunFind(
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
                    return element;
            }
        }

        for (; k < length; k++)
        {
            GetArrayLikeIndex(realm, obj, k, out var element);
            if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return element;
        }

        return JsValue.Undefined;
    }

    private static JsValue RunFindLast(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        JsValue callbackThis
    )
    {
        if (
            TryOpenDenseRange(obj, length, out var dense, out var store)
            && DenseRangeFullyValid(dense, store, length)
        )
        {
            for (var k = length - 1; k >= 0; k--)
            {
                var element = store[(int)k];
                if (element.IsTheHole)
                    GetArrayLikeIndex(realm, obj, k, out element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return element;
            }
            return JsValue.Undefined;
        }

        for (var k = length - 1; k >= 0; k--)
        {
            GetArrayLikeIndex(realm, obj, k, out var element);
            if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return element;
        }

        return JsValue.Undefined;
    }

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

    private static long RunFindLastIndex(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction callback,
        JsValue callbackThis
    )
    {
        if (
            TryOpenDenseRange(obj, length, out var dense, out var store)
            && DenseRangeFullyValid(dense, store, length)
        )
        {
            for (var k = length - 1; k >= 0; k--)
            {
                var element = store[(int)k];
                if (element.IsTheHole)
                    GetArrayLikeIndex(realm, obj, k, out element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return k;
            }
            return -1;
        }

        for (var k = length - 1; k >= 0; k--)
        {
            GetArrayLikeIndex(realm, obj, k, out var element);
            if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                return k;
        }

        return -1;
    }

    /// <summary>True when every slot in [0, count) can be read densely right now.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DenseRangeFullyValid(JsArray dense, JsValue[] store, long count) =>
        (ulong)count <= dense.Length && ReferenceEquals(store, dense.Dense);

    // ------------------------------------------------------------------
    // includes / indexOf / lastIndexOf
    // ------------------------------------------------------------------

    private static bool RunIncludes(
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

    // ------------------------------------------------------------------
    // push / pop / shift / unshift
    // ------------------------------------------------------------------

    /// <summary>
    /// Write-extending fast paths (push/unshift) store at indices that were
    /// previously absent, where Set would consult the prototype chain for
    /// indexed properties or accessors. Without a no-elements protector we
    /// therefore require an element-free prototype chain before bypassing it.
    /// </summary>
    private static bool ReceiverChainFreeOfElements(JsObject receiver)
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

    internal static bool TryPushDense(JsObject obj, ReadOnlySpan<JsValue> items, out JsValue result)
    {
        result = default;
        if (
            items.Length == 0
            || !DenseArrayFastPath.TryGet(obj, out var target)
            || !target.IsExtensible
            || !target.LengthIsWritable
            || !ReceiverChainFreeOfElements(obj)
        )
            return false;

        var start = target.Length;
        const long maxSafeInteger = 9007199254740991L;
        if (start > maxSafeInteger - items.Length)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Invalid array length",
                "ARRAY_LENGTH_INVALID"
            );
        if (start + items.Length > uint.MaxValue)
            throw new JsRuntimeException(
                JsErrorKind.RangeError,
                "Invalid array length",
                "ARRAY_LENGTH_INVALID"
            );

        DenseArrayFastPath.EnsureCapacity(target, start + items.Length);
        var store = target.Dense!;
        for (var i = 0; i < items.Length; i++)
            store[start + i] = items[i];
        target.SetLength((uint)(start + items.Length));
        result = FromLength(start + items.Length);
        return true;
    }

    internal static bool TryPopDense(JsObject obj, out JsValue result)
    {
        result = default;
        if (!DenseArrayFastPath.TryGet(obj, out var target) || !target.LengthIsWritable)
            return false;

        var length = target.Length;
        if (length == 0)
        {
            result = JsValue.Undefined;
            return true;
        }

        var tail = target.Dense![length - 1];
        if (tail.IsTheHole)
            return false; // prototype chain must be consulted; use generic pop

        target.Dense![length - 1] = JsValue.TheHole;
        target.SetLength(length - 1);
        result = tail;
        return true;
    }

    internal static bool TryShiftDense(JsObject obj, out JsValue result)
    {
        result = default;
        if (
            !DenseArrayFastPath.TryGet(obj, out var target)
            || !target.LengthIsWritable
            || target.Length > int.MaxValue
        )
            return false;

        var length = target.Length;
        var store = target.Dense!;
        if (length == 0)
        {
            result = JsValue.Undefined;
            return true;
        }

        if (DenseArrayFastPath.RangeHasHole(store.AsSpan(0, (int)length)))
            return false; // moved range would need prototype consultation

        result = store[0];
        Array.Copy(store, 1, store, 0, length - 1);
        store[length - 1] = JsValue.TheHole;
        target.SetLength(length - 1);
        return true;
    }

    internal static bool TryUnshiftDense(
        JsObject obj,
        ReadOnlySpan<JsValue> items,
        out JsValue result
    )
    {
        result = default;
        if (
            items.Length == 0
            || !DenseArrayFastPath.TryGet(obj, out var target)
            || !target.IsExtensible
            || !target.LengthIsWritable
            || target.Length > int.MaxValue
            || !ReceiverChainFreeOfElements(obj)
        )
            return false;

        var length = target.Length;
        var newLength = length + items.Length;
        if (newLength > MaxSafeIntegerLength || newLength > uint.MaxValue)
            return false;
        if (DenseArrayFastPath.RangeHasHole(target.Dense!.AsSpan(0, (int)length)))
            return false;

        DenseArrayFastPath.EnsureCapacity(target, newLength);
        var store = target.Dense!;
        Array.Copy(store, 0, store, items.Length, length);
        for (var i = 0; i < items.Length; i++)
            store[i] = items[i];
        target.SetLength((uint)newLength);
        result = FromLength(newLength);
        return true;
    }
}
