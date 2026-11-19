using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.JavaScript.Bytecode;

namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeThrowIteratorResultNotObject(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        ThrowTypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleIntrinsicGeneratorGetResumeMode(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (!realm.TryGetActiveGeneratorForFrame(fp, out var generator))
            ThrowTypeError(
                "GENERATOR_INTRINSIC_NO_ACTIVE",
                "No active generator for intrinsic mode query"
            );
        acc = JsValue.FromInt32((int)generator.PendingResumeMode);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleIntrinsicGeneratorClearResumeState(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (!realm.TryGetActiveGeneratorForFrame(fp, out var generator))
            ThrowTypeError(
                "GENERATOR_INTRINSIC_NO_ACTIVE",
                "No active generator for intrinsic state clear"
            );
        generator.PendingResumeMode = GeneratorResumeMode.Next;
        generator.PendingResumeValue = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleIntrinsicGeneratorHasActiveDelegateIterator(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (!realm.TryGetActiveGeneratorForFrame(fp, out var generator))
            ThrowTypeError(
                "GENERATOR_INTRINSIC_NO_ACTIVE",
                "No active generator for intrinsic delegate query"
            );
        acc = generator.HasActiveDelegateIterator ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeForOfIteratorNext(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 3)
            ThrowTypeError(
                "FOROF_ITERATOR_NEXT_ARGC",
                "ForOfIteratorNext requires three registers"
            );
        ref var iteratorValue = ref Unsafe.Add(ref registers, argRegStart);
        var value = realm.AdvanceSyncIterator(ref iteratorValue, true);
        Unsafe.Add(ref iteratorValue, 2) = value;
        acc = value.IsTheHole ? JsValue.False : JsValue.True;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureArrayAssignment(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount < 1)
            ThrowTypeError(
                "DESTRUCTURE_ARRAY_ASSIGN_ARGC",
                "DestructureArrayAssignment requires at least one argument"
            );

        var source = Unsafe.Add(ref registers, argRegStart);
        acc = realm.RuntimeDestructureArrayAssignment(
            source,
            MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.Add(ref registers, argRegStart + 1),
                argCount - 1
            )
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureArrayAssignmentMemberTargets(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount < 1)
            ThrowTypeError(
                "DESTRUCTURE_ARRAY_TARGETS_ARGC",
                "DestructureArrayAssignmentMemberTargets requires at least one argument"
            );

        var source = Unsafe.Add(ref registers, argRegStart);
        acc = realm.RuntimeDestructureArrayAssignmentMemberTargets(
            source,
            MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.Add(ref registers, argRegStart + 1),
                argCount - 1
            )
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCreateRestParameterFromArrayLike(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 2)
            ThrowTypeError(
                "CREATE_REST_FROM_ARRAYLIKE_ARGC",
                "CreateRestParameterFromArrayLike requires arrayLike and startIndex"
            );

        var arrayLike = Unsafe.Add(ref registers, argRegStart);
        var startIndexValue = Unsafe.Add(ref registers, argRegStart + 1);
        var startIndex = startIndexValue.IsInt32
            ? startIndexValue.Int32Value
            : (int)startIndexValue.NumberValue;
        acc = realm.CreateRestParameterFromArrayLike(arrayLike, startIndex);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeWrapSyncIteratorForAsyncDelegate(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 1)
            ThrowTypeError(
                "WRAP_SYNC_ITERATOR_FOR_ASYNC_DELEGATE_ARGC",
                "WrapSyncIteratorForAsyncDelegate requires an iterator"
            );

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError(
                "WRAP_SYNC_ITERATOR_FOR_ASYNC_DELEGATE_OBJECT",
                "sync iterator must be an object"
            );

        acc = JsValue.FromObject(new JsAsyncFromSyncIteratorObject(realm, iterator));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeAsyncIteratorClose(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
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
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 1)
            ThrowTypeError(
                "ASYNC_ITERATOR_CLOSE_BESTEFFORT_ARGC",
                "AsyncIteratorCloseBestEffort requires an iterator"
            );

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError(
                "ASYNC_ITERATOR_CLOSE_BESTEFFORT_OBJECT",
                "async iterator must be an object"
            );

        acc = realm.AsyncIteratorCloseBestEffort(iterator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCreateSyncIteratorRecord(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 2)
            ThrowTypeError(
                "ITERATOR_RECORD_CREATE_ARGC",
                "CreateSyncIteratorRecord requires two registers"
            );

        ref var iterator = ref Unsafe.Add(ref registers, argRegStart);
        var iteratorObject = realm.GetSyncIterator(iterator, out var nextMethod);
        iterator = JsValue.FromObject(iteratorObject);
        Unsafe.Add(ref iterator, 1) = nextMethod;
        acc = iterator;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorStepValue(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 2)
            ThrowTypeError(
                "DESTRUCTURE_ITERATOR_STEP_ARGC",
                "DestructureIteratorStepValue requires an iterator record"
            );

        var value = realm.AdvanceSyncIterator(ref Unsafe.Add(ref registers, argRegStart), true);
        acc = value.IsTheHole ? JsValue.Undefined : value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorSkip(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 2)
            ThrowTypeError(
                "DESTRUCTURE_ITERATOR_STEP_ARGC",
                "DestructureIteratorSkip requires an iterator record"
            );

        _ = realm.AdvanceSyncIterator(ref Unsafe.Add(ref registers, argRegStart), false);
        acc = JsValue.Undefined;
    }

    private JsValue AdvanceSyncIterator(ref JsValue iteratorValue, bool readValue)
    {
        ref var nextSlot = ref Unsafe.Add(ref iteratorValue, 1);
        var nextMethod = nextSlot;
        if (nextMethod.IsTheHole)
            return JsValue.TheHole;

        if (nextMethod.IsInt32)
            return StepArrayIteratorRecord(
                (JsArray)iteratorValue.Obj!,
                ref nextSlot,
                unchecked((uint)nextMethod.Int32Value)
            );

        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError(
                "DESTRUCTURE_ITERATOR_STEP_OBJECT",
                "destructuring iterator must be an object"
            );

        nextSlot = JsValue.TheHole;
        var value = DestructureArrayStepValue(iterator, nextMethod, out var done, readValue);
        if (!done)
            nextSlot = nextMethod;
        return value;
    }

    private static JsValue StepArrayIteratorRecord(JsArray array, ref JsValue nextSlot, uint index)
    {
        if (index >= array.Length)
        {
            nextSlot = JsValue.TheHole;
            return JsValue.TheHole;
        }

        if (!array.TryGetDenseElement(index, out var value))
        {
            nextSlot = JsValue.TheHole;
            if (!Intrinsics.TryGetArrayLikeIndex(array.Realm, array, index, out value))
                value = JsValue.Undefined;
        }

        nextSlot = JsValue.FromInt32(unchecked((int)(index + 1)));
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorClose(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 2)
            ThrowTypeError(
                "DESTRUCTURE_ITERATOR_CLOSE_ARGC",
                "DestructureIteratorClose requires an iterator"
            );

        acc = JsValue.Undefined;
        if (realm.GetIteratorForClose(ref Unsafe.Add(ref registers, argRegStart)) is { } iterator)
            realm.IteratorCloseForDestructuring(iterator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorCloseBestEffort(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 2)
            ThrowTypeError(
                "DESTRUCTURE_ITERATOR_CLOSE_BESTEFFORT_ARGC",
                "DestructureIteratorCloseBestEffort requires an iterator"
            );

        acc = JsValue.Undefined;
        if (realm.GetIteratorForClose(ref Unsafe.Add(ref registers, argRegStart)) is { } iterator)
            realm.BestEffortIteratorCloseOnThrow(iterator);
    }

    private JsObject? GetIteratorForClose(ref JsValue iteratorValue)
    {
        var next = Unsafe.Add(ref iteratorValue, 1);
        if (next.IsTheHole)
            return null;
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError("ITERATOR_RECORD_NOT_OBJECT", "Iterator record must contain an object");

        // Return can be installed after acquisition, and its receiver can escape.
        return next.IsInt32
            ? new JsArrayIteratorObject(
                this,
                iterator,
                JsArrayIteratorObject.IterationKind.Values,
                unchecked((uint)next.Int32Value)
            )
            : iterator;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDestructureIteratorRestArray(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 2)
            ThrowTypeError(
                "DESTRUCTURE_ITERATOR_REST_ARGC",
                "DestructureIteratorRestArray requires an iterator record"
            );

        var iteratorValue = Unsafe.Add(ref registers, argRegStart);
        if (!iteratorValue.TryGetObject(out var iterator))
            ThrowTypeError(
                "DESTRUCTURE_ITERATOR_REST_OBJECT",
                "destructuring iterator must be an object"
            );

        ref var nextSlot = ref Unsafe.Add(ref registers, argRegStart + 1);
        var nextMethod = nextSlot;
        nextSlot = JsValue.TheHole;
        acc = JsValue.FromObject(realm.DestructureIteratorRestArray(iterator, nextMethod));
    }

    private static void HandleRuntimeGetAsyncIteratorMethod(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 1)
            ThrowTypeError(
                "GET_ASYNC_ITERATOR_METHOD_ARGC",
                "GetAsyncIteratorMethod requires one argument"
            );

        acc = realm.GetIteratorMethod(
            Unsafe.Add(ref registers, argRegStart),
            IdSymbolAsyncIterator
        );
    }

    private static void HandleRuntimeGetIteratorMethod(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 1)
            ThrowTypeError("GET_ITERATOR_METHOD_ARGC", "GetIteratorMethod requires one argument");

        acc = realm.GetIteratorMethod(Unsafe.Add(ref registers, argRegStart), IdSymbolIterator);
    }

    private JsValue GetIteratorMethod(in JsValue value, int symbol)
    {
        JsObject obj;
        if (value.IsString)
            obj = StringPrototype;
        else if (!this.TryToObject(value, out obj!))
            return JsValue.Undefined;

        return obj.TryGetPropertyAtomWithReceiverValue(this, value, symbol, out var method, out _)
            ? method
            : JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue AsyncIteratorClose(JsObject iterator)
    {
        if (
            !iterator.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _)
            || returnMethod.IsUndefined
            || returnMethod.IsNull
        )
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
            if (
                !iterator.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _)
                || returnMethod.IsUndefined
                || returnMethod.IsNull
            )
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
        var resolveFn = new JsHostFunction(
            this,
            static (in info) =>
            {
                var realm = info.Realm;
                var host = (JsHostFunction)info.Function;
                var promise = (JsPromiseObject)host.UserData!;
                realm.ResolvePromise(promise, JsValue.Undefined);
                return JsValue.Undefined;
            },
            string.Empty,
            1
        )
        {
            UserData = completion,
        };
        var handlerValue = JsValue.FromObject(resolveFn);
        this.PromiseThenNoCapability(awaited, handlerValue, handlerValue);
        return JsValue.FromObject(completion);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsObject GetSyncIterator(JsValue source, out JsValue nextMethod)
    {
        var iteratorMethod = GetIteratorMethod(source, IdSymbolIterator);
        if (
            !iteratorMethod.TryGetObject(out var iteratorMethodObj)
            || iteratorMethodObj is not JsFunction iteratorFn
        )
            throw TypeError("VALUE_NOT_ITERABLE", "Value is not iterable");

        // An unobservable built-in array iterator is represented by its source and index.
        if (
            source.Obj is JsArray array
            && ReferenceEquals(iteratorFn, ArrayPrototypeValuesFunction)
            && ArrayIteratorPrototype.TryGetOwnNamedDataSlotIndex(IdNext, out var nextSlot)
            && ArrayIteratorPrototype.GetNamedSlotUnchecked(nextSlot).Obj is JsHostFunction host
            && ReferenceEquals(host.BodyField, Intrinsics.ArrayIteratorNextBody)
        )
        {
            nextMethod = JsValue.FromInt32(0);
            return array;
        }

        var iteratorValue = InvokeFunction(iteratorFn, source, ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator is not an object");

        if (
            !iteratorObj.TryGetPropertyAtom(this, IdNext, out nextMethod, out _)
            || nextMethod.IsInt32
        )
            nextMethod = JsValue.Undefined;
        return iteratorObj;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsArray DestructureIteratorRestArray(JsObject iterator, JsValue nextMethod)
    {
        var restArray = CreateArrayObject();
        if (nextMethod.IsTheHole)
            return restArray;

        uint restIndex = 0;
        if (nextMethod.IsInt32)
        {
            var array = (JsArray)iterator;
            for (var index = unchecked((uint)nextMethod.Int32Value); index < array.Length; index++)
            {
                if (
                    !array.TryGetDenseElement(index, out var item)
                    && !Intrinsics.TryGetArrayLikeIndex(this, array, index, out item)
                )
                    item = JsValue.Undefined;
                restArray.InitializeLiteralElement(restIndex++, item);
            }
            return restArray;
        }

        while (true)
        {
            var restValue = DestructureArrayStepValue(iterator, nextMethod, out var done);
            if (done)
                break;
            FreshArrayOperations.DefineElement(restArray, restIndex++, restValue);
        }

        return restArray;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeAwaitPrepare(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
    {
        if (argCount != 1)
            ThrowTypeError("AWAIT_PREPARE_ARGC", "AwaitPrepare requires one argument");
        acc = Unsafe.Add(ref registers, argRegStart);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeAwaitValue(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc
    )
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
                ThrowTypeError(
                    "AWAIT_PENDING_UNSUPPORTED",
                    "await encountered a still-pending promise without runnable jobs"
                );
        }

        if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
        {
            acc = promise.Result;
            return;
        }

        ThrowJsValue(promise.Result);
    }
}
