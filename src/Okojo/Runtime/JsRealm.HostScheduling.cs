namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    public void QueueMicrotask(JsFunction callback, params ReadOnlySpan<JsValue> args)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var copiedArgs = args.Length == 0 ? Array.Empty<JsValue>() : args.ToArray();
        Agent.EnqueueMicrotask(static stateObj =>
        {
            var state = (QueuedRealmCallback)stateObj!;
            _ = state.Realm.InvokeFunction(state.Callback, JsValue.FromObject(state.Realm.GlobalObject),
                state.Arguments);
        }, new QueuedRealmCallback
        {
            Realm = this,
            Callback = callback,
            Arguments = copiedArgs
        });
    }

    public void QueueHostTask(JsFunction callback, params ReadOnlySpan<JsValue> args)
    {
        QueueHostTask(InternalHostTaskQueueDefaults.Default, callback, args);
    }

    public void QueueHostTask(HostTaskQueueKey queueKey, JsFunction callback, params ReadOnlySpan<JsValue> args)
    {
        ArgumentNullException.ThrowIfNull(callback);
        EnqueueHostCallback(queueKey, callback, args);
    }

    internal void EnqueueHostCallback(JsFunction callback, params ReadOnlySpan<JsValue> args)
    {
        EnqueueHostCallback(InternalHostTaskQueueDefaults.Default, callback, args);
    }

    internal void EnqueueHostCallback(HostTaskQueueKey queueKey, JsFunction callback, params ReadOnlySpan<JsValue> args)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var copiedArgs = args.Length == 0 ? Array.Empty<JsValue>() : args.ToArray();
        Agent.EnqueueHostTask(queueKey, static stateObj =>
        {
            var state = (QueuedRealmCallback)stateObj!;
            _ = state.Realm.InvokeFunction(state.Callback, JsValue.FromObject(state.Realm.GlobalObject),
                state.Arguments);
        }, new QueuedRealmCallback
        {
            Realm = this,
            Callback = callback,
            Arguments = copiedArgs
        });
    }

    private sealed class QueuedRealmCallback
    {
        public required JsRealm Realm { get; init; }
        public required JsFunction Callback { get; init; }
        public required JsValue[] Arguments { get; init; }
    }
}
