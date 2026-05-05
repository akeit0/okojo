using Okojo.Runtime;

namespace Okojo.Hosting;

public sealed class WorkerRuntimeHost(Action<WorkerRuntimeOptions>? configure = null) : IHostingJsWorkerHost
{
    public WorkerRuntime CreateWorker(
        JsRealm ownerRealm,
        string? scriptEntry,
        string? ownerReferrer,
        WorkerScriptType scriptType)
    {
        return WorkerRuntimeFactory.CreateWorkerRuntime(ownerRealm, options =>
        {
            options.ScriptEntry = scriptEntry;
            options.ScriptReferrer = ownerReferrer;
            options.ScriptType = scriptType;
            configure?.Invoke(options);
        });
    }
}
