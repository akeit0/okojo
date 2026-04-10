namespace Okojo.Runtime;

public sealed class HostPump
{
    public HostPump(JsAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        Agent = agent;
    }

    public JsAgent Agent { get; }

    public void PumpOnce()
    {
        Agent.PumpJobs();
    }

    public void PumpUntilIdle(int maxPasses = 1024)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            var pendingBefore = Agent.PendingJobCount;
            if (pendingBefore == 0)
                return;

            Agent.PumpJobs();
            if (Agent.PendingJobCount == 0)
                return;
        }
    }

    public void PumpUntilIdleWith(HostPump other, int maxPassesPerAgent = 1024)
    {
        ArgumentNullException.ThrowIfNull(other);

        for (var i = 0; i < maxPassesPerAgent; i++)
        {
            var pumped = false;

            if (Agent.PendingJobCount > 0)
            {
                PumpOnce();
                pumped = true;
            }

            if (other.Agent.PendingJobCount > 0)
            {
                other.PumpOnce();
                pumped = true;
            }

            if (!pumped)
                return;
        }
    }

    public bool RunUntil(Func<bool> completed, TimeSpan timeout, int idleSleepMilliseconds = 5)
    {
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
            if (Agent.IsTerminated)
                return completed();

            if (Agent.PendingJobCount > 0)
                Agent.PumpJobs();
            else if (idleSleepMilliseconds != 0) Thread.Sleep(idleSleepMilliseconds);

            if (completed())
                return true;
        }

        return completed();
    }

    public void RunUntilOrThrow(Func<bool> completed, TimeSpan timeout, int idleSleepMilliseconds = 5)
    {
        if (RunUntil(completed, timeout, idleSleepMilliseconds))
            return;

        throw new TimeoutException("HostPump timed out waiting for the requested condition.");
    }

    public async Task WaitForWorkAsync(CancellationToken cancellationToken = default)
    {
        await Agent.Engine.Options.HostServices.BackgroundScheduler
            .WaitHandleAsync(Agent.JobsAvailableWaitHandle, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Run(CancellationToken cancellationToken)
    {
        WaitHandle[] waits = [cancellationToken.WaitHandle, Agent.JobsAvailableWaitHandle];
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Agent.IsTerminated)
                return;

            if (Agent.PendingJobCount > 0)
            {
                Agent.PumpJobs();
                continue;
            }

            try
            {
                var signaled = WaitHandle.WaitAny(waits);
                if (signaled == 0)
                    return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }
}
