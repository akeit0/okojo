namespace Okojo.Runtime;

public sealed class QueueMicrotaskApiModule : IRealmApiModule
{
    private QueueMicrotaskApiModule()
    {
    }

    public static QueueMicrotaskApiModule Shared { get; } = new();

    public void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);

        if (!realm.Global.TryGetValue("queueMicrotask", out _))
            realm.Global["queueMicrotask"] = JsValue.FromObject(CreateQueueMicrotaskFunction(realm));
    }

    internal static JsHostFunction CreateQueueMicrotaskFunction(JsRealm realm)
    {
        return new(realm, static (in info) =>
        {
            if (info.Arguments.Length == 0 ||
                !info.Arguments[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callback)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "queueMicrotask callback is not a function",
                    "QUEUE_MICROTASK_CALLBACK_NOT_FUNCTION");

            info.Realm.QueueMicrotask(callback);
            return JsValue.Undefined;
        }, "queueMicrotask", 1);
    }
}
