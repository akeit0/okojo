using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateIteratorConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct ||
                (info.NewTarget.TryGetObject(out var newTargetObj) && ReferenceEquals(newTargetObj, callee)))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator constructor cannot be directly called or constructed");

            var prototype =
                GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.IteratorPrototype);
            return new JsPlainObject(realm, false)
            {
                Prototype = prototype
            };
        }, "Iterator", 0, true);
    }

    private void InstallIteratorConstructorBuiltins()
    {
        var constructorGetFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            return JsValue.FromObject(realm.IteratorConstructor);
        }, "get constructor", 0);

        var constructorSetFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var thisObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.constructor setter called on incompatible receiver");

            if (ReferenceEquals(thisObj, realm.IteratorPrototype))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot assign Iterator.prototype.constructor");

            var value = args.Length == 0 ? JsValue.Undefined : args[0];
            const int atomConstructor = IdConstructor;
            if (!thisObj.TryGetOwnNamedPropertyDescriptorAtom(realm, atomConstructor, out _))
            {
                if (!thisObj.DefineOwnDataPropertyExact(realm, atomConstructor, value, JsShapePropertyFlags.Open))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to property");

                return JsValue.Undefined;
            }

            if (!thisObj.TrySetPropertyAtom(realm, atomConstructor, value, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to property");
            return JsValue.Undefined;
        }, "set constructor", 1);

        var toStringTagGetFn = new JsHostFunction(Realm, (in info) => { return JsValue.FromString("Iterator"); },
            "get [Symbol.toStringTag]", 0);

        var toStringTagSetFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var thisObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype[Symbol.toStringTag] setter called on incompatible receiver");

            if (ReferenceEquals(thisObj, realm.IteratorPrototype))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot assign Iterator.prototype[Symbol.toStringTag]");

            var value = args.Length == 0 ? JsValue.Undefined : args[0];
            const int atomToStringTag = IdSymbolToStringTag;
            if (!thisObj.TryGetOwnNamedPropertyDescriptorAtom(realm, atomToStringTag, out _))
            {
                if (!thisObj.DefineOwnDataPropertyExact(realm, atomToStringTag, value, JsShapePropertyFlags.Open))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to property");
                return JsValue.Undefined;
            }

            if (!thisObj.TrySetPropertyAtom(realm, atomToStringTag, value, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to property");
            return JsValue.Undefined;
        }, "set [Symbol.toStringTag]", 1);

        var concatFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var items = new List<IteratorConcatItem>(args.Length);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].TryGetObject(out var itemObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator.concat item must be an object");

                if (!itemObj.TryGetPropertyAtom(realm, IdSymbolIterator, out var methodValue, out _) ||
                    !methodValue.TryGetObject(out var methodObj) || methodObj is not JsFunction iteratorMethod)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Iterator.concat item is not iterable");

                items.Add(new(itemObj, iteratorMethod));
            }

            return new JsIteratorConcatObject(realm, items);
        }, "concat", 0);

        var dropFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.drop called on incompatible receiver");

            double numLimit;
            try
            {
                numLimit = args.Length == 0 ? double.NaN : realm.ToNumberSlowPath(args[0]);
            }
            catch (Exception)
            {
                CloseIteratorForIteratorHelper(realm, iteratorObj);
                throw;
            }

            if (double.IsNaN(numLimit))
            {
                CloseIteratorForIteratorHelper(realm, iteratorObj);
                throw new JsRuntimeException(JsErrorKind.RangeError, "Iterator.prototype.drop limit is invalid");
            }

            var integerLimit = double.IsInfinity(numLimit)
                ? numLimit
                : numLimit == 0d
                    ? 0d
                    : Math.Truncate(numLimit);
            if (integerLimit < 0d)
            {
                CloseIteratorForIteratorHelper(realm, iteratorObj);
                throw new JsRuntimeException(JsErrorKind.RangeError, "Iterator.prototype.drop limit is invalid");
            }

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            var limit = double.IsPositiveInfinity(integerLimit) ? long.MaxValue : (long)integerLimit;
            return new JsIteratorDropObject(realm, iteratorObj, nextValue, limit);
        }, "drop", 1);

        var everyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.every called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var predicateObj) ||
                predicateObj is not JsFunction predicate)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.every predicate must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _) ||
                !nextValue.TryGetObject(out var nextObj) || nextObj is not JsFunction)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator next must be callable");

            long counter = 0;
            while (true)
            {
                var step = CallIteratorHelperMethod(realm, nextValue, iteratorObj, "Iterator next must be callable");
                if (!step.TryGetObject(out var stepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                stepObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
                if (ToBooleanLocal(doneValue))
                    return JsValue.True;

                stepObj.TryGetPropertyAtom(realm, IdValue, out var value, out _);
                JsValue predicateResult;
                var predicateArgs = new[] { value, new JsValue(counter) };
                try
                {
                    predicateResult = realm.InvokeFunction(predicate, JsValue.Undefined, predicateArgs);
                }
                catch
                {
                    BestEffortCloseIteratorForIteratorHelper(realm, iteratorObj);
                    throw;
                }

                if (!ToBooleanLocal(predicateResult))
                {
                    CloseIteratorForIteratorHelper(realm, iteratorObj);
                    return JsValue.False;
                }

                counter++;
            }
        }, "every", 1);

        var filterFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.filter called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var predicateObj) ||
                predicateObj is not JsFunction predicate)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.filter predicate must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            return new JsIteratorFilterObject(realm, iteratorObj, nextValue, predicate);
        }, "filter", 1);

        var findFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.find called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var predicateObj) ||
                predicateObj is not JsFunction predicate)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.find predicate must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            long counter = 0;
            while (true)
            {
                var step = CallIteratorHelperMethod(realm, nextValue, iteratorObj, "Iterator next must be callable");
                if (!step.TryGetObject(out var stepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                stepObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
                if (ToBooleanLocal(doneValue))
                    return JsValue.Undefined;

                stepObj.TryGetPropertyAtom(realm, IdValue, out var value, out _);
                JsValue predicateResult;
                var predicateArgs = new[] { value, new JsValue(counter) };
                try
                {
                    predicateResult = realm.InvokeFunction(predicate, JsValue.Undefined, predicateArgs);
                }
                catch
                {
                    BestEffortCloseIteratorForIteratorHelper(realm, iteratorObj);
                    throw;
                }

                if (ToBooleanLocal(predicateResult))
                {
                    CloseIteratorForIteratorHelper(realm, iteratorObj);
                    return value;
                }

                counter++;
            }
        }, "find", 1);

        var flatMapFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.flatMap called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var mapperObj) || mapperObj is not JsFunction mapper)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.flatMap mapper must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            return new JsIteratorFlatMapObject(realm, iteratorObj, nextValue, mapper);
        }, "flatMap", 1);

        var forEachFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.forEach called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.forEach callback must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            long counter = 0;
            while (true)
            {
                var step = CallIteratorHelperMethod(realm, nextValue, iteratorObj, "Iterator next must be callable");
                if (!step.TryGetObject(out var stepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                stepObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
                if (ToBooleanLocal(doneValue))
                    return JsValue.Undefined;

                stepObj.TryGetPropertyAtom(realm, IdValue, out var value, out _);
                var callbackArgs = new InlineJsValueArray2 { Item0 = value, Item1 = new(counter) };
                try
                {
                    realm.InvokeFunction(fn, JsValue.Undefined, callbackArgs.AsSpan());
                }
                catch
                {
                    BestEffortCloseIteratorForIteratorHelper(realm, iteratorObj);
                    throw;
                }

                counter++;
            }
        }, "forEach", 1);

        var mapFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.map called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var mapperObj) || mapperObj is not JsFunction mapper)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.map mapper must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            return new JsIteratorMapObject(realm, iteratorObj, nextValue, mapper);
        }, "map", 1);

        var reduceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.reduce called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var reducerObj) || reducerObj is not JsFunction reducer)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.reduce reducer must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _) ||
                !nextValue.TryGetObject(out var nextObj) || nextObj is not JsFunction)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator next must be callable");

            JsValue accumulator;
            long counter;

            if (args.Length < 2)
            {
                var firstStep =
                    CallIteratorHelperMethod(realm, nextValue, iteratorObj, "Iterator next must be callable");
                if (!firstStep.TryGetObject(out var firstStepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                firstStepObj.TryGetPropertyAtom(realm, IdDone, out var firstDoneValue, out _);
                if (ToBooleanLocal(firstDoneValue))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Iterator.prototype.reduce of empty iterator with no initial value");

                firstStepObj.TryGetPropertyAtom(realm, IdValue, out accumulator, out _);
                counter = 1;
            }
            else
            {
                accumulator = args[1];
                counter = 0;
            }

            while (true)
            {
                var step = CallIteratorHelperMethod(realm, nextValue, iteratorObj, "Iterator next must be callable");
                if (!step.TryGetObject(out var stepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                stepObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
                if (ToBooleanLocal(doneValue))
                    return accumulator;

                stepObj.TryGetPropertyAtom(realm, IdValue, out var value, out _);
                JsValue result;
                var reducerArgs = new[] { accumulator, value, new JsValue(counter) };
                try
                {
                    result = realm.InvokeFunction(reducer, JsValue.Undefined, reducerArgs);
                }
                catch
                {
                    BestEffortCloseIteratorForIteratorHelper(realm, iteratorObj);
                    throw;
                }

                accumulator = result;
                counter++;
            }
        }, "reduce", 1);

        var someFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.some called on incompatible receiver");

            if (args.Length == 0 || !args[0].TryGetObject(out var predicateObj) ||
                predicateObj is not JsFunction predicate)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.some predicate must be callable");

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            long counter = 0;
            while (true)
            {
                var step = CallIteratorHelperMethod(realm, nextValue, iteratorObj, "Iterator next must be callable");
                if (!step.TryGetObject(out var stepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                stepObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
                if (ToBooleanLocal(doneValue))
                    return JsValue.False;

                stepObj.TryGetPropertyAtom(realm, IdValue, out var value, out _);
                JsValue result;
                var predicateArgs = new[] { value, new JsValue(counter) };
                try
                {
                    result = realm.InvokeFunction(predicate, JsValue.Undefined, predicateArgs);
                }
                catch
                {
                    BestEffortCloseIteratorForIteratorHelper(realm, iteratorObj);
                    throw;
                }

                if (ToBooleanLocal(result))
                {
                    CloseIteratorForIteratorHelper(realm, iteratorObj);
                    return JsValue.True;
                }

                counter++;
            }
        }, "some", 1);

        var takeFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.take called on incompatible receiver");

            double numLimit;
            try
            {
                numLimit = args.Length == 0 ? double.NaN : realm.ToNumberSlowPath(args[0]);
            }
            catch
            {
                BestEffortCloseIteratorForIteratorHelper(realm, iteratorObj);
                throw;
            }

            if (double.IsNaN(numLimit))
            {
                CloseIteratorForIteratorHelper(realm, iteratorObj);
                throw new JsRuntimeException(JsErrorKind.RangeError, "Iterator.prototype.take limit is invalid");
            }

            var integerLimit = double.IsInfinity(numLimit)
                ? numLimit
                : numLimit == 0d
                    ? 0d
                    : Math.Truncate(numLimit);
            if (integerLimit < 0d)
            {
                CloseIteratorForIteratorHelper(realm, iteratorObj);
                throw new JsRuntimeException(JsErrorKind.RangeError, "Iterator.prototype.take limit is invalid");
            }

            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
                nextValue = JsValue.Undefined;

            var limit = double.IsPositiveInfinity(integerLimit) ? long.MaxValue : (long)integerLimit;
            return new JsIteratorTakeObject(realm, iteratorObj, nextValue, limit);
        }, "take", 1);

        var disposeFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype[Symbol.dispose] called on incompatible receiver");

            if (iteratorObj.TryGetPropertyAtom(realm, IdReturn, out var returnValue, out _) &&
                !returnValue.IsUndefined && !returnValue.IsNull)
                _ = CallIteratorHelperMethod(realm, returnValue, iteratorObj, "Iterator return must be callable");

            return JsValue.Undefined;
        }, "[Symbol.dispose]", 0);

        var asyncDisposeFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            try
            {
                var thisValue = info.ThisValue;
                if (!thisValue.TryGetObject(out var iteratorObj))
                {
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "AsyncIterator.prototype[Symbol.asyncDispose] called on incompatible receiver");
                }

                if (iteratorObj.TryGetPropertyAtom(realm, IdReturn, out var returnValue, out _) &&
                    !returnValue.IsUndefined && !returnValue.IsNull)
                {
                    var result = CallIteratorHelperMethod(realm, returnValue, iteratorObj,
                        "AsyncIterator return must be callable");
                    return realm.PromiseResolveValue(result);
                }

                return realm.PromiseResolveValue(JsValue.Undefined);
            }
            catch (JsRuntimeException ex)
            {
                var promise = realm.CreatePromiseObject();
                realm.RejectPromise(promise, realm.GetPromiseAbruptReason(ex));
                return promise;
            }
        }, "[Symbol.asyncDispose]", 0);

        var fromFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator.from requires an input");

            var source = args[0];
            var sourceWasPrimitiveString = source.IsString;

            if (sourceWasPrimitiveString)
                if (TryGetIteratorMethodForPrimitiveString(realm, source, out var stringMethodValue))
                {
                    if (!stringMethodValue.TryGetObject(out var stringMethodObj) ||
                        stringMethodObj is not JsFunction stringMethod)
                        throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator.from input is not iterable");

                    var iteratorValue = realm.InvokeFunction(stringMethod, source, ReadOnlySpan<JsValue>.Empty);
                    if (!iteratorValue.TryGetObject(out _))
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Iterator method must return an object");
                    return iteratorValue;
                }

            if (!source.TryGetObject(out var sourceObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator.from requires an object or string");

            if (sourceObj.TryGetPropertyAtom(realm, IdSymbolIterator, out var iteratorMethodValue, out _))
            {
                if (iteratorMethodValue.IsUndefined || iteratorMethodValue.IsNull)
                    return WrapDirectIterator(realm, sourceObj);

                if (!iteratorMethodValue.TryGetObject(out var iteratorMethodObj) ||
                    iteratorMethodObj is not JsFunction iteratorMethod)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator.from input is not iterable");

                var iteratorValue = realm.InvokeFunction(iteratorMethod, JsValue.FromObject(sourceObj),
                    ReadOnlySpan<JsValue>.Empty);
                if (!iteratorValue.TryGetObject(out _))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator method must return an object");
                return iteratorValue;
            }

            return WrapDirectIterator(realm, sourceObj);
        }, "from", 1);

        var iteratorSelfFn = new JsHostFunction(Realm, (in info) =>
            {
                var thisValue = info.ThisValue;
                return thisValue;
            },
            "[Symbol.iterator]", 0);

        var asyncIteratorSelfFn =
            new JsHostFunction(Realm, (in info) => { return info.ThisValue; }, "[Symbol.asyncIterator]", 0);
        IteratorSelfFunction = iteratorSelfFn;
        AsyncIteratorSelfFunction = asyncIteratorSelfFn;

        var toArrayFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var iteratorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.toArray called on incompatible receiver");
            if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _) ||
                !nextValue.TryGetObject(out var nextObj) || nextObj is not JsFunction nextFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.prototype.toArray requires a callable next");

            var outArray = realm.CreateArrayObject();
            uint index = 0;
            while (true)
            {
                var step = realm.InvokeFunction(nextFn, thisValue, ReadOnlySpan<JsValue>.Empty);
                if (!step.TryGetObject(out var stepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");
                stepObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
                if (ToBooleanLocal(doneValue))
                    return outArray;
                stepObj.TryGetPropertyAtom(realm, IdValue, out var value, out _);
                FreshArrayOperations.DefineElement(outArray, index++, value);
            }
        }, "toArray", 0);

        Span<PropertyDefinition> iteratorCtorDefs =
        [
            PropertyDefinition.Mutable(IdConcat, JsValue.FromObject(concatFn)),
            PropertyDefinition.Mutable(IdFrom, JsValue.FromObject(fromFn))
        ];
        IteratorConstructor.InitializePrototypeProperty(IteratorPrototype);
        IteratorConstructor.DefineNewPropertiesNoCollision(Realm, iteratorCtorDefs);

        Span<PropertyDefinition> iteratorProtoDefs =
        [
            PropertyDefinition.GetterSetterData(IdConstructor, constructorGetFn, constructorSetFn,
                configurable: true),
            PropertyDefinition.GetterSetterData(IdSymbolToStringTag, toStringTagGetFn, toStringTagSetFn,
                configurable: true),
            PropertyDefinition.Mutable(IdDrop, JsValue.FromObject(dropFn)),
            PropertyDefinition.Mutable(IdEvery, JsValue.FromObject(everyFn)),
            PropertyDefinition.Mutable(IdFilter, JsValue.FromObject(filterFn)),
            PropertyDefinition.Mutable(IdFind, JsValue.FromObject(findFn)),
            PropertyDefinition.Mutable(IdFlatMap, JsValue.FromObject(flatMapFn)),
            PropertyDefinition.Mutable(IdForEach, JsValue.FromObject(forEachFn)),
            PropertyDefinition.Mutable(IdMap, JsValue.FromObject(mapFn)),
            PropertyDefinition.Mutable(IdReduce, JsValue.FromObject(reduceFn)),
            PropertyDefinition.Mutable(IdSome, JsValue.FromObject(someFn)),
            PropertyDefinition.Mutable(IdTake, JsValue.FromObject(takeFn)),
            PropertyDefinition.Mutable(IdSymbolDispose, JsValue.FromObject(disposeFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(iteratorSelfFn)),
            PropertyDefinition.Mutable(IdToArray, JsValue.FromObject(toArrayFn))
        ];
        IteratorPrototype.DefineNewPropertiesNoCollision(Realm, iteratorProtoDefs);

        Span<PropertyDefinition> asyncIteratorProtoDefs =
        [
            PropertyDefinition.Mutable(IdSymbolAsyncIterator, JsValue.FromObject(asyncIteratorSelfFn)),
            PropertyDefinition.Mutable(IdSymbolAsyncDispose, JsValue.FromObject(asyncDisposeFn))
        ];
        AsyncIteratorPrototype.DefineNewPropertiesNoCollision(Realm, asyncIteratorProtoDefs);
    }

    private static JsValue WrapDirectIterator(JsRealm realm, JsObject iterator)
    {
        if (!iterator.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _))
            nextValue = JsValue.Undefined;

        return new JsWrappedIteratorObject(realm, iterator, nextValue);
    }

    internal static bool TryGetIteratorMethodForPrimitiveString(JsRealm realm, in JsValue source,
        out JsValue methodValue)
    {
        JsObject? cursor = realm.StringPrototype;
        while (cursor is not null)
        {
            if (cursor.TryGetOwnNamedPropertyDescriptorAtom(realm, IdSymbolIterator, out var descriptor))
            {
                if (descriptor.IsAccessor)
                {
                    var getter = descriptor.Getter;
                    if (getter is null)
                    {
                        methodValue = JsValue.Undefined;
                        return true;
                    }

                    methodValue = realm.InvokeFunction(getter, source, ReadOnlySpan<JsValue>.Empty);
                    return true;
                }

                methodValue = descriptor.Value;
                return true;
            }

            cursor = cursor.Prototype;
        }

        methodValue = JsValue.Undefined;
        return false;
    }

    private static bool ToBooleanLocal(in JsValue value)
    {
        if (value.IsBool)
            return value.IsTrue;
        if (value.IsNullOrUndefined)
            return false;
        if (value.IsNumber)
        {
            var number = value.NumberValue;
            return number != 0d && !double.IsNaN(number);
        }

        if (value.IsString)
            return value.AsString().Length != 0;
        if (value.IsBigInt)
            return !value.AsBigInt().Value.IsZero;
        return true;
    }

    private static void CloseIteratorForIteratorHelper(JsRealm realm, JsObject iteratorObj)
    {
        if (!iteratorObj.TryGetPropertyAtom(realm, IdReturn, out var returnValue, out _) ||
            returnValue.IsUndefined || returnValue.IsNull)
            return;

        CallIteratorHelperMethod(realm, returnValue, iteratorObj, "Iterator return must be callable");
    }

    internal static JsValue CallIteratorHelperMethod(JsRealm realm, in JsValue methodValue, JsObject thisObj,
        string typeErrorMessage)
    {
        if (!methodValue.TryGetObject(out var methodObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, typeErrorMessage);

        if (!methodObj.TryGetPropertyAtom(realm, IdCall, out var callValue, out _))
            throw new JsRuntimeException(JsErrorKind.TypeError, typeErrorMessage);

        var args = new[] { JsValue.FromObject(thisObj) };
        return realm.Call(callValue, methodValue, args);
    }

    private static void BestEffortCloseIteratorForIteratorHelper(JsRealm realm, JsObject iteratorObj)
    {
        try
        {
            CloseIteratorForIteratorHelper(realm, iteratorObj);
        }
        catch
        {
            // Preserve the original abrupt completion.
        }
    }

    internal readonly record struct IteratorConcatItem(JsObject Iterable, JsFunction IteratorMethod);
}
