namespace Okojo.Hosting;

/// <summary>
///     Snapshot of host event-loop state for diagnostics.
/// </summary>
public sealed record HostEventLoopSnapshot(
    IReadOnlyList<HostTaskQueueSnapshot> Queues,
    int PendingDelayedCount,
    DateTimeOffset? NextDelayedDueAt);
