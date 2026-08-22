using Okojo.JavaScript.Objects;

namespace Okojo.JavaScript.Embedding;

internal sealed class WorkerMessaging(
    IHostMessageSerializer messageSerializer,
    IWorkerHost workerHost,
    HostTaskQueueKey workerMessageQueueKey
)
{
    private readonly IHostMessageSerializer serializer = messageSerializer;
    private readonly IWorkerHost host = workerHost;
    private readonly HostTaskQueueKey queueKey = workerMessageQueueKey;
    private readonly object realmsGate = new();
    private readonly Dictionary<JsRealm, RealmState> realms = new(
        ReferenceEqualityComparer.Instance
    );

    internal (WorkerHostBinding Binding, JsPlainObject Handle) CreateWorkerHandle(
        JsRealm ownerRealm,
        string? moduleEntry,
        string? ownerReferrer,
        WorkerHandleFactory.OkojoWorkerHandleAtoms atoms
    )
    {
        var binding = host.CreateWorker(ownerRealm, moduleEntry, ownerReferrer);
        var handle = WorkerHandleFactory.CreateHandle(
            ownerRealm,
            binding,
            this,
            atoms,
            agentId => RemoveWorkerHandle(ownerRealm, agentId)
        );
        return (binding, handle);
    }

    internal object? SerializeOutgoing(JsRealm realm, in JsValue value)
    {
        return serializer.SerializeOutgoing(realm, value);
    }

    internal void PostSerializedMessage(JsAgent source, JsAgent target, object? payload)
    {
        source.PostMessage(target, payload, queueKey);
    }

    internal void RegisterGlobalReceiver(JsRealm realm, Action<JsValue, bool> receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        var state = GetState(realm);
        lock (state.Gate)
        {
            state.GlobalReceiver = receiver;
        }

        EnsureMessageDispatchHook(realm, state);
    }

    internal void RegisterWorkerHandle(
        JsRealm ownerRealm,
        int workerAgentId,
        Action<JsValue, bool> receiver
    )
    {
        ArgumentNullException.ThrowIfNull(receiver);
        var state = GetState(ownerRealm);
        lock (state.Gate)
        {
            state.WorkerReceiversByAgentId[workerAgentId] = receiver;
        }

        EnsureMessageDispatchHook(ownerRealm, state);
    }

    internal void RemoveWorkerHandle(JsRealm ownerRealm, int workerAgentId)
    {
        var state = GetState(ownerRealm);
        lock (state.Gate)
        {
            state.WorkerReceiversByAgentId.Remove(workerAgentId);
        }
    }

    private void EnsureMessageDispatchHook(JsRealm realm, RealmState state)
    {
        lock (state.Gate)
        {
            if (state.MessageDispatchHookInstalled)
                return;

            state.MessageDispatchHookInstalled = true;
            realm.Agent.MessageReceived += (sender, payload) =>
                DispatchMessage(realm, state, sender, payload);
        }
    }

    private void DispatchMessage(JsRealm realm, RealmState state, JsAgent sender, object? payload)
    {
        if (
            realm.Agent.ParentAgent is not null
            && !ReferenceEquals(sender, realm.Agent.ParentAgent)
        )
            return;

        Action<JsValue, bool>? receiver;
        lock (state.Gate)
        {
            state.WorkerReceiversByAgentId.TryGetValue(sender.Id, out receiver);
            receiver ??= state.GlobalReceiver;
        }

        if (receiver is null)
            return;

        var isError = false;
        JsValue data;
        try
        {
            data = serializer.DeserializeIncoming(realm, payload);
        }
        catch (Exception)
        {
            data = JsValue.Undefined;
            isError = true;
        }

        receiver(data, isError);
    }

    private RealmState GetState(JsRealm realm)
    {
        lock (realmsGate)
        {
            if (!realms.TryGetValue(realm, out var state))
            {
                state = new RealmState();
                realms.Add(realm, state);
            }

            return state;
        }
    }

    private sealed class RealmState
    {
        public readonly object Gate = new();
        public readonly Dictionary<int, Action<JsValue, bool>> WorkerReceiversByAgentId = new();
        public Action<JsValue, bool>? GlobalReceiver;
        public bool MessageDispatchHookInstalled;
    }
}
