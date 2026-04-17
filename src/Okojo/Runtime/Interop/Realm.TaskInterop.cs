using System.Runtime.CompilerServices;
using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Runtime.Interop
{
    public sealed class PromiseRejectedException(JsValue reason) : Exception($"JavaScript promise rejected: {reason}")
    {
        public JsValue Reason { get; } = reason;
    }
}

namespace Okojo.Runtime
{
    public sealed partial class JsRealm
    {
        private static readonly Action<object?> SCompleteTaskPromiseJob = static state =>
        {
            var completion = (PendingTaskPromiseState)state!;
            completion.Realm.CompletePromiseFromTask(completion.Task, completion.Promise,
                completion.CanceledReasonFactory);
        };

        public JsValue WrapTask(Task task)
        {
            return WrapTask(task, false, InternalHostTaskQueueDefaults.Default, null);
        }

        public JsValue WrapTask(Task task, Func<JsValue> canceledReasonFactory)
        {
            ArgumentNullException.ThrowIfNull(canceledReasonFactory);
            return WrapTask(task, false, InternalHostTaskQueueDefaults.Default, canceledReasonFactory);
        }

        internal JsValue WrapTaskOnHostQueue(Task task, HostTaskQueueKey completionQueueKey)
        {
            return WrapTask(task, true, completionQueueKey, null);
        }

        internal JsValue WrapTaskOnHostQueue(Task task, HostTaskQueueKey completionQueueKey,
            Func<JsValue> canceledReasonFactory)
        {
            ArgumentNullException.ThrowIfNull(canceledReasonFactory);
            return WrapTask(task, true, completionQueueKey, canceledReasonFactory);
        }

