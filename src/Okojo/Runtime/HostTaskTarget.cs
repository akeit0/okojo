namespace Okojo.Runtime;

public sealed class HostTaskTarget
{
    private readonly Action<Action<object?>, object?> enqueueTask;
    private readonly Func<bool> isTerminated;

    internal HostTaskTarget(TimeProvider timeProvider, Action<Action<object?>, object?> enqueueTask,
        Func<bool> isTerminated)
    {
        TimeProvider = timeProvider;
        this.enqueueTask = enqueueTask;
        this.isTerminated = isTerminated;
    }

    public TimeProvider TimeProvider { get; }
    public bool IsTerminated => isTerminated();

    public void EnqueueTask(Action<object?> callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        enqueueTask(callback, state);
    }
}
