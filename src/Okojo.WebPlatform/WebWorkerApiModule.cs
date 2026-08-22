using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.WebPlatform.Internal;

namespace Okojo.WebPlatform;

public sealed class WebWorkerApiModule : IRealmApiModule
{
    private readonly WorkerMessaging workerMessaging;

    public WebWorkerApiModule(WorkerMessaging workerMessaging)
    {
        ArgumentNullException.ThrowIfNull(workerMessaging);
        this.workerMessaging = workerMessaging;
    }

    public void Install(JsRealm realm)
    {
        var messaging = workerMessaging;
        messaging.RegisterGlobalReceiver(
            realm,
            (data, isError) => DispatchGlobalMessageEvent(realm, data, isError)
        );

        realm.Global["onmessage"] = JsValue.Undefined;
        realm.Global["onmessageerror"] = JsValue.Undefined;

        if (realm.Agent.ParentAgent is not null)
        {
            realm.Global["postMessage"] = JsValue.FromObject(
                new JsHostFunction(
                    realm,
                    static (in info) =>
                    {
                        var realm = info.Realm;
                        var messaging = (
                            (WebWorkerApiModuleData)((JsHostFunction)info.Function).UserData!
                        ).WorkerMessaging;
                        var target = realm.Agent.ParentAgent;
                        if (target is null)
                            throw new JsRuntimeException(
                                JsErrorKind.TypeError,
                                "postMessage target is not available for this realm",
                                "POSTMESSAGE_TARGET_UNAVAILABLE"
                            );

                        var args = info.Arguments;
                        messaging.PostMessage(
                            realm,
                            target,
                            args.Length != 0 ? (JsValue?)args[0] : null
                        );
                        return JsValue.Undefined;
                    },
                    "postMessage",
                    1
                )
                {
                    UserData = new WebWorkerApiModuleData(messaging),
                }
            );
        }

        if (realm.Global.TryGetValue("Worker", out _))
            return;

        var workerApi = WebWorkerObjectFactory.For(realm);
        var prototype = workerApi.PrototypeObject;

        var ctor = new JsHostFunction(
            realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var callee = (JsHostFunction)info.Function;
                var ctorData = (WorkerCtorData)callee.UserData!;
                var messaging = ctorData.WorkerMessaging;
                if (!info.IsConstruct)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "Constructor Worker requires 'new'"
                    );

                if (args.Length == 0 || !args[0].IsString)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "Worker script URL must be a string"
                    );

                var scriptType = GetWorkerScriptType(args.Length > 1 ? args[1] : JsValue.Undefined);
                var created = messaging.CreateWorkerHandle(
                    realm,
                    args[0].AsString(),
                    realm.CurrentModuleResolvedId,
                    scriptType
                );
                messaging.RegisterWorkerHandle(
                    realm,
                    created.Binding.Agent.Id,
                    (data, isError) =>
                        DispatchHandleMessageEvent(realm, created.Handle, data, isError)
                );
                return JsValue.FromObject(
                    ctorData.WorkerApi.CreateWorkerObject(realm, created.Handle)
                );
            },
            "Worker",
            1,
            true
        );
        ctor.UserData = new WorkerCtorData { WorkerApi = workerApi, WorkerMessaging = messaging };
        prototype.DefineDataProperty(
            "constructor",
            JsValue.FromObject(ctor),
            JsShapePropertyFlags.Configurable
        );
        ctor.InitializePrototypeProperty(prototype);

        realm.Global["Worker"] = JsValue.FromObject(ctor);
    }

    private static WorkerScriptType GetWorkerScriptType(in JsValue optionsValue)
    {
        if (optionsValue.IsUndefined || optionsValue.IsNull)
            return WorkerScriptType.Classic;

        if (!optionsValue.TryGetObject(out var options))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Worker options must be an object");

        if (
            !options.TryGetProperty("type", out var typeValue)
            || typeValue.IsUndefined
            || typeValue.IsNull
        )
            return WorkerScriptType.Classic;

        var typeText = typeValue.IsString ? typeValue.AsString() : typeValue.ToString();
        if (string.Equals(typeText, "classic", StringComparison.Ordinal))
            return WorkerScriptType.Classic;
        if (string.Equals(typeText, "module", StringComparison.Ordinal))
            return WorkerScriptType.Module;

        throw new JsRuntimeException(
            JsErrorKind.TypeError,
            "Worker type must be \"classic\" or \"module\"",
            "WEB_WORKER_TYPE_INVALID"
        );
    }

    private static void DispatchGlobalMessageEvent(JsRealm realm, in JsValue data, bool isError)
    {
        var handlerName = isError ? "onmessageerror" : "onmessage";
        if (
            !realm.GlobalObject.TryGetProperty(handlerName, out var handler)
            || !handler.TryGetObject(out var handlerObject)
            || handlerObject is not JsFunction function
        )
            return;

        var messageEvent = CreateMessageEvent(realm, data);
        Span<JsValue> args = [JsValue.FromObject(messageEvent)];
        try
        {
            _ = realm.Call(function, JsValue.FromObject(realm.GlobalObject), args);
        }
        catch (JsRuntimeException)
        {
            // Async message handler errors are host-observable through existing error hooks.
        }
    }

    private static void DispatchHandleMessageEvent(
        JsRealm realm,
        JsPlainObject handle,
        in JsValue data,
        bool isError
    )
    {
        var handlerName = isError ? "onmessageerror" : "onmessage";
        if (
            !handle.TryGetProperty(handlerName, out var handler)
            || !handler.TryGetObject(out var handlerObject)
            || handlerObject is not JsFunction function
        )
        {
            DispatchGlobalMessageEvent(realm, data, isError);
            return;
        }

        var messageEvent = CreateMessageEvent(realm, data);
        Span<JsValue> args = [JsValue.FromObject(messageEvent)];
        try
        {
            _ = realm.Call(function, JsValue.FromObject(handle), args);
        }
        catch (JsRuntimeException)
        {
            // Async message handler errors are host-observable through existing error hooks.
        }
    }

    private static JsPlainObject CreateMessageEvent(JsRealm realm, in JsValue data)
    {
        var messageEvent = new JsPlainObject(realm);
        messageEvent.DefineDataProperty("data", data, JsShapePropertyFlags.Open);
        return messageEvent;
    }

    private sealed class WorkerCtorData
    {
        public required WebWorkerObjectFactory.CachedWorkerApi WorkerApi { get; init; }
        public required WorkerMessaging WorkerMessaging { get; init; }
    }

    private sealed class WebWorkerApiModuleData(WorkerMessaging workerMessaging)
    {
        public WorkerMessaging WorkerMessaging { get; } = workerMessaging;
    }
}
