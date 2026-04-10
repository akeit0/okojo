namespace Okojo.Hosting;

public sealed class ManualDelayScheduler : IHostDelayScheduler
{
    private readonly object gate = new();
    private readonly List<ScheduledOperation> scheduled = [];
    private readonly TimeProvider timeProvider;

    public ManualDelayScheduler(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    public IHostDelayedOperation ScheduleDelayed(TimeSpan delay, Action<object?> callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var dueAt = timeProvider.GetUtcNow() + (delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay);
        var operation = new ScheduledOperation(this, dueAt, callback, state);
        lock (gate)
        {
            scheduled.Add(operation);
        }

        return operation;
    }

    public int PumpReady()
    {
        List<ScheduledOperation>? ready = null;
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            for (var i = scheduled.Count - 1; i >= 0; i--)
            {
                var operation = scheduled[i];
                if (!operation.IsReady(now))
                    continue;

                scheduled.RemoveAt(i);
                if (!operation.TryMarkDelivered())
                    continue;
                (ready ??= []).Add(operation);
            }
        }

        if (ready is null)
            return 0;

        for (var i = 0; i < ready.Count; i++)
            ready[i].Fire();
        return ready.Count;
    }

    private void Remove(ScheduledOperation operation)
    {
        lock (gate)
        {
            scheduled.Remove(operation);
        }
    }

    private sealed class ScheduledOperation(
        ManualDelayScheduler owner,
        DateTimeOffset dueAt,
        Action<object?> callback,
        object? state)
        : IHostDelayedOperation
    {
        private int status;

        public bool Cancel()
        {
            if (Interlocked.CompareExchange(ref status, 1, 0) != 0)
                return false;

            owner.Remove(this);
            return true;
        }

        public void Dispose()
        {
            _ = Cancel();
        }

        public bool IsReady(DateTimeOffset now)
        {
            return Volatile.Read(ref status) == 0 && dueAt <= now;
        }

        public bool TryMarkDelivered()
        {
            return Interlocked.CompareExchange(ref status, 2, 0) == 0;
        }

        public void Fire()
        {
            callback(state);
        }
    }
}
