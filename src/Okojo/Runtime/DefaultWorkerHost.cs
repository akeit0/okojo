namespace Okojo.Runtime;

internal sealed class DefaultWorkerHost : IWorkerHost
{
    public static readonly DefaultWorkerHost Shared = new();

    private DefaultWorkerHost()
    {
    }

    public WorkerHostBinding CreateWorker(JsRealm ownerRealm, string? moduleEntry, string? ownerReferrer)
    {
        var agent = ownerRealm.Engine.CreateWorkerAgent();
        var realm = agent.MainRealm;
        var workerPump = new HostPump(agent);
        if (!string.IsNullOrEmpty(moduleEntry))
            _ = agent.EvaluateModule(realm, moduleEntry, ownerReferrer);

        return new()
        {
            Agent = agent,
            Realm = realm,
            Eval = source => realm.Eval(source),
            LoadModule = (ownerRealm, specifier) =>
            {
                var moduleNs = agent.EvaluateModule(realm, specifier, ownerRealm.GetCurrentModuleResolvedIdOrNull());
                return ownerRealm.BridgeFromOtherRealm(moduleNs);
            },
            Pump = callerRealm => { workerPump.PumpUntilIdleWith(new(callerRealm.Agent)); },
            Terminate = agent.Terminate
        };
    }
}
