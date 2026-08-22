namespace Okojo.Runtime;

public interface IHostTaskScheduler
{
    IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target);
}

public interface IHostAgentScheduler
{
    void EnqueueTask(Action<object?> callback, object? state);
}

public interface IQueuedHostAgentScheduler : IHostAgentScheduler
{
    void EnqueueTask(HostTaskQueueKey queueKey, Action<object?> callback, object? state);
}
