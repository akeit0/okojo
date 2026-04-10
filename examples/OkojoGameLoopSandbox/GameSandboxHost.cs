using System.Runtime.CompilerServices;
using Okojo;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.WebPlatform;

namespace OkojoGameLoopSandbox;

internal sealed class GameSandboxHost : IDisposable
{
    private static readonly HostTaskQueueKey[] SQueueOrder = [WebTaskQueueKeys.Timers, HostingTaskQueueKeys.Default];
    private readonly List<string> appLogs = [];
    private readonly List<string> appSignals = [];
    private readonly CancellationTokenSource disposeCancellationSource = new();
    private readonly ManualHostEventLoop eventLoop;
    private readonly List<FrameWaiter> frameWaiters = [];
    private readonly HostPump pump;

    private int currentFrame;

    private GameSandboxHost(JsRuntime runtime, ManualHostEventLoop eventLoop)
    {
        this.Runtime = runtime;
        this.eventLoop = eventLoop;
        pump = runtime.CreateHostPump();
    }

    public JsRuntime Runtime { get; }

    public JsRealm Realm => Runtime.MainRealm;
    public IReadOnlyList<string> AppLogs => appLogs;
    public IReadOnlyList<string> AppSignals => appSignals;

    public void Dispose()
    {
        disposeCancellationSource.Cancel();
        for (var i = frameWaiters.Count - 1; i >= 0; i--)
            _ = frameWaiters[i].Awaitable.TryCancel("Game sandbox host disposed");
        frameWaiters.Clear();
        disposeCancellationSource.Dispose();
        Runtime.Dispose();
    }

    public static GameSandboxHost Create(GameSandboxAssets assets, FrameBudget initialBudget)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var eventLoop = new ManualHostEventLoop(TimeProvider.System);
        var moduleLoader = new GameModuleLoader(assets);
        var runtime = JsRuntime.CreateBuilder()
            .UseLowLevelHost(host => host.UseTaskScheduler(eventLoop))
            .UseModuleSourceLoader(moduleLoader)
            .UseWebDelayScheduler(eventLoop)
            .UseWebTimerQueue(WebTaskQueueKeys.Timers)
            .UseWebRuntimeGlobals()
            .UseAgent(agent =>
            {
                agent.ApplyExecutionBudget(
                    initialBudget.MaxInstructions,
                    checkInterval: initialBudget.CheckInterval);
            })
            .Build();

