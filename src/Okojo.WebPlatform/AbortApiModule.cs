using System.Runtime.CompilerServices;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.WebPlatform;

public sealed class AbortApiModule : IRealmApiModule
{
    private readonly Func<JsRealm, IHostDelayScheduler> delaySchedulerFactory;

    private readonly ConditionalWeakTable<JsRealm, RealmState> realmStates = new();
    private readonly HostTaskQueueKey timerQueueKey;

    public AbortApiModule(Func<JsRealm, IHostDelayScheduler> delaySchedulerFactory, HostTaskQueueKey timerQueueKey)
    {
        ArgumentNullException.ThrowIfNull(delaySchedulerFactory);
        this.delaySchedulerFactory = delaySchedulerFactory;
        this.timerQueueKey = timerQueueKey;
    }

    public static AbortApiModule Shared { get; } = new(
        static realm => new TimeProviderDelayScheduler(realm.Engine.TimeProvider),
        WebTaskQueueKeys.Timers);

    public void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        if (!realm.Global.TryGetValue("AbortController", out _))
            realm.Global["AbortController"] = JsValue.FromObject(GetAbortControllerConstructor(realm));
        if (!realm.Global.TryGetValue("AbortSignal", out _))
            realm.Global["AbortSignal"] = JsValue.FromObject(GetAbortSignalConstructor(realm));
    }

    private RealmState GetState(JsRealm realm)
    {
        return realmStates.GetValue(realm, static _ => new());
    }

    private JsHostFunction GetAbortControllerConstructor(JsRealm realm)
    {
        var state = GetState(realm);
        if (state.AbortControllerConstructor is not null)
            return state.AbortControllerConstructor;

        var prototype = GetAbortControllerPrototype(realm);
        var ctor = new JsHostFunction(realm, "AbortController", 0, static (in info) =>
        {
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Class constructor AbortController cannot be invoked without 'new'");

            var constructorState = (ConstructorState)((JsHostFunction)info.Function).UserData!;
            return JsValue.FromObject(constructorState.Owner.CreateAbortController(constructorState.Realm));
        })
        {
            UserData = new ConstructorState(this, realm)
        };
        state.AbortControllerConstructor = ctor;
        ctor.DefineDataProperty("prototype", JsValue.FromObject(prototype), JsShapePropertyFlags.Open);
        prototype.DefineDataProperty("constructor", JsValue.FromObject(ctor), JsShapePropertyFlags.Open);
        return ctor;
    }

    private JsHostFunction GetAbortSignalConstructor(JsRealm realm)
    {
        var state = GetState(realm);
        if (state.AbortSignalConstructor is not null)
            return state.AbortSignalConstructor;

        var prototype = GetAbortSignalPrototype(realm);
        var ctor = new JsHostFunction(realm, "AbortSignal", 0,
            static (in _) => { throw new JsRuntimeException(JsErrorKind.TypeError, "Illegal constructor"); });
        state.AbortSignalConstructor = ctor;
        ctor.DefineDataProperty("prototype", JsValue.FromObject(prototype), JsShapePropertyFlags.Open);
        ctor.DefineDataProperty("timeout", JsValue.FromObject(CreateTimeoutMethod(realm)), JsShapePropertyFlags.Open);
        prototype.DefineDataProperty("constructor", JsValue.FromObject(ctor), JsShapePropertyFlags.Open);
        return ctor;
    }

    private JsPlainObject CreateAbortController(JsRealm realm)
    {
        var controller = new JsPlainObject(realm, false, true)
        {
            Prototype = GetAbortControllerPrototype(realm)
        };

        var signal = CreateAbortSignal(realm);
        controller.DefineDataProperty("signal", JsValue.FromObject(signal), JsShapePropertyFlags.Open);
        controller.DefineDataProperty("abort", JsValue.FromObject(CreateAbortMethod(realm, signal)),
            JsShapePropertyFlags.Open);
        return controller;
    }

    private JsUserDataObject<AbortSignalState> CreateAbortSignal(JsRealm realm)
    {
        var signal = new JsUserDataObject<AbortSignalState>(realm, false, true)
        {
            Prototype = GetAbortSignalPrototype(realm),
            UserData = new()
        };
        signal.DefineDataProperty("aborted", JsValue.False, JsShapePropertyFlags.Open);
        signal.DefineDataProperty("reason", JsValue.Undefined, JsShapePropertyFlags.Open);
        signal.DefineDataProperty("onabort", JsValue.Null, JsShapePropertyFlags.Open);
        return signal;
    }

    private JsPlainObject GetAbortControllerPrototype(JsRealm realm)
    {
        var state = GetState(realm);
        if (state.AbortControllerPrototype is not null)
            return state.AbortControllerPrototype;

        var prototype = new JsPlainObject(realm, false, true);
        state.AbortControllerPrototype = prototype;
        return prototype;
    }

    private JsPlainObject GetAbortSignalPrototype(JsRealm realm)
    {
        var state = GetState(realm);
        if (state.AbortSignalPrototype is not null)
            return state.AbortSignalPrototype;

        var prototype = new JsPlainObject(realm, false, true);
        state.AbortSignalPrototype = prototype;
        prototype.DefineDataProperty("addEventListener", JsValue.FromObject(CreateAddEventListenerFunction(realm)),
            JsShapePropertyFlags.Open);
        prototype.DefineDataProperty("removeEventListener",
            JsValue.FromObject(CreateRemoveEventListenerFunction(realm)), JsShapePropertyFlags.Open);
        prototype.DefineDataProperty("dispatchEvent", JsValue.FromObject(CreateDispatchEventFunction(realm)),
            JsShapePropertyFlags.Open);
        prototype.DefineDataProperty("throwIfAborted", JsValue.FromObject(CreateThrowIfAbortedFunction(realm)),
            JsShapePropertyFlags.Open);
        return prototype;
    }

    private JsHostFunction CreateAbortMethod(JsRealm realm, JsUserDataObject<AbortSignalState> signal)
    {
        return new(realm, "abort", 0, static (in info) =>
        {
            var targetSignal = (JsUserDataObject<AbortSignalState>)((JsHostFunction)info.Function).UserData!;
            AbortSignal(targetSignal, info.GetArgument(0));
            return JsValue.Undefined;
        }, false)
        {
            UserData = signal
        };
    }

    private JsHostFunction CreateTimeoutMethod(JsRealm realm)
    {
        return new(realm, "timeout", 1, static (in info) =>
        {
            var module = (AbortApiModule)((JsHostFunction)info.Function).UserData!;
            var delayMs = NormalizeDelay(info.GetArgumentOrDefault(0, JsValue.Undefined));
            var signal = module.CreateAbortSignal(info.Realm);
            module.ScheduleTimeout(signal, delayMs);
            return JsValue.FromObject(signal);
        }, false)
        {
            UserData = this
        };
    }

    private static JsHostFunction CreateAddEventListenerFunction(JsRealm realm)
    {
        return new(realm, "addEventListener", 2, static (in info) =>
        {
            var signal = GetSignal(info);
            var type = info.GetArgument(0);
            if (!type.IsString || !string.Equals(type.AsString(), "abort", StringComparison.Ordinal))
                return JsValue.Undefined;

            var callbackValue = info.GetArgument(1);
            if (!callbackValue.TryGetObject(out var callbackObject) || callbackObject is not JsFunction callback)
                return JsValue.Undefined;

            signal.UserData!.AbortListeners.Add(callback);
            return JsValue.Undefined;
        }, false);
    }

    private static JsHostFunction CreateRemoveEventListenerFunction(JsRealm realm)
    {
        return new(realm, "removeEventListener", 2, static (in info) =>
        {
            var signal = GetSignal(info);
            var type = info.GetArgument(0);
            if (!type.IsString || !string.Equals(type.AsString(), "abort", StringComparison.Ordinal))
                return JsValue.Undefined;

            var callbackValue = info.GetArgument(1);
            if (!callbackValue.TryGetObject(out var callbackObject) || callbackObject is not JsFunction callback)
                return JsValue.Undefined;

            for (var i = signal.UserData!.AbortListeners.Count - 1; i >= 0; i--)
                if (ReferenceEquals(signal.UserData.AbortListeners[i], callback))
                    signal.UserData.AbortListeners.RemoveAt(i);

            if (signal.TryGetProperty("onabort", out var onAbortValue) &&
                onAbortValue.TryGetObject(out var onAbortObject) &&
                ReferenceEquals(onAbortObject, callbackObject))
            {
                signal.UserData.OnAbort = null;
                signal.SetProperty("onabort", JsValue.Null);
            }

            return JsValue.Undefined;
        }, false);
    }

    private static JsHostFunction CreateDispatchEventFunction(JsRealm realm)
    {
        return new(realm, "dispatchEvent", 1, static (in info) =>
        {
            var signal = GetSignal(info);
            var eventValue = info.GetArgument(0);
            if (!eventValue.TryGetObject(out var eventObject))
                return JsValue.False;

            string? type = null;
            if (eventObject.TryGetProperty("type", out var typeValue) && typeValue.IsString)
                type = typeValue.AsString();
            if (!string.Equals(type, "abort", StringComparison.Ordinal))
                return JsValue.False;

            DispatchAbort(signal, eventObject);
            return JsValue.True;
        }, false);
    }

    private static JsHostFunction CreateThrowIfAbortedFunction(JsRealm realm)
    {
        return new(realm, "throwIfAborted", 0, static (in info) =>
        {
            var signal = GetSignal(info);
            if (signal.UserData!.Aborted)
                throw new JsRuntimeException(JsErrorKind.InternalError,
                    signal.UserData.Reason.IsUndefined
                        ? "This operation was aborted"
                        : info.Realm.ToJsStringSlowPath(signal.UserData.Reason));
            return JsValue.Undefined;
        }, false);
    }

    private static JsUserDataObject<AbortSignalState> GetSignal(in CallInfo info)
    {
        if (info.ThisValue.TryGetObject(out var thisObject) &&
            thisObject is JsUserDataObject<AbortSignalState> signal &&
            signal.UserData is not null)
            return signal;

        throw new JsRuntimeException(JsErrorKind.TypeError, "AbortSignal method called on incompatible receiver");
    }

    private static void AbortSignal(JsUserDataObject<AbortSignalState> signal, JsValue reason)
    {
        var state = signal.UserData!;
        if (state.Aborted)
            return;

        state.Aborted = true;
        state.Reason = reason.IsUndefined ? JsValue.FromString("AbortError") : reason;
        ReleaseTimeout(state);
        signal.SetProperty("aborted", JsValue.True);
        signal.SetProperty("reason", state.Reason);
        NotifyHostAbort(state);

        var evt = new JsPlainObject(signal.Realm, useDictionaryMode: true);
        evt.DefineDataProperty("type", JsValue.FromString("abort"), JsShapePropertyFlags.Open);
        evt.DefineDataProperty("target", JsValue.FromObject(signal), JsShapePropertyFlags.Open);
        DispatchAbort(signal, evt);
    }

    internal static AbortRegistration Link(JsValue signalValue, CancellationToken cancellationToken = default)
    {
        if (signalValue.IsUndefined || signalValue.IsNull)
            return new(null, cancellationToken);

        if (signalValue.TryGetObject(out var signalObject) &&
            signalObject is JsUserDataObject<AbortSignalState> signal &&
            signal.UserData is not null)
            return new(signal, cancellationToken);

        throw new ArgumentException("Value is not an AbortSignal.", nameof(signalValue));
    }

    private static void DispatchAbort(JsUserDataObject<AbortSignalState> signal, JsObject evt)
    {
        var state = signal.UserData!;
        var listeners = state.AbortListeners.ToArray();
        for (var i = 0; i < listeners.Length; i++)
            _ = signal.Realm.Call(listeners[i], JsValue.FromObject(signal), JsValue.FromObject(evt));

        if (signal.TryGetProperty("onabort", out var onAbortValue) &&
            onAbortValue.TryGetObject(out var onAbortObject) &&
            onAbortObject is JsFunction onAbort)
        {
            state.OnAbort = onAbort;
            _ = signal.Realm.Call(onAbort, JsValue.FromObject(signal), JsValue.FromObject(evt));
        }
    }

    private static void NotifyHostAbort(AbortSignalState state)
    {
        var listeners = state.HostAbortListeners.ToArray();
        for (var i = 0; i < listeners.Length; i++)
            listeners[i](state.Reason);
    }

    private void ScheduleTimeout(JsUserDataObject<AbortSignalState> signal, int delayMs)
    {
        var state = signal.UserData!;
        ReleaseTimeout(state);
        var dueTime = delayMs <= 0 ? TimeSpan.FromTicks(1) : TimeSpan.FromMilliseconds(delayMs);
        var delayScheduler = delaySchedulerFactory(signal.Realm);
        if (delayScheduler is IQueuedHostDelayScheduler queuedDelayScheduler)
        {
            state.TimeoutOperation = queuedDelayScheduler.ScheduleDelayed(dueTime, timerQueueKey, static timeoutState =>
            {
                var targetSignal = (JsUserDataObject<AbortSignalState>)timeoutState!;
                AbortSignal(targetSignal, CreateTimeoutReason(targetSignal.Realm));
            }, signal);
            return;
        }

        var driver = new JsHostFunction(signal.Realm, static (in info) =>
        {
            var targetSignal = (JsUserDataObject<AbortSignalState>)((JsHostFunction)info.Function).UserData!;
            AbortSignal(targetSignal, CreateTimeoutReason(info.Realm));
            return JsValue.Undefined;
        }, "AbortSignal.timeout callback", 0)
        {
            UserData = signal
        };

        state.TimeoutOperation = delayScheduler.ScheduleDelayed(dueTime, static timeoutState =>
        {
            var fn = (JsHostFunction)timeoutState!;
            fn.Realm.QueueHostTask(fn);
        }, driver);
    }

    private static void ReleaseTimeout(AbortSignalState state)
    {
        state.TimeoutOperation?.Dispose();
        state.TimeoutOperation = null;
    }

    private static JsValue CreateTimeoutReason(JsRealm realm)
    {
        var error = new JsPlainObject(realm, false, true);
        error.DefineDataProperty("name", JsValue.FromString("TimeoutError"), JsShapePropertyFlags.Open);
        error.DefineDataProperty("message", JsValue.FromString("The operation timed out."), JsShapePropertyFlags.Open);
        return JsValue.FromObject(error);
    }

    private static int NormalizeDelay(in JsValue value)
    {
        var delayNumber = value.NumberValue;
        if (double.IsNaN(delayNumber) || delayNumber <= 0)
            return 0;
        if (delayNumber >= int.MaxValue)
            return int.MaxValue;
        return (int)delayNumber;
    }

    internal sealed class AbortSignalState
    {
        public readonly List<JsFunction> AbortListeners = [];
        public readonly List<Action<JsValue>> HostAbortListeners = [];
        public bool Aborted;
        public JsFunction? OnAbort;
        public JsValue Reason = JsValue.Undefined;
        public IHostDelayedOperation? TimeoutOperation;
    }

    private sealed class ConstructorState(AbortApiModule owner, JsRealm realm)
    {
        public AbortApiModule Owner { get; } = owner;
        public JsRealm Realm { get; } = realm;
    }

    private sealed class RealmState
    {
        public JsHostFunction? AbortControllerConstructor;
        public JsPlainObject? AbortControllerPrototype;
        public JsHostFunction? AbortSignalConstructor;
        public JsPlainObject? AbortSignalPrototype;
    }
}

