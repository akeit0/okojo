using Okojo.JavaScript;
using Okojo.JavaScript.Execution;

internal sealed class Test262RunnerAtomicsWaitPolicy : IAtomicsWaitPolicy
{
    public static Test262RunnerAtomicsWaitPolicy Shared { get; } = new();

    public bool CanSuspend(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return true;
    }

    public bool Wait(JsRealm realm, AtomicsWaitSignal signal, TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(signal);
        if (timeout is null)
        {
            signal.Wait();
            return true;
        }

        if (realm.TimeProvider is not Test262RunnerTimeProvider runnerTime)
            return signal.Wait(timeout.Value);

        var dueTime = NormalizeTimeout(timeout.Value);
        var timeoutState = new WaitTimeoutState();
        using var waitTimer = runnerTime.CreateWaitTimer(
            static state => Interlocked.Exchange(ref ((WaitTimeoutState)state!).TimedOut, 1),
            timeoutState,
            dueTime
        );

        while (!signal.IsSet && Volatile.Read(ref timeoutState.TimedOut) == 0)
        {
            if (!runnerTime.AdvanceForAsyncPump())
                runnerTime.Advance(TimeSpan.FromMilliseconds(1));
            Thread.Yield();
        }

        return signal.IsSet;
    }

    public IDisposable? ScheduleTimeout(JsRealm realm, TimeSpan timeout, Action timeoutCallback)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(timeoutCallback);
        return realm.TimeProvider.CreateTimer(
            static state => ((Action)state!).Invoke(),
            timeoutCallback,
            NormalizeTimeout(timeout),
            Timeout.InfiniteTimeSpan
        );
    }

    private static TimeSpan NormalizeTimeout(TimeSpan timeout)
    {
        return timeout <= TimeSpan.Zero ? TimeSpan.Zero
            : timeout.TotalMilliseconds >= int.MaxValue ? TimeSpan.FromMilliseconds(int.MaxValue)
            : timeout;
    }

    private sealed class WaitTimeoutState
    {
        public int TimedOut;
    }
}
