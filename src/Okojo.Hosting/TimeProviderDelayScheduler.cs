using Okojo.Runtime;

namespace Okojo.Hosting;

public sealed class TimeProviderDelayScheduler : IHostDelayScheduler
{
    private readonly TimeProvider timeProvider;

    public TimeProviderDelayScheduler(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    public IHostDelayedOperation ScheduleDelayed(TimeSpan delay, Action<object?> callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ScheduledOperation.Create(timeProvider, delay, callback, state);
    }

    private sealed class ScheduledOperation : IHostDelayedOperation
    {
        private readonly Action<object?> callback;
        private readonly object? state;
        private int status;
        private ITimer? timer;

        private ScheduledOperation(Action<object?> callback, object? state)
        {
            this.callback = callback;
            this.state = state;
        }

        public bool Cancel()
        {
            if (Interlocked.CompareExchange(ref status, 1, 0) != 0)
                return false;

            ReleaseTimer();
            return true;
        }

        public void Dispose()
        {
            _ = Cancel();
        }

        public static ScheduledOperation Create(
            TimeProvider timeProvider,
            TimeSpan delay,
            Action<object?> callback,
            object? state)
        {
            var operation = new ScheduledOperation(callback, state);
            var dueTime = delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay;
            operation.timer = timeProvider is ITimerFactory timerFactory
                ? timerFactory.CreateJsTimer(static opState => ((ScheduledOperation)opState!).OnReady(), operation,
                    dueTime,
                    Timeout.InfiniteTimeSpan)
                : timeProvider.CreateTimer(static opState => ((ScheduledOperation)opState!).OnReady(), operation,
                    dueTime,
                    Timeout.InfiniteTimeSpan);
            return operation;
        }

        private void OnReady()
        {
            if (Interlocked.CompareExchange(ref status, 2, 0) != 0)
                return;

            ReleaseTimer();
            callback(state);
        }

        private void ReleaseTimer()
        {
            Interlocked.Exchange(ref timer, null)?.Dispose();
        }
    }
}
