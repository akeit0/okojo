using System.Buffers;
using Okojo.Runtime;

namespace Okojo.Hosting;

/// <summary>
///     Thread-affine host loop with explicit host queues.
///     HTML event loops have one or more task queues. See WHATWG HTML 8.1.7.1:
///     https://html.spec.whatwg.org/multipage/webappapis.html#event-loops
/// </summary>
public sealed class ThreadAffinityHostLoop(TimeProvider? timeProvider = null) : IHostTaskScheduler,
    IQueuedHostDelayScheduler, IHostTaskQueuePump, IHostEventLoopDiagnostics, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<HostTaskQueueKey, Queue<Action>> queues = [];
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ManualResetEventSlim workAvailable = new(false);
    private bool disposed;

    public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;

    private int PendingDelayedCount => DelayedOperation.GetPendingCount(this);

    private DateTimeOffset? NextDelayedDueAt => DelayedOperation.GetNextDueAt(this);

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        workAvailable.Set();
        workAvailable.Dispose();
    }

    public HostEventLoopSnapshot GetSnapshot()
    {
        lock (gate)
        {
            var snapshots = new HostTaskQueueSnapshot[queues.Count];
            var index = 0;
            foreach (var pair in queues.OrderBy(static pair => pair.Key.Name, StringComparer.Ordinal))
                snapshots[index++] = new(pair.Key, pair.Value.Count);

            return new(snapshots, PendingDelayedCount, NextDelayedDueAt);
        }
    }

    public int PumpQueue(HostTaskQueueKey queueKey, int maxTasks = int.MaxValue)
    {
        AssertOwnerThread();

        Action? single = null;
        Action[]? batch = null;
        var batchCount = 0;
        lock (gate)
        {
            if (!queues.TryGetValue(queueKey, out var queue) || queue.Count == 0)
            {
                workAvailable.Reset();
                return 0;
            }

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

            if (!HasQueuedWork_NoLock())
                workAvailable.Reset();
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

    public IHostDelayedOperation ScheduleDelayed(
        TimeSpan delay,
        HostTaskQueueKey targetQueue,
        Action<object?> callback,
        object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();
        return DelayedOperation.Create(this, timeProvider, delay, targetQueue, callback, state);
    }

    public void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != OwnerThreadId)
            throw new InvalidOperationException("ThreadAffinityHostLoop must run on its owner thread.");
    }

    public bool TryPumpOne(out HostTaskQueueKey pumpedQueueKey, ReadOnlySpan<HostTaskQueueKey> preferredOrder)
    {
        AssertOwnerThread();

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

    public bool WaitForWork(TimeSpan timeout)
    {
        AssertOwnerThread();
        ThrowIfDisposed();

        lock (gate)
        {
            if (HasQueuedWork_NoLock())
                return true;
        }

        return workAvailable.Wait(timeout);
    }

    public bool RunOneTurn(TimeSpan timeout, params ReadOnlySpan<HostPump> pumps)
    {
        return RunOneTurn(timeout, ReadOnlySpan<HostTaskQueueKey>.Empty, pumps);
    }

    public bool RunOneTurn(TimeSpan timeout, ReadOnlySpan<HostTaskQueueKey> preferredOrder,
        params ReadOnlySpan<HostPump> pumps)
    {
        AssertOwnerThread();

        while (preferredOrder.Length == 0
                   ? PumpOne()
                   : TryPumpOne(out _, preferredOrder))
        {
        }

        for (var i = 0; i < pumps.Length; i++)
            pumps[i].PumpUntilIdle();

        if (HasPendingWork(pumps))
            return true;

        if (!WaitForWork(timeout))
            return false;

        while (preferredOrder.Length == 0
                   ? PumpOne()
                   : TryPumpOne(out _, preferredOrder))
        {
        }

        for (var i = 0; i < pumps.Length; i++)
            pumps[i].PumpUntilIdle();
        return true;
    }

    private void EnqueueReady(HostTaskQueueKey queueKey, Action action)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (!queues.TryGetValue(queueKey, out var queue))
            {
                queue = [];
                queues[queueKey] = queue;
            }

            queue.Enqueue(action);
            workAvailable.Set();
        }
    }

    private bool HasPendingWork(ReadOnlySpan<HostPump> pumps)
    {
        lock (gate)
        {
            if (HasQueuedWork_NoLock())
                return true;
        }

        for (var i = 0; i < pumps.Length; i++)
            if (pumps[i].Agent.PendingJobCount != 0)
                return true;

        return false;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(ThreadAffinityHostLoop));
    }

    private bool HasQueuedWork_NoLock()
    {
        foreach (var pair in queues)
            if (pair.Value.Count != 0)
                return true;

        return false;
    }

    private sealed class AgentScheduler(ThreadAffinityHostLoop owner, HostTaskTarget target) : IQueuedHostAgentScheduler
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

    private sealed class DelayedOperation : IHostDelayedOperation
    {
        private static readonly object RegistryGate = new();

        private static readonly Dictionary<ThreadAffinityHostLoop, HashSet<DelayedOperation>> Registry =
            new(ReferenceEqualityComparer.Instance);

        private readonly Action<object?> callback;
        private readonly DateTimeOffset dueAt;
        private readonly ThreadAffinityHostLoop owner;
        private readonly object? state;

        private readonly HostTaskQueueKey targetQueue;
        private int status;
        private ITimer? timer;

        private DelayedOperation(ThreadAffinityHostLoop owner, HostTaskQueueKey targetQueue, DateTimeOffset dueAt,
            Action<object?> callback, object? state)
        {
            this.owner = owner;
            this.targetQueue = targetQueue;
            this.dueAt = dueAt;
            this.callback = callback;
            this.state = state;
        }

        public bool Cancel()
        {
            if (Interlocked.CompareExchange(ref status, 1, 0) != 0)
                return false;

            Unregister(this);
            ReleaseTimer();
            return true;
        }

        public void Dispose()
        {
            _ = Cancel();
        }

        public static DelayedOperation Create(
            ThreadAffinityHostLoop owner,
            TimeProvider timeProvider,
            TimeSpan delay,
            HostTaskQueueKey targetQueue,
            Action<object?> callback,
            object? state)
        {
            var normalizedDueTime = delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay;
            var operation = new DelayedOperation(owner, targetQueue, timeProvider.GetUtcNow() + normalizedDueTime,
                callback, state);
            var dueTime = delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay;
            Register(operation);
            operation.timer = timeProvider is ITimerFactory timerFactory
                ? timerFactory.CreateJsTimer(static opState => ((DelayedOperation)opState!).OnReady(), operation,
                    dueTime,
                    Timeout.InfiniteTimeSpan)
                : timeProvider.CreateTimer(static opState => ((DelayedOperation)opState!).OnReady(), operation, dueTime,
                    Timeout.InfiniteTimeSpan);
            return operation;
        }

        private void OnReady()
        {
            if (Interlocked.CompareExchange(ref status, 2, 0) != 0)
                return;

            Unregister(this);
            ReleaseTimer();
            owner.EnqueueReady(targetQueue, () => callback(state));
        }

        private void ReleaseTimer()
        {
            Interlocked.Exchange(ref timer, null)?.Dispose();
        }

        public static int GetPendingCount(ThreadAffinityHostLoop owner)
        {
            lock (RegistryGate)
            {
                return Registry.TryGetValue(owner, out var items) ? items.Count : 0;
            }
        }

        public static DateTimeOffset? GetNextDueAt(ThreadAffinityHostLoop owner)
        {
            lock (RegistryGate)
            {
                if (!Registry.TryGetValue(owner, out var items) || items.Count == 0)
                    return null;

                DateTimeOffset? next = null;
                foreach (var item in items)
                    if (next is null || item.dueAt < next.Value)
                        next = item.dueAt;

                return next;
            }
        }

        private static void Register(DelayedOperation operation)
        {
            lock (RegistryGate)
            {
                if (!Registry.TryGetValue(operation.owner, out var items))
                {
                    items = new(ReferenceEqualityComparer.Instance);
                    Registry[operation.owner] = items;
                }

                items.Add(operation);
            }
        }

        private static void Unregister(DelayedOperation operation)
        {
            lock (RegistryGate)
            {
                if (!Registry.TryGetValue(operation.owner, out var items))
                    return;

                items.Remove(operation);
                if (items.Count == 0)
                    Registry.Remove(operation.owner);
            }
        }
    }
}
