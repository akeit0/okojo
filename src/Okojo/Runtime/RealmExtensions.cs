using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Internals;

namespace Okojo.Runtime;

internal static class RealmExtensions
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static double StringToNumberSlowPath(string s)
    {
        var span = s.AsSpan().Trim();
        if (span.Length == 0) return 0d;
        if (span.SequenceEqual("Infinity".AsSpan()) || span.SequenceEqual("+Infinity".AsSpan()))
            return double.PositiveInfinity;
        if (span.SequenceEqual("-Infinity".AsSpan()))
            return double.NegativeInfinity;
        if (span.Equals("Infinity".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            span.Equals("+Infinity".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            span.Equals("-Infinity".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return double.NaN;
        if (TryParseHexLiteral(span, out var hex))
            return hex;
        if (TryParseBinaryLiteral(span, out var binary))
            return binary;
        if (TryParseOctalLiteral(span, out var octal))
            return octal;
        if (double.TryParse(span, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var n))
            return n;
        return double.NaN;

        static bool TryParseHexLiteral(ReadOnlySpan<char> text, out double number)
        {
            number = 0;
            if (text.Length < 3)
                return false;
            if (text[0] == '+' || text[0] == '-')
                return false;
            if (text[0] != '0' || (text[1] != 'x' && text[1] != 'X'))
                return false;

            double acc = 0;
            for (var i = 2; i < text.Length; i++)
            {
                var digit = text[i] switch
                {
                    >= '0' and <= '9' => text[i] - '0',
                    >= 'a' and <= 'f' => text[i] - 'a' + 10,
                    >= 'A' and <= 'F' => text[i] - 'A' + 10,
                    _ => -1
                };
                if (digit < 0)
                    return false;
                acc = acc * 16d + digit;
            }

            number = acc;
            return true;
        }

        static bool TryParseBinaryLiteral(ReadOnlySpan<char> text, out double number)
        {
            return TryParseRadixLiteral(text, 'b', 'B', 2, out number);
        }

        static bool TryParseOctalLiteral(ReadOnlySpan<char> text, out double number)
        {
            return TryParseRadixLiteral(text, 'o', 'O', 8, out number);
        }

        static bool TryParseRadixLiteral(ReadOnlySpan<char> text, char lowerPrefix, char upperPrefix, int radix,
            out double number)
        {
            number = 0;
            if (text.Length < 3)
                return false;
            if (text[0] == '+' || text[0] == '-')
                return false;
            if (text[0] != '0' || (text[1] != lowerPrefix && text[1] != upperPrefix))
                return false;

            double acc = 0;
            for (var i = 2; i < text.Length; i++)
            {
                var digit = text[i] switch
                {
                    >= '0' and <= '9' => text[i] - '0',
                    _ => -1
                };
                if (digit < 0 || digit >= radix)
                    return false;
                acc = acc * radix + digit;
            }

            number = acc;
            return true;
        }
    }


    private static double BigIntToNumber(JsBigInt value)
    {
        return double.Parse(value.Value.ToString(), CultureInfo.InvariantCulture);
    }

    extension(JsRealm realm)
    {
        internal JsValue InvokeObjectConstructorMethod(string methodName, ReadOnlySpan<JsValue> args)
        {
            var atom = realm.Atoms.InternNoCheck(methodName);
            var objectConstructor = realm.Intrinsics.ObjectConstructor;
            if (!objectConstructor.TryGetPropertyAtom(realm, atom, out var methodValue, out _) ||
                !methodValue.TryGetObject(out var methodObj) || methodObj is not JsFunction methodFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, $"Object.{methodName} is not callable");
            return realm.InvokeFunction(methodFn, objectConstructor, args);
        }

        internal JsRegExpObject CreateRegExpInstance(string pattern, string flags)
        {
            try
            {
                var parsedFlags = JsRegExpRuntime.ParseFlags(flags);
                var canonicalFlags = JsRegExpRuntime.CanonicalizeFlags(parsedFlags);
                return new(realm, pattern, canonicalFlags,
                    parsedFlags.Global, parsedFlags.IgnoreCase, parsedFlags.Multiline,
                    parsedFlags.Sticky, parsedFlags.Unicode, parsedFlags.DotAll);
            }
            catch (ArgumentException ex)
            {
                throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "REGEXP_INVALID_PATTERN");
            }
        }

        internal JsValue CreateRegExpObject(string pattern, string flags)
        {
            return realm.CreateRegExpInstance(pattern, flags);
        }

        internal JsValue CreateRegExpObject(string pattern, string flags, scoped in CallInfo info)
        {
            var rx = realm.CreateRegExpInstance(pattern, flags);
            if (info.IsConstruct)
            {
                var callee = (JsHostFunction)info.Function;
                rx.Prototype =
                    realm.Intrinsics.GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                        realm.Intrinsics.RegExpPrototype);
            }

            return rx;
        }

        internal bool TryResolvePropertyKey(in JsValue key, out uint index, out int atom)
        {
            index = 0;
            atom = -1;

            if (key.IsSymbol)
            {
                atom = key.AsSymbol().Atom;
                return false;
            }

            if (key.IsString)
            {
                var text = key.AsString();
                if (TryGetArrayIndexFromCanonicalString(text, out index))
                    return true;
                atom = realm.Atoms.InternNoCheck(text);
                return false;
            }

            if (key.IsInt32)
            {
                var i = key.Int32Value;
                if (i >= 0)
                {
                    index = (uint)i;
                    return true;
                }

                atom = realm.Atoms.InternNoCheck(i.ToString());
                return false;
            }

            if (key.IsFloat64)
            {
                var n = key.FastFloat64Value;
                if (n >= 0 && n < uint.MaxValue && n == Math.Truncate(n))
                {
                    index = (uint)n;
                    return true;
                }

                var text = realm.ToJsStringSlowPath(key);
                atom = realm.Atoms.InternNoCheck(text);
                return false;
            }

            var fallback = realm.ToJsStringSlowPath(key);
            if (TryGetArrayIndexFromCanonicalString(fallback, out index))
                return true;
            atom = realm.Atoms.InternNoCheck(fallback);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryToObject(in JsValue value, out JsObject obj)
        {
            if (value.TryGetObject(out var objectValue))
            {
                obj = objectValue;
                return true;
            }

            if (value.IsNull || value.IsUndefined)
            {
                obj = null!;
                return false;
            }

            obj = realm.BoxPrimitive(value);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal JsObject BoxPrimitive(in JsValue value)
        {
            if (value.IsString)
                return new JsStringObject(realm, value.AsJsString());
            if (value.IsBool)
                return new JsBooleanObject(realm, value.IsTrue);
            if (value.IsNumber)
                return new JsNumberObject(realm, value.NumberValue);
            if (value.IsBigInt)
                return new JsBigIntObject(realm, value.AsBigInt());
            if (value.IsSymbol)
                return new JsSymbolObject(realm, value.AsSymbol());
            return new JsPlainObject(realm);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal JsObject BoxPrimitiveForPropertyAccess(in JsValue value)
        {
            var boxed = realm.BoxPrimitive(value);
            boxed.SetExtensibleFlag(false);
            return boxed;
        }

        internal double ToNumberConstructorValue(in JsValue value)
        {
            if (value.IsBigInt)
                return BigIntToNumber(value.AsBigInt());

            var primitive = value.IsObject ? realm.ToPrimitiveSlowPath(value, false) : value;
            if (primitive.IsBigInt)
                return BigIntToNumber(primitive.AsBigInt());
            return realm.ToNumberSlowPath(primitive);
        }

        internal long GetArrayLikeLengthLong(JsObject obj)
        {
            if (obj is JsTypedArrayObject typedArray)
                return typedArray.Length;
            if (obj is JsArray array)
                return array.Length;

            if (!obj.TryGetPropertyAtom(realm, IdLength, out var lengthValue, out _))
                return 0;

            var lengthNum = realm.ToNumber(lengthValue);
            if (double.IsNaN(lengthNum) || lengthNum <= 0)
                return 0;

            const double maxSafeInteger = 9007199254740991d;
            return (long)Math.Min(maxSafeInteger, Math.Floor(lengthNum));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double ToNumber(in JsValue value)
        {
            if (value.IsFloat64) return value.FastFloat64Value;
            if (value.IsInt32) return value.Int32Value;
            return realm.ToNumberSlowPath(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal double ToNumberSlowPath(in JsValue value)
        {
            if (value.IsNumber) return value.NumberValue;
            if (value.IsBool) return value.IsTrue ? 1d : 0d;
            if (value.IsNull) return 0d;
            if (value.IsUndefined) return double.NaN;
            if (value.IsString) return StringToNumberSlowPath(value.AsString());
            if (value.IsSymbol)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert a Symbol value to a number");
            if (value.IsBigInt)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert a BigInt value to a number");
            if (value.IsObject) return realm.ToNumberSlowPath(realm.ToPrimitiveSlowPath(value, false));
            return double.NaN;
        }

        public long ToLength(in JsValue value)
        {
            var number = realm.ToNumberSlowPath(value);
            if (double.IsNaN(number) || number <= 0d)
                return 0;
            const double maxSafeInteger = 9007199254740991d;
            if (double.IsPositiveInfinity(number))
                return (long)maxSafeInteger;
            return (long)Math.Min(maxSafeInteger, Math.Floor(number));
        }

        internal ulong ToIndexForBigInt(in JsValue value)
        {
            var primitive = realm.ToPrimitiveSlowPath(value, false);
            if (primitive.IsBigInt)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert a BigInt value to an index");

            var number = realm.ToNumberSlowPath(primitive);
            if (double.IsNaN(number))
                return 0;

            var integer = Math.Truncate(number);
            if (integer <= 0d)
            {
                if (integer < 0d)
                    throw new JsRuntimeException(JsErrorKind.RangeError, "Index out of range");
                return 0;
            }

            const double maxSafeInteger = 9007199254740991d;
            if (double.IsPositiveInfinity(integer) || integer > maxSafeInteger)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Index out of range");

            return (ulong)integer;
        }

        internal uint ToTypedArrayByteOffset(in JsValue value)
        {
            var number = realm.ToIntegerOrInfinity(value);
            if (double.IsNaN(number) || number == 0d)
                return 0;
            if (number < 0d || double.IsInfinity(number) || number > uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError,
                    $"Start offset {number} is outside the bounds of the buffer");
            return (uint)number;
        }

        internal uint ToTypedArrayLengthOrOffset(in JsValue value, string rangeErrorMessage)
        {
            var number = realm.ToIntegerOrInfinity(value);
            if (double.IsNaN(number) || number == 0d)
                return 0;
            if (number < 0d || double.IsInfinity(number) || number > uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError, rangeErrorMessage);
            return (uint)number;
        }

        internal void PopulateArrayFromArrayLike(
            JsObject result,
            uint length,
            JsObject source,
            JsFunction? mapFn,
            in JsValue thisArg)
        {
            for (uint index = 0; index < length; index++)
            {
                var value = Intrinsics.TryGetArrayLikeIndex(realm, source, index, out var elementValue)
                    ? elementValue
                    : JsValue.Undefined;
                var mappedValue = JsRealm.MapArrayFromValue(realm, mapFn, thisArg, value, index);
                Intrinsics.CreateDataPropertyOrThrowForArrayLike(realm, result, index, mappedValue, "Array.from");
            }

            Intrinsics.SetArrayLikeLengthOrThrow(realm, result, length, "Array.from");
        }

        internal JsPromiseObject CreatePromiseObject()
        {
            return realm.Intrinsics.CreatePromiseObject();
        }

        internal JsPromiseObject CreatePromiseObject(JsObject prototype)
        {
            return realm.Intrinsics.CreatePromiseObject(prototype);
        }

        internal JsPromiseObject.PromiseCapability CreatePromiseCapability(JsFunction ctor)
        {
            return realm.Intrinsics.CreatePromiseCapability(ctor);
        }

        internal void ResolvePromiseCapability(JsPromiseObject.PromiseCapability capability, in JsValue value)
        {
            realm.Intrinsics.ResolvePromiseCapability(capability, value);
        }

        internal void RejectPromiseCapability(JsPromiseObject.PromiseCapability capability, in JsValue reason)
        {
            realm.Intrinsics.RejectPromiseCapability(capability, reason);
        }

        internal void ResolvePromise(JsPromiseObject promise, in JsValue value)
        {
            realm.Intrinsics.ResolvePromise(promise, value);
        }

        internal void RejectPromise(JsPromiseObject promise, in JsValue reason)
        {
            realm.Intrinsics.RejectPromise(promise, reason);
        }

        internal void ResolvePromiseWithAssimilation(JsPromiseObject target, in JsValue resolution)
        {
            realm.Intrinsics.ResolvePromiseWithAssimilation(target, resolution);
        }

        internal void PromiseThenNoCapability(JsPromiseObject promise, in JsValue onFulfilled, in JsValue onRejected)
        {
            realm.Intrinsics.PromiseThenNoCapability(promise, onFulfilled, onRejected);
        }

        internal JsValue PromiseResolveValue(in JsValue value)
        {
            return realm.Intrinsics.PromiseResolveValue(value);
        }

        internal JsValue PromiseResolveByConstructor(JsFunction ctor, in JsValue value)
        {
            return realm.Intrinsics.PromiseResolveByConstructor(ctor, value);
        }

        internal JsValue PromiseRejectByConstructor(JsFunction ctor, in JsValue value)
        {
            return realm.Intrinsics.PromiseRejectByConstructor(ctor, value);
        }

        internal JsValue CreateAsyncFromSyncIteratorResultPromise(
            in JsValue value,
            bool done,
            JsObject? iteratorToClose = null,
            bool closeOnRejection = false)
        {
            return realm.Intrinsics.CreateAsyncFromSyncIteratorResultPromise(value, done, iteratorToClose,
                closeOnRejection);
        }

        internal JsValue CreateAsyncFromSyncIteratorResultPromise(in JsValue value, bool done)
        {
            return realm.Intrinsics.CreateAsyncFromSyncIteratorResultPromise(value, done);
        }

        internal JsValue CreateAsyncFromSyncIteratorResultPromise(
            JsObject resultObject,
            JsObject? iteratorToClose = null,
            bool closeOnRejection = false)
        {
            return realm.Intrinsics.CreateAsyncFromSyncIteratorResultPromise(resultObject, iteratorToClose,
                closeOnRejection);
        }

        internal JsPlainObject CreateIteratorResultObject(JsValue value, bool done)
        {
            return JsIteratorHelperOperations.CreateIteratorResultObject(realm, value, done);
        }

        internal void StartOrResumeAsyncDriver(JsGeneratorObject generator, GeneratorResumeMode mode, JsValue value)
        {
            realm.StartOrResumeAsyncDriver(generator, mode, value);
        }

        internal JsValue ResumeGeneratorObject(JsGeneratorObject generator, GeneratorResumeMode mode, JsValue input)
        {
            return realm.ResumeGeneratorObject(generator, mode, input);
        }

        internal void ContinueActiveAsyncGeneratorRequest(JsGeneratorObject generator, GeneratorResumeMode mode,
            JsValue value)
        {
            realm.ContinueActiveAsyncGeneratorRequest(generator, mode, value);
        }

        internal void FinishAsyncGeneratorRequest(JsGeneratorObject generator)
        {
            realm.FinishAsyncGeneratorRequest(generator);
        }

        internal void ContinueAsyncGeneratorYieldDelegateAfterAwait(
            JsGeneratorObject generator,
            GeneratorResumeMode originalMode,
            JsPromiseObject.PromiseState settledState,
            JsValue settledResult)
        {
            realm.ContinueAsyncGeneratorYieldDelegateAfterAwait(generator, originalMode, settledState, settledResult);
        }

        internal void ClearDelegateIteratorRegisterInContinuationSnapshot(JsGeneratorObject generator)
        {
            realm.ClearDelegateIteratorRegisterInContinuationSnapshot(generator);
        }

        internal JsValue ExecuteGeneratorFromStart(JsGeneratorObject generator)
        {
            return realm.ExecuteGeneratorFromStart(generator);
        }

        internal JsValue ExecuteGeneratorFromContinuation(JsGeneratorObject generator)
        {
            return realm.ExecuteGeneratorFromContinuation(generator);
        }

        internal void EnqueuePromiseReactionJob(JsPromiseObject sourcePromise, JsPromiseObject.Reaction reaction)
        {
            realm.Intrinsics.EnqueuePromiseReactionJob(sourcePromise, reaction);
        }

        internal JsValue PromiseThen(JsPromiseObject promise, in JsValue onFulfilled, in JsValue onRejected)
        {
            return realm.Intrinsics.PromiseThen(promise, onFulfilled, onRejected);
        }

        internal JsValue ParseJsonModuleSource(string source)
        {
            return realm.Intrinsics.ParseJsonModuleSource(source);
        }

        internal JsValue GetPromiseAbruptReason(JsRuntimeException ex)
        {
            for (Exception? current = ex; current is not null; current = current.InnerException)
                if (current is JsRuntimeException runtime && runtime.ThrownValue is JsValue thrownValue)
                    return thrownValue;

            return realm.CreateErrorObjectFromException(ex);
        }

        internal void ExecuteUnhandledRejectionCheckJob(JsPromiseObject promise)
        {
            realm.Intrinsics.ExecuteUnhandledRejectionCheckJob(promise);
        }

        internal void ExecutePromiseReactionJob(JsPromiseObject sourcePromise, JsPromiseObject.Reaction reaction)
        {
            realm.Intrinsics.ExecutePromiseReactionJob(sourcePromise, reaction);
        }

        internal void ExecuteFireAndForgetHandlerReaction(
            JsPromiseObject.Reaction reaction,
            JsPromiseObject.PromiseState settledState,
            in JsValue settledResult)
        {
            realm.Intrinsics.ExecuteFireAndForgetHandlerReaction(reaction, settledState, settledResult);
        }

        internal bool TryGetCanonicalNumericIndexString(in JsValue key, out double numericIndex)
        {
            return Intrinsics.TryGetCanonicalNumericIndexString(realm, key, out numericIndex);
        }

        internal bool TryHasTypedArrayIntegerIndexedElement(
            JsTypedArrayObject typedArray,
            in JsValue key,
            out bool hasProperty,
            out bool handled)
        {
            return Intrinsics.TryHasTypedArrayIntegerIndexedElement(realm, typedArray, key, out hasProperty,
                out handled);
        }

        internal bool SetCanonicalNumericIndexOnTypedArrayForSet(
            JsTypedArrayObject typedArray,
            double numericIndex,
            in JsValue value)
        {
            return Intrinsics.SetCanonicalNumericIndexOnTypedArrayForSet(typedArray, numericIndex, value);
        }

        internal bool IsValidTypedArrayCanonicalNumericIndex(JsTypedArrayObject typedArray, double numericIndex)
        {
            return Intrinsics.IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex);
        }

        internal bool OrdinarySetOwnWritableDataIndex(JsObject receiver, uint index, in JsValue value)
        {
            return Intrinsics.OrdinarySetOwnWritableDataIndex(realm, receiver, index, value);
        }

        internal CultureInfo ResolveRequestedLocaleCulture(ReadOnlySpan<JsValue> args)
        {
            return Intrinsics.ResolveRequestedLocaleCulture(realm, args);
        }

        internal static bool TryTimeClipToEpochMillisecondsForIntl(double timeValue, out long epochMilliseconds)
        {
            return Intrinsics.TryTimeClipToEpochMillisecondsForIntl(timeValue, out epochMilliseconds);
        }

        internal static Intrinsics.OkojoEcmaDateTimeParts GetEcmaDateTimePartsForIntl(long epochMilliseconds, bool utc)
        {
            return Intrinsics.GetEcmaDateTimePartsForIntl(epochMilliseconds, utc);
        }

        internal static JsValue CallIteratorHelperMethod(JsRealm ownerRealm, in JsValue methodValue, JsObject thisObj,
            string typeErrorMessage)
        {
            return Intrinsics.CallIteratorHelperMethod(ownerRealm, methodValue, thisObj, typeErrorMessage);
        }

        internal JsObject CreateArrayFromTarget(
            in JsValue thisValue,
            bool iterablePath,
            long length,
            string methodName)
        {
            if (thisValue.TryGetObject(out var ctorObj) && ctorObj is JsFunction ctor && ctor.IsConstructor)
            {
                JsValue created;
                if (iterablePath)
                {
                    created = realm.ConstructWithExplicitNewTarget(ctor, ReadOnlySpan<JsValue>.Empty, ctor, 0);
                }
                else
                {
                    var lenArg = new InlineJsValueArray1
                    {
                        Item0 = length <= int.MaxValue ? JsValue.FromInt32((int)length) : new(length)
                    };
                    created = realm.ConstructWithExplicitNewTarget(ctor, lenArg.AsSpan(), ctor, 0);
                }

                if (!created.TryGetObject(out var target))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        $"{methodName} constructor must return an object");
                return target;
            }

            return realm.CreateArrayObject();
        }

        private static JsValue MapArrayFromValue(JsRealm ownerRealm, JsFunction? mapFn, in JsValue thisArg,
            in JsValue value,
            long index)
        {
            if (mapFn is null)
                return value;

            var callbackArgs = new InlineJsValueArray2
            {
                Item0 = value,
                Item1 = index <= int.MaxValue ? JsValue.FromInt32((int)index) : new(index)
            };
            return ownerRealm.InvokeFunction(mapFn, thisArg, callbackArgs.AsSpan());
        }

        internal static bool TryGetIteratorMethodForArrayFrom(JsRealm ownerRealm, JsObject items,
            out JsFunction iteratorMethod)
        {
            iteratorMethod = null!;
            if (!items.TryGetPropertyAtom(ownerRealm, IdSymbolIterator, out var iteratorMethodValue, out _) ||
                iteratorMethodValue.IsUndefined || iteratorMethodValue.IsNull)
                return false;
            if (!iteratorMethodValue.TryGetObject(out var iteratorMethodObj) ||
                iteratorMethodObj is not JsFunction iteratorFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Array.from items is not iterable");

            iteratorMethod = iteratorFn;
            return true;
        }

        internal static JsObject GetIteratorObjectForArrayFrom(JsRealm ownerRealm, JsObject items,
            JsFunction iteratorMethod)
        {
            var iteratorValue =
                ownerRealm.InvokeFunction(iteratorMethod, JsValue.FromObject(items), ReadOnlySpan<JsValue>.Empty);
            if (!iteratorValue.TryGetObject(out var iteratorObject))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Array.from iterator result must be object");
            return iteratorObject;
        }

        internal static bool TryGetIteratorObjectForArrayFrom(JsRealm ownerRealm, JsObject items, out JsObject iterator)
        {
            if (!JsRealm.TryGetIteratorMethodForArrayFrom(ownerRealm, items, out var iteratorMethod))
            {
                iterator = null!;
                return false;
            }

            iterator = JsRealm.GetIteratorObjectForArrayFrom(ownerRealm, items, iteratorMethod);
            return true;
        }

        internal JsValue StepIteratorForArrayFrom(JsObject iterator, out bool done)
        {
            if (!iterator.TryGetPropertyAtom(realm, IdNext, out var nextMethod, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Array.from iterator.next is not a function");
            if (!nextMethod.TryGetObject(out var nextMethodObj) || nextMethodObj is not JsFunction nextFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Array.from iterator.next is not a function");

            var stepResult = realm.InvokeFunction(nextFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
            if (!stepResult.TryGetObject(out var resultObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Array.from iterator result must be object");

            _ = resultObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
            done = doneValue.ToBoolean();
            if (done)
                return JsValue.Undefined;

            return resultObj.TryGetPropertyAtom(realm, IdValue, out var value, out _)
                ? value
                : JsValue.Undefined;
        }

        internal uint GetArrayFromLength(JsObject items)
        {
            if (!items.TryGetPropertyAtom(realm, IdLength, out var lengthValue, out _))
                return 0;

            var length = realm.ToIntegerOrInfinity(lengthValue);
            if (double.IsNaN(length) || length <= 0)
                return 0;
            if (double.IsPositiveInfinity(length) || length >= uint.MaxValue)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length", "ARRAY_LENGTH_INVALID");
            return (uint)length;
        }

        internal JsObject GetIteratorObjectForIterable(
            JsObject iterable,
            string notIterableMessage,
            string iteratorResultMessage)
        {
            if (!iterable.TryGetPropertyAtom(realm, IdSymbolIterator, out var iteratorMethod, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, notIterableMessage);
            if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) ||
                iteratorMethodObj is not JsFunction iteratorFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, notIterableMessage);

            var iteratorValue =
                realm.InvokeFunction(iteratorFn, JsValue.FromObject(iterable), ReadOnlySpan<JsValue>.Empty);
            if (!iteratorValue.TryGetObject(out var iterator))
                throw new JsRuntimeException(JsErrorKind.TypeError, iteratorResultMessage);
            return iterator;
        }

        internal uint NormalizeRelativeIndex(in JsValue value, uint length, uint defaultIndex)
        {
            if (value.IsUndefined)
                return defaultIndex;
            var relativeIndex = realm.ToIntegerOrInfinity(value);
            if (double.IsNegativeInfinity(relativeIndex))
                return 0;
            if (double.IsPositiveInfinity(relativeIndex))
                return length;
            var actualIndex = relativeIndex >= 0d ? relativeIndex : length + relativeIndex;
            if (actualIndex <= 0d)
                return 0;
            if (actualIndex >= length)
                return length;
            return (uint)actualIndex;
        }

        internal long NormalizeLastIndex(in JsValue value, uint length)
        {
            if (length == 0)
                return -1;
            var fromIndex = realm.ToIntegerOrInfinity(value);
            if (double.IsNegativeInfinity(fromIndex))
                return -1;
            if (double.IsPositiveInfinity(fromIndex))
                return length - 1L;
            var actualIndex = fromIndex >= 0d ? (long)fromIndex : length + (long)fromIndex;
            if (actualIndex < 0)
                return -1;
            if (actualIndex >= length)
                return length - 1L;
            return actualIndex;
        }

        internal bool HasTypedArrayIndexWithoutGet(JsObject? obj, uint index)
        {
            if (obj is null)
                return false;

            var key = index.ToString(CultureInfo.InvariantCulture);
            if (obj.TryHasPropertyViaTrap(realm, key, out var viaTrap))
                return viaTrap;
            if (obj.HasOwnPropertyKey(realm, key))
                return true;
            return realm.HasTypedArrayIndexWithoutGet(obj.Prototype, index);
        }

        internal double ToIntegerOrInfinity(in JsValue value)
        {
            var number = realm.ToNumberSlowPath(value);
            if (double.IsNaN(number) || number == 0d)
                return 0d;
            if (double.IsInfinity(number))
                return number;
            return Math.Truncate(number);
        }

        public uint ToUint32(in JsValue value)
        {
            var number = realm.ToNumberSlowPath(value);
            if (double.IsNaN(number) || number == 0d || double.IsInfinity(number))
                return 0;
            var intPart = Math.Truncate(number);
            var uint32 = intPart % 4294967296d;
            if (uint32 < 0)
                uint32 += 4294967296d;
            return (uint)uint32;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal JsValue ToNumericSlowPath(in JsValue value)
        {
            if (value.IsNumber || value.IsBigInt)
                return value;

            var primitive = realm.ToPrimitiveSlowPath(value, false);
            if (primitive.IsBigInt)
                return primitive;
            return new(realm.ToNumberSlowPath(primitive));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal string ToJsStringSlowPath(in JsValue value)
        {
            if (value.IsString) return value.AsJsString().Flatten();
            if (value.IsNumber) return JsNumberFormatting.ToJsString(value.NumberValue);
            if (value.IsBool) return value.IsTrue ? "true" : "false";
            if (value.IsNull) return "null";
            if (value.IsUndefined) return "undefined";
            if (value.IsSymbol)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert a Symbol value to a string");
            if (value.IsBigInt) return value.AsBigInt().Value.ToString();
            if (value.IsObject) return realm.ToJsStringSlowPath(realm.ToPrimitiveSlowPath(value, true));
            return value.ToString();
        }

        internal JsString ToJsStringValue(in JsValue value)
        {
            if (value.IsString) return value.AsJsString();
            return realm.ToJsStringValueSlowPath(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal JsValue ToPrimitiveSlowPath(in JsValue value, bool preferString)
        {
            if (!value.IsObject)
                return value;

            var obj = value.AsObject();

            if (obj.TryGetPropertyAtom(realm, IdSymbolToPrimitive, out var exoticToPrim, out _) &&
                !exoticToPrim.IsUndefined && !exoticToPrim.IsNull)
            {
                if (!exoticToPrim.TryGetObject(out var exoticObj) || exoticObj is not JsFunction exoticFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "@@toPrimitive is not callable");

                var hint = JsValue.FromString(preferString ? "string" : "number");
                var hintArgs = MemoryMarshal.CreateReadOnlySpan(ref hint, 1);
                var exoticResult = realm.InvokeFunction(exoticFn, obj, hintArgs);
                if (exoticResult.IsObject)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "@@toPrimitive must return a primitive value");
                return exoticResult;
            }

            var first = preferString ? IdToString : IdValueOf;
            var second = preferString ? IdValueOf : IdToString;
            var thisValue = obj;

            if (realm.TryInvokePrimitiveMethodSlowPath(obj, thisValue, first, out var primitive))
                return primitive;
            if (realm.TryInvokePrimitiveMethodSlowPath(obj, thisValue, second, out primitive))
                return primitive;

            throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert object to primitive value");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal JsValue ToPrimitiveDefaultHintSlowPath(in JsValue value)
        {
            if (!value.IsObject)
                return value;

            var obj = value.AsObject();

            if (obj.TryGetPropertyAtom(realm, IdSymbolToPrimitive, out var exoticToPrim, out _) &&
                !exoticToPrim.IsUndefined && !exoticToPrim.IsNull)
            {
                if (!exoticToPrim.TryGetObject(out var exoticObj) || exoticObj is not JsFunction exoticFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "@@toPrimitive is not callable");

                var hint = JsValue.FromString("default");
                var hintArgs = MemoryMarshal.CreateReadOnlySpan(ref hint, 1);
                var exoticResult = realm.InvokeFunction(exoticFn, obj, hintArgs);
                if (exoticResult.IsObject)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "@@toPrimitive must return a primitive value");
                return exoticResult;
            }

            if (realm.TryInvokePrimitiveMethodSlowPath(obj, obj, IdValueOf, out var primitive))
                return primitive;
            if (realm.TryInvokePrimitiveMethodSlowPath(obj, obj, IdToString, out primitive))
                return primitive;

            throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert object to primitive value");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryInvokePrimitiveMethodSlowPath(JsObject obj, JsValue thisValue,
            int methodAtom, out JsValue primitive)
        {
            if (obj.TryGetPropertyAtom(realm, methodAtom, out var candidate, out _) &&
                candidate.TryGetObject(out var fnObj) && fnObj is JsFunction fn)
            {
                var value = realm.InvokeFunction(fn, thisValue, ReadOnlySpan<JsValue>.Empty);
                if (!value.IsObject)
                {
                    primitive = value;
                    return true;
                }
            }

            primitive = JsValue.Undefined;
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal JsString ToJsStringValueSlowPath(in JsValue value)
        {
            if (value.IsString) return value.AsJsString();
            if (value.IsNumber) return JsNumberFormatting.ToJsString(value.NumberValue);
            if (value.IsBool) return value.IsTrue ? "true" : "false";
            if (value.IsNull) return "null";
            if (value.IsUndefined) return "undefined";
            if (value.IsSymbol)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert a Symbol value to a string");
            if (value.IsBigInt) return value.AsBigInt().Value.ToString();
            if (value.IsObject)
                return realm.ToJsStringValueSlowPath(realm.ToPrimitiveSlowPath(value, true));
            return value.ToString() ?? string.Empty;
        }
    }
}
