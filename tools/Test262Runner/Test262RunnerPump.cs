using Okojo.Runtime;

internal static class Test262RunnerPump
{
    public static bool PumpUntil(
        JsRealm realm,
        Func<bool> completed,
        Func<bool> hasTimedOut,
        int timeoutMs,
        Test262RunnerTimeProvider? runnerTime,
        out string? timeoutMessage)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(hasTimedOut);

        while (!completed())
        {
            if (hasTimedOut())
            {
                timeoutMessage = $"Timeout after {timeoutMs} ms";
                return false;
            }

            realm.PumpJobs();
            if (completed())
                break;

            WaitForWorkOrAdvance(realm.Agent, runnerTime, 1);
        }

        timeoutMessage = null;
        return true;
    }

    public static void RunWorkerLoop(
        JsRealm workerRealm,
        Func<bool> shouldStop,
        Test262RunnerTimeProvider? runnerTime)
    {
        ArgumentNullException.ThrowIfNull(workerRealm);
        ArgumentNullException.ThrowIfNull(shouldStop);

        while (!shouldStop())
        {
            workerRealm.PumpJobs();
            if (shouldStop())
                break;

            WaitForWorkOrAdvance(workerRealm.Agent, runnerTime, 5);
        }
    }

    private static void WaitForWorkOrAdvance(JsAgent agent, Test262RunnerTimeProvider? runnerTime,
        int idleWaitMilliseconds)
    {
        if (runnerTime is not null)
        {
            if (!agent.JobsAvailableWaitHandle.WaitOne(0) &&
                !runnerTime.AdvanceForAsyncPump())
                Thread.Yield();

            return;
        }

        agent.JobsAvailableWaitHandle.WaitOne(idleWaitMilliseconds);
    }
}
