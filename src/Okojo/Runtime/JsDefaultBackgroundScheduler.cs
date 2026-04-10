namespace Okojo.Runtime;

internal sealed class JsDefaultBackgroundScheduler : IBackgroundScheduler
{
    public static JsDefaultBackgroundScheduler Shared { get; } = new();

    public void Queue(Action<object?> callback, object? state)
    {
        ThreadPool.QueueUserWorkItem(static item =>
        {
            var work = ((Action<object?> Callback, object? State))item!;
            work.Callback(work.State);
        }, (callback, state));
    }

    public Task WaitHandleAsync(WaitHandle handle, CancellationToken cancellationToken)
    {
        return Task.Run(() => handle.WaitOne(), cancellationToken);
    }
}
