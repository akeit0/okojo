using Okojo.Runtime;

namespace Okojo.Hosting;

public sealed class WorkerRuntimeHost(Action<WorkerRuntimeOptions>? configure = null) : IHostingJsWorkerHost
{
    public WorkerRuntime CreateWorker(JsRealm ownerRealm, string? moduleEntry, string? ownerReferrer)
    {
        return WorkerRuntimeFactory.CreateWorkerRuntime(ownerRealm, options =>
        {
            options.ModuleEntry = moduleEntry;
            options.ModuleReferrer = ownerReferrer;
            configure?.Invoke(options);
        });
    }
}
