namespace Okojo.Runtime;

public interface IBackgroundScheduler
{
    void Queue(Action<object?> callback, object? state);
    Task WaitHandleAsync(WaitHandle handle, CancellationToken cancellationToken);
}
