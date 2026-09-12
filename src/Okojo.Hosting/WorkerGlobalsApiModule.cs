using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Hosting;

public sealed class WorkerGlobalsApiModule : IRealmApiModule
{
    private readonly WorkerMessaging workerMessaging;

    public WorkerGlobalsApiModule(WorkerMessaging workerMessaging)
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
                            (WorkerGlobalsApiModuleData)((JsHostFunction)info.Function).UserData!
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
                    UserData = new WorkerGlobalsApiModuleData(messaging),
                }
            );
            return;
        }

        if (realm.Agent.Kind != JsAgentKind.Main || realm.Global.TryGetValue("createWorker", out _))
            return;

        realm.Global["createWorker"] = JsValue.FromObject(
            new JsHostFunction(
                realm,
                static (in info) =>
                {
                    var realm = info.Realm;
                    var messaging = (
                        (WorkerGlobalsApiModuleData)((JsHostFunction)info.Function).UserData!
                    ).WorkerMessaging;
                    var args = info.Arguments;
                    string? moduleEntry = null;
                    if (args.Length != 0 && !args[0].IsUndefined && !args[0].IsNull)
                    {
                        if (!args[0].IsString)
                            throw new JsRuntimeException(
                                JsErrorKind.TypeError,
                                "createWorker module specifier must be a string",
                                "WORKER_MODULE_SPECIFIER_TYPE"
                            );

                        moduleEntry = args[0].AsString();
                    }

                    var created = messaging.CreateWorkerHandle(
                        realm,
                        moduleEntry,
                        realm.CurrentModuleResolvedId,
                        WorkerScriptType.Module
                    );
                    messaging.RegisterWorkerHandle(
                        realm,
                        created.Binding.Agent.Id,
                        (data, isError) =>
                            DispatchHandleMessageEvent(realm, created.Handle, data, isError)
                    );
                    return JsValue.FromObject(created.Handle);
                },
                "createWorker",
                1
            )
            {
                UserData = new WorkerGlobalsApiModuleData(messaging),
            }
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

    private sealed class WorkerGlobalsApiModuleData(WorkerMessaging workerMessaging)
    {
        public WorkerMessaging WorkerMessaging { get; } = workerMessaging;
    }
}
