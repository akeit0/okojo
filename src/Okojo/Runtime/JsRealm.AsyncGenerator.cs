using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private const int MaxAsyncGeneratorRequestPoolSize = 512;

    private static readonly Action<object?> SResumeAsyncGeneratorRequestJob = static state =>
    {
        var job = (GeneratorResumeJobState)state!;
        job.Realm.ContinueActiveAsyncGeneratorRequest(job.Generator, job.Mode, job.Value);
    };

    private static readonly Action<object?> SResolveCompletedAsyncGeneratorNextJob = static state =>
    {
        var promise = (JsPromiseObject)state!;
        var realm = promise.Realm;
        realm.Intrinsics.ResolvePromise(promise,
            JsValue.FromObject(realm.CreateIteratorResultObject(JsValue.Undefined, true)));
    };

    private static readonly Action<object?> SResolveSettledAsyncGeneratorRequestJob = static state =>
    {
        var resolveState = (AsyncGeneratorSettledResolveState)state!;
        if (resolveState.SettledState == JsPromiseObject.PromiseState.Fulfilled)
            resolveState.Realm.Intrinsics.ResolvePromise(resolveState.RequestPromise,
                JsValue.FromObject(
                    resolveState.Realm.CreateIteratorResultObject(resolveState.SettledResult, resolveState.Done)));
        else
            resolveState.Realm.Intrinsics.RejectPromise(resolveState.RequestPromise, resolveState.SettledResult);

        resolveState.Realm.FinishAsyncGeneratorRequest(resolveState.Generator);
    };

    private readonly Stack<GeneratorObjectCore.AsyncGeneratorRequest> asyncGeneratorRequestPool = new();

    internal void InstallAsyncGeneratorPrototypeBuiltins()
    {
        AsyncGeneratorObjectPrototype.Prototype = AsyncIteratorPrototype;

        var nextFn = new JsHostFunction(this, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!realm.TryGetAsyncGeneratorReceiver(info.ThisValue, "next", out var generator, out var rejectedPromise))
                return rejectedPromise;
            var input = args.Length != 0 ? args[0] : JsValue.Undefined;
            return realm.ResumeAsyncGeneratorObject(generator, GeneratorResumeMode.Next, input);
        }, "next", 1);

        var returnFn = new JsHostFunction(this, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!realm.TryGetAsyncGeneratorReceiver(info.ThisValue, "return", out var generator,
                    out var rejectedPromise))
                return rejectedPromise;
            var input = args.Length != 0 ? args[0] : JsValue.Undefined;
            return realm.ResumeAsyncGeneratorObject(generator, GeneratorResumeMode.Return, input);
        }, "return", 1);

        var throwFn = new JsHostFunction(this, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!realm.TryGetAsyncGeneratorReceiver(info.ThisValue, "throw", out var generator,
                    out var rejectedPromise))
                return rejectedPromise;
            var input = args.Length != 0 ? args[0] : JsValue.Undefined;
            return realm.ResumeAsyncGeneratorObject(generator, GeneratorResumeMode.Throw, input);
        }, "throw", 1);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdConstructor, JsValue.FromObject(AsyncGeneratorFunctionPrototype),
                configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("AsyncGenerator"),
                configurable: true),
            PropertyDefinition.Mutable(IdNext, JsValue.FromObject(nextFn)),
            PropertyDefinition.Mutable(IdReturn, JsValue.FromObject(returnFn)),
            PropertyDefinition.Mutable(IdThrow, JsValue.FromObject(throwFn))
        ];
        AsyncGeneratorObjectPrototype.DefineNewPropertiesNoCollision(this, defs);

        Span<PropertyDefinition> asyncGeneratorFunctionPrototypeDefs =
        [
            PropertyDefinition.Const(IdPrototype, AsyncGeneratorObjectPrototype, configurable: true)
        ];
        AsyncGeneratorFunctionPrototype.DefineNewPropertiesNoCollision(this, asyncGeneratorFunctionPrototypeDefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue ResumeAsyncGeneratorObject(JsGeneratorObject generator, GeneratorResumeMode mode,
        JsValue input)
    {
        if (!generator.AsyncGeneratorRequestActive && generator.State == GeneratorState.Completed &&
            mode == GeneratorResumeMode.Next)
        {
            var completedPromise = Intrinsics.CreatePromiseObject();
            Agent.EnqueueMicrotask(SResolveCompletedAsyncGeneratorNextJob, completedPromise);
            return completedPromise;
        }

        var promise = Intrinsics.CreatePromiseObject();
        var request = RentAsyncGeneratorRequest();
        request.Mode = mode;
        request.Value = input;
        request.Promise = promise;
        (generator.Core.AsyncRequestQueue ??= new()).Enqueue(request);

        if (!generator.AsyncGeneratorRequestActive)
            ProcessQueuedAsyncGeneratorRequests(generator);

        return promise;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ProcessQueuedAsyncGeneratorRequests(JsGeneratorObject generator)
    {
        if (generator.AsyncGeneratorRequestActive)
            return;

        var queue = generator.Core.AsyncRequestQueue;
        if (queue is null || queue.Count == 0)
            return;

        generator.AsyncGeneratorRequestActive = true;
        generator.Core.ActiveAsyncRequest = queue.Dequeue();
        var request = generator.Core.ActiveAsyncRequest!;
        ContinueActiveAsyncGeneratorRequest(generator, request.Mode, request.Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void ContinueActiveAsyncGeneratorRequest(JsGeneratorObject generator, GeneratorResumeMode mode,
        JsValue value)
    {
        var request = generator.Core.ActiveAsyncRequest;
        if (request is null)
        {
            generator.AsyncGeneratorRequestActive = false;
            return;
        }

        try
        {
            if (generator.State == GeneratorState.Completed)
            {
                if (mode == GeneratorResumeMode.Return && !request.ReturnValueAwaited)
                {
                    request.ReturnValueAwaited = true;
                    request.CompletedAfterAwait = true;
                    try
                    {
                        AwaitAsyncGeneratorReturn(generator, value);
                    }
                    catch (JsRuntimeException ex)
                    {
                        ContinueActiveAsyncGeneratorRequest(generator, GeneratorResumeMode.Throw,
                            ex.ThrownValue ?? CreateErrorObjectFromException(ex));
                    }

                    return;
                }

                if (mode == GeneratorResumeMode.Throw)
                {
                    Intrinsics.RejectPromise(request.Promise, value);
                    FinishAsyncGeneratorRequest(generator);
                }
                else
                {
                    ResolveAsyncGeneratorRequest(generator, request.Promise,
                        mode == GeneratorResumeMode.Return ? value : JsValue.Undefined, true);
                }

                return;
            }

            if (generator.State == GeneratorState.SuspendedStart)
            {
                if (generator.HasContinuation)
                {
                    if (mode == GeneratorResumeMode.Return)
                    {
                        FinalizeGenerator(generator);
                        request.ReturnValueAwaited = true;
                        request.CompletedAfterAwait = true;
                        try
                        {
                            AwaitAsyncGeneratorReturn(generator, value);
                        }
                        catch (JsRuntimeException ex)
                        {
                            ContinueActiveAsyncGeneratorRequest(generator, GeneratorResumeMode.Throw,
                                ex.ThrownValue ?? CreateErrorObjectFromException(ex));
                        }

                        return;
                    }

                    if (mode == GeneratorResumeMode.Throw)
                    {
                        FinalizeGenerator(generator);
                        Intrinsics.RejectPromise(request.Promise, value);
                        FinishAsyncGeneratorRequest(generator);
                        return;
                    }

                    generator.PendingResumeMode = GeneratorResumeMode.Next;
                    generator.PendingResumeValue = JsValue.Undefined;
                    var resumedAfterAwait = generator.LastSuspendWasAwait;
                    request.CompletedAfterAwait |= resumedAfterAwait;
                    var resumedStep = ExecuteGeneratorFromContinuation(generator);

                    if (generator.Core.HasPendingAsyncYieldDelegateAwait)
                    {
                        generator.Core.HasPendingAsyncYieldDelegateAwait = false;
                        return;
                    }

                    if (generator.State == GeneratorState.Completed)
                    {
                        ResolveAsyncGeneratorRequestFromStep(
                            generator,
                            request.Promise,
                            resumedStep,
                            true,
                            request.CompletedAfterAwait);
                        return;
                    }

                    if (generator.LastSuspendWasAwait)
                    {
                        request.CompletedAfterAwait = true;
                        AttachAsyncGeneratorAwaitContinuation(generator, resumedStep);
                        return;
                    }

                    ResolveAsyncGeneratorRequestFromStep(generator, request.Promise, resumedStep, false);
                    return;
                }

                if (mode == GeneratorResumeMode.Return)
                {
                    FinalizeGenerator(generator);
                    request.ReturnValueAwaited = true;
                    request.CompletedAfterAwait = true;
                    try
                    {
                        AwaitAsyncGeneratorReturn(generator, value);
                    }
                    catch (JsRuntimeException ex)
                    {
                        ContinueActiveAsyncGeneratorRequest(generator, GeneratorResumeMode.Throw,
                            ex.ThrownValue ?? CreateErrorObjectFromException(ex));
                    }

                    return;
                }

                if (mode == GeneratorResumeMode.Throw)
                {
                    FinalizeGenerator(generator);
                    Intrinsics.RejectPromise(request.Promise, value);
                    FinishAsyncGeneratorRequest(generator);
                    return;
                }
            }

            if (mode == GeneratorResumeMode.Return && !request.ReturnValueAwaited)
            {
                request.ReturnValueAwaited = true;
                request.CompletedAfterAwait = true;
                try
                {
                    AwaitAsyncGeneratorReturn(generator, value);
                }
                catch (JsRuntimeException ex)
                {
                    ContinueActiveAsyncGeneratorRequest(generator, GeneratorResumeMode.Throw,
                        ex.ThrownValue ?? CreateErrorObjectFromException(ex));
                }

                return;
            }

            JsValue stepResult;
            var completedAfterAwaitResume = false;
            if (generator.State == GeneratorState.SuspendedStart)
            {
                generator.PendingResumeMode = GeneratorResumeMode.Next;
                generator.PendingResumeValue = JsValue.Undefined;
                stepResult = ExecuteGeneratorFromStart(generator);
            }
            else
            {
                completedAfterAwaitResume = generator.LastSuspendWasAwait;
                request.CompletedAfterAwait |= completedAfterAwaitResume;
                generator.PendingResumeMode = mode;
                generator.PendingResumeValue = value;
                stepResult = ExecuteGeneratorFromContinuation(generator);
            }

            if (generator.Core.HasPendingAsyncYieldDelegateAwait)
            {
                generator.Core.HasPendingAsyncYieldDelegateAwait = false;
                return;
            }

            if (mode == GeneratorResumeMode.Return &&
                generator.State != GeneratorState.Completed &&
                TryGetIteratorResultParts(stepResult, out _, out var returnDone) &&
                returnDone)
            {
                FinalizeGenerator(generator);
                ResolveAsyncGeneratorRequestFromStep(generator, request.Promise, stepResult, true);
                return;
            }

            if (generator.State == GeneratorState.Completed)
            {
                ResolveAsyncGeneratorRequestFromStep(generator, request.Promise, stepResult, true,
                    request.CompletedAfterAwait);
                return;
            }

            if (generator.LastSuspendWasAwait)
            {
                request.CompletedAfterAwait = true;
                AttachAsyncGeneratorAwaitContinuation(generator, stepResult);
                return;
            }

            ResolveAsyncGeneratorRequestFromStep(generator, request.Promise, stepResult, false);
        }
        catch (JsRuntimeException ex)
        {
            FinalizeGenerator(generator);
            Intrinsics.RejectPromise(request.Promise, ex.ThrownValue ?? CreateErrorObjectFromException(ex));
            FinishAsyncGeneratorRequest(generator);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AwaitAsyncGeneratorReturn(JsGeneratorObject generator, JsValue value)
    {
        var promiseValue = Intrinsics.PromiseResolveByConstructor(PromiseConstructor, value);
        if (!promiseValue.TryGetObject(out var promiseObj) || promiseObj is not JsPromiseObject promise)
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "Promise.resolve must produce a promise object");

        promise.IsHandled = true;
        if (promise.State == JsPromiseObject.PromiseState.Pending)
        {
            promise.AddReaction(JsPromiseObject.Reaction.CreateAsyncGeneratorReturn(generator));
            return;
        }

        var mode = promise.State == JsPromiseObject.PromiseState.Fulfilled
            ? GeneratorResumeMode.Return
            : GeneratorResumeMode.Throw;
        var resumeValue = promise.Result;
        Agent.EnqueueMicrotask(SResumeAsyncGeneratorRequestJob,
            new GeneratorResumeJobState(this, generator, mode, resumeValue));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResolveAsyncGeneratorRequestFromStep(
        JsGeneratorObject generator,
        JsPromiseObject requestPromise,
        JsValue stepResult,
        bool defaultDone,
        bool forceAsyncSettlement = false)
    {
        if (TryGetIteratorResultParts(stepResult, out var value, out var done))
        {
            ResolveAsyncGeneratorRequest(generator, requestPromise, value, done, forceAsyncSettlement);
            return;
        }

        ResolveAsyncGeneratorRequest(generator, requestPromise, stepResult, defaultDone, forceAsyncSettlement);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResolveAsyncGeneratorRequest(
        JsGeneratorObject generator,
        JsPromiseObject requestPromise,
        JsValue value,
        bool done,
        bool forceAsyncSettlement = false)
    {
        if (done)
        {
            Intrinsics.ResolvePromise(requestPromise, JsValue.FromObject(CreateIteratorResultObject(value, true)));
            FinishAsyncGeneratorRequest(generator);
            return;
        }

        if (generator.HasActiveDelegateIterator &&
            generator.ActiveDelegateIterator is not JsAsyncFromSyncIteratorObject)
        {
            Intrinsics.ResolvePromise(requestPromise, JsValue.FromObject(CreateIteratorResultObject(value, false)));
            FinishAsyncGeneratorRequest(generator);
            return;
        }

        var promiseValue = Intrinsics.PromiseResolveByConstructor(PromiseConstructor, value);
        if (!promiseValue.TryGetObject(out var promiseObj) || promiseObj is not JsPromiseObject promise)
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "Promise.resolve must produce a promise object");
        promise.IsHandled = true;

        var reaction = JsPromiseObject.Reaction.CreateAsyncGeneratorYieldValue(
            new(generator, requestPromise));
        if (promise.State == JsPromiseObject.PromiseState.Pending)
            promise.AddReaction(reaction);
        else
            Intrinsics.EnqueuePromiseReactionJob(promise, reaction);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AttachAsyncGeneratorAwaitContinuation(JsGeneratorObject generator, JsValue stepResult)
    {
        var promiseValue = Intrinsics.PromiseResolveByConstructor(PromiseConstructor, stepResult);
        if (!promiseValue.TryGetObject(out var promiseObj) || promiseObj is not JsPromiseObject promise)
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "PromiseResolve(%Promise%, awaitValue) must produce a promise object");
        PromiseThenContinueAsyncGenerator(promise, generator);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PromiseThenContinueAsyncGenerator(JsPromiseObject promise, JsGeneratorObject generator)
    {
        promise.IsHandled = true;
        if (promise.State == JsPromiseObject.PromiseState.Pending)
        {
            promise.AddReaction(new(generator));
            return;
        }

        var mode = promise.State == JsPromiseObject.PromiseState.Fulfilled
            ? GeneratorResumeMode.Next
            : GeneratorResumeMode.Throw;
        var resumeValue = promise.Result;
        Agent.EnqueueMicrotask(SResumeAsyncGeneratorRequestJob,
            new GeneratorResumeJobState(this, generator, mode, resumeValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void FinishAsyncGeneratorRequest(JsGeneratorObject generator)
    {
        var activeRequest = generator.Core.ActiveAsyncRequest;
        generator.Core.ActiveAsyncRequest = null;
        generator.AsyncGeneratorRequestActive = false;
        if (activeRequest is not null)
            ReturnAsyncGeneratorRequest(activeRequest);
        ProcessQueuedAsyncGeneratorRequests(generator);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GeneratorObjectCore.AsyncGeneratorRequest RentAsyncGeneratorRequest()
    {
        return asyncGeneratorRequestPool.Count != 0
            ? asyncGeneratorRequestPool.Pop()
            : new();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReturnAsyncGeneratorRequest(GeneratorObjectCore.AsyncGeneratorRequest request)
    {
        request.Reset();
        if (asyncGeneratorRequestPool.Count >= MaxAsyncGeneratorRequestPoolSize)
            return;
        asyncGeneratorRequestPool.Push(request);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void ContinueAsyncGeneratorYieldDelegateAfterAwait(
        JsGeneratorObject generator,
        GeneratorResumeMode originalMode,
        JsPromiseObject.PromiseState settledState,
        JsValue settledResult)
    {
        if (settledState != JsPromiseObject.PromiseState.Fulfilled)
        {
            generator.ActiveDelegateIterator = null;
            ClearDelegateIteratorRegisterInContinuationSnapshot(generator);
            ContinueActiveAsyncGeneratorRequest(generator, GeneratorResumeMode.Throw, settledResult);
            return;
        }

        try
        {
            if (!settledResult.TryGetObject(out var resultObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator result is not an object");

            _ = resultObj.TryGetPropertyAtom(this, IdDone, out var doneValue, out _);
            var done = doneValue.ToBoolean();
            if (!resultObj.TryGetPropertyAtom(this, IdValue, out var value, out _))
                value = JsValue.Undefined;

            if (!done)
            {
                var request = generator.Core.ActiveAsyncRequest;
                if (request is null)
                    return;

                Intrinsics.ResolvePromise(request.Promise,
                    JsValue.FromObject(CreateIteratorResultObject(value, false)));
                FinishAsyncGeneratorRequest(generator);
                return;
            }

            generator.ActiveDelegateIterator = null;
            ClearDelegateIteratorRegisterInContinuationSnapshot(generator);
            var continueMode = originalMode == GeneratorResumeMode.Return
                ? GeneratorResumeMode.Return
                : GeneratorResumeMode.Next;
            ContinueActiveAsyncGeneratorRequest(generator, continueMode, value);
        }
        catch (JsRuntimeException ex)
        {
            generator.ActiveDelegateIterator = null;
            ClearDelegateIteratorRegisterInContinuationSnapshot(generator);
            ContinueActiveAsyncGeneratorRequest(generator, GeneratorResumeMode.Throw,
                ex.ThrownValue ?? CreateErrorObjectFromException(ex));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsIteratorResultObject(in JsValue value)
    {
        return TryGetIteratorResultParts(value, out _, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetIteratorResultParts(in JsValue value, out JsValue resultValue, out bool done)
    {
        resultValue = JsValue.Undefined;
        done = false;
        if (!value.TryGetObject(out var obj))
            return false;
        if (obj.TryGetOwnNamedDataSlotIndex(IdValue, out var valueSlot) &&
            obj.TryGetOwnNamedDataSlotIndex(IdDone, out var doneSlot))
        {
            resultValue = obj.GetNamedSlotUnchecked(valueSlot);
            done = obj.GetNamedSlotUnchecked(doneSlot).ToBoolean();
            return true;
        }

        if (!obj.HasOwnPropertyAtom(this, IdValue) || !obj.HasOwnPropertyAtom(this, IdDone))
            return false;

        obj.TryGetPropertyAtom(this, IdValue, out resultValue, out _);
        obj.TryGetPropertyAtom(this, IdDone, out var doneValue, out _);
        done = doneValue.ToBoolean();
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetAsyncGeneratorReceiver(
        in JsValue thisValue,
        string methodName,
        out JsGeneratorObject generator,
        out JsValue rejectedPromise)
    {
        if (thisValue.TryGetObject(out var obj) && obj is JsGeneratorObject generatorObject &&
            generatorObject.IsAsyncGenerator)
        {
            generator = generatorObject;
            rejectedPromise = default;
            return true;
        }

        generator = null!;
        var promise = Intrinsics.CreatePromiseObject();
        Intrinsics.RejectPromise(promise, CreateErrorObjectFromException(new(
            JsErrorKind.TypeError,
            $"AsyncGenerator.prototype.{methodName} called on incompatible receiver")));
        rejectedPromise = promise;
        return false;
    }
}
