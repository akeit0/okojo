using System.Text;
using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    internal JsHostFunction CreateTypedArrayConstructor(TypedArrayElementKind kind)
    {
        var name = kind.GetConstructorName();
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, $"Constructor {name} requires 'new'");

            if (args.Length != 0 &&
                args[0].TryGetObject(out var arg0Obj) &&
                arg0Obj is JsArrayBufferObject arrayBuffer)
                return CreateTypedArrayView(realm, kind, arrayBuffer, args, info.NewTarget, callee);

            if (args.Length != 0 &&
                args[0].TryGetObject(out arg0Obj))
                return CreateTypedArrayFromObject(realm, kind, arg0Obj, info.NewTarget, callee);

            uint length = 0;
            if (args.Length != 0 && !args[0].IsUndefined)
                length = ToTypedArrayLength(realm, args[0]);

            var prototype = GetTypedArrayConstructionPrototype(realm, kind, info.NewTarget, callee);
            return new JsTypedArrayObject(realm, length, kind, prototype);
        }, name, 3, true);
    }

    private JsHostFunction CreateDataViewConstructor()
    {
        return new(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Constructor DataView requires 'new'");
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "First argument to DataView constructor must be an ArrayBuffer");

            var byteOffset = args.Length > 1 && !args[1].IsUndefined ? realm.ToTypedArrayByteOffset(args[1]) : 0;
            if (buffer.IsDetached)
                throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

            var initialBufferByteLength = buffer.ByteLength;
            if (byteOffset > initialBufferByteLength)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid DataView length");

            var lengthTracking = args.Length <= 2 || args[2].IsUndefined;
            var byteLength = lengthTracking
                ? initialBufferByteLength - byteOffset
                : ToTypedArrayLength(realm, args[2]);
            if (!lengthTracking && byteLength > initialBufferByteLength - byteOffset)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid DataView length");

            var prototype =
                realm.Intrinsics.GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                    realm.DataViewPrototype);
            if (buffer.IsDetached)
                throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

            var finalBufferByteLength = buffer.ByteLength;
            if (byteOffset > finalBufferByteLength)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid DataView length");
            if (!lengthTracking && byteLength > finalBufferByteLength - byteOffset)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid DataView length");

            return new JsDataViewObject(realm, buffer, byteOffset, byteLength, lengthTracking, prototype);
        }, "DataView", 1, true);
    }

    private void InstallTypedArrayConstructorBuiltins()
    {
        const int atomAt = IdAt;
        const int atomByteLength = IdByteLength;
        const int atomBytesPerElement = IdBytesPerElement;
        const int atomCopyWithin = IdCopyWithin;
        const int atomEntries = IdEntries;
        const int atomEvery = IdEvery;
        const int atomFill = IdFill;
        const int atomFilter = IdFilter;
        const int atomFind = IdFind;
        const int atomFindIndex = IdFindIndex;
        const int atomFindLast = IdFindLast;
        const int atomFindLastIndex = IdFindLastIndex;
        const int atomForEach = IdForEach;
        const int atomFrom = IdFrom;
        const int atomFromBase64 = IdFromBase64;
        const int atomFromHex = IdFromHex;
        const int atomIncludes = IdIncludes;
        const int atomIndexOf = IdIndexOf;
        const int atomJoin = IdJoin;
        const int atomKeys = IdKeys;
        const int atomLastIndexOf = IdLastIndexOf;
        const int atomMap = IdMap;
        const int atomNextLocal = IdNext;
        const int atomOf = IdOf;
        const int atomReduce = IdReduce;
        const int atomReduceRight = IdReduceRight;
        const int atomReverse = IdReverse;
        const int atomSet = IdSet;
        const int atomSetFromBase64 = IdSetFromBase64;
        const int atomSetFromHex = IdSetFromHex;
        const int atomSlice = IdSlice;
        const int atomSome = IdSome;
        const int atomSort = IdSort;
        const int atomSubarray = IdSubarray;
        const int atomToLocaleString = IdToLocaleString;
        const int atomToBase64 = IdToBase64;
        const int atomToHex = IdToHex;
        const int atomToReversed = IdToReversed;
        const int atomToSorted = IdToSorted;
        const int atomValues = IdValues;
        const int atomWith = IdWith;
        const int atomBuffer = IdBuffer;
        const int atomByteOffset = IdByteOffset;

        var lengthGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromInt32((int)RequireTypedArrayObject(realm, thisValue).Length);
        }, "get length", 0);
        var byteLengthGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromInt32((int)RequireTypedArrayObject(realm, thisValue).ByteLength);
        }, "get byteLength", 0);
        var bufferGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return RequireTypedArrayObject(realm, thisValue).Buffer;
        }, "get buffer", 0);
        var byteOffsetGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromInt32((int)RequireTypedArrayObject(realm, thisValue).ByteOffset);
        }, "get byteOffset", 0);
        var toStringTagGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!TryGetTypedArrayValue(thisValue, out var typedArray))
                return JsValue.Undefined;
            return typedArray.TypeName;
        }, "get [Symbol.toStringTag]", 0);

        var fromFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.from requires a constructor this value");
            if (args.Length == 0 || !realm.TryToObject(args[0], out var items))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.from source must not be null or undefined");

            JsFunction? mapFn = null;
            var thisArg = JsValue.Undefined;
            if (args.Length > 1 && !args[1].IsUndefined)
            {
                if (!args[1].TryGetObject(out var mapFnObj) || mapFnObj is not JsFunction callback)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray.from mapfn must be a function");
                mapFn = callback;
                if (args.Length > 2)
                    thisArg = args[2];
            }

            if (JsRealm.TryGetIteratorObjectForArrayFrom(realm, items, out var iterator))
            {
                var sourceValues = new List<JsValue>(4);
                while (true)
                {
                    var nextValue = realm.StepIteratorForArrayFrom(iterator, out var done);
                    if (done)
                        break;
                    sourceValues.Add(nextValue);
                }

                var iterLenArg = new InlineJsValueArray1
                {
                    Item0 = sourceValues.Count <= int.MaxValue
                        ? JsValue.FromInt32(sourceValues.Count)
                        : new(sourceValues.Count)
                };
                var iterTargetValue =
                    realm.ConstructWithExplicitNewTarget(ctor, iterLenArg.AsSpan(), ctor, 0);
                var iterTarget = RequireTypedArrayCreateResult(realm, iterTargetValue, (uint)sourceValues.Count);

                for (var i = 0; i < sourceValues.Count; i++)
                {
                    var mappedValue = MapTypedArrayFromValue(realm, mapFn, thisArg, sourceValues[i], i);
                    iterTarget.TrySetElement((uint)i, mappedValue);
                }

                return iterTargetValue;
            }

            if (TryGetTypedArrayFromLength(realm, items, out var sourceLength))
            {
                var lenArg = new InlineJsValueArray1
                {
                    Item0 = sourceLength <= int.MaxValue
                        ? JsValue.FromInt32((int)sourceLength)
                        : new((double)sourceLength)
                };
                var targetValue = realm.ConstructWithExplicitNewTarget(ctor, lenArg.AsSpan(), ctor, 0);
                var target = RequireTypedArrayCreateResult(realm, targetValue, sourceLength);

                for (uint index = 0; index < sourceLength; index++)
                {
                    var sourceValue = GetTypedArrayFromSourceValue(realm, items, index);
                    var mappedValue = MapTypedArrayFromValue(realm, mapFn, thisArg, sourceValue, index);
                    target.TrySetElement(index, mappedValue);
                }

                return targetValue;
            }

            throw new JsRuntimeException(JsErrorKind.TypeError,
                "TypedArray.from source is not iterable or array-like");
        }, "from", 1);

        var ofFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.of requires a constructor this value");

            var lenArg = new InlineJsValueArray1
            {
                Item0 = args.Length <= int.MaxValue ? JsValue.FromInt32(args.Length) : new(args.Length)
            };
            var targetValue = realm.ConstructWithExplicitNewTarget(ctor, lenArg.AsSpan(), ctor, 0);
            var target = RequireTypedArrayCreateResult(realm, targetValue, (uint)args.Length);
            for (uint index = 0; index < (uint)args.Length; index++)
                target.TrySetElement(index, args[(int)index]);
            return targetValue;
        }, "of", 0);

        var fromBase64Fn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].IsString)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Uint8Array.fromBase64 source must be a string");

            var source = args[0].AsString();
            var alphabet = "base64";
            var lastChunkHandling = Base64LastChunkHandling.Loose;
            if (args.Length > 1 && !args[1].IsUndefined)
            {
                if (!realm.TryToObject(args[1], out var options))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Uint8Array.fromBase64 options must be an object");
                if (options.TryGetProperty("alphabet", out var alphabetValue) && !alphabetValue.IsUndefined)
                {
                    if (!alphabetValue.IsString)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Uint8Array.fromBase64 alphabet must be a string");
                    alphabet = alphabetValue.AsString();
                }

                if (options.TryGetProperty("lastChunkHandling", out var handlingValue) && !handlingValue.IsUndefined)
                {
                    if (!handlingValue.IsString)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Uint8Array.fromBase64 lastChunkHandling must be a string");
                    lastChunkHandling = ParseBase64LastChunkHandling(handlingValue.AsString());
                }
            }

            var bytes = DecodeUint8ArrayBase64(source, alphabet, lastChunkHandling);
            var array = new JsTypedArrayObject(realm, (uint)bytes.Length, TypedArrayElementKind.Uint8,
                realm.Uint8ArrayPrototype);
            for (var i = 0; i < bytes.Length; i++)
                array.SetElement((uint)i, JsValue.FromInt32(bytes[i]));
            return array;
        }, "fromBase64", 1);

        var fromHexFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].IsString)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Uint8Array.fromHex source must be a string");

            var source = args[0].AsString();
            var bytes = DecodeUint8ArrayHex(source);
            var array = new JsTypedArrayObject(realm, (uint)bytes.Length, TypedArrayElementKind.Uint8,
                realm.Uint8ArrayPrototype);
            for (var i = 0; i < bytes.Length; i++)
                array.SetElement((uint)i, JsValue.FromInt32(bytes[i]));
            return array;
        }, "fromHex", 1);

        var setFromBase64Fn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var target = RequireTypedArrayObject(realm, thisValue);
            if (target.Kind != TypedArrayElementKind.Uint8)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Uint8Array.prototype.setFromBase64 called on incompatible receiver");
            if (target.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");
            if (args.Length == 0 || !args[0].IsString)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Uint8Array.fromBase64 source must be a string");

            var source = args[0].AsString();
            var alphabet = "base64";
            var lastChunkHandling = Base64LastChunkHandling.Loose;
            if (args.Length > 1 && !args[1].IsUndefined)
            {
                if (!realm.TryToObject(args[1], out var options))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Uint8Array.fromBase64 options must be an object");
                if (options.TryGetProperty("alphabet", out var alphabetValue) && !alphabetValue.IsUndefined)
                {
                    if (!alphabetValue.IsString)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Uint8Array.fromBase64 alphabet must be a string");
                    alphabet = alphabetValue.AsString();
                }

                if (options.TryGetProperty("lastChunkHandling", out var handlingValue) && !handlingValue.IsUndefined)
                {
                    if (!handlingValue.IsString)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Uint8Array.fromBase64 lastChunkHandling must be a string");
                    lastChunkHandling = ParseBase64LastChunkHandling(handlingValue.AsString());
                }
            }

            if (target.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");

            var (read, written) = SetUint8ArrayFromBase64(realm, target, source, alphabet, lastChunkHandling);
            return CreateBase64SetResultObject(realm, read, written);
        }, "setFromBase64", 1);

        var setFromHexFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var target = RequireTypedArrayObject(realm, thisValue);
            if (target.Kind != TypedArrayElementKind.Uint8)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Uint8Array.prototype.setFromHex called on incompatible receiver");
            if (target.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");
            if (args.Length == 0 || !args[0].IsString)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Uint8Array.fromHex source must be a string");

            var (read, written) = SetUint8ArrayFromHex(target, args[0].AsString());
            return CreateBase64SetResultObject(realm, read, written);
        }, "setFromHex", 1);

        var toBase64Fn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var target = RequireTypedArrayObject(realm, thisValue);
            if (target.Kind != TypedArrayElementKind.Uint8)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Uint8Array.prototype.toBase64 called on incompatible receiver");

            var alphabet = "base64";
            var omitPadding = false;
            if (args.Length > 0 && !args[0].IsUndefined)
            {
                if (!realm.TryToObject(args[0], out var options))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Uint8Array.prototype.toBase64 options must be an object");
                if (options.TryGetProperty("alphabet", out var alphabetValue) && !alphabetValue.IsUndefined)
                {
                    if (!alphabetValue.IsString)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Uint8Array.prototype.toBase64 alphabet must be a string");
                    alphabet = alphabetValue.AsString();
                }

                if (options.TryGetProperty("omitPadding", out var omitPaddingValue) && !omitPaddingValue.IsUndefined)
                    omitPadding = omitPaddingValue.ToBoolean();
            }

            if (target.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");

            return ToUint8ArrayBase64(realm, target, alphabet, omitPadding);
        }, "toBase64", 0);

        var toHexFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var target = RequireTypedArrayObject(realm, thisValue);
            if (target.Kind != TypedArrayElementKind.Uint8)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Uint8Array.prototype.toHex called on incompatible receiver");
            if (target.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");

            return ToUint8ArrayHex(target);
        }, "toHex", 0);

        var speciesGetter = new JsHostFunction(Realm, static (in info) =>
            {
                var thisValue = info.ThisValue;
                return thisValue;
            }, "get [Symbol.species]",
            0);

        var atFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var relativeIndex = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            var actualIndex = relativeIndex >= 0d ? relativeIndex : length + relativeIndex;
            if (actualIndex < 0d || actualIndex >= length)
                return JsValue.Undefined;
            return typedArray.TryGetElement((uint)actualIndex, out var value) ? value : JsValue.Undefined;
        }, "at", 1);

        var copyWithinFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            long snapshotLength = typedArray.Length;
            var relativeTarget = args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d;
            var relativeStart = args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d;
            var relativeEnd = args.Length > 2 && !args[2].IsUndefined
                ? realm.ToIntegerOrInfinity(args[2])
                : double.PositiveInfinity;

            if (typedArray.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");

            long currentLength = typedArray.Length;
            var snapLen = (uint)snapshotLength;
            long to = realm.NormalizeRelativeIndex(relativeTarget, snapLen, 0);
            long from = realm.NormalizeRelativeIndex(relativeStart, snapLen, 0);
            var final = args.Length > 2 ? realm.NormalizeRelativeIndex(relativeEnd, snapLen, snapLen) : snapshotLength;
            var count = Math.Min(Math.Max(final - from, 0L), Math.Max(snapshotLength - to, 0L));
            count = Math.Min(count, Math.Max(currentLength - to, 0L));
            count = Math.Min(count, Math.Max(currentLength - from, 0L));
            if (count == 0)
                return thisValue;

            typedArray.CopyWithin((uint)to, (uint)from, (uint)count);
            return thisValue;
        }, "copyWithin", 2);

        var valuesFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return new JsArrayIteratorObject(realm, ValidateTypedArray(realm, thisValue),
                JsArrayIteratorObject.IterationKind.Values);
        }, "values", 0);
        var keysFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return new JsArrayIteratorObject(realm, ValidateTypedArray(realm, thisValue),
                JsArrayIteratorObject.IterationKind.Keys);
        }, "keys", 0);
        var entriesFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return new JsArrayIteratorObject(realm, ValidateTypedArray(realm, thisValue),
                JsArrayIteratorObject.IterationKind.Entries);
        }, "entries", 0);

        var subarrayFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = RequireTypedArrayObject(realm, thisValue);
            var sourceLength = typedArray.Buffer.IsDetached && !typedArray.IsLengthTracking
                ? typedArray.StoredLength
                : typedArray.Length;
            var sourceByteOffset = typedArray.StoredByteOffset;
            var beginIndex = realm.NormalizeRelativeIndex(args.Length == 0 ? JsValue.Undefined : args[0],
                sourceLength, 0);
            var endIsUndefined = args.Length <= 1 || args[1].IsUndefined;
            var endIndex = !endIsUndefined
                ? realm.NormalizeRelativeIndex(args[1], sourceLength, sourceLength)
                : sourceLength;
            if (endIndex < beginIndex)
                endIndex = beginIndex;

            var newLength = endIndex - beginIndex;
            var byteOffset = checked(sourceByteOffset + beginIndex * (uint)typedArray.BytesPerElement);
            var ctor = GetTypedArraySpeciesConstructor(realm, typedArray);
            var byteOffsetValue = byteOffset <= int.MaxValue
                ? JsValue.FromInt32((int)byteOffset)
                : new((double)byteOffset);
            if (typedArray.IsLengthTracking && endIsUndefined)
            {
                var lengthTrackingCtorArgs = new InlineJsValueArray2
                {
                    Item0 = typedArray.Buffer,
                    Item1 = byteOffsetValue
                };
                var lengthTrackingCreated =
                    realm.ConstructWithExplicitNewTarget(ctor, lengthTrackingCtorArgs.AsSpan(), ctor, 0);
                RequireTypedArrayCreateResult(realm, lengthTrackingCreated, null);
                return lengthTrackingCreated;
            }

            var ctorArgs = new InlineJsValueArray3
            {
                Item0 = typedArray.Buffer,
                Item1 = byteOffsetValue,
                Item2 = newLength <= int.MaxValue ? JsValue.FromInt32((int)newLength) : new((double)newLength)
            };
            var createdValue = realm.ConstructWithExplicitNewTarget(ctor, ctorArgs.AsSpan(), ctor, 0);
            RequireTypedArrayCreateResult(realm, createdValue, newLength);
            return createdValue;
        }, "subarray", 2);

        var setFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var target = ValidateTypedArray(realm, thisValue);
            if (args.Length == 0 || !realm.TryToObject(args[0], out var source))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.set source must not be null or undefined");

            var targetOffset =
                args.Length > 1 ? realm.ToTypedArrayLengthOrOffset(args[1], "offset is out of bounds") : 0;
            if (target.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");
            var targetLength = target.Length;

            if (source is JsTypedArrayObject sourceTypedArray)
            {
                var values = CollectTypedArraySetSourceValues(realm, target.Kind, sourceTypedArray);
                if (targetOffset > targetLength || values.Count > targetLength - targetOffset)
                    throw new JsRuntimeException(JsErrorKind.RangeError, "offset is out of bounds");

                for (var i = 0; i < values.Count; i++)
                    target.SetElement(targetOffset + (uint)i, values[i]);
                return JsValue.Undefined;
            }

            var sourceLength = realm.GetArrayFromLength(source);
            if (targetOffset > targetLength || sourceLength > targetLength - targetOffset)
                throw new JsRuntimeException(JsErrorKind.RangeError, "offset is out of bounds");

            for (uint index = 0; index < sourceLength; index++)
            {
                var value = source.TryGetElement(index, out var elementValue) ? elementValue : JsValue.Undefined;
                target.SetElement(targetOffset + index, value);
            }

            return JsValue.Undefined;
        }, "set", 1);

        var everyFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.every callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (uint index = 0; index < length; index++)
            {
                var value = typedArray.TryGetElement(index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                if (!realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan()).ToBoolean())
                    return JsValue.False;
            }

            return JsValue.True;
        }, "every", 1);

        var someFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.some callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (uint index = 0; index < length; index++)
            {
                var value = typedArray.TryGetElement(index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                if (realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan()).ToBoolean())
                    return JsValue.True;
            }

            return JsValue.False;
        }, "some", 1);

        var sortFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            JsFunction? compareFn = null;
            if (args.Length > 0 && !args[0].IsUndefined)
            {
                if (!args[0].TryGetObject(out var callbackObj) || callbackObj is not JsFunction callback)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "TypedArray.prototype.sort comparefn must be a function");
                compareFn = callback;
            }

            TypedArraySort.Sort(realm, typedArray, compareFn);
            return thisValue;
        }, "sort", 1);

        var fillFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var initialLength = typedArray.Length;
            var fillValue = args.Length == 0 ? JsValue.Undefined : args[0];
            var normalizedValue = TypedArrayElementKindInfo.NormalizeValue(realm, typedArray.Kind, fillValue);
            var startIndex = args.Length > 1
                ? realm.NormalizeRelativeIndex(args[1], initialLength, 0)
                : 0;
            var endIndex = args.Length > 2 && !args[2].IsUndefined
                ? realm.NormalizeRelativeIndex(args[2], initialLength, initialLength)
                : initialLength;

            if (typedArray.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");

            for (var index = startIndex; index < endIndex; index++)
                typedArray.TrySetNormalizedElement(index, normalizedValue);

            return thisValue;
        }, "fill", 1);

        var filterFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.filter callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            var selectedValues = new List<JsValue>((int)Math.Min(length, 16));
            for (uint index = 0; index < length; index++)
            {
                var value = typedArray.TryGetElement(index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                if (realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan()).ToBoolean())
                    selectedValues.Add(value);
            }

            return CreateTypedArraySpeciesResult(realm, typedArray, selectedValues);
        }, "filter", 1);

        var findFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.find callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (uint index = 0; index < length; index++)
            {
                var value = typedArray.TryGetElement(index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                if (realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan()).ToBoolean())
                    return value;
            }

            return JsValue.Undefined;
        }, "find", 1);

        var findIndexFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.findIndex callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (uint index = 0; index < length; index++)
            {
                var value = typedArray.TryGetElement(index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                if (realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan()).ToBoolean())
                    return JsValue.FromInt32((int)index);
            }

            return JsValue.FromInt32(-1);
        }, "findIndex", 1);

        var findLastFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.findLast callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (var index = length - 1L; index >= 0; index--)
            {
                var value = typedArray.TryGetElement((uint)index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                if (realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan()).ToBoolean())
                    return value;
            }

            return JsValue.Undefined;
        }, "findLast", 1);

        var findLastIndexFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.findLastIndex callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (var index = length - 1L; index >= 0; index--)
            {
                var value = typedArray.TryGetElement((uint)index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                if (realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan()).ToBoolean())
                    return JsValue.FromInt32((int)index);
            }

            return JsValue.FromInt32(-1);
        }, "findLastIndex", 1);

        var forEachFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.forEach callbackfn must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (uint index = 0; index < length; index++)
            {
                var value = typedArray.TryGetElement(index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan());
            }

            return JsValue.Undefined;
        }, "forEach", 1);

        var mapFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray.prototype.map callbackfn must be a function");

            var result = CreateTypedArraySpeciesResult(realm, typedArray, length);
            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (uint index = 0; index < length; index++)
            {
                var value = typedArray.TryGetElement(index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = JsValue.FromInt32((int)index),
                    Item2 = thisValue
                };
                var mappedValue = realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan());
                result.TrySetElement(index, mappedValue);
            }

            return result;
        }, "map", 1);

        var reduceFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                return ReduceTypedArray(realm, thisValue, args, false, "TypedArray.prototype.reduce");
            }, "reduce", 1);

        var reduceRightFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                return ReduceTypedArray(realm, thisValue, args, true, "TypedArray.prototype.reduceRight");
            }, "reduceRight", 1);

        var reverseFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var typedArray = ValidateTypedArray(realm, thisValue);
            typedArray.Reverse();
            return thisValue;
        }, "reverse", 0);

        var sliceFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var beginIndex = args.Length > 0
                ? realm.NormalizeRelativeIndex(args[0], length, 0)
                : 0;
            var endIndex = args.Length > 1
                ? realm.NormalizeRelativeIndex(args[1], length, length)
                : length;

            if (typedArray.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");

            if (endIndex < beginIndex)
                endIndex = beginIndex;

            var count = endIndex - beginIndex;
            var result = CreateTypedArraySpeciesResult(realm, typedArray, count);
            if (count > 0 && typedArray.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");
            for (uint index = 0; index < count; index++)
                if (typedArray.TryGetElement(beginIndex + index, out var value))
                    result.TrySetElement(index, value);

            return result;
        }, "slice", 2);

        var includesFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var searchElement = args.Length == 0 ? JsValue.Undefined : args[0];
            if (length == 0)
                return JsValue.False;

            var startIndex = args.Length > 1 ? realm.NormalizeRelativeIndex(args[1], length, 0) : 0;
            for (var index = startIndex; index < length; index++)
            {
                typedArray.TryGetElement(index, out var value);
                if (JsValueSameValueZeroComparer.Instance.Equals(value, searchElement))
                    return JsValue.True;
            }

            return JsValue.False;
        }, "includes", 1);

        var indexOfFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var searchElement = args.Length == 0 ? JsValue.Undefined : args[0];
            if (length == 0)
                return JsValue.FromInt32(-1);

            var startIndex = args.Length > 1 ? realm.NormalizeRelativeIndex(args[1], length, 0) : 0;
            for (var index = startIndex; index < length; index++)
            {
                if (!realm.HasTypedArrayIndexWithoutGet(typedArray, index))
                    continue;
                typedArray.TryGetElement(index, out var value);
                if (JsRealm.StrictEquals(value, searchElement))
                    return index <= int.MaxValue ? JsValue.FromInt32((int)index) : new((double)index);
            }

            return JsValue.FromInt32(-1);
        }, "indexOf", 1);

        var lastIndexOfFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var searchElement = args.Length == 0 ? JsValue.Undefined : args[0];
            if (length == 0)
                return JsValue.FromInt32(-1);

            var startIndex = args.Length > 1 ? realm.NormalizeLastIndex(args[1], length) : length - 1L;
            for (var index = startIndex; index >= 0; index--)
            {
                if (!realm.HasTypedArrayIndexWithoutGet(typedArray, (uint)index))
                    continue;
                typedArray.TryGetElement((uint)index, out var value);
                if (JsRealm.StrictEquals(value, searchElement))
                    return index <= int.MaxValue ? JsValue.FromInt32((int)index) : new(index);
            }

            return JsValue.FromInt32(-1);
        }, "lastIndexOf", 1);

        var joinFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var separator = args.Length == 0 || args[0].IsUndefined ? "," : realm.ToJsStringSlowPath(args[0]);
            if (length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (uint index = 0; index < length; index++)
            {
                if (index > 0)
                    sb.Append(separator);
                typedArray.TryGetElement(index, out var value);
                if (!value.IsUndefined && !value.IsNull)
                    sb.Append(realm.ToJsStringSlowPath(value));
            }

            return sb.ToString();
        }, "join", 1);

        var toLocaleStringFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            if (length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (uint index = 0; index < length; index++)
            {
                if (index > 0)
                    sb.Append(',');
                typedArray.TryGetElement(index, out var value);
                if (value.IsUndefined || value.IsNull)
                    continue;
                sb.Append(InvokeElementToLocaleString(realm, value, info.Arguments));
            }

            return sb.ToString();
        }, "toLocaleString", 0);

        var toReversedFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var result = CreateTypedArraySameTypeResult(realm, typedArray, length);
            for (uint index = 0; index < length; index++)
            {
                typedArray.TryGetElement(length - index - 1, out var value);
                result.TrySetElement(index, value);
            }

            return result;
        }, "toReversed", 0);

        var toSortedFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            JsFunction? compareFn = null;
            if (args.Length > 0 && !args[0].IsUndefined)
            {
                if (!args[0].TryGetObject(out var callbackObj) || callbackObj is not JsFunction callback)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "TypedArray.prototype.toSorted comparefn must be a function");
                compareFn = callback;
            }

            var length = typedArray.Length;
            var result = CreateTypedArraySameTypeResult(realm, typedArray, length);
            for (uint index = 0; index < length; index++)
            {
                typedArray.TryGetElement(index, out var value);
                result.TrySetElement(index, value);
            }

            TypedArraySort.Sort(realm, result, compareFn);
            return result;
        }, "toSorted", 1);

        var withFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var typedArray = ValidateTypedArray(realm, thisValue);
            var length = typedArray.Length;
            var relativeIndex = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            var actualIndexDouble = relativeIndex >= 0d ? relativeIndex : length + relativeIndex;

            var replacement = args.Length > 1 ? args[1] : JsValue.Undefined;
            var normalizedReplacement =
                TypedArrayElementKindInfo.NormalizeValue(realm, typedArray.Kind, replacement);
            if (!IsValidTypedArrayIntegerIndex(typedArray, actualIndexDouble))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Index out of range");

            var result = CreateTypedArraySameTypeResult(realm, typedArray, length);
            var actualIndex = (uint)actualIndexDouble;
            for (uint index = 0; index < length; index++)
            {
                if (index == actualIndex)
                {
                    result.TrySetNormalizedElement(index, normalizedReplacement);
                    continue;
                }

                typedArray.TryGetElement(index, out var value);
                result.TrySetElement(index, value);
            }

            return result;
        }, "with", 2);

        var iteratorNextFn = new JsHostFunction(Realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not TypedArrayIteratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Array Iterator.prototype.next called on incompatible receiver");
            return iterator.Next();
        }, "next", 0);
        var iteratorSelfFn = new JsHostFunction(Realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not TypedArrayIteratorObject)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Array Iterator [Symbol.iterator] called on incompatible receiver");
            return thisValue;
        }, "[Symbol.iterator]", 0);

        Span<PropertyDefinition> iteratorProtoDefs =
        [
            PropertyDefinition.Mutable(atomNextLocal, iteratorNextFn),
            PropertyDefinition.Mutable(IdSymbolIterator, iteratorSelfFn),
            PropertyDefinition.Const(IdSymbolToStringTag, "Array Iterator", configurable: true)
        ];
        TypedArrayIteratorPrototype.DefineNewPropertiesNoCollision(Realm, iteratorProtoDefs);

        Span<PropertyDefinition> typedArrayProtoDefs =
        [
            PropertyDefinition.Mutable(atomAt, atFn),
            PropertyDefinition.Mutable(atomCopyWithin, copyWithinFn),
            PropertyDefinition.Mutable(IdConstructor, TypedArrayConstructor),
            PropertyDefinition.Mutable(atomEntries, entriesFn),
            PropertyDefinition.Mutable(atomEvery, everyFn),
            PropertyDefinition.Mutable(atomFill, fillFn),
            PropertyDefinition.Mutable(atomFilter, filterFn),
            PropertyDefinition.Mutable(atomFind, findFn),
            PropertyDefinition.Mutable(atomFindIndex, findIndexFn),
            PropertyDefinition.Mutable(atomFindLast, findLastFn),
            PropertyDefinition.Mutable(atomFindLastIndex, findLastIndexFn),
            PropertyDefinition.Mutable(atomForEach, forEachFn),
            PropertyDefinition.Mutable(atomIncludes, includesFn),
            PropertyDefinition.Mutable(atomIndexOf, indexOfFn),
            PropertyDefinition.Mutable(atomJoin, joinFn),
            PropertyDefinition.GetterData(IdLength, lengthGetter, configurable: true),
            PropertyDefinition.GetterData(atomByteLength, byteLengthGetter, configurable: true),
            PropertyDefinition.Mutable(atomKeys, keysFn),
            PropertyDefinition.Mutable(atomLastIndexOf, lastIndexOfFn),
            PropertyDefinition.Mutable(atomMap, mapFn),
            PropertyDefinition.Mutable(atomReduce, reduceFn),
            PropertyDefinition.Mutable(atomReduceRight, reduceRightFn),
            PropertyDefinition.Mutable(atomReverse, reverseFn),
            PropertyDefinition.Mutable(atomSet, setFn),
            PropertyDefinition.Mutable(atomSlice, sliceFn),
            PropertyDefinition.Mutable(atomSome, someFn),
            PropertyDefinition.Mutable(atomSort, sortFn),
            PropertyDefinition.Mutable(atomSubarray, subarrayFn),
            PropertyDefinition.Mutable(atomToLocaleString, toLocaleStringFn),
            PropertyDefinition.Mutable(atomToReversed, toReversedFn),
            PropertyDefinition.Mutable(atomToSorted, toSortedFn),
            PropertyDefinition.Mutable(IdToString, GetArrayPrototypeToStringFunction(Realm)),
            PropertyDefinition.Mutable(atomValues, valuesFn),
            PropertyDefinition.Mutable(atomWith, withFn),
            PropertyDefinition.Mutable(IdSymbolIterator, valuesFn),
            PropertyDefinition.GetterData(atomBuffer, bufferGetter, configurable: true),
            PropertyDefinition.GetterData(atomByteOffset, byteOffsetGetter, configurable: true),
            PropertyDefinition.GetterData(IdSymbolToStringTag, toStringTagGetter, configurable: true)
        ];
        TypedArrayPrototype.DefineNewPropertiesNoCollision(Realm, typedArrayProtoDefs);

        Span<PropertyDefinition> typedArrayCtorDefs =
        [
            PropertyDefinition.Mutable(atomFrom, fromFn),
            PropertyDefinition.Mutable(atomOf, ofFn),
            PropertyDefinition.GetterData(IdSymbolSpecies, speciesGetter, configurable: true)
        ];
        TypedArrayConstructor.InitializePrototypeProperty(TypedArrayPrototype);
        TypedArrayConstructor.DefineNewPropertiesNoCollision(Realm, typedArrayCtorDefs);

        for (var i = 0; i < 12; i++)
        {
            var kind = (TypedArrayElementKind)i;
            var ctor = Realm.Intrinsics.GetTypedArrayConstructor(kind);
            var proto = Realm.Intrinsics.GetTypedArrayPrototype(kind);
            var bpe = kind.GetBytesPerElement();
            ctor.InitializePrototypeProperty(proto);
            ctor.DefineDataPropertyAtom(Realm, atomBytesPerElement, JsValue.FromInt32(bpe), JsShapePropertyFlags.None);

            Span<PropertyDefinition> protoDefs =
            [
                PropertyDefinition.Mutable(IdConstructor, ctor),
                PropertyDefinition.Const(atomBytesPerElement, JsValue.FromInt32(bpe))
            ];
            proto.DefineNewPropertiesNoCollision(Realm, protoDefs);
        }

        Span<PropertyDefinition> uint8CtorDefs =
        [
            PropertyDefinition.Mutable(atomFromBase64, fromBase64Fn),
            PropertyDefinition.Mutable(atomFromHex, fromHexFn)
        ];
        Uint8ArrayConstructor.DefineNewPropertiesNoCollision(Realm, uint8CtorDefs);

        Span<PropertyDefinition> uint8ProtoDefs =
        [
            PropertyDefinition.Mutable(atomSetFromBase64, setFromBase64Fn),
            PropertyDefinition.Mutable(atomSetFromHex, setFromHexFn),
            PropertyDefinition.Mutable(atomToBase64, toBase64Fn),
            PropertyDefinition.Mutable(atomToHex, toHexFn)
        ];
        Uint8ArrayPrototype.DefineNewPropertiesNoCollision(Realm, uint8ProtoDefs);
    }

    private static uint ToTypedArrayLength(JsRealm realm, in JsValue value)
    {
        var number = realm.ToIntegerOrInfinity(value);
        if (double.IsNaN(number) || number == 0d)
            return 0;
        if (number < 0d || double.IsInfinity(number) || number > uint.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid typed array length: {number}");
        return (uint)number;
    }

    internal static JsTypedArrayObject ValidateTypedArray(JsRealm realm, in JsValue value)
    {
        var typedArray = RequireTypedArrayObject(realm, value);
        if (typedArray.IsOutOfBounds)
            throw new JsRuntimeException(JsErrorKind.TypeError, "TypedArray is out of bounds");
        return typedArray;
    }

    private static JsTypedArrayObject RequireTypedArrayObject(JsRealm realm, in JsValue value)
    {
        if (TryGetTypedArrayValue(value, out var typedArray))
            return typedArray;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "TypedArray.prototype accessor called on incompatible receiver");
    }

    private static bool TryGetTypedArrayValue(in JsValue value, out JsTypedArrayObject typedArray)
    {
        if (value.TryGetObject(out var obj) && obj is JsTypedArrayObject result)
        {
            typedArray = result;
            return true;
        }

        typedArray = null!;
        return false;
    }

    private static JsTypedArrayObject CreateTypedArrayView(JsRealm realm, TypedArrayElementKind kind,
        JsArrayBufferObject buffer, ReadOnlySpan<JsValue> args, in JsValue newTarget, JsHostFunction callee)
    {
        if (buffer.IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

        var byteOffset = args.Length > 1 && !args[1].IsUndefined ? realm.ToTypedArrayByteOffset(args[1]) : 0u;
        if (buffer.IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

        var bpe = (uint)kind.GetBytesPerElement();
        var initialBufferByteLength = buffer.ByteLength;
        if (byteOffset > initialBufferByteLength || byteOffset % bpe != 0)
            throw new JsRuntimeException(JsErrorKind.RangeError,
                $"Start offset {byteOffset} is outside the bounds of the buffer");

        uint length;
        var lengthTracking = false;
        if (args.Length > 2 && !args[2].IsUndefined)
        {
            length = ToTypedArrayLength(realm, args[2]);
        }
        else
        {
            var remainder = initialBufferByteLength - byteOffset;
            if (!buffer.IsResizable && !buffer.IsGrowable && remainder % bpe != 0)
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    "byte length of TypedArray should be a multiple of the element size");
            length = remainder / bpe;
            lengthTracking = buffer.IsResizable || buffer.IsGrowable;
        }

        if (buffer.IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

        var requiredBytes = checked(length * bpe);
        if (requiredBytes > initialBufferByteLength - byteOffset)
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid typed array length: {length}");

        var prototype = GetTypedArrayConstructionPrototype(realm, kind, newTarget, callee);
        if (buffer.IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

        var finalBufferByteLength = buffer.ByteLength;
        if (byteOffset > finalBufferByteLength || byteOffset % bpe != 0)
            throw new JsRuntimeException(JsErrorKind.RangeError,
                $"Start offset {byteOffset} is outside the bounds of the buffer");
        if (!lengthTracking && requiredBytes > finalBufferByteLength - byteOffset)
            throw new JsRuntimeException(JsErrorKind.RangeError, $"Invalid typed array length: {length}");

        return new(realm, buffer, byteOffset, length, kind, lengthTracking, prototype);
    }

    private static JsObject GetTypedArrayConstructionPrototype(JsRealm realm, TypedArrayElementKind kind,
        in JsValue newTarget, JsHostFunction callee)
    {
        return realm.Intrinsics.GetPrototypeFromConstructorOrIntrinsic(newTarget, callee,
            realm.Intrinsics.GetTypedArrayPrototype(kind));
    }


    private static JsValue ReduceTypedArray(JsRealm realm, in JsValue thisValue, ReadOnlySpan<JsValue> args,
        bool fromRight, string methodName)
    {
        var typedArray = ValidateTypedArray(realm, thisValue);
        var length = typedArray.Length;
        if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
            callbackObj is not JsFunction callbackFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} callbackfn must be a function");

        if (length == 0 && args.Length < 2)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Reduce of empty array with no initial value");

        long index;
        JsValue accumulator;
        if (args.Length > 1)
        {
            accumulator = args[1];
            index = fromRight ? length - 1L : 0L;
        }
        else if (fromRight)
        {
            typedArray.TryGetElement(length - 1, out accumulator);
            index = length - 2L;
        }
        else
        {
            typedArray.TryGetElement(0, out accumulator);
            index = 1L;
        }

        for (; fromRight ? index >= 0 : index < length; index += fromRight ? -1 : 1)
        {
            typedArray.TryGetElement((uint)index, out var value);
            var callbackArgs = new InlineJsValueArray4
            {
                Item0 = accumulator,
                Item1 = value,
                Item2 = JsValue.FromInt32((int)index),
                Item3 = thisValue
            };
            accumulator = realm.InvokeFunction(callbackFn, JsValue.Undefined, callbackArgs.AsSpan());
        }

        return accumulator;
    }

    private static List<JsValue> CollectTypedArraySetSourceValues(JsRealm realm, TypedArrayElementKind targetKind,
        JsTypedArrayObject source)
    {
        ValidateTypedArray(realm, source);
        if (source.Kind.IsBigIntFamily() != targetKind.IsBigIntFamily())
            throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot mix BigInt and other TypedArray families");

        var values = new List<JsValue>((int)source.Length);
        for (uint index = 0; index < source.Length; index++)
        {
            source.TryGetElement(index, out var value);
            values.Add(value);
        }

        return values;
    }

    private static JsTypedArrayObject CreateTypedArrayFromObject(JsRealm realm, TypedArrayElementKind kind,
        JsObject source, in JsValue newTarget, JsHostFunction callee, JsFunction? mapFn = null,
        in JsValue thisArg = default)
    {
        if (source is JsTypedArrayObject typedArray)
        {
            if (typedArray.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Source typed array is out of bounds");
            if (typedArray.Kind.IsBigIntFamily() != kind.IsBigIntFamily())
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot mix BigInt and other TypedArray families");

            var prototype = GetTypedArrayConstructionPrototype(realm, kind, newTarget, callee);
            var copy = new JsTypedArrayObject(realm, typedArray.Length, kind, prototype);
            for (uint index = 0; index < typedArray.Length; index++)
            {
                typedArray.TryGetElement(index, out var value);
                copy.SetElement(index, MapTypedArrayFromValue(realm, mapFn, thisArg, value, index));
            }

            return copy;
        }

        if (JsRealm.TryGetIteratorObjectForArrayFrom(realm, source, out var iterator))
        {
            var values = new List<JsValue>(4);
            long index = 0;
            while (true)
            {
                var nextValue = realm.StepIteratorForArrayFrom(iterator, out var done);
                if (done)
                    break;
                values.Add(MapTypedArrayFromValue(realm, mapFn, thisArg, nextValue, index));
                index++;
            }

            return CreateTypedArrayFromValues(realm, kind, newTarget, callee, values);
        }

        if (source is JsArray array)
            return CreateTypedArrayFromArrayLike(realm, kind, newTarget, callee, array.Length,
                static (_, sourceLocal, index) => ((JsArray)sourceLocal).TryGetElement(index, out var value)
                    ? value
                    : JsValue.Undefined,
                source, mapFn, thisArg);

        if (source is JsStringObject stringObject)
        {
            var values = new List<JsValue>(stringObject.Value.Length);
            foreach (var rune in stringObject.Value.EnumerateRunes())
                values.Add(MapTypedArrayFromValue(realm, mapFn, thisArg, rune.ToString(), values.Count));
            return CreateTypedArrayFromValues(realm, kind, newTarget, callee, values);
        }

        var length = realm.GetArrayFromLength(source);
        return CreateTypedArrayFromArrayLike(realm, kind, newTarget, callee, length,
            static (_, sourceLocal, index) =>
                sourceLocal.TryGetElement(index, out var value) ? value : JsValue.Undefined,
            source, mapFn, thisArg);
    }

    private static List<JsValue> CollectTypedArrayFromSourceValues(JsRealm realm, JsObject source,
        JsFunction? mapFn, in JsValue thisArg)
    {
        if (source is JsTypedArrayObject typedArray)
        {
            var values = new List<JsValue>((int)typedArray.Length);
            for (uint index = 0; index < typedArray.Length; index++)
            {
                typedArray.TryGetElement(index, out var value);
                values.Add(MapTypedArrayFromValue(realm, mapFn, thisArg, value, index));
            }

            return values;
        }

        if (source is JsArray array)
        {
            var values = new List<JsValue>((int)array.Length);
            for (uint index = 0; index < array.Length; index++)
                values.Add(MapTypedArrayFromValue(realm, mapFn, thisArg,
                    array.TryGetElement(index, out var value) ? value : JsValue.Undefined, index));
            return values;
        }

        if (source is JsStringObject stringObject)
        {
            var values = new List<JsValue>(stringObject.Value.Length);
            foreach (var rune in stringObject.Value.EnumerateRunes())
                values.Add(MapTypedArrayFromValue(realm, mapFn, thisArg, rune.ToString(), values.Count));
            return values;
        }

        if (JsRealm.TryGetIteratorObjectForArrayFrom(realm, source, out var iterator))
        {
            var values = new List<JsValue>(4);
            long index = 0;
            while (true)
            {
                var nextValue = realm.StepIteratorForArrayFrom(iterator, out var done);
                if (done)
                    break;
                values.Add(MapTypedArrayFromValue(realm, mapFn, thisArg, nextValue, index));
                index++;
            }

            return values;
        }

        var length = realm.GetArrayFromLength(source);
        var result = new List<JsValue>((int)length);
        for (uint index = 0; index < length; index++)
            result.Add(MapTypedArrayFromValue(realm, mapFn, thisArg,
                source.TryGetElement(index, out var value) ? value : JsValue.Undefined, index));
        return result;
    }

    private static JsTypedArrayObject CreateTypedArrayFromArrayLike(JsRealm realm, TypedArrayElementKind kind,
        in JsValue newTarget, JsHostFunction callee, uint length, Func<JsRealm, JsObject, uint, JsValue> getter,
        JsObject source, JsFunction? mapFn, in JsValue thisArg)
    {
        var prototype = GetTypedArrayConstructionPrototype(realm, kind, newTarget, callee);
        var typedArray = new JsTypedArrayObject(realm, length, kind, prototype);
        for (uint index = 0; index < length; index++)
            typedArray.SetElement(index,
                MapTypedArrayFromValue(realm, mapFn, thisArg, getter(realm, source, index), index));
        return typedArray;
    }

    private static JsTypedArrayObject CreateTypedArrayFromValues(JsRealm realm, TypedArrayElementKind kind,
        in JsValue newTarget, JsHostFunction callee, List<JsValue> values)
    {
        var prototype = GetTypedArrayConstructionPrototype(realm, kind, newTarget, callee);
        var typedArray = new JsTypedArrayObject(realm, (uint)values.Count, kind, prototype);
        for (uint index = 0; index < (uint)values.Count; index++)
            typedArray.SetElement(index, values[(int)index]);
        return typedArray;
    }

    private static JsTypedArrayObject CreateTypedArraySpeciesResult(JsRealm realm, JsTypedArrayObject exemplar,
        List<JsValue> values)
    {
        var created = CreateTypedArraySpeciesResult(realm, exemplar, (uint)values.Count);
        for (uint index = 0; index < (uint)values.Count; index++)
            created.SetElement(index, values[(int)index]);
        return created;
    }

    private static JsTypedArrayObject CreateTypedArraySpeciesResult(JsRealm realm, JsTypedArrayObject exemplar,
        uint length)
    {
        var ctor = GetTypedArraySpeciesConstructor(realm, exemplar);
        var lenArg = new InlineJsValueArray1
        {
            Item0 = length <= int.MaxValue ? JsValue.FromInt32((int)length) : new((double)length)
        };
        var createdValue = realm.ConstructWithExplicitNewTarget(ctor, lenArg.AsSpan(), ctor, 0);
        return RequireTypedArrayCreateResult(realm, createdValue, length);
    }

    private static JsTypedArrayObject CreateTypedArraySameTypeResult(JsRealm realm, JsTypedArrayObject exemplar,
        uint length)
    {
        JsFunction ctor = realm.Intrinsics.GetTypedArrayConstructor(exemplar.Kind);
        var lenArg = new InlineJsValueArray1
        {
            Item0 = length <= int.MaxValue ? JsValue.FromInt32((int)length) : new((double)length)
        };
        var createdValue = realm.ConstructWithExplicitNewTarget(ctor, lenArg.AsSpan(), ctor, 0);
        return RequireTypedArrayCreateResult(realm, createdValue, length);
    }

    private static bool IsValidTypedArrayIntegerIndex(JsTypedArrayObject typedArray, double index)
    {
        if (double.IsNaN(index) || double.IsInfinity(index))
            return false;
        if (index < 0d || index != Math.Truncate(index) || index > uint.MaxValue)
            return false;
        if (typedArray.IsOutOfBounds)
            return false;
        return index < typedArray.Length;
    }

    private static JsFunction GetTypedArraySpeciesConstructor(JsRealm realm, JsTypedArrayObject exemplar)
    {
        JsFunction defaultCtor = realm.Intrinsics.GetTypedArrayConstructor(exemplar.Kind);
        if (!exemplar.TryGetPropertyAtom(realm, IdConstructor, out var ctorValue, out _))
            return defaultCtor;
        if (ctorValue.IsUndefined)
            return defaultCtor;
        if (!ctorValue.TryGetObject(out var ctorObj))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "TypedArray constructor property must be an object");
        if (ctorObj.TryGetPropertyAtom(realm, IdSymbolSpecies, out var speciesValue, out _) &&
            !speciesValue.IsUndefined && !speciesValue.IsNull)
        {
            if (!speciesValue.TryGetObject(out var speciesObj) || speciesObj is not JsFunction speciesFn ||
                !speciesFn.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "TypedArray [Symbol.species] must be a constructor");
            return speciesFn;
        }

        return defaultCtor;
    }

    private static bool TryGetTypedArrayFromLength(JsRealm realm, JsObject source, out uint length)
    {
        if (source is JsTypedArrayObject typedArray)
        {
            length = typedArray.Length;
            return true;
        }

        if (source is JsArray array)
        {
            length = array.Length;
            return true;
        }

        if (source is JsStringObject stringObject)
        {
            uint count = 0;
            foreach (var _ in stringObject.Value.EnumerateRunes())
                count++;
            length = count;
            return true;
        }

        if (JsRealm.TryGetIteratorObjectForArrayFrom(realm, source, out _))
        {
            length = 0;
            return false;
        }

        length = realm.GetArrayFromLength(source);
        return true;
    }

    private static JsValue GetTypedArrayFromSourceValue(JsRealm realm, JsObject source, uint index)
    {
        if (source is JsTypedArrayObject typedArray)
            return typedArray.TryGetElement(index, out var value) ? value : JsValue.Undefined;

        if (source is JsArray array) return array.TryGetElement(index, out var value) ? value : JsValue.Undefined;

        if (source is JsStringObject stringObject)
        {
            uint current = 0;
            foreach (var rune in stringObject.Value.EnumerateRunes())
            {
                if (current == index)
                    return rune.ToString();
                current++;
            }

            return JsValue.Undefined;
        }

        return source.TryGetElement(index, out var result) ? result : JsValue.Undefined;
    }

    private static JsTypedArrayObject RequireTypedArrayCreateResult(JsRealm realm, in JsValue value,
        uint? requiredLength)
    {
        if (!TryGetTypedArrayValue(value, out var typedArray))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "TypedArray constructor must return a TypedArray instance");
        if (requiredLength.HasValue && typedArray.Length < requiredLength.Value)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Constructed TypedArray is too small");
        return typedArray;
    }

    private static JsValue MapTypedArrayFromValue(JsRealm realm, JsFunction? mapFn, in JsValue thisArg,
        in JsValue value, long index)
    {
        if (mapFn is null)
            return value;

        var callbackArgs = new InlineJsValueArray2
        {
            Item0 = value,
            Item1 = index <= int.MaxValue ? JsValue.FromInt32((int)index) : new(index)
        };
        return realm.InvokeFunction(mapFn, thisArg, callbackArgs.AsSpan());
    }

    private static Base64LastChunkHandling ParseBase64LastChunkHandling(string value)
    {
        return value switch
        {
            "loose" => Base64LastChunkHandling.Loose,
            "strict" => Base64LastChunkHandling.Strict,
            "stop-before-partial" => Base64LastChunkHandling.StopBeforePartial,
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                "Uint8Array.fromBase64 lastChunkHandling must be 'loose', 'strict', or 'stop-before-partial'")
        };
    }

    private static byte[] DecodeUint8ArrayBase64(string source, string alphabet,
        Base64LastChunkHandling lastChunkHandling)
    {
        var base64Url = alphabet switch
        {
            "base64" => false,
            "base64url" => true,
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                "Uint8Array.fromBase64 alphabet must be 'base64' or 'base64url'")
        };

        source = StripAsciiBase64Whitespace(source);
        var bytes = new List<byte>(source.Length);
        var position = 0;
        while (position < source.Length)
        {
            var remaining = source.Length - position;
            if (remaining >= 4)
            {
                var a = source[position];
                var b = source[position + 1];
                var c = source[position + 2];
                var d = source[position + 3];

                if (c == '=' || d == '=')
                {
                    if (position + 4 != source.Length)
                        throw InvalidBase64Syntax();

                    DecodeFinalPaddedBase64Chunk(bytes, a, b, c, d, base64Url, lastChunkHandling);
                    position += 4;
                    continue;
                }

                var sa = DecodeBase64Sextet(a, base64Url);
                var sb = DecodeBase64Sextet(b, base64Url);
                var sc = DecodeBase64Sextet(c, base64Url);
                var sd = DecodeBase64Sextet(d, base64Url);
                if ((sa | sb | sc | sd) < 0)
                    throw InvalidBase64Syntax();

                bytes.Add((byte)((sa << 2) | (sb >> 4)));
                bytes.Add((byte)(((sb & 0x0F) << 4) | (sc >> 2)));
                bytes.Add((byte)(((sc & 0x03) << 6) | sd));
                position += 4;
                continue;
            }

            DecodeFinalUnpaddedBase64Chunk(bytes, source.AsSpan(position), base64Url, lastChunkHandling);
            position = source.Length;
        }

        return bytes.ToArray();
    }

    private static string StripAsciiBase64Whitespace(string source)
    {
        var firstWhitespace = -1;
        for (var i = 0; i < source.Length; i++)
            if (IsAsciiBase64Whitespace(source[i]))
            {
                firstWhitespace = i;
                break;
            }

        if (firstWhitespace < 0)
            return source;

        var sb = new StringBuilder(source.Length);
        if (firstWhitespace > 0)
            sb.Append(source, 0, firstWhitespace);

        for (var i = firstWhitespace; i < source.Length; i++)
        {
            var c = source[i];
            if (!IsAsciiBase64Whitespace(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsAsciiBase64Whitespace(char c)
    {
        return c is ' ' or '\t' or '\n' or '\f' or '\r';
    }

    private static void DecodeFinalPaddedBase64Chunk(List<byte> bytes, char a, char b, char c, char d, bool base64Url,
        Base64LastChunkHandling lastChunkHandling)
    {
        var sa = DecodeBase64Sextet(a, base64Url);
        var sb = DecodeBase64Sextet(b, base64Url);
        if ((sa | sb) < 0)
            throw InvalidBase64Syntax();

        if (c == '=')
        {
            if (d != '=')
                throw InvalidBase64Syntax();

            var paddingBits = sb & 0x0F;
            if (lastChunkHandling == Base64LastChunkHandling.Strict && paddingBits != 0)
                throw InvalidBase64Syntax();

            bytes.Add((byte)((sa << 2) | (sb >> 4)));
            return;
        }

        if (d == '=')
        {
            var sc = DecodeBase64Sextet(c, base64Url);
            if (sc < 0)
                throw InvalidBase64Syntax();

            if (lastChunkHandling == Base64LastChunkHandling.Strict && (sc & 0x03) != 0)
                throw InvalidBase64Syntax();

            bytes.Add((byte)((sa << 2) | (sb >> 4)));
            bytes.Add((byte)(((sb & 0x0F) << 4) | (sc >> 2)));
            return;
        }

        var sc2 = DecodeBase64Sextet(c, base64Url);
        var sd = DecodeBase64Sextet(d, base64Url);
        if ((sc2 | sd) < 0)
            throw InvalidBase64Syntax();

        bytes.Add((byte)((sa << 2) | (sb >> 4)));
        bytes.Add((byte)(((sb & 0x0F) << 4) | (sc2 >> 2)));
        bytes.Add((byte)(((sc2 & 0x03) << 6) | sd));
    }

    private static void DecodeFinalUnpaddedBase64Chunk(List<byte> bytes, ReadOnlySpan<char> chunk, bool base64Url,
        Base64LastChunkHandling lastChunkHandling)
    {
        if (chunk.Length == 0)
            return;

        for (var i = 0; i < chunk.Length; i++)
            if (chunk[i] == '=')
            {
                if (lastChunkHandling == Base64LastChunkHandling.StopBeforePartial &&
                    chunk.Length == 3 &&
                    chunk[2] == '=' &&
                    DecodeBase64Sextet(chunk[0], base64Url) >= 0 &&
                    DecodeBase64Sextet(chunk[1], base64Url) >= 0)
                    return;

                throw InvalidBase64Syntax();
            }

        switch (chunk.Length)
        {
            case 1:
                if (lastChunkHandling == Base64LastChunkHandling.StopBeforePartial)
                    return;
                throw InvalidBase64Syntax();
            case 2:
            {
                if (lastChunkHandling == Base64LastChunkHandling.StopBeforePartial)
                    return;
                if (lastChunkHandling == Base64LastChunkHandling.Strict)
                    throw InvalidBase64Syntax();

                var sa = DecodeBase64Sextet(chunk[0], base64Url);
                var sb = DecodeBase64Sextet(chunk[1], base64Url);
                if ((sa | sb) < 0)
                    throw InvalidBase64Syntax();
                bytes.Add((byte)((sa << 2) | (sb >> 4)));
                return;
            }
            case 3:
            {
                if (lastChunkHandling == Base64LastChunkHandling.StopBeforePartial)
                    return;
                if (lastChunkHandling == Base64LastChunkHandling.Strict)
                    throw InvalidBase64Syntax();

                var sa = DecodeBase64Sextet(chunk[0], base64Url);
                var sb = DecodeBase64Sextet(chunk[1], base64Url);
                var sc = DecodeBase64Sextet(chunk[2], base64Url);
                if ((sa | sb | sc) < 0)
                    throw InvalidBase64Syntax();
                bytes.Add((byte)((sa << 2) | (sb >> 4)));
                bytes.Add((byte)(((sb & 0x0F) << 4) | (sc >> 2)));
                return;
            }
            default:
                throw InvalidBase64Syntax();
        }
    }

    private static int DecodeBase64Sextet(char c, bool base64Url)
    {
        return c switch
        {
            >= 'A' and <= 'Z' => c - 'A',
            >= 'a' and <= 'z' => 26 + (c - 'a'),
            >= '0' and <= '9' => 52 + (c - '0'),
            '+' when !base64Url => 62,
            '/' when !base64Url => 63,
            '-' when base64Url => 62,
            '_' when base64Url => 63,
            _ => -1
        };
    }

    private static JsRuntimeException InvalidBase64Syntax()
    {
        return new(JsErrorKind.SyntaxError, "Invalid Base64 string");
    }

    private static byte[] DecodeUint8ArrayHex(string source)
    {
        if ((source.Length & 1) != 0)
            throw new JsRuntimeException(JsErrorKind.SyntaxError, "Invalid hex string");

        var bytes = new byte[source.Length / 2];
        for (var i = 0; i < source.Length; i += 2)
        {
            var high = HexNibble(source[i]);
            var low = HexNibble(source[i + 1]);
            if (high < 0 || low < 0)
                throw new JsRuntimeException(JsErrorKind.SyntaxError, "Invalid hex string");
            bytes[i / 2] = (byte)((high << 4) | low);
        }

        return bytes;
    }

    private static int HexNibble(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => 10 + (c - 'a'),
            >= 'A' and <= 'F' => 10 + (c - 'A'),
            _ => -1
        };
    }

    private static JsValue GetArrayPrototypeToStringFunction(JsRealm realm)
    {
        if (realm.ArrayPrototype.TryGetPropertyAtom(realm, IdToString, out var toStringValue, out _))
            return toStringValue;
        throw new InvalidOperationException("Array.prototype.toString must be installed before TypedArray builtins.");
    }

    private enum Base64LastChunkHandling : byte
    {
        Loose,
        Strict,
        StopBeforePartial
    }
}
