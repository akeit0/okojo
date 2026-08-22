namespace Okojo.JavaScript.Embedding;

internal sealed class HostJobQueue
{
    private readonly object gate = new();
    private readonly Queue<PendingJob> hostJobs = new();
    private readonly Queue<PendingJob> hostPriorityJobs = new();
    private readonly IHostAgentScheduler scheduler;
    private readonly Action signalWork;
    private readonly Func<bool> isTerminated;
    private readonly Func<int> runPromiseJobs;

    internal HostJobQueue(
        TimeProvider timeProvider,
        IHostTaskScheduler taskScheduler,
        Func<bool> isTerminated,
        Action signalWork,
        Func<int> runPromiseJobs
    )
    {
        this.isTerminated = isTerminated;
        this.signalWork = signalWork;
        this.runPromiseJobs = runPromiseJobs;
        var target = new HostTaskTarget(timeProvider, EnqueueHostJobDirect, isTerminated);
        scheduler = taskScheduler.CreateAgentScheduler(target);
    }

    internal int PendingJobCount
    {
        get
        {
            lock (gate)
                return hostJobs.Count + hostPriorityJobs.Count;
        }
    }

    internal int GetJobCount(string queueName)
    {
        lock (gate)
        {
            return queueName switch
            {
                JobQueueName.HostJobs => hostJobs.Count,
                JobQueueName.HostPriorityJobs => hostPriorityJobs.Count,
                _ => 0,
            };
        }
    }

    internal void Enqueue(string queueName, Action<object?> callback, object? state)
    {
        if (isTerminated())
            return;

        lock (gate)
        {
            var queue = queueName == JobQueueName.HostPriorityJobs ? hostPriorityJobs : hostJobs;
            queue.Enqueue(new(callback, state));
        }

        signalWork();
    }

    internal void Enqueue(HostTaskQueueKey queueKey, Action<object?> callback, object? state)
    {
        if (isTerminated())
            return;

        if (scheduler is IQueuedHostAgentScheduler queuedScheduler)
            queuedScheduler.EnqueueTask(queueKey, callback, state);
        else
            scheduler.EnqueueTask(callback, state);
    }

    internal int RunJobs(string queueName)
    {
        var executed = 0;
        while (TryDequeue(queueName, out var job))
        {
            job.Invoke();
            executed++;
        }

        return executed;
    }

    internal bool RunOneHostJob()
    {
        if (!TryDequeue(JobQueueName.HostJobs, out var job))
            return false;

        job.Invoke();
        return true;
    }

    internal void PumpJobs()
    {
        while (true)
        {
            var didWork = RunJobs(JobQueueName.HostPriorityJobs) != 0;
            didWork |= runPromiseJobs() != 0;
            if (didWork)
                continue;

            if (!RunOneHostJob())
                return;
        }
    }

    internal void Clear()
    {
        lock (gate)
        {
            hostJobs.Clear();
            hostPriorityJobs.Clear();
        }
    }

    private void EnqueueHostJobDirect(Action<object?> callback, object? state)
    {
        Enqueue(JobQueueName.HostJobs, callback, state);
    }

    private bool TryDequeue(string queueName, out PendingJob job)
    {
        lock (gate)
        {
            var queue = queueName == JobQueueName.HostPriorityJobs ? hostPriorityJobs : hostJobs;
            if (queue.Count == 0)
            {
                job = default;
                return false;
            }

            job = queue.Dequeue();
            return true;
        }
    }

    private readonly struct PendingJob(Action<object?> callback, object? state)
    {
        private readonly Action<object?> callback = callback;
        private readonly object? state = state;

        public void Invoke()
        {
            callback(state);
        }
    }
}