        private JsValue WrapTask(Task task, bool useHostQueue, HostTaskQueueKey completionQueueKey,
            Func<JsValue>? canceledReasonFactory)
        {
            ArgumentNullException.ThrowIfNull(task);
            var promise = this.CreatePromiseObject();
            if (task.IsCompleted)
            {
                CompletePromiseFromTask(task, promise, canceledReasonFactory);
                return JsValue.FromObject(promise);
            }

            _ = task.ContinueWith(static (completed, state) =>
                {
                    var pending = (PendingTaskPromiseState)state!;
                    var queued = new PendingTaskPromiseState
                    {
                        Realm = pending.Realm,
                        Task = completed,
                        Promise = pending.Promise,
                        CompletionQueueKey = pending.CompletionQueueKey,
                        UseHostQueue = pending.UseHostQueue,
                        CanceledReasonFactory = pending.CanceledReasonFactory
                    };
                    if (pending.UseHostQueue)
                        pending.Realm.Agent.EnqueueHostTask(pending.CompletionQueueKey, SCompleteTaskPromiseJob,
                            queued);
                    else
                        pending.Realm.Agent.EnqueueMicrotask(SCompleteTaskPromiseJob, queued);
                },
                new PendingTaskPromiseState
                {
                    Realm = this,
                    Task = task,
                    Promise = promise,
                    CompletionQueueKey = completionQueueKey,
                    UseHostQueue = useHostQueue,
                    CanceledReasonFactory = canceledReasonFactory
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return JsValue.FromObject(promise);
        }

        public JsValue WrapTask<T>(Task<T> task)
        {
            return WrapTask(task, false, InternalHostTaskQueueDefaults.Default, null);
        }

        public JsValue WrapTask<T>(Task<T> task, Func<JsValue> canceledReasonFactory)
        {
            ArgumentNullException.ThrowIfNull(canceledReasonFactory);
            return WrapTask(task, false, InternalHostTaskQueueDefaults.Default, canceledReasonFactory);
        }

        internal JsValue WrapTaskOnHostQueue<T>(Task<T> task, HostTaskQueueKey completionQueueKey)
        {
            return WrapTask(task, true, completionQueueKey, null);
        }

        internal JsValue WrapTaskOnHostQueue<T>(Task<T> task, HostTaskQueueKey completionQueueKey,
            Func<JsValue> canceledReasonFactory)
        {
            ArgumentNullException.ThrowIfNull(canceledReasonFactory);
            return WrapTask(task, true, completionQueueKey, canceledReasonFactory);
        }

        private JsValue WrapTask<T>(Task<T> task, bool useHostQueue, HostTaskQueueKey completionQueueKey,
            Func<JsValue>? canceledReasonFactory)
        {
            ArgumentNullException.ThrowIfNull(task);
            var promise = this.CreatePromiseObject();
            if (task.IsCompleted)
            {
                CompletePromiseFromTask(task, promise, canceledReasonFactory);
                return JsValue.FromObject(promise);
            }

            _ = task.ContinueWith(static (completed, state) =>
                {
                    var pending = (PendingTaskResultPromiseState<T>)state!;
                    var queued = new PendingTaskResultPromiseState<T>
                    {
                        Realm = pending.Realm,
                        Task = completed,
                        Promise = pending.Promise,
                        CompletionQueueKey = pending.CompletionQueueKey,
                        UseHostQueue = pending.UseHostQueue,
                        CanceledReasonFactory = pending.CanceledReasonFactory
                    };
                    if (pending.UseHostQueue)
                        pending.Realm.Agent.EnqueueHostTask(pending.CompletionQueueKey,
                            TaskPromiseCompletion<T>.CompletePromiseJob, queued);
                    else
                        pending.Realm.Agent.EnqueueMicrotask(TaskPromiseCompletion<T>.CompletePromiseJob, queued);
                },
                new PendingTaskResultPromiseState<T>
                {
                    Realm = this,
                    Task = task,
                    Promise = promise,
                    CompletionQueueKey = completionQueueKey,
                    UseHostQueue = useHostQueue,
                    CanceledReasonFactory = canceledReasonFactory
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return JsValue.FromObject(promise);
        }

        public JsValue WrapTask(ValueTask task)
        {
            if (task.IsCompletedSuccessfully)
                return this.PromiseResolveValue(JsValue.Undefined);
            return WrapValueTask(task, false, InternalHostTaskQueueDefaults.Default, null, null);
        }

        public JsValue WrapTask<T>(ValueTask<T> task)
        {
            if (task.IsCompletedSuccessfully)
                return this.PromiseResolveValue(WrapHostValue(task.Result));
            return WrapValueTask(task, false, InternalHostTaskQueueDefaults.Default, null, null);
        }

        public Task<JsValue> ToTask(JsValue value, CancellationToken cancellationToken = default)
        {
            if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
                return Task.FromResult(value);

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
                return Task.FromResult(promise.Result);
            if (promise.State == JsPromiseObject.PromiseState.Rejected)
                return Task.FromException<JsValue>(new PromiseRejectedException(promise.Result));

            var completionSource =
                new TaskCompletionSource<JsValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            var state = new PromiseTaskState
            {
                CompletionSource = completionSource
            };

            if (cancellationToken.CanBeCanceled)
                state.CancellationRegistration = cancellationToken.Register(
                    static sourceObj => { ((TaskCompletionSource<JsValue>)sourceObj!).TrySetCanceled(); },
                    completionSource);

            var resolve = new JsHostFunction(this, static (in info) =>
            {
                var host = (JsHostFunction)info.Function;
                var promiseState = (PromiseTaskState)host.UserData!;
                promiseState.CancellationRegistration.Dispose();
                promiseState.CompletionSource.TrySetResult(info.GetArgumentOrDefault(0, JsValue.Undefined));
                return JsValue.Undefined;
            }, string.Empty, 1)
            {
                UserData = state
            };

            var reject = new JsHostFunction(this, static (in info) =>
            {
                var host = (JsHostFunction)info.Function;
                var promiseState = (PromiseTaskState)host.UserData!;
                promiseState.CancellationRegistration.Dispose();
                promiseState.CompletionSource.TrySetException(
                    new PromiseRejectedException(info.GetArgumentOrDefault(0, JsValue.Undefined)));
                return JsValue.Undefined;
            }, string.Empty, 1)
            {
                UserData = state
            };

            this.PromiseThenNoCapability(promise, JsValue.FromObject(resolve), JsValue.FromObject(reject));
            return PumpJobsUntilAsync(completionSource.Task, cancellationToken);
        }

        public Task<T> ToTask<T>(JsValue value, CancellationToken cancellationToken = default)
        {
            if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject)
            {
                var converted = (T)HostValueConverter.ConvertFromJsValue(this, value, typeof(T))!;
                return Task.FromResult(converted);
            }

            return AwaitTaskResultAsync<T>(ToTask(value, cancellationToken), cancellationToken);
        }

        public ValueTask ToValueTask(JsValue value, CancellationToken cancellationToken = default)
        {
            if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
                return ValueTask.CompletedTask;

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
                return ValueTask.CompletedTask;
            if (promise.State == JsPromiseObject.PromiseState.Rejected)
                return ValueTask.FromException(new PromiseRejectedException(promise.Result));

            var source = new PromiseValueTaskSource();
            source.RegisterCancellation(cancellationToken);
            AttachPromiseValueTaskSource(promise, source);
            return new(source, source.Version);
        }

        public ValueTask<T> ToValueTask<T>(JsValue value, CancellationToken cancellationToken = default)
        {
            if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
            {
                var converted = (T)HostValueConverter.ConvertFromJsValue(this, value, typeof(T))!;
                return ValueTask.FromResult(converted);
            }

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
            {
                var converted = (T)HostValueConverter.ConvertFromJsValue(this, promise.Result, typeof(T))!;
                return ValueTask.FromResult(converted);
            }

            if (promise.State == JsPromiseObject.PromiseState.Rejected)
                return ValueTask.FromException<T>(new PromiseRejectedException(promise.Result));

            var source = new PromiseValueTaskSource<T>();
            source.RegisterCancellation(cancellationToken);
            AttachPromiseValueTaskSource(promise, source);
            return new(source, source.Version);
        }

        public ValueTask ToPumpedValueTask(JsValue value, CancellationToken cancellationToken = default)
        {
            if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
                return ValueTask.CompletedTask;

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
                return ValueTask.CompletedTask;
            if (promise.State == JsPromiseObject.PromiseState.Rejected)
                return ValueTask.FromException(new PromiseRejectedException(promise.Result));

            var source = new PumpedPromiseValueTaskSource(this, cancellationToken);
            AttachPromiseValueTaskSource(promise, source);
            return new(source, source.Version);
        }

        public ValueTask<T> ToPumpedValueTask<T>(JsValue value, CancellationToken cancellationToken = default)
        {
            if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
            {
                var converted = (T)HostValueConverter.ConvertFromJsValue(this, value, typeof(T))!;
                return ValueTask.FromResult(converted);
            }

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
            {
                var converted = (T)HostValueConverter.ConvertFromJsValue(this, promise.Result, typeof(T))!;
                return ValueTask.FromResult(converted);
            }

            if (promise.State == JsPromiseObject.PromiseState.Rejected)
                return ValueTask.FromException<T>(new PromiseRejectedException(promise.Result));

            var source = new PumpedPromiseValueTaskSource<T>(this, cancellationToken);
            AttachPromiseValueTaskSource(promise, source);
            return new(source, source.Version);
        }

        public ValueTask<JsValue> CallAsync(JsFunction function, JsValue thisValue, params ReadOnlySpan<JsValue> args)
        {
            return ToPumpedValueTask<JsValue>(Call(function, thisValue, args));
        }

        public ValueTask<JsValue> CallAsync(JsValue function, JsValue thisValue, params ReadOnlySpan<JsValue> args)
        {
            return ToPumpedValueTask<JsValue>(Call(function, thisValue, args));
        }

        public ValueTask<JsValue> EvalAsync(string script, CancellationToken cancellationToken = default)
        {
            return EvaluateAsync(script, cancellationToken);
        }

        internal static bool TryConvertTaskObjectToJsValue(JsRealm realm, object value, out JsValue jsValue)
        {
            if (value is Task<JsValue> jsTask)
            {
                jsValue = realm.WrapTask(jsTask);
                return true;
            }

            if (value is Task task)
            {
                if (realm.Engine.ClrAccessProvider is { } provider &&
                    provider.TryConvertTaskObjectToJsValue(realm, value, out jsValue))
                    return true;

                jsValue = realm.WrapTask(task);
                return true;
            }

            var valueType = value.GetType();
            if (valueType == typeof(ValueTask))
            {
                jsValue = realm.WrapTask((ValueTask)value);
                return true;
            }

            if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                if (realm.Engine.ClrAccessProvider is { } provider &&
                    provider.TryConvertTaskObjectToJsValue(realm, value, out jsValue))
                    return true;

            jsValue = JsValue.Undefined;
            return false;
        }

        internal static bool TryConvertJsValueToTaskObject(JsRealm realm, JsValue value, Type targetType,
            out object? result,
            out int score)
        {
            score = 0;

            if (targetType == typeof(Task))
            {
                result = realm.ToTask(value);
                return true;
            }

            if (targetType == typeof(ValueTask))
            {
                result = realm.ToValueTask(value);
                return true;
            }

            if (realm.Engine.ClrAccessProvider is { } provider &&
                provider.TryConvertJsValueToTaskObject(realm, value, targetType, out result, out score))
                return true;

            result = null;
            return false;
        }

        private async Task<T> AwaitTaskResultAsync<T>(Task<JsValue> task, CancellationToken cancellationToken)
        {
            var settled = await PumpJobsUntilAsync(task, cancellationToken).ConfigureAwait(false);
            return (T)HostValueConverter.ConvertFromJsValue(this, settled, typeof(T))!;
        }

        private void AttachPromiseValueTaskSource(JsPromiseObject promise, PromiseValueTaskSource source)
        {
            AttachPromiseValueTaskSourceCore(promise, source,
                static (_, _, state) => ((PromiseValueTaskSource)state).TrySetResult(),
                static (reason, state) =>
                    ((PromiseValueTaskSource)state).TrySetException(new PromiseRejectedException(reason)));
        }

        private void AttachPromiseValueTaskSource(JsPromiseObject promise, PumpedPromiseValueTaskSource source)
        {
            AttachPromiseValueTaskSourceCore(promise, source,
                static (_, _, state) => ((PumpedPromiseValueTaskSource)state).TrySetResult(),
                static (reason, state) =>
                    ((PumpedPromiseValueTaskSource)state).TrySetException(new PromiseRejectedException(reason)));
        }

        private void AttachPromiseValueTaskSource<T>(JsPromiseObject promise, PromiseValueTaskSource<T> source)
        {
            AttachPromiseValueTaskSourceCore(promise, source,
                static (realm, resolved, state) =>
                {
                    var converted = (T)HostValueConverter.ConvertFromJsValue(realm, resolved, typeof(T))!;
                    ((PromiseValueTaskSource<T>)state).TrySetResult(converted);
                },
                static (reason, state) =>
                    ((PromiseValueTaskSource<T>)state).TrySetException(new PromiseRejectedException(reason)));
        }

        private void AttachPromiseValueTaskSource<T>(JsPromiseObject promise, PumpedPromiseValueTaskSource<T> source)
        {
            AttachPromiseValueTaskSourceCore(promise, source,
                static (realm, resolved, state) =>
                {
                    var converted = (T)HostValueConverter.ConvertFromJsValue(realm, resolved, typeof(T))!;
                    ((PumpedPromiseValueTaskSource<T>)state).TrySetResult(converted);
                },
                static (reason, state) =>
                    ((PumpedPromiseValueTaskSource<T>)state).TrySetException(new PromiseRejectedException(reason)));
        }

        private void AttachPromiseValueTaskSourceCore(JsPromiseObject promise, object source,
            Action<JsRealm, JsValue, object> resolveAction,
            Action<JsValue, object> rejectAction)
        {
            var resolve = new JsHostFunction(this, static (in info) =>
            {
                var host = (JsHostFunction)info.Function;
                var state = (PromiseValueTaskSourceAttachment)host.UserData!;
                state.ResolveAction(info.Realm, info.GetArgumentOrDefault(0, JsValue.Undefined), state.Source);
                return JsValue.Undefined;
            }, string.Empty, 1)
            {
                UserData = new PromiseValueTaskSourceAttachment(source, resolveAction, rejectAction)
            };

            var reject = new JsHostFunction(this, static (in info) =>
            {
                var host = (JsHostFunction)info.Function;
                var state = (PromiseValueTaskSourceAttachment)host.UserData!;
                state.RejectAction(info.GetArgumentOrDefault(0, JsValue.Undefined), state.Source);
                return JsValue.Undefined;
            }, string.Empty, 1)
            {
                UserData = new PromiseValueTaskSourceAttachment(source, resolveAction, rejectAction)
            };

            this.PromiseThenNoCapability(promise, JsValue.FromObject(resolve), JsValue.FromObject(reject));
        }

        internal JsValue WrapTask(ValueTask task, Func<JsValue> canceledReasonFactory,
            Action? cleanupAction = null)
        {
            ArgumentNullException.ThrowIfNull(canceledReasonFactory);
            return WrapValueTask(task, false, InternalHostTaskQueueDefaults.Default, canceledReasonFactory,
                cleanupAction);
        }

        internal JsValue WrapTask<T>(ValueTask<T> task, Func<JsValue> canceledReasonFactory,
            Action? cleanupAction = null)
        {
            ArgumentNullException.ThrowIfNull(canceledReasonFactory);
            return WrapValueTask(task, false, InternalHostTaskQueueDefaults.Default, canceledReasonFactory,
                cleanupAction);
        }

        private JsValue WrapValueTask(ValueTask task, bool useHostQueue, HostTaskQueueKey completionQueueKey,
            Func<JsValue>? canceledReasonFactory, Action? cleanupAction)
        {
            var promise = this.CreatePromiseObject();
            var awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                CompletePromiseFromValueTask(awaiter, promise, canceledReasonFactory, cleanupAction);
                return JsValue.FromObject(promise);
            }

            var state = new PendingValueTaskPromiseState
            {
                Realm = this,
                Promise = promise,
                CompletionQueueKey = completionQueueKey,
                UseHostQueue = useHostQueue,
                CanceledReasonFactory = canceledReasonFactory,
                CleanupAction = cleanupAction,
                Awaiter = awaiter
            };
            awaiter.OnCompleted(state.OnCompleted);
            return JsValue.FromObject(promise);
        }

        private JsValue WrapValueTask<T>(ValueTask<T> task, bool useHostQueue, HostTaskQueueKey completionQueueKey,
            Func<JsValue>? canceledReasonFactory, Action? cleanupAction)
        {
            var promise = this.CreatePromiseObject();
            var awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                CompletePromiseFromValueTask(awaiter, promise, canceledReasonFactory, cleanupAction);
                return JsValue.FromObject(promise);
            }

            var state = new PendingValueTaskResultPromiseState<T>
            {
                Realm = this,
                Promise = promise,
                CompletionQueueKey = completionQueueKey,
                UseHostQueue = useHostQueue,
                CanceledReasonFactory = canceledReasonFactory,
                CleanupAction = cleanupAction,
                Awaiter = awaiter
            };
            awaiter.OnCompleted(state.OnCompleted);
            return JsValue.FromObject(promise);
        }

        private void EnqueueValueTaskPromiseCompletion(PendingValueTaskPromiseState state)
        {
            state.CleanupAction?.Invoke();
            state.CleanupAction = null;
            if (state.UseHostQueue)
                Agent.EnqueueHostTask(state.CompletionQueueKey, static boxed =>
                {
                    var pending = (PendingValueTaskPromiseState)boxed!;
                    pending.Realm.CompletePromiseFromValueTask(pending);
                }, state);
            else
                Agent.EnqueueMicrotask(static boxed =>
                {
                    var pending = (PendingValueTaskPromiseState)boxed!;
                    pending.Realm.CompletePromiseFromValueTask(pending);
                }, state);
        }

        private void EnqueueValueTaskPromiseCompletion<T>(PendingValueTaskResultPromiseState<T> state)
        {
            state.CleanupAction?.Invoke();
            state.CleanupAction = null;
            if (state.UseHostQueue)
                Agent.EnqueueHostTask(state.CompletionQueueKey, static boxed =>
                {
                    var pending = (PendingValueTaskResultPromiseState<T>)boxed!;
                    pending.Realm.CompletePromiseFromValueTask(pending);
                }, state);
            else
                Agent.EnqueueMicrotask(static boxed =>
                {
                    var pending = (PendingValueTaskResultPromiseState<T>)boxed!;
                    pending.Realm.CompletePromiseFromValueTask(pending);
                }, state);
        }

        private void CompletePromiseFromValueTask(ValueTaskAwaiter awaiter, JsPromiseObject promise,
            Func<JsValue>? canceledReasonFactory, Action? cleanupAction)
        {
            try
            {
                awaiter.GetResult();
                this.ResolvePromise(promise, JsValue.Undefined);
            }
            catch (Exception ex)
            {
                if (!TryCompleteCanceledPromise(ex, promise, canceledReasonFactory))
                    this.RejectPromise(promise, GetTaskFaultReason(ex));
            }
            finally
            {
                cleanupAction?.Invoke();
            }
        }

        private void CompletePromiseFromValueTask<T>(ValueTaskAwaiter<T> awaiter, JsPromiseObject promise,
            Func<JsValue>? canceledReasonFactory, Action? cleanupAction)
        {
            try
            {
                var result = awaiter.GetResult();
                this.ResolvePromiseWithAssimilation(promise, WrapHostValue(result!));
            }
            catch (Exception ex)
            {
                if (!TryCompleteCanceledPromise(ex, promise, canceledReasonFactory))
                    this.RejectPromise(promise, GetTaskFaultReason(ex));
            }
            finally
            {
                cleanupAction?.Invoke();
            }
        }

        private void CompletePromiseFromValueTask(PendingValueTaskPromiseState state)
        {
            CompletePromiseFromValueTask(state.Awaiter, state.Promise, state.CanceledReasonFactory, null);
        }

        private void CompletePromiseFromValueTask<T>(PendingValueTaskResultPromiseState<T> state)
        {
            CompletePromiseFromValueTask(state.Awaiter, state.Promise, state.CanceledReasonFactory, null);
        }

        private bool TryCompleteCanceledPromise(Exception ex, JsPromiseObject promise,
            Func<JsValue>? canceledReasonFactory)
        {
            if (ex is OperationCanceledException)
            {
                if (TryGetCanceledReason(canceledReasonFactory, out var canceledReason))
                {
                    this.RejectPromise(promise, canceledReason);
                    return true;
                }

                this.RejectPromise(promise, CreateHostExceptionValue(ex));
                return true;
            }

            return false;
        }

        private async Task PumpJobsUntilAsync(Task target, CancellationToken cancellationToken)
        {
            while (!target.IsCompleted)
            {
                PumpJobs();
                if (target.IsCompleted)
                    break;

                await WaitForJobsOrCompletionAsync(target, cancellationToken).ConfigureAwait(false);
            }

            PumpJobs();
            await target.ConfigureAwait(false);
        }

        private async Task<T> PumpJobsUntilAsync<T>(Task<T> target, CancellationToken cancellationToken)
        {
            while (!target.IsCompleted)
            {
                PumpJobs();
                if (target.IsCompleted)
                    break;

                await WaitForJobsOrCompletionAsync(target, cancellationToken).ConfigureAwait(false);
            }

            PumpJobs();
            return await target.ConfigureAwait(false);
        }

        private async Task WaitForJobsOrCompletionAsync(Task target, CancellationToken cancellationToken)
        {
            var jobSignal = WaitForJobsAsync(cancellationToken);
            var completed = await Task.WhenAny(target, jobSignal).ConfigureAwait(false);
            if (!ReferenceEquals(completed, target))
                await jobSignal.ConfigureAwait(false);
        }

        private async Task WaitForJobsAsync(CancellationToken cancellationToken)
        {
            await Engine.Options.HostServices.BackgroundScheduler
                .WaitHandleAsync(Agent.JobsAvailableWaitHandle, cancellationToken)
                .ConfigureAwait(false);
        }

        private void CompletePromiseFromTask(Task task, JsPromiseObject promise, Func<JsValue>? canceledReasonFactory)
        {
            if (task.IsCanceled)
            {
                if (TryGetCanceledReason(canceledReasonFactory, out var canceledReason))
                {
                    this.RejectPromise(promise, canceledReason);
                    return;
                }

                this.RejectPromise(promise, CreateHostExceptionValue(new TaskCanceledException(task)));
                return;
            }

            if (task.IsFaulted)
            {
                this.RejectPromise(promise, GetTaskFaultReason(task.Exception?.InnerException ?? task.Exception!));
                return;
            }

            this.ResolvePromise(promise, JsValue.Undefined);
        }

        private void CompletePromiseFromTask<T>(Task<T> task, JsPromiseObject promise,
            Func<JsValue>? canceledReasonFactory)
        {
            if (task.IsCanceled)
            {
                if (TryGetCanceledReason(canceledReasonFactory, out var canceledReason))
                {
                    this.RejectPromise(promise, canceledReason);
                    return;
                }

                this.RejectPromise(promise, CreateHostExceptionValue(new TaskCanceledException(task)));
                return;
            }

            if (task.IsFaulted)
            {
                this.RejectPromise(promise, GetTaskFaultReason(task.Exception?.InnerException ?? task.Exception!));
                return;
            }

            this.ResolvePromiseWithAssimilation(promise, WrapHostValue(task.Result));
        }

        private bool TryGetCanceledReason(Func<JsValue>? canceledReasonFactory, out JsValue canceledReason)
        {
            if (canceledReasonFactory is not null)
            {
                canceledReason = canceledReasonFactory();
                if (!canceledReason.IsUndefined)
                    return true;
            }

            canceledReason = JsValue.Undefined;
            return false;
        }

        private JsValue GetTaskFaultReason(Exception ex)
        {
            if (ex is JsRuntimeException okojoEx)
                return okojoEx.ThrownValue ?? CreateErrorObjectFromException(okojoEx);
            return CreateHostExceptionValue(ex);
        }

        private JsValue CreateHostExceptionValue(Exception ex)
        {
            var err = new JsPlainObject(this, false)
            {
                Prototype = ErrorPrototype
            };
            err.DefineDataPropertyAtom(this, IdName, JsValue.FromString(ex.GetType().Name),
                JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
            err.DefineDataPropertyAtom(this, IdMessage, JsValue.FromString(ex.Message ?? string.Empty),
                JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
            return JsValue.FromObject(err);
        }

        private sealed class PromiseValueTaskSourceAttachment(
            object source,
            Action<JsRealm, JsValue, object> resolveAction,
            Action<JsValue, object> rejectAction)
        {
            public object Source { get; } = source;
            public Action<JsRealm, JsValue, object> ResolveAction { get; } = resolveAction;
            public Action<JsValue, object> RejectAction { get; } = rejectAction;
        }

        private sealed class PromiseTaskState
        {
            public CancellationTokenRegistration CancellationRegistration;
            public required TaskCompletionSource<JsValue> CompletionSource;
        }

        private sealed class PendingTaskPromiseState
        {
            public Func<JsValue>? CanceledReasonFactory;
            public HostTaskQueueKey CompletionQueueKey;
            public required JsPromiseObject Promise;
            public required JsRealm Realm;
            public required Task Task;
            public bool UseHostQueue;
        }

        private sealed class PendingTaskResultPromiseState<T>
        {
            public Func<JsValue>? CanceledReasonFactory;
            public HostTaskQueueKey CompletionQueueKey;
            public required JsPromiseObject Promise;
            public required JsRealm Realm;
            public required Task<T> Task;
            public bool UseHostQueue;
        }

        private sealed class PendingValueTaskPromiseState
        {
            public required ValueTaskAwaiter Awaiter;
            public Func<JsValue>? CanceledReasonFactory;
            public Action? CleanupAction;
            public HostTaskQueueKey CompletionQueueKey;
            public required JsPromiseObject Promise;
            public required JsRealm Realm;
            public bool UseHostQueue;

            public void OnCompleted()
            {
                Realm.EnqueueValueTaskPromiseCompletion(this);
            }
        }

        private sealed class PendingValueTaskResultPromiseState<T>
        {
            public required ValueTaskAwaiter<T> Awaiter;
            public Func<JsValue>? CanceledReasonFactory;
            public Action? CleanupAction;
            public HostTaskQueueKey CompletionQueueKey;
            public required JsPromiseObject Promise;
            public required JsRealm Realm;
            public bool UseHostQueue;

            public void OnCompleted()
            {
                Realm.EnqueueValueTaskPromiseCompletion(this);
            }
        }

        private static class TaskPromiseCompletion<T>
        {
            internal static readonly Action<object?> CompletePromiseJob = static state =>
            {
                var completion = (PendingTaskResultPromiseState<T>)state!;
                completion.Realm.CompletePromiseFromTask(completion.Task, completion.Promise,
                    completion.CanceledReasonFactory);
            };
        }
    }
}
