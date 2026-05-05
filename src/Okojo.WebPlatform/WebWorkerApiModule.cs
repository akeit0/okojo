using Okojo.Objects;
using Okojo.Runtime;
using Okojo.WebPlatform.Internal;

namespace Okojo.WebPlatform;

public sealed class WebWorkerApiModule : IRealmApiModule
{
    private WebWorkerApiModule()
    {
    }

    public static WebWorkerApiModule Shared { get; } = new();

    public void Install(JsRealm realm)
    {
        if (realm.Global.TryGetValue("Worker", out _))
            return;

        var workerApi = WebWorkerObjectFactory.For(realm);
        var prototype = workerApi.PrototypeObject;

        var ctor = new JsHostFunction(realm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            var ctorData = (WorkerCtorData)callee.UserData!;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Constructor Worker requires 'new'");

            if (args.Length == 0 || !args[0].IsString)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Worker script URL must be a string");

            var scriptType = GetWorkerScriptType(args.Length > 1 ? args[1] : JsValue.Undefined);
            var workerHandle = realm.CreateWorkerHandleObject(args[0].AsString(), scriptType);
            return JsValue.FromObject(ctorData.WorkerApi.CreateWorkerObject(realm, workerHandle));
        }, "Worker", 1, true);
        ctor.UserData = new WorkerCtorData { WorkerApi = workerApi };
        prototype.DefineDataPropertyAtom(realm, AtomTable.IdConstructor, JsValue.FromObject(ctor),
            JsShapePropertyFlags.Configurable);
        ctor.InitializePrototypeProperty(prototype);

        realm.Global["Worker"] = JsValue.FromObject(ctor);
    }

    private static WorkerScriptType GetWorkerScriptType(in JsValue optionsValue)
    {
        if (optionsValue.IsUndefined || optionsValue.IsNull)
            return WorkerScriptType.Classic;

        if (!optionsValue.TryGetObject(out var options))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Worker options must be an object");

        if (!options.TryGetProperty("type", out var typeValue) || typeValue.IsUndefined || typeValue.IsNull)
            return WorkerScriptType.Classic;

        var typeText = typeValue.IsString ? typeValue.AsString() : typeValue.ToString();
        if (string.Equals(typeText, "classic", StringComparison.Ordinal))
            return WorkerScriptType.Classic;
        if (string.Equals(typeText, "module", StringComparison.Ordinal))
            return WorkerScriptType.Module;

        throw new JsRuntimeException(JsErrorKind.TypeError,
            "Worker type must be \"classic\" or \"module\"",
            "WEB_WORKER_TYPE_INVALID");
    }

    private sealed class WorkerCtorData
    {
        public required WebWorkerObjectFactory.CachedWorkerApi WorkerApi { get; init; }
    }
}
