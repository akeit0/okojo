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

            ValidateWorkerOptions(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var workerHandle = realm.CreateWorkerHandleObject(args[0].AsString());
            return JsValue.FromObject(ctorData.WorkerApi.CreateWorkerObject(realm, workerHandle));
        }, "Worker", 1, true);
        ctor.UserData = new WorkerCtorData { WorkerApi = workerApi };
        prototype.DefineDataPropertyAtom(realm, AtomTable.IdConstructor, JsValue.FromObject(ctor),
            JsShapePropertyFlags.Configurable);
        ctor.InitializePrototypeProperty(prototype);

        realm.Global["Worker"] = JsValue.FromObject(ctor);
    }

    private static void ValidateWorkerOptions(JsRealm realm, in JsValue optionsValue)
    {
        if (optionsValue.IsUndefined || optionsValue.IsNull)
            return;

        if (!optionsValue.TryGetObject(out var options))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Worker options must be an object");

        if (!options.TryGetProperty("type", out var typeValue) || typeValue.IsUndefined || typeValue.IsNull)
            return;

        var typeText = typeValue.IsString ? typeValue.AsString() : typeValue.ToString();
        if (string.Equals(typeText, "module", StringComparison.Ordinal))
            return;

        throw new JsRuntimeException(JsErrorKind.TypeError,
            "Only module workers are currently supported",
            "WEB_WORKER_TYPE_UNSUPPORTED");
    }

    private sealed class WorkerCtorData
    {
        public required WebWorkerObjectFactory.CachedWorkerApi WorkerApi { get; init; }
    }
}
