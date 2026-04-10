using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateArrayFromAsyncFunction()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var promise = realm.CreatePromiseObject();
            try
            {
                var operation = CreateArrayFromAsyncOperation(promise, thisValue, args);
                StartArrayFromAsync(operation);
            }
            catch (JsRuntimeException ex)
            {
                realm.RejectPromise(promise, ex.ThrownValue ?? realm.CreateErrorObjectFromException(ex));
            }

            return promise;
        }, "fromAsync", 1);
    }

    private JsArrayFromAsyncOperation CreateArrayFromAsyncOperation(
        JsPromiseObject promise,
        JsValue thisValue,
        ReadOnlySpan<JsValue> args)
    {
        JsFunction? mapFn = null;
        var thisArg = JsValue.Undefined;
        if (args.Length > 1 && !args[1].IsUndefined)
        {
            if (!args[1].TryGetObject(out var mapFnObj) || mapFnObj is not JsFunction callback)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync mapfn must be a function");
            mapFn = callback;
            if (args.Length > 2)
                thisArg = args[2];
        }

        if (args.Length == 0 || !Realm.TryToObject(args[0], out var items))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Array.fromAsync items must not be null or undefined");

        JsObject target;
        var ctor = thisValue.TryGetObject(out var ctorObject) && ctorObject is JsFunction ctorCandidate &&
                   ctorCandidate.IsConstructor
            ? ctorCandidate
            : null;
        var usedConstructor = ctor is not null;
        var operation = new JsArrayFromAsyncOperation
        {
            Promise = promise,
            MapFunction = mapFn,
            ThisArg = thisArg
        };

        if (TryGetAsyncIteratorObjectForArrayFromAsync(Realm, items, out var asyncIterator, out var asyncNextMethod))
        {
            target = usedConstructor
                ? ConstructArrayFromAsyncTarget(ctor!, true, 0)
                : Realm.CreateArrayObject();
            operation.Target = target;
            operation.Iterator = asyncIterator;
            operation.NextMethod = asyncNextMethod;
            operation.AwaitInputValue = false;
            return operation;
        }

        if (TryGetIteratorObjectForArrayFromAsync(Realm, items, out var iterator, out var nextMethod))
        {
            target = usedConstructor
                ? ConstructArrayFromAsyncTarget(ctor!, true, 0)
                : Realm.CreateArrayObject();
            operation.Target = target;
            operation.Iterator = iterator;
            operation.NextMethod = nextMethod;
            operation.AwaitInputValue = true;
            return operation;
        }

        var arrayLikeLength = Realm.GetArrayLikeLengthLong(items);
        if (!usedConstructor && arrayLikeLength > uint.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length", "ARRAY_LENGTH_INVALID");

        target = usedConstructor
            ? ConstructArrayFromAsyncTarget(ctor!, false, arrayLikeLength)
            : Realm.CreateArrayObject();
        operation.Target = target;
        operation.ArrayLike = items;
        operation.ArrayLikeLength = arrayLikeLength;
        operation.AwaitInputValue = true;
        return operation;
    }

    private JsObject ConstructArrayFromAsyncTarget(JsFunction ctor, bool iterablePath, long length)
    {
        JsValue created;
        if (iterablePath)
        {
            created = Realm.ConstructWithExplicitNewTarget(ctor, ReadOnlySpan<JsValue>.Empty, ctor, 0);
        }
        else
        {
            var lenArg = new InlineJsValueArray1
            {
                Item0 = length <= int.MaxValue ? JsValue.FromInt32((int)length) : new(length)
            };
            created = Realm.ConstructWithExplicitNewTarget(ctor, lenArg.AsSpan(), ctor, 0);
        }

        if (!created.TryGetObject(out var target))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync constructor must return an object");
        return target;
    }

    private void StartArrayFromAsync(JsArrayFromAsyncOperation operation)
    {
        if (operation.IsArrayLike)
        {
            ContinueArrayFromAsyncArrayLike(operation);
            return;
        }

        ContinueArrayFromAsyncIterator(operation);
    }

    private void ContinueArrayFromAsyncIterator(JsArrayFromAsyncOperation operation)
    {
        try
        {
            var stepResult = Realm.InvokeFunction(operation.NextMethod!, JsValue.FromObject(operation.Iterator!),
                ReadOnlySpan<JsValue>.Empty);
            AwaitArrayFromAsync(operation, stepResult,
                static (realm, op, settled) => { realm.Intrinsics.OnArrayFromAsyncIteratorStepSettled(op, settled); });
        }
        catch (JsRuntimeException ex)
        {
            RejectArrayFromAsync(operation, ex);
        }
    }

    private void OnArrayFromAsyncIteratorStepSettled(JsArrayFromAsyncOperation operation, JsValue settled)
    {
        try
        {
            if (!settled.TryGetObject(out var stepObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync iterator result must be object");

            stepObj.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
            if (JsRealm.ToBoolean(doneValue))
            {
                SetArrayLikeLengthOrThrow(Realm, operation.Target, operation.Index, "Array.fromAsync");
                ResolvePromise(operation.Promise, JsValue.FromObject(operation.Target));
                return;
            }

            stepObj.TryGetPropertyAtom(Realm, IdValue, out var value, out _);
            ProcessArrayFromAsyncSourceValue(operation, value);
        }
        catch (JsRuntimeException ex)
        {
            RejectArrayFromAsync(operation, ex);
        }
    }

    private void ContinueArrayFromAsyncArrayLike(JsArrayFromAsyncOperation operation)
    {
        if (operation.Index >= operation.ArrayLikeLength)
        {
            try
            {
                SetArrayLikeLengthOrThrow(Realm, operation.Target, operation.Index, "Array.fromAsync");
                ResolvePromise(operation.Promise, JsValue.FromObject(operation.Target));
            }
            catch (JsRuntimeException ex)
            {
                RejectArrayFromAsync(operation, ex);
            }

            return;
        }

        try
        {
            GetArrayLikeIndex(Realm, operation.ArrayLike!, operation.Index, out var value);
            ProcessArrayFromAsyncSourceValue(operation, value);
        }
        catch (JsRuntimeException ex)
        {
            RejectArrayFromAsync(operation, ex);
        }
    }

    private void ProcessArrayFromAsyncSourceValue(JsArrayFromAsyncOperation operation, JsValue value)
    {
        if (operation.MapFunction is null)
        {
            if (operation.AwaitInputValue)
                AwaitArrayFromAsync(operation, value,
                    static (realm, op, settled) =>
                    {
                        realm.Intrinsics.StoreArrayFromAsyncValueAndContinue(op, settled);
                    });
            else
                StoreArrayFromAsyncValueAndContinue(operation, value);

            return;
        }

        AwaitArrayFromAsync(operation, value,
            static (realm, op, settled) => { realm.Intrinsics.MapArrayFromAsyncValueAndAwaitResult(op, settled); });
    }

    private void MapArrayFromAsyncValueAndAwaitResult(JsArrayFromAsyncOperation operation, JsValue value)
    {
        try
        {
            Span<JsValue> callbackArgs =
            [
                value,
                operation.Index <= int.MaxValue ? JsValue.FromInt32((int)operation.Index) : new(operation.Index)
            ];
            var mapped = Realm.InvokeFunction(operation.MapFunction!, operation.ThisArg, callbackArgs);
            AwaitArrayFromAsync(operation, mapped,
                static (realm, op, settled) => { realm.Intrinsics.StoreArrayFromAsyncValueAndContinue(op, settled); });
        }
        catch (JsRuntimeException ex)
        {
            RejectArrayFromAsync(operation, ex);
        }
    }

    private void StoreArrayFromAsyncValueAndContinue(JsArrayFromAsyncOperation operation, JsValue value)
    {
        try
        {
            CreateDataPropertyOrThrowForArrayFromAsync(operation.Target, operation.Index, value);
            operation.Index++;
            if (operation.IsArrayLike)
                ContinueArrayFromAsyncArrayLike(operation);
            else
                ContinueArrayFromAsyncIterator(operation);
        }
        catch (JsRuntimeException ex)
        {
            RejectArrayFromAsync(operation, ex);
        }
    }

    private void AwaitArrayFromAsync(
        JsArrayFromAsyncOperation operation,
        JsValue value,
        Action<JsRealm, JsArrayFromAsyncOperation, JsValue> onFulfilled)
    {
        var promise = PromiseResolveValue(value);
        var fulfilled = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var f = (JsHostFunction)info.Function;
            var continuation = (ArrayFromAsyncContinuation)f.UserData!;
            continuation.OnFulfilled(realm, continuation.Operation, args.Length != 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }, string.Empty, 1);
        fulfilled.UserData = new ArrayFromAsyncContinuation(operation, onFulfilled);

        var rejected = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var f = (JsHostFunction)info.Function;
            var continuation = (ArrayFromAsyncContinuation)f.UserData!;
            var reason = args.Length != 0 ? args[0] : JsValue.Undefined;
            realm.Intrinsics.CloseArrayFromAsyncIteratorBestEffort(continuation.Operation);
            realm.RejectPromise(continuation.Operation.Promise, reason);
            return JsValue.Undefined;
        }, string.Empty, 1);
        rejected.UserData = new ArrayFromAsyncContinuation(operation, onFulfilled);

        PromiseThen(promise, JsValue.FromObject(fulfilled), JsValue.FromObject(rejected));
    }

    private void RejectArrayFromAsync(JsArrayFromAsyncOperation operation, JsRuntimeException ex)
    {
        CloseArrayFromAsyncIteratorBestEffort(operation);
        RejectPromise(operation.Promise, ex.ThrownValue ?? Realm.CreateErrorObjectFromException(ex));
    }

    private void CreateDataPropertyOrThrowForArrayFromAsync(JsObject target, long index, JsValue value)
    {
        CreateDataPropertyOrThrowForArrayLike(Realm, target, index, value, "Array.fromAsync");
    }

    private void CloseArrayFromAsyncIteratorBestEffort(JsArrayFromAsyncOperation operation)
    {
        if (operation.Iterator is null)
            return;

        try
        {
            if (!operation.Iterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) ||
                !returnValue.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
                return;

            Realm.InvokeFunction(returnFn, JsValue.FromObject(operation.Iterator), ReadOnlySpan<JsValue>.Empty);
        }
        catch (JsRuntimeException)
        {
        }
    }

    private static bool TryGetAsyncIteratorObjectForArrayFromAsync(
        JsRealm realm,
        JsObject items,
        out JsObject iterator,
        out JsFunction nextMethod)
    {
        iterator = null!;
        nextMethod = null!;
        if (!items.TryGetPropertyAtom(realm, IdSymbolAsyncIterator, out var iteratorMethod, out _) ||
            iteratorMethod.IsUndefined || iteratorMethod.IsNull)
            return false;

        if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Array.fromAsync async iterator method is not a function");

        var iteratorValue = realm.InvokeFunction(iteratorFn, JsValue.FromObject(items), ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObject))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync async iterator result must be object");

        if (!iteratorObject.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _) ||
            !nextValue.TryGetObject(out var nextObj) || nextObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync iterator.next is not a function");

        iterator = iteratorObject;
        nextMethod = nextFn;
        return true;
    }

    private static bool TryGetIteratorObjectForArrayFromAsync(
        JsRealm realm,
        JsObject items,
        out JsObject iterator,
        out JsFunction nextMethod)
    {
        iterator = null!;
        nextMethod = null!;
        if (!items.TryGetPropertyAtom(realm, IdSymbolIterator, out var iteratorMethod, out _) ||
            iteratorMethod.IsUndefined || iteratorMethod.IsNull)
            return false;

        if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync iterator method is not a function");

        var iteratorValue = realm.InvokeFunction(iteratorFn, JsValue.FromObject(items), ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObject))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync iterator result must be object");

        if (!iteratorObject.TryGetPropertyAtom(realm, IdNext, out var nextValue, out _) ||
            !nextValue.TryGetObject(out var nextObj) || nextObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Array.fromAsync iterator.next is not a function");

        iterator = iteratorObject;
        nextMethod = nextFn;
        return true;
    }

    private sealed class ArrayFromAsyncContinuation(
        JsArrayFromAsyncOperation operation,
        Action<JsRealm, JsArrayFromAsyncOperation, JsValue> onFulfilled)
    {
        public JsArrayFromAsyncOperation Operation { get; } = operation;
        public Action<JsRealm, JsArrayFromAsyncOperation, JsValue> OnFulfilled { get; } = onFulfilled;
    }
}
