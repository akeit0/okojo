using System.Globalization;
using System.Text;
using static Okojo.Runtime.JsRealm;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private const long MaxSafeIntegerLength = 9007199254740991L;

    internal JsHostFunction CreateArrayConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var callee = (JsHostFunction)info.Function;
            var args = info.Arguments;
            var array = realm.CreateArrayObject();
            if (info.IsConstruct)
                array.Prototype =
                    GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, ArrayPrototype);
            if (args.Length == 0)
                return array;

            if (args.Length == 1 && args[0].IsNumber)
            {
                var len = args[0].NumberValue;
                if (len < 0 || len > uint.MaxValue || len != Math.Truncate(len))
                    throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length",
                        "ARRAY_LENGTH_INVALID");
                array.SetLength((uint)len);
                return array;
            }

            for (uint i = 0; i < (uint)args.Length; i++)
                array.SetElement(i, args[(int)i]);
            return array;
        }, "Array", 1, true);
    }

    internal void InstallArrayPrototypeBuiltins()
    {
        var realm = Realm;
        const int atomAt = IdAt;
        const int atomConcat = IdConcat;
        const int atomCopyWithin = IdCopyWithin;
        const int atomEntries = IdEntries;
        const int atomEvery = IdEvery;
        const int atomFill = IdFill;
        const int atomFilter = IdFilter;
        const int atomFind = IdFind;
        const int atomFindIndex = IdFindIndex;
        const int atomFindLast = IdFindLast;
        const int atomFindLastIndex = IdFindLastIndex;
        const int atomFlat = IdFlat;
        const int atomFlatMap = IdFlatMap;
        const int atomForEach = IdForEach;
        const int atomIncludes = IdIncludes;
        const int atomIndexOf = IdIndexOf;
        const int atomJoin = IdJoin;
        const int atomKeys = IdKeys;
        const int atomLastIndexOf = IdLastIndexOf;
        const int atomMap = IdMap;
        const int atomNext = IdNext;
        const int atomPop = IdPop;
        const int atomPush = IdPush;
        const int atomReduce = IdReduce;
        const int atomReduceRight = IdReduceRight;
        const int atomReverse = IdReverse;
        const int atomShift = IdShift;
        const int atomSlice = IdSlice;
        const int atomSome = IdSome;
        const int atomSort = IdSort;
        const int atomSplice = IdSplice;
        const int atomToLocaleString = IdToLocaleString;
        const int atomToReversed = IdToReversed;
        const int atomToSorted = IdToSorted;
        const int atomToSpliced = IdToSpliced;
        const int atomUnshift = IdUnshift;
        const int atomValues = IdValues;
        const int atomWith = IdWith;
        var arrayUnscopables = CreateArrayUnscopablesObject(realm);

        var toStringFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.toString");
            if (obj.TryGetProperty("join", out var joinValue) &&
                joinValue.TryGetObject(out var joinObj) &&
                joinObj is JsFunction joinFn)
                return realm.InvokeFunction(joinFn, thisValue, ReadOnlySpan<JsValue>.Empty);

            return realm.InvokeFunction(realm.ObjectPrototypeToStringIntrinsic, thisValue,
                ReadOnlySpan<JsValue>.Empty);
        }, "toString", 0);

        var joinFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.join");
            var length = GetArrayLikeLengthLong(realm, obj);
            var separator = args.Length == 0 || args[0].IsUndefined ? "," : realm.ToJsStringSlowPath(args[0]);
            if (length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (long i = 0; i < length; i++)
            {
                if (i > 0)
                    sb.Append(separator);
                if (!TryGetArrayLikeIndex(realm, obj, i, out var elem) || elem.IsUndefined || elem.IsNull)
                    continue;
                sb.Append(realm.ToJsStringSlowPath(elem));
            }

            return sb.ToString();
        }, "join", 1);

        var toLocaleStringFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.toLocaleString");
            var length = GetArrayLikeLengthLong(realm, obj);
            if (length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (long i = 0; i < length; i++)
            {
                if (i > 0)
                    sb.Append(',');

                if (!TryGetArrayLikeIndex(realm, obj, i, out var elem) || elem.IsUndefined || elem.IsNull)
                    continue;
                sb.Append(InvokeElementToLocaleString(realm, elem, info.Arguments));
            }

            return sb.ToString();
        }, "toLocaleString", 0);

        var atFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.at");
            var length = GetArrayLikeLengthLong(realm, obj);
            var index =
                NormalizeRelativeIndex(args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]), length);
            if (index < 0 || index >= length)
                return JsValue.Undefined;
            return TryGetArrayLikeIndex(realm, obj, index, out var value) ? value : JsValue.Undefined;
        }, "at", 1);

        var pushFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.push");
            var start = GetArrayLikeLengthLong(realm, obj);
            const long maxSafeInteger = 9007199254740991L;
            if (start > maxSafeInteger - args.Length)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            for (long i = 0; i < args.Length; i++)
                SetArrayLikeIndexOrThrow(realm, obj, start + i, args[(int)i]);
            var nextLen = checked(start + args.Length);
            if (obj is JsArray && nextLen > uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            SetArrayLikeLengthOrThrow(realm, obj, nextLen, "Array.prototype.push");
            return FromLength(nextLen);
        }, "push", 1);

        var popFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.pop");
            var length = GetArrayLikeLengthLong(realm, obj);
            if (length == 0)
            {
                SetArrayLikeLengthOrThrow(realm, obj, 0, "Array.prototype.pop");
                return JsValue.Undefined;
            }

            var index = length - 1;
            var element = TryGetArrayLikeIndex(realm, obj, index, out var value) ? value : JsValue.Undefined;
            DeleteArrayLikeIndexOrThrow(realm, obj, index);
            SetArrayLikeLengthOrThrow(realm, obj, index, "Array.prototype.pop");
            return element;
        }, "pop", 0);

        var shiftFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.shift");
            var length = GetArrayLikeLengthLong(realm, obj);
            if (length == 0)
            {
                SetArrayLikeLengthOrThrow(realm, obj, 0, "Array.prototype.shift");
                return JsValue.Undefined;
            }

            var first = TryGetArrayLikeIndex(realm, obj, 0, out var firstValue) ? firstValue : JsValue.Undefined;
            for (long k = 1; k < length; k++)
                MoveArrayLikeElement(realm, obj, k, k - 1);

            DeleteArrayLikeIndex(realm, obj, length - 1);
            SetArrayLikeLengthOrThrow(realm, obj, length - 1, "Array.prototype.shift");
            return first;
        }, "shift", 0);

        var unshiftFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.unshift");
            var length = GetArrayLikeLengthLong(realm, obj);
            var insertCount = (uint)args.Length;
            if (insertCount != 0 && length > MaxSafeIntegerLength - insertCount)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            if (insertCount != 0)
            {
                for (var k = length - 1; k >= 0; k--)
                    MoveArrayLikeElement(realm, obj, k, k + insertCount);
                for (uint i = 0; i < insertCount; i++)
                    SetArrayLikeIndexOrThrow(realm, obj, i, args[(int)i]);
            }

            var newLength = checked(length + insertCount);
            SetArrayLikeLengthOrThrow(realm, obj, newLength, "Array.prototype.unshift");
            return FromLength(newLength);
        }, "unshift", 1);

        var forEachFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.forEach");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.forEach");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;

            for (long k = 0; k < length; k++)
            {
                if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                    continue;
                InvokeArrayCallback(realm, callback, callbackThis, element, k, obj);
            }

            return JsValue.Undefined;
        }, "forEach", 1);

        var everyFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.every");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.every");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;

            for (long k = 0; k < length; k++)
            {
                if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                    continue;
                if (!ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return JsValue.False;
            }

            return JsValue.True;
        }, "every", 1);

        var someFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.some");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.some");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;

            for (long k = 0; k < length; k++)
            {
                if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                    continue;
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return JsValue.True;
            }

            return JsValue.False;
        }, "some", 1);

        var mapFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.map");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.map");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;
            var result = realm.CreateArrayObject();
            var resultLength = RequireArrayStorageLength(length);

            for (long k = 0; k < length; k++)
            {
                if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                    continue;
                FreshArrayOperations.DefineElement(result, (uint)k,
                    InvokeArrayCallback(realm, callback, callbackThis, element, k, obj));
            }

            result.SetLength(resultLength);
            return result;
        }, "map", 1);

        var filterFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.filter");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.filter");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;
            var result = realm.CreateArrayObject();
            long to = 0;

            for (long k = 0; k < length; k++)
            {
                if (!TryGetArrayLikeIndex(realm, obj, k, out var element))
                    continue;
                if (!ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    continue;
                DefineFreshArrayLikeIndex(result, to++, element);
            }

            result.SetLength((uint)to);
            return result;
        }, "filter", 1);

        var findFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.find");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.find");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;

            for (long k = 0; k < length; k++)
            {
                GetArrayLikeIndex(realm, obj, k, out var element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return element;
            }

            return JsValue.Undefined;
        }, "find", 1);

        var findIndexFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.findIndex");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.findIndex");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;

            for (long k = 0; k < length; k++)
            {
                GetArrayLikeIndex(realm, obj, k, out var element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return FromLength(k);
            }

            return JsValue.FromInt32(-1);
        }, "findIndex", 1);

        var findLastFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.findLast");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.findLast");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;

            for (var k = length - 1; k >= 0; k--)
            {
                GetArrayLikeIndex(realm, obj, k, out var element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return element;
            }

            return JsValue.Undefined;
        }, "findLast", 1);

        var findLastIndexFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.findLastIndex");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.findLastIndex");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;

            for (var k = length - 1; k >= 0; k--)
            {
                GetArrayLikeIndex(realm, obj, k, out var element);
                if (ToBoolean(InvokeArrayCallback(realm, callback, callbackThis, element, k, obj)))
                    return FromLength(k);
            }

            return JsValue.FromInt32(-1);
        }, "findLastIndex", 1);

        var includesFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.includes");
            var length = GetArrayLikeLengthLong(realm, obj);
            if (length == 0)
                return JsValue.False;

            var searchElement = args.Length == 0 ? JsValue.Undefined : args[0];
            var start = NormalizeStartIndex(args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d, length);
            for (var k = start; k < length; k++)
            {
                var element = TryGetArrayLikeIndex(realm, obj, k, out var value) ? value : JsValue.Undefined;
                if (JsValueSameValueZeroComparer.Instance.Equals(element, searchElement))
                    return JsValue.True;
            }

            return JsValue.False;
        }, "includes", 1);

        var indexOfFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.indexOf");
            var length = GetArrayLikeLengthLong(realm, obj);
            if (length == 0)
                return JsValue.FromInt32(-1);
            var searchElement = args.Length == 0 ? JsValue.Undefined : args[0];
            var start = NormalizeStartIndex(args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d, length);

            for (var k = start; k < length; k++)
            {
                if (!HasArrayLikeIndex(realm, obj, k))
                    continue;
                GetArrayLikeIndex(realm, obj, k, out var element);
                if (StrictEquals(element, searchElement))
                    return FromLength(k);
            }

            return JsValue.FromInt32(-1);
        }, "indexOf", 1);

        var lastIndexOfFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.lastIndexOf");
            var length = GetArrayLikeLengthLong(realm, obj);
            if (length == 0)
                return JsValue.FromInt32(-1);

            var searchElement = args.Length == 0 ? JsValue.Undefined : args[0];
            var start = args.Length > 1
                ? NormalizeLastIndex(realm.ToIntegerOrInfinity(args[1]), length)
                : length - 1;

            for (var k = start; k >= 0; k--)
            {
                if (!HasArrayLikeIndex(realm, obj, k))
                    continue;
                GetArrayLikeIndex(realm, obj, k, out var element);
                if (StrictEquals(element, searchElement))
                    return FromLength(k);
            }

            return JsValue.FromInt32(-1);
        }, "lastIndexOf", 1);

        var reduceFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.reduce");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.reduce");
            var hasAccumulator = args.Length > 1;
            var accumulator = hasAccumulator ? args[1] : JsValue.Undefined;

            long k = 0;
            if (!hasAccumulator)
            {
                while (k < length && !TryGetArrayLikeIndex(realm, obj, k, out accumulator))
                    k++;
                if (k >= length)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Reduce of empty array with no initial value");
                k++;
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
                    JsValue.FromObject(obj)
                ];
                accumulator = realm.InvokeFunction(callback, JsValue.Undefined, callbackArgs);
            }

            return accumulator;
        }, "reduce", 1);

        var reduceRightFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.reduceRight");
            var length = GetArrayLikeLengthLong(realm, obj);
            var callback = RequireArrayCallback(args, "Array.prototype.reduceRight");
            var hasAccumulator = args.Length > 1;
            var accumulator = hasAccumulator ? args[1] : JsValue.Undefined;

            var k = length - 1;
            if (!hasAccumulator)
            {
                while (k >= 0 && !TryGetArrayLikeIndex(realm, obj, k, out accumulator))
                    k--;
                if (k < 0)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Reduce of empty array with no initial value");
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
                    JsValue.FromObject(obj)
                ];
                accumulator = realm.InvokeFunction(callback, JsValue.Undefined, callbackArgs);
            }

            return accumulator;
        }, "reduceRight", 1);

        var reverseFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.reverse");
            var length = GetArrayLikeLengthLong(realm, obj);
            ReverseArrayLike(realm, obj, length);
            return obj;
        }, "reverse", 0);

        var fillFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.fill");
            var length = GetArrayLikeLengthLong(realm, obj);
            var value = args.Length == 0 ? JsValue.Undefined : args[0];
            var start = NormalizeRelativeIndex(args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d, length);
            var end = args.Length > 2 && !args[2].IsUndefined
                ? NormalizeRelativeIndex(realm.ToIntegerOrInfinity(args[2]), length)
                : length;
            if (start < 0)
                start = 0;
            if (end < start)
                end = start;

            for (var k = start; k < end; k++)
                SetArrayLikeIndexOrThrow(realm, obj, k, value);

            return obj;
        }, "fill", 1);

        var copyWithinFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.copyWithin");
            var length = GetArrayLikeLengthLong(realm, obj);
            var to = NormalizeRelativeIndex(args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d, length);
            var from = NormalizeRelativeIndex(args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d, length);
            var end = args.Length > 2 && !args[2].IsUndefined
                ? NormalizeRelativeIndex(realm.ToIntegerOrInfinity(args[2]), length)
                : length;
            if (length == 0)
                return obj;
            CopyWithinArrayLike(realm, obj, length, to, from, end);
            return obj;
        }, "copyWithin", 2);

        var sliceFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.slice");
            var length = GetArrayLikeLengthLong(realm, obj);
            var start = NormalizeRelativeIndex(args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d, length);
            var end = args.Length > 1 && !args[1].IsUndefined
                ? NormalizeRelativeIndex(realm.ToIntegerOrInfinity(args[1]), length)
                : length;
            var count = Math.Max(0, Math.Min(end, length) - Math.Min(start, length));
            if (count > uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            return SliceArrayLike(realm, obj, length, start, end);
        }, "slice", 2);

        var spliceFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.splice");
            var length = GetArrayLikeLengthLong(realm, obj);
            var start = NormalizeRelativeIndex(args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d, length);
            var deleteCount = args.Length switch
            {
                0 => 0,
                1 => length - start,
                _ => NormalizeDeleteCountLong(realm.ToIntegerOrInfinity(args[1]), length - start)
            };
            long itemCount = Math.Max(0, args.Length - 2);
            var newLength = checked(length - deleteCount + itemCount);
            if (newLength > MaxSafeIntegerLength)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            if (deleteCount > uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            ExecuteSplice(realm, obj, length, start, deleteCount, args.Length <= 2 ? [] : args[2..],
                out var deleted);
            return deleted;
        }, "splice", 2);

        var sortFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.sort");
            JsFunction? compareFn = null;
            if (args.Length > 0 && !args[0].IsUndefined)
            {
                if (!args[0].TryGetObject(out var compareObj) || compareObj is not JsFunction compare)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Array.prototype.sort comparefn must be a function or undefined");
                compareFn = compare;
            }

            ArraySortHelpers.SortArrayLike(realm, obj, GetArrayLikeLengthLong(realm, obj), compareFn,
                HasArrayLikeIndex, GetArrayLikeIndex, SetArrayLikeIndexOrThrowValue, DeleteArrayLikeIndexOrThrow);
            return obj;
        }, "sort", 1);

        var concatFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!realm.TryToObject(thisValue, out var receiver))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Array.prototype.concat called on null or undefined");

            var result = realm.CreateArrayObject();
            uint nextIndex = 0;

            AppendConcatValue(realm, result, JsValue.FromObject(receiver), ref nextIndex);
            for (var i = 0; i < args.Length; i++)
                AppendConcatValue(realm, result, args[i], ref nextIndex);

            result.SetLength(nextIndex);
            return result;
        }, "concat", 1);

        var flatFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.flat");
            var depthNum = args.Length == 0 || args[0].IsUndefined ? 1d : realm.ToIntegerOrInfinity(args[0]);
            var depth = double.IsPositiveInfinity(depthNum) ? int.MaxValue :
                depthNum <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(depthNum));
            var result = realm.CreateArrayObject();
            uint targetIndex = 0;
            FlattenIntoArray(realm, result, ref targetIndex, obj, GetArrayLikeLengthLong(realm, obj), depth);
            result.SetLength(targetIndex);
            return result;
        }, "flat", 0);

        var flatMapFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.flatMap");
            var callback = RequireArrayCallback(args, "Array.prototype.flatMap");
            var callbackThis = args.Length > 1 ? args[1] : JsValue.Undefined;
            var length = GetArrayLikeLengthLong(realm, obj);
            var result = realm.CreateArrayObject();
            uint targetIndex = 0;

            for (long k = 0; k < length; k++)
            {
                if (!HasArrayLikeIndex(realm, obj, k))
                    continue;
                GetArrayLikeIndex(realm, obj, k, out var element);
                var mapped = InvokeArrayCallback(realm, callback, callbackThis, element, k, obj);
                if (mapped.TryGetObject(out var mappedObj) && IsArrayObject(realm, mappedObj))
                    FlattenIntoArray(realm, result, ref targetIndex, mappedObj,
                        GetArrayLikeLengthLong(realm, mappedObj), 0);
                else
                    FreshArrayOperations.DefineElement(result, targetIndex++, mapped);
            }

            result.SetLength(targetIndex);
            return result;
        }, "flatMap", 1);

        var toReversedFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.toReversed");
            var length = GetArrayLikeLengthLong(realm, obj);
            var result = CreateDenseArrayLikeCopy(realm, obj, length, true);
            return result;
        }, "toReversed", 0);

        var toSortedFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.toSorted");
            JsFunction? compareFn = null;
            if (args.Length > 0 && !args[0].IsUndefined)
            {
                if (!args[0].TryGetObject(out var compareObj) || compareObj is not JsFunction compare)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Array.prototype.toSorted comparefn must be a function or undefined");
                compareFn = compare;
            }

            var length = GetArrayLikeLengthLong(realm, obj);
            var result = CreateDenseArrayLikeCopy(realm, obj, length, false);
            ArraySortHelpers.SortArrayLike(realm, result, length, compareFn,
                HasArrayLikeIndex, GetArrayLikeIndex, SetArrayLikeIndexOrThrowValue, DeleteArrayLikeIndexOrThrow);
            return result;
        }, "toSorted", 1);

        var toSplicedFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.toSpliced");
            var length = GetArrayLikeLengthLong(realm, obj);
            var start = NormalizeRelativeIndex(args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d, length);
            var deleteCount = args.Length switch
            {
                0 => 0,
                1 => length - start,
                _ => NormalizeDeleteCountLong(realm.ToIntegerOrInfinity(args[1]), length - start)
            };
            long itemCount = Math.Max(0, args.Length - 2);
            var newLength = checked(length - deleteCount + itemCount);
            if (newLength > MaxSafeIntegerLength)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            if (newLength > uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");

            var result = realm.CreateArrayObject();
            uint writeIndex = 0;

            for (long k = 0; k < start; k++)
            {
                GetArrayLikeIndex(realm, obj, k, out var value);
                FreshArrayOperations.DefineElement(result, writeIndex, value);
                writeIndex++;
            }

            for (var i = 2; i < args.Length; i++)
                FreshArrayOperations.DefineElement(result, writeIndex++, args[i]);

            for (var k = start + deleteCount; k < length; k++)
            {
                GetArrayLikeIndex(realm, obj, k, out var value);
                FreshArrayOperations.DefineElement(result, writeIndex, value);
                writeIndex++;
            }

            result.SetLength(RequireArrayStorageLength(newLength));
            return result;
        }, "toSpliced", 2);

        var withFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var obj = ThisArrayLikeObject(realm, thisValue, "Array.prototype.with");
            var length = GetArrayLikeLengthLong(realm, obj);
            if (length > uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");

            var relativeIndex = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            var actualIndexDouble = relativeIndex >= 0 ? relativeIndex : length + relativeIndex;
            if (double.IsInfinity(actualIndexDouble) || actualIndexDouble < 0 || actualIndexDouble >= length)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array index");
            var index = (long)actualIndexDouble;

            var result = realm.CreateArrayObject();
            var replacementValue = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (long k = 0; k < length; k++)
            {
                if (k == index)
                {
                    FreshArrayOperations.DefineElement(result, (uint)k, replacementValue);
                    continue;
                }

                GetArrayLikeIndex(realm, obj, k, out var value);
                FreshArrayOperations.DefineElement(result, (uint)k, value);
            }

            result.SetLength(RequireArrayStorageLength(length));
            return result;
        }, "with", 2);

        var valuesFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var arrayLike = ThisArrayLikeObject(realm, thisValue, "Array.prototype.values");
            return new JsArrayIteratorObject(realm, arrayLike,
                JsArrayIteratorObject.IterationKind.Values);
        }, "values", 0);
        ArrayPrototypeValuesFunction = valuesFn;

        var keysFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var arrayLike = ThisArrayLikeObject(realm, thisValue, "Array.prototype.keys");
            return new JsArrayIteratorObject(realm, arrayLike,
                JsArrayIteratorObject.IterationKind.Keys);
        }, "keys", 0);

        var entriesFn = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var arrayLike = ThisArrayLikeObject(realm, thisValue, "Array.prototype.entries");
            return new JsArrayIteratorObject(realm, arrayLike,
                JsArrayIteratorObject.IterationKind.Entries);
        }, "entries", 0);

        var iteratorNextFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsArrayIteratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Array Iterator.prototype.next called on incompatible receiver");
            return iterator.Next();
        }, "next", 0);

        var iteratorSelfFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsArrayIteratorObject)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Array Iterator [Symbol.iterator] called on incompatible receiver");
            return thisValue;
        }, "[Symbol.iterator]", 0);

        Span<PropertyDefinition> iteratorProtoDefs =
        [
            PropertyDefinition.Mutable(atomNext, JsValue.FromObject(iteratorNextFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(iteratorSelfFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Array Iterator"),
                configurable: true)
        ];
        ArrayIteratorPrototype.DefineNewPropertiesNoCollision(realm, iteratorProtoDefs);

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, realm.ArrayConstructor),
            PropertyDefinition.Mutable(atomAt, atFn),
            PropertyDefinition.Mutable(atomConcat, concatFn),
            PropertyDefinition.Mutable(atomCopyWithin, copyWithinFn),
            PropertyDefinition.Mutable(atomEntries, entriesFn),
            PropertyDefinition.Mutable(atomEvery, everyFn),
            PropertyDefinition.Mutable(atomFill, fillFn),
            PropertyDefinition.Mutable(atomFilter, filterFn),
            PropertyDefinition.Mutable(atomFind, findFn),
            PropertyDefinition.Mutable(atomFindIndex, findIndexFn),
            PropertyDefinition.Mutable(atomFindLast, findLastFn),
            PropertyDefinition.Mutable(atomFindLastIndex, findLastIndexFn),
            PropertyDefinition.Mutable(atomFlat, flatFn),
            PropertyDefinition.Mutable(atomFlatMap, flatMapFn),
            PropertyDefinition.Mutable(atomForEach, forEachFn),
            PropertyDefinition.Mutable(atomIncludes, includesFn),
            PropertyDefinition.Mutable(atomIndexOf, indexOfFn),
            PropertyDefinition.Mutable(atomJoin, joinFn),
            PropertyDefinition.Mutable(atomKeys, keysFn),
            PropertyDefinition.Mutable(atomLastIndexOf, lastIndexOfFn),
            PropertyDefinition.Mutable(atomMap, mapFn),
            PropertyDefinition.Mutable(atomPop, popFn),
            PropertyDefinition.Mutable(atomPush, pushFn),
            PropertyDefinition.Mutable(atomReduce, reduceFn),
            PropertyDefinition.Mutable(atomReduceRight, reduceRightFn),
            PropertyDefinition.Mutable(atomReverse, reverseFn),
            PropertyDefinition.Mutable(atomShift, shiftFn),
            PropertyDefinition.Mutable(atomSlice, sliceFn),
            PropertyDefinition.Mutable(atomSome, someFn),
            PropertyDefinition.Mutable(atomSort, sortFn),
            PropertyDefinition.Mutable(atomSplice, spliceFn),
            PropertyDefinition.Mutable(atomToLocaleString, toLocaleStringFn),
            PropertyDefinition.Mutable(atomToReversed, toReversedFn),
            PropertyDefinition.Mutable(atomToSorted, toSortedFn),
            PropertyDefinition.Mutable(atomToSpliced, toSplicedFn),
            PropertyDefinition.Mutable(IdToString, toStringFn),
            PropertyDefinition.Mutable(atomUnshift, unshiftFn),
            PropertyDefinition.Mutable(atomValues, valuesFn),
            PropertyDefinition.Mutable(atomWith, withFn),
            PropertyDefinition.Const(IdSymbolUnscopables, JsValue.FromObject(arrayUnscopables),
                configurable: true),
            PropertyDefinition.Mutable(IdSymbolIterator, valuesFn)
        ];
        realm.ArrayPrototype.DefineNewPropertiesNoCollision(realm, protoDefs);
    }

    private static JsPlainObject CreateArrayUnscopablesObject(JsRealm realm)
    {
        var unscopables = new JsPlainObject(realm, false) { Prototype = null };
        unscopables.DefineNewPropertiesNoCollision(realm, [
                PropertyDefinition.OpenData(IdAt, JsValue.True),
                PropertyDefinition.OpenData(IdCopyWithin, JsValue.True),
                PropertyDefinition.OpenData(IdEntries, JsValue.True),
                PropertyDefinition.OpenData(IdFill, JsValue.True),
                PropertyDefinition.OpenData(IdFind, JsValue.True),
                PropertyDefinition.OpenData(IdFindIndex, JsValue.True),
                PropertyDefinition.OpenData(IdFindLast, JsValue.True),
                PropertyDefinition.OpenData(IdFindLastIndex, JsValue.True),
                PropertyDefinition.OpenData(IdFlat, JsValue.True),
                PropertyDefinition.OpenData(IdFlatMap, JsValue.True),
                PropertyDefinition.OpenData(IdIncludes, JsValue.True),
                PropertyDefinition.OpenData(IdKeys, JsValue.True),
                PropertyDefinition.OpenData(IdToReversed, JsValue.True),
                PropertyDefinition.OpenData(IdToSorted, JsValue.True),
                PropertyDefinition.OpenData(IdToSpliced, JsValue.True),
                PropertyDefinition.OpenData(IdValues, JsValue.True)
            ]
        );
        return unscopables;
    }

    internal static void CreateDataPropertyOrThrowForArrayLike(JsRealm realm, JsObject target, long index,
        JsValue value, string methodName)
    {
        var key = index.ToString(CultureInfo.InvariantCulture);
        var descriptor = new JsPlainObject(realm);
        descriptor.DefineNewPropertiesNoCollision(realm,
        [
            PropertyDefinition.OpenData(IdValue, value),
            PropertyDefinition.OpenData(IdWritable, JsValue.True),
            PropertyDefinition.OpenData(IdEnumerable, JsValue.True),
            PropertyDefinition.OpenData(IdConfigurable, JsValue.True)
        ]);

        Span<JsValue> defineArgs =
        [
            JsValue.FromObject(target),
            JsValue.FromString(key),
            JsValue.FromObject(descriptor)
        ];

        try
        {
            _ = realm.InvokeObjectConstructorMethod("defineProperty", defineArgs);
        }
        catch (JsRuntimeException ex)
        {
            if (ex.Kind == JsErrorKind.TypeError && string.IsNullOrEmpty(ex.DetailCode))
                throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} could not define property");
            throw;
        }
    }

    private static JsObject ThisArrayLikeObject(JsRealm realm, in JsValue value, string methodName)
    {
        if (realm.TryToObject(value, out var obj))
            return obj;
        throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} called on null or undefined");
    }

    private static JsFunction RequireArrayCallback(ReadOnlySpan<JsValue> args, string methodName)
    {
        if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) || callbackObj is not JsFunction callback)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} callback must be a function");
        return callback;
    }

    internal static string InvokeElementToLocaleString(JsRealm realm, in JsValue element, ReadOnlySpan<JsValue> args)
    {
        if (!realm.TryToObject(element, out var elemObj) ||
            !elemObj.TryGetProperty("toLocaleString", out var localeFnValue) ||
            !localeFnValue.TryGetObject(out var localeFnObj) ||
            localeFnObj is not JsFunction localeFn)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Array.prototype.toLocaleString element toLocaleString must be callable");

        var locales = args.Length > 0 ? args[0] : JsValue.Undefined;
        var options = args.Length > 1 ? args[1] : JsValue.Undefined;
        var localeString = realm.InvokeFunction(localeFn, element, [locales, options]);
        return realm.ToJsStringSlowPath(localeString);
    }

    private static JsValue InvokeArrayCallback(
        JsRealm realm,
        JsFunction callback,
        in JsValue thisArg,
        in JsValue element,
        long index,
        JsObject source)
    {
        Span<JsValue> callbackArgs =
        [
            element,
            FromLength(index),
            JsValue.FromObject(source)
        ];
        return realm.InvokeFunction(callback, thisArg, callbackArgs);
    }


    private static long GetArrayLikeLengthLong(JsRealm realm, JsObject obj)
    {
        if (obj is JsTypedArrayObject typedArray)
            return typedArray.Length;
        if (obj is JsArray array)
            return array.Length;

        if (!obj.TryGetPropertyAtom(realm, IdLength, out var lengthValue, out _))
            return 0;

        var lengthNum = realm.ToNumberSlowPath(lengthValue);
        if (double.IsNaN(lengthNum) || lengthNum <= 0)
            return 0;

        const double maxSafeInteger = 9007199254740991d;
        return (long)Math.Min(maxSafeInteger, Math.Floor(lengthNum));
    }

    internal static void SetArrayLikeLengthOrThrow(JsRealm realm, JsObject obj, long length, string methodName)
    {
        if (length < 0)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length", "ARRAY_LENGTH_INVALID");

        if (obj is JsArray array)
        {
            var newLength = RequireArrayStorageLength(length);
            if (!array.TrySetPropertyAtom(realm, IdLength, FromLength(newLength), out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} could not set length");
            return;
        }

        if (!obj.TrySetPropertyAtom(realm, IdLength, FromLength(length), out _))
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} could not set length");
    }

    private static JsValue FromLength(uint value)
    {
        return FromLength((long)value);
    }

    private static JsValue FromLength(long value)
    {
        return value <= int.MaxValue ? JsValue.FromInt32((int)value) : new(value);
    }

    internal static long NormalizeRelativeIndex(double index, long length)
    {
        var relative = double.IsNegativeInfinity(index) ? long.MinValue :
            double.IsPositiveInfinity(index) ? long.MaxValue :
            (long)Math.Truncate(index);
        if (relative < 0)
            return Math.Max(length + relative, 0);
        return Math.Min(relative, length);
    }

    private static long NormalizeStartIndex(double fromIndex, long length)
    {
        var index = double.IsNegativeInfinity(fromIndex) ? long.MinValue :
            double.IsPositiveInfinity(fromIndex) ? long.MaxValue :
            (long)Math.Truncate(fromIndex);
        if (index < 0)
            index = length + index;
        if (index < 0)
            index = 0;
        if (index > length)
            index = length;
        return index;
    }

    private static long NormalizeLastIndex(double fromIndex, long length)
    {
        var index = double.IsNegativeInfinity(fromIndex) ? long.MinValue :
            double.IsPositiveInfinity(fromIndex) ? long.MaxValue :
            (long)Math.Truncate(fromIndex);
        if (index >= 0)
            return Math.Min(index, length - 1);
        return length + index;
    }

    private static long NormalizeDeleteCountLong(double deleteCount, long maxDeleteCount)
    {
        if (double.IsNaN(deleteCount) || deleteCount <= 0)
            return 0;
        if (double.IsPositiveInfinity(deleteCount))
            return maxDeleteCount;
        return (long)Math.Min(maxDeleteCount, Math.Floor(deleteCount));
    }

    private static void MoveArrayLikeElement(JsRealm realm, JsObject obj, long from, long to)
    {
        if (TryGetArrayLikeIndex(realm, obj, from, out var value))
            SetArrayLikeIndexOrThrow(realm, obj, to, value);
        else
            DeleteArrayLikeIndex(realm, obj, to);
    }

    private static void ReverseArrayLike(JsRealm realm, JsObject obj, long length)
    {
        var middle = length / 2;
        for (long lower = 0; lower < middle; lower++)
        {
            var upper = length - lower - 1;
            var lowerExists = HasArrayLikeIndex(realm, obj, lower);
            var lowerValue = JsValue.Undefined;
            var upperValue = JsValue.Undefined;
            if (lowerExists)
                GetArrayLikeIndex(realm, obj, lower, out lowerValue);

            var upperExists = HasArrayLikeIndex(realm, obj, upper);
            if (upperExists)
                GetArrayLikeIndex(realm, obj, upper, out upperValue);

            if (lowerExists && upperExists)
            {
                SetArrayLikeIndexOrThrow(realm, obj, lower, upperValue);
                SetArrayLikeIndexOrThrow(realm, obj, upper, lowerValue);
            }
            else if (!lowerExists && upperExists)
            {
                SetArrayLikeIndexOrThrow(realm, obj, lower, upperValue);
                DeleteArrayLikeIndexOrThrow(realm, obj, upper);
            }
            else if (lowerExists)
            {
                DeleteArrayLikeIndexOrThrow(realm, obj, lower);
                SetArrayLikeIndexOrThrow(realm, obj, upper, lowerValue);
            }
        }
    }

    private static void CopyWithinArrayLike(JsRealm realm, JsObject obj, long length, long to, long from, long end)
    {
        if (to < 0)
            to = 0;
        if (from < 0)
            from = 0;
        if (end < from)
            end = from;

        var count = Math.Min(end - from, length - to);
        if (count <= 0)
            return;

        long direction = 1;
        if (from < to && to < from + count)
        {
            direction = -1;
            from += count - 1;
            to += count - 1;
        }

        while (count-- > 0)
        {
            if (HasArrayLikeIndex(realm, obj, from))
            {
                GetArrayLikeIndex(realm, obj, from, out var value);
                SetArrayLikeIndexOrThrow(realm, obj, to, value);
            }
            else
            {
                DeleteArrayLikeIndexOrThrow(realm, obj, to);
            }

            from += direction;
            to += direction;
        }
    }

    private static bool HasArrayLikeIndex(JsRealm realm, JsObject obj, long index)
    {
        var key = JsValue.FromString(index.ToString(CultureInfo.InvariantCulture));
        if (obj.TryHasPropertyViaTrap(realm, key, out var viaTrap))
            return viaTrap;

        return HasArrayLikeIndexWithoutGet(realm, obj, index);
    }

    private static bool HasArrayLikeIndexWithoutGet(JsRealm realm, JsObject? obj, long index)
    {
        if (obj is null)
            return false;

        var key = JsValue.FromString(index.ToString(CultureInfo.InvariantCulture));
        if (obj.HasOwnPropertyKey(realm, key))
            return true;

        return HasArrayLikeIndexWithoutGet(realm, obj.Prototype, index);
    }

    private static void GetArrayLikeIndex(JsRealm realm, JsObject obj, long index, out JsValue value)
    {
        if (!TryGetArrayLikeIndex(realm, obj, index, out value))
            value = JsValue.Undefined;
    }

    internal static bool TryGetArrayLikeIndex(JsRealm realm, JsObject obj, long index, out JsValue value)
    {
        if ((ulong)index < uint.MaxValue)
            if (obj.TryGetElement((uint)index, out value))
                return true;

        return obj.TryGetProperty(index.ToString(CultureInfo.InvariantCulture), out value);
    }

    private static void SetArrayLikeIndexOrThrow(JsRealm realm, JsObject obj, long index, in JsValue value)
    {
        if ((ulong)index < uint.MaxValue)
        {
            if (!obj.TrySetElement((uint)index, value))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to property");
            return;
        }

        var key = index.ToString(CultureInfo.InvariantCulture);
        var atom = realm.Atoms.InternNoCheck(key);
        if (!obj.TrySetPropertyAtom(realm, atom, value, out _))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to property");
    }

    private static void SetArrayLikeIndexOrThrowValue(JsRealm realm, JsObject obj, long index, JsValue value)
    {
        SetArrayLikeIndexOrThrow(realm, obj, index, value);
    }


    private static void DefineFreshArrayLikeIndex(JsArray result, long index, in JsValue value)
    {
        if ((ulong)index >= uint.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length", "ARRAY_LENGTH_INVALID");

        FreshArrayOperations.DefineElement(result, (uint)index, value);
    }

    private static void DeleteArrayLikeIndex(JsRealm realm, JsObject obj, long index)
    {
        if ((ulong)index < uint.MaxValue)
        {
            if (obj.HasOwnElement((uint)index))
                if (obj.DeleteElement((uint)index))
                    return;

            return;
        }

        var key = index.ToString(CultureInfo.InvariantCulture);
        var atom = realm.Atoms.InternNoCheck(key);
        if (!obj.DeletePropertyAtom(realm, atom))
            return;
    }

    private static void DeleteArrayLikeIndexOrThrow(JsRealm realm, JsObject obj, long index)
    {
        if ((ulong)index < uint.MaxValue)
        {
            if (!obj.HasOwnElement((uint)index))
                return;
            if (obj.DeleteElement((uint)index))
                return;

            throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot delete property during copyWithin");
        }

        var key = index.ToString(CultureInfo.InvariantCulture);
        var atom = realm.Atoms.InternNoCheck(key);
        if (!obj.DeletePropertyAtom(realm, atom))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot delete property during copyWithin");
    }

    private static JsArray SliceArrayLike(JsRealm realm, JsObject obj, long length, long start, long end)
    {
        if (start < 0)
            start = 0;
        if (end < start)
            end = start;
        if (end > length)
            end = length;

        var result = realm.CreateArrayObject();
        uint to = 0;
        for (var k = start; k < end; k++, to++)
        {
            if (!TryGetArrayLikeIndex(realm, obj, k, out var value))
                continue;
            FreshArrayOperations.DefineElement(result, to, value);
        }

        result.SetLength(RequireArrayStorageLength(Math.Max(0, end - start)));
        return result;
    }

    private static JsArray CreateDenseArrayLikeCopy(JsRealm realm, JsObject obj, long length, bool reverse)
    {
        var resultLength = RequireArrayStorageLength(length);
        var result = realm.CreateArrayObject();
        for (long k = 0; k < length; k++)
        {
            var from = reverse ? length - 1 - k : k;
            GetArrayLikeIndex(realm, obj, from, out var value);
            FreshArrayOperations.DefineElement(result, (uint)k, value);
        }

        result.SetLength(resultLength);
        return result;
    }

    private static void ExecuteSplice(
        JsRealm realm,
        JsObject obj,
        long length,
        long actualStart,
        long actualDeleteCount,
        ReadOnlySpan<JsValue> items,
        out JsArray deletedElements)
    {
        deletedElements = obj.Realm.CreateArrayObject();
        for (long k = 0; k < actualDeleteCount; k++)
        {
            var from = actualStart + k;
            if (TryGetArrayLikeIndex(realm, obj, from, out var value))
                FreshArrayOperations.DefineElement(deletedElements, (uint)k, value);
        }

        deletedElements.SetLength((uint)actualDeleteCount);

        long itemCount = items.Length;
        if (itemCount < actualDeleteCount)
        {
            for (var k = actualStart; k < length - actualDeleteCount; k++)
                MoveArrayLikeElement(obj.Realm, obj, k + actualDeleteCount, k + itemCount);

            for (var k = length; k > length - (actualDeleteCount - itemCount); k--)
                DeleteArrayLikeIndexOrThrow(realm, obj, k - 1);
        }
        else if (itemCount > actualDeleteCount)
        {
            for (var k = length - actualDeleteCount; k > actualStart; k--)
                MoveArrayLikeElement(obj.Realm, obj, k + actualDeleteCount - 1, k + itemCount - 1);
        }

        for (long k = 0; k < itemCount; k++)
            SetArrayLikeIndexOrThrow(realm, obj, actualStart + k, items[(int)k]);

        SetArrayLikeLengthOrThrow(realm, obj, checked(length - actualDeleteCount + itemCount),
            "Array.prototype.splice");
    }

    private static void FlattenIntoArray(
        JsRealm realm,
        JsArray target,
        ref uint targetIndex,
        JsObject source,
        long sourceLength,
        int depth)
    {
        for (long k = 0; k < sourceLength; k++)
        {
            if (!TryGetArrayLikeIndex(realm, source, k, out var element))
                continue;

            if (depth > 0 && element.TryGetObject(out var elementObj) && IsArrayObject(realm, elementObj))
            {
                FlattenIntoArray(realm, target, ref targetIndex, elementObj, GetArrayLikeLengthLong(realm, elementObj),
                    depth - 1);
                continue;
            }

            FreshArrayOperations.DefineElement(target, targetIndex++, element);
        }
    }

    private static void AppendConcatValue(JsRealm realm, JsArray result, in JsValue item, ref uint nextIndex)
    {
        const ulong maxSafeInteger = 9007199254740991UL;

        if (!item.TryGetObject(out var obj))
        {
            result.DefineOwnOpenElementSparse(nextIndex++, item);
            return;
        }

        if (!IsConcatSpreadable(realm, obj))
        {
            result.DefineOwnOpenElementSparse(nextIndex++, item);
            return;
        }

        var length = GetConcatSpreadableLength(realm, obj);
        if (nextIndex + length > maxSafeInteger)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid array length", "ARRAY_LENGTH_INVALID");

        if (length > uint.MaxValue)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid array length", "ARRAY_LENGTH_INVALID");

        for (uint k = 0; k < length; k++)
        {
            if (obj.TryGetElement(k, out var value))
                result.DefineOwnOpenElementSparse(nextIndex, value);
            nextIndex++;
        }
    }

    private static bool IsConcatSpreadable(JsRealm realm, JsObject obj)
    {
        if (obj.TryGetPropertyAtom(realm, IdSymbolIsConcatSpreadable, out var spreadable, out _))
            if (!spreadable.IsUndefined)
                return ToBoolean(spreadable);

        return IsArrayObject(realm, obj);
    }

    private static ulong GetConcatSpreadableLength(JsRealm realm, JsObject obj)
    {
        if (obj is JsArray array)
            return array.Length;

        if (!obj.TryGetProperty("length", out var lengthValue))
            return 0;

        var lengthNum = realm.ToNumberSlowPath(lengthValue);
        if (double.IsNaN(lengthNum) || lengthNum <= 0)
            return 0;
        const double maxSafeInteger = 9007199254740991d;
        return (ulong)Math.Min(maxSafeInteger, Math.Floor(lengthNum));
    }

    private static uint RequireArrayStorageLength(long length)
    {
        if (length < 0 || length > uint.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length", "ARRAY_LENGTH_INVALID");
        return (uint)length;
    }

    private static bool IsArrayObject(JsRealm realm, JsObject obj)
    {
        while (true)
        {
            if (obj is JsArray)
                return true;
            if (obj.TryGetProxyTargetOrThrow(realm, out var target))
            {
                obj = target;
                continue;
            }

            return false;
        }
    }
}
