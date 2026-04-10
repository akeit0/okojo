namespace Okojo.Runtime;

public interface IHostMessageSerializer
{
    object? CloneCrossAgentPayload(object? payload);
    object? SerializeOutgoing(JsRealm realm, in JsValue value);
    JsValue DeserializeIncoming(JsRealm realm, object? payload);
}
