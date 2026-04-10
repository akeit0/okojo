using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.Values;
using Okojo.WebPlatform;
using System.Runtime.CompilerServices;

namespace Okojo.Browser;

public sealed class BrowserApiModule : IRealmApiModule
{
    private readonly Func<JsRealm, IHostDelayScheduler> delaySchedulerFactory;
    private readonly WebRuntimeApiModule webRuntimeApiModule;
    private readonly HostTaskQueueKey animationFrameQueueKey;
    private readonly TimeSpan animationFrameInterval;

    private sealed class RealmBrowserState
    {
        public readonly object Gate = new();
        public readonly Dictionary<int, AnimationFrameRequestState> AnimationFrameRequests = [];
        public int NextAnimationFrameId;
        public IHostDelayedOperation? ScheduledAnimationFrame;
        public DateTimeOffset TimeOrigin;
    }

    private sealed class AnimationFrameRequestState
    {
        public required RealmBrowserState OwnerState { get; init; }
        public required JsRealm Realm { get; init; }
        public required int PublicRequestId { get; init; }
        public required JsFunction Callback { get; init; }
        public required HostTaskQueueKey AnimationFrameQueueKey { get; init; }
        public bool Active { get; set; } = true;
    }

    private readonly ConditionalWeakTable<JsRealm, RealmBrowserState> realmStates = new();

    public static BrowserApiModule Shared { get; } = new(
        static realm => new TimeProviderDelayScheduler(realm.Engine.TimeProvider),
        WebTaskQueueKeys.Timers,
        WebTaskQueueKeys.Rendering,
        TimeSpan.FromMilliseconds(16));

    public BrowserApiModule(
        Func<JsRealm, IHostDelayScheduler> delaySchedulerFactory,
        HostTaskQueueKey timerQueueKey,
        HostTaskQueueKey animationFrameQueueKey,
        TimeSpan animationFrameInterval)
    {
        if (animationFrameInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(animationFrameInterval));
        this.delaySchedulerFactory = delaySchedulerFactory;
        webRuntimeApiModule = new WebRuntimeApiModule(delaySchedulerFactory, timerQueueKey);
        this.animationFrameQueueKey = animationFrameQueueKey;
        this.animationFrameInterval = animationFrameInterval;
    }

    public void Install(JsRealm realm)
    {
        webRuntimeApiModule.Install(realm);
        var global = JsValue.FromObject(realm.GlobalObject);
        if (!realm.Global.TryGetValue("window", out _))
            realm.Global["window"] = global;
        if (!realm.Global.TryGetValue("self", out _))
            realm.Global["self"] = global;
        if (!realm.Global.TryGetValue("requestAnimationFrame", out _))
            realm.Global["requestAnimationFrame"] = JsValue.FromObject(CreateRequestAnimationFrameFunction(realm));
        if (!realm.Global.TryGetValue("cancelAnimationFrame", out _))
            realm.Global["cancelAnimationFrame"] = JsValue.FromObject(CreateCancelAnimationFrameFunction(realm));
    }

    private RealmBrowserState GetState(JsRealm realm)
    {
        return realmStates.GetValue(realm, static key => new RealmBrowserState
        {
            TimeOrigin = key.Engine.TimeProvider.GetUtcNow()
        });
    }

    private JsHostFunction CreateRequestAnimationFrameFunction(JsRealm realm)
    {
        return new JsHostFunction(realm, "requestAnimationFrame", 1, (in CallInfo info) =>
        {
            if (info.Arguments.Length == 0 ||
                !info.Arguments[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callback)
            {
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "requestAnimationFrame callback is not a function",
                    "REQUEST_ANIMATION_FRAME_CALLBACK_NOT_FUNCTION");
            }

            return JsValue.FromInt32(CreateAnimationFrameRequest(info.Realm, callback));
        }, isConstructor: false);
    }

    private JsHostFunction CreateCancelAnimationFrameFunction(JsRealm realm)
    {
        return new JsHostFunction(realm, "cancelAnimationFrame", 1, (in CallInfo info) =>
        {
            if (info.Arguments.Length != 0)
                CancelAnimationFrameRequest(info.Realm, ToAnimationFrameId(info.Arguments[0]));
            return JsValue.Undefined;
        }, isConstructor: false);
    }

    private int CreateAnimationFrameRequest(JsRealm realm, JsFunction callback)
    {
        var state = GetState(realm);
        lock (state.Gate)
        {
            int requestId = ++state.NextAnimationFrameId;
            state.AnimationFrameRequests.Add(requestId, new AnimationFrameRequestState
            {
                OwnerState = state,
                Realm = realm,
                PublicRequestId = requestId,
                Callback = callback,
                AnimationFrameQueueKey = animationFrameQueueKey
            });
            EnsureAnimationFrameScheduled(state, realm);
            return requestId;
        }
    }

    private void CancelAnimationFrameRequest(JsRealm realm, int requestId)
    {
        var state = GetState(realm);
        IHostDelayedOperation? scheduled = null;
        lock (state.Gate)
        {
            if (!state.AnimationFrameRequests.Remove(requestId, out var request))
                return;

            request.Active = false;
            if (state.AnimationFrameRequests.Count == 0)
            {
                scheduled = state.ScheduledAnimationFrame;
                state.ScheduledAnimationFrame = null;
            }
        }

        scheduled?.Dispose();
    }

    private void EnsureAnimationFrameScheduled(RealmBrowserState state, JsRealm realm)
    {
        if (state.ScheduledAnimationFrame is not null || state.AnimationFrameRequests.Count == 0)
            return;

        var scheduler = delaySchedulerFactory(realm);
        if (scheduler is IQueuedHostDelayScheduler queuedDelayScheduler)
        {
            state.ScheduledAnimationFrame = queuedDelayScheduler.ScheduleDelayed(
                animationFrameInterval,
                animationFrameQueueKey,
                static queuedState => ProcessAnimationFrame((AnimationFrameDueState)queuedState!),
                new AnimationFrameDueState(state, realm));
            return;
        }

        state.ScheduledAnimationFrame = scheduler.ScheduleDelayed(
            animationFrameInterval,
            static queuedState => ProcessAnimationFrame((AnimationFrameDueState)queuedState!),
            new AnimationFrameDueState(state, realm));
    }

    private static void ProcessAnimationFrame(AnimationFrameDueState dueState)
    {
        AnimationFrameRequestState[] requests;
        double timestamp;
        lock (dueState.State.Gate)
        {
            dueState.State.ScheduledAnimationFrame = null;
            if (dueState.State.AnimationFrameRequests.Count == 0)
                return;

            requests = dueState.State.AnimationFrameRequests.Values.ToArray();
            dueState.State.AnimationFrameRequests.Clear();
            timestamp = (dueState.Realm.Engine.TimeProvider.GetUtcNow() - dueState.State.TimeOrigin).TotalMilliseconds;
        }

        for (int i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            if (!request.Active)
                continue;

            request.Active = false;
            request.Realm.QueueHostTask(
                request.AnimationFrameQueueKey,
                request.Callback,
                [new JsValue(timestamp)]);
        }
    }

    private static int ToAnimationFrameId(in JsValue value)
    {
        return value.IsInt32 ? value.Int32Value : (int)value.NumberValue;
    }

    private sealed class AnimationFrameDueState(RealmBrowserState state, JsRealm realm)
    {
        public RealmBrowserState State { get; } = state;
        public JsRealm Realm { get; } = realm;
    }
}
