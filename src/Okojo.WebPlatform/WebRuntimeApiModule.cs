using System.Runtime.CompilerServices;
using System.Text;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.WebPlatform;

public sealed class WebRuntimeApiModule : IRealmApiModule
{
    private readonly AbortApiModule abortApiModule;
    private readonly Func<JsRealm, IHostDelayScheduler> delaySchedulerFactory;

    private readonly ConditionalWeakTable<JsRealm, RealmWebRuntimeState> realmStates = new();
    private readonly HostTaskQueueKey timerQueueKey;

    public WebRuntimeApiModule(Func<JsRealm, IHostDelayScheduler> delaySchedulerFactory, HostTaskQueueKey timerQueueKey)
    {
        ArgumentNullException.ThrowIfNull(delaySchedulerFactory);
        this.delaySchedulerFactory = delaySchedulerFactory;
        this.timerQueueKey = timerQueueKey;
        abortApiModule = new(delaySchedulerFactory, timerQueueKey);
    }

    public static WebRuntimeApiModule Shared { get; } = new(
        static realm => new TimeProviderDelayScheduler(realm.Engine.TimeProvider),
        WebTaskQueueKeys.Timers);

    public void Install(JsRealm realm)
    {
        abortApiModule.Install(realm);
        if (!realm.Global.TryGetValue("atob", out _))
            realm.Global["atob"] = JsValue.FromObject(CreateAtobFunction(realm));
        if (!realm.Global.TryGetValue("btoa", out _))
            realm.Global["btoa"] = JsValue.FromObject(CreateBtoaFunction(realm));
        QueueMicrotaskApiModule.Shared.Install(realm);
        if (!realm.Global.TryGetValue("setTimeout", out _))
            realm.Global["setTimeout"] = JsValue.FromObject(CreateSetTimeoutFunction(realm, false));
        if (!realm.Global.TryGetValue("setInterval", out _))
            realm.Global["setInterval"] = JsValue.FromObject(CreateSetTimeoutFunction(realm, true));
        if (!realm.Global.TryGetValue("clearTimeout", out _))
            realm.Global["clearTimeout"] = JsValue.FromObject(CreateClearTimerFunction(realm, "clearTimeout"));
        if (!realm.Global.TryGetValue("clearInterval", out _))
            realm.Global["clearInterval"] = JsValue.FromObject(CreateClearTimerFunction(realm, "clearInterval"));
    }

    private RealmWebRuntimeState GetState(JsRealm realm)
    {
        return realmStates.GetValue(realm, static _ => new());
    }

