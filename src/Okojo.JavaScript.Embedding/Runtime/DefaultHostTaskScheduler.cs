namespace Okojo.Runtime;

internal sealed class DefaultHostTaskScheduler : IHostTaskScheduler
{
    public static DefaultHostTaskScheduler Shared { get; } = new();

    public IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new DefaultAgentScheduler(target);
    }

    private sealed class DefaultAgentScheduler(HostTaskTarget target) : IHostAgentScheduler
    {
        public void EnqueueTask(Action<object?> callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            target.EnqueueTask(callback, state);
        }
    }
}