public static class AbortInterop
{
    public static AbortRegistration Link(JsValue signalValue, CancellationToken cancellationToken = default)
    {
        return AbortApiModule.Link(signalValue, cancellationToken);
    }
}

public sealed class AbortRegistration : IDisposable
{
    private readonly CancellationTokenSource? cancellationSource;
    private readonly Action<JsValue>? onAbort;
    private readonly JsUserDataObject<AbortApiModule.AbortSignalState>? signal;
    private bool disposed;

    internal AbortRegistration(JsUserDataObject<AbortApiModule.AbortSignalState>? signal,
        CancellationToken cancellationToken)
    {
        this.signal = signal;
        cancellationSource = signal is null && !cancellationToken.CanBeCanceled
            ? null
            : cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new();

        if (signal?.UserData is null)
        {
            Token = cancellationSource?.Token ?? CancellationToken.None;
            return;
        }

        Token = cancellationSource?.Token ?? CancellationToken.None;
        if (signal.UserData.Aborted)
        {
            Reason = signal.UserData.Reason;
            cancellationSource?.Cancel();
            return;
        }

        onAbort = reason =>
        {
            Reason = reason;
            cancellationSource?.Cancel();
        };
        signal.UserData.HostAbortListeners.Add(onAbort);
    }

    public CancellationToken Token { get; }
    public JsValue Reason { get; private set; } = JsValue.Undefined;
    public bool IsAborted => !Reason.IsUndefined;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (signal?.UserData is not null && onAbort is not null)
            signal.UserData.HostAbortListeners.Remove(onAbort);
        cancellationSource?.Dispose();
    }

    public JsValue WrapTask(JsRealm realm, Task task)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(task);
        AttachDispose(task);
        return realm.WrapTask(task, GetCanceledReason);
    }

    public JsValue WrapTaskOnHostQueue(JsRealm realm, Task task, HostTaskQueueKey completionQueueKey)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(task);
        AttachDispose(task);
        return realm.WrapTaskOnHostQueue(task, completionQueueKey, GetCanceledReason);
    }

    public JsValue WrapTask<T>(JsRealm realm, Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(task);
        AttachDispose(task);
        return realm.WrapTask(task, GetCanceledReason);
    }

    public JsValue WrapTask(JsRealm realm, ValueTask task, CancellationTokenSource? cancellationSource = null,
        IDisposable? disposable = null)
    {
        ArgumentNullException.ThrowIfNull(realm);
        AttachDispose(disposable, cancellationSource);
        return realm.WrapTask(task, GetCanceledReason, () => DisposeResources(cancellationSource, disposable));
    }

    public JsValue WrapTask<T>(JsRealm realm, ValueTask<T> task, CancellationTokenSource? cancellationSource = null,
        IDisposable? disposable = null)
    {
        ArgumentNullException.ThrowIfNull(realm);
        AttachDispose(disposable, cancellationSource);
        return realm.WrapTask(task, GetCanceledReason, () => DisposeResources(cancellationSource, disposable));
    }

    public JsValue WrapTaskOnHostQueue<T>(JsRealm realm, Task<T> task, HostTaskQueueKey completionQueueKey)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(task);
        AttachDispose(task);
        return realm.WrapTaskOnHostQueue(task, completionQueueKey, GetCanceledReason);
    }

    private JsValue GetCanceledReason()
    {
        return IsAborted ? Reason : JsValue.Undefined;
    }

    private void AttachDispose(Task task)
    {
        _ = task.ContinueWith(static (_, state) => ((AbortRegistration)state!).Dispose(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void AttachDispose(IDisposable? disposable, CancellationTokenSource? cancellationSource)
    {
        if (IsAborted)
            DisposeResources(cancellationSource, disposable);
    }

    private static void DisposeResources(CancellationTokenSource? cancellationSource, IDisposable? disposable)
    {
        disposable?.Dispose();
        cancellationSource?.Dispose();
    }
}
