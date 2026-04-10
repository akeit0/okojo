using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeTimersBuiltIn(NodeRuntime runtime)
{
    private JsPlainObject? schedulerObject;

    private JsPlainObject? timersModule;
    private JsPlainObject? timersPromisesModule;

    public JsPlainObject GetTimersModule()
    {
        if (timersModule is not null)
            return timersModule;

        var realm = runtime.MainRealm;
        var module = new JsPlainObject(realm, useDictionaryMode: true);
        DefineGlobalAlias(module, realm, "setTimeout");
        DefineGlobalAlias(module, realm, "clearTimeout");
        DefineGlobalAlias(module, realm, "setInterval");
        DefineGlobalAlias(module, realm, "clearInterval");
        module.DefineDataProperty("setImmediate",
            JsValue.FromObject(runtime.BuiltIns.CreateSetImmediateFunction(realm)), JsShapePropertyFlags.Open);
        module.DefineDataProperty("clearImmediate",
            JsValue.FromObject(runtime.BuiltIns.CreateClearImmediateFunction(realm)), JsShapePropertyFlags.Open);
        timersModule = module;
        return module;
    }

    public JsPlainObject GetTimersPromisesModule()
    {
        if (timersPromisesModule is not null)
            return timersPromisesModule;

        var realm = runtime.MainRealm;
        var timers = GetTimersModule();
        var module = new JsPlainObject(realm, useDictionaryMode: true);
        module.DefineDataProperty(
            "setTimeout",
            JsValue.FromObject(CreateSetTimeoutPromiseFunction(realm, GetRequiredModuleFunction(timers, "setTimeout"))),
            JsShapePropertyFlags.Open);
        module.DefineDataProperty(
            "setImmediate",
            JsValue.FromObject(
                CreateSetImmediatePromiseFunction(realm, GetRequiredModuleFunction(timers, "setImmediate"))),
            JsShapePropertyFlags.Open);
        module.DefineDataProperty("scheduler", JsValue.FromObject(GetSchedulerObject()), JsShapePropertyFlags.Open);
        timersPromisesModule = module;
        return module;
    }

    private JsPlainObject GetSchedulerObject()
    {
        if (schedulerObject is not null)
            return schedulerObject;

        var realm = runtime.MainRealm;
        var timers = GetTimersModule();
        var scheduler = new JsPlainObject(realm, useDictionaryMode: true);
        scheduler.DefineDataProperty(
            "wait",
            JsValue.FromObject(CreateSchedulerWaitFunction(realm, GetRequiredModuleFunction(timers, "setTimeout"))),
            JsShapePropertyFlags.Open);
        scheduler.DefineDataProperty(
            "yield",
            JsValue.FromObject(CreateSchedulerYieldFunction(realm, GetRequiredModuleFunction(timers, "setImmediate"))),
            JsShapePropertyFlags.Open);
        schedulerObject = scheduler;
        return scheduler;
    }

    private static void DefineGlobalAlias(JsPlainObject module, JsRealm realm, string name)
    {
        if (!realm.GlobalObject.TryGetProperty(name, out var value))
            value = JsValue.Undefined;
        module.DefineDataProperty(name, value, JsShapePropertyFlags.Open);
    }

    private static JsValue GetRequiredModuleFunction(JsObject module, string name)
    {
        if (!module.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"Timer module function '{name}' is not available.");

        return value;
    }

    private static JsHostFunction CreateSetTimeoutPromiseFunction(JsRealm realm, JsValue scheduler)
    {
        return new(realm, "setTimeout", 1, static (in info) =>
        {
            var state = (PromiseSchedulerState)((JsHostFunction)info.Function).UserData!;
            var delay = info.Arguments.Length == 0 ? 1 : NormalizeDelay(info.Realm.ToNumber(info.GetArgument(0)));
            var value = info.Arguments.Length > 1 ? info.GetArgument(1) : JsValue.Undefined;
            return CreateScheduledPromise(info, state, [JsValue.FromInt32(delay), value]);
        }, false)
        {
            UserData = new PromiseSchedulerState(scheduler, "setTimeout")
        };
    }

    private static JsHostFunction CreateSetImmediatePromiseFunction(JsRealm realm, JsValue scheduler)
    {
        return new(realm, "setImmediate", 0, static (in info) =>
        {
            var state = (PromiseSchedulerState)((JsHostFunction)info.Function).UserData!;
            var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.GetArgument(0);
            return CreateScheduledPromise(info, state, [value]);
        }, false)
        {
            UserData = new PromiseSchedulerState(scheduler, "setImmediate")
        };
    }

    private static JsHostFunction CreateSchedulerWaitFunction(JsRealm realm, JsValue scheduler)
    {
        return new(realm, "wait", 1, static (in info) =>
        {
            var state = (PromiseSchedulerState)((JsHostFunction)info.Function).UserData!;
            var delay = info.Arguments.Length == 0 ? 1 : NormalizeDelay(info.Realm.ToNumber(info.GetArgument(0)));
            return CreateScheduledPromise(info, state, [JsValue.FromInt32(delay), JsValue.Undefined]);
        }, false)
        {
            UserData = new PromiseSchedulerState(scheduler, "wait")
        };
    }

    private static JsHostFunction CreateSchedulerYieldFunction(JsRealm realm, JsValue scheduler)
    {
        return new(realm, "yield", 0, static (in info) =>
        {
            var state = (PromiseSchedulerState)((JsHostFunction)info.Function).UserData!;
            return CreateScheduledPromise(info, state, [JsValue.Undefined]);
        }, false)
        {
            UserData = new PromiseSchedulerState(scheduler, "yield")
        };
    }

    private static JsValue CreateScheduledPromise(
        in CallInfo info,
        PromiseSchedulerState schedulerState,
        ReadOnlySpan<JsValue> schedulerArgs)
    {
        var completion = new TaskCompletionSource<JsValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolve = new JsHostFunction(
            info.Realm,
            schedulerState.SchedulerName + " promise resolve",
            1,
            static (in callbackInfo) =>
            {
                var state = (PromiseTimerState)((JsHostFunction)callbackInfo.Function).UserData!;
                var settledValue = callbackInfo.Arguments.Length == 0 ? JsValue.Undefined : callbackInfo.GetArgument(0);
                state.Completion.TrySetResult(settledValue);
                return JsValue.Undefined;
            },
            false)
        {
            UserData = new PromiseTimerState(completion)
        };

        var invokeArgs = new JsValue[1 + schedulerArgs.Length];
        invokeArgs[0] = JsValue.FromObject(resolve);
        for (var i = 0; i < schedulerArgs.Length; i++)
            invokeArgs[i + 1] = schedulerArgs[i];
        _ = info.Realm.Call(schedulerState.Scheduler, JsValue.FromObject(info.Realm.GlobalObject), invokeArgs);
        return info.Realm.WrapTask(completion.Task);
    }

    private static int NormalizeDelay(double delay)
    {
        if (double.IsNaN(delay) || delay < 1)
            return 1;
        if (delay > int.MaxValue)
            return int.MaxValue;
        return (int)Math.Truncate(delay);
    }

    private sealed class PromiseTimerState(TaskCompletionSource<JsValue> completion)
    {
        public TaskCompletionSource<JsValue> Completion { get; } = completion;
    }

    private sealed class PromiseSchedulerState(JsValue scheduler, string schedulerName)
    {
        public JsValue Scheduler { get; } = scheduler;
        public string SchedulerName { get; } = schedulerName;
    }
}
