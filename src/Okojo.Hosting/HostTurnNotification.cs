using Okojo.Runtime;

namespace Okojo.Hosting;

/// <summary>
///     Observability payload for one host turn.
///     This is for embedder/runtime diagnostics and should not change host semantics.
/// </summary>
public readonly record struct HostTurnNotification(
    HostTurnPhase Phase,
    bool RanHostTask,
    HostTaskQueueKey? HostTaskQueueKey,
    int ReadyDelayedCount,
    int PendingJobCount);
