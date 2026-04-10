using Okojo.Runtime;

namespace Okojo.Hosting;

/// <summary>
///     Runs host event-loop turns for embedders.
///     Each turn pumps at most one selected host queue task and then performs the
///     ECMAScript-side job drain for the affected agent.
///     See WHATWG HTML 8.1.7.3 and ECMA-262 9.5.
/// </summary>
public static class HostTurnRunner
{
    public static bool RunTurn(
        IHostTaskQueuePump queuePump,
        HostPump pump,
        IHostTurnObserver? observer = null)
    {
        return RunTurn(queuePump, pump, Array.Empty<HostTaskQueueKey>(), observer);
    }

    public static bool RunTurn(
        IHostTaskQueuePump queuePump,
        HostPump pump,
        ReadOnlySpan<HostTaskQueueKey> preferredOrder,
        IHostTurnObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(queuePump);
        ArgumentNullException.ThrowIfNull(pump);

        var (readyDelayedCount, pumpedHostTask, pumpedQueueKey) = queuePump switch
        {
            ManualHostEventLoop manualLoop => RunManualTurn(manualLoop, preferredOrder),
            ThreadAffinityHostLoop threadAffinityLoop => RunThreadAffinityTurn(threadAffinityLoop, preferredOrder),
            _ => RunGenericTurn(queuePump, preferredOrder)
        };

        observer?.OnTurnEvent(new(
            HostTurnPhase.BeforeTurn,
            false,
            null,
            readyDelayedCount,
            pump.Agent.PendingJobCount));

        observer?.OnTurnEvent(new(
            HostTurnPhase.AfterHostTask,
            pumpedHostTask,
            pumpedQueueKey,
            readyDelayedCount,
            pump.Agent.PendingJobCount));

        if (pump.Agent.PendingJobCount != 0)
        {
            observer?.OnTurnEvent(new(
                HostTurnPhase.BeforeMicrotaskCheckpoint,
                pumpedHostTask,
                pumpedQueueKey,
                readyDelayedCount,
                pump.Agent.PendingJobCount));
            pump.PumpUntilIdle();
            observer?.OnTurnEvent(new(
                HostTurnPhase.AfterMicrotaskCheckpoint,
                pumpedHostTask,
                pumpedQueueKey,
                readyDelayedCount,
                pump.Agent.PendingJobCount));
            observer?.OnTurnEvent(new(
                HostTurnPhase.AfterTurn,
                pumpedHostTask,
                pumpedQueueKey,
                readyDelayedCount,
                pump.Agent.PendingJobCount));
            return true;
        }

        observer?.OnTurnEvent(new(
            HostTurnPhase.AfterTurn,
            pumpedHostTask,
            pumpedQueueKey,
            readyDelayedCount,
            pump.Agent.PendingJobCount));
        return pumpedHostTask;
    }

    public static bool RunUntil(
        IHostTaskQueuePump queuePump,
        HostPump pump,
        Func<bool> completed,
        TimeSpan timeout,
        int idleSleepMilliseconds = 5,
        IHostTurnObserver? observer = null)
    {
        return RunUntil(queuePump, pump, completed, timeout, Array.Empty<HostTaskQueueKey>(), idleSleepMilliseconds,
            observer);
    }

    public static bool RunUntil(
        IHostTaskQueuePump queuePump,
        HostPump pump,
        Func<bool> completed,
        TimeSpan timeout,
        ReadOnlySpan<HostTaskQueueKey> preferredOrder,
        int idleSleepMilliseconds = 5,
        IHostTurnObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(queuePump);
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(completed);
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (idleSleepMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(idleSleepMilliseconds));

        if (completed())
            return true;

        var timeoutMs = timeout == Timeout.InfiniteTimeSpan
            ? Timeout.Infinite
            : checked((long)Math.Ceiling(timeout.TotalMilliseconds));
        var deadline = timeout == Timeout.InfiniteTimeSpan
            ? long.MaxValue
            : Environment.TickCount64 + timeoutMs;

        while (timeout == Timeout.InfiniteTimeSpan || Environment.TickCount64 < deadline)
        {
            if (completed())
                return true;

            var moved = RunTurn(queuePump, pump, preferredOrder, observer);
            if (!moved && idleSleepMilliseconds != 0)
                Thread.Sleep(idleSleepMilliseconds);
        }

        return completed();
    }

    private static (int ReadyDelayedCount, bool PumpedHostTask, HostTaskQueueKey? PumpedQueueKey) RunManualTurn(
        ManualHostEventLoop eventLoop,
        ReadOnlySpan<HostTaskQueueKey> preferredOrder)
    {
        var readyDelayedCount = eventLoop.PumpReadyDelayed();
        var pumped = preferredOrder.Length == 0
            ? eventLoop.TryPumpOne(out var queueKey)
            : eventLoop.TryPumpOne(out queueKey, preferredOrder);
        return (readyDelayedCount, pumped, pumped ? queueKey : null);
    }

    private static (int ReadyDelayedCount, bool PumpedHostTask, HostTaskQueueKey? PumpedQueueKey) RunThreadAffinityTurn(
        ThreadAffinityHostLoop eventLoop,
        ReadOnlySpan<HostTaskQueueKey> preferredOrder)
    {
        var pumped = preferredOrder.Length == 0
            ? eventLoop.TryPumpOne(out var queueKey, [])
            : eventLoop.TryPumpOne(out queueKey, preferredOrder);
        return (0, pumped, pumped ? queueKey : null);
    }

    private static (int ReadyDelayedCount, bool PumpedHostTask, HostTaskQueueKey? PumpedQueueKey) RunGenericTurn(
        IHostTaskQueuePump queuePump,
        ReadOnlySpan<HostTaskQueueKey> preferredOrder)
    {
        var pumped = preferredOrder.Length == 0
            ? queuePump.PumpOne()
            : queuePump.PumpOne(preferredOrder);
        return (0, pumped, null);
    }
}
