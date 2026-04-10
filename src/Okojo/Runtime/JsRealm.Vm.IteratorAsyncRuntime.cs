using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeThrowIteratorResultNotObject(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        ThrowTypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleIntrinsicGeneratorGetResumeMode(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (!realm.TryGetActiveGeneratorForFrame(fp, out var generator))
            ThrowTypeError("GENERATOR_INTRINSIC_NO_ACTIVE", "No active generator for intrinsic mode query");
        acc = JsValue.FromInt32((int)generator.PendingResumeMode);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleIntrinsicGeneratorClearResumeState(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (!realm.TryGetActiveGeneratorForFrame(fp, out var generator))
            ThrowTypeError("GENERATOR_INTRINSIC_NO_ACTIVE", "No active generator for intrinsic state clear");
        generator.PendingResumeMode = GeneratorResumeMode.Next;
        generator.PendingResumeValue = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleIntrinsicGeneratorHasActiveDelegateIterator(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (!realm.TryGetActiveGeneratorForFrame(fp, out var generator))
            ThrowTypeError("GENERATOR_INTRINSIC_NO_ACTIVE", "No active generator for intrinsic delegate query");
        acc = generator.HasActiveDelegateIterator ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeForOfFastPathLength(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("FOROF_FASTPATH_LENGTH_ARGC", "ForOfFastPathLength requires one argument");
        var iterable = Unsafe.Add(ref registers, argRegStart);
        if (iterable.TryGetObject(out var obj) && obj is JsArray array && array.Length <= int.MaxValue)
            acc = JsValue.FromInt32((int)array.Length);
        else
            acc = JsValue.FromInt32(-1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeForOfStepValue(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("FOROF_STEP_VALUE_ARGC", "ForOfStepValue requires one argument");
        var iterable = Unsafe.Add(ref registers, argRegStart);
        acc = realm.ForOfStepValue(iterable);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureArrayAssignment(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount < 1)
            ThrowTypeError("DESTRUCTURE_ARRAY_ASSIGN_ARGC",
                "DestructureArrayAssignment requires at least one argument");

        var source = Unsafe.Add(ref registers, argRegStart);
        acc = realm.RuntimeDestructureArrayAssignment(source,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref registers, argRegStart + 1), argCount - 1));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureArrayAssignmentMemberTargets(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount < 1)
            ThrowTypeError("DESTRUCTURE_ARRAY_TARGETS_ARGC",
                "DestructureArrayAssignmentMemberTargets requires at least one argument");

        var source = Unsafe.Add(ref registers, argRegStart);
        acc = realm.RuntimeDestructureArrayAssignmentMemberTargets(source,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref registers, argRegStart + 1), argCount - 1));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCreateRestParameterFromArrayLike(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("CREATE_REST_FROM_ARRAYLIKE_ARGC",
                "CreateRestParameterFromArrayLike requires arrayLike and startIndex");

        var arrayLike = Unsafe.Add(ref registers, argRegStart);
        var startIndexValue = Unsafe.Add(ref registers, argRegStart + 1);
        var startIndex = startIndexValue.IsInt32 ? startIndexValue.Int32Value : (int)startIndexValue.NumberValue;
        acc = realm.CreateRestParameterFromArrayLike(arrayLike, startIndex);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeWrapSyncIteratorForAsyncDelegate(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("WRAP_SYNC_ITERATOR_FOR_ASYNC_DELEGATE_ARGC",
                "WrapSyncIteratorForAsyncDelegate requires an iterator");

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("WRAP_SYNC_ITERATOR_FOR_ASYNC_DELEGATE_OBJECT", "sync iterator must be an object");

        acc = JsValue.FromObject(new JsAsyncFromSyncIteratorObject(realm, iterator));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeAsyncIteratorClose(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("ASYNC_ITERATOR_CLOSE_ARGC", "AsyncIteratorClose requires an iterator");

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("ASYNC_ITERATOR_CLOSE_OBJECT", "async iterator must be an object");

        acc = realm.AsyncIteratorClose(iterator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeAsyncIteratorCloseBestEffort(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("ASYNC_ITERATOR_CLOSE_BESTEFFORT_ARGC",
                "AsyncIteratorCloseBestEffort requires an iterator");

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("ASYNC_ITERATOR_CLOSE_BESTEFFORT_OBJECT", "async iterator must be an object");

        acc = realm.AsyncIteratorCloseBestEffort(iterator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCreateArrayDestructureIterator(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("DESTRUCTURE_ITERATOR_CREATE_ARGC", "CreateArrayDestructureIterator requires one argument");

        acc = JsValue.FromObject(realm.CreateArrayDestructureIterator(Unsafe.Add(ref registers, argRegStart)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorStepValue(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("DESTRUCTURE_ITERATOR_STEP_ARGC", "DestructureIteratorStepValue requires one iterator");

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("DESTRUCTURE_ITERATOR_STEP_OBJECT", "destructuring iterator must be an object");

        acc = realm.DestructureIteratorStepValue(iterator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorClose(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("DESTRUCTURE_ITERATOR_CLOSE_ARGC", "DestructureIteratorClose requires one iterator");

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("DESTRUCTURE_ITERATOR_CLOSE_OBJECT", "destructuring iterator must be an object");

        realm.DestructureIteratorClose(iterator);
        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorCloseBestEffort(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("DESTRUCTURE_ITERATOR_CLOSE_BESTEFFORT_ARGC",
                "DestructureIteratorCloseBestEffort requires one iterator");

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("DESTRUCTURE_ITERATOR_CLOSE_BESTEFFORT_OBJECT", "destructuring iterator must be an object");

        realm.BestEffortDestructureIteratorClose(iterator);
        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorRestArray(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("DESTRUCTURE_ITERATOR_REST_ARGC", "DestructureIteratorRestArray requires one iterator");

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("DESTRUCTURE_ITERATOR_REST_OBJECT", "destructuring iterator must be an object");

        acc = JsValue.FromObject(realm.DestructureIteratorRestArray(iterator));
    }

    private static void HandleRuntimeGetAsyncIteratorMethod(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("GET_ASYNC_ITERATOR_METHOD_ARGC", "GetAsyncIteratorMethod requires one argument");

        var value = Unsafe.Add(ref registers, argRegStart);
        if (!value.TryGetObject(out var obj))
        {
            acc = JsValue.Undefined;
            return;
        }

        if (obj is JsGeneratorObject { IsAsyncGenerator: true })
        {
            acc = JsValue.FromObject(realm.AsyncIteratorSelfFunction);
            return;
        }

        _ = obj.TryGetPropertyAtom(realm, IdSymbolAsyncIterator, out acc, out _);
    }

    private static void HandleRuntimeGetIteratorMethod(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("GET_ITERATOR_METHOD_ARGC", "GetIteratorMethod requires one argument");

        var value = Unsafe.Add(ref registers, argRegStart);
        if (value.IsString && Intrinsics.TryGetIteratorMethodForPrimitiveString(realm, value, out var stringMethod))
        {
            acc = stringMethod;
            return;
        }

        if (!value.TryGetObject(out var obj))
            if (!realm.TryToObject(value, out obj))
            {
                acc = JsValue.Undefined;
                return;
            }

        if (obj is JsGeneratorObject { IsAsyncGenerator: false })
        {
            acc = JsValue.FromObject(realm.IteratorSelfFunction);
            return;
        }

        _ = obj.TryGetPropertyAtom(realm, IdSymbolIterator, out acc, out _);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue AsyncIteratorClose(JsObject iterator)
    {
        if (!iterator.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _) ||
            returnMethod.IsUndefined || returnMethod.IsNull)
            return JsValue.TheHole;

        if (!returnMethod.TryGetObject(out var fnObj) || fnObj is not JsFunction)
            ThrowTypeError("ITERATOR_RETURN_NOT_FUNCTION", "iterator.return is not a function");
        var returnFn = (JsFunction)fnObj;

        return InvokeFunction(returnFn, iterator, ReadOnlySpan<JsValue>.Empty);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue AsyncIteratorCloseBestEffort(JsObject iterator)
    {
        try
        {
            if (!iterator.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _) ||
                returnMethod.IsUndefined || returnMethod.IsNull)
                return JsValue.TheHole;

            if (!returnMethod.TryGetObject(out var fnObj) || fnObj is not JsFunction returnFn)
                return JsValue.TheHole;

            var closeResult = InvokeFunction(returnFn, iterator, ReadOnlySpan<JsValue>.Empty);
            return CreateAsyncIteratorCloseSuppressionPromise(closeResult);
        }
        catch
        {
            return JsValue.TheHole;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue CreateAsyncIteratorCloseSuppressionPromise(in JsValue closeResult)
    {
        var awaited = Intrinsics.PromiseResolveValue(closeResult);
        if (awaited.State != JsPromiseObject.PromiseState.Pending)
            return JsValue.TheHole;

        var completion = this.CreatePromiseObject();
        var resolveFn = new JsHostFunction(this, static (in info) =>
        {
            var realm = info.Realm;
            var host = (JsHostFunction)info.Function;
            var promise = (JsPromiseObject)host.UserData!;
            realm.ResolvePromise(promise, JsValue.Undefined);
            return JsValue.Undefined;
        }, string.Empty, 1)
        {
            UserData = completion
        };
        var handlerValue = JsValue.FromObject(resolveFn);
        this.PromiseThenNoCapability(awaited, handlerValue, handlerValue);
        return JsValue.FromObject(completion);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsObject CreateArrayDestructureIterator(in JsValue source)
    {
        if (!this.TryToObject(source, out var iterableObj))
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        JsValue iteratorMethod;
        if (iterableObj is JsGeneratorObject { IsAsyncGenerator: false })
            iteratorMethod = JsValue.FromObject(IteratorSelfFunction);
        else if (!iterableObj.TryGetPropertyAtom(this, IdSymbolIterator, out iteratorMethod, out _))
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        var iteratorValue = InvokeFunction(iteratorFn, iterableObj, ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator is not an object");

        return iteratorObj;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue DestructureIteratorStepValue(JsObject iterator)
    {
        var value = DestructureArrayStepValue(iterator, out _);
        return value.IsTheHole ? JsValue.TheHole : value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DestructureIteratorClose(JsObject iterator)
    {
        IteratorCloseForDestructuring(iterator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BestEffortDestructureIteratorClose(JsObject iterator)
    {
        BestEffortIteratorCloseOnThrow(iterator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsArray DestructureIteratorRestArray(JsObject iterator)
    {
        var restArray = CreateArrayObject();
        uint restIndex = 0;
        var done = false;
        while (!done)
        {
            var restValue = DestructureArrayStepValue(iterator, out done);
            if (restValue.IsTheHole)
                break;
            restArray.SetElement(restIndex++, restValue);
        }

        return restArray;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeAwaitPrepare(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("AWAIT_PREPARE_ARGC", "AwaitPrepare requires one argument");
        acc = Unsafe.Add(ref registers, argRegStart);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeAwaitValue(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("AWAIT_VALUE_ARGC", "AwaitValue requires one argument");

        var awaited = Unsafe.Add(ref registers, argRegStart);
        if (!awaited.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
        {
            acc = awaited;
            return;
        }

        while (promise.State == JsPromiseObject.PromiseState.Pending)
        {
            var queuedBefore = realm.Agent.PendingJobCount;
            realm.Agent.PumpJobs();
            if (promise.State != JsPromiseObject.PromiseState.Pending)
                break;
            if (realm.Agent.PendingJobCount == 0 && queuedBefore == 0)
                ThrowTypeError("AWAIT_PENDING_UNSUPPORTED",
                    "await encountered a still-pending promise without runnable jobs");
        }

        if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
        {
            acc = promise.Result;
            return;
        }

        ThrowJsValue(promise.Result);
    }
}
