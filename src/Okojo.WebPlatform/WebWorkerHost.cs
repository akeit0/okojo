using Okojo.Hosting;
using Okojo.Runtime;

namespace Okojo.WebPlatform;

public sealed class WebWorkerHost : IHostingJsWorkerHost
{
    private readonly WebWorkerOptions options;

    public WebWorkerHost(WebWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    public WorkerRuntime CreateWorker(
        JsRealm ownerRealm,
        string? scriptEntry,
        string? ownerReferrer,
        WorkerScriptType scriptType)
    {
        ArgumentNullException.ThrowIfNull(ownerRealm);

        return WorkerRuntimeFactory.CreateWorkerRuntime(ownerRealm, hostedWorker =>
        {
            hostedWorker.ScriptEntry = scriptEntry;
            hostedWorker.ScriptReferrer = ownerReferrer;
            hostedWorker.ScriptType = scriptType;
            hostedWorker.StartBackgroundHost = options.StartBackgroundHost;
        });
    }
}
