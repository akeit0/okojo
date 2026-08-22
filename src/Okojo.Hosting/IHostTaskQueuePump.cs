using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Hosting;

/// <summary>
///     Host-managed queue pumping.
///     Hosts choose which queue to service next; this is not part of ECMAScript core.
///     See WHATWG HTML 8.1.7.3 Event loop processing model:
///     https://html.spec.whatwg.org/multipage/webappapis.html#event-loop-processing-model
/// </summary>
public interface IHostTaskQueuePump
{
    int PumpQueue(HostTaskQueueKey queueKey, int maxTasks = int.MaxValue);
    bool PumpOne(params ReadOnlySpan<HostTaskQueueKey> preferredOrder);
}
