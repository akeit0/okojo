using System.Buffers;
using Okojo.Runtime;

namespace Okojo.Hosting;

/// <summary>
///     Manual host event loop with explicit task queues.
///     This models the host-side "one or more task queues" concept from WHATWG HTML
///     8.1.7.1 and leaves queue selection to the embedder.
///     https://html.spec.whatwg.org/multipage/webappapis.html#event-loops
/// </summary>
public sealed class ManualHostEventLoop : IHostTaskScheduler, IQueuedHostDelayScheduler, IHostTaskQueuePump,
    IHostEventLoopDiagnostics
{
    private readonly List<DelayedOperation> delayed = [];
    private readonly object gate = new();
    private readonly Dictionary<HostTaskQueueKey, Queue<Action>> queues = [];
    private readonly TimeProvider timeProvider;

    public ManualHostEventLoop(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    public HostEventLoopSnapshot GetSnapshot()
    {
        lock (gate)
        {
            var snapshots = new HostTaskQueueSnapshot[queues.Count];
            var index = 0;
            foreach (var pair in queues.OrderBy(static pair => pair.Key.Name, StringComparer.Ordinal))
                snapshots[index++] = new(pair.Key, pair.Value.Count);

            DateTimeOffset? nextDueAt = null;
            for (var i = 0; i < delayed.Count; i++)
            {
                var item = delayed[i];
                if (nextDueAt is null || item.DueAt < nextDueAt.Value)
                    nextDueAt = item.DueAt;
            }

            return new(snapshots, delayed.Count, nextDueAt);
        }
    }

    public int PumpQueue(HostTaskQueueKey queueKey, int maxTasks = int.MaxValue)
    {
        if (maxTasks <= 0)
            return 0;

        Action? single = null;
        Action[]? batch = null;
        var batchCount = 0;
        lock (gate)
        {
            if (!queues.TryGetValue(queueKey, out var queue) || queue.Count == 0)
                return 0;

            var count = Math.Min(maxTasks, queue.Count);
            if (count == 1)
            {
                single = queue.Dequeue();
            }
            else
            {
                batch = ArrayPool<Action>.Shared.Rent(count);
                batchCount = count;
                for (var i = 0; i < count; i++)
                    batch[i] = queue.Dequeue();
            }
        }

        if (single is not null)
        {
            single();
            return 1;
        }

        try
        {
            for (var i = 0; i < batchCount; i++)
                batch![i]();
            return batchCount;
        }
        finally
        {
            Array.Clear(batch!, 0, batchCount);
            ArrayPool<Action>.Shared.Return(batch!);
        }
    }

    public bool PumpOne(params ReadOnlySpan<HostTaskQueueKey> preferredOrder)
    {
        return TryPumpOne(out _, preferredOrder);
    }

    public IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new AgentScheduler(this, target);
    }

    public IHostDelayedOperation ScheduleDelayed(TimeSpan delay, Action<object?> callback, object? state)
    {
        return ScheduleDelayed(delay, HostingTaskQueueKeys.Default, callback, state);
    }

    public IHostDelayedOperation ScheduleDelayed(TimeSpan delay, HostTaskQueueKey targetQueue, Action<object?> callback,
        object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var operation = new DelayedOperation(this, targetQueue, timeProvider.GetUtcNow() + NormalizeDelay(delay),
            callback, state);
        lock (gate)
        {
            delayed.Add(operation);
        }

        return operation;
    }

    public int PumpReadyDelayed()
    {
        DelayedOperation[]? due = null;
        var dueCount = 0;
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            for (var i = delayed.Count - 1; i >= 0; i--)
            {
                var item = delayed[i];
                if (!item.IsDue(now))
                    continue;

                delayed.RemoveAt(i);
                if (!item.TryMarkDelivered())
                    continue;

                due ??= ArrayPool<DelayedOperation>.Shared.Rent(4);
                if (dueCount == due.Length)
                    due = GrowPooledArray(due, dueCount);
                due[dueCount++] = item;
            }
        }

        if (dueCount == 0)
            return 0;

        try
        {
            for (var i = 0; i < dueCount; i++)
            {
                var item = due![i];
                EnqueueReady(item.TargetQueue, item.Invoke);
            }

            return dueCount;
        }
        finally
        {
            Array.Clear(due!, 0, dueCount);
            ArrayPool<DelayedOperation>.Shared.Return(due!);
        }
    }

    public bool TryPumpOne(out HostTaskQueueKey pumpedQueueKey, ReadOnlySpan<HostTaskQueueKey> preferredOrder)
    {
        if (preferredOrder.Length != 0)
            for (var i = 0; i < preferredOrder.Length; i++)
                if (PumpQueue(preferredOrder[i], 1) != 0)
                {
                    pumpedQueueKey = preferredOrder[i];
                    return true;
                }

        HostTaskQueueKey[]? keys = null;
        var keyCount = 0;
        lock (gate)
        {
            if (queues.Count == 0)
            {
                pumpedQueueKey = default;
                return false;
            }

            keys = ArrayPool<HostTaskQueueKey>.Shared.Rent(queues.Count);
            foreach (var key in queues.Keys)
                keys[keyCount++] = key;
        }

        try
        {
            for (var i = 0; i < keyCount; i++)
                if (PumpQueue(keys[i], 1) != 0)
                {
                    pumpedQueueKey = keys[i];
                    return true;
                }

            pumpedQueueKey = default;
            return false;
        }
        finally
        {
            ArrayPool<HostTaskQueueKey>.Shared.Return(keys!);
        }
    }

    public bool TryPumpOne(out HostTaskQueueKey pumpedQueueKey, params HostTaskQueueKey[] preferredOrder)
    {
        return TryPumpOne(out pumpedQueueKey, preferredOrder.AsSpan());
    }

    private void EnqueueReady(HostTaskQueueKey queueKey, Action action)
    {
        lock (gate)
        {
            if (!queues.TryGetValue(queueKey, out var queue))
            {
                queue = [];
                queues[queueKey] = queue;
            }

            queue.Enqueue(action);
        }
    }

    private void RemoveDelayed(DelayedOperation operation)
    {
        lock (gate)
        {
            delayed.Remove(operation);
        }
    }

    private static TimeSpan NormalizeDelay(TimeSpan delay)
    {
        return delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay;
    }

    private static T[] GrowPooledArray<T>(T[] current, int count)
    {
        var next = ArrayPool<T>.Shared.Rent(current.Length * 2);
        Array.Copy(current, next, count);
        Array.Clear(current, 0, count);
        ArrayPool<T>.Shared.Return(current);
        return next;
    }

    private sealed class AgentScheduler(ManualHostEventLoop owner, HostTaskTarget target) : IQueuedHostAgentScheduler
    {
        public void EnqueueTask(Action<object?> callback, object? state)
        {
            EnqueueTask(HostingTaskQueueKeys.Default, callback, state);
        }

        public void EnqueueTask(HostTaskQueueKey queueKey, Action<object?> callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            owner.EnqueueReady(queueKey, () =>
            {
                if (!target.IsTerminated)
                    target.EnqueueTask(callback, state);
            });
        }
    }

    private sealed class DelayedOperation(
        ManualHostEventLoop owner,
        HostTaskQueueKey targetQueue,
        DateTimeOffset dueAt,
        Action<object?> callback,
        object? state)
        : IHostDelayedOperation
    {
        private int status;

        public HostTaskQueueKey TargetQueue { get; } = targetQueue;
        public DateTimeOffset DueAt { get; } = dueAt;

        public bool Cancel()
        {
            if (Interlocked.CompareExchange(ref status, 1, 0) != 0)
                return false;

            owner.RemoveDelayed(this);
            return true;
        }

        public void Dispose()
        {
            _ = Cancel();
        }

        public bool IsDue(DateTimeOffset now)
        {
            return Volatile.Read(ref status) == 0 && DueAt <= now;
        }

        public bool TryMarkDelivered()
        {
            return Interlocked.CompareExchange(ref status, 2, 0) == 0;
        }

        public void Invoke()
        {
            callback(state);
        }
    }
}
