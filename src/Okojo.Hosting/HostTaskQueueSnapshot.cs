using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Hosting;

/// <summary>
///     Snapshot of one host task queue for diagnostics and testing.
///     Queue selection is host-defined; this snapshot only reports current state.
/// </summary>
public readonly record struct HostTaskQueueSnapshot(
    HostTaskQueueKey QueueKey,
    int PendingTaskCount
);
