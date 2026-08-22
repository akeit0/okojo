namespace Okojo.JavaScript.Embedding;

public interface IWorkerHost
{
    WorkerHostBinding CreateWorker(JsRealm ownerRealm, string? moduleEntry, string? ownerReferrer);
}
