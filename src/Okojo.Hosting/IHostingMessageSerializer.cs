using Okojo.Runtime;

namespace Okojo.Hosting;

public interface IHostingMessageSerializer
{
    object? CloneCrossAgentPayload(object? payload);
    object? SerializeOutgoing(JsRealm realm, in JsValue value);
    JsValue DeserializeIncoming(JsRealm realm, object? payload);
}
