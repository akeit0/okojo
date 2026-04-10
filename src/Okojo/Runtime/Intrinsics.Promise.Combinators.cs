using System.Runtime.InteropServices;
using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsValue PromiseRace(JsFunction ctor, in JsValue iterable)
    {
        var capability = CreatePromiseCapabilityWithFunctions(ctor);
        try
        {
            var promiseResolve = GetPromiseResolveFunction(ctor);
            var useFastPath = CanUseIntrinsicPromiseCombinatorFastPath(ctor, promiseResolve);
            var iteratorObj = GetPromiseRaceIterator(iterable);
            while (TryGetPromiseCombinatorNextValue(iteratorObj, capability, out var nextValue, out var done))
            {
                if (done)
                    return JsValue.FromObject(capability.Promise);

                if (!TryInvokePromiseCombinatorThen(useFastPath, ctor, promiseResolve, iteratorObj, capability,
                        nextValue,
                        JsValue.FromObject(capability.Resolve!), JsValue.FromObject(capability.Reject!)))
                    return JsValue.FromObject(capability.Promise);
            }
        }
        catch (JsRuntimeException ex)
        {
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
        }

        return JsValue.FromObject(capability.Promise);
    }

    private JsValue PromiseAll(JsFunction ctor, in JsValue iterable)
    {
        var capability = CreatePromiseCapabilityWithFunctions(ctor);
        try
        {
            var promiseResolve = GetPromiseResolveFunction(ctor);
            var useFastPath = CanUseIntrinsicPromiseCombinatorFastPath(ctor, promiseResolve);
            var iteratorObj = GetPromiseRaceIterator(iterable);
            var state = new PromiseAllState(Realm.CreateArrayObject(), capability);
            var index = 0;
            while (TryGetPromiseCombinatorNextValue(iteratorObj, capability, out var nextValue, out var done))
            {
                if (done)
                {
                    state.Remaining--;
                    if (state.Remaining == 0)
                        ResolvePromiseCapability(capability, JsValue.FromObject(state.Values));
                    return JsValue.FromObject(capability.Promise);
                }

                FreshArrayOperations.DefineElement(state.Values, (uint)index, JsValue.Undefined);
                state.Remaining++;
                var resolveElement = CreatePromiseAllResolveElement(state, index);
                if (!TryInvokePromiseCombinatorThen(useFastPath, ctor, promiseResolve, iteratorObj, capability,
                        nextValue,
                        JsValue.FromObject(resolveElement), JsValue.FromObject(capability.Reject!)))
                    return JsValue.FromObject(capability.Promise);

                index++;
            }
        }
        catch (JsRuntimeException ex)
        {
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
        }

        return JsValue.FromObject(capability.Promise);
    }

    private JsValue PromiseAny(JsFunction ctor, in JsValue iterable)
    {
        var capability = CreatePromiseCapabilityWithFunctions(ctor);
        try
        {
            var promiseResolve = GetPromiseResolveFunction(ctor);
            var useFastPath = CanUseIntrinsicPromiseCombinatorFastPath(ctor, promiseResolve);
            var iteratorObj = GetPromiseRaceIterator(iterable);
            var state = new PromiseAnyState(Realm.CreateArrayObject(), capability);
            var index = 0;
            while (TryGetPromiseCombinatorNextValue(iteratorObj, capability, out var nextValue, out var done))
            {
                if (done)
                {
                    state.Remaining--;
                    if (state.Remaining == 0)
                    {
                        var error = CreatePromiseAnyAggregateError(state.Errors);
                        RejectPromiseCapability(capability, error);
                    }

                    return JsValue.FromObject(capability.Promise);
                }

                FreshArrayOperations.DefineElement(state.Errors, (uint)index, JsValue.Undefined);
                state.Remaining++;
                var rejectElement = CreatePromiseAnyRejectElement(state, index);
                if (!TryInvokePromiseCombinatorThen(useFastPath, ctor, promiseResolve, iteratorObj, capability,
                        nextValue,
                        JsValue.FromObject(capability.Resolve!), JsValue.FromObject(rejectElement)))
                    return JsValue.FromObject(capability.Promise);

                index++;
            }
        }
        catch (JsRuntimeException ex)
        {
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
        }

        return JsValue.FromObject(capability.Promise);
    }

    private JsValue PromiseAllSettled(JsFunction ctor, in JsValue iterable)
    {
        var capability = CreatePromiseCapabilityWithFunctions(ctor);
        try
        {
            var promiseResolve = GetPromiseResolveFunction(ctor);
            var useFastPath = CanUseIntrinsicPromiseCombinatorFastPath(ctor, promiseResolve);
            var iteratorObj = GetPromiseRaceIterator(iterable);
            var state = new PromiseAllSettledState(Realm.CreateArrayObject(), capability);
            var index = 0;
            while (TryGetPromiseCombinatorNextValue(iteratorObj, capability, out var nextValue, out var done))
            {
                if (done)
                {
                    state.Remaining--;
                    if (state.Remaining == 0)
                        ResolvePromiseCapability(capability, JsValue.FromObject(state.Values));
                    return JsValue.FromObject(capability.Promise);
                }

                FreshArrayOperations.DefineElement(state.Values, (uint)index, JsValue.Undefined);
                state.Remaining++;
                var fulfilled = CreatePromiseAllSettledElement(state, index, false);
                var rejected = CreatePromiseAllSettledElement(state, index, true);
                if (!TryInvokePromiseCombinatorThen(useFastPath, ctor, promiseResolve, iteratorObj, capability,
                        nextValue,
                        JsValue.FromObject(fulfilled), JsValue.FromObject(rejected)))
                    return JsValue.FromObject(capability.Promise);

                index++;
            }
        }
        catch (JsRuntimeException ex)
        {
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
        }

        return JsValue.FromObject(capability.Promise);
    }

    private JsHostFunction CreatePromiseAllResolveElement(PromiseAllState owner, int index)
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var state = (PromiseAllElementState)((JsHostFunction)info.Function).UserData!;
            if (state.AlreadyCalled)
                return JsValue.Undefined;

            state.AlreadyCalled = true;
            var value = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            FreshArrayOperations.DefineElement(state.Owner.Values, (uint)state.Index, value);
            state.Owner.Remaining--;
            if (state.Owner.Remaining == 0)
                realm.Intrinsics.ResolvePromiseCapability(state.Owner.Capability,
                    JsValue.FromObject(state.Owner.Values));
            return JsValue.Undefined;
        }, string.Empty, 1)
        {
            UserData = new PromiseAllElementState(owner, index)
        };
    }

    private JsHostFunction CreatePromiseAnyRejectElement(PromiseAnyState owner, int index)
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var state = (PromiseAnyRejectElementState)((JsHostFunction)info.Function).UserData!;
            if (state.AlreadyCalled)
                return JsValue.Undefined;

            state.AlreadyCalled = true;
            var reason = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            FreshArrayOperations.DefineElement(state.Owner.Errors, (uint)state.Index, reason);
            state.Owner.Remaining--;
            if (state.Owner.Remaining == 0)
            {
                var error = realm.Intrinsics.CreatePromiseAnyAggregateError(state.Owner.Errors);
                realm.Intrinsics.RejectPromiseCapability(state.Owner.Capability, error);
            }

            return JsValue.Undefined;
        }, string.Empty, 1)
        {
            UserData = new PromiseAnyRejectElementState(owner, index)
        };
    }

    private JsHostFunction CreatePromiseAllSettledElement(PromiseAllSettledState owner, int index, bool isReject)
    {
        return new(Realm, (in info) =>
        {
            var state = (PromiseAllSettledElementState)((JsHostFunction)info.Function).UserData!;
            if (state.AlreadyCalled)
                return JsValue.Undefined;

            state.AlreadyCalled = true;
            var settledValue = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            return info.Realm.Intrinsics.CompletePromiseAllSettledElement(state, settledValue);
        }, string.Empty, 1)
        {
            UserData = new PromiseAllSettledElementState(owner, index, isReject)
        };
    }

    private JsValue CompletePromiseAllSettledElement(PromiseAllSettledElementState state, in JsValue settledValue)
    {
        var result = new JsPlainObject(Realm);
        if (state.IsReject)
        {
            result.DefineDataProperty("status", JsValue.FromString("rejected"), JsShapePropertyFlags.Open);
            result.DefineDataProperty("reason", settledValue, JsShapePropertyFlags.Open);
        }
        else
        {
            result.DefineDataProperty("status", JsValue.FromString("fulfilled"), JsShapePropertyFlags.Open);
            result.DefineDataProperty("value", settledValue, JsShapePropertyFlags.Open);
        }

        FreshArrayOperations.DefineElement(state.Owner.Values, (uint)state.Index, JsValue.FromObject(result));
        state.Owner.Remaining--;
        if (state.Owner.Remaining == 0)
            ResolvePromiseCapability(state.Owner.Capability, JsValue.FromObject(state.Owner.Values));
        return JsValue.Undefined;
    }

    private JsValue CreatePromiseAnyAggregateError(JsArray errors)
    {
        var errorArgs = new InlineJsValueArray2
        {
            Item0 = JsValue.FromObject(errors),
            Item1 = JsValue.Undefined
        };
        return Realm.ConstructWithExplicitNewTarget(AggregateErrorConstructor, errorArgs.AsSpan(),
            JsValue.FromObject(AggregateErrorConstructor), 0);
    }

    private bool TryGetPromiseCombinatorNextValue(
        JsObject iteratorObj,
        JsPromiseObject.PromiseCapability capability,
        out JsValue nextValue,
        out bool done)
    {
        nextValue = JsValue.Undefined;
        done = false;

        JsObject step;
        try
        {
            step = GetPromiseRaceIteratorStep(iteratorObj, out done);
        }
        catch (JsRuntimeException ex)
        {
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
            return false;
        }

        if (done)
            return true;

        try
        {
            if (!step.TryGetPropertyAtom(Realm, IdValue, out nextValue, out _))
                nextValue = JsValue.Undefined;
            return true;
        }
        catch (JsRuntimeException ex)
        {
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
            return false;
        }
    }

    private bool TryInvokePromiseCombinatorThen(
        bool useFastPath,
        JsFunction ctor,
        JsFunction promiseResolve,
        JsObject iteratorObj,
        JsPromiseObject.PromiseCapability capability,
        in JsValue nextValue,
        in JsValue onFulfilled,
        in JsValue onRejected)
    {
        if (useFastPath)
        {
            if (!nextValue.TryGetObject(out var nextObj))
            {
                EnqueueImmediatePromiseHandlerJob(onFulfilled, onRejected, nextValue);
                return true;
            }

            if (nextObj is JsPromiseObject promise &&
                ReferenceEquals(nextObj.Prototype, PromisePrototype) &&
                !nextObj.TryGetOwnNamedPropertyDescriptorAtom(Realm, IdThen, out _))
            {
                PromiseThenNoCapability(promise, onFulfilled, onRejected);
                return true;
            }
        }

        try
        {
            var arg0 = nextValue;
            var nextPromise = Realm.InvokeFunction(promiseResolve, JsValue.FromObject(ctor),
                MemoryMarshal.CreateReadOnlySpan(ref arg0, 1));
            InvokePromiseThen(nextPromise, onFulfilled, onRejected);
            return true;
        }
        catch (JsRuntimeException ex)
        {
            BestEffortClosePromiseRaceIterator(iteratorObj);
            RejectPromiseCapability(capability, GetPromiseAbruptReason(ex));
            return false;
        }
    }

    private JsObject GetPromiseRaceIterator(in JsValue iterable)
    {
        if (!Realm.TryToObject(iterable, out var iterableObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Object is not iterable");
        return Realm.GetIteratorObjectForIterable(iterableObj, "Object is not iterable", "Iterator is not an object");
    }

    private JsObject GetPromiseRaceIteratorStep(JsObject iteratorObj, out bool done)
    {
        if (!iteratorObj.TryGetPropertyAtom(Realm, IdNext, out var nextValue, out _) ||
            !nextValue.TryGetObject(out var nextObj) ||
            nextObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator next is not callable");

        var stepValue = Realm.InvokeFunction(nextFn, JsValue.FromObject(iteratorObj), ReadOnlySpan<JsValue>.Empty);
        if (!stepValue.TryGetObject(out var stepObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result is not an object");

        if (!stepObj.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _))
            doneValue = JsValue.Undefined;
        done = JsRealm.ToBoolean(doneValue);
        return stepObj;
    }

    private void BestEffortClosePromiseRaceIterator(JsObject iteratorObj)
    {
        try
        {
            if (!iteratorObj.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) ||
                returnValue.IsUndefined || returnValue.IsNull)
                return;

            if (!returnValue.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator return is not callable");
            _ = Realm.InvokeFunction(returnFn, JsValue.FromObject(iteratorObj), ReadOnlySpan<JsValue>.Empty);
        }
        catch
        {
            // Preserve the original abrupt completion.
        }
    }

    private sealed class PromiseAllState(JsArray values, JsPromiseObject.PromiseCapability capability)
    {
        public readonly JsPromiseObject.PromiseCapability Capability = capability;
        public readonly JsArray Values = values;
        public int Remaining = 1;
    }

    private sealed class PromiseAllElementState(PromiseAllState owner, int index)
    {
        public readonly int Index = index;
        public readonly PromiseAllState Owner = owner;
        public bool AlreadyCalled;
    }

    private sealed class PromiseAnyState(JsArray errors, JsPromiseObject.PromiseCapability capability)
    {
        public readonly JsPromiseObject.PromiseCapability Capability = capability;
        public readonly JsArray Errors = errors;
        public int Remaining = 1;
    }

    private sealed class PromiseAnyRejectElementState(PromiseAnyState owner, int index)
    {
        public readonly int Index = index;
        public readonly PromiseAnyState Owner = owner;
        public bool AlreadyCalled;
    }

    private sealed class PromiseAllSettledState(JsArray values, JsPromiseObject.PromiseCapability capability)
    {
        public readonly JsPromiseObject.PromiseCapability Capability = capability;
        public readonly JsArray Values = values;
        public int Remaining = 1;
    }

    private sealed class PromiseAllSettledElementState(PromiseAllSettledState owner, int index, bool isReject)
    {
        public readonly int Index = index;
        public readonly bool IsReject = isReject;
        public readonly PromiseAllSettledState Owner = owner;
        public bool AlreadyCalled;
    }
}
