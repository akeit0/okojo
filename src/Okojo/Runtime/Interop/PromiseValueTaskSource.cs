using System.Threading.Tasks.Sources;

namespace Okojo.Runtime.Interop;

internal sealed class PromiseValueTaskSource : IValueTaskSource
{
    private CancellationTokenRegistration cancellationRegistration;
    private int completionState;
    private ManualResetValueTaskSourceCore<bool> core;

    public PromiseValueTaskSource()
    {
        core.RunContinuationsAsynchronously = true;
    }

    public short Version => core.Version;

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    public void GetResult(short token)
    {
        core.GetResult(token);
    }

    public void RegisterCancellation(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
            cancellationRegistration = cancellationToken.Register(static state =>
            {
                var registration = (CancellationRegistrationState<PromiseValueTaskSource>)state!;
                registration.Owner.TrySetException(new OperationCanceledException(registration.Token));
            }, new CancellationRegistrationState<PromiseValueTaskSource>(this, cancellationToken));
    }

    public void TrySetResult()
    {
        if (Interlocked.Exchange(ref completionState, 1) != 0)
            return;

        cancellationRegistration.Dispose();
        core.SetResult(true);
    }

    public void TrySetException(Exception exception)
    {
        if (Interlocked.Exchange(ref completionState, 1) != 0)
            return;

        cancellationRegistration.Dispose();
        core.SetException(exception);
    }
}

internal sealed class PromiseValueTaskSource<T> : IValueTaskSource<T>
{
    private CancellationTokenRegistration cancellationRegistration;
    private int completionState;
    private ManualResetValueTaskSourceCore<T> core;

    public PromiseValueTaskSource()
    {
        core.RunContinuationsAsynchronously = true;
    }

    public short Version => core.Version;

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    public T GetResult(short token)
    {
        return core.GetResult(token);
    }

    public void RegisterCancellation(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
            cancellationRegistration = cancellationToken.Register(static state =>
            {
                var registration = (CancellationRegistrationState<PromiseValueTaskSource<T>>)state!;
                registration.Owner.TrySetException(new OperationCanceledException(registration.Token));
            }, new CancellationRegistrationState<PromiseValueTaskSource<T>>(this, cancellationToken));
    }

    public void TrySetResult(T result)
    {
        if (Interlocked.Exchange(ref completionState, 1) != 0)
            return;

        cancellationRegistration.Dispose();
        core.SetResult(result);
    }

    public void TrySetException(Exception exception)
    {
        if (Interlocked.Exchange(ref completionState, 1) != 0)
            return;

        cancellationRegistration.Dispose();
        core.SetException(exception);
    }
}

internal readonly record struct CancellationRegistrationState<TOwner>(TOwner Owner, CancellationToken Token)
    where TOwner : class;

internal sealed class PumpedPromiseValueTaskSource<T> : IValueTaskSource<T>
{
    private readonly CancellationToken cancellationToken;
    private readonly JsRealm realm;
    private int completionState;
    private ManualResetValueTaskSourceCore<T> core;

    public PumpedPromiseValueTaskSource(JsRealm realm, CancellationToken cancellationToken)
    {
        this.realm = realm;
        this.cancellationToken = cancellationToken;
        core.RunContinuationsAsynchronously = true;
        realm.Engine.Options.HostServices.BackgroundScheduler.Queue(
            static state => { ((PumpedPromiseValueTaskSource<T>)state!).RunPumpLoop(); }, this);
    }

    public short Version => core.Version;

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    public T GetResult(short token)
    {
        return core.GetResult(token);
    }

    public void TrySetResult(T result)
    {
        if (Interlocked.Exchange(ref completionState, 1) != 0)
            return;

        core.SetResult(result);
    }

    public void TrySetException(Exception exception)
    {
        if (Interlocked.Exchange(ref completionState, 1) != 0)
            return;

        core.SetException(exception);
    }

    private void RunPumpLoop()
    {
        try
        {
            WaitHandle[]? waits = cancellationToken.CanBeCanceled
                ? [cancellationToken.WaitHandle, realm.Agent.JobsAvailableWaitHandle]
                : null;

            while (Volatile.Read(ref completionState) == 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TrySetException(new OperationCanceledException(cancellationToken));
                    return;
                }

                if (realm.Agent.IsTerminated)
                    return;

                if (realm.Agent.PendingJobCount > 0)
                {
                    realm.PumpJobs();
                    continue;
                }

                if (waits is null)
                {
                    realm.Agent.JobsAvailableWaitHandle.WaitOne();
                    continue;
                }

                var signaled = WaitHandle.WaitAny(waits);
                if (signaled == 0)
                {
                    TrySetException(new OperationCanceledException(cancellationToken));
                    return;
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            TrySetException(ex);
        }
    }
}
