using Okojo.Runtime;

namespace Okojo.Hosting;

public interface IHostingJsWorkerHost
{
    WorkerRuntime CreateWorker(JsRealm ownerRealm, string? moduleEntry, string? ownerReferrer);
}
