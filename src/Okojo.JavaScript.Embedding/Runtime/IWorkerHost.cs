namespace Okojo.Runtime;

public interface IWorkerHost
{
    WorkerHostBinding CreateWorker(JsRealm ownerRealm, string? moduleEntry, string? ownerReferrer);
}
