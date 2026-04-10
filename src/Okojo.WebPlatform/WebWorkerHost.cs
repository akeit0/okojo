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

    public WorkerRuntime CreateWorker(JsRealm ownerRealm, string? moduleEntry, string? ownerReferrer)
    {
        ArgumentNullException.ThrowIfNull(ownerRealm);

        return WorkerRuntimeFactory.CreateWorkerRuntime(ownerRealm, hostedWorker =>
        {
            hostedWorker.ModuleEntry = moduleEntry;
            hostedWorker.ModuleReferrer = ownerReferrer;
            hostedWorker.StartBackgroundHost = options.StartBackgroundHost;
        });
    }
}
