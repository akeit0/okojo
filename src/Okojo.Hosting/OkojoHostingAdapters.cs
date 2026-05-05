using Okojo.Runtime;

namespace Okojo.Hosting;

internal sealed class HostingMessageSerializerAdapter(IHostingMessageSerializer inner) : IHostMessageSerializer
{
    public object? CloneCrossAgentPayload(object? payload)
    {
        return inner.CloneCrossAgentPayload(payload);
    }

    public object? SerializeOutgoing(JsRealm realm, in JsValue value)
    {
        return inner.SerializeOutgoing(realm, value);
    }

    public JsValue DeserializeIncoming(JsRealm realm, object? payload)
    {
        return inner.DeserializeIncoming(realm, payload);
    }
}

internal sealed class HostingJsWorkerHostAdapter(IHostingJsWorkerHost inner) : IWorkerHost
{
    public WorkerHostBinding CreateWorker(
        JsRealm ownerRealm,
        string? scriptEntry,
        string? ownerReferrer,
        WorkerScriptType scriptType)
    {
        var hostedWorker = inner.CreateWorker(ownerRealm, scriptEntry, ownerReferrer, scriptType);
        return new()
        {
            Agent = hostedWorker.Agent,
            Realm = hostedWorker.Realm,
            Eval = hostedWorker.Eval,
            LoadModule = hostedWorker.LoadModule,
            Pump = callerRealm =>
            {
                if (!hostedWorker.IsBackgroundHostRunning)
                    hostedWorker.Pump.PumpUntilIdleWith(new(callerRealm.Agent));
                else
                    new HostPump(callerRealm.Agent).PumpUntilIdle();
            },
            Terminate = hostedWorker.Terminate
        };
    }
}
