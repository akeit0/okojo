using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Hosting;

public sealed class ThreadPoolTaskScheduler : IHostTaskScheduler
{
    public IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new ThreadPoolAgentScheduler(target);
    }

    private sealed class ThreadPoolAgentScheduler(HostTaskTarget target) : IHostAgentScheduler
    {
        public void EnqueueTask(Action<object?> callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            target.EnqueueTask(callback, state);
        }
    }
}
