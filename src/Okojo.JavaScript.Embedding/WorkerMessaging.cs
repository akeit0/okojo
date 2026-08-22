using Okojo.JavaScript.Objects;

namespace Okojo.JavaScript.Embedding;

public sealed class WorkerMessaging
{
    private readonly IHostMessageSerializer serializer;
    private readonly IWorkerHost host;
    private readonly HostTaskQueueKey queueKey;

    internal WorkerMessaging(
        IHostMessageSerializer messageSerializer,
        IWorkerHost workerHost,
        HostTaskQueueKey workerMessageQueueKey
    )
    {
        serializer = messageSerializer;
        host = workerHost;
        queueKey = workerMessageQueueKey;
    }

    private static readonly WorkerHandleFactory.OkojoWorkerHandleAtoms WorkerHandleAtoms = new(
        AtomTable.IdOnmessage,
        AtomTable.IdOnmessageerror,
        AtomTable.IdPostMessage,
        AtomTable.IdEval,
        AtomTable.IdLoadModule,
        AtomTable.IdPump,
        AtomTable.IdTerminate
    );

    private readonly object realmsGate = new();
    private readonly Dictionary<JsRealm, RealmState> realms = new(
        ReferenceEqualityComparer.Instance
    );

    public (WorkerHostBinding Binding, JsPlainObject Handle) CreateWorkerHandle(
        JsRealm ownerRealm,
        string? scriptEntry,
        string? ownerReferrer,
        WorkerScriptType scriptType
    )
    {
        ArgumentNullException.ThrowIfNull(ownerRealm);
        var binding = host.CreateWorker(ownerRealm, scriptEntry, ownerReferrer, scriptType);
        var handle = WorkerHandleFactory.CreateHandle(
            ownerRealm,
            binding,
            this,
            WorkerHandleAtoms,
            agentId => RemoveWorkerHandle(ownerRealm, agentId)
        );
        return (binding, handle);
    }

    public void PostMessage(JsRealm sourceRealm, JsAgent target, JsValue? value)
    {
        ArgumentNullException.ThrowIfNull(sourceRealm);
        ArgumentNullException.ThrowIfNull(target);
        object? payload = null;
        if (value.HasValue)
        {
            var valueToSerialize = value.Value;
            payload = serializer.SerializeOutgoing(sourceRealm, in valueToSerialize);
        }

        PostSerializedMessage(sourceRealm.Agent, target, payload);
    }

    internal object? SerializeOutgoing(JsRealm realm, in JsValue value)
    {
        return serializer.SerializeOutgoing(realm, value);
    }

    internal void PostSerializedMessage(JsAgent source, JsAgent target, object? payload)
    {
        source.PostMessage(target, payload, queueKey);
    }

    public void RegisterGlobalReceiver(JsRealm realm, Action<JsValue, bool> receiver)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(receiver);
        var state = GetState(realm);
        lock (state.Gate)
        {
            state.GlobalReceiver = receiver;
        }

        EnsureMessageDispatchHook(realm, state);
    }

    public void RegisterWorkerHandle(
        JsRealm ownerRealm,
        int workerAgentId,
        Action<JsValue, bool> receiver
    )
    {
        ArgumentNullException.ThrowIfNull(ownerRealm);
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
