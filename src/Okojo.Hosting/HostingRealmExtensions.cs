using Okojo.Runtime;

namespace Okojo.Hosting;

public static class HostingRealmExtensions
{
    public static HostPump CreateHostPump(this JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return new(realm.Agent);
    }

    public static WorkerRuntime CreateWorkerRuntime(
        this JsRealm realm,
        Action<WorkerRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return WorkerRuntimeFactory.CreateWorkerRuntime(realm, configure);
    }
}
