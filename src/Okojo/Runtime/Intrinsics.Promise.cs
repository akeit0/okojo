using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private const int PromiseWithResolversPromiseSlot = 0;
    private const int PromiseWithResolversResolveSlot = 1;
    private const int PromiseWithResolversRejectSlot = 2;

    private static readonly Action<object?> SExecuteThenableResolveJob = static state =>
    {
        var job = (PromiseThenableResolveJobState)state!;
        try
        {
            var inlineArray = new InlineJsValueArray2
            {
                Item0 = job.Resolve,
                Item1 = job.Reject
            };
            var args = inlineArray.AsSpan();
            if (job.ThenFunction is JsHostFunction host)
                job.Realm.InvokeHostFunctionWithExitFrame(host, job.Resolution, args, 0, JsValue.Undefined);
            if (job.ThenFunction is JsBytecodeFunction bytecodeFunc)
                job.Realm.InvokeBytecodeFunction(bytecodeFunc, job.Resolution, args, JsValue.Undefined);
        }
        catch (JsRuntimeException ex)
        {
            if (job.TargetHolder.Value != null)
                job.Realm.RejectPromise((JsPromiseObject)job.Target, job.Realm.GetPromiseAbruptReason(ex));
        }
    };

    private static readonly Action<object?> SUnhandledRejectionCheckJob = static state =>
    {
        var promise = (JsPromiseObject)state!;
        promise.Realm.Intrinsics.ExecuteUnhandledRejectionCheckJob(promise);
    };

    private static readonly Action<object?> SResumeAsyncDriverJob = static state =>
    {
        var job = (GeneratorResumeJobState)state!;
        job.Realm.StartOrResumeAsyncDriver(job.Generator, job.Mode, job.Value);
    };

    private static readonly Action<object?> SExecutePromiseReactionJob = static state =>
    {
        var reaction = (JsPromiseObject.Reaction)state!;
        var sourcePromise = reaction.SourcePromise!;
        reaction.SourcePromise = null;
        sourcePromise.Realm.Intrinsics.ExecutePromiseReactionJob(sourcePromise, reaction);
    };

    private static readonly Action<object?> SExecuteImmediatePromiseHandlerJob = static state =>
    {
        var job = (ImmediatePromiseHandlerJobState)state!;
        job.Realm.Intrinsics.ExecuteFireAndForgetHandlerReaction(
            new(job.OnFulfilled, job.OnRejected),
            JsPromiseObject.PromiseState.Fulfilled,
            job.Value);
    };

    private JsHostFunction promiseResolveFunction = null!;
    private JsHostFunction promiseThenFunction = null!;
    private StaticNamedPropertyLayout? promiseWithResolversResultShape;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsPromiseObject CreatePromiseObject()
    {
        var promise = new JsPromiseObject(Realm)
        {
            Prototype = PromisePrototype
        };
        return promise;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsPromiseObject CreatePromiseObject(JsObject prototype)
    {
        var promise = new JsPromiseObject(Realm)
        {
            Prototype = prototype
        };
        return promise;
    }

    private void CreateBuiltinPromiseSettlingFunctions(
        JsPromiseObject promise,
        out JsHostFunction resolve,
        out JsHostFunction reject)
    {
        var settleState = new PromiseSettledState(promise);

        resolve = new(Realm, (in info) =>
        {
            var innerVm = info.Realm;
            var innerArgs = info.Arguments;
            var f = (JsHostFunction)info.Function;
            var state = (PromiseSettledState)f.UserData!;
            if (state.AlreadyResolved)
                return JsValue.Undefined;
            state.AlreadyResolved = true;
            innerVm.Intrinsics.ResolvePromiseWithAssimilation(state.Promise,
                innerArgs.Length > 0 ? innerArgs[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }, string.Empty, 1);
        resolve.UserData = settleState;

        reject = new(Realm, (in info) =>
        {
            var innerVm = info.Realm;
            var innerArgs = info.Arguments;
            var f = (JsHostFunction)info.Function;
            var state = (PromiseSettledState)f.UserData!;
            if (state.AlreadyResolved)
                return JsValue.Undefined;
            state.AlreadyResolved = true;
            innerVm.Intrinsics.RejectPromise(state.Promise,
                innerArgs.Length > 0 ? innerArgs[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }, string.Empty, 1);
        reject.UserData = settleState;
    }

    private JsHostFunction CreatePromiseConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var callee = (JsHostFunction)info.Function;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise constructor requires 'new'");
            if (args.Length == 0 || !args[0].TryGetObject(out var execObj) || execObj is not JsFunction executor)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise resolver is not a function",
                    "PROMISE_RESOLVER_NOT_FUNCTION");

            var promise = CreatePromiseObject(
                GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, callee.Realm.PromisePrototype));
            CreateBuiltinPromiseSettlingFunctions(promise, out var resolve, out var reject);
            var settleState = (PromiseSettledState)resolve.UserData!;

            var resolveArgs = new InlineJsValueArray2
                { Item0 = resolve, Item1 = reject };
            try
            {
                realm.InvokeFunction(executor, JsValue.Undefined, resolveArgs.AsSpan());
            }
            catch (JsRuntimeException ex)
            {
                if (!settleState.AlreadyResolved)
                    realm.Intrinsics.RejectPromise(promise, realm.Intrinsics.GetPromiseAbruptReason(ex));
            }

            return promise;
        }, "Promise", 1, true);
    }

    private void InstallPromiseConstructorBuiltins()
    {
        var thenFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Promise.prototype.then called on incompatible receiver",
                    "PROMISE_THEN_BAD_RECEIVER");

            var onFulfilled = args.Length > 0 ? args[0] : JsValue.Undefined;
            var onRejected = args.Length > 1 ? args[1] : JsValue.Undefined;
            return realm.Intrinsics.PromiseThen(promise, onFulfilled, onRejected);
        }, "then", 2);

        var catchFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var onRejected = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.InvokePromiseThen(thisValue, JsValue.Undefined, onRejected);
        }, "catch", 1);

        var finallyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var onFinally = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.PromiseFinally(info.ThisValue, onFinally);
        }, "finally", 1);

        var resolveFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise.resolve requires a constructor receiver");
            var args = info.Arguments;
            var value = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.PromiseResolveByConstructor(ctor, value);
        }, "resolve", 1);
        promiseResolveFunction = resolveFn;

        var rejectFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise.reject requires a constructor receiver");
            var args = info.Arguments;
            var value = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.PromiseRejectByConstructor(ctor, value);
        }, "reject", 1);

        var tryFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise.try requires a constructor receiver");

            var args = info.Arguments;
            var callback = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (!callback.TryGetObject(out var callbackObj) || callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise.try callback is not a function");

            var capability = realm.CreatePromiseCapability(ctor);
            try
            {
                var callbackArgs = args.Length > 1 ? args[1..] : ReadOnlySpan<JsValue>.Empty;
                var value = realm.InvokeFunction(callbackFn, JsValue.Undefined, callbackArgs);
                realm.Intrinsics.ResolvePromiseCapability(capability, value);
            }
            catch (JsRuntimeException ex)
            {
                realm.Intrinsics.RejectPromiseCapability(capability,
                    ex.ThrownValue ?? realm.CreateErrorObjectFromException(ex));
            }

            return JsValue.FromObject(capability.Promise);
        }, "try", 1);

        var raceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise.race requires a constructor receiver");

            var args = info.Arguments;
            var iterable = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.PromiseRace(ctor, iterable);
        }, "race", 1);
        var allFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise.all requires a constructor receiver");
            var args = info.Arguments;
            var iterable = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.PromiseAll(ctor, iterable);
        }, "all", 1);
        var anyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Promise.any requires a constructor receiver");
            var args = info.Arguments;
            var iterable = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.PromiseAny(ctor, iterable);
        }, "any", 1);
        var allSettledFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Promise.allSettled requires a constructor receiver");
            var args = info.Arguments;
            var iterable = args.Length > 0 ? args[0] : JsValue.Undefined;
            return realm.Intrinsics.PromiseAllSettled(ctor, iterable);
        }, "allSettled", 1);

        var withResolversFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction ctor || !ctor.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Promise.withResolvers requires a constructor receiver");

            var capability = realm.Intrinsics.CreatePromiseCapabilityWithFunctions(ctor);
            var resolveWrapper = new JsHostFunction(realm, static (in forwardInfo) =>
            {
                var realm = forwardInfo.Realm;
                var target = (JsFunction)((JsHostFunction)forwardInfo.Function).UserData!;
                var value = forwardInfo.Arguments.Length > 0 ? forwardInfo.Arguments[0] : JsValue.Undefined;
                return realm.InvokeFunction(target, JsValue.Undefined, MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            }, string.Empty, 1)
            {
                UserData = capability.Resolve!
            };
            var rejectWrapper = new JsHostFunction(realm, static (in forwardInfo) =>
            {
                var realm = forwardInfo.Realm;
                var target = (JsFunction)((JsHostFunction)forwardInfo.Function).UserData!;
                var value = forwardInfo.Arguments.Length > 0 ? forwardInfo.Arguments[0] : JsValue.Undefined;
                return realm.InvokeFunction(target, JsValue.Undefined, MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            }, string.Empty, 1)
            {
                UserData = capability.Reject!
            };
            return realm.Intrinsics.CreatePromiseWithResolversResult(capability.Promise, resolveWrapper, rejectWrapper);
        }, "withResolvers", 0);

        var speciesGetter =
            new JsHostFunction(Realm, (in info) => { return info.ThisValue; }, "get [Symbol.species]", 0);
        promiseThenFunction = thenFn;

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdThen, thenFn),
            PropertyDefinition.Mutable(IdCatch, catchFn),
            PropertyDefinition.Mutable(IdConstructor, PromiseConstructor),
            PropertyDefinition.Mutable(IdFinally, finallyFn),
            PropertyDefinition.Const(IdSymbolToStringTag, "Promise", configurable: true)
        ];
        PromisePrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);

        Span<PropertyDefinition> ctorDefs =
        [
            PropertyDefinition.Mutable(IdResolve, resolveFn),
            PropertyDefinition.Mutable(IdReject, rejectFn),
            PropertyDefinition.GetterData(IdSymbolSpecies, speciesGetter, configurable: true),
            PropertyDefinition.Mutable(IdTry, tryFn),
            PropertyDefinition.Mutable(IdRace, raceFn),
            PropertyDefinition.Mutable(IdAll, allFn),
            PropertyDefinition.Mutable(IdAny, anyFn),
            PropertyDefinition.Mutable(IdAllSettled, allSettledFn),
            PropertyDefinition.Mutable(IdWithResolvers, withResolversFn)
        ];
        PromiseConstructor.InitializePrototypeProperty(PromisePrototype);
        PromiseConstructor.DefineNewPropertiesNoCollision(Realm, ctorDefs);
    }

    internal JsPromiseObject PromiseResolveValue(in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsPromiseObject promise)
            return promise;

        var wrapped = CreatePromiseObject();
        ResolvePromiseWithAssimilation(wrapped, value);
        return wrapped;
    }

    private JsPlainObject CreatePromiseWithResolversResult(
        JsObject promise,
        JsHostFunction resolve,
        JsHostFunction reject)
    {
        var shape = promiseWithResolversResultShape ??= CreatePromiseWithResolversResultShape();
        var result = new JsPlainObject(shape);
        result.SetNamedSlotUnchecked(PromiseWithResolversPromiseSlot, JsValue.FromObject(promise));
        result.SetNamedSlotUnchecked(PromiseWithResolversResolveSlot, JsValue.FromObject(resolve));
        result.SetNamedSlotUnchecked(PromiseWithResolversRejectSlot, JsValue.FromObject(reject));
        return result;
    }

    private StaticNamedPropertyLayout CreatePromiseWithResolversResultShape()
    {
        var shape = Realm.EmptyShape.GetOrAddTransition(IdPromise, JsShapePropertyFlags.Open, out var promiseInfo);
        shape = shape.GetOrAddTransition(IdResolve, JsShapePropertyFlags.Open, out var resolveInfo);
        shape = shape.GetOrAddTransition(IdReject, JsShapePropertyFlags.Open, out var rejectInfo);
        Debug.Assert(promiseInfo.Slot == PromiseWithResolversPromiseSlot);
        Debug.Assert(resolveInfo.Slot == PromiseWithResolversResolveSlot);
        Debug.Assert(rejectInfo.Slot == PromiseWithResolversRejectSlot);
        return shape;
    }

    internal JsValue CreateAsyncFromSyncIteratorResultPromise(
        JsObject resultObject,
        JsObject? iteratorToClose = null,
        bool closeOnRejection = false)
    {
        _ = resultObject.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
        var done = doneValue.ToBoolean();
        if (!resultObject.TryGetPropertyAtom(Realm, IdValue, out var value, out _))
            value = JsValue.Undefined;
        return CreateAsyncFromSyncIteratorResultPromise(value, done, iteratorToClose, closeOnRejection);
    }

    internal JsValue CreateAsyncFromSyncIteratorResultPromise(in JsValue value, bool done)
    {
        return CreateAsyncFromSyncIteratorResultPromise(value, done, null, false);
    }

    internal JsValue CreateAsyncFromSyncIteratorResultPromise(
        in JsValue value,
        bool done,
        JsObject? iteratorToClose,
        bool closeOnRejection)
    {
        var promise = CreatePromiseObject();
        JsValue valuePromiseValue;
        try
        {
            valuePromiseValue = PromiseResolveByConstructor(PromiseConstructor, value);
        }
        catch
        {
            if (!done && closeOnRejection && iteratorToClose is not null)
                Realm.BestEffortIteratorCloseOnThrow(iteratorToClose);
            throw;
        }

        if (!valuePromiseValue.TryGetObject(out var valuePromiseObj) ||
            valuePromiseObj is not JsPromiseObject valuePromise)
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "PromiseResolve(%Promise%, value) must produce a promise object");
        valuePromise.IsHandled = true;

        var reaction = JsPromiseObject.Reaction.CreateAsyncFromSyncIteratorResult(
            new(promise, done, iteratorToClose, closeOnRejection));
        if (valuePromise.State == JsPromiseObject.PromiseState.Pending)
            valuePromise.AddReaction(reaction);
        else
            EnqueuePromiseReactionJob(valuePromise, reaction);

        return JsValue.FromObject(promise);
    }

    internal JsPromiseObject.PromiseCapability CreatePromiseCapability(JsFunction ctor)
    {
        if (ReferenceEquals(ctor, PromiseConstructor))
            return new(CreatePromiseObject());

        var state = new StrongBox<PromiseCapabilityState>(new(null, null, false));
        var executor = new JsHostFunction(Realm, static (in info) =>
        {
            var host = (JsHostFunction)info.Function;
            var state = (StrongBox<PromiseCapabilityState>)host.UserData!;
            var args = info.Arguments;
            if (state.Value.HasDefinedEntries)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Promise capability executor called more than once");

            var resolve = args.Length > 0 && args[0].TryGetObject(out var resolveObj) ? resolveObj as JsFunction : null;
            var reject = args.Length > 1 && args[1].TryGetObject(out var rejectObj) ? rejectObj as JsFunction : null;
            var hasDefinedEntries =
                (args.Length > 0 && !args[0].IsUndefined) || (args.Length > 1 && !args[1].IsUndefined);
            state.Value = new(resolve, reject, hasDefinedEntries);
            return JsValue.Undefined;
        }, string.Empty, 2)
        {
            UserData = state
        };

        var arg0 = JsValue.FromObject(executor);
        var promise = Realm.ConstructWithExplicitNewTarget(ctor, MemoryMarshal.CreateReadOnlySpan(ref arg0, 1),
            JsValue.FromObject(ctor), 0);
        var capabilityState = state.Value with { };
        state.Value = capabilityState;

        if (capabilityState.Resolve is null || capabilityState.Reject is null)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Promise capability executor did not provide callables");

        if (!promise.TryGetObject(out var promiseObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Promise capability constructor must return an object");

        return new(promiseObj, capabilityState.Resolve, capabilityState.Reject);
    }

    private JsPromiseObject.PromiseCapability CreatePromiseCapabilityWithFunctions(JsFunction ctor)
    {
        if (!ReferenceEquals(ctor, PromiseConstructor))
            return CreatePromiseCapability(ctor);

        var promise = CreatePromiseObject();
        CreateBuiltinPromiseSettlingFunctions(promise, out var resolve, out var reject);
        return new(promise, resolve, reject);
    }

    private JsFunction GetPromiseResolveFunction(JsFunction ctor)
    {
        if (!ctor.TryGetPropertyByAtom(IdResolve, out var resolveValue) ||
            !resolveValue.TryGetObject(out var resolveObj) ||
            resolveObj is not JsFunction resolveFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Promise resolve is not callable");

        return resolveFn;
    }

    internal JsValue GetPromiseAbruptReason(JsRuntimeException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is JsRuntimeException runtime && runtime.ThrownValue is { } thrownValue)
                return thrownValue;

        return Realm.CreateErrorObjectFromException(ex);
    }

    internal JsValue PromiseResolveByConstructor(JsFunction ctor, in JsValue value)
    {
        if (value.TryGetObject(out var valueObj) && valueObj is JsPromiseObject)
            if (valueObj.TryGetPropertyAtom(Realm, IdConstructor, out var constructorValue, out _) &&
                constructorValue.TryGetObject(out var constructorObj) &&
                ReferenceEquals(constructorObj, ctor))
                return value;

        if (ReferenceEquals(ctor, PromiseConstructor))
        {
            var wrapped = CreatePromiseObject();
            ResolvePromiseWithAssimilation(wrapped, value);
            return wrapped;
        }

        var capability = CreatePromiseCapability(ctor);
        ResolvePromiseCapability(capability, value);
        return JsValue.FromObject(capability.Promise);
    }

    internal JsValue PromiseRejectByConstructor(JsFunction ctor, in JsValue value)
    {
        var capability = CreatePromiseCapability(ctor);
        RejectPromiseCapability(capability, value);
        return JsValue.FromObject(capability.Promise);
    }

    private JsFunction GetPromiseSpeciesConstructor(JsObject promise)
    {
        if (!promise.TryGetPropertyAtom(Realm, IdConstructor, out var ctorValue, out _) || ctorValue.IsUndefined)
            return PromiseConstructor;
        if (ctorValue.IsNull)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Promise constructor must be an object");
        if (!ctorValue.TryGetObject(out var ctorObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Promise constructor must be an object");
        if (ctorObj.TryGetPropertyAtom(Realm, IdSymbolSpecies, out var speciesValue, out _))
        {
            if (speciesValue.IsUndefined || speciesValue.IsNull)
                return PromiseConstructor;
            if (speciesValue.TryGetObject(out var speciesObj) && speciesObj is JsFunction speciesCtor &&
                speciesCtor.IsConstructor)
                return speciesCtor;
            throw new JsRuntimeException(JsErrorKind.TypeError, "Promise [Symbol.species] must be a constructor");
        }

        if (ctorObj is JsFunction ctorFn && ctorFn.IsConstructor)
            return ctorFn;
        throw new JsRuntimeException(JsErrorKind.TypeError, "Promise constructor must be a constructor");
    }

    private JsValue InvokePromiseThen(in JsValue thisValue, in JsValue onFulfilled, in JsValue onRejected)
    {
        if (!Realm.TryToObject(thisValue, out var promiseObj))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Promise.prototype.catch called on incompatible receiver",
                "PROMISE_CATCH_BAD_RECEIVER");
        if (!promiseObj.TryGetPropertyAtom(Realm, IdThen, out var thenValue, out _)
            || !thenValue.TryGetObject(out var thenObj)
            || thenObj is not JsFunction thenFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Promise receiver does not provide a callable then");

        var inlineArgs = new InlineJsValueArray2
        {
            Item0 = onFulfilled,
            Item1 = onRejected
        };
        return Realm.InvokeFunction(thenFn, thisValue, inlineArgs.AsSpan());
    }

    private JsValue PromiseFinally(in JsValue thisValue, in JsValue onFinally)
    {
        JsFunction constructor = PromiseConstructor;
        if (thisValue.TryGetObject(out var obj))
            constructor = GetPromiseSpeciesConstructor(obj);

        if (!onFinally.TryGetObject(out var onFinallyObj) || onFinallyObj is not JsFunction onFinallyFn)
            return InvokePromiseThen(thisValue, onFinally, onFinally);

        var finallyState = new PromiseFinallyState(constructor, onFinallyFn);
        var thenFinally = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var host = (JsHostFunction)info.Function;
            var state = (PromiseFinallyState)host.UserData!;
            var value = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            var result = realm.InvokeFunction(state.Callback, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
            var promise = realm.Intrinsics.PromiseResolveByConstructor(state.Constructor, result);
            if (!promise.TryGetObject(out var promiseObj) || !promiseObj.TryGetPropertyAtom(realm, IdThen,
                                                              out var thenValue, out _)
                                                          || !thenValue.TryGetObject(out var thenObj) ||
                                                          thenObj is not JsFunction thenFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Promise.prototype.finally expected a thenable result");

            var valueThunk = new JsHostFunction(realm, static (in thunkInfo) =>
            {
                var thunkHost = (JsHostFunction)thunkInfo.Function;
                return (JsValue)thunkHost.UserData!;
            }, string.Empty, 0)
            {
                UserData = value
            };
            var arg0 = JsValue.FromObject(valueThunk);
            return realm.InvokeFunction(thenFn, promise, MemoryMarshal.CreateReadOnlySpan(ref arg0, 1));
        }, string.Empty, 1)
        {
            UserData = finallyState
        };

        var catchFinally = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var host = (JsHostFunction)info.Function;
            var state = (PromiseFinallyState)host.UserData!;
            var reason = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            var result = realm.InvokeFunction(state.Callback, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
            var promise = realm.Intrinsics.PromiseResolveByConstructor(state.Constructor, result);
            if (!promise.TryGetObject(out var promiseObj) || !promiseObj.TryGetPropertyAtom(realm, IdThen,
                                                              out var thenValue, out _)
                                                          || !thenValue.TryGetObject(out var thenObj) ||
                                                          thenObj is not JsFunction thenFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Promise.prototype.finally expected a thenable result");

            var thrower = new JsHostFunction(realm, static (in thunkInfo) =>
            {
                var thunkHost = (JsHostFunction)thunkInfo.Function;
                var reason = (JsValue)thunkHost.UserData!;
                throw new JsRuntimeException(JsErrorKind.InternalError, "Promise finally rejection passthrough",
                    "PROMISE_FINALLY_RETHROW", reason);
            }, string.Empty, 0)
            {
                UserData = reason
            };
            var arg0 = JsValue.FromObject(thrower);
            return realm.InvokeFunction(thenFn, promise, MemoryMarshal.CreateReadOnlySpan(ref arg0, 1));
        }, string.Empty, 1)
        {
            UserData = finallyState
        };

        var args = new InlineJsValueArray2
        {
            Item0 = JsValue.FromObject(thenFinally),
            Item1 = JsValue.FromObject(catchFinally)
        };
        return InvokePromiseThen(thisValue, args.Item0, args.Item1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void ResolvePromiseWithAssimilation(JsPromiseObject target, in JsValue resolution)
    {
        if (!resolution.TryGetObject(out var resolutionObj))
        {
            ResolvePromise(target, resolution);
            return;
        }

        if (resolutionObj is JsPromiseObject resolvedPromise &&
            ReferenceEquals(resolutionObj.Prototype, PromisePrototype) &&
            !resolutionObj.TryGetOwnNamedPropertyDescriptorAtom(Realm, IdThen, out _))
        {
            if (ReferenceEquals(resolvedPromise, target))
            {
                RejectPromise(target, Realm.CreateErrorObjectFromException(new(
                    JsErrorKind.TypeError,
                    "Chaining cycle detected for promise",
                    "PROMISE_CHAIN_CYCLE")));
                return;
            }

            PromiseThenAssimilate(resolvedPromise, target);
            return;
        }

        JsValue thenValue;
        try
        {
            if (!resolutionObj.TryGetPropertyAtom(Realm, IdThen, out thenValue, out _))
            {
                ResolvePromise(target, resolution);
                return;
            }
        }
        catch (JsRuntimeException ex)
        {
            RejectPromise(target, GetPromiseAbruptReason(ex));
            return;
        }

        if (!thenValue.TryGetObject(out var thenObj) || thenObj is not JsFunction thenFn)
        {
            ResolvePromise(target, resolution);
            return;
        }

        var targetHolder = new StrongBox<JsPromiseObject?>(target);

        var resolveFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var f = (JsHostFunction)info.Function;
            if (f.UserData is not StrongBox<JsPromiseObject?> holder)
                return JsValue.Undefined;
            var target = holder.Value;
            if (target == null)
            {
                f.UserData = null;
                return JsValue.Undefined;
            }

            holder.Value = null;
            f.UserData = null;
            realm.Intrinsics.ResolvePromiseWithAssimilation(target, args.Length != 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }, "", 1)
        {
            UserData = targetHolder
        };
        var rejectFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var f = (JsHostFunction)info.Function;
            if (f.UserData is not StrongBox<JsPromiseObject?> holder)
                return JsValue.Undefined;
            var target = holder.Value;
            if (target == null)
            {
                f.UserData = null;
                return JsValue.Undefined;
            }

            holder.Value = null;
            f.UserData = null;
            realm.Intrinsics.RejectPromise(target, args.Length != 0 ? args[0] : JsValue.Undefined);
            return JsValue.Undefined;
        }, "", 1)
        {
            UserData = targetHolder
        };
        var resolutionValue = resolution;

        Realm.Agent.EnqueueMicrotask(SExecuteThenableResolveJob,
            new PromiseThenableResolveJobState(Realm, resolutionValue, target, thenFn, resolveFn, rejectFn,
                targetHolder));
    }

    internal void ResolvePromise(JsPromiseObject promise, in JsValue value)
    {
        if (!promise.TrySettle(JsPromiseObject.PromiseState.Fulfilled, value))
            return;
        SchedulePromiseReactions(promise);
    }

    internal void RejectPromise(JsPromiseObject promise, in JsValue reason)
    {
        if (!promise.TrySettle(JsPromiseObject.PromiseState.Rejected, reason))
            return;
        SchedulePromiseReactions(promise);
        if (!promise.IsHandled) Realm.Agent.EnqueueMicrotask(SUnhandledRejectionCheckJob, promise);
    }

    internal JsValue PromiseThen(JsPromiseObject promise, in JsValue onFulfilled, in JsValue onRejected)
    {
        promise.IsHandled = true;
        var capability = CreatePromiseCapability(GetPromiseSpeciesConstructor(promise));
        var reaction = new JsPromiseObject.Reaction(onFulfilled, onRejected, capability);
        if (promise.State == JsPromiseObject.PromiseState.Pending)
            promise.AddReaction(reaction);
        else
            EnqueuePromiseReactionJob(promise, reaction);

        return JsValue.FromObject(capability.Promise);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PromiseThenAssimilate(JsPromiseObject promise, JsPromiseObject targetPromise)
    {
        promise.IsHandled = true;
        var reaction = new JsPromiseObject.Reaction(targetPromise);
        if (promise.State == JsPromiseObject.PromiseState.Pending)
            promise.AddReaction(reaction);
        else
            EnqueuePromiseReactionJob(promise, reaction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PromiseThenNoCapability(JsPromiseObject promise, in JsValue onFulfilled, in JsValue onRejected)
    {
        promise.IsHandled = true;
        var reaction = new JsPromiseObject.Reaction(onFulfilled, onRejected);
        if (promise.State == JsPromiseObject.PromiseState.Pending)
            promise.AddReaction(reaction);
        else
            EnqueuePromiseReactionJob(promise, reaction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PromiseThenResumeAsync(JsPromiseObject promise, JsGeneratorObject generator)
    {
        promise.IsHandled = true;
        if (promise.State == JsPromiseObject.PromiseState.Pending)
        {
            var reaction = new JsPromiseObject.Reaction(generator);
            promise.AddReaction(reaction);
            return;
        }

        var mode = promise.State == JsPromiseObject.PromiseState.Fulfilled
            ? GeneratorResumeMode.Next
            : GeneratorResumeMode.Throw;
        var resumeValue = promise.Result;
        Realm.Agent.EnqueueMicrotask(SResumeAsyncDriverJob,
            new GeneratorResumeJobState(Realm, generator, mode, resumeValue));
    }

    private void SchedulePromiseReactions(JsPromiseObject promise)
    {
        var reactions = promise.ConsumeReactions();
        if (reactions is null || reactions.Count == 0)
            return;
        for (var i = 0; i < reactions.Count; i++)
            EnqueuePromiseReactionJob(promise, reactions[i]);
    }

    internal void EnqueuePromiseReactionJob(JsPromiseObject sourcePromise, JsPromiseObject.Reaction reaction)
    {
        reaction.SourcePromise = sourcePromise;
        Realm.Agent.EnqueueMicrotask(SExecutePromiseReactionJob, reaction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ExecuteUnhandledRejectionCheckJob(JsPromiseObject promise)
    {
        if (promise.State == JsPromiseObject.PromiseState.Rejected && !promise.IsHandled)
            Realm.RaiseUnhandledRejection(promise.Result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void ExecutePromiseReactionJob(JsPromiseObject sourcePromise, JsPromiseObject.Reaction reaction)
    {
        var settledState = sourcePromise.State;
        var settledResult = sourcePromise.Result;
        switch (reaction.Kind)
        {
            case JsPromiseObject.Reaction.ReactionKind.AssimilateToPromise:
            {
                var target = reaction.TargetPromise!;
                if (settledState == JsPromiseObject.PromiseState.Fulfilled)
                    Realm.ResolvePromiseWithAssimilation(target, settledResult);
                else
                    Realm.RejectPromise(target, settledResult);
                return;
            }
            case JsPromiseObject.Reaction.ReactionKind.ResumeAsyncDriver:
            {
                var generator = reaction.AsyncGenerator!;
                var mode = settledState == JsPromiseObject.PromiseState.Fulfilled
                    ? GeneratorResumeMode.Next
                    : GeneratorResumeMode.Throw;
                if (generator.IsAsyncGenerator)
                    Realm.ContinueActiveAsyncGeneratorRequest(generator, mode, settledResult);
                else
                    Realm.StartOrResumeAsyncDriver(generator, mode, settledResult);
                return;
            }
            case JsPromiseObject.Reaction.ReactionKind.InvokeHandlersOnly:
                ExecuteFireAndForgetHandlerReaction(reaction, settledState, settledResult);
                return;
            case JsPromiseObject.Reaction.ReactionKind.ResumeAsyncGeneratorReturn:
            {
                var generator = reaction.AsyncGeneratorReturnTarget!;
                var mode = settledState == JsPromiseObject.PromiseState.Fulfilled
                    ? GeneratorResumeMode.Return
                    : GeneratorResumeMode.Throw;
                Realm.ContinueActiveAsyncGeneratorRequest(generator, mode, settledResult);
                return;
            }
            case JsPromiseObject.Reaction.ReactionKind.ResumeAsyncGeneratorYieldDelegate:
            {
                var state = reaction.AsyncGeneratorYieldDelegateAwaitState!;
                Realm.ContinueAsyncGeneratorYieldDelegateAfterAwait(
                    state.Generator,
                    state.OriginalMode,
                    settledState,
                    settledResult);
                return;
            }
            case JsPromiseObject.Reaction.ReactionKind.CompleteAsyncFromSyncIteratorResult:
            {
                var resolution = reaction.AsyncFromSyncIteratorResolution!;
                if (settledState == JsPromiseObject.PromiseState.Fulfilled)
                {
                    Realm.ResolvePromise(resolution.Promise,
                        JsValue.FromObject(Realm.CreateIteratorResultObject(settledResult, resolution.Done)));
                }
                else
                {
                    if (!resolution.Done && resolution.CloseOnRejection &&
                        resolution.IteratorToClose is { } iteratorToClose)
                        Realm.BestEffortIteratorCloseOnThrow(iteratorToClose);
                    Realm.RejectPromise(resolution.Promise, settledResult);
                }

                return;
            }
            case JsPromiseObject.Reaction.ReactionKind.AwaitAsyncGeneratorYieldValue:
            {
                var resolution = reaction.AsyncGeneratorYieldValueResolution!;
                if (settledState == JsPromiseObject.PromiseState.Fulfilled)
                {
                    Realm.ResolvePromise(resolution.Promise,
                        JsValue.FromObject(Realm.CreateIteratorResultObject(settledResult, false)));
                    Realm.FinishAsyncGeneratorRequest(resolution.Generator);
                }
                else
                {
                    Realm.ContinueActiveAsyncGeneratorRequest(
                        resolution.Generator,
                        GeneratorResumeMode.Throw,
                        settledResult);
                }

                return;
            }
            default:
                ExecuteUserHandlerReaction(reaction, settledState, settledResult);
                return;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void ExecuteFireAndForgetHandlerReaction(
        JsPromiseObject.Reaction reaction,
        JsPromiseObject.PromiseState settledState,
        in JsValue settledResult)
    {
        var handler = settledState == JsPromiseObject.PromiseState.Fulfilled
            ? reaction.OnFulfilled
            : reaction.OnRejected;
        if (!handler.TryGetObject(out var handlerObj) || handlerObj is not JsFunction fn)
            return;

        try
        {
            var arg0 = settledResult;
            _ = Realm.InvokeFunction(fn, JsValue.Undefined, MemoryMarshal.CreateReadOnlySpan(ref arg0, 1));
        }
        catch (JsRuntimeException)
        {
            // No chained capability is observed for these internal combinator callbacks.
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanUseIntrinsicPromiseCombinatorFastPath(JsFunction ctor, JsFunction promiseResolve)
    {
        if (!ReferenceEquals(ctor, PromiseConstructor) || !ReferenceEquals(promiseResolve, promiseResolveFunction))
            return false;
        return PromisePrototype.TryGetOwnNamedPropertyDescriptorAtom(Realm, IdThen, out var descriptor) &&
               descriptor.HasValue &&
               descriptor.Value.TryGetObject(out var thenObj) &&
               ReferenceEquals(thenObj, promiseThenFunction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnqueueImmediatePromiseHandlerJob(in JsValue onFulfilled, in JsValue onRejected, in JsValue value)
    {
        var onFulfilledCopy = onFulfilled;
        var onRejectedCopy = onRejected;
        var valueCopy = value;
        Realm.Agent.EnqueueMicrotask(SExecuteImmediatePromiseHandlerJob,
            new ImmediatePromiseHandlerJobState(Realm, onFulfilledCopy, onRejectedCopy, valueCopy));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteUserHandlerReaction(
        JsPromiseObject.Reaction reaction,
        JsPromiseObject.PromiseState settledState,
        in JsValue settledResult)
    {
        var handler = settledState == JsPromiseObject.PromiseState.Fulfilled
            ? reaction.OnFulfilled
            : reaction.OnRejected;

        var capability = reaction.Capability!;
        if (!handler.TryGetObject(out var handlerObj) || handlerObj is not JsFunction fn)
        {
            if (settledState == JsPromiseObject.PromiseState.Fulfilled)
                ResolvePromiseCapability(capability, settledResult);
            else
                RejectPromiseCapability(capability, settledResult);
            return;
        }

        try
        {
            var arg0 = settledResult;
            var args = MemoryMarshal.CreateReadOnlySpan(ref arg0, 1);
            var value = Realm.InvokeFunction(fn, JsValue.Undefined, args);
            ResolvePromiseCapability(capability, value);
        }
        catch (JsRuntimeException ex)
        {
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ResolvePromiseCapability(JsPromiseObject.PromiseCapability capability, in JsValue value)
    {
        CompletePromiseCapability(capability, value, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void RejectPromiseCapability(JsPromiseObject.PromiseCapability capability, in JsValue reason)
    {
        CompletePromiseCapability(capability, reason, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CompletePromiseCapability(JsPromiseObject.PromiseCapability capability, in JsValue value,
        bool isReject)
    {
        if (!isReject)
        {
            if (capability.Resolve is null && capability.Promise is JsPromiseObject promise)
            {
                ResolvePromiseWithAssimilation(promise, value);
                return;
            }

            var resolve = capability.Resolve!;
            var arg0 = value;
            _ = Realm.InvokeFunction(resolve, JsValue.Undefined, MemoryMarshal.CreateReadOnlySpan(ref arg0, 1));
            return;
        }

        if (capability.Reject is null && capability.Promise is JsPromiseObject rejectPromise)
        {
            RejectPromise(rejectPromise, value);
            return;
        }

        var reject = capability.Reject!;
        var arg0Reject = value;
        _ = Realm.InvokeFunction(reject, JsValue.Undefined, MemoryMarshal.CreateReadOnlySpan(ref arg0Reject, 1));
    }

    private sealed class PromiseThenableResolveJobState(
        JsRealm realm,
        JsValue resolution,
        JsObject target,
        JsFunction thenFunction,
        JsHostFunction resolve,
        JsHostFunction reject,
        StrongBox<JsPromiseObject?> targetHolder)
    {
        public readonly JsRealm Realm = realm;
        public readonly JsHostFunction Reject = reject;
        public readonly JsValue Resolution = resolution;
        public readonly JsHostFunction Resolve = resolve;
        public readonly JsObject Target = target;
        public readonly StrongBox<JsPromiseObject?> TargetHolder = targetHolder;
        public readonly JsFunction ThenFunction = thenFunction;
    }

    private sealed class ImmediatePromiseHandlerJobState(
        JsRealm realm,
        JsValue onFulfilled,
        JsValue onRejected,
        JsValue value)
    {
        public readonly JsValue OnFulfilled = onFulfilled;
        public readonly JsValue OnRejected = onRejected;
        public readonly JsRealm Realm = realm;
        public readonly JsValue Value = value;
    }

    internal sealed class AsyncFromSyncIteratorResolution(
        JsPromiseObject promise,
        bool done,
        JsObject? iteratorToClose = null,
        bool closeOnRejection = false)
    {
        public readonly bool CloseOnRejection = closeOnRejection;
        public readonly bool Done = done;
        public readonly JsObject? IteratorToClose = iteratorToClose;
        public readonly JsPromiseObject Promise = promise;
    }

    private readonly record struct PromiseCapabilityState(
        JsFunction? Resolve,
        JsFunction? Reject,
        bool HasDefinedEntries);

    private sealed class PromiseSettledState(JsPromiseObject promise)
    {
        public readonly JsPromiseObject Promise = promise;
        public bool AlreadyResolved;
    }

    private sealed class PromiseFinallyState(JsFunction constructor, JsFunction callback)
    {
        public readonly JsFunction Callback = callback;
        public readonly JsFunction Constructor = constructor;
    }
}
