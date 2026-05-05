using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Hosting;

public sealed class WorkerGlobalsApiModule : IRealmApiModule
{
    private WorkerGlobalsApiModule()
    {
    }

    public static WorkerGlobalsApiModule Shared { get; } = new();

    public void Install(JsRealm realm)
    {
        realm.InstallWorkerMessagingGlobals(realm.Agent.ParentAgent is not null);

        if (realm.Agent.Kind != JsAgentKind.Main || realm.Global.TryGetValue("createWorker", out _))
            return;

        realm.Global["createWorker"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            string? moduleEntry = null;
            if (args.Length != 0 && !args[0].IsUndefined && !args[0].IsNull)
            {
                if (!args[0].IsString)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "createWorker module specifier must be a string",
                        "WORKER_MODULE_SPECIFIER_TYPE");

                moduleEntry = args[0].AsString();
            }

            return realm.CreateWorkerHandleObject(moduleEntry, WorkerScriptType.Module);
        }, "createWorker", 1));
    }
}