        var host = new GameSandboxHost(runtime, eventLoop);
        host.InstallAppApi(runtime.MainRealm, initialBudget);
        return host;
    }

    public JsModuleNamespace LoadModule(string specifier)
    {
        var result = Runtime.LoadModule(specifier);
        if (!result.IsCompleted)
            throw new InvalidOperationException("This sandbox expects sync module linking/evaluation at load time.");
        return result.Namespace;
    }

    public FrameRunResult RunFrames(JsModuleNamespace module, int frameCount, FrameBudget budget,
        int framesPerSecond = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);

        var frameDurationMs = (int)Math.Round(1000d / framesPerSecond);
        var nextFrameTick = Environment.TickCount64;
        InvokeIfPresent(module, "init");

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sleepMs = nextFrameTick - Environment.TickCount64;
            if (sleepMs > 0)
                Thread.Sleep((int)sleepMs);
            nextFrameTick += frameDurationMs;
            currentFrame = frame;
            UpdateAppFrameState(frame, frameDurationMs);

            try
            {
                Runtime.MainAgent.ResetExecutionBudget(budget.MaxInstructions, budget.MaxWallTime);
                module.CallExport("update", JsValue.FromInt32(frame), new JsValue(frameDurationMs));
                while (HostTurnRunner.RunTurn(eventLoop, pump, SQueueOrder))
                {
                }

                DrainFrameWaiters(frame);
                while (HostTurnRunner.RunTurn(eventLoop, pump, SQueueOrder))
                {
                }
            }
            catch (JsRuntimeException ex) when (IsFrameBudgetError(ex))
            {
                return new(false, frame, ex.DetailCode ?? "FRAME_BUDGET_EXCEEDED", ex.Message);
            }
        }

        return new(true, frameCount, null, null);
    }

    public string GetSnapshot(JsModuleNamespace module)
    {
        return module.CallExport("snapshot").AsString();
    }

    private static bool IsFrameBudgetError(JsRuntimeException ex)
    {
        return string.Equals(ex.DetailCode, "EXECUTION_LIMIT_EXCEEDED", StringComparison.Ordinal) ||
               string.Equals(ex.DetailCode, "EXECUTION_TIMEOUT_EXCEEDED", StringComparison.Ordinal);
    }

    private void InstallAppApi(JsRealm realm, FrameBudget initialBudget)
    {
        var app = new JsPlainObject(realm);
        app.DefineDataProperty("targetFps", JsValue.FromInt32(30), JsShapePropertyFlags.Open);
        app.DefineDataProperty("maxInstructionsPerFrame", new(initialBudget.MaxInstructions),
            JsShapePropertyFlags.Open);
        app.DefineDataProperty("maxFrameTimeMs", new(initialBudget.MaxWallTime.TotalMilliseconds),
            JsShapePropertyFlags.Open);
        app.DefineDataProperty("frame", JsValue.FromInt32(0), JsShapePropertyFlags.Open);
        app.DefineDataProperty("deltaMs", JsValue.FromInt32(0), JsShapePropertyFlags.Open);
        app.DefineDataProperty("log", JsValue.FromObject(new JsHostFunction(realm, "log", 1, Log)),
            JsShapePropertyFlags.Open);
        app.DefineDataProperty("emit", JsValue.FromObject(new JsHostFunction(realm, "emit", 1, Emit)),
            JsShapePropertyFlags.Open);
        app.DefineDataProperty("delayMs", JsValue.FromObject(new JsHostFunction(realm, "delayMs", 1, DelayMs)),
            JsShapePropertyFlags.Open);
        app.DefineDataProperty("waitFrames", JsValue.FromObject(new JsHostFunction(realm, "waitFrames", 1, WaitFrames)),
            JsShapePropertyFlags.Open);
        realm.Global["app"] = JsValue.FromObject(app);
        return;

        JsValue Log(in CallInfo info)
        {
            var parts = new string[info.Arguments.Length];
            for (var i = 0; i < info.Arguments.Length; i++)
                parts[i] = FormatValue(info.Arguments[i]);
            appLogs.Add(parts.Length == 0 ? string.Empty : string.Join(" ", parts));
            return JsValue.Undefined;
        }

        JsValue Emit(in CallInfo info)
        {
            if (info.Arguments.Length != 0)
                appSignals.Add(FormatValue(info.Arguments[0]));
            return JsValue.Undefined;
        }

        JsValue DelayMs(in CallInfo info)
        {
            var delayMs = CoerceNonNegativeMilliseconds(info.Arguments[0]);
            var awaitable = new SandboxAwaitableSource(info.Realm);
            var operation = eventLoop.ScheduleDelayed(
                TimeSpan.FromMilliseconds(delayMs),
                WebTaskQueueKeys.Timers,
                static state => { _ = ((SandboxAwaitableSource)state!).TryResolve(); },
                awaitable);
            awaitable.CancelOn(disposeCancellationSource.Token);
            _ = disposeCancellationSource.Token.Register(
                static state => { _ = ((IHostDelayedOperation)state!).Cancel(); }, operation);
            return awaitable.ThenableValue;
        }

        JsValue WaitFrames(in CallInfo info)
        {
            var frames = CoerceFrameCount(info.Arguments[0]);
            var awaitable = new SandboxAwaitableSource(info.Realm);
            awaitable.CancelOn(disposeCancellationSource.Token);
            frameWaiters.Add(new(currentFrame + Math.Max(1, frames), awaitable));
            return awaitable.ThenableValue;
        }
    }

    private void UpdateAppFrameState(int frame, int frameDurationMs)
    {
        if (!Runtime.MainRealm.Global.TryGetValue("app", out var appValue) || !appValue.TryGetObject(out var appObject))
            return;

        appObject.SetProperty("frame", JsValue.FromInt32(frame));
        appObject.SetProperty("deltaMs", JsValue.FromInt32(frameDurationMs));
    }

    private void DrainFrameWaiters(int frame)
    {
        for (var i = frameWaiters.Count - 1; i >= 0; i--)
        {
            var waiter = frameWaiters[i];
            if (waiter.TargetFrame > frame)
                continue;

            frameWaiters.RemoveAt(i);
            _ = waiter.Awaitable.TryResolve();
        }
    }

    private static void InvokeIfPresent(JsModuleNamespace module, string exportName)
    {
        if (module.TryGetExport(exportName, out _))
            module.CallExport(exportName);
    }

    private static string FormatValue(JsValue value)
    {
        if (value.TryGetObject(out var obj))
            return obj.ToDisplayString(2);
        return value.ToString();
    }

    private static int CoerceFrameCount(JsValue value)
    {
        if (value.IsInt32)
            return Math.Max(0, value.Int32Value);
        if (value.IsNumber)
            return Math.Max(0, (int)Math.Truncate(value.NumberValue));
        return 0;
    }

    private static double CoerceNonNegativeMilliseconds(JsValue value)
    {
        if (value.IsInt32)
            return Math.Max(0, value.Int32Value);
        if (value.IsNumber)
            return Math.Max(0, value.NumberValue);
        return 0;
    }

    private sealed class SandboxAwaitableSource
    {
        private static readonly ConditionalWeakTable<JsRealm, JsHostFunction> SThenFunctions = new();
        private readonly object gate = new();
        private readonly JsUserDataObject<SandboxAwaitableSource> thenableObject;
        private CancellationTokenRegistration cancellationRegistration;
        private List<PendingHandler>? extraHandlers;
        private PendingHandler firstHandler;
        private bool hasCancellationRegistration;
        private SettlementKind settlementKind;
        private JsValue settlementValue;

        public SandboxAwaitableSource(JsRealm realm)
        {
            thenableObject = new(realm);
            thenableObject.UserData = this;
            thenableObject.DefineDataProperty(
                "then",
                JsValue.FromObject(SThenFunctions.GetValue(realm, static realm =>
                    new(realm, "then", 2, ThenBody, false))),
                JsShapePropertyFlags.Open);
            settlementValue = JsValue.Undefined;
        }

        public JsValue ThenableValue => JsValue.FromObject(thenableObject);

        public bool TryResolve()
        {
            return TryResolve(JsValue.Undefined);
        }

        public bool TryResolve(JsValue value)
        {
            return TrySettle(SettlementKind.Resolved, value);
        }

        public bool TryCancel(string reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            return TrySettle(SettlementKind.Rejected, JsValue.FromString(reason));
        }

        public void CancelOn(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return;

            if (cancellationToken.IsCancellationRequested)
            {
                _ = TryCancel("Game sandbox canceled");
                return;
            }

            var registration = cancellationToken.Register(
                static state => { _ = ((SandboxAwaitableSource)state!).TryCancel("Game sandbox canceled"); }, this);

            CancellationTokenRegistration previousRegistration = default;
            var disposePrevious = false;
            var disposeCurrent = false;
            lock (gate)
            {
                if (settlementKind != SettlementKind.Pending)
                {
                    disposeCurrent = true;
                }
                else
                {
                    if (hasCancellationRegistration)
                    {
                        previousRegistration = cancellationRegistration;
                        disposePrevious = true;
                    }

                    cancellationRegistration = registration;
                    hasCancellationRegistration = true;
                }
            }

            if (disposePrevious)
                previousRegistration.Dispose();
            if (disposeCurrent)
                registration.Dispose();
        }

        private static JsValue ThenBody(scoped in CallInfo info)
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) ||
                thisObj is not JsUserDataObject<SandboxAwaitableSource> awaitableObject ||
                awaitableObject.UserData is not SandboxAwaitableSource source)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Sandbox awaitable called on incompatible receiver.");

            return source.OnThen(in info);
        }

        private JsValue OnThen(scoped in CallInfo info)
        {
            var resolve = GetFunctionArgument(info.GetArgument(0));
            var reject = GetFunctionArgument(info.GetArgument(1));
            if (resolve is null && reject is null)
                return JsValue.Undefined;

            SettlementKind settledKind;
            JsValue settledValue;
            lock (gate)
            {
                if (settlementKind == SettlementKind.Pending)
                {
                    AddHandler(new(info.Realm, resolve, reject));
                    return JsValue.Undefined;
                }

                settledKind = settlementKind;
                settledValue = settlementValue;
            }

            InvokeHandler(new(info.Realm, resolve, reject), settledKind, settledValue);
            return JsValue.Undefined;
        }

        private bool TrySettle(SettlementKind kind, JsValue value)
        {
            PendingHandler[]? handlers = null;
            CancellationTokenRegistration registration = default;
            var disposeRegistration = false;
            lock (gate)
            {
                if (settlementKind != SettlementKind.Pending)
                    return false;

                settlementKind = kind;
                settlementValue = value;
                if (hasCancellationRegistration)
                {
                    registration = cancellationRegistration;
                    cancellationRegistration = default;
                    hasCancellationRegistration = false;
                    disposeRegistration = true;
                }

                if (firstHandler.Resolve is not null || firstHandler.Reject is not null)
                {
                    var extraCount = extraHandlers?.Count ?? 0;
                    handlers = new PendingHandler[1 + extraCount];
                    handlers[0] = firstHandler;
                    if (extraCount != 0)
                        extraHandlers!.CopyTo(0, handlers, 1, extraCount);
                    firstHandler = default;
                    extraHandlers?.Clear();
                    extraHandlers = null;
                }
            }

            if (disposeRegistration)
                registration.Dispose();

            if (handlers is null)
                return true;

            for (var i = 0; i < handlers.Length; i++)
                InvokeHandler(handlers[i], kind, value);

            return true;
        }

        private void AddHandler(PendingHandler handler)
        {
            if (firstHandler.Resolve is null && firstHandler.Reject is null)
            {
                firstHandler = handler;
                return;
            }

            extraHandlers ??= [];
            extraHandlers.Add(handler);
        }

        private static JsFunction? GetFunctionArgument(JsValue value)
        {
            return value.TryGetObject(out var obj) && obj is JsFunction function ? function : null;
        }

        private static void InvokeHandler(PendingHandler handler, SettlementKind kind, JsValue value)
        {
            var callback = kind == SettlementKind.Resolved ? handler.Resolve : handler.Reject;
            if (callback is null)
                return;

            _ = handler.Realm.Call(callback, JsValue.Undefined, value);
        }

        private readonly struct PendingHandler(JsRealm realm, JsFunction? resolve, JsFunction? reject)
        {
            public readonly JsRealm Realm = realm;
            public readonly JsFunction? Resolve = resolve;
            public readonly JsFunction? Reject = reject;
        }

        private enum SettlementKind : byte
        {
            Pending,
            Resolved,
            Rejected
        }
    }

    private sealed record FrameWaiter(int TargetFrame, SandboxAwaitableSource Awaitable);
}
