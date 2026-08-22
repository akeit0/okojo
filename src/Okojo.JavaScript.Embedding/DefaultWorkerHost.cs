namespace Okojo.JavaScript.Embedding;

internal sealed class DefaultWorkerHost : IWorkerHost
{
    public static readonly DefaultWorkerHost Shared = new();

    private DefaultWorkerHost() { }

    public WorkerHostBinding CreateWorker(
        JsRealm ownerRealm,
        string? scriptEntry,
        string? ownerReferrer,
        WorkerScriptType scriptType
    )
    {
        var agent = ownerRealm.Agent.CreateWorkerAgent();
        var realm = agent.MainRealm;
        var workerPump = new HostPump(agent);
        if (!string.IsNullOrEmpty(scriptEntry))
        {
            if (scriptType == WorkerScriptType.Module)
            {
                _ = agent.EvaluateModule(realm, scriptEntry, ownerReferrer);
            }
            else
            {
                var source = ownerRealm.LoadWorkerScript(scriptEntry, ownerReferrer);
                realm.Execute(source);
            }
        }

        return new()
        {
            Agent = agent,
            Realm = realm,
            Eval = source => realm.Eval(source),
            LoadModule = (ownerRealm, specifier) =>
            {
                var moduleNs = agent.EvaluateModule(
                    realm,
                    specifier,
                    ownerRealm.CurrentModuleResolvedId
                );
                return ownerRealm.BridgeFromOtherRealm(moduleNs);
            },
            Pump = callerRealm =>
            {
                workerPump.PumpUntilIdleWith(new(callerRealm.Agent));
            },
            Terminate = agent.Terminate,
        };
    }
}
