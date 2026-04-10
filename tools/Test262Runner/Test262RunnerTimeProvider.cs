using Okojo.Runtime;

internal sealed class Test262RunnerTimeProvider(
    TimeSpan? startUtcOffset = null,
    TimeSpan? observationQuantum = null,
    TimeSpan? sleepQuantum = null,
    TimeSpan? asyncPumpMinimumAdvanceQuantum = null)
    : TimeProvider, ITimerFactory
{
    private readonly TimeSpan asyncPumpMinimumAdvanceQuantum =
        asyncPumpMinimumAdvanceQuantum ?? TimeSpan.FromMilliseconds(1);

    private readonly object gate = new();
    private readonly TimeSpan observationQuantum = observationQuantum ?? TimeSpan.FromMilliseconds(1);
    private readonly TimeSpan sleepQuantum = sleepQuantum ?? TimeSpan.FromMilliseconds(100);
    private readonly List<RunnerTimer> timers = [];
    private long timestamp;
    private DateTimeOffset utcNow = DateTimeOffset.UnixEpoch + (startUtcOffset ?? TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public ITimer CreateJsTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        return CreateTimerCore(RunnerTimerKind.Js, callback, state, dueTime, period);
    }

    public ITimer CreateWaitTimer(TimerCallback callback, object? state, TimeSpan dueTime)
    {
        return CreateTimerCore(RunnerTimerKind.Wait, callback, state, dueTime, Timeout.InfiniteTimeSpan);
    }

    public override DateTimeOffset GetUtcNow()
    {
        AdvanceOnObservation();
        lock (gate)
        {
            return utcNow;
        }
    }

    public override long GetTimestamp()
    {
        AdvanceOnObservation();
        lock (gate)
        {
            return timestamp;
        }
    }

    public void Advance(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero)
            return;

        List<RunnerTimer>? dueTimers = null;
        lock (gate)
        {
            utcNow += delta;
            timestamp += delta.Ticks;
            CollectDueTimers_NoLock(ref dueTimers);
        }

        InvokeDueTimers(dueTimers);
    }

    public void AdvanceForSleep(TimeSpan requestedDelay)
    {
        if (requestedDelay <= TimeSpan.Zero)
            return;

        Advance(requestedDelay < sleepQuantum ? sleepQuantum : requestedDelay);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        return CreateTimerCore(RunnerTimerKind.Js, callback, state, dueTime, period);
    }

    public bool AdvanceForAsyncPump()
    {
        if (TryGetNextDueTimerDelta(true, out var delta))
        {
            if (delta < asyncPumpMinimumAdvanceQuantum)
                delta = asyncPumpMinimumAdvanceQuantum;
            if (observationQuantum > TimeSpan.Zero && delta > observationQuantum)
                delta = observationQuantum;
            Advance(delta);
            return true;
        }

        if (observationQuantum > TimeSpan.Zero)
        {
            Advance(observationQuantum);
            return true;
        }

        return false;
    }

    private void AdvanceOnObservation()
    {
        if (observationQuantum <= TimeSpan.Zero)
            return;
        Advance(observationQuantum);
    }

    private ITimer CreateTimerCore(RunnerTimerKind kind, TimerCallback callback, object? state, TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new RunnerTimer(this, kind, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private bool TryGetNextDueTimerDelta(bool allowWaitTimers, out TimeSpan delta)
    {
        var nextDueTimestamp = long.MaxValue;

        lock (gate)
        {
            for (var i = 0; i < timers.Count; i++)
            {
                var timer = timers[i];
                if (!timer.IsArmed)
                    continue;
                if (!allowWaitTimers && timer.Kind == RunnerTimerKind.Wait)
                    continue;
                if (timer.NextDueTimestamp < nextDueTimestamp)
                    nextDueTimestamp = timer.NextDueTimestamp;
            }
        }

        if (nextDueTimestamp == long.MaxValue)
        {
            delta = default;
            return false;
        }

        lock (gate)
        {
            var ticks = nextDueTimestamp - timestamp;
            delta = ticks <= 0 ? TimeSpan.FromTicks(1) : TimeSpan.FromTicks(ticks);
        }

        return true;
    }

    private void Register(RunnerTimer timer)
    {
        lock (gate)
        {
            timers.Add(timer);
        }
    }

    private void Unregister(RunnerTimer timer)
    {
        lock (gate)
        {
            timers.Remove(timer);
        }
    }

    private void CollectDueTimers_NoLock(ref List<RunnerTimer>? dueTimers)
    {
        for (var i = timers.Count - 1; i >= 0; i--)
        {
            var timer = timers[i];
            if (!timer.IsArmed || timer.NextDueTimestamp > timestamp)
                continue;

            dueTimers ??= [];
            dueTimers.Add(timer);
            if (!timer.MoveNext_NoLock(timestamp))
                timers.RemoveAt(i);
        }
    }

    private static void InvokeDueTimers(List<RunnerTimer>? dueTimers)
    {
        if (dueTimers is null)
            return;

        for (var i = 0; i < dueTimers.Count; i++)
            dueTimers[i].Fire();
    }

    private enum RunnerTimerKind
    {
        Js,
        Wait
    }

    private sealed class RunnerTimer(
        Test262RunnerTimeProvider owner,
        RunnerTimerKind kind,
        TimerCallback callback,
        object? state)
        : ITimer
    {
        private bool armed;
        private bool disposed;
        private long periodTicks;

        public RunnerTimerKind Kind => kind;
        public long NextDueTimestamp { get; private set; }
        public bool IsArmed => armed && !disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (disposed)
                    return false;

                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    armed = false;
                    owner.timers.Remove(this);
                    return true;
                }

                var dueTicks = dueTime <= TimeSpan.Zero ? 1 : dueTime.Ticks;
                periodTicks = period <= TimeSpan.Zero || period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                NextDueTimestamp = owner.timestamp + dueTicks;
                armed = true;
                if (!owner.timers.Contains(this))
                    owner.timers.Add(this);
                return true;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            armed = false;
            owner.Unregister(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Fire()
        {
            if (disposed)
                return;
            callback(state);
        }

        public bool MoveNext_NoLock(long nowTimestamp)
        {
            if (disposed || !armed)
                return false;

            if (periodTicks == 0)
            {
                armed = false;
                return false;
            }

            NextDueTimestamp = nowTimestamp + periodTicks;
            return true;
        }
    }
}
