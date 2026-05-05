namespace Okojo.Runtime;

public interface IWorkerHost
{
    WorkerHostBinding CreateWorker(
        JsRealm ownerRealm,
        string? scriptEntry,
        string? ownerReferrer,
        WorkerScriptType scriptType);
}