    private JsHostFunction CreateAtobFunction(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            var input = info.GetArgumentStringOrDefault(0, string.Empty);
            try
            {
                var bytes = Convert.FromBase64String(input);
                return JsValue.FromString(Encoding.Latin1.GetString(bytes));
            }
            catch (FormatException)
            {
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "The string to be decoded is not correctly encoded.",
                    "WEBRUNTIME_ATOB_INVALID");
            }
        }, "atob", 1);
    }

    private JsHostFunction CreateBtoaFunction(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            var input = info.GetArgumentStringOrDefault(0, string.Empty);
            for (var i = 0; i < input.Length; i++)
                if (input[i] > 0xFF)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "The string to be encoded contains characters outside of the Latin1 range.",
                        "WEBRUNTIME_BTOA_INVALID");

            return JsValue.FromString(Convert.ToBase64String(Encoding.Latin1.GetBytes(input)));
        }, "btoa", 1);
    }

    private JsHostFunction CreateSetTimeoutFunction(JsRealm realm, bool repeat)
    {
        var name = repeat ? "setInterval" : "setTimeout";
        return new(realm, (in info) =>
        {
            if (info.Arguments.Length == 0 ||
                !info.Arguments[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callback)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"{name} callback is not a function",
                    repeat ? "SET_INTERVAL_CALLBACK_NOT_FUNCTION" : "SET_TIMEOUT_CALLBACK_NOT_FUNCTION");

            var delayMs = NormalizeDelay(info.GetArgumentOrDefault(1, JsValue.Undefined));
            var timerArgs = info.Arguments.Length > 2 ? info.Arguments[2..].ToArray() : Array.Empty<JsValue>();
            var timerId = CreateTimer(info.Realm, callback, delayMs, timerArgs, repeat);
            return JsValue.FromInt32(timerId);
        }, name, 2);
    }

    private JsHostFunction CreateClearTimerFunction(JsRealm realm, string name)
    {
        return new(realm, (in info) =>
        {
            if (info.Arguments.Length != 0)
                CancelTimer(info.Realm, ToTimerId(info.Arguments[0]));
            return JsValue.Undefined;
        }, name, 1);
    }

    private int CreateTimer(JsRealm realm, JsFunction callback, int delayMs, JsValue[] args, bool repeat)
    {
        var state = GetState(realm);
        var delayScheduler = delaySchedulerFactory(realm);
        TimerState timer;
        lock (state.Gate)
        {
            var publicTimerId = ++state.NextTimerId;
            var driver = new JsHostFunction(realm, static (in info) =>
            {
                var timerState = (TimerState)((JsHostFunction)info.Function).UserData!;
                return timerState.Repeat ? FireInterval(timerState) : FireTimeout(timerState);
            }, repeat ? "setInterval callback" : "setTimeout callback", 0);

            timer = new()
            {
                OwnerState = state,
                Realm = realm,
                PublicTimerId = publicTimerId,
                Callback = callback,
                Arguments = args,
                DelayMs = delayMs,
                Repeat = repeat,
                TimerQueueKey = timerQueueKey,
                DelayScheduler = delayScheduler,
                Driver = driver
            };
            driver.UserData = timer;
            state.Timers.Add(publicTimerId, timer);
            ScheduleTimer(timer);
        }

        return timer.PublicTimerId;
    }

    private void CancelTimer(JsRealm realm, int publicTimerId)
    {
        var state = GetState(realm);
        IHostDelayedOperation? operation = null;
        lock (state.Gate)
        {
            if (!state.Timers.Remove(publicTimerId, out var timer))
                return;
            timer.Active = false;
            operation = timer.ScheduledOperation;
            timer.ScheduledOperation = null;
        }

        operation?.Dispose();
    }

    private static JsValue FireTimeout(TimerState timer)
    {
        lock (timer.OwnerState.Gate)
        {
            if (!timer.Active)
                return JsValue.Undefined;

            timer.Active = false;
            timer.OwnerState.Timers.Remove(timer.PublicTimerId);
            ReleaseTimerHandle(timer);
        }

        return timer.Realm.Call(timer.Callback, JsValue.Undefined, timer.Arguments);
    }

    private static JsValue FireInterval(TimerState timer)
    {
        var shouldReschedule = false;
        try
        {
            return timer.Realm.Call(timer.Callback, JsValue.Undefined, timer.Arguments);
        }
        finally
        {
            lock (timer.OwnerState.Gate)
            {
                shouldReschedule = timer.Active && timer.OwnerState.Timers.ContainsKey(timer.PublicTimerId);
                if (shouldReschedule)
                    ScheduleTimer(timer);
                else
                    ReleaseTimerHandle(timer);
            }
        }
    }

    private static void ScheduleTimer(TimerState timer)
    {
        ReleaseTimerHandle(timer);
        var dueTime = timer.DelayMs <= 0 ? TimeSpan.FromTicks(1) : TimeSpan.FromMilliseconds(timer.DelayMs);
        if (timer.DelayScheduler is IQueuedHostDelayScheduler queuedDelayScheduler)
            timer.ScheduledOperation = queuedDelayScheduler.ScheduleDelayed(dueTime, timer.TimerQueueKey,
                static state =>
                {
                    var timerState = (TimerState)state!;
                    InvokeQueuedTimerTask(timerState);
                }, timer);
        else
            timer.ScheduledOperation = timer.DelayScheduler.ScheduleDelayed(dueTime, static state =>
            {
                var timerState = (TimerState)state!;
                timerState.Realm.QueueHostTask(timerState.Driver);
            }, timer);
    }

    private static void ReleaseTimerHandle(TimerState timer)
    {
        timer.ScheduledOperation?.Dispose();
        timer.ScheduledOperation = null;
    }

    private static void InvokeQueuedTimerTask(TimerState timerState)
    {
        try
        {
            _ = timerState.Realm.InvokeFunction(timerState.Driver, JsValue.Undefined, []);
        }
        catch (JsRuntimeException)
        {
        }
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

    private static int ToTimerId(in JsValue value)
    {
        return value.IsInt32 ? value.Int32Value : (int)value.NumberValue;
    }

    private sealed class RealmWebRuntimeState
    {
        public readonly object Gate = new();
        public readonly Dictionary<int, TimerState> Timers = [];
        public int NextTimerId;
    }

    private sealed class TimerState
    {
        public required RealmWebRuntimeState OwnerState { get; init; }
        public required JsRealm Realm { get; init; }
        public required int PublicTimerId { get; init; }
        public required JsFunction Callback { get; init; }
        public required JsValue[] Arguments { get; init; }
        public required int DelayMs { get; init; }
        public required bool Repeat { get; init; }
        public required HostTaskQueueKey TimerQueueKey { get; init; }
        public required IHostDelayScheduler DelayScheduler { get; init; }
        public required JsHostFunction Driver { get; init; }
        public IHostDelayedOperation? ScheduledOperation { get; set; }
        public bool Active { get; set; } = true;
    }
}
