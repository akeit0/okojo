using System.Threading.Tasks.Sources;

namespace Okojo.JavaScript.Execution.Interop;

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

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    )
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
            cancellationRegistration = cancellationToken.Register(
                static state =>
                {
                    var registration =
                        (CancellationRegistrationState<PromiseValueTaskSource>)state!;
                    registration.Owner.TrySetException(
                        new OperationCanceledException(registration.Token)
                    );
                },
                new CancellationRegistrationState<PromiseValueTaskSource>(this, cancellationToken)
            );
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

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    )
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
            cancellationRegistration = cancellationToken.Register(
                static state =>
                {
                    var registration =
                        (CancellationRegistrationState<PromiseValueTaskSource<T>>)state!;
                    registration.Owner.TrySetException(
                        new OperationCanceledException(registration.Token)
                    );
                },
                new CancellationRegistrationState<PromiseValueTaskSource<T>>(
                    this,
                    cancellationToken
                )
            );
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

internal readonly record struct CancellationRegistrationState<TOwner>(
    TOwner Owner,
    CancellationToken Token
)
    where TOwner : class;
