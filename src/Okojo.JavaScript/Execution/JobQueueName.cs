namespace Okojo.JavaScript.Execution;

/// <summary>
///     ECMA-262 job queue names. The engine owns <see cref="ScriptJobs"/> and
///     <see cref="PromiseJobs"/>; hosts may define additional named job queues
///     for their own task sources (timers, messages, I/O, rendering).
/// </summary>
public static class JobQueueName
{
    /// <summary>Jobs created by script/module evaluation.</summary>
    public const string ScriptJobs = "ScriptJobs";

    /// <summary>Promise reaction jobs, async continuations, and <c>queueMicrotask</c>.</summary>
    public const string PromiseJobs = "PromiseJobs";

    /// <summary>Default host task queue (timers, messages, host callbacks).</summary>
    public const string HostJobs = "HostJobs";

    /// <summary>Host-defined job class that runs before Promise jobs (e.g. Node nextTick).</summary>
    public const string HostPriorityJobs = "HostPriorityJobs";
}
